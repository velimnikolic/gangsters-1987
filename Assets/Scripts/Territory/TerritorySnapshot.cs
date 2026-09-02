using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>Where one door stands with one family, and how it got there.</summary>
    [Serializable]
    public sealed class ProtectionRowDto
    {
        public string businessId;
        public int gangId;
        public int state;
        public double stateSince;
        public double lastInteraction;
        public double refusedAt;
        public int demands;
        public int threats;
        public int escalations;
    }

    [Serializable]
    public sealed class DuesRowDto
    {
        public string businessId;
        public int gangId;
        public int weeklyRate;
        public int owedSevenths;
        public int lastCollectedDay;
        public int missedInARow;
    }

    [Serializable]
    public sealed class RoundStopDto
    {
        public string businessId;
        public float x;
        public float z;
    }

    [Serializable]
    public sealed class RoundDto
    {
        public int house;
        public int crewId;
        public int collectorId;
        public string blockId;
        public int kind;
        public int stopIndex;
        public int carried;
        public int missed;
        public int stage;
        public double openedAt;
        public double lastMoveAt;
        public bool inTheDoor;
        public RoundStopDto[] stops;
    }

    [Serializable]
    public sealed class TerritoryDto
    {
        public ProtectionRowDto[] protection;
        public DuesRowDto[] dues;
        public RoundDto[] rounds;
    }

    /// <summary>
    /// THE CITY'S OWN BOOKS, WRITTEN DOWN AND READ BACK: who pays whom, what they owe,
    /// and the rounds that were out when the game stopped.
    ///
    /// NOT here, and by design: fear, presence and derived control. All three are
    /// DECAYING READINGS OF BODIES - what a street has lately felt, who has lately stood
    /// on it - and the bodies are re-stood on load. Saving them would freeze a
    /// measurement of a city that no longer exists; the streets re-learn what they feel
    /// within a day, which is what they are for.
    /// </summary>
    public static class TerritorySnapshot
    {
        public static TerritoryDto Snapshot(
            TerritoryRacketLedger racket, TerritoryDuesLedger dues,
            TerritoryRoundLedger rounds)
        {
            var dto = new TerritoryDto();

            var protection = new List<ProtectionRowDto>();
            var owed = new List<DuesRowDto>();
            racket?.Collect(protection);
            dues?.Collect(owed);
            dto.protection = protection.ToArray();
            dto.dues = owed.ToArray();

            var out_ = new List<RoundDto>();
            var live = rounds?.Rounds;
            for (var i = 0; live != null && i < live.Count; i++)
                out_.Add(Snapshot(live[i]));
            dto.rounds = out_.ToArray();
            return dto;
        }

        public static void Restore(
            TerritoryRacketLedger racket, TerritoryDuesLedger dues,
            TerritoryRoundLedger rounds, TerritoryDto dto)
        {
            if (dto == null)
                return;
            racket?.RestoreFrom(dto.protection);
            dues?.RestoreFrom(dto.dues);
            rounds?.RestoreFrom(Restore(dto.rounds));
        }

        static RoundDto Snapshot(TerritoryRound round)
        {
            var dto = new RoundDto
            {
                house = round.House.Value,
                crewId = round.CrewId,
                collectorId = round.CollectorId,
                blockId = round.BlockId.Value,
                kind = (int)round.Kind,
                stopIndex = round.StopIndex,
                carried = round.Carried,
                missed = round.Missed,
                stage = (int)round.Stage,
                openedAt = round.OpenedAt,
                lastMoveAt = round.LastMoveAt,
                inTheDoor = round.InTheDoor,
                stops = new RoundStopDto[round.Stops.Count],
            };
            for (var i = 0; i < round.Stops.Count; i++)
                dto.stops[i] = new RoundStopDto
                {
                    businessId = round.Stops[i].BusinessId.Value,
                    x = round.Stops[i].Doorstep.X,
                    z = round.Stops[i].Doorstep.Z,
                };
            return dto;
        }

        static List<TerritoryRound> Restore(RoundDto[] rows)
        {
            var rounds = new List<TerritoryRound>();
            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                var dto = rows[i];
                var round = new TerritoryRound
                {
                    House = new TerritoryGangId(dto.house),
                    CrewId = dto.crewId,
                    CollectorId = dto.collectorId,
                    BlockId = new TerritoryBlockId(dto.blockId),
                    Kind = (TerritoryRoundKind)dto.kind,
                    StopIndex = dto.stopIndex,
                    Carried = dto.carried,
                    Missed = dto.missed,
                    Stage = (TerritoryRoundStage)dto.stage,
                    OpenedAt = dto.openedAt,
                    LastMoveAt = dto.lastMoveAt,
                    InTheDoor = dto.inTheDoor,
                };
                for (var s = 0; dto.stops != null && s < dto.stops.Length; s++)
                    round.Stops.Add(new TerritoryRoundStop(
                        new TerritoryBusinessId(dto.stops[s].businessId),
                        new TerritoryPoint(dto.stops[s].x, dto.stops[s].z)));
                rounds.Add(round);
            }
            return rounds;
        }
    }
}
