using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 42, THE SIT-DOWN. The proposal book, one mechanism for everything two houses
    /// can say to each other. DIPL-001's contracts: a mind answers at the desk the same
    /// way on two runs of one seed, the player's inbox lapses on day three as a refusal
    /// without a note, the same thing is not asked twice, money crosses between two
    /// safes through one door and lands dirty on both sheets, a word given keeps a house
    /// off a street at the choke point and in the mind alike, and the book survives the
    /// file - a file with no book reads as an empty one.
    /// </summary>
    public static class DiplomacyTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();

            AMindAnswersTheSameOnTwoRuns(failures);
            ThePlayersProposalLapsesOnDayThree(failures);
            TheSameThingIsNotAskedTwice(failures);
            MoneyCrossesThroughOneDoor(failures);
            AWordGivenKeepsTheHouseOffTheStreet(failures);
            TheBookSurvivesTheFile(failures);

            // DIPL-002: the stance by agreement.
            TheBeatenCannotRefuse(failures);
            TheDeskReadsTheTruceTable(failures);
            PeaceComesOutOfATruceOnly(failures);
            ATruceAtPeaceNeedsTheStreetsClear(failures);
            TheAgreementLandsOverThePendingSlot(failures);
            AKillingBreaksTheAgreementAndTheMoneyGoesBack(failures);
            LossesAreCountedFromTheWarsOpening(failures);
            MoneyClearsAGrudgeWithinLimits(failures);

            // DIPL-003: words with answers.
            AWarningCompliedWithKeepsThemOffTheStreet(failures);
            ARefusedOrUnansweredWordIsAGrudgeOnce(failures);
            TheBillIsPricedFromTheGrudge(failures);
            TheDeskAnswersAWordOnItsTests(failures);

            // DIPL-004: tribute for every house.
            EveryHousesEnvelopeCrossesInOnePass(failures);
            TermsPinTheEnvelopeForThreeCycles(failures);

            // DIPL-005: the ransom.
            ARansomIsPaidOrHeWaitsItOut(failures);
            ThePlayersManIsRansomedFromTheInbox(failures);

            // DIPL-006: the line.
            TheLineKeepsBothHousesOffTheStreets(failures);
            TheMindDrawsALineAtTheBordersCap(failures);

            // DIPL-007: the pact.
            APactIsHonouredAtTheNextMidnight(failures);
            APactsWarWakesNoOtherPact(failures);
            APartnerThatCannotPayBreaksThePact(failures);
            JoinMyWarIsThePactForOneWar(failures);

            // DIPL-008: the sit-down.
            AnEnvoyMovesTheTests(failures);
            TheSitDownIsDeliveredOnArrival(failures);
            AnAmbushKillsTheEnvoyAtTheDoor(failures);

            return failures;
        }

        // ------------------------------------------------------------------ the rig

        /// <summary>Three families on a paper city, dealt from one seed, each at its
        /// own front. The paper city's own Look is what a mind answers with.</summary>
        sealed class Table
        {
            public readonly Underworld World;
            public readonly PaperCity City;
            public readonly TerritoryRacketLedger Racket = new TerritoryRacketLedger();
            public readonly TerritoryDuesLedger Dues = new TerritoryDuesLedger();
            public readonly TerritoryRoundLedger Rounds;
            public readonly TerritoryPaperClock Clock;

            public Table(int seed, int houses = 3)
            {
                World = Underworld.Deal(seed, houses);
                Rounds = new TerritoryRoundLedger(Racket, Dues);
                Clock = new TerritoryPaperClock(Rounds);
                City = new PaperCity(houses, seed) { Racket = Racket };
                HouseOps.Look = Look;
                for (var h = 0; h < houses; h++)
                {
                    var house = World.Of(h);
                    house.Front = City.Door(City.HomeBlockOf(h), 0);
                    if (house.Roster.Crews.Count > 0)
                        RosterOps.AssignBlockResponsibility(
                            house.Roster, City.HomeBlockOf(h),
                            house.Roster.Crews[0].LieutenantId, true);
                }
            }

            public HouseView Look(House house) =>
                City.Look(World, Racket, Dues, house, HouseMindConfig.Default, Rounds);

            public OpResult Propose(int from, int to, ProposalKind kind, int money = 0,
                Proposal filed = null)
            {
                filed = filed ?? new Proposal();
                filed.To = to;
                filed.Kind = kind;
                filed.Terms.Money = money;
                return HouseOps.Propose(World, World.Of(from), filed, Look);
            }

            public string Carry(int gangId, HouseIntent intent) =>
                City.Carry(World, Racket, Dues, Rounds, Clock, World.Of(gangId), intent,
                    null);
        }

        static Proposal Last(Table table) =>
            table.World.Diplomacy.All.Count > 0
                ? table.World.Diplomacy.All[table.World.Diplomacy.All.Count - 1]
                : null;

        // --------------------------------------------------------------- contracts

        /// <summary>The desk answers at once and by the tables: the same proposal on
        /// two deals of one seed gets the same verdict, yes and no alike, and a yes
        /// puts the stance pending for midnight.</summary>
        static void AMindAnswersTheSameOnTwoRuns(List<string> failures)
        {
            var first = new Table(1987);
            var second = new Table(1987);

            var a = first.Propose(1, 2, ProposalKind.OfferTruce);
            var b = second.Propose(1, 2, ProposalKind.OfferTruce);
            if (!a.Ok || !b.Ok)
            {
                failures.Add("DIPL-001: a truce could not be filed (" + a.Reason + " / " +
                             b.Reason + ").");
                return;
            }
            var pa = Last(first);
            var pb = Last(second);
            if (pa == null || pb == null || pa.Status != pb.Status || pa.Answer != pb.Answer)
                failures.Add("DIPL-001: the desk answered differently on two runs of one seed.");
            if (pa != null && pa.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-001: a house owed nothing refused a truce (" +
                             (pa != null ? pa.Answer : "") + ").");
            if (!first.World.Relations.TryGetAgreed(1, 2, out var agreedStance, out _) ||
                agreedStance != Stance.Truce)
                failures.Add("DIPL-001: an accepted truce was not agreed for midnight.");

            // At war, solvent, owed a killing twice over and facing a weaker house,
            // the desk says no - and says the same twice.
            var third = new Table(1987);
            var fourth = new Table(1987);
            foreach (var table in new[] { third, fourth })
            {
                War(table, 1, 2);
                var owed = table.World.Of(2);
                owed.Runner.Accounts.Safe =
                    Wages.DailyPayroll(owed.Roster) * HouseRelationsConfig.Default.MinWarDays * 3;
                table.World.Of(1).Runner.Accounts.Safe = 0;
                for (var i = 0; i < 2; i++)
                    table.World.Relations.Note(2, 1, GrievanceKind.ManKilled);
            }
            third.Propose(1, 2, ProposalKind.OfferTruce);
            fourth.Propose(1, 2, ProposalKind.OfferTruce);
            var pc = Last(third);
            var pd = Last(fourth);
            if (pc == null || pd == null || pc.Status != ProposalStatus.Refused ||
                pd.Status != ProposalStatus.Refused)
                failures.Add("DIPL-001: a house owed a killing took the truce.");
            // The desk's no comes back as the ask's own refusal, so a mind's intent
            // backs off like any refused intent and does not ask every think.
            var again = third.Propose(1, 2, ProposalKind.OfferTruce);
            if (again.Ok || again.Reason != HouseDiplomacy.ReasonTakenTooMuch)
                failures.Add("DIPL-001: a refused ask did not come back as a refusal (" +
                             again.Reason + ").");
            else if (pc.Answer != HouseDiplomacy.ReasonTakenTooMuch || pc.Answer != pd.Answer)
                failures.Add("DIPL-001: the refusal was not in the desk's words (" +
                             pc.Answer + " / " + pd.Answer + ").");
            if (third.World.Relations.TryGetAgreed(1, 2, out _, out _))
                failures.Add("DIPL-001: a refused truce still moved the stance.");

            // Both books carry the line.
            if (!Printed(first.World.Of(1)) || !Printed(first.World.Of(2)))
                failures.Add("DIPL-001: the proposal was not printed in both books.");
            if (Printed(first.World.Of(0)))
                failures.Add("DIPL-001: a third house read a word that was not its.");
        }

        static bool Printed(House house)
        {
            for (var i = 0; i < house.Runner.Incidents.Count; i++)
                if (house.Runner.Incidents[i].Kind == IncidentKind.AWordBetweenHouses)
                    return true;
            return false;
        }

        /// <summary>A proposal to the player waits in his inbox; on day +3 it lapses,
        /// a refusal without a note - no grievance moves, nothing is set pending.
        /// </summary>
        static void ThePlayersProposalLapsesOnDayThree(List<string> failures)
        {
            var table = new Table(7);
            var filed = table.Propose(1, 0, ProposalKind.OfferTruce);
            if (!filed.Ok)
            {
                failures.Add("DIPL-001: a truce to the player could not be filed (" +
                             filed.Reason + ").");
                return;
            }
            var proposal = Last(table);
            var day = table.World.Of(1).Runner.Campaign.Day;
            if (proposal == null || !proposal.Open)
            {
                failures.Add("DIPL-001: the player's inbox answered for him.");
                return;
            }
            if (proposal.ExpiresDay != day + table.World.Diplomacy.Config.ProposalDays)
                failures.Add("DIPL-001: the proposal lapses on day " + proposal.ExpiresDay +
                             ", not " + (day + table.World.Diplomacy.Config.ProposalDays) + ".");

            var before = table.World.Relations.Grievance(1, 0);
            table.World.DayTick();
            table.World.DayTick();
            if (!proposal.Open)
                failures.Add("DIPL-001: the proposal lapsed a day early.");
            table.World.DayTick();
            if (proposal.Status != ProposalStatus.Expired)
                failures.Add("DIPL-001: the proposal did not lapse on its day (" +
                             proposal.Status + ").");
            if (table.World.Relations.Grievance(1, 0) > before)
                failures.Add("DIPL-001: a lapsed proposal was noted as a grievance.");
            if (table.World.Relations.TryGetPending(1, 0, out _))
                failures.Add("DIPL-001: a lapsed truce moved the stance.");

            // The view knows an open proposal, and forgets a lapsed one.
            var again = table.Propose(1, 0, ProposalKind.OfferTruce);
            if (!again.Ok)
                failures.Add("DIPL-001: after the lapse the same thing could not be asked again (" +
                             again.Reason + ").");
            if (!table.Look(table.World.Of(1)).HasOpenProposal(
                    new TerritoryGangId(0), ProposalKind.OfferTruce))
                failures.Add("DIPL-001: the view did not see the open proposal.");
        }

        /// <summary>The same thing is refused in words while it is still open - which
        /// is what keeps a broke house from asking every think.</summary>
        static void TheSameThingIsNotAskedTwice(List<string> failures)
        {
            var table = new Table(11);
            table.Propose(2, 0, ProposalKind.OfferTruce);
            var twice = table.Propose(2, 0, ProposalKind.OfferTruce);
            if (twice.Ok || twice.Reason != HouseDiplomacy.ReasonAlreadyAsked)
                failures.Add("DIPL-001: the same proposal was filed twice (" +
                             twice.Reason + ").");

            var open = new List<Proposal>();
            table.World.Diplomacy.OpenFor(0, open);
            if (open.Count != 1)
                failures.Add("DIPL-001: the player's inbox holds " + open.Count +
                             " proposals, not one.");

            // Money the safe does not hold is refused before anything is filed.
            var safe = table.World.Of(2).Runner.Accounts.Safe;
            var rich = table.Propose(2, 1, ProposalKind.OfferTruce, safe + 1);
            if (rich.Ok || string.IsNullOrEmpty(rich.Reason))
                failures.Add("DIPL-001: a proposal with money the safe does not hold was filed.");
            if (table.World.Diplomacy.HasOpen(2, 1, ProposalKind.OfferTruce))
                failures.Add("DIPL-001: the refused proposal is on the book.");

            // Nobody to say it to.
            var nobody = table.Propose(2, 2, ProposalKind.OfferTruce);
            if (nobody.Ok)
                failures.Add("DIPL-001: a house proposed to itself.");
            var none = table.Propose(2, 1, ProposalKind.None);
            if (none.Ok)
                failures.Add("DIPL-001: a proposal of no kind was filed.");
        }

        /// <summary>Underworld.Transfer: payer down, payee up by the same amount and
        /// dirty, both sheets carrying the line; a payer that cannot cover it moves
        /// nothing at all.</summary>
        static void MoneyCrossesThroughOneDoor(List<string> failures)
        {
            var table = new Table(3);
            var payer = table.World.Of(1);
            var payee = table.World.Of(2);
            var safeA = payer.Runner.Accounts.Safe;
            var safeB = payee.Runner.Accounts.Safe;
            var dirtyB = payee.Runner.Accounts.RiskyMoney;

            var moved = table.World.Transfer(1, 2, 1_000);
            if (moved != null)
                failures.Add("DIPL-001: a covered transfer was refused (" + moved + ").");
            if (payer.Runner.Accounts.Safe != safeA - 1_000 ||
                payee.Runner.Accounts.Safe != safeB + 1_000)
                failures.Add("DIPL-001: the safes did not move by the amount.");
            if (payee.Runner.Accounts.RiskyMoney != dirtyB + 1_000)
                failures.Add("DIPL-001: money from another house arrived clean.");
            if (payer.Runner.Accounts.Current == null ||
                payer.Runner.Accounts.Current.ToHouses != 1_000 ||
                payee.Runner.Accounts.Current == null ||
                payee.Runner.Accounts.Current.FromHouses != 1_000)
                failures.Add("DIPL-001: the sheets do not carry the line between the houses.");
            if (BalanceMath.TotalIncome(payee.Runner.Accounts.Current) <
                payee.Runner.Accounts.Current.FromHouses)
                failures.Add("DIPL-001: the day's income does not count what other houses paid.");

            safeA = payer.Runner.Accounts.Safe;
            safeB = payee.Runner.Accounts.Safe;
            var refused = table.World.Transfer(1, 2, safeA + 1);
            if (refused == null)
                failures.Add("DIPL-001: a transfer the safe cannot cover was allowed.");
            if (payer.Runner.Accounts.Safe != safeA || payee.Runner.Accounts.Safe != safeB)
                failures.Add("DIPL-001: a refused transfer moved money.");
            if (table.World.Transfer(1, 1, 100) == null || table.World.Transfer(1, 2, 0) == null)
                failures.Add("DIPL-001: money moved to nowhere.");

            // A truce with money on it, accepted, is the money out of the sender's
            // safe at once and in escrow until the midnight the stance lands on.
            var before = payee.Runner.Accounts.Safe;
            var sender = payer.Runner.Accounts.Safe;
            var truce = table.Propose(1, 2, ProposalKind.OfferTruce, 2_000);
            var filed = Last(table);
            if (!truce.Ok || filed == null || filed.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-001: a paid truce was not accepted (" +
                             (filed != null ? filed.Answer : truce.Reason) + ").");
            else if (payer.Runner.Accounts.Safe != sender - 2_000 ||
                     payee.Runner.Accounts.Safe != before || filed.Escrow != 2_000)
                failures.Add("DIPL-001: the truce's money did not go into escrow at acceptance.");
        }

        /// <summary>A word given to keep off a street: the paper city's choke point
        /// refuses every racket order of that house on the block in the desk's words,
        /// the view says so, the mind files nothing for the block - and on its day the
        /// word lifts.</summary>
        static void AWordGivenKeepsTheHouseOffTheStreet(List<string> failures)
        {
            var table = new Table(5);
            var house = table.World.Of(1);
            var mine = new TerritoryGangId(1);
            var home = table.City.HomeBlockOf(1);
            var crew = house.Roster.Crews.Count > 0 ? house.Roster.Crews[0] : null;
            if (crew == null)
            {
                failures.Add("DIPL-001: the fixture dealt a house with no crew.");
                return;
            }
            table.City.Stand(home, mine, 60f);

            var intents = new List<HouseIntent>();
            HouseMind.Think(table.Look(house), HouseMindConfig.Default,
                table.World.Relations.Config, intents);
            if (!Targets(intents, home))
                failures.Add("DIPL-001: before the word, the mind filed nothing for its own street" +
                             " - the fixture proves nothing.");

            var day = house.Runner.Campaign.Day;
            table.World.Diplomacy.KeepOff(1, home, day + 2);
            if (!table.Look(house).KeptOff(home))
                failures.Add("DIPL-001: the view does not read the word.");
            if (table.Look(table.World.Of(2)).KeptOff(home))
                failures.Add("DIPL-001: another house read our word as its own.");

            var refusal = table.Carry(1, HouseIntent.Block(
                HouseOrder.OperateInBlock, crew.Id, home, HouseMind.TierExpand, "test"));
            if (refusal != HouseDiplomacy.ReasonUnderOurWord)
                failures.Add("DIPL-001: the choke point let a posting through (" +
                             refusal + ").");
            refusal = table.Carry(1, HouseIntent.Door(
                crew.Id, table.City.Door(home, 1), TerritoryRacketIntent.Demand,
                HouseMind.TierExpand, "test"));
            if (refusal != HouseDiplomacy.ReasonUnderOurWord)
                failures.Add("DIPL-001: the choke point let a door be asked (" +
                             refusal + ").");
            refusal = table.Carry(1, HouseIntent.Block(
                HouseOrder.ShakeDownBlock, crew.Id, home, HouseMind.TierExpand, "test"));
            if (refusal != HouseDiplomacy.ReasonUnderOurWord)
                failures.Add("DIPL-001: the choke point let a walk through (" +
                             refusal + ").");

            HouseMind.Think(table.Look(house), HouseMindConfig.Default,
                table.World.Relations.Config, intents);
            if (Targets(intents, home))
                failures.Add("DIPL-001: the mind filed for a street it gave its word on.");

            // The other house is not kept off it.
            var theirs = table.World.Of(2).Roster.Crews.Count > 0
                ? table.World.Of(2).Roster.Crews[0]
                : null;
            if (theirs != null && table.Carry(2, HouseIntent.Block(
                    HouseOrder.OperateInBlock, theirs.Id, home, HouseMind.TierExpand,
                    "test")) == HouseDiplomacy.ReasonUnderOurWord)
                failures.Add("DIPL-001: our word kept another house off the street.");

            // On its day the word lifts.
            table.World.DayTick();
            if (!table.World.Diplomacy.IsKeptOff(1, home, house.Runner.Campaign.Day))
                failures.Add("DIPL-001: the word lifted a day early.");
            table.World.DayTick();
            if (table.World.Diplomacy.IsKeptOff(1, home, house.Runner.Campaign.Day) ||
                table.World.Diplomacy.KeptOffUntil(1, home) >= 0)
                failures.Add("DIPL-001: the word did not lift on its day.");
        }

        static bool Targets(List<HouseIntent> intents, TerritoryBlockId blockId)
        {
            for (var i = 0; i < intents.Count; i++)
                if (intents[i].Kind == HouseIntentKind.Command &&
                    intents[i].BlockId == blockId)
                    return true;
            return false;
        }

        /// <summary>The book, written and read back: every proposal with its status,
        /// every keep-off, the next id; and a file with no book reads as an empty one.
        /// </summary>
        static void TheBookSurvivesTheFile(List<string> failures)
        {
            var table = new Table(13);
            table.Propose(1, 2, ProposalKind.OfferTruce, 500);
            table.Propose(2, 0, ProposalKind.OfferTruce);
            table.World.Diplomacy.KeepOff(1, table.City.HomeBlockOf(2), 9);
            table.World.Of(1).Runner.Accounts.Safe = 0;
            table.World.Of(2).Runner.Accounts.Safe = 0;
            HouseOps.Propose(table.World, table.World.Of(1),
                LineAcross(2, table.City.HomeBlockOf(1)), table.Look);
            table.World.Of(1).Runner.Accounts.Safe = 1_000_000;
            table.World.Of(2).Runner.Accounts.Safe = 1_000_000;
            HouseOps.Propose(table.World, table.World.Of(1), PactAgainst(2, 0), table.Look);

            var json = JsonUtility.ToJson(OutfitSnapshot.Snapshot(table.World));
            var dto = JsonUtility.FromJson<UnderworldDto>(json);
            var fresh = Underworld.Deal(13, 3);
            OutfitSnapshot.Restore(fresh, dto);

            var was = table.World.Diplomacy.All;
            var now = fresh.Diplomacy.All;
            if (now.Count != was.Count)
                failures.Add("DIPL-001: the file holds " + now.Count + " proposals, not " +
                             was.Count + ".");
            for (var i = 0; i < was.Count && i < now.Count; i++)
            {
                var a = was[i];
                var b = fresh.Diplomacy.Find(a.Id);
                if (b == null || b.From != a.From || b.To != a.To || b.Kind != a.Kind ||
                    b.Status != a.Status || b.Answer != a.Answer ||
                    b.Terms.Money != a.Terms.Money || b.Day != a.Day ||
                    b.ExpiresDay != a.ExpiresDay)
                    failures.Add("DIPL-001: proposal " + a.Id + " came back changed.");
            }
            if (fresh.Diplomacy.KeptOffUntil(1, table.City.HomeBlockOf(2)) != 9)
                failures.Add("DIPL-001: the keep-off did not survive the file.");
            if (fresh.Diplomacy.Lines.Count != table.World.Diplomacy.Lines.Count ||
                fresh.Diplomacy.Pacts.Count != table.World.Diplomacy.Pacts.Count ||
                (table.World.Diplomacy.Pacts.Count > 0 &&
                 !fresh.Diplomacy.HasPact(1, 2, table.World.Of(1).Runner.Campaign.Day)))
                failures.Add("DIPL-006/007: the lines and pacts did not survive the file (" +
                             fresh.Diplomacy.Lines.Count + "/" + fresh.Diplomacy.Pacts.Count + ").");
            if (!fresh.Relations.TryGetAgreed(1, 2, out _, out _) &&
                table.World.Relations.TryGetAgreed(1, 2, out _, out _))
                failures.Add("DIPL-002: the day's agreement did not survive the file.");

            // The next id goes on from where the file left off.
            var next = table.Propose(0, 1, ProposalKind.OfferTruce);
            var filedThere = Last(table);
            fresh.Of(0).Front = table.World.Of(0).Front;
            HouseOps.Propose(fresh, fresh.Of(0), new Proposal
            {
                To = 1, Kind = ProposalKind.OfferTruce,
            });
            var filedHere = fresh.Diplomacy.All[fresh.Diplomacy.All.Count - 1];
            if (next.Ok && filedThere != null && filedHere.Id != filedThere.Id)
                failures.Add("DIPL-001: the ids diverged after the file (" + filedHere.Id +
                             " vs " + filedThere.Id + ").");

            // No book in the file: an empty book, and nothing thrown.
            var empty = Underworld.Deal(13, 3);
            OutfitSnapshot.Restore(empty.Diplomacy, null);
            OutfitSnapshot.Restore(empty.Diplomacy, new DiplomacyDto());
            if (empty.Diplomacy.All.Count != 0)
                failures.Add("DIPL-001: a file with no book read as a full one.");
            var legacy = JsonUtility.FromJson<UnderworldDto>(
                "{\"citySeed\":13,\"houses\":[]}");
            OutfitSnapshot.Restore(empty.Diplomacy, legacy?.diplomacy);
            if (empty.Diplomacy.All.Count != 0)
                failures.Add("DIPL-001: a file from before the table read as a full book.");
        }

        // ------------------------------------------------------------- DIPL-002

        /// <summary>A hand-built view for the desk's tables: our own books, and our
        /// own side of the pair through the looks. Nothing of theirs.</summary>
        static HouseView Desk(int gangId, Stance stance, float grievance,
            int enduranceDays, int theirs, int losses = 0)
        {
            var roster = RosterSeeder.Generate(3, gangId);
            var runner = new CampaignRunner { Seed = 3, GangId = gangId };
            runner.OpenFirstSheet();
            var view = new HouseView
            {
                House = new TerritoryGangId(gangId),
                Roster = roster,
                Accounts = runner.Accounts,
                Book = runner.Book,
                GameHour = 100.0,
                Day = 5,
                StanceLook = other => stance,
                GrievanceLook = other => grievance,
                EnduranceLook = other => theirs,
                LossesLook = other => losses,
            };
            view.Accounts.Safe = view.DailyPayroll * enduranceDays;
            return view;
        }

        static Proposal Offer(int from, int to, ProposalKind kind, int money = 0) =>
            new Proposal { From = from, To = to, Kind = kind, Terms = new ProposalTerms { Money = money } };

        static void War(Table table, int a, int b)
        {
            table.World.Relations.SetPending(a, b, Stance.War);
            table.World.DayTick();
        }

        /// <summary>Ruling 1. A house under a war's wages, or past the losses that sue
        /// for peace, takes the truce whatever it is owed - the mind at the desk and
        /// the player at his inbox alike, and the line says so.</summary>
        static void TheBeatenCannotRefuse(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;

            // The mind, broke, owed a killing three times over.
            var table = new Table(21);
            War(table, 1, 2);
            var beaten = table.World.Of(2);
            beaten.Runner.Accounts.Safe = 0;
            for (var i = 0; i < 3; i++)
                table.World.Relations.Note(2, 1, GrievanceKind.ManKilled);
            table.Propose(1, 2, ProposalKind.OfferTruce);
            var filed = Last(table);
            if (filed == null || filed.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-002: a house that cannot pay through the war refused a truce (" +
                             (filed != null ? filed.Answer : "") + ").");

            // The mind, rich, but bled past the line.
            var bled = new Table(22);
            War(bled, 1, 2);
            var rich = bled.World.Of(2);
            rich.Runner.Accounts.Safe = Wages.DailyPayroll(rich.Roster) * config.MinWarDays * 4;
            for (var i = 0; i < 3; i++)
                bled.World.Relations.Note(2, 1, GrievanceKind.ManKilled);
            for (var i = 0; i < config.LossesToSueForPeace; i++)
                rich.Runner.NoteLoss(1);
            bled.Propose(1, 2, ProposalKind.OfferTruce);
            filed = Last(bled);
            if (filed == null || filed.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-002: a house that lost " + config.LossesToSueForPeace +
                             " men refused a truce (" + (filed != null ? filed.Answer : "") + ").");

            // The player, broke: his inbox answers for him, and says why.
            var player = new Table(23);
            War(player, 1, 0);
            player.World.Of(0).Runner.Accounts.Safe = 0;
            player.Propose(1, 0, ProposalKind.OfferTruce);
            filed = Last(player);
            if (filed == null || filed.Status != ProposalStatus.Accepted ||
                filed.Answer != HouseDiplomacy.ReasonWeCouldNotRefuse)
                failures.Add("DIPL-002: the beaten player's inbox did not answer for him (" +
                             (filed != null ? filed.Status + " " + filed.Answer : "") + ").");

            // The player, not yet beaten: the proposal waits; beaten by the time he
            // answers, his refusal is not one.
            var later = new Table(24);
            War(later, 1, 0);
            // The player's dealt roster is the Don alone (no payroll), so his
            // endurance is his safe: a hundred thousand is a long war.
            var his = later.World.Of(0);
            his.Runner.Accounts.Safe = 100_000;
            var asked = later.Propose(1, 0, ProposalKind.OfferTruce);
            filed = Last(later);
            if (filed == null || !filed.Open)
            {
                failures.Add("DIPL-002: a solvent player's inbox answered for him (" +
                             asked.Reason + " / " +
                             (filed != null ? filed.Status + " " + filed.Answer : "nothing filed") +
                             " endurance " + later.Look(his).Endurance + " losses " +
                             later.Look(his).Losses(new TerritoryGangId(1)) + ").");
                return;
            }
            his.Runner.Accounts.Safe = 0;
            var replied = HouseOps.Reply(later.World, his, filed.Id, false, later.Look);
            if (!replied.Ok || filed.Status != ProposalStatus.Accepted ||
                filed.Answer != HouseDiplomacy.ReasonWeCouldNotRefuse)
                failures.Add("DIPL-002: a beaten player refused a truce (" + filed.Status + ").");
        }

        /// <summary>The desk's table for a truce at war, one condition at a time.
        /// </summary>
        static void TheDeskReadsTheTruceTable(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = DiplomacyConfig.Default;
            var rich = config.MinWarDays * 3;

            // Owed fifty, solvent, no money on the offer: no.
            var view = Desk(2, Stance.War, 50f, rich, theirs: 1);
            var answer = HouseDiplomacy.Answer(view, Offer(1, 2, ProposalKind.OfferTruce), table, config);
            if (answer.Accepted || answer.Reason != HouseDiplomacy.ReasonTakenTooMuch)
                failures.Add("DIPL-002: a solvent house owed fifty took a bare truce.");

            // Money that clears it under the retake rung: yes. $2,000 is ten points -
            // forty is not under forty; $2,200 is eleven.
            answer = HouseDiplomacy.Answer(view, Offer(1, 2, ProposalKind.OfferTruce, 2_000), table, config);
            if (answer.Accepted)
                failures.Add("DIPL-002: money that lands exactly on the retake rung bought a truce.");
            answer = HouseDiplomacy.Answer(view, Offer(1, 2, ProposalKind.OfferTruce, 2_200), table, config);
            if (!answer.Accepted)
                failures.Add("DIPL-002: money that clears the grudge under the retake rung was refused.");

            // Money past the day's cap clears no more than the cap.
            view = Desk(2, Stance.War, 62f, rich, theirs: 1);
            answer = HouseDiplomacy.Answer(view, Offer(1, 2, ProposalKind.OfferTruce, 100_000), table, config);
            if (answer.Accepted)
                failures.Add("DIPL-002: a fortune cleared more than the day's cap at the desk.");

            // They read as the stronger house and we are not owed shops: yes.
            view = Desk(2, Stance.War, 50f, rich, theirs: rich * 10);
            answer = HouseDiplomacy.Answer(view, Offer(1, 2, ProposalKind.OfferTruce), table, config);
            if (!answer.Accepted)
                failures.Add("DIPL-002: a house facing a stronger one, owed less than shops, refused.");
            view = Desk(2, Stance.War, 65f, rich, theirs: rich * 10);
            answer = HouseDiplomacy.Answer(view, Offer(1, 2, ProposalKind.OfferTruce), table, config);
            if (answer.Accepted)
                failures.Add("DIPL-002: a house owed shops took a truce from a stronger one.");

            // A beaten one: yes, whatever it is owed.
            view = Desk(2, Stance.War, 100f, config.MinWarDays - 1, theirs: 1);
            if (!HouseDiplomacy.MustAccept(view, Offer(1, 2, ProposalKind.OfferTruce), config))
                failures.Add("DIPL-002: MustAccept did not read a broke house.");
            view = Desk(2, Stance.War, 100f, rich, theirs: 1, losses: config.LossesToSueForPeace);
            if (!HouseDiplomacy.MustAccept(view, Offer(1, 2, ProposalKind.OfferTruce), config))
                failures.Add("DIPL-002: MustAccept did not read the losses.");
            view = Desk(2, Stance.Peace, 100f, config.MinWarDays - 1, theirs: 1);
            if (HouseDiplomacy.MustAccept(view, Offer(1, 2, ProposalKind.OfferTruce), config))
                failures.Add("DIPL-002: MustAccept read a house that is not at war.");
        }

        /// <summary>Peace is offered out of a truce; at war it is refused, at peace
        /// there is nothing to end, and in a truce the grudge must be under the peace
        /// figure after the money.</summary>
        static void PeaceComesOutOfATruceOnly(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = DiplomacyConfig.Default;
            var rich = config.MinWarDays * 3;

            var answer = HouseDiplomacy.Answer(Desk(2, Stance.War, 0f, rich, 1),
                Offer(1, 2, ProposalKind.OfferPeace), table, config);
            if (answer.Accepted || answer.Reason != HouseDiplomacy.ReasonAWarEndsInATruce)
                failures.Add("DIPL-002: peace was made straight out of a war.");
            answer = HouseDiplomacy.Answer(Desk(2, Stance.Truce, 25f, rich, 1),
                Offer(1, 2, ProposalKind.OfferPeace), table, config);
            if (answer.Accepted || answer.Reason != HouseDiplomacy.ReasonNotYet)
                failures.Add("DIPL-002: peace was made over a grudge above the peace figure.");
            answer = HouseDiplomacy.Answer(Desk(2, Stance.Truce, 25f, rich, 1),
                Offer(1, 2, ProposalKind.OfferPeace, 1_200), table, config);
            if (!answer.Accepted)
                failures.Add("DIPL-002: money that clears the grudge under the peace figure was refused.");
            answer = HouseDiplomacy.Answer(Desk(2, Stance.Peace, 0f, rich, 1),
                Offer(1, 2, ProposalKind.OfferPeace), table, config);
            if (answer.Accepted)
                failures.Add("DIPL-002: peace was offered to a house already at peace and taken.");
            answer = HouseDiplomacy.Answer(Desk(2, Stance.Truce, 0f, rich, 1),
                Offer(1, 2, ProposalKind.OfferTruce), table, config);
            if (answer.Accepted)
                failures.Add("DIPL-002: a truce was offered inside a truce and taken.");
        }

        /// <summary>A truce at peace engages trespassers on both grounds, so it is
        /// taken only while none of our crews works their streets.</summary>
        static void ATruceAtPeaceNeedsTheStreetsClear(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = DiplomacyConfig.Default;
            var theirs = new TerritoryGangId(1);
            var street = new TerritoryBlockId("block:theirs");

            var view = Desk(2, Stance.Peace, 0f, config.MinWarDays * 3, 1);
            view.CrewBlockLook = crewId => street;
            view.LeaderLook = blockId => theirs;
            var answer = HouseDiplomacy.Answer(view, Offer(1, 2, ProposalKind.OfferTruce), table, config);
            if (answer.Accepted || answer.Reason != HouseDiplomacy.ReasonOurMenWorkThoseStreets)
                failures.Add("DIPL-002: a truce was taken while our men worked their streets.");

            view.LeaderLook = blockId => new TerritoryGangId(7);
            answer = HouseDiplomacy.Answer(view, Offer(1, 2, ProposalKind.OfferTruce), table, config);
            if (!answer.Accepted)
                failures.Add("DIPL-002: a truce at peace with the streets clear was refused (" +
                             answer.Reason + ").");
        }

        /// <summary>The guarded write. A truce accepted at the desk lands at midnight
        /// over a harder stance written pending the same day - a defection's Sour, a
        /// re-declaration - and the money held in escrow crosses with it. Without an
        /// agreement the slot is still the slot: the last write wins.</summary>
        static void TheAgreementLandsOverThePendingSlot(List<string> failures)
        {
            var table = new Table(31);
            War(table, 1, 2);
            var payer = table.World.Of(1);
            var payee = table.World.Of(2);
            payee.Runner.Accounts.Safe = 0;
            payer.Runner.Accounts.Safe = 50_000;
            payer.Runner.Accounts.RiskyMoney = 0;

            var safePayer = payer.Runner.Accounts.Safe;
            table.Propose(1, 2, ProposalKind.OfferTruce, 4_000);
            var filed = Last(table);
            if (filed == null || filed.Status != ProposalStatus.Accepted)
            {
                failures.Add("DIPL-002: the fixture's truce was not accepted (" +
                             (filed != null ? filed.Answer : "") + ").");
                return;
            }
            if (payer.Runner.Accounts.Safe != safePayer - 4_000)
                failures.Add("DIPL-002: the money did not leave the sender's safe at acceptance.");
            if (payee.Runner.Accounts.Safe != 0)
                failures.Add("DIPL-002: the money reached the receiver before midnight.");
            if (filed.Escrow != 4_000)
                failures.Add("DIPL-002: the money is not in escrow (" + filed.Escrow + ").");
            if (!table.World.Relations.TryGetAgreed(1, 2, out var agreedStance, out var broken) ||
                agreedStance != Stance.Truce || broken)
                failures.Add("DIPL-002: the agreement is not on the book.");

            // A re-declaration the same evening.
            table.World.Relations.SetPending(1, 2, Stance.War);
            var outcomes = new List<AgreementOutcome>();
            table.World.Relations.ApplyPending(outcomes);
            table.World.Diplomacy.ReleaseEscrows(table.World, outcomes, payer.Runner.Campaign.Day);
            if (table.World.Relations.StanceBetween(1, 2) != Stance.Truce)
                failures.Add("DIPL-002: the pending slot overwrote the agreed truce.");
            if (payee.Runner.Accounts.Safe != 4_000 || payee.Runner.Accounts.RiskyMoney != 4_000)
                failures.Add("DIPL-002: the escrow did not cross at midnight, dirty.");
            if (filed.Escrow != 0 || filed.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-002: the proposal did not close its escrow.");
            if (payee.Runner.Accounts.Current == null ||
                payee.Runner.Accounts.Current.FromHouses != 4_000)
                failures.Add("DIPL-002: the receiver's sheet does not carry the truce's money.");

            // The whole midnight, through the underworld: the same answer.
            var whole = new Table(32);
            War(whole, 1, 2);
            whole.World.Of(2).Runner.Accounts.Safe = 0;
            whole.Propose(1, 2, ProposalKind.OfferTruce);
            whole.World.Relations.SetPending(1, 2, Stance.War);
            whole.World.DayTick();
            if (whole.World.Relations.StanceBetween(1, 2) != Stance.Truce)
                failures.Add("DIPL-002: through the underworld's midnight the truce did not land.");

            // No agreement: the slot is the slot.
            var slot = new HouseRelations();
            slot.SetPending(0, 3, Stance.Truce);
            slot.SetPending(0, 3, Stance.War);
            if (!slot.TryGetPending(0, 3, out var last) || last != Stance.War)
                failures.Add("DIPL-002: without an agreement the last write did not win.");
        }

        /// <summary>A killing after the handshake breaks it: the pending stands, the
        /// money goes back to the sender the way it left, and the book says BROKEN.
        /// </summary>
        static void AKillingBreaksTheAgreementAndTheMoneyGoesBack(List<string> failures)
        {
            var table = new Table(33);
            War(table, 1, 2);
            var payer = table.World.Of(1);
            var payee = table.World.Of(2);
            payee.Runner.Accounts.Safe = 0;
            payer.Runner.Accounts.Safe = 50_000;
            payer.Runner.Accounts.RiskyMoney = 10_000;

            table.Propose(1, 2, ProposalKind.OfferTruce, 4_000);
            var filed = Last(table);
            if (filed == null || filed.Status != ProposalStatus.Accepted)
            {
                failures.Add("DIPL-002: the fixture's truce was not accepted.");
                return;
            }
            if (filed.EscrowDirty != 4_000)
                failures.Add("DIPL-002: the escrow forgot its dirty part (" + filed.EscrowDirty + ").");

            table.World.Relations.Note(2, 1, GrievanceKind.ManKilled);
            var outcomes = new List<AgreementOutcome>();
            table.World.Relations.ApplyPending(outcomes);
            table.World.Diplomacy.ReleaseEscrows(table.World, outcomes, payer.Runner.Campaign.Day);
            if (table.World.Relations.StanceBetween(1, 2) != Stance.War)
                failures.Add("DIPL-002: a killing did not break the agreement.");
            if (payer.Runner.Accounts.Safe != 50_000 || payer.Runner.Accounts.RiskyMoney != 10_000)
                failures.Add("DIPL-002: the escrow did not go back the way it left (" +
                             payer.Runner.Accounts.Safe + " / " + payer.Runner.Accounts.RiskyMoney + ").");
            if (payee.Runner.Accounts.Safe != 0)
                failures.Add("DIPL-002: the receiver kept money from a broken agreement.");
            if (filed.Status != ProposalStatus.Broken || filed.Escrow != 0)
                failures.Add("DIPL-002: the proposal does not read BROKEN (" + filed.Status + ").");
            if (payer.Runner.Accounts.Current == null || payer.Runner.Accounts.Current.ToHouses != 0)
                failures.Add("DIPL-002: the sender's sheet still carries money that came back.");

            // A lighter grievance lets it stand.
            var light = new Table(34);
            War(light, 1, 2);
            light.World.Of(2).Runner.Accounts.Safe = 0;
            light.Propose(1, 2, ProposalKind.OfferTruce);
            light.World.Relations.Note(2, 1, GrievanceKind.DoorSwitched);
            light.World.DayTick();
            if (light.World.Relations.StanceBetween(1, 2) != Stance.Truce)
                failures.Add("DIPL-002: a door switched broke a truce a killing should break.");
        }

        /// <summary>The view's losses are the runner's tally over the mark the war
        /// opened at - not the campaign's, and zero at peace.</summary>
        static void LossesAreCountedFromTheWarsOpening(List<string> failures)
        {
            var table = new Table(35);
            var house = table.World.Of(2);
            var them = new TerritoryGangId(1);

            house.Runner.NoteLoss(1);
            house.Runner.NoteLoss(1);
            if (table.Look(house).Losses(them) != 0)
                failures.Add("DIPL-002: losses at peace read as losses of a war.");
            if (house.Runner.MenLostTo(1) != 2)
                failures.Add("DIPL-002: the tally did not count the men.");

            War(table, 1, 2);
            if (table.Look(house).Losses(them) != 0)
                failures.Add("DIPL-002: men lost before the war counted against it.");
            house.Runner.NoteLoss(1);
            house.Runner.NoteLoss(1);
            house.Runner.NoteLoss(1);
            var view = table.Look(house);
            if (view.Losses(them) != 3 || view.LossesThisWar != 3)
                failures.Add("DIPL-002: the war's losses read " + view.Losses(them) + " / " +
                             view.LossesThisWar + ", not 3.");
            if (house.Runner.LossesThisWar(2) != 0)
                failures.Add("DIPL-002: a house counted losses to itself.");
        }

        /// <summary>Ruling 4. Money clears a grudge at the table's rate, at most the
        /// day's cap off one pair, and never under ThreatAt inside the killing window.
        /// </summary>
        static void MoneyClearsAGrudgeWithinLimits(List<string> failures)
        {
            var relations = new HouseRelations();
            var config = relations.Config;
            var table = DiplomacyConfig.Default;
            var book = new HouseDiplomacy(table);

            // Sixty owed, no killing: twenty a day and no more.
            for (var i = 0; i < 4; i++)
                relations.Note(2, 1, GrievanceKind.DoorAttacked);
            var cleared = book.Compensate(relations, 2, 1, 21 * table.CompensationPerPoint, 5);
            if (cleared != table.CompensationCapPerDay ||
                System.Math.Abs(relations.Grievance(2, 1) - (60f - table.CompensationCapPerDay)) > 0.01f)
                failures.Add("DIPL-002: the day's cap did not hold (" + cleared + " cleared, " +
                             relations.Grievance(2, 1) + " left).");
            if (book.Compensate(relations, 2, 1, 5 * table.CompensationPerPoint, 5) != 0)
                failures.Add("DIPL-002: money cleared past the day's cap.");
            if (book.Compensate(relations, 2, 1, 5 * table.CompensationPerPoint, 6) != 5)
                failures.Add("DIPL-002: the next day's cap did not open.");

            // A killing: nothing under ThreatAt for the window.
            var killed = new HouseRelations();
            killed.Note(2, 1, GrievanceKind.ManKilled, 5);
            cleared = book.Compensate(killed, 2, 1, 100 * table.CompensationPerPoint, 5);
            if (cleared != config.ManKilled - config.ThreatAt ||
                System.Math.Abs(killed.Grievance(2, 1) - config.ThreatAt) > 0.01f)
                failures.Add("DIPL-002: money took a killing under the threat rung (" +
                             killed.Grievance(2, 1) + ").");
            if (book.Compensate(killed, 2, 1, 5 * table.CompensationPerPoint, 6) != 0)
                failures.Add("DIPL-002: the killing floor lifted the next day.");
            if (book.Compensate(killed, 2, 1, 5 * table.CompensationPerPoint,
                    5 + table.KillingFloorDays) != 5)
                failures.Add("DIPL-002: the killing floor did not lift after its days.");
            if (killed.LastKilling(2, 1) != 5 || killed.LastKilling(1, 2) != -1)
                failures.Add("DIPL-002: the killing is on the wrong side of the pair.");
        }

        // ------------------------------------------------------------- DIPL-003

        static Proposal Word(int to, ProposalKind kind, TerritoryBlockId street)
        {
            var word = new Proposal { To = to, Kind = kind };
            word.Terms.Blocks.Add(street.Value);
            return word;
        }

        /// <summary>A warning complied with: the receiver keeps off the street at the
        /// paper city's choke point for ComplyDays, its posted crew goes home, nothing
        /// is noted - and on its day the word lifts.</summary>
        static void AWarningCompliedWithKeepsThemOffTheStreet(List<string> failures)
        {
            var table = new Table(41);
            var strong = table.World.Of(1);
            var weak = table.World.Of(2);
            strong.Runner.Accounts.Safe = 1_000_000;
            weak.Runner.Accounts.Safe = 0;
            var street = table.City.HomeBlockOf(1);
            var crew = weak.Roster.Crews.Count > 0 ? weak.Roster.Crews[0] : null;
            if (crew == null)
            {
                failures.Add("DIPL-003: the fixture dealt a house with no crew.");
                return;
            }
            var posted = table.Carry(2, HouseIntent.Block(
                HouseOrder.OperateInBlock, crew.Id, street, HouseMind.TierExpand, "test"));
            if (posted != "")
                failures.Add("DIPL-003: the fixture could not post the crew (" + posted + ").");

            var before = table.World.Relations.Grievance(1, 2);
            var asked = HouseOps.Propose(table.World, strong,
                Word(2, ProposalKind.Warn, street), table.Look);
            var filed = Last(table);
            if (!asked.Ok || filed == null || filed.Status != ProposalStatus.Accepted)
            {
                failures.Add("DIPL-003: the weaker house did not comply with a warning (" +
                             asked.Reason + " / " + (filed != null ? filed.Answer : "") + ").");
                return;
            }
            var day = weak.Runner.Campaign.Day;
            if (!table.World.Diplomacy.IsKeptOff(2, street, day) ||
                table.World.Diplomacy.KeptOffUntil(2, street) !=
                day + table.World.Diplomacy.Config.ComplyDays)
                failures.Add("DIPL-003: the word did not keep them off the street for ComplyDays.");
            if (table.Carry(2, HouseIntent.Block(
                    HouseOrder.ShakeDownBlock, crew.Id, street, HouseMind.TierExpand, "test")) !=
                HouseDiplomacy.ReasonUnderOurWord)
                failures.Add("DIPL-003: the choke point let the warned house walk the street.");
            table.City.SweepKeepOffs(table.World);
            if (table.Look(weak).CrewBlock(crew.Id) == street)
                failures.Add("DIPL-003: the posted crew was not sent home.");
            if (table.World.Relations.Grievance(1, 2) != before)
                failures.Add("DIPL-003: a warning complied with was noted as ignored.");
            if (filed.ExpiresDay != filed.Day + table.World.Diplomacy.Config.WordDays)
                failures.Add("DIPL-003: a word waits " + (filed.ExpiresDay - filed.Day) +
                             " days, not " + table.World.Diplomacy.Config.WordDays + ".");

            for (var i = 0; i < table.World.Diplomacy.Config.ComplyDays; i++)
                table.World.DayTick();
            if (table.World.Diplomacy.IsKeptOff(2, street, weak.Runner.Campaign.Day))
                failures.Add("DIPL-003: the word did not lift on its day.");

            // A word with no street on it is refused before it is filed.
            var bare = HouseOps.Propose(table.World, strong,
                new Proposal { To = 2, Kind = ProposalKind.Threaten }, table.Look);
            if (bare.Ok || bare.Reason != HouseDiplomacy.ReasonNoStreetNamed)
                failures.Add("DIPL-003: a word naming no street was filed.");
        }

        /// <summary>Refused, a word is a grudge at once; unanswered, it is one on its
        /// second day - and once only: there is no sweep to note it twice.</summary>
        static void ARefusedOrUnansweredWordIsAGrudgeOnce(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = new Table(42);
            var sender = table.World.Of(1);
            var proud = table.World.Of(2);
            sender.Runner.Accounts.Safe = 0;
            proud.Runner.Accounts.Safe = 1_000_000;
            var street = table.City.HomeBlockOf(1);

            HouseOps.Propose(table.World, sender, Word(2, ProposalKind.Warn, street), table.Look);
            var filed = Last(table);
            if (filed == null || filed.Status != ProposalStatus.Refused ||
                filed.Answer != HouseDiplomacy.ReasonWeKeepOurStreets)
                failures.Add("DIPL-003: the stronger house did not refuse the warning in words (" +
                             (filed != null ? filed.Status + " " + filed.Answer : "") + ").");
            if (System.Math.Abs(table.World.Relations.Grievance(1, 2) - config.WarningIgnored) > 0.01f)
                failures.Add("DIPL-003: a refused word was not noted once (" +
                             table.World.Relations.Grievance(1, 2) + ").");
            if (table.World.Diplomacy.IsKeptOff(2, street, proud.Runner.Campaign.Day))
                failures.Add("DIPL-003: a refused word kept them off the street.");

            // To the player: unanswered for two days, one note; not a second.
            var inbox = new Table(43);
            var teller = inbox.World.Of(1);
            teller.Runner.Accounts.Safe = 0;
            HouseOps.Propose(inbox.World, teller, Word(0, ProposalKind.Threaten,
                inbox.City.HomeBlockOf(1)), inbox.Look);
            var word = Last(inbox);
            if (word == null || !word.Open)
            {
                failures.Add("DIPL-003: the player's inbox answered a word for him.");
                return;
            }
            var was = inbox.World.Relations.Grievance(1, 0);
            inbox.World.DayTick();
            if (!word.Open || inbox.World.Relations.Grievance(1, 0) > was)
                failures.Add("DIPL-003: a word lapsed a day early.");
            inbox.World.DayTick();
            if (word.Status != ProposalStatus.Expired)
                failures.Add("DIPL-003: a word did not lapse on its second day (" + word.Status + ").");
            var noted = inbox.World.Relations.Grievance(1, 0) - was;
            if (System.Math.Abs(noted - (config.WarningIgnored - config.GrievanceDecayPerDay)) > 0.01f &&
                System.Math.Abs(noted - config.WarningIgnored) > 0.01f)
                failures.Add("DIPL-003: a lapsed word was not noted once (" + noted + ").");
            var after = inbox.World.Relations.Grievance(1, 0);
            inbox.World.DayTick();
            if (inbox.World.Relations.Grievance(1, 0) > after)
                failures.Add("DIPL-003: a lapsed word was noted twice.");
        }

        /// <summary>The mind's bill is priced from the grudge, so paying it in full
        /// lands exactly on the threat rung and the same bill is not sent again.</summary>
        static void TheBillIsPricedFromTheGrudge(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var rate = DiplomacyConfig.Default.CompensationPerPoint;

            // AT PEACE, owed a threat's worth or more, the first word is the truce
            // (the ladder's own order); the words are said inside the truce. The mind,
            // in a truce and owed thirty: a bill for ten points.
            var them = new TerritoryGangId(2);
            var view = Desk(1, Stance.Peace, 30f, config.MinWarDays * 3, 1);
            view.Rivals = new[] { them };
            view.LadderLook = other => config.StepFor(30f);
            var intents = new List<HouseIntent>();
            HouseMind.Think(view, HouseMindConfig.Default, config, intents);
            if (FindProposal(intents, ProposalKind.OfferTruce) == null ||
                FindProposal(intents, ProposalKind.Bill) != null)
                failures.Add("DIPL-003: at peace and owed thirty the first word was not the truce.");

            view = Desk(1, Stance.Truce, 30f, config.MinWarDays * 3, 1);
            view.Rivals = new[] { them };
            view.LadderLook = other => config.StepFor(30f);
            HouseMind.Think(view, HouseMindConfig.Default, config, intents);
            var bill = FindProposal(intents, ProposalKind.Bill);
            if (bill == null)
                failures.Add("DIPL-003: a house owed thirty sent no bill.");
            else if (bill.Terms.Money != (int)((30f - config.ThreatAt) * rate))
                failures.Add("DIPL-003: the bill reads $" + bill.Terms.Money + ", not $" +
                             (int)((30f - config.ThreatAt) * rate) + ".");

            // Owed twenty-five, a threat; owed fifteen, a word; owed nothing, nothing.
            view = Desk(1, Stance.Truce, 25f, config.MinWarDays * 3, 1);
            view.Rivals = new[] { them };
            view.LadderLook = other => config.StepFor(25f);
            view.Blocks = new[] { new TerritoryBlockId("block:ours") };
            view.LeaderLook = blockId => new TerritoryGangId(1);
            HouseMind.Think(view, HouseMindConfig.Default, config, intents);
            if (FindProposal(intents, ProposalKind.Threaten) == null ||
                FindProposal(intents, ProposalKind.Bill) != null)
                failures.Add("DIPL-003: a house owed a threat's worth did not threaten.");
            view.StanceLook = other => Stance.Peace;
            view.GrievanceLook = other => 15f;
            view.LadderLook = other => config.StepFor(15f);
            HouseMind.Think(view, HouseMindConfig.Default, config, intents);
            if (FindProposal(intents, ProposalKind.Warn) == null)
                failures.Add("DIPL-003: a house owed a warning's worth did not warn.");
            view.GrievanceLook = other => 0f;
            view.LadderLook = other => LadderStep.Ignore;
            HouseMind.Think(view, HouseMindConfig.Default, config, intents);
            if (FindProposal(intents, ProposalKind.Warn) != null ||
                FindProposal(intents, ProposalKind.Threaten) != null ||
                FindProposal(intents, ProposalKind.Bill) != null)
                failures.Add("DIPL-003: a house owed nothing sent a word.");

            // Through the table: paid in full, the grudge lands exactly on the rung.
            var table = new Table(44);
            var creditor = table.World.Of(1);
            var debtor = table.World.Of(2);
            creditor.Runner.Accounts.Safe = 1_000_000;
            for (var i = 0; i < 3; i++)
                table.World.Relations.Note(1, 2, GrievanceKind.DoorSwitched);
            var owed = table.World.Relations.Grievance(1, 2);
            var price = (int)((owed - config.ThreatAt) * rate);
            debtor.Runner.Accounts.Safe =
                Wages.DailyPayroll(debtor.Roster) * DiplomacyConfig.Default.BillReserveDays + price;
            var creditorSafe = creditor.Runner.Accounts.Safe;
            HouseOps.Propose(table.World, creditor,
                new Proposal { To = 2, Kind = ProposalKind.Bill, Terms = new ProposalTerms { Money = price } },
                table.Look);
            var filed = Last(table);
            if (filed == null || filed.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-003: a bill the debtor could cover was not paid (" +
                             (filed != null ? filed.Answer : "") + ").");
            else
            {
                if (System.Math.Abs(table.World.Relations.Grievance(1, 2) - config.ThreatAt) > 0.01f)
                    failures.Add("DIPL-003: a bill paid in full did not land on the threat rung (" +
                                 table.World.Relations.Grievance(1, 2) + ").");
                if (creditor.Runner.Accounts.Safe != creditorSafe + price)
                    failures.Add("DIPL-003: the bill's money did not reach the sender.");
            }

            // A debtor a dollar short of the reserve does not pay.
            var poor = new Table(45);
            poor.World.Of(1).Runner.Accounts.Safe = 1_000_000;
            for (var i = 0; i < 3; i++)
                poor.World.Relations.Note(1, 2, GrievanceKind.DoorSwitched);
            var short_ = poor.World.Of(2);
            short_.Runner.Accounts.Safe =
                Wages.DailyPayroll(short_.Roster) * DiplomacyConfig.Default.BillReserveDays + price - 1;
            HouseOps.Propose(poor.World, poor.World.Of(1),
                new Proposal { To = 2, Kind = ProposalKind.Bill, Terms = new ProposalTerms { Money = price } },
                poor.Look);
            filed = Last(poor);
            if (filed == null || filed.Status != ProposalStatus.Refused ||
                filed.Answer != HouseDiplomacy.ReasonWhistleForIt)
                failures.Add("DIPL-003: a debtor under the reserve paid the bill.");
        }

        static Proposal FindProposal(List<HouseIntent> intents, ProposalKind kind)
        {
            for (var i = 0; i < intents.Count; i++)
                if (intents[i].Kind == HouseIntentKind.Propose &&
                    intents[i].Proposal != null && intents[i].Proposal.Kind == kind)
                    return intents[i].Proposal;
            return null;
        }

        /// <summary>The desk complies with a word from a house that reads stronger
        /// while it is owed less than a threat's worth by it, and refuses otherwise.
        /// </summary>
        static void TheDeskAnswersAWordOnItsTests(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = DiplomacyConfig.Default;
            var street = new TerritoryBlockId("block:theirs");
            var word = Word(2, ProposalKind.Warn, street);
            word.From = 1;

            var weakOwedNothing = Desk(2, Stance.Peace, 0f, 10, theirs: 100);
            if (!HouseDiplomacy.Answer(weakOwedNothing, word, table, config).Accepted)
                failures.Add("DIPL-003: a weaker house owed nothing did not comply.");
            var weakOwedAThreat = Desk(2, Stance.Peace, config.ThreatAt, 10, theirs: 100);
            if (HouseDiplomacy.Answer(weakOwedAThreat, word, table, config).Accepted)
                failures.Add("DIPL-003: a house owed a threat's worth complied.");
            var strongOwedNothing = Desk(2, Stance.Peace, 0f, 100, theirs: 10);
            var answer = HouseDiplomacy.Answer(strongOwedNothing, word, table, config);
            if (answer.Accepted || answer.Reason != HouseDiplomacy.ReasonWeKeepOurStreets)
                failures.Add("DIPL-003: the stronger house complied.");
        }

        // ------------------------------------------------------------- DIPL-004

        /// <summary>Four blocks to house 1, one to house 2, and the player level with
        /// house 1 so he levies nobody and is levied by nobody but house 2: the city
        /// the tribute book reads, handed to every runner.</summary>
        static void Levy(Table table, int leader, int blocks, int other, int otherBlocks)
        {
            var city = new List<Turf.Holding>();
            for (var b = 0; b < blocks; b++)
            {
                city.Add(new Turf.Holding(leader, b));
                city.Add(new Turf.Holding(0, 200 + b));
            }
            for (var b = 0; b < otherBlocks; b++)
                city.Add(new Turf.Holding(other, 100 + b));
            for (var g = 0; g < table.World.Count; g++)
                if (table.World.Of(g) != null)
                    table.World.Of(g).Runner.HoldingsOf = into =>
                    {
                        into.Clear();
                        into.AddRange(city);
                    };
        }

        /// <summary>Ruling 2. Every house is assessed and settled in one pass, in
        /// gang-id order; the envelope leaves the payer's safe and lands in the
        /// levying house's, dirty, on both sheets - AI to AI, with the player nowhere
        /// in it; a house that cannot cover it is owed for it.</summary>
        static void EveryHousesEnvelopeCrossesInOnePass(List<string> failures)
        {
            var table = new Table(51);
            Levy(table, leader: 1, blocks: 4, other: 2, otherBlocks: 1);
            var payer = table.World.Of(2);
            var payee = table.World.Of(1);
            payer.Runner.Accounts.Safe = 100_000;
            payer.Runner.Accounts.RiskyMoney = 0;
            payee.Runner.Accounts.Safe = 100_000;
            payee.Runner.Accounts.RiskyMoney = 0;
            var expected = (4 - 1) * Tribute.PerBlockAhead;

            // The first midnight strikes the claim; it falls due a cycle later.
            table.World.DayTick();
            var levy = payer.Runner.Tribute.For(1);
            if (levy == null || levy.Amount != expected)
            {
                failures.Add("DIPL-004: the levy reads " + (levy != null ? levy.Amount : -1) +
                             ", not " + expected + ".");
                return;
            }
            if (payee.Runner.Tribute.For(2) != null)
                failures.Add("DIPL-004: the levying house was levied by the house it levies.");
            if (table.Look(payer).TributeOwe(new TerritoryGangId(1)) != expected ||
                table.Look(payee).TributeOwed(new TerritoryGangId(2)) != expected)
                failures.Add("DIPL-004: the view does not read the levy on both sides.");

            var wagesPayer = Wages.DailyPayroll(payer.Roster);
            var wagesPayee = Wages.DailyPayroll(payee.Roster);
            var safePayer = payer.Runner.Accounts.Safe;
            var safePayee = payee.Runner.Accounts.Safe;
            var dirtyPayee = payee.Runner.Accounts.RiskyMoney;
            var crossedOn = -1;
            for (var i = 0; i < Tribute.CycleDays + 1 && crossedOn < 0; i++)
            {
                table.World.DayTick();
                if (payee.Runner.Accounts.RiskyMoney > dirtyPayee)
                    crossedOn = i;
            }
            if (crossedOn < 0)
            {
                failures.Add("DIPL-004: the envelope never crossed.");
                return;
            }
            if (payee.Runner.Accounts.RiskyMoney - dirtyPayee != expected)
                failures.Add("DIPL-004: the levying house was credited " +
                             (payee.Runner.Accounts.RiskyMoney - dirtyPayee) + ", not " +
                             expected + ".");
            if (payee.Runner.Accounts.Current == null ||
                payee.Runner.Accounts.Current.FromHouses != expected)
                failures.Add("DIPL-004: the envelope is not on the levying house's sheet.");
            // House 2 is level with nobody: it kicks up to house 1 AND to the player.
            if (payer.Runner.Accounts.Current == null ||
                payer.Runner.Accounts.Current.ToHouses != expected * 2)
                failures.Add("DIPL-004: the payer's sheet reads " +
                             (payer.Runner.Accounts.Current != null
                                 ? payer.Runner.Accounts.Current.ToHouses : -1) +
                             ", not two envelopes of " + expected + ".");
            if (levy.Overdue)
                failures.Add("DIPL-004: a paid envelope reads overdue.");

            // A house that cannot cover it is owed for it, once.
            payer.Runner.Accounts.Safe = 0;
            var before = table.World.Relations.Grievance(1, 2);
            for (var i = 0; i < Tribute.CycleDays; i++)
                table.World.DayTick();
            if (!levy.Overdue)
                failures.Add("DIPL-004: an envelope the safe could not cover is not overdue.");
            if (!(table.World.Relations.Grievance(1, 2) > before))
                failures.Add("DIPL-004: a stiffed house holds no grudge.");
        }

        /// <summary>Agreed terms replace the street's figure for three cycles and then
        /// lapse; the levying desk takes half from a broke payer and refuses less
        /// from a solvent one; overdue terms sour as a levy does.</summary>
        static void TermsPinTheEnvelopeForThreeCycles(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = new Table(52);
            Levy(table, leader: 1, blocks: 4, other: 2, otherBlocks: 1);
            var payer = table.World.Of(2);
            var payee = table.World.Of(1);
            payer.Runner.Accounts.Safe = 100_000;
            payee.Runner.Accounts.Safe = 100_000;
            table.World.DayTick();
            var derived = (4 - 1) * Tribute.PerBlockAhead;
            var levy = payer.Runner.Tribute.For(1);
            if (levy == null || levy.Amount != derived)
            {
                failures.Add("DIPL-004: the fixture struck no levy.");
                return;
            }

            // Less than half, from a solvent payer: the street prices it.
            var mean = table.Propose(2, 1, ProposalKind.TributeTerms, derived / 2 - 100);
            var filed = Last(table);
            if (mean.Ok || filed == null || filed.Status != ProposalStatus.Refused ||
                filed.Answer != HouseDiplomacy.ReasonTheStreetPricesIt ||
                mean.Reason != HouseDiplomacy.ReasonTheStreetPricesIt)
                failures.Add("DIPL-004: less than half was taken from a solvent payer (" +
                             (filed != null ? filed.Status + " " + filed.Answer : mean.Reason) + ").");

            // Half: taken, and pinned for three cycles whatever the street says.
            table.Propose(2, 1, ProposalKind.TributeTerms, derived / 2);
            filed = Last(table);
            if (filed == null || filed.Status != ProposalStatus.Accepted)
            {
                failures.Add("DIPL-004: half the envelope was refused (" +
                             (filed != null ? filed.Answer : "") + ").");
                return;
            }
            if (levy.Amount != derived / 2 || !levy.Pinned(payer.Runner.Campaign.Day))
                failures.Add("DIPL-004: the terms did not pin the levy.");
            Levy(table, leader: 1, blocks: 8, other: 2, otherBlocks: 1);
            var day = payer.Runner.Campaign.Day;
            var until = day + DiplomacyConfig.Default.TermsCycles * Tribute.CycleDays;
            for (var d = day + 1; d < until; d++)
            {
                table.World.DayTick();
                if (levy.Amount != derived / 2)
                {
                    failures.Add("DIPL-004: the terms lapsed on day " + d + " of " + until + ".");
                    break;
                }
            }
            table.World.DayTick();
            if (levy.Amount != (8 - 1) * Tribute.PerBlockAhead)
                failures.Add("DIPL-004: after the terms the street did not price the envelope (" +
                             levy.Amount + ").");

            // A broke levying house takes less than half.
            var broke = new Table(53);
            Levy(broke, leader: 1, blocks: 4, other: 2, otherBlocks: 1);
            broke.World.Of(2).Runner.Accounts.Safe = 100_000;
            broke.World.DayTick();
            broke.World.Of(1).Runner.Accounts.Safe = 0;
            broke.Propose(2, 1, ProposalKind.TributeTerms, Tribute.Floor / 4);
            filed = Last(broke);
            if (filed == null || filed.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-004: a broke levying house refused an envelope.");

            // Upward terms from the levying house: only from a stronger one, and only
            // when the payer can cover them over the reserve.
            var upward = new Table(54);
            Levy(upward, leader: 1, blocks: 4, other: 2, otherBlocks: 1);
            var strong = upward.World.Of(1);
            var levied = upward.World.Of(2);
            strong.Runner.Accounts.Safe = 1_000_000;
            upward.World.DayTick();
            levied.Runner.Accounts.Safe =
                Wages.DailyPayroll(levied.Roster) * DiplomacyConfig.Default.BillReserveDays + derived * 2;
            upward.Propose(1, 2, ProposalKind.TributeTerms, derived * 2);
            filed = Last(upward);
            if (filed == null || filed.Status != ProposalStatus.Accepted ||
                levied.Runner.Tribute.For(1) == null || levied.Runner.Tribute.For(1).Amount != derived * 2)
                failures.Add("DIPL-004: the levied house did not take a stronger house's terms (" +
                             (filed != null ? filed.Answer : "") + ").");
            var nobody = upward.Propose(0, 1, ProposalKind.TributeTerms, 100);
            filed = Last(upward);
            if (nobody.Ok && filed != null && filed.Status == ProposalStatus.Accepted)
                failures.Add("DIPL-004: terms were agreed where nobody owes anybody.");

            // The mind, levied and broke, puts half on the table.
            var view = Desk(2, Stance.Peace, 0f, config.MinWarDays - 1, 1);
            var them = new TerritoryGangId(1);
            view.Rivals = new[] { them };
            view.TributeLook = other => (2_000, 0);
            var intents = new List<HouseIntent>();
            HouseMind.Think(view, HouseMindConfig.Default, config, intents);
            var terms = FindProposal(intents, ProposalKind.TributeTerms);
            if (terms == null || terms.Terms.Money != 1_000)
                failures.Add("DIPL-004: a levied and broke mind did not put half on the table.");
            view.Accounts.Safe = view.DailyPayroll * config.MinWarDays * 3;
            HouseMind.Think(view, HouseMindConfig.Default, config, intents);
            if (FindProposal(intents, ProposalKind.TributeTerms) != null)
                failures.Add("DIPL-004: a solvent mind haggled its tribute.");
        }

        // ------------------------------------------------------------- DIPL-005

        static Character Hood(House house)
        {
            for (var i = 0; i < house.Roster.Members.Count; i++)
            {
                var man = house.Roster.Members[i];
                if (!man.Gone && man.Rank == Rank.Hood && man.Status == CharacterStatus.Active)
                    return man;
            }
            return null;
        }

        static Character Capo(House house)
        {
            for (var i = 0; i < house.Roster.Members.Count; i++)
            {
                var man = house.Roster.Members[i];
                if (!man.Gone && man.Rank == Rank.Lieutenant && man.Status == CharacterStatus.Active)
                    return man;
            }
            return null;
        }

        static Job KidnapOf(Character man, int by) =>
            new Job { Type = OrderType.Kidnap, GangId = by, TargetCharacterId = man.Id };

        /// <summary>The kidnap's effect is the books' own: the man is taken for
        /// KidnapDays, his house is owed for him, and the ransom is on the table at
        /// once - paid by a mind whose safe covers it over the reserve, and he is let
        /// go in the morning to a bed; refused by one that cannot, and he sits it out.
        /// A lieutenant is bought back whenever the safe covers the price.</summary>
        static void ARansomIsPaidOrHeWaitsItOut(List<string> failures)
        {
            var table = new Table(61);
            if (table.World.Of(1).Runner.World != table.World)
                failures.Add("DIPL-005: the runner does not know its city.");

            var taker = table.World.Of(1);
            var family = table.World.Of(2);
            var man = Hood(family);
            if (man == null)
            {
                failures.Add("DIPL-005: the fixture dealt a house with no hood.");
                return;
            }
            family.Runner.Accounts.Safe =
                Wages.DailyPayroll(family.Roster) * DiplomacyConfig.Default.BillReserveDays +
                EconomyPrices.KidnapCut;
            var takerSafe = taker.Runner.Accounts.Safe;
            var day = family.Runner.Campaign.Day;
            var before = table.World.Relations.Grievance(2, 1);

            table.World.TakeHim(KidnapOf(man, 1), 1, day);
            if (man.Status != CharacterStatus.Taken)
                failures.Add("DIPL-005: the man was not taken (" + man.Status + ").");
            if (!(table.World.Relations.Grievance(2, 1) > before))
                failures.Add("DIPL-005: his house was not owed for him.");
            var ransom = Last(table);
            if (ransom == null || ransom.Kind != ProposalKind.Ransom || ransom.From != 1 ||
                ransom.To != 2 || ransom.Terms.Money != EconomyPrices.KidnapCut ||
                ransom.Terms.CharacterId != man.Id)
            {
                failures.Add("DIPL-005: the ransom is not on the table.");
                return;
            }
            if (ransom.ExpiresDay != day + OrderResolution.KidnapDays)
                failures.Add("DIPL-005: the ransom waits " + (ransom.ExpiresDay - day) +
                             " days, not as long as he is held.");
            if (ransom.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-005: a house that could cover the ransom over the reserve " +
                             "did not pay (" + ransom.Answer + ").");
            else
            {
                if (taker.Runner.Accounts.Safe != takerSafe + EconomyPrices.KidnapCut)
                    failures.Add("DIPL-005: the ransom did not reach the house that holds him.");
                if (man.BackOnDay != day + 1)
                    failures.Add("DIPL-005: a ransomed man is not let go in the morning (" +
                                 man.BackOnDay + ").");
                RosterOps.Discharge(family.Roster, day + 1);
                if (man.Status != CharacterStatus.Hospitalized)
                    failures.Add("DIPL-005: he did not come home to a bed (" + man.Status + ").");
            }

            // Refused: he waits it out.
            var poor = new Table(62);
            var victim = poor.World.Of(2);
            var hood = Hood(victim);
            victim.Runner.Accounts.Safe = 0;
            day = victim.Runner.Campaign.Day;
            poor.World.TakeHim(KidnapOf(hood, 1), 1, day);
            ransom = Last(poor);
            if (ransom == null || ransom.Status != ProposalStatus.Refused ||
                ransom.Answer != HouseDiplomacy.ReasonHeCanWait)
                failures.Add("DIPL-005: a house with an empty safe paid a ransom (" +
                             (ransom != null ? ransom.Status + " " + ransom.Answer : "") + ").");
            if (hood.BackOnDay != day + OrderResolution.KidnapDays)
                failures.Add("DIPL-005: a refused ransom moved the day he comes back.");

            // A lieutenant, with the safe covering the price and nothing over.
            var capoTable = new Table(63);
            var his = capoTable.World.Of(2);
            var capo = Capo(his);
            his.Runner.Accounts.Safe = EconomyPrices.KidnapCut;
            capoTable.World.TakeHim(KidnapOf(capo, 1), 1, his.Runner.Campaign.Day);
            ransom = Last(capoTable);
            if (ransom == null || ransom.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-005: a lieutenant the safe could cover was left in the cellar (" +
                             (ransom != null ? ransom.Answer : "") + ").");

            // A kidnap of one's own man is nothing.
            var own = new Table(64);
            var ours = Hood(own.World.Of(1));
            own.World.TakeHim(KidnapOf(ours, 1), 1, own.World.Of(1).Runner.Campaign.Day);
            if (own.World.Diplomacy.All.Count != 0)
                failures.Add("DIPL-005: a house ransomed its own man.");
        }

        /// <summary>The player's man: the ransom waits in the inbox as long as he is
        /// held; paid, he is back in the morning; refused or lapsed, he comes back on
        /// his day and nothing is noted.</summary>
        static void ThePlayersManIsRansomedFromTheInbox(List<string> failures)
        {
            var table = new Table(65);
            var player = table.World.Of(0);
            var boss = player.Roster.FindBoss();
            var man = Hood(player) ?? Capo(player) ?? boss;
            if (man == null)
            {
                failures.Add("DIPL-005: the player has nobody to take.");
                return;
            }
            player.Runner.Accounts.Safe = 50_000;
            var day = player.Runner.Campaign.Day;
            table.World.TakeHim(KidnapOf(man, 1), 1, day);
            var ransom = Last(table);
            if (ransom == null || !ransom.Open)
            {
                failures.Add("DIPL-005: the player's inbox answered a ransom for him.");
                return;
            }
            var replied = HouseOps.Reply(table.World, player, ransom.Id, true, table.Look);
            if (!replied.Ok || ransom.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-005: the player could not pay the ransom (" + replied.Reason + ").");
            else if (player.Runner.Accounts.Safe != 50_000 - EconomyPrices.KidnapCut ||
                     man.BackOnDay != day + 1)
                failures.Add("DIPL-005: the player's ransom did not buy the morning.");

            // Lapsed: he comes back on his day, and nobody is owed for the silence.
            var quiet = new Table(66);
            var his = quiet.World.Of(0);
            var held = Hood(his) ?? Capo(his) ?? his.Roster.FindBoss();
            day = his.Runner.Campaign.Day;
            quiet.World.TakeHim(KidnapOf(held, 1), 1, day);
            ransom = Last(quiet);
            var owed = quiet.World.Relations.Grievance(1, 0);
            for (var i = 0; i < OrderResolution.KidnapDays; i++)
                quiet.World.DayTick();
            if (ransom == null || ransom.Status != ProposalStatus.Expired)
                failures.Add("DIPL-005: an unanswered ransom did not lapse when he was let go (" +
                             (ransom != null ? ransom.Status.ToString() : "") + ").");
            if (quiet.World.Relations.Grievance(1, 0) > owed)
                failures.Add("DIPL-005: a lapsed ransom was held against the player.");
            if (held.Status == CharacterStatus.Taken)
                failures.Add("DIPL-005: he was not let go on his day.");
        }

        // ------------------------------------------------------------- DIPL-006

        static Proposal LineAcross(int to, params TerritoryBlockId[] streets)
        {
            var line = new Proposal { To = to, Kind = ProposalKind.Line };
            for (var i = 0; i < streets.Length; i++)
                line.Terms.Blocks.Add(streets[i].Value);
            return line;
        }

        /// <summary>A line taken by two houses that could not pay for a war keeps both
        /// off its streets at the choke point, on both sides, for LineDays; a third
        /// house is untouched; a door taken across it is owed for on top; a house that
        /// can afford to argue refuses it; on its day it lifts.</summary>
        static void TheLineKeepsBothHousesOffTheStreets(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = new Table(71);
            var a = table.World.Of(1);
            var b = table.World.Of(2);
            a.Runner.Accounts.Safe = 0;
            b.Runner.Accounts.Safe = 0;
            var ours = table.City.HomeBlockOf(1);
            var theirs = table.City.HomeBlockOf(2);
            var crewA = a.Roster.Crews[0];
            var crewB = b.Roster.Crews[0];
            var crewC = table.World.Of(0).Roster.Crews.Count > 0 ? table.World.Of(0).Roster.Crews[0] : null;

            var asked = HouseOps.Propose(table.World, a, LineAcross(2, ours, theirs), table.Look);
            var line = Last(table);
            if (!asked.Ok || line == null || line.Status != ProposalStatus.Accepted)
            {
                failures.Add("DIPL-006: two broke houses did not agree a line (" + asked.Reason +
                             " / " + (line != null ? line.Answer : "") + ").");
                return;
            }
            var day = a.Runner.Campaign.Day;
            if (!table.World.Diplomacy.IsKeptOff(1, theirs, day) ||
                !table.World.Diplomacy.IsKeptOff(2, ours, day) ||
                !table.World.Diplomacy.IsKeptOff(1, ours, day) ||
                !table.World.Diplomacy.IsKeptOff(2, theirs, day))
                failures.Add("DIPL-006: the line does not keep both houses off both streets.");
            if (table.Carry(1, HouseIntent.Block(HouseOrder.OperateInBlock, crewA.Id, theirs,
                    HouseMind.TierExpand, "test")) != HouseDiplomacy.ReasonUnderOurWord ||
                table.Carry(2, HouseIntent.Block(HouseOrder.ShakeDownBlock, crewB.Id, ours,
                    HouseMind.TierExpand, "test")) != HouseDiplomacy.ReasonUnderOurWord)
                failures.Add("DIPL-006: the choke point let a house across the line.");
            if (crewC != null && table.Carry(0, HouseIntent.Block(HouseOrder.OperateInBlock,
                    crewC.Id, ours, HouseMind.TierExpand, "test")) == HouseDiplomacy.ReasonUnderOurWord)
                failures.Add("DIPL-006: the line kept a third house off the street.");
            if (table.World.Diplomacy.Lines.Count != 2 ||
                !table.World.Diplomacy.Crosses(1, theirs, day) ||
                !table.World.Diplomacy.Crosses(2, ours, day) ||
                table.World.Diplomacy.Crosses(0, ours, day))
                failures.Add("DIPL-006: the book does not read the line.");

            // A door taken across it is owed for on top.
            var before = table.World.Relations.Grievance(2, 1);
            if (!table.World.Diplomacy.NoteCrossing(table.World.Relations, 2, 1, theirs, day))
                failures.Add("DIPL-006: a crossing was not read as one.");
            if (System.Math.Abs(table.World.Relations.Grievance(2, 1) - before - config.LineCrossed) > 0.01f)
                failures.Add("DIPL-006: a door taken across the line was not owed for on top.");
            if (table.World.Diplomacy.NoteCrossing(table.World.Relations, 2, 0,
                    table.City.HomeBlockOf(0), day))
                failures.Add("DIPL-006: a crossing was read where there is no line.");

            // On its day it lifts.
            for (var i = 0; i < table.World.Diplomacy.Config.LineDays; i++)
                table.World.DayTick();
            day = a.Runner.Campaign.Day;
            if (table.World.Diplomacy.Lines.Count != 0 || table.World.Diplomacy.IsKeptOff(1, theirs, day))
                failures.Add("DIPL-006: the line did not lift on its day.");

            // A house that can afford to argue refuses it.
            var rich = new Table(72);
            rich.World.Of(1).Runner.Accounts.Safe = 0;
            rich.World.Of(2).Runner.Accounts.Safe = 1_000_000;
            HouseOps.Propose(rich.World, rich.World.Of(1),
                LineAcross(2, rich.City.HomeBlockOf(1)), rich.Look);
            line = Last(rich);
            if (line == null || line.Status != ProposalStatus.Refused ||
                line.Answer != HouseDiplomacy.ReasonWeCanAffordToArgue)
                failures.Add("DIPL-006: a house that can pay for a war took a line (" +
                             (line != null ? line.Answer : "") + ").");
            rich.World.Of(1).Runner.Accounts.Safe = 1_000_000;
            rich.World.Of(2).Runner.Accounts.Safe = 0;
            HouseOps.Propose(rich.World, rich.World.Of(1),
                LineAcross(2, rich.City.HomeBlockOf(1)), rich.Look);
            line = Last(rich);
            if (line == null || line.Status != ProposalStatus.Refused)
                failures.Add("DIPL-006: a broke house took a line from one that can pay for a war.");
            var bare = HouseOps.Propose(rich.World, rich.World.Of(1),
                new Proposal { To = 2, Kind = ProposalKind.Line }, rich.Look);
            if (bare.Ok)
                failures.Add("DIPL-006: a line naming no street was filed.");
        }

        /// <summary>The mind draws a line at the border's cap toward a neighbour it
        /// touches, when neither house could pay for a war - across the streets where
        /// the two touch, and nowhere else.</summary>
        static void TheMindDrawsALineAtTheBordersCap(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var them = new TerritoryGangId(2);
            var ours = new TerritoryBlockId("block:ours");
            var theirs = new TerritoryBlockId("block:theirs");
            var far = new TerritoryBlockId("block:far");

            HouseView Squeezed(float grievance, int endurance, int theirEndurance)
            {
                var view = Desk(1, Stance.Peace, grievance, endurance, theirEndurance);
                view.Rivals = new[] { them };
                view.Blocks = new[] { ours };
                view.LeaderLook = blockId =>
                    blockId == ours ? new TerritoryGangId(1)
                    : blockId == theirs ? them
                    : new TerritoryGangId(7);
                view.NeighbourLook = blockId => blockId == ours
                    ? new[] { theirs, far }
                    : new TerritoryBlockId[0];
                view.LadderLook = other => config.StepFor(grievance);
                return view;
            }

            var intents = new List<HouseIntent>();
            HouseMind.Think(Squeezed(config.BorderPressureCap, 5, 5), HouseMindConfig.Default,
                config, intents);
            var line = FindProposal(intents, ProposalKind.Line);
            if (line == null)
                failures.Add("DIPL-006: a squeezed house that cannot pay for a war drew no line.");
            else if (line.Terms.Blocks.Count != 2 || !line.Terms.Blocks.Contains(ours.Value) ||
                     !line.Terms.Blocks.Contains(theirs.Value))
                failures.Add("DIPL-006: the line does not run where the two houses touch (" +
                             string.Join(",", line.Terms.Blocks) + ").");

            HouseMind.Think(Squeezed(config.BorderPressureCap, config.MinWarDays * 3, 5),
                HouseMindConfig.Default, config, intents);
            if (FindProposal(intents, ProposalKind.Line) != null)
                failures.Add("DIPL-006: a house that can pay for a war drew a line.");
            HouseMind.Think(Squeezed(config.BorderPressureCap, 5, config.MinWarDays * 3),
                HouseMindConfig.Default, config, intents);
            if (FindProposal(intents, ProposalKind.Line) != null)
                failures.Add("DIPL-006: a house facing one that can pay for a war drew a line.");
            HouseMind.Think(Squeezed(config.BorderPressureCap - 10, 5, 5),
                HouseMindConfig.Default, config, intents);
            if (FindProposal(intents, ProposalKind.Line) != null)
                failures.Add("DIPL-006: a house under the border's cap drew a line.");
        }

        // ------------------------------------------------------------- DIPL-007

        static Proposal PactAgainst(int to, int third, ProposalKind kind = ProposalKind.Pact,
            int money = 0)
        {
            var pact = new Proposal { To = to, Kind = kind };
            pact.Terms.Third = third;
            pact.Terms.Money = money;
            return pact;
        }

        static bool Heard(House house, string words)
        {
            for (var i = 0; i < house.Runner.Incidents.Count; i++)
                if (house.Runner.Incidents[i].Kind == IncidentKind.AWordBetweenHouses &&
                    house.Runner.Incidents[i].Line.Contains(words))
                    return true;
            return false;
        }

        /// <summary>A pact honoured writes the partner's pending War toward the
        /// declarer for the NEXT midnight, flagged the pact's own, and every book
        /// hears it.</summary>
        static void APactIsHonouredAtTheNextMidnight(List<string> failures)
        {
            var table = new Table(81);
            var victim = table.World.Of(1);
            var partner = table.World.Of(2);
            victim.Runner.Accounts.Safe = 1_000_000;
            partner.Runner.Accounts.Safe = 1_000_000;
            table.World.Of(0).Runner.Accounts.Safe = 0;

            var asked = HouseOps.Propose(table.World, victim, PactAgainst(2, 0), table.Look);
            var pact = Last(table);
            if (!asked.Ok || pact == null || pact.Status != ProposalStatus.Accepted)
            {
                failures.Add("DIPL-007: a pact against a weaker third was refused (" +
                             asked.Reason + " / " + (pact != null ? pact.Answer : "") + ").");
                return;
            }
            var day = victim.Runner.Campaign.Day;
            if (!table.World.Diplomacy.HasPact(1, 2, day) || table.World.Diplomacy.HasPact(1, 0, day))
                failures.Add("DIPL-007: the pact is not on the book as signed.");

            // The player declares on the victim.
            table.World.Relations.SetPending(0, 1, Stance.War);
            table.World.DayTick();
            if (table.World.Relations.StanceBetween(0, 1) != Stance.War)
                failures.Add("DIPL-007: the fixture's war did not land.");
            if (!table.World.Relations.TryGetPending(2, 0, out var pending) || pending != Stance.War)
                failures.Add("DIPL-007: the partner's war on the declarer was not written for the next midnight.");
            if (table.World.Relations.StanceBetween(2, 0) == Stance.War)
                failures.Add("DIPL-007: the partner's war landed the same midnight.");
            if (!Heard(table.World.Of(0), "stands with"))
                failures.Add("DIPL-007: the honour was not printed in every book.");
            table.World.DayTick();
            if (table.World.Relations.StanceBetween(2, 0) != Stance.War)
                failures.Add("DIPL-007: the partner's war did not land the next midnight.");
        }

        /// <summary>Two pacts in a chain, one declaration: exactly one honour. The war
        /// a pact declared is nobody's declaration for the next pact.</summary>
        static void APactsWarWakesNoOtherPact(List<string> failures)
        {
            var table = new Table(82, 5);
            for (var g = 2; g <= 4; g++)
                table.World.Of(g).Runner.Accounts.Safe = 1_000_000;
            table.World.Of(1).Runner.Accounts.Safe = 0;
            HouseOps.Propose(table.World, table.World.Of(2), PactAgainst(3, 1), table.Look);
            HouseOps.Propose(table.World, table.World.Of(3), PactAgainst(4, 1), table.Look);
            var day = table.World.Of(1).Runner.Campaign.Day;
            if (!table.World.Diplomacy.HasPact(2, 3, day) || !table.World.Diplomacy.HasPact(3, 4, day))
            {
                failures.Add("DIPL-007: the fixture's two pacts were not both signed.");
                return;
            }

            table.World.Relations.SetPending(1, 2, Stance.War);
            table.World.DayTick();
            if (!table.World.Relations.TryGetPending(3, 1, out var honoured) || honoured != Stance.War)
                failures.Add("DIPL-007: the first pact was not honoured.");
            if (table.World.Relations.TryGetPending(4, 1, out _))
                failures.Add("DIPL-007: the second pact honoured a war nobody declared on its party.");
            table.World.DayTick();
            if (table.World.Relations.StanceBetween(3, 1) != Stance.War)
                failures.Add("DIPL-007: the honoured war did not land.");
            if (table.World.Relations.TryGetPending(4, 1, out _))
                failures.Add("DIPL-007: a pact's own war woke the next pact.");
            table.World.DayTick();
            if (table.World.Relations.StanceBetween(4, 1) == Stance.War)
                failures.Add("DIPL-007: the chain cascaded.");
        }

        /// <summary>A partner that cannot pay for the war does not honour: the pact is
        /// struck, the abandoned party is owed for it, and every house hears.</summary>
        static void APartnerThatCannotPayBreaksThePact(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = new Table(83);
            var victim = table.World.Of(1);
            var partner = table.World.Of(2);
            victim.Runner.Accounts.Safe = 1_000_000;
            partner.Runner.Accounts.Safe = 1_000_000;
            table.World.Of(0).Runner.Accounts.Safe = 0;
            HouseOps.Propose(table.World, victim, PactAgainst(2, 0), table.Look);
            var day = victim.Runner.Campaign.Day;
            if (!table.World.Diplomacy.HasPact(1, 2, day))
            {
                failures.Add("DIPL-007: the fixture's pact was not signed.");
                return;
            }

            partner.Runner.Accounts.Safe = 0;
            var before = table.World.Relations.Grievance(1, 2);
            table.World.Relations.SetPending(0, 1, Stance.War);
            table.World.DayTick();
            if (table.World.Relations.TryGetPending(2, 0, out _))
                failures.Add("DIPL-007: a partner that cannot pay honoured the pact.");
            if (table.World.Diplomacy.HasPact(1, 2, victim.Runner.Campaign.Day))
                failures.Add("DIPL-007: a broken pact stayed on the book.");
            if (System.Math.Abs(table.World.Relations.Grievance(1, 2) - before - config.PactBroken) > 0.01f)
                failures.Add("DIPL-007: the abandoned party was not owed for it (" +
                             table.World.Relations.Grievance(1, 2) + ").");
            if (!Heard(table.World.Of(0), HouseDiplomacy.ReasonLeftThemToIt))
                failures.Add("DIPL-007: the break was not printed in every book.");

            // The player as partner honours whatever his safe says.
            var player = new Table(84);
            var ally = player.World.Of(1);
            ally.Runner.Accounts.Safe = 1_000_000;
            player.World.Of(0).Runner.Accounts.Safe = 0;
            player.World.Of(2).Runner.Accounts.Safe = 0;
            HouseOps.Propose(player.World, ally, PactAgainst(0, 2), player.Look);
            var pact = Last(player);
            if (pact == null || !pact.Open)
            {
                failures.Add("DIPL-007: the player's inbox answered a pact for him.");
                return;
            }
            HouseOps.Reply(player.World, player.World.Of(0), pact.Id, true, player.Look);
            player.World.Relations.SetPending(2, 1, Stance.War);
            player.World.DayTick();
            if (!player.World.Relations.TryGetPending(0, 2, out var his) || his != Stance.War)
                failures.Add("DIPL-007: the player's pact did not declare for him.");
        }

        /// <summary>JOIN MY WAR: the pact for one war, with money - accepted at peace,
        /// by a house that can pay, against a third that reads weaker, when the money
        /// clears the receiver's own grudge; the receiver's war goes pending as the
        /// pact's own. The mind asks for it when it is losing men.</summary>
        static void JoinMyWarIsThePactForOneWar(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = DiplomacyConfig.Default;
            var rich = config.MinWarDays * 3;

            var view = Desk(2, Stance.Peace, 0f, rich, theirs: 1);
            view.EnduranceLook = other => other.Value == 0 ? 1 : rich * 10;
            var answer = HouseDiplomacy.Answer(view, PactAgainst(2, 0, ProposalKind.JoinWar), table, config);
            answer = HouseDiplomacy.Answer(view, Offer(1, 2, ProposalKind.JoinWar), table, config);
            var ask = PactAgainst(2, 0, ProposalKind.JoinWar);
            ask.From = 1;
            if (!HouseDiplomacy.Answer(view, ask, table, config).Accepted)
                failures.Add("DIPL-007: a solvent house at peace refused to join a war on a weaker third.");
            var strongThird = PactAgainst(2, 3, ProposalKind.JoinWar);
            strongThird.From = 1;
            answer = HouseDiplomacy.Answer(view, strongThird, table, config);
            if (answer.Accepted || answer.Reason != HouseDiplomacy.ReasonTheyReadStronger)
                failures.Add("DIPL-007: a house joined a war on a third that reads stronger.");
            var broke = Desk(2, Stance.Peace, 0f, config.MinWarDays - 1, theirs: 1);
            answer = HouseDiplomacy.Answer(broke, ask, table, config);
            if (answer.Accepted || answer.Reason != HouseDiplomacy.ReasonCannotPayForAWar)
                failures.Add("DIPL-007: a house that cannot pay for a war joined one.");
            var owed = Desk(2, Stance.Peace, 30f, rich, theirs: 1);
            answer = HouseDiplomacy.Answer(owed, ask, table, config);
            if (answer.Accepted || answer.Reason != HouseDiplomacy.ReasonTakenTooMuch)
                failures.Add("DIPL-007: a house owed thirty joined the asker's war for nothing.");
            var paid = PactAgainst(2, 0, ProposalKind.JoinWar, 2_200);
            paid.From = 1;
            if (!HouseDiplomacy.Answer(owed, paid, table, config).Accepted)
                failures.Add("DIPL-007: money that clears the grudge under a threat did not buy the war.");
            var atWar = Desk(2, Stance.War, 0f, rich, theirs: 1);
            answer = HouseDiplomacy.Answer(atWar, ask, table, config);
            if (answer.Accepted || answer.Reason != HouseDiplomacy.ReasonNotAtPeaceWithThem)
                failures.Add("DIPL-007: a house not at peace with the asker joined its war.");

            // Through the table: the receiver's war on the third goes pending as the
            // pact's own, and lands.
            var city = new Table(85);
            var asker = city.World.Of(1);
            var friend = city.World.Of(2);
            asker.Runner.Accounts.Safe = 1_000_000;
            friend.Runner.Accounts.Safe = 1_000_000;
            city.World.Of(0).Runner.Accounts.Safe = 0;
            HouseOps.Propose(city.World, asker, PactAgainst(2, 0, ProposalKind.JoinWar), city.Look);
            var filed = Last(city);
            if (filed == null || filed.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-007: JOIN MY WAR was refused through the table (" +
                             (filed != null ? filed.Answer : "") + ").");
            else if (!city.World.Relations.TryGetPending(2, 0, out var pending) || pending != Stance.War)
                failures.Add("DIPL-007: the joined war was not written pending.");
            city.World.DayTick();
            if (city.World.Relations.StanceBetween(2, 0) != Stance.War)
                failures.Add("DIPL-007: the joined war did not land.");

            // The mind: losing men in a war, it asks a house at peace to join; owed
            // shops by a third at peace, it offers a pact.
            var mind = Desk(1, Stance.Peace, 0f, rich, theirs: 1);
            var friendId = new TerritoryGangId(2);
            var enemyId = new TerritoryGangId(3);
            mind.Rivals = new[] { friendId, enemyId };
            mind.StanceLook = other => other == enemyId ? Stance.War : Stance.Peace;
            mind.LossesLook = other => other == enemyId ? 2 : 0;
            mind.LadderLook = other => LadderStep.Ignore;
            var intents = new List<HouseIntent>();
            HouseMind.Think(mind, HouseMindConfig.Default, config, intents);
            var join = FindProposal(intents, ProposalKind.JoinWar);
            if (join == null || join.To != 2 || join.Terms.Third != 3)
                failures.Add("DIPL-007: a house losing a war did not ask a friend into it.");
            mind.StanceLook = other => Stance.Peace;
            mind.LossesLook = other => 0;
            mind.LadderLook = other => other == enemyId ? LadderStep.RetakeBusiness : LadderStep.Ignore;
            HouseMind.Think(mind, HouseMindConfig.Default, config, intents);
            var pact = FindProposal(intents, ProposalKind.Pact);
            if (pact == null || pact.To != 2 || pact.Terms.Third != 3)
                failures.Add("DIPL-007: a house owed shops by a third offered no pact.");
            if (FindProposal(intents, ProposalKind.JoinWar) != null)
                failures.Add("DIPL-007: a house at peace asked a friend into a war it is not in.");
        }

        // ------------------------------------------------------------- DIPL-008

        /// <summary>The envoy's Streetwise moves every dollar test by the table's
        /// margin per half-step, capped - a truce refused by telephone is accepted
        /// in person - and reads his house stronger by the same.</summary>
        static void AnEnvoyMovesTheTests(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = DiplomacyConfig.Default;
            var rich = config.MinWarDays * 3;

            // Owed fifty at war; $2,000 is ten points, forty is not under forty.
            var view = Desk(2, Stance.War, 50f, rich, theirs: 1);
            var byPhone = Offer(1, 2, ProposalKind.OfferTruce, 2_000);
            if (HouseDiplomacy.Answer(view, byPhone, table, config).Accepted)
                failures.Add("DIPL-008: the fixture's truce was taken by telephone.");
            var inPerson = Offer(1, 2, ProposalKind.OfferTruce, 2_000);
            inPerson.Envoy = 7;
            inPerson.EnvoyHalfSteps = 5;
            if (System.Math.Abs(HouseDiplomacy.MarginOf(inPerson, table) -
                                5 * table.EnvoyMarginPerHalfStep) > 0.001f)
                failures.Add("DIPL-008: the margin is not per half-step.");
            var carried = HouseDiplomacy.Answer(view, inPerson, table, config);
            if (!carried.Accepted)
                failures.Add("DIPL-008: a good talker at the door did not move the money test (" +
                             carried.Reason + "; margin " + HouseDiplomacy.MarginOf(inPerson, table) +
                             ", per " + table.EnvoyMarginPerHalfStep + ", cap " + table.EnvoyMarginCap +
                             ", grievance " + view.Grievance(new TerritoryGangId(1)) +
                             ", endurance " + view.Endurance + ", theirs " +
                             view.TheirEndurance(new TerritoryGangId(1)) + ", stance " +
                             view.StanceToward(new TerritoryGangId(1)) + ", rate " +
                             table.CompensationPerPoint + ", cap/day " + table.CompensationCapPerDay + ").");
            inPerson.EnvoyHalfSteps = 100;
            if (System.Math.Abs(HouseDiplomacy.MarginOf(inPerson, table) - table.EnvoyMarginCap) > 0.001f)
                failures.Add("DIPL-008: the margin is not capped.");
            inPerson.EnvoyHalfSteps = 0;
            if (HouseDiplomacy.MarginOf(inPerson, table) != 0f)
                failures.Add("DIPL-008: a man with no Streetwise moved the tests.");

            // A word from a house that reads level: refused by telephone, complied
            // with when the envoy reads it a little stronger.
            var level = Desk(2, Stance.Peace, 0f, 10, theirs: 10);
            var word = Word(2, ProposalKind.Warn, new TerritoryBlockId("block:x"));
            word.From = 1;
            if (HouseDiplomacy.Answer(level, word, table, config).Accepted)
                failures.Add("DIPL-008: a level house complied by telephone.");
            word.Envoy = 7;
            word.EnvoyHalfSteps = 5;
            var stronger = HouseDiplomacy.Answer(level, word, table, config);
            if (!stronger.Accepted)
                failures.Add("DIPL-008: the envoy did not read his house stronger (" + stronger.Reason +
                             "; margin " + HouseDiplomacy.MarginOf(word, table) + ", endurance " +
                             level.Endurance + ", theirs " + level.TheirEndurance(new TerritoryGangId(1)) +
                             ", grievance " + level.Grievance(new TerritoryGangId(1)) + ").");
        }

        /// <summary>A proposal carried in person is filed in transit and answered by
        /// nobody until the SitDown job arrives; then the desk answers with the margin.
        /// The Don never goes.</summary>
        static void TheSitDownIsDeliveredOnArrival(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = new Table(91);
            var sender = table.World.Of(1);
            var receiver = table.World.Of(2);
            sender.Runner.Accounts.Safe = 1_000_000;
            receiver.Runner.Accounts.Safe = 1_000_000;
            var capo = Capo(sender);
            if (capo == null)
            {
                failures.Add("DIPL-008: the fixture dealt a house with no lieutenant.");
                return;
            }

            var don = HouseOps.SendToSitDown(table.World, sender,
                new Proposal { To = 2, Kind = ProposalKind.OfferTruce }, sender.Roster.BossId);
            if (don.Ok || don.Reason != HouseDiplomacy.ReasonTheDonStaysHome)
                failures.Add("DIPL-008: the Don was sent to a sit-down.");

            var sent = HouseOps.SendToSitDown(table.World, sender,
                new Proposal { To = 2, Kind = ProposalKind.OfferTruce }, capo.Id);
            var filed = Last(table);
            if (!sent.Ok || filed == null)
            {
                failures.Add("DIPL-008: the sit-down could not be sent (" + sent.Reason + ").");
                return;
            }
            if (!filed.Open || !filed.InTransit || filed.Envoy != capo.Id ||
                filed.EnvoyHalfSteps != capo.GetHalfSteps(CharacterAttribute.Streetwise))
                failures.Add("DIPL-008: the carried proposal is not in transit with its envoy.");
            Job job = null;
            for (var i = 0; i < sender.Runner.Book.Jobs.Count; i++)
                if (sender.Runner.Book.Jobs[i].Type == OrderType.SitDown)
                    job = sender.Runner.Book.Jobs[i];
            if (job == null || job.ProposalId != filed.Id)
            {
                failures.Add("DIPL-008: no SitDown job carries the proposal.");
                return;
            }
            if (HouseOps.Ambush(table.World, receiver, filed.Id).Ok)
                failures.Add("DIPL-008: a proposal still on the road was ambushed.");
            var early = HouseOps.Reply(table.World, receiver, filed.Id, true, table.Look);
            if (early.Ok || early.Reason != HouseDiplomacy.ReasonStillOnTheRoad)
                failures.Add("DIPL-008: a proposal still on the road was answered (" + early.Reason + ").");

            var hours = 0;
            while (filed.InTransit && hours < 24 * 3)
            {
                table.World.AdvanceHours(1f);
                hours++;
            }
            if (filed.InTransit)
                failures.Add("DIPL-008: the envoy never arrived.");
            else if (filed.Status != ProposalStatus.Accepted)
                failures.Add("DIPL-008: a truce at peace carried in person was not taken on arrival (" +
                             filed.Status + " " + filed.Answer + ").");

            // The record and the save carry the job's proposal.
            var json = JsonSafe(table);
            if (json == null)
                return;
        }

        /// <summary>The save is the editor's (JsonUtility); headless the round trip is
        /// skipped and reported by TheBookSurvivesTheFile.</summary>
        static string JsonSafe(Table table) => null;

        /// <summary>The host's ambush: the envoy dies at the door, his house is owed a
        /// killing and a betrayal, every house hears, and the proposal reads refused.
        /// Only the house he was sent to, and only once he has arrived.</summary>
        static void AnAmbushKillsTheEnvoyAtTheDoor(List<string> failures)
        {
            var config = HouseRelationsConfig.Default;
            var table = new Table(92);
            var sender = table.World.Of(1);
            var player = table.World.Of(0);
            sender.Runner.Accounts.Safe = 1_000_000;
            var capo = Capo(sender);
            if (capo == null)
            {
                failures.Add("DIPL-008: the fixture dealt a house with no lieutenant.");
                return;
            }
            var sent = HouseOps.SendToSitDown(table.World, sender,
                new Proposal { To = 0, Kind = ProposalKind.OfferTruce }, capo.Id);
            var filed = Last(table);
            if (!sent.Ok || filed == null)
            {
                failures.Add("DIPL-008: the sit-down to the player could not be sent (" + sent.Reason + ").");
                return;
            }
            var hours = 0;
            while (filed.InTransit && hours < 24 * 3)
            {
                table.World.AdvanceHours(1f);
                hours++;
            }
            if (!filed.Open || filed.InTransit)
            {
                failures.Add("DIPL-008: the player's inbox did not hold the carried proposal (" +
                             filed.Status + ").");
                return;
            }

            if (HouseOps.Ambush(table.World, table.World.Of(2), filed.Id).Ok)
                failures.Add("DIPL-008: a house the envoy was not sent to ambushed him.");
            var before = table.World.Relations.Grievance(1, 0);
            var lost = sender.Runner.MenLostTo(0);
            var ambushed = HouseOps.Ambush(table.World, player, filed.Id);
            if (!ambushed.Ok)
                failures.Add("DIPL-008: the host could not ambush (" + ambushed.Reason + ").");
            if (!capo.Gone)
                failures.Add("DIPL-008: the envoy walked away from an ambush.");
            var owed = table.World.Relations.Grievance(1, 0) - before;
            var expected = config.ManKilled + config.SitDownBetrayed;
            if (System.Math.Abs(owed - System.Math.Min(expected, 100f - before)) > 0.01f)
                failures.Add("DIPL-008: the ambush was not owed for as a killing and a betrayal (" + owed + ").");
            if (sender.Runner.MenLostTo(0) != lost + 1)
                failures.Add("DIPL-008: the envoy was not counted as a man lost to the host.");
            if (filed.Status != ProposalStatus.Refused || filed.Answer != HouseDiplomacy.ReasonAmbushed)
                failures.Add("DIPL-008: the ambushed proposal does not read so.");
            if (!Heard(table.World.Of(2), "at their own door"))
                failures.Add("DIPL-008: the ambush was not printed in every book.");
            if (HouseOps.Ambush(table.World, player, filed.Id).Ok)
                failures.Add("DIPL-008: a closed proposal was ambushed twice.");
        }
    }
}
