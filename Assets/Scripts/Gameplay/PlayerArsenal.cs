using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// What the player owns and what is in his hand. Added to the player by
    /// GameplayBootstrap; PlayerMafioso and WeaponController read their combat numbers
    /// through the accessors here, which fall back to CombatConfig wherever the current
    /// weapon says 0 - so a player without this component (or a def without a number)
    /// shoots exactly as before the catalog existed. Police read CombatConfig directly and
    /// never come through here; a bought weapon is the player's alone.
    ///
    /// TryPay is the whole economy for now, and says yes to everything - money is a
    /// decided-later feature, and this one method is where the wallet plugs in without
    /// touching the shop, the menu or the catalog.
    /// </summary>
    public sealed class PlayerArsenal : MonoBehaviour
    {
        readonly HashSet<string> owned = new HashSet<string>();

        WeaponDef current;

        /// <summary>The weapon in hand. Never null once Awake has run against a non-empty
        /// catalog.</summary>
        public WeaponDef Current => current;

        public int OwnedCount => owned.Count;

        void Awake()
        {
            var starting = WeaponCatalog.Instance.Starting;
            if (starting == null)
                return;

            owned.Add(starting.id);
            current = starting;
        }

        public bool Owns(WeaponDef def) => def != null && owned.Contains(def.id);

        /// <summary>
        /// The purchase gate. The outfit's safe is the wallet where a campaign is
        /// running - the same gate the Armory uses, so the gun counter and the Finances
        /// page can never disagree. A bench scene with no director still sells free,
        /// because there is no money in it to charge.
        /// </summary>
        public bool TryPay(int price)
        {
            var outfit = OutfitDirector.Instance;
            if (outfit == null)
                return true;
            return outfit.Purchase(price, "Sidearm over the counter").Ok;
        }

        public void Grant(WeaponDef def)
        {
            if (def == null || owned.Contains(def.id))
                return;

            owned.Add(def.id);
            Equip(def);
            Debug.Log($"[Arsenal] Bought {def.displayName} (${def.price}).", this);
        }

        /// <summary>
        /// A bought weapon goes straight into the hand. The visual half is a seam: every
        /// def ships prefab-null (one gun mesh in the project), so the revolver stays in
        /// the socket and only the numbers change. When a second mesh exists, the
        /// WeaponSocket Release/Equip swap goes here.
        /// </summary>
        public void Equip(WeaponDef def)
        {
            if (def != null && owned.Contains(def.id))
                current = def;
        }

        // ----------------------------------------------------- the stat accessors
        // The zero rule, applied: the current weapon's number when it has one, the global
        // CombatConfig number when it does not, the class default when even that is gone -
        // the same final fallbacks PlayerMafioso and WeaponController used before.

        public float DamagePerShot(CombatConfig combat) =>
            current != null && current.damagePerShot > 0f
                ? current.damagePerShot
                : combat ? combat.damagePerShot : 50f;

        public float FireInterval(CombatConfig combat) =>
            current != null && current.fireInterval > 0f
                ? current.fireInterval
                : combat ? combat.fireInterval : 0.9f;

        public float EngageRange(CombatConfig combat) =>
            current != null && current.engageRange > 0f
                ? current.engageRange
                : combat ? combat.engageRange : 4f;
    }
}
