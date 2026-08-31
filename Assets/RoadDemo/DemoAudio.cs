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
    //   BEDS - a daylight street and a night street, crossfaded on DemoSky's own
    //   nightness ramp, plus two censused layers: a traffic hum that follows how
    //   many cars are near the focus and a crowd murmur that follows how many
    //   people are. The hum rises as the camera pulls out and the individual
    //   engines fade (a city seen from above still roars, it just stops being
    //   cars); the murmur does the opposite, because a pavement does not carry.
    //
    //   ENGINES - six looping voices handed to the six nearest cars, pitched by
    //   speed. Thirty cars needing at most six audible; a voice already held gets a
    //   distance edge so a pair straddling the cutoff cannot trade it back and
    //   forth, restarting the loop every scan.
    //
    //   EVENTS - one pooled set of positional one-shots for the pass-by, street
    //   voices, doors, and whatever else asks (DemoAudio.At). The pass-by is the
    //   traffic's only moving sound: the engine voices idle whatever the car is
    //   doing, so a car that actually goes past the ear gets the recording of one.
    //
    //   No footsteps. A trickle of concrete cracks over the crowd is a patter at a
    //   distance and a metronome up close, and the clip is 0.19 s long, so any rate
    //   worth hearing lays them end to end. The pavement is the murmur bed's job.
    //
    // Pause is a fade of the world layers, not AudioListener.pause: the top bar's
    // own click has to survive the frame that pauses the demo, and a hard cut on
    // space reads as a bug. Everything here runs on UNSCALED time for its fades and
    // on scaled time for its emission, so a paused demo goes quiet and a 4x demo
    // spends its street voices four times as fast.
    //
    // No horns. A city that honks on a timer is a city that honks at nothing, and at
    // 4x it honks four times as often at nothing - it read as a fault rather than as
    // traffic. Bringing them back means bringing back a reason to sound one, not a
    // shorter interval.
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

        // The pass-by ring. A car fires on the census that first finds it inside
        // PassReach and is not heard from again until it is PassLeave clear, so one
        // sitting on the boundary cannot fire twice a second. The reach is set by
        // the recording rather than by taste: car_pass_by is 3.2 s with its loudest
        // moment in the middle, so a car at town speed wants roughly two seconds of
        // road left when it starts.
        const float PassReach = 24f;
        const float PassLeave = 34f;
        const float PassMinSpeed = 4f;    // m/s; a crawl is an idle, not a pass
        const float PassSpacing = 1.2f;   // s between passes, however busy the street

        /// <summary>The demo's mix, for anything that wants to make a noise without
        /// being wired to it (a civilian's door, the top bar's click).</summary>
        public static DemoAudio Active { get; private set; }

        public DemoClock clock;
        public DemoCamera rig;

        List<DemoVehicle> _cars;
        List<PolicePatrolCar> _police;
        List<CivilianAgent> _walkers;

        Transform _ear;
        AudioSource _dayBed, _nightBed, _hum, _murmur;
        readonly AudioSource[] _engines = new AudioSource[EngineVoices];
        readonly DemoVehicle[] _engineOf = new DemoVehicle[EngineVoices];
        readonly AudioSource[] _oneShots = new AudioSource[OneShotVoices];
        int _nextOneShot;

        float _detail = 1f;      // 1 zoomed in, 0 zoomed out
        float _worldGain = 1f;   // 0 while the demo is paused, faded
        float _busy;             // cars near the focus, 0..1
        float _crowd;            // people near the focus, 0..1
        float _rescan, _voiceIn = 8f, _passIn;

        // Cars inside the pass-by ring, this census and the one before it. Two sets
        // swapped rather than one set edited: a car that despawns mid-pass simply
        // fails to turn up in the new one, so nothing has to evict it.
        HashSet<int> _ringWas = new HashSet<int>();
        HashSet<int> _ringNow = new HashSet<int>();
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

            _dayBed = Bed(DemoSounds.DayBed, "Day Bed");
            _nightBed = Bed(DemoSounds.NightBed, "Night Bed");

            // A block of traffic heard from four streets away is not any one car,
            // it is its own recording - a downtown bed with the top rolled off.
            _hum = Bed(DemoSounds.TrafficHum, "Traffic Hum");

            // The crowd, once, scaled by how many people are near the focus. Never a
            // source per pedestrian: thirty walkers would be thirty copies of one
            // walla recording beating against itself.
            _murmur = Bed(DemoSounds.Murmur, "Murmur");

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
            // never lift the mute back off a headless run (BatchAudioMute keeps it down)
            AudioListener.volume = Application.isBatchMode ? 0f : 1f;
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
            // demo, and it must be instant. SHIFT+M, not M: plain M opens the strategic
            // map, which now installs in this scene as well as in the generated city.
            var keyboard = Keyboard.current;
            var shift = keyboard != null &&
                        (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (keyboard != null && shift && keyboard.mKey.wasPressedThisFrame &&
                !LivingCity.UI.PersonnelAlmanac.IsOpen)
            {
                _muted = !_muted;
                AudioListener.volume = _muted ? 0f : 1f;
            }

            _detail = 1f - Mathf.Clamp01(Mathf.InverseLerp(DetailNear, DetailFar, rig.distance));
            _worldGain = Mathf.MoveTowards(_worldGain,
                clock != null && clock.Paused ? 0f : 1f, unscaled * 4f);

            if (_passIn > 0f) _passIn -= unscaled;
            _rescan -= unscaled;
            if (_rescan <= 0f)
            {
                _rescan = 0.5f;
                Rescan();
            }

            UpdateBeds();
            FollowCars();

            if (_worldGain > 0.01f) EmitStreetVoices(scaled);
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

            if (_murmur)
            {
                // The other way round from the hum: the roar of a city carries to the
                // far view, a pavement full of people does not.
                _murmur.volume = DemoSounds.MurmurVolume * _crowd
                                 * Mathf.Lerp(0.3f, 1f, _detail) * master;
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
            _ringNow.Clear();
            for (int i = 0; i < CarCount; i++)
            {
                var car = CarAt(i);
                if (car?.Tf == null) continue;
                var delta = car.Tf.position - focus;
                delta.y = 0f;
                float away = delta.sqrMagnitude;
                if (away < reachSqr) near++;

                bool held = _ringWas.Contains(car.Id);
                float ring = held ? PassLeave : PassReach;
                if (away >= ring * ring) continue;

                _ringNow.Add(car.Id);
                if (!held && car.RoadSpeed >= PassMinSpeed) PassBy(car, focus);
            }
            (_ringWas, _ringNow) = (_ringNow, _ringWas);
            // Square root: the third car on a street changes how busy it sounds far
            // more than the tenth.
            _busy = Mathf.Sqrt(Mathf.Clamp01(near / 10f));

            int walking = 0;
            for (int i = 0; i < (_walkers?.Count ?? 0); i++)
            {
                var walker = _walkers[i];
                if (walker?.Tf == null) continue;
                var delta = walker.Tf.position - focus;
                delta.y = 0f;
                if (delta.sqrMagnitude < reachSqr) walking++;
            }
            _crowd = Mathf.Sqrt(Mathf.Clamp01(walking / 16f));

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

        /// <summary>A car going past the ear, laid down where it will be at its
        /// closest rather than where it is now: the clip is a whole pass, so its
        /// loudest moment has to land with the car's. Anything long enough to be a
        /// flatbed gets the heavier recording, and the whole thing rides the zoom
        /// gain - one car passing is a detail, and details die on the wide view
        /// where the traffic hum takes the street over.</summary>
        void PassBy(DemoVehicle car, Vector3 focus)
        {
            // Zoomed out there are no individual cars to hear, and a rank of them
            // arriving together is one pass, not five: a street at a junction can
            // put half a dozen through the ring on the same census.
            if (_detail <= 0.05f || _passIn > 0f || _worldGain <= 0.01f) return;

            var pos = car.Tf.position;
            var fwd = car.Tf.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return;
            fwd.Normalize();

            // Closest approach along its own heading, capped at two seconds of road
            // so a car aimed straight at the focus cannot throw the sound past it.
            var toEar = focus - pos;
            toEar.y = 0f;
            float run = Mathf.Clamp(Vector3.Dot(toEar, fwd), 0f, car.RoadSpeed * 2f);

            var clip = car.HalfLen > 3f ? DemoSounds.TruckPassBy : DemoSounds.CarPassBy;
            At(clip, pos + fwd * run, DemoSounds.PassByVolume * _detail, pitchJitter: 0.07f);
            _passIn = PassSpacing;
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

        /// <summary>Roughly the visible half-frame: the ring a voice may come out
        /// of, so it lands on somebody the view can plausibly attribute it to.
        /// </summary>
        float VoiceReach => Mathf.Clamp(rig.distance * 0.45f, 20f, 120f);

        /// <summary>A walker out on the pavement near the focus - not one sat on a
        /// bench, stood talking, or currently indoors.</summary>
        CivilianAgent SampleWalker()
        {
            if (_walkers == null || _walkers.Count == 0) return null;

            var focus = rig.pivot;
            float reachSqr = VoiceReach * VoiceReach;

            for (int tries = 0; tries < 8; tries++)
            {
                var walker = _walkers[Random.Range(0, _walkers.Count)];
                if (walker?.Tf == null || !walker.Tf.gameObject.activeSelf) continue;
                if (walker.State != CivilianAgent.Mode.Walking &&
                    walker.State != CivilianAgent.Mode.Flee) continue;

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
            float pitchJitter = 0f, float pitch = 1f)
        {
            if (clip == null || Active == null) return;
            Active.PlayLocal(clip, position, volume * DemoSounds.Master, pitchJitter, pitch: pitch);
        }

        /// <summary>A one-shot from the interface - 2D, unaffected by zoom, and NOT
        /// silenced by pause: the click that pauses the demo has to be heard.</summary>
        public static void Ui(AudioClip clip, float volume = DemoSounds.UiVolume)
        {
            if (clip == null || Active == null) return;
            Active.PlayLocal(clip, Active._ear.position, volume * DemoSounds.Master, 0f, ui: true);
        }

        void PlayLocal(AudioClip clip, Vector3 position, float volume, float pitchJitter,
            bool ui = false, float pitch = 1f)
        {
            for (int i = 0; i < OneShotVoices; i++)
            {
                var voice = _oneShots[_nextOneShot];
                _nextOneShot = (_nextOneShot + 1) % OneShotVoices;
                if (voice.isPlaying) continue; // all busy: the sound is dropped, never queued

                voice.transform.position = position;
                voice.spatialBlend = ui ? 0f : 1f;
                voice.pitch = pitch * (pitchJitter > 0f
                    ? 1f + Random.Range(-pitchJitter, pitchJitter)
                    : 1f);
                voice.PlayOneShot(clip, ui ? volume : volume * _worldGain);
                return;
            }
        }
    }
}
