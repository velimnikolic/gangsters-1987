using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.Police;
using LivingCity.UI;

namespace RoadDemo
{
    /// <summary>
    /// WHAT THE BOSS CAN DO ABOUT A MAN THE CITY IS HOLDING (GAN-245).
    ///
    /// Three decisions and no more: put up his bail, tell him not to appear, or cut him
    /// loose. They are taken on his own file in the ledger, and they live here rather
    /// than in the page because two of them move money and all three move the docket -
    /// a click handler that reached into the pipeline and the safe by itself would be a
    /// fourth place that knows the rules.
    ///
    /// Every one of them refuses in words (LedgerText), and the money leaves the safe
    /// through the one purchase gate there has ever been (OutfitDirector.Purchase), so
    /// bail lands on the day sheet beside every other cost.
    /// </summary>
    public static class LawDesk
    {
        /// <summary>The docket, or null in a scene with no station in it.</summary>
        public static PrisonPipeline Pipeline =>
            PoliceForce.Instance != null ? PoliceForce.Instance.Pipeline : null;

        /// <summary>What the city has this man in for, or null.</summary>
        public static Prisoner Held(int characterId) =>
            Pipeline != null ? Pipeline.Find(characterId) : null;

        /// <summary>The case he is on, or null.</summary>
        public static CourtCase CaseOf(int characterId) =>
            Pipeline != null ? Pipeline.CaseOf(characterId) : null;

        /// <summary>What it would cost to get him out; 0 where there is no bail.</summary>
        public static int BailPrice(int characterId) =>
            PrisonPipeline.BailPrice(Held(characterId));

        /// <summary>Why he cannot be bailed, or null when he can - the lawyer's own
        /// gate included, because a man with no counsel gets no hearing listed.</summary>
        public static string BailRefusal(int characterId)
        {
            var pipeline = Pipeline;
            if (pipeline == null)
                return LedgerText.ReasonNoCase;
            var roster = PersonnelDirector.Instance != null
                ? PersonnelDirector.Instance.Roster : null;
            return pipeline.BailRefusal(Held(characterId), Lawyer.SkillOf(roster));
        }

        /// <summary>
        /// POST BAIL. The money goes first and the man follows: a safe that cannot
        /// cover it leaves him exactly where he was, and nothing is half done.
        /// </summary>
        public static OpResult PostBail(int characterId)
        {
            var pipeline = Pipeline;
            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            if (pipeline == null || roster == null)
                return OpResult.Fail(LedgerText.ReasonNoCase);

            var prisoner = pipeline.Find(characterId);
            var refusal = pipeline.BailRefusal(prisoner, Lawyer.SkillOf(roster));
            if (refusal != null)
                return OpResult.Fail(refusal);

            var price = PrisonPipeline.BailPrice(prisoner);
            var outfit = OutfitDirector.Instance;
            if (outfit != null)
            {
                var paid = outfit.Purchase(price, "bail");
                if (!paid.Ok)
                    return paid;
            }

            var today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            if (!pipeline.PostBail(roster, prisoner, price, today))
            {
                // The pipeline refused after the safe paid: unbook it exactly as it was
                // booked, the way a refused hire is (PersonnelDirector.HireFromAd).
                if (outfit != null) outfit.Refund(price, "bail");
                return OpResult.Fail(LedgerText.ReasonNotInside);
            }

            var man = roster.Find(characterId);
            LawWire.BailPosted(man);
            director.Touch();
            return OpResult.Success;
        }

        /// <summary>
        /// SKIP BAIL. Nothing happens until his court day - the money is written off
        /// then, not now, so a boss who changes his mind still has a man to send.
        /// </summary>
        public static OpResult SkipBail(int characterId)
        {
            var pipeline = Pipeline;
            if (pipeline == null)
                return OpResult.Fail(LedgerText.ReasonNoCase);
            var prisoner = pipeline.Find(characterId);
            if (prisoner == null || prisoner.Stage != PrisonStage.Bailed)
                return OpResult.Fail(LedgerText.ReasonNotInside);
            if (!pipeline.SkipBail(prisoner))
                return OpResult.Fail(LedgerText.ReasonNotInside);

            var director = PersonnelDirector.Instance;
            if (director != null) director.Touch();
            return OpResult.Success;
        }

        /// <summary>Whether the boss has already told him not to appear.</summary>
        public static bool Skipping(int characterId)
        {
            var prisoner = Held(characterId);
            return prisoner != null && prisoner.SkipOrdered;
        }

        /// <summary>
        /// CUT HIM LOOSE. The outfit's file closes and the city keeps him - and the men
        /// are told, at the weights Loyalty prints (RosterOps.CutLoose does both).
        /// </summary>
        public static OpResult CutLoose(int characterId)
        {
            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            if (roster == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);

            var man = roster.Find(characterId);
            // The loyalty movements land on the campaign's own list beside the ones
            // midnight makes: a betrayal nobody can account for is the one thing the
            // personality layer's single door exists to prevent.
            var outfit = OutfitDirector.Instance;
            var changes = outfit != null && outfit.Runner != null
                ? outfit.Runner.CharacterChanges : null;
            var result = RosterOps.CutLoose(roster, characterId, changes);
            if (!result.Ok)
                return result;

            Pipeline?.CutLoose(characterId);
            LawWire.CutLoose(man);
            director.Touch();
            return OpResult.Success;
        }

        /// <summary>Whether the boss may decline to carry him at all - a man in a cell
        /// or out on the outfit's own money, and nobody else.</summary>
        public static bool CanCutLoose(Character man) =>
            man != null && !man.Gone &&
            (man.Status == CharacterStatus.Jailed || man.BailedUntil > 0);
    }
}
