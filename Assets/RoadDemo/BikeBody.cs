using UnityEngine;

namespace RoadDemo
{
    // A two-wheeler read off whatever pack prefab it is: how long its wheelbase, where
    // its bars are and which way they turn, where a man's hips go on the saddle, where
    // his boots go on the pegs, and the same again for whoever rides behind him.
    // Nothing about how it MOVES - that is RoadBike's - and nothing pack-specific
    // beyond the pack's own naming (HandleBars, Wheel_Front_01, Steering_Wheel): the
    // Palm City motorbike, the moped, the police pair and the quad all read the same
    // way, so any of them can be ridden.
    //
    // The whole point of the class is that nothing here is authored by eye. A man on a
    // bike is not a man in a car: his legs cannot be folded away under a sill
    // (CarOccupant's trick), they are in plain sight on the pegs - so the pegs, the
    // grips and the saddle have to be the bike's own, measured, or the pose is wrong
    // in a way the eye catches at once. What IS authored is only the handful of
    // proportions below, and those are the same on every motorcycle ever built.
    public sealed class BikeBody
    {
        /// <summary>The model. It leans about its own origin, which is the contact
        /// line - the packs stand a vehicle on y=0 - so a lean never lifts a tyre.</summary>
        public readonly Transform Tf;

        // ------------------------------------------------------------ the proportions
        //
        // Metres, off the bike's own measurements. A rider sits back off the bars with
        // his boots a little ahead of his hips; his mate sits behind him with his boots
        // further back and higher. Static so a demo can push them about during Play.

        /// <summary>Hips back from the line of the grips.</summary>
        public static float SaddleBehindBars = 0.56f;
        /// <summary>Saddle below the grips.</summary>
        public static float SaddleBelowBars = 0.30f;
        /// <summary>Hips above the saddle top - a man sinks into a seat.</summary>
        public static float HipsAboveSaddle = 0.09f;
        /// <summary>The pillion's hips behind the rider's, and higher.</summary>
        public static float PillionBehind = 0.36f, PillionAbove = 0.07f;
        /// <summary>Pegs. The height is a share of the saddle's, NOT a clearance off
        /// the tyre: the packs draw a motorcycle a size up to match the men who ride it
        /// - the city bike measures a 2.45 m wheelbase on wheels 1.1 m across - and a
        /// peg set a hand above a wheel that big lands level with the seat, which folds
        /// a rider up like a jockey. The proportion between a saddle and a peg is the
        /// same on every machine ever built, so use the proportion.
        ///
        /// The width is a share of the flank for the same reason: the widest thing on a
        /// bike is its handlebars, and a boot out at bar width is a boot in mid-air.</summary>
        public static float PegHeightOfSaddle = 0.42f, PegWidthOfFlank = 0.55f, PegAhead = 0.26f;
        /// <summary>The pillion's pegs, behind his own hips and higher than the
        /// rider's - his knees are folded up round the man in front.</summary>
        public static float PillionPegBack = 0.06f, PillionPegLift = 0.11f;

        // ------------------------------------------------------------ what was measured

        public float HalfLength { get; private set; } = 1.05f;
        public float HalfWidth { get; private set; } = 0.36f;
        public float Wheelbase { get; private set; } = 1.4f;
        public float WheelRadius { get; private set; } = 0.32f;

        /// <summary>Metres from the origin back to the rear axle - the point that
        /// follows the line while the front swings (RoadCar.AxleBack).</summary>
        public float AxleBack { get; private set; } = 0.6f;

        // Every one of these is in the bike's own frame, and every one of them is a
        // point the pose reaches for: the hips to the saddle, the fists to the grips,
        // the boots to the pegs.
        public Vector3 GripLeft { get; private set; }
        public Vector3 GripRight { get; private set; }
        public Vector3 SaddleRider { get; private set; }
        public Vector3 SaddlePillion { get; private set; }
        public Vector3 PegLeft { get; private set; }
        public Vector3 PegRight { get; private set; }
        public Vector3 PillionPegLeft { get; private set; }
        public Vector3 PillionPegRight { get; private set; }

        /// <summary>Room for a second man. The packs' bodies all have it; a bicycle
        /// frame would not.</summary>
        public bool SeatsTwo { get; set; } = true;

        /// <summary>The grips as one axis: left grip to right, unit length, in the
        /// bike's frame. A fist is turned along it.</summary>
        public Vector3 BarAxis => (GripRight - GripLeft).sqrMagnitude > 1e-4f
            ? (GripRight - GripLeft).normalized : Vector3.right;

        // ------------------------------------------------------------ the moving parts

