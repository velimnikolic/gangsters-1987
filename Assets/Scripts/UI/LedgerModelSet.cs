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
        [Tooltip("Plays the .38 Pistol listing, and both halves of the Twin Pack.")]
        public GameObject pistol;

        public GameObject shotgun;

        [Tooltip("The armory Rifle - the long, scoped body reads as 'longest range'.")]
        public GameObject rifle;

        public GameObject tommyGun;

        [Tooltip("The city's own PrefabDatabase - PortraitStudio's model source in " +
                 "scenes that have no CityBuilder (the standalone Ledger menu).")]
        public LivingCity.Data.PrefabDatabase database;

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

        /// <summary>The body that plays this kind on the catalogue board. Null (kind
        /// unknown, asset not baked yet, pack missing) means "no photograph" - the row
        /// simply keeps its text.</summary>
        public static GameObject WeaponModelFor(EquipmentKind kind)
        {
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
                _ => null,
            };
        }
    }
}
