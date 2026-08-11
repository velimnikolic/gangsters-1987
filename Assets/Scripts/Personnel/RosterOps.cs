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

        /// <summary>Guns are dealt by Firearms, vehicles by Driving - the one split
        /// NormalizeArms cares about. The chain-of-command rule itself covers BOTH:
        /// everything in the drawer issues to a lieutenant.</summary>
        public static bool IsWeapon(EquipmentKind kind) => kind != EquipmentKind.Vehicle;

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

            // The boss hands gear to his lieutenants, nobody else - each lieutenant
            // deals his crew in himself (NormalizeArms): guns by who can shoot,
            // wheels by who can drive.
            if (member.Rank != Rank.Lieutenant)
                return OpResult.Fail(LedgerText.ReasonGearViaLieutenant);

            if (item.OwnerId == id)
                return OpResult.Fail(LedgerText.ReasonAlreadyHolds);
            if (item.OwnerId != RosterEquipment.Unheld)
            {
                var holder = roster.Find(item.HolderId);
                return OpResult.Fail(LedgerText.HeldByLine(
                    holder != null ? holder.FullName
                    : item.OwnerId == RosterEquipment.FrontArmory
                        ? "the front" : "another man"));
            }

            item.OwnerId = id;
            item.HolderId = id;
            return OpResult.Success;
        }

        /// <summary>The boss dumps gear at headquarters: the FRONT becomes its owner,
        /// and NormalizeArms deals the locker out to the men guarding the desk - the
        /// front manager and the pooled hoods.</summary>
        public static OpResult GiveEquipmentToFront(Roster roster, int itemId)
        {
            var item = FindItem(roster, itemId);
            if (item == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchItem);
            if (item.OwnerId == RosterEquipment.FrontArmory)
                return OpResult.Fail(LedgerText.ReasonAlreadyHolds);
            if (item.OwnerId != RosterEquipment.Unheld)
            {
                var holder = roster.Find(item.HolderId);
                return OpResult.Fail(LedgerText.HeldByLine(
                    holder != null ? holder.FullName : "another man"));
            }

            item.OwnerId = RosterEquipment.FrontArmory;
            item.HolderId = RosterEquipment.FrontArmory;
            return OpResult.Success;
        }

        /// <summary>New stock enters the shared pool unheld - the purchase path's
        /// second half; the money half lives with the outfit's accounts.</summary>
        public static RosterEquipment AddEquipment(Roster roster, EquipmentKind kind,
            string displayName, int value)
        {
            var item = new RosterEquipment
            {
                Id = roster.NextEquipmentId(),
                Kind = kind,
                DisplayName = displayName,
                Value = value,
            };
            roster.Equipment.Add(item);
            return item;
        }

        public static OpResult ReturnEquipment(Roster roster, int itemId)
        {
            var item = FindItem(roster, itemId);
            if (item == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchItem);
            if (item.OwnerId == RosterEquipment.Unheld)
                return OpResult.Fail(LedgerText.ReasonNotHeld);

            item.OwnerId = RosterEquipment.Unheld;
            item.HolderId = RosterEquipment.Unheld;
            return OpResult.Success;
        }

        /// <summary>
        /// The quartermaster discipline, re-derived from the current roster: gear
        /// BELONGS to a parent group (a lieutenant's crew, or the front) and never
        /// leaves it on a man's back - OwnerId is the deed, HolderId just says who
        /// carries it today. Each deal re-runs over the group's current hands: guns
        /// by Firearms, wheels by Driving, the best of each to the best hand when
        /// the dealer is organized, progressively more backwards when he is not.
        /// A man who leaves the group is simply no longer a hand - the next deal
        /// passes his old piece to whoever remains. Deterministic and idempotent:
        /// no draws, so re-running never reshuffles a settled hand.
        /// PersonnelDirector runs this after every mutation; headless tests call it
        /// directly, the same split as every op here.
        /// </summary>
        public static void NormalizeArms(Roster roster)
        {
            // Ownership first: gear whose parent group no longer exists (the owner
            // demoted, dead, off the books) reverts to the safe. The FRONT is always
            // a valid parent; a lieutenant is one while he still runs a crew.
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId == RosterEquipment.Unheld)
                {
                    item.HolderId = RosterEquipment.Unheld;
                    continue;
                }
                if (item.OwnerId == RosterEquipment.FrontArmory)
                    continue;

                var owner = roster.Find(item.OwnerId);
                if (owner == null || owner.Rank != Rank.Lieutenant ||
                    roster.CrewOf(owner.Id) == null)
                {
                    item.OwnerId = RosterEquipment.Unheld;
                    item.HolderId = RosterEquipment.Unheld;
                }
            }

            for (var i = 0; i < roster.Crews.Count; i++)
                DealCrewArms(roster, roster.Crews[i]);

            DealFrontArms(roster);
        }

        /// <summary>The men who guard headquarters: the front manager and every pooled
        /// hood - the pool IS the muscle kept at the desk between assignments. Public
        /// because the front card lists exactly this group's hands.</summary>
        public static bool InFrontGuard(Roster roster, int id)
        {
            if (id == roster.FrontId)
                return true;
            return roster.AssignmentOf(id).Kind == AssignmentKind.Pool;
        }

        /// <summary>The front's deal: everything the FRONT owns, re-dealt over the
        /// desk's hands. The BOSS deals this one himself - gear lands ideally - and
        /// the surplus stays in the locker, not on a man.</summary>
        static void DealFrontArms(Roster roster)
        {
            var guns = new System.Collections.Generic.List<RosterEquipment>();
            var wheels = new System.Collections.Generic.List<RosterEquipment>();
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId == RosterEquipment.FrontArmory)
                    (IsWeapon(item.Kind) ? guns : wheels).Add(item);
            }
            if (guns.Count == 0 && wheels.Count == 0)
                return;

            var hands = new System.Collections.Generic.List<Character>();
            var manager = roster.Find(roster.FrontId);
            if (manager != null && manager.Status == CharacterStatus.Active)
                hands.Add(manager);
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Status == CharacterStatus.Active &&
                    member.Id != roster.FrontId &&
                    roster.AssignmentOf(member.Id).Kind == AssignmentKind.Pool)
                    hands.Add(member);
            }

            Deal(guns, hands, CharacterAttribute.Firearms,
                AttributeScale.MaxHalfSteps, RosterEquipment.FrontArmory);
            Deal(wheels, hands, CharacterAttribute.Driving,
                AttributeScale.MaxHalfSteps, RosterEquipment.FrontArmory);
        }

        /// <summary>One crew's deal, in two decks - guns and wheels - over the same
        /// hands. The lieutenant is a pair of hands like his men (he carries and he
        /// drives too) and the warehouse for whatever is left over.</summary>
        static void DealCrewArms(Roster roster, Crew crew)
        {
            var lieutenant = roster.Find(crew.LieutenantId);
            if (lieutenant == null)
                return;

            // The crew's deck is what the LIEUTENANT owns - the user's rule: gear
            // stays in the parent, whoever carried it yesterday.
            var guns = new System.Collections.Generic.List<RosterEquipment>();
            var wheels = new System.Collections.Generic.List<RosterEquipment>();
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId == crew.LieutenantId)
                    (IsWeapon(item.Kind) ? guns : wheels).Add(item);
            }
            if (guns.Count == 0 && wheels.Count == 0)
                return;

            // Who stands to be dealt in: everyone in the crew still on his feet.
            var hands = new System.Collections.Generic.List<Character>();
            if (lieutenant.Status == CharacterStatus.Active)
                hands.Add(lieutenant);
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var hood = roster.Find(crew.HoodIds[i]);
                if (hood != null && hood.Status == CharacterStatus.Active)
                    hands.Add(hood);
            }

            var organization = lieutenant.GetHalfSteps(CharacterAttribute.Organization);
            Deal(guns, hands, CharacterAttribute.Firearms, organization, lieutenant.Id);
            Deal(wheels, hands, CharacterAttribute.Driving, organization, lieutenant.Id);
        }

        /// <summary>One deck over one group's hands, ranked by the stat that deck
        /// runs on. The organization half-steps decide how much of the ideal deal
        /// survives: at five stars everything lands right; at none the whole hand
        /// is dealt backwards - the tommy to the wild miss, the sedan to the man
        /// who cannot park it. Ids break ties so the deal is stable across
        /// repaints. Surplus lands on warehouseId - the lieutenant for a crew, the
        /// front locker for the desk.</summary>
        static void Deal(System.Collections.Generic.List<RosterEquipment> items,
            System.Collections.Generic.List<Character> hands,
            CharacterAttribute stat, int organization, int warehouseId)
        {
            if (items.Count == 0)
                return;

            items.Sort((x, y) => y.Value != x.Value
                ? y.Value.CompareTo(x.Value) : x.Id.CompareTo(y.Id));

            var ranked = new System.Collections.Generic.List<Character>(hands);
            ranked.Sort((x, y) =>
            {
                var sx = x.GetHalfSteps(stat);
                var sy = y.GetHalfSteps(stat);
                return sy != sx ? sy.CompareTo(sx) : x.Id.CompareTo(y.Id);
            });

            var pairs = System.Math.Min(items.Count, ranked.Count);
            var correct = pairs * organization / AttributeScale.MaxHalfSteps;

            for (var i = 0; i < pairs; i++)
            {
                var hand = i < correct ? ranked[i] : ranked[correct + (pairs - 1 - i)];
                items[i].HolderId = hand.Id;
            }

            // One piece per pair of hands; the warehouse takes the surplus.
            for (var i = pairs; i < items.Count; i++)
                items[i].HolderId = warehouseId;
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
