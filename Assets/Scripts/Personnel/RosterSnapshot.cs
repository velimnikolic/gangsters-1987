using System;
using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Personnel
{
    /// <summary>
    /// ONE MAN ON PAPER. Arrays only and no properties: JsonUtility serializes public
    /// fields of a [Serializable] class and nothing else, and a save that quietly loses
    /// a man's practice because it was behind a property is worse than no save at all.
    /// </summary>
    [Serializable]
    public sealed class CharacterDto
    {
        public int id;
        public string firstName;
        public string surname;
        public int rank;
        public int specialty;
        public int duty;
        public int status;
        public string look;
        public int loyalty;
        public int courage;
        public int greed;
        public int ambition;
        public int discipline;
        public int temper;
        public int wantedLevel;
        public int hidingSince;
        public bool outOfTown;
        public int birthYear;
        public int birthDayOfYear;
        public int wageAsked;
        public int underpaidSince;
        public int rankSince;
        public bool skimming;
        public int wageDemand;
        public int backOnDay;
        public string conditionNote;
        public int flagsAnnounced;

        /// <summary>The three attribute arrays, in CharacterAttribute order.</summary>
        public int[] halfSteps;
        public int[] practice;
        public int[] potential;
    }

    [Serializable]
    public sealed class CrewDto
    {
        public int id;
        public int lieutenantId;
        public int[] hoodIds;
        public int policy;
    }

    [Serializable]
    public sealed class EquipmentDto
    {
        public int id;
        public int kind;
        public string displayName;
        public int ownerId;
        public int holderId;
        public int pinnedTo;
        public int value;
    }

    [Serializable]
    public sealed class BlockResponsibilityDto
    {
        public string blockId;
        public int leaderId;
    }

    [Serializable]
    public sealed class RosterDto
    {
        public int gangId;
        public int seed;
        public int year;
        public int day;
        public int frontId;
        public int nextCharacterId;
        public int nextCrewId;
        public int nextEquipmentId;

        public int bossId;
        public int[] bossHoodIds;
        public BlockResponsibilityDto[] responsibilities;

        public CharacterDto[] members;
        public CrewDto[] crews;
        public EquipmentDto[] equipment;
    }

    /// <summary>
    /// THE ROSTER, WRITTEN DOWN AND READ BACK.
    ///
    /// Nothing here reflects over a private field: every value a man carries is copied by
    /// name, which is what makes a save survive a refactor loudly (a compile error)
    /// rather than quietly (a lost attribute).
    ///
    /// A restored roster is the SAME OBJECT the game already holds, refilled - not a new
    /// one - because half the city holds references to it.
    /// </summary>
    public static class RosterSnapshot
    {
        public static RosterDto Snapshot(Roster roster)
        {
            if (roster == null)
                return null;

            var dto = new RosterDto
            {
                gangId = roster.GangId,
                seed = roster.Seed,
                year = roster.Year,
                day = roster.Day,
                frontId = roster.FrontId,
                nextCharacterId = roster.PeekNextCharacterId,
                nextCrewId = roster.PeekNextCrewId,
                nextEquipmentId = roster.PeekNextEquipmentId,
                bossId = roster.Organization.BossId,
                members = new CharacterDto[roster.Members.Count],
                crews = new CrewDto[roster.Crews.Count],
                equipment = new EquipmentDto[roster.Equipment.Count],
            };

            var bossHoods = roster.Organization.BossHoodIds;
            dto.bossHoodIds = new int[bossHoods.Count];
            for (var i = 0; i < bossHoods.Count; i++)
                dto.bossHoodIds[i] = bossHoods[i];

            var paper = roster.Organization.BlockResponsibilities;
            dto.responsibilities = new BlockResponsibilityDto[paper.Count];
            for (var i = 0; i < paper.Count; i++)
                dto.responsibilities[i] = new BlockResponsibilityDto
                {
                    blockId = paper[i].BlockId.Value,
                    leaderId = paper[i].LeaderId,
                };

            for (var i = 0; i < roster.Members.Count; i++)
                dto.members[i] = Snapshot(roster.Members[i]);
            for (var i = 0; i < roster.Crews.Count; i++)
                dto.crews[i] = Snapshot(roster.Crews[i]);
            for (var i = 0; i < roster.Equipment.Count; i++)
                dto.equipment[i] = Snapshot(roster.Equipment[i]);
            return dto;
        }

        public static void Restore(Roster roster, RosterDto dto)
        {
            if (roster == null || dto == null)
                return;

            roster.Members.Clear();
            roster.Crews.Clear();
            roster.Equipment.Clear();
            roster.Organization.BossHoodIds.Clear();
            roster.Organization.BlockResponsibilities.Clear();

            roster.RestoreIdentity(
                dto.gangId, dto.seed, dto.year, dto.day, dto.frontId,
                dto.nextCharacterId, dto.nextCrewId, dto.nextEquipmentId);

            roster.Organization.BossId = dto.bossId;
            for (var i = 0; dto.bossHoodIds != null && i < dto.bossHoodIds.Length; i++)
                roster.Organization.BossHoodIds.Add(dto.bossHoodIds[i]);
            for (var i = 0; dto.responsibilities != null && i < dto.responsibilities.Length; i++)
                roster.Organization.BlockResponsibilities.Add(
                    new OrganizationBlockResponsibility(
                        new TerritoryBlockId(dto.responsibilities[i].blockId),
                        dto.responsibilities[i].leaderId));

            for (var i = 0; dto.members != null && i < dto.members.Length; i++)
                roster.Members.Add(Restore(dto.members[i]));
            for (var i = 0; dto.crews != null && i < dto.crews.Length; i++)
                roster.Crews.Add(Restore(dto.crews[i]));
            for (var i = 0; dto.equipment != null && i < dto.equipment.Length; i++)
                roster.Equipment.Add(Restore(dto.equipment[i]));
        }

        // ------------------------------------------------------------------- the man

        static CharacterDto Snapshot(Character man)
        {
            var dto = new CharacterDto
            {
                id = man.Id,
                firstName = man.FirstName,
                surname = man.Surname,
                rank = (int)man.Rank,
                specialty = (int)man.Specialty,
                duty = (int)man.Duty,
                status = (int)man.Status,
                look = man.Look,
                loyalty = man.Loyalty,
                courage = man.Courage,
                greed = man.Greed,
                ambition = man.Ambition,
                discipline = man.Discipline,
                temper = man.Temper,
                wantedLevel = man.WantedLevel,
                hidingSince = man.HidingSince,
                outOfTown = man.OutOfTown,
                birthYear = man.BirthYear,
                birthDayOfYear = man.BirthDayOfYear,
                wageAsked = man.WageAsked,
                underpaidSince = man.UnderpaidSince,
                rankSince = man.RankSince,
                skimming = man.Skimming,
                wageDemand = man.WageDemand,
                backOnDay = man.BackOnDay,
                conditionNote = man.ConditionNote,
                flagsAnnounced = (int)man.FlagsAnnounced,
                halfSteps = new int[AttributeScale.Count],
                practice = new int[AttributeScale.Count],
                potential = new int[AttributeScale.Count],
            };

            for (var i = 0; i < AttributeScale.Count; i++)
            {
                var attribute = (CharacterAttribute)i;
                dto.halfSteps[i] = man.GetHalfSteps(attribute);
                dto.practice[i] = man.GetPractice(attribute);
                dto.potential[i] = man.PotentialValue(attribute);
            }
            return dto;
        }

        static Character Restore(CharacterDto dto)
        {
            var man = new Character
            {
                Id = dto.id,
                FirstName = dto.firstName ?? "",
                Surname = dto.surname ?? "",
                Rank = (Rank)dto.rank,
                Specialty = (Specialty)dto.specialty,
                Duty = (Duty)dto.duty,
                Status = (CharacterStatus)dto.status,
                Look = dto.look ?? "",
                Loyalty = dto.loyalty,
                Courage = dto.courage,
                Greed = dto.greed,
                Ambition = dto.ambition,
                Discipline = dto.discipline,
                Temper = dto.temper,
                WantedLevel = dto.wantedLevel,
                HidingSince = dto.hidingSince,
                OutOfTown = dto.outOfTown,
                BirthYear = dto.birthYear,
                BirthDayOfYear = dto.birthDayOfYear,
                WageAsked = dto.wageAsked,
                UnderpaidSince = dto.underpaidSince,
                RankSince = dto.rankSince,
                Skimming = dto.skimming,
                WageDemand = dto.wageDemand,
                BackOnDay = dto.backOnDay,
                ConditionNote = dto.conditionNote ?? "",
                FlagsAnnounced = (ManFlag)dto.flagsAnnounced,
            };

            for (var i = 0; i < AttributeScale.Count; i++)
            {
                var attribute = (CharacterAttribute)i;
                if (dto.halfSteps != null && i < dto.halfSteps.Length)
                    man.SetHalfSteps(attribute, dto.halfSteps[i]);
                if (dto.potential != null && i < dto.potential.Length)
                    man.SetPotential(attribute, dto.potential[i]);
                if (dto.practice != null && i < dto.practice.Length)
                    man.SetPractice(attribute, dto.practice[i]);
            }
            return man;
        }

        // ------------------------------------------------------------------ the rest

        static CrewDto Snapshot(Crew crew)
        {
            var dto = new CrewDto
            {
                id = crew.Id,
                lieutenantId = crew.LieutenantId,
                policy = (int)crew.Policy,
                hoodIds = new int[crew.HoodIds.Count],
            };
            for (var i = 0; i < crew.HoodIds.Count; i++)
                dto.hoodIds[i] = crew.HoodIds[i];
            return dto;
        }

        static Crew Restore(CrewDto dto)
        {
            var crew = new Crew
            {
                Id = dto.id,
                LieutenantId = dto.lieutenantId,
                Policy = (CrewPolicy)dto.policy,
            };
            for (var i = 0; dto.hoodIds != null && i < dto.hoodIds.Length; i++)
                crew.HoodIds.Add(dto.hoodIds[i]);
            return crew;
        }

        static EquipmentDto Snapshot(RosterEquipment item) =>
            new EquipmentDto
            {
                id = item.Id,
                kind = (int)item.Kind,
                displayName = item.DisplayName,
                ownerId = item.OwnerId,
                holderId = item.HolderId,
                pinnedTo = item.PinnedTo,
                value = item.Value,
            };

        static RosterEquipment Restore(EquipmentDto dto) =>
            new RosterEquipment
            {
                Id = dto.id,
                Kind = (EquipmentKind)dto.kind,
                DisplayName = dto.displayName ?? "",
                OwnerId = dto.ownerId,
                HolderId = dto.holderId,
                PinnedTo = dto.pinnedTo,
                Value = dto.value,
            };
    }
}
