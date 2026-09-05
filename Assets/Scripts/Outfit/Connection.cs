using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Property;
using LivingCity.Territory;

namespace LivingCity.Outfit
{
    /// <summary>Where a house stands with the Colombian. Append only; None = 0 so a
    /// file with no connection block reads as a house with none.</summary>
    public enum ConnectionStage
    {
        None = 0,
        PortMan,
        Rumour,
        Contact,
        Tested,
        Supplier,
        Burned,
    }

    /// <summary>Which line the man opened: the harbour, or the county field.</summary>
    public enum ConnectionLine
    {
        None = 0,
        Port,
        Field,
    }

    public enum SupplierGrade
    {
        None = 0,
        Broker,
        Direct,
    }

    /// <summary>A man's trade before the outfit, derived at read and never stored on
    /// the man (the pedestrian census is untouched). The connection keeps its own
    /// man's trade so the card and the almanac row agree.</summary>
    public enum Background
    {
        None = 0,
        Docker,
        Sailor,
        Fisherman,
        Baggage,
        FieldMechanic,
        Pilot,
        Direct,
    }

    /// <summary>How the man appears. Two of the four are open per house.</summary>
    public enum ConnectionPath
    {
        OurMan = 0,
        Column = 1,
        Cell = 2,
        Bar = 3,
    }

    /// <summary>How a test buy or a meeting came out, for the record and the wire.</summary>
    public enum ConnectionOutcome
    {
        Contact,
        Robbed,
        Cold,
        Good,
        Short,
        Sting,
    }

    /// <summary>
    /// ONE HOUSE'S CONNECTION PAPER (EPIC 40). The stage, the line, the man who opened
    /// it, the terms once they are agreed, the kilos in the room and the trust the
    /// supplier keeps. Pure: every move here is a method the runner or an op calls, and
    /// every one of them says something on the wire (the UI rule).
    /// </summary>
    public sealed class Connection
    {
        public ConnectionStage Stage;
        public ConnectionLine Line;

        /// <summary>A bitmask over <see cref="ConnectionPath"/>; 0 until first drawn.</summary>
        public int Paths;

        public int ManId = -1;
        public Background ManTrade;
        public SupplierGrade Grade;
        public int Trust;
        public int Kilos;
        public int PricePerKilo;
        public int MinLoad;
        public int NextLoadDay;
        public int BurnedUntilDay;

        /// <summary>The day the introducer went missing, or 0 while he stands.
        /// Applies only before the first Supplier acceptance.</summary>
        public int WithoutManSinceDay;

        /// <summary>The day the last load landed; a sale inside a week of it is
        /// "on time".</summary>
        public int LastLoadDay;

        /// <summary>The broker does not talk to us before this day (Robbed, WALK AWAY).</summary>
        public int CoolUntilDay;

        /// <summary>An Explore inside three days pulls the score up (CONN-002).</summary>
        public int LastExploreDay = -1;

        public int MeetAttempts;
        public int BuyAttempts;

        /// <summary>The buyer takes BuyerCapacity a week (EPIC 42's amendment).</summary>
        public int SoldThisWeek;
        public int SoldWeek = -1;

        /// <summary>The half of a load bought on credit, owed tomorrow.</summary>
        public int OwedTomorrow;

        /// <summary>Whether the load that fell due found no room; trust falls once.</summary>
        public bool LoadHeld;

        /// <summary>Moves on every change.</summary>
        public int Version { get; private set; }

        public void Touch() => Version++;

        public bool HasMan => ManId >= 0;
        public bool Established => Grade != SupplierGrade.None;
        public bool IsBurned(int day) => Stage == ConnectionStage.Burned && day < BurnedUntilDay;

        /// <summary>Whether the introducer is still needed: before the first Supplier
        /// acceptance only (ruling 13).</summary>
        public bool NeedsIntroducer =>
            !Established && Stage >= ConnectionStage.PortMan && Stage <= ConnectionStage.Tested;

        // ----------------------------------------------------------------- the money

        public const int KiloPrice = EconomyPrices.KiloPrice;
        public const int BrokerFee = EconomyPrices.BrokerFee;
        public const int BuyerPrice = EconomyPrices.BuyerPrice;
        public const int TestBuyKilos = 2;
        public const int TestBuyPrice = KiloPrice * TestBuyKilos;
        public const int BurnedDays = 30;
        public const int AbsenceDays = 14;
        public const int RobbedCoolDays = 5;
        public const int WalkAwayCoolDays = 10;
        public const int LoadEveryDays = 7;
        public const int TrustGood = 40;
        public const int TrustShort = 25;
        public const int CreditTrust = 60;
        public const int DirectCreditTrust = 40;

        public static int MinLoadFor(ConnectionLine line, SupplierGrade grade) =>
            grade == SupplierGrade.Direct ? 10 : line == ConnectionLine.Field ? 2 : 5;

        /// <summary>Price per kilo: KiloPrice less Trust/10 per cent, and a fifth off
        /// on the Direct line.</summary>
        public static int PriceFor(int trust, SupplierGrade grade)
        {
            var price = KiloPrice - KiloPrice * (trust < 0 ? 0 : trust) / 1000;
            if (grade == SupplierGrade.Direct)
                price = price * 80 / 100;
            return price;
        }

        public static bool CreditAt(int trust, SupplierGrade grade) =>
            trust >= (grade == SupplierGrade.Direct ? DirectCreditTrust : CreditTrust);

        public int BuyerCapacity => MinLoad > 0 ? MinLoad : MinLoadFor(Line, Grade);

        // ------------------------------------------------------------------ the moves

        /// <summary>The man is ours. Stage PortMan; the line and his trade are the
        /// card's.</summary>
        public void Signed(int manId, ConnectionLine line, Background trade, int day)
        {
            ManId = manId;
            ManTrade = trade;
            Line = line;
            WithoutManSinceDay = 0;
            if (Stage == ConnectionStage.None)
                Stage = ConnectionStage.PortMan;
            Touch();
        }

        public void NamedTheBroker(int day)
        {
            if (Stage == ConnectionStage.PortMan)
                Stage = ConnectionStage.Rumour;
            Touch();
        }

        /// <summary>The meeting, decided. Contact opens the test buy; Robbed cools the
        /// door; Cold is a retry.</summary>
        public void Met(ConnectionOutcome outcome, int day)
        {
            MeetAttempts++;
            switch (outcome)
            {
                case ConnectionOutcome.Contact:
                    Stage = ConnectionStage.Contact;
                    break;
                case ConnectionOutcome.Robbed:
                    CoolUntilDay = day + RobbedCoolDays;
                    break;
            }
            Touch();
        }

        /// <summary>The test buy, decided.</summary>
        public void Bought(ConnectionOutcome outcome, int day)
        {
            BuyAttempts++;
            switch (outcome)
            {
                case ConnectionOutcome.Good:
                    Kilos += TestBuyKilos;
                    Trust = TrustGood;
                    Stage = ConnectionStage.Tested;
                    LastLoadDay = day;
                    break;
                case ConnectionOutcome.Short:
                    Kilos += 1;
                    Trust = TrustShort;
                    Stage = ConnectionStage.Tested;
                    LastLoadDay = day;
                    break;
                case ConnectionOutcome.Sting:
                    Burn(day);
                    return;
            }
            Touch();
        }

        /// <summary>WALK AWAY from the test buy: back to Rumour, ten days cold.</summary>
        public void WalkedAwayFromTheBuy(int day)
        {
            if (Stage == ConnectionStage.Contact)
                Stage = ConnectionStage.Rumour;
            CoolUntilDay = day + WalkAwayCoolDays;
            Touch();
        }

