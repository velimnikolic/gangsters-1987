namespace LivingCity.Territory
{
    /// <summary>
    /// THE HIDEOUT (GAN-235). One address in the city that the family has said is the one
    /// a man runs to.
    ///
    /// Hiding itself is older than this: a crew that breaks a pursuit walks into the
    /// nearest door the outfit already holds and is off the street (PoliceDispatch.Wanted,
    /// CrewQuarters). What was missing is that the door was never a place the PLAYER
    /// chose - it was whichever shop of ours happened to be closest, which is a fallback
    /// and not a plan.
    ///
    /// So the designation lives here, as one id and nothing else:
    ///
    ///   * ONE hideout in v1. Naming a second MOVES it - there is no list, because a
    ///     player with three hideouts has none he can name;
    ///   * it is keyed by the premises, never by a GameObject. The block it stands on is
    ///     streamed in and out a hundred times a session and the designation must not
    ///     travel with the bricks;
    ///   * and it is only ours while the paper is. A deed sold, lost or taken clears the
    ///     designation the moment it changes hands (BusinessDeeds.SetGang calls
    ///     <see cref="DeedChanged"/>), so the map can never point at a building somebody
    ///     else owns.
    ///
    /// Pure and free of UnityEngine, like the rest of the sim's arithmetic, so the police
    /// suite can set, move and clear it with no scene at all. The Play-mode reset rides on
    /// BusinessDeeds' own, because a designation without its deed is meaningless anyway.
    /// </summary>
    public static class TerritoryHideout
    {
        /// <summary>The premises, or an invalid id when the family has not named one.</summary>
        public static TerritoryBusinessId Where { get; private set; }

        /// <summary>Has the family a hideout at all?</summary>
        public static bool Any => Where.IsValid;

        /// <summary>A repaint key: it moves whenever the address does. The surfaces PULL
        /// - the map paints off this number and the door menu asks at the moment it is
        /// painted - so there is no event here for somebody to forget to unsubscribe.
        /// </summary>
        public static int Version { get; private set; }

        public static bool Is(TerritoryBusinessId id) => id.IsValid && Where == id;

        /// <summary>
        /// MAKE THIS THE HIDEOUT. A second designation moves it rather than adding one.
        /// False when the id is no premises at all, or when it is already the hideout -
        /// so a surface can tell "nothing happened" from "the address moved".
        /// </summary>
        public static bool Designate(TerritoryBusinessId id)
        {
            if (!id.IsValid || Where == id)
                return false;
            Where = id;
            Version++;
            return true;
        }

        /// <summary>Give it up. False when there was nothing to give up.</summary>
        public static bool Clear()
        {
            if (!Where.IsValid)
                return false;
            Where = default;
            Version++;
            return true;
        }

        /// <summary>
        /// A deed changed hands. If it was the hideout's and the family is not on it any
        /// more, the designation goes with the paper - sold, taken or burned, it is not
        /// our door to walk into.
        /// </summary>
        public static void DeedChanged(TerritoryBusinessId id, int gangId, int playerGangId)
        {
            if (Is(id) && gangId != playerGangId)
                Clear();
        }

        /// <summary>Statics outlive Play with domain reload off - closed the same way the
        /// deed book is, and from the same place.</summary>
        public static void Reset()
        {
            Where = default;
            Version++;
        }
    }
}
