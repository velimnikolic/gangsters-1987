using System;
using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;

namespace LivingCity.Property
{
    /// <summary>What one flat did overnight, for the sheet that has to apply it.</summary>
    public readonly struct FlatHeatDeposit
    {
        public FlatHeatDeposit(ApartmentBuildingId building, ApartmentUnitId unit, int heat)
        {
            Building = building;
            Unit = unit;
            Heat = heat;
        }

        public ApartmentBuildingId Building { get; }
        public ApartmentUnitId Unit { get; }

        /// <summary>Police attention this flat put on its block today.</summary>
        public int Heat { get; }
    }

    public readonly struct FlatRaid
    {
        public FlatRaid(ApartmentUnitId unit, int keeperId, int untilDay, int seized)
        {
            Unit = unit;
            KeeperId = keeperId;
            UntilDay = untilDay;
            Seized = seized;
        }

        public ApartmentUnitId Unit { get; }

        /// <summary>The man the precinct took, or -1 if the room was standing empty.</summary>
        public int KeeperId { get; }

        public int UntilDay { get; }
        public int Seized { get; }
    }

    /// <summary>The night's flats, in figures the finances page already knows how to
    /// print.</summary>
    public sealed class FlatDayReport
    {
        public int Rent;
        public int IllegalIncome;
        public int Skimmed;
        public int Fines;
        public int StaffWages;
        public int Open;
        public int Dark;

        public readonly List<FlatHeatDeposit> Heat = new List<FlatHeatDeposit>();
        public readonly List<FlatRaid> Raids = new List<FlatRaid>();

        public void Clear()
        {
            Rent = IllegalIncome = Skimmed = Fines = StaffWages = Open = Dark = 0;
            Heat.Clear();
            Raids.Clear();
        }
    }

    /// <summary>
    /// EVERY FLAT, EVERY MIDNIGHT (EPIC 27, FLAT-003/004).
    ///
    /// Pure: it reads the book and the roster, moves money through the outfit's own
    /// accounts, writes the incidents the paper prints, and hands back the two things it
    /// cannot do itself - the heat a block has to be given and the raids a scene has to
    /// carry out. The city, the police and the camera are all somebody else's business.
    ///
    /// Nothing here is silent (EPIC 13's law): a skim, a raid and a fine each leave a
    /// line with a man's name on it.
    /// </summary>
    public static class FlatDay
    {
        /// <summary>What the rent on one flat costs a day. $400 a month in the price
        /// authority (Docs/economy-prices.md §3), which is thirteen dollars a day.</summary>
        public const int RentPerDay = 13;

        /// <summary>
        /// What a role's heat-per-day is worth in the block's OWN pool.
        ///
        /// `PoliceAttention` decays on an eight-hour half-life against a cap of 100, so a
        /// day is three half-lives and a bare deposit of 4 peaks at about 4.6 out of 100 -
        /// invisible beside a gunshot's 4 and a killing's 30, which do not decay away
        /// before the next one lands. The daily lump is therefore scaled to what a room
        /// running every night actually holds. FLAT-009 balances the number; this is where
        /// it lives, once.
        /// </summary>
        public const int HeatPerDayScale = 12;

        /// <summary>A raided flat is sealed for a fortnight.</summary>
        public const int SealedDays = 14;

        /// <summary>How likely a well-run open room is to be raided on any one night,
        /// before its own heat and the keeper's care are counted, in tenths of a percent.</summary>
        public const int RaidBaseChance = 2;

        static readonly List<ApartmentRecord> ours = new List<ApartmentRecord>();

