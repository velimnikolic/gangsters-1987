using System.Collections.Generic;
using LivingCity.Data;
using LivingCity.Entities;
using LivingCity.Generation;
using LivingCity.Personnel;
using LivingCity.UI;
using RoadDemo;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace RifleDemo
{
    /// <summary>
    /// The long-gun review bench: one man out of the live city's crowd catalogue, a
    /// rifle out of the armoury, and the Mixamo Pro Rifle pack under him.
    ///
    /// He walks a regular octagon WITHOUT TURNING. That is the whole point of the
    /// bench: the pack authors every gait eight ways, and a man who keeps his muzzle
    /// pointed one way while his feet take him round a circuit plays all eight in
    /// order - forward, forward-right, right, backward-right, backward, backward-left,
    /// left, forward-left - one clip a leg, in the order they are listed. A lap of the
    /// octagon is the contact sheet.
    ///
    /// He is moved the way the city moves everybody: HIS OWN TRANSFORM, at the pace the
    /// take carries (clip.averageSpeed), with applyRootMotion off. That is the city's
    /// rule (PedestrianAgent.ClipPace, CrewKit's note on the RootMotion takes) and it is
    /// what this bench is here to check - if the feet skate here they will skate in the
    /// street.
    ///
    /// Keys: 1 walk, 2 run, 3 sprint, 4 crouch walk, 5 idle, 6 idle aiming,
    /// 7 crouching idle, 8 crouching idle aiming. Space cycles the gait tier by hand,
    /// otherwise a lap of the circuit changes it on its own.
    /// </summary>
    public sealed class RifleDemoBuilder : MonoBehaviour
    {
        const string RuntimeRootName = "Rifle Animation Review";

        /// <summary>Corner-to-centre of the circuit. A lap is eight sides of
        /// 2*R*sin(22.5) = 0.765*R, so 12 m gives a 73 m lap: about ten seconds at the
        /// pack's sprint and forty at its walk, both comfortable to watch.</summary>
        const float CircuitRadius = 12f;

        static readonly RifleStep[] Lap =
        {
            RifleStep.Forward, RifleStep.ForwardRight, RifleStep.Right,
            RifleStep.BackwardRight, RifleStep.Backward, RifleStep.BackwardLeft,
            RifleStep.Left, RifleStep.ForwardLeft
        };

        enum Tier { Walk, Run, Sprint, CrouchWalk }

        static readonly Tier[] Tiers = { Tier.Walk, Tier.Run, Tier.Sprint, Tier.CrouchWalk };

        readonly List<GameObject> _fallbackCast = new List<GameObject>();

        Transform _runtime;
        GameObject _actor;
        Animator _animator;
        Transform _gun;
        PedestrianPicker _cityPicker;
        System.Random _rng;

        PlayableGraph _graph;
        AnimationPlayableOutput _output;

        // Two slots and a crossfade between them. A take is never cut to another take:
        // the new one comes up on the free slot while the old one goes down, so a man
        // who turns a corner of the circuit rolls from one direction into the next
        // instead of snapping into it a frame.
        AnimationMixerPlayable _mix;
        readonly AnimationClipPlayable[] _slot = new AnimationClipPlayable[2];
        readonly AnimationClip[] _slotClip = new AnimationClip[2];
        int _live;
        float _blend = 1f, _blendRate = 4f;
        float _paceFrom, _paceTo;
        float _clipPace;
        AnimationClip _clip => _slotClip[_live];

        int _leg;
        int _tier;
        int _tierPlayed = -1;
        AnimationClip _held;          // a stand held until a key says otherwise
        Vector3 _legFrom, _legTo;
        float _legTravelled;
        float _legOverrun;

        Camera _camera;
        TextMeshProUGUI _caption;

        void Awake() => EnsureBuilt();

        void OnEnable()
        {
            // With domain reload disabled Unity keeps the scene objects but not this
            // non-serialized graph wrapper, exactly as the push-up bench found.
            if (Application.isPlaying && (_runtime == null || !_graph.IsValid()))
                EnsureBuilt();
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        // ------------------------------------------------------------- the standing up

        void EnsureBuilt()
        {
            if (_runtime != null && _graph.IsValid()) return;
            if (_graph.IsValid()) _graph.Destroy();

            var stale = _runtime != null ? _runtime : transform.Find(RuntimeRootName);
            if (stale != null)
            {
                stale.gameObject.SetActive(false);
                Destroy(stale.gameObject);
            }

            _runtime = new GameObject(RuntimeRootName).transform;
            _runtime.SetParent(transform, false);
            _rng = new System.Random(unchecked(
                System.Environment.TickCount * 397 ^ (int)System.DateTime.UtcNow.Ticks));

            if (!RifleKit.Installed)
            {
                Debug.LogError("[RifleDemo] The Mixamo rifle pack is not in " +
                               "Assets/Animations/Mixamo/Rifle - nothing to review.", this);
                return;
            }

            PrepareCityCast();
            var prefab = DrawAdultCityCharacter();
            if (prefab == null)
            {
                Debug.LogError("[RifleDemo] The city cast has no adult Humanoid character.", this);
                return;
            }

            BuildGround();
            BuildActor(prefab);
            BuildCameraAndLight();
            BuildCaption();
            StartLeg(0, first: true);
        }

        void PrepareCityCast()
        {
            _fallbackCast.Clear();
            _cityPicker = null;

            var set = LedgerModelSet.Instance;
            if (set == null) return;

            var database = set.database;
            if (database != null && database.pedestrianGroups != null)
            {
                var picker = new PedestrianPicker(database.pedestrianGroups, _rng);
                if (!picker.IsEmpty) _cityPicker = picker;
            }

            if (set.people == null) return;
            foreach (var prefab in set.people)
                if (IsAdultHumanoid(prefab, null) && !_fallbackCast.Contains(prefab))
                    _fallbackCast.Add(prefab);
        }

        GameObject DrawAdultCityCharacter()
        {
            if (_cityPicker != null)
                for (var attempt = 0; attempt < 48; attempt++)
                {
                    var candidate = _cityPicker.Next(out var group);
                    if (IsAdultHumanoid(candidate, group)) return candidate;
                }

            return _fallbackCast.Count > 0
                ? _fallbackCast[_rng.Next(_fallbackCast.Count)]
                : null;
        }

        static bool IsAdultHumanoid(GameObject prefab, string group)
        {
            if (prefab == null ||
                PedestrianAnthropometry.CohortFor(group, prefab.name) != PedestrianAgeCohort.Adult)
                return false;

            var name = prefab.name;
            if (name.IndexOf("Daughter", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("_Son_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Kid", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            var animator = prefab.GetComponentInChildren<Animator>(true);
            return animator != null && animator.avatar != null && animator.avatar.isHuman;
        }

        void BuildActor(GameObject prefab)
        {
            _actor = Instantiate(prefab, _runtime);
            _actor.name = prefab.name + " - rifle";
            _actor.transform.SetPositionAndRotation(Corner(0), Quaternion.identity);

            _animator = _actor.GetComponentInChildren<Animator>(true);
            StripCityRuntime(_actor, _animator);

            PedestrianAnthropometry.Apply(
                _actor, _rng.Next(), PedestrianIdentity.IsFemale(prefab.name),
                PedestrianAgeCohort.Adult, prefab.name);

            _animator.enabled = true;
            _animator.runtimeAnimatorController = null;
            // The city never applies root motion: the walker carries his own transform
            // and the take only says how fast it should go. Same here.
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.Rebind();

            // The armoury's own rifle, put in the fist by the same hand the street uses.
            var gunPrefab = CrewArms.ModelForKind(EquipmentKind.Rifle);
            if (gunPrefab != null) _gun = CrewArms.Attach(_animator, gunPrefab);
            else Debug.LogWarning("[RifleDemo] The armoury sells no rifle body - " +
                                  "the review runs with empty hands.", this);

            _graph = PlayableGraph.Create("Rifle Demo");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _output = AnimationPlayableOutput.Create(_graph, "Rifle", _animator);
            _mix = AnimationMixerPlayable.Create(_graph, 2);
            _output.SetSourcePlayable(_mix);
            Play(RifleKit.Idle, 0f, 0f);
            _graph.Play();
        }

        static void StripCityRuntime(GameObject root, Animator keepAnimator)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                Destroy(body);
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
                if (behaviour != keepAnimator)
                    Destroy(behaviour);
        }

        // ------------------------------------------------------------------ the circuit

        /// <summary>Corner i of the octagon. The first corner is due south of the
        /// centre so the first leg - the plain forward run - is straight up the screen.</summary>
        static Vector3 Corner(int i)
        {
            float a = (i / 8f) * Mathf.PI * 2f - Mathf.PI * 0.5f;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * CircuitRadius;
        }

        void StartLeg(int leg, bool first = false)
        {
            _leg = ((leg % 8) + 8) % 8;
            if (!first && _leg == 0)
                _tier = (_tier + 1) % Tiers.Length;    // a lap a tier, round and round

            _legFrom = Corner(_leg);
            _legTo = Corner(_leg + 1);
            // whatever he overran the last corner by is walked off this leg, so no
            // frame of ground is dropped at a corner and nothing snaps back
            _legTravelled = Mathf.Max(0f, _legOverrun);
            _legOverrun = 0f;
            _actor.transform.position = Vector3.Lerp(_legFrom, _legTo,
                _legTravelled / Mathf.Max(0.01f, Vector3.Distance(_legFrom, _legTo)));
            // He never turns: the muzzle stays north for the whole lap, which is what
            // makes the eight legs read as the pack's eight directions.
            _actor.transform.rotation = Quaternion.identity;
            PlayGait();
        }

        void PlayGait()
        {
            if (_held != null) { Play(_held, 0f, 0.3f); return; }
            var step = Lap[_leg];
            var clip = Tiers[_tier] switch
            {
                Tier.Walk => RifleKit.Walk(step),
                Tier.Run => RifleKit.Run(step),
                Tier.Sprint => RifleKit.Sprint(step),
                _ => RifleKit.CrouchWalk(step)
            };
            // a corner is a change of direction inside one gait and wants the shorter
            // blend; a change of tier is a change of stride and wants the longer one
            Play(clip, clip != null ? clip.averageSpeed.magnitude : 0f,
                 _tier == _tierPlayed ? 0.22f : 0.35f);
            _tierPlayed = _tier;
        }

        /// <summary>Bring a take up under him over <paramref name="seconds"/>. The pace
        /// is the CLIP's, not a number picked here - the same oracle the street keys its
        /// feet to - and it is carried across the blend with the weight, so the ground he
        /// covers eases from the old take's speed into the new one's instead of jumping.
        ///
        /// THE NEW TAKE STARTS ON THE OLD ONE'S PHASE. Every gait in the pack is a stride
        /// of the same man at the same cadence; entered at frame one, a stride that was
        /// half done starts again with the wrong foot, and that is exactly the hitch the
        /// eye reads as a cut. Entered at the phase the old stride had reached, the foot
        /// that was coming down still comes down.</summary>
        void Play(AnimationClip clip, float pace, float seconds = 0.22f)
        {
            if (clip == null || _slotClip[_live] == clip) return;

            int next = 1 - _live;
            float phase = 0f;
            var old = _slot[_live];
            var oldClip = _slotClip[_live];
            if (old.IsValid() && oldClip != null && oldClip.length > 0f && clip.length > 0f)
                phase = (float)(old.GetTime() % oldClip.length) / oldClip.length * clip.length;

            if (_slot[next].IsValid())
            {
                _mix.DisconnectInput(next);
                _graph.DestroyPlayable(_slot[next]);
            }

            var playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetTime(phase);
            _graph.Connect(playable, 0, _mix, next);
            _slot[next] = playable;
            _slotClip[next] = clip;

            _paceFrom = _clipPace;
            _paceTo = pace;
            _live = next;
            if (seconds <= 0.001f)
            {
                _blend = 1f;
                _blendRate = 0f;
                _clipPace = pace;
                _mix.SetInputWeight(_live, 1f);
                _mix.SetInputWeight(1 - _live, 0f);
                return;
            }

            _blend = 0f;
            _blendRate = 1f / seconds;
            _mix.SetInputWeight(_live, 0f);
            _mix.SetInputWeight(1 - _live, 1f);
        }

        /// <summary>Carry the crossfade along. Smoothstepped, so the swap has no corner
        /// at either end of it.</summary>
        void Blend(float dt)
        {
            if (!_mix.IsValid() || _blend >= 1f) return;
            _blend = Mathf.Min(1f, _blend + _blendRate * dt);
            float w = _blend * _blend * (3f - 2f * _blend);
            _mix.SetInputWeight(_live, w);
            _mix.SetInputWeight(1 - _live, 1f - w);
            _clipPace = Mathf.Lerp(_paceFrom, _paceTo, w);
        }

        void Update()
        {
            if (_actor == null || !_graph.IsValid()) return;
            float dt = Time.deltaTime;
            ReadKeys();
            Blend(dt);
            Loop();

            if (_held == null) Travel(dt);
            Frame();
            Caption();
        }

        /// <summary>Carry him along the leg at the take's own metres a second. He
        /// arrives, the next leg starts, and the clip under him changes with it.</summary>
        void Travel(float dt)
        {
            if (_clipPace <= 0.01f) return;
            float length = Vector3.Distance(_legFrom, _legTo);
            _legTravelled += _clipPace * dt;
            if (_legTravelled >= length)
            {
                _legOverrun = _legTravelled - length;
                StartLeg(_leg + 1);
                return;
            }

            _actor.transform.position =
                Vector3.Lerp(_legFrom, _legTo, _legTravelled / length);
        }

        /// <summary>The pack's takes are imported looping, but a playable that runs off
        /// the end of a one-shot stops dead; hold every take in its own loop by hand,
        /// the same guard the push-up bench keeps.</summary>
        void Loop()
        {
            for (int i = 0; i < 2; i++)
            {
                var clip = _slotClip[i];
                if (!_slot[i].IsValid() || clip == null || clip.length <= 0f) continue;
                double time = _slot[i].GetTime();
                if (time < clip.length) continue;
                _slot[i].SetTime(time % clip.length);
                _slot[i].SetDone(false);
                _slot[i].SetSpeed(1d);
            }
        }

        /// <summary>The project reads its keys through the Input System package - the
        /// old UnityEngine.Input class THROWS here, and an exception a frame in Update
        /// is what leaves the editor stuck on Error Pause.</summary>
        void ReadKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) SetTier(Tier.Walk);
            if (keyboard.digit2Key.wasPressedThisFrame) SetTier(Tier.Run);
            if (keyboard.digit3Key.wasPressedThisFrame) SetTier(Tier.Sprint);
            if (keyboard.digit4Key.wasPressedThisFrame) SetTier(Tier.CrouchWalk);
            if (keyboard.digit5Key.wasPressedThisFrame) Hold(RifleKit.Idle);
            if (keyboard.digit6Key.wasPressedThisFrame) Hold(RifleKit.IdleAiming);
            if (keyboard.digit7Key.wasPressedThisFrame) Hold(RifleKit.IdleCrouching);
            if (keyboard.digit8Key.wasPressedThisFrame) Hold(RifleKit.IdleCrouchingAiming);
            if (keyboard.cKey.wasPressedThisFrame) _follow = !_follow;
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                _tier = (_tier + 1) % Tiers.Length;
                _held = null;
                PlayGait();
            }
        }

        void SetTier(Tier tier)
        {
            _held = null;
            for (int i = 0; i < Tiers.Length; i++)
                if (Tiers[i] == tier) _tier = i;
            PlayGait();
        }

        /// <summary>Stop him where he stands on one of the four stands. Any gait key
        /// puts him back on the circuit.</summary>
        void Hold(AnimationClip stand)
        {
            if (stand == null) return;
            _held = stand;
            Play(stand, 0f, 0.3f);
        }

        // ------------------------------------------------------------------ the bench

        void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(_runtime, false);
            ground.transform.localScale = Vector3.one * (CircuitRadius * 0.4f);
            Destroy(ground.GetComponent<Collider>());

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "Review ground" };
            material.SetColor("_BaseColor", new Color(0.20f, 0.21f, 0.23f));
            material.SetFloat("_Smoothness", 0.08f);
            ground.GetComponent<Renderer>().sharedMaterial = material;
        }

        void BuildCameraAndLight()
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.44f, 0.47f, 0.52f);

            var lightObject = new GameObject("Review light");
            lightObject.transform.SetParent(_runtime, false);
            lightObject.transform.rotation = Quaternion.Euler(46f, -28f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.88f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;

            var cameraObject = new GameObject("Review camera");
            cameraObject.transform.SetParent(_runtime, false);
            cameraObject.tag = "MainCamera";
            _camera = cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.10f, 0.115f, 0.14f);
            _camera.fieldOfView = 42f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 200f;
            _camera.allowHDR = true;
            _camera.GetUniversalAdditionalCameraData().antialiasing =
                AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraObject.AddComponent<AudioListener>();
            Frame();
        }

        bool _follow = true;

        /// <summary>Two ways to look at him, C between them. Following is the one that
        /// reviews the animation - a man twelve metres across the shot is too small to
        /// see a foot slip. The wide shot is for reading the circuit itself: he never
        /// turns, so the leg he is on IS the direction the take is authored for.
        ///
        /// The camera never turns either. A camera that swung to follow him would undo
        /// the whole point: sideways has to LOOK sideways.</summary>
        void Frame()
        {
            if (_camera == null) return;
            var target = _follow && _actor != null
                ? _actor.transform.position + Vector3.up * 1.05f
                : new Vector3(0f, 1f, 0f);
            var eye = _follow
                ? target + new Vector3(1.1f, 1.9f, -4.4f)
                : target + new Vector3(0f, CircuitRadius * 1.15f, -CircuitRadius * 1.55f);
            _camera.transform.position = eye;
            _camera.transform.rotation = Quaternion.LookRotation(target - eye, Vector3.up);
        }

        void BuildCaption()
        {
            var canvasObject = new GameObject("Review canvas");
            canvasObject.transform.SetParent(_runtime, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            // No GraphicRaycaster: nothing here is clicked, and one over a demo scene
            // is what eats the clicks meant for the world.

            _caption = DemoUi.Text(canvasObject.transform, "Caption", 26f,
                DemoUi.Ink, TextAlignmentOptions.TopLeft);
            var rect = _caption.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(28f, -24f);
            rect.sizeDelta = new Vector2(760f, 120f);
            _caption.textWrappingMode = TextWrappingModes.Normal;
        }

        string _captionShown;

        /// <summary>Written only when the line has actually changed - a string built
        /// every frame for a caption nobody is reading is the kind of garbage the
        /// crowd's HUD is careful about.</summary>
        void Caption()
        {
            if (_caption == null) return;
            var line = (_clip != null ? _clip.name : "-") +
                       "   |   " + _clipPace.ToString("F2") + " m/s off the take\n" +
                       "1 walk  2 run  3 sprint  4 crouch walk   |   " +
                       "5 idle  6 aiming  7 crouched  8 crouched aiming   |   " +
                       "space: next tier   |   c: wide shot";
            if (line == _captionShown) return;
            _captionShown = line;
            _caption.text = line;
        }
    }
}
