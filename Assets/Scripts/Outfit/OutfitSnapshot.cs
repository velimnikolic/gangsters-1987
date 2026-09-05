using System;
using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Outfit
{
    [Serializable]
    public sealed class DaySheetDto
    {
        public int day;
        public int legalIncome;
        public int illegalIncome;
        public int jobIncome;
        public int salesIncome;
        public int bribes;
        public int purchases;
        public int otherCosts;
        public int wagesPaid;
        public int taxPaid;
        public bool closed;

        /// <summary>BETWEEN THE HOUSES (EPIC 42). Appended; a file from before reads 0.</summary>
        public int fromHouses;
        public int toHouses;
    }

    /// <summary>One tribute claim on a house's book (EPIC 42): the envelope, its day,
    /// whether it is overdue, and the terms pinned on it. The book was never in the
    /// file before; a load re-assessed every levy from the turf and lost the terms.</summary>
    [Serializable]
    public sealed class LevyDto
    {
        public int gangId;
        public int amount;
        public int dueDay;
        public bool overdue;
        public int pinnedAmount;
        public int pinnedUntilDay;
    }

    [Serializable]
    public sealed class AccountsDto
    {
        public int safe;
        public int riskyMoney;
        public DaySheetDto[] sheets;
    }

    [Serializable]
    public sealed class JobDto
    {
        public int id;
        public int crewId;
        public int type;
        public int gangId;
        public int[] blockTargets;
        public int targetWorth;
        public int targetCharacterId;
        public int targetBlockId;
        public float targetX;
        public float targetZ;
        public string targetLabel;
        public string targetBusinessId;
        public int men;
        public int issuedDay;
        public int stage;
        public float travelHoursLeft;
        public float workHoursLeft;
        public int daysStood;
        public int bookDepth;

        /// <summary>-1 for "the street has not answered yet", else the outcome.</summary>
        public int streetOutcome;

        /// <summary>The proposal a sit-down carries (EPIC 42); 0 for none.</summary>
        public int proposalId;
    }

    [Serializable]
    public sealed class RunnerDto
    {
        public int gangId;
        public int day;
        public int heat;
        public int seed;
        public int nextJobId;
        public AccountsDto accounts;
        public JobDto[] jobs;

        /// <summary>How the campaign ended and when, and the run of broke nights it
        /// was on. All four default to a running campaign, which is what every file
        /// written before the endings existed was.</summary>
        public bool fallen;
        public int fallenOnDay;
        public int ending;
        public int brokeNights;

        /// <summary>The tribute book (EPIC 42). Null in a file from before: the next
        /// midnight re-assesses it from the turf, as it always did.</summary>
        public LevyDto[] levies;
    }

    [Serializable]
    public sealed class StanceDto
    {
        public int a;
        public int b;
        public int stance;
        public bool pending;

        /// <summary>EPIC 42: who wrote a pending stance (-1 in a file from before), and
        /// whether a pact wrote it. A pact honours against the declarer, and a war a
        /// pact declared wakes no other pact - neither may be lost at a save.</summary>
        public int by = -1;
        public bool byPact;
    }

    [Serializable]
    public sealed class GrievanceDto
    {
        public int aggrieved;
        public int offender;
        public float owed;
    }

    [Serializable]
    public sealed class RelationsDto
    {
        public StanceDto[] stances;
        public GrievanceDto[] grievances;

        /// <summary>EPIC 42: the day's agreements, the killings the floor reads, what
        /// money cleared today. Appended; a file without them reads as none.</summary>
        public AgreementDto[] agreements;
        public KillingDto[] killings;
        public ClearedDto[] cleared;
    }

    [Serializable]
    public sealed class HouseDto
    {
        public int gangId;
        public string front;
        public double nextThinkHour;
        public RosterDto roster;
        public RunnerDto runner;

        /// <summary>EPIC 40. Both nullable: a file written before the connection
        /// existed reads as a house with none and an empty book. No version bump.</summary>
        public ConnectionDto connection;
        public EventBookDto events;
    }

    [Serializable]
    public sealed class ConnectionDto
    {
        public int stage;
        public int line;
        public int paths;
        public int manId = -1;
        public int manTrade;
        public int supplierGrade;
        public int trust;
        public int kilos;
        public int pricePerKilo;
        public int minLoad;
        public int nextLoadDay;
        public int burnedUntilDay;
        public int withoutManSinceDay;
        public int lastLoadDay;
        public int coolUntilDay;
        public int lastExploreDay = -1;
        public int meetAttempts;
        public int buyAttempts;
        public int soldThisWeek;
        public int soldWeek = -1;
        public int owedTomorrow;
        public bool loadHeld;
    }

    [Serializable]
    public sealed class PendingCardDto
    {
        public int id;
        public int def;
        public int dealtDay;
        public int expiresDay;
        public int speaker = -1;
        public int hold;

        /// <summary>The frozen half of the deal, so a re-deal is the same offer.</summary>
        public int path;
        public int line;
        public int trade;
        public int manId = -1;
        public int cellmateId = -1;
        public int crewId = -1;
        public string door = "";
    }

    [Serializable]
    public sealed class EventPotDto
    {
        public int id;
        public float pot;
    }

    [Serializable]
    public sealed class EventDayDto
    {
        public int id;
        public int day;
    }

    [Serializable]
    public sealed class WireDto
    {
        public int day;
        public string text;
        public bool isPublic;
        public bool filed;
    }

    [Serializable]
    public sealed class EventBookDto
    {
        public PendingCardDto pending;
        public EventPotDto[] pots;
        public EventDayDto[] fired;
        public EventDayDto[] cooling;
        public WireDto[] wire;
        public int cardsDealt;
        public int cardsAnswered;
        public int cardsExpired;
        public string lastAnswer;
    }

    /// <summary>One flat on somebody's deed (EPIC 27). Written at the underworld level
    /// rather than per house because the book is the CITY's - a rival's room and ours are
    /// rows of the same ledger, keyed on the building the plan deals.</summary>
    [Serializable]
    public sealed class FlatDto
    {
        public string building;
        public int floor;
        public int slot;
        public int gangId;
        public string name;
        public int role;
        public int keeper;
        public int paidRole;
        public int bank;
        public int raidUntilDay;
        public int boughtOnDay;
        public int staff;
    }

    /// <summary>The proposal book, flat (EPIC 42): arrays only, appended to the
    /// underworld like the relations were, so a file without it reads as an empty book
    /// and needs no version bump.</summary>
    [Serializable]
    public sealed class DiplomacyDto
    {
        public ProposalDto[] proposals;
        public KeepOffDto[] keepOffs;
        public int nextId;
        public LineDto[] lines;
        public PactDto[] pacts;
    }

    [Serializable]
    public sealed class UnderworldDto
    {
        public int citySeed;
        public HouseDto[] houses;
        public RelationsDto relations;
        public FlatDto[] flats;
        public DiplomacyDto diplomacy;

        /// <summary>Pablo's man (EPIC 40): which signing is his, his id once bound,
        /// the day he will take a call again, and how many have been signed.</summary>
        public int directTurn = 1;
        public int directManId = -1;
        public int directNotBeforeDay;
        public int theManSigned;
    }

    /// <summary>
    /// THE TWENTY-ONE BOOKS, WRITTEN DOWN AND READ BACK.
    ///
    /// Money, days, orders in flight, who stands where with whom, and every man of every
    /// family. Copied field by field: a save that loses a value because somebody moved
    /// it behind a property is a save nobody can trust, and this way the compiler says so.
    ///
    /// What is NOT here, on purpose: positions. Bodies are re-stood from the rosters at
    /// their fronts (D19), because where a man happened to be standing is not campaign
    /// state - it is a frame.
    /// </summary>
    public static class OutfitSnapshot
    {
        public static UnderworldDto Snapshot(Underworld underworld)
        {
            if (underworld == null)
                return null;

            var dto = new UnderworldDto
            {
                citySeed = underworld.CitySeed,
                directTurn = underworld.DirectTurn,
                directManId = underworld.DirectManId,
                directNotBeforeDay = underworld.DirectNotBeforeDay,
                theManSigned = underworld.TheManSigned,
                houses = new HouseDto[underworld.Count],
                relations = Snapshot(underworld.Relations),
                flats = SnapshotFlats(),
                diplomacy = Snapshot(underworld.Diplomacy),
            };
            for (var g = 0; g < underworld.Count; g++)
                dto.houses[g] = Snapshot(underworld.Of(g));
            return dto;
        }

        public static void Restore(Underworld underworld, UnderworldDto dto)
        {
            if (underworld == null || dto?.houses == null)
                return;

            for (var i = 0; i < dto.houses.Length; i++)
            {
                // A city that holds fewer families than the catalogue names writes an
                // empty slot for every house it never dealt.
                if (dto.houses[i] == null)
                    continue;
                var house = underworld.Of(dto.houses[i].gangId);
                if (house != null)
                    Restore(house, dto.houses[i]);
            }
            Restore(underworld.Relations, dto.relations);
            underworld.DirectTurn = dto.directTurn > 0 ? dto.directTurn : 1;
            underworld.DirectManId = dto.directManId;
            underworld.DirectNotBeforeDay = dto.directNotBeforeDay;
            underworld.TheManSigned = dto.theManSigned;
            RestoreFlats(dto.flats);
            Restore(underworld.Diplomacy, dto.diplomacy);
        }

        // ------------------------------------------------------------------ the flats

        static FlatDto[] SnapshotFlats()
        {
            var book = Property.Apartments.All;
            var flats = new FlatDto[book.Count];
            for (var i = 0; i < book.Count; i++)
            {
                var record = book[i];
                flats[i] = new FlatDto
                {
                    building = record.Unit.Building.Value,
                    floor = record.Unit.Floor,
                    slot = record.Unit.Slot,
                    gangId = record.GangId,
                    name = record.Name,
                    role = (int)record.Role,
                    keeper = record.KeeperId,
                    paidRole = (int)record.PaidRole,
                    bank = record.Bank,
                    raidUntilDay = record.RaidUntilDay,
                    boughtOnDay = record.BoughtOnDay,
                    staff = record.Staff,
                };
            }
            return flats;
        }

        /// <summary>
        /// The deeds back onto the buildings. The city has already been dealt from the
        /// file's own seed by the time this runs, so a building id written last week names
        /// the same building tonight - which is the whole reason the book is keyed on the
        /// PLAN and not on anything the scene composed.
        /// </summary>
        static void RestoreFlats(FlatDto[] flats)
        {
            Property.Apartments.Clear();
            if (flats == null)
                return;
            for (var i = 0; i < flats.Length; i++)
            {
                var flat = flats[i];
                if (flat == null || string.IsNullOrEmpty(flat.building))
                    continue;
                Property.Apartments.Restore(
                    new Property.ApartmentUnitId(
                        new Property.ApartmentBuildingId(flat.building),
                        flat.floor, flat.slot),
                    flat.gangId, flat.name, (Property.UnitRole)flat.role, flat.keeper,
                    (Property.UnitRole)flat.paidRole, flat.bank, flat.raidUntilDay,
                    flat.boughtOnDay, flat.staff);
            }
        }

        // ----------------------------------------------------------------- the house

        static HouseDto Snapshot(House house)
        {
            if (house == null)
                return null;
            return new HouseDto
            {
                gangId = house.GangId,
                front = house.Front.Value,
                nextThinkHour = house.NextThinkHour,
                roster = RosterSnapshot.Snapshot(house.Roster),
                runner = Snapshot(house.Runner, house.GangId),
                connection = Snapshot(house.Runner?.Connection),
                events = Snapshot(house.Runner?.Events),
            };
        }

        // ------------------------------------------------------------ the connection

        static ConnectionDto Snapshot(Connection c)
        {
            if (c == null)
                return null;
            return new ConnectionDto
            {
                stage = (int)c.Stage,
                line = (int)c.Line,
                paths = c.Paths,
                manId = c.ManId,
                manTrade = (int)c.ManTrade,
                supplierGrade = (int)c.Grade,
                trust = c.Trust,
                kilos = c.Kilos,
                pricePerKilo = c.PricePerKilo,
                minLoad = c.MinLoad,
                nextLoadDay = c.NextLoadDay,
                burnedUntilDay = c.BurnedUntilDay,
                withoutManSinceDay = c.WithoutManSinceDay,
                lastLoadDay = c.LastLoadDay,
                coolUntilDay = c.CoolUntilDay,
                lastExploreDay = c.LastExploreDay,
                meetAttempts = c.MeetAttempts,
                buyAttempts = c.BuyAttempts,
                soldThisWeek = c.SoldThisWeek,
                soldWeek = c.SoldWeek,
                owedTomorrow = c.OwedTomorrow,
                loadHeld = c.LoadHeld,
            };
        }

        static void Restore(Connection c, ConnectionDto dto)
        {
            if (c == null)
                return;
            if (dto == null)
            {
                // No block: a house with no connection and nothing on the paper. Said
                // explicitly so a default-enum read can never mean anything else.
                c.Stage = ConnectionStage.None;
                c.Line = ConnectionLine.None;
                c.Grade = SupplierGrade.None;
                c.ManId = -1;
                c.Kilos = 0;
                c.Touch();
                return;
            }
            c.Stage = TryEnum(dto.stage, out ConnectionStage stage) ? stage : ConnectionStage.None;
            c.Line = TryEnum(dto.line, out ConnectionLine line) ? line : ConnectionLine.None;
            c.Paths = dto.paths;
            c.ManId = dto.manId;
            c.ManTrade = TryEnum(dto.manTrade, out Background trade) ? trade : Background.None;
            c.Grade = TryEnum(dto.supplierGrade, out SupplierGrade grade) ? grade : SupplierGrade.None;
            c.Trust = dto.trust;
            c.Kilos = dto.kilos;
            c.PricePerKilo = dto.pricePerKilo;
            c.MinLoad = dto.minLoad;
            c.NextLoadDay = dto.nextLoadDay;
            c.BurnedUntilDay = dto.burnedUntilDay;
            c.WithoutManSinceDay = dto.withoutManSinceDay;
            c.LastLoadDay = dto.lastLoadDay;
            c.CoolUntilDay = dto.coolUntilDay;
            c.LastExploreDay = dto.lastExploreDay;
            c.MeetAttempts = dto.meetAttempts;
            c.BuyAttempts = dto.buyAttempts;
            c.SoldThisWeek = dto.soldThisWeek;
            c.SoldWeek = dto.soldWeek;
            c.OwedTomorrow = dto.owedTomorrow;
            c.LoadHeld = dto.loadHeld;
            // An established relationship never restarts an absence timer.
            if (c.Established)
                c.WithoutManSinceDay = 0;
            c.Touch();
        }

        static EventBookDto Snapshot(EventBook book)
        {
            if (book == null)
                return null;
            var dto = new EventBookDto
            {
                pending = book.Pending == null ? null : new PendingCardDto
                {
                    id = (int)book.Pending.Id,
                    def = (int)book.Pending.Def,
                    dealtDay = book.Pending.DealtDay,
                    expiresDay = book.Pending.ExpiresDay,
                    speaker = book.Pending.Speaker,
                    hold = (int)book.Pending.Hold,
                    path = (int)book.Pending.Path,
                    line = (int)book.Pending.Line,
                    trade = (int)book.Pending.Trade,
                    manId = book.Pending.ManId,
                    cellmateId = book.Pending.CellmateId,
                    crewId = book.Pending.CrewId,
                    door = book.Pending.Door ?? "",
                },
                pots = new EventPotDto[book.Pots.Count],
                fired = new EventDayDto[book.Fired.Count],
                cooling = new EventDayDto[book.Cooling.Count],
                wire = new WireDto[book.Wire.Count],
                cardsDealt = book.CardsDealt,
                cardsAnswered = book.CardsAnswered,
                cardsExpired = book.CardsExpired,
                lastAnswer = book.LastAnswer,
            };
            var i = 0;
            foreach (var pair in book.Pots)
                dto.pots[i++] = new EventPotDto { id = (int)pair.Key, pot = pair.Value };
            i = 0;
            foreach (var pair in book.Fired)
                dto.fired[i++] = new EventDayDto { id = (int)pair.Key, day = pair.Value };
            i = 0;
            foreach (var pair in book.Cooling)
                dto.cooling[i++] = new EventDayDto { id = (int)pair.Key, day = pair.Value };
            for (i = 0; i < book.Wire.Count; i++)
                dto.wire[i] = new WireDto
                {
                    day = book.Wire[i].Day, text = book.Wire[i].Text, isPublic = book.Wire[i].Public,
                    filed = book.Wire[i].Filed,
                };
            return dto;
        }

        static void Restore(EventBook book, EventBookDto dto)
        {
            if (book == null)
                return;
            book.Clear();
            if (dto == null)
                return;
            if (dto.pending != null && TryEnum(dto.pending.id, out CardId cardId) &&
                TryEnum(dto.pending.def, out EventId defId))
                book.Pending = new PendingCard
                {
                    Id = cardId,
                    Def = defId,
                    DealtDay = dto.pending.dealtDay,
                    ExpiresDay = dto.pending.expiresDay,
                    Speaker = dto.pending.speaker,
                    Hold = TryEnum(dto.pending.hold, out HoldReason hold) ? hold : HoldReason.None,
                    Path = TryEnum(dto.pending.path, out ConnectionPath path) ? path : ConnectionPath.Column,
                    Line = TryEnum(dto.pending.line, out ConnectionLine line) ? line : ConnectionLine.None,
                    Trade = TryEnum(dto.pending.trade, out Background trade) ? trade : Background.None,
                    ManId = dto.pending.manId,
                    CellmateId = dto.pending.cellmateId,
                    CrewId = dto.pending.crewId,
                    Door = dto.pending.door ?? "",
                };
            for (var i = 0; dto.pots != null && i < dto.pots.Length; i++)
                if (dto.pots[i] != null && TryEnum(dto.pots[i].id, out EventId id))
                    book.Pots[id] = dto.pots[i].pot;
            for (var i = 0; dto.fired != null && i < dto.fired.Length; i++)
                if (dto.fired[i] != null && TryEnum(dto.fired[i].id, out EventId id))
                    book.Fired[id] = dto.fired[i].day;
            for (var i = 0; dto.cooling != null && i < dto.cooling.Length; i++)
                if (dto.cooling[i] != null && TryEnum(dto.cooling[i].id, out EventId id))
                    book.Cooling[id] = dto.cooling[i].day;
            for (var i = 0; dto.wire != null && i < dto.wire.Length; i++)
                if (dto.wire[i] != null)
                    book.Wire.Add(new WireLine
                    {
                        Day = dto.wire[i].day, Text = dto.wire[i].text ?? "",
                        Public = dto.wire[i].isPublic, Filed = dto.wire[i].filed,
                    });
            book.CardsDealt = dto.cardsDealt;
            book.CardsAnswered = dto.cardsAnswered;
            book.CardsExpired = dto.cardsExpired;
            book.LastAnswer = dto.lastAnswer ?? "";
            book.Touch();
        }

        static void Restore(House house, HouseDto dto)
        {
            if (house == null || dto == null)
                return;
            house.Front = new TerritoryBusinessId(dto.front);
            house.NextThinkHour = dto.nextThinkHour;
            RosterSnapshot.Restore(house.Roster, dto.roster);
            Restore(house.Runner, dto.runner);
            Restore(house.Runner?.Connection, dto.connection);
            Restore(house.Runner?.Events, dto.events);
            house.Touch();
        }

        // ---------------------------------------------------------------- the runner

        static RunnerDto Snapshot(CampaignRunner runner, int gangId)
        {
            if (runner == null)
                return null;

            var dto = new RunnerDto
            {
                gangId = gangId,
                day = runner.Campaign.Day,
                heat = runner.Heat,
                seed = runner.Seed,
                nextJobId = runner.Book.PeekNextJobId,
                accounts = Snapshot(runner.Accounts),
                jobs = new JobDto[runner.Book.Jobs.Count],
                fallen = runner.Fallen,
                fallenOnDay = runner.FallenOnDay,
                ending = (int)runner.Ending,
                brokeNights = runner.BrokeNights,
                levies = new LevyDto[runner.Tribute.Levies.Count],
            };
            for (var i = 0; i < runner.Book.Jobs.Count; i++)
                dto.jobs[i] = Snapshot(runner.Book.Jobs[i]);
            for (var i = 0; i < runner.Tribute.Levies.Count; i++)
            {
                var levy = runner.Tribute.Levies[i];
                dto.levies[i] = new LevyDto
                {
                    gangId = levy.GangId,
                    amount = levy.Amount,
                    dueDay = levy.DueDay,
                    overdue = levy.Overdue,
                    pinnedAmount = levy.PinnedAmount,
                    pinnedUntilDay = levy.PinnedUntilDay,
                };
            }
            return dto;
        }

        static void Restore(CampaignRunner runner, RunnerDto dto)
        {
            if (runner == null || dto == null)
                return;

            runner.Campaign.Day = dto.day;
            runner.Heat = dto.heat;
            runner.Seed = dto.seed;
            Restore(runner.Accounts, dto.accounts);

            runner.Book.Jobs.Clear();
            for (var i = 0; dto.jobs != null && i < dto.jobs.Length; i++)
            {
                var job = Restore(dto.jobs[i]);
                if (job != null)
                    runner.Book.Jobs.Add(job);
            }
            runner.Book.RestoreNextJobId(dto.nextJobId);
            runner.RestoreEnding(dto.fallen, dto.fallenOnDay,
                (OutfitEnding)dto.ending, dto.brokeNights);

            if (dto.levies != null)
            {
                runner.Tribute.Levies.Clear();
                for (var i = 0; i < dto.levies.Length; i++)
                {
                    var row = dto.levies[i];
                    if (row == null)
                        continue;
                    runner.Tribute.Levies.Add(new Levy
                    {
                        GangId = row.gangId,
                        Amount = row.amount,
                        DueDay = row.dueDay,
                        Overdue = row.overdue,
                        PinnedAmount = row.pinnedAmount,
                        PinnedUntilDay = row.pinnedUntilDay,
                    });
                }
            }
        }

        // -------------------------------------------------------------- the accounts

        static AccountsDto Snapshot(Accounts accounts)
        {
            var dto = new AccountsDto
            {
                safe = accounts.Safe,
                riskyMoney = accounts.RiskyMoney,
                sheets = new DaySheetDto[accounts.Sheets.Count],
            };
            for (var i = 0; i < accounts.Sheets.Count; i++)
            {
                var sheet = accounts.Sheets[i];
                dto.sheets[i] = new DaySheetDto
                {
                    day = sheet.Day,
                    legalIncome = sheet.LegalIncome,
                    illegalIncome = sheet.IllegalIncome,
                    jobIncome = sheet.JobIncome,
                    salesIncome = sheet.SalesIncome,
                    bribes = sheet.Bribes,
                    purchases = sheet.Purchases,
                    otherCosts = sheet.OtherCosts,
                    wagesPaid = sheet.WagesPaid,
                    taxPaid = sheet.TaxPaid,
                    closed = sheet.Closed,
                    fromHouses = sheet.FromHouses,
                    toHouses = sheet.ToHouses,
                };
            }
            return dto;
        }

        static void Restore(Accounts accounts, AccountsDto dto)
        {
            if (accounts == null || dto == null)
                return;

            accounts.Safe = dto.safe;
            accounts.RiskyMoney = dto.riskyMoney;
            BalanceMath.Normalize(accounts);
            accounts.Sheets.Clear();
            for (var i = 0; dto.sheets != null && i < dto.sheets.Length; i++)
            {
                var sheet = dto.sheets[i];
                accounts.Sheets.Add(new DaySheet
                {
                    Day = sheet.day,
                    LegalIncome = sheet.legalIncome,
                    IllegalIncome = sheet.illegalIncome,
                    JobIncome = sheet.jobIncome,
                    SalesIncome = sheet.salesIncome,
                    Bribes = sheet.bribes,
                    Purchases = sheet.purchases,
                    OtherCosts = sheet.otherCosts,
                    WagesPaid = sheet.wagesPaid,
                    TaxPaid = sheet.taxPaid,
                    Closed = sheet.closed,
                    FromHouses = sheet.fromHouses,
                    ToHouses = sheet.toHouses,
                });
            }
        }

        // ------------------------------------------------------------------- the job

        static JobDto Snapshot(Job job)
        {
            var dto = new JobDto
            {
                id = job.Id,
                crewId = job.CrewId,
                type = (int)job.Type,
                gangId = job.GangId,
                blockTargets = new int[job.BlockTargets.Count],
                targetWorth = job.TargetWorth,
                targetCharacterId = job.TargetCharacterId,
                targetBlockId = job.TargetBlockId,
                targetX = job.TargetX,
                targetZ = job.TargetZ,
                targetLabel = job.TargetLabel,
                targetBusinessId = job.TargetBusinessId,
                men = job.Men,
                issuedDay = job.IssuedDay,
                stage = (int)job.Stage,
                travelHoursLeft = job.TravelHoursLeft,
                workHoursLeft = job.WorkHoursLeft,
                daysStood = job.DaysStood,
                bookDepth = job.BookDepth,
                streetOutcome = job.StreetOutcome.HasValue ? (int)job.StreetOutcome.Value : -1,
                proposalId = job.ProposalId,
            };
            for (var i = 0; i < job.BlockTargets.Count; i++)
                dto.blockTargets[i] = job.BlockTargets[i];
            return dto;
        }

        static Job Restore(JobDto dto)
        {
            if (dto == null || !TryEnum(dto.type, out OrderType type) ||
                !TryEnum(dto.stage, out JobStage stage))
                return null;

            OrderOutcome? streetOutcome = null;
            if (dto.streetOutcome >= 0)
            {
                if (!TryEnum(dto.streetOutcome, out OrderOutcome restoredOutcome))
                    return null;
                streetOutcome = restoredOutcome;
            }

            var job = new Job
            {
                Id = dto.id,
                CrewId = dto.crewId,
                Type = type,
                GangId = dto.gangId,
                TargetWorth = dto.targetWorth,
                TargetCharacterId = dto.targetCharacterId,
                TargetBlockId = dto.targetBlockId,
                TargetX = dto.targetX,
                TargetZ = dto.targetZ,
                TargetLabel = dto.targetLabel ?? "",
                TargetBusinessId = dto.targetBusinessId ?? "",
                Men = dto.men,
                IssuedDay = dto.issuedDay,
                Stage = stage,
                TravelHoursLeft = dto.travelHoursLeft,
                WorkHoursLeft = dto.workHoursLeft,
                DaysStood = dto.daysStood,
                BookDepth = dto.bookDepth,
                StreetOutcome = streetOutcome,
                ProposalId = dto.proposalId,
            };
            for (var i = 0; dto.blockTargets != null && i < dto.blockTargets.Length; i++)
                job.BlockTargets.Add(dto.blockTargets[i]);
            return job;
        }

        /// <summary>DTO integers are untrusted at the load boundary. In particular an
        /// unknown OrderType reaches exhaustive production switches later, far from
        /// the bad row. Reject that row here instead of planting a delayed exception in
        /// the running book.</summary>
        static bool TryEnum<T>(int raw, out T value) where T : struct
        {
            if (Enum.IsDefined(typeof(T), raw))
            {
                value = (T)Enum.ToObject(typeof(T), raw);
                return true;
            }
            value = default(T);
            return false;
        }

        // ------------------------------------------------------------- the relations

        public static RelationsDto Snapshot(HouseRelations relations)
        {
            if (relations == null)
                return null;

            var stances = new List<StanceDto>();
            var grievances = new List<GrievanceDto>();
            relations.Collect(stances, grievances);
            var agreements = new List<AgreementDto>();
            var killings = new List<KillingDto>();
            var cleared = new List<ClearedDto>();
            relations.CollectTable(agreements, killings, cleared);
            return new RelationsDto
            {
                stances = stances.ToArray(),
                grievances = grievances.ToArray(),
                agreements = agreements.ToArray(),
                killings = killings.ToArray(),
                cleared = cleared.ToArray(),
            };
        }

        public static void Restore(HouseRelations relations, RelationsDto dto)
        {
            if (relations == null || dto == null)
                return;
            relations.RestoreFrom(dto.stances, dto.grievances);
            relations.RestoreTable(dto.agreements, dto.killings, dto.cleared);
        }

        // ------------------------------------------------------------- the table

        public static DiplomacyDto Snapshot(HouseDiplomacy diplomacy)
        {
            if (diplomacy == null)
                return null;
            var rows = new List<ProposalDto>();
            var offs = new List<KeepOffDto>();
            var lines = new List<LineDto>();
            var pacts = new List<PactDto>();
            diplomacy.Collect(rows, offs, out var next);
            diplomacy.CollectLines(lines);
            diplomacy.CollectPacts(pacts);
            return new DiplomacyDto
            {
                proposals = rows.ToArray(),
                keepOffs = offs.ToArray(),
                nextId = next,
                lines = lines.ToArray(),
                pacts = pacts.ToArray(),
            };
        }

        /// <summary>A file from before the table, or one written with an empty book,
        /// reads as an empty book: a null block and a {} block both restore to none.
        /// </summary>
        public static void Restore(HouseDiplomacy diplomacy, DiplomacyDto dto)
        {
            if (diplomacy == null)
                return;
            diplomacy.RestoreFrom(dto?.proposals, dto?.keepOffs, dto?.nextId ?? 0);
            diplomacy.RestoreLines(dto?.lines);
            diplomacy.RestorePacts(dto?.pacts);
        }
    }
}
