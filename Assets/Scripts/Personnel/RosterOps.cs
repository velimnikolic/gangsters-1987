using LivingCity.UI;
using LivingCity.Territory;

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
        /// <summary>3.0 stars. Below this in Awareness OR Organization, promotion is
        /// warned against but allowed - lieutenancy lives on those two stats.</summary>
        public const int LowStatHalfSteps = 6;

        public static void ConfigureOrganization(Roster roster, OrganizationLimits limits)
        {
            if (roster != null)
                roster.Organization.Limits = limits;
        }

        /// <summary>
        /// Yes. His envelope is brought up to what he asked, and the asking stops -
        /// the bargain moves, which is the one thing that closes a pay gap for good.
        /// A man who was skimming over it stops that too: he was taking what he thought
        /// he was owed.
        /// </summary>
        public static OpResult GrantRaise(Roster roster, int id)
        {
            var member = roster?.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.WageDemand <= 0)
                return OpResult.Fail(LedgerText.ReasonNoDemand);

            member.WageAsked = member.WageDemand;
            member.WageDemand = 0;
            member.UnderpaidSince = 0;
            member.Skimming = false;
            return OpResult.Success;
        }

        /// <summary>
        /// No. He goes on drawing what he drew, and he remembers being told. The clock
        /// is NOT reset - he is still underpaid, and the ladder goes on from where it
        /// was.
        /// </summary>
        public static OpResult RefuseRaise(Roster roster, int id,
            System.Collections.Generic.List<PersonalityChange> into = null)
        {
            var member = roster?.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.WageDemand <= 0)
                return OpResult.Fail(LedgerText.ReasonNoDemand);

            member.WageDemand = 0;
            NudgePersonality(member, PersonalityTrait.Loyalty,
                -GreedLadder.RefusalLoyaltyHit, "asked for the rate and was refused",
                into);
            return OpResult.Success;
        }

        /// <summary>
        /// Moves one of a man's traits, and says why. This is the ONLY door: nothing
        /// else in the codebase writes a personality field, and the reason string is
        /// what makes that rule enforceable rather than merely stated - a caller who
        /// cannot say why a man got greedier has no business making him greedier.
        ///
        /// A nudge that moves nothing - a zero delta, or a man already at the end of
        /// the scale - records nothing. The feed prints what happened, not what was
        /// attempted.
        /// </summary>
        /// <returns>The movement. Its <c>Delta</c> is 0 when nothing moved.</returns>
        public static PersonalityChange NudgePersonality(Character man,
            PersonalityTrait trait, int delta, string reason,
            System.Collections.Generic.List<PersonalityChange> into = null)
        {
            if (man == null)
                return default;

            var from = Personality.Get(man, trait);
            Personality.Set(man, trait, from + delta);
            var to = Personality.Get(man, trait);

            var change = new PersonalityChange(man.Id, man.FullName, trait, from, to,
                reason ?? "");
            if (to != from)
                into?.Add(change);
            return change;
        }

        public static PromoteCheck CheckPromote(Roster roster, int id)
        {
            var member = roster.Find(id);
            if (member == null)
                return new PromoteCheck(false, false, LedgerText.ReasonNoSuchMember);
            if (member.Specialty != Specialty.None)
                return new PromoteCheck(false, false, LedgerText.ReasonSpecialist);
            if (member.Gone)
                return new PromoteCheck(false, false, GoneReason(member));
            if (member.Rank == Rank.Lieutenant)
                return new PromoteCheck(false, false, LedgerText.ReasonAlreadyLieutenant);
            if (member.Rank == Rank.Boss)
                return new PromoteCheck(false, false, LedgerText.ReasonBossMoves);

            // The outfit can only hold as many branches as its Boss can keep an eye
            // on. Past that the answer is not "promote him anyway" - it is that the
            // Boss himself has to get better at command first.
            var boss = roster.FindBoss();
            if (boss != null)
            {
                var held = Command.LieutenantsHeld(roster);
                if (held >= Command.LieutenantCap(boss))
                    return new PromoteCheck(false, false,
                        LedgerText.SpanFull(boss.FullName, held));
            }

            var low = member.GetHalfSteps(CharacterAttribute.Awareness) < LowStatHalfSteps ||
                      member.GetHalfSteps(CharacterAttribute.Organization) < LowStatHalfSteps;
            return new PromoteCheck(true, low, "");
        }

        /// <summary>
        /// Promotes a hood: he leaves crew, pool or front, and a new empty crew forms
        /// under him. The warning is advisory - callers show it, this method does not
        /// re-refuse over it.
        ///
        /// A promotion changes his RANK and what is expected of him, and NOTHING ELSE.
        /// Not one skill moves here, and the headless suite asserts it: the whole point
        /// of choosing a man on his history is that the choice is real, and a promotion
        /// that quietly made him better at leading men would make every choice the same
        /// choice. His wage changes because the house scale is read off his rank
        /// (Wages.WageFor) - not because anything was written on him.
        ///
        /// What it does move is the men around him. His old crewmates watch one of
        /// their own rise, and an ambitious man passed over feels it exactly as much as
        /// a contented one is pleased by it.
        /// </summary>
        public static OpResult Promote(Roster roster, int id, out int newCrewId,
            System.Collections.Generic.List<Incident> incidents = null,
            System.Collections.Generic.List<PersonalityChange> changes = null)
        {
            newCrewId = -1;
            var check = CheckPromote(roster, id);
            if (!check.CanPromote)
                return OpResult.Fail(check.Reason);

            // The men he stood beside, taken BEFORE he is detached from them - after
            // Detach there is nothing left to say who they were.
            var oldCrew = roster.CrewOf(id);
            var witnesses = oldCrew != null
                ? new System.Collections.Generic.List<int>(oldCrew.HoodIds)
                : null;

            Detach(roster, id);
            var member = roster.Find(id);
            member.Rank = Rank.Lieutenant;
            member.RankSince = roster.Day;
            // He answers to the Boss now, and a new relationship starts near zero
            // history - what he felt about his old lieutenant does not come with him.
            Loyalty.Reaim(member, "made a lieutenant, and answers to the Boss now", changes);

            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = id };
            roster.Crews.Add(crew);
            newCrewId = crew.Id;

            Career.RankChanged(member, roster.Day, Rank.Lieutenant, "given a crew");
            incidents?.Add(new Incident(member.Id, member.FullName, IncidentKind.Promoted,
                roster.Day, "", 0,
                IncidentText.Line(IncidentKind.Promoted, member.FullName, "")));

            if (witnesses != null)
                Ripple(roster, witnesses, id, member.FullName, changes);
            return OpResult.Success;
        }

        /// <summary>What a promotion costs a hood who is not being promoted. Small on
        /// purpose - it is a mood, not an event - but real, and it is what makes a
        /// crew full of hungry men a crew the player has to keep feeding.</summary>
        public const int PassedOverLoss = 3;

        /// <summary>And what it is worth to a man with no designs of his own: one of
        /// ours went up, so there is somewhere to go.</summary>
        public const int OneOfOursGain = 1;

        /// <summary>The old crew watching one of their own rise.</summary>
        static void Ripple(Roster roster,
            System.Collections.Generic.List<int> crewmates, int promotedId, string name,
            System.Collections.Generic.List<PersonalityChange> changes)
        {
            for (var i = 0; i < crewmates.Count; i++)
            {
                if (crewmates[i] == promotedId)
                    continue;
                var mate = roster.Find(crewmates[i]);
                if (mate == null || mate.Gone)
                    continue;

                if (Personality.Get(mate, PersonalityTrait.Ambition) >= Loyalty.AmbitionFloor)
                    NudgePersonality(mate, PersonalityTrait.Loyalty, -PassedOverLoss,
                        name + " was made, and he was not", changes);
                else
                    NudgePersonality(mate, PersonalityTrait.Loyalty, OneOfOursGain,
                        "one of theirs was made", changes);
            }
        }

        /// <summary>
        /// Disbands the lieutenant's crew: everyone, him included, reverts to the pool
        /// (derived - removing the crew IS the reversion). Equipment stays with its
        /// holders; a demotion is a personnel event, not a shakedown.
        ///
        /// Kept in the design, and kept BRUTAL. The player may take a man's crew off
        /// him, and the man will not forgive it: his loyalty resets to a new
        /// relationship like any transfer and is then cut again, harder the more
        /// ambitious he is (<see cref="Loyalty.TakenDownSting"/>). An ambitious man
        /// demoted is very often a red flag by the end of the same afternoon, and from
        /// there the defection arithmetic is already looking at him - which is the
        /// intended shape: firing a lieutenant should be a decision with a man in it,
        /// not a menu item.
        /// </summary>
        public static OpResult Demote(Roster roster, int lieutenantId,
            System.Collections.Generic.List<Incident> incidents = null,
            System.Collections.Generic.List<PersonalityChange> changes = null)
        {
            var member = roster.Find(lieutenantId);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Rank != Rank.Lieutenant)
                return OpResult.Fail(LedgerText.ReasonNotLieutenant);

            var crew = roster.CrewOf(lieutenantId);
            var formerHoods = crew != null
                ? new System.Collections.Generic.List<int>(crew.HoodIds)
                : null;
            if (crew != null)
                roster.Crews.Remove(crew);

            member.Rank = Rank.Hood;
            member.RankSince = roster.Day;
            Loyalty.Reaim(member, "taken back down to a hood", changes);
            Loyalty.Sting(member, changes);
            PutUnderBossIfPresent(roster, member.Id);
            if (formerHoods != null)
                for (var i = 0; i < formerHoods.Count; i++)
                    PutUnderBossIfPresent(roster, formerHoods[i]);

            Career.RankChanged(member, roster.Day, Rank.Hood, "his crew broken up");
            incidents?.Add(new Incident(member.Id, member.FullName, IncidentKind.Demoted,
                roster.Day, "", 0,
                IncidentText.Line(IncidentKind.Demoted, member.FullName, "")));
            return OpResult.Success;
        }

        public static OpResult AssignToCrew(Roster roster, int id, int crewId,
            System.Collections.Generic.List<PersonalityChange> changes = null)
        {
            var refusal = CheckAssignable(roster, id);
            if (refusal != null)
                return OpResult.Fail(refusal);

            var crew = roster.FindCrew(crewId);
            if (crew == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchCrew);
            if (crew.HoodIds.Contains(id))
                return OpResult.Fail(LedgerText.ReasonAlreadyInCrew);

            // What his Leadership can actually hold. The player is refused here; the
            // WORLD is not - a recruit who comes back to an overloaded lieutenant is
            // still a man on the books, and the overload is then something the ledger
            // shows rather than something the sim quietly fixes by losing him.
            var lieutenant = roster.Find(crew.LieutenantId);
            var men = CountLiveHoods(roster, crew);
            if (lieutenant != null &&
                men >= Command.ManCap(lieutenant, roster.Organization.Limits))
                return OpResult.Fail(
                    LedgerText.CrewFull(lieutenant.FullName, men));

            Detach(roster, id);
            crew.HoodIds.Add(id);
            // A new superior is a new relationship: loyalty starts near neutral again.
            var moved = roster.Find(id);
            Loyalty.Reaim(moved, "put under a new lieutenant", changes);
            Career.Posted(moved, roster.Day,
                lieutenant != null ? lieutenant.FullName : "");
            return OpResult.Success;
        }

        /// <summary>Men in the crew who are still on the books.</summary>
        static int CountLiveHoods(Roster roster, Crew crew)
        {
            var men = 0;
            for (var i = 0; i < crew.HoodIds.Count; i++)
            {
                var hood = roster.Find(crew.HoodIds[i]);
                if (hood != null && !hood.Gone)
                    men++;
            }
            return men;
        }

        /// <summary>Moves one real Hood directly under the one authoritative Boss.</summary>
        public static OpResult AssignToBoss(Roster roster, int id, int bossId)
        {
            var refusal = CheckAssignable(roster, id);
            if (refusal != null)
                return OpResult.Fail(refusal);

            var boss = roster.Find(bossId);
            if (boss == null || boss.Id != roster.BossId || boss.Rank != Rank.Boss)
                return OpResult.Fail(LedgerText.ReasonNoBoss);
            if (roster.Organization.BossHoodIds.Contains(id))
                return OpResult.Fail(LedgerText.ReasonAlreadyUnderBoss);

            Detach(roster, id);
            roster.Organization.BossHoodIds.Add(id);
            Career.Posted(roster.Find(id), roster.Day, boss.FullName);
            return OpResult.Success;
        }

        public static OpResult AssignToPool(Roster roster, int id)
        {
            var refusal = CheckAssignable(roster, id);
            if (refusal != null)
                return OpResult.Fail(refusal);

            Detach(roster, id);
            PutUnderBossIfPresent(roster, id);
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
            PutUnderBossIfPresent(roster, id);
            return OpResult.Success;
        }

        public static OpResult AssignBlockResponsibility(
            Roster roster, TerritoryBlockId blockId, int leaderId, bool blockExists)
        {
            if (roster == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (!blockId.IsValid || !blockExists)
                return OpResult.Fail(LedgerText.ReasonUnknownBlock);

            var leader = roster.Find(leaderId);
            if (leader == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (leader.Gone)
                return OpResult.Fail(GoneReason(leader));
            if (leader.Rank != Rank.Boss && leader.Rank != Rank.Lieutenant)
                return OpResult.Fail(LedgerText.ReasonInvalidCommandParent);
            if (leader.Rank == Rank.Boss && leader.Id != roster.BossId)
                return OpResult.Fail(LedgerText.ReasonNoBoss);
            if (leader.Rank == Rank.Lieutenant)
            {
                var crew = roster.CrewOf(leader.Id);
                if (crew == null || crew.LieutenantId != leader.Id)
                    return OpResult.Fail(LedgerText.ReasonInvalidCommandParent);
            }

            // Ground he cannot carry is ground the outfit does not really hold. This
            // binds at assignment: growth has to force the player to promote somebody,
            // and a cap he can walk past is not a cap.
            var held = 0;
            for (var i = 0; i < roster.Organization.BlockResponsibilities.Count; i++)
                if (roster.Organization.BlockResponsibilities[i].LeaderId == leaderId)
                    held++;
            if (held >= Command.BlockCap(leader, roster.Organization.Limits))
                return OpResult.Fail(LedgerText.BlocksFull(leader.FullName, held));

            var assignments = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < assignments.Count; i++)
            {
                if (assignments[i].BlockId != blockId)
                    continue;
                if (assignments[i].LeaderId == leaderId)
                    return OpResult.Fail("That leader is already responsible for this block.");
                assignments[i] = new OrganizationBlockResponsibility(blockId, leaderId);
                return OpResult.Success;
            }

            assignments.Add(new OrganizationBlockResponsibility(blockId, leaderId));
            return OpResult.Success;
        }

        public static OpResult RemoveBlockResponsibility(
            Roster roster, TerritoryBlockId blockId, int expectedLeaderId = -1)
        {
            if (roster == null || !blockId.IsValid)
                return OpResult.Fail(LedgerText.ReasonUnknownBlock);

            var assignments = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < assignments.Count; i++)
            {
                if (assignments[i].BlockId != blockId)
                    continue;
                if (expectedLeaderId >= 0 && assignments[i].LeaderId != expectedLeaderId)
                    return OpResult.Fail("That block belongs to another command file.");
                assignments.RemoveAt(i);
                return OpResult.Success;
            }
            return OpResult.Fail("The block has no organization responsibility.");
        }

        /// <summary>Guns are dealt by Combat, vehicles by Driving - the one split
        /// NormalizeArms cares about. The chain-of-command rule itself covers BOTH:
        /// everything in the drawer issues to a lieutenant.</summary>
        public static bool IsWeapon(EquipmentKind kind) =>
            kind != EquipmentKind.Vehicle && kind != EquipmentKind.Motorcycle &&
            kind != EquipmentKind.Grenade;

        /// <summary>A grenade: gear a crew owns but the quartermaster deals into no
        /// man's hand - a COUNTABLE stock (DemoCrews.BindBombs), spent when thrown. It
        /// is neither a gun (a hand slot) nor a wheel, so it stays out of both decks.</summary>
        public static bool IsGrenade(EquipmentKind kind) => kind == EquipmentKind.Grenade;

        /// <summary>How many grenades the given owner (a lieutenant, or the front) holds.</summary>
        public static int GrenadesOwnedBy(Roster roster, int ownerId)
        {
            var n = 0;
            for (var i = 0; i < roster.Equipment.Count; i++)
                if (roster.Equipment[i].Kind == EquipmentKind.Grenade &&
                    roster.Equipment[i].OwnerId == ownerId) n++;
            return n;
        }

        /// <summary>Spend one of the owner's grenades - struck off the books the moment
        /// it is thrown. True if there was one to spend.</summary>
        public static bool SpendGrenade(Roster roster, int ownerId)
        {
            for (var i = roster.Equipment.Count - 1; i >= 0; i--)
                if (roster.Equipment[i].Kind == EquipmentKind.Grenade &&
                    roster.Equipment[i].OwnerId == ownerId)
                {
                    roster.Equipment.RemoveAt(i);
                    return true;
                }
            return false;
        }

        /// <summary>Struck off the books - the thing itself is gone, not sold. One
        /// caller so far: a motorcycle whose tank went up under a drive-by (DemoCrews).
        /// Nothing is refunded and nobody is told; the line simply stops being there,
        /// which is what the street has just made true. False if it was not on them.</summary>
        public static bool LoseItem(Roster roster, int itemId)
        {
            if (roster == null) return false;
            for (var i = roster.Equipment.Count - 1; i >= 0; i--)
                if (roster.Equipment[i].Id == itemId)
                {
                    roster.Equipment.RemoveAt(i);
                    return true;
                }
            return false;
        }

        /// <summary>
        /// A man gear can be signed out to: one who RUNS A BRANCH. Every lieutenant with
        /// a crew, and the Boss, whose detail is a crew of his own (<see cref="Bodyguards"/>).
        ///
        /// The Boss belongs here because a campaign opens with him alone on the books:
        /// his detail is then the only branch there is, and a rule reading "lieutenants
        /// only" left the outfit's one car in the safe with nobody in the world allowed
        /// to take the keys. He deals his own detail in like any other branch parent
        /// (<see cref="NormalizeArms"/>).
        /// </summary>
        public static bool RunsABranch(Roster roster, int id)
        {
            if (roster == null)
                return false;
            // Nothing about being GONE is asked here: a lieutenant in a hospital bed
            // still runs his crew and still owns its guns. Whether a man is fit to be
            // handed something is the giving rule's question, and it asks separately.
            var member = roster.Find(id);
            if (member == null ||
                (member.Rank != Rank.Lieutenant && member.Rank != Rank.Boss))
                return false;
            for (var i = 0; i < roster.Crews.Count; i++)
                if (roster.Crews[i].LieutenantId == id)
                    return true;
            return false;
        }

        /// <summary>Who the safe will sign a thing out to: any lieutenant - a man just
        /// promoted has an empty crew and still draws its guns - and the Boss while his
        /// detail is standing. Wider than <see cref="RunsABranch"/> on purpose: what the
        /// deal keeps (a branch that still exists) and what the boss may hand over are
        /// two questions.</summary>
        public static bool CanBeIssuedGear(Roster roster, int id)
        {
            var member = roster?.Find(id);
            return member != null &&
                   (member.Rank == Rank.Lieutenant || RunsABranch(roster, id));
        }

        public static OpResult GiveEquipment(Roster roster, int itemId, int id)
        {
            var item = FindItem(roster, itemId);
            if (item == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchItem);

            var member = roster.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Gone)
                return OpResult.Fail(GoneReason(member));

            // Gear goes to a man who runs a branch and nobody else - he deals his own
            // crew in himself (NormalizeArms): guns by who can shoot, wheels by who can
            // drive. The Don's detail is such a branch, and on day one it is the only
            // one (RunsABranch).
            if (!CanBeIssuedGear(roster, id))
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

        /// <summary>The keys handed straight from whoever has them to another
        /// lieutenant - the STREET's order: a lieutenant is picked, one of the outfit's
        /// cars is clicked, and it is his.
        ///
        /// GiveEquipment refuses an item somebody already holds, and rightly: on the
        /// armory page the boss is reading a list, and taking a gun off a named man
        /// without saying so would be a book that lies. On the street the man doing the
        /// taking is being pointed at, so the two halves are one act - and this does
        /// both. The via-lieutenant rule still stands: the receiver must run a crew.</summary>
        public static OpResult MoveEquipment(Roster roster, int itemId, int id)
        {
            var item = FindItem(roster, itemId);
            if (item == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchItem);

            var member = roster.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Gone)
                return OpResult.Fail(GoneReason(member));
            if (!CanBeIssuedGear(roster, id))
                return OpResult.Fail(LedgerText.ReasonGearViaLieutenant);
            if (item.OwnerId == id)
                return OpResult.Fail(LedgerText.ReasonAlreadyHolds);

            // his crew's deed. Who in it actually drives the thing is the deal's call
            // (NormalizeArms, wheels by Driving) and runs immediately after.
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
        /// by Combat, wheels by Driving, the best of each to the best hand when
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
            // a valid parent; anyone else is one while he still runs a branch - a
            // lieutenant with his crew, or the Don with his detail (RunsABranch).
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

                if (!RunsABranch(roster, item.OwnerId))
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
                if (item.OwnerId == RosterEquipment.FrontArmory && !IsGrenade(item.Kind))
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

            Deal(guns, hands, CharacterAttribute.Combat,
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
                if (item.OwnerId == crew.LieutenantId && !IsGrenade(item.Kind))
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
            Deal(guns, hands, CharacterAttribute.Combat, organization, lieutenant.Id);
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
            if (member.Gone)
                return GoneReason(member);
            if (member.Rank == Rank.Lieutenant)
                return LedgerText.ReasonLieutenantMoves;
            if (member.Rank == Rank.Boss)
                return LedgerText.ReasonBossMoves;
            return null;
        }

        /// <summary>
        /// A man shot dead on the street. He stays on the books, struck through (the
        /// record keeps his line), his gear goes back to the pool unheld, and his post
        /// is his no longer: a hood leaves his crew; a lieutenant's crew passes to his
        /// most loyal living hood - the outfit does not lose a crew to one bullet - or,
        /// with nobody left to take it, folds.
        /// </summary>
        public static OpResult Kill(Roster roster, int id,
            System.Collections.Generic.List<PersonalityChange> changes = null) =>
            StrikeOff(roster, id, CharacterStatus.Dead, "", 0, changes);

        /// <summary>
        /// A man who ran from a fight and kept running. Struck off the same way as the
        /// dead - his line kept, his gear pooled, his post passed on - but marked as
        /// what he is: a deserter, not a casualty.
        ///
        /// <paramref name="story"/> is the one line his own file will carry instead of
        /// the clerk's stock sentence about a runner. A defection comes out through
        /// this door too - one door, so gear, wages and posts settle identically - and
        /// a man who walked out behind his lieutenant did not run from anything.
        /// </summary>
        public static OpResult Desert(Roster roster, int id, string story = "",
            int weight = 0,
            System.Collections.Generic.List<PersonalityChange> changes = null) =>
            StrikeOff(roster, id, CharacterStatus.Deserted, story, weight, changes);

        /// <summary>
        /// A man laid up - his own charge went off early, or the other side got the
        /// better of it. Unlike the dead he keeps his post, his crew and his gun: he is
        /// coming back, and the outfit pays him while he is in there (see Wages). The
        /// day he is back on is stored, not counted down, so nothing drifts.
        /// </summary>
        public static OpResult Hospitalize(Roster roster, int id, int backOnDay,
            string note = "")
        {
            var member = roster.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Gone)
                return OpResult.Fail(GoneReason(member));

            member.Status = CharacterStatus.Hospitalized;
            member.BackOnDay = backOnDay;
            member.ConditionNote = note ?? "";
            // The note is cleared the day he stands up; the history is not. A file has
            // to be able to say he was shot in the spring after the spring is over.
            Career.WentDown(member, roster.Day, CharacterStatus.Hospitalized, member.ConditionNote);
            return OpResult.Success;
        }

        /// <summary>
        /// Taken and held. Same bargain as a hospital bed - he keeps his post, his crew
        /// and his place on the payroll, because an outfit that stops paying the men
        /// inside is an outfit that gets informed on - and the day he is out is stored
        /// rather than counted down.
        /// </summary>
        public static OpResult Jail(Roster roster, int id, int backOnDay, string note = "",
            string charge = "", string dateStamp = "")
        {
            var member = roster.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Gone)
                return OpResult.Fail(GoneReason(member));

            member.Status = CharacterStatus.Jailed;
            member.BackOnDay = backOnDay;
            member.ConditionNote = note ?? "";

            // Being taken goes on his record - that IS the rap sheet, and a file that
            // showed only the priors would stop being a record the day he joined.
            if (!string.IsNullOrEmpty(charge))
                RapSheet.Add(member, dateStamp, charge,
                    backOnDay > 0 ? "Held — out day " + backOnDay : "Held");
            Career.WentDown(member, roster.Day, CharacterStatus.Jailed,
                string.IsNullOrEmpty(charge) ? member.ConditionNote : charge);
            return OpResult.Success;
        }

        /// <summary>
        /// Puts back to work everyone whose day has come. Returns how many stood up, so
        /// the caller can decide whether the page needs repainting. The dead are not
        /// checked - nothing brings them back.
        /// </summary>
        public static int Discharge(Roster roster, int day)
        {
            if (roster == null)
                return 0;

            var back = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Gone || member.Status == CharacterStatus.Active)
                    continue;
                // No date means no release: a man held at somebody else's pleasure
                // stays held until whatever put him there lets him out. Only a stated
                // day discharges him, or day one would empty every cell in the city.
                if (member.BackOnDay <= 0 || member.BackOnDay > day)
                    continue;

                member.Status = CharacterStatus.Active;
                member.BackOnDay = 0;
                // A man on his feet carries no note - leaving the old one would print
                // "two ribs" beside FIT for the rest of his career.
                member.ConditionNote = "";
                back++;
            }
            return back;
        }

        static OpResult StrikeOff(Roster roster, int id, CharacterStatus status,
            string story = "", int weight = 0,
            System.Collections.Generic.List<PersonalityChange> changes = null)
        {
            var member = roster.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Gone)
                return OpResult.Fail(GoneReason(member));

            member.Status = status;
            member.Wanted = false;
            Career.StruckOff(member, roster.Day, status, story, weight);
            for (var i = 0; i < roster.Equipment.Count; i++)
                if (roster.Equipment[i].HolderId == id)
                    roster.Equipment[i].HolderId = RosterEquipment.Unheld;

            var crew = roster.CrewOf(id);
            if (crew != null && crew.LieutenantId == id)
            {
                Character heir = null;
                foreach (var hoodId in crew.HoodIds)
                {
                    var hood = roster.Find(hoodId);
                    if (hood == null || hood.Status != CharacterStatus.Active) continue;
                    if (heir == null || hood.Loyalty > heir.Loyalty) heir = hood;
                }
                if (heir != null)
                {
                    crew.HoodIds.Remove(heir.Id);
                    roster.Organization.BossHoodIds.Remove(heir.Id);
                    heir.Rank = Rank.Lieutenant;
                    crew.LieutenantId = heir.Id;
                    // Succession is still a promotion: the rank clock restarts and his
                    // loyalty re-aims at the Boss, exactly as the regular path stamps
                    // them. Without these the drift kept fining the new lieutenant as
                    // "parked" against a date from his corner days.
                    heir.RankSince = roster.Day;
                    Loyalty.Reaim(heir,
                        "stepped up when his lieutenant went down, and answers to the Boss now",
                        changes);
                    Career.RankChanged(heir, roster.Day, Rank.Lieutenant,
                        "stepped up when " + member.Surname + " went down");
                }
                else
                {
                    var formerHoods = new System.Collections.Generic.List<int>(crew.HoodIds);
                    roster.Crews.Remove(crew);
                    for (var i = 0; i < formerHoods.Count; i++)
                        PutUnderBossIfPresent(roster, formerHoods[i]);
                }
            }
            else
                Detach(roster, id);

            if (roster.FrontId == id)
                roster.FrontId = -1;
            return OpResult.Success;
        }

        static string GoneReason(Character member) =>
            member.Status == CharacterStatus.Deserted ? LedgerText.ReasonDeserted : LedgerText.ReasonDead;

        /// <summary>Removes the Hood from every post and command branch before one
        /// authoritative destination is written.</summary>
        static void Detach(Roster roster, int id)
        {
            if (roster.FrontId == id)
                roster.FrontId = -1;

            roster.Organization.BossHoodIds.Remove(id);
            for (var i = 0; i < roster.Crews.Count; i++)
                roster.Crews[i].HoodIds.Remove(id);
        }

        static void PutUnderBossIfPresent(Roster roster, int id)
        {
            var boss = roster.FindBoss();
            var member = roster.Find(id);
            if (boss == null || boss.Rank != Rank.Boss || member == null ||
                member.Rank != Rank.Hood || member.Specialty != Specialty.None || member.Gone ||
                roster.Organization.BossHoodIds.Contains(id))
                return;
            roster.Organization.BossHoodIds.Add(id);
            // Where a man went when his crew broke up under him is part of his story,
            // and it is the relationship his loyalty is measured against from now on.
            Career.Posted(member, roster.Day, boss.FullName);
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
