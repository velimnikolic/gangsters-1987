using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Anything the right-click menu can open over. The menu was hard-typed to
    /// InteractableNpc while pedestrians were the only thing worth a menu; buildings are
    /// now targets too (the gun shop first, BusinessMarker's racket actions later), and
    /// this is the one seam they all come through. An action decides what it can serve by
    /// pattern-matching the target - KillAction wants an InteractableNpc, BuyWeaponAction
    /// wants a GunShopMarker - so adding a target kind never touches the menu or the
    /// registry.
    /// </summary>
    public interface IContextTarget
    {
        /// <summary>Where the target stands - actions that approach walk toward this.</summary>
        Transform TargetTransform { get; }

        /// <summary>False keeps the menu shut entirely - a dead pedestrian, a burnt-out
        /// business. Availability of the WHOLE menu, not of one row.</summary>
        bool ContextAvailable { get; }
    }
}