        readonly WheelSpin _wheels = new WheelSpin();
        Transform _bars;
        Vector3 _barsRest, _barsHub, _barsSpoke;
        Quaternion _barsTurn;
        Bounds _barsBox;
        float _barsShown;

        public BikeBody(Transform tf)
        {
            Tf = tf;
            _wheels.Read(tf);
            WheelRadius = _wheels.Radius;
            Measure();
            FindBars();
            Place();
            Log();
        }

        // ------------------------------------------------------------ world points

        public Vector3 Point(Vector3 local) => Tf ? Tf.TransformPoint(local) : local;
        public Vector3 Way(Vector3 local) => Tf ? Tf.TransformDirection(local) : local;

        public Vector3 Saddle(bool pillion) => Point(pillion ? SaddlePillion : SaddleRider);
        public Vector3 Grip(bool right) => Point(right ? GripRight : GripLeft);
        public Vector3 Peg(bool right, bool pillion) => Point(
            pillion ? (right ? PillionPegRight : PillionPegLeft) : (right ? PegRight : PegLeft));

        /// <summary>Where a rider puts a boot down at a stop: off the peg, out and
        /// straight to the road, on the side asked for.</summary>
        public Vector3 Ground(bool right)
        {
            var peg = right ? PegRight : PegLeft;
            return Point(new Vector3(Mathf.Sign(peg.x) * (HalfWidth + 0.20f), 0f, peg.z - 0.06f));
        }

        // ------------------------------------------------------------ the frame

        public void Tick(float dt, float speed, float steerDegrees)
        {
            _wheels.Tick(dt, speed, steerDegrees);
            if (!_bars) return;
            // the bars turn with the front wheel, about the vertical through their own
            // middle. Like every other pack part they pivot at the vehicle's origin
            // (WheelSpin's whole subject), so the pivot is slid over by as much as the
            // turn moved that middle.
            _barsShown = Mathf.MoveTowards(_barsShown, Mathf.Clamp(steerDegrees, -32f, 32f), 160f * dt);
            var q = Quaternion.AngleAxis(_barsShown, Vector3.up) * _barsTurn;
            _bars.localRotation = q;
            if (_barsHub != Vector3.zero) _bars.localPosition = _barsRest + _barsHub - q * _barsSpoke;
        }

        /// <summary>How far the bars are turned this instant - the fists follow them,
        /// so a rider's arms cross the tank when the bars are hard over.</summary>
        public float BarsTurned => _barsShown;

        /// <summary>A grip where it actually is this instant: the measured point swung
        /// with the bars. A rider's fists are put here, so his arms steer with him
        /// instead of hanging on a bar that has turned out from under them.</summary>
        public Vector3 GripNow(bool right)
        {
            var g = right ? GripRight : GripLeft;
            if (!_bars || Mathf.Abs(_barsShown) < 0.01f) return Point(g);
            var pivot = _barsBox.center;
            return Point(pivot + Quaternion.AngleAxis(_barsShown, Vector3.up) * (g - pivot));
        }

        // ------------------------------------------------------------ the measuring

        void Measure()
        {
            if (!LocalBounds(Tf, Tf, out var b)) return;
            HalfLength = Mathf.Max(0.7f, b.extents.z);
            HalfWidth = Mathf.Clamp(b.extents.x, 0.22f, 0.75f);
            if (!float.IsNaN(_wheels.RearAxle) && !float.IsNaN(_wheels.FrontAxle))
            {
                Wheelbase = Mathf.Max(0.8f, _wheels.FrontAxle - _wheels.RearAxle);
                AxleBack = -_wheels.RearAxle;
            }
            else
            {
                Wheelbase = HalfLength * 1.4f;
                AxleBack = HalfLength * 0.6f;
            }
        }

