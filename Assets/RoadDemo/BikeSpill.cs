using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A machine going down: over onto its side, along the road on the side, and - when
    /// it was the fire that put it there - burning where it stops.
    ///
    /// It is deliberately not physics. Nothing in this project falls over with a
    /// rigidbody; the traffic, the crews and the riders are all hand-driven kinematics,
    /// and a spill is three numbers and a curve like everything else. What that buys is
    /// a fall that always reads the same way and always ends flat on the road, instead
    /// of a machine that occasionally cartwheels into a shopfront because a collider
    /// clipped a kerb.
    ///
    /// THE PIVOT IS THE POINT. The packs draw a two-wheeler with its origin on the
    /// CONTACT LINE - the line the tyres stand on - which is the same fact that lets a
    /// bike lean without lifting a tyre (BikeBody, and the lean the bench pushes). So
    /// going down is one rotation about the machine's own forward axis, through that
    /// origin, all the way to ninety degrees: the body swings over and comes to rest
    /// lying flat, and the origin never has to be moved down to meet the road.
    ///
    /// Whoever was on it is NOT this class's business. A machine that goes down throws
    /// its riders (RiderSpill) and the caller does both, because who dies and who gets
    /// up is a story decision and this is only the machine.
    /// </summary>
    public sealed class BikeSpill : MonoBehaviour
    {
        /// <summary>Degrees a second it goes over. It is quick - a machine at speed that
        /// loses its rider is on its side in a third of a second, not a second and a
        /// half - and the number is here because that is exactly the sort of thing that
        /// looks wrong before anyone can say why.</summary>
        public static float RollRate = 300f;

        /// <summary>Metres a second squared once it is down. Metal on tarmac carries a
        /// long way, so this is gentler than the man's (RiderSpill.Drag) and a machine
        /// slides on past him.</summary>
        public static float Drag = 4.2f;

        /// <summary>How far it slews round while it slides, in degrees a second at full
        /// pace, falling away with the speed. A fallen machine does not go straight on:
        /// it comes round on the side it fell.</summary>
        public static float Slew = 55f;

        /// <summary>Where the fire sits on it, up the machine's own axes from the
        /// contact line - about the engine, which is what catches.</summary>
        public static Vector3 FireAt = new Vector3(0f, 0.45f, 0.05f);

        /// <summary>How big the pack's fire is drawn on something the size of a
        /// motorcycle. The prefabs are authored for a burning car.</summary>
        public static float FireScale = 0.55f;

        /// <summary>Which way it went over: +1 to the machine's right, -1 to its left.</summary>
        public float Side { get; private set; } = 1f;

        /// <summary>Degrees over. Ninety is flat on the road.</summary>
        public float Roll { get; private set; }

        /// <summary>It is all the way over.</summary>
        public bool Flat => Roll >= 89.5f;

        /// <summary>It has stopped moving and finished falling.</summary>
        public bool Settled => Flat && Speed <= 0.05f;

        /// <summary>Metres a second it is still carrying along the road.</summary>
        public float Speed { get; private set; }

        /// <summary>It is burning.</summary>
        public bool Alight { get; private set; }

        Vector3 _heading = Vector3.forward;
        Transform _fire;
        float _ground;

        /// <summary>Put it down. <paramref name="speed"/> and <paramref name="heading"/>
        /// are what it was doing at the moment it lost the road; <paramref name="side"/>
        /// is which way it falls.</summary>
        public static BikeSpill Begin(Transform machine, float speed, Vector3 heading, float side,
            bool alight = false, float groundY = 0f)
        {
            if (machine == null) return null;
            var spill = machine.GetComponent<BikeSpill>();
            if (spill != null) return spill;   // already going down
            spill = machine.gameObject.AddComponent<BikeSpill>();
            var flat = new Vector3(heading.x, 0f, heading.z);
            spill._heading = flat.sqrMagnitude > 1e-4f ? flat.normalized : machine.forward;
            spill.Speed = Mathf.Abs(speed);
            spill.Side = side >= 0f ? 1f : -1f;
            spill._ground = groundY;
            if (alight) spill.Ignite();
            return spill;
        }

        /// <summary>Set it alight - the fire rides with it while it is still sliding,
        /// which is what it looks like when a tank goes up under a machine that is
        /// already down.</summary>
        public void Ignite()
        {
            if (Alight) return;
            Alight = true;
            var prefab = CrewKit.Fire;
            if (prefab == null) return;
            var go = Instantiate(prefab, transform);
            go.name = "Fire";
            DemoScratch.Unsaved(go);
            go.transform.localPosition = FireAt;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * FireScale;
            _fire = go.transform;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Roll = Mathf.MoveTowards(Roll, 90f, RollRate * dt);

            if (Speed > 0.05f)
            {
                // it comes round on the side it fell, harder the faster it is going
                float slew = Slew * Mathf.Clamp01(Speed / 12f) * Side * dt;
                _heading = Quaternion.AngleAxis(slew, Vector3.up) * _heading;
                Speed = Mathf.Max(0f, Speed - Drag * dt);
                var at = transform.position + _heading * (Speed * dt);
                at.y = _ground;
                transform.position = at;
            }
            else Speed = 0f;

            // the roll is about the machine's OWN forward, through its origin, which the
            // packs put on the contact line - so nothing sinks and nothing lifts
            transform.rotation = Quaternion.LookRotation(_heading, Vector3.up) *
                                 Quaternion.AngleAxis(Roll * Side, Vector3.forward);

            // the fire stands up out of the machine, not out of the machine's side
            if (_fire != null) _fire.rotation = Quaternion.LookRotation(_heading, Vector3.up);
        }
    }
}
