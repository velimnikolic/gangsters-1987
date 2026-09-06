using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using UnityEngine;

namespace LivingCity.UI
{
    public enum WireAction { Record, Person, Door, Block, Law, Finances, Families }

    /// <summary>
    /// How hard a slip hits, which is NOT the same question as which pen it is written
    /// in. The register sets a severe line in bold ink under a filled square and rules
    /// its edge; a routine one is a hollow square and regular type. Red is the pen for
    /// blood AND for a door that came up empty, and those two are not the same night.
    /// </summary>
    public enum WireWeight { Routine, Notable, Severe }

    /// <summary>
    /// One line of the wire, whoever is printing it.
    ///
    /// Nothing here composes a sentence. IncidentText wrote the incident's line the day
    /// it happened and TerritoryStandingVocabulary wrote the door's, so the paper, the
    /// ledger's rail and the strip over the street all print the same words about the
    /// same night - the one rule the wire has ever had.
    /// </summary>
    public readonly struct WireLine
    {
        public readonly string Source, Stamp, Body, Tag, Figure;
        public readonly Color Ink;

        /// <summary>How hard it hits - the register's hierarchy.</summary>
        public readonly WireWeight Weight;

        /// <summary>Which of the two books filed it: the racket's doors, or our own
        /// men. They are counted on different clocks and the register keeps them in
        /// separate runs under one day.</summary>
        public readonly bool FromDoor;

        /// <summary>Police attention the night drew, and money that changed hands.
        /// TWO figures and never one: a wire that prints them in one slot makes a
        /// reader do arithmetic across units.</summary>
        public readonly int Heat, Money;

        /// <summary>The money came IN. A door that paid short still carries a figure,
        /// and a day's takings must not count what was owed as what was taken.</summary>
        public readonly bool MoneyIn;

        /// <summary>The campaign day it belongs to, for ordering a whole book of them.</summary>
        public readonly int Day;

        /// <summary>The door this slip is about, where it is about one. Invalid on an
        /// incident, which happened to MEN and not at an address - a surface that offers
        /// to open the door beside the line has to be able to tell the two apart.</summary>
        public readonly TerritoryBusinessId BusinessId;

        /// <summary>The block the slip belongs to, where it belongs to one. Invalid on
        /// an incident, which is why THIS BLOCK drops them.</summary>
        public readonly TerritoryBlockId BlockId;

        // Presentation targets are reconstructed from the persisted incident/dispatch.
        // Keep IDs, never references to streamed actors or an old menu's commands.
        public readonly int CharacterId;
        public readonly WireAction Action;

        /// <summary>Where the slip came from, without the wire's own prefix - the
        /// quarter it happened in, or the racket. It was in the data from the first
        /// day and no surface ever printed it.</summary>
        public string Origin =>
            Source != null && Source.StartsWith("WIRE - ") ? Source.Substring(7) : "";

        /// <summary>The hour off a door slip's stamp, and nothing at all off an
        /// incident's: a man's night is filed to the day, and a slip that borrowed an
        /// hour it was never given would be the page inventing a fact.</summary>
        public string ClockFace
        {
            get
            {
                var mark = Stamp != null ? Stamp.LastIndexOf('·') : -1;
                return mark >= 0 ? Stamp.Substring(mark + 1).Trim() : "";
            }
        }

        /// <summary>The register's FILE column - the same destination in one word, so a
        /// reader can see down the column what each line opens. It sits beside the key's
        /// own wording because two words for one destination must never drift apart.</summary>
        public string FileWord => Action switch
        {
            WireAction.Person => "DOSSIER",
            WireAction.Door => "DOOR",
            WireAction.Block => "BLOCK",
            WireAction.Law => "THE LAW",
            WireAction.Finances => "FINANCES",
            WireAction.Families => "FAMILIES",
            _ => "ITEM",
        };

        /// <summary>The destination key's word on the slip.</summary>
        public string ActionLabel => Action switch
        {
            WireAction.Person => "OPEN DOSSIER",
            WireAction.Door => "DOOR ACTIONS",
            WireAction.Block => "OPEN BLOCK",
            WireAction.Law => "OPEN THE LAW",
            WireAction.Finances => "OPEN FINANCES",
            WireAction.Families => "OPEN FAMILIES",
            _ => "READ ITEM",
        };

        public WireLine(string source, string stamp, string body, string tag,
            string figure, Color ink, int day)
            : this(source, stamp, body, tag, figure, ink, day, default, default)
        {
        }

        public WireLine(string source, string stamp, string body, string tag,
            string figure, Color ink, int day,
            TerritoryBusinessId businessId, TerritoryBlockId blockId)
            : this(source, stamp, body, tag, figure, ink, day, businessId, blockId,
                -1, businessId.IsValid ? WireAction.Door :
                    blockId.IsValid ? WireAction.Block : WireAction.Record)
        {
        }

        public WireLine(string source, string stamp, string body, string tag,
            string figure, Color ink, int day, TerritoryBusinessId businessId,
            TerritoryBlockId blockId, int characterId, WireAction action,
            WireWeight weight = WireWeight.Routine, bool fromDoor = false,
            int heat = 0, int money = 0, bool moneyIn = false)
        {
            Weight = weight;
            FromDoor = fromDoor;
            Heat = heat;
            Money = money;
            MoneyIn = moneyIn;
            Source = source;
            Stamp = stamp;
            Body = body;
            Tag = tag;
            Figure = figure;
            Ink = ink;
            Day = day;
            BusinessId = businessId;
            BlockId = blockId;
            CharacterId = characterId;
            Action = action;
        }
    }

