using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Exhaust for every car on the street, drawn by a handful of emitters that are lent
    /// to whichever cars are nearest the camera.
    ///
    /// A city this size runs hundreds of cars and a ParticleSystem each would be hundreds
    /// of simulations for smoke nobody can see, so this borrows the headlights' shape
    /// (DemoHeadlights): a fixed pool, a partial sort around where the camera is LOOKING
    /// rather than where it stands, and the pool handed to the nearest cars. A car that
    /// falls out of the budget stops smoking, and nothing far enough away to lose its
    /// emitter is near enough to be seen losing it.
    ///
    /// The smoke itself is world-simulated (CarSmoke), so a puff stays in the air it was
    /// left in while the car drives out of it - which is the only reason exhaust reads as
    /// exhaust rather than as a thing stuck to a bumper. It also means an emitter changing
    /// cars would strand its old puffs mid-street, so a slot that changes hands is Cleared.
    ///
    /// The registry is StreetTraffic.Users - the street's own list of everything on the
    /// road. Nothing has to register with this and nothing has to remember to: a car that
    /// exists is a car that smokes.
    /// </summary>
    public sealed class DemoExhaust : MonoBehaviour
    {
        /// <summary>How many cars smoke at once. Counted in cars, not in beams like the
        /// headlights, because a car has one tailpipe.</summary>
        public static int Budget = 14;

        /// <summary>Seconds between one ranking of the traffic and the next. The same
        /// interval the headlights re-rank on, and for the same reason: a car crosses very
        /// little ground in it and a partial sort over every car on the road is not a thing
        /// to do every frame.</summary>
        const float ResortInterval = 0.4f;

        // where the pipe is: out to one side of the tail, at about the height of a bumper
        const float PipeSide = 0.55f;
        const float PipeHeight = 0.3f;
        const float PipeBack = 0.1f;

        /// <summary>Puffs a second at a standstill, and what a hard-driven engine adds on
        /// top. An idling car is what you actually see smoking on a street, so the floor
        /// carries most of it; speed alone adds very little (a car at forty is not smoking
        /// harder than a car at ten, it is only leaving it behind faster).</summary>
        const float IdleRate = 11f * LivingCity.Ambient.FireSmokeFx.DefaultExhaustAmount;
        const float CruiseRate = 5f * LivingCity.Ambient.FireSmokeFx.DefaultExhaustAmount;
        const float AccelRate = 26f * LivingCity.Ambient.FireSmokeFx.DefaultExhaustAmount;

        /// <summary>Metres a second per second that counts as flooring it.</summary>
        const float HardAccel = 3f;

        // a wisp, not a bonfire: a hand's width leaving the pipe, growing as it goes
        const float PuffWide = 0.18f * LivingCity.Ambient.FireSmokeFx.DefaultExhaustSize;
        const float PuffGrow = 1.8f;
        const float PuffLifeLo = 0.9f * LivingCity.Ambient.FireSmokeFx.DefaultExhaustLifetime;
        const float PuffLifeHi = 1.7f * LivingCity.Ambient.FireSmokeFx.DefaultExhaustLifetime;
        const float PuffSpeed = 0.7f * LivingCity.Ambient.FireSmokeFx.DefaultExhaustSpeed;

        /// <summary>The pipe points back and a little down - smoke off a car goes into the
        /// road behind it before it rises.</summary>
        const float PipeTilt = 12f;

        sealed class Slot
        {
            public ParticleSystem Fx;
            public Transform Tf;
            public RoadCar Car;
            public float WasSpeed;
        }

        readonly List<Slot> _slots = new List<Slot>();
        readonly List<RoadCar> _cars = new List<RoadCar>();
        float[] _key = new float[0];
        int[] _order = new int[0];
        float _nextResort;
        bool _noPack;   // the particle pack has no smoke prefab: this scene draws none

        /// <summary>The layer puts itself up the moment the game runs: no menu, no builder
        /// line, nothing for a scene to remember to do. Every scene with traffic in it gets
        /// exhaust, and a scene with none has a component that finds an empty list.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindFirstObjectByType<DemoExhaust>() != null) return;
            var go = new GameObject("Exhaust");
            go.AddComponent<DemoExhaust>();
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= _nextResort)
            {
                _nextResort = Time.unscaledTime + ResortInterval;
                Rank();
            }
            float dt = Time.deltaTime;
            for (int i = 0; i < _slots.Count; i++) Aim(_slots[i], dt);
        }

        /// <summary>Which cars have the pool this time round.</summary>
        void Rank()
        {
            if (_noPack) return;
            _cars.Clear();
            var users = StreetTraffic.Users;
            for (int i = 0; i < users.Count; i++)
                if (users[i] is RoadCar car && Running(car)) _cars.Add(car);
            if (_cars.Count == 0) { Park(0); return; }

            if (_cars.Count > Budget)
            {
                // rank around where the camera looks, not where it stands: the rig parks it
                // a couple of hundred metres back along its boom (DemoHeadlights)
                var camera = Camera.main;
                var eye = camera ? camera.transform.position : Vector3.zero;
                if (camera)
                {
                    var forward = camera.transform.forward;
                    if (forward.y < -0.05f && eye.y > 0f) eye += forward * (eye.y / -forward.y);
                }
                // BOTH arrays, together, and through the one helper every ranking in
                // the demo sizes its pair with: sizing _order off _key's length let the
                // two drift apart on a busy street, then a quiet one, then a busy one
                // again, and the fill walked off the end of the short one.
                DemoStreetLamps.Prepare(ref _key, ref _order, _cars.Count);
                for (int i = 0; i < _cars.Count; i++)
                    _key[i] = camera ? (_cars[i].Tf.position - eye).sqrMagnitude : i;
                DemoStreetLamps.Nearest(_key, _order, Budget);
            }
            else DemoStreetLamps.Prepare(ref _key, ref _order, _cars.Count);

            int want = Mathf.Min(Budget, _cars.Count);
            for (int rank = 0; rank < want; rank++)
            {
                var slot = Slot_(rank);
                if (slot == null) break;
                Give(slot, _cars[_order[rank]]);
            }
            Park(want);
        }

        /// <summary>An engine that is turning. A car stood at the kerb has switched off, one
        /// standing at a pump has been switched off by its driver (EngineOff), and
        /// a derelict, a wreck and a car whose bonnet has been shot through have all stopped
        /// for good - the last of those is smoking out of the FRONT instead (CarSmoke).</summary>
        static bool Running(RoadCar car)
        {
            if (car == null || car.Tf == null) return false;
            if (!LivingCity.Gameplay.MapVisionRegistry.IsRevealed(car.Tf.position)) return false;
            if (car.Parked || car.EngineOff || car.Derelict || car.Wrecked) return false;
            return !(car is CrewCar crew && crew.EngineDead);
        }

        /// <summary>The pool's slot at this rank, made on the spot the first time it is
        /// wanted - a scene with four cars in it never builds fourteen emitters.</summary>
        Slot Slot_(int rank)
        {
            while (_slots.Count <= rank)
            {
                var fx = CarSmoke.Tuned(CrewKit.Exhaust, transform, Vector3.zero,
                                        PuffWide, PuffGrow, PuffLifeLo, PuffLifeHi,
                                        PuffSpeed, IdleRate,
                                        LivingCity.Ambient.FireSmokeFx.ExhaustSmoke);
                // no pack, no exhaust - and it is THIS layer that gives up, not the
                // budget: Budget is a tuning knob a scene may set, and a play mode with
                // the domain reload off carries a zeroed one into every later run
                if (fx == null) { _noPack = true; return null; }
                fx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                _slots.Add(new Slot { Fx = fx, Tf = fx.transform });
            }
            return _slots[rank];
        }

        /// <summary>Hand a slot to a car. A slot that changes hands is CLEARED: its old
        /// puffs are simulated in world space and would otherwise be left hanging over a
        /// street the emitter is no longer anywhere near.</summary>
        static void Give(Slot slot, RoadCar car)
        {
            if (slot.Car == car) return;
            slot.Car = car;
            slot.WasSpeed = Mathf.Abs(car.Speed);
            slot.Fx.Clear();
            slot.Fx.Play();
        }

        /// <summary>Every slot from here down has nobody: stop it emitting and let what is
        /// already in the air finish.</summary>
        void Park(int from)
        {
            for (int i = from; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Car == null) continue;
                slot.Car = null;
                slot.Fx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>Put the emitter on its car's pipe and set how hard it is smoking.</summary>
        void Aim(Slot slot, float dt)
        {
            var car = slot.Car;
            if (car == null) return;
            if (!Running(car))
            {
                slot.Car = null;
                slot.Fx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            var body = car.Tf;
            // which side the pipe is on, settled by the car's own id: the same car keeps
            // the same pipe for its whole life, and no two neighbours need agree. Not a
            // Random draw - the street's rng is one stream and this is not worth a step in it.
            float side = (car.Id & 1) == 0 ? 1f : -1f;
            var at = body.TransformPoint(new Vector3(side * car.HalfWide * PipeSide,
                                                     PipeHeight, -car.HalfLen + PipeBack));
            slot.Tf.SetPositionAndRotation(
                at, Quaternion.LookRotation(-body.forward, Vector3.up) * Quaternion.Euler(PipeTilt, 0f, 0f));

            float speed = Mathf.Abs(car.Speed);
            float accel = dt > 0f ? (speed - slot.WasSpeed) / dt : 0f;
            slot.WasSpeed = speed;

            float rate = IdleRate
                       + CruiseRate * Mathf.InverseLerp(0f, 12f, speed)
                       + AccelRate * Mathf.Clamp01(accel / HardAccel);
            var emission = slot.Fx.emission;
            emission.rateOverTime = rate;
        }
    }
}
