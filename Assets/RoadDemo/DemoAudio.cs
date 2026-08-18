using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    // What the demo sounds like. Self-contained in this folder like the clock and
    // the sky: the city's CityAudioDirector reads registries this scene does not
    // have, so the demo runs its own small mix over its own lists.
    //
    // Four layers, and none of them is a source per body:
    //
    //   THE EAR - the scene's only AudioListener, parked on the camera's focus
    //   rather than on the camera itself. The demo's boom sits 190 m out, so a
    //   listener on the lens would put every car beyond any sane rolloff and the
    //   whole street would be a whisper (which is exactly why the crews' gunshot
    //   plays 2D). On the focus, distances are the ones the player is looking at.
    //
    //   BEDS - wind by day, a low hiss by night, crossfaded on DemoSky's own
    //   nightness ramp, plus a traffic hum whose level follows how many cars are
    //   near the focus. The hum rises as the camera pulls out and the individual
    //   engines fade: a city seen from above still roars, it just stops being cars.
    //
    //   ENGINES - six looping voices handed to the six nearest cars, pitched by
    //   speed. Thirty cars needing at most six audible; a voice already held gets a
    //   distance edge so a pair straddling the cutoff cannot trade it back and
    //   forth, restarting the loop every scan.
    //
    //   EVENTS - one pooled set of positional one-shots for footsteps, street
    //   voices, horns, doors, and whatever else asks (DemoAudio.At). Footsteps are
    //   a budgeted trickle over walkers near the focus, not foot-to-ground sync:
    //   from this camera a plausible patter at the right density is
    //   indistinguishable, and sync would mean touching every walker every frame.
    //
    // Pause is a fade of the world layers, not AudioListener.pause: the top bar's
    // own click has to survive the frame that pauses the demo, and a hard cut on
    // space reads as a bug. Everything here runs on UNSCALED time for its fades and
    // on scaled time for its emission, so a paused demo goes quiet and a 4x demo
    // walks and honks four times as often.
    public class DemoAudio : MonoBehaviour
    {
        const int EngineVoices = 6;
        const int OneShotVoices = 10;

        // The zoom band detail dies across: everything is at full level inside 120 m
        // of boom, gone by 320 (the camera clamps at 520).
        const float DetailNear = 120f, DetailFar = 320f;

        const float EngineReach = 150f;   // engines compete for a voice within this
        const float EngineMinDist = 8f;   // rolloff plateau around the focus
        const float EngineMaxDist = 150f;
        const float EventMinDist = 12f;
        const float EventMaxDist = 180f;
        const float FullSpeed = 12f;      // m/s at the top of the engine pitch ramp
        const float StepsPerSecond = 5f;

        /// <summary>The demo's mix, for anything that wants to make a noise without
        /// being wired to it (a civilian's door, the top bar's click).</summary>
        public static DemoAudio Active { get; private set; }

        public DemoClock clock;
        public DemoCamera rig;

        List<DemoVehicle> _cars;
        List<PolicePatrolCar> _police;
        List<CivilianAgent> _walkers;

        Transform _ear;
        AudioSource _dayBed, _nightBed, _hum;
        readonly AudioSource[] _engines = new AudioSource[EngineVoices];
        readonly DemoVehicle[] _engineOf = new DemoVehicle[EngineVoices];
        readonly AudioSource[] _oneShots = new AudioSource[OneShotVoices];
        int _nextOneShot;

        float _detail = 1f;      // 1 zoomed in, 0 zoomed out
        float _worldGain = 1f;   // 0 while the demo is paused, faded
        float _busy;             // cars near the focus, 0..1
        float _rescan, _stepBudget, _voiceIn = 8f, _hornIn = 6f;
        bool _muted;

        /// <summary>Wired by the builder once the city, the crowd and the clock all
        /// exist. The lists are the builder's own and are read live, so anything it
        /// spawns later is heard without re-registering.</summary>
        public void Init(DemoClock demoClock, DemoCamera camera, List<DemoVehicle> cars,
            List<PolicePatrolCar> police, List<CivilianAgent> walkers)
        {
            clock = demoClock;
            rig = camera;
            _cars = cars;
            _police = police;
            _walkers = walkers;
        }

        void Awake()
        {
            Active = this;

            // One listener, and it is ours. The builder's camera does not carry one
            // (a second listener is a warning and an arbitrary winner).
            foreach (var stray in FindObjectsByType<AudioListener>())
                stray.enabled = false;

            var ear = new GameObject("Ear");
            ear.transform.SetParent(transform, worldPositionStays: false);
            ear.AddComponent<AudioListener>();
            _ear = ear.transform;

            _dayBed = Bed(DemoSounds.Wind, "Day Bed");
            _nightBed = Bed(DemoSounds.NightAir, "Night Bed");

            // The hum is one engine idle taken down an octave and a half: a block of
            // traffic heard from four streets away is not any one car, it is this.
            _hum = Bed(DemoSounds.Pick(DemoSounds.EngineLoops), "Traffic Hum");
            if (_hum) _hum.pitch = 0.55f;

            for (int i = 0; i < EngineVoices; i++)
            {
                var voice = Voice("Engine " + i, EngineMinDist, EngineMaxDist);
                voice.loop = true;
                _engines[i] = voice;
            }

            for (int i = 0; i < OneShotVoices; i++)
                _oneShots[i] = Voice("One Shot " + i, EventMinDist, EventMaxDist);
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            AudioListener.volume = 1f;
        }

        AudioSource Bed(AudioClip clip, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            if (clip == null) return source;
            source.clip = clip;
            source.Play();
            return source;
        }

        AudioSource Voice(string name, float min, float max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f; // the ear teleports with the camera; doppler on that is a siren
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = min;
            source.maxDistance = max;
            return source;
        }

        // ---------------------------------------------------------------- the mix

        void LateUpdate()
        {
            if (rig == null) return;

            float unscaled = Time.unscaledDeltaTime;
            float scaled = Time.deltaTime;

            PlaceEar();

            // Mute is unscaled and outside the pause fade: it must work on a frozen
            // demo, and it must be instant.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame && !LivingCity.UI.PersonnelAlmanac.IsOpen)
            {
                _muted = !_muted;
                AudioListener.volume = _muted ? 0f : 1f;
            }

            _detail = 1f - Mathf.Clamp01(Mathf.InverseLerp(DetailNear, DetailFar, rig.distance));
            _worldGain = Mathf.MoveTowards(_worldGain,
                clock != null && clock.Paused ? 0f : 1f, unscaled * 4f);

            _rescan -= unscaled;
            if (_rescan <= 0f)
            {
                _rescan = 0.5f;
                Rescan();
            }

            UpdateBeds();
            FollowCars();

            if (_worldGain > 0.01f)
            {
                EmitFootsteps(scaled);
                EmitStreetVoices(scaled);
                EmitHorns(scaled);
            }
        }

        /// <summary>The ear on the focus, turned the way the camera looks - so a car
        /// on the left of the screen is a car on the left.</summary>
        void PlaceEar()
        {
            _ear.SetPositionAndRotation(rig.pivot, Quaternion.Euler(0f, rig.yaw, 0f));
        }

        void UpdateBeds()
        {
            float night = clock != null ? DemoSky.Nightness(clock.Hour) : 0f;
            float master = DemoSounds.Master * _worldGain;

            if (_dayBed) _dayBed.volume = DemoSounds.DayBedVolume * (1f - night) * master;
            if (_nightBed) _nightBed.volume = DemoSounds.NightBedVolume * night * master;

            if (_hum)
            {
                // Loudest exactly where the engines are quietest. Never zero while
                // there is traffic at all: the far view is where the bed IS the city.
                float zoom = Mathf.Lerp(1f, 0.45f, _detail);
                _hum.volume = DemoSounds.TrafficHumVolume * _busy * zoom * master;
            }
        }

        // ------------------------------------------------------------- the engines

        /// <summary>Hands the engine voices to the nearest cars and takes the census
        /// the traffic hum rides on. Twice a second, not per frame.</summary>
        void Rescan()
        {
            var focus = rig.pivot;
            float reachSqr = EngineReach * EngineReach;

            int near = 0;
            for (int i = 0; i < CarCount; i++)
            {
                var car = CarAt(i);
                if (car?.Tf == null) continue;
                var delta = car.Tf.position - focus;
                delta.y = 0f;
                if (delta.sqrMagnitude < reachSqr) near++;
            }
            // Square root: the third car on a street changes how busy it sounds far
            // more than the tenth.
            _busy = Mathf.Sqrt(Mathf.Clamp01(near / 10f));

            for (int slot = 0; slot < EngineVoices; slot++)
            {
                DemoVehicle best = null;
                float bestScore = reachSqr;

                for (int i = 0; i < CarCount; i++)
                {
                    var car = CarAt(i);
                    if (car?.Tf == null || HeldBefore(car, slot)) continue;

                    var delta = car.Tf.position - focus;
                    delta.y = 0f;
                    float score = delta.sqrMagnitude;
                    if (car == _engineOf[slot]) score *= 0.64f; // holding it is worth a 20% edge

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = car;
                    }
                }

                if (best != _engineOf[slot]) Attach(slot, best);
            }
        }

        bool HeldBefore(DemoVehicle car, int slot)
        {
            for (int i = 0; i < slot; i++)
                if (_engineOf[i] == car) return true;
            return false;
        }

        void Attach(int slot, DemoVehicle car)
        {
            _engineOf[slot] = car;
            var voice = _engines[slot];

            var clips = DemoSounds.EngineLoops;
            if (car == null || clips.Length == 0)
            {
                voice.Stop();
                return;
            }

            int id = car.GetHashCode() & 0x7fffffff;
            var clip = clips[id % clips.Length];
            voice.clip = clip;
            // Each car starts somewhere else in the recording, or six engines off one
            // file play as one big engine with chorus.
            voice.time = id % 997 / 997f * clip.length;
            voice.Play();
        }

        void FollowCars()
        {
            float gain = DemoSounds.EngineVolume * DemoSounds.Master * _detail * _worldGain;
            // A demo at 4x drives four times as fast; the engines follow it part of
            // the way, because pitch is not scaled by timeScale and stock idle under
            // a car doing 50 m/s reads as a film run at the wrong speed.
            float rate = clock != null ? Mathf.Lerp(1f, Mathf.Sqrt(clock.SpeedMultiplier), 0.6f) : 1f;

            for (int i = 0; i < EngineVoices; i++)
            {
                var car = _engineOf[i];
                if (car == null) continue;

                if (car.Tf == null)
                {
                    Attach(i, null); // despawned out from under us; drop it now
                    continue;
                }

                var voice = _engines[i];
                voice.transform.position = car.Tf.position;
                voice.pitch = Mathf.Lerp(0.85f, 1.3f, car.Speed / FullSpeed) * rate;
                voice.volume = gain;
            }
        }

        int CarCount => (_cars?.Count ?? 0) + (_police?.Count ?? 0);

        DemoVehicle CarAt(int i)
        {
            int own = _cars?.Count ?? 0;
            if (i < own) return _cars[i];
            return _police[i - own];
        }

        // -------------------------------------------------------------- the events

        /// <summary>The footstep trickle. An accumulator rather than a timer: at five
        /// steps a second and a detail gain of 0.3 it correctly emits 1.5 steps'
        /// worth, where a timer would emit either five or none.</summary>
        void EmitFootsteps(float dt)
        {
            if (DemoSounds.Footsteps.Length == 0) return;

            _stepBudget += StepsPerSecond * _detail * dt;
            if (_stepBudget < 1f) return;
            _stepBudget -= 1f;

            var walker = SampleWalker();
            if (walker == null) return; // this tick's step is forfeit, not deferred

            At(DemoSounds.Pick(DemoSounds.Footsteps), walker.Tf.position,
                DemoSounds.FootstepVolume, pitchJitter: 0.12f);
        }

        void EmitStreetVoices(float dt)
        {
            if (DemoSounds.StreetVoices.Length == 0) return;

            _voiceIn -= dt * Mathf.Max(_detail, 0.15f);
            if (_voiceIn > 0f) return;
            _voiceIn = Random.Range(9f, 24f);

            var walker = SampleWalker();
            if (walker == null) return;

            At(DemoSounds.Pick(DemoSounds.StreetVoices), walker.Tf.position,
                DemoSounds.StreetVoiceVolume * Mathf.Lerp(0.4f, 1f, _detail), pitchJitter: 0.1f);
        }

        /// <summary>Somebody leans on the horn. Only cars actually held up sound one -
        /// a honk from a car rolling freely reads as a mistake.</summary>
        void EmitHorns(float dt)
        {
            if (DemoSounds.Horns.Length == 0) return;

            _hornIn -= dt;
            if (_hornIn > 0f) return;

            var focus = rig.pivot;
            float reachSqr = EngineReach * EngineReach;
            DemoVehicle held = null;
            int seen = 0;

            for (int i = 0; i < CarCount; i++)
            {
                var car = CarAt(i);
                if (car?.Tf == null || car.Speed > 0.4f) continue;
                var delta = car.Tf.position - focus;
                delta.y = 0f;
                if (delta.sqrMagnitude > reachSqr) continue;
                // reservoir pick: one stopped car, uniformly, in a single pass
                if (Random.Range(0, ++seen) == 0) held = car;
            }

            if (held == null)
            {
                _hornIn = 2f; // nobody is queuing; look again shortly
                return;
            }

            _hornIn = Random.Range(8f, 20f);
            At(DemoSounds.Pick(DemoSounds.Horns), held.Tf.position,
                DemoSounds.HornVolume * Mathf.Lerp(0.5f, 1f, _detail), pitchJitter: 0.06f);
        }

        /// <summary>A walker out on the pavement near the focus - not one sat on a
        /// bench, stood talking, or currently indoors.</summary>
        CivilianAgent SampleWalker()
        {
            if (_walkers == null || _walkers.Count == 0) return null;

            var focus = rig.pivot;
            // Inside roughly the visible half-frame, so a step lands on somebody the
            // view can plausibly attribute it to.
            float reach = Mathf.Clamp(rig.distance * 0.45f, 20f, 120f);
            float reachSqr = reach * reach;

            for (int tries = 0; tries < 8; tries++)
            {
                var walker = _walkers[Random.Range(0, _walkers.Count)];
                if (walker?.Tf == null || !walker.Tf.gameObject.activeSelf) continue;
                if (walker.State != CivilianAgent.Mode.Walking) continue;

                var delta = walker.Tf.position - focus;
                delta.y = 0f;
                if (delta.sqrMagnitude < reachSqr) return walker;
            }
            return null;
        }

        // ------------------------------------------------------------- the outside
        //
        // What the rest of the demo calls. Both are null-safe on the mix itself: a
        // scene built without a DemoAudio simply makes no noise.

        /// <summary>A one-shot out in the world, at a place.</summary>
        public static void At(AudioClip clip, Vector3 position, float volume,
            float pitchJitter = 0f)
        {
            if (clip == null || Active == null) return;
            Active.PlayLocal(clip, position, volume * DemoSounds.Master, pitchJitter);
        }

        /// <summary>A one-shot from the interface - 2D, unaffected by zoom, and NOT
        /// silenced by pause: the click that pauses the demo has to be heard.</summary>
        public static void Ui(AudioClip clip, float volume = DemoSounds.UiVolume)
        {
            if (clip == null || Active == null) return;
            Active.PlayLocal(clip, Active._ear.position, volume * DemoSounds.Master, 0f, ui: true);
        }

        void PlayLocal(AudioClip clip, Vector3 position, float volume, float pitchJitter,
            bool ui = false)
        {
            for (int i = 0; i < OneShotVoices; i++)
            {
                var voice = _oneShots[_nextOneShot];
                _nextOneShot = (_nextOneShot + 1) % OneShotVoices;
                if (voice.isPlaying) continue; // all busy: the sound is dropped, never queued

                voice.transform.position = position;
                voice.spatialBlend = ui ? 0f : 1f;
                voice.pitch = pitchJitter > 0f
                    ? 1f + Random.Range(-pitchJitter, pitchJitter)
                    : 1f;
                voice.PlayOneShot(clip, ui ? volume : volume * _worldGain);
                return;
            }
        }
    }
}