        /// <summary>Terms accepted: the relationship is the house's from here.</summary>
        public void Accepted(SupplierGrade grade, int day)
        {
            Grade = grade;
            Stage = ConnectionStage.Supplier;
            MinLoad = MinLoadFor(Line, grade);
            PricePerKilo = PriceFor(Trust, grade);
            NextLoadDay = day + LoadEveryDays;
            WithoutManSinceDay = 0;
            LoadHeld = false;
            Touch();
        }

        public void TermsUnanswered()
        {
            Trust -= 10;
            Touch();
        }

        public void Raided(int day)
        {
            Kilos = 0;
            Trust -= 20;
            Touch();
            if (Trust < 0)
                Burn(day);
        }

        /// <summary>A sting, or trust gone under nought: thirty days with nobody
        /// talking to us.</summary>
        public void Burn(int day)
        {
            Stage = ConnectionStage.Burned;
            BurnedUntilDay = day + BurnedDays;
            Touch();
        }

        /// <summary>The paper load, on its day (CONN-004). Pays at the rail - or half
        /// on credit - and puts the kilos in the room. Answers the wire line.</summary>
        public string Load(Accounts accounts, int day, bool hasRoom)
        {
            if (Stage != ConnectionStage.Supplier || day < NextLoadDay || accounts == null)
                return "";

            if (!hasRoom)
            {
                if (!LoadHeld)
                {
                    LoadHeld = true;
                    Trust -= 5;
                }
                NextLoadDay = day + 1;
                Touch();
                return "The load is held - there is nowhere to keep it. Trust down.";
            }

            var load = MinLoad > 0 ? MinLoad : MinLoadFor(Line, Grade);
            var price = PricePerKilo > 0 ? PricePerKilo : PriceFor(Trust, Grade);
            var bill = load * price;
            var credit = CreditAt(Trust, Grade);
            var due = credit ? bill / 2 : bill;
            if (BalanceMath.Pay(accounts, due, out _) != null)
            {
                Trust -= 10;
                NextLoadDay = day + LoadEveryDays;
                Touch();
                if (Trust < 0)
                    Burn(day);
                return (Line == ConnectionLine.Field ? "The plane" : "The boat") +
                       " wanted paying at the rail and the safe could not. Trust down.";
            }
            if (accounts.Current != null)
                accounts.Current.Purchases += due;
            if (credit)
                OwedTomorrow += bill - due;

            Kilos += load;
            LastLoadDay = day;
            LoadHeld = false;
            NextLoadDay = day + LoadEveryDays;
            Touch();
            return (Line == ConnectionLine.Field ? "A plane came in. " : "A boat came in. ") +
                   load + " kilos in the room, " + UI.LedgerText.Cash(price) + " a kilo" +
                   (credit ? ", half on credit." : ", paid at the rail.");
        }

        /// <summary>The credit half, the morning after.</summary>
        public string SettleCredit(Accounts accounts, int day)
        {
            if (OwedTomorrow <= 0 || accounts == null)
                return "";
            var owed = OwedTomorrow;
            OwedTomorrow = 0;
            if (BalanceMath.Pay(accounts, owed, out _) != null)
            {
                Trust -= 10;
                Touch();
                if (Trust < 0)
                    Burn(day);
                return "The credit half went unpaid. Trust down.";
            }
            if (accounts.Current != null)
                accounts.Current.Purchases += owed;
            Touch();
            return "";
        }

        /// <summary>SELL TO HIS BUYER: every kilo the buyer will take this week, flat,
        /// dirty. Answers what was made.</summary>
        public int Sell(Accounts accounts, int day, out int sold)
        {
            sold = 0;
            if (accounts == null || Kilos <= 0)
                return 0;
            var week = day / LoadEveryDays;
            if (week != SoldWeek)
            {
                SoldWeek = week;
                SoldThisWeek = 0;
            }
            var room = BuyerCapacity - SoldThisWeek;
            if (room <= 0)
                return 0;
            sold = Kilos < room ? Kilos : room;
            var money = sold * BuyerPrice;
            BalanceMath.Receive(accounts, money, MoneyKind.Dirty);
            if (accounts.Current != null)
                accounts.Current.IllegalIncome += money;
            Kilos -= sold;
            SoldThisWeek += sold;
            if (day - LastLoadDay <= LoadEveryDays)
                Trust += 5;
            Touch();
            return money;
        }

        /// <summary>What the next kilo would fetch at our own outlet this week - 0
        /// past the buyer's capacity (EPIC 42 reads it).</summary>
        public int OutletForNextKilo(int day)
        {
            var week = day / LoadEveryDays;
            var soldThisWeek = week == SoldWeek ? SoldThisWeek : 0;
            return soldThisWeek < BuyerCapacity ? BuyerPrice : 0;
        }

        /// <summary>The Explore pull (CONN-002).</summary>
        public void Explored(int day)
        {
            LastExploreDay = day;
            Touch();
        }

        /// <summary>
        /// The day's own turn on the paper: Burned lifts, and before Supplier a
        /// missing introducer cools the introduction one stage a fortnight (ruling 13).
        /// Answers the wire line, or empty.
        /// </summary>
        public string DayTick(Roster roster, int day)
        {
            if (Stage == ConnectionStage.Burned)
            {
                if (day < BurnedUntilDay)
                    return "";
                Stage = Established ? ConnectionStage.Supplier
                    : HasMan ? ConnectionStage.PortMan
                    : ConnectionStage.None;
                if (Trust < 10)
                    Trust = 10;
                if (Established)
                    NextLoadDay = day + LoadEveryDays;
                Touch();
                return "Thirty days. The " + (Line == ConnectionLine.Field ? "field" : "docks") +
                       " are talking to us again.";
            }

            if (!NeedsIntroducer || !HasMan)
                return "";

            var man = roster?.Find(ManId);
            var standing = man != null && !man.Gone && man.Status == CharacterStatus.Active;
            if (standing)
            {
                if (WithoutManSinceDay == 0)
                    return "";
                WithoutManSinceDay = 0;
                Touch();
                return (man.FullName) + " is back. The introduction stands.";
            }

            if (WithoutManSinceDay == 0)
            {
                WithoutManSinceDay = day;
                Touch();
                return (man != null ? man.FullName : "The man") +
                       " is gone. Fourteen days without him and the " +
                       (Line == ConnectionLine.Field ? "field forgets" : "docks forget") + " us.";
            }
            if (day - WithoutManSinceDay < AbsenceDays)
                return "";

            WithoutManSinceDay = day;
            var name = man != null ? man.FullName : "the man";
            switch (Stage)
            {
                case ConnectionStage.Tested:
                    Stage = ConnectionStage.Contact;
                    break;
                case ConnectionStage.Contact:
                    Stage = ConnectionStage.Rumour;
                    break;
                case ConnectionStage.Rumour:
                    Stage = ConnectionStage.PortMan;
                    break;
                default:
                    Stage = ConnectionStage.None;
                    ManId = -1;
                    ManTrade = Background.None;
                    WithoutManSinceDay = 0;
                    break;
            }
            Touch();
            return "Fourteen days without " + name + ". The " +
                   (Line == ConnectionLine.Field ? "field" : "docks") + " went quiet" +
                   (Stage == ConnectionStage.None ? " - the introduction is lost." : ".");
        }

        /// <summary>A replacement resumes at the stage held (ruling 13).</summary>
        public void Replaced(int manId, Background trade)
        {
            ManId = manId;
            ManTrade = trade;
            WithoutManSinceDay = 0;
            Touch();
        }

        // ------------------------------------------------------------------ the words