    /// <summary>
    /// Everything that has come in over the wire since the first morning, out of the two
    /// books that keep it: the campaign's incidents - what our men did that nobody
    /// ordered - and OUR racket's door dispatches - the answer an owner gave, the front
    /// that went in. Rival houses use the same simulation ledger, but their private
    /// doorstep answers are not slips on the player's wire.
    ///
    /// The books are kept by different systems and this composes neither. It only reads
    /// them, dresses each entry the one way the design dresses a wire slip, and hands
    /// back the run. The street strip takes the head of it; the ledger's rail takes all
    /// of it and lets the boss scroll back to day one.
    /// </summary>
    public static class WireBook
    {
        /// <summary>The shared racket ledger holds every family's visits. A player-facing
        /// wire is entitled only to the house whose book it is.</summary>
        public static bool IsPlayerDispatch(TerritoryDoorDispatch dispatch) =>
            dispatch.GangId.Value == Gangs.GangCatalog.PlayerGangId;

        static int PreviousPlayerDoor(
            IReadOnlyList<TerritoryDoorDispatch> doors, int index)
        {
            while (doors != null && index >= 0)
            {
                if (IsPlayerDispatch(doors[index]))
                    return index;
                index--;
            }
            return -1;
        }

        /// <summary>
        /// The whole run, newest first.
        ///
        /// THREE books, not two, and the first of them is easy to miss: the campaign
        /// keeps the day's incidents in one list and sweeps them into the book at the day
        /// tick, so a wire that read only the first would go blank every midnight and one
        /// that read only the second would never show what happened this afternoon.
        ///
        /// Neither book is bottomless. The campaign keeps its last CampaignRunner.
        /// IncidentsKept filed nights and the racket its last TerritoryRacket.
        /// DispatchesKept doors; what fell off the back of those fell off before this was
        /// asked, and nothing here can put it back.
        ///
        /// The books are counted on DIFFERENT clocks - the campaign's day tick and the
        /// city clock's hour - so the day is the finest grain this list will claim.
        /// Inside one day the books are left in their own order rather than shuffled
        /// against each other on a comparison neither of them supports, and every slip
        /// prints its own stamp so a reader is never told an order that was invented
        /// for him.
        /// </summary>
        public static void Collect(OutfitDirector outfit, List<WireLine> into)
        {
            into.Clear();

            var today = outfit != null ? outfit.Incidents : null;
            var book = outfit != null ? outfit.IncidentBook : null;
            var doors = TerritoryRuntime.Instance?.Racket?.Dispatches;

            // One index over both incident lists, walked backwards: today's nights first,
            // then the filed ones under them.
            var todayCount = today != null ? today.Count : 0;
            var bookCount = book != null ? book.Count : 0;
            var incident = todayCount + bookCount - 1;
            var door = PreviousPlayerDoor(doors, doors != null ? doors.Count - 1 : -1);

            Incident At(int index) =>
                index >= bookCount ? today[index - bookCount] : book[index];

            while (incident >= 0 || door >= 0)
            {
                if (incident < 0)
                {
                    into.Add(Of(doors[door]));
                    door = PreviousPlayerDoor(doors, door - 1);
                    continue;
                }
                if (door < 0)
                {
                    into.Add(Of(At(incident--)));
                    continue;
                }

                // Equal days keep the incident first: it is the campaign's own book, and
                // a tie is not evidence of an order.
                if (doors[door].Day > At(incident).Day)
                {
                    into.Add(Of(doors[door]));
                    door = PreviousPlayerDoor(doors, door - 1);
                }
                else
                    into.Add(Of(At(incident--)));
            }
        }

