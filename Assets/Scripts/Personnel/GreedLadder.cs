using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// What an underpaid man with an eye for money does about it. Not a modifier: three
    /// things that HAPPEN, in order, each of them visible somewhere the player can find
    /// it - money missing off a block, a rumour in the paper, a man asking for more.
    ///
    /// The ladder is climbed by TIME, not by luck: a man over the greed line who has
    /// been underpaid long enough starts skimming on a known day, takes rival money on
    /// a known day, and asks for a raise on a known day. Given the same campaign it is
    /// the same day every time, which is what lets the player learn the pattern instead
    /// of being ambushed by it.
    ///
    /// Pure and free of UnityEngine and of the Outfit layer: the pay gap is handed in,
    /// because what a man is worth is the wage table's business.
    /// </summary>
    public static class GreedLadder
    {
        /// <summary>Below this much Greed a man never climbs the ladder at all,
        /// whatever he is paid. Most men just work.</summary>
        public const int GreedFloor = 60;

        /// <summary>Days underpaid before he starts taking a cut of what he
        /// handles.</summary>
        public const int SkimAfterDays = 7;

        /// <summary>Before somebody else's money starts looking reasonable.</summary>
        public const int RivalAfterDays = 21;

        /// <summary>Before he stops being quiet about it and asks.</summary>
        public const int DemandAfterDays = 35;

        /// <summary>What a skimming man takes off a collection he handles, percent. A
        /// fifth is enough to show as thin takes on a block without being obvious.</summary>
        public const int SkimPercent = 20;

        /// <summary>However many hands are in it, a round of collections never comes
        /// back with nothing - that would read as a failed job, and it was not one.</summary>
        public const int MaxSkimPercent = 60;

        /// <summary>What rival money costs the outfit in loyalty the day he takes
        /// it.</summary>
        public const int RivalLoyaltyHit = 12;

        /// <summary>And what refusing his demand costs, on top.</summary>
        public const int RefusalLoyaltyHit = 15;

        /// <summary>Percent chance his superior notices, per half-step of the two
        /// stats that count a room: what he sees and how well he keeps his books.</summary>
        public const int CatchPerHalfStep = 2;

        /// <summary>
        /// The superior counts up. A short take is only a short take until somebody
        /// with an eye for both the street and the paperwork looks at it twice.
        /// </summary>
        /// <returns>True when the skim was caught, and the man has stopped.</returns>
        public static bool TryCatch(Character skimmer, int superiorAwarenessHalfSteps,
            int superiorOrganizationHalfSteps, System.Random rng, int day, string where,
            List<Incident> incidents)
        {
            if (skimmer == null || rng == null || !skimmer.Skimming)
                return false;

            var eye = (superiorAwarenessHalfSteps + superiorOrganizationHalfSteps) *
                      CatchPerHalfStep;
            if (rng.Next(100) >= eye)
                return false;

            skimmer.Skimming = false;
            RapSheet.Add(skimmer, "", "Short in the count", "Established");
            incidents?.Add(new Incident(skimmer.Id, skimmer.FullName,
                IncidentKind.CaughtSkimming, day, where, 0,
                skimmer.FullName + " has been taking a cut off the top" +
                (string.IsNullOrEmpty(where) ? "" : " at " + where) + "."));
            return true;
        }

        /// <summary>
        /// One day of the books, for one man. Both figures come from the wage table:
        /// what he DRAWS and what a man of his rank and stats is WORTH. The gap between
        /// them is the whole mechanism, and the worth is what he eventually asks for -
        /// asking for his current envelope plus the gap would leave a man on the house
        /// scale, whose envelope is not a bargain at all, still short after being given
        /// exactly what he asked.
        /// </summary>
        public static void Tick(Character man, int paidNow, int worth, int day,
            List<Incident> incidents, List<PersonalityChange> changes)
        {
            if (man == null || man.Gone)
                return;

            var payGap = worth - paidNow;
            if (payGap <= 0)
            {
                // Paid what he is worth: the clock resets and whatever he was doing
                // about it stops. A raise is the one thing that reliably fixes a man.
                man.UnderpaidSince = 0;
                man.Skimming = false;
                man.WageDemand = 0;
                return;
            }

            if (man.UnderpaidSince <= 0)
                man.UnderpaidSince = day;

            if (Personality.Get(man, PersonalityTrait.Greed) <= GreedFloor)
                return;

            var days = day - man.UnderpaidSince;

            if (days == SkimAfterDays && !man.Skimming)
            {
                man.Skimming = true;
                // Nothing is printed. This is the one thing on the ladder the player is
                // not told about: it shows as thin takes on a block until somebody
                // catches him at it.
                RapSheet.Add(man, "", "Short in the count", "Not established");
                return;
            }

            if (days == RivalAfterDays)
            {
                RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty,
                    -RivalLoyaltyHit, "took money from somebody else", changes);
                incidents?.Add(new Incident(man.Id, man.FullName, IncidentKind.TookRivalMoney,
                    day, "", 0,
                    "Word is " + man.FullName + " has been drinking with men who are " +
                    "not ours."));
                return;
            }

            if (days == DemandAfterDays && man.WageDemand <= 0)
            {
                // What he asks is the rate for a man like him, and not a cent over: he
                // wants what he is worth, not a windfall.
                man.WageDemand = worth;
                incidents?.Add(new Incident(man.Id, man.FullName, IncidentKind.DemandedARaise,
                    day, "", 0,
                    man.FullName + " wants his envelope brought up to the rate."));
            }
        }
    }
}