        /// <summary>
        /// Runs the night. <paramref name="accounts"/> is charged and paid; the report
        /// carries what the scene has to apply.
        /// </summary>
        public static void Tick(
            Roster roster, int gangId, int day, int citySeed,
            Accounts accounts, List<Incident> incidents, FlatDayReport report)
        {
            report?.Clear();
            if (report == null)
                return;

            Apartments.OwnedBy(gangId, ours);
            if (ours.Count == 0)
                return;

            for (var i = 0; i < ours.Count; i++)
            {
                var record = ours[i];
                var unit = record.Unit;

                // The rent is owed on a room whether or not anything happens in it. It is
                // what makes a dark flat a cost rather than a curiosity.
                report.Rent += RentPerDay;

                var keeper = roster?.Find(record.KeeperId);
                var standing = keeper != null && !keeper.Gone &&
                               keeper.Status == CharacterStatus.Active;
                var state = Apartments.StateOf(unit, gangId, day, standing);

                // A keeper who has left the books stops being the keeper: the book must
                // not hold a dead man's name against a room for the rest of the campaign.
                if (record.KeeperId >= 0 && (keeper == null || keeper.Gone))
                    Apartments.SetKeeper(unit, -1);

                if (state != UnitState.Open)
                {
                    report.Dark++;
                    continue;
                }

                report.Open++;
                var spec = UnitRoles.Of(record.Role);

                if (spec.Earn > 0)
                    report.IllegalIncome += NightsTake(record, spec, day, citySeed);

                // The hired hands are paid whether the night was good or bad. Ledger rows,
                // not men on the roster: nobody walks anywhere for this (FLAT-007).
                var hands = UnitRoles.StaffCeiling(record.Role) > 0
                    ? Math.Min(record.Staff, UnitRoles.StaffCeiling(record.Role))
                    : 0;
                report.StaffWages += hands * UnitRoles.StaffWage(record.Role);

                if (spec.Heat > 0)
                    report.Heat.Add(new FlatHeatDeposit(
                        unit.Building, unit, spec.Heat * HeatPerDayScale));

                Skim(record, keeper, day, citySeed, incidents, report);
                Doctor(record, roster, day);
                Raid(record, keeper, day, citySeed, incidents, report);
            }

            Settle(accounts, report);
        }

        // ------------------------------------------------------------------ the money

        /// <summary>
        /// A night at the table, or a night in the rooms. Seed-varied off the flat's own
        /// stream and the day, so the same campaign has the same run of nights - and a bad
        /// night is a real one: the house can lose a third of what a good night takes.
        /// </summary>
        static int NightsTake(ApartmentRecord record, UnitRoleSpec spec, int day, int citySeed)
        {
            var rng = Stream(record.Unit, citySeed, day, 1);
            var swing = 0.65f + (float)rng.NextDouble() * 0.7f;
            var take = (int)(spec.Earn * swing);

            // A BROTHEL TAKES WHAT ITS GIRLS TAKE. An empty room is a room with a keeper
            // standing in it and nothing happening, which is the point of hiring anybody.
            if (record.Role == UnitRole.Brothel)
            {
                var girls = Math.Min(record.Staff, UnitRoles.BrothelGirls);
                return girls <= 0 ? 0 : (int)(UnitRoles.BrothelTakePerGirl * girls * swing);
            }

            // A room with no bank behind it simply takes its cut of the night.
            if (!spec.NeedsBank)
                return take;

            // THE TABLE CAN LOSE. One night in four the players are ahead, and it comes
            // out of the bank the house put behind the table - which is what makes a card
            // room a risk rather than an annuity, and what empties it if nobody watches.
            if (rng.Next(4) != 0)
                return take;

            var lost = Math.Min(record.Bank, (int)(spec.Earn * swing * 0.6f));
            if (lost > 0)
                Apartments.SetBank(record.Unit, record.Bank - lost);
            return 0;
        }

        /// <summary>
        /// What the man minding the money helps himself to. The GreedLadder already says
        /// who does that and how badly; this is the same rule applied to what he SITS ON
        /// rather than to what he is paid.
        /// </summary>
        static void Skim(ApartmentRecord record, Character keeper, int day, int citySeed,
            List<Incident> incidents, FlatDayReport report)
        {
            if (keeper == null)
                return;
            if (record.Role != UnitRole.CashStash && record.Role != UnitRole.CardRoom)
                return;

            // A loyal man does not, however greedy he is; a disloyal one is a matter of
            // when. Both readings are the man's own numbers, nothing invented here.
            var temptation = keeper.Greed - keeper.Loyalty;
            if (temptation <= 0)
                return;

            var rng = Stream(record.Unit, citySeed, day, 2);
            if (rng.Next(100) >= temptation)
                return;

            var took = 40 + rng.Next(temptation * 4);
            report.Skimmed += took;
            incidents?.Add(new Incident(keeper.Id, keeper.FullName,
                IncidentKind.SkimmedTheStash, day, record.Unit.Door, 0,
                IncidentText.Line(IncidentKind.SkimmedTheStash, keeper.FullName,
                    record.Unit.Door)));
        }

