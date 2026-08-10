using LivingCity.UI;

namespace LivingCity.Personnel
{
    public readonly struct OpResult
    {
        public readonly bool Ok;
        public readonly string Reason;

        OpResult(bool ok, string reason)
        {
            Ok = ok;
            Reason = reason;
        }

        public static readonly OpResult Success = new OpResult(true, "");
        public static OpResult Fail(string reason) => new OpResult(false, reason);
    }

    public readonly struct PromoteCheck
    {
        public readonly bool CanPromote;

        /// <summary>He qualifies on paper but not in the head - warn, then let the player
        /// make his own mistake.</summary>
        public readonly bool LowStatWarning;

        public readonly string Reason;

        public PromoteCheck(bool canPromote, bool lowStatWarning, string reason)
        {
            CanPromote = canPromote;
            LowStatWarning = lowStatWarning;
            Reason = reason;
        }
    }

    /// <summary>
    /// Every roster mutation, as pure statics returning results instead of throwing or
    /// logging. This is the seam the weekly order system will issue through later - which
    /// is why the rules live here and not in the almanac's click handlers: the UI merely
    /// repeats what these methods enforce, and the headless suite exercises them directly.
    ///
    /// Standing rules: the dead are off the books for everything; specialists take no
    /// assignment of any kind; a lieutenant is never click-assigned - demoting him is the
    /// one path out of his crew, so a crew cannot silently lose its head.
    /// </summary>
    public static class RosterOps
    {
        /// <summary>3.0 stars. Below this in Intelligence OR Organization, promotion is
        /// warned against but allowed - lieutenancy lives on those two stats.</summary>
        public const int LowStatHalfSteps = 6;

        public static PromoteCheck CheckPromote(Roster roster, int id)
        {
            var member = roster.Find(id);
            if (member == null)
                return new PromoteCheck(false, false, LedgerText.ReasonNoSuchMember);
            if (member.Specialty != Specialty.None)
                return new PromoteCheck(false, false, LedgerText.ReasonSpecialist);
            if (member.Status == CharacterStatus.Dead)
                return new PromoteCheck(false, false, LedgerText.ReasonDead);
            if (member.Rank == Rank.Lieutenant)
                return new PromoteCheck(false, false, LedgerText.ReasonAlreadyLieutenant);

            var low = member.GetHalfSteps(CharacterAttribute.Intelligence) < LowStatHalfSteps ||
                      member.GetHalfSteps(CharacterAttribute.Organization) < LowStatHalfSteps;
            return new PromoteCheck(true, low, "");
        }

        /// <summary>Promotes a hood: he leaves crew, pool or front, and a new empty crew
        /// forms under him. The warning is advisory - callers show it, this method does not
        /// re-refuse over it.</summary>
        public static OpResult Promote(Roster roster, int id, out int newCrewId)
        {
            newCrewId = -1;
            var check = CheckPromote(roster, id);
            if (!check.CanPromote)
                return OpResult.Fail(check.Reason);

            Detach(roster, id);
            var member = roster.Find(id);
            member.Rank = Rank.Lieutenant;

            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = id };
            roster.Crews.Add(crew);
            newCrewId = crew.Id;
            return OpResult.Success;
        }

        /// <summary>Disbands the lieutenant's crew: everyone, him included, reverts to the
        /// pool (derived - removing the crew IS the reversion). Equipment stays with its
        /// holders; a demotion is a personnel event, not a shakedown.</summary>
        public static OpResult Demote(Roster roster, int lieutenantId)
        {
            var member = roster.Find(lieutenantId);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Rank != Rank.Lieutenant)
                return OpResult.Fail(LedgerText.ReasonNotLieutenant);

            var crew = roster.CrewOf(lieutenantId);
            if (crew != null)
                roster.Crews.Remove(crew);

            member.Rank = Rank.Hood;
            return OpResult.Success;
        }

        public static OpResult AssignToCrew(Roster roster, int id, int crewId)
        {
            var refusal = CheckAssignable(roster, id);
            if (refusal != null)
                return OpResult.Fail(refusal);

            var crew = roster.FindCrew(crewId);
            if (crew == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchCrew);
            if (crew.HoodIds.Contains(id))
                return OpResult.Fail(LedgerText.ReasonAlreadyInCrew);

            Detach(roster, id);
            crew.HoodIds.Add(id);
            return OpResult.Success;
        }

        public static OpResult AssignToPool(Roster roster, int id)
        {
            var refusal = CheckAssignable(roster, id);
            if (refusal != null)
                return OpResult.Fail(refusal);

            Detach(roster, id);
            return OpResult.Success;
        }

        /// <summary>The previous manager, if any, simply stops being the front - which by
        /// derivation lands him in the pool.</summary>
        public static OpResult AssignToFront(Roster roster, int id)
        {
            var refusal = CheckAssignable(roster, id);
            if (refusal != null)
                return OpResult.Fail(refusal);
            if (roster.FrontId == id)
                return OpResult.Fail(LedgerText.ReasonAlreadyFront);

            Detach(roster, id);
            roster.FrontId = id;
            return OpResult.Success;
        }

        public static OpResult GiveEquipment(Roster roster, int itemId, int id)
        {
            var item = FindItem(roster, itemId);
            if (item == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchItem);

            var member = roster.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Status == CharacterStatus.Dead)
                return OpResult.Fail(LedgerText.ReasonDead);

            if (item.HolderId == id)
                return OpResult.Fail(LedgerText.ReasonAlreadyHolds);
            if (item.HolderId != RosterEquipment.Unheld)
            {
                var holder = roster.Find(item.HolderId);
                return OpResult.Fail(LedgerText.HeldByLine(
                    holder != null ? holder.FullName : "another man"));
            }

            item.HolderId = id;
            return OpResult.Success;
        }

        public static OpResult ReturnEquipment(Roster roster, int itemId)
        {
            var item = FindItem(roster, itemId);
            if (item == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchItem);
            if (item.HolderId == RosterEquipment.Unheld)
                return OpResult.Fail(LedgerText.ReasonNotHeld);

            item.HolderId = RosterEquipment.Unheld;
            return OpResult.Success;
        }

        /// <summary>The shared gate for every assignment move; null means assignable.</summary>
        static string CheckAssignable(Roster roster, int id)
        {
            var member = roster.Find(id);
            if (member == null)
                return LedgerText.ReasonNoSuchMember;
            if (member.Specialty != Specialty.None)
                return LedgerText.ReasonSpecialist;
            if (member.Status == CharacterStatus.Dead)
                return LedgerText.ReasonDead;
            if (member.Rank == Rank.Lieutenant)
                return LedgerText.ReasonLieutenantMoves;
            return null;
        }

        /// <summary>Removes the member from wherever he stands - crew or front. After this
        /// he is, by derivation, in the pool.</summary>
        static void Detach(Roster roster, int id)
        {
            if (roster.FrontId == id)
                roster.FrontId = -1;

            var crew = roster.CrewOf(id);
            crew?.HoodIds.Remove(id);
        }

        static RosterEquipment FindItem(Roster roster, int itemId)
        {
            for (var i = 0; i < roster.Equipment.Count; i++)
                if (roster.Equipment[i].Id == itemId)
                    return roster.Equipment[i];
            return null;
        }
    }
}
