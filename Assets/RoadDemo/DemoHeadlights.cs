using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // Headlights for the demo's cars: two tungsten spots per car, intensity riding
    // DemoSky's night curve, and only the beams nearest the camera's ground
    // focus burning - 100 cars is 200 beams, and URP Forward+ renders at most 256
    // additional lights per frame on desktop, most of which the street lamps take.
    public class DemoHeadlights : MonoBehaviour
    {
        public LivingCity.Ambient.CityClock clock;

        // 24 cars' worth. Counted in beams, cut between cars (a pair shares one
        // sort key); sized so lamps (192) + beams (48) stay under URP's 256.
        const int LitBeamBudget = 48;
        const float ResortInterval = 0.4f;

        // beam geometry and colour follow CarHeadlights: tilted down so the pool
        // hugs the bumper, period tungsten yellow rather than modern white
        const float DownTilt = 24f;
        const float SpotOuterAngle = 70f;
        const float SpotInnerAngle = 35f;
        const float Range = 12f;
        const float Intensity = 16f;
        // Shared with the street-lamp bulbs: the visible source and the light it
        // promises should use the same period-tungsten colour across the city.
        internal static readonly Color BeamColour = new Color(1f, 0.85f, 0.45f);

        class Rig
        {
            public Transform Car;
            public RoadCar Vehicle;
            public VehicleLampRig Fittings;
            public Light L, R;
            public Light[] Auxiliary;
            public bool Burning;
            public VehicleBrakeState Brakes;
        }

        readonly List<Rig> _rigs = new List<Rig>();
        readonly HashSet<RoadCar> _registered = new HashSet<RoadCar>();
        DemoCrews _crews;
        float[] _key = new float[0];
        int[] _order = new int[0];
        float _nextResort, _nextBrakeSample, _brakeSampleAt;
        float _lit = -1f;

        public void Register(Transform car, float halfLen)
        {
            if (car) _rigs.Add(CreateRig(car, null, halfLen));
        }

        /// <summary>Register a live road car so its engine/parking state can drive the
        /// lamps. This includes traffic, parking customers and crew cars; the
        /// transform-only overload remains for decorative highway cars.</summary>
        public void Register(RoadCar car)
        {
            if (car == null || !car.Tf || !_registered.Add(car)) return;
            _rigs.Add(CreateRig(car.Tf, car, car.HalfLen));
        }

        static Rig CreateRig(Transform car, RoadCar vehicle, float halfLen)
        {
            var fittings = car.GetComponentInChildren<VehicleLampRig>(true);
            Vector3 Position(bool left)
            {
                if (!fittings) return new Vector3(left ? -0.55f : 0.55f, 0.7f, halfLen - 0.5f);
                return car.InverseTransformPoint(fittings.transform.TransformPoint(
                    left ? fittings.leftHeadlight : fittings.rightHeadlight));
            }
            int extra = fittings && fittings.auxiliaryHeadlights != null ? fittings.auxiliaryHeadlights.Length : 0;
            var auxiliary = new Light[extra];
            for (int i = 0; i < extra; i++)
                auxiliary[i] = Attach(car, car.InverseTransformPoint(fittings.transform.TransformPoint(fittings.auxiliaryHeadlights[i])));
            return new Rig
            {
                Car = car, Vehicle = vehicle, Fittings = fittings,
                L = Attach(car, Position(true)), R = Attach(car, Position(false)), Auxiliary = auxiliary,
            };
        }

        /// <summary>Crew cars are dealt from the ledger after the city and its night
        /// stack have already been built. Watch that live list so every car added later
        /// receives the same bounded headlight pair as ordinary traffic.</summary>
        public void Watch(DemoCrews crews)
        {
            _crews = crews;
            RegisterCrewCars();
        }

        void RegisterCrewCars()
        {
            if (_crews == null) return;
            for (int i = 0; i < _crews.Cars.Count; i++)
                Register(_crews.Cars[i]);
        }

        static bool WantsLights(Rig rig) => rig.Car != null && rig.Car.gameObject.activeInHierarchy &&
            LivingCity.Gameplay.MapVisionRegistry.IsRevealed(rig.Car.position) &&
            (rig.Vehicle == null ||
             (!rig.Vehicle.Parked && !rig.Vehicle.EngineOff &&
              !rig.Vehicle.Derelict && !rig.Vehicle.Wrecked &&
              !rig.Vehicle.EngineDead));

        static Light Attach(Transform car, Vector3 localPos)
        {
            var holder = new GameObject("headlight");
            holder.transform.SetParent(car, false);
            holder.transform.localPosition = localPos;
            holder.transform.localRotation = Quaternion.Euler(DownTilt, 0f, 0f);

            var light = holder.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = SpotOuterAngle;
            light.innerSpotAngle = SpotInnerAngle;
            light.color = BeamColour;
            light.range = Range;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.renderMode = LightRenderMode.ForcePixel;
            holder.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>()
                  .usePipelineSettings = true;
            return light;
        }

        void LateUpdate()
        {
            RegisterCrewCars();

            // a car that was destroyed took its lamps with it; its rig is not walked
            // and ranked for the rest of the run
            for (int i = _rigs.Count - 1; i >= 0; i--)
                if (!_rigs[i].L || !_rigs[i].R)
                {
                    Release(_rigs[i]);
                    if (_rigs[i].Vehicle != null) _registered.Remove(_rigs[i].Vehicle);
                    _rigs.RemoveAt(i);
                }
            if (_rigs.Count == 0)
                return;

            // Brake indication runs in daylight too, independently of the slower
            // beam ranking. No new Light objects or per-vehicle Update callbacks.
            if (Time.unscaledTime >= _nextBrakeSample)
            {
                float now = Time.unscaledTime;
                float dt = _brakeSampleAt > 0f ? now - _brakeSampleAt : 0f;
                _brakeSampleAt = now; _nextBrakeSample = now + .08f;
                foreach (var rig in _rigs)
                    if (rig.Fittings && rig.Vehicle != null)
                        rig.Fittings.SetBrakeLights(rig.Brakes.Step(rig.Vehicle.Speed, dt,
                            WantsLights(rig), rig.Vehicle.Halted));
            }

            float night = DemoSky.Nightness(clock ? clock.Hour : 12f);
            float target = Intensity * night;

            bool resortDue = Time.unscaledTime >= _nextResort;
            if (resortDue)
                _nextResort = Time.unscaledTime + ResortInterval;
            if (!resortDue && Mathf.Approximately(target, _lit))
                return;
            _lit = target;

            bool burn = target > 0.001f;
            var camera = Camera.main;
            int installedBeams = _rigs.Count * 2;
            foreach (var rig in _rigs) installedBeams += rig.Auxiliary.Length;
            if (burn && camera && installedBeams > LitBeamBudget)
            {
                // rank around where the camera looks, not where it stands - the rig
                // parks it a couple hundred metres back along its boom
                var eye = camera.transform.position;
                var forward = camera.transform.forward;
                if (forward.y < -0.05f && eye.y > 0f)
                    eye += forward * (eye.y / -forward.y);

                // one position read per car, then a partial selection over plain
                // floats: the nearest 24 cars to the front, the rest in any order
                DemoStreetLamps.Prepare(ref _key, ref _order, _rigs.Count);
                for (int i = 0; i < _rigs.Count; i++)
                {
                    var car = _rigs[i].Car;
                    _key[i] = car && WantsLights(_rigs[i])
                        ? (car.position - eye).sqrMagnitude
                        : float.MaxValue;
                }
                DemoStreetLamps.Nearest(_key, _order, Mathf.Min(_rigs.Count, LitBeamBudget / 2));
            }
            else DemoStreetLamps.Prepare(ref _key, ref _order, _rigs.Count);

            int litBeams = 0;
            for (int rank = 0; rank < _order.Length; rank++)
            {
                var rig = _rigs[_order[rank]];
                bool wants = WantsLights(rig);
                if (rig.Fittings) rig.Fittings.SetRunningLights(wants ? night : 0f);
                int beams = 2 + rig.Auxiliary.Length;
                bool burns = burn && wants && litBeams + beams <= LitBeamBudget;
                if (burns) litBeams += beams;
                // enabling a light re-registers it with the renderer: touched only on change
                if (burns != rig.Burning)
                {
                    rig.L.enabled = burns;
                    rig.R.enabled = burns;
                    foreach (var light in rig.Auxiliary) if (light) light.enabled = burns;
                    rig.Burning = burns;
                }
                if (burns)
                {
                    rig.L.intensity = target;
                    rig.R.intensity = target;
                    foreach (var light in rig.Auxiliary) if (light) light.intensity = target;
                }
            }
        }

        void OnDisable()
        {
            foreach (var rig in _rigs)
            {
                if (rig.L) rig.L.enabled = false;
                if (rig.R) rig.R.enabled = false;
                foreach (var light in rig.Auxiliary) if (light) light.enabled = false;
                if (rig.Fittings) { rig.Fittings.SetBrakeLights(false); rig.Fittings.SetRunningLights(0f); }
                rig.Burning = false;
                rig.Brakes = default;
            }
            _lit = -1f;
            _nextResort = _nextBrakeSample = _brakeSampleAt = 0f;
        }

        static void Release(Rig rig)
        {
            if (rig.Fittings) { rig.Fittings.SetBrakeLights(false); rig.Fittings.SetRunningLights(0f); }
            if (rig.L) { rig.L.enabled = false; Destroy(rig.L.gameObject); }
            if (rig.R) { rig.R.enabled = false; Destroy(rig.R.gameObject); }
            foreach (var light in rig.Auxiliary) if (light) { light.enabled = false; Destroy(light.gameObject); }
        }

        void OnDestroy()
        {
            foreach (var rig in _rigs) Release(rig);
            _rigs.Clear();
            _registered.Clear();
        }
    }
}