        public static string StageWord(ConnectionStage stage) => stage switch
        {
            ConnectionStage.PortMan => "A MAN SIGNED",
            ConnectionStage.Rumour => "THE BROKER NAMED",
            ConnectionStage.Contact => "CONTACT MADE",
            ConnectionStage.Tested => "TESTED",
            ConnectionStage.Supplier => "SUPPLIER",
            ConnectionStage.Burned => "BURNED",
            _ => "NO CONNECTION",
        };

        public static string GradeWord(SupplierGrade grade) => grade switch
        {
            SupplierGrade.Broker => "BROKER",
            SupplierGrade.Direct => "DIRECT - PABLO'S LINE",
            _ => "",
        };

        public static string LineWord(ConnectionLine line) =>
            line == ConnectionLine.Field ? "the county field" : "the boats";

        /// <summary>Whose the line is, in words (the UI rule).</summary>
        public string WhoseLine(Roster roster)
        {
            if (Established)
                return "our line - the introducer is no longer needed";
            var man = roster?.Find(ManId);
            return man != null ? man.FullName + "'s introduction" : "no line yet";
        }
    }

    /// <summary>Where a house keeps its kilos: the open Stash room, if one stands.</summary>
    public static class StashRoom
    {
        static readonly List<ApartmentRecord> scratch = new List<ApartmentRecord>();

        public static ApartmentUnitId Of(int gangId, Roster roster, int day)
        {
            Apartments.OwnedBy(gangId, scratch);
            for (var i = 0; i < scratch.Count; i++)
            {
                var record = scratch[i];
                if (record.Role != UnitRole.Stash)
                    continue;
                var keeper = roster?.Find(record.KeeperId);
                var standing = keeper != null && !keeper.Gone &&
                               keeper.Status == CharacterStatus.Active;
                if (Apartments.StateOf(record.Unit, gangId, day, standing) == UnitState.Open)
                    return record.Unit;
            }
            return default;
        }

        /// <summary>Any Stash room on the deed, open or not - what a mind checks
        /// before leasing another.</summary>
        public static bool Held(int gangId)
        {
            Apartments.OwnedBy(gangId, scratch);
            for (var i = 0; i < scratch.Count; i++)
                if (scratch[i].Role == UnitRole.Stash)
                    return true;
            return false;
        }
    }

    /// <summary>A man's trade, derived at read (CONN-001).</summary>
    public static class Backgrounds
    {
        public const int Salt = 40_777;

        /// <summary>One man in eight has a trade at all; three in four of those are
        /// port men. Pablo's man reads Direct by his id; the connection's own man
        /// reads the trade his card gave him.</summary>
        public static Background Of(int rosterSeed, int characterId, int directManId = -1,
            Connection connection = null)
        {
            if (characterId < 0)
                return Background.None;
            if (characterId == directManId)
                return Background.Direct;
            if (connection != null && connection.ManId == characterId &&
                connection.ManTrade != Background.None)
                return connection.ManTrade;
            var mix = (uint)Potential.Mix(rosterSeed + Salt, characterId);
            if (mix % 100 >= 12)
                return Background.None;
            var line = mix % 4 != 0 ? ConnectionLine.Port : ConnectionLine.Field;
            return TradeOf(line, (int)((mix / 7) % 3));
        }

        public static ConnectionLine LineOf(Background trade) => trade switch
        {
            Background.Docker => ConnectionLine.Port,
            Background.Sailor => ConnectionLine.Port,
            Background.Fisherman => ConnectionLine.Port,
            Background.Baggage => ConnectionLine.Field,
            Background.FieldMechanic => ConnectionLine.Field,
            Background.Pilot => ConnectionLine.Field,
            _ => ConnectionLine.None,
        };

        public static Background TradeOf(ConnectionLine line, int pick)
        {
            pick = ((pick % 3) + 3) % 3;
            return line == ConnectionLine.Field
                ? pick == 0 ? Background.Baggage
                : pick == 1 ? Background.FieldMechanic
                : Background.Pilot
                : pick == 0 ? Background.Docker
                : pick == 1 ? Background.Sailor
                : Background.Fisherman;
        }

        /// <summary>The trade in words, for the man's row.</summary>
        public static string Word(Background trade) => trade switch
        {
            Background.Docker => "worked the docks",
            Background.Sailor => "sailed the South American runs",
            Background.Fisherman => "fished forty miles out",
            Background.Baggage => "worked the ramp at the county field",
            Background.FieldMechanic => "kept the planes at the county field",
            Background.Pilot => "flew crop dusters out of the county field",
            Background.Direct => "Pablo's man",
            _ => "",
        };

        public static string Noun(Background trade) => trade switch
        {
            Background.Docker => "docker",
            Background.Sailor => "sailor",
            Background.Fisherman => "fisherman",
            Background.Baggage => "ramp agent",
            Background.FieldMechanic => "mechanic",
            Background.Pilot => "pilot",
            _ => "man",
        };
    }

    /// <summary>
    /// READINESS (CONN-001). Two weighted signals off the live books fill the pot;
    /// QUIET is a deal gate and not a weight, because the paper city carries no
    /// attention at all and a weight the yardstick could never tune is a number
    /// nobody can rule on.
    /// </summary>
    public static class ConnectionScore
    {
        public const float MoneyWeight = 0.5f;
        public const float NameWeight = 0.5f;
        public const float ExplorePull = 0.15f;
        public const int ExploreDays = 3;
        public const int NamedInsideDays = 14;

        static readonly List<Character> topScratch = new List<Character>();

        public static float Money(HouseView view)
        {
            var safe = view?.Safe ?? 0;
            if (safe >= 2 * Connection.TestBuyPrice)
                return 1f;
            if (safe < Connection.BrokerFee)
                return 0f;
            return (safe - Connection.BrokerFee) /
                   (float)(2 * Connection.TestBuyPrice - Connection.BrokerFee);
        }

        public static float Name(HouseView view, EventContext ctx)
        {
            if (view?.Roster == null)
                return 0f;
            Notability.Top(view.Roster, view.Day, 1, topScratch);
            var top = topScratch.Count > 0 ? Notability.Of(topScratch[0], view.Day) : 0;
            var byNotability = top >= Notability.NewsBand
                ? 1f
                : top / (float)Notability.NewsBand;
            return NamedInThePaper(ctx) ? 1f : byNotability;
        }

        /// <summary>The paper printed one of ours inside a fortnight.</summary>
        public static bool NamedInThePaper(EventContext ctx)
        {
            var press = ctx?.Press;
            if (press == null)
                return false;
            for (var i = press.Count - 1; i >= 0; i--)
            {
                var record = press[i];
                if (ctx.Day - record.Day > NamedInsideDays)
                    break;
                if (record.Attribution != News.PressAttribution.Named)
                    continue;
                if (record.NamedGangId == ctx.GangId)
                    return true;
                for (var f = 0; record.Factions != null && f < record.Factions.Length; f++)
                    if (record.Factions[f] == ctx.GangId)
                        return true;
            }
            return false;
        }

        /// <summary>The QUIET gate: the law is watching a street we stand on.</summary>
        public static bool Watched(HouseView view, EventContext ctx)
        {
            if (view == null)
                return false;
            var threshold = ctx != null ? ctx.RaidThreshold : HouseMindConfig.Default.WalkAttentionCap;
            for (var b = 0; b < view.Blocks.Count; b++)
                if (view.PoliceAttention(view.Blocks[b]) > threshold)
                    return true;
            return false;
        }

        public static float MaxAttention(HouseView view)
        {
            var most = 0f;
            for (var b = 0; view != null && b < view.Blocks.Count; b++)
            {
                var here = view.PoliceAttention(view.Blocks[b]);
                if (here > most)
                    most = here;
            }
            return most;
        }

