using LivingCity.Personnel;
using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// The Resources bridge between the armory page and the gun meshes: the FBX files in
    /// Assets/Weapons/GunPack are invisible to a running Play session (not in Resources,
    /// not in the PrefabDatabase), so LedgerArtBootstrap bakes references to a chosen
    /// body per EquipmentKind into this asset at every editor load - the same
    /// no-menu-steps contract every runtime layer honours. Slots the bootstrap finds
    /// already filled are left alone, so an Inspector override survives re-bakes the way
    /// the sound trims do.
    ///
    /// Vehicles are deliberately absent: the city's own PrefabDatabase already reaches
    /// runtime, and PortraitStudio scans it directly.
    /// </summary>
    public sealed class LedgerModelSet : ScriptableObject
    {
        [Tooltip("The Synty arsenal the catalogue photographs - the PolygonGangWarfare " +
                 "guns and street tools, baked by name so a listing names its own body " +
                 "and two pistols can look different. Same table shape as people.")]
        public GameObject[] weapons = System.Array.Empty<GameObject>();

        [Tooltip("Fallback body for a Pistol listing that names no model of its own.")]
        public GameObject pistol;

        public GameObject shotgun;

        [Tooltip("The armory Rifle - the long body reads as 'longest range'.")]
        public GameObject rifle;

        public GameObject tommyGun;

        [Tooltip("The city's own PrefabDatabase - PortraitStudio's model source in " +
                 "scenes that have no CityBuilder (the standalone Ledger menu).")]
        public LivingCity.Data.PrefabDatabase database;

        [Tooltip("The Synty cast the book photographs - the men's mugshots, the capos, the " +
                 "paper's faces. Plain pack character prefabs (Humanoid, no scripts), " +
                 "baked by name from the Synty folders so a portrait never depends on the " +
                 "PrefabDatabase's crowd slots being dealt.")]
        public GameObject[] people = System.Array.Empty<GameObject>();

        static LedgerModelSet loaded;
        static bool loadTried;
        static bool warnedMissing;

        // Static state outlives Play when domain reload is off - same fix as WeaponCatalog.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            loaded = null;
            loadTried = false;
            warnedMissing = false;
        }

        public static LedgerModelSet Instance
        {
            get
            {
                if (loaded || loadTried)
                    return loaded;

                loadTried = true;
                loaded = Resources.Load<LedgerModelSet>("LedgerModelSet");
                return loaded;
            }
        }

        /// <summary>The pack prefab of this name, or null. Accepts the "_AI" suffix the
        /// converted street walkers carry - GangCatalog and the picture desk still name
        /// men that way - so one table answers every caller.</summary>
        public static GameObject PersonNamed(string name)
        {
            var set = Instance;
            if (!set || set.people == null || string.IsNullOrEmpty(name))
                return null;

            var bare = name.EndsWith("_AI") ? name.Substring(0, name.Length - 3) : name;
            foreach (var prefab in set.people)
                if (prefab && (prefab.name == name || prefab.name == bare))
                    return prefab;
            return null;
        }

        /// <summary>The pack prefab of this name among the baked weapons, or null.</summary>
        public static GameObject WeaponNamed(string name)
        {
            var set = Instance;
            if (!set || set.weapons == null || string.IsNullOrEmpty(name))
                return null;

            foreach (var prefab in set.weapons)
                if (prefab && prefab.name == name)
                    return prefab;
            return null;
        }

        /// <summary>The body that plays this listing on the catalogue board - the model
        /// it names, else its kind's fallback slot. Null (nothing named, asset not baked
        /// yet, pack missing) means "no photograph" - the row simply keeps its
        /// text.</summary>
        public static GameObject WeaponModelFor(EquipmentKind kind, string modelName)
        {
            // The listing's own body first - that is how the .38 and the plated pistol,
            // one kind between them, photograph differently.
            var named = WeaponNamed(modelName);
            if (named)
                return named;

            var set = Instance;
            if (!set)
            {
                if (!warnedMissing)
                {
                    warnedMissing = true;
                    Debug.LogWarning("[LedgerModelSet] Resources has no LedgerModelSet " +
                        "asset - gun photos are off until the editor bootstrap bakes it.");
                }

                return null;
            }

            return kind switch
            {
                EquipmentKind.Pistol => set.pistol,
                EquipmentKind.TwinPistols => set.pistol,
                EquipmentKind.Shotgun => set.shotgun,
                EquipmentKind.Rifle => set.rifle,
                EquipmentKind.TommyGun => set.tommyGun,
                EquipmentKind.MachinePistol => set.tommyGun,
                _ => null,
            };
        }
    }
}
