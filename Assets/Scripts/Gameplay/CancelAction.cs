namespace LivingCity.Gameplay
{
    /// <summary>
    /// The menu's way out. Executes nothing - the menu closes itself before any action
    /// runs, so closing IS the whole effect - but its presence means every menu has a
    /// harmless bottom row, which matters more on touch, where there is no Esc.
    /// </summary>
    public sealed class CancelAction : IContextAction
    {
        public string Label => "Cancel";

        public int SortOrder => int.MaxValue;

        public bool IsAvailable(PlayerMafioso actor, IContextTarget target) => true;

        public void Execute(PlayerMafioso actor, IContextTarget target)
        {
            // Deliberately nothing.
        }
    }
}