        public static float Of(HouseView view, EventContext ctx)
        {
            var score = MoneyWeight * Money(view) + NameWeight * Name(view, ctx);
            var connection = ctx?.Connection;
            if (connection != null && connection.LastExploreDay >= 0 &&
                ctx.Day - connection.LastExploreDay <= ExploreDays)
                score += ExplorePull;
            return score > 1f ? 1f : score;
        }

        public static List<EventSignal> Signals(HouseView view, EventContext ctx)
        {
            var money = Money(view);
            var name = Name(view, ctx);
            var line = ctx?.Connection != null ? ctx.Connection.Line : ConnectionLine.None;
            var street = line == ConnectionLine.Field ? "the field" : "the docks";
            var list = new List<EventSignal>
            {
                new EventSignal("MONEY", money,
                    money >= 1f ? "the safe would cover a test buy twice over"
                    : money > 0.5f ? "the safe would cover a test buy"
                    : money > 0f ? "the safe would cover the broker's fee and not much more"
                    : "the safe would not cover the broker's fee"),
                new EventSignal("NAME", name,
                    name >= 1f ? "your name has been in the paper"
                    : name > 0.5f ? "people at " + street + " have heard of you"
                    : "nobody at " + street + " knows your name"),
            };
            var watched = Watched(view, ctx);
            list.Add(new EventSignal("QUIET", watched ? 0f : 1f,
                watched ? street + " are being watched" : street + " are quiet"));
            return list;
        }
    }

    /// <summary>Every line on the two cards and the wires, in one place (CONN-001).
    /// Placeholders: {Lt} the speaker, {Man} the man, {Bar}/{Barman}, {Cellmate},
    /// {Box}, {Door}.</summary>
    public static class ConnectionText
    {
        public const string PortTitle = "A MAN OFF THE BOATS";
        public const string FieldTitle = "A MAN OFF THE COUNTY FIELD";

        public static string Opening(ConnectionLine line, ConnectionPath path)
        {
            if (line == ConnectionLine.Field)
                return path switch
                {
                    ConnectionPath.OurMan =>
                        "{Man} used to work the county field. He says planes come in after " +
                        "dark that nobody logs, and he says he knows who meets them.",
                    ConnectionPath.Column =>
                        "There's an ad in the paper from a man at the county field. I drove " +
                        "out. He knows the difference between a flight plan and a flight.",
                    ConnectionPath.Cell =>
                        "{Cellmate} came out with a name. A man from the county field doing " +
                        "eighteen months over a logbook he didn't keep. Gets out Tuesday and " +
                        "wants work that doesn't ask about the logbook.",
                    _ =>
                        "{Barman} at {Bar} says a man from the field drinks there after the " +
                        "last flight. Cash, no friends, and he asked {Barman} who runs this " +
                        "street.",
                };
            return path switch
            {
                ConnectionPath.OurMan =>
                    "{Man}'s been with us a while. Before us he worked the water. He says he " +
                    "knows a man who knows a Colombian, and he says it like he's said it " +
                    "before.",
                ConnectionPath.Column =>
                    "There's a man in the paper asking for serious people. I made the call. " +
                    "He worked the river twelve years and he talks like a man who's carried " +
                    "more than fruit.",
                ConnectionPath.Cell =>
                    "{Cellmate} did his nights. Shared a cell with a Cuban named {Man} who " +
                    "was in for a manifest that didn't add up. {Cellmate} came out with a " +
                    "name and a number.",
                _ =>
                    "{Barman} at {Bar} pulled me aside. Says a man off the boats drinks there " +
                    "Thursdays, pays cash, and asks about people like us.",
            };
        }

        public static string OwnWords(Background trade) => trade switch
        {
            Background.Docker =>
                "I load what the manifest says. I also know which boxes the manifest lies " +
                "about. There's a container every third week that nobody signs for, and I " +
                "know who doesn't sign for it.",
            Background.Sailor =>
                "Nine runs to Barranquilla on a banana boat. The chief mate has a cousin. " +
                "The cousin has a cousin. That's the whole trade - cousins and a quiet mate.",
            Background.Fisherman =>
                "Forty miles out there's no Coast Guard, only me and what comes off a " +
                "freighter in the dark. I've brought it in before. I know the man who sends " +
                "the freighter.",
            Background.Baggage =>
                "Everything that comes off a plane goes through my hands before it goes " +
                "through customs. Some of it doesn't go through customs. I decide which.",
            Background.FieldMechanic =>
                "I sign the airworthiness. I know which Cessna goes to Bimini with the seats " +
                "out and the tanks full and comes back with the tanks empty and the seats " +
                "still out.",
            Background.Pilot =>
                "I've flown the Bahamas run. Two hours, under the radar, no flight plan. Two " +
                "kilos ride in the wheel wells and nobody at that field has ever looked in a " +
                "wheel well.",
            _ => "I know a man.",
        };

        public static string Ad(Background trade) => trade switch
        {
            Background.Docker =>
                "LONGSHOREMAN, 12 yrs Port of Miami. Knows the yard, knows the night gate. " +
                "Serious people only. Box {Box}.",
            Background.Sailor =>
                "ABLE SEAMAN, South American runs, papers in order. Discreet, will travel. " +
                "Box {Box}.",
            Background.Fisherman =>
                "CAPTAIN w/ own boat, 34 ft, twin diesels. Charters, deliveries, no questions " +
                "asked or answered. Box {Box}.",
            Background.Baggage =>
                "RAMP AGENT, county field, nights. Fast hands, short memory. Box {Box}.",
            Background.FieldMechanic =>
                "A&P MECHANIC, light twins and singles, no paperwork either way. Box {Box}.",
            Background.Pilot =>
                "PILOT, twin rated, island time, cash only, leaves at dusk. Box {Box}.",
            _ => "SITUATION WANTED. Box {Box}.",
        };

        public static string TheLine(ConnectionLine line) =>
            line == ConnectionLine.Field
                ? "The Colombian has a man who sits in a diner on the field road. {Man} can " +
                  "put us at his table. It's a smaller line - two kilos a flight, a plane a " +
                  "week - but the Coast Guard doesn't fly over a cow pasture, and the man at " +
                  "the diner is hungrier than the man on the water."
                : "The Colombian doesn't talk to strangers. There's a broker - a Cuban with a " +
                  "table at a bar on the water - who talks for him. {Man} can get us the " +
                  "table. After that it's our money and our nerve. A boat brings five kilos " +
                  "at a time and never less, and the boat wants paying at the rail.";

        public static string Cold(ConnectionLine line, ConnectionPath path) =>
            path switch
            {
                ConnectionPath.Bar => line == ConnectionLine.Field
                    ? "{Barman} says nobody from the field drinks at {Bar}."
                    : "{Barman} says nobody off the boats drinks at {Bar}.",
                ConnectionPath.Cell =>
                    "{Cellmate} kept his head down inside. He came out with nothing.",
                _ => line == ConnectionLine.Field
                    ? "{Lt} drove out to the field. The men there don't know him and don't " +
                      "want to."
                    : "{Lt} asked around the docks. Nobody's buying.",
            };

        public static string PotLine(ConnectionLine line) =>
            line == ConnectionLine.Field
                ? "the field is starting to talk"
                : "the docks are starting to talk";

        public static string Rumour(ConnectionLine line) =>
            line == ConnectionLine.Field
                ? "{Man} says the man from the diner is at {Door} after the last flight."
                : "{Man} says the Cuban sits at {Door}. Thursdays.";

        public static string Watch(float attention, float threshold, string door)
        {
            if (attention <= threshold * 0.5f)
                return door + " is quiet";
            if (attention <= threshold)
                return door + " has had a uniform past it";
            return door + " is being watched - the seller could be a cop";
        }

