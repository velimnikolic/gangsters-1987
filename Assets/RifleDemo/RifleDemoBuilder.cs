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
using UnityEngine.EventSystems;
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
        Vector3 _gunGripBase;
        Vector3 _idleGripOffset;
        float _idleGripBlend;
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

        // What he is pointed at, and the line that says so.
        Transform _target;
        LineRenderer _aimLine;
        bool _aimAtTarget = true;

        /// <summary>He is being driven by hand. The circuit is a contact sheet - it
        /// plays all eight ways in order whether or not anyone is watching - and this is
        /// the other half of the same question: what the set feels like when a man is
        /// walked about while his weapon stays on something.</summary>
        bool _manual;
        TMP_Dropdown _picker;

        /// <summary>The take the list is holding him on, or null when he is walking the
        /// circuit. A pinned take is played where he stands and carries him the way the
        /// take itself carries - a strafe goes sideways, a death goes nowhere.</summary>
        AnimationClip _pinned;

        void Awake() => EnsureBuilt();

        void OnEnable()
        {
            // With domain reload disabled Unity keeps the scene objects but not this
            // non-serialized graph wrapper, exactly as the push-up bench found.
            if (Application.isPlaying && (_runtime == null || !_graph.IsValid()))
                EnsureBuilt();
        }

        /// <summary>After the graph has written the pose, and before the frame is
        /// drawn: the one moment a foreign body's feet can be put on the ground.</summary>
        void LateUpdate()
        {
            StandHimOnTheGround();
            ApplyIdleGrip(Time.deltaTime);
            AimRifleAtTarget();
            TurnHeadToTarget();
            DrawAimLine();
        }

        /// <summary>Bring his eyes round onto the mark, after the graph has posed him,
        /// but only when the wardrobe says the current take tracks with the head.
        ///
        /// The take holds his head where it was drawn, and the body has been turned by
        /// the HANDS rather than the chest, so the two do not agree: he ends up bladed
        /// with his face still pointed off where the take left it. A man aiming looks at
        /// what he is aiming at.
        ///
        /// A man at ease, turning, jumping or falling does NOT inherit this procedural
        /// glance merely because the target still exists in the scene. Those takes keep
        /// their own authored head motion.
        ///
        /// Only a little, and only the head: the turn is capped so he never wrings his
        /// neck round a mark behind him - past the cap he simply looks as far as he
        /// can, which is what a man does. His look axis comes off the avatar's T-pose
        /// (CrewArms.LookDirection), because a Synty skull's own axes are 36 degrees off
        /// the way it faces.</summary>
        void TurnHeadToTarget()
        {
            if (!_aimAtTarget || !RifleKit.TracksTargetWithHead(_clip) ||
                _animator == null || _target == null) return;
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;

            var wanted = _target.position - head.position;
            if (wanted.sqrMagnitude < 0.04f) return;
            var look = CrewArms.LookDirection(_animator);
            if (look.sqrMagnitude < 1e-4f) return;

            var turn = Quaternion.FromToRotation(look, wanted.normalized);
            head.rotation = Quaternion.RotateTowards(
                head.rotation, turn * head.rotation, HeadTurnCap);
        }

        /// <summary>How far off the take's own head pose he may be turned. A neck, not
        /// a swivel.</summary>
        const float HeadTurnCap = 32f;

        // The support-arm solve is OUT until the hold is settled. It read the weapon's
        // mesh every frame to find the fore-end, and a pack mesh is not readable at
        // runtime - "Not allowed to access vertices on mesh 'SM_Wep_Rifle_01'" once a
        // frame, and the arm dragged to a target it could not reach. If it comes back it
        // measures the fore-end ONCE, in the editor where the mesh can be read, and
        // keeps the offset.

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
            BuildTarget();
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

        /// <summary>The four bodies the M key deals round: this city's own cast, then
        /// the pack's preview figure in three dresses. The takes never change - only who
        /// is wearing them - which is the whole point of having the pack's own body
        /// here: it is the figure they were authored on.</summary>
        enum Body { City, MixamoSyntyFlat, MixamoSyntyAtlas, MixamoOriginal }

        static readonly Body[] Bodies =
        {
            Body.City, Body.MixamoSyntyFlat, Body.MixamoSyntyAtlas, Body.MixamoOriginal
        };

        int _bodyRow;
        GameObject _cityPrefab;

        Body CurrentBody => Bodies[_bodyRow];

        /// <summary>Stand a different body up in his place, on the same take.
        ///
        /// The WHOLE bench is rebuilt, not just the actor. Taking the actor out from
        /// under a running PlayableGraph and hanging a fresh one back on left the graph
        /// valid, with its output, and not playing - the man stood in his bind pose with
        /// his arms out - and chasing that is not worth it when the bench costs a
        /// millisecond to build from nothing. What is carried across is what the man
        /// looking at it chose: the body, the take, the tier and where the camera is.</summary>
        void SwapBody(int row)
        {
            _bodyRow = ((row % Bodies.Length) + Bodies.Length) % Bodies.Length;
            var take = _pinned;
            int tier = _tier, leg = _leg;
            float yaw = _yaw, pitch = _pitch, near = _followDistance, wide = _wideDistance;
            bool follow = _follow;

            if (_graph.IsValid()) _graph.Destroy();
            if (_runtime != null) Destroy(_runtime.gameObject);
            _runtime = null;
            EnsureBuilt();

            _tier = tier;
            _leg = leg;
            _yaw = yaw;
            _pitch = pitch;
            _followDistance = near;
            _wideDistance = wide;
            _follow = follow;
            if (take != null) Pin(take);
            else PlayGait();
        }

        void BuildActor(GameObject prefab)
        {
            _cityPrefab = prefab;
            bool city = CurrentBody == Body.City;
            var source = city ? prefab : MixamoBody.Source;
            if (source == null)
            {
                Debug.LogWarning("[RifleDemo] " + MixamoBody.Path + " is not in the " +
                                 "project; the city body stands in.", this);
                source = prefab;
                city = true;
            }

            // The body hangs under a holder the bench moves, and is offset inside it so
            // its feet are on the ground. A pack body is not obliged to stand with its
            // feet at its own origin - the Mixamo preview figure has its root at the
            // hips, and dropped straight into the scene it stood 1.15 m under the
            // pavement - and nothing about that is worth arguing with: it is measured
            // off the first posed frame and taken out.
            _actor = new GameObject(source.name + " - rifle");
            _actor.transform.SetParent(_runtime, false);
            _actor.transform.SetPositionAndRotation(Corner(0), Quaternion.identity);
            _body = Instantiate(source, _actor.transform).transform;
            _body.localPosition = Vector3.zero;
            _body.localRotation = Quaternion.identity;

            _animator = _actor.GetComponentInChildren<Animator>(true);
            StripCityRuntime(_actor, _animator);

            if (city)
                PedestrianAnthropometry.Apply(
                    _actor, _rng.Next(), PedestrianIdentity.IsFemale(prefab.name),
                    PedestrianAgeCohort.Adult, prefab.name);
            else
                MixamoBody.Dress(_actor, prefab,
                    CurrentBody == Body.MixamoSyntyFlat ? MixamoBody.Skin.SyntyFlat
                    : CurrentBody == Body.MixamoSyntyAtlas ? MixamoBody.Skin.SyntyAtlas
                    : MixamoBody.Skin.Original);

            _animator.enabled = true;
            _animator.runtimeAnimatorController = null;
            // The city never applies root motion: the walker carries his own transform
            // and the take only says how fast it should go. Same here.
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.Rebind();

            // The armoury's own rifle, put in the fist by the same hand the street uses.
            // The review only adds pose-semantic corrections afterwards: target aim and
            // the normal idle's small wrist-to-palm placement. G takes the prop out when
            // the bare authored pose is what is being judged.
            var gunPrefab = CrewArms.ModelForKind(EquipmentKind.Rifle);
            if (gunPrefab != null) _gun = CrewArms.Attach(_animator, gunPrefab);
            else Debug.LogWarning("[RifleDemo] The armoury sells no rifle body - " +
                                  "the review runs with empty hands.", this);

            // THE BENCH SHOWS THE PIECE AT THE SIZE IT WAS AUTHORED. Attach trims a long
            // gun down to the street's length cap - the pack's kalashnikov is 1.15 m and
            // the city carries it at 0.80, a cut to 0.70 - and that cap is the city's
            // business, not this bench's: what is being judged here is how the pack's
            // takes read with a real rifle in the fist. The city keeps its trim.
            if (_gun != null)
            {
                _gun.localScale = Vector3.one;
                CaptureIdleGrip();
            }


            // A NEW GRAPH STARTS FROM NOTHING. The slots remembered which clip they
            // were holding across a rebuild, so Play saw the take it was asked for
            // already sitting on the live slot and returned without building anything -
            // and a mixer with two dead inputs writes the avatar's rest pose. That is
            // the man who stood there with his palms out instead of aiming.
            _slot[0] = default;
            _slot[1] = default;
            _slotClip[0] = null;
            _slotClip[1] = null;
            _live = 0;
            _blend = 1f;
            _clipPace = 0f;

            _graph = PlayableGraph.Create("Rifle Demo");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _output = AnimationPlayableOutput.Create(_graph, "Rifle", _animator);
            _mix = AnimationMixerPlayable.Create(_graph, 2);
            _output.SetSourcePlayable(_mix);
            Play(RifleKit.IdleAiming ?? RifleKit.Idle, 0f, 0f);
            _graph.Play();
            _graph.Evaluate(0f);
            AimRifleAtTarget();
        }

        Transform _body;

        /// <summary>Remember the shared grip and derive the ordinary idle correction
        /// from this rig's own wrist-to-fist direction. This is deliberately not a
        /// global CrewArms nudge: aiming, gunplay and every other take already sit well.</summary>
        void CaptureIdleGrip()
        {
            _gunGripBase = _gun.localPosition;
            _idleGripOffset = Vector3.zero;
            _idleGripBlend = 0f;

            var wrist = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (wrist == null || _gun.parent == null) return;
            var towardFist = CrewArms.GripPoint(_animator, false) - wrist.position;
            if (towardFist.sqrMagnitude < 1e-6f) return;
            _idleGripOffset = _gun.parent.InverseTransformDirection(towardFist.normalized) *
                              IdleGripAdvance;
        }

        /// <summary>Only the plain at-ease idle moves the handle off the wrist and into
        /// the fist. Blend the three-centimetre correction so entering or leaving idle
        /// cannot pop the prop.</summary>
        void ApplyIdleGrip(float dt)
        {
            if (_gun == null) return;
            float wanted = _clip == RifleKit.Idle ? 1f : 0f;
            _idleGripBlend = Mathf.MoveTowards(
                _idleGripBlend, wanted, IdleGripBlendSpeed * Mathf.Max(0f, dt));
            _gun.localPosition = _gunGripBase + _idleGripOffset * _idleGripBlend;
        }

        const float IdleGripAdvance = 0.03f;
        const float IdleGripBlendSpeed = 9f;

        /// <summary>Put the barrel onto the line from its muzzle to the selected mark
        /// whenever the current take is an aiming pose.
        ///
        /// The two hands determine the BODY yaw (TurnToTarget), giving the bladed stance
        /// and keeping the piece between them. They do not determine barrel pitch: a
        /// low hand in a foreign take must not turn the target axis into the pavement.
        /// The selected mark owns the complete three-dimensional aim direction.
        ///
        /// Rotating the prop also moves its child muzzle a few centimetres around the
        /// trigger-hand pivot. A second exact pass removes that tiny parallax, leaving
        /// muzzle.forward parallel with the final muzzle-to-mark indicator. No hand or
        /// arm bone is procedural and the trigger-hand position is never moved.</summary>
        void AimRifleAtTarget()
        {
            if (_gun == null || !_gun.gameObject.activeInHierarchy ||
                _animator == null || !RifleKit.IsAimingPose(_clip)) return;

            if (_aimAtTarget && _target != null)
            {
                var muzzle = CrewArms.MuzzleOf(_gun);
                if (muzzle == null) return;
                for (int pass = 0; pass < 2; pass++)
                {
                    var axis = _target.position - muzzle.position;
                    if (axis.sqrMagnitude < 0.04f) return;
                    CrewArms.FitToAim(_animator, _gun, axis.normalized, Vector3.up);
                }
                return;
            }

            // T disables the target contract, but an aiming take still keeps the prop
            // inside its authored two-hand hold.
            var hands = HandAxis();
            if (hands.sqrMagnitude > 1e-4f)
                CrewArms.FitToAim(_animator, _gun, hands, Vector3.up);
        }

        /// <summary>Put the lowest thing on him on the ground, every frame.
        ///
        /// A pack body is not obliged to stand with its feet at its own origin: the
        /// Mixamo preview figure carries its root at the hips, and dropped straight in
        /// it stood 1.15 m UNDER the pavement. One constant offset does not fix it -
        /// measured off the bind pose he floated, measured off one posed frame he sank
        /// 7.5 cm, because every take holds the hips at its own height and a still
        /// number cannot follow that. So it is not a constant: the sole is put on y = 0
        /// each frame, which is exact and cannot drift.
        ///
        /// This is a BENCH fudge and it is only ever applied to a foreign body. The
        /// city's own cast needs none of it - its rigs stand on their own origin - and
        /// the one thing the fudge flattens is a take that genuinely leaves the ground:
        /// the three jump takes are held down by it, and are read on a city body.</summary>
        void StandHimOnTheGround()
        {
            if (_body == null || _actor == null || CurrentBody == Body.City) return;
            var bounds = BoundsOf(_body.gameObject);
            _body.localPosition += new Vector3(
                0f, _actor.transform.position.y - bounds.min.y, 0f);
        }

        static Bounds BoundsOf(GameObject root)
        {
            var found = false;
            var bounds = default(Bounds);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return found ? bounds : new Bounds(Vector3.up * 0.9f, new Vector3(0.6f, 1.8f, 0.6f));
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
            PlayGait();
        }

        /// <summary>Walk him by hand. W A S D lay the ground he covers in the CAMERA's
        /// frame, which is the only frame a man at a keyboard is thinking in, and the
        /// take is then chosen by where that ground lies relative to the way he faces -
        /// so a man holding the mark on his right and pushed forward plays the pack's
        /// left strafe without being told to. He moves at that take's own pace, the same
        /// oracle the circuit uses.
        ///
        /// Nothing here interrupts him until a key is actually pressed; the first press
        /// takes him off the circuit, and the list (or the circuit row) puts him back.</summary>
        void ReadWalkKeys(float dt)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || _actor == null) return;

            var push = Vector2.zero;
            if (keyboard.wKey.isPressed) push.y += 1f;
            if (keyboard.sKey.isPressed) push.y -= 1f;
            if (keyboard.dKey.isPressed) push.x += 1f;
            if (keyboard.aKey.isPressed) push.x -= 1f;

            if (push.sqrMagnitude < 0.01f)
            {
                if (!_manual) return;
                // let go and he stands, on the mark, in the pack's own aiming stand
                Play(RifleKit.IdleAiming ?? RifleKit.Idle, 0f, 0.25f);
                return;
            }

            if (!_manual)
            {
                _manual = true;
                _pinned = null;
                _held = null;
                SyncPicker(null);
            }

            var eye = _camera != null ? _camera.transform : _actor.transform;
            var ahead = eye.forward; ahead.y = 0f;
            var side = eye.right; side.y = 0f;
            if (ahead.sqrMagnitude < 1e-4f) ahead = Vector3.forward;
            var world = (ahead.normalized * push.y + side.normalized * push.x).normalized;

            var step = StepFor(world);
            var clip = Tiers[_tier] switch
            {
                Tier.Walk => RifleKit.Walk(step),
                Tier.Run => RifleKit.Run(step),
                Tier.Sprint => RifleKit.Sprint(step),
                _ => RifleKit.CrouchWalk(step)
            };
            if (clip == null) return;
            Play(clip, clip.averageSpeed.magnitude, 0.18f);
            _actor.transform.position += world * _clipPace * dt;
        }

        /// <summary>Which of the pack's eight ways covers this piece of ground, given
        /// the way he is facing.</summary>
        RifleStep StepFor(Vector3 world)
        {
            var local = Quaternion.Inverse(_actor.transform.rotation) * world;
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;   // 0 = forward
            int octant = Mathf.RoundToInt(angle / 45f);
            octant = ((octant % 8) + 8) % 8;
            switch (octant)
            {
                case 0: return RifleStep.Forward;
                case 1: return RifleStep.ForwardRight;
                case 2: return RifleStep.Right;
                case 3: return RifleStep.BackwardRight;
                case 4: return RifleStep.Backward;
                case 5: return RifleStep.BackwardLeft;
                case 6: return RifleStep.Left;
                default: return RifleStep.ForwardLeft;
            }
        }

        /// <summary>Which of the pack's eight ways this leg is being taken, read off
        /// the ground he is actually covering against the way he is actually facing.
        ///
        /// The lap used to be a fixed list, because he never turned. Once he turns to
        /// face a target it has to be worked out: a leg that was his forward becomes his
        /// left when he swings 90 degrees onto a target, and the take has to follow or
        /// he moon-walks.</summary>
        RifleStep StepForLeg()
        {
            var world = _legTo - _legFrom;
            world.y = 0f;
            if (world.sqrMagnitude < 1e-4f) return Lap[_leg];
            return StepFor(world.normalized);
        }

        void PlayGait()
        {
            if (_held != null) { Play(_held, 0f, 0.3f); return; }
            var step = StepForLeg();
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
            ReadCameraKeys(dt);
            PlaceTargetFromClick();
            ReadWalkKeys(dt);
            TurnToTarget(dt);
            Blend(dt);
            Loop();

            if (_manual) { }                       // ReadWalkKeys already carried him
            else if (_pinned != null) Drift(dt);
            else if (_held == null) Travel(dt);
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

        /// <summary>Carry a pinned take the way the take itself carries. Its
        /// averageSpeed is in the CLIP's own frame, so it is turned by the man's facing
        /// first; a take that carries nothing leaves him standing.</summary>
        void Drift(float dt)
        {
            if (_pinned == null || _actor == null) return;
            var carry = _actor.transform.rotation * _pinned.averageSpeed;
            carry.y = 0f;
            if (carry.sqrMagnitude < 0.01f) return;
            var next = _actor.transform.position + carry * dt;
            // Round he goes: out to the rim of the ground and back to the middle, so a
            // take can be watched for as long as anybody wants without losing him.
            if (next.sqrMagnitude > (CircuitRadius * 1.15f) * (CircuitRadius * 1.15f))
                next = Vector3.zero;
            _actor.transform.position = next;
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
            if (keyboard.tKey.wasPressedThisFrame) _aimAtTarget = !_aimAtTarget;
            if (keyboard.gKey.wasPressedThisFrame && _gun != null)
                _gun.gameObject.SetActive(!_gun.gameObject.activeSelf);
            if (keyboard.mKey.wasPressedThisFrame) SwapBody(_bodyRow + 1);
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                _tier = (_tier + 1) % Tiers.Length;
                _manual = false;
                _pinned = null;
                _held = null;
                SyncPicker(null);
                PlayGait();
            }
        }

        void SetTier(Tier tier)
        {
            _pinned = null;
            _held = null;
            SyncPicker(null);
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
            Pin(stand);
        }

        /// <summary>Hold him on ONE take, wherever the list points. He plays it on the
        /// spot and is carried the way the take carries him - the clip's averageSpeed is
        /// a VECTOR, so a strafe take goes sideways and a death take goes nowhere,
        /// without this bench having to know which is which. When he has walked far
        /// enough out he is set back at the middle and does it again.</summary>
        void Pin(AnimationClip take)
        {
            if (take == null) return;
            _manual = false;
            _pinned = take;
            _held = take;
            Play(take, take.averageSpeed.magnitude, 0.3f);
            SyncPicker(take);
        }

        /// <summary>Back onto the octagon, from wherever the list left him.</summary>
        void Unpin()
        {
            _pinned = null;
            _held = null;
            _manual = false;
            StartLeg(_leg, first: true);
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

        /// <summary>The mark he answers to, and the line from his muzzle to it.
        ///
        /// A bench that only ever showed a man facing north could not show the one thing
        /// the eight-way set exists for: a man who keeps his weapon on something while
        /// his feet take him somewhere else. The mark is moved with a click.</summary>
        void BuildTarget()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color");

            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "Target";
            post.transform.SetParent(_runtime, false);
            post.transform.localScale = new Vector3(0.16f, 0.9f, 0.16f);
            post.transform.position = new Vector3(0f, 0.9f, CircuitRadius + 6f);
            Destroy(post.GetComponent<Collider>());
            var paint = new Material(shader) { name = "Target paint" };
            paint.SetColor("_BaseColor", DemoUi.Gold);
            paint.color = DemoUi.Gold;
            post.GetComponent<Renderer>().sharedMaterial = paint;
            _target = post.transform;

            var line = new GameObject("Aim line");
            line.transform.SetParent(_runtime, false);
            _aimLine = line.AddComponent<LineRenderer>();
            _aimLine.positionCount = 2;
            _aimLine.widthMultiplier = 0.02f;
            _aimLine.useWorldSpace = true;
            _aimLine.numCapVertices = 2;
            var thread = new Material(shader) { name = "Aim line paint" };
            thread.SetColor("_BaseColor", DemoUi.Accent);
            thread.color = DemoUi.Accent;
            _aimLine.sharedMaterial = thread;
        }

        /// <summary>The line between his two hands, in the world, flattened. This is
        /// where the weapon lies in every two-handed take the pack ships.</summary>
        Vector3 HandLine()
        {
            var line = HandAxis();
            line.y = 0f;
            return line.sqrMagnitude < 1e-4f ? Vector3.zero : line.normalized;
        }

        /// <summary>The full weapon axis from the trigger fist to the supporting hand.
        /// Unlike HandLine this keeps pitch, so a firing take cannot leave the prop
        /// pointing at the pavement while the two hands are aiming ahead.</summary>
        Vector3 HandAxis()
        {
            return CrewArms.HandAimAxis(_animator);
        }

        /// <summary>Swing him onto the mark by his HANDS, not by his chest.
        ///
        /// This is the whole trick, and it is his: the aim axis has to run through both
        /// hands, so turn the man until it does. Point his chest at the mark instead and
        /// he stands square with the rifle lying across him at whatever angle the take
        /// holds it - a peasant with a plank. Turned by the hand line he ends up BLADED,
        /// left shoulder forward, which is how a man holds a rifle on something and is
        /// what the take was drawn as in the first place.
        ///
        /// Exact, not iterative: the hand line is rigid in his own frame, so the offset
        /// between it and his facing is read there once a frame and simply subtracted
        /// from the bearing to the mark.</summary>
        void TurnToTarget(float dt)
        {
            if (!_aimAtTarget || _actor == null || _target == null) return;
            var to = _target.position - _actor.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.04f) return;

            float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            var hands = HandLine();
            if (hands.sqrMagnitude > 0.01f)
            {
                var local = Quaternion.Inverse(_actor.transform.rotation) * hands;
                bearing -= Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            }

            _actor.transform.rotation = Quaternion.RotateTowards(
                _actor.transform.rotation, Quaternion.Euler(0f, bearing, 0f), 220f * dt);
        }

        /// <summary>The aiming contract: from the bore to the selected mark. In target
        /// mode its end is the mark itself, never a projection of an already-wrong gun
        /// direction. With targeting disabled it becomes an ordinary barrel guide.</summary>
        void DrawAimLine()
        {
            if (_aimLine == null || _actor == null) return;
            var muzzle = _gun != null && _gun.gameObject.activeInHierarchy
                ? CrewArms.MuzzleOf(_gun)
                : null;
            var from = muzzle != null ? muzzle.position
                : _actor.transform.position + Vector3.up * 1.35f;
            if (_aimAtTarget && _target != null)
            {
                _aimLine.SetPosition(0, from);
                _aimLine.SetPosition(1, _target.position);
                return;
            }

            var along = muzzle != null
                ? muzzle.forward
                : _actor.transform.forward;
            _aimLine.SetPosition(0, from);
            _aimLine.SetPosition(1, from + along * 8f);
        }

        /// <summary>Put the mark where he clicked. The ground is a plane at y = 0 and
        /// the click is a ray onto it - no collider on the ground to be dragged around
        /// by, and none wanted.</summary>
        void PlaceTargetFromClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || _camera == null || _target == null) return;
            if (!mouse.leftButton.wasPressedThisFrame || PointerOverUi()) return;

            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            if (Mathf.Abs(ray.direction.y) < 1e-3f) return;
            float distance = -ray.origin.y / ray.direction.y;
            if (distance <= 0f) return;
            var hit = ray.GetPoint(distance);
            _target.position = new Vector3(hit.x, 0.9f, hit.z);
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

        // The camera's own frame: where it stands round him, how high, how far. Yaw 0
        // is due south of him - looking north, the way he faces all lap - so the shot
        // the bench opens on is the one the eight legs were laid out to be read in.
        const float HomeYaw = 0f, HomePitch = 20f, HomeFollowDistance = 4.9f;
        float _yaw = HomeYaw, _pitch = HomePitch;
        float _followDistance = HomeFollowDistance;
        float _wideDistance = CircuitRadius * 1.9f;

        /// <summary>Two ways to look at him, C between them. Following is the one that
        /// reviews the animation - a man twelve metres across the shot is too small to
        /// see a foot slip. The wide shot is for reading the circuit itself: he never
        /// turns, so the leg he is on IS the direction the take is authored for.
        ///
        /// NOTE WHAT THE CAMERA IS FOR HERE. Swung round behind him it stops telling you
        /// which way the take carries him - a backward run looks like a forward one from
        /// the front. Home (backspace) is always the shot that reads: due south, so
        /// left is left. Turn it to inspect the hold and the hands, turn it back to
        /// judge the direction.</summary>
        void Frame()
        {
            if (_camera == null) return;
            var target = _follow && _actor != null
                ? _actor.transform.position + Vector3.up * 1.05f
                : new Vector3(0f, 1f, 0f);
            float distance = _follow ? _followDistance : _wideDistance;
            var offset = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back * distance;
            var eye = target + offset;
            if (eye.y < 0.35f) eye.y = 0.35f;          // never under the ground
            _camera.transform.position = eye;
            _camera.transform.rotation = Quaternion.LookRotation(target - eye, Vector3.up);
        }

        /// <summary>Mouse and keys on the camera. Held mouse button turns it, wheel
        /// pulls it in and out, the arrows do the same from the keyboard for anyone
        /// driving this from a laptop pad, backspace puts it home.</summary>
        void ReadCameraKeys(float dt)
        {
            var mouse = Mouse.current;
            // A drag that started on the list is the list's, not the camera's.
            if (mouse != null && !PointerOverUi())
            {
                // the LEFT button belongs to the mark now; the camera turns on the
                // right or the middle one
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
                {
                    var drag = mouse.delta.ReadValue();
                    _yaw += drag.x * 0.22f;
                    _pitch -= drag.y * 0.14f;
                }

                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) Zoom(1f - Mathf.Clamp(wheel, -6f, 6f) * 0.06f);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed) _yaw -= 70f * dt;
                if (keyboard.rightArrowKey.isPressed) _yaw += 70f * dt;
                if (keyboard.upArrowKey.isPressed) _pitch += 45f * dt;
                if (keyboard.downArrowKey.isPressed) _pitch -= 45f * dt;
                if (keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed)
                    Zoom(1f - 1.2f * dt);
                if (keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed)
                    Zoom(1f + 1.2f * dt);
                if (keyboard.backspaceKey.wasPressedThisFrame)
                {
                    _yaw = HomeYaw;
                    _pitch = HomePitch;
                    _followDistance = HomeFollowDistance;
                    _wideDistance = CircuitRadius * 1.9f;
                }
            }

            // Kept off the poles: straight down on a man's cap says nothing about his
            // gait, and straight up through the ground says less.
            _pitch = Mathf.Clamp(_pitch, -12f, 82f);
        }

        static bool PointerOverUi() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        void Zoom(float factor)
        {
            if (_follow) _followDistance = Mathf.Clamp(_followDistance * factor, 1.4f, 26f);
            else _wideDistance = Mathf.Clamp(_wideDistance * factor, 6f, 90f);
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
            // A GraphicRaycaster, unlike the crowd's HUDs: the take list is meant to be
            // clicked. Nothing else on this canvas is a raycast target, and the camera
            // drag asks the pointer stack before it turns anything (PointerOverUi), so
            // the list never eats a drag meant for the world.
            canvasObject.AddComponent<GraphicRaycaster>();

            _caption = DemoUi.Text(canvasObject.transform, "Caption", 26f,
                DemoUi.Ink, TextAlignmentOptions.TopLeft);
            var rect = _caption.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(28f, -24f);
            rect.sizeDelta = new Vector2(760f, 120f);
            _caption.textWrappingMode = TextWrappingModes.Normal;
            BuildPicker(canvasObject.transform);
        }

        /// <summary>The whole pack in a list, in the order RifleKit keeps it. The first
        /// row is the circuit itself, so there is always a way back out of a single
        /// take without reaching for the keyboard.</summary>
        void BuildPicker(Transform canvas)
        {
            EnsureEventSystem();
            _takes.Clear();
            _takes.Add(null);                       // row 0: back to the circuit
            foreach (var clip in RifleKit.All)
                if (clip != null) _takes.Add(clip);

            _picker = DemoUi.Dropdown(canvas, "Take picker", 300f);
            var rect = _picker.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);

            var rows = new List<TMP_Dropdown.OptionData>();
            foreach (var clip in _takes)
                rows.Add(new TMP_Dropdown.OptionData(clip == null ? "the circuit" : clip.name));
            _picker.options = rows;
            _picker.SetValueWithoutNotify(0);
            _picker.RefreshShownValue();
            _picker.onValueChanged.AddListener(row =>
            {
                if (row <= 0) Unpin();
                else Pin(_takes[row]);
            });
        }

        readonly List<AnimationClip> _takes = new List<AnimationClip>();

        /// <summary>Keep the list showing what he is actually playing when the keys, and
        /// not the list, chose it. Set without notifying: the listener would answer its
        /// own write and pin him all over again.</summary>
        void SyncPicker(AnimationClip take)
        {
            if (_picker == null) return;
            int row = take == null ? 0 : Mathf.Max(0, _takes.IndexOf(take));
            if (_picker.value == row) return;
            _picker.SetValueWithoutNotify(row);
            _picker.RefreshShownValue();
        }

        /// <summary>Whichever layer gets here first brings the pointer stack. Not
        /// StandaloneInputModule: the legacy module throws under this project's Input
        /// System setting, the same note StreetHud keeps.</summary>
        static void EnsureEventSystem()
        {
            if (EventSystem.current) return;
            var host = new GameObject("EventSystem");
            host.AddComponent<EventSystem>();
            host.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
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
                       "space: next tier\n" +
                       "w a s d: walk him   |   click: move the mark   |   t: " +
                       (_aimAtTarget ? "on the mark" : "facing free") +
                       "\n" +
                       "camera: right-drag to turn, wheel to zoom, arrows the same, " +
                       "c wide shot, backspace home   |   g: the rifle prop   |   " +
                       "m: " + CurrentBody + "   |   list: any take on its own";
            if (line == _captionShown) return;
            _captionShown = line;
            _caption.text = line;
        }
    }
}
