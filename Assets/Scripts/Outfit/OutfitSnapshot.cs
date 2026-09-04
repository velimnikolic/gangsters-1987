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
    }

    [Serializable]
    public sealed class StanceDto
    {
        public int a;
        public int b;
        public int stance;
        public bool pending;
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
    }

    [Serializable]
    public sealed class HouseDto
    {
        public int gangId;
        public string front;
        public double nextThinkHour;
        public RosterDto roster;
        public RunnerDto runner;
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

    [Serializable]
    public sealed class UnderworldDto
    {
        public int citySeed;
        public HouseDto[] houses;
        public RelationsDto relations;
        public FlatDto[] flats;
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
                houses = new HouseDto[underworld.Count],
                relations = Snapshot(underworld.Relations),
                flats = SnapshotFlats(),
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
            RestoreFlats(dto.flats);
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
            };
        }

        static void Restore(House house, HouseDto dto)
        {
            if (house == null || dto == null)
                return;
            house.Front = new TerritoryBusinessId(dto.front);
            house.NextThinkHour = dto.nextThinkHour;
            RosterSnapshot.Restore(house.Roster, dto.roster);
            Restore(house.Runner, dto.runner);
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
            };
            for (var i = 0; i < runner.Book.Jobs.Count; i++)
                dto.jobs[i] = Snapshot(runner.Book.Jobs[i]);
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
                runner.Book.Jobs.Add(Restore(dto.jobs[i]));
            runner.Book.RestoreNextJobId(dto.nextJobId);
            runner.RestoreEnding(dto.fallen, dto.fallenOnDay,
                (OutfitEnding)dto.ending, dto.brokeNights);
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
            };
            for (var i = 0; i < job.BlockTargets.Count; i++)
                dto.blockTargets[i] = job.BlockTargets[i];
            return dto;
        }

        static Job Restore(JobDto dto)
        {
            var job = new Job
            {
                Id = dto.id,
                CrewId = dto.crewId,
                Type = (OrderType)dto.type,
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
                Stage = (JobStage)dto.stage,
                TravelHoursLeft = dto.travelHoursLeft,
                WorkHoursLeft = dto.workHoursLeft,
                DaysStood = dto.daysStood,
                BookDepth = dto.bookDepth,
                StreetOutcome = dto.streetOutcome < 0
                    ? (OrderOutcome?)null
                    : (OrderOutcome)dto.streetOutcome,
            };
            for (var i = 0; dto.blockTargets != null && i < dto.blockTargets.Length; i++)
                job.BlockTargets.Add(dto.blockTargets[i]);
            return job;
        }

        // ------------------------------------------------------------- the relations

        public static RelationsDto Snapshot(HouseRelations relations)
        {
            if (relations == null)
                return null;

            var stances = new List<StanceDto>();
            var grievances = new List<GrievanceDto>();
            relations.Collect(stances, grievances);
            return new RelationsDto
            {
                stances = stances.ToArray(),
                grievances = grievances.ToArray(),
            };
        }

        public static void Restore(HouseRelations relations, RelationsDto dto)
        {
            if (relations == null || dto == null)
                return;
            relations.RestoreFrom(dto.stances, dto.grievances);
        }
    }
}
