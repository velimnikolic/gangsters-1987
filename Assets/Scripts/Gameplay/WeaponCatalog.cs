using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// One purchasable weapon: identity, price, and the stats that differ from the global
    /// CombatConfig numbers. The contract that keeps this small is the ZERO RULE - a stat
    /// left at 0 means "use CombatConfig's value", so the revolver entry carries no numbers
    /// at all and a new weapon only states what it changes. The prefab is the same seam:
    /// null means "keep the revolver model in the socket", which every entry does today
    /// because the pack ships exactly one gun mesh.
    /// </summary>
    [System.Serializable]
    public sealed class WeaponDef
    {
        [Tooltip("Stable key - ownership is recorded against this, so renaming the display " +
                 "name never un-buys a weapon.")]
        public string id;

        [Tooltip("The name the menu row shows.")]
        public string displayName;

        [Tooltip("Dollars. Shown in the menu now; charged once a wallet exists.")]
        [Min(0)] public int price;

        [Tooltip("0 = CombatConfig's damagePerShot.")]
        [Min(0f)] public float damagePerShot;

        [Tooltip("0 = CombatConfig's fireInterval.")]
        [Min(0f)] public float fireInterval;

        [Tooltip("0 = CombatConfig's engageRange.")]
        [Min(0f)] public float engageRange;

        [Tooltip("Hand mesh for this weapon. Null keeps the revolver - the only gun model " +
                 "the project has.")]
        public GameObject prefab;

        [Tooltip("The player owns this from the start; it never appears in the shop.")]
        public bool starting;
    }

    /// <summary>
    /// Every weapon the gun shop can sell, plus the one the player starts with. An asset in
    /// Resources like the other gameplay configs - but the DEFAULTS live in the field
    /// initializer below, so a session where the asset was never authored (the menu is
    /// never re-run; see GameplayBootstrap's class comment) still fields a full catalog,
    /// and GetOrCreate seeds a fresh asset with the same entries for free.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponCatalog", menuName = "Living City/Weapon Catalog")]
    public sealed class WeaponCatalog : ScriptableObject
    {
        public WeaponDef[] weapons =
        {
            new WeaponDef
            {
                id = "revolver",
                displayName = "Revolver",
                starting = true,
                // No numbers on purpose - the zero rule routes every stat to CombatConfig,
                // which is exactly what the pre-catalog game did.
            },
            new WeaponDef
            {
                id = "magnum",
                displayName = "Magnum",
                price = 450,
                damagePerShot = 250f,
                fireInterval = 0.5f,
                engageRange = 6f,
            },
        };

        /// <summary>The def ownership starts with - first flagged starting, else first.</summary>
        public WeaponDef Starting
        {
            get
            {
                if (weapons == null || weapons.Length == 0)
                    return null;

                foreach (var def in weapons)
                    if (def != null && def.starting)
                        return def;

                return weapons[0];
            }
        }

        static WeaponCatalog loaded;

        public static WeaponCatalog Instance
        {
            get
            {
                if (loaded)
                    return loaded;

                loaded = Resources.Load<WeaponCatalog>("WeaponCatalog");
                if (!loaded || loaded.weapons == null || loaded.weapons.Length == 0)
                    loaded = CreateInstance<WeaponCatalog>(); // field initializer = defaults

                return loaded;
            }
        }

        /// <summary>Static state outlives Play when domain reload is off - same fix as
        /// ContextActionRegistry.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => loaded = null;
    }
}
