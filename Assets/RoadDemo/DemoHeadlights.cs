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
        public DemoClock clock;

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
        static readonly Color BeamColour = new Color(1f, 0.85f, 0.45f);

        class Rig
        {
            public Transform Car;
            public RoadCar Vehicle;
            public Light L, R;
            public bool Burning;
        }

        readonly List<Rig> _rigs = new List<Rig>();
        float[] _key = new float[0];
        int[] _order = new int[0];
        float _nextResort;
        float _lit = -1f;

        public void Register(Transform car, float halfLen)
        {
            var rig = new Rig
            {
                Car = car,
                L = Attach(car, new Vector3(-0.55f, 0.7f, halfLen - 0.5f)),
                R = Attach(car, new Vector3(0.55f, 0.7f, halfLen - 0.5f)),
            };
            _rigs.Add(rig);
        }

        /// <summary>Register a live traffic car so its engine/parking state can drive
        /// the lamps. The transform-only overload remains for decorative highway cars.</summary>
        public void Register(DemoVehicle car)
        {
            if (car == null || !car.Tf) return;
            var rig = new Rig
            {
                Car = car.Tf,
                Vehicle = car,
                L = Attach(car.Tf, new Vector3(-0.55f, 0.7f, car.HalfLen - 0.5f)),
                R = Attach(car.Tf, new Vector3(0.55f, 0.7f, car.HalfLen - 0.5f)),
            };
            _rigs.Add(rig);
        }

        static bool WantsLights(Rig rig) => rig.Vehicle == null ||
            (!rig.Vehicle.Parked && !rig.Vehicle.EngineOff &&
             !rig.Vehicle.Derelict && !rig.Vehicle.Wrecked);

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
            // a car that was destroyed took its lamps with it; its rig is not walked
            // and ranked for the rest of the run
            for (int i = _rigs.Count - 1; i >= 0; i--)
                if (!_rigs[i].L || !_rigs[i].R) _rigs.RemoveAt(i);
            if (_rigs.Count == 0)
                return;

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
            if (burn && camera && _rigs.Count * 2 > LitBeamBudget)
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
                DemoStreetLamps.Nearest(_key, _order, LitBeamBudget / 2);
            }
            else DemoStreetLamps.Prepare(ref _key, ref _order, _rigs.Count);

            int litCars = 0;
            for (int rank = 0; rank < _order.Length; rank++)
            {
                var rig = _rigs[_order[rank]];
                bool wants = WantsLights(rig);
                bool burns = burn && wants && litCars * 2 < LitBeamBudget;
                if (wants) litCars++;
                // enabling a light re-registers it with the renderer: touched only on change
                if (burns != rig.Burning)
                {
                    rig.L.enabled = burns;
                    rig.R.enabled = burns;
                    rig.Burning = burns;
                }
                if (burns)
                {
                    rig.L.intensity = target;
                    rig.R.intensity = target;
                }
            }
        }
    }
}
