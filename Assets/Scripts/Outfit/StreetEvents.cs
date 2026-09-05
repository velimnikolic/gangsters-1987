using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>What can fire. Append only: the pots are saved under these numbers.</summary>
    public enum EventId
    {
        /// <summary>The man who knows the Colombian. One pot; the card dealt is
        /// <see cref="CardId.PortMan"/> or <see cref="CardId.FieldMan"/> by the line.</summary>
        TheMan,
        BrokerRumour,
        TestBuy,
        SupplierTerms,
        LoadLanded,
    }

    /// <summary>What was dealt. Append only: a pending card is saved under it.</summary>
    public enum CardId
    {
        PortMan,
        FieldMan,
        BrokerRumour,
        TestBuy,
        SupplierTerms,
        LoadLanded,
    }

    /// <summary>
    /// Why a card is not on the table, or not answered - a VALUE, never a string, so the
    /// page can print the reason and what clears it in the same words the probe prints
    /// (the epic's UI rule). Two classes: a DEAL GATE keeps the card off the table
    /// altogether; a HOLD REASON deals it and lets it wait its three days.
    /// </summary>
    public enum HoldReason
    {
        None,
        NoMoney,
        NoCrew,
        NoRoom,
        BossInCell,
        AtWar,
        NoSpeaker,

        /// <summary>The QUIET gate: the law is watching our ground (CONN-001).</summary>
        Watched,

        /// <summary>Burned: a sting, or trust under nought - thirty days with nobody
        /// talking to the house (CONN-003). Appended so a saved hold keeps its number.</summary>
        Burned,
    }

    public static class HoldReasons
    {
        /// <summary>Not dealt at all while it holds. A room and a free crew are the two
        /// things a house can DO something about, so they hold a dealt card instead.
        /// </summary>
        public static bool IsGate(HoldReason reason) => reason switch
        {
            HoldReason.BossInCell => true,
            HoldReason.AtWar => true,
            HoldReason.NoSpeaker => true,
            HoldReason.Watched => true,
            HoldReason.Burned => true,
            _ => false,
        };

        public static string Line(HoldReason reason) => reason switch
        {
            HoldReason.NoMoney => "There is not the money for it",
            HoldReason.NoCrew => "There is nobody free to send",
            HoldReason.NoRoom => "There is nowhere to keep it",
            HoldReason.BossInCell => "The Boss is inside",
            HoldReason.AtWar => "There is a war on",
            HoldReason.NoSpeaker => "Nobody to bring the word",
            HoldReason.Watched => "The law is watching our streets",
            HoldReason.Burned => "Nobody is talking to us",
            _ => "",
        };

        public static string Clears(HoldReason reason) => reason switch
        {
            HoldReason.NoMoney => "put money in the safe and he will call back",
            HoldReason.NoCrew => "bring a crew home and he will call back",
            HoldReason.NoRoom => "rent a room and he will call back",
            HoldReason.BossInCell => "get him out and the street will talk again",
            HoldReason.AtWar => "make peace and the street will talk again",
            HoldReason.NoSpeaker => "a lieutenant on the books is a man who hears things",
            HoldReason.Watched => "let the attention cool and the docks will talk",
            HoldReason.Burned => "thirty days from the sting and the street forgets",
            _ => "",
        };
    }

    /// <summary>One readiness signal, with its state in words for STREET TALK.</summary>
    public readonly struct EventSignal
    {
        public EventSignal(string name, float value, string line)
        {
            Name = name;
            Value = value;
            Line = line;
        }

        public string Name { get; }
        public float Value { get; }
        public string Line { get; }
    }

    /// <summary>
    /// Everything a def may read beside the view. The view is the wall a mind looks
    /// through; this is the house's own connection paper, the city's press book and the
    /// two or three city-wide facts the roll needs (whose turn Pablo's man is on).
    /// </summary>
    public sealed class EventContext
    {
        public int CitySeed;
        public int GangId;
        public int RosterSeed;
        public int Day;
        public Connection Connection;
        public EventBook Book;
        public Underworld World;
        public News.PressBook Press;
        public HouseMindConfig Config = HouseMindConfig.Default;

        /// <summary>Whether the house holds a Stash room that is OPEN today - read off
        /// the apartment book by the context builder, never by a def.</summary>
        public bool HasStashRoom;

        /// <summary>The unit that room is, for the page.</summary>
        public string StashRoom = "";

        /// <summary>Police attention at which the QUIET gate shuts (the mind's own
        /// "law watching" line, HouseMindConfig.WalkAttentionCap).</summary>
        public float RaidThreshold = HouseMindConfig.Default.WalkAttentionCap;
    }

    /// <summary>One row on a card: what it says, what it costs, who it needs, and the
    /// intent it becomes. Appeal is what a mind weighs it by.</summary>
    public sealed class EventChoice
    {
        public string Label = "";
        public int Cost;

        /// <summary>What the row adds to the payroll every day after - a signed man's
        /// wage - so the reserve rule can weigh it (D9).</summary>
        public int Upkeep;
        public bool NeedsCrew;

        /// <summary>The risk in words, off the door's watch, or empty (the UI rule).</summary>
        public string Risk = "";

        /// <summary>What the row does, in one line under the label.</summary>
        public string Note = "";

        public System.Func<HouseView, float> Appeal = _ => 0.5f;

        /// <summary>Built when the card is materialised; the carrier carries it.</summary>
        public HouseIntent Intent;

        /// <summary>What choosing it does to the house's own paper, before the intent
        /// is carried - WALK AWAY cooling the broker, say. Pure state on the context.
        /// </summary>
        public System.Action<EventContext, int> OnChosen;
    }

    /// <summary>A card as spoken: who says it, what he says, and the rows.</summary>
    public sealed class EventCard
    {
        public CardId Id;
        public EventId Def;
        public int Speaker = -1;
        public string SpeakerName = "";
        public int DealtDay;
        public int ExpiresDay;
        public string Title = "";
        public readonly List<string> Lines = new List<string>();
        public readonly List<EventChoice> Choices = new List<EventChoice>();

        /// <summary>The man on a PortMan / FieldMan card - dealt, not yet on the
        /// books - or null for a card about nobody.</summary>
        public HireAd Ad;

        /// <summary>The roster man an OUR MAN card names, or -1.</summary>
        public int ManId = -1;

        /// <summary>The crew he lands in when signed - the speaker's.</summary>
        public int CrewId = -1;

        public ConnectionLine Line;
        public ConnectionPath Path;
        public Background Trade;

        /// <summary>What dealing it does to the paper, ONCE, the first time - a re-deal
        /// after a load does not run it.</summary>
        public System.Action<EventContext, int> Dealt;

        /// <summary>A door the scene should mark as learnt when this is dealt.</summary>
        public string DoorToLearn = "";

        /// <summary>The man of ours who did the nights, on THE CELL's card; -1 else.</summary>
        public int CellmateId = -1;

        /// <summary>The door the card is about - the broker's - as its id, or empty.</summary>
        public string Door = "";

        public bool IsWire => Choices.Count == 0;

        public string LabelOf(int choice) =>
            choice >= 0 && choice < Choices.Count ? Choices[choice].Label : "?";

        public int CostOf(int choice) =>
            choice >= 0 && choice < Choices.Count ? Choices[choice].Cost : 0;

        /// <summary>The cheapest row - what the deal gate NoMoney is read against.</summary>
        public int CheapestCost
        {
            get
            {
                var cheapest = int.MaxValue;
                for (var i = 0; i < Choices.Count; i++)
                    if (Choices[i].Cost < cheapest)
                        cheapest = Choices[i].Cost;
                return cheapest == int.MaxValue ? 0 : cheapest;
            }
        }
    }

    /// <summary>
    /// One kind of event: how full its pot has to be, what fills it, what keeps it off
    /// the table, and how it is spoken. Pure: every delegate reads the view and the
    /// context and touches nothing.
    /// </summary>
    public sealed class EventDef
    {
        public EventId Id;

        /// <summary>The score a day must reach before the pot fills at all.</summary>
        public float Threshold = 1f;

        public int CooldownDays;
        public bool Once;

        /// <summary>0..1 off the live books.</summary>
        public System.Func<HouseView, EventContext, float> Score = (_, __) => 0f;

        /// <summary>Every signal with its state, for the page. May be null.</summary>
        public System.Func<HouseView, EventContext, List<EventSignal>> Signals;

        /// <summary>The DEAL GATES: the reason the card stays off the table today, or
        /// None. NoMoney is asked here against the cheapest row.</summary>
        public System.Func<HouseView, EventContext, HoldReason> Gate = (_, __) => HoldReason.None;

        /// <summary>The HOLD REASONS: what keeps a dealt card waiting, or None.</summary>
        public System.Func<HouseView, EventContext, HoldReason> Hold = (_, __) => HoldReason.None;

        /// <summary>Whether the def is even in play for this house today (a stage
        /// before it, a man on the books).</summary>
        public System.Func<HouseView, EventContext, bool> Applies = (_, __) => true;

        /// <summary>The card, spoken. Pure on (view, ctx, seed) the first time; after a
        /// load it is re-dealt with the FROZEN half of the pending card - the path, the
        /// line, the man, the crew, the door - so the offer cannot change under the
        /// house because the street moved on (the Codex review's finding).</summary>
        public System.Func<HouseView, EventContext, int, PendingCard, EventCard> Deal;

        /// <summary>A wire (a card with no rows) does its work here when it fires.</summary>
        public System.Action<HouseView, EventContext, EventCard, EventBook> Fired;

        /// <summary>What a card going unanswered costs (the terms: trust).</summary>
        public System.Action<EventContext, PendingCard> Expired;

        /// <summary>How full the pot is, in words, once over the threshold.</summary>
        public System.Func<EventContext, string> PotLine = _ => "the street is starting to talk";

        /// <summary>Its name on the page.</summary>
        public string Name = "";
    }

    /// <summary>What is on the table: the light half a save keeps. The spoken card is
    /// re-dealt from these (<see cref="StreetEvents.CardOf"/>).</summary>
    public sealed class PendingCard
    {
        public CardId Id;
        public EventId Def;
        public int DealtDay;
        public int ExpiresDay;
        public int Speaker = -1;
        public HoldReason Hold;

        /// <summary>THE FROZEN HALF: what the deal decided, kept so a re-deal after a
        /// load is the same offer - the path and the line, the man (ours) or the
        /// cellmate who brought the name, the crew he lands in, the door.</summary>
        public ConnectionPath Path;
        public ConnectionLine Line;
        public Background Trade;
        public int ManId = -1;
        public int CellmateId = -1;
        public int CrewId = -1;
        public string Door = "";

        public static PendingCard Of(EventCard card, HoldReason hold) => new PendingCard
        {
            Id = card.Id,
            Def = card.Def,
            DealtDay = card.DealtDay,
            ExpiresDay = card.ExpiresDay,
            Speaker = card.Speaker,
            Hold = hold,
            Path = card.Path,
            Line = card.Line,
            Trade = card.Trade,
            ManId = card.ManId,
            CellmateId = card.CellmateId,
            CrewId = card.CrewId,
            Door = card.Door ?? "",
        };
    }

    public sealed class WireLine
    {
        public int Day;
        public string Text = "";

        /// <summary>Whether the police made a record of it - what the paper may print.
        /// A rumour never is.</summary>
        public bool Public;
    }

    /// <summary>
    /// THE BOOK ONE HOUSE KEEPS OF ITS STREET: what is pending, how full every pot is,
    /// what fired and when, what is cooling, and the last lines the wire brought. Saved
    /// per house, nullable - a file with none reads as an empty book.
    /// </summary>
    public sealed class EventBook
    {
        public PendingCard Pending;

        /// <summary>The pending card, spoken - memory only, re-dealt after a load.</summary>
        public EventCard Spoken;

        public readonly Dictionary<EventId, float> Pots = new Dictionary<EventId, float>();
        public readonly Dictionary<EventId, int> Fired = new Dictionary<EventId, int>();
        public readonly Dictionary<EventId, int> Cooling = new Dictionary<EventId, int>();
        public readonly List<WireLine> Wire = new List<WireLine>();

        public int CardsDealt;
        public int CardsAnswered;
        public int CardsExpired;

        /// <summary>"TestBuy/PAY on day 12", for the probe and the page.</summary>
        public string LastAnswer = "";

        /// <summary>Moves on every change, so a page repaints on a number.</summary>
        public int Version { get; private set; }

        public void Touch() => Version++;

        public float PotOf(EventId id) => Pots.TryGetValue(id, out var pot) ? pot : 0f;

        public bool IsCooling(EventId id, int day) =>
            Cooling.TryGetValue(id, out var until) && day < until;

        public bool FiredOnce(EventId id) => Fired.ContainsKey(id);

        public void Say(int day, string text, bool isPublic = false)
        {
            if (string.IsNullOrEmpty(text))
                return;
            Wire.Add(new WireLine { Day = day, Text = text, Public = isPublic });
            if (Wire.Count > StreetEvents.WireKept)
                Wire.RemoveRange(0, Wire.Count - StreetEvents.WireKept);
            Touch();
        }

        public void Clear()
        {
            Pending = null;
            Spoken = null;
            Pots.Clear();
            Fired.Clear();
            Cooling.Clear();
            Wire.Clear();
            CardsDealt = CardsAnswered = CardsExpired = 0;
            LastAnswer = "";
            Touch();
        }
    }

    /// <summary>What one day's pass did to one house, for the scene edge to carry out
    /// - a door to learn, a public line to file - and for the yardstick to count.</summary>
    public readonly struct EventFiring
    {
        public EventFiring(int gangId, CardId card, bool wire, string text, string doorToLearn)
        {
            GangId = gangId;
            Card = card;
            Wire = wire;
            Text = text ?? "";
            DoorToLearn = doorToLearn ?? "";
        }

        public int GangId { get; }
        public CardId Card { get; }
        public bool Wire { get; }
        public string Text { get; }
        public string DoorToLearn { get; }
    }

    /// <summary>
    /// THE STREET EVENT BOOK, PURE (EPIC 40, STREET-001).
    ///
    /// Every midnight each house's pots are fed off its own view: a def whose score is
    /// over its threshold adds (s - t) / (1 - t) to its pot, and at 1.0 it fires. No
    /// die between "ready" and "fired" - the same campaign fires the same card on the
    /// same day headless and in the editor. One card a day at most; the fullest pot
    /// wins. A card is dealt only past its deal gates and waits on its hold reasons
    /// for three days, then expires Unanswered and the def cools.
    ///
    /// Nothing here executes anything: <see cref="Answer"/> hands back the choice's
    /// intent and the runtime carries it through the same doors a button uses.
    /// </summary>
    public static class StreetEvents
    {
        public const int HoldDays = 3;
        public const int WireKept = 20;

        /// <summary>The pot shows on the page from here up.</summary>
        public const float ShowFrom = 0.2f;

        /// <summary>A pot is full from here: two steps of 0.5 add to 0.9999998 in
        /// single precision, and a card a day late for a rounding error is a bug.</summary>
        public const float Full = 0.9995f;

        /// <summary>What a day over the threshold adds. Linear in the excess: a house
        /// at the line waits for ever, a house at 1.0 fires tomorrow.</summary>
        public static float PotStep(float score, float threshold)
        {
            if (threshold >= 1f)
                return score >= 1f ? 1f : 0f;
            if (score <= threshold)
                return 0f;
            var step = (score - threshold) / (1f - threshold);
            return step > 1f ? 1f : step;
        }

        /// <summary>The roll stream for one (house, event, day). Distinct from the
        /// trust and path streams the connection keeps.</summary>
        public static int Seed(int citySeed, int gangId, EventId id, int day) =>
            Potential.Mix(Potential.Mix(citySeed + 40_001, gangId * 131 + (int)id), day);

        /// <summary>
        /// One house's midnight. Feeds every pot, expires the card on the table if its
        /// day has come, deals the fullest pot past its gates, and re-reads the hold on
        /// whatever is pending.
        /// </summary>
        public static void Roll(HouseView view, EventBook book, EventContext ctx,
            IReadOnlyList<EventDef> defs, List<EventFiring> firings = null)
        {
            if (view == null || book == null || ctx == null || defs == null)
                return;
            var day = ctx.Day;

            // The card on the table: expired, or still waiting.
            if (book.Pending != null)
            {
                var def = DefOf(defs, book.Pending.Def);
                if (day >= book.Pending.ExpiresDay || def == null)
                {
                    Expire(book, def, ctx);
                }
                else
                {
                    book.Pending.Hold = def.Hold(view, ctx);
                    book.Touch();
                }
            }

            // The pots.
            EventDef fullest = null;
            var fullestPot = 0f;
            for (var i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def.Once && book.FiredOnce(def.Id))
                    continue;
                if (!def.Applies(view, ctx))
                    continue;
                if (book.IsCooling(def.Id, day))
                    continue;

                var pot = book.PotOf(def.Id) + PotStep(def.Score(view, ctx), def.Threshold);
                if (pot >= Full)
                    pot = 1f;
                book.Pots[def.Id] = pot;
                if (pot < Full || pot <= fullestPot)
                    continue;
                var shut = def.Gate(view, ctx);
                if (shut == HoldReason.None)
                {
                    fullest = def;
                    fullestPot = pot;
                }
                else if (book.Pending == null)
                    // A full pot behind a shut gate is a phone that does not ring. The
                    // wire says why, every midnight it stays shut, rather than leaving
                    // the silence to be guessed at (the user's day 4: the test buy sat
                    // behind WATCHED and nothing anywhere said so).
                    book.Say(day, (string.IsNullOrEmpty(def.Name) ? def.Id.ToString() : def.Name) +
                                  " waits. " + HoldReasons.Line(shut) + " - " +
                                  HoldReasons.Clears(shut) + ".");
            }
            book.Touch();

            // One card a day, and never over one already on the table.
            if (fullest == null || book.Pending != null || fullest.Deal == null)
                return;
            var card = fullest.Deal(view, ctx, Seed(ctx.CitySeed, ctx.GangId, fullest.Id, day), null);
            if (card == null)
                return;
            if (!card.IsWire && card.Speaker < 0)
                return;   // nobody to bring the word: the pot stays full, the deal waits

            book.Pots[fullest.Id] = 0f;
            card.DealtDay = day;
            card.ExpiresDay = day + HoldDays;
            card.Dealt?.Invoke(ctx, day);
            if (card.IsWire)
            {
                book.Fired[fullest.Id] = day;
                if (fullest.CooldownDays > 0)
                    book.Cooling[fullest.Id] = day + fullest.CooldownDays;
                fullest.Fired?.Invoke(view, ctx, card, book);
                var line = card.Lines.Count > 0 ? card.Lines[0] : card.Title;
                book.Say(day, line);
                firings?.Add(new EventFiring(ctx.GangId, card.Id, true, line, card.DoorToLearn));
                return;
            }

            book.Pending = PendingCard.Of(card, fullest.Hold(view, ctx));
            book.Spoken = card;
            book.CardsDealt++;
            book.Touch();
            firings?.Add(new EventFiring(ctx.GangId, card.Id, false, card.Title, card.DoorToLearn));
        }

        /// <summary>The card on the table, spoken - re-dealt from its day when the
        /// book came off a file. Null when nothing is pending.</summary>
        public static EventCard CardOf(EventBook book, HouseView view, EventContext ctx,
            IReadOnlyList<EventDef> defs)
        {
            if (book?.Pending == null || view == null || ctx == null)
                return null;
            if (book.Spoken != null && book.Spoken.DealtDay == book.Pending.DealtDay &&
                book.Spoken.Id == book.Pending.Id)
                return book.Spoken;
            var def = DefOf(defs, book.Pending.Def);
            if (def?.Deal == null)
                return null;
            var card = def.Deal(view, ctx,
                Seed(ctx.CitySeed, ctx.GangId, def.Id, book.Pending.DealtDay), book.Pending);
            if (card == null)
                return null;
            card.DealtDay = book.Pending.DealtDay;
            card.ExpiresDay = book.Pending.ExpiresDay;
            book.Spoken = card;
            return card;
        }

        /// <summary>The hold on the pending card as of NOW - the safe may have moved
        /// since midnight, and a mind that leased a room this think wants to know.</summary>
        public static HoldReason HoldOf(EventBook book, HouseView view, EventContext ctx,
            IReadOnlyList<EventDef> defs)
        {
            if (book?.Pending == null)
                return HoldReason.None;
            var def = DefOf(defs, book.Pending.Def);
            if (def == null)
                return HoldReason.None;
            var hold = def.Hold(view, ctx);
            if (hold != book.Pending.Hold)
            {
                book.Pending.Hold = hold;
                book.Touch();
            }
            return hold;
        }

        /// <summary>The intent a row carries, without touching the book - what a
        /// carrier EXECUTES before it commits the answer, so a refused action leaves the
        /// card on the table (the Codex review's finding). A row whose intent is a Card
        /// choice of its own has no action beyond <see cref="Answer"/>.</summary>
        public static HouseIntent IntentOf(EventCard card, int choice) =>
            card == null || choice < 0 || choice >= card.Choices.Count
                ? default
                : card.Choices[choice].Intent;

        /// <summary>Whether a row's intent is something to carry, or the row is its
        /// own answer (WALK AWAY, NOT YET).</summary>
        public static bool HasAction(HouseIntent intent) =>
            intent.Kind != HouseIntentKind.None && intent.Kind != HouseIntentKind.Card;

        /// <summary>The choice taken. Records it, clears the table, and hands back the
        /// intent for the carrier. WALK AWAY is a row like any other. Called AFTER the
        /// row's action succeeded, never before.</summary>
        public static HouseIntent Answer(EventBook book, EventCard card, int choice,
            EventContext ctx)
        {
            if (book == null || card == null || choice < 0 || choice >= card.Choices.Count)
                return default;
            var day = ctx != null ? ctx.Day : card.DealtDay;
            var row = card.Choices[choice];
            row.OnChosen?.Invoke(ctx, day);
            book.Fired[card.Def] = day;
            book.Pending = null;
            book.Spoken = null;
            book.CardsAnswered++;
            book.LastAnswer = card.Id + "/" + row.Label + " on day " + day;
            book.Touch();
            return row.Intent;
        }

        static void Expire(EventBook book, EventDef def, EventContext ctx)
        {
            var day = ctx.Day;
            var pending = book.Pending;
            book.Pending = null;
            book.Spoken = null;
            book.CardsExpired++;
            book.Fired[pending.Def] = day;
            if (def != null && def.CooldownDays > 0)
                book.Cooling[pending.Def] = day + def.CooldownDays;
            def?.Expired?.Invoke(ctx, pending);
            if (pending.Id == CardId.PortMan || pending.Id == CardId.FieldMan)
                ctx.World?.DirectDeclined(day);
            book.Say(day, "The " + Word(pending.Id) + " went unanswered" +
                          (pending.Hold != HoldReason.None
                              ? " - " + HoldReasons.Line(pending.Hold).ToLowerInvariant()
                              : "") + ".");
        }

        public static string Word(CardId id) => id switch
        {
            CardId.PortMan => "man off the boats",
            CardId.FieldMan => "man off the county field",
            CardId.BrokerRumour => "word about the broker",
            CardId.TestBuy => "test buy",
            CardId.SupplierTerms => "supplier's terms",
            CardId.LoadLanded => "load",
            _ => "card",
        };

        public static EventDef DefOf(IReadOnlyList<EventDef> defs, EventId id)
        {
            for (var i = 0; defs != null && i < defs.Count; i++)
                if (defs[i].Id == id)
                    return defs[i];
            return null;
        }

        /// <summary>
        /// THE ONE PASS, TWO CALLERS (PRE-002). Every house, the player included, is
        /// looked at through the <paramref name="look"/> it is handed and rolled; the
        /// scene calls this once after the day tick with the runtime's own Look, and the
        /// paper city calls it with its own. There is no second sweep anywhere, so the
        /// yardstick and the editor deal the same card on the same day.
        /// </summary>
        /// <returns>How many houses were rolled.</returns>
        public static int DayPass(Underworld world, System.Func<House, HouseView> look,
            System.Func<House, EventContext> context, IReadOnlyList<EventDef> defs,
            List<EventFiring> firings = null)
        {
            if (world == null || look == null || context == null)
                return 0;
            var rolled = 0;
            for (var g = 0; g < world.Count; g++)
            {
                var house = world.Of(g);
                if (house == null || house.Finished || house.Runner == null)
                    continue;
                var view = look(house);
                var ctx = context(house);
                if (view == null || ctx == null)
                    continue;
                Roll(view, house.Runner.Events, ctx, defs, firings);
                rolled++;
            }
            return rolled;
        }

        /// <summary>
        /// The context every caller builds the same way: the house's own connection
        /// paper, the city's press book, and whether a Stash room stands open today.
        /// Pure - the apartment book is a static ledger, read here and written nowhere.
        /// </summary>
        public static EventContext ContextFor(Underworld world, House house,
            HouseMindConfig config = null)
        {
            if (world == null || house?.Runner == null)
                return null;
            config = config ?? HouseMindConfig.Default;
            var day = house.Runner.Campaign.Day;
            var room = StashRoom.Of(house.GangId, house.Roster, day);
            return new EventContext
            {
                CitySeed = world.CitySeed,
                GangId = house.GangId,
                RosterSeed = house.Roster != null ? house.Roster.Seed : world.CitySeed,
                Day = day,
                Connection = house.Runner.Connection,
                Book = house.Runner.Events,
                World = world,
                Press = world.Press,
                Config = config,
                HasStashRoom = room.IsValid,
                StashRoom = room.IsValid ? room.ToString() : "",
                RaidThreshold = config.WalkAttentionCap,
            };
        }
    }
}
