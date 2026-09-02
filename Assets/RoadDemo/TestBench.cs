using System.Collections.Generic;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What the demo benches (CrewDemo, CoverDemo, PumpDemo and the rest) share:
    /// the pack cars anybody may be driving, the rival mobs dealt off the gang
    /// catalog, the outfit's long guns issued through the ledger, the pavement
    /// graph's wiring and its tick, and the materials a scene makes for itself.
    ///
    /// None of it is new behaviour - every routine here was copied verbatim between
    /// two or three of the benches, and a fix made in one of them never reached the
    /// others. A bench passes in what differs (where its rivals stand, which guns
    /// they carry, its log tag) and takes the rest from here.
    /// </summary>
    public static class TestBench
    {
        // ------------------------------------------------------------------ cars

        /// <summary>The civilian pack cars a bench street is driven by.</summary>
        public static readonly string[] StreetCars =
        {
            "SM_Veh_Car_Sedan_01", "SM_Veh_Car_Medium_01", "SM_Veh_Car_Small_01", "SM_Veh_Car_Muscle_01",
            "SM_Veh_Sedan_01", "SM_Veh_Suv_01", "SM_Veh_Pickup_01", "SM_Veh_LowCar_01", "SM_Veh_LowCar_02",
        };

        /// <summary>The street cars, each found by the bench's own lookup, weighted the
        /// way the city's pool is: duplicate-as-weight, since the list is drawn from
        /// uniformly, and a hand-written list of nine bodies made the muscle car one
        /// street car in nine. It takes two seats where a saloon takes six
        /// (VehicleCatalog.PoolWeight), which is the mix the quarter drives.</summary>
        public static List<GameObject> WeightedCars(System.Func<string, GameObject> find)
        {
            var bodies = new List<GameObject>();
            foreach (var name in StreetCars)
            {
                var p = find(name);
                if (!p) continue;
                for (int seat = 0, seats = LivingCity.Gameplay.VehicleCatalog.PoolWeight(name);
                     seat < seats; seat++)
                    bodies.Add(p);
            }
            return bodies;
        }

        // ------------------------------------------------------------------ people

        /// <summary>The plain pack body of this name - the ledger's baked cast first (no
        /// crowd scripts to strip), the PrefabDatabase's street copy as the fallback.</summary>
        public static GameObject Cast(string name) =>
            LivingCity.UI.LedgerModelSet.PersonNamed(name) ??
            LivingCity.UI.PortraitStudio.FindPeoplePrefab(name);

        /// <summary>A man's name off the pedestrian identity tables.</summary>
        public static string DrawName(System.Random rng)
        {
            var firsts = LivingCity.Entities.PedestrianIdentity.AllMaleNames;
            var surnames = LivingCity.Entities.PedestrianIdentity.AllSurnames;
            return firsts[rng.Next(firsts.Count)] + " " + surnames[rng.Next(surnames.Count)];
        }

        // ------------------------------------------------------------------ rivals

        /// <summary>The rival mobs, one per place the bench names: a lieutenant and his
        /// hoods out of the gang catalog, a body per man and none of them the
        /// lieutenant's (a rival crew is five men, not one man standing five times),
        /// armed round-robin off <paramref name="arms"/>. <paramref name="place"/> is
        /// where the i-th crew musters and which way it faces - the only thing the
        /// benches differ on. Gang 0 is the outfit, so the rivals start at 1.</summary>
        public static void SpawnRivals(DemoCrews crews, int nameSeed, int rivalCrews, int rivalHoods,
            (string weapon, EquipmentKind kind)[] arms,
            System.Func<int, (Vector3 at, Vector3 facing)> place, string tag,
            bool mixArmsWithinCrew = false)
        {
            if (arms == null || arms.Length == 0)
            {
                Debug.LogWarning(tag + " no rival guns were listed - the rival crews sit out.");
                return;
            }

            var rng = new System.Random(nameSeed);
            var gangNames = LivingCity.Gangs.GangCatalog.Names;
            var bossModels = LivingCity.Gangs.GangCatalog.LieutenantModels;
            var soldierModels = LivingCity.Gangs.GangCatalog.SoldierModels;
            var resolvedArms = new (GameObject weapon, EquipmentKind kind)[arms.Length];
            for (int i = 0; i < arms.Length; i++)
            {
                resolvedArms[i] = (CrewKit.Weapon(arms[i].weapon), arms[i].kind);
                if (resolvedArms[i].weapon == null)
                    Debug.LogWarning(tag + " gun " + arms[i].weapon + " not found - " +
                                     "rivals dealt that slot come unarmed.");
            }

            int count = Mathf.Clamp(rivalCrews, 1, Mathf.Min(gangNames.Length - 1, 4));
            for (int i = 0; i < count; i++)
            {
                int gang = 1 + i;
                var bossModel = bossModels[gang % bossModels.Length];
                var bossPrefab = Cast(bossModel);
                if (bossPrefab == null)
                {
                    Debug.LogWarning(tag + " no body for the " + gangNames[gang] + " lieutenant (" +
                                     bossModel + ") - that crew sits out.");
                    continue;
                }

                var hoodNames = new List<string>();
                for (int k = 0; k < rivalHoods; k++) hoodNames.Add(DrawName(rng));

                var hoodPrefabs = new List<GameObject>();
                foreach (var look in LivingCity.Gangs.GangLooks.HoodsFor(
                             bossModel, soldierModels[gang % soldierModels.Length], rivalHoods))
                {
                    var body = Cast(look);
                    if (body) hoodPrefabs.Add(body);
                }

                var arm = resolvedArms[i % resolvedArms.Length];
                int crewIndex = i;
                System.Func<int, (GameObject weapon, EquipmentKind kind)> armsFor = null;
                if (mixArmsWithinCrew)
                    armsFor = member => resolvedArms[(crewIndex + member) % resolvedArms.Length];

                var (anchor, facing) = place(i);
                crews.AddRival(gang, gangNames[gang], DrawName(rng), bossPrefab, hoodNames,
                    hoodPrefabs, anchor, facing, arm.weapon, arm.kind, lineUp: true,
                    armsFor: armsFor);
            }
        }

        // ------------------------------------------------------------------ the outfit's guns

        /// <summary>The outfit opens a bench with long guns rather than the .38 in the
        /// coat: the ledger takes in one per pair of hands for each crew and the boss
        /// hands the lot to its lieutenant, who deals them out himself (NormalizeArms) -
        /// so the armory page and the street show the same guns, and a man who dies or
        /// changes crews leaves his rifle to the crew, as the books require. Free, like
        /// the car: this is a bench, and the money half of a purchase lives with the
        /// outfit's accounts.
        ///
        /// Returns false while the roster is not in yet, so the bench asks again next
        /// frame; true once the issue is settled either way.</summary>
        public static bool ArmTheOutfit(EquipmentKind arms, string tag)
        {
            return ArmTheOutfit(new[] { arms }, tag);
        }

        /// <summary>Mixed-arms counterpart to the single-kind bench issue. Every crew
        /// receives one gun per active member, cycling through <paramref name="arms"/>
        /// from its lieutenant down. NormalizeArms remains the dealer: combat skill and
        /// organization decide which named member actually carries each piece.</summary>
        public static bool ArmTheOutfit(IReadOnlyList<EquipmentKind> arms, string tag)
        {
            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            if (director == null || director.Roster == null) return false;
            var roster = director.Roster;
            if (roster.Crews.Count == 0) return true;

            if (arms == null || arms.Count == 0)
            {
                Debug.LogWarning(tag + " no outfit guns were listed - the outfit keeps its .38s.");
                return true;
            }

            for (int i = 0; i < arms.Count; i++)
                if (!CrewArms.IsFirearm(arms[i]))
                {
                    Debug.LogWarning(tag + " " + arms[i] +
                                     " is not a gun - the outfit keeps its .38s.");
                    return true;
                }

            int issued = 0;
            foreach (var crew in roster.Crews)
            {
                var lieutenant = roster.Find(crew.LieutenantId);
                if (lieutenant == null || lieutenant.Status != CharacterStatus.Active) continue;
                int hands = 1; // the lieutenant carries one himself
                foreach (int id in crew.HoodIds)
                {
                    var hood = roster.Find(id);
                    if (hood != null && hood.Status == CharacterStatus.Active) hands++;
                }
                for (int i = 0; i < hands; i++)
                {
                    var kind = arms[i % arms.Count];
                    // Bought under the counter's own name and price, so the item reads
                    // and photographs exactly as one the player paid for.
                    string gun = kind.ToString();
                    int price = 0;
                    foreach (var listing in LivingCity.Outfit.ArmoryCatalog.Weapons)
                        if (listing.Kind == kind)
                        {
                            gun = listing.DisplayName;
                            price = listing.Price;
                            break;
                        }
                    var item = director.AddEquipment(kind, gun, price);
                    if (item == null) continue;
                    if (director.GiveEquipment(item.Id, lieutenant.Id).Ok) issued++;
                }
            }
            string issue = arms.Count == 1 ? arms[0].ToString() : "mixed firearms";
            Debug.Log(tag + " " + issued + " x " + issue + " issued to the outfit.");
            return true;
        }

        // ------------------------------------------------------------------ the pavement graph

        /// <summary>Two nodes joined both ways, the pair of links filed in <paramref name="into"/>.</summary>
        public static void Join(PedNode a, PedNode b, bool gated, List<PedLink> into)
        {
            float len = Vector3.Distance(a.Pos, b.Pos);
            var ab = new PedLink { From = a, To = b, Length = len, Gated = gated };
            var ba = new PedLink { From = b, To = a, Length = len, Gated = gated };
            a.Links.Add(ab);
            b.Links.Add(ba);
            into.Add(ab);
            into.Add(ba);
        }

        /// <summary>The pavement's frame: every walker, the crowd, the beat (if the
        /// bench has one), and every second and a half a pass pairing walkers off to
        /// chat. <paramref name="chatScan"/> is the bench's own countdown to that pass.</summary>
        public static void TickPavementLife(List<CivilianAgent> walkers, List<PoliceFootPatrol> beat,
                                            float dt, ref float chatScan)
        {
            for (int i = 0; i < walkers.Count; i++) walkers[i].TickCivilian(dt);
            CivilianAgent.TickCrowd(dt);
            if (beat != null)
                for (int i = 0; i < beat.Count; i++) beat[i].TickPatrol(dt);
            chatScan -= dt;
            if (chatScan <= 0f && walkers.Count > 0)
            {
                chatScan = 1.5f;
                CivilianAgent.PairChats(walkers, new Vector2(6f, 14f));
            }
        }

        // ------------------------------------------------------------------ materials

        /// <summary>Every material a scene made for itself, destroyed - a `new Material`
        /// is not taken down with the objects that wore it, and a bench that stands up
        /// and clears a dozen times leaks a dozen sets of them. Immediate off Play,
        /// where Destroy is not available.</summary>
        public static void DestroyAll(List<Material> mats)
        {
            for (int i = 0; i < mats.Count; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (Application.isPlaying) Object.Destroy(m);
                else Object.DestroyImmediate(m);
            }
            mats.Clear();
        }
    }
}
