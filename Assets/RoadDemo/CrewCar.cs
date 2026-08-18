using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The outfit's wheels on the demo's street: the car the ledger's armory lists
    // (a Sedan, a Coupe, a Panel Van - one body plays it), owned by whichever
    // lieutenant the book has assigned it to, and driven by his crew once they are
    // in it. Kinematic, no physics: a heading, a speed, a yaw rate capped by a
    // turning radius, and a short queue of points it steers for (pure pursuit) -
    // enough for a straight run down the street, a stop at the kerb, and the
    // drive-by's arc round at the end of a pass. Who is aboard, and who fires from
    // which window, is DemoCrews' business; this class moves the tin and keeps the
    // pass plan.
    public sealed class CrewCar : IRoadUser
    {
        public enum Mode { Parked, Driving, DriveBy }

        public Transform Tf;

        /// <summary>The ledger equipment item this car stands for; -1 until the first
        /// deal binds it to the roster's first vehicle.</summary>
        public int ItemId = -1;
        public string DisplayName = "Car";

        /// <summary>The crew whose lieutenant OWNS the item per the ledger - only that
        /// crew may board. Re-read on every deal; null means nobody's car.</summary>
        public DemoCrews.Unit Owner;

        /// <summary>The crew inside it, or null.</summary>
        public DemoCrews.Unit Occupant;

        /// <summary>The men in it - hidden bodies riding the car's transform.</summary>
        public readonly HashSet<CrewWalker> Aboard = new HashSet<CrewWalker>();

        /// <summary>Which seat each man has - dealt when he is sent to the car, so he
        /// walks to that seat's door and rides in that seat.</summary>
        public readonly Dictionary<CrewWalker, int> SeatOf = new Dictionary<CrewWalker, int>();

        /// <summary>The lowest seat nobody has been given, or -1 when the car is full.</summary>
        public int FreeSeat()
        {
            for (int s = 0; s < Seats; s++)
            {
                bool taken = false;
                foreach (var kv in SeatOf) if (kv.Value == s) { taken = true; break; }
                if (!taken) return s;
            }
            return -1;
        }

        public Mode State { get; private set; } = Mode.Parked;
        public float Speed { get; private set; }

        /// <summary>The crew being shot up on a drive-by, or null.</summary>
        public DemoCrews.Unit DriveByTarget { get; private set; }

        /// <summary>The road surface the car sits on.</summary>
        public float RoadY;

        /// <summary>The street the car keeps to for its passes: its centre line's z,
        /// running along world X. NaN when the scene has no street - passes then run
        /// straight through the target's position and back.</summary>
        public float StreetZ = float.NaN;

        const float MaxSpeed = 11f, PassSpeed = 8.5f, Accel = 5f, Brake = 8f;
        const float TurnRadius = 4.5f;
        const float TurnLock = 2.2f;        // full lock on the U-turn arc, at the crawl
        const float TurnSpeed = 3.5f;
        const float LaneOffset = 2.5f;      // metres off the centre line, either lane
        const float PassOvershoot = 14f;    // metres past the target before the turn
        const float ArriveWithin = 2.2f;

        readonly List<Vector3> _route = new List<Vector3>();
        float _passDir = 1f;                // +1 heading +X on the current pass

        public Vector3 Position => Tf ? Tf.position : Vector3.zero;
        public Vector3 Forward => Tf ? Tf.forward : Vector3.forward;
        public bool Moving => State != Mode.Parked || Speed > 0.05f;

        // ------------------------------------------------------------------ seats and doors

        /// <summary>How many men it carries. The van seats six, a car four; the
        /// builder says which. Whoever finds no seat stays on the pavement.</summary>
        public int Seats = 4;
        public int FreeSeats => Mathf.Max(0, Seats - Aboard.Count);

        // Seats, left-hand drive: 0 is the driver's (front left), 1 the front
        // passenger's, 2 and 3 the back seat, 4 and 5 a van's third row. In the car's
        // own frame; y is where a seated man's ROOT goes (the sit clip carries his
        // pelvis 0.43 above it), which puts him on the cushion.
        static readonly Vector3[] SeatLocal =
        {
            new Vector3(-0.42f, 0.10f, 0.45f), new Vector3(0.42f, 0.10f, 0.45f),
            new Vector3(-0.42f, 0.10f, -0.55f), new Vector3(0.42f, 0.10f, -0.55f),
            new Vector3(-0.42f, 0.10f, -1.45f), new Vector3(0.42f, 0.10f, -1.45f),
        };

        sealed class Door
        {
            public Transform Tf;
            public Quaternion Closed;
            public float Sign;   // which way round the hinge is "open"
            public float Side;   // +1 right flank, -1 left flank, 0 front/back (a van's rear pair, a boot)
            public int Seat;     // the seat it serves: 0..3 for fl/fr/rl/rr, -1 for a back door, -2 for a boot
            public float Open;   // 0 shut .. 1 wide, this door
            public bool Wanted;
        }

        readonly List<Door> _doors = new List<Door>();
        const float DoorSwing = 70f, DoorSeconds = 0.55f;

        public bool HasDoors => _doors.Count > 0;

        /// <summary>The door a man in this seat uses: his own (fl/fr/rl/rr by name);
        /// failing that a back door (the van - everyone in through the back); failing
        /// that nothing (a body without door parts). Never the boot.</summary>
        Door DoorFor(int seat)
        {
            foreach (var d in _doors) if (d.Seat == seat) return d;
            foreach (var d in _doors) if (d.Seat == -1) return d;
            return null;
        }

        /// <summary>Ask the door for this seat to swing open (the driver's for the
        /// driver, the back doors for a van), and shut again with CloseDoorFor.</summary>
        public void OpenDoorFor(int seat)
        {
            var d = DoorFor(seat);
            if (d == null) return;
            // a van's back pair opens together
            if (d.Seat == -1) { foreach (var b in _doors) if (b.Seat == -1) b.Wanted = true; }
            else d.Wanted = true;
        }

        public void CloseDoorFor(int seat)
        {
            var d = DoorFor(seat);
            if (d == null) return;
            if (d.Seat == -1) { foreach (var b in _doors) if (b.Seat == -1) b.Wanted = false; }
            else d.Wanted = false;
        }

        public void CloseAllDoors()
        {
            foreach (var d in _doors) d.Wanted = false;
        }

        /// <summary>Is the door for this seat open enough to get through? A body with
        /// no door for the seat is always "open" - there is nothing to wait for.</summary>
        public bool DoorOpenFor(int seat)
        {
            var d = DoorFor(seat);
            return d == null || d.Open >= 0.85f;
        }

        /// <summary>Finds the body's door parts - any child named "...Door..." (the
        /// pack's convention: Door_FL, Door_r, Door_Rear) - which seat each serves
        /// (f/r front-rear, l/r left-right in the last name token; a plain "l"/"r" is a
        /// van's back door; "Rear" or "Boot" is the boot and never opens for a man),
        /// and which way each swings out: the way that carries the door's own middle
        /// away from the car. A door's glass the pack left as a sibling is put under
        /// the door so it swings with it.</summary>
        public void FindDoors()
        {
            _doors.Clear();
            if (Tf == null) return;
            var all = Tf.GetComponentsInChildren<Transform>(true);
            var carCentre = Position + Vector3.up * 0.8f;
            foreach (var t in all)
            {
                if (t == Tf) continue;
                string n = t.name;
                if (n.IndexOf("Door", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // its glass, if it stands beside it rather than under it
                foreach (var g in all)
                    if (g != t && g.name.StartsWith(n) && g.name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0
                        && !g.IsChildOf(t))
                        g.SetParent(t, true);

                var renderers = t.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                var arm = b.center - t.position;
                arm.y = 0f;
                // try a small swing each way; the one that pushes the door's middle
                // out from the car is the way it opens
                var plus = Quaternion.AngleAxis(20f, Vector3.up) * arm;
                var minus = Quaternion.AngleAxis(-20f, Vector3.up) * arm;
                float dPlus = (t.position + plus - carCentre).sqrMagnitude;
                float dMinus = (t.position + minus - carCentre).sqrMagnitude;
                float flank = Vector3.Dot(b.center - Position, Tf.right);

                // which seat, by the pack's naming
                int cut = n.LastIndexOf('_');
                string token = (cut >= 0 ? n.Substring(cut + 1) : n).ToLowerInvariant();
                int seat;
                if (token.Contains("rear") || token.Contains("boot") || token.Contains("trunk") || token.Contains("back"))
                    seat = -2;
                else if (token.Length <= 1)
                    seat = -1; // "l" / "r": a van's back door
                else
                {
                    bool front = token.Contains("f");
                    bool left = token.Contains("l");
                    seat = (front ? 0 : 2) + (left ? 0 : 1);
                    if (Mathf.Abs(flank) < 0.35f) seat = -1; // flankless: a back door whatever the name
                }

                _doors.Add(new Door
                {
                    Tf = t, Closed = t.localRotation, Sign = dPlus >= dMinus ? 1f : -1f,
                    Side = flank > 0.35f ? 1f : flank < -0.35f ? -1f : 0f,
                    Seat = seat,
                });
            }
        }

        void TickDoors(float dt)
        {
            foreach (var d in _doors)
            {
                float want = d.Wanted ? 1f : 0f;
                if (Mathf.Approximately(d.Open, want)) continue;
                d.Open = Mathf.MoveTowards(d.Open, want, dt / DoorSeconds);
                // eased: doors swing out fast and settle
                float eased = Mathf.SmoothStep(0f, 1f, d.Open);
                if (d.Tf) d.Tf.localRotation = d.Closed * Quaternion.Euler(0f, d.Sign * DoorSwing * eased, 0f);
            }
        }

        // ------------------------------------------------------------------ the kerb

        /// <summary>Metres off the centre line the car parks: its flank a hand off the
        /// kerb (the kerb stands 5 m off the crown), whatever the body's width.</summary>
        float KerbOffset => 5f - HalfWidth + 0.38f; // the mirror over the kerb; the flank against the stone

        /// <summary>Half the body's width, off its bounds - the low car is narrower than the van.</summary>
        public float HalfWidth { get; private set; } = 0.95f;

        /// <summary>Pull in at the kerb nearest this point and stop there, nose along the
        /// street the way that side's traffic runs - never on the crown of the road.
        /// Off any street the point itself is the stop.</summary>
        public void ParkNear(Vector3 point)
        {
            DriveByTarget = null;
            _route.Clear();
            _turning = false;
            _arcLeft = 0;
            if (float.IsNaN(StreetZ))
            {
                point.y = RoadY;
                _route.Add(point);
                State = Mode.Driving;
                return;
            }
            // the way to go is the way to the point; right-hand traffic puts the car in
            // the lane on that side and, at the end, against that side's kerb
            var here = Position;
            float dir = point.x >= here.x ? 1f : -1f;
            if (Mathf.Abs(point.x - here.x) < 6f) dir = Forward.x >= 0f ? 1f : -1f; // right beside: carry on this way
            float laneZ = StreetZ - dir * LaneOffset;
            float kerbZ = StreetZ - dir * KerbOffset;
            // facing the wrong way: round first, inside the road, then down the lane
            if (Vector3.Dot(Forward, Vector3.right * dir) < 0f)
                AddUTurn(Forward.x >= 0f ? 1f : -1f);
            // down the middle of the lane to just short of the point...
            float far = Mathf.Max(0f, (point.x - here.x) * dir);
            if (far > 14f)
            {
                if (Mathf.Abs(here.z - laneZ) > 0.8f) _route.Add(new Vector3(here.x + dir * 8f, RoadY, laneZ));
                _route.Add(new Vector3(point.x - dir * 12f, RoadY, laneZ));
            }
            // ...then a slant in to the kerb, and to a stop against it
            _route.Add(new Vector3(point.x - dir * 4f, RoadY, kerbZ));
            _route.Add(new Vector3(point.x + dir * 2f, RoadY, kerbZ));
            State = Mode.Driving;
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Drive toward this point - and pull in at the kerb nearest it. A
        /// car of the outfit's does not stop in the middle of the road.</summary>
        public void DriveTo(Vector3 point) => ParkNear(point);

        /// <summary>Shoot the place up: passes along the street past this crew, a
        /// turn at the end of each, until told otherwise or nobody is left.</summary>
        public void DriveBy(DemoCrews.Unit target)
        {
            if (target == null) return;
            DriveByTarget = target;
            State = Mode.DriveBy;
            _turning = false;
            _arcLeft = 0;
            PlanPass(first: true);
        }

        /// <summary>Coast to a stop where it is (the pass is over, the crew is
        /// getting out, or the player changed his mind).</summary>
        public void Stop()
        {
            _route.Clear();
            DriveByTarget = null;
            State = Mode.Parked;
        }

        // ------------------------------------------------------------------ passes

        // A pass runs down the near lane past the target and PassOvershoot beyond;
        // the turn is two more points - across into the far lane just ahead, then
        // far back down it - and pure pursuit with the yaw rate capped draws the arc
        // between them. The next pass is planned when the last point of this one is
        // reached, in the other direction, so the loop is pass, turn, pass, turn...
        void PlanPass(bool first)
        {
            _route.Clear();
            var t = DriveByTarget.Position;
            var here = Position;

            if (float.IsNaN(StreetZ))
            {
                // no street: run straight through, past, and come round
                var line = t - here;
                line.y = 0f;
                if (line.sqrMagnitude < 1f) line = Forward;
                var dir = line.normalized;
                var side = Vector3.Cross(Vector3.up, dir);
                _route.Add(At(t + dir * PassOvershoot));
                _route.Add(At(t + dir * (PassOvershoot + 6f) + side * 5f));
                _route.Add(At(here - dir * 6f + side * 5f));
                return;
            }

            if (first)
                _passDir = t.x >= here.x ? 1f : -1f;
            float lane = StreetZ - _passDir * LaneOffset;      // right-hand traffic: near lane heading +X is south
            // facing the wrong way for the first pass: round inside the road first,
            // rather than the wide loop pure pursuit would draw over the pavement
            if (first && Vector3.Dot(Forward, Vector3.right * _passDir) < 0f)
                AddUTurn(Forward.x >= 0f ? 1f : -1f);

            // first: get into the lane a little ahead if we are off it
            if (Mathf.Abs(here.z - lane) > 1.2f)
                _route.Add(At(new Vector3(here.x + _passDir * 8f, 0f, lane)));
            // the pass itself, past the target; the turn is drawn when this is reached
            _route.Add(At(new Vector3(t.x + _passDir * PassOvershoot, 0f, lane)));
            _turned = false;
        }

        bool _turned;

        /// <summary>The U-turn, drawn as a half circle INSIDE the carriageway: about the
        /// centre line, from where the car is to the mirror point across it, bulging
        /// forward - so the car swings round between the kerbs and never up the pavement
        /// through the palms. Six points; pure pursuit at the turn's crawl follows them.</summary>
        void AddUTurn(float dir)
        {
            var here = Position;
            float cz = float.IsNaN(StreetZ) ? here.z : StreetZ;
            float r = Mathf.Clamp(Mathf.Abs(here.z - cz), 2.2f, 4.2f);
            float side = here.z >= cz ? 1f : -1f;
            for (int i = 1; i <= 6; i++)
            {
                float t = i / 6f;
                float x = here.x + dir * r * Mathf.Sin(Mathf.PI * t);
                float z = cz + side * r * Mathf.Cos(Mathf.PI * t);
                _route.Add(At(new Vector3(x, 0f, z)));
            }
            _turning = true;
            _arcLeft = 6;
        }

        bool _turning; // on the arc: crawl, and the tight lock
        int _arcLeft;  // arc points still to reach before the pace comes back

        Vector3 At(Vector3 p) => new Vector3(p.x, RoadY, p.z);

        // ------------------------------------------------------------------ frame

        public void Tick(float dt)
        {
            if (Tf == null) return;
            TickDoors(dt);

            if (_route.Count == 0)
            {
                // nowhere to go: brake to a halt
                if (State == Mode.DriveBy && DriveByTarget != null && !DriveByTarget.Wiped)
                {
                    // the pass ended: swing round inside the road, then the next pass back
                    if (!_turned)
                    {
                        AddUTurn(_passDir);
                        _turned = true;
                    }
                    else
                    {
                        _turning = false;
                        _passDir = -_passDir;
                        PlanPass(first: false);
                    }
                }
                else if (State == Mode.DriveBy)
                {
                    // the job is done: pull in at the kerb on this side, out of the road
                    ParkNear(Position);
                }
                else
                {
                    if (State != Mode.Parked) State = Mode.Parked;
                    Speed = Mathf.MoveTowards(Speed, 0f, Brake * dt);
                    if (Speed > 0.01f) Tf.position += Tf.forward * Speed * dt;
                    Wheels(dt, 0f);
                    // settle straight along the kerb as it stops
                    if (!float.IsNaN(StreetZ) && Speed < 2f)
                    {
                        float dir = Forward.x >= 0f ? 1f : -1f;
                        Tf.rotation = Quaternion.RotateTowards(Tf.rotation,
                            Quaternion.LookRotation(Vector3.right * dir, Vector3.up), 60f * dt);
                    }
                    return;
                }
            }

            var goal = _route[0];
            var to = goal - Tf.position;
            to.y = 0f;
            float dist = to.magnitude;
            bool last = _route.Count == 1;
            // the last point of a parking run is hit close, so the car sits where it was told
            float arrive = last && State == Mode.Driving ? 0.9f : ArriveWithin;
            if (dist < arrive)
            {
                _route.RemoveAt(0);
                if (_arcLeft > 0 && --_arcLeft == 0) _turning = false;
                if (_route.Count == 0 && State == Mode.Driving) State = Mode.Parked;
                return;
            }

            // about to swing across the road: wait for a gap in what is coming the
            // other way (and behind on this side) before pulling out
            if (_arcLeft == 6 && !GapForTurn())
            {
                Speed = Mathf.MoveTowards(Speed, 0f, Brake * dt);
                if (Speed > 0.01f) Tf.position += Tf.forward * Speed * dt;
                Wheels(dt, 0f);
                return;
            }

            // speed: full down the road, easier on a pass, a crawl round the turn, and
            // braking for the last point - and never into the back of the car ahead
            float want = _turning ? TurnSpeed : State == Mode.DriveBy ? PassSpeed : MaxSpeed;
            if (last && State != Mode.DriveBy)
                want = Mathf.Min(want, Mathf.Sqrt(2f * Brake * Mathf.Max(0f, dist - 0.6f)));
            float clear = ClearAhead();
            if (clear < FollowGap) want = 0f;
            else if (clear < FollowGap + 12f) want = Mathf.Min(want, 1.5f + (clear - FollowGap) / 12f * MaxSpeed);
            Speed = Speed < want ? Mathf.MoveTowards(Speed, want, Accel * dt)
                                 : Mathf.MoveTowards(Speed, want, Brake * dt);

            // heading: turn toward the goal, no tighter than the turning radius allows
            float steer = 0f;
            if (dist > 1e-3f && Speed > 0.05f)
            {
                var wantRot = Quaternion.LookRotation(to / dist, Vector3.up);
                float maxTurn = Mathf.Rad2Deg * (Speed / (_turning ? TurnLock : TurnRadius)) * dt;
                Tf.rotation = Quaternion.RotateTowards(Tf.rotation, wantRot, maxTurn);
                steer = Vector3.SignedAngle(Tf.forward, to / dist, Vector3.up);
            }
            var pos = Tf.position + Tf.forward * Speed * dt;
            pos.y = RoadY;
            // never a wheel on the pavement: whatever the steering did, the body stays
            // between the kerbs
            if (!float.IsNaN(StreetZ))
                pos.z = Mathf.Clamp(pos.z, StreetZ - (5f - HalfWidth), StreetZ + (5f - HalfWidth));
            Tf.position = pos;
            Wheels(dt, steer);
        }

        // ------------------------------------------------------------------ the road

        const float FollowGap = 6f;

        public Vector3 RoadPosition => Position;
        public Vector3 RoadForward => Forward;
        public float RoadSpeed => Speed;
        public float HalfLength { get; private set; } = 2.3f;

        /// <summary>Metres of clear road to the nearest other road user ahead of the
        /// nose and near the car's line - what the throttle answers to.</summary>
        float ClearAhead()
        {
            float best = float.MaxValue;
            var p = Position;
            var f = Forward;
            var right = Tf.right;
            // a man on foot in the way - ours or theirs - is braked for, not driven through
            foreach (var b in StreetTraffic.Bodies)
            {
                var d = b - p;
                d.y = 0f;
                float ahead = Vector3.Dot(d, f);
                if (ahead <= 0f || Mathf.Abs(Vector3.Dot(d, right)) > 1.6f) continue;
                float gap = ahead - HalfLength - 0.5f;
                if (gap < best) best = gap;
            }
            foreach (var u in StreetTraffic.Users)
            {
                if (ReferenceEquals(u, this)) continue;
                var d = u.RoadPosition - p;
                d.y = 0f;
                float ahead = Vector3.Dot(d, f);
                float side = Mathf.Abs(Vector3.Dot(d, right));
                if (ahead <= 0f || side > 2.2f) continue;
                // going the other way in the next lane is not in my way
                if (Vector3.Dot(u.RoadForward, f) < -0.5f && u.RoadSpeed > 0.5f) continue;
                float gap = ahead - u.HalfLength - HalfLength;
                if (gap < best) best = gap;
            }
            return best;
        }

        /// <summary>Is the road clear enough either way to swing across it: nobody in
        /// the far lane within a long stone's throw of the turn either side, nobody
        /// close behind on this side about to run into the arc.</summary>
        bool GapForTurn()
        {
            if (float.IsNaN(StreetZ)) return true;
            var p = Position;
            foreach (var u in StreetTraffic.Users)
            {
                if (ReferenceEquals(u, this)) continue;
                var q = u.RoadPosition;
                float dx = Mathf.Abs(q.x - p.x);
                bool farLane = Mathf.Sign(q.z - StreetZ) != Mathf.Sign(p.z - StreetZ);
                bool stopped = u.RoadSpeed < 0.5f;
                // anything standing where the arc goes - a car stopped dead in the far
                // lane, right beside - blocks the turn as surely as one coming
                if (stopped) { if (dx < 9f) return false; continue; }
                float closing = Vector3.Dot(u.RoadForward, (p - q).normalized) > 0f ? 1f : 0f;
                if (farLane && dx < 30f + closing * 20f) return false;
                if (!farLane && dx < 14f && closing > 0f) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ wheels

        readonly List<Transform> _wheels = new List<Transform>();
        readonly List<Quaternion> _wheelRest = new List<Quaternion>();
        readonly List<bool> _wheelFront = new List<bool>();
        float _wheelRadius = 0.33f, _spin, _steerShown;

        /// <summary>The wheel parts of the body, and which are the steered pair: the
        /// pack names them Wheel_fl / Wheel_LF and so on - an f in the last token is a
        /// front wheel. Called once by the arena after the body is set.</summary>
        public void FindWheels()
        {
            _wheels.Clear(); _wheelRest.Clear(); _wheelFront.Clear();
            if (Tf == null) return;
            foreach (var t in Tf.GetComponentsInChildren<Transform>(true))
            {
                if (t == Tf || t.name.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (t.name.IndexOf("Steering", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                int cut = t.name.LastIndexOf('_');
                string token = cut >= 0 ? t.name.Substring(cut + 1) : t.name;
                _wheels.Add(t);
                _wheelRest.Add(t.localRotation);
                _wheelFront.Add(token.IndexOf('f') >= 0 || token.IndexOf('F') >= 0);
                var r = t.GetComponentInChildren<Renderer>();
                if (r) _wheelRadius = Mathf.Max(0.2f, r.bounds.extents.y);
            }
            var rs = Tf.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                HalfLength = Mathf.Max(1.5f, Vector3.Dot(b.extents, new Vector3(Mathf.Abs(Tf.forward.x), 0f, Mathf.Abs(Tf.forward.z))));
                HalfWidth = Mathf.Clamp(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(Tf.right.x), 0f, Mathf.Abs(Tf.right.z))), 0.7f, 1.3f);
            }
        }

        // Rolling with the road, the front pair turned into the corner.
        void Wheels(float dt, float steer)
        {
            if (_wheels.Count == 0) return;
            _spin = (_spin + Speed * dt / _wheelRadius * Mathf.Rad2Deg) % 360f;
            _steerShown = Mathf.MoveTowards(_steerShown, Mathf.Clamp(steer, -32f, 32f), 160f * dt);
            for (int i = 0; i < _wheels.Count; i++)
            {
                var w = _wheels[i];
                if (!w) continue;
                var turn = _wheelFront[i] ? Quaternion.AngleAxis(_steerShown, Vector3.up) : Quaternion.identity;
                w.localRotation = turn * _wheelRest[i] * Quaternion.AngleAxis(_spin, Vector3.right);
            }
        }


        // ------------------------------------------------------------------ doors and windows

        /// <summary>Where the man for this seat stands to get in or out: outside his
        /// own door - the driver on the driver's side, front left - a stride off the
        /// flank; the back door of a van behind it.</summary>
        public Vector3 DoorPoint(int seat)
        {
            var d = DoorFor(seat);
            if (d != null && d.Seat == -1)
                return Position - Tf.forward * (HalfLength + 1.2f) + Tf.right * (seat % 2 == 0 ? -0.5f : 0.5f);
            var s = SeatLocal[Mathf.Clamp(seat, 0, SeatLocal.Length - 1)];
            return Position + Tf.right * Mathf.Sign(s.x) * (HalfWidth + 0.9f) + Tf.forward * s.z;
        }

        /// <summary>The window a man fires from: the car's flank facing the target,
        /// at head height, staggered front to back per man.</summary>
        public Vector3 Window(int index, Vector3 target)
        {
            var toTarget = target - Position;
            float side = Vector3.Dot(toTarget, Tf.right) >= 0f ? 1f : -1f;
            float along = 0.9f - (index % 3) * 0.9f;
            return Position + Tf.right * side * 0.95f + Tf.forward * along + Vector3.up * 1.15f;
        }

        /// <summary>Where the rider in this seat sits: his root on the cushion, so a
        /// rival aiming at him aims into the car and the blood lands where the car is.</summary>
        public Vector3 Seat(int index) =>
            Tf.TransformPoint(SeatLocal[Mathf.Clamp(index, 0, SeatLocal.Length - 1)]);

        /// <summary>Which flank a seat is on: +1 right, -1 left.</summary>
        public static float SeatSide(int index) => index % 2 == 0 ? -1f : 1f;

        public string StatusLine => State switch
        {
            Mode.Driving => "On the road",
            Mode.DriveBy => DriveByTarget != null ? "Drive-by on " + DriveByTarget.GangName : "Drive-by",
            _ => Occupant != null ? "In the car" : "Parked",
        };
    }
}
