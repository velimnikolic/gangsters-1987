using LivingCity.Gangs;
using LivingCity.Territory;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// THE ONE PLACE THE PLAYER'S NAME GOES ON AN ORDER.
    ///
    /// Twenty-one families file through one gateway, and every order carries the house
    /// that filed it (<see cref="ITerritoryHouseCommand"/>). A mind puts its own house
    /// on its own orders; the ledger, the block file, the turf map and the street menu
    /// all put the player's on his - here, and nowhere else, so that "which house is
    /// this?" can never be answered by a rule reading a constant it happened to have
    /// to hand.
    ///
    /// A source-scan contract holds the line: nothing outside this helper may stamp a
    /// house id onto a command.
    /// </summary>
    public static class PlayerCommands
    {
        /// <summary>The player's own house, as an order names it.</summary>
        public static TerritoryGangId House =>
            new TerritoryGangId(GangCatalog.PlayerGangId);

        /// <summary>
        /// The player's name on an order he is filing. A struct constraint, so the
        /// stamp costs no boxing on the path every click takes.
        /// </summary>
        public static T Stamp<T>(T command) where T : struct, ITerritoryHouseCommand
        {
            command.House = House;
            return command;
        }
    }
}
