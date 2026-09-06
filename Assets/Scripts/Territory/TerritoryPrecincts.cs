using System;
using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>
    /// A station house, as the layout knows it: an id and the ground it stands on. The
    /// scene hands over a POINT, because that is all it honestly has - where the house
    /// was placed - and the geography decides which block that point is on. A paper city
    /// with no ground under it names the block outright instead.
    /// </summary>
    public readonly struct TerritoryPrecinctSeat
    {
        /// <summary>A house standing at this point; its block is resolved later.</summary>
        public TerritoryPrecinctSeat(int stationId, TerritoryPoint where)
        {
            StationId = stationId;
            Where = where;
            Block = default;
        }

        /// <summary>A house already seated on a named block.</summary>
        public TerritoryPrecinctSeat(int stationId, TerritoryBlockId block, TerritoryPoint where)
        {
            StationId = stationId;
            Where = where;
            Block = block;
        }

        /// <summary>Which precinct's books this house keeps. The scene's own station id,
        /// carried unchanged so a roster, a car and a block all name the same house.</summary>
        public int StationId { get; }

        /// <summary>Where the house stands, in plan coordinates.</summary>
        public TerritoryPoint Where { get; }

        /// <summary>The block it stands on, once something has resolved it.</summary>
        public TerritoryBlockId Block { get; }

        public bool IsSeated => Block.IsValid;

        /// <summary>The same house, seated on a block.</summary>
        public TerritoryPrecinctSeat On(TerritoryBlockId block) =>
            new TerritoryPrecinctSeat(StationId, block, Where);
    }

    /// <summary>
    /// WHICH STATION HOUSE POLICES WHICH BLOCK, computed once for a whole city and then
    /// only read (GAN-236). The rule is the plainest one a city can have: a block belongs
    /// to the station house it is fewest STREET CROSSINGS away from, walked over the same
    /// block graph everything else in the territory layer walks over. Distance in metres
    /// is not the rule - a house on the far side of a river is not the nearest one to
    /// anybody, whatever a tape measure says.
    ///
    /// Pure: no GameObject, no engine call, no seed. Three tickets each proposed a
    /// coverage map of their own (EPIC 44 REC-005, EPIC 45 FIX-007 and FIX-003); this is
    /// the one owner they were ruled into, so the scene, the paper city and the minds all
    /// read the same map.
    ///
    /// Ties break on the LOWER station id, which makes the answer independent of the
    /// order the houses were handed over in.
    /// </summary>
    public sealed class TerritoryPrecinctMap
    {
        /// <summary>No house polices this block: the city stood no station at all, or the
        /// block was never handed to the map. Never guessed at - a caller that gets this
        /// is being told the truth and can say so.</summary>
        public const int NoPrecinct = -1;

        static readonly TerritoryBlockId[] NoBlocks = Array.Empty<TerritoryBlockId>();
        static readonly TerritoryPrecinctSeat[] NoSeats = Array.Empty<TerritoryPrecinctSeat>();

        readonly Dictionary<TerritoryBlockId, int> station =
            new Dictionary<TerritoryBlockId, int>();
        readonly Dictionary<TerritoryBlockId, int> hops =
            new Dictionary<TerritoryBlockId, int>();
        readonly Dictionary<int, List<TerritoryBlockId>> ground =
            new Dictionary<int, List<TerritoryBlockId>>();
        readonly List<TerritoryPrecinctSeat> seats = new List<TerritoryPrecinctSeat>();
        readonly List<string> notes = new List<string>();

        /// <summary>An empty map - a city with no station house in it. Everything answers
        /// <see cref="NoPrecinct"/>, which is what a city with no law actually means.</summary>
        public static TerritoryPrecinctMap Empty { get; } = new TerritoryPrecinctMap();

        TerritoryPrecinctMap()
        {
        }

        /// <param name="blocks">Every block in the city, in the geography's own order.</param>
        /// <param name="neighbours">The block graph - one lookup, never a second rule.</param>
        /// <param name="centre">Where a block is, for the blocks the graph cannot reach.</param>
        /// <param name="houses">The seated station houses. A seat with no block is dropped
        /// and noted rather than placed somewhere plausible.</param>
        public TerritoryPrecinctMap(
            IReadOnlyList<TerritoryBlockId> blocks,
            Func<TerritoryBlockId, IReadOnlyList<TerritoryBlockId>> neighbours,
            Func<TerritoryBlockId, TerritoryPoint> centre,
            IReadOnlyList<TerritoryPrecinctSeat> houses)
        {
            if (blocks == null || blocks.Count == 0 || houses == null || houses.Count == 0)
                return;

            // Ascending station id, so the walk below meets the houses in the order the
            // tie rule wants them met and the map does not depend on the caller's list.
            for (var i = 0; i < houses.Count; i++)
            {
                var seat = houses[i];
                if (!seat.IsSeated)
                {
                    notes.Add("PRECINCT: station " + seat.StationId +
                              " stands on no canonical block and polices nothing.");
                    continue;
                }

                var already = IndexOfStation(seat.StationId);
                if (already >= 0)
                {
                    notes.Add("PRECINCT: station " + seat.StationId +
                              " was handed over twice; the second seat was dropped.");
                    continue;
                }

                seats.Add(seat);
            }

            seats.Sort((a, b) => a.StationId.CompareTo(b.StationId));
            if (seats.Count == 0)
                return;

            Walk(neighbours);
            Strand(blocks, centre);
            Collect(blocks);
        }

        /// <summary>The houses this map was built from, ascending by station id.</summary>
        public IReadOnlyList<TerritoryPrecinctSeat> Seats =>
            seats.Count > 0 ? (IReadOnlyList<TerritoryPrecinctSeat>)seats : NoSeats;

        public int Stations => seats.Count;

        /// <summary>What the map could not walk to, in words, for whoever prints the
        /// geography's report.</summary>
        public IReadOnlyList<string> Notes => notes;

        /// <summary>Which station house polices this block, or <see cref="NoPrecinct"/>.</summary>
        public int PrecinctOf(TerritoryBlockId blockId) =>
            station.TryGetValue(blockId, out var id) ? id : NoPrecinct;

        /// <summary>How many streets a man crosses from this block to its own station
        /// house; -1 when nothing polices it. Zero on the station's own block.</summary>
        public int HopsToStation(TerritoryBlockId blockId) =>
            hops.TryGetValue(blockId, out var walked) ? walked : -1;

        /// <summary>Every block this house polices, in the geography's own block order.</summary>
        public IReadOnlyList<TerritoryBlockId> GroundOf(int stationId) =>
            ground.TryGetValue(stationId, out var list)
                ? (IReadOnlyList<TerritoryBlockId>)list
                : NoBlocks;

        /// <summary>The block a house stands on, or an invalid id when there is no such
        /// house on this map.</summary>
        public TerritoryBlockId SeatOf(int stationId)
        {
            var index = IndexOfStation(stationId);
            return index >= 0 ? seats[index].Block : default;
        }

        int IndexOfStation(int stationId)
        {
            for (var i = 0; i < seats.Count; i++)
                if (seats[i].StationId == stationId)
                    return i;
            return -1;
        }

        /// <summary>
        /// One breadth-first walk out of every station house at once. A block is claimed
        /// by the house it is fewest crossings from; on an equal walk the lower station id
        /// takes it, which is why an improved claim is walked on again - a block that
        /// changes hands hands its neighbours on, and it can only ever change to a lower
        /// id at the same distance, so the walk cannot circle.
        /// </summary>
        void Walk(Func<TerritoryBlockId, IReadOnlyList<TerritoryBlockId>> neighbours)
        {
            var queue = new Queue<TerritoryBlockId>();
            for (var i = 0; i < seats.Count; i++)
            {
                var seat = seats[i];
                if (station.ContainsKey(seat.Block))
                {
                    notes.Add("PRECINCT: stations " + station[seat.Block] + " and " +
                              seat.StationId + " stand on the same block '" +
                              seat.Block.Value + "'; the lower id keeps it.");
                    continue;
                }

                station[seat.Block] = seat.StationId;
                hops[seat.Block] = 0;
                queue.Enqueue(seat.Block);
            }

            while (queue.Count > 0)
            {
                var here = queue.Dequeue();
                var walked = hops[here] + 1;
                var mine = station[here];
                var next = neighbours != null ? neighbours(here) : null;
                if (next == null)
                    continue;

                for (var i = 0; i < next.Count; i++)
                {
                    var other = next[i];
                    if (hops.TryGetValue(other, out var known))
                    {
                        if (known < walked || (known == walked && station[other] <= mine))
                            continue;
                    }

                    hops[other] = walked;
                    station[other] = mine;
                    queue.Enqueue(other);
                }
            }
        }

        /// <summary>
        /// The blocks the graph never reached - an island, a lot across open ground, a
        /// city whose only station stands on one. They are NOT left lawless: the house
        /// whose own block is nearest theirs takes them, which is the guess the whole city
        /// used to make everywhere, made here only where a walk is impossible and noted
        /// every time so a bad plan reads as a bad plan.
        /// </summary>
        void Strand(IReadOnlyList<TerritoryBlockId> blocks,
                    Func<TerritoryBlockId, TerritoryPoint> centre)
        {
            var stranded = 0;
            for (var i = 0; i < blocks.Count; i++)
            {
                var id = blocks[i];
                if (station.ContainsKey(id))
                    continue;

                var best = NoPrecinct;
                var bestDistance = float.MaxValue;
                var from = centre != null ? centre(id) : default;
                for (var s = 0; s < seats.Count; s++)
                {
                    var seat = seats[s];
                    var at = centre != null ? centre(seat.Block) : seat.Where;
                    var dx = at.X - from.X;
                    var dz = at.Z - from.Z;
                    var distance = dx * dx + dz * dz;
                    if (distance >= bestDistance)
                        continue;
                    bestDistance = distance;
                    best = seat.StationId;
                }

                if (best == NoPrecinct)
                    continue;

                station[id] = best;
                stranded++;
            }

            if (stranded > 0)
                notes.Add("PRECINCT: " + stranded + " block(s) are off the block graph; the " +
                          "nearest station house by open ground took them.");
        }

        void Collect(IReadOnlyList<TerritoryBlockId> blocks)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                var id = blocks[i];
                if (!station.TryGetValue(id, out var owner))
                    continue;
                if (!ground.TryGetValue(owner, out var list))
                {
                    list = new List<TerritoryBlockId>();
                    ground.Add(owner, list);
                }

                list.Add(id);
            }
        }
    }
}
