using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The road as the cars know it. A CARRIAGEWAY is one straight run of road between
    // two junctions (or a junction and a dead end), kerb to kerb, both ways: a frame of
    // its own - s along its axis, d across it, right-hand positive - with its lanes
    // (RoadEdge) laid at lateral offsets, traffic heading +1 on the right-hand lanes
    // and -1 on the left. A car drives in (s, d): it belongs to a lane but stands
    // wherever its manoeuvre has put it - the crown, the far lane, the kerb - and
    // what it stands on is a RoadOccupant in the carriageway's list, the box of
    // road it has (or has claimed). Every driver's questions - who is ahead of me
    // in my band, is this band free to swing into, is anyone coming down it - are
    // answered off that list, in constant-ish time, never by scanning the whole
    // city's cars.
    //
    // Junctions: a RoadNode with CONNECTORS across it, one per lane-to-lane
    // movement, each knowing which others it crosses. A car is in the node's
    // Inside list from the moment its nose crosses the stop line until its tail
    // is out the far side, and nobody enters on a connector that crosses one that
    // is occupied. That, with the signals, is the whole of the junction discipline.

    /// <summary>What kind of road a carriageway is. A driver cruises at a different
    /// speed on each of them, and a limit is not enough to say it: the city's traffic
    /// obeys a 25 m/s deck by driving down it at ten, because ten is all the profile
    /// ever wanted. What a road IS has to be part of the road.</summary>
    public enum RoadClass
    {
        Street,
        Boulevard,
        /// <summary>A slip road: one lane, tight curves, a posted 25-40 mph.</summary>
        Ramp,
        /// <summary>A motorway carriageway - grade separated, no junctions on it.</summary>
        Freeway,
    }

    /// <summary>Anything on the road other cars must reckon with: where it is, which
    /// way it points, how fast it goes, how big it is. Every car answers it, and so
    /// does a parked prop; the men on foot (WalkObstacles) and the belt (RoadSpace)
    /// read it.</summary>
    public interface IRoadUser
    {
        Vector3 RoadPosition { get; }
        Vector3 RoadForward { get; }
        float RoadSpeed { get; }
        float HalfLength { get; }
        float HalfWidth { get; }
    }

    /// <summary>A box of road one car has, in a carriageway's frame: its body, and the
    /// claim it has laid beyond it (the stretch of a manoeuvre it is in the middle
    /// of). Everyone else plans round the claim; nobody enters it.</summary>
    public sealed class RoadOccupant
    {
        public IRoadUser Who;
        public RoadCar Car;            // null for something that never moves (a parked prop)
        public Carriageway Road;
        public float S0, S1;           // the claim, along the road (S0 < S1)
        public float D0, D1;           // the claim, across it (D0 < D1)
        public float BodyS0, BodyS1;   // the body alone
        public float BodyD0, BodyD1;
        public float Vel;              // signed speed along the axis (+ toward B)
        /// <summary>How hard he is braking this instant, in metres a second squared
        /// (0 while he is not slowing). What is behind him stops short of where he
        /// will COME TO REST, and that point runs back to meet him when he stands on
        /// the brakes - which is the difference between braking with him and braking
        /// half a second after him.</summary>
        public float Slowing;
        /// <summary>The road-s his CENTRE means to come to rest at, if he means to
        /// come to rest at all (NaN while he is going somewhere). A queue at a red
        /// light is a line of cars all stopping in the same few metres, and each of
        /// them starts braking a little later than the one in front - the one behind
        /// is further from the line, so its own curve says it may still go. Three
        /// tenths of a second of that eats a two-metre gap. Reading the man in front's
        /// stopping place instead of his speed, the whole queue brakes together and
        /// the gap is kept; it says nothing at all in open road, so it costs nothing
        /// there.</summary>
        public float StopAt = float.NaN;
        public int Heading;            // the way the nose points: +1 / -1 along the axis, 0 crosswise
        public bool Parked;            // stood at the kerb, out of the running lanes
        public int Priority;           // who gives way in a standoff (higher wins)

        public float Length => BodyS1 - BodyS0;
        public bool Moving => Mathf.Abs(Vel) > 0.5f;

        public bool Overlaps(float d0, float d1) => D0 < d1 && D1 > d0;
        public bool BodyOverlaps(float d0, float d1) => BodyD0 < d1 && BodyD1 > d0;
    }

    /// <summary>One car in a junction box: which connector it is on, how far along.</summary>
    public sealed class NodeOccupant
    {
        public RoadCar Car;
        public Connector Via;
        public float S;      // metres along the connector
    }

    /// <summary>One way across a junction: from the end of one lane to the start of
    /// another, as a polyline (a quadratic Bezier through the corner for a turn, a
    /// straight for straight on, a half circle for the turn-round at a dead end),
    /// with the list of the node's other connectors it crosses.</summary>
    public sealed class Connector
    {
        public RoadNode Node;
        public RoadEdge From, To;
        public Turn Kind;
        public int Index;
        public float Length;
        public Vector3[] Pts;          // world points, y = 0
        public float[] Cum;            // cumulative length at each point
        public Vector3[] Tan;          // the smoothed tangent at each point (blended between its neighbours)
        public bool[] Conflicts;       // coarse topology/debug table; runtime admission uses body envelopes
        public bool UTurn;             // the dead-end turn-round
        public float MinRadius = float.MaxValue;

        /// <summary>Where a car this far along the connector is, and which way it faces.</summary>
        public void Pose(float s, out Vector3 pos, out Vector3 fwd)
        {
            int n = Pts.Length;
            if (n < 2) { pos = Pts.Length > 0 ? Pts[0] : Vector3.zero; fwd = Vector3.forward; return; }
            s = Mathf.Clamp(s, 0f, Length);
            int i = 1;
            while (i < n - 1 && Cum[i] < s) i++;
            float seg = Cum[i] - Cum[i - 1];
            float t = seg > 1e-5f ? (s - Cum[i - 1]) / seg : 0f;
            var p0 = Pts[i - 1]; var p1 = Pts[i];
            if (Tan == null || seg <= 1e-5f)
            {
                pos = Vector3.LerpUnclamped(p0, p1, t);
                var tan = p1 - p0; tan.y = 0f;
                fwd = tan.sqrMagnitude > 1e-8f ? tan.normalized : To.Dir;
                return;
            }
            // a cubic Hermite between the points on their tangents: the heading IS the
            // direction the point moves (a chord with a blended tangent crabs by half
            // the corner between the chords), and it turns smoothly through the points
            var m0 = Tan[i - 1] * seg; var m1 = Tan[i] * seg;
            float t2 = t * t, t3 = t2 * t;
            pos = p0 * (2f * t3 - 3f * t2 + 1f) + m0 * (t3 - 2f * t2 + t) + p1 * (-2f * t3 + 3f * t2) + m1 * (t3 - t2);
            var d = p0 * (6f * t2 - 6f * t) + m0 * (3f * t2 - 4f * t + 1f) + p1 * (-6f * t2 + 6f * t) + m1 * (3f * t2 - 2f * t);
            d.y = 0f;
            fwd = d.sqrMagnitude > 1e-8f ? d.normalized : To.Dir;
        }

        public Vector3 Point(float s)
        {
            Pose(s, out var p, out _);
            return p;
        }
    }

    /// <summary>A run of road between two nodes, kerb to kerb, both ways, with its
    /// lanes, its parking strips and the cars on it. Frame: s along Axis from A,
    /// d across it (positive to the right of the axis), y = 0.</summary>
    public sealed class Carriageway
    {
        public Vector3 A, B, Axis, Right;
        public float Length;
        public float HalfRoad;
        public float SpeedLimit;
        /// <summary>What kind of road this is: what a driver cruises at on it.</summary>
        public RoadClass Class = RoadClass.Street;
        /// <summary>The line the road actually follows, when it is not a straight one
        /// (an expressway's corner, a slip road's curve down to its terminal). Null on
        /// a street, which is every road the city had before there were motorways: then
        /// Axis and Right answer, as they always did, and nothing costs anything.
        ///
        /// s is metres ALONG this line and d metres to the right OF IT, so every band,
        /// gap and claim in the driving is unchanged - only the frame bends.</summary>
        public RoadLine Path;
        /// <summary>How far the asphalt reaches either side of the axis, when it is not
        /// the same both ways (a deck with an auxiliary lane down one side is wider on
        /// that side and nowhere else). NaN: HalfRoad, symmetrically.</summary>
        public float HalfMinus = float.NaN, HalfPlus = float.NaN;
        public float EdgeLo => float.IsNaN(HalfMinus) ? -HalfRoad : -HalfMinus;
        public float EdgeHi => float.IsNaN(HalfPlus) ? HalfRoad : HalfPlus;
        public RoadNode NodeA, NodeB;          // the junctions at either end (null: open end)
        public float MedianHalf;               // |d| under this is a median, not road (boulevards)
        /// <summary>The road surface's height over the city's road level: a car on it
        /// rides this much higher. The belt freeway's decks stand a hand over the plain.</summary>
        public float SurfaceY;
        /// <summary>A surface that CLIMBS: the height over the city's road level at a
        /// given s along the road, for a slip road off an elevated freeway or the
        /// freeway's own run down off its pillars. Null on a road that lies flat, which
        /// is nearly all of them - then SurfaceY alone answers.</summary>
        public System.Func<float, float> SurfaceAt;
        /// <summary>A road off the ground: the deck of an elevated freeway or a slip
        /// road climbing to one. Nothing in the driving reads it - it is what the black
        /// box calls a freeway, so a run can be asked whether the freeway was used at
        /// all (DriveTrace's "deck" rows).</summary>
        public bool Elevated;

        /// <summary>The surface's height at a point along the road, and at its ends.</summary>
        public float SurfaceOn(float s) => SurfaceAt != null ? SurfaceAt(Mathf.Clamp(s, 0f, Length)) : SurfaceY;
        public float SurfaceA => SurfaceOn(0f);
        public float SurfaceB => SurfaceOn(Length);
        public readonly List<RoadEdge> Lanes = new List<RoadEdge>();       // by offset, ascending
        public readonly List<RoadOccupant> Occupants = new List<RoadOccupant>();
        public bool ParkingA = true, ParkingB = true;                        // kerb parking allowed, left/right of axis
        public int Index;
        /// <summary>The network this road belongs to.</summary>
        public LaneNet Net;

        public Vector3 Pose(float s, float d)
            => Path != null ? Path.Pose(s, d) : A + Axis * s + Right * d;

        /// <summary>Which way the road runs at s (its axis on a straight one).</summary>
        public Vector3 DirAt(float s) => Path != null ? Path.DirAt(s) : Axis;
        /// <summary>And across it, to the right.</summary>
        public Vector3 RightAt(float s) => Path != null ? Path.RightAt(s) : Right;
        /// <summary>The turn radius under s; a straight answers float.MaxValue.</summary>
        public float RadiusAt(float s) => Path != null ? Path.RadiusAt(s) : float.MaxValue;

        public void Project(Vector3 p, out float s, out float d)
        {
            if (Path != null) { Path.Project(p, out s, out d); return; }
            var v = p - A;
            v.y = 0f;
            s = Vector3.Dot(v, Axis);
            d = Vector3.Dot(v, Right);
        }

        /// <summary>The node at the end a car heading this way runs into (null: dead end).</summary>
        public RoadNode NodeAhead(int heading) => heading > 0 ? NodeB : NodeA;
        public RoadNode NodeBehind(int heading) => heading > 0 ? NodeA : NodeB;

        /// <summary>Road-s of the end a car heading this way runs toward.</summary>
        public float EndS(int heading) => heading > 0 ? Length : 0f;

        /// <summary>The lateral position a car of this half width parks at against
        /// the kerb on the side traffic heading this way keeps to (its flank a hand
        /// over the stone, the way the pack's cars sit on a kerb).</summary>
        public float KerbD(int heading, float halfWidth) => heading * (HalfRoad - halfWidth + 0.38f);

        /// <summary>The kerb on the side of the axis this lateral position lies.</summary>
        public float KerbDOnSide(float d, float halfWidth) => (d >= 0f ? 1f : -1f) * (HalfRoad - halfWidth + 0.38f);

        /// <summary>Inside the carriageway for a body reaching this far across it.</summary>
        public float ClampD(float d, float lateralExtent)
            => Mathf.Clamp(d, EdgeLo + lateralExtent - 0.45f, EdgeHi - lateralExtent + 0.45f);

        /// <summary>The nearest lane heading this way to lateral d (null: none that way).</summary>
        public RoadEdge LaneFor(int heading, float d)
        {
            RoadEdge best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < Lanes.Count; i++)
            {
                var l = Lanes[i];
                if (l.Heading != heading) continue;
                float dist = Mathf.Abs(l.Offset - d);
                if (dist < bestDist) { bestDist = dist; best = l; }
            }
            return best;
        }

        /// <summary>The next lane over from this one, on the side <paramref name="toward"/>
        /// points (+1: to the right of travel, -1: to the left) - what a car changing
        /// lane moves into. Null when there is nothing that way.</summary>
        public RoadEdge Beside(RoadEdge lane, int toward)
        {
            if (lane == null) return null;
            RoadEdge best = null;
            float bestGap = float.MaxValue;
            float want = lane.Heading * toward;          // in road-d terms
            for (int i = 0; i < Lanes.Count; i++)
            {
                var l = Lanes[i];
                if (l == lane || l.Heading != lane.Heading) continue;
                float gap = (l.Offset - lane.Offset) * want;
                if (gap <= 0.2f) continue;
                if (gap < bestGap) { bestGap = gap; best = l; }
            }
            return best;
        }

        /// <summary>The lanes heading this way, innermost (nearest the crown) first.</summary>
        public void LanesHeading(int heading, List<RoadEdge> into)
        {
            into.Clear();
            for (int i = 0; i < Lanes.Count; i++) if (Lanes[i].Heading == heading) into.Add(Lanes[i]);
            into.Sort((x, y) => Mathf.Abs(x.Offset).CompareTo(Mathf.Abs(y.Offset)));
        }

        public bool TwoWay
        {
            get
            {
                bool plus = false, minus = false;
                for (int i = 0; i < Lanes.Count; i++) { if (Lanes[i].Heading > 0) plus = true; else minus = true; }
                return plus && minus;
            }
        }

        /// <summary>Can a car stand at lateral d without being on a median or over a kerb?</summary>
        public bool Drivable(float d, float halfWidth)
        {
            if (d - halfWidth < EdgeLo - 0.45f || d + halfWidth > EdgeHi + 0.45f) return false;
            if (MedianHalf > 0f && Mathf.Abs(d) - halfWidth < MedianHalf) return false;
            return true;
        }

        // ------------------------------------------------------------ the occupants

        /// <summary>The nearest occupant ahead of s (heading this way) whose claim
        /// overlaps the band [d0, d1], and the metres of road between <paramref
        /// name="noseS"/> and its near end (negative: alongside / overlapping).</summary>
        public RoadOccupant Ahead(RoadOccupant self, int heading, float noseS, float tailS,
            float d0, float d1, out float gap, System.Predicate<RoadOccupant> accepts = null)
        {
            RoadOccupant best = null;
            gap = float.MaxValue;
            for (int i = 0; i < Occupants.Count; i++)
            {
                var o = Occupants[i];
                if (ReferenceEquals(o, self) || (self != null && ReferenceEquals(o.Who, self.Who))) continue;
                // a stopped car is followed up to its body; a moving one up to its claim
                bool body = !o.Moving;
                if (body ? !o.BodyOverlaps(d0, d1) : !o.Overlaps(d0, d1)) continue;
                float near = heading > 0 ? (body ? o.BodyS0 : o.S0) : (body ? o.BodyS1 : o.S1);
                float far = heading > 0 ? (body ? o.BodyS1 : o.S1) : (body ? o.BodyS0 : o.S0);
                float g = (near - noseS) * heading;
                // its far end short of our nose: behind us, or alongside and behind - not ahead
                if ((far - noseS) * heading < 0.3f) continue;
                if (g < gap && (accepts == null || accepts(o))) { gap = g; best = o; }
            }
            return best;
        }

        /// <summary>The nearest occupant behind s (heading this way) whose claim
        /// overlaps the band, and the metres between its near end and our tail.</summary>
        public RoadOccupant Behind(RoadOccupant self, int heading, float tailS, float d0, float d1, out float gap)
        {
            RoadOccupant best = null;
            gap = float.MaxValue;
            for (int i = 0; i < Occupants.Count; i++)
            {
                var o = Occupants[i];
                if (ReferenceEquals(o, self) || (self != null && ReferenceEquals(o.Who, self.Who))) continue;
                if (!o.Overlaps(d0, d1)) continue;
                float near = heading > 0 ? o.S1 : o.S0;
                float g = (tailS - near) * heading;
                if (g < -0.01f) continue; // not behind
                if (g < gap) { gap = g; best = o; }
            }
            return best;
        }

        /// <summary>Is anyone's claim in this box of road (other than ours)?</summary>
        public bool Busy(RoadOccupant self, float s0, float s1, float d0, float d1,
            bool ignoreParked = false, bool stationaryOnly = false)
        {
            for (int i = 0; i < Occupants.Count; i++)
            {
                var o = Occupants[i];
                if (ReferenceEquals(o, self) || (self != null && ReferenceEquals(o.Who, self.Who))) continue;
                if (ignoreParked && o.Parked) continue;
                if (stationaryOnly && o.Car != null && !o.Car.Parked && !o.Car.Derelict && !o.Car.Wrecked) continue;
                if (o.S0 < s1 && o.S1 > s0 && o.Overlaps(d0, d1)) return true;
            }
            return false;
        }

        /// <summary>Metres of the band [d0, d1] free ahead of noseS (heading this way)
        /// before the first claim in it, up to <paramref name="upTo"/>; negative when
        /// something in the band is already alongside (between tail and nose).</summary>
        /// <param name="skipParked">Leave the parked out of it. For a car deciding
        /// whether it may MOVE OFF, a parked car ahead is not a reason to wait: it is
        /// never going anywhere, so waiting for it is waiting for ever. What it is
        /// instead is a thing to go round once we are rolling, which the tactics handle
        /// (Decide's behindParked). For every other question - a gap to change lane
        /// into, room to overtake - a parked car is as solid as any other and this
        /// stays false.</param>
        public float FreeAhead(RoadOccupant self, int heading, float noseS, float tailS, float d0, float d1, float upTo,
            bool skipParked = false)
        {
            float free = upTo;
            for (int i = 0; i < Occupants.Count; i++)
            {
                var o = Occupants[i];
                if (ReferenceEquals(o, self) || (self != null && ReferenceEquals(o.Who, self.Who))) continue;
                if (skipParked && o.Parked) continue;
                if (!o.Overlaps(d0, d1)) continue;
                float near = heading > 0 ? o.S0 : o.S1;
                float far = heading > 0 ? o.S1 : o.S0;
                if ((far - tailS) * heading < 0f) continue;       // behind us
                float g = (near - noseS) * heading;
                if (g < 0.3f) return -1f;                            // beside us, or on us
                if (g < free) free = g;
            }
            return free;
        }

        /// <summary>Would a car coming the other way down this band reach
        /// <paramref name="farS"/> (the far end of what we mean to do) within
        /// <paramref name="seconds"/>? Anyone heading against us in the band ahead of
        /// our nose, moving, allowing for him not braking at all.</summary>
        public bool OncomingWithin(RoadOccupant self, int heading, float noseS, float farS, float d0, float d1, float seconds, float ourSpeed)
        {
            for (int i = 0; i < Occupants.Count; i++)
            {
                var o = Occupants[i];
                if (ReferenceEquals(o, self) || (self != null && ReferenceEquals(o.Who, self.Who))) continue;
                if (!o.Overlaps(d0, d1)) continue;
                float v = -o.Vel * heading;                          // his speed toward us
                if (v < 0.5f) continue;                              // not coming
                float near = heading > 0 ? o.S0 : o.S1;
                float dist = (near - farS) * heading;                // from his nose to the far end of our stretch
                if (dist < 0f) return true;                          // already on it
                if (dist / (v + Mathf.Max(ourSpeed, 3f)) < seconds) return true;
            }
            return false;
        }
    }

    /// <summary>The road network of a scene - its carriageways, nodes and lanes -
    /// and the two services on it every car uses: where it is on the road, and how
    /// to get from this lane to that one.</summary>
    public sealed class LaneNet
    {
        public readonly List<Carriageway> Roads = new List<Carriageway>();
        public readonly List<RoadNode> Nodes = new List<RoadNode>();
        public readonly List<RoadEdge> Edges = new List<RoadEdge>();

        /// <summary>The network of the scene being played, for whatever needs one and
        /// was not handed one (a prop parked by a scene builder, a crew car attached
        /// to a body). Set by the scene builder; cleared for play.</summary>
        public static LaneNet Active;

        static LaneNet _shared;
        /// <summary>The network cars fall back on when nobody gave them one: edges
        /// built the old way (a scene with its own RoadEdge list and no LaneNet) are
        /// adopted into it as they are driven.</summary>
        public static LaneNet Shared => _shared ??= new LaneNet();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() { _shared = null; Active = null; }

        /// <summary>Metres between the points a connector is laid from.</summary>
        const float ConnectorStep = 0.5f;
        /// <summary>Two connectors nearer than this anywhere along their length cross.</summary>
        // Conservative topology/debug table. Runtime admission refines this using
        // JunctionClearance envelopes for the actual bodies and axle offsets.
        const float ConflictReach = 3.6f;
        /// <summary>Metres of car each side of the connector's ends - the body still on
        /// the approach, and the nose already out the far side.</summary>
        const float BodyOverhang = 2.6f;
        /// <summary>The same, where either line bends: the body leans out of the bend.</summary>
        const float ConflictReachTurn = 6.0f;

        // ------------------------------------------------------------ building

        /// <summary>A junction box at (x, z) of this half size.</summary>
        public RoadNode AddNode(float x, float z, float halfX, float halfZ, float stopSetback = 5.7f)
        {
            var n = new RoadNode
            {
                X = x, Z = z, XMin = x - halfX, XMax = x + halfX, ZMin = z - halfZ, ZMax = z + halfZ,
                StopSetback = stopSetback,
            };
            Nodes.Add(n);
            return n;
        }

        /// <summary>A carriageway from a to b (the centre line, node box edge to node
        /// box edge) with lanes at these offsets either side of the axis: for each
        /// offset a lane heading +1 at +offset and one heading -1 at -offset. Lanes
        /// are linked into the nodes like any edge.</summary>
        public Carriageway AddRoad(Vector3 a, Vector3 b, float halfRoad, float[] laneOffsets, float speedLimit,
            RoadNode nodeA, RoadNode nodeB, bool northSouth, float medianHalf = 0f)
        {
            a.y = 0f; b.y = 0f;
            var axis = b - a;
            float len = axis.magnitude;
            axis = len > 1e-5f ? axis / len : Vector3.forward;
            var road = new Carriageway
            {
                A = a, B = b, Axis = axis, Right = Vector3.Cross(Vector3.up, axis), Length = len,
                HalfRoad = halfRoad, SpeedLimit = speedLimit, NodeA = nodeA, NodeB = nodeB,
                MedianHalf = medianHalf, Index = Roads.Count, Net = this,
            };
            Roads.Add(road);
            foreach (float off in laneOffsets)
            {
                AddLane(road, +1, +off, nodeA, nodeB, northSouth, speedLimit);
                AddLane(road, -1, -off, nodeB, nodeA, northSouth, speedLimit);
            }
            road.Lanes.Sort((x, y) => x.Offset.CompareTo(y.Offset));
            return road;
        }

        /// <summary>A carriageway that runs ONE WAY only - every lane with its axis,
        /// A to B. An elevated freeway is two of these side by side rather than one
        /// road with four lanes, and that is what lets a slip road join one deck
        /// without a car having to cross the other one to reach it.</summary>
        public Carriageway AddOneWay(Vector3 a, Vector3 b, float halfRoad, float[] laneOffsets, float speedLimit,
            RoadNode nodeA, RoadNode nodeB, bool northSouth)
        {
            a.y = 0f; b.y = 0f;
            var axis = b - a;
            float len = axis.magnitude;
            axis = len > 1e-5f ? axis / len : Vector3.forward;
            var road = new Carriageway
            {
                A = a, B = b, Axis = axis, Right = Vector3.Cross(Vector3.up, axis), Length = len,
                HalfRoad = halfRoad, SpeedLimit = speedLimit, NodeA = nodeA, NodeB = nodeB,
                Index = Roads.Count, Net = this, ParkingA = false, ParkingB = false,
            };
            Roads.Add(road);
            foreach (float off in laneOffsets)
                AddLane(road, +1, off, nodeA, nodeB, northSouth, speedLimit);
            road.Lanes.Sort((x, y) => x.Offset.CompareTo(y.Offset));
            return road;
        }

        /// <summary>A carriageway that BENDS: the same road as AddRoad or AddOneWay,
        /// laid along a line instead of between two points. Its s runs along that line
        /// and its d across it, so a bend costs the driving nothing.</summary>
        public Carriageway AddCurve(RoadLine path, float halfRoad, float[] laneOffsets, float speedLimit,
            RoadNode nodeA, RoadNode nodeB, bool oneWay, RoadClass cls = RoadClass.Street, float medianHalf = 0f)
        {
            var a = path.Start; var b = path.End;
            var road = new Carriageway
            {
                A = a, B = b, Axis = path.StartDir, Right = new Vector3(path.StartDir.z, 0f, -path.StartDir.x),
                Length = path.Length, Path = path,
                HalfRoad = halfRoad, SpeedLimit = speedLimit, NodeA = nodeA, NodeB = nodeB,
                MedianHalf = medianHalf, Index = Roads.Count, Net = this, Class = cls,
                ParkingA = !oneWay, ParkingB = !oneWay,
            };
            Roads.Add(road);
            bool northSouth = Mathf.Abs(path.StartDir.z) >= Mathf.Abs(path.StartDir.x);
            foreach (float off in laneOffsets)
            {
                AddLane(road, +1, off, nodeA, nodeB, northSouth, speedLimit);
                if (!oneWay) AddLane(road, -1, -off, nodeB, nodeA, northSouth, speedLimit);
            }
            road.Lanes.Sort((x, y) => x.Offset.CompareTo(y.Offset));
            return road;
        }

        /// <summary>One lane on a road, heading with (+1) or against (-1) its axis.</summary>
        public RoadEdge AddLane(Carriageway road, int heading, float offset, RoadNode from, RoadNode to, bool northSouth, float speedLimit)
        {
            float sStart = heading > 0 ? 0f : road.Length;
            float sEnd = heading > 0 ? road.Length : 0f;
            var start = road.Pose(sStart, offset);
            var end = road.Pose(sEnd, offset);
            // a lane on a bend leaves its road pointing one way and arrives pointing
            // another: the junction at each end is built off the direction AT THAT END,
            // and Dir stays the chord, which is what everything else means by it
            var chord = end - start; chord.y = 0f;
            var e = new RoadEdge
            {
                From = from, To = to, Start = start, End = end,
                Dir = chord.sqrMagnitude > 1e-6f ? chord.normalized : road.Axis * heading,
                DirOut = road.DirAt(sStart) * heading,
                DirIn = road.DirAt(sEnd) * heading,
                Length = road.Length, NorthSouth = northSouth, SpeedLimit = speedLimit,
                Road = road, Offset = offset, Heading = heading, S0 = sStart,
            };
            from?.Outgoing.Add(e);
            to?.Incoming.Add(e);
            road.Lanes.Add(e);
            Edges.Add(e);
            return e;
        }

        /// <summary>Takes an edge built the old way (Start, End, From, To set; no
        /// road) under its wing: a one-lane carriageway of its own, so the car logic
        /// has a frame to work in. Half road = a lane.</summary>
        public Carriageway Adopt(RoadEdge e, float halfRoad = 2.5f)
        {
            if (e.Road != null) return e.Road;
            var road = new Carriageway
            {
                A = e.Start, B = e.End, Axis = e.Dir, Right = Vector3.Cross(Vector3.up, e.Dir), Length = e.Length,
                HalfRoad = halfRoad, SpeedLimit = e.SpeedLimit, NodeA = e.From, NodeB = e.To, Index = Roads.Count,
                ParkingA = false, ParkingB = false, Net = this,
            };
            road.A.y = 0f; road.B.y = 0f;
            Roads.Add(road);
            e.Road = road;
            e.Offset = 0f;
            e.Heading = 1;
            e.S0 = 0f;
            road.Lanes.Add(e);
            if (!Edges.Contains(e)) Edges.Add(e);
            if (e.From != null && !Nodes.Contains(e.From)) Nodes.Add(e.From);
            if (e.To != null && !Nodes.Contains(e.To)) Nodes.Add(e.To);
            return road;
        }

        /// <summary>Lays the connectors of every node and their conflict tables. Call
        /// once the roads are in. Edges without a road are adopted first.</summary>
        public void Finish()
        {
            foreach (var e in Edges) if (e.Road == null) Adopt(e);
            foreach (var n in Nodes) { n.Connectors.Clear(); Prepare(n); }
        }

        /// <summary>A node's connectors laid if they are not yet (a scene that built
        /// its edges the old way and never called Finish): every edge into and out of
        /// it adopted first.</summary>
        public void Prepare(RoadNode n)
        {
            if (n == null || n.Connectors.Count > 0) return;
            n.BodyClearance = null;
            foreach (var e in n.Incoming) if (e.Road == null) Adopt(e);
            foreach (var e in n.Outgoing) if (e.Road == null) Adopt(e);
            if (!Nodes.Contains(n)) Nodes.Add(n);
            if (n.Seam) { PrepareSeam(n); return; }
            foreach (var a in n.Incoming)
            {
                bool anyOn = false;
                foreach (var b in n.Outgoing)
                {
                    if (Vector3.Dot(Arriving(a), Leaving(b)) < -0.5f) continue;
                    AddConnector(n, a, b);
                    anyOn = true;
                }
                if (!anyOn)
                {
                    var back = a.Road.LaneFor(-a.Heading, -a.Offset);
                    if (back != null && back.From == n) AddConnector(n, a, back, uturn: true);
                    else
                    {
                        // A ONE-WAY PAIR TURNS ROUND ONTO ITS TWIN. Where a divided
                        // road ends, the lane back is not this road's - it is the
                        // other carriageway's, a few metres across the median, and it
                        // points dead against us so the loop above skipped it. Without
                        // this the arriving lane got NO connector at all, and every
                        // car that reached the line stood there wanting nothing for
                        // the rest of the run (cars 94 and 303, measured live: one in,
                        // one out, dot -1.0, gap 5 m, nothing to plan into).
                        RoadEdge turn = null; float bestD = float.MaxValue;
                        foreach (var b in n.Outgoing)
                        {
                            if (Vector3.Dot(Arriving(a), Leaving(b)) > -0.5f) continue;
                            var v = b.Start - a.End; v.y = 0f;
                            float d2 = v.sqrMagnitude;
                            if (d2 < bestD) { bestD = d2; turn = b; }
                        }
                        if (turn != null && bestD < 2f * MergeReach * 2f * MergeReach)
                            AddConnector(n, a, turn, uturn: true);
                    }
                }
            }
            BuildConflicts(n);
        }

        /// <summary>Which way a lane runs where it ARRIVES at its node, and where it
        /// LEAVES one. On a bend those are not the same and not the chord either; on
        /// everything else, and on any edge built without a carriageway, they are the
        /// lane's one direction - which is what they answer when nobody set them.</summary>
        static Vector3 Arriving(RoadEdge e) => e.DirIn.sqrMagnitude > 0.1f ? e.DirIn : e.Dir;
        static Vector3 Leaving(RoadEdge e) => e.DirOut.sqrMagnitude > 0.1f ? e.DirOut : e.Dir;

        /// <summary>Lanes that line up across a seam count as the same lane: within
        /// this far sideways, the one carries on into the other.</summary>
        const float SeamReach = 2.5f;
        /// <summary>And how far over a lane that ends may look for one to merge into.</summary>
        const float MergeReach = 6f;

        /// <summary>A SEAM: where one road hands over to the next and nothing turns.
        /// A motorway gains a lane for its exit, drops it again past the entrance, and
        /// gives a lane away to a slip road - and each of those is a place two
        /// carriageways meet, not a crossing. Every incoming lane is matched to the ONE
        /// outgoing lane that carries straight on from it, no two to the same, and
        /// nothing conflicts with anything: the through traffic never stops for a ramp,
        /// which is the whole difference between a motorway and the ring the city used
        /// to roll.
        ///
        /// A lane with no match is a lane that ENDS here (the far end of an acceleration
        /// lane). Its cars have to be somewhere else by the time they reach it, and the
        /// driver knows it (RoadCar's lane changing); a car that failed stands at the
        /// line and waits for its gap, which is what a real one does.</summary>
        void PrepareSeam(RoadNode n)
        {
            var taken = new HashSet<RoadEdge>();
            // the best matches first, so a lane that lines up exactly is never robbed
            // of its continuation by one that merely points the same way
            var pairs = new List<(float cost, RoadEdge a, RoadEdge b)>();
            foreach (var a in n.Incoming)
                foreach (var b in n.Outgoing)
                {
                    var dir = Arriving(a);
                    if (Vector3.Dot(dir, Leaving(b)) < 0.6f) continue;
                    var v = b.Start - a.End; v.y = 0f;
                    float along = Vector3.Dot(v, dir);
                    if (along < -3f) continue;
                    float across = (v - dir * along).magnitude;
                    if (across > SeamReach) continue;
                    pairs.Add((across + Mathf.Abs(along) * 0.02f, a, b));
                }
            pairs.Sort((x, y) => x.cost.CompareTo(y.cost));
            var used = new HashSet<RoadEdge>();
            foreach (var pr in pairs)
            {
                if (taken.Contains(pr.a) || used.Contains(pr.b)) continue;
                taken.Add(pr.a); used.Add(pr.b);
                AddConnector(n, pr.a, pr.b);
            }
            // A lane that lines up with nothing of its own ENDS here - the far end of an
            // acceleration lane, where the road goes back to being two lanes wide. It is
            // still given a way on: the nearest lane beside it, which some other lane is
            // already carrying on into. That is a MERGE, and the one thing at a seam that
            // does conflict: the car coming off the ramp waits for a gap in the lane it is
            // joining. Without it the lane simply stopped, and a car that had not got
            // itself over in time stood at the line for the rest of the run with nowhere
            // to go at all - it cannot change lane from a standstill, a slide is metres
            // travelled.
            foreach (var a in n.Incoming)
            {
                if (taken.Contains(a)) continue;
                RoadEdge best = null; float bestCost = float.MaxValue;
                var arriving = Arriving(a);
                foreach (var b in n.Outgoing)
                {
                    if (Vector3.Dot(arriving, Leaving(b)) < 0.6f) continue;
                    var v = b.Start - a.End; v.y = 0f;
                    float along = Vector3.Dot(v, arriving);
                    if (along < -3f) continue;
                    float across = (v - arriving * along).magnitude;
                    if (across > MergeReach) continue;
                    if (across < bestCost) { bestCost = across; best = b; }
                }
                if (best != null) AddConnector(n, a, best);
            }
            // and what crosses what: at a seam, only two ways into the same lane.
            int k = n.Connectors.Count;
            foreach (var c in n.Connectors) c.Conflicts = new bool[k];
            for (int i = 0; i < k; i++)
                for (int j = i + 1; j < k; j++)
                {
                    // and a turn-round, wherever it stands, takes the whole box (BuildConflicts)
                    bool clash = n.Connectors[i].To == n.Connectors[j].To ||
                                 n.Connectors[i].UTurn || n.Connectors[j].UTurn;
                    n.Connectors[i].Conflicts[j] = clash;
                    n.Connectors[j].Conflicts[i] = clash;
                }
        }

        /// <summary>A node's connectors laid afresh (a road added to it after Finish).</summary>
        public void Rebuild(RoadNode n)
        {
            if (n == null) return;
            n.Connectors.Clear();
            Prepare(n);
        }

        void AddConnector(RoadNode n, RoadEdge a, RoadEdge b, bool uturn = false)
        {
            var c = new Connector { Node = n, From = a, To = b, Index = n.Connectors.Count, UTurn = uturn };
            var pts = new List<Vector3>();
            var p0 = a.End; p0.y = 0f;
            var p2 = b.Start; p2.y = 0f;
            if (uturn)
            {
                // a half circle bulging into the box, from the lane's end to the lane back
                var mid = (p0 + p2) * 0.5f;
                float r = Mathf.Max(1.5f, (p2 - p0).magnitude * 0.5f);
                var side = (p0 - mid).normalized;
                var fwd = a.Dir;
                int steps = 14;
                for (int i = 0; i <= steps; i++)
                {
                    float t = Mathf.PI * i / steps;
                    pts.Add(mid + side * (r * Mathf.Cos(t)) + fwd * (r * Mathf.Sin(t)));
                }
                c.Kind = Turn.Left;
                c.MinRadius = r;
            }
            else
            {
                float dot = Vector3.Dot(a.Dir, b.Dir);
                c.Kind = dot > 0.5f ? Turn.Straight : Vector3.Cross(a.Dir, b.Dir).y > 0f ? Turn.Right : Turn.Left;
                Vector3 p1;
                bool cornered = false;
                if (c.Kind == Turn.Straight) p1 = (p0 + p2) * 0.5f;
                else
                {
                    // the corner point: where the two lane lines cross
                    var d1 = a.Dir; var d2 = b.Dir;
                    float denom = d1.x * d2.z - d1.z * d2.x;
                    if (Mathf.Abs(denom) > 1e-4f)
                    {
                        var w = p2 - p0;
                        float t = (w.x * d2.z - w.z * d2.x) / denom;
                        p1 = p0 + d1 * t;
                        cornered = t > 0.2f && Vector3.Dot(p2 - p1, d2) > 0.2f;
                    }
                    else p1 = (p0 + p2) * 0.5f;
                }
                if (cornered && Mathf.Abs(dot) < 0.2f)
                {
                    // a square corner: a straight to the arc, a circular arc of the radius
                    // the shorter leg allows (tangent to both lane lines), a straight out
                    float la = (p1 - p0).magnitude, lb = (p2 - p1).magnitude;
                    float r = Mathf.Max(1.5f, Mathf.Min(la, lb));
                    var t0 = p1 + (p0 - p1).normalized * r;
                    var t2 = p1 + (p2 - p1).normalized * r;
                    var centre = t0 + (p2 - p1).normalized * r;
                    c.MinRadius = r;
                    int sa = Mathf.Max(1, Mathf.CeilToInt((la - r) / ConnectorStep));
                    for (int i = 0; i < sa; i++) pts.Add(Vector3.Lerp(p0, t0, i / (float)sa));
                    int arc = Mathf.Max(4, Mathf.CeilToInt(0.5f * Mathf.PI * r / ConnectorStep));
                    var u = t0 - centre; var v = t2 - centre;
                    for (int i = 0; i <= arc; i++)
                    {
                        float ang = 0.5f * Mathf.PI * i / arc;
                        pts.Add(centre + u * Mathf.Cos(ang) + v * Mathf.Sin(ang));
                    }
                    int sb = Mathf.Max(1, Mathf.CeilToInt((lb - r) / ConnectorStep));
                    for (int i = 1; i <= sb; i++) pts.Add(Vector3.Lerp(t2, p2, i / (float)sb));
                }
                else
                {
                    float approx = (p1 - p0).magnitude + (p2 - p1).magnitude;
                    int steps = Mathf.Max(2, Mathf.CeilToInt(approx / ConnectorStep));
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = i / (float)steps;
                        var q = Vector3.Lerp(Vector3.Lerp(p0, p1, t), Vector3.Lerp(p1, p2, t), t);
                        pts.Add(q);
                    }
                    if (c.Kind != Turn.Straight) c.MinRadius = Mathf.Max(1.5f, Mathf.Min((p1 - p0).magnitude, (p2 - p1).magnitude) * 0.7f);
                }
            }
            // no two points on top of each other (a corner whose straight leg is zero
            // long would double its first point, and the doubled point's tangent would
            // be the chord into the arc: a kink in the heading at the box's edge)
            for (int i = pts.Count - 1; i > 0; i--)
                if ((pts[i] - pts[i - 1]).sqrMagnitude < 1e-4f) pts.RemoveAt(i == pts.Count - 1 ? i - 1 : i);
            c.Pts = pts.ToArray();
            c.Cum = new float[c.Pts.Length];
            float s = 0f;
            for (int i = 1; i < c.Pts.Length; i++)
            {
                s += Vector3.Distance(c.Pts[i - 1], c.Pts[i]);
                c.Cum[i] = s;
            }
            c.Length = Mathf.Max(0.1f, s);
            // the tangent at each point: the chord over its neighbours; the ends are the
            // lanes' own directions, so the heading meets the road without a kink
            int m = c.Pts.Length;
            c.Tan = new Vector3[m];
            for (int i = 0; i < m; i++)
            {
                Vector3 d;
                if (i == 0) d = a.Dir;
                else if (i == m - 1) d = b.Dir;
                else d = c.Pts[i + 1] - c.Pts[i - 1];
                d.y = 0f;
                c.Tan[i] = d.sqrMagnitude > 1e-8f ? d.normalized : (i > 0 ? c.Tan[i - 1] : a.Dir);
            }
            n.Connectors.Add(c);
        }

        // Which connectors cross which: any two that come within a car's width of
        // each other somewhere along their length and do not simply leave the same
        // lane (those are sequenced on the approach). Two that end on the same lane
        // merge - that is a conflict too (the second waits for the first to clear).
        static void BuildConflicts(RoadNode n)
        {
            int k = n.Connectors.Count;
            foreach (var c in n.Connectors) c.Conflicts = new bool[k];
            for (int i = 0; i < k; i++)
                for (int j = i + 1; j < k; j++)
                {
                    var a = n.Connectors[i];
                    var b = n.Connectors[j];
                    bool conflict;
                    if (a.From == b.From) conflict = false;
                    else if (a.To == b.To) conflict = true;
                    // THE TURN-ROUND TAKES THE WHOLE BOX. Its line is a half circle at the
                    // dead end, and the body on it leans out further than any turn's; the
                    // segment test called it clear of the turn off the next road and the
                    // two met nose to nose, and the one turning round never backs out
                    // (DEPOT-004 S2 seed 102, a minute stood, 2 547 refusals).
                    else if (a.UTurn || b.UTurn) conflict = true;
                    else
                    {
                        // A car going straight through keeps to its line. One TURNING
                        // does not: the line is the rear axle's, and the body leans out
                        // of the bend the whole way round, further the wider the junction
                        // (a boulevard's turns are the longest, and they were the ones
                        // whose cars kept meeting). Where either way through the box is
                        // a turn, the pair wants a car's width more air between them.
                        bool turning = a.Kind != Turn.Straight || b.Kind != Turn.Straight;
                        conflict = Near(a, b, turning ? ConflictReachTurn : ConflictReach);
                    }
                    a.Conflicts[j] = conflict;
                    b.Conflicts[i] = conflict;
                }
        }

        /// <summary>Do these two lines through the box come within a car's width of one
        /// another? SEGMENT to segment, not point to point: two lines crossing at a
        /// shallow angle can slip between one another's sample points and be called
        /// clear, and then two cars are given the box at once and meet in the middle of
        /// it - which is what the belt was refusing three hundred times in one run.</summary>
        static bool Near(Connector a, Connector b, float reach)
        {
            float r2 = reach * reach;
            var pa = WithOverhang(a);
            var pb = WithOverhang(b);
            for (int i = 0; i + 1 < pa.Count; i++)
                for (int j = 0; j + 1 < pb.Count; j++)
                    if (SegmentGap(pa[i], pa[i + 1], pb[j], pb[j + 1]) < r2) return true;
            return false;
        }

        /// <summary>The line a car covers crossing the box, which is longer than the
        /// connector: the line is what the rear axle follows, and at the near end the
        /// body is still out on the approach while at the far end the nose is already
        /// down the road. Two cars scraping at the MOUTH of a junction - one turning
        /// through it, one just let off a red - are a pair whose connectors never come
        /// near each other, and the belt was refusing their steps for seconds at a time
        /// until the overhang was counted.</summary>
        static readonly List<Vector3> _spanA = new List<Vector3>(), _spanB = new List<Vector3>();
        static bool _spanFlip;

        static List<Vector3> WithOverhang(Connector c)
        {
            var into = _spanFlip ? _spanB : _spanA;
            _spanFlip = !_spanFlip;
            into.Clear();
            int n = c.Pts.Length;
            if (n == 0) return into;
            var head = n > 1 ? (c.Pts[1] - c.Pts[0]) : Vector3.forward;
            var tail = n > 1 ? (c.Pts[n - 1] - c.Pts[n - 2]) : Vector3.forward;
            head.y = tail.y = 0f;
            if (head.sqrMagnitude > 1e-6f) head.Normalize();
            if (tail.sqrMagnitude > 1e-6f) tail.Normalize();
            into.Add(c.Pts[0] - head * BodyOverhang);
            for (int i = 0; i < n; i++) into.Add(c.Pts[i]);
            into.Add(c.Pts[n - 1] + tail * BodyOverhang);
            return into;
        }

        /// <summary>The square of the closest approach of two segments, flat (y ignored).</summary>
        static float SegmentGap(Vector3 p1, Vector3 p2, Vector3 q1, Vector3 q2)
        {
            p1.y = p2.y = q1.y = q2.y = 0f;
            var u = p2 - p1;
            var v = q2 - q1;
            var w = p1 - q1;
            float a = Vector3.Dot(u, u), b = Vector3.Dot(u, v), c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w), e = Vector3.Dot(v, w);
            float det = a * c - b * b;
            float s, t;
            if (det < 1e-6f)   // near parallel: take the ends
            {
                s = 0f;
                t = c > 1e-6f ? Mathf.Clamp01(e / c) : 0f;
            }
            else
            {
                s = Mathf.Clamp01((b * e - c * d) / det);
                t = Mathf.Clamp01((a * e - b * d) / det);
            }
            var gap = w + u * s - v * t;
            return gap.sqrMagnitude;
        }

        // ------------------------------------------------------------ where am I

        /// <summary>The carriageway nearest this point (within <paramref name="within"/>
        /// metres of its surface), with (s, d) on it; null when off the road.</summary>
        public Carriageway Locate(Vector3 p, out float s, out float d, float within = 3f)
        {
            Carriageway best = null;
            float bestCost = float.MaxValue;
            s = d = 0f;
            foreach (var r in Roads)
            {
                r.Project(p, out float rs, out float rd);
                float along = rs < 0f ? -rs : rs > r.Length ? rs - r.Length : 0f;
                float across = Mathf.Max(0f, Mathf.Abs(rd) - r.HalfRoad);
                float cost = along * along + across * across;
                if (along > within || across > within) continue;
                if (cost < bestCost) { bestCost = cost; best = r; s = Mathf.Clamp(rs, 0f, r.Length); d = rd; }
            }
            return best;
        }

        /// <summary>The lane nearest this point, and the progress along it.</summary>
        public RoadEdge NearestLane(Vector3 p, out float progress, float minLength = 0f)
        {
            RoadEdge best = null;
            float bestD = float.MaxValue;
            progress = 0f;
            foreach (var e in Edges)
            {
                if (e.Length < minLength) continue;
                float s = Mathf.Clamp(Vector3.Dot(p - e.Start, e.Dir), 0f, e.Length);
                var q = e.Start + e.Dir * s;
                q.y = p.y;
                float d = (q - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e; progress = s; }
            }
            return best;
        }

        // ------------------------------------------------------------ routing

        /// <summary>Shortest routes (by metres) from every lane to the target lane, as
        /// "the next lane after this one": follow it hop by hop. Turns at dead ends
        /// are allowed (those connectors exist); U-turns through live junctions are
        /// not. The target maps to its shortest loop back to itself.</summary>
        public static Dictionary<RoadEdge, RoadEdge> RouteToward(List<RoadEdge> edges, RoadEdge target) =>
            RouteToward(edges, target, out _);

        /// <summary>The same routes, and the METRES each lane is from the target - the
        /// search's own working, which it used to throw away.
        ///
        /// A driver wants it to answer one question the table cannot: whether the way
        /// he is pointing is the short way. The table is a graph of ONE-WAY lanes and a
        /// U-turn in the middle of a street is not an edge of it (only the dead-end
        /// turn-round is a connector), so a car sent to a mark behind it is routed the
        /// only way the graph knows - forward, round the block. Given the distance from
        /// his own lane AND from the one facing the other way, the driver can see that
        /// for himself and turn round instead (RoadCar.Replan).
        ///
        /// <paramref name="dist"/> is measured from the START of each lane, which is
        /// what the search is built on; a car part way down one subtracts its own
        /// progress.</summary>
        public static Dictionary<RoadEdge, RoadEdge> RouteToward(
            List<RoadEdge> edges, RoadEdge target, out Dictionary<RoadEdge, float> dist)
            => RouteToward(edges, target, out dist, null);

        /// <summary>The speed a route is costed at on a road with no lower limit, and
        /// the street speed the cost is expressed in. A lane's weight is its length
        /// scaled by how much SLOWER than a street it is - so a street still weighs its
        /// own metres (every number tuned against this search still means what it did)
        /// and a motorway deck weighs four tenths of them. That, and nothing else, is
        /// what makes a driver take the long way round on the fast road: measured in
        /// metres the freeway was always the longer way and nobody ever chose it.</summary>
        const float RouteRefSpeed = 10f, RouteSpeedCap = 25f;

        static float RouteWeight(RoadEdge e)
            => e.Length * (RouteRefSpeed / Mathf.Clamp(e.SpeedLimit, 3f, RouteSpeedCap));

        /// <summary>What changing lane costs the search: a couple of seconds' worth of
        /// street, so a route that needs one is taken when it is plainly better and not
        /// for a metre.</summary>
        const float LaneShiftCost = 25f;

        /// <summary>The routes, and - where the way on is in ANOTHER LANE of the road
        /// the car is already on - which lane it has to be in. A motorway exit is not
        /// reached from the through lane at all: it is taken from the deceleration lane,
        /// and a driver who does not move over never leaves the deck. The search knows
        /// that because a lane change is one of its own edges.</summary>
        public static Dictionary<RoadEdge, RoadEdge> RouteToward(
            List<RoadEdge> edges, RoadEdge target, out Dictionary<RoadEdge, float> dist,
            Dictionary<RoadEdge, RoadEdge> shift)
        {
            dist = new Dictionary<RoadEdge, float> { [target] = 0f };
            shift?.Clear();
            // Dijkstra backwards over the turn graph. The open set is a heap keyed on
            // distance with ties broken by arrival, which is exactly the lane the
            // linear scan this replaced would have picked, so every route comes out
            // the same as it did - the scan was a quadratic walk on a city's worth of
            // lanes, once per patrol leg and per errand.
            var open = new RouteOpenSet();
            open.Push(target, 0f);
            var closed = new HashSet<RoadEdge>();
            while (open.Count > 0)
            {
                var f = open.Pop(out float df);
                if (!closed.Add(f)) continue;
                // the lane beside it: a car in that one reaches this one by moving over
                var road = f.Road;
                if (road != null)
                    for (int i = 0; i < road.Lanes.Count; i++)
                    {
                        var sib = road.Lanes[i];
                        if (sib == f || sib.Heading != f.Heading || closed.Contains(sib)) continue;
                        float nd = df + LaneShiftCost;
                        if (dist.TryGetValue(sib, out float had) && had <= nd) continue;
                        dist[sib] = nd;
                        if (shift != null) shift[sib] = f;
                        open.Push(sib, nd);
                    }
                if (f.From == null) continue;
                foreach (var e in f.From.Incoming)
                {
                    if (f.From.ConnectorFor(e, f) == null || closed.Contains(e)) continue;
                    float nd = df + RouteWeight(e);
                    if (dist.TryGetValue(e, out float old) && old <= nd) continue;
                    dist[e] = nd;
                    shift?.Remove(e);          // it is reached by driving on, not by moving over
                    open.Push(e, nd);
                }
            }
            var next = new Dictionary<RoadEdge, RoadEdge>();
            foreach (var e in edges)
            {
                if (e.To == null) continue;
                RoadEdge best = null;
                float bestD = float.MaxValue;
                foreach (var f in e.To.Outgoing)
                {
                    if (e.To.ConnectorFor(e, f) == null) continue;
                    if (dist.TryGetValue(f, out float d) && d < bestD) { bestD = d; best = f; }
                }
                if (best != null) next[e] = best;
            }
            return next;
        }

        /// <summary>The route search's open set: a binary heap of lanes keyed on the
        /// distance found so far, ties broken by the order the lanes first entered it.
        /// That tie-break is the point - it is the lane a first-strictly-smaller scan
        /// of an insertion-ordered list picks, so the search is deterministic and picks
        /// what it always did where two ways cost the same. A lane already in the set
        /// is moved up in place when a shorter way to it is found (it keeps its place
        /// in the arrival order, as it kept its slot in the list).</summary>
        sealed class RouteOpenSet
        {
            struct Entry
            {
                public RoadEdge Edge;
                public float Dist;
                public int Seq;
            }

            readonly List<Entry> _heap = new List<Entry>();
            readonly Dictionary<RoadEdge, int> _at = new Dictionary<RoadEdge, int>();
            int _seq;

            public int Count => _heap.Count;

            public void Push(RoadEdge edge, float dist)
            {
                if (_at.TryGetValue(edge, out int i))
                {
                    var held = _heap[i];
                    if (dist >= held.Dist) return;
                    held.Dist = dist;
                    _heap[i] = held;
                    Up(i);
                    return;
                }
                _heap.Add(new Entry { Edge = edge, Dist = dist, Seq = _seq++ });
                _at[edge] = _heap.Count - 1;
                Up(_heap.Count - 1);
            }

            public RoadEdge Pop(out float dist)
            {
                var top = _heap[0];
                int last = _heap.Count - 1;
                _at.Remove(top.Edge);
                if (last > 0)
                {
                    _heap[0] = _heap[last];
                    _at[_heap[0].Edge] = 0;
                }
                _heap.RemoveAt(last);
                if (_heap.Count > 1) Down(0);
                dist = top.Dist;
                return top.Edge;
            }

            static bool Before(Entry a, Entry b) => a.Dist < b.Dist || (a.Dist == b.Dist && a.Seq < b.Seq);

            void Up(int i)
            {
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (!Before(_heap[i], _heap[parent])) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            void Down(int i)
            {
                int n = _heap.Count;
                while (true)
                {
                    int left = 2 * i + 1, right = left + 1, least = i;
                    if (left < n && Before(_heap[left], _heap[least])) least = left;
                    if (right < n && Before(_heap[right], _heap[least])) least = right;
                    if (least == i) break;
                    Swap(i, least);
                    i = least;
                }
            }

            void Swap(int a, int b)
            {
                var t = _heap[a];
                _heap[a] = _heap[b];
                _heap[b] = t;
                _at[_heap[a].Edge] = a;
                _at[_heap[b].Edge] = b;
            }
        }

        public Dictionary<RoadEdge, RoadEdge> RouteToward(RoadEdge target) => RouteToward(Edges, target);

        public Dictionary<RoadEdge, RoadEdge> RouteToward(RoadEdge target, out Dictionary<RoadEdge, float> dist) =>
            RouteToward(Edges, target, out dist, null);

        /// <summary>The routes to a lane and, for each lane, which lane of its own road
        /// a car has to move into to follow them.</summary>
        public Dictionary<RoadEdge, RoadEdge> RouteToward(RoadEdge target, Dictionary<RoadEdge, RoadEdge> shift) =>
            RouteToward(Edges, target, out _, shift);

        // ------------------------------------------------------------ statics

        /// <summary>Something stood on the road that never moves (a parked prop car):
        /// entered in its carriageway's list so the traffic passes it like anything
        /// else at the kerb. Returns the occupant, or null off any road.</summary>
        public RoadOccupant AddStatic(IRoadUser who)
        {
            var road = Locate(who.RoadPosition, out float s, out float d, within: 2f);
            if (road == null) return null;
            var o = new RoadOccupant { Who = who, Road = road, Priority = 0, Heading = 0, Parked = true };
            var f = who.RoadForward;
            float along = Mathf.Abs(Vector3.Dot(f, road.Axis)) * who.HalfLength + Mathf.Abs(Vector3.Dot(f, road.Right)) * who.HalfWidth;
            float across = Mathf.Abs(Vector3.Dot(f, road.Right)) * who.HalfLength + Mathf.Abs(Vector3.Dot(f, road.Axis)) * who.HalfWidth;
            o.BodyS0 = o.S0 = s - along;
            o.BodyS1 = o.S1 = s + along;
            o.BodyD0 = o.D0 = d - across;
            o.BodyD1 = o.D1 = d + across;
            o.Heading = Vector3.Dot(f, road.Axis) >= 0f ? 1 : -1;
            // at the kerb, out of every lane's band: passed, not queued for; in a lane: a wreck
            o.Parked = true;
            foreach (var l in road.Lanes)
                if (o.BodyD0 < l.Offset + 1.25f && o.BodyD1 > l.Offset - 1.25f) { o.Parked = false; break; }
            road.Occupants.Add(o);
            return o;
        }

        public void Remove(RoadOccupant o)
        {
            if (o?.Road != null) o.Road.Occupants.Remove(o);
        }
    }

    /// <summary>A body stood on the road that is not driven - the kerb's parked
    /// props, a wreck: what the traffic and the men on foot reckon with like any
    /// other car, and the belt (RoadSpace) keeps everyone out of.</summary>
    public sealed class StaticRoadUser : IRoadUser
    {
        public Vector3 Position, Forward;
        public float HalfLen, HalfWide;
        public Vector3 RoadPosition => Position;
        public Vector3 RoadForward => Forward;
        public float RoadSpeed => 0f;
        public float HalfLength => HalfLen;
        public float HalfWidth => HalfWide;
    }
}