        // The bars: the part the pack calls a handlebar, or - on the mopeds and the
        // quad, which are steered by a "wheel" the pack never means to roll - the
        // steering part. WheelSpin passes over anything named Steering, so nothing is
        // turned twice.
        void FindBars()
        {
            Transform best = null;
            float bestY = float.MinValue;
            foreach (var t in Tf.GetComponentsInChildren<Transform>(true))
            {
                if (t == Tf) continue;
                bool handle = t.name.IndexOf("Handle", System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool steer = t.name.IndexOf("Steering", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!handle && !steer) continue;
                if (!LocalBounds(t, Tf, out var box)) continue;
                // the highest such part is the bar itself, never a bracket under it
                if (box.center.y <= bestY) continue;
                bestY = box.center.y;
                best = t;
                _barsBox = box;
            }
            _bars = best;
            if (!_bars) return;
            _barsRest = _bars.localPosition;
            _barsTurn = _bars.localRotation;
            var parent = _bars.parent ? _bars.parent : Tf;
            var hub = parent.InverseTransformPoint(Tf.TransformPoint(_barsBox.center));
            _barsHub = hub - _barsRest;
            _barsSpoke = Quaternion.Inverse(_barsTurn) * _barsHub;
        }

        void Place()
        {
            // the grips: the ends of the bar, a hand's width in off the tips, so a fist
            // sits on the rubber rather than off the end of it
            if (_bars)
            {
                float y = _barsBox.center.y + _barsBox.extents.y * 0.35f;
                float z = _barsBox.center.z;
                float x = Mathf.Max(0.16f, _barsBox.extents.x * 0.86f);
                GripRight = new Vector3(x, y, z);
                GripLeft = new Vector3(-x, y, z);
            }
            else
            {
                // no bar part at all: over the front axle at a fair bar height
                float z = float.IsNaN(_wheels.FrontAxle) ? HalfLength * 0.7f : _wheels.FrontAxle - 0.08f;
                GripRight = new Vector3(0.30f, 1.02f, z);
                GripLeft = new Vector3(-0.30f, 1.02f, z);
            }

            float rear = float.IsNaN(_wheels.RearAxle) ? -HalfLength * 0.7f : _wheels.RearAxle;
            float saddleZ = Mathf.Max(GripRight.z - SaddleBehindBars, rear + 0.10f);
            float saddleY = Mathf.Max(GripRight.y - SaddleBelowBars, WheelRadius + 0.28f);

            SaddleRider = new Vector3(0f, saddleY + HipsAboveSaddle, saddleZ);
            SaddlePillion = new Vector3(0f, saddleY + HipsAboveSaddle + PillionAbove, saddleZ - PillionBehind);
            SeatsTwo = Wheelbase > 1.05f;

            float pegY = Mathf.Max(0.20f, (saddleY + HipsAboveSaddle) * PegHeightOfSaddle);
            float pegX = Mathf.Clamp(HalfWidth * PegWidthOfFlank, 0.16f, 0.34f);
            PegRight = new Vector3(pegX, pegY, saddleZ + PegAhead);
            PegLeft = new Vector3(-pegX, pegY, saddleZ + PegAhead);
            PillionPegRight = new Vector3(pegX, pegY + PillionPegLift, SaddlePillion.z - PillionPegBack);
            PillionPegLeft = new Vector3(-pegX, pegY + PillionPegLift, SaddlePillion.z - PillionPegBack);
        }

        static readonly System.Collections.Generic.HashSet<string> Logged =
            new System.Collections.Generic.HashSet<string>();

        // One line per body the first time it is read - the same courtesy CarBody does
        // its seats: the numbers a man would want when a rider sits wrong.
        void Log()
        {
            if (Tf == null || !Logged.Add(Tf.name)) return;
            Debug.Log($"[Bike] {Tf.name}: wheelbase {Wheelbase:F2}, wheel r {WheelRadius:F2}, " +
                      $"bars {(_bars ? _bars.name : "none")}, grip ({GripRight.x:F2}, {GripRight.y:F2}, {GripRight.z:F2}), " +
                      $"saddle ({SaddleRider.y:F2}, {SaddleRider.z:F2}), peg ({PegRight.x:F2}, {PegRight.y:F2}, {PegRight.z:F2}), " +
                      $"two up {SeatsTwo}");
        }

        /// <summary>The box a part's meshes draw, in another transform's frame. Off the
        /// meshes, not Renderer.bounds, so a part that is switched off (a livery
        /// variant, a spare) still measures true, and nothing depends on which way the
        /// prefab happens to be standing when it is read.</summary>
        public static bool LocalBounds(Transform part, Transform frame, out Bounds box)
        {
            box = default;
            bool any = false;
            if (part == null || frame == null) return false;
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                Add(mesh.bounds, mf.transform, frame, ref box, ref any);
            }
            foreach (var sm in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = sm.sharedMesh;
                if (mesh == null) continue;
                Add(mesh.bounds, sm.transform, frame, ref box, ref any);
            }
            return any;
        }

        static void Add(Bounds mesh, Transform of, Transform frame, ref Bounds box, ref bool any)
        {
            for (int c = 0; c < 8; c++)
            {
                var corner = mesh.center + new Vector3(
                    (c & 1) == 0 ? -mesh.extents.x : mesh.extents.x,
                    (c & 2) == 0 ? -mesh.extents.y : mesh.extents.y,
                    (c & 4) == 0 ? -mesh.extents.z : mesh.extents.z);
                var p = frame.InverseTransformPoint(of.TransformPoint(corner));
                if (any) box.Encapsulate(p);
                else { box = new Bounds(p, Vector3.zero); any = true; }
            }
        }
    }
}
