using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 40, STREET-001: the street event book, pure. The pot is monotone and
    /// deterministic; nothing is dealt against a deal gate; a card is dealt against a
    /// hold reason and waits; a held card expires on day +3 and cools; one card a day;
    /// a card with no speaker is not dealt; Answer returns the intent and records it;
    /// Esc leaves Pending unchanged; every hold reason has a line and what clears it.
    /// </summary>
    public static class StreetEventTests
    {
        static readonly (string Name, System.Action<List<string>> Check)[] Contracts =
        {
            ("ThePotIsMonotoneAndDeterministic", ThePotIsMonotoneAndDeterministic),
            ("NothingIsDealtAgainstAGate", NothingIsDealtAgainstAGate),
            ("ACardIsDealtAgainstAHoldAndWaits", ACardIsDealtAgainstAHoldAndWaits),
            ("AHeldCardExpiresOnDayPlusThreeAndCools", AHeldCardExpiresOnDayPlusThreeAndCools),
            ("OneCardADayAndTheFullestPotWins", OneCardADayAndTheFullestPotWins),
            ("ACardWithNoSpeakerIsNotDealt", ACardWithNoSpeakerIsNotDealt),
            ("AnswerReturnsTheIntentAndRecordsIt", AnswerReturnsTheIntentAndRecordsIt),
            ("EscLeavesPendingUnchanged", EscLeavesPendingUnchanged),
            ("EveryHoldReasonHasALineAndAClears", EveryHoldReasonHasALineAndAClears),
            ("AWireSaysItsLineAndCools", AWireSaysItsLineAndCools),
            ("TheSameTwentyEightDaysFireTheSameCards", TheSameTwentyEightDaysFireTheSameCards),
            ("TheDayPassRollsEveryHouseThePlayerIncluded", TheDayPassRollsEveryHouseThePlayerIncluded),
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
                Contracts[i].Check(failures);
            return failures;
        }

        // ------------------------------------------------------------------ the rig

        /// <summary>A view with books and nothing else: the pure book needs a roster
        /// (for a speaker), a safe, and a day.</summary>
        static HouseView View(int seed, int day, int safe = 30_000)
        {
            var roster = RosterSeeder.Generate(seed, 1);
            RosterOps.ConfigureOrganization(roster, OrganizationLimits.Default);
            var accounts = new Accounts { Safe = safe };
            return new HouseView
            {
                House = new TerritoryGangId(1),
                Roster = roster,
                Accounts = accounts,
                Book = new OrderBook(),
                Day = day,
                GameHour = day * 24.0,
            };
        }

        static EventContext Context(int seed, int day) => new EventContext
        {
            CitySeed = seed,
            GangId = 1,
            RosterSeed = seed,
            Day = day,
            Connection = new Connection(),
        };

        static EventDef Def(EventId id, float score, float threshold, int cooldown = 0,
            HoldReason gate = HoldReason.None, HoldReason hold = HoldReason.None,
            bool wire = false, bool speaker = true)
        {
            return new EventDef
            {
                Id = id,
                Name = id.ToString(),
                Threshold = threshold,
                CooldownDays = cooldown,
                Score = (v, c) => score,
                Gate = (v, c) => gate,
                Hold = (v, c) => hold,
                PotLine = c => "talking",
                Deal = (v, c, s) =>
                {
                    var card = new EventCard
                    {
                        Id = (CardId)(int)id,
                        Def = id,
                        Speaker = speaker ? v.Roster.BossId : -1,
                        SpeakerName = "TEST",
                        Title = id.ToString(),
                    };
                    card.Lines.Add("a line, seed " + s);
                    if (!wire)
                    {
                        card.Choices.Add(new EventChoice
                        {
                            Label = "YES", Cost = 100, Appeal = _ => 0.6f,
                            Intent = HouseIntent.SellKilos(HouseMind.TierCollect, "yes"),
                        });
                        card.Choices.Add(new EventChoice
                        {
                            Label = "WALK AWAY", Cost = 0, Appeal = _ => 0.3f,
                        });
                        card.Choices[1].Intent = HouseIntent.Choose(card, 1,
                            HouseMind.TierCollect, "walk");
                    }
                    return card;
                },
            };
        }

        static List<EventDef> Defs(params EventDef[] defs) => new List<EventDef>(defs);

        // ------------------------------------------------------------ the contracts

        static void ThePotIsMonotoneAndDeterministic(List<string> failures)
        {
            // Score 0.7 over a 0.4 threshold adds (0.3 / 0.6) = 0.5 a day: the card
            // is dealt on the second day, and never before.
            var defs = Defs(Def(EventId.TheMan, 0.7f, 0.4f));
            int Fires()
            {
                var book = new EventBook();
                var last = 0f;
                for (var day = 1; day <= 10; day++)
                {
                    StreetEvents.Roll(View(11, day), book, Context(11, day), defs);
                    if (book.Pending != null)
                        return day;
                    var pot = book.PotOf(EventId.TheMan);
                    if (pot < last)
                        failures.Add("STREET-001: the pot went down from " + last + " to " + pot);
                    last = pot;
                }
                return -1;
            }
            var first = Fires();
            var second = Fires();
            if (first != 2)
                failures.Add("STREET-001: a pot fed 0.5 a day fired on day " + first + ", not 2.");
            if (first != second)
                failures.Add("STREET-001: the same books fired on day " + first + " and then " + second);
            if (StreetEvents.PotStep(0.4f, 0.4f) != 0f || StreetEvents.PotStep(1f, 0.4f) != 1f ||
                StreetEvents.PotStep(0.2f, 0.4f) != 0f)
                failures.Add("STREET-001: PotStep is not (s - t) / (1 - t) clamped.");
        }

        static void NothingIsDealtAgainstAGate(List<string> failures)
        {
            var defs = Defs(Def(EventId.TheMan, 1f, 0.4f, gate: HoldReason.Watched));
            var book = new EventBook();
            for (var day = 1; day <= 5; day++)
                StreetEvents.Roll(View(12, day), book, Context(12, day), defs);
            if (book.Pending != null)
                failures.Add("STREET-001: a card was dealt against the Watched gate.");
            if (book.PotOf(EventId.TheMan) < 1f)
                failures.Add("STREET-001: the pot did not keep filling behind the gate.");
            if (!HoldReasons.IsGate(HoldReason.Watched) || HoldReasons.IsGate(HoldReason.NoRoom) ||
                HoldReasons.IsGate(HoldReason.NoCrew) || !HoldReasons.IsGate(HoldReason.NoSpeaker))
                failures.Add("STREET-001: the gate / hold split is wrong.");
        }

        static void ACardIsDealtAgainstAHoldAndWaits(List<string> failures)
        {
            var defs = Defs(Def(EventId.TestBuy, 1f, 0f, hold: HoldReason.NoRoom));
            var book = new EventBook();
            StreetEvents.Roll(View(13, 1), book, Context(13, 1), defs);
            if (book.Pending == null)
            {
                failures.Add("STREET-001: a card was not dealt against the NoRoom hold.");
                return;
            }
            if (book.Pending.Hold != HoldReason.NoRoom)
                failures.Add("STREET-001: the pending card does not carry its hold reason.");
            if (book.Pending.ExpiresDay != 1 + StreetEvents.HoldDays)
                failures.Add("STREET-001: the card does not expire on day +3.");
            var hold = StreetEvents.HoldOf(book, View(13, 1), Context(13, 1), defs);
            if (hold != HoldReason.NoRoom)
                failures.Add("STREET-001: HoldOf did not re-read the hold.");
        }

        static void AHeldCardExpiresOnDayPlusThreeAndCools(List<string> failures)
        {
            var defs = Defs(Def(EventId.TestBuy, 1f, 0f, cooldown: 5, hold: HoldReason.NoRoom));
            var book = new EventBook();
            for (var day = 1; day <= 3; day++)
                StreetEvents.Roll(View(14, day), book, Context(14, day), defs);
            if (book.Pending == null)
            {
                failures.Add("STREET-001: the held card was gone before its day.");
                return;
            }
            StreetEvents.Roll(View(14, 4), book, Context(14, 4), defs);
            if (book.Pending != null)
                failures.Add("STREET-001: the held card did not expire on day +3.");
            if (book.CardsExpired != 1)
                failures.Add("STREET-001: the expiry was not counted.");
            if (!book.IsCooling(EventId.TestBuy, 5) || book.IsCooling(EventId.TestBuy, 9))
                failures.Add("STREET-001: the def did not cool for its five days.");
            if (book.Wire.Count == 0 || !book.Wire[0].Text.Contains("unanswered"))
                failures.Add("STREET-001: the expiry left no line on the wire.");
            // And nothing is re-dealt while it cools.
            StreetEvents.Roll(View(14, 5), book, Context(14, 5), defs);
            if (book.Pending != null)
                failures.Add("STREET-001: a cooling def was dealt again.");
        }

        static void OneCardADayAndTheFullestPotWins(List<string> failures)
        {
            var defs = Defs(Def(EventId.TheMan, 1f, 0f), Def(EventId.TestBuy, 1f, 0f));
            var book = new EventBook();
            StreetEvents.Roll(View(15, 1), book, Context(15, 1), defs);
            if (book.Pending == null || book.CardsDealt != 1)
                failures.Add("STREET-001: two full pots dealt " + book.CardsDealt + " cards in a day.");
            // The other pot stays full and waits for the table to clear.
            var other = book.Pending != null && book.Pending.Def == EventId.TheMan
                ? EventId.TestBuy : EventId.TheMan;
            if (book.PotOf(other) < 1f)
                failures.Add("STREET-001: the second pot was emptied without a deal.");
        }

        static void ACardWithNoSpeakerIsNotDealt(List<string> failures)
        {
            var defs = Defs(Def(EventId.TheMan, 1f, 0f, speaker: false));
            var book = new EventBook();
            StreetEvents.Roll(View(16, 1), book, Context(16, 1), defs);
            if (book.Pending != null)
                failures.Add("STREET-001: a card with no speaker was dealt.");
            if (book.PotOf(EventId.TheMan) < 1f)
                failures.Add("STREET-001: the pot was emptied though nobody brought the word.");
        }

        static void AnswerReturnsTheIntentAndRecordsIt(List<string> failures)
        {
            var defs = Defs(Def(EventId.TheMan, 1f, 0f));
            var book = new EventBook();
            var ctx = Context(17, 1);
            StreetEvents.Roll(View(17, 1), book, ctx, defs);
            var card = StreetEvents.CardOf(book, View(17, 1), ctx, defs);
            if (card == null)
            {
                failures.Add("STREET-001: no card to answer.");
                return;
            }
            var intent = StreetEvents.Answer(book, card, 0, ctx);
            if (intent.Kind != HouseIntentKind.Sell)
                failures.Add("STREET-001: Answer did not hand back the row's intent.");
            if (book.Pending != null || book.CardsAnswered != 1 ||
                !book.LastAnswer.StartsWith("PortMan/YES"))
                failures.Add("STREET-001: the answer was not recorded (" + book.LastAnswer + ").");
            if (!book.Fired.ContainsKey(EventId.TheMan))
                failures.Add("STREET-001: the def was not marked fired.");

            // WALK AWAY is a row like any other: its intent is a Card choice.
            StreetEvents.Roll(View(17, 2), new EventBook(), Context(17, 2), defs);
            var book2 = new EventBook();
            StreetEvents.Roll(View(17, 2), book2, Context(17, 2), defs);
            var card2 = StreetEvents.CardOf(book2, View(17, 2), Context(17, 2), defs);
            var walk = StreetEvents.Answer(book2, card2, 1, Context(17, 2));
            if (walk.Kind != HouseIntentKind.Card || !walk.Listing.EndsWith("WALK AWAY"))
                failures.Add("STREET-001: WALK AWAY is not a real answer (" + walk + ").");
        }

        static void EscLeavesPendingUnchanged(List<string> failures)
        {
            var defs = Defs(Def(EventId.TheMan, 1f, 0f));
            var book = new EventBook();
            var ctx = Context(18, 1);
            StreetEvents.Roll(View(18, 1), book, ctx, defs);
            var before = book.Pending;
            // Esc is nothing at all on the book: a re-read and a re-deal change nothing.
            StreetEvents.HoldOf(book, View(18, 1), ctx, defs);
            var spoken = StreetEvents.CardOf(book, View(18, 1), ctx, defs);
            book.Spoken = null;
            var again = StreetEvents.CardOf(book, View(18, 1), ctx, defs);
            if (book.Pending != before || before == null)
                failures.Add("STREET-001: reading the card moved the pending card.");
            if (spoken == null || again == null || spoken.Lines[0] != again.Lines[0] ||
                spoken.Choices.Count != again.Choices.Count)
                failures.Add("STREET-001: a re-deal of the pending card is not the same card.");
        }

        static void EveryHoldReasonHasALineAndAClears(List<string> failures)
        {
            foreach (HoldReason reason in System.Enum.GetValues(typeof(HoldReason)))
            {
                if (reason == HoldReason.None)
                    continue;
                if (string.IsNullOrEmpty(HoldReasons.Line(reason)) ||
                    string.IsNullOrEmpty(HoldReasons.Clears(reason)))
                    failures.Add("STREET-001: " + reason + " has no line or no clears.");
            }
        }

        static void AWireSaysItsLineAndCools(List<string> failures)
        {
            var fired = 0;
            var def = Def(EventId.BrokerRumour, 1f, 0f, cooldown: 3, wire: true);
            def.Fired = (v, c, card, book) => fired++;
            var defs = Defs(def);
            var book = new EventBook();
            var firings = new List<EventFiring>();
            StreetEvents.Roll(View(19, 1), book, Context(19, 1), defs, firings);
            if (fired != 1 || book.Pending != null || book.Wire.Count != 1)
                failures.Add("STREET-001: a wire did not fire as a wire (" + fired + "/" +
                             book.Wire.Count + ").");
            if (firings.Count != 1 || !firings[0].Wire)
                failures.Add("STREET-001: the wire was not reported to the caller.");
            StreetEvents.Roll(View(19, 2), book, Context(19, 2), defs, firings);
            if (fired != 1)
                failures.Add("STREET-001: a cooling wire fired again.");
        }

        static void TheSameTwentyEightDaysFireTheSameCards(List<string> failures)
        {
            var defs = Defs(Def(EventId.TheMan, 0.55f, 0.4f, cooldown: 2),
                Def(EventId.TestBuy, 0.5f, 0.4f, cooldown: 4));
            string Play()
            {
                var book = new EventBook();
                var log = "";
                for (var day = 1; day <= 28; day++)
                {
                    var ctx = Context(20, day);
                    StreetEvents.Roll(View(20, day), book, ctx, defs);
                    if (book.Pending != null)
                    {
                        var card = StreetEvents.CardOf(book, View(20, day), ctx, defs);
                        log += day + ":" + card.Id + ";";
                        StreetEvents.Answer(book, card, 0, ctx);
                    }
                }
                return log;
            }
            var first = Play();
            if (first != Play())
                failures.Add("STREET-001: the same 28 days fired different cards.");
            if (first.Length == 0)
                failures.Add("STREET-001: 28 days fired nothing at all.");
        }

        static void TheDayPassRollsEveryHouseThePlayerIncluded(List<string> failures)
        {
            var world = Underworld.Deal(21, 3);
            var defs = Defs(Def(EventId.TheMan, 1f, 0f));
            var looked = new List<int>();
            var rolled = StreetEvents.DayPass(world,
                h =>
                {
                    looked.Add(h.GangId);
                    return new HouseView
                    {
                        House = new TerritoryGangId(h.GangId), Roster = h.Roster,
                        Accounts = h.Runner.Accounts, Book = h.Runner.Book, Day = 1,
                    };
                },
                h => new EventContext { CitySeed = 21, GangId = h.GangId, Day = 1,
                    Connection = h.Runner.Connection, World = world },
                defs);
            if (rolled != 3 || !looked.Contains(0))
                failures.Add("PRE-002: the day pass rolled " + rolled + " houses; the player " +
                             (looked.Contains(0) ? "was" : "was NOT") + " among them.");
            if (world.Player.Runner.Events.Pending == null)
                failures.Add("PRE-002: the player's book got no card from the pass.");
        }
    }
}