        /// <summary>Door slips belonging to the player, excluding rival traffic in the
        /// shared simulation ledger.</summary>
        public static int PlayerDoorCount()
        {
            var doors = TerritoryRuntime.Instance?.Racket?.Dispatches;
            var count = 0;
            if (doors == null)
                return count;
            for (var i = 0; i < doors.Count; i++)
                if (IsPlayerDispatch(doors[i]))
                    count++;
            return count;
        }

        /// <summary>How many slips the books come to - the figure a head prints without
        /// laying a single one of them out.</summary>
        public static int Count(OutfitDirector outfit)
        {
            var filed = outfit != null
                ? outfit.Incidents.Count + outfit.IncidentBook.Count
                : 0;
            return filed + PlayerDoorCount();
        }

        /// <summary>
        /// A fingerprint of only the player's door slips. AI traffic must not repaint
        /// the strip and make an old player line animate as if it had just arrived.
        /// </summary>
        public static int PlayerDoorVersion()
        {
            var doors = TerritoryRuntime.Instance?.Racket?.Dispatches;
            unchecked
            {
                var hash = 17;
                if (doors == null)
                    return hash;
                for (var i = 0; i < doors.Count; i++)
                {
                    var dispatch = doors[i];
                    if (!IsPlayerDispatch(dispatch))
                        continue;
                    hash = hash * 31 + dispatch.BusinessId.GetHashCode();
                    hash = hash * 31 + dispatch.BlockId.GetHashCode();
                    hash = hash * 31 + (int)dispatch.News;
                    hash = hash * 31 + dispatch.GameHour.GetHashCode();
                    hash = hash * 31 + dispatch.Amount;
                    hash = hash * 31 + (int)dispatch.Excuse;
                    hash = hash * 31 + dispatch.Stops;
                    hash = hash * 31 + dispatch.Short;
                }
                return hash;
            }
        }

