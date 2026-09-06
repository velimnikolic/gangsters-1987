using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// WHO A LIEUTENANT HANDS THE BAG TO (GAN-262). The player may name any man of the
    /// crew from the ledger; where he does not, the lieutenant picks one himself, and how
    /// well he picks is his own Organization skill: a four-star organizer gives the bag
    /// to the best man he has, a one-star organizer gives it to whoever is nearest the
    /// door. Anyone in the crew is a candidate - the men who walk the street included.
    ///
    /// Pure and total, like the rest of Personnel: the same crew under the same
    /// lieutenant always reads the same man, there is no draw anywhere, and every tie is
    /// broken by the ledger's own order (lower greed, then lower id).
    /// </summary>
    public static class CollectorChoice
    {
        public const string NoGroundReason =
            "his leader has no blocks to collect from · assign him a block first";

        /// <summary>Appointment requires the same canonical responsibility the round
        /// scheduler reads. Dues and the weekday decide departure, not appointment.</summary>
        public static string GroundRefusal(Roster roster, Crew crew)
        {
            if (roster == null || crew == null)
                return UI.LedgerText.ReasonNoSuchCrew;
            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
                if (paper[i].LeaderId == crew.LieutenantId && paper[i].BlockId.IsValid)
                    return null;
            return NoGroundReason;
        }

        /// <summary>
        /// What makes a good bag man, in half-steps: he knows what a shop turns over
        /// (Streetwise), gets the money without a scene (Persuasion) and notices the
        /// tail on the way home (Awareness). Combat is not on the list - the bag is
        /// carried, not fought over, and a man who fights for it is a man who lost it.
        /// </summary>
        public static int Fitness(Character man) =>
            man == null
                ? 0
                : man.GetHalfSteps(CharacterAttribute.Streetwise) +
                  man.GetHalfSteps(CharacterAttribute.Persuasion) +
                  man.GetHalfSteps(CharacterAttribute.Awareness);

        /// <summary>
        /// The place in the fitness-sorted list (best first) a lieutenant of this
        /// Organization reaches for. Four stars and up sees the best man; three sees the
        /// second; two picks from the middle; one takes the worst. Clamped to the list
        /// so a crew of one always yields its one man. -1 for an empty list.
        /// </summary>
        public static int PickRank(int organizationHalfSteps, int candidates)
        {
            if (candidates <= 0)
                return -1;
            int rank;
            if (organizationHalfSteps >= 8)
                rank = 0;
            else if (organizationHalfSteps >= 6)
                rank = 1;
            else if (organizationHalfSteps >= 4)
                rank = candidates / 2;
            else
                rank = candidates - 1;
            return rank < 0 ? 0 : rank >= candidates ? candidates - 1 : rank;
        }

        /// <summary>Best first: fitness descending, then the less greedy man (he skims
        /// less), then the lower id so the order is total.</summary>
        public static int Compare(Character a, Character b)
        {
            var byFitness = Fitness(b).CompareTo(Fitness(a));
            if (byFitness != 0)
                return byFitness;
            var byGreed = a.Greed.CompareTo(b.Greed);
            return byGreed != 0 ? byGreed : a.Id.CompareTo(b.Id);
        }

        /// <summary>The men of the crew who could carry it today: hoods on the books,
        /// on their feet, not in a cell or a bed. Sorted best first.</summary>
        public static void Candidates(Roster roster, Crew crew, List<Character> into)
        {
            into?.Clear();
            if (roster == null || crew == null || into == null)
                return;
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var man = roster.Find(crew.HoodIds[i]);
                if (man != null && !man.Gone && man.Rank == Rank.Hood &&
                    man.Status == CharacterStatus.Active && !man.OutOfTown &&
                    roster.DoorOrders.Find(man.Id) == null)
                    into.Add(man);
            }
            into.Sort(Compare);
        }

        /// <summary>The man this lieutenant hands the bag to, or -1 when he has nobody
        /// on his feet to hand it to.</summary>
        public static int Pick(Roster roster, Crew crew)
        {
            if (roster == null || crew == null)
                return -1;
            var lieutenant = roster.Find(crew.LieutenantId);
            var organization = lieutenant != null
                ? lieutenant.GetHalfSteps(CharacterAttribute.Organization)
                : AttributeScale.MinHalfSteps;
            var candidates = new List<Character>();
            Candidates(roster, crew, candidates);
            var rank = PickRank(organization, candidates.Count);
            return rank < 0 ? -1 : candidates[rank].Id;
        }
    }
}
