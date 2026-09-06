using LivingCity.UI;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Bakes LedgerModelSet.asset (the armory page's gun references) on every editor
    /// load - the SoundAssetBootstrap discipline: no menu item to re-run, idempotent,
    /// and it only fills slots that are EMPTY, so a hand-picked override in the
    /// Inspector is never fought.
    ///
    /// The bodies come from the Synty packs by asset path - PolygonGangWarfare's arsenal,
    /// which is what the men on the street are holding. A missing file (pack not
    /// imported, the import still running in another session) is skipped and picked up on
    /// a later load; the catalogue row simply keeps its text meanwhile.
    /// </summary>
    [InitializeOnLoad]
    static class LedgerArtBootstrap
    {
        const string AssetPath = "Assets/Configs/UI/Resources/LedgerModelSet.asset";

        /// <summary>The pre-Synty gun pack the fallback slots used to point at. Its
        /// modern military rifles never matched the low-poly street, so a slot still
        /// holding one is migrated rather than treated as a hand-picked override.</summary>
        const string RetiredPackPath = "Assets/Weapons/GunPack/";

        const string GangWarfare = "Assets/Synty/PolygonGangWarfare/Prefabs/Weapons/";
        const string PalmCity = "Assets/Synty/PolygonPalmCity/Prefabs/Weapons/";

        static LedgerArtBootstrap()
        {
            // delayCall: the asset database is not safe to touch from a static
            // constructor running mid-refresh.
            EditorApplication.delayCall += Bake;
        }

        static void Bake()
        {
            var set = AssetDatabase.LoadAssetAtPath<LedgerModelSet>(AssetPath);
            if (!set)
            {
                set = ScriptableObject.CreateInstance<LedgerModelSet>();
                AssetDatabase.CreateAsset(set, AssetPath);
            }

            var changed = false;
            changed |= Wire(ref set.pistol, GangWarfare + "SM_Wep_Pistol_Revolver_01.prefab");
            changed |= Wire(ref set.shotgun, GangWarfare + "SM_Wep_Shotgun_01.prefab");
            changed |= Wire(ref set.rifle, PalmCity + "SM_Wep_Rifle_01.prefab");
            changed |= Wire(ref set.tommyGun, GangWarfare + "SM_Wep_SubMachineGun_01.prefab");
            changed |= Wire(ref set.grenade, PalmCity + "SM_Wep_Grenade_01.prefab");
            changed |= WireWeapons(set);
            changed |= WireMotorcycles(set);
            changed |= WireVehicles(set);
            changed |= WirePeople(set);

            // The photographs' model source for city-less scenes.
            if (!set.database)
            {
                set.database = AssetDatabase.LoadAssetAtPath<LivingCity.Data.PrefabDatabase>(
                    "Assets/Configs/PrefabDatabase.asset");
                changed |= set.database != null;
            }

            if (!changed)
                return;

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);
        }

        /// <summary>
        /// The hand-held merchandise the ledger may show, named the way ArmoryCatalog
        /// names it. The pack's bats and blades are not stock, so baking them would put
        /// bodies in the asset that no listing can ever ask for. A piece the counter
        /// starts selling is one line here and one in the catalogue.
        /// </summary>
        static readonly string[] WeaponPaths =
        {
            GangWarfare + "SM_Wep_Pistol_Revolver_01.prefab",
            GangWarfare + "SM_Wep_Machine_Pistol_01.prefab",
            GangWarfare + "SM_Wep_SubMachineGun_01.prefab",
            GangWarfare + "SM_Wep_Shotgun_01.prefab",
            PalmCity + "SM_Wep_Rifle_01.prefab",
            PalmCity + "SM_Wep_Grenade_01.prefab",
        };

        /// <summary>Fills set.weapons with every WeaponPaths prefab it does not already
        /// hold - WirePeople's rule, for the same reason: additive, so a hand-added body
        /// in the Inspector stays.</summary>
        static bool WireWeapons(LedgerModelSet set)
        {
            var weapons = new System.Collections.Generic.List<GameObject>();
            if (set.weapons != null)
                foreach (var prefab in set.weapons)
                    if (prefab)
                        weapons.Add(prefab);

            var before = weapons.Count;
            foreach (var path in WeaponPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab && !weapons.Contains(prefab))
                    weapons.Add(prefab);
            }

            var pruned = set.weapons != null && set.weapons.Length != before;
            if (weapons.Count == before && !pruned)
                return false;

            set.weapons = weapons.ToArray();
            return true;
        }

        /// <summary>The two-wheelers the counter sells
        /// (LivingCity.Outfit.ArmoryCatalog.Motorcycles), by path. Palm City's, and
        /// never the police pack's machine of the same name - that one is liveried and
        /// is the law's.
        ///
        /// FOUR, and only the four the shelf actually lists - the outfit's black tourer,
        /// the pack's motorbike, the enduro and the boxless moped. The scooter used to be
        /// baked in beside them and has not been for sale since it was measured (0.80 m of
        /// wheelbase, four wheels, no handlebars - ArmoryCatalog says why), so the book
        /// was carrying a photograph of a machine nobody can buy.</summary>
        static readonly string[] MotorcyclePaths =
        {
            "Assets/Prefabs/Vehicles/SM_Veh_Motorbike_Tourer_Black.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Motorbike_01.prefab",
            // the enduro is in no pack and in no traffic bucket: without this line the
            // shelf photographs nothing at all where the dirt bike should be
            // (EnduroBikeBaker bakes it)
            "Assets/Prefabs/Vehicles/SM_Veh_Motorbike_Enduro_450.prefab",
            // the counter's moped is the boxless variant, not the pack's delivery
            // machine - the book must photograph the body that turns up at the kerb
            // (PortraitStudio.VehicleModelFor says why the box came off)
            "Assets/Prefabs/Vehicles/SM_Veh_Moped_01_NoBox.prefab",
        };

        /// <summary>Fills set.motorcycles the additive way the other two tables are
        /// filled, so an Inspector override survives a re-bake.</summary>
        static bool WireMotorcycles(LedgerModelSet set)
        {
            var bikes = new System.Collections.Generic.List<GameObject>();
            if (set.motorcycles != null)
                foreach (var prefab in set.motorcycles)
                    if (prefab)
                        bikes.Add(prefab);

            var before = bikes.Count;
            foreach (var path in MotorcyclePaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab && !bikes.Contains(prefab))
                    bikes.Add(prefab);
            }

            var pruned = set.motorcycles != null && set.motorcycles.Length != before;
            if (bikes.Count == before && !pruned)
                return false;

            set.motorcycles = bikes.ToArray();
            return true;
        }

        /// <summary>The CARS the counter sells that no traffic bucket holds - the ones
        /// this project built for itself. A pack car is found in the PrefabDatabase and
        /// needs no line here; the armoured wagon is a variant of Palm City's SUV
        /// (ArmouredSuvBuilder) that the city never drives, so without this its listing
        /// would photograph nothing outside the editor.</summary>
        static readonly string[] VehiclePaths = System.Array.ConvertAll(
            LivingCity.Outfit.ArmoryCatalog.Vehicles,
            item => LivingCity.Gameplay.CivilianVehicleCatalog.PathFor(item.ModelName));

        /// <summary>Fills set.vehicles additively, exactly as the bikes are filled, so an
        /// Inspector override survives a re-bake.</summary>
        static bool WireVehicles(LedgerModelSet set)
        {
            var cars = new System.Collections.Generic.List<GameObject>();
            if (set.vehicles != null)
                foreach (var prefab in set.vehicles)
                    if (prefab)
                        cars.Add(prefab);

            var before = cars.Count;
            foreach (var path in VehiclePaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab && !cars.Contains(prefab))
                    cars.Add(prefab);
            }

            var pruned = set.vehicles != null && set.vehicles.Length != before;
            if (cars.Count == before && !pruned)
                return false;

            set.vehicles = cars.ToArray();
            return true;
        }

        /// <summary>The Synty character prefabs the book photographs, by pack path.
        /// The whole cast the game names anywhere - GangCatalog's soldiers and capos,
        /// the almanac's looks, the picture desk's faces - so every portrait resolves
        /// off this table alone. A path missing on disk (pack not imported) is skipped
        /// and picked up on a later load; nothing else is fought.</summary>
        static readonly string[] PeoplePaths =
        {
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Male_02.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Female_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Female_02.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Rich_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Rich_Female_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Salesman_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Surfer_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Surfer_Female_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Gang_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Gang_Male_02.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Gang_Female_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Goon_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Detective_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_SeaCaptain_Male_01.prefab",
            "Assets/Synty/PolygonPoliceStation/Prefabs/Characters/SM_Chr_Criminal_Male_01.prefab",
            "Assets/Synty/PolygonPoliceStation/Prefabs/Characters/SM_Chr_Criminal_Female_01.prefab",
            "Assets/Synty/PolygonPoliceStation/Prefabs/Characters/SM_Chr_Officer_Male_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Street_Male_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Street_Male_02.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Street_Female_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Street_Female_02.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Business_Male_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Business_Female_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Jumpsuit_Male_01.prefab",
            "Assets/Synty/PolygonGeneric/Prefabs/Characters/SM_Gen_Chr_Jumpsuit_Female_01.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Character/SM_Chr_Italian_Gangster_01.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Character/SM_Chr_GangBoss_01.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Character/SM_Chr_GangMember_Male_01.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Character/SM_Chr_GangMember_Male_02.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Character/SM_Chr_GangMember_Male_03.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Character/SM_Chr_GangMember_Female_01.prefab",
            "Assets/Synty/PolygonGangWarfare/Prefabs/Character/SM_Chr_DEA_Plainclothes_Male_01.prefab",
            "Assets/Synty/PolygonNightclubs/Prefabs/Characters/SM_Chr_Bouncer_Male_01.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Characters/Character_BusinessMan_Suit.prefab",
        };

        /// <summary>Fills set.people with every PeoplePaths prefab it does not already
        /// hold. Additive: a hand-added face in the Inspector stays.</summary>
        static bool WirePeople(LedgerModelSet set)
        {
            var people = new System.Collections.Generic.List<GameObject>();
            if (set.people != null)
                foreach (var prefab in set.people)
                    if (prefab && !LivingCity.Gangs.GangLooks.IsRetired(prefab.name))
                        people.Add(prefab);

            var before = people.Count;
            foreach (var path in PeoplePaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab && !people.Contains(prefab))
                    people.Add(prefab);
            }

            var pruned = set.people != null && set.people.Length != before;
            if (people.Count == before && !pruned)
                return false;

            set.people = people.ToArray();
            return true;
        }

        /// <summary>Fills an empty fallback slot - and re-points one still holding a
        /// RetiredPackPath body, which is a migration, not a fight with an override. Any
        /// other hand-picked prefab is left exactly where it is.</summary>
        static bool Wire(ref GameObject slot, string path)
        {
            if (slot && !AssetDatabase.GetAssetPath(slot).StartsWith(RetiredPackPath))
                return false;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!model || model == slot)
                return false;

            slot = model;
            return true;
        }
    }
}
