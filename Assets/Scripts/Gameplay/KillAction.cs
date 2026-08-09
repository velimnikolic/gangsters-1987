namespace LivingCity.Gameplay
{
    /// <summary>
    /// The first real context action: hand the target to the player's kill order. All the
    /// choreography - approach, aim, fire, the beat after - lives in PlayerMafioso and
    /// WeaponController; the action is just the menu's handle on it.
    /// </summary>
    public sealed class KillAction : IContextAction
    {
        public string Label => "Kill";

        public int SortOrder => 0;

        public bool IsAvailable(PlayerMafioso actor, InteractableNpc target) =>
            actor && target && !target.IsDead;

        public void Execute(PlayerMafioso actor, InteractableNpc target) =>
            actor.OrderKill(target);
    }
}