        /// <summary>The infirmary: a man in a bed gets a day of it back, and no police
        /// report is written about the wound. One day per open infirmary per night, so a
        /// second surgery is a second day and not a miracle.</summary>
        static void Doctor(ApartmentRecord record, Roster roster, int day)
        {
            if (record.Role != UnitRole.Infirmary || roster == null)
                return;

            // A room with a bed in it and nobody who knows what to do with it is a room.
            // The doctor is hired like the girls are, and he is what shortens the bed.
            if (record.Staff <= 0)
                return;

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Status != CharacterStatus.Hospitalized)
                    continue;
                if (member.BackOnDay <= day + 1)
                    continue;
                member.BackOnDay--;
                return;
            }
        }

        /// <summary>
        /// The precinct at the door. The chance is the flat's OWN heat against the
        /// keeper's care: a card room run by a careless man is raided in weeks, and a
        /// safehouse with a quiet man in it is not raided at all.
        /// </summary>
        static void Raid(ApartmentRecord record, Character keeper, int day, int citySeed,
            List<Incident> incidents, FlatDayReport report)
        {
            var spec = UnitRoles.Of(record.Role);
            if (spec.Heat <= 0)
                return;

            var care = keeper != null
                ? keeper.GetHalfSteps(CharacterAttribute.Stealth)
                : AttributeScale.MinHalfSteps;
            // Tenths of a percent: base, plus the heat the room makes, less what a careful
            // man keeps off the street.
            var chance = RaidBaseChance + spec.Heat * 6 - care;
            if (chance <= 0)
                return;

            var rng = Stream(record.Unit, citySeed, day, 3);
            if (rng.Next(1000) >= chance)
                return;

            var seized = record.Bank;
            Apartments.Raid(record.Unit, day + SealedDays);
            report.Fines += EconomyPrices.Raid;
            report.Raids.Add(new FlatRaid(record.Unit,
                keeper != null ? keeper.Id : -1, day + SealedDays, seized));

            if (keeper != null)
                incidents?.Add(new Incident(keeper.Id, keeper.FullName,
                    IncidentKind.FlatRaided, day, record.Unit.Door, spec.Heat,
                    IncidentText.Line(IncidentKind.FlatRaided, keeper.FullName,
                        record.Unit.Door)));
        }

        /// <summary>The night's figures against the books, in the lines the finances page
        /// already prints: rent and fines are other costs, a night's take is illegal
        /// income, and what a man stole is simply gone.</summary>
        static void Settle(Accounts accounts, FlatDayReport report)
        {
            if (accounts == null)
                return;

            var owed = report.Rent + report.Fines + report.StaffWages;
            if (owed > 0)
            {
                BalanceMath.Pay(accounts, owed, out _);
                if (accounts.Current != null)
                    accounts.Current.OtherCosts += owed;
            }

            var kept = report.IllegalIncome - report.Skimmed;
            if (kept > 0)
            {
                BalanceMath.Receive(accounts, kept, MoneyKind.Dirty);
                if (accounts.Current != null)
                    accounts.Current.IllegalIncome += kept;
            }
        }

        /// <summary>One flat's own stream for one night. MixSeed-shaped: the unit, the
        /// city, the day and which roll it is, so two rooms never move together and the
        /// same campaign replays the same nights.</summary>
        static Random Stream(ApartmentUnitId unit, int citySeed, int day, int roll)
        {
            unchecked
            {
                var hash = (int)2166136261;
                var value = unit.ToString();
                for (var i = 0; i < value.Length; i++)
                    hash = (hash ^ value[i]) * 16777619;
                var mixed = hash ^ (citySeed * 31) ^ (day * 7919) ^ (roll * 104729);
                return new Random(mixed & int.MaxValue);
            }
        }
    }
}
