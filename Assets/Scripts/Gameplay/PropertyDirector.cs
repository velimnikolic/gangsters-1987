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
    /// sorted by world position - never by child order - so every session names the same owners
    /// over the same doors.
    ///
    /// Owners are ordinary CIVILIANS, one per building - the gangs layer carries the underworld
    /// now, and a mafia name on every deed would make the five families read as wallpaper.
    /// (This replaced the original five-bosses-own-blocks scheme, deliberately re-rolling every
    /// campaign's owners once; the draw order below is frozen from that change on.) The two
    /// named institutions draw nothing from the stream: City Hall up front, and the Harbor
    /// Company created lazily on the first port shed, so a seed without a port cannot shift
    /// the civilian names.
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

        /// <summary>Chance in a hundred that a business's civilian owner is a woman - it is
        /// 1980 and the ledgers skew male, but not absurdly so.</summary>
        const int FemaleOwnerChance = 45;

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

            var civic = PropertyRegistry.AddOwner("City Hall", civic: true);
            PropertyOwner harbor = null;
            var takenNames = new HashSet<string>();

            // Per business, in the sorted order. Draw order per candidate is FROZEN
            // (title roll where the title varies, then the owner's gender/first/surname,
            // then takings) - inserting a draw here reshuffles every campaign.
            foreach (var candidate in candidates)
            {
                string title;
                PropertyOwner owner;
                int income;

                switch (candidate.Category)
                {
                    case BusinessCategory.Port:
                        // One company for the whole waterfront: clicking any shed inside
                        // the wall means "the port", and one business means one owner.
                        harbor ??= PropertyRegistry.AddOwner("Harbor Company");
                        title = UI.BusinessIntention.PortTitle();
                        owner = harbor;
                        income = 5000;
                        break;

                    case BusinessCategory.Commercial:
                        var civicPost = candidate.Name.StartsWith("building-post");
                        title = civicPost
                            ? UI.BusinessIntention.CommercialTitle(candidate.Name, 0)
                            : UI.BusinessIntention.CommercialTitle(
                                candidate.Name, rng.Next(1000));
                        owner = civicPost ? civic : DrawCivilianOwner(rng, takenNames);
                        income = rng.Next(6, 25) * 50;
                        break;

                    default:
                        title = UI.BusinessIntention.IndustrialTitle(
                            candidate.Name, candidate.BlockId);
                        owner = DrawCivilianOwner(rng, takenNames);
                        income = rng.Next(16, 49) * 50;
                        break;
                }

                candidate.Target.gameObject.AddComponent<BusinessMarker>()
                    .Init(candidate.Category, title, candidate.BlockId, owner, income);
            }

            Debug.Log($"[PropertyDirector] {candidates.Count} businesses, " +
                      $"{PropertyRegistry.Owners.Count} owners (seed {builder.Config.seed}).");
        }

        /// <summary>
        /// One civilian per door, from the same name tables the crowd draws its identities
        /// from - a gangster and a grocer can share a name across town, exactly as two
        /// civilians already can. The redraw keeps DEED names unique while it has luck;
        /// after fifty collisions a duplicate beats an unbounded loop (RosterSeeder's guard).
        /// </summary>
        static PropertyOwner DrawCivilianOwner(System.Random rng, HashSet<string> takenNames)
        {
            var fullName = "";
            for (var guard = 0; guard < 50; guard++)
            {
                var female = rng.Next(100) < FemaleOwnerChance;
                var firstNames = female
                    ? PedestrianIdentity.AllFemaleNames
                    : PedestrianIdentity.AllMaleNames;
                var first = firstNames[rng.Next(firstNames.Count)];
                var surname =
                    PedestrianIdentity.AllSurnames[rng.Next(PedestrianIdentity.AllSurnames.Count)];

                fullName = first + " " + surname;
                if (takenNames.Add(fullName))
                    break;
            }

            return PropertyRegistry.AddOwner(fullName);
        }

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
