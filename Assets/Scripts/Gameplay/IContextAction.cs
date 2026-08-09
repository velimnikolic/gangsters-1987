namespace LivingCity.Gameplay
{
    /// <summary>
    /// One entry in the right-click menu. The menu is built from every registered action
    /// whose IsAvailable says yes, so adding an option to the game is: write a class,
    /// register it in ContextActionRegistry - no menu code, no UI change. That contract is
    /// the point; Rob, Talk, Recruit, Follow and Frame are all this interface later.
    /// </summary>
    public interface IContextAction
    {
        /// <summary>The button's text.</summary>
        string Label { get; }

        /// <summary>Menu position - lower first. Cancel pins itself last with int.MaxValue.</summary>
        int SortOrder { get; }

        /// <summary>Whether this action appears for this actor/target pair right now.</summary>
        bool IsAvailable(PlayerMafioso actor, InteractableNpc target);

        /// <summary>Do it. The menu has already closed itself by the time this runs.</summary>
        void Execute(PlayerMafioso actor, InteractableNpc target);
    }
}
