using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// EPIC 25 / RIVAL-008 — the houses the city never stood up.
    ///
    /// Every family is simulated always; how many have bodies on the street is a
    /// performance decision. A family with no bodies is not a family that does nothing:
    /// its orders go through the same ledgers, its men hold the same ground, its rounds
    /// walk on <see cref="TerritoryPaperClock"/> and its fights resolve by the same
    /// paper roll a street-absent job has always used.
    ///
    /// ONE RULE, TWO CLOCKS. Nothing here computes anything the street does not - it
    /// calls the same <see cref="ResolveDemand"/>, the same racket, the same presence
    /// weights. The only difference is that nobody watches.
    /// </summary>
    public sealed partial class TerritoryRuntime
    {
        /// <summary>Which paper crew is standing on which block, and for which house.
        /// A posting is what OPERATE IN THIS BLOCK means when there is nobody to march.
        /// </summary>
        readonly Dictionary<int, (TerritoryGangId House, TerritoryBlockId Block)> postings =
            new Dictionary<int, (TerritoryGangId, TerritoryBlockId)>();

        readonly List<TerritoryRoundStop> paperStops = new List<TerritoryRoundStop>();
        readonly List<Character> paperCollectors = new List<Character>();

        /// <summary>How many houses are running with no bodies at all - the figure the
        /// measurement in RIVAL-008 is read against.</summary>
        public int PaperHouses { get; private set; }

        /// <summary>Whether this family has anybody on the street. A house with no
        /// bodies runs on paper; nothing else about it changes.</summary>
        public bool Stands(TerritoryGangId house)
        {
            if (crews == null || !house.IsValid)
                return false;
            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit != null && !unit.Wiped && !unit.IsPolice &&
                    unit.Faction == house.Value)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A house's order, worked with nobody standing anywhere. The same ledgers, the
        /// same rules, the same slips on the wire - the men simply are not rendered.
        /// </summary>
        string PaperOrder(House house, TerritoryGangId mine, HouseIntent intent)
        {
            switch (intent.Order)
            {
                case HouseOrder.OperateInBlock:
                    if (!intent.BlockId.IsValid)
                        return "no such street";
                    postings[intent.CrewId] = (mine, intent.BlockId);
                    return "";

                case HouseOrder.ApproachBusiness:
                    return PaperDoor(mine, intent.BusinessId, intent.FollowUp);

                case HouseOrder.LeanOnHoldouts:
                    return PaperWalk(mine, intent.BlockId, TerritoryRoundKind.Lean);

                case HouseOrder.ShakeDownBlock:
                    return PaperWalk(mine, intent.BlockId, TerritoryRoundKind.ShakeDown);

                case HouseOrder.CollectDues:
                    return PaperRound(house, mine, intent);
            }
            return "no such order";
        }

        /// <summary>One door, asked or leant on. The street's own resolutions, called
        /// without a body in front of the counter.</summary>
        string PaperDoor(
            TerritoryGangId mine, TerritoryBusinessId businessId,
            TerritoryRacketIntent followUp)
        {
            if (!IsRacketable(businessId))
                return "there is nothing behind that door";

            racketChanges.Clear();
            racket.Approach(businessId, mine, lastGameHour, racketChanges, announce: false);

            // NOBODY IS AT THIS DOOR TO REPORT. The telephone is a thing a shopkeeper
            // does about the men in front of him; a house working on paper has no men
            // anywhere, so an officer sent here would arrive at a door with nothing
            // standing at it. A city deals only the families it can stand
            // (RoadDemoBuilder.HousesInThisCity), so this path is the wiped-out and the
            // bench alone - and neither of them rings.
            if (followUp == TerritoryRacketIntent.Demand)
                ResolveDemand(mine, businessId, false, out _, out _);
            else if (followUp == TerritoryRacketIntent.Threaten)
                ResolveThreat(mine, businessId, default, false, out _, out _);

            if (geography.TryGetBusinessBlock(businessId, out var blockId))
                PublishRacket(blockId);
            return "";
        }

        /// <summary>A whole block walked - the ask, or the leaning. The same doors the
        /// physical walk would choose, by the same pure rule.</summary>
        string PaperWalk(
            TerritoryGangId mine, TerritoryBlockId blockId, TerritoryRoundKind kind)
        {
            if (!blockId.IsValid)
                return "no such street";

            var here = geography.BusinessesOf(blockId);
            var asked = 0;
            for (var i = 0; i < here.Count; i++)
            {
                var businessId = here[i].BusinessId;
                if (!IsRacketable(businessId) ||
                    !RacketCanAccrueAt(businessId, lastGameHour))
                    continue;

                var state = racket.StateOf(businessId, mine);
                var wanted = kind == TerritoryRoundKind.Lean
                    ? TerritoryShakedown.IsHoldout(state, HeldByDeed(businessId, mine))
                    : TerritoryShakedown.WorthAsking(state, HeldByDeed(businessId, mine));
                if (!wanted)
                    continue;

                PaperDoor(mine, businessId,
                    kind == TerritoryRoundKind.Lean
                        ? TerritoryRacketIntent.Threaten
                        : TerritoryRacketIntent.Demand);
                asked++;
            }
            return asked > 0 ? "" : "every door there has answered";
        }

        /// <summary>
        /// A round with no bodies: the same ledger opens it, the paper clock walks it,
        /// and the take reaches the same safe through the same OnRoundEnded.
        /// </summary>
        string PaperRound(House house, TerritoryGangId mine, HouseIntent intent)
        {
            if (roundLedger == null || paperClock == null)
                return "the racket is not running in this scene";
            if (roundLedger.RoundRunning(intent.CrewId))
                return "that crew is already out";

            paperStops.Clear();
            var here = geography.BusinessesOf(intent.BlockId);
            for (var i = 0; i < here.Count; i++)
            {
                var businessId = here[i].BusinessId;
                if (racket.StateOf(businessId, mine) != TerritoryProtectionState.Compliant)
                    continue;
                if (!RacketCanAccrueAt(businessId, lastGameHour))
                    continue;
                if (dues.OwedOf(businessId, mine) <= 0)
                    continue;
                if (!geography.TryGetDoorstep(businessId, out var doorstep))
                    continue;
                paperStops.Add(new TerritoryRoundStop(businessId, doorstep));
            }

            if (paperStops.Count == 0)
                return "nothing on that block owes them anything yet";

            RosterOps.CollectorsOf(house.Roster, intent.CrewId, paperCollectors);
            var round = roundLedger.Open(
                mine, intent.CrewId, paperCollectors.Count > 0 ? paperCollectors[0].Id : -1,
                intent.BlockId, TerritoryRoundKind.Collect, paperStops, lastGameHour);
            if (round == null)
                return "nothing on that block owes them anything yet";

            // Home is the family's own front. A house whose front the city cannot place
            // banks where it stands, exactly as a bench scene does.
            var hasHome = geography.TryGetDoorstep(house.Front, out var home);
            var crew = house.Roster.FindCrew(intent.CrewId);
            paperClock.Send(
                round, paperStops[0].Doorstep, home, hasHome,
                LivingCity.Outfit.CrewKit.HasVehicle(house.Roster, crew),
                crew != null ? BestDriving(house.Roster, crew) : 0,
                LivingCity.Outfit.CrewKit.MachineTopOf(house.Roster, crew), lastGameHour);
            return "";
        }

        static int BestDriving(Roster roster, Crew crew) =>
            LivingCity.Outfit.CrewKit.BestAt(roster, crew, CharacterAttribute.Driving);

        /// <summary>
        /// THE GROUND A PAPER HOUSE HOLDS. One synthetic observation per man of a posted
        /// crew, at the activity a man standing on a corner has - the SAME weights the
        /// street's own bodies are counted with, because there is one presence rule and
        /// this is not a second one.
        /// </summary>
        void SamplePaperHouses(double gameHour)
        {
            PaperHouses = 0;
            var underworld = Underworld.Current;
            if (underworld == null || presence == null || postings.Count == 0)
                return;

            foreach (var posting in postings)
            {
                var house = underworld.Of(posting.Value.House.Value);
                if (house == null || house.Finished || house.Roster == null)
                    continue;
                if (Stands(posting.Value.House))
                    continue;

                var crew = house.Roster.FindCrew(posting.Key);
                if (crew == null)
                    continue;

                var blockId = posting.Value.Block;
                var ground = fear == null ? 1f : fear.PresenceScale(blockId, gameHour);
                var name = LivingCity.Gangs.GangCatalog.Names[house.GangId];

                Contribute(house, crew.LieutenantId, TerritoryRank.Lieutenant, blockId,
                    ground, name, crew.Id);
                for (var i = 0; i < crew.HoodIds.Count; i++)
                    Contribute(house, crew.HoodIds[i], TerritoryRank.Hood, blockId,
                        ground, name, crew.Id);
            }

            for (var g = 0; g < underworld.Count; g++)
            {
                var house = underworld.Of(g);
                if (house != null && !house.Finished &&
                    !Stands(new TerritoryGangId(house.GangId)))
                    PaperHouses++;
            }
        }

        void Contribute(House house, int characterId, TerritoryRank rank,
            TerritoryBlockId blockId, float ground, string gangName, int crewId)
        {
            var man = house.Roster.Find(characterId);
            if (man == null || man.Gone || man.Status != CharacterStatus.Active)
                return;

            var observation = new TerritoryActorObservation(
                new TerritoryCharacterId(characterId),
                new TerritoryGangId(house.GangId),
                TerritoryCommandNodeId.Crew(crewId),
                man.FullName, gangName, rank == TerritoryRank.Lieutenant, rank,
                TerritoryActorActivity.Stationed);

            presence.Contribute(
                blockId, observation,
                ground * Command.PresenceFactorFor(house.Roster, characterId));
        }

        /// <summary>
        /// The canonical block behind a plan index. Orders carry the legacy number
        /// because the job card is older than the geography; every reader that needs the
        /// real block asks here rather than keeping its own table.
        /// </summary>
        public TerritoryBlockId BlockOfLegacy(int legacyBlockId)
        {
            if (geography == null || legacyBlockId < 0)
                return default;
            var blocks = geography.BlockIds;
            for (var i = 0; i < blocks.Count; i++)
                if (geography.TryGetBlock(blocks[i], out var definition) &&
                    definition.LegacyBlockId == legacyBlockId)
                    return blocks[i];
            return default;
        }

        /// <summary>A crew called off, or a house that has just been stood up: the
        /// posting goes.</summary>
        void ForgetPosting(int crewId) => postings.Remove(crewId);
    }
}
