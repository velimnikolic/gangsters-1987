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
        /// Yes. He asked for the rate, and he is put ON the rate: his bargain is torn
        /// up (<c>WageAsked = 0</c>) and from that midnight he draws the house scale
        /// like every man the outfit raised itself. The asking stops, and a man who was
        /// skimming over it stops too - he was taking what he thought he was owed.
        ///
        /// WAGE-002. This used to write the demanded FIGURE onto his bargain, which
        /// froze his envelope at what he was worth on the day he asked while his stars
        /// went on rising underneath it - so a greedy man came back with a new demand
        /// every thirty-five days, forever, and every grant bought thirty-five days of
        /// quiet instead of settling anything.
        /// </summary>
        public static OpResult GrantRaise(Roster roster, int id)
        {
            var member = roster?.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.WageDemand <= 0)
                return OpResult.Fail(LedgerText.ReasonNoDemand);

            member.WageAsked = 0;
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
        /// (Wages.WageFor) - not because anything was written on him, and it always
        /// RISES: the lieutenant base sits above the hood ceiling by construction.
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
            var witnesses = oldCrew != null ? AllCrewHoods(oldCrew) : null;

            Detach(roster, id);
            var member = roster.Find(id);
            member.Duty = Duty.None;   // he runs the branch now; he does not walk it
            member.Rank = Rank.Lieutenant;
            member.RankSince = roster.Day;
            // A NEW RANK IS A NEW BARGAIN (WAGE-002): whatever he signed for as a hood
            // has nothing to say about what a lieutenant costs, and the house rate of
            // the rank he now holds applies the same midnight. A man signed out of the
            // classified column is promoted on his way onto the books, so the door that
            // signs him re-stamps his ask AFTER this returns.
            member.WageAsked = 0;
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
            var formerHoods = crew != null ? AllCrewHoods(crew) : null;
            if (crew != null)
            {
                roster.Crews.Remove(crew);
                for (var i = 0; i < formerHoods.Count; i++)
                    ClearDuty(roster, formerHoods[i]);
            }

            member.Rank = Rank.Hood;
            member.RankSince = roster.Day;
            member.Duty = Duty.None;
            // The other half of WAGE-002's rule: a demoted paper lieutenant used to
            // draw his old lieutenant's ask as a hood for the rest of his life. He is
            // a hood now, and he is paid a hood's house rate.
            member.WageAsked = 0;
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

        /// <summary>
        /// Marks a man for a standing duty, or takes it off him.
        ///
        /// Only a HOOD IN A CREW carries one. A lieutenant runs the branch and does not
        /// walk its doors; a man in the pool, on the front desk or straight under the
        /// Boss is on nobody's round. Taking a duty OFF is always allowed - a man whose
        /// footing has changed under him must never be stuck holding a job the books no
        /// longer let him do.
        /// </summary>
        public static OpResult SetDuty(Roster roster, int id, Duty duty)
        {
            var member = roster?.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);

            if (duty == Duty.None)
            {
                var assigned = roster.CrewOf(id);
                if (assigned != null && assigned.BagId == id)
                    ReturnBagNodeToLine(roster, assigned);
                else if (assigned != null && assigned.EscortIds.Remove(id))
                {
                    member.Duty = Duty.None;
                    AddLineHood(roster, assigned, id);
                }
                else
                    member.Duty = Duty.None;
                return OpResult.Success;
            }

            if (member.Rank != Rank.Hood)
                return OpResult.Fail("only a hood carries the bag");
            var assignment = roster.AssignmentOf(id);
            if (assignment.Kind != AssignmentKind.Crew)
                return OpResult.Fail("he has to be in a crew");

            var crew = roster.FindCrew(assignment.CrewId);
            if (crew == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchCrew);

            if (duty == Duty.Escort)
            {
                if (crew.BagId < 0)
                    return OpResult.Fail("nobody carries the bag for him to guard");
                if (crew.BagId == id)
                    return OpResult.Fail("the collector cannot escort himself");
                if (crew.EscortIds.Contains(id))
                    return OpResult.Fail("he is already on the bag's detail");
                if (crew.EscortIds.Count >= Crew.MaxEscorts)
                    return OpResult.Fail("his escort is full · " + Crew.MaxEscorts + " men");
                crew.HoodIds.Remove(id);
                crew.EscortIds.Add(id);
                member.Duty = Duty.Escort;
                return OpResult.Success;
            }

            if (duty != Duty.Collector)
                return OpResult.Fail("that duty is not carried by a crew");

            // One collector node per crew. Replacing its head returns the previous
            // collector to the line; the escorts remain attached to the bag.
            if (crew.BagId >= 0 && crew.BagId != id)
            {
                var previous = roster.Find(crew.BagId);
                if (previous != null)
                    previous.Duty = Duty.None;
                AddLineHood(roster, crew, crew.BagId);
            }
            crew.HoodIds.Remove(id);
            crew.EscortIds.Remove(id);
            crew.BagId = id;

            member.Duty = duty;
            return OpResult.Success;
        }

        // ------------------------------------------------------------------ the keeper

        /// <summary>
        /// Whether this man can be spared to keep a flat, and why not when he cannot. Asked
        /// by the sheet BEFORE it offers him, so the picker greys a man out with the reason
        /// on him rather than failing on the click.
        ///
        /// The boss runs the outfit, a lieutenant runs his branch, and a specialist was
        /// bought for other work; a man in a cell or a bed cannot stand in a room at all.
        /// </summary>
        public static bool CanKeep(Roster roster, int id, out string reason)
        {
            reason = "";
            var member = roster?.Find(id);
            if (member == null)
            {
                reason = LedgerText.ReasonNoSuchMember;
                return false;
            }
            if (member.Gone)
            {
                reason = "off the books";
                return false;
            }
            if (member.Status == CharacterStatus.Jailed)
            {
                reason = "jailed";
                return false;
            }
            if (member.Status == CharacterStatus.Hospitalized)
            {
                reason = "hurt";
                return false;
            }
            if (id == roster.BossId)
            {
                reason = "he runs the outfit";
                return false;
            }
            if (member.Specialty != Specialty.None)
            {
                reason = "bought for other work";
                return false;
            }
            if (member.Rank != Rank.Hood)
            {
                reason = "he runs a crew";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Takes a man OFF THE STREET and puts him in a room. He leaves his crew's line -
        /// the bag and its escort included - so nothing walks him anywhere while he keeps
        /// the flat; by derivation he then reads as pooled, which is what "off the street"
        /// means to every other sheet.
        ///
        /// The flat itself is written by <see cref="LivingCity.Property.Apartments"/>: this
        /// owns only the rules about who may be spared.
        /// </summary>
        public static OpResult SetKeeper(Roster roster, int id)
        {
            if (!CanKeep(roster, id, out var reason))
                return OpResult.Fail(reason);

            var member = roster.Find(id);
            var crew = roster.CrewOf(id);
            if (crew != null)
            {
                if (crew.BagId == id)
                    ReturnBagNodeToLine(roster, crew);
                crew.HoodIds.Remove(id);
                crew.EscortIds.Remove(id);
                if (crew.BagId == id)
                    crew.BagId = -1;
            }

            member.Duty = Duty.Keeper;
            return OpResult.Success;
        }

        /// <summary>Pulls him out of the room and back onto the street. Always allowed: a
        /// man must never be stuck holding a job the books no longer let him do.</summary>
        public static OpResult ClearKeeper(Roster roster, int id)
        {
            var member = roster?.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Duty == Duty.Keeper)
                member.Duty = Duty.None;
            return OpResult.Success;
        }

        /// <summary>The one man of the crew marked for the bag and still on the books,
        /// or -1. A man in a cell or a bed still holds the mark - he is not walking
        /// it, but it is his (CollectorsOf is the list that answers who can walk).</summary>
        public static int CollectorOf(Roster roster, int crewId)
        {
            var crew = roster?.FindCrew(crewId);
            if (crew == null)
                return -1;
            var man = roster.Find(crew.BagId);
            if (man != null && !man.Gone && man.Duty == Duty.Collector)
                return man.Id;
            return -1;
        }

        /// <summary>THE BOSS NAMES THE BAG MAN, from the ledger: one of that
        /// lieutenant's own hoods, and the ruling sticks until the boss changes it.</summary>
        public static OpResult NameCollector(Roster roster, int crewId, int hoodId)
        {
            var crew = roster?.FindCrew(crewId);
            if (crew == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchCrew);
            if (!crew.HoodIds.Contains(hoodId))
                return OpResult.Fail("he is not one of that lieutenant's men");
            var result = SetDuty(roster, hoodId, Duty.Collector);
            if (result.Ok)
            {
                crew.BagNamedByBoss = true;
                crew.BagNamedId = hoodId;
            }
            return result;
        }

        /// <summary>THE BOSS TAKES THE BAG OFF HIM and leaves it with nobody. That is a
        /// ruling too: the lieutenant does not quietly hand it to the next man at
        /// midnight - the boss said nobody, and nobody it is until LET HIM PICK.</summary>
        public static OpResult TakeOffTheBag(Roster roster, int hoodId)
        {
            var member = roster?.Find(hoodId);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            var crew = roster.CrewOf(hoodId);
            if (crew == null || crew.BagId != hoodId)
                return OpResult.Fail("he is not carrying a crew's bag");
            ReturnBagNodeToLine(roster, crew);
            crew.BagNamedByBoss = true;
            crew.BagNamedId = -1;   // he ruled NOBODY, and that is a ruling too
            return OpResult.Success;
        }

        /// <summary>Posts a line hood, or an unassigned hood first put into the crew,
        /// to the collector's own detail.</summary>
        public static OpResult PostEscort(Roster roster, int crewId, int hoodId,
            System.Collections.Generic.List<PersonalityChange> changes = null)
        {
            var crew = roster?.FindCrew(crewId);
            if (crew == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchCrew);
            if (crew.BagId < 0)
                return OpResult.Fail("nobody carries the bag for him to guard");
            if (crew.LieutenantId == hoodId || crew.BagId == hoodId)
                return OpResult.Fail("that man cannot take an escort place");
            if (roster.FrontId == hoodId)
                return OpResult.Fail("the front man cannot leave the desk");
            if (crew.EscortIds.Contains(hoodId))
                return OpResult.Fail("he is already on the bag's detail");
            if (crew.EscortIds.Count >= Crew.MaxEscorts)
                return OpResult.Fail("his escort is full · " + Crew.MaxEscorts + " men");

            var member = roster.Find(hoodId);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Rank != Rank.Hood || member.Specialty != Specialty.None || member.Gone)
                return OpResult.Fail("only an available hood can escort the bag");

            if (!crew.HoodIds.Contains(hoodId))
            {
                if (roster.CrewOf(hoodId) != null)
                    return OpResult.Fail("he already answers to another crew");
                var assigned = AssignToCrew(roster, hoodId, crewId, changes);
                if (!assigned.Ok)
                    return assigned;
            }
            return SetDuty(roster, hoodId, Duty.Escort);
        }

        public static OpResult PullEscort(Roster roster, int hoodId)
        {
            var crew = roster?.CrewOf(hoodId);
            if (crew == null || !crew.EscortIds.Contains(hoodId))
                return OpResult.Fail("he is not on the bag's detail");
            return SetDuty(roster, hoodId, Duty.None);
        }

        /// <summary>The living men posted to this bag, in posting order. A man in a
        /// cell or a bed keeps his post on the books, just as the collector does.</summary>
        public static void EscortsOf(Roster roster, int crewId,
            System.Collections.Generic.List<Character> into)
        {
            into?.Clear();
            var crew = roster?.FindCrew(crewId);
            if (crew == null || into == null)
                return;
            for (var i = 0; i < crew.EscortIds.Count; i++)
            {
                var escort = roster.Find(crew.EscortIds[i]);
                if (escort != null && !escort.Gone && escort.Duty == Duty.Escort)
                    into.Add(escort);
            }
        }

        /// <summary>
        /// THE LIEUTENANT HANDS THE BAG TO ONE OF HIS OWN (CollectorChoice): the best
        /// man he has if he is an organizer, a worse one if he is not. Clears the
        /// boss's ruling on this crew's bag - the job is his again from here.
        /// </summary>
        public static OpResult LetLieutenantPick(Roster roster, int crewId, out int hoodId)
        {
            hoodId = -1;
            var crew = roster?.FindCrew(crewId);
            if (crew == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchCrew);
            var pick = CollectorChoice.Pick(roster, crew);
            if (pick < 0)
                return OpResult.Fail("he has nobody to give the bag to");
            var result = SetDuty(roster, pick, Duty.Collector);
            if (!result.Ok)
                return result;
            crew.BagNamedByBoss = false;
            crew.BagNamedId = -1;
            hoodId = pick;
            return OpResult.Success;
        }

        /// <summary>
        /// One crew's bag, looked at the way its lieutenant looks at it every morning:
        /// a man on his feet holds it - nothing to do; nobody holds it, or the man who
        /// does is in a cell or a bed - hand it to another, unless the boss has ruled
        /// on this bag, in which case the boss's word stands (his named man keeps it
        /// through a sentence; his "nobody" stays nobody). Returns the id newly handed
        /// the bag, or -1 when nothing moved.
        /// </summary>
        public static int TendCrewBag(Roster roster, Crew crew)
        {
            if (roster == null || crew == null)
                return -1;
            var lieutenant = roster.Find(crew.LieutenantId);
            if (lieutenant == null || lieutenant.Gone)
                return -1;

            var current = CollectorOf(roster, crew.Id);
            if (current >= 0)
            {
                var holder = roster.Find(current);
                if (holder != null && holder.Status == CharacterStatus.Active)
                    return -1;
            }

            // THE BOSS'S RULING OUTLIVES A SENTENCE, NOT A MAN. A named man in a cell
            // or a bed keeps the bag - that is the whole point of naming him. A named
            // man who is DEAD, or who now answers to another lieutenant, is not a
            // ruling any more, he is a hole in the books: the ruling is spent and the
            // lieutenant hands the bag out again. Without this the crew's ground was
            // never collected on again, because nothing else clears the flag.
            if (crew.BagNamedByBoss)
            {
                if (crew.BagNamedId < 0)
                    return -1;   // he ruled NOBODY, and nobody it stays
                var named = roster.Find(crew.BagNamedId);
                if (named != null && !named.Gone && crew.BagId == crew.BagNamedId)
                    return -1;
                crew.BagNamedByBoss = false;
                crew.BagNamedId = -1;
            }

            return LetLieutenantPick(roster, crew.Id, out var picked).Ok ? picked : -1;
        }

        /// <summary>
        /// Every crew that answers for a block on the organization's paper gets its bag
        /// tended (TendCrewBag) - a crew with no ground collects nothing and needs no
        /// bag man. The pairs handed are returned so the day can print who gave the
        /// bag to whom.
        /// </summary>
        public static void TendCollectors(
            Roster roster, System.Collections.Generic.List<(int crewId, int hoodId)> handed)
        {
            handed?.Clear();
            if (roster == null)
                return;
            for (var c = 0; c < roster.Crews.Count; c++)
            {
                var crew = roster.Crews[c];
                if (!AnswersForABlock(roster, crew.LieutenantId))
                    continue;
                var picked = TendCrewBag(roster, crew);
                if (picked >= 0)
                    handed?.Add((crew.Id, picked));
            }
        }

        static bool AnswersForABlock(Roster roster, int leaderId)
        {
            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
                if (paper[i].LeaderId == leaderId && paper[i].BlockId.IsValid)
                    return true;
            return false;
        }

        /// <summary>The men of one crew who are marked for the bag and able to walk it.
        /// A man in a bed or a cell is on the books and not on the round.</summary>
        public static void CollectorsOf(
            Roster roster, int crewId, System.Collections.Generic.List<Character> into)
        {
            into?.Clear();
            var crew = roster?.FindCrew(crewId);
            if (crew == null || into == null)
                return;
            var man = roster.Find(crew.BagId);
            if (man != null && !man.Gone && man.Status == CharacterStatus.Active &&
                man.Duty == Duty.Collector)
                into.Add(man);
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
            var collector = roster.Find(crew.BagId);
            if (collector != null && !collector.Gone)
                men++;
            for (var i = 0; i < crew.EscortIds.Count; i++)
            {
                var escort = roster.Find(crew.EscortIds[i]);
                if (escort != null && !escort.Gone)
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
            ClearDuty(roster, id);
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
            ClearDuty(roster, id);
            PutUnderBossIfPresent(roster, id);
            return OpResult.Success;
        }

        /// <summary>A man moved off a crew is off its round with it - the duty belongs
        /// to the branch he was walking for, not to him.</summary>
        static void ClearDuty(Roster roster, int id)
        {
            var member = roster.Find(id);
            if (member != null)
                member.Duty = Duty.None;
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
            ClearDuty(roster, id);
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

        /// <summary>
        /// Sign a thing out. <paramref name="pin"/> is the difference between the two
        /// ways this door is used: STOCK for a branch (the lieutenant signs, his crew
        /// is dealt in by who can shoot) and a thing put in ONE named hand, which the
        /// deal then steps over for as long as he stands where he stands. The ledger's
        /// gun drawer pins - the boss clicked that man's file - and the street and the
        /// demos do not.
        /// </summary>
        public static OpResult GiveEquipment(Roster roster, int itemId, int id,
            bool pin = false)
        {
            var item = FindItem(roster, itemId);
            if (item == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchItem);

            var member = roster.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Gone)
                return OpResult.Fail(GoneReason(member));

            // A GUN goes into whatever hand the boss names - his own lieutenant's, or
            // a corner hood's over that lieutenant's head. WHEELS and grenades still go
            // through the man who runs the branch: a car belongs to a crew and to
            // whoever drives it that day, and a grenade is a countable stock the
            // quartermaster deals into no man's hand at all.
            var runsIt = CanBeIssuedGear(roster, id);
            if (!IsWeapon(item.Kind) && !runsIt)
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

            if (!pin && runsIt)
            {
                // Stock for his own deck. He deals it out himself, as he always has -
                // nothing here is pinned, or a crew would go unarmed behind a
                // lieutenant standing there holding six guns.
                item.OwnerId = id;
                item.HolderId = id;
                item.PinnedTo = RosterEquipment.Unheld;
                return OpResult.Success;
            }

            // A named man. The deed goes where his gear belongs - he carries nothing
            // out of the group he stands in - but the piece is HIS while he stands
            // there, and the deal does not take it off him (NormalizeArms).
            item.OwnerId = DeedGroupOf(roster, id);
            item.HolderId = id;
            item.PinnedTo = id;
            return OpResult.Success;
        }

        /// <summary>
        /// Whose deck a man's gear belongs to: the crew he stands in, or the front's
        /// locker when he stands in the pool - the parent rule, so a man who moves
        /// carries nothing out with him.
        ///
        /// The exception is a man who HAS a deck by rank and no crew around him: a
        /// lieutenant between crews, and above all the Don before his detail is
        /// standing. His own iron goes on his own name, or the outfit's head could not
        /// be handed a gun at all - the front would take the deed and the desk's guards
        /// would be dealt it out from under him.
        /// </summary>
        static int DeedGroupOf(Roster roster, int id)
        {
            var crew = roster.CrewOf(id);
            if (crew != null)
                return crew.LieutenantId;
            var member = roster.Find(id);
            return member != null &&
                   (member.Rank == Rank.Boss || member.Rank == Rank.Lieutenant)
                ? id
                : RosterEquipment.FrontArmory;
        }

        /// <summary>A man's own iron: the deed and the pin name the same man, and he
        /// is still on the books. It belongs to no deck and is dealt by nobody.</summary>
        static bool IsOwnIron(Roster roster, RosterEquipment item)
        {
            if (item.PinnedTo == RosterEquipment.Unheld ||
                item.PinnedTo != item.OwnerId)
                return false;
            var man = roster.Find(item.OwnerId);
            return man != null && !man.Gone;
        }

        /// <summary>Whether a man stands in the group that owns a piece - the one
        /// question a pin outlives everything else on.</summary>
        static bool InOwnerGroup(Roster roster, int ownerId, int id)
        {
            if (ownerId == RosterEquipment.FrontArmory)
                return InFrontGuard(roster, id);
            if (ownerId == RosterEquipment.Unheld)
                return false;
            if (ownerId == id)
                return true;
            var crew = roster.CrewOf(id);
            return crew != null && crew.LieutenantId == ownerId;
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
            // (NormalizeArms, wheels by Driving) and runs immediately after - and any
            // pin on it dies with the old deed.
            item.OwnerId = id;
            item.HolderId = id;
            item.PinnedTo = RosterEquipment.Unheld;
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
            item.OwnerId = RosterEquipment.FrontArmory;
            item.HolderId = RosterEquipment.FrontArmory;
            item.PinnedTo = RosterEquipment.Unheld;
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
            item.PinnedTo = RosterEquipment.Unheld;
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

                // A man's OWN iron - the deed and the pin naming the same man - is a
                // valid holding whether or not he runs anything: the Don before his
                // detail stands, a lieutenant whose crew was disbanded under him. It is
                // in no deck, so no deal ever touches it; it goes back to the safe when
                // he goes off the books, which the line below catches on the same pass.
                if (!RunsABranch(roster, item.OwnerId) && !IsOwnIron(roster, item))
                {
                    item.OwnerId = RosterEquipment.Unheld;
                    item.HolderId = RosterEquipment.Unheld;
                }
            }

            // Then the boss's own hand. A pin is his word about ONE man and it outlives
            // a deal, a re-shuffle of the crew and a spell in a hospital bed; it lapses
            // only when the man it names is off the books for good or no longer stands
            // in the group the piece belongs to. A man in a bed keeps his pin and loses
            // the piece for as long as he is off his feet - it comes back with him.
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.PinnedTo == RosterEquipment.Unheld)
                    continue;
                var man = roster.Find(item.PinnedTo);
                if (man == null || man.Gone ||
                    !InOwnerGroup(roster, item.OwnerId, item.PinnedTo))
                    item.PinnedTo = RosterEquipment.Unheld;
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

            // The boss's own hand comes off the top. A piece pinned to a man who is
            // standing today is his; it and he both step out of the deal, and the
            // quartermaster deals what is left over the hands that are left. He does
            // not take a man's iron off him to make his arithmetic come out.
            var deck = new System.Collections.Generic.List<RosterEquipment>(items);
            var ranked = new System.Collections.Generic.List<Character>(hands);
            for (var i = deck.Count - 1; i >= 0; i--)
            {
                var pinned = deck[i].PinnedTo;
                if (pinned == RosterEquipment.Unheld)
                    continue;
                var at = -1;
                for (var h = 0; h < ranked.Count; h++)
                    if (ranked[h].Id == pinned)
                    {
                        at = h;
                        break;
                    }
                // Not standing today - the pin keeps, the piece deals on without him.
                if (at < 0)
                    continue;
                deck[i].HolderId = pinned;
                deck.RemoveAt(i);
                ranked.RemoveAt(at);
            }
            if (deck.Count == 0)
                return;

            deck.Sort((x, y) => y.Value != x.Value
                ? y.Value.CompareTo(x.Value) : x.Id.CompareTo(y.Id));

            ranked.Sort((x, y) =>
            {
                var sx = x.GetHalfSteps(stat);
                var sy = y.GetHalfSteps(stat);
                return sy != sx ? sy.CompareTo(sx) : x.Id.CompareTo(y.Id);
            });

            var pairs = System.Math.Min(deck.Count, ranked.Count);
            var correct = pairs * organization / AttributeScale.MaxHalfSteps;

            for (var i = 0; i < pairs; i++)
            {
                var hand = i < correct ? ranked[i] : ranked[correct + (pairs - 1 - i)];
                deck[i].HolderId = hand.Id;
            }

            // One piece per pair of hands; the warehouse takes the surplus.
            for (var i = pairs; i < deck.Count; i++)
                deck[i].HolderId = warehouseId;
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
        /// with nobody left to take it, folds. And the Don's chair passes the same way,
        /// to his most loyal lieutenant (<see cref="SucceedTheBoss"/>): a house is not
        /// lost to one bullet either.
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
        /// CUT HIM LOOSE (GAN-245). The city has him, and the boss has decided not to
        /// carry him: struck off the same way a deserter is - his line kept, his gear
        /// pooled, his post passed on - but marked as what it is, because it was not
        /// the man who walked away.
        ///
        /// And the outfit is TOLD. Every loyalty movement here goes through
        /// <see cref="NudgePersonality"/> with a printed reason, because a crew whose
        /// lieutenant was sold and whose loyalty fell for reasons nobody was given is
        /// the betrayal the player could not have seen coming. The weights are
        /// <see cref="Loyalty"/>'s.
        ///
        /// The men are read BEFORE he is struck off: striking a lieutenant off promotes
        /// an heir out of his own crew, and a crew read afterwards is a different crew.
        /// </summary>
        /// <summary>
        /// WHETHER HE CAN BE SOLD. Only a man the city is holding - in a cell, or out on
        /// the outfit's own bail money waiting on a court day. Nobody is cut loose off a
        /// street corner.
        ///
        /// The ONE predicate: the desk offers the key on it, the ledger's sheets show it
        /// on it, and <see cref="CutLoose"/> refuses on it. Two copies of this rule
        /// drift into a button that is offered and then refused (GAN-302).
        /// </summary>
        public static bool CanCutLoose(Character member) =>
            member != null && !member.Gone &&
            (member.Status == CharacterStatus.Jailed || member.BailedUntil > 0);

        public static OpResult CutLoose(Roster roster, int id,
            System.Collections.Generic.List<PersonalityChange> changes = null)
        {
            var member = roster?.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Gone)
                return OpResult.Fail(GoneReason(member));
            if (!CanCutLoose(member))
                return OpResult.Fail(LedgerText.ReasonNotInside);

            var wasLieutenant = member.Rank == Rank.Lieutenant;
            var crew = roster.CrewOf(id);
            var his = new System.Collections.Generic.List<int>();
            if (crew != null)
            {
                if (crew.LieutenantId != id)
                    his.Add(crew.LieutenantId);
                for (var i = 0; i < crew.HoodIds.Count; i++)
                    if (crew.HoodIds[i] != id)
                        his.Add(crew.HoodIds[i]);
            }

            var result = StrikeOff(roster, id, CharacterStatus.CutLoose,
                "Cut loose by the boss while inside.", 0, changes);
            if (!result.Ok)
                return result;

            var crewHit = wasLieutenant
                ? Loyalty.CutLooseCrewHit : Loyalty.CutLooseHoodCrewHit;
            var restHit = wasLieutenant
                ? Loyalty.CutLooseOutfitHit : Loyalty.CutLooseHoodOutfitHit;
            var ownReason = wasLieutenant
                ? "the boss sold their lieutenant"
                : "the boss sold one of their own";

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man.Id == id || man.Gone || man.Rank == Rank.Boss ||
                    man.Specialty != Specialty.None)
                    continue;

                int hit;
                string why;
                if (his.Contains(man.Id))
                {
                    hit = crewHit;
                    why = ownReason;
                }
                else if (wasLieutenant && man.Rank == Rank.Lieutenant)
                {
                    hit = Loyalty.CutLooseLieutenantHit;
                    why = "watched the boss sell a lieutenant";
                }
                else
                {
                    hit = restHit;
                    why = "word went round that the boss sold a man inside";
                }

                NudgePersonality(man, PersonalityTrait.Loyalty,
                    -Loyalty.CutLooseHit(man, hit), why, changes);
            }

            return OpResult.Success;
        }

        /// <summary>
        /// A man laid up - his own charge went off early, or the other side got the
        /// better of it. Unlike the dead he keeps his post, his crew and his gun: he is
        /// coming back, and the outfit pays him while he is in there (see Wages). The
        /// day he is back on is stored, not counted down, so nothing drifts.
        /// </summary>
        /// <summary>How long a man they let go spends in a bed - the same span they
        /// held him for (RIVAL-009 step 6, D22's KidnapDays). Named here rather than
        /// read from the order table because Personnel owes the order book nothing.
        /// </summary>
        public const int HeldDays = 3;

        /// <summary>
        /// ANOTHER FAMILY HAS HIM. Off the books until they let him go, and he does not
        /// walk back onto the street when they do - see the return sweep.
        /// </summary>
        public static OpResult Taken(Roster roster, int id, int backOnDay, string note = "")
        {
            var member = roster?.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Gone)
                return OpResult.Fail(GoneReason(member));

            member.Status = CharacterStatus.Taken;
            member.BackOnDay = backOnDay;
            member.ConditionNote = note ?? "";
            Career.WentDown(member, roster.Day, CharacterStatus.Taken, member.ConditionNote);
            return OpResult.Success;
        }

        /// <summary>THE RANSOM IS PAID (EPIC 42, DIPL-005): a man they hold is let go
        /// on the day named - still to a bed, since Discharge puts a taken man there -
        /// and nothing else about him moves.</summary>
        public static OpResult LetGo(Roster roster, int id, int backOnDay)
        {
            var member = roster?.Find(id);
            if (member == null)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            if (member.Status != CharacterStatus.Taken)
                return OpResult.Fail(LedgerText.ReasonNoSuchMember);
            member.BackOnDay = backOnDay;
            return OpResult.Success;
        }

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
            member.JailedOnDay = roster.Day;
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

                // A MAN THEY LET GO IS NOT A MAN WHO WALKS IN WHISTLING. He comes
                // back the way people come back from three days in somebody's cellar:
                // in a bed, for as long again as they held him.
                if (member.Status == CharacterStatus.Taken)
                {
                    member.Status = CharacterStatus.Hospitalized;
                    member.BackOnDay = day + HeldDays;
                    continue;
                }

                if (member.Status == CharacterStatus.Jailed)
                {
                    // The cell remembers how long he was in it (EPIC 40, THE CELL).
                    member.ReleasedOnDay = day;
                    member.NightsInside = member.JailedOnDay > 0
                        ? day - member.JailedOnDay
                        : 0;
                }
                member.Status = CharacterStatus.Active;
                member.BackOnDay = 0;
                // A man on his feet carries no note - leaving the old one would print
                // "two ribs" beside FIT for the rest of his career.
                member.ConditionNote = "";
                // AND HE IS BACK IN THE CITY. Sending a man away (Police.WantedLevels
                // .SendAway) is the one away-state that also takes him off the payroll,
                // and nothing anywhere put the flag down again: he came back on his
                // feet, drew nothing for the rest of the campaign, and could never be
                // sent away a second time because CanSendAway refuses a man already
                // gone. The bus ticket buys FOURTEEN DAYS, not a free man for life.
                member.OutOfTown = false;
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

            // THE CHAIR HAS AN HEIR (EPIC 25, Q3 - the user's word of 2026-09-03: one
            // of the lieutenants takes it). Read BEFORE the crew below is passed on:
            // the Boss's own crew is his detail, and the man who takes the chair takes
            // the detail with it.
            if (roster.Organization.BossId == id)
                SucceedTheBoss(roster, member, changes);

            var crew = roster.CrewOf(id);
            if (crew != null && crew.LieutenantId == id)
                PassTheCrewOn(roster, crew, member, changes);
            else
                Detach(roster, id);

            if (roster.FrontId == id)
                roster.FrontId = -1;
            return OpResult.Success;
        }

        /// <summary>
        /// THE GROUND GOES WITH THE CHAIR. A leader who is struck off leaves rows in
        /// <see cref="OrganizationState.BlockResponsibilities"/> naming him, and nothing
        /// ever rewrote them: the block went on answering to a corpse, no crew matched
        /// that leader, and the collector rota (<see cref="TendCollectors"/>) quietly
        /// skipped every block he held while the house played on (Codex adversarial
        /// review, 2026-09-03).
        ///
        /// Written straight onto the paper rather than through
        /// <see cref="AssignBlockResponsibility"/>: succession is not a new assignment,
        /// and ground that no longer fits under one man is FLAGGED by the validator,
        /// never quietly dropped (ORG: OverCapacityIsFlaggedNeverFixed).
        /// </summary>
        static void PassTheGroundOn(Roster roster, int fromLeaderId, int toLeaderId)
        {
            if (roster == null || fromLeaderId == toLeaderId)
                return;
            var paper = roster.Organization.BlockResponsibilities;
            for (var i = 0; i < paper.Count; i++)
                if (paper[i].LeaderId == fromLeaderId)
                    paper[i] = new OrganizationBlockResponsibility(
                        paper[i].BlockId, toLeaderId);
        }

        /// <summary>
        /// The crew of a lieutenant who is going: his most loyal man on his feet steps
        /// up into the chair, and a crew with nobody left to take it is dissolved under
        /// the Boss. Succession is still a promotion - the rank clock restarts and his
        /// loyalty re-aims at the Boss, exactly as the regular path stamps them.
        /// Without these the drift kept fining the new lieutenant as "parked" against a
        /// date from his corner days.
        /// </summary>
        static void PassTheCrewOn(Roster roster, Crew crew, Character leaving,
            System.Collections.Generic.List<PersonalityChange> changes)
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
                heir.RankSince = roster.Day;
                Loyalty.Reaim(heir,
                    "stepped up when his lieutenant went down, and answers to the Boss now",
                    changes);
                Career.RankChanged(heir, roster.Day, Rank.Lieutenant,
                    "stepped up when " + leaving.Surname + " went down");
                // What his crew answered for is his crew's, and it goes with it.
                PassTheGroundOn(roster, leaving.Id, heir.Id);
            }
            else
            {
                var formerHoods = AllCrewHoods(crew);
                roster.Crews.Remove(crew);
                for (var i = 0; i < formerHoods.Count; i++)
                {
                    ClearDuty(roster, formerHoods[i]);
                    PutUnderBossIfPresent(roster, formerHoods[i]);
                }
                // The men went under the Boss; so does the ground they were holding.
                PassTheGroundOn(roster, leaving.Id, roster.Organization.BossId);
            }
        }

        /// <summary>
        /// A DEAD DON IS SUCCEEDED (EPIC 25, Q3). Nothing moved
        /// <see cref="Organization.BossId"/> when the man it named went down, so
        /// <see cref="Roster.FindBoss"/> went on answering a corpse and the house
        /// latched Fallen (ours) or read Extinct (a family's) with twenty men still on
        /// its books. Now the most loyal LIEUTENANT still on his feet takes the chair -
        /// the same rule the crews are passed on by, one rank up, so no new number and
        /// no new preference enter the game.
        ///
        /// He leaves his own crew behind him first (it passes on by the ordinary rule),
        /// then takes the dead man's detail, and his bargain is torn up because a new
        /// rank is a new bargain (WAGE-002) - a Don left on a lieutenant's envelope
        /// would read as underpaid against his own house rate from the hour he took the
        /// chair, and demand a raise of himself.
        ///
        /// A house with no lieutenant left has nobody to take it and falls, which is
        /// what it did before.
        /// </summary>
        static void SucceedTheBoss(Roster roster, Character fallen,
            System.Collections.Generic.List<PersonalityChange> changes)
        {
            Character heir = null;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man == null || man.Id == fallen.Id || man.Gone ||
                    man.Status != CharacterStatus.Active || man.Rank != Rank.Lieutenant)
                    continue;
                if (heir == null || man.Loyalty > heir.Loyalty) heir = man;
            }
            if (heir == null)
                return;

            var his = roster.CrewOf(heir.Id);
            if (his != null && his.LieutenantId == heir.Id)
                PassTheCrewOn(roster, his, heir, changes);

            var detail = roster.CrewOf(fallen.Id);
            if (detail != null && detail.LieutenantId == fallen.Id)
                detail.LieutenantId = heir.Id;

            heir.Rank = Rank.Boss;
            heir.RankSince = roster.Day;
            heir.WageAsked = 0;
            roster.Organization.BossId = heir.Id;
            roster.Organization.BossHoodIds.Remove(heir.Id);
            // Whatever the Don answered for himself is answered for by the man in his
            // chair - read AFTER the crews above, so his own old crew's ground has
            // already gone to his own successor and is not swept up with it.
            PassTheGroundOn(roster, fallen.Id, heir.Id);
            Career.RankChanged(heir, roster.Day, Rank.Boss,
                "took the chair when " + fallen.Surname + " went down");
        }

        static string GoneReason(Character member) => member.Status switch
        {
            CharacterStatus.Deserted => LedgerText.ReasonDeserted,
            CharacterStatus.CutLoose => LedgerText.ReasonCutLoose,
            _ => LedgerText.ReasonDead,
        };

        /// <summary>Removes the Hood from every post and command branch before one
        /// authoritative destination is written.</summary>
        static void Detach(Roster roster, int id)
        {
            if (roster.FrontId == id)
                roster.FrontId = -1;

            roster.Organization.BossHoodIds.Remove(id);
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                if (crew.BagId == id)
                {
                    crew.BagId = -1;
                    ClearDuty(roster, id);
                    crew.BagNamedByBoss = false;
                    crew.BagNamedId = -1;
                    for (var e = crew.EscortIds.Count - 1; e >= 0; e--)
                    {
                        var escortId = crew.EscortIds[e];
                        ClearDuty(roster, escortId);
                        AddLineHood(roster, crew, escortId);
                    }
                    crew.EscortIds.Clear();
                }
                else if (crew.EscortIds.Remove(id))
                    ClearDuty(roster, id);
                crew.HoodIds.Remove(id);
            }
        }

        static System.Collections.Generic.List<int> AllCrewHoods(Crew crew)
        {
            var all = new System.Collections.Generic.List<int>(crew.HoodIds);
            if (crew.BagId >= 0 && !all.Contains(crew.BagId))
                all.Add(crew.BagId);
            for (var i = 0; i < crew.EscortIds.Count; i++)
                if (!all.Contains(crew.EscortIds[i]))
                    all.Add(crew.EscortIds[i]);
            return all;
        }

        static void ReturnBagNodeToLine(Roster roster, Crew crew)
        {
            if (crew.BagId >= 0)
            {
                ClearDuty(roster, crew.BagId);
                AddLineHood(roster, crew, crew.BagId);
            }
            crew.BagId = -1;
            for (var i = 0; i < crew.EscortIds.Count; i++)
            {
                ClearDuty(roster, crew.EscortIds[i]);
                AddLineHood(roster, crew, crew.EscortIds[i]);
            }
            crew.EscortIds.Clear();
        }

        static void AddLineHood(Roster roster, Crew crew, int id)
        {
            var member = roster.Find(id);
            if (member != null && !member.Gone && member.Rank == Rank.Hood &&
                !crew.HoodIds.Contains(id))
                crew.HoodIds.Add(id);
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
