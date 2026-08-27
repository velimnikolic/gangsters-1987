using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // A car body, read off whatever pack prefab it is: how big it is, how many it
    // seats and where, which parts are its doors (and whose seat each door serves),
    // which parts are its wheels (and which pair steers). Nothing about how it moves -
    // that is CrewCar's - and nothing pack-specific beyond the pack's own naming
    // (Door_FL, Wheel_rr, ...): a sedan bought, a van stolen, a truck borrowed all
    // read the same way, so the outfit can own any of them.
    public sealed class CarBody
    {
        public readonly Transform Tf;

        /// <summary>Half the footprint along and across the car, off its renderers.</summary>
        public float HalfLength { get; private set; } = 2.3f;
        public float HalfWidth { get; private set; } = 0.95f;

        /// <summary>How many men it carries: a van or a limousine six, a truck cab three,
        /// a supercar two, a car four.</summary>
        public int Seats { get; set; } = 4;

        // Seats, left-hand drive: 0 is the driver's (front left), 1 the front
        // passenger's, 2 and 3 the back seat, 4 and 5 a van's third row. In the car's
        // own frame; y is where a seated man's ROOT goes (the sit clip carries his
        // pelvis 0.43 above it), which puts him on the cushion. The fallback for a
        // body with no steering wheel to measure from - see MeasureSeats.
        static readonly Vector3[] SeatLocal =
        {
            new Vector3(-0.42f, -0.10f, 0.10f), new Vector3(0.42f, -0.10f, 0.10f),
            new Vector3(-0.42f, -0.10f, -0.85f), new Vector3(0.42f, -0.10f, -0.85f),
            new Vector3(-0.42f, -0.10f, -1.75f), new Vector3(0.42f, -0.10f, -1.75f),
        };

        /// <summary>Metres the driver's root (under his hips) sits behind the middle of
        /// the steering wheel - his torso back off it, the wheel over his thighs.</summary>
        public static float RootBehindWheel = 0.60f;
        /// <summary>Metres between the back seat's roots and the front's.</summary>
        public static float RowPitch = 0.95f;
        /// <summary>A steering wheel higher than this off the car's origin is a tall
        /// cab (a van, a pickup) and the men are lifted by the difference.</summary>
        public static float WheelHeightOfCar = 0.95f;

        readonly Vector3[] _seats;

        public CarBody(Transform tf)
        {
            Tf = tf;
            Measure();
            string n = tf.name.ToLowerInvariant();
            Seats = n.Contains("van") || n.Contains("limo") ? 6 : n.Contains("truck") ? 3 : n.Contains("supercar") ? 2 : 4;
            _seats = MeasureSeats(tf);
            FindDoors();
            FindWheels();
        }

        static readonly HashSet<string> SeatsLogged = new HashSet<string>();

        /// <summary>Where the seats of this body are, in its own frame: the table above,
        /// moved to sit behind the body's own steering wheel - its part named
        /// "...Steering..." (SM_Veh_Car_Sedan_SteeringW, SM_Veh_Steering_Wheel_08) -
        /// so the driver is back off the wheel in every pack car alike, the front pair
        /// on the wheel's line across, the rows a RowPitch apart behind it, and a tall
        /// cab's men lifted with its wheel. A body without the part keeps the table.</summary>
        public static Vector3[] MeasureSeats(Transform car)
        {
            var seats = (Vector3[])SeatLocal.Clone();
            if (car == null) return seats;
            Renderer wheel = null;
            foreach (var t in car.GetComponentsInChildren<Transform>(true))
            {
                if (t == car || t.name.IndexOf("Steering", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                wheel = t.GetComponentInChildren<Renderer>();
                if (wheel != null) break;
            }
            if (wheel == null) return seats;
            // the part pivots at the car's origin (the pack's way), so its place is read
            // off its renderer's bounds - a wheel is small and round enough that the
            // box's middle is its middle whichever way the car stands - back into the
            // car's frame
            Vector3 c = car.InverseTransformPoint(wheel.bounds.center);

            float frontZ = c.z - RootBehindWheel;
            float lift = Mathf.Max(0f, c.y - WheelHeightOfCar);
            float across = Mathf.Abs(c.x) > 0.2f && Mathf.Abs(c.x) < 0.9f ? Mathf.Abs(c.x) : 0.42f;
            for (int i = 0; i < seats.Length; i++)
            {
                seats[i].x = (i % 2 == 0 ? -1f : 1f) * across;
                seats[i].y += lift;
                seats[i].z = frontZ - RowPitch * (i / 2);
            }
            if (SeatsLogged.Add(car.name))
                Debug.Log($"[RoadDemo] {car.name}: steering wheel at ({c.x:F2}, {c.y:F2}, {c.z:F2}); " +
                          $"driver's root at ({seats[0].x:F2}, {seats[0].y:F2}, {seats[0].z:F2})");
            return seats;
        }

        void Measure()
        {
            var rs = Tf.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var f = Tf.forward; f.y = 0f; f.Normalize();
            var r2 = Tf.right; r2.y = 0f; r2.Normalize();
            HalfLength = Mathf.Max(1.5f, Vector3.Dot(b.extents, new Vector3(Mathf.Abs(f.x), 0f, Mathf.Abs(f.z))));
            HalfWidth = Mathf.Clamp(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(r2.x), 0f, Mathf.Abs(r2.z))), 0.7f, 1.3f);
        }

        // ------------------------------------------------------------------ seats

        /// <summary>Where the rider in this seat sits (his root, on the cushion).</summary>
        public Vector3 Seat(int index) =>
            Tf.TransformPoint(_seats[Mathf.Clamp(index, 0, _seats.Length - 1)]);

        /// <summary>The same seat in the car's own frame.</summary>
        public Vector3 SeatLocalPoint(int index) =>
            _seats[Mathf.Clamp(index, 0, _seats.Length - 1)];

        /// <summary>Which flank a seat is on: +1 right, -1 left.</summary>
        public static float SeatSide(int index) => index % 2 == 0 ? -1f : 1f;

        /// <summary>Where the man for this seat stands to get in or out: outside his
        /// own door a stride off the flank; behind the back door of a van.</summary>
        public Vector3 DoorPoint(int seat)
        {
            var d = DoorFor(seat);
            if (d != null && d.Seat == -1)
                return Tf.position - Tf.forward * (HalfLength + 1.2f) + Tf.right * (seat % 2 == 0 ? -0.5f : 0.5f);
            var s = _seats[Mathf.Clamp(seat, 0, _seats.Length - 1)];
            return Tf.position + Tf.right * Mathf.Sign(s.x) * (HalfWidth + 0.9f) + Tf.forward * s.z;
        }

        /// <summary>A window on the flank facing the target, at head height, staggered
        /// front to back per man - where a shot leaves a car without a rider's own gun.</summary>
        public Vector3 Window(int index, Vector3 target)
        {
            var toTarget = target - Tf.position;
            float side = Vector3.Dot(toTarget, Tf.right) >= 0f ? 1f : -1f;
            float along = 0.9f - (index % 3) * 0.9f;
            return Tf.position + Tf.right * side * HalfWidth + Tf.forward * along + Vector3.up * 1.15f;
        }

        // ------------------------------------------------------------------ doors

        sealed class Door
        {
            public Transform Tf;
            public Quaternion Closed;
            public float Sign;   // which way round the hinge is "open"
            public float Side;   // +1 right flank, -1 left flank, 0 front/back (a van's rear pair, a boot)
            public int Seat;     // the seat it serves: 0..3 for fl/fr/rl/rr, -1 for a back door, -2 for a boot
            public float Open;   // 0 shut .. 1 wide
            public bool Wanted;
            // its window: the glass part(s) under it, where they sit shut, how tall,
            // and how far down they are wound (0 up .. 1 down)
            public readonly List<Transform> Glass = new List<Transform>();
            public readonly List<Vector3> GlassShut = new List<Vector3>();
            public float GlassHeight;
            public float Down;
            public bool WantDown;
        }

        readonly List<Door> _doors = new List<Door>();
        const float DoorSwing = 70f, DoorSeconds = 0.55f, WindowSeconds = 0.7f;

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
        /// driver, the back pair for a van), and shut again with CloseDoorFor.</summary>
        public void OpenDoorFor(int seat)
        {
            var d = DoorFor(seat);
            if (d == null) return;
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

        // Finds the body's door parts - any child named "...Door..." (the pack's
        // convention: Door_FL, Door_r, Door_Rear) - which seat each serves (f/r
        // front-rear, l/r left-right in the last name token; a plain "l"/"r" is a van's
        // back door; "Rear" or "Boot" is the boot and never opens for a man), and which
        // way each swings out: the way that carries the door's own middle away from
        // the car. A door's glass the pack left as a sibling is put under the door so
        // it swings with it.
        void FindDoors()
        {
            _doors.Clear();
            var all = Tf.GetComponentsInChildren<Transform>(true);
            var carCentre = Tf.position + Vector3.up * 0.8f;
            foreach (var t in all)
            {
                if (t == Tf) continue;
                string n = t.name;
                if (n.IndexOf("Door", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

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
                var plus = Quaternion.AngleAxis(20f, Vector3.up) * arm;
                var minus = Quaternion.AngleAxis(-20f, Vector3.up) * arm;
                float dPlus = (t.position + plus - carCentre).sqrMagnitude;
                float dMinus = (t.position + minus - carCentre).sqrMagnitude;
                float flank = Vector3.Dot(b.center - Tf.position, Tf.right);

                int cut = n.LastIndexOf('_');
                string token = (cut >= 0 ? n.Substring(cut + 1) : n).ToLowerInvariant();
                int seat;
                if (token.Contains("rear") || token.Contains("boot") || token.Contains("trunk") || token.Contains("back"))
                    seat = -2;
                else if (token.Length <= 1)
                    seat = -1;
                else
                {
                    bool front = token.Contains("f");
                    bool left = token.Contains("l");
                    seat = (front ? 0 : 2) + (left ? 0 : 1);
                    if (Mathf.Abs(flank) < 0.35f) seat = -1;
                }

                var door = new Door
                {
                    Tf = t, Closed = t.localRotation, Sign = dPlus >= dMinus ? 1f : -1f,
                    Side = flank > 0.35f ? 1f : flank < -0.35f ? -1f : 0f,
                    Seat = seat,
                };
                // its window: the glass parts now under it, wound down by their own height
                foreach (var g in t.GetComponentsInChildren<Transform>(true))
                {
                    if (g == t || g.parent != t || g.name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var gr = g.GetComponentInChildren<Renderer>();
                    if (gr == null) continue;
                    door.Glass.Add(g);
                    door.GlassShut.Add(g.localPosition);
                    door.GlassHeight = Mathf.Max(door.GlassHeight, gr.bounds.size.y);
                }
                _doors.Add(door);
            }
        }

        public void TickDoors(float dt)
        {
            foreach (var d in _doors)
            {
                float want = d.Wanted ? 1f : 0f;
                if (!Mathf.Approximately(d.Open, want))
                {
                    d.Open = Mathf.MoveTowards(d.Open, want, dt / DoorSeconds);
                    float eased = Mathf.SmoothStep(0f, 1f, d.Open);
                    if (d.Tf) d.Tf.localRotation = d.Closed * Quaternion.Euler(0f, d.Sign * DoorSwing * eased, 0f);
                }
                // the window winds down into the door (the glass slides down its own
                // height, and a little more so no sliver shows) and back up
                float wantDown = d.WantDown ? 1f : 0f;
                if (d.Glass.Count == 0 || Mathf.Approximately(d.Down, wantDown)) continue;
                d.Down = Mathf.MoveTowards(d.Down, wantDown, dt / WindowSeconds);
                float drop = d.GlassHeight * 1.05f * Mathf.SmoothStep(0f, 1f, d.Down);
                for (int i = 0; i < d.Glass.Count; i++)
                {
                    var g = d.Glass[i];
                    if (!g) continue;
                    var downLocal = g.parent ? g.parent.InverseTransformDirection(Tf.up) : Vector3.up;
                    g.localPosition = d.GlassShut[i] - downLocal * drop;
                }
            }
        }

        /// <summary>Wind the window of the door serving this seat down (a rider putting
        /// his gun out of it) or back up. A body without the glass part does nothing.</summary>
        public void SetWindow(int seat, bool down)
        {
            var d = DoorFor(seat);
            if (d == null || d.Seat == -1) return; // a van's back doors: no window to wind
            d.WantDown = down;
        }

        public void CloseAllWindows()
        {
            foreach (var d in _doors) d.WantDown = false;
        }

        // ------------------------------------------------------------------ wheels

        readonly WheelSpin _wheels = new WheelSpin();

        // The wheel parts of the body, each spun about its own hub and the front pair
        // steered - a sedan's four, a truck's six (WheelSpin).
        void FindWheels() => _wheels.Read(Tf);

        /// <summary>Metres from the body's origin back to its rear axle - the point
        /// that follows a path while the front swings (NaN without wheels).</summary>
        public float AxleBack => float.IsNaN(_wheels.RearAxle) ? float.NaN : -_wheels.RearAxle;

        /// <summary>Rolling with the road, the front pair turned into the corner.</summary>
        public void TickWheels(float dt, float speed, float steerDegrees) =>
            _wheels.Tick(dt, speed, steerDegrees);
    }
}
