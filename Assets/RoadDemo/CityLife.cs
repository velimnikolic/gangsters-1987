using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // A street door civilians come out of and go in through. The builder knows
    // where every facade lands at placement time, so doors are derived there and
    // wired to the nearest sidewalk link - the same join the police station's
    // beat officers use for their forecourt walk.
    public class DemoDoor
    {
        public Vector3 Pos;               // the doorstep, on the facade plane
        public Vector3 Outward;           // facade normal, toward the street

        /// <summary>The bake this door is cut into - the block or the single building.
        /// A door is the only handle the street has on a BUILDING (the sidewalk graph
        /// knows pavement, the crowd knows doors), so a family's front premises is
        /// chosen as a door and the card opens over whatever this points at. Null for a
        /// door nobody claimed an owner for.</summary>
        public GameObject Building;
        public PedLink LinkFwd, LinkBack; // the sidewalk stretch fronting it
        public float EntryT;              // along LinkFwd
        public Vector3 EntryPos;

        /// <summary>One walker on the threshold at a time; held only for the door
        /// leg itself, so whoever is inside never padlocks the building.</summary>
        public bool Busy;
    }

    // A placed street bench. Seat geometry is the city's own measured table
    // (InteractionMarkers): the demo places SM_Prop_Bench_Seat_01/02 only, both
    // backed, so a single offset pair covers every bench here.
    public class DemoBench
    {
        public Vector3[] SeatTops; // world contact patches, one per sitter
        public Vector3 Facing;     // bench front (+Z by pack convention)
        public float GroundY;

        bool[] _occupied;

        public bool TryClaim(out int seat)
        {
            _occupied ??= new bool[SeatTops.Length];
            for (int i = 0; i < _occupied.Length; i++)
            {
                if (_occupied[i]) continue;
                _occupied[i] = true;
                seat = i;
                return true;
            }
            seat = -1;
            return false;
        }

        public void Release(int seat)
        {
            if (_occupied != null && seat >= 0 && seat < _occupied.Length)
                _occupied[seat] = false;
        }

        /// <summary>Where to stand before turning and sitting: one short step out
        /// front, on the ground - the sit-down clip covers the rest.</summary>
        public Vector3 Approach(int seat)
        {
            var stand = SeatTops[seat] + Facing * 0.35f;
            stand.y = GroundY;
            return stand;
        }
    }

    // Something worth stopping for at position T along a link - a door to slip
    // into or a bench to rest on. Registered on both directions of the sidewalk.
    public class LinkStop
    {
        public float T;
        public DemoDoor Door;
        public DemoBench Bench;
    }

    // The demo's shared street-life registry: every wired door and bench, keyed
    // by the sidewalk link that passes it, plus the behaviour tuning the builder
    // exposes. Handed to every CivilianAgent at spawn.
    public class CityLife
    {
        public readonly List<DemoDoor> Doors = new List<DemoDoor>();
        readonly Dictionary<PedLink, List<LinkStop>> _stops =
            new Dictionary<PedLink, List<LinkStop>>();

        public float SitChance = 0.45f;
        public float EnterChance = 0.3f;
        public Vector2 InsideSeconds = new Vector2(10f, 45f);
        public Vector2 SitSeconds = new Vector2(10f, 35f);
        public bool CanSit;  // all three sit clips present
        public bool CanChat; // talk clip present

        public List<LinkStop> StopsFor(PedLink link)
            => _stops.TryGetValue(link, out var list) ? list : null;

        public void AddStop(PedLink link, float t, DemoDoor door, DemoBench bench)
        {
            // a stop hugging a node would trigger mid-turn; keep it on the open stretch
            if (t < 1f || t > link.Length - 1f) return;
            if (!_stops.TryGetValue(link, out var list))
                _stops[link] = list = new List<LinkStop>();
            list.Add(new LinkStop { T = t, Door = door, Bench = bench });
        }

        /// <summary>Crossing checks walk the list in order; sort once after wiring.</summary>
        public void SortStops()
        {
            foreach (var list in _stops.Values)
                list.Sort((a, b) => a.T.CompareTo(b.T));
        }

        public DemoDoor PickFreeDoor()
        {
            if (Doors.Count == 0) return null;
            for (int tries = 0; tries < 6; tries++)
            {
                var door = Doors[Random.Range(0, Doors.Count)];
                if (!door.Busy) return door;
            }
            return null;
        }
    }
}
