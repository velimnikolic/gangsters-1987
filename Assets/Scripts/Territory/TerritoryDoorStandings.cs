namespace LivingCity.Territory
{
    /// <summary>
    /// WHERE ONE DOOR STANDS WITH US, in one word and one line.
    ///
    /// The block file prints a column of these and colours it; the RULE about which
    /// state a door is in, and the WORDS that say so, live here. The page never composes
    /// a sentence about a door - it renders the one this hands it - so the street card,
    /// the paper map and the block file cannot tell the player three different stories
    /// about the same shop.
    ///
    /// Pure and free of UnityEngine: the headless suites drive the whole table.
    ///
    /// The order below is the order it is asked in, and the FIRST match wins. That order
    /// is the priority a boss reads by: a shut door is a shut door whatever it owes, a
    /// door another house holds is not ours to be owed by, and a refusal outranks a debt
    /// because it is the thing that has to be answered.
    /// </summary>
    public static class TerritoryDoorStandings
    {
        /// <summary>The kinds, in the order the seam's DoorStandingKind declares them -
        /// this is a pure layer and must not reference the UI's enum, so it answers the
        /// integer and the page reads it back as its own.</summary>
        public const int Shut = 0;
        public const int Rival = 1;
        public const int Refused = 2;
        public const int Wavering = 3;
        public const int Late = 4;
        public const int Short = 5;
        public const int Paying = 6;
        public const int Unvisited = 7;
        public const int Other = 8;

        /// <summary>Whether a paying door is fully square with this house. A compliant
        /// relationship alone is not enough: the latest envelope may be short or late.</summary>
        public static bool InGoodStanding(
            TerritoryProtectionState state, TerritoryDoorDispatch? lastNews,
            bool hasDues, int owed, int weeklyRate, int lastCollectedDay,
            int missedInARow, int campaignDay)
        {
            if (state != TerritoryProtectionState.Compliant)
                return false;
            if (hasDues && TerritoryCollectionSchedule.IsLate(
                    owed, weeklyRate, campaignDay, lastCollectedDay))
                return false;
            return missedInARow < 1 &&
                   (!lastNews.HasValue ||
                    lastNews.Value.News != TerritoryDoorNews.PaidShort);
        }

        /// <summary>
        /// Reads one door.
        /// </summary>
        /// <param name="state">Where the door stands with US.</param>
        /// <param name="shut">Whether the premises is closed at all.</param>
        /// <param name="closureNote">What the closure says, printed as the line.</param>
        /// <param name="rivalName">The house being paid instead, or empty.</param>
        /// <param name="lastNews">The last thing filed about this door, if any.</param>
        /// <param name="hasDues">Whether the door has a dues account with us.</param>
        /// <param name="owed">Dollars owed right now.</param>
        /// <param name="weeklyRate">What a week at this door is worth.</param>
        /// <param name="lastCollectedDay">Campaign day it last paid, or -1.</param>
        /// <param name="missedInARow">Rounds it has missed running.</param>
        /// <param name="campaignDay">Today.</param>
        /// <param name="collectsWord">"Thursdays" - the block's collection day.</param>
        /// <param name="ours">Whether the family holds the deed to this door.</param>
        public static void Of(
            TerritoryProtectionState state, bool ours, bool shut, string closureNote,
            string rivalName, TerritoryDoorDispatch? lastNews,
            bool hasDues, int owed, int weeklyRate, int lastCollectedDay,
            int missedInARow, int campaignDay, string collectsWord,
            out int kind, out string line, out int outOwed, out int daysLate,
            out int newsDay)
        {
            outOwed = hasDues ? owed : 0;
            daysLate = 0;
            newsDay = lastNews.HasValue ? lastNews.Value.Day : 0;

            // OUR OWN PREMISES HAS NO STANDING WITH US. It is not a door in the racket -
            // it is a room the family owns - so the column falls back to the tenure
            // phrase that says so rather than reporting that nobody has called on it.
            if (ours)
            {
                kind = Other;
                line = "";
                return;
            }

            if (shut)
            {
                kind = Shut;
                line = string.IsNullOrEmpty(closureNote) ? "shut" : closureNote;
                return;
            }

            if (!string.IsNullOrEmpty(rivalName))
            {
                kind = Rival;
                line = rivalName + " holds it";
                return;
            }

            if (state == TerritoryProtectionState.Defiant)
            {
                kind = Refused;
                line = "refused us" + Since(lastNews);
                return;
            }

            if (state == TerritoryProtectionState.Hesitant)
            {
                kind = Wavering;
                line = "wavering" + (newsDay > 0
                    ? " · not visited since day " + newsDay
                    : " · nobody has been back");
                return;
            }

            if (state == TerritoryProtectionState.Compliant)
            {
                if (hasDues && TerritoryCollectionSchedule.IsLate(
                        owed, weeklyRate, campaignDay, lastCollectedDay))
                {
                    kind = Late;
                    daysLate = TerritoryCollectionSchedule.DaysLate(
                        campaignDay, lastCollectedDay);
                    line = "owes $" + owed +
                           (daysLate > 0
                               ? " · " + daysLate +
                                 (daysLate == 1 ? " day late" : " days late")
                               : " · a week behind");
                    return;
                }

                var wentShort = lastNews.HasValue &&
                                lastNews.Value.News == TerritoryDoorNews.PaidShort;
                if (missedInARow >= 1 || wentShort)
                {
                    kind = Short;
                    var excuse = lastNews.HasValue
                        ? TerritoryStandingVocabulary.ExcuseWord(lastNews.Value.Excuse)
                        : "";
                    line = "short last round" +
                           (excuse.Length > 0 ? " · " + excuse.ToLowerInvariant() : "");
                    return;
                }

                kind = Paying;
                line = "pays us" +
                       (owed > 0 ? " · $" + owed + " owed" : "") +
                       (string.IsNullOrEmpty(collectsWord)
                           ? ""
                           : " · collects " + collectsWord.ToLowerInvariant());
                return;
            }

            if (state == TerritoryProtectionState.Unaffiliated && !lastNews.HasValue)
            {
                kind = Unvisited;
                line = "nobody has been to see him";
                return;
            }

            kind = Other;
            line = "";
        }

        /// <summary>The day a thing was last said about this door, as the wire stamps
        /// it. Empty where nothing has been.</summary>
        static string Since(TerritoryDoorDispatch? news) =>
            news.HasValue ? " · day " + news.Value.Day : "";

        /// <summary>Red before amber before the rest: 2 is a door that has to be
        /// answered today, 1 one that will have to be soon.</summary>
        public static int SeverityOf(int kind) =>
            kind == Refused || kind == Late ? 2
            : kind == Wavering || kind == Short ? 1
            : 0;

    }

    /// <summary>
    /// WHO A BLOCK ORDER WALKS, and what the crew does at the door.
    ///
    /// Pure and total. The two lists are deliberately different: a shakedown asks every
    /// door that has not answered us, and leaning on the holdouts threatens the ones
    /// that said no or will not say yes. A door can be on both - a waverer is a door
    /// still worth asking AND a door worth frightening - and that is the point: the
    /// player chooses which of the two he is doing.
    /// </summary>
    public static class TerritoryShakedown
    {
        /// <summary>
        /// A door still worth ASKING - one that does not pay us yet and has not told us
        /// no. What SHAKE DOWN THE BLOCK walks.
        ///
        /// OUR OWN PREMISES ARE NOT ON IT. A shop the family holds the deed to has no
        /// protection to be sold: it never entered the racket, so its standing sits at
        /// Unaffiliated for ever and it would otherwise be asked on every sweep of its
        /// block - the crew turning up to shake down the headquarters. The deed is the
        /// test, and the caller supplies it because a deed is the business layer's fact,
        /// not the territory layer's.
        /// </summary>
        public static bool WorthAsking(TerritoryProtectionState state, bool ours) =>
            !ours &&
            (state == TerritoryProtectionState.Unaffiliated ||
             state == TerritoryProtectionState.Approached ||
             state == TerritoryProtectionState.Hesitant ||
             state == TerritoryProtectionState.Intimidated);

        /// <summary>A door HOLDING OUT: it refused us, or it is wavering and has not
        /// been brought round. What LEAN ON THE HOLDOUTS walks - and never one of ours,
        /// for the same reason.</summary>
        public static bool IsHoldout(TerritoryProtectionState state, bool ours) =>
            !ours &&
            (state == TerritoryProtectionState.Defiant ||
             state == TerritoryProtectionState.Hesitant);

        /// <summary>
        /// Whether the crew leans on this door ON THE SPOT after the answer it just got.
        ///
        /// A no or a maybe is the only thing worth leaning on - a yes is a yes. Whether
        /// the men then DO lean is the lieutenant's standing policy: Lenient and Normal
        /// crews walk on and file the refusal, Strict and Brutal ones put hands on the
        /// door while they are still standing in it. That is the whole of what policy
        /// decides here; what a lean COSTS is the racket's business.
        /// </summary>
        public static bool ThreatenAfter(
            TerritoryComplianceVerdict verdict, int policyLevel) =>
            policyLevel >= (int)LivingCity.Personnel.CrewPolicy.Strict &&
            (verdict == TerritoryComplianceVerdict.Refuse ||
             verdict == TerritoryComplianceVerdict.Hesitate);
    }
}