        public static string Fill(string text, string lt, string man, string bar,
            string barman, string cellmate, string box, string door)
        {
            return (text ?? "")
                .Replace("{Lt}", lt ?? "")
                .Replace("{Man}", man ?? "")
                .Replace("{Bar}", bar ?? "")
                .Replace("{Barman}", barman ?? "")
                .Replace("{Cellmate}", cellmate ?? "")
                .Replace("{Box}", box ?? "")
                .Replace("{Door}", door ?? "");
        }
    }

    /// <summary>
    /// THE DEFS (CONN-001..004): the man, the broker, the test buy, the terms. Every
    /// one reads the view and the context and touches nothing but the card it deals;
    /// what a choice DOES is its intent, carried by the runtime, and what a wire does
    /// is its Fired.
    /// </summary>
    public static class ConnectionEvents
    {
        public const float TheManThreshold = 0.4f;
        public const float BrokerThreshold = 0.6f;
        public const int TheManCooldown = 30;
        public const int ColdCooldown = 7;
        public const int OurManLoyalty = 60;
        public const int BarFear = 30;
        public const int CellNights = 2;

        static readonly List<EventDef> defs = new List<EventDef>
        {
            TheMan(), BrokerRumour(), TestBuy(), SupplierTerms(),
        };

        public static IReadOnlyList<EventDef> Defs => defs;

        // ------------------------------------------------------------------ the man

        static EventDef TheMan() => new EventDef
        {
            Id = EventId.TheMan,
            Name = "THE MAN",
            Threshold = TheManThreshold,
            // Told cold, or unanswered: a week. WALK AWAY cools it thirty (below).
            CooldownDays = ColdCooldown,
            Applies = (view, ctx) =>
                ctx.Connection != null && ctx.Connection.Stage == ConnectionStage.None &&
                !ctx.Connection.HasMan,
            Score = ConnectionScore.Of,
            Signals = ConnectionScore.Signals,
            PotLine = ctx => ConnectionText.PotLine(LineOf(ctx)),
            Gate = (view, ctx) =>
            {
                var common = CommonGate(view, ctx);
                if (common != HoldReason.None)
                    return common;
                // A lieutenant with a crew to land him in - not a FREE crew: signing a
                // man sends nobody anywhere.
                if (SpeakerFor(view, ctx, -1, out var landing) < 0 || landing < 0)
                    return HoldReason.NoSpeaker;
                return HoldReason.None;
            },
            Hold = (view, ctx) => HoldReason.None,
            Deal = DealTheMan,
        };

        static EventDef BrokerRumour() => new EventDef
        {
            Id = EventId.BrokerRumour,
            Name = "THE BROKER",
            Threshold = BrokerThreshold,
            CooldownDays = 0,
            Applies = (view, ctx) =>
                ctx.Connection != null && ctx.Connection.HasMan && ManStands(view, ctx) &&
                (ctx.Connection.Stage == ConnectionStage.PortMan ||
                 ctx.Connection.Stage == ConnectionStage.Rumour) &&
                ctx.Day >= ctx.Connection.CoolUntilDay,
            Score = ConnectionScore.Of,
            Signals = ConnectionScore.Signals,
            PotLine = ctx => ConnectionText.PotLine(LineOf(ctx)),
            Gate = (view, ctx) =>
            {
                var common = CommonGate(view, ctx);
                if (common != HoldReason.None)
                    return common;
                if (SpeakerFor(view, ctx, ctx.Connection.ManId, out _) < 0)
                    return HoldReason.NoSpeaker;
                if (view.Safe < Connection.BrokerFee)
                    return HoldReason.NoMoney;
                if (!BrokerDoor(view, ctx).IsValid)
                    return HoldReason.NoSpeaker;
                return HoldReason.None;
            },
            Hold = (view, ctx) => CrewOf(view) == null ? HoldReason.NoCrew : HoldReason.None,
            Deal = DealBroker,
        };

        static EventDef TestBuy() => new EventDef
        {
            Id = EventId.TestBuy,
            Name = "THE TEST BUY",
            Threshold = 0f,
            CooldownDays = 0,
            Applies = (view, ctx) =>
                ctx.Connection != null && ctx.Connection.Stage == ConnectionStage.Contact &&
                ctx.Day >= ctx.Connection.CoolUntilDay,
            Score = (view, ctx) => 1f,
            Signals = ConnectionScore.Signals,
            PotLine = ctx => "the broker is waiting on an answer",
            Gate = (view, ctx) =>
            {
                var common = CommonGate(view, ctx);
                if (common != HoldReason.None)
                    return common;
                if (SpeakerFor(view, ctx, ctx.Connection.ManId, out _) < 0)
                    return HoldReason.NoSpeaker;
                if (view.Safe < Connection.KiloPrice)
                    return HoldReason.NoMoney;
                return HoldReason.None;
            },
            Hold = (view, ctx) =>
                !ctx.HasStashRoom ? HoldReason.NoRoom
                : CrewOf(view) == null ? HoldReason.NoCrew
                : view.Safe < Connection.KiloPrice ? HoldReason.NoMoney
                : HoldReason.None,
            Deal = DealTestBuy,
        };

        static EventDef SupplierTerms() => new EventDef
        {
            Id = EventId.SupplierTerms,
            Name = "THE TERMS",
            Threshold = 0f,
            CooldownDays = 1,
            Applies = (view, ctx) =>
                ctx.Connection != null && ctx.Connection.Stage == ConnectionStage.Tested,
            Score = (view, ctx) => 1f,
            Signals = ConnectionScore.Signals,
            PotLine = ctx => "the supplier has terms for us",
            Gate = (view, ctx) =>
            {
                var common = CommonGate(view, ctx);
                if (common != HoldReason.None)
                    return common;
                if (SpeakerFor(view, ctx, ctx.Connection.ManId, out _) < 0)
                    return HoldReason.NoSpeaker;
                return HoldReason.None;
            },
            Hold = (view, ctx) => HoldReason.None,
            Deal = DealTerms,
            Expired = (ctx, pending) => ctx.Connection?.TermsUnanswered(),
        };

        // ---------------------------------------------------------------- the gates

        static HoldReason CommonGate(HouseView view, EventContext ctx)
        {
            if (ctx?.Connection == null || view?.Roster == null)
                return HoldReason.NoSpeaker;
            if (ctx.Connection.IsBurned(ctx.Day))
                return HoldReason.Watched;
            if (BossInside(view))
                return HoldReason.BossInCell;
            if (AtWar(view))
                return HoldReason.AtWar;
            if (ConnectionScore.Watched(view, ctx))
                return HoldReason.Watched;
            return HoldReason.None;
        }

        static bool BossInside(HouseView view)
        {
            var boss = view.Roster.FindBoss();
            return boss != null && boss.Status == CharacterStatus.Jailed;
        }

        static bool AtWar(HouseView view)
        {
            for (var i = 0; i < view.Rivals.Count; i++)
                if (view.StanceToward(view.Rivals[i]) == Stance.War)
                    return true;
            return false;
        }

        static bool ManStands(HouseView view, EventContext ctx)
        {
            var man = view.Roster.Find(ctx.Connection.ManId);
            return man != null && !man.Gone && man.Status == CharacterStatus.Active;
        }

        static ConnectionLine LineOf(EventContext ctx) =>
            ctx?.Connection != null && ctx.Connection.Line != ConnectionLine.None
                ? ctx.Connection.Line
                : ConnectionLine.Port;

        /// <summary>A crew a mind or a card could send: the mind's own rule.</summary>
        public static Crew CrewOf(HouseView view) => HouseMind.AnyFreeCrew(view);

