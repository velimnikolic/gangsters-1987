using LivingCity.Entities;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// One shop row: right-click the gun shop, buy this weapon. The first action to serve
    /// a building target - it pattern-matches GunShopMarker and says no to pedestrians,
    /// which is the whole of how the widened menu keeps Kill and Buy from ever sharing a
    /// menu. Registered by GameplayBootstrap, one instance per non-starting catalog entry,
    /// and disappears from the menu once owned - the registry rebuilds the row list at
    /// every open, so no cleanup is needed.
    /// </summary>
    public sealed class BuyWeaponAction : IContextAction
    {
        readonly WeaponDef def;
        readonly int index;

        public BuyWeaponAction(WeaponDef def, int index)
        {
            this.def = def;
            this.index = index;
        }

        /// <summary>The price on the label IS the economy's UI for now - when the wallet
        /// lands, the row is already telling the player what he is about to lose.</summary>
        public string Label => $"Buy {def.displayName} (${def.price})";

        /// <summary>Below Kill's 0, above Cancel's pin - catalog order preserved.</summary>
        public int SortOrder => 10 + index;

        public bool IsAvailable(PlayerMafioso actor, IContextTarget target)
        {
            if (!actor || actor.IsDead || !(target is GunShopMarker))
                return false;

            var arsenal = actor.GetComponent<PlayerArsenal>();
            return arsenal && !arsenal.Owns(def);
        }

        public void Execute(PlayerMafioso actor, IContextTarget target)
        {
            var arsenal = actor ? actor.GetComponent<PlayerArsenal>() : null;
            if (!arsenal || !arsenal.TryPay(def.price))
                return;

            arsenal.Grant(def);

            // The walk sells the visit: the deal happens at the counter, not across the
            // street. Becomes an order that PAYS on arrival when money lands.
            if (target is GunShopMarker shop && shop)
                actor.OrderMove(shop.StandWorld);
        }
    }
}
