using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What is left of the map's own order book after it stopped having one.
    ///
    /// The map used to carry a vocabulary invented from the design sheet - MOVE HERE,
    /// ATTACK HERE, PATROL AREA, HOLD POSITION, FALL BACK - with its own state table
    /// behind it. That was the wrong shape for this game. The street already has orders,
    /// the player already knows them, and a second set that exists only on the map is a
    /// second game to learn. So the right button on the map now resolves exactly as it
    /// does in the street and calls the same verbs on <see cref="DemoCrews"/>, and what
    /// remains here is the two things that are genuinely the map's own:
    ///
    ///   the MARKER it drops where an order was given, and
    ///   the HOOKS for the card's verbs that no rule has been written for yet.
    ///
    /// The hooks are null by default and are called where the rule belongs. Whether
    /// ground can be taken at all, what it costs, what a stakeout watches for, what a
    /// headquarters IS - none of that is decided anywhere in this project, and deciding
    /// it here would be putting a mechanic into the game through a map.
    /// </summary>
    public static class MapOrders
    {
        /// <summary>What a marker on the ground is for. It decides only its colour.</summary>
        public enum Kind
        {
            Move,
            Attack,
            Claim,
        }

        // ---------------------------------------------------------------- the hooks

        /// <summary>CLAIM BUILDING. The map has already flipped the deed in
        /// <see cref="MapOwnership"/> and fired the marker; this is where the price, the
        /// refusal, and the reaction of whoever held it belong.</summary>
        public static System.Action<MapBuilding, int> Claimed;

        /// <summary>STAKEOUT on a building: who watches, for how long, and what it is
        /// they are supposed to see.</summary>
        public static System.Action<DemoCrews.Unit, MapBuilding> Stakeout;

        /// <summary>EXTORT and SET HQ - both log-only in the design sheet too.</summary>
        public static System.Action<MapBuilding> Extort;
        public static System.Action<MapBuilding> MakeHq;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Claimed = null;
            Stakeout = null;
            Extort = null;
            MakeHq = null;
        }

        // ---------------------------------------------------------- what they are at

        /// <summary>
        /// What a crew is doing, for the roster to print - read off the men themselves
        /// rather than remembered from the last thing the map told them.
        ///
        /// It used to be a table keyed by crew id, written whenever the map issued an
        /// order. That table was a lie the moment anything else moved the crew - a job
        /// off the ledger's order book, a fight it walked into, a car it got into - and
        /// the roster would go on printing MOVE at men who had been standing still for a
        /// minute. A crew's own state cannot go stale.
        /// </summary>
        public static string StateOf(DemoCrews.Unit unit)
        {
            if (unit == null)
                return "-";
            if (unit.Wiped)
                return "WIPED";
            if (unit.TargetUnit != null && !unit.TargetUnit.Wiped)
                return "FIGHTING";

            var boss = unit.Boss;
            if (boss == null || boss.Dead)
                return "-";

            switch (boss.State)
            {
                case CrewWalker.Mode.Walking:
                case CrewWalker.Mode.Striding:
                    return "MOVING";
                case CrewWalker.Mode.Homing:
                    return "GOING HOME";
                case CrewWalker.Mode.Engaging:
                    return "FIGHTING";
                case CrewWalker.Mode.Fleeing:
                    return "RUNNING";
                case CrewWalker.Mode.Riding:
                    return "IN CAR";
                case CrewWalker.Mode.Dead:
                    return "DOWN";
                default:
                    return "HOLDING";
            }
        }

        // ------------------------------------------------------------------ markers

        /// <summary>An expanding cross at a place an order was given - the sheet's own
        /// seventy frames, growing a pixel every fourteen.</summary>
        public sealed class Marker
        {
            public Vector2 World;
            public Kind Kind;
            public int Life;
        }

        public const int MarkerLife = 70;
        const int MarkerGrow = 14;

        public static int MarkerRadius(Marker marker) =>
            1 + (MarkerLife - marker.Life) / MarkerGrow;

        public static Color32 MarkerColour(Kind kind)
        {
            switch (kind)
            {
                case Kind.Attack: return MapPalette.Red;
                case Kind.Claim:
                    return MapPalette.Gang(LivingCity.Gangs.GangCatalog.PlayerGangId);
                default: return MapPalette.Yellow;
            }
        }
    }
}