        /// <summary>
        /// WHO BRINGS THE WORD (ruling 3). The lieutenant whose crew holds the man,
        /// else the desk manager, else nobody. For the first card - the man is not
        /// ours yet - the lieutenant is whoever the path found him through.
        /// </summary>
        public static int SpeakerFor(HouseView view, EventContext ctx, int aboutManId,
            out int crewId)
        {
            crewId = -1;
            var roster = view?.Roster;
            if (roster == null)
                return -1;
            if (aboutManId >= 0)
            {
                var crew = roster.CrewOf(aboutManId);
                var lt = crew != null ? roster.Find(crew.LieutenantId) : null;
                if (lt != null && Stands(lt) && lt.Rank == Rank.Lieutenant)
                {
                    crewId = crew.Id;
                    return lt.Id;
                }
            }
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                if (crew.LieutenantId == roster.BossId)
                    continue;
                var lt = roster.Find(crew.LieutenantId);
                if (lt != null && Stands(lt) && lt.Rank == Rank.Lieutenant)
                {
                    crewId = crew.Id;
                    return lt.Id;
                }
            }
            var desk = roster.Find(roster.FrontId);
            if (desk != null && Stands(desk))
                return desk.Id;
            return -1;
        }

        static bool Stands(Character man) =>
            man != null && !man.Gone && man.Status == CharacterStatus.Active;

        /// <summary>Any crew a standing lieutenant leads, free or not - where a
        /// signed man lands when the speaker was the desk.</summary>
        static int FirstLedCrew(Roster roster)
        {
            for (var i = 0; i < roster.Crews.Count; i++)
            {
                var crew = roster.Crews[i];
                if (crew.LieutenantId == roster.BossId)
                    continue;
                var lt = roster.Find(crew.LieutenantId);
                if (lt != null && Stands(lt) && lt.Rank == Rank.Lieutenant)
                    return crew.Id;
            }
            return -1;
        }

        // ---------------------------------------------------------------- the paths

        public static int PathsFor(EventContext ctx)
        {
            var connection = ctx.Connection;
            if (connection.Paths != 0)
                return connection.Paths;
            var rng = new System.Random(Potential.Mix(
                Potential.Mix(ctx.CitySeed + 40_002, ctx.GangId), 7_3_1));
            var first = rng.Next(4);
            var second = (first + 1 + rng.Next(3)) % 4;
            connection.Paths = (1 << first) | (1 << second);
            connection.Touch();
            return connection.Paths;
        }

        public static bool PathOpen(EventContext ctx, ConnectionPath path) =>
            (PathsFor(ctx) & (1 << (int)path)) != 0;

