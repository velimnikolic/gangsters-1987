using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Outfit
{
    /// <summary>
    /// The one door through which a house's men and money change - the ledger's buttons
    /// and a mind's intents alike. Everything here is the existing rule
    /// (<see cref="RosterOps"/>, <see cref="BalanceMath"/>) with the house's own roster
    /// and its own safe put in front of it, plus the two things every mutation owes the
    /// world afterwards: the arms are re-dealt and the house's Version moves.
    ///
    /// It exists so that "the player can do this and a rival cannot" is impossible to
    /// write by accident. If a mind may do it, the player's ledger does it through this
    /// same call, and the other way round.
    ///
    /// Pure and free of UnityEngine.
    /// </summary>
    public static class HouseOps
    {
        // --------------------------------------------------------------------- men

        /// <summary>
        /// One more man on the books, off a corner. ONE PRICE THROUGH EVERY DOOR
        /// (<see cref="EconomyPrices.RecruitSigning"/>), paid out of this house's own
        /// safe before anybody is dealt, and a signing never fails once it is paid for.
        ///
        /// <paramref name="crewId"/> puts him straight under a lieutenant; a branch at
        /// its cap refuses him and he waits in the Boss's pool, which is where the
        /// seeder left him. <paramref name="recruiterId"/> is whoever went looking -
        /// his Awareness buys the second looks that find a better man. Both are
        /// optional: the ledger's counter passes neither.
        /// </summary>
        public static OpResult Recruit(House house, out Character member,
            int crewId = -1, int recruiterId = -1)
        {
            member = null;
            if (house?.Roster == null || house.Runner == null)
                return OpResult.Fail(UI.LedgerText.ReasonNoSuchMember);

            var refusal = BalanceMath.TryPurchase(
                house.Runner.Accounts, EconomyPrices.RecruitSigning);
            if (refusal != null)
                return OpResult.Fail(refusal);

            var recruiter = recruiterId >= 0 ? house.Roster.Find(recruiterId) : null;
            var eye = recruiter != null
                ? recruiter.GetHalfSteps(CharacterAttribute.Awareness)
                : 0;

            member = RosterSeeder.Recruit(house.Roster, house.Draw, eye,
                recruiter != null ? recruiter.FullName : "");
            if (crewId >= 0)
                RosterOps.AssignToCrew(house.Roster, member.Id, crewId);

            Settle(house);
            return OpResult.Success;
        }

        public static OpResult AssignToCrew(House house, int id, int crewId,
            List<PersonalityChange> changes = null) =>
            Commit(house, house?.Roster != null
                ? RosterOps.AssignToCrew(house.Roster, id, crewId, changes)
                : NoRoster);

        public static OpResult AssignToPool(House house, int id) =>
            Commit(house, house?.Roster != null
                ? RosterOps.AssignToPool(house.Roster, id)
                : NoRoster);

        public static OpResult AssignToBoss(House house, int id) =>
            Commit(house, house?.Roster != null
                ? RosterOps.AssignToBoss(house.Roster, id, house.Roster.BossId)
                : NoRoster);

        public static OpResult AssignToFront(House house, int id) =>
            Commit(house, house?.Roster != null
                ? RosterOps.AssignToFront(house.Roster, id)
                : NoRoster);

        public static OpResult SetDuty(House house, int id, Duty duty) =>
            Commit(house, house?.Roster != null
                ? RosterOps.SetDuty(house.Roster, id, duty)
                : NoRoster);

        public static OpResult Promote(House house, int id, out int newCrewId,
            List<Incident> incidents = null, List<PersonalityChange> changes = null)
        {
            newCrewId = -1;
            if (house?.Roster == null)
                return NoRoster;
            return Commit(house,
                RosterOps.Promote(house.Roster, id, out newCrewId, incidents, changes));
        }

        public static OpResult Demote(House house, int id,
            List<Incident> incidents = null, List<PersonalityChange> changes = null) =>
            Commit(house, house?.Roster != null
                ? RosterOps.Demote(house.Roster, id, incidents, changes)
                : NoRoster);

        /// <summary>How a crew works a door. Not a RosterOps rule - the policy is a
        /// field on the crew - so the refusal is written here.</summary>
        public static OpResult SetPolicy(House house, int crewId, CrewPolicy policy)
        {
            var crew = house?.Roster?.FindCrew(crewId);
            if (crew == null)
                return OpResult.Fail(UI.LedgerText.ReasonNoSuchCrew);
            crew.Policy = policy;
            house.Touch();
            return OpResult.Success;
        }

        public static OpResult AssignBlock(House house, TerritoryBlockId blockId,
            int leaderId, bool blockExists) =>
            Commit(house, house?.Roster != null
                ? RosterOps.AssignBlockResponsibility(
                    house.Roster, blockId, leaderId, blockExists)
                : NoRoster);

        public static OpResult RemoveBlock(House house, TerritoryBlockId blockId,
            int expectedLeaderId = -1) =>
            Commit(house, house?.Roster != null
                ? RosterOps.RemoveBlockResponsibility(
                    house.Roster, blockId, expectedLeaderId)
                : NoRoster);

        // ---------------------------------------------------------------- casualties

        public static OpResult Kill(House house, int id,
            List<PersonalityChange> changes = null) =>
            Commit(house, house?.Roster != null
                ? RosterOps.Kill(house.Roster, id, changes)
                : NoRoster);

        public static OpResult Desert(House house, int id, string story = "",
            int weight = 0, List<PersonalityChange> changes = null) =>
            Commit(house, house?.Roster != null
                ? RosterOps.Desert(house.Roster, id, story, weight, changes)
                : NoRoster);

        public static OpResult Hospitalize(House house, int id, int backOnDay,
            string note = "") =>
            Commit(house, house?.Roster != null
                ? RosterOps.Hospitalize(house.Roster, id, backOnDay, note)
                : NoRoster);

        public static OpResult Jail(House house, int id, int backOnDay,
            string note = "", string charge = "", string dateStamp = "") =>
            Commit(house, house?.Roster != null
                ? RosterOps.Jail(house.Roster, id, backOnDay, note, charge, dateStamp)
                : NoRoster);

        // -------------------------------------------------------------------- money

        /// <summary>Money out of this house's safe, booked on this house's sheet. The
        /// same gate the Armory counter uses, with the family named.</summary>
        public static OpResult Purchase(House house, int price)
        {
            if (house?.Runner == null)
                return OpResult.Fail(UI.LedgerText.ReasonFinanceUnavailable);
            var refusal = BalanceMath.TryPurchase(house.Runner.Accounts, price);
            if (refusal != null)
                return OpResult.Fail(refusal);
            house.Touch();
            return OpResult.Success;
        }

        /// <summary>Money back, and the purchase line with it - a sale that fell
        /// through is not a sale.</summary>
        public static void Refund(House house, int price)
        {
            if (house?.Runner == null || price <= 0)
                return;
            house.Runner.Accounts.Safe += price;
            if (house.Runner.Accounts.Current != null)
                house.Runner.Accounts.Current.Purchases -= price;
            house.Touch();
        }

        // ------------------------------------------------------------------ plumbing

        static readonly OpResult NoRoster =
            OpResult.Fail(UI.LedgerText.ReasonNoSuchMember);

        static OpResult Commit(House house, OpResult result)
        {
            if (result.Ok)
                Settle(house);
            return result;
        }

        /// <summary>What every successful mutation owes the house: men and guns can
        /// have crossed crew lines, so the lieutenants re-deal, and the page is told
        /// there is something to repaint.</summary>
        static void Settle(House house)
        {
            if (house?.Roster == null)
                return;
            RosterOps.NormalizeArms(house.Roster);
            house.Touch();
        }
    }
}
