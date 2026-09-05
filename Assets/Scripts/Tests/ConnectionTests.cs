using System.Collections.Generic;
using LivingCity.News;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Property;
using LivingCity.Territory;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 40, CONN-001..005: the man, the broker, the test buy, the terms, the load,
    /// the save - on the paper city, through the same HouseOps doors the runtime and
    /// the ledger use. Nothing here reads a scene.
    /// </summary>
    public static class ConnectionTests
    {
        static readonly (string Name, System.Action<List<string>> Check)[] Contracts =
        {
            ("TwoPathsOpenPerSeed", TwoPathsOpenPerSeed),
            ("BackgroundIsStableAndRare", BackgroundIsStableAndRare),
            ("EachSignalRunsFromZeroToOne", EachSignalRunsFromZeroToOne),
            ("TheWatchedGateRefusesTheDealWhileThePotFills", TheWatchedGateRefusesTheDealWhileThePotFills),
            ("TheManIsDealtWhenThePotIsFull", TheManIsDealtWhenThePotIsFull),
            ("SigningTheManOpensTheStage", SigningTheManOpensTheStage),
            ("ExactlyOneDirectManPerCity", ExactlyOneDirectManPerCity),
            ("TheCellFiresOnReleaseNeverInside", TheCellFiresOnReleaseNeverInside),
            ("TheBrokerIsNamedAndTheMeetingIsFiled", TheBrokerIsNamedAndTheMeetingIsFiled),
            ("TheTestBuyHoldsWithoutARoomAndTheMindLeases", TheTestBuyHoldsWithoutARoomAndTheMindLeases),
            ("TheStingSeizesThePaymentOnlyAndBurns", TheStingSeizesThePaymentOnlyAndBurns),
            ("ARaidSeizesAndSealsWithoutACase", ARaidSeizesAndSealsWithoutACase),
            ("SoldKilosAreDirtyAndCapped", SoldKilosAreDirtyAndCapped),
            ("TermsDifferByLineAndGrade", TermsDifferByLineAndGrade),
            ("ALoadLandsOnItsDayAndAgainSevenOn", ALoadLandsOnItsDayAndAgainSevenOn),
            ("FourteenDaysWithoutTheManDropsAStageBeforeSupplierOnly",
                FourteenDaysWithoutTheManDropsAStageBeforeSupplierOnly),
            ("ARoundTripKeepsTheConnectionAndTheBook", ARoundTripKeepsTheConnectionAndTheBook),
            ("WithACardPendingTheMindAnswersBeforeWalk", WithACardPendingTheMindAnswersBeforeWalk),
            ("WalkAwayIsChosenWhenItsAppealIsHighest", WalkAwayIsChosenWhenItsAppealIsHighest),
            ("TraffickingIsFifteenToThirtyAndBindsAHood", TraffickingIsFifteenToThirtyAndBindsAHood),
        };

        public static string[] ContractNames()
        {
            var names = new string[Contracts.Length];
            for (var i = 0; i < Contracts.Length; i++)
                names[i] = Contracts[i].Name;
            return names;
        }

        public static List<string> Run()
        {
            var failures = new List<string>();
            for (var i = 0; i < Contracts.Length; i++)
            {
                Apartments.Clear();
                CampaignRunner.WatchOnTheDoor = null;
                CampaignRunner.StungOnTheStreet = null;
                try
                {
                    Contracts[i].Check(failures);
                }
                catch (System.Exception error)
                {
                    failures.Add(Contracts[i].Name + ": " + error.GetType().Name + ": " +
                                 error.Message);
                }
            }
            Apartments.Clear();
            CampaignRunner.WatchOnTheDoor = null;
            CampaignRunner.StungOnTheStreet = null;
            return failures;
        }

        // ------------------------------------------------------------------ the rig

        /// <summary>A paper city with three families and their books, on the same
        /// ledgers the yardstick runs on.</summary>
        public sealed class Rig
        {
            public readonly Underworld World;
            public readonly PaperCity City;
            public readonly TerritoryRacketLedger Racket = new TerritoryRacketLedger();
            public readonly TerritoryDuesLedger Dues = new TerritoryDuesLedger();
            public readonly TerritoryRoundLedger Rounds;
            public readonly TerritoryPaperClock Clock;
            public readonly HouseMindConfig Config = new HouseMindConfig();
            public readonly List<HouseIntent> Intents = new List<HouseIntent>();

            public Rig(int seed, int houses = 3)
            {
                World = Underworld.Deal(seed, houses);
                Rounds = new TerritoryRoundLedger(Racket, Dues);
                Clock = new TerritoryPaperClock(Rounds);
                City = new PaperCity(houses, seed) { Racket = Racket };
                for (var h = 0; h < houses; h++)
                {
                    var house = World.Of(h);
                    var home = City.HomeBlockOf(h);
                    house.Front = City.Door(home, 0);
                    if (house.Roster.Crews.Count > 0)
                        RosterOps.AssignBlockResponsibility(house.Roster, home,
                            house.Roster.Crews[0].LieutenantId, true);
                    City.Stand(home, new TerritoryGangId(h), 60f);
                    // THE COLUMN is open for every house on the bench: a contract
                    // about signing must not depend on which two paths the seed
                    // happened to open. TwoPathsOpenPerSeed reads the draw itself.
                    house.Runner.Connection.Paths =
                        (1 << (int)ConnectionPath.Column) | (1 << (int)ConnectionPath.Cell);
                }
            }

            public House House(int g) => World.Of(g);
            public int Day => House(0).Runner.Campaign.Day;

            public HouseView Look(int g) =>
                City.Look(World, Racket, Dues, House(g), Config, Rounds);

            public EventContext Ctx(int g) => StreetEvents.ContextFor(World, House(g), Config);

            public string Carry(int g, HouseIntent intent) =>
                City.Carry(World, Racket, Dues, Rounds, Clock, House(g), intent, null);

            /// <summary>The hours pass, the books turn, the street rolls.</summary>
            public void Midnight()
            {
                City.Hour += 24.0;
                World.AdvanceHours(24f);
                World.DayTick();
                City.RollTheStreet(World, Racket, Dues, Config, Rounds);
            }

            /// <summary>A few hours, so a job in the book travels and works.</summary>
            public void Hours(float hours)
            {
                City.Hour += hours;
                World.AdvanceHours(hours);
            }

            /// <summary>The paper printed one of ours: NAME reads 1.</summary>
            public void NameInThePaper(int g)
            {
                World.Press.Add(new PressRecord
                {
                    Day = Day, Hour = 6f, Kind = PressKind.Arrest, Where = "RIVERSIDE",
                    NamedGangId = g, Factions = new[] { g },
                    Attribution = PressAttribution.Named,
                });
            }

            public int Think(int g)
            {
                var tier = HouseMind.Think(Look(g), Config, World.Relations.Config, Intents);
                return tier;
            }
        }

        static EventDef TheMan => StreetEvents.DefOf(ConnectionEvents.Defs, EventId.TheMan);
        static EventDef Broker => StreetEvents.DefOf(ConnectionEvents.Defs, EventId.BrokerRumour);

        /// <summary>Deal the man's card straight off the def, whatever the pot says.</summary>
        static EventCard DealTheMan(Rig rig, int g, int seed = 7)
        {
            var view = rig.Look(g);
            var ctx = rig.Ctx(g);
            var card = TheMan.Deal(view, ctx, seed);
            if (card == null)
                return null;
            card.DealtDay = rig.Day;
            card.ExpiresDay = rig.Day + StreetEvents.HoldDays;
            var book = rig.House(g).Runner.Events;
            book.Pending = new PendingCard
            {
                Id = card.Id, Def = card.Def, DealtDay = card.DealtDay,
                ExpiresDay = card.ExpiresDay, Speaker = card.Speaker,
            };
            book.Spoken = card;
            book.CardsDealt++;
            return card;
        }

        static int ChoiceOf(EventCard card, string label)
        {
            for (var i = 0; card != null && i < card.Choices.Count; i++)
                if (card.Choices[i].Label == label)
                    return i;
            return -1;
        }

        // ------------------------------------------------------------ the contracts

        static void TwoPathsOpenPerSeed(List<string> failures)
        {
            for (var seed = 1; seed <= 30; seed++)
            {
                var rig = new Rig(seed);
                rig.House(1).Runner.Connection.Paths = 0;
                var paths = ConnectionEvents.PathsFor(rig.Ctx(1));
                var bits = 0;
                for (var p = 0; p < 4; p++)
                    if ((paths & (1 << p)) != 0)
                        bits++;
                if (bits != 2)
                    failures.Add("CONN-001: seed " + seed + " opened " + bits + " paths, not two.");
                if (paths != ConnectionEvents.PathsFor(rig.Ctx(1)))
                    failures.Add("CONN-001: seed " + seed + " re-drew its paths.");
            }
        }

        static void BackgroundIsStableAndRare(List<string> failures)
        {
            var trades = 0;
            for (var id = 0; id < 800; id++)
            {
                var a = Backgrounds.Of(1987, id);
                var b = Backgrounds.Of(1987, id);
                if (a != b)
                    failures.Add("CONN-001: Background.Of is not stable for id " + id);
                if (a == Background.Direct)
                    failures.Add("CONN-001: Direct was derived from a seed for id " + id);
                if (a != Background.None)
                    trades++;
            }
            if (trades < 50 || trades > 150)
                failures.Add("CONN-001: " + trades + " of 800 men have a trade; one in eight was meant.");
            if (Backgrounds.Of(1987, 5, directManId: 5) != Background.Direct)
                failures.Add("CONN-001: Pablo's man does not read Direct by his id.");
            var connection = new Connection { ManId = 9, ManTrade = Background.Pilot };
            if (Backgrounds.Of(1987, 9, -1, connection) != Background.Pilot)
                failures.Add("CONN-001: the connection's own man does not read the card's trade.");
        }

        static void EachSignalRunsFromZeroToOne(List<string> failures)
        {
            var rig = new Rig(3);
            var view = rig.Look(1);
            var ctx = rig.Ctx(1);
            view.Accounts.Safe = 0;
            if (ConnectionScore.Money(view) != 0f)
                failures.Add("CONN-001: MONEY is not 0 with an empty safe.");
            view.Accounts.Safe = 2 * Connection.TestBuyPrice;
            if (ConnectionScore.Money(view) != 1f)
                failures.Add("CONN-001: MONEY is not 1 with two test buys in the safe.");
            var name = ConnectionScore.Name(view, ctx);
            if (name < 0f || name > 1f)
                failures.Add("CONN-001: NAME out of range: " + name);
            rig.NameInThePaper(1);
            if (ConnectionScore.Name(view, rig.Ctx(1)) != 1f)
                failures.Add("CONN-001: NAME is not 1 after the paper printed us.");
            var signals = ConnectionScore.Signals(view, ctx);
            if (signals.Count != 3)
                failures.Add("CONN-001: Signals returned " + signals.Count + " rows, not three.");
            for (var i = 0; i < signals.Count; i++)
                if (string.IsNullOrEmpty(signals[i].Line) || string.IsNullOrEmpty(signals[i].Name))
                    failures.Add("CONN-001: a signal has no line.");
        }

        static void TheWatchedGateRefusesTheDealWhileThePotFills(List<string> failures)
        {
            var rig = new Rig(4);
            var house = rig.House(1);
            house.Runner.Accounts.Safe = 2 * Connection.TestBuyPrice;
            var book = house.Runner.Events;
            for (var day = 1; day <= 12; day++)
            {
                var view = rig.Look(1);
                view.AttentionLook = _ => 100f;
                var ctx = rig.Ctx(1);
                ctx.Day = day;
                if (TheMan.Gate(view, ctx) != HoldReason.Watched)
                    failures.Add("CONN-001: the gate is not Watched at attention 100.");
                StreetEvents.Roll(view, book, ctx, ConnectionEvents.Defs);
            }
            if (book.Pending != null)
                failures.Add("CONN-001: the man was dealt behind the Watched gate.");
            if (book.PotOf(EventId.TheMan) <= 0f)
                failures.Add("CONN-001: the pot did not fill behind the gate.");
            // Attention 0 - the paper city's own - and the gate is open.
            var open = rig.Look(1);
            if (TheMan.Gate(open, rig.Ctx(1)) == HoldReason.Watched)
                failures.Add("CONN-001: the gate is shut at attention 0.");
        }

        static void TheManIsDealtWhenThePotIsFull(List<string> failures)
        {
            var rig = new Rig(5);
            var house = rig.House(1);
            house.Runner.Accounts.Safe = 4 * Connection.TestBuyPrice;
            rig.NameInThePaper(1);
            var fired = -1;
            for (var day = 1; day <= 12 && fired < 0; day++)
            {
                rig.Midnight();
                house.Runner.Accounts.Safe = 4 * Connection.TestBuyPrice;
                if (house.Runner.Events.Pending != null)
                    fired = rig.Day;
            }
            if (fired < 0)
            {
                failures.Add("CONN-001: a house with money and a name got no card in 12 days.");
                return;
            }
            var pending = house.Runner.Events.Pending;
            if (pending.Id != CardId.PortMan && pending.Id != CardId.FieldMan)
                failures.Add("CONN-001: the first card is " + pending.Id + ", not the man.");
            var card = StreetEvents.CardOf(house.Runner.Events, rig.Look(1), rig.Ctx(1),
                ConnectionEvents.Defs);
            if (card == null || card.Speaker < 0 || card.Choices.Count != 2 || card.Lines.Count < 3)
            {
                failures.Add("CONN-001: the man's card is not spoken in full.");
                return;
            }
            for (var i = 0; i < card.Lines.Count; i++)
                if (string.IsNullOrEmpty(card.Lines[i]) || card.Lines[i].Contains("{"))
                    failures.Add("CONN-001: a line on the card is empty or has a placeholder left: " +
                                 card.Lines[i]);
            if (card.Ad == null && card.ManId < 0)
                failures.Add("CONN-001: the card names nobody.");
            if (card.Choices[0].Cost < 0 || string.IsNullOrEmpty(card.Choices[0].Note))
                failures.Add("CONN-001: the SIGN HIM row does not explain itself.");
            if ((card.Id == CardId.FieldMan) != (card.Line == ConnectionLine.Field))
                failures.Add("CONN-001: the card does not match its line.");
        }

        static void SigningTheManOpensTheStage(List<string> failures)
        {
            var rig = new Rig(6);
            var house = rig.House(1);
            house.Runner.Accounts.Safe = 100_000;
            var card = DealTheMan(rig, 1);
            if (card == null || card.IsWire)
            {
                failures.Add("CONN-001: no card to sign from.");
                return;
            }
            var men = house.Roster.Members.Count;
            var refusal = rig.Carry(1, HouseIntent.Choose(card, 0, HouseMind.TierCollect, "sign"));
            if (!string.IsNullOrEmpty(refusal))
            {
                failures.Add("CONN-001: SIGN HIM was refused: " + refusal);
                return;
            }
            var connection = house.Runner.Connection;
            if (connection.Stage != ConnectionStage.PortMan || connection.ManId < 0)
                failures.Add("CONN-001: signing did not open the stage (" + connection.Stage + ").");
            var man = house.Roster.Find(connection.ManId);
            if (man == null || house.Roster.CrewOf(man.Id) == null)
                failures.Add("CONN-001: the signed man is not standing in a crew.");
            if (card.Ad != null && house.Roster.Members.Count != men + 1)
                failures.Add("CONN-001: the dealt man did not land on the books.");
            if (rig.World.TheManSigned != 1)
                failures.Add("CONN-001: the city did not count the signing.");
            if (house.Runner.Events.Pending != null)
                failures.Add("CONN-001: the card is still on the table after the answer.");
            if (Backgrounds.Of(house.Roster.Seed, connection.ManId, -1, connection) !=
                connection.ManTrade || connection.ManTrade == Background.None)
                failures.Add("CONN-001: the signed man's trade does not read off the paper.");
        }

        static void ExactlyOneDirectManPerCity(List<string> failures)
        {
            var rig = new Rig(8);
            rig.World.DirectTurn = 2;
            var direct = new List<int>();
            for (var g = 1; g < 3; g++)
            {
                var house = rig.House(g);
                house.Runner.Accounts.Safe = 100_000;
                var card = DealTheMan(rig, g, 3 + g);
                if (card == null || card.IsWire)
                {
                    failures.Add("CONN-001: house " + g + " got no card to sign from.");
                    continue;
                }
                var refusal = rig.Carry(g, HouseIntent.Choose(card, 0, HouseMind.TierCollect, "sign"));
                if (!string.IsNullOrEmpty(refusal))
                    failures.Add("CONN-001: house " + g + " could not sign: " + refusal);
                if (rig.World.DirectManId >= 0 && !direct.Contains(rig.World.DirectManId))
                    direct.Add(rig.World.DirectManId);
            }
            if (direct.Count != 1)
                failures.Add("CONN-001: " + direct.Count + " Direct men were bound, not one.");
            if (rig.World.DirectManId != rig.House(2).Runner.Connection.ManId)
                failures.Add("CONN-001: the second signing did not bind Pablo's man.");
            if (Backgrounds.Of(0, rig.World.DirectManId, rig.World.DirectManId) != Background.Direct)
                failures.Add("CONN-001: Pablo's man does not read Direct.");

            // Unsigned, he moves on: a declined turn is not before thirty days.
            var later = new Rig(9);
            later.World.DirectTurn = 1;
            later.World.DirectDeclined(5);
            if (later.World.NextSigningIsDirect(10) || !later.World.NextSigningIsDirect(35))
                failures.Add("CONN-001: a declined turn did not wait thirty days.");
        }

        static void TheCellFiresOnReleaseNeverInside(List<string> failures)
        {
            var rig = new Rig(10);
            var house = rig.House(1);
            house.Runner.Accounts.Safe = 100_000;
            house.Runner.Connection.Paths = 1 << (int)ConnectionPath.Cell;
            var roster = house.Roster;
            var day = rig.Day;
            Character hood = null;
            for (var i = 0; i < roster.Members.Count && hood == null; i++)
                if (roster.Members[i].Rank == Rank.Hood && roster.Members[i].Specialty == Specialty.None)
                    hood = roster.Members[i];
            if (hood == null)
            {
                failures.Add("CONN-001: no hood to put in a cell.");
                return;
            }
            roster.Day = day;
            RosterOps.Jail(roster, hood.Id, day + 2, "held", "Affray", "");
            var inside = TheMan.Deal(rig.Look(1), rig.Ctx(1), 5);
            if (inside == null || !inside.IsWire)
                failures.Add("CONN-001: THE CELL produced a man while he was inside.");
            else if (string.IsNullOrEmpty(inside.Lines[0]))
                failures.Add("CONN-001: the cold line is empty.");

            RosterOps.Discharge(roster, day + 2);
            if (hood.ReleasedOnDay != day + 2 || hood.NightsInside != 2)
                failures.Add("CONN-001: the cell did not remember his nights (" +
                             hood.NightsInside + ").");
            house.Runner.Campaign.Day = day + 2;
            var released = TheMan.Deal(rig.Look(1), rig.Ctx(1), 5);
            if (released == null || released.IsWire || released.Path != ConnectionPath.Cell ||
                released.Ad == null)
                failures.Add("CONN-001: THE CELL did not fire on the day of release.");
            else if (!released.Lines[0].Contains(hood.FirstName))
                failures.Add("CONN-001: the cell card does not name the man who did the nights.");
        }

        static void TheBrokerIsNamedAndTheMeetingIsFiled(List<string> failures)
        {
            var reached = false;
            for (var seed = 11; seed <= 16 && !reached; seed++)
            {
                var rig = new Rig(seed);
                var house = rig.House(1);
                house.Runner.Accounts.Safe = 100_000;
                var card = DealTheMan(rig, 1);
                if (card == null || card.IsWire)
                    continue;
                rig.Carry(1, HouseIntent.Choose(card, 0, HouseMind.TierCollect, "sign"));
                rig.NameInThePaper(1);
                var connection = house.Runner.Connection;
                for (var day = 0; day < 8 && house.Runner.Events.Pending == null; day++)
                {
                    rig.Midnight();
                    house.Runner.Accounts.Safe = 100_000;
                    rig.NameInThePaper(1);
                }
                var pending = house.Runner.Events.Pending;
                if (pending == null || pending.Id != CardId.BrokerRumour)
                {
                    failures.Add("CONN-002: seed " + seed + " never dealt the broker's card (stage " +
                                 connection.Stage + ").");
                    continue;
                }
                if (connection.Stage != ConnectionStage.Rumour)
                    failures.Add("CONN-002: the rumour did not set the stage.");
                var spoken = StreetEvents.CardOf(house.Runner.Events, rig.Look(1), rig.Ctx(1),
                    ConnectionEvents.Defs);
                var meet = ChoiceOf(spoken, "MEET THE MAN");
                if (meet < 0 || !spoken.Choices[meet].NeedsCrew ||
                    spoken.Choices[meet].Cost != Connection.BrokerFee ||
                    string.IsNullOrEmpty(spoken.Choices[meet].Risk))
                {
                    failures.Add("CONN-002: MEET THE MAN does not explain itself.");
                    continue;
                }
                var refusal = rig.Carry(1, HouseIntent.Choose(spoken, meet, HouseMind.TierCollect, "meet"));
                if (!string.IsNullOrEmpty(refusal))
                {
                    failures.Add("CONN-002: the meeting was refused: " + refusal);
                    continue;
                }
                var filed = house.Runner.Book.CurrentFor(spoken.CrewId);
                if (filed == null || filed.Type != OrderType.Meet)
                {
                    failures.Add("CONN-002: no Meet job on the book.");
                    continue;
                }
                rig.Hours(12f);
                if (connection.MeetAttempts != 1)
                    failures.Add("CONN-002: the meeting did not resolve inside twelve hours.");
                var record = house.Runner.Records.Count > 0 ? house.Runner.Records[0] : null;
                if (record == null || record.Type != OrderType.Meet ||
                    record.Money != -Connection.BrokerFee)
                    failures.Add("CONN-002: the fee did not leave the safe on arrival (" +
                                 (record != null ? record.Money.ToString() : "no record") + ").");
                if (connection.Stage == ConnectionStage.Contact)
                {
                    reached = true;
                    rig.Midnight();
                    var next = house.Runner.Events.Pending;
                    if (next == null || next.Id != CardId.TestBuy)
                        failures.Add("CONN-002: Contact did not deal the test buy next tick.");
                }
                else if (connection.CoolUntilDay <= rig.Day && connection.Stage != ConnectionStage.Rumour)
                    failures.Add("CONN-002: a failed meeting neither cooled nor left the stage.");
            }
            if (!reached)
                failures.Add("CONN-002: six seeds and no meeting ever reached Contact.");
        }

        /// <summary>Puts a house at Contact by hand: a man signed, the broker named.</summary>
        static Rig AtContact(int seed, out House house)
        {
            var rig = new Rig(seed);
            house = rig.House(1);
            house.Runner.Accounts.Safe = 200_000;
            var card = DealTheMan(rig, 1);
            rig.Carry(1, HouseIntent.Choose(card, 0, HouseMind.TierCollect, "sign"));
            house.Runner.Connection.NamedTheBroker(rig.Day);
            house.Runner.Connection.Met(ConnectionOutcome.Contact, rig.Day);
            return rig;
        }

        static void TheTestBuyHoldsWithoutARoomAndTheMindLeases(List<string> failures)
        {
            var rig = AtContact(20, out var house);
            rig.Midnight();
            house.Runner.Accounts.Safe = 200_000;
            var pending = house.Runner.Events.Pending;
            if (pending == null || pending.Id != CardId.TestBuy)
            {
                failures.Add("CONN-003: no test buy on the table at Contact (" +
                             (pending != null ? pending.Id.ToString() : "nothing") + ").");
                return;
            }
            if (pending.Hold != HoldReason.NoRoom)
                failures.Add("CONN-003: the test buy is not held for NoRoom but " + pending.Hold);

            // The mind leases the room this think, before any Walk tier.
            rig.Think(1);
            var lease = -1;
            for (var i = 0; i < rig.Intents.Count; i++)
                if (rig.Intents[i].Kind == HouseIntentKind.Lease &&
                    rig.Intents[i].Role == UnitRole.Stash)
                    lease = i;
            if (lease < 0)
            {
                failures.Add("STREET-003: a held NoRoom card did not make the mind lease a Stash.");
                return;
            }
            var refusal = rig.Carry(1, rig.Intents[lease]);
            if (!string.IsNullOrEmpty(refusal))
            {
                failures.Add("PRE-001: the lease was refused: " + refusal);
                return;
            }
            var room = StashRoom.Of(1, house.Roster, rig.Day);
            if (!room.IsValid)
            {
                failures.Add("PRE-001: no open Stash room after the lease.");
                return;
            }
            var hold = StreetEvents.HoldOf(house.Runner.Events, rig.Look(1), rig.Ctx(1),
                ConnectionEvents.Defs);
            if (hold != HoldReason.None)
                failures.Add("CONN-003: the card is still held for " + hold + " with a room standing.");

            // And answered the next think, with the row the safe covers best.
            rig.Think(1);
            var choice = -1;
            for (var i = 0; i < rig.Intents.Count; i++)
                if (rig.Intents[i].Kind == HouseIntentKind.Card)
                    choice = i;
            if (choice < 0)
            {
                failures.Add("STREET-003: the mind did not answer the test buy once the room stood.");
                return;
            }
            refusal = rig.Carry(1, rig.Intents[choice]);
            if (!string.IsNullOrEmpty(refusal))
            {
                failures.Add("CONN-003: the buy was refused: " + refusal);
                return;
            }
            rig.Hours(12f);
            var connection = house.Runner.Connection;
            if (connection.BuyAttempts != 1 || connection.Stage != ConnectionStage.Tested)
                failures.Add("CONN-003: the buy did not resolve to Tested (" + connection.Stage + ").");
            if (connection.Kilos < 1 || connection.Kilos > 2 ||
                (connection.Kilos == 2 && connection.Trust != Connection.TrustGood) ||
                (connection.Kilos == 1 && connection.Trust != Connection.TrustShort))
                failures.Add("CONN-003: Good/Short did not put the right kilos and trust in " +
                             "(" + connection.Kilos + "/" + connection.Trust + ").");
            // The room reads its kilos: hot as what is in it.
            var report = new FlatDayReport();
            FlatDay.Tick(house.Roster, 1, rig.Day, 20, house.Runner.Accounts, null, report,
                connection.Kilos);
            if (report.Heat.Count != 1 ||
                report.Heat[0].Heat != (1 + connection.Kilos) * FlatDay.HeatPerDayScale)
                failures.Add("CONN-003: the Stash's heat is not read off the kilos.");
        }

        static void TheStingSeizesThePaymentOnlyAndBurns(List<string> failures)
        {
            var rig = AtContact(21, out var house);
            var connection = house.Runner.Connection;
            connection.Trust = -100;   // chance = 0.5 + 0.5 = 1: the sting is certain
            CampaignRunner.WatchOnTheDoor = _ => 100f;
            var stung = 0;
            CampaignRunner.StungOnTheStreet = (g, job) => { stung++; return false; };

            var crew = house.Roster.Crews[0];
            var job = new Job
            {
                Type = OrderType.TestBuy, CrewId = crew.Id, GangId = 1, Men = 2,
                TargetBusinessId = rig.City.Door(rig.City.HomeBlockOf(1), 1).Value,
                TargetLabel = "the bar", TargetWorth = Connection.TestBuyPrice,
            };
            var issued = rig.World.Issue(job);
            if (!issued.Ok)
            {
                failures.Add("CONN-003: the buy was refused: " + issued.Reason);
                return;
            }
            rig.Hours(12f);
            var record = house.Runner.Records.Count > 0 ? house.Runner.Records[0] : null;
            if (connection.Stage != ConnectionStage.Burned ||
                connection.BurnedUntilDay != rig.Day + Connection.BurnedDays)
                failures.Add("CONN-003: the sting did not burn the house for thirty days.");
            if (connection.Kilos != 0)
                failures.Add("CONN-003: a sting put kilos in the room.");
            if (record == null || record.Type != OrderType.TestBuy ||
                record.Money != -Connection.TestBuyPrice)
                failures.Add("CONN-003: the sting took " +
                             (record != null ? record.Money.ToString() : "no record") +
                             ", not the payment only.");
            if (stung != 1)
                failures.Add("CONN-003: the street was not asked to take the collar.");
            var jailed = 0;
            for (var i = 0; i < house.Roster.Members.Count; i++)
                if (house.Roster.Members[i].Status == CharacterStatus.Jailed)
                    jailed++;
            if (jailed != 2)
                failures.Add("CONN-003: with no street " + jailed + " men were taken, not the two who walked.");
            CampaignRunner.WatchOnTheDoor = null;
            CampaignRunner.StungOnTheStreet = null;

            // Attention under the threshold never stings, however low the trust.
            var quiet = AtContact(22, out var quietHouse);
            quietHouse.Runner.Connection.Trust = -100;
            CampaignRunner.WatchOnTheDoor = _ => 10f;
            var buy = new Job
            {
                Type = OrderType.TestBuy, CrewId = quietHouse.Roster.Crews[0].Id, GangId = 1,
                Men = 2, TargetBusinessId = quiet.City.Door(quiet.City.HomeBlockOf(1), 1).Value,
                TargetLabel = "the bar", TargetWorth = Connection.TestBuyPrice,
            };
            quiet.World.Issue(buy);
            quiet.Hours(12f);
            if (quietHouse.Runner.Connection.Stage == ConnectionStage.Burned)
                failures.Add("CONN-003: a quiet door stung.");
            CampaignRunner.WatchOnTheDoor = null;
        }

        static void ARaidSeizesAndSealsWithoutACase(List<string> failures)
        {
            var connection = new Connection { Stage = ConnectionStage.Tested, Trust = 40, Kilos = 5 };
            connection.Raided(12);
            if (connection.Kilos != 0 || connection.Trust != 20 ||
                connection.Stage != ConnectionStage.Tested)
                failures.Add("CONN-003: a raid did not seize the kilos and take twenty trust.");
            var burned = new Connection { Stage = ConnectionStage.Tested, Trust = 10, Kilos = 1 };
            burned.Raided(12);
            if (burned.Stage != ConnectionStage.Burned)
                failures.Add("CONN-003: trust under nought after a raid did not burn.");
        }

        static void SoldKilosAreDirtyAndCapped(List<string> failures)
        {
            var accounts = new Accounts { Safe = 1_000 };
            var connection = new Connection { Line = ConnectionLine.Port, Trust = 40, Kilos = 8 };
            connection.Accepted(SupplierGrade.Broker, 10);
            connection.LastLoadDay = 10;
            var money = connection.Sell(accounts, 12, out var sold);
            if (sold != 5 || money != 5 * Connection.BuyerPrice || connection.Kilos != 3)
                failures.Add("CONN-004: the buyer took " + sold + " for " + money + "; five at the flat price was meant.");
            if (accounts.RiskyMoney < money)
                failures.Add("CONN-004: sold kilos are not dirty money.");
            if (connection.Trust != 45)
                failures.Add("CONN-004: a sale on time did not add five trust.");
            connection.Sell(accounts, 13, out var again);
            if (again != 0)
                failures.Add("CONN-004: the buyer took more than his week's capacity.");
            if (connection.OutletForNextKilo(13) != 0 || connection.OutletForNextKilo(20) != Connection.BuyerPrice)
                failures.Add("CONN-004: the outlet for the next kilo is wrong.");
        }

        static void TermsDifferByLineAndGrade(List<string> failures)
        {
            if (Connection.MinLoadFor(ConnectionLine.Port, SupplierGrade.Broker) != 5 ||
                Connection.MinLoadFor(ConnectionLine.Field, SupplierGrade.Broker) != 2 ||
                Connection.MinLoadFor(ConnectionLine.Port, SupplierGrade.Direct) != 10)
                failures.Add("CONN-004: MinLoad is not 5 / 2 / 10.");
            if (Connection.PriceFor(40, SupplierGrade.Broker) != 13_440 ||
                Connection.PriceFor(40, SupplierGrade.Direct) != 10_752)
                failures.Add("CONN-004: the price per kilo is not KiloPrice less trust/10 per cent, a fifth off Direct.");
            if (Connection.CreditAt(59, SupplierGrade.Broker) || !Connection.CreditAt(60, SupplierGrade.Broker) ||
                !Connection.CreditAt(40, SupplierGrade.Direct))
                failures.Add("CONN-004: credit is not at 60 (Broker) / 40 (Direct).");
        }

        static void ALoadLandsOnItsDayAndAgainSevenOn(List<string> failures)
        {
            var accounts = new Accounts { Safe = 500_000 };
            var connection = new Connection { Line = ConnectionLine.Port, Trust = 40, Stage = ConnectionStage.Tested };
            connection.Accepted(SupplierGrade.Broker, 10);
            if (connection.Stage != ConnectionStage.Supplier || connection.NextLoadDay != 17)
                failures.Add("CONN-004: accepting did not set Supplier and the next load day.");
            if (connection.Load(accounts, 16, true) != "")
                failures.Add("CONN-004: a load landed before its day.");
            var line = connection.Load(accounts, 17, true);
            if (connection.Kilos != 5 || connection.NextLoadDay != 24 ||
                accounts.Safe != 500_000 - 5 * connection.PricePerKilo || !line.Contains("boat"))
                failures.Add("CONN-004: the load did not land as the terms say (" + line + ").");
            var held = connection.Load(accounts, 24, false);
            if (connection.Kilos != 5 || connection.Trust != 35 || connection.NextLoadDay != 25 ||
                !held.Contains("held"))
                failures.Add("CONN-004: a load with no room was not held with trust down (" + held + ").");
            connection.Load(accounts, 25, false);
            if (connection.Trust != 35)
                failures.Add("CONN-004: a held load took trust twice.");
        }

        static void FourteenDaysWithoutTheManDropsAStageBeforeSupplierOnly(List<string> failures)
        {
            var rig = new Rig(30);
            var house = rig.House(1);
            var roster = house.Roster;
            var connection = house.Runner.Connection;
            var hood = roster.Members[roster.Members.Count - 1];
            connection.Signed(hood.Id, ConnectionLine.Port, Background.Docker, 1);
            connection.NamedTheBroker(1);
            connection.Met(ConnectionOutcome.Contact, 1);
            RosterOps.Kill(roster, hood.Id);
            var first = connection.DayTick(roster, 2);
            if (connection.WithoutManSinceDay != 2 || !first.Contains("gone"))
                failures.Add("CONN-001: the absence was not noticed (" + first + ").");
            for (var day = 3; day < 16; day++)
                connection.DayTick(roster, day);
            if (connection.Stage != ConnectionStage.Contact)
                failures.Add("CONN-001: the stage dropped before fourteen days.");
            var dropped = connection.DayTick(roster, 16);
            if (connection.Stage != ConnectionStage.Rumour || !dropped.Contains("Fourteen"))
                failures.Add("CONN-001: fourteen days without the man did not drop one stage (" +
                             connection.Stage + ").");
            connection.Replaced(roster.Members[0].Id, Background.Sailor);
            if (connection.WithoutManSinceDay != 0 || connection.Stage != ConnectionStage.Rumour)
                failures.Add("CONN-001: a replacement did not resume at the stage held.");

            // After Supplier the loss changes nothing: not the stage, not the trust,
            // not the terms, not the next load.
            connection.Stage = ConnectionStage.Tested;
            connection.Trust = 40;
            connection.Accepted(SupplierGrade.Broker, 20);
            RosterOps.Kill(roster, roster.Members[0].Id);
            var terms = connection.PricePerKilo;
            for (var day = 21; day < 60; day++)
                if (connection.DayTick(roster, day) != "")
                    failures.Add("CONN-004: the paper spoke of the introducer after Supplier on day " + day);
            if (connection.Stage != ConnectionStage.Supplier || connection.Trust != 40 ||
                connection.PricePerKilo != terms || connection.NextLoadDay != 27 ||
                connection.WithoutManSinceDay != 0)
                failures.Add("CONN-004: losing the introducer after Supplier changed the relationship.");
        }

        static void ARoundTripKeepsTheConnectionAndTheBook(List<string> failures)
        {
            var rig = new Rig(40);
            var house = rig.House(1);
            house.Runner.Accounts.Safe = 100_000;
            var card = DealTheMan(rig, 1);
            rig.Carry(1, HouseIntent.Choose(card, 0, HouseMind.TierCollect, "sign"));
            var connection = house.Runner.Connection;
            connection.NamedTheBroker(2);
            connection.Met(ConnectionOutcome.Contact, 3);
            connection.Bought(ConnectionOutcome.Good, 4);
            connection.Accepted(SupplierGrade.Broker, 5);
            connection.Kilos = 7;
            rig.World.DirectTurn = 3;
            rig.World.DirectNotBeforeDay = 44;
            var book = house.Runner.Events;
            book.Pots[EventId.TheMan] = 0.4f;
            book.Say(5, "a line on the wire");
            var pendingBefore = new PendingCard
            {
                Id = CardId.SupplierTerms, Def = EventId.SupplierTerms, DealtDay = 5,
                ExpiresDay = 8, Speaker = 3, Hold = HoldReason.NoCrew,
            };
            book.Pending = pendingBefore;

            var dto = OutfitSnapshot.Snapshot(rig.World);
            var json = JsonUtility.ToJson(dto);
            var read = JsonUtility.FromJson<UnderworldDto>(json);
            var other = Underworld.Deal(40, 3);
            OutfitSnapshot.Restore(other, read);
            var back = other.Of(1).Runner.Connection;
            if (back.Stage != ConnectionStage.Supplier || back.Grade != SupplierGrade.Broker ||
                back.Kilos != 7 || back.Trust != connection.Trust || back.ManId != connection.ManId ||
                back.ManTrade != connection.ManTrade || back.PricePerKilo != connection.PricePerKilo ||
                back.NextLoadDay != connection.NextLoadDay || back.Line != connection.Line ||
                back.Paths != connection.Paths)
                failures.Add("CONN-005: the connection did not come back the same.");
            if (other.DirectTurn != 3 || other.DirectNotBeforeDay != 44 ||
                other.TheManSigned != rig.World.TheManSigned)
                failures.Add("CONN-005: Pablo's turn did not come back.");
            var backBook = other.Of(1).Runner.Events;
            if (backBook.Pending == null || backBook.Pending.Id != CardId.SupplierTerms ||
                backBook.Pending.Hold != HoldReason.NoCrew || backBook.Pending.ExpiresDay != 8 ||
                backBook.PotOf(EventId.TheMan) != 0.4f || backBook.Wire.Count != 1 ||
                backBook.CardsDealt != 1)
                failures.Add("CONN-005: the event book did not come back the same.");
            if (!json.Contains("\"connection\"") || !json.Contains("\"events\""))
                failures.Add("CONN-005: the file carries no connection or events block.");

            // A file with no block reads None with an empty book.
            read.houses[1].connection = null;
            read.houses[1].events = null;
            var bare = Underworld.Deal(40, 3);
            OutfitSnapshot.Restore(bare, read);
            if (bare.Of(1).Runner.Connection.Stage != ConnectionStage.None ||
                bare.Of(1).Runner.Connection.Grade != SupplierGrade.None ||
                bare.Of(1).Runner.Events.Pending != null)
                failures.Add("CONN-005: a file with no connection block did not read None.");
        }

        static void WithACardPendingTheMindAnswersBeforeWalk(List<string> failures)
        {
            var rig = new Rig(50);
            var house = rig.House(1);
            house.Runner.Accounts.Safe = 100_000;
            var card = DealTheMan(rig, 1);
            if (card == null || card.IsWire)
            {
                failures.Add("STREET-003: no card to answer.");
                return;
            }
            var view = rig.Look(1);
            if (view.Card == null)
            {
                failures.Add("STREET-003: the view does not carry the card on the table.");
                return;
            }
            rig.Think(1);
            var choice = -1;
            var walkTier = -1;
            for (var i = 0; i < rig.Intents.Count; i++)
            {
                if (rig.Intents[i].Kind == HouseIntentKind.Card && choice < 0)
                    choice = i;
                else if (rig.Intents[i].Tier > HouseMind.TierCollect && walkTier < 0)
                    walkTier = i;
            }
            if (choice < 0)
                failures.Add("STREET-003: the mind did not answer the pending card.");
            else if (walkTier >= 0 && walkTier < choice)
                failures.Add("STREET-003: a Walk tier was proposed before the card.");

            // With the safe under every priced row it proposes nothing and the card holds.
            house.Runner.Accounts.Safe = 0;
            rig.Think(1);
            for (var i = 0; i < rig.Intents.Count; i++)
                if (rig.Intents[i].Kind == HouseIntentKind.Card && rig.Intents[i].Price > 0)
                    failures.Add("STREET-003: the mind chose a row the safe could not cover.");
            if (house.Runner.Events.Pending == null)
                failures.Add("STREET-003: the card left the table unanswered.");
        }

        static void WalkAwayIsChosenWhenItsAppealIsHighest(List<string> failures)
        {
            var rig = new Rig(51);
            var house = rig.House(1);
            // The terms' def holds nothing, so the row's appeal alone decides.
            var card = new EventCard
            {
                Id = CardId.SupplierTerms, Def = EventId.SupplierTerms, Speaker = house.Roster.BossId,
                SpeakerName = "TEST", Title = "TEST", DealtDay = rig.Day, ExpiresDay = rig.Day + 3,
            };
            card.Choices.Add(new EventChoice
            {
                Label = "PAY", Cost = 10, Appeal = _ => 0.2f,
                Intent = HouseIntent.SellKilos(HouseMind.TierCollect, "pay"),
            });
            card.Choices.Add(new EventChoice { Label = "WALK AWAY", Cost = 0, Appeal = _ => 0.9f });
            card.Choices[1].Intent = HouseIntent.Choose(card, 1, HouseMind.TierCollect, "walk");
            var book = house.Runner.Events;
            book.Pending = new PendingCard
            {
                Id = card.Id, Def = card.Def, DealtDay = card.DealtDay, ExpiresDay = card.ExpiresDay,
                Speaker = card.Speaker,
            };
            book.Spoken = card;
            house.Runner.Connection.Stage = ConnectionStage.Tested;
            rig.Think(1);
            var chosen = -1;
            for (var i = 0; i < rig.Intents.Count; i++)
                if (rig.Intents[i].Kind == HouseIntentKind.Card)
                    chosen = rig.Intents[i].CharacterId;
            if (chosen != 1)
                failures.Add("STREET-003: WALK AWAY with the highest appeal was not chosen (" + chosen + ").");
        }

        static void TraffickingIsFifteenToThirtyAndBindsAHood(List<string> failures)
        {
            if (Sentencing.BandLow(Deed.Trafficking) != 15 || Sentencing.BandHigh(Deed.Trafficking) != 30)
                failures.Add("CONN-003: Trafficking is not 15-30.");
            var rng = new System.Random(1);
            for (var i = 0; i < 50; i++)
            {
                var days = Sentencing.Days(Deed.Trafficking, rng, false, Rank.Hood, false, 5, 0);
                if (days < 15)
                    failures.Add("CONN-003: a hood with a lawyer served " + days + "; the minimum binds.");
            }
            if (!Sentencing.ChargeFor(Deed.Trafficking).Contains("400 grams"))
                failures.Add("CONN-003: the rap sheet's words are wrong.");
        }
    }

    /// <summary>
    /// THE PROBE (CONN-005): the paper campaign, one row per house per day - the
    /// signals in the page's own words, the gate, the pot, the stage, the man, the
    /// card on the table and its hold, and the answer. What the user rules the
    /// thresholds on.
    /// </summary>
    public static class ConnectionProbe
    {
        public static List<string> Run(int seed, int days, int houses, int gang = -1)
        {
            var lines = new List<string>();
            var rig = new ConnectionTests.Rig(seed, houses);
            var relations = rig.World.Relations.Config;
            for (var hour = 1; hour <= days * 24; hour++)
            {
                rig.City.Hour += 1.0;
                rig.World.AdvanceHours(1f);
                rig.World.Think(rig.City.Hour, rig.Config.ThinkEveryHours, house =>
                {
                    HouseMind.Think(rig.Look(house.GangId), rig.Config, relations, rig.Intents);
                    for (var i = 0; i < rig.Intents.Count && i < rig.Config.MaxIntentsPerThink; i++)
                        rig.Carry(house.GangId, rig.Intents[i]);
                }, houses);
                if (hour % 24 != 0)
                    continue;
                rig.World.DayTick();
                rig.City.RollTheStreet(rig.World, rig.Racket, rig.Dues, rig.Config, rig.Rounds);
                for (var g = 0; g < houses; g++)
                {
                    if (gang >= 0 && g != gang)
                        continue;
                    var house = rig.House(g);
                    var view = rig.Look(g);
                    var ctx = rig.Ctx(g);
                    var book = house.Runner.Events;
                    var connection = house.Runner.Connection;
                    var signals = ConnectionScore.Signals(view, ctx);
                    var man = house.Roster.Find(connection.ManId);
                    var row = "seed " + seed + " day " + rig.Day + " house " + g +
                              " safe " + house.Runner.Accounts.Safe;
                    for (var s = 0; s < signals.Count; s++)
                        row += " · " + signals[s].Name + " " + signals[s].Value.ToString("0.00") +
                               " (" + signals[s].Line + ")";
                    row += " · pot " + book.PotOf(EventId.TheMan).ToString("0.00") + "/" +
                           book.PotOf(EventId.BrokerRumour).ToString("0.00") +
                           " · stage " + connection.Stage +
                           (connection.Grade != SupplierGrade.None ? " " + connection.Grade : "") +
                           " · man " + (man != null ? man.FullName + " (" + Backgrounds.Word(
                               Backgrounds.Of(house.Roster.Seed, man.Id, rig.World.DirectManId,
                                   connection)) + ")" : "-") +
                           " · kilos " + connection.Kilos + " · trust " + connection.Trust +
                           " · card " + (book.Pending != null
                               ? book.Pending.Id + (book.Pending.Hold != HoldReason.None
                                   ? " HELD: " + HoldReasons.Line(book.Pending.Hold) + " - " +
                                     HoldReasons.Clears(book.Pending.Hold)
                                   : "")
                               : "-") +
                           " · answered " + (string.IsNullOrEmpty(book.LastAnswer) ? "-" : book.LastAnswer) +
                           " · dealt/answered/expired " + book.CardsDealt + "/" + book.CardsAnswered +
                           "/" + book.CardsExpired;
                    if (book.Wire.Count > 0 && book.Wire[book.Wire.Count - 1].Day == rig.Day)
                        row += " · wire: " + book.Wire[book.Wire.Count - 1].Text;
                    lines.Add(row);
                }
            }
            return lines;
        }
    }
}