        /// <summary>A roster man with a trade and the loyalty to be trusted with it.</summary>
        static Character OurManCandidate(HouseView view, EventContext ctx)
        {
            var roster = view.Roster;
            var direct = ctx.World != null ? ctx.World.DirectManId : -1;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (!Stands(man) || man.Loyalty < OurManLoyalty || man.Id == roster.BossId)
                    continue;
                if (man.Specialty != Specialty.None)
                    continue;
                var trade = Backgrounds.Of(ctx.RosterSeed, man.Id, direct, ctx.Connection);
                if (trade == Background.None || trade == Background.Direct)
                    continue;
                return man;
            }
            return null;
        }

        /// <summary>A man of ours released today after two nights inside. With
        /// DaysToCourt = 1 two nights means a convicted man; the path fires on release,
        /// never while he is inside.</summary>
        static Character ReleasedToday(HouseView view, EventContext ctx)
        {
            var roster = view.Roster;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (!Stands(man) || man.RapSheet.Count == 0)
                    continue;
                if (man.ReleasedOnDay != ctx.Day || man.NightsInside < CellNights)
                    continue;
                return man;
            }
            return null;
        }

        /// <summary>A bar of ours - a Pub, Nightclub or Cafe paying us, on a block the
        /// family is feared on.</summary>
        static HouseDoor BarOfOurs(HouseView view, EventContext ctx, out TerritoryBlockId block)
        {
            block = default;
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var blockId = view.Blocks[b];
                if (view.OurFear(blockId) < BarFear)
                    continue;
                var doors = view.Businesses(blockId);
                for (var i = 0; i < doors.Count; i++)
                {
                    var door = doors[i];
                    if (!IsBar(door.Trade))
                        continue;
                    if (door.Tenure != DoorTenure.Ours && door.Tenure != DoorTenure.Paying)
                        continue;
                    if (door.Standing != TerritoryProtectionState.Compliant &&
                        door.Tenure != DoorTenure.Ours)
                        continue;
                    block = blockId;
                    return door;
                }
            }
            return default;
        }

        static bool IsBar(Business.BusinessArchetypeId trade) =>
            trade == Business.BusinessArchetypeId.Pub ||
            trade == Business.BusinessArchetypeId.Nightclub ||
            trade == Business.BusinessArchetypeId.Cafe;

        /// <summary>
        /// THE BROKER'S DOOR, derived at read (CONN-002): the nearest Pub or Nightclub
        /// the house can see on the port line, the nearest Cafe on the field line; the
        /// other kind when the line's is out of sight. Never stored.
        /// </summary>
        public static TerritoryBusinessId BrokerDoor(HouseView view, EventContext ctx)
        {
            var line = LineOf(ctx);
            var wanted = FindDoor(view, line);
            if (wanted.IsValid)
                return wanted;
            return FindDoor(view, line == ConnectionLine.Port ? ConnectionLine.Field
                : ConnectionLine.Port);
        }

        static TerritoryBusinessId FindDoor(HouseView view, ConnectionLine line)
        {
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var doors = view.Businesses(view.Blocks[b]);
                for (var i = 0; i < doors.Count; i++)
                {
                    var trade = doors[i].Trade;
                    var matches = line == ConnectionLine.Field
                        ? trade == Business.BusinessArchetypeId.Cafe
                        : trade == Business.BusinessArchetypeId.Pub ||
                          trade == Business.BusinessArchetypeId.Nightclub;
                    if (matches && !doors[i].Shut)
                        return doors[i].BusinessId;
                }
            }
            return default;
        }

        public static TerritoryBlockId BlockOfDoor(HouseView view, TerritoryBusinessId door)
        {
            for (var b = 0; b < view.Blocks.Count; b++)
            {
                var doors = view.Businesses(view.Blocks[b]);
                for (var i = 0; i < doors.Count; i++)
                    if (doors[i].BusinessId == door)
                        return view.Blocks[b];
            }
            return default;
        }

        /// <summary>What the door is called on a card - the plain id until the scene
        /// names it; the runtime substitutes the shop's name when it paints.</summary>
        public static string DoorWord(TerritoryBusinessId door) =>
            door.IsValid ? DoorNamer?.Invoke(door) ?? door.Value : "the bar";

        /// <summary>The scene's naming of a door, for the cards. Null headless.</summary>
        public static System.Func<TerritoryBusinessId, string> DoorNamer;

        // ---------------------------------------------------------------- the deals

        static EventCard DealTheMan(HouseView view, EventContext ctx, int seed)
        {
            var rng = new System.Random(seed);
            var roster = view.Roster;
            var connection = ctx.Connection;

            // The line, Port 3 : Field 1 - the field is the thinner line.
            var line = rng.Next(4) == 0 ? ConnectionLine.Field : ConnectionLine.Port;

            // The path: the first open one that can produce him today.
            var paths = PathsFor(ctx);
            Character ourMan = null, cellmate = null;
            HouseDoor bar = default;
            var barBlock = default(TerritoryBlockId);
            var path = ConnectionPath.Column;
            var found = false;
            for (var p = 0; p < 4 && !found; p++)
            {
                var candidate = (ConnectionPath)p;
                if ((paths & (1 << p)) == 0)
                    continue;
                switch (candidate)
                {
                    case ConnectionPath.OurMan:
                        ourMan = OurManCandidate(view, ctx);
                        found = ourMan != null;
                        break;
                    case ConnectionPath.Column:
                        found = true;
                        break;
                    case ConnectionPath.Cell:
                        cellmate = ReleasedToday(view, ctx);
                        found = cellmate != null;
                        break;
                    case ConnectionPath.Bar:
                        bar = BarOfOurs(view, ctx, out barBlock);
                        found = bar.BusinessId.IsValid;
                        break;
                }
                if (found)
                    path = candidate;
            }

            var direct = ctx.World != null ? ctx.World.DirectManId : -1;
            if (ourMan != null)
                line = Backgrounds.LineOf(Backgrounds.Of(ctx.RosterSeed, ourMan.Id, direct, connection));
            var trade = ourMan != null
                ? Backgrounds.Of(ctx.RosterSeed, ourMan.Id, direct, connection)
                : Backgrounds.TradeOf(line, rng.Next(3));

            // Whose word it is.
            int crewId;
            var speaker = path == ConnectionPath.OurMan && ourMan != null
                ? SpeakerFor(view, ctx, ourMan.Id, out crewId)
                : path == ConnectionPath.Cell && cellmate != null
                    ? SpeakerFor(view, ctx, cellmate.Id, out crewId)
                    : SpeakerFor(view, ctx, -1, out crewId);
            var lt = roster.Find(speaker);
            var ltName = lt != null ? lt.FirstName : "the desk";
            if (crewId < 0)
                crewId = CrewOf(view)?.Id ?? FirstLedCrew(roster);

            var card = new EventCard
            {
                Id = line == ConnectionLine.Field ? CardId.FieldMan : CardId.PortMan,
                Def = EventId.TheMan,
                Speaker = speaker,
                SpeakerName = lt != null ? lt.FullName : "THE DESK",
                Title = line == ConnectionLine.Field ? ConnectionText.FieldTitle : ConnectionText.PortTitle,
                Line = line,
                Path = path,
                Trade = trade,
                CrewId = crewId,
            };

            if (!found)
            {
                // Told cold: a wire, and the def cools a week.
                card.Lines.Add(ConnectionText.Fill(
                    ConnectionText.Cold(line, FirstOpen(paths)), ltName, "", "", "", "", "", ""));
                return card;
            }

            // The man himself.
            string manName, box = "", barName = "", barman = "", cellName = "";
            if (ourMan != null)
            {
                card.ManId = ourMan.Id;
                manName = ourMan.FirstName;
            }
            else
            {
                var ad = DealTheAd(roster, rng, ctx, line, trade, seed);
                card.Ad = ad;
                box = ad.Box;
                manName = ad.Man.FirstName;
            }
            if (cellmate != null)
                cellName = cellmate.FirstName;
            if (bar.BusinessId.IsValid)
            {
                barName = DoorWord(bar.BusinessId);
                barman = "the barman";
            }
            var door = DoorWord(BrokerDoor(view, ctx));

            string F(string text) =>
                ConnectionText.Fill(text, ltName, manName, barName, barman, cellName, box, door);

            card.Lines.Add(F(ConnectionText.Opening(line, path)));
            card.Lines.Add("\"" + F(ConnectionText.OwnWords(trade)) + "\"");
            card.Lines.Add(F(ConnectionText.TheLine(line)));

            var price = card.Ad != null ? card.Ad.Down : 0;
            var wage = card.Ad != null ? card.Ad.Daily : 0;
            var manFull = ourMan != null ? ourMan.FullName : card.Ad.Man.FullName;
            card.Choices.Add(new EventChoice
            {
                Label = ourMan != null ? "PUT HIM ON IT" : "SIGN HIM",
                Cost = price,
                Upkeep = wage,
                NeedsCrew = false,
                Note = ourMan != null
                    ? "No fee - he is ours already. He stands in " + ltName + "'s crew."
                    : UI.LedgerText.Cash(price) + " now, " + UI.LedgerText.Cash(wage) +
                      " a day. He stands in " + ltName + "'s crew.",
                Appeal = v => 0.8f,
                Intent = HouseIntent.Sign(card, crewId, price, HouseMind.TierCollect,
                    "the man who knows the Colombian: " + manFull),
            });
            card.Choices.Add(new EventChoice
            {
                Label = "WALK AWAY",
                Cost = 0,
                Note = "We're not in that business. Thirty days before the street brings it up again.",
                Appeal = v => 0.2f,
                OnChosen = (c, day) =>
                {
                    c.World?.DirectDeclined(day);
                    if (c.Book != null)
                        c.Book.Cooling[EventId.TheMan] = day + TheManCooldown;
                },
            });
            return Finished(card);
        }

        /// <summary>Every row that carries no intent of its own is a choice on the
        /// card itself - WALK AWAY, NOT YET - and gets its index once the rows are all
        /// on it, so the trace prints "Card:TestBuy/WALK AWAY".</summary>
        static EventCard Finished(EventCard card)
        {
            for (var i = 0; i < card.Choices.Count; i++)
            {
                var row = card.Choices[i];
                if (row.Intent.Kind == HouseIntentKind.None)
                    row.Intent = HouseIntent.Choose(card, i, HouseMind.TierCollect,
                        row.Label.ToLowerInvariant());
            }
            return card;
        }

        static ConnectionPath FirstOpen(int paths)
        {
            for (var p = 0; p < 4; p++)
                if ((paths & (1 << p)) != 0)
                    return (ConnectionPath)p;
            return ConnectionPath.Column;
        }

        /// <summary>The man dealt for a Column, Cell or Bar card - a real man off the
        /// same seeder the classified column uses, priced by the same wage table.</summary>
        static HireAd DealTheAd(Roster roster, System.Random rng, EventContext ctx,
            ConnectionLine line, Background trade, int seed)
        {
            var man = RosterSeeder.Deal(roster, rng, HireMarket.AdvertisedCeilingHalfSteps,
                Potential.Mix(Potential.StreamFor(ctx.CitySeed, -3), seed));
            man.Rank = Rank.Hood;
            man.WageAsked = Wages.AskFor(man);
            var box = "BOX " + (11 + rng.Next(80)) + "-" + (char)('A' + rng.Next(8));
            return new HireAd
            {
                Man = man,
                Trade = CharacterAttribute.Connections,
                From = line == ConnectionLine.Field ? "THE COUNTY FIELD" : "HARBOR ROW",
                Box = box,
            };
        }

        static EventCard DealBroker(HouseView view, EventContext ctx, int seed)
        {
            var roster = view.Roster;
            var connection = ctx.Connection;
            var speaker = SpeakerFor(view, ctx, connection.ManId, out var crewId);
            var lt = roster.Find(speaker);
            var man = roster.Find(connection.ManId);
            var door = BrokerDoor(view, ctx);
            var doorWord = DoorWord(door);
            var line = LineOf(ctx);
            var crew = CrewOf(view);
            if (crewId < 0 && crew != null)
                crewId = crew.Id;
            var block = BlockOfDoor(view, door);
            var watch = ConnectionText.Watch(view.PoliceAttention(block), ctx.RaidThreshold, doorWord);

            var card = new EventCard
            {
                Id = CardId.BrokerRumour,
                Def = EventId.BrokerRumour,
                Speaker = speaker,
                SpeakerName = lt != null ? lt.FullName : "THE DESK",
                Title = "THE BROKER",
                Line = line,
                CrewId = crewId,
                ManId = connection.ManId,
            };
            var ltName = lt != null ? lt.FirstName : "the desk";
            var manName = man != null ? man.FirstName : "our man";
            card.Lines.Add(ConnectionText.Fill(ConnectionText.Rumour(line), ltName, manName,
                "", "", "", "", doorWord));
            card.Lines.Add("It costs " + UI.LedgerText.Cash(Connection.BrokerFee) +
                           " to sit at his table, and " + ltName + " goes himself. " +
                           char.ToUpperInvariant(watch[0]) + watch.Substring(1) + ".");

            var job = new Job
            {
                Type = OrderType.Meet,
                CrewId = crewId,
                GangId = ctx.GangId,
                Men = 2,
                TargetBusinessId = door.Value,
                TargetLabel = doorWord,
                TargetWorth = Connection.BrokerFee,
            };
            card.Choices.Add(new EventChoice
            {
                Label = "MEET THE MAN",
                Cost = Connection.BrokerFee,
                NeedsCrew = true,
                Risk = watch,
                Note = ltName + " and one man walk to " + doorWord + " with " +
                       UI.LedgerText.Cash(Connection.BrokerFee) + ".",
                Appeal = v => 0.75f,
                Intent = HouseIntent.Work(job, HouseMind.TierCollect,
                    "the broker's table at " + doorWord),
            });
            card.Choices.Add(new EventChoice
            {
                Label = "NOT YET",
                Cost = 0,
                Note = "Five days before he is asked again.",
                Appeal = v => 0.15f,
                OnChosen = (c, day) =>
                {
                    if (c.Connection != null)
                    {
                        c.Connection.CoolUntilDay = day + Connection.RobbedCoolDays;
                        c.Connection.Touch();
                    }
                },
            });
            card.Dealt = (c, day) => c.Connection?.NamedTheBroker(day);
            card.DoorToLearn = door.Value;
            return Finished(card);
        }

        static EventCard DealTestBuy(HouseView view, EventContext ctx, int seed)
        {
            var roster = view.Roster;
            var connection = ctx.Connection;
            var speaker = SpeakerFor(view, ctx, connection.ManId, out var crewId);
            var lt = roster.Find(speaker);
            var door = BrokerDoor(view, ctx);
            var doorWord = DoorWord(door);
            var crew = CrewOf(view);
            if (crewId < 0 && crew != null)
                crewId = crew.Id;
            var block = BlockOfDoor(view, door);
            var watch = ConnectionText.Watch(view.PoliceAttention(block), ctx.RaidThreshold, doorWord);
            var ltName = lt != null ? lt.FirstName : "the desk";

            var card = new EventCard
            {
                Id = CardId.TestBuy,
                Def = EventId.TestBuy,
                Speaker = speaker,
                SpeakerName = lt != null ? lt.FullName : "THE DESK",
                Title = "THE TEST BUY",
                Line = LineOf(ctx),
                CrewId = crewId,
                ManId = connection.ManId,
            };
            card.Lines.Add("The Cuban will sell two kilos to see if we're serious. " +
                           UI.LedgerText.Cash(Connection.TestBuyPrice) + " at " + doorWord +
                           ", cash, and it goes in our room the same night.");
            card.Lines.Add(char.ToUpperInvariant(watch[0]) + watch.Substring(1) + ".");

            Job JobFor(int worth) => new Job
            {
                Type = OrderType.TestBuy,
                CrewId = crewId,
                GangId = ctx.GangId,
                Men = 2,
                TargetBusinessId = door.Value,
                TargetLabel = doorWord,
                TargetWorth = worth,
            };
            card.Choices.Add(new EventChoice
            {
                Label = "PAY",
                Cost = Connection.TestBuyPrice,
                NeedsCrew = true,
                Risk = watch,
                Note = ltName + " walks two men to " + doorWord + " with the full " +
                       UI.LedgerText.Cash(Connection.TestBuyPrice) + ".",
                Appeal = v => v.Safe >= 2 * Connection.TestBuyPrice ? 0.8f : 0.5f,
                Intent = HouseIntent.Work(JobFor(Connection.TestBuyPrice), HouseMind.TierCollect,
                    "two kilos to see if we're serious"),
            });
            card.Choices.Add(new EventChoice
            {
                Label = "SEND TWO MEN",
                Cost = Connection.KiloPrice,
                NeedsCrew = true,
                Risk = watch,
                Note = "Half the money and a harder bargain - the Cuban may come up short.",
                Appeal = v => v.Safe >= 2 * Connection.TestBuyPrice ? 0.4f : 0.6f,
                Intent = HouseIntent.Work(JobFor(Connection.KiloPrice), HouseMind.TierCollect,
                    "half the money and a harder bargain"),
            });
            card.Choices.Add(new EventChoice
            {
                Label = "WALK AWAY",
                Cost = 0,
                Note = "Back to the rumour. Ten days before the Cuban takes a call.",
                Appeal = v => 0.1f,
                OnChosen = (c, day) => c.Connection?.WalkedAwayFromTheBuy(day),
            });
            return Finished(card);
        }

        static EventCard DealTerms(HouseView view, EventContext ctx, int seed)
        {
            var roster = view.Roster;
            var connection = ctx.Connection;
            var speaker = SpeakerFor(view, ctx, connection.ManId, out var crewId);
            var lt = roster.Find(speaker);
            var direct = ctx.World != null && ctx.World.DirectManId >= 0 &&
                         ctx.World.DirectManId == connection.ManId;
            var grade = direct ? SupplierGrade.Direct : SupplierGrade.Broker;
            var line = LineOf(ctx);
            var price = Connection.PriceFor(connection.Trust, grade);
            var load = Connection.MinLoadFor(line, grade);
            var credit = Connection.CreditAt(connection.Trust, grade);
            var creditAt = grade == SupplierGrade.Direct
                ? Connection.DirectCreditTrust
                : Connection.CreditTrust;
            var ltName = lt != null ? lt.FirstName : "the desk";

            var card = new EventCard
            {
                Id = CardId.SupplierTerms,
                Def = EventId.SupplierTerms,
                Speaker = speaker,
                SpeakerName = lt != null ? lt.FullName : "THE DESK",
                Title = "THE TERMS",
                Line = line,
                CrewId = crewId,
                ManId = connection.ManId,
            };
            if (direct)
                card.Lines.Add("He's not a broker. He's Pablo's. The line goes straight to Medellin.");
            card.Lines.Add((line == ConnectionLine.Field ? "A plane a week, " : "A boat a week, ") +
                           load + " kilos and never less, at " + UI.LedgerText.Cash(price) +
                           " a kilo. Trust " + connection.Trust + " - he takes " +
                           (connection.Trust / 10) + " per cent off" +
                           (grade == SupplierGrade.Direct ? " and a fifth off for Pablo" : "") +
                           "; at " + creditAt + " he gives credit for half" +
                           (credit ? ", and he does" : "") + ".");
            card.Lines.Add("Say yes and the line is ours. " +
                           (lt != null ? lt.FirstName : "The desk") +
                           " says the man who introduced us is not needed after that.");

            card.Choices.Add(new EventChoice
            {
                Label = "ACCEPT",
                Cost = 0,
                Note = "The first load lands in seven days, " + load + " kilos at " +
                       UI.LedgerText.Cash(load * price) + (credit ? " (half on credit)" : "") + ".",
                Appeal = v => 0.9f,
                Intent = HouseIntent.AcceptTerms(HouseMind.TierCollect,
                    "the supplier's terms: " + load + " kilos at " + UI.LedgerText.Cash(price)),
            });
            card.Choices.Add(new EventChoice
            {
                Label = "NOT THESE TERMS",
                Cost = 0,
                Note = "He asks again tomorrow. Unanswered three days, trust falls.",
                Appeal = v => 0.05f,
            });
            return Finished(card);
        }
    }
}
