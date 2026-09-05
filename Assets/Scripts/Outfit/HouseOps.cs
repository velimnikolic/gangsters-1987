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

        /// <summary>
        /// COUNSEL ON RETAINER (AI-005 P1, ruling A14). The signing money leaves this
        /// house's own safe first and the man follows: a specialist takes no rank, no
        /// crew and no place in the chain of command, and starts drawing the price he
        /// printed. The same shape as the ledger's HIRE on a lawyer's ad
        /// (PersonnelDirector.HireFromAd), with the house named.
        /// </summary>
        public static OpResult Retain(House house, HireAd ad)
        {
            if (house?.Roster == null || house.Runner == null)
                return OpResult.Fail(UI.LedgerText.ReasonNoSuchMember);
            if (ad?.Man == null || ad.Specialty == Specialty.None)
                return OpResult.Fail(UI.LedgerText.ReasonNoSuchMember);
            if (Lawyer.OnBooks(house.Roster) != null)
                return OpResult.Fail("the house already keeps counsel");

            var refusal = BalanceMath.TryPurchase(house.Runner.Accounts, ad.Down);
            if (refusal != null)
                return OpResult.Fail(refusal);

            var man = ad.Man;
            man.Id = house.Roster.NextCharacterId();
            man.Rank = Rank.Hood;
            man.Specialty = ad.Specialty;
            man.WageAsked = ad.Daily;
            house.Roster.Members.Add(man);
            Career.Joined(man, house.Roster.Day, "retained by the house");
            house.Touch();
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
            return Purchase(house, price, out _);
        }

        public static OpResult Purchase(House house, int price, out int dirtyPart)
        {
            dirtyPart = 0;
            if (house?.Runner == null)
                return OpResult.Fail(UI.LedgerText.ReasonFinanceUnavailable);
            var refusal = BalanceMath.TryPurchase(house.Runner.Accounts, price,
                out dirtyPart);
            if (refusal != null)
                return OpResult.Fail(refusal);
            house.Touch();
            return OpResult.Success;
        }

        /// <summary>Money back, and the purchase line with it - a sale that fell
        /// through is not a sale.</summary>
        public static void Refund(House house, int price, int dirtyPart)
        {
            if (house?.Runner == null || price <= 0)
                return;
            BalanceMath.RefundPurchase(house.Runner.Accounts, price, dirtyPart);
            house.Touch();
        }

        // ---------------------------------------------------------------- the table

        /// <summary>
        /// A PROPOSAL TO ANOTHER HOUSE (EPIC 42). The one door for the ledger's TABLE
        /// and a mind's intent alike. It refuses what should not be filed - nobody to
        /// say it to, the same thing already open, money the safe does not hold - in
        /// the words the ledger prints; then it files, prints the ask in both books,
        /// and, when the receiver is a mind and the edge has handed over its look,
        /// answers AT THE DESK: at once, by the tables, with the receiver's own view.
        /// A proposal to the player waits in his inbox for a Reply or its expiry.
        /// </summary>
        /// <summary>
        /// THE VIEW THE LEDGER'S TABLE ANSWERS WITH. The runtime edge hands its own
        /// Look over at install (the GuardOnTheDoor precedent), so a proposal the
        /// player files from a button reaches the same desk a mind's intent does.
        /// Null in a scene without the runtime: then a mind's answer waits for its
        /// next think, which is never.
        /// </summary>
        public static System.Func<House, HouseView> Look;

        public static OpResult Propose(Underworld world, House from, Proposal proposal,
            System.Func<House, HouseView> look = null)
        {
            if (world?.Diplomacy == null || from?.Runner == null || proposal == null)
                return OpResult.Fail(HouseDiplomacy.ReasonNothingToSay);
            if (proposal.Kind == ProposalKind.None)
                return OpResult.Fail(HouseDiplomacy.ReasonNothingToSay);
            var to = world.Of(proposal.To);
            if (to == null || to.Finished || to.GangId == from.GangId)
                return OpResult.Fail(HouseDiplomacy.ReasonNobodyToAskWord);
            if (world.Diplomacy.HasOpen(from.GangId, to.GangId, proposal.Kind))
                return OpResult.Fail(HouseDiplomacy.ReasonAlreadyAsked);
            var money = proposal.Terms != null ? proposal.Terms.Money : 0;
            // A bill is money the OTHER house pays; everything else is ours to put up.
            if (proposal.Kind != ProposalKind.Bill && money > from.Runner.Accounts.Safe)
                return OpResult.Fail(
                    UI.LedgerText.InsufficientFunds(money, from.Runner.Accounts.Safe));
            if ((proposal.Kind == ProposalKind.Warn || proposal.Kind == ProposalKind.Threaten ||
                 proposal.Kind == ProposalKind.Line) &&
                (proposal.Terms == null || proposal.Terms.Blocks.Count == 0))
                return OpResult.Fail(HouseDiplomacy.ReasonNoStreetNamed);
            // A BILL IS FOR WHAT THEY OWE US, and no more (Codex, EPIC 42): the sender's
            // own grudge prices it - for the player exactly as for a mind - so a weaker
            // house cannot be billed for nothing every morning.
            if (proposal.Kind == ProposalKind.Bill &&
                (money <= 0 || money > HouseDiplomacy.BillCeiling(world.Relations,
                    from.GangId, to.GangId, world.Diplomacy.Config,
                    from.Runner.Campaign.Day)))
                return OpResult.Fail(HouseDiplomacy.ReasonNoSuchDebt);

            var day = from.Runner.Campaign.Day;
            var filed = world.Diplomacy.File(
                from.GangId, to.GangId, proposal.Kind, proposal.Terms, day);
            proposal.Id = filed.Id;
            proposal.Day = filed.Day;
            proposal.ExpiresDay = filed.ExpiresDay;
            filed.Envoy = proposal.Envoy;
            filed.EnvoyHalfSteps = proposal.EnvoyHalfSteps;
            filed.InTransit = proposal.InTransit;
            if (proposal.Kind == ProposalKind.Bill)
            {
                var (clearedDay, points) = world.Relations.ClearedOn(from.GangId, to.GangId);
                filed.ClearedAtFiling = clearedDay == day ? points : 0;
                proposal.ClearedAtFiling = filed.ClearedAtFiling;
            }
            HouseDiplomacy.Print(world, filed, HouseDiplomacy.Describe(filed), day);
            from.Touch();

            if (look != null && !proposal.InTransit)
            {
                var view = look(to);
                if (!to.IsPlayer)
                {
                    var answer = HouseDiplomacy.Answer(view, filed, world.Diplomacy.Config,
                        world.Relations.Config);
                    var taken = world.Diplomacy.Settle(world, filed, answer.Accepted,
                        answer.Reason, day);
                    to.Touch();
                    // A NO AT THE DESK IS A REFUSAL OF THE ASK: the intent that carried it
                    // backs off like any refused intent (P4), so a mind does not ask the
                    // same thing every think until the answer changes, and the trace
                    // prints the desk's own words. The proposal is on the record either
                    // way.
                    if (!taken)
                        return OpResult.Fail(filed.Answer);
                }
                else if (HouseDiplomacy.MustAccept(view, filed, world.Relations.Config))
                {
                    // THE BEATEN CANNOT REFUSE, the player included (ruling 1): the
                    // inbox answers for him and the line says so.
                    world.Diplomacy.Settle(world, filed, true,
                        HouseDiplomacy.ReasonWeCouldNotRefuse, day);
                    to.Touch();
                }
            }
            return OpResult.Success;
        }

        /// <summary>
        /// THE SIT-DOWN (EPIC 42, DIPL-008). The same proposal, carried by a lieutenant
        /// in person: filed in transit, and a SitDown job put on his crew's book to the
        /// other house's front. Delivered on arrival by <see cref="Deliver"/>, with
        /// his Streetwise moving their desk's tests. The Don never goes.
        /// </summary>
        public static OpResult SendToSitDown(Underworld world, House from, Proposal proposal,
            int envoyId, System.Action<Job> place = null)
        {
            if (world?.Diplomacy == null || from?.Runner == null || proposal == null)
                return OpResult.Fail(HouseDiplomacy.ReasonNothingToSay);
            var envoy = from.Roster?.Find(envoyId);
            if (envoy == null || envoy.Gone || envoy.Status != CharacterStatus.Active)
                return OpResult.Fail(HouseDiplomacy.ReasonNoEnvoy);
            if (envoy.Rank == Rank.Boss || envoy.Id == from.Roster.BossId)
                return OpResult.Fail(HouseDiplomacy.ReasonTheDonStaysHome);
            var crew = CrewOf(from.Roster, envoy.Id);
            if (crew == null)
                return OpResult.Fail(HouseDiplomacy.ReasonNoEnvoy);
            var to = world.Of(proposal.To);
            if (to == null || to.Finished || to.GangId == from.GangId)
                return OpResult.Fail(HouseDiplomacy.ReasonNobodyToAskWord);

            proposal.Envoy = envoy.Id;
            proposal.EnvoyHalfSteps = envoy.GetHalfSteps(CharacterAttribute.Streetwise);
            proposal.InTransit = true;
            var filed = Propose(world, from, proposal, null);
            if (!filed.Ok)
                return filed;
            var carried = world.Diplomacy.Find(proposal.Id);
            if (carried != null)
            {
                carried.Envoy = proposal.Envoy;
                carried.EnvoyHalfSteps = proposal.EnvoyHalfSteps;
                carried.InTransit = true;
            }

            var job = new Job
            {
                Type = OrderType.SitDown,
                CrewId = crew.Id,
                GangId = from.GangId,
                Men = 1,
                TargetBusinessId = to.Front.IsValid ? to.Front.Value : "",
                TargetLabel = "sit-down with " + Gangs.GangCatalog.Names[to.GangId],
                ProposalId = proposal.Id,
            };
            // The scene edge resolves their door into a place to walk to; a bench
            // with no city hands nothing and the job resolves on paper.
            place?.Invoke(job);
            var issued = world.Issue(job);
            if (!issued.Ok && carried != null)
            {
                carried.Status = ProposalStatus.Refused;
                carried.Answer = issued.Reason;
                carried.InTransit = false;
            }
            return issued;
        }

        /// <summary>
        /// THE ENVOY STANDS AT THEIR DOOR. A mind answers at the desk with his margin;
        /// the player's inbox holds it, with the ambush open to him (DIPL-010).
        /// </summary>
        public static OpResult Deliver(Underworld world, int proposalId,
            System.Func<House, HouseView> look)
        {
            var proposal = world?.Diplomacy?.Find(proposalId);
            if (proposal == null || !proposal.Open)
                return OpResult.Fail(HouseDiplomacy.ReasonNoSuchProposal);
            proposal.InTransit = false;
            var to = world.Of(proposal.To);
            if (to == null)
                return OpResult.Fail(HouseDiplomacy.ReasonNobodyToAskWord);
            var day = to.Runner.Campaign.Day;
            if (look == null)
                return OpResult.Success;
            var view = look(to);
            if (!to.IsPlayer)
            {
                var answer = HouseDiplomacy.Answer(view, proposal, world.Diplomacy.Config,
                    world.Relations.Config);
                world.Diplomacy.Settle(world, proposal, answer.Accepted, answer.Reason, day);
                to.Touch();
            }
            else if (HouseDiplomacy.MustAccept(view, proposal, world.Relations.Config))
            {
                world.Diplomacy.Settle(world, proposal, true,
                    HouseDiplomacy.ReasonWeCouldNotRefuse, day);
                to.Touch();
            }
            return OpResult.Success;
        }

        /// <summary>
        /// THE AMBUSH (DIPL-008): the host kills the envoy at his own door. On paper he
        /// dies where he stands; the sender is owed a killing and a betrayal, and every
        /// house hears it. Only a proposal that has arrived can be ambushed, and only
        /// by the house it was carried to. A mind never does this.
        /// </summary>
        public static OpResult Ambush(Underworld world, House host, int proposalId)
        {
            var proposal = world?.Diplomacy?.Find(proposalId);
            if (proposal == null || !proposal.Open || host == null || proposal.To != host.GangId)
                return OpResult.Fail(HouseDiplomacy.ReasonNoSuchProposal);
            if (proposal.Envoy < 0)
                return OpResult.Fail(HouseDiplomacy.ReasonNoEnvoy);
            if (proposal.InTransit)
                return OpResult.Fail(HouseDiplomacy.ReasonStillOnTheRoad);
            var sender = world.Of(proposal.From);
            if (sender == null)
                return OpResult.Fail(HouseDiplomacy.ReasonNobodyToAskWord);
            var envoy = sender.Roster?.Find(proposal.Envoy);
            if (envoy == null || envoy.Gone)
                return OpResult.Fail(HouseDiplomacy.ReasonNoEnvoy);

            var day = host.Runner.Campaign.Day;
            Kill(sender, proposal.Envoy);
            sender.Runner.NoteLoss(host.GangId);
            world.Relations.Note(sender.GangId, host.GangId, GrievanceKind.ManKilled, day);
            world.Relations.Note(sender.GangId, host.GangId, GrievanceKind.SitDownBetrayed, day);
            world.Diplomacy.Settle(world, proposal, false, HouseDiplomacy.ReasonAmbushed, day);
            HouseDiplomacy.PrintEverywhere(world,
                Gangs.GangCatalog.Names[host.GangId] + " shot " +
                Gangs.GangCatalog.Names[sender.GangId] + "'s man at their own door", day);
            host.Touch();
            return OpResult.Success;
        }

        static Crew CrewOf(Roster roster, int characterId)
        {
            for (var i = 0; roster != null && i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                if (crew.LieutenantId == characterId || crew.HoodIds.Contains(characterId))
                    return crew;
            }
            return null;
        }

        /// <summary>An answer from the inbox - the player's ANSWER row, or a mind's
        /// Reply intent. Yes carries the effect; no prints the refusal.</summary>
        public static OpResult Reply(Underworld world, House to, int proposalId,
            bool accept, System.Func<House, HouseView> look = null)
        {
            if (world?.Diplomacy == null || to?.Runner == null)
                return OpResult.Fail(HouseDiplomacy.ReasonNoSuchProposal);
            var proposal = world.Diplomacy.Find(proposalId);
            if (proposal == null || !proposal.Open || proposal.To != to.GangId)
                return OpResult.Fail(HouseDiplomacy.ReasonNoSuchProposal);
            // A proposal carried in person is not answered before the envoy stands at
            // the door - the inbox shows it on the road and offers no key for it.
            if (proposal.InTransit)
                return OpResult.Fail(HouseDiplomacy.ReasonStillOnTheRoad);
            var note = "";
            if (!accept && look != null &&
                HouseDiplomacy.MustAccept(look(to), proposal, world.Relations.Config))
            {
                accept = true;
                note = HouseDiplomacy.ReasonWeCouldNotRefuse;
            }
            var carried = world.Diplomacy.Settle(world, proposal, accept, note,
                to.Runner.Campaign.Day);
            to.Touch();
            world.Of(proposal.From)?.Touch();
            // A yes the book could not carry - the money not there, a bill lapsed -
            // answers with the record's own words, so the caller is not told a deal
            // was made when the file says otherwise.
            return accept && !carried ? OpResult.Fail(proposal.Answer) : OpResult.Success;
        }

        // ------------------------------------------------------------------- EPIC 40

        /// <summary>
        /// A FLAT ON THE HOUSE'S DEED (PRE-001) - the same Apartments.Buy the blueprint
        /// form calls, with the price paid out of this house's own safe first. The
        /// unit is the caller's: the scene edge picks it for a mind, the form for the
        /// player.
        /// </summary>
        public static OpResult BuyFlat(House house, Property.ApartmentUnitId unit, int day)
        {
            if (house?.Runner == null || !unit.IsValid)
                return OpResult.Fail(UI.LedgerText.ReasonNoSuchMember);
            if (Property.Apartments.OwnerOf(unit) >= 0)
                return OpResult.Fail("somebody holds the lease");
            var refusal = BalanceMath.TryPurchase(
                house.Runner.Accounts, EconomyPrices.Apartment);
            if (refusal != null)
                return OpResult.Fail(refusal);
            Property.Apartments.Buy(unit, house.GangId, day);
            house.Touch();
            return OpResult.Success;
        }

        /// <summary>A held room turned to a use, the fit-out charged when the room has
        /// not been paid for that use - the form's own rule.</summary>
        public static OpResult FitOut(House house, Property.ApartmentUnitId unit,
            Property.UnitRole role)
        {
            if (house?.Runner == null || !Property.Apartments.IsOurs(unit, house.GangId))
                return OpResult.Fail("not our room");
            if (!Property.Apartments.TryGet(unit, out var record))
                return OpResult.Fail("not our room");
            if (role != Property.UnitRole.Empty && record.PaidRole != role)
            {
                var refusal = BalanceMath.TryPurchase(
                    house.Runner.Accounts, Property.UnitRoles.Of(role).FitOut);
                if (refusal != null)
                    return OpResult.Fail(refusal);
                Property.Apartments.SetRole(unit, role, true);
            }
            else
            {
                Property.Apartments.SetRole(unit, role, false);
            }
            house.Touch();
            return OpResult.Success;
        }

        /// <summary>A man put in a held room, through RosterOps.SetKeeper's own rule.</summary>
        public static OpResult SetKeeper(House house, Property.ApartmentUnitId unit,
            int keeperId)
        {
            if (house?.Roster == null || !Property.Apartments.IsOurs(unit, house.GangId))
                return OpResult.Fail("not our room");
            if (!Property.Apartments.TryGet(unit, out var record))
                return OpResult.Fail("not our room");
            if (record.KeeperId >= 0 && record.KeeperId != keeperId)
                RosterOps.ClearKeeper(house.Roster, record.KeeperId);
            if (keeperId >= 0)
            {
                var kept = RosterOps.SetKeeper(house.Roster, keeperId);
                if (!kept.Ok)
                    return kept;
            }
            Property.Apartments.SetKeeper(unit, keeperId);
            Settle(house);
            return OpResult.Success;
        }

        /// <summary>
        /// THE MAN WHO KNOWS THE COLOMBIAN, SIGNED (CONN-001). The twin of Retain with
        /// a crew named: his signing money out of this house's safe first, then onto
        /// the books as a hood in the speaker's crew - gear and men reach a crew only
        /// through its lieutenant. A man already ours (OUR MAN) pays nothing and keeps
        /// his crew. The connection paper is written, and the city's count of signings
        /// moves so Pablo's turn can come round.
        /// </summary>
        public static OpResult Sign(House house, Underworld world, EventCard card,
            int crewId, int day)
        {
            if (house?.Roster == null || house.Runner == null || card == null)
                return OpResult.Fail(UI.LedgerText.ReasonNoSuchMember);
            var connection = house.Runner.Connection;
            var roster = house.Roster;

            Character man;
            if (card.ManId >= 0)
            {
                man = roster.Find(card.ManId);
                if (man == null || man.Gone)
                    return OpResult.Fail(UI.LedgerText.ReasonNoSuchMember);
                if (roster.CrewOf(man.Id) == null && crewId >= 0)
                    RosterOps.AssignToCrew(roster, man.Id, crewId);
            }
            else
            {
                if (card.Ad?.Man == null)
                    return OpResult.Fail(UI.LedgerText.ReasonNoSuchMember);
                if (crewId < 0 || roster.FindCrew(crewId) == null)
                    return OpResult.Fail(UI.LedgerText.ReasonNoSuchCrew);
                var refusal = BalanceMath.TryPurchase(house.Runner.Accounts, card.Ad.Down);
                if (refusal != null)
                    return OpResult.Fail(refusal);
                man = card.Ad.Man;
                man.Id = roster.NextCharacterId();
                man.Rank = Rank.Hood;
                man.WageAsked = card.Ad.Daily;
                roster.Members.Add(man);
                Career.Joined(man, roster.Day, "the man who knows the Colombian");
                var crewed = RosterOps.AssignToCrew(roster, man.Id, crewId);
                if (!crewed.Ok)
                    RosterOps.AssignToPool(roster, man.Id);
            }

            if (connection.Stage == ConnectionStage.None || !connection.HasMan)
                connection.Signed(man.Id, card.Line, card.Trade, day);
            else
                connection.Replaced(man.Id, card.Trade);
            world?.ConnectionManSigned(man.Id, day);
            Settle(house);
            return OpResult.Success;
        }

        /// <summary>The supplier's terms taken: the line is the house's (CONN-004).</summary>
        public static OpResult AcceptTerms(House house, Underworld world, int day)
        {
            var connection = house?.Runner?.Connection;
            if (connection == null)
                return OpResult.Fail(UI.LedgerText.ReasonFinanceUnavailable);
            if (connection.Stage != ConnectionStage.Tested)
                return OpResult.Fail("there are no terms on the table");
            var direct = world != null && world.DirectManId >= 0 &&
                         world.DirectManId == connection.ManId;
            connection.Accepted(direct ? SupplierGrade.Direct : SupplierGrade.Broker, day);
            house.Touch();
            return OpResult.Success;
        }

        /// <summary>SELL TO HIS BUYER (CONN-004). Answers what was made in the reason.</summary>
        public static OpResult Sell(House house, int day, out int money, out int sold)
        {
            money = 0;
            sold = 0;
            var connection = house?.Runner?.Connection;
            if (connection == null)
                return OpResult.Fail(UI.LedgerText.ReasonFinanceUnavailable);
            if (connection.Kilos <= 0)
                return OpResult.Fail("there is nothing in the room");
            money = connection.Sell(house.Runner.Accounts, day, out sold);
            if (sold <= 0)
                return OpResult.Fail("the buyer has taken all he will this week");
            house.Runner.Incidents.Add(new Incident(-1, "", IncidentKind.KilosSold, day, "",
                0, sold + (sold == 1 ? " kilo" : " kilos") + " sold to the buyer for " +
                   UI.LedgerText.Cash(money) + "."));
            house.Touch();
            return OpResult.Success;
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