        /// <summary>
        /// A figure that moves whenever anything on the wire does - what a surface holds
        /// on to so it can tell a night with news on it from the four hundredth repaint
        /// of a quiet one.
        ///
        /// It cannot be the count. Both books are capped: once they are full the oldest
        /// line drops off as the newest lands and the total sits still while the whole
        /// wire changes under it. So the day and the racket's own filing counter are
        /// mixed in - between them nothing can be added anywhere without this moving.
        /// </summary>
        public static int Version(OutfitDirector outfit)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (outfit != null ? outfit.Campaign.Day : 0);
                hash = hash * 31 + (outfit != null ? outfit.Incidents.Count : 0);
                hash = hash * 31 + (outfit != null ? outfit.IncidentBook.Count : 0);
                hash = hash * 31 + PlayerDoorVersion();
                return hash;
            }
        }

        /// <summary>
        /// The money on this slip came IN. Only two answers at a door do that - the
        /// envelope handed over and the round carried home - and the day's takings are
        /// summed off nothing else, so the register never reports what a shopkeeper
        /// owed as though the outfit had it.
        /// </summary>
        public static bool Received(TerritoryDoorNews news) =>
            news == TerritoryDoorNews.Agreed || news == TerritoryDoorNews.RoundBanked;

        /// <summary>
        /// How hard an incident hits. Severe is a body, a gun fired, a man taken or
        /// sold, a sentence read out; notable is anything the boss must answer this
        /// week; routine is the rest of the traffic. The register's whole hierarchy
        /// hangs off this one table - never off the pen, which answers a different
        /// question.
        /// </summary>
        public static WireWeight WeightOf(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Escalated:
                case IncidentKind.DiedOnTheDetail:
                case IncidentKind.StoppedIt:
                case IncidentKind.TookRivalMoney:
                case IncidentKind.Defected:
                case IncidentKind.Demoted:
                case IncidentKind.Convicted:
                case IncidentKind.TakenIn:
                case IncidentKind.WitnessKilled:
                case IncidentKind.FiredOnTheOfficer:
                case IncidentKind.PrisonerKilled:
                case IncidentKind.TransferHalted:
                case IncidentKind.FlatRaided:
                    return WireWeight.Severe;
                case IncidentKind.Froze:
                case IncidentKind.Fled:
                case IncidentKind.Deviated:
                case IncidentKind.CaughtSkimming:
                case IncidentKind.SkimmedTheStash:
                case IncidentKind.BearsWatching:
                case IncidentKind.NotToBeTrusted:
                case IncidentKind.DemandedARaise:
                case IncidentKind.PayrollShort:
                case IncidentKind.KilosSold:
                case IncidentKind.StatementTaken:
                case IncidentKind.CaseOpened:
                case IncidentKind.WitnessWithdrawn:
                case IncidentKind.BailForfeit:
                case IncidentKind.CutLoose:
                case IncidentKind.Acquitted:
                case IncidentKind.Promoted:
                case IncidentKind.Sprung:
                case IncidentKind.RefusedTheOfficer:
                case IncidentKind.RanFromTheOfficer:
                    return WireWeight.Notable;
                default:
                    return WireWeight.Routine;
            }
        }

        /// <summary>The same rank at a door: a front smashed, a man on the floor, a
        /// bag that never came home or a house that pays somebody else is severe; an
        /// answer the boss has to deal with is notable; a week that went the way it
        /// always goes is routine.</summary>
        public static WireWeight WeightOf(TerritoryDoorNews news)
        {
            switch (news)
            {
                case TerritoryDoorNews.Wrecked:
                case TerritoryDoorNews.Beaten:
                case TerritoryDoorNews.OwnerBeaten:
                case TerritoryDoorNews.RoundLost:
                case TerritoryDoorNews.ChangedHands:
                    return WireWeight.Severe;
                case TerritoryDoorNews.Refused:
                case TerritoryDoorNews.StoppedPaying:
                case TerritoryDoorNews.PaidShort:
                case TerritoryDoorNews.Threatened:
                case TerritoryDoorNews.RoundBanked:
                    return WireWeight.Notable;
                default:
                    return WireWeight.Routine;
            }
        }

        /// <summary>One incident, dressed as a slip.</summary>
        public static WireLine Of(Incident incident) =>
            new WireLine(
                incident.Where.Length > 0
                    ? "WIRE - " + incident.Where.ToUpperInvariant()
                    : "WIRE",
                "DAY " + incident.Day,
                incident.Line,
                LedgerText.IncidentLabel(incident.Kind),
                // The figure the design puts beside the tag is whatever this one cost.
                // For an incident that is the police attention it drew, and an incident
                // that drew none says nothing rather than nothing-shaped.
                incident.Heat > 0 ? "+" + incident.Heat + " HEAT" : "",
                InkOf(incident.Kind),
                incident.Day, default, default, incident.CharacterId,
                ActionOf(incident), WeightOf(incident.Kind), false, incident.Heat);

        static WireAction ActionOf(Incident incident)
        {
            switch (incident.Kind)
            {
                case IncidentKind.ComplaintRung:
                case IncidentKind.StatementTaken:
                case IncidentKind.CaseOpened:
                case IncidentKind.WitnessWithdrawn:
                case IncidentKind.WitnessKilled:
                case IncidentKind.BailPosted:
                case IncidentKind.BailForfeit:
                case IncidentKind.Convicted:
                case IncidentKind.Acquitted:
                case IncidentKind.CaseDismissed:
                case IncidentKind.CutLoose:
                case IncidentKind.FlatRaided:
                case IncidentKind.NobodyCame:
                case IncidentKind.RefusedTheOfficer:
                case IncidentKind.RanFromTheOfficer:
                case IncidentKind.FiredOnTheOfficer:
                case IncidentKind.TakenIn:
                case IncidentKind.Sprung:
                case IncidentKind.PrisonerKilled:
                case IncidentKind.TransferHalted:
                case IncidentKind.WalkedIn:
                    return WireAction.Law;
                case IncidentKind.PayrollShort:
                case IncidentKind.KilosSold:
                    return WireAction.Finances;
                case IncidentKind.AWordBetweenHouses:
                    return WireAction.Families;
                default:
                    return incident.CharacterId >= 0 ? WireAction.Person : WireAction.Record;
            }
        }

        /// <summary>One thing that happened at a door, in the racket's own words.</summary>
        public static WireLine Of(TerritoryDoorDispatch dispatch)
        {
            var name = "";
            var blockName = "";
            var block = dispatch.BlockId;
            var rows = Business.CityBusinesses.All;
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Id == dispatch.BusinessId)
                {
                    name = rows[i].Name;
                    if (!block.IsValid)
                        block = rows[i].CanonicalBlockId;
                    break;
                }

            // A ROUND slip is about a block, not a door, and it carries its own. The
            // name is looked up the same way the shop's is.
            if (block.IsValid)
            {
                var query = RoadDemo.TerritoryRuntime.Instance?.PlayerQuery;
                if (query != null && query.TryGetBlock(block, out var view) && view != null)
                    blockName = view.BlockName;
            }

            var ours = dispatch.GangId ==
                new TerritoryGangId(Gangs.GangCatalog.PlayerGangId);
            // A door slip carries its address: the ledger's rail can be narrowed to one
            // block by it, and a click on the line opens that door's own menu.
            return new WireLine(
                ours ? "WIRE - THE RACKET" : "WIRE - ANOTHER HOUSE",
                "DAY " + dispatch.Day + " · " + Clock(dispatch.HourOfDay),
                TerritoryStandingVocabulary.Default.Describe(dispatch, name, blockName),
                LedgerText.DoorNewsLabel(dispatch.News),
                dispatch.Amount > 0 ? LedgerText.Cash(dispatch.Amount) : "",
                DoorInk(dispatch.News),
                dispatch.Day,
                dispatch.BusinessId,
                block,
                -1,
                dispatch.BusinessId.IsValid ? WireAction.Door :
                    block.IsValid ? WireAction.Block : WireAction.Record,
                WeightOf(dispatch.News), true, 0, dispatch.Amount,
                Received(dispatch.News));
        }

        /// <summary>The hour of a slip, as a clock face. A door answers at a time, and
        /// two slips filed on one day have to read in the order they happened.</summary>
        static string Clock(double hourOfDay)
        {
            var hour = (int)hourOfDay;
            var minute = (int)((hourOfDay - hour) * 60.0);
            return hour.ToString("00") + ":" + minute.ToString("00");
        }

        /// <summary>
        /// The ink a slip's edge is ruled in - the design's rule that a wire is read by
        /// colour before it is read by word. Every one of these is a pen the book
        /// already writes in: the red for blood, the blue ballpoint for a man of ours
        /// who is no longer ours, amber for money being asked for, green for a promotion,
        /// and plain for the rest.
        /// </summary>
        public static Color InkOf(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Froze:
                case IncidentKind.Fled:
                case IncidentKind.Escalated:
                case IncidentKind.DiedOnTheDetail:
                case IncidentKind.StoppedIt:
                case IncidentKind.Convicted:
                case IncidentKind.BailForfeit:
                case IncidentKind.CutLoose:
                    return LedgerStyle.RedPen;
                case IncidentKind.TookRivalMoney:
                case IncidentKind.Defected:
                case IncidentKind.BearsWatching:
                case IncidentKind.NotToBeTrusted:
                case IncidentKind.Demoted:
                case IncidentKind.CaughtSkimming:
                case IncidentKind.CaseOpened:
                case IncidentKind.WitnessKilled:
                case IncidentKind.RefusedTheOfficer:
                case IncidentKind.RanFromTheOfficer:
                case IncidentKind.FiredOnTheOfficer:
                case IncidentKind.TakenIn:
                case IncidentKind.Sprung:
                    return LedgerStyle.Ballpoint;
                case IncidentKind.DemandedARaise:
                case IncidentKind.ComplaintRung:
                case IncidentKind.StatementTaken:
                case IncidentKind.NobodyCame:
                    return LedgerStyle.PenAmber;
                case IncidentKind.Promoted:
                case IncidentKind.ReadyForACrew:
                case IncidentKind.AGunForHire:
                case IncidentKind.Acquitted:
                case IncidentKind.CaseDismissed:
                case IncidentKind.WitnessWithdrawn:
                case IncidentKind.BailPosted:
                    return LedgerStyle.GreenOk;
                default:
                    return LedgerStyle.TelexPlain;
            }
        }

        /// <summary>The same rule at a door: a no or a house lost in the red pen, hands
        /// laid on a man or his front in ballpoint, the rest plain.</summary>
        public static Color DoorInk(TerritoryDoorNews news)
        {
            switch (news)
            {
                case TerritoryDoorNews.Refused:
                case TerritoryDoorNews.StoppedPaying:
                case TerritoryDoorNews.ChangedHands:
                case TerritoryDoorNews.Missed:
                case TerritoryDoorNews.RoundLost:
                    return LedgerStyle.RedPen;
                case TerritoryDoorNews.PaidShort:
                    return LedgerStyle.PenAmber;
                case TerritoryDoorNews.RoundBanked:
                case TerritoryDoorNews.Reopened:
                    return LedgerStyle.GreenOk;
                case TerritoryDoorNews.RoundOut:
                    return LedgerStyle.TelexPlain;
                case TerritoryDoorNews.Wrecked:
                case TerritoryDoorNews.Beaten:
                case TerritoryDoorNews.OwnerBeaten:
                case TerritoryDoorNews.Threatened:
                    return LedgerStyle.Ballpoint;
                default:
                    return LedgerStyle.TelexPlain;
            }
        }
    }
}
