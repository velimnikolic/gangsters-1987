using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;
using LivingCity.Generation;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Gives the city its underworld ledger: sweeps the saved hierarchy once at Play, decides
    /// which buildings are businesses, and stamps each with a BusinessMarker naming its gazda
    /// and takings. Installed by GameplayBootstrap - the runtime-self-install rule: the shipping
    /// city is generated in the editor and SAVED, so this pass must work over a scene it cannot
    /// regenerate, which is also why CityGrid is not consulted (it is null at Play) and block
    /// ids are read back from the ground slab names, BlockOverlayHud's trick.
    ///
    /// Determinism is the contract the whole Generation layer holds and this pass keeps at Play:
    /// one rng stream (seed + SeedOffsets.Ownership), drawn in one fixed order over candidates
    /// sorted by world position - never by child order - so every session names the same bosses
    /// over the same doors.
    ///
    /// The one classification trap is the port: its palette places the SAME warehouse prefabs
    /// as the industrial blocks, so names alone would misfile them. Buildings inside a
    /// PortMarker's wall rect are claimed for the docks first - all of them one business under
    /// one boss, because clicking any shed inside the wall means "the port", not "shed #4".
    /// </summary>
    public sealed class PropertyDirector : MonoBehaviour
    {
        /// <summary>The Industrial palette's building roster. Explicit names rather than an
        /// "industry-" prefix so a future palette addition is a decision here, not an accident.</summary>
        static readonly string[] IndustrialNames =
        {
            "industry-factory-old",
            "industry-factory-hall",
            "industry-factory",
            "industry-warehouse",
            "industry-storage",
            "industry-refinery",
            "industry-building",
        };

        /// <summary>InteractionMarkers.ShopFronts' judgement exactly: the fire station has
        /// engine doors nobody pops in through, and corner variants are facade filler.</summary>
        static readonly string[] CommercialNames =
        {
            "building-cafe",
            "building-restaurant",
            "building-post",
        };

        // Public for the headless suite: the popup-width assertion walks every combination
        // these tables can roll, so a name added here that breaks the line budget fails the
        // tests instead of the popup.
        public static readonly string[] BossFirstNames =
        {
            "Sal", "Vito", "Carmine", "Rocco", "Enzo", "Frankie", "Tony", "Nico",
        };

        // Surnames capped at nine letters: the popup line carries a full name, takings and a
        // protection word in 280px, and "Marcheselli" was the char that broke the budget.
        public static readonly string[] BossLastNames =
        {
            "Petrosino", "Falcone", "Greco", "Bonanno",
            "Rizzo", "Lombardi", "Caruso", "Moretti",
        };

        const int BossCount = 5;

        /// <summary>Metres of slack around the port wall rect - a shed whose origin sits on
        /// the wall line is still the port's.</summary>
        const float PortMargin = 3f;

        struct Candidate
        {
            public Transform Target;
            public BusinessCategory Category;
            public string Name;
            public int BlockId;
        }

        void Start()
        {
            var builder = FindAnyObjectByType<CityBuilder>();
            var root = builder ? builder.GeneratedRoot : null;
            var buildings = root ? root.Find("Buildings") : null;
            if (!buildings || !builder.Config)
            {
                Debug.LogWarning("[PropertyDirector] No generated city - nobody owns anything.",
                    this);
                return;
            }

            var slabs = CollectSlabs(root);
            var ports = FindObjectsByType<PortMarker>(FindObjectsSortMode.None);
            var candidates = Classify(buildings, slabs, ports);
            if (candidates.Count == 0)
                return;

            // Sorted by position, not by child order: the hierarchy's order is whatever the
            // generator happened to append, and this list IS the draw order below.
            candidates.Sort((a, b) =>
            {
                var ax = Mathf.RoundToInt(a.Target.position.x * 10f);
                var bx = Mathf.RoundToInt(b.Target.position.x * 10f);
                if (ax != bx)
                    return ax.CompareTo(bx);

                var az = Mathf.RoundToInt(a.Target.position.z * 10f);
                var bz = Mathf.RoundToInt(b.Target.position.z * 10f);
                if (az != bz)
                    return az.CompareTo(bz);

                return string.CompareOrdinal(a.Name, b.Name);
            });

            var rng = new System.Random(builder.Config.seed + SeedOffsets.Ownership);

            // Draw 1: the boss pool. Distinct surnames - two bosses out of five sharing one
            // would read as a typo, not a family.
            var bosses = new PropertyOwner[BossCount];
            var surnames = new List<string>(BossLastNames);
            for (var i = 0; i < BossCount; i++)
            {
                var first = BossFirstNames[rng.Next(BossFirstNames.Length)];
                var lastIndex = rng.Next(surnames.Count);
                bosses[i] = PropertyRegistry.AddOwner(first + " " + surnames[lastIndex]);
                surnames.RemoveAt(lastIndex);
            }
            var civic = PropertyRegistry.AddOwner("City Hall", civic: true);

            // Draw 2: the docks boss - the whole waterfront is one racket.
            var docksBoss = bosses[rng.Next(BossCount)];

            // Draw 3: a boss per block, ascending block id. Every business on a block shares
            // its gazda - a district boss, not a stranger per door.
            var blockIds = new SortedSet<int>();
            foreach (var candidate in candidates)
                if (candidate.Category != BusinessCategory.Port && candidate.BlockId >= 0)
                    blockIds.Add(candidate.BlockId);

            var blockBoss = new Dictionary<int, PropertyOwner>();
            foreach (var blockId in blockIds)
                blockBoss[blockId] = bosses[rng.Next(BossCount)];

            // Draw 4: per business, in the sorted order.
            foreach (var candidate in candidates)
            {
                string title;
                PropertyOwner owner;
                int income;

                switch (candidate.Category)
                {
                    case BusinessCategory.Port:
                        title = UI.BusinessIntention.PortTitle();
                        owner = docksBoss;
                        income = 5000;
                        break;

                    case BusinessCategory.Commercial:
                        var civicPost = candidate.Name.StartsWith("building-post");
                        title = civicPost
                            ? UI.BusinessIntention.CommercialTitle(candidate.Name, 0)
                            : UI.BusinessIntention.CommercialTitle(
                                candidate.Name, rng.Next(1000));
                        owner = civicPost ? civic : BossFor(blockBoss, candidate.BlockId);
                        income = rng.Next(6, 25) * 50;
                        break;

                    default:
                        title = UI.BusinessIntention.IndustrialTitle(
                            candidate.Name, candidate.BlockId);
                        owner = BossFor(blockBoss, candidate.BlockId);
                        income = rng.Next(16, 49) * 50;
                        break;
                }

                candidate.Target.gameObject.AddComponent<BusinessMarker>()
                    .Init(candidate.Category, title, candidate.BlockId, owner, income);
            }

            Debug.Log($"[PropertyDirector] {candidates.Count} businesses under " +
                      $"{BossCount} bosses (seed {builder.Config.seed}).");
        }

        static PropertyOwner BossFor(Dictionary<int, PropertyOwner> blockBoss, int blockId) =>
            blockBoss.TryGetValue(blockId, out var owner) ? owner : null;

        /// <summary>
        /// Block ids read back from the ground slab names, BlockOverlayHud's parse exactly:
        /// "ground_{zone}_{blockId}" (the park lays one slab per cell; the paint and patch
        /// decorations fail the enum parse and drop out). All slabs are kept - a business
        /// takes the block of its NEAREST slab, which the park's many tiles serve for free.
        /// </summary>
        static List<(int blockId, Vector3 position)> CollectSlabs(Transform root)
        {
            var slabs = new List<(int, Vector3)>();
            var ground = root.Find("Ground");
            if (!ground)
                return slabs;

            foreach (Transform child in ground)
            {
                var parts = child.name.Split('_');
                if (parts.Length < 3 || parts[0] != "ground")
                    continue;
                if (!System.Enum.TryParse(parts[1], out BlockZone _))
                    continue;
                if (!int.TryParse(parts[2], out var blockId))
                    continue;

                slabs.Add((blockId, child.position));
            }

            return slabs;
        }

        static List<Candidate> Classify(
            Transform buildings,
            List<(int blockId, Vector3 position)> slabs,
            PortMarker[] ports)
        {
            var candidates = new List<Candidate>();

            foreach (Transform child in buildings)
            {
                // StartsWith everywhere below absorbs the "(Clone)" a runtime rebuild appends.
                var buildingName = child.name;
                if (buildingName.Contains("corner"))
                    continue;
                if (child.GetComponent<BusinessMarker>())
                    continue; // Already stamped - the pass is idempotent.

                var portBlock = PortBlockAt(ports, child.position);
                BusinessCategory category;
                int blockId;

                if (portBlock >= 0 &&
                    (MatchesAny(buildingName, IndustrialNames) ||
                     buildingName.StartsWith("building-port")))
                {
                    category = BusinessCategory.Port;
                    blockId = portBlock;
                }
                else if (MatchesAny(buildingName, IndustrialNames))
                {
                    category = BusinessCategory.Industrial;
                    blockId = NearestBlock(slabs, child.position);
                }
                else if (MatchesAny(buildingName, CommercialNames))
                {
                    category = BusinessCategory.Commercial;
                    blockId = NearestBlock(slabs, child.position);
                }
                else
                {
                    continue;
                }

                candidates.Add(new Candidate
                {
                    Target = child,
                    Category = category,
                    Name = buildingName,
                    BlockId = blockId,
                });
            }

            return candidates;
        }

        static bool MatchesAny(string buildingName, string[] prefixes)
        {
            foreach (var prefix in prefixes)
                if (buildingName.StartsWith(prefix))
                    return true;

            return false;
        }

        /// <summary>
        /// The port palette places the same warehouse prefabs as the industrial blocks, so the
        /// wall rect - not the name - decides what belongs to the docks. First matching port
        /// wins; compounds never overlap.
        /// </summary>
        static int PortBlockAt(PortMarker[] ports, Vector3 position)
        {
            foreach (var port in ports)
            {
                if (!port)
                    continue;

                var min = port.WallMin;
                var max = port.WallMax;
                if (position.x >= min.x - PortMargin && position.x <= max.x + PortMargin &&
                    position.z >= min.y - PortMargin && position.z <= max.y + PortMargin)
                    return port.BlockId;
            }

            return -1;
        }

        static int NearestBlock(
            List<(int blockId, Vector3 position)> slabs, Vector3 position)
        {
            var best = -1;
            var bestSqr = float.MaxValue;
            foreach (var (blockId, slab) in slabs)
            {
                var dx = slab.x - position.x;
                var dz = slab.z - position.z;
                var sqr = dx * dx + dz * dz;
                if (sqr >= bestSqr)
                    continue;

                bestSqr = sqr;
                best = blockId;
            }

            return best;
        }
    }
}
