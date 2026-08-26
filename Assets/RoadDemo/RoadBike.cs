using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A motorcycle on the lane network: the shared driving underneath (RoadCar - the
    /// lanes, the claims, the junctions, the lights, the flinch at gunfire), with the
    /// two things that make a bike a bike on top.
    ///
    /// It LEANS. A car is set down flat on the road every frame by RoadCar.Place, and
    /// a bike that did that through a bend would look like a filing cabinet on wheels.
    /// So the model is not the car's transform: it hangs one level below it and rolls
    /// about the contact line, by the angle the corner actually asks for - the same
    /// arithmetic a rider's inner ear does, speed against the radius the bars have
    /// cut. Stood still with nobody on it, it goes over the other way onto its stand.
    ///
    /// And it is RIDDEN in the open. A car's occupants are a detail behind glass; a
    /// rider is the silhouette. So every rider aboard is told the speed and the lean
    /// each frame, and told to put a boot down when the bike stops - which is the
    /// whole difference between a parked motorcycle with a man on it and a motorcycle
    /// hovering with a man welded to it.
    /// </summary>
    public class RoadBike : RoadCar
    {
        // a street machine is nobody's property: stood dead, the backstop clears
        // it (CrewBike says otherwise - a crew's machine, or one down in a drive-by,
        // is the scene's to keep)
        protected override bool VanishesWhenStuck => true;

        public BikeBody Body { get; private set; }

        /// <summary>The model under the car's transform: this is what rolls.</summary>
        public Transform Tilt { get; private set; }

        /// <summary>Everyone aboard, the rider first.</summary>
        public readonly List<BikePose> Riders = new List<BikePose>();

        /// <summary>Degrees it will go over in a bend, and how quickly it gets there.
        /// Thirty-odd is a hard road corner; a bike that leans further is on a track.</summary>
        public static float MaxLean = 30f, LeanSeconds = 0.16f;

        /// <summary>Degrees it rests at on its side stand.</summary>
        public static float StandLean = 8f;

        /// <summary>What the ROAD takes a machine to be, as a share of what its MESH
        /// measures. The model is untouched - this is only the box the lane network and
        /// the belt reason about.
        ///
        /// The mesh is measured over everything the prefab has: mirrors, screen, panniers,
        /// the rear rack. A tourer's bounds are a small car's, and the road then treats it
        /// as one - it fits nowhere a bike should fit, and it meets things a bike would
        /// have gone past. The big machine was the only one of the three that wedged in a
        /// junction (1618 refused steps in one run; the moped and the motorbike ran at
        /// zero). What the road wants is the machine's own footprint: the wheels, the
        /// engine and the two men, which is most of the length and well under the width of
        /// the widest handlebar.</summary>
        public static float RoadBodyLong = 0.82f, RoadBodyWide = 0.7f;

        float _lean, _leanRate;

        public RoadBike()
        {
            Profile = DriverProfile.Traffic;
            Tag = "bike";
        }

        /// <summary>How far over it is this instant: for anything riding on it.</summary>
        public float Lean => _lean;

        // ------------------------------------------------------------------ building

        /// <summary>A bike of this pack prefab, stood at this pose, ready to be put on
        /// the road (Spawn / PlaceAt). The prefab goes UNDER a transform of its own -
        /// the driving moves that one, and the model rolls beneath it - which is the
        /// only structural difference from a car and the reason a bike cannot simply
        /// be a DemoVehicle with a narrow body.</summary>
        public static RoadBike Build(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot,
            float roadY, LaneNet net) => Build<RoadBike>(prefab, parent, pos, rot, roadY, net);

        /// <summary>The same, for a bike of a particular kind - the outfit's
        /// (CrewBike), the law's - so the building of one is written once.</summary>
        public static T Build<T>(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot,
            float roadY, LaneNet net) where T : RoadBike, new()
        {
            if (prefab == null) return null;
            var pivot = new GameObject(prefab.name).transform;
            pivot.SetParent(parent, false);
            pivot.SetPositionAndRotation(pos, rot);

            var model = Object.Instantiate(prefab, pivot).transform;
            model.name = prefab.name + " (frame)";
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>()) Object.Destroy(mb);
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>()) Object.Destroy(rb);
            foreach (var col in model.GetComponentsInChildren<Collider>()) Object.Destroy(col);

            // A COLOUR OF ITS OWN, and here rather than at the four places that build
            // bikes. Every two-wheeler in the game comes through this method - the ones
            // riding a lane (StreetBikes.Init), the ones on their stands at a kerb
            // (StreetBikes.Park), the outfit's (DemoCrews.AddBike) and the drive-by's -
            // so painting them anywhere else would be painting them three times out of
            // four. The cars have been asking the same question at their own spawns
            // since VehiclePaint was written; bikes simply never asked it, which is the
            // whole reason a street of them came out in one colour.
            //
            // The law is safe without being named: its tourer wears the police atlas,
            // which VehiclePaint holds no palette for, and the tourer paints the OUTFIT's
            // machine carries are on no pack body at all. Which matters more than it
            // looks, because both machines answer to the bare name "SM_Veh_Motorbike_01"
            // and a name is all VehicleCatalog.WearsLivery is given here.
            //
            // The draw comes off UnityEngine.Random, the same cosmetic stream the cars
            // have always painted out of. No seeded layout moves for it: the city is laid
            // out of System.Random instances of its own.
            LivingCity.Gameplay.VehiclePaint.Apply(model.gameObject, prefab);

            var body = new BikeBody(model);
            var bike = new T
            {
                Tf = pivot,
                Tilt = model,
                Body = body,
                // narrow, and honestly so: it is the whole reason a bike is worth having
                // on the road at all. It does NOT filter between the lanes - that is the
                // lane network's business and not a body measurement - but it takes a
                // bike's room at a kerb and behind a lorry, not a car's.
                HalfLen = Mathf.Max(0.9f, body.HalfLength * RoadBodyLong),
                HalfWide = Mathf.Max(0.34f, body.HalfWidth * RoadBodyWide),
                AxleBack = body.AxleBack,
                RoadY = roadY,
                Net = net != null ? net : LaneNet.Active,
            };
            return bike;
        }

        // ------------------------------------------------------------------ riders

        /// <summary>Somebody on it out of these bodies, and a mate behind him with this
        /// chance (BikeOccupant - the dumb kind, for the traffic).</summary>
        public void Crew(IList<GameObject> people, AnimationClip sitLoop, float pillionChance = 0f, int layer = -1)
        {
            foreach (var man in BikeOccupant.Ride(Body, people, sitLoop, pillionChance, layer))
                Riders.Add(man.Pose);
        }

        /// <summary>A pose already made - a crew man put on the saddle by hand.</summary>
        public void Take(BikePose pose)
        {
            if (pose != null && !Riders.Contains(pose)) Riders.Add(pose);
        }

        public void Drop(BikePose pose) => Riders.Remove(pose);

        /// <summary>Nobody aboard: it stands on its stand.</summary>
        public bool Empty => Riders.Count == 0;

        // ------------------------------------------------------------------ the frame

        protected override void OnPlaced(float dt, float speed, float steerDegrees)
        {
            Body?.Tick(dt, speed, steerDegrees);

            float want = LeanFor(speed, YawRate(dt));
            _lean = Mathf.SmoothDamp(_lean, want, ref _leanRate, LeanSeconds, 600f, Mathf.Max(dt, 1e-4f));
            if (Tilt) Tilt.localRotation = Quaternion.AngleAxis(_lean, Vector3.forward);

            // a boot goes down below walking pace - and stays down until he is properly
            // moving again, so it does not flick up and down while he creeps in a queue
            bool stopped = Mathf.Abs(speed) < (_footDown ? 1.6f : 0.7f);
            _footDown = stopped && !Empty;
            for (int i = 0; i < Riders.Count; i++)
            {
                var r = Riders[i];
                if (r == null) continue;
                r.Speed = Mathf.Abs(speed);
                r.FootDown = _footDown && i == 0;
            }
        }

        bool _footDown;

        /// <summary>Put it on its stand here and now, with no easing: a bike a builder
        /// has stood at a kerb and will never tick has to be leaning the moment the
        /// scene opens, not a second and a half into it.</summary>
        public void SettleStand()
        {
            _lean = -StandLean;
            _leanRate = 0f;
            if (Tilt) Tilt.localRotation = Quaternion.AngleAxis(_lean, Vector3.forward);
        }

        // How fast the nose is coming round, radians a second, positive to the right.
        // Off the transform itself rather than off the steer angle handed down: that
        // angle is worked back out of the yaw with a CAR's wheelbase in it (2.6 m, in
        // RoadCar.Place), and using it here with a motorcycle's 1.4 m would put the
        // radius at half its true size and lay the bike on its ear in a gentle bend.
        // The yaw is what a lean actually answers to, so ask for the yaw.
        float YawRate(float dt)
        {
            if (Tf == null || dt <= 1e-5f) return 0f;
            var fwd = Tf.forward;
            if (_lastFwd.sqrMagnitude < 0.5f) { _lastFwd = fwd; return 0f; }
            float yaw = Vector3.SignedAngle(_lastFwd, fwd, Vector3.up) * Mathf.Deg2Rad / dt;
            _lastFwd = fwd;
            // a step the driving took sideways in one frame (a respawn, a snap onto a
            // lane) is not a corner; a real bend at road speed is well under this
            return Mathf.Clamp(yaw, -2.5f, 2.5f);
        }

        Vector3 _lastFwd;

        // The angle: a bike holds a corner by falling into it just far enough that the
        // road pushes back up the middle of it, which is atan(v * w / g) - the pace
        // against the rate the nose is coming round. Stopped with nobody on it, the
        // other way: over onto the stand, which is always on the near side.
        float LeanFor(float speed, float yawRate)
        {
            float v = Mathf.Abs(speed);
            if (v < 0.35f && (Empty || Parked || Derelict))
                return Empty || Parked ? -StandLean : -StandLean * 0.5f;
            if (v < 0.35f) return 0f;
            if (Mathf.Abs(yawRate) < 1e-3f) return 0f;
            float lean = Mathf.Atan(v * yawRate / 9.81f) * Mathf.Rad2Deg;
            return Mathf.Clamp(lean, -MaxLean, MaxLean);
        }
    }
}
