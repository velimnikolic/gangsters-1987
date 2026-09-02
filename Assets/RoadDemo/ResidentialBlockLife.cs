using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RoadDemo
{
    /// <summary>
    /// One scheduler for all decorative people on a generated block. Each visible rig has
    /// a continuous Unity animation graph, while its short prop-to-prop route is baked from
    /// the block's static geometry; figures are not registered pedestrians and do not own
    /// navigation, perception, combat or collision components.
    /// Their only city-wide reaction is to hear <see cref="StreetAlarm"/> and disappear
    /// through the nearest authored doorway.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResidentialBlockLife : MonoBehaviour
    {
        static readonly List<ResidentialBlockLife> VisiblePopulations =
            new List<ResidentialBlockLife>();

        internal static IReadOnlyList<ResidentialBlockLife> ActivePopulations =>
            VisiblePopulations;

        internal enum Routine
        {
            Pose,
            Shuttle,
            Door,
            Trash,
            Activity,
            Conversation,
            Seated,
            SeatedConversation,
        }

        [Serializable]
        internal struct Shelter
        {
            public Vector3 Outside;
            public Vector3 Inside;

            public Shelter(Vector3 outside, Vector3 inside)
            {
                Outside = outside;
                Inside = inside;
            }
        }

        [Serializable]
        sealed class Actor
        {
            public GameObject Root;
            public Transform Tf;
            public Animator Animator;
            public GameObject Carry;
            public AnimationClip Action;
            public AnimationClip Secondary;
            public Routine Job;
            public Vector3 A, B;
            public Vector3 Via;
            public bool HasVia;
            public Vector3 Departure;
            public bool HasDeparture;
            public float YawA, YawB;
            public float Phase, Period, Cadence;

            public float PanicSpeed;

            [NonSerialized] internal AmbientMotion Motion;
            [NonSerialized] public float StartDelay;
            [NonSerialized] public bool OverlayWasOn;
            [NonSerialized] public bool PanicDone;
            [NonSerialized] public int PanicLeg;
            [NonSerialized] public Vector3[] PanicRoute;
            [NonSerialized] public bool PanicStanding;
            [NonSerialized] public float PanicStandTime, PanicStandDuration;
        }

        const float Arrived = 0.09f;
        const int CurrentDataVersion = 2;

        // ResidentialDemo is composed in edit mode and then saved. These are therefore
        // scene data, not transient runtime caches: without serialization the figures
        // survive but every route disappears on the next domain reload.
        [SerializeField] List<Actor> _actors = new List<Actor>(12);
        [SerializeField] Shelter[] _shelters = Array.Empty<Shelter>();
        [SerializeField] AnimationClip _idle;
        [SerializeField] AnimationClip _walk;
        [SerializeField] AnimationClip _run;
        [SerializeField] AnimationClip _standUp;
        [SerializeField] Vector3 _blockCentre;
        [SerializeField] float _blockRadius;
        [SerializeField] int _dataVersion;

        float _startedAt;
        float _startOffset;
        bool _started;
        bool _panicking;
        AmbientStandingClearance _runtimeClearance;

        // A gunshot can be heard by several streamed blocks at once. Building every
        // block's renderer clearance and every actor-to-door route inside OnShot made
        // the first round pay the whole neighbourhood's navigation bill in one frame.
        // The shared queue below performs one small route attempt at a time under one
        // city-wide frame budget; actors hold their current pose for that brief startle
        // beat and begin running as soon as their own route is ready.
        const double PanicPrepareBudgetMs = 1.5;
        const int PanicPrepareStepCap = 32;
        static readonly Queue<ResidentialBlockLife> PanicPrepareQueue =
            new Queue<ResidentialBlockLife>();
        static int _panicPrepareFrame = -1;

        bool _panicQueued;
        bool _panicPrepared;
        int _panicActorIndex;
        Actor _panicPreparing;
        Vector3 _panicCurrent;
        Vector3 _panicRouteStart;
        int[] _panicShelterOrder = Array.Empty<int>();
        int _panicShelterCursor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPanicPreparation()
        {
            PanicPrepareQueue.Clear();
            _panicPrepareFrame = -1;
            VisiblePopulations.Clear();
        }

        internal void Configure(IReadOnlyList<Shelter> shelters, AnimationClip idle,
                                AnimationClip walk, AnimationClip run, AnimationClip standUp,
                                Vector3 blockCentre, float blockRadius, float sampleOffset)
        {
            _shelters = shelters == null || shelters.Count == 0
                ? Array.Empty<Shelter>()
                : Copy(shelters);
            _idle = idle;
            _walk = walk != null ? walk : idle;
            _run = run != null ? run : _walk;
            _standUp = standUp != null ? standUp : _idle;
            _blockCentre = blockCentre;
            _blockRadius = Mathf.Max(1f, blockRadius);
            _startOffset = Mathf.Max(0f, sampleOffset);
            _dataVersion = CurrentDataVersion;
        }

        /// <summary>Runtime composition has already scanned this exact block to place its
        /// figures. Retain that immutable local-space clearance instead of scanning every
        /// renderer in the hierarchy again when the first shot is heard. Saved scenes
        /// simply rebuild it later through the budgeted panic queue.</summary>
        internal void UseRuntimeClearance(AmbientStandingClearance clearance)
        {
            if (clearance != null) _runtimeClearance = clearance;
        }

        static Shelter[] Copy(IReadOnlyList<Shelter> source)
        {
            var copy = new Shelter[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }

        internal void Add(GameObject root, Animator animator, GameObject carry,
                          AnimationClip action, AnimationClip secondary, Routine routine,
                          Vector3 a, Vector3 b, float yawA, float yawB,
                          float phase, float period, float cadence,
                          bool hasDeparture = false, Vector3 departure = default,
                          bool hasVia = false, Vector3 via = default)
        {
            if (root == null) return;
            float actualCadence = Mathf.Max(0.72f, cadence);
            float actionPeriod = action != null ? action.length / actualCadence + 3.2f : 8f;
            if (routine == Routine.Trash && action != null)
                actionPeriod = Mathf.Max(actionPeriod,
                    action.length / actualCadence / 0.16f + 0.2f);
            var actor = new Actor
            {
                Root = root,
                Tf = root.transform,
                Animator = animator,
                Carry = carry,
                Action = action != null ? action : _idle,
                Secondary = secondary,
                Job = routine,
                A = a,
                B = b,
                HasVia = hasVia,
                Via = via,
                HasDeparture = hasDeparture,
                Departure = departure,
                YawA = yawA,
                YawB = yawB,
                Phase = phase,
                Period = Mathf.Max(8f, period, actionPeriod),
                Cadence = actualCadence,
                PanicSpeed = 4.2f + Mathf.Repeat(phase * 0.37f, 1.15f),
            };
            _actors.Add(actor);
            if (_started) InitializeActor(actor);
        }

        void OnEnable()
        {
            if (Application.isPlaying && !VisiblePopulations.Contains(this))
                VisiblePopulations.Add(this);
            if (Application.isPlaying) StreetAlarm.OnShot += Shot;
            if (_panicking && !_panicPrepared) SchedulePanicPreparation();
            if (!_started) return;
            for (int i = 0; i < _actors.Count; i++) _actors[i]?.Motion?.Play();
        }

        void OnDisable()
        {
            VisiblePopulations.Remove(this);
            StreetAlarm.OnShot -= Shot;
            if (!_started) return;
            for (int i = 0; i < _actors.Count; i++) _actors[i]?.Motion?.Pause();
        }

        void OnDestroy()
        {
            VisiblePopulations.Remove(this);
            _panicQueued = false;
            for (int i = 0; i < _actors.Count; i++) _actors[i]?.Motion?.Dispose();
        }

        void Start()
        {
            RepairSerializedState();
            bool needsRecovery = _actors.Count == 0 || _dataVersion < CurrentDataVersion;
            if (needsRecovery)
            {
                // Version 1 serialized the original direct routes. Rebuild from the
                // existing semantic children so seat contacts and prop-safe paths are
                // corrected even when that old actor list is present in the scene.
                _actors.Clear();
                RecoverLegacyBakedState();
                _dataVersion = CurrentDataVersion;
            }
            // This is intentionally content-driven rather than keyed only to an empty
            // actor list. An older scene may have since serialized its original people
            // while still containing no restaurant guests at all.
            ResidentialBlocks.SupplementLegacyCafePeople(
                this, transform.parent != null ? transform.parent : transform,
                _runtimeClearance);

            _startedAt = Time.time + _startOffset;
            for (int i = 0; i < _actors.Count; i++) InitializeActor(_actors[i]);
            _started = true;

            // A holder can be recycled into earshot after the first round. Do not let a
            // newly composed restaurant calmly repopulate in the middle of the incident.
            if (!Application.isPlaying || _panicking) return;
            Vector3 centre = transform.TransformPoint(_blockCentre);
            if (StreetAlarm.HeardSince(centre, 8f, out _)) BeginPanic();
        }

        void Update()
        {
            TickPanicPreparation();
            if (_actors.Count == 0) return;
            float now = Time.time;
            float dt = Time.deltaTime;

            for (int i = 0; i < _actors.Count; i++)
            {
                var actor = _actors[i];
                if (!Owned(actor)) continue;
                if (_panicking) ApplyPanic(actor, dt);
                else ApplyNormal(actor, now);
                actor.Motion?.Tick(dt);
            }
        }

        void InitializeActor(Actor actor)
        {
            if (!Owned(actor) || actor.Animator == null || actor.Motion != null) return;
            actor.StartDelay = 0.35f + Mathf.Repeat(actor.Phase * 0.618f, 3.4f);
            actor.Animator.runtimeAnimatorController = null;
            actor.Animator.applyRootMotion = false;
            actor.Animator.cullingMode = AnimatorCullingMode.CullCompletely;
            actor.Animator.enabled = true;
            actor.Motion = new AmbientMotion(actor.Animator, _idle, _walk, _run,
                                             actor.Action, actor.Secondary, _standUp,
                                             actor.Phase, IsSeated(actor.Job));
        }

        bool Owned(Actor actor) => actor?.Root != null && actor.Tf != null &&
                                   actor.Tf.parent == transform;

        internal int VisionActorCount => _actors.Count;

        internal Transform VisionActorAt(int index)
        {
            if (index < 0 || index >= _actors.Count)
                return null;
            var actor = _actors[index];
            return Owned(actor) ? actor.Tf : null;
        }

        void RepairSerializedState()
        {
            _actors ??= new List<Actor>(12);
            _shelters ??= Array.Empty<Shelter>();
            _idle ??= CrewKit.StockIdle;
            _walk ??= CrewKit.StockWalk ?? _idle;
            _run ??= CrewKit.StockRun ?? _walk;
            _standUp ??= ResidentialBlocks.RecoverAmbientStandUp() ?? _idle;
            for (int i = _actors.Count - 1; i >= 0; i--)
            {
                var actor = _actors[i];
                if (actor?.Root == null)
                {
                    _actors.RemoveAt(i);
                    continue;
                }
                actor.Tf = actor.Root.transform;
                actor.Animator ??= actor.Root.GetComponentInChildren<Animator>(true);
                if (actor.Animator != null)
                {
                    actor.Animator.runtimeAnimatorController = null;
                    actor.Animator.applyRootMotion = false;
                    actor.Animator.cullingMode = AnimatorCullingMode.CullCompletely;
                    actor.Animator.enabled = false;
                }
                actor.Action ??= _idle;
                if (actor.Job == Routine.SeatedConversation && actor.Secondary == null)
                    actor.Secondary = ResidentialBlocks.RecoverAmbientSecondary(actor.Root.name);
                actor.Period = Mathf.Max(8f, actor.Period);
                actor.Cadence = Mathf.Max(0.72f, actor.Cadence);
                if ((actor.Job == Routine.Activity || actor.Job == Routine.Conversation) &&
                    actor.Action != null)
                    actor.Period = Mathf.Max(actor.Period,
                        actor.Action.length / actor.Cadence + 3.2f);
                if (actor.Job == Routine.Trash && actor.Action != null)
                    actor.Period = Mathf.Max(actor.Period,
                        actor.Action.length / actor.Cadence / 0.16f + 0.2f);
                if (actor.PanicSpeed <= 0f)
                    actor.PanicSpeed = 4.2f + Mathf.Repeat(actor.Phase * 0.37f, 1.15f);
            }
        }

        /// <summary>
        /// Compatibility for ResidentialDemo scenes saved by the first implementation.
        /// Those scenes contain the figures and this controller, but no serialized route
        /// list. Rebind the existing children by their semantic labels so animation and
        /// panic work immediately; the next shared regeneration writes the exact routes.
        /// </summary>
        void RecoverLegacyBakedState()
        {
            var block = transform.parent != null ? transform.parent : transform;
            var clearance = new AmbientStandingClearance(block, transform);
            _runtimeClearance = clearance;
            var plannedShelters = ResidentialBlocks.RecoverAmbientShelters(
                block.name, out ResidentialLot.Plan recoveredPlan);
            if (_shelters.Length == 0 && plannedShelters.Count > 0)
                _shelters = plannedShelters.ToArray();
            var seats = ResidentialBlocks.RecoverAmbientSeats(block, recoveredPlan);
            var claimedSeats = new HashSet<int>();
            if (_blockRadius <= 1f)
            {
                _blockCentre = clearance.Centre;
                _blockRadius = Mathf.Max(4f, clearance.Radius);
            }

            if (_shelters.Length == 0)
            {
                var recovered = new List<Shelter>();
                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (child.name.IndexOf("entering and leaving", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    Vector3 outside = child.localPosition;
                    Vector3 inward = _blockCentre - outside;
                    inward.y = 0f;
                    if (inward.sqrMagnitude < 0.01f) inward = child.forward;
                    inward.Normalize();
                    recovered.Add(new Shelter(outside, outside + inward * 1.15f));
                }
                if (recovered.Count == 0)
                {
                    Vector3 outside = _blockCentre + Vector3.back * Mathf.Min(3f, _blockRadius * 0.2f);
                    recovered.Add(new Shelter(outside, outside + Vector3.forward * 1.15f));
                }
                _shelters = recovered.ToArray();
            }

            bool hasVisibleShuttle = false;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                var animator = child.GetComponentInChildren<Animator>(true);
                if (animator == null) continue;
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
                animator.enabled = false;

                string label = child.name;
                bool seated = IsSeatedLabel(label);
                Vector3 at = child.localPosition;
                bool hasDeparture = false;
                Vector3 departure = default;
                if (seated && TryClaimSeat(seats, claimedSeats, at, out var seat))
                {
                    at = seat.At;
                    departure = seat.Departure;
                    if (!clearance.IsClear(departure) &&
                        clearance.TryNearest(departure, clearance.ContentRect,
                                             out Vector3 clearDeparture, 2.4f))
                        departure = clearDeparture;
                    hasDeparture = clearance.IsClear(departure);
                    child.localPosition = at;
                    child.localRotation = Quaternion.Euler(0f, seat.Yaw, 0f);
                }
                else if (!seated && !clearance.IsClear(at))
                {
                    if (!clearance.TryNearest(at, clearance.ContentRect, out Vector3 clear, 6f))
                    {
                        // A decorative person is better omitted than left intersecting a
                        // car, wall or large prop in an older baked scene.
                        child.gameObject.SetActive(false);
                        continue;
                    }
                    at = clear;
                    child.localPosition = at;
                }

                bool female = child.name.IndexOf("Female", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              child.GetComponentInChildren<SkinnedMeshRenderer>(true)?.name
                                  .IndexOf("Female", StringComparison.OrdinalIgnoreCase) >= 0;
                var actor = new Actor
                {
                    Root = child.gameObject,
                    Tf = child,
                    Animator = animator,
                    Carry = CarriedProp(child),
                    Action = ResidentialBlocks.RecoverAmbientPose(label, female),
                    Secondary = ResidentialBlocks.RecoverAmbientSecondary(label),
                    Job = RecoveredRoutine(label),
                    A = at,
                    B = at,
                    HasDeparture = hasDeparture,
                    Departure = departure,
                    YawA = child.localEulerAngles.y,
                    YawB = child.localEulerAngles.y,
                    Phase = LegacyPhase(label, i),
                    Period = 22f + (i % 5),
                    Cadence = 0.82f + (i % 4) * 0.035f,
                    PanicSpeed = 4.2f + (i % 4) * 0.24f,
                };

                if (label.IndexOf("entering and leaving", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int door = NearestShelter(at);
                    var recoveredDoor = _shelters[door];
                    Vector3 pause = clearance.IsPathClear(recoveredDoor.Outside, at)
                        ? at
                        : recoveredDoor.Outside;
                    actor.Job = Routine.Door;
                    actor.A = recoveredDoor.Inside;
                    actor.B = pause;
                    actor.HasVia = true;
                    actor.Via = recoveredDoor.Outside;
                    actor.YawA = Yaw(recoveredDoor.Outside, recoveredDoor.Inside);
                    actor.YawB = actor.YawA;
                    child.localPosition = actor.A;
                    child.gameObject.SetActive(false);
                }
                else if (label.IndexOf("Taking rubbish", StringComparison.OrdinalIgnoreCase) >= 0 &&
                         TryRecoveredBin(block, at, clearance, out Vector3 approach))
                {
                    actor.Job = Routine.Trash;
                    actor.B = approach;
                    actor.YawB = Yaw(approach, at);
                }
                else if (IsShortShuttle(label))
                {
                    Vector3 direction = child.localRotation * Vector3.forward;
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
                    Vector3 other = at + direction.normalized * 1.6f;
                    if (clearance.IsClear(other) && clearance.IsPathClear(at, other))
                    {
                        actor.Job = Routine.Shuttle;
                        actor.B = other;
                        actor.YawB = Yaw(other, at);
                    }
                }

                if (actor.Job == Routine.Shuttle) hasVisibleShuttle = true;
                actor.Cadence = Mathf.Max(0.72f, actor.Cadence);
                if ((actor.Job == Routine.Activity || actor.Job == Routine.Conversation) &&
                    actor.Action != null)
                    actor.Period = Mathf.Max(actor.Period,
                        actor.Action.length / actor.Cadence + 3.2f);
                if (actor.Job == Routine.Trash && actor.Action != null)
                    actor.Period = Mathf.Max(actor.Period,
                        actor.Action.length / actor.Cadence / 0.16f + 0.2f);
                _actors.Add(actor);
            }

            // Door/bin loops can spend a long time paused or hidden. The old baked scene
            // therefore gets one ordinary resident who is visibly crossing clear ground.
            if (hasVisibleShuttle || PromoteLegacyWalker(clearance, preferredOnly: true) ||
                PromoteLegacyWalker(clearance, preferredOnly: false))
                return;
        }

        static bool TryClaimSeat(List<ResidentialBlocks.SeatAnchor> seats,
                                 HashSet<int> claimed, Vector3 near,
                                 out ResidentialBlocks.SeatAnchor seat)
        {
            int best = -1;
            float bestDistance = 3.5f * 3.5f;
            for (int i = 0; i < seats.Count; i++)
            {
                if (claimed.Contains(i)) continue;
                Vector3 delta = seats[i].At - near;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            if (best < 0)
            {
                seat = default;
                return false;
            }
            claimed.Add(best);
            seat = seats[best];
            return true;
        }

        static Routine RecoveredRoutine(string label)
        {
            if (label.IndexOf("guest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("customer", StringComparison.OrdinalIgnoreCase) >= 0)
                return Routine.SeatedConversation;
            if (label.IndexOf("sitting", StringComparison.OrdinalIgnoreCase) >= 0)
                return Routine.Seated;
            if (label.IndexOf("talk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("salesman", StringComparison.OrdinalIgnoreCase) >= 0)
                return Routine.Conversation;
            if (label.IndexOf("Training", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Stretching", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Warming", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Inspect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Looking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Comparing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Watching", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("dumbbells", StringComparison.OrdinalIgnoreCase) >= 0)
                return Routine.Activity;
            return Routine.Pose;
        }

        bool PromoteLegacyWalker(AmbientStandingClearance clearance, bool preferredOnly)
        {
            for (int i = 0; i < _actors.Count; i++)
            {
                var actor = _actors[i];
                if (actor?.Root == null || actor.Job != Routine.Pose ||
                    IsSeatedLabel(actor.Root.name)) continue;

                string label = actor.Root.name;
                bool preferred = label.IndexOf("Waiting outside", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 label.IndexOf("taking a break", StringComparison.OrdinalIgnoreCase) >= 0;
                if (preferredOnly != preferred) continue;

                float startYaw = actor.Tf.localEulerAngles.y + 90f;
                for (int direction = 0; direction < 8; direction++)
                {
                    Vector3 along = Quaternion.Euler(0f, startYaw + direction * 45f, 0f) *
                                    Vector3.forward;
                    for (float distance = 1.6f; distance >= 1.0f; distance -= 0.3f)
                    {
                        Vector3 other = actor.A + along * distance;
                        if (!clearance.IsClear(other) ||
                            !clearance.IsPathClear(actor.A, other)) continue;
                        actor.Job = Routine.Shuttle;
                        actor.B = other;
                        actor.YawA = Yaw(other, actor.A);
                        actor.YawB = Yaw(actor.A, other);
                        return true;
                    }
                }
            }
            return false;
        }

        static bool IsSeatedLabel(string label) =>
            label.IndexOf("sitting", StringComparison.OrdinalIgnoreCase) >= 0 ||
            label.IndexOf("guest", StringComparison.OrdinalIgnoreCase) >= 0 ||
            label.IndexOf("customer", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool IsSeated(Routine routine) =>
            routine == Routine.Seated || routine == Routine.SeatedConversation;

        static bool IsShortShuttle(string label) =>
            label.IndexOf("Moving between", StringComparison.OrdinalIgnoreCase) >= 0 ||
            label.IndexOf("Walking through", StringComparison.OrdinalIgnoreCase) >= 0 ||
            label.IndexOf("Crossing basketball", StringComparison.OrdinalIgnoreCase) >= 0;

        static float LegacyPhase(string label, int index)
        {
            int hash = unchecked(index * 73856093 + 1987);
            for (int i = 0; i < label.Length; i++) hash = unchecked(hash * 31 + label[i]);
            return (hash & 0x7fffffff) % 1900 / 100f;
        }

        static GameObject CarriedProp(Transform actor)
        {
            var children = actor.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
                if (children[i] != actor &&
                    children[i].name.IndexOf("Carried rubbish", StringComparison.OrdinalIgnoreCase) >= 0)
                    return children[i].gameObject;
            return null;
        }

        bool TryRecoveredBin(Transform block, Vector3 from, AmbientStandingClearance clearance,
                             out Vector3 approach)
        {
            approach = default;
            float best = float.MaxValue;
            var children = block.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                var candidate = children[i];
                if (candidate.IsChildOf(transform)) continue;
                string name = candidate.name;
                if (name.IndexOf("Trashbin", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("Trash_Bin", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Vector3 bin = block.InverseTransformPoint(candidate.position);
                Vector3 away = from - bin;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                Vector3 at = bin + away.normalized * 0.82f;
                at.y = from.y;
                float distance = (from - at).sqrMagnitude;
                if (distance >= best || !clearance.IsClear(at) ||
                    !clearance.IsPathClear(from, at)) continue;
                best = distance;
                approach = at;
            }
            return best < float.MaxValue;
        }

        static float Yaw(Vector3 towards, Vector3 from)
        {
            Vector3 direction = towards - from;
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        void ApplyNormal(Actor actor, float now)
        {
            float elapsed = now - _startedAt - actor.StartDelay;
            if (elapsed < 0f)
            {
                Carry(actor, false);
                actor.Motion?.SetOverlay(false);
                if (actor.Job == Routine.Door) Visible(actor, false);
                else
                {
                    Visible(actor, true);
                    Place(actor, actor.A, actor.YawA);
                    actor.Motion?.Select(IsSeated(actor.Job) || actor.Job == Routine.Pose
                        ? AmbientMotion.BasePose.Action
                        : AmbientMotion.BasePose.Idle, true, actor.Cadence);
                }
                return;
            }

            float cycle = Mathf.Repeat(elapsed, actor.Period) / actor.Period;
            var pose = AmbientMotion.BasePose.Action;
            bool loop = true;
            float playback = actor.Cadence;
            bool overlay = false;

            switch (actor.Job)
            {
                case Routine.Door:
                    // Hidden inside -> walk out -> linger -> walk back -> hidden inside.
                    if (cycle < 0.12f || cycle >= 0.88f)
                    {
                        Visible(actor, false);
                        actor.Motion?.SetOverlay(false);
                        return;
                    }
                    Visible(actor, true);
                    if (cycle < 0.35f)
                    {
                        float t = Smooth((cycle - 0.12f) / 0.23f);
                        Travel(actor, actor.A, actor.B, t);
                        pose = AmbientMotion.BasePose.Walk;
                        playback = GaitPlayback(_walk, Vector3.Distance(actor.A, actor.B),
                                                actor.Period * 0.23f, actor.Cadence);
                    }
                    else if (cycle < 0.65f)
                    {
                        Place(actor, actor.B, actor.YawB);
                    }
                    else
                    {
                        float t = Smooth((cycle - 0.65f) / 0.23f);
                        Travel(actor, actor.B, actor.A, t);
                        pose = AmbientMotion.BasePose.Walk;
                        playback = GaitPlayback(_walk, Vector3.Distance(actor.A, actor.B),
                                                actor.Period * 0.23f, actor.Cadence);
                    }
                    Carry(actor, false);
                    break;

                case Routine.Shuttle:
                    Visible(actor, true);
                    if (cycle < 0.18f)
                        Place(actor, actor.A, actor.YawA);
                    else if (cycle < 0.40f)
                    {
                        Travel(actor, actor.A, actor.B, Smooth((cycle - 0.18f) / 0.22f));
                        pose = AmbientMotion.BasePose.Walk;
                        playback = GaitPlayback(_walk, Vector3.Distance(actor.A, actor.B),
                                                actor.Period * 0.22f, actor.Cadence);
                    }
                    else if (cycle < 0.70f)
                        Place(actor, actor.B, actor.YawB);
                    else if (cycle < 0.92f)
                    {
                        Travel(actor, actor.B, actor.A, Smooth((cycle - 0.70f) / 0.22f));
                        pose = AmbientMotion.BasePose.Walk;
                        playback = GaitPlayback(_walk, Vector3.Distance(actor.A, actor.B),
                                                actor.Period * 0.22f, actor.Cadence);
                    }
                    else Place(actor, actor.A, actor.YawA);
                    Carry(actor, false);
                    break;

                case Routine.Trash:
                    Visible(actor, true);
                    if (cycle < 0.30f)
                    {
                        Travel(actor, actor.A, actor.B, Smooth(cycle / 0.30f));
                        Carry(actor, true);
                        pose = AmbientMotion.BasePose.Walk;
                        playback = GaitPlayback(_walk, Vector3.Distance(actor.A, actor.B),
                                                actor.Period * 0.30f, actor.Cadence);
                    }
                    else if (cycle < 0.46f)
                    {
                        Place(actor, actor.B, actor.YawB);
                        Carry(actor, cycle < 0.38f);
                        loop = false;
                    }
                    else if (cycle < 0.72f)
                    {
                        Travel(actor, actor.B, actor.A, Smooth((cycle - 0.46f) / 0.26f));
                        Carry(actor, false);
                        pose = AmbientMotion.BasePose.Walk;
                        playback = GaitPlayback(_walk, Vector3.Distance(actor.A, actor.B),
                                                actor.Period * 0.26f, actor.Cadence);
                    }
                    else
                    {
                        Place(actor, actor.A, actor.YawA);
                        Carry(actor, false);
                        pose = AmbientMotion.BasePose.Idle;
                        playback = 1f;
                    }
                    break;

                case Routine.Activity:
                case Routine.Conversation:
                {
                    Visible(actor, true);
                    Place(actor, actor.A, actor.YawA);
                    Carry(actor, false);
                    float actionSeconds = actor.Action != null
                        ? Mathf.Max(0.4f, actor.Action.length / actor.Cadence)
                        : 0f;
                    if (Mathf.Repeat(elapsed, actor.Period) >= actionSeconds)
                    {
                        pose = AmbientMotion.BasePose.Idle;
                        playback = 1f;
                    }
                    else loop = false;
                    break;
                }

                case Routine.SeatedConversation:
                    Visible(actor, true);
                    Place(actor, actor.A, actor.YawA);
                    Carry(actor, false);
                    if (actor.Secondary != null)
                    {
                        float talkSeconds = Mathf.Max(0.5f, actor.Secondary.length / actor.Cadence);
                        float talkCycle = Mathf.Repeat(elapsed, talkSeconds + 3.2f);
                        overlay = talkCycle < talkSeconds;
                    }
                    break;

                default:
                    Visible(actor, true);
                    Place(actor, actor.A, actor.YawA);
                    Carry(actor, false);
                    break;
            }

            actor.Motion?.Select(pose, loop, playback);
            actor.Motion?.SetOverlay(overlay, overlay && !actor.OverlayWasOn, actor.Cadence);
            actor.OverlayWasOn = overlay;
        }

        static float GaitPlayback(AnimationClip clip, float distance, float seconds,
                                  float fallback)
        {
            if (clip == null || seconds <= 0.01f) return fallback;
            float authored = clip.averageSpeed.magnitude;
            if (authored <= 0.05f) return fallback;
            return Mathf.Clamp(distance / seconds / authored, 0.72f, 1.35f);
        }

        static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        static void Place(Actor actor, Vector3 at, float yaw)
        {
            actor.Tf.localPosition = at;
            actor.Tf.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        static void Travel(Actor actor, Vector3 from, Vector3 to, float t)
        {
            Vector3 before = actor.Tf.localPosition;
            if (!actor.HasVia)
                actor.Tf.localPosition = Vector3.Lerp(from, to, t);
            else
            {
                float first = Vector3.Distance(from, actor.Via);
                float second = Vector3.Distance(actor.Via, to);
                float total = Mathf.Max(0.001f, first + second);
                float split = first / total;
                actor.Tf.localPosition = t <= split
                    ? Vector3.Lerp(from, actor.Via, split > 0.001f ? t / split : 1f)
                    : Vector3.Lerp(actor.Via, to,
                        second > 0.001f ? (t - split) / Mathf.Max(0.001f, 1f - split) : 1f);
            }
            Vector3 direction = actor.Tf.localPosition - before;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                actor.Tf.localRotation = Quaternion.Slerp(
                    actor.Tf.localRotation,
                    Quaternion.LookRotation(direction.normalized, Vector3.up),
                    Mathf.Clamp01(10f * Time.deltaTime));
        }

        static void Visible(Actor actor, bool visible)
        {
            if (actor.Root.activeSelf != visible) actor.Root.SetActive(visible);
        }

        static void Carry(Actor actor, bool visible)
        {
            if (actor.Carry != null && actor.Carry.activeSelf != visible)
                actor.Carry.SetActive(visible);
        }

        void Shot(StreetAlarm.Shot shot)
        {
            if (_panicking || _shelters.Length == 0) return;
            Vector3 centre = transform.TransformPoint(_blockCentre);
            float heard = Mathf.Max(18f, shot.Loudness) + _blockRadius;
            if ((shot.Pos - centre).sqrMagnitude > heard * heard) return;
            BeginPanic();
        }

        void BeginPanic()
        {
            if (_panicking || _shelters.Length == 0) return;
            _panicking = true;
            _panicPrepared = false;
            _panicActorIndex = 0;
            _panicPreparing = null;
            _panicShelterCursor = 0;
            SchedulePanicPreparation();
        }

        void SchedulePanicPreparation()
        {
            if (_panicQueued || _panicPrepared || !_panicking) return;
            _panicQueued = true;
            PanicPrepareQueue.Enqueue(this);
        }

        static void TickPanicPreparation()
        {
            int frame = Time.frameCount;
            if (_panicPrepareFrame == frame) return;
            _panicPrepareFrame = frame;

            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            int steps = 0;
            while (PanicPrepareQueue.Count > 0 && steps < PanicPrepareStepCap)
            {
                var life = PanicPrepareQueue.Dequeue();
                if (life == null) continue;
                life._panicQueued = false;
                if (!life.isActiveAndEnabled || !life._panicking || life._panicPrepared)
                    continue;

                bool complete = life.PreparePanicStep();
                steps++;
                if (complete) life._panicPrepared = true;
                else life.SchedulePanicPreparation();

                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - began) *
                                   1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (elapsedMs >= PanicPrepareBudgetMs) break;
            }
        }

        /// <summary>One bounded unit of panic preparation: either the block clearance,
        /// or one actor's attempt at one doorway. Returning false puts this block at the
        /// back of the shared queue so another nearby block gets a turn next.</summary>
        bool PreparePanicStep()
        {
            if (_runtimeClearance == null)
            {
                _runtimeClearance = new AmbientStandingClearance(
                    transform.parent != null ? transform.parent : transform, transform);
                return false;
            }

            while (_panicPreparing == null)
            {
                if (_panicActorIndex >= _actors.Count) return true;
                var actor = _actors[_panicActorIndex];
                if (!Owned(actor))
                {
                    _panicActorIndex++;
                    continue;
                }

                // Someone currently hidden by the enter/exit loop is already indoors.
                if (!actor.Root.activeSelf)
                {
                    FinishPanicRoute(actor, null);
                    continue;
                }

                _panicPreparing = actor;
                _panicCurrent = actor.Tf.localPosition;
                _panicRouteStart = actor.HasDeparture ? actor.Departure : _panicCurrent;
                OrderShelters(_panicRouteStart);
                Carry(actor, false);
                actor.Motion?.SetOverlay(false);
            }

            if (_panicShelterCursor >= _shelters.Length)
            {
                FinishPanicRoute(_panicPreparing, null);
                return _panicActorIndex >= _actors.Count;
            }

            int shelterIndex = _panicShelterOrder[_panicShelterCursor++];
            var shelter = _shelters[shelterIndex];
            if (_runtimeClearance.TryRoute(_panicRouteStart, shelter.Outside,
                                           out var outsideRoute))
            {
                int prefix = _panicPreparing.HasDeparture ? 1 : 0;
                var route = new Vector3[prefix + outsideRoute.Length + 1];
                int at = 0;
                if (prefix != 0) route[at++] = _panicCurrent;
                Array.Copy(outsideRoute, 0, route, at, outsideRoute.Length);
                route[route.Length - 1] = shelter.Inside;
                FinishPanicRoute(_panicPreparing, route);
            }
            else if (_panicShelterCursor >= _shelters.Length)
                FinishPanicRoute(_panicPreparing, null);

            return _panicPreparing == null && _panicActorIndex >= _actors.Count;
        }

        void OrderShelters(Vector3 from)
        {
            if (_panicShelterOrder.Length != _shelters.Length)
                _panicShelterOrder = new int[_shelters.Length];
            for (int i = 0; i < _panicShelterOrder.Length; i++)
                _panicShelterOrder[i] = i;
            for (int i = 1; i < _panicShelterOrder.Length; i++)
            {
                int value = _panicShelterOrder[i];
                float distance = ShelterDistanceSq(value, from);
                int j = i - 1;
                while (j >= 0 && ShelterDistanceSq(_panicShelterOrder[j], from) > distance)
                {
                    _panicShelterOrder[j + 1] = _panicShelterOrder[j];
                    j--;
                }
                _panicShelterOrder[j + 1] = value;
            }
            _panicShelterCursor = 0;
        }

        float ShelterDistanceSq(int index, Vector3 from)
        {
            Vector3 delta = _shelters[index].Outside - from;
            delta.y = 0f;
            return delta.sqrMagnitude;
        }

        void FinishPanicRoute(Actor actor, Vector3[] route)
        {
            actor.PanicRoute = route;
            actor.PanicLeg = route != null && route.Length > 1 ? 1 : 0;
            actor.PanicDone = route == null || route.Length < 2;
            actor.PanicStanding = !actor.PanicDone && actor.HasDeparture;
            actor.PanicStandTime = 0f;
            actor.PanicStandDuration = Mathf.Max(0.55f,
                (_standUp != null ? _standUp.length : 0.7f) / 1.25f);
            Carry(actor, false);
            actor.Motion?.SetOverlay(false);
            _panicPreparing = null;
            _panicShelterCursor = 0;
            _panicActorIndex++;
        }

        int NearestShelter(Vector3 at)
        {
            int best = 0;
            float distance = float.MaxValue;
            for (int i = 0; i < _shelters.Length; i++)
            {
                Vector3 delta = _shelters[i].Outside - at;
                delta.y = 0f;
                float square = delta.sqrMagnitude;
                if (square >= distance) continue;
                distance = square;
                best = i;
            }
            return best;
        }

        void ApplyPanic(Actor actor, float dt)
        {
            if (actor.PanicDone || actor.PanicRoute == null ||
                actor.PanicLeg >= actor.PanicRoute.Length) return;
            Visible(actor, true);
            if (actor.PanicStanding)
            {
                actor.PanicStandTime += dt;
                float t = Smooth(actor.PanicStandTime / actor.PanicStandDuration);
                Vector3 from = actor.PanicRoute[0];
                Vector3 stand = actor.PanicRoute[1];
                actor.Tf.localPosition = Vector3.Lerp(from, stand, t);
                Vector3 face = actor.PanicRoute.Length > 2
                    ? actor.PanicRoute[2] - stand
                    : stand - from;
                face.y = 0f;
                if (face.sqrMagnitude > 0.001f)
                    actor.Tf.localRotation = Quaternion.Slerp(
                        actor.Tf.localRotation,
                        Quaternion.LookRotation(face.normalized, Vector3.up),
                        Mathf.Clamp01(8f * dt));
                actor.Motion?.Select(AmbientMotion.BasePose.StandUp, false, 1.25f);
                actor.Motion?.SetOverlay(false);
                if (actor.PanicStandTime < actor.PanicStandDuration) return;
                actor.Tf.localPosition = stand;
                actor.PanicStanding = false;
                actor.PanicLeg = 2;
                if (actor.PanicLeg >= actor.PanicRoute.Length)
                {
                    actor.PanicDone = true;
                    Visible(actor, false);
                }
                return;
            }
            Vector3 target = actor.PanicRoute[actor.PanicLeg];
            Vector3 before = actor.Tf.localPosition;
            actor.Tf.localPosition = Vector3.MoveTowards(before, target, actor.PanicSpeed * dt);
            Vector3 direction = target - before;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                actor.Tf.localRotation = Quaternion.Slerp(
                    actor.Tf.localRotation,
                    Quaternion.LookRotation(direction.normalized, Vector3.up),
                    Mathf.Clamp01(12f * dt));

            if ((actor.Tf.localPosition - target).sqrMagnitude <= Arrived * Arrived)
            {
                actor.Tf.localPosition = target;
                actor.PanicLeg++;
                if (actor.PanicLeg >= actor.PanicRoute.Length)
                {
                    actor.PanicDone = true;
                    Visible(actor, false);
                    return;
                }
            }

            actor.Motion?.Select(AmbientMotion.BasePose.Run, true,
                                 GaitPlaybackForSpeed(_run, actor.PanicSpeed, 1.18f));
            actor.Motion?.SetOverlay(false);
        }

        static float GaitPlaybackForSpeed(AnimationClip clip, float speed, float fallback)
        {
            float authored = clip != null ? clip.averageSpeed.magnitude : 0f;
            return authored > 0.05f ? Mathf.Clamp(speed / authored, 0.8f, 1.5f) : fallback;
        }

        /// <summary>
        /// A real Unity animation graph per visible prop. Clips advance every rendered
        /// frame, cross-fade without returning to frame zero, and one-shot activities hold
        /// their last frame before the next natural rest. This replaces the old 12 Hz
        /// SampleAnimation snapshots that made every body visibly judder.
        /// </summary>
        internal sealed class AmbientMotion
        {
            internal enum BasePose { Idle, Walk, Run, Action, StandUp }

            const int Slots = 5;
            const float BlendRate = 7.5f;

            PlayableGraph _graph;
            AnimationMixerPlayable _mixer;
            AnimationLayerMixerPlayable _layers;
            readonly AnimationClipPlayable[] _playables = new AnimationClipPlayable[Slots];
            readonly AnimationClip[] _clips = new AnimationClip[Slots];
            readonly float[] _weights = new float[Slots];
            AnimationClipPlayable _overlay;
            AnimationClip _overlayClip;
            int _current = -1;
            bool _currentLoops;
            bool _currentHeld;
            float _overlayWeight;
            float _overlayTarget;
            bool _overlayHeld;

            static AvatarMask _seatedTalkMask;

            public AmbientMotion(Animator animator, AnimationClip idle, AnimationClip walk,
                                 AnimationClip run, AnimationClip action,
                                 AnimationClip secondary, AnimationClip standUp,
                                 float phase, bool seated)
            {
                if (animator == null) return;
                _graph = PlayableGraph.Create("Residential ambient prop");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                _mixer = AnimationMixerPlayable.Create(_graph, Slots, true);

                Wire((int)BasePose.Idle, idle);
                Wire((int)BasePose.Walk, walk ?? idle);
                Wire((int)BasePose.Run, run ?? walk ?? idle);
                Wire((int)BasePose.Action, action ?? idle);
                Wire((int)BasePose.StandUp, standUp ?? idle);

                var output = AnimationPlayableOutput.Create(_graph, "ambient", animator);
                if (secondary != null)
                {
                    _layers = AnimationLayerMixerPlayable.Create(_graph, 2);
                    _graph.Connect(_mixer, 0, _layers, 0);
                    _layers.SetInputWeight(0, 1f);
                    _overlay = AnimationClipPlayable.Create(_graph, secondary);
                    _overlay.SetApplyFootIK(false);
                    _graph.Connect(_overlay, 0, _layers, 1);
                    _layers.SetInputWeight(1, 0f);
                    _layers.SetLayerMaskFromAvatarMask(1, SeatedTalkMask());
                    _overlayClip = secondary;
                    output.SetSourcePlayable(_layers);
                }
                else output.SetSourcePlayable(_mixer);

                for (int i = 0; i < Slots; i++)
                    if (_playables[i].IsValid() && _clips[i] != null)
                        _playables[i].SetTime(Mathf.Repeat(phase, Mathf.Max(0.01f, _clips[i].length)));

                int initial = seated ? (int)BasePose.Action : (int)BasePose.Idle;
                if (!_playables[initial].IsValid()) initial = FirstValid();
                if (initial >= 0)
                {
                    _current = initial;
                    _currentLoops = true;
                    _weights[initial] = 1f;
                    _mixer.SetInputWeight(initial, 1f);
                }
                _graph.Play();
            }

            void Wire(int slot, AnimationClip clip)
            {
                if (clip == null) return;
                var playable = AnimationClipPlayable.Create(_graph, clip);
                playable.SetApplyFootIK(false);
                _graph.Connect(playable, 0, _mixer, slot);
                _mixer.SetInputWeight(slot, 0f);
                _playables[slot] = playable;
                _clips[slot] = clip;
            }

            int FirstValid()
            {
                for (int i = 0; i < Slots; i++) if (_playables[i].IsValid()) return i;
                return -1;
            }

            public void Select(BasePose requested, bool loop, float speed)
            {
                int next = (int)requested;
                if (!_playables[next].IsValid()) next = FirstValid();
                if (next < 0) return;

                speed = Mathf.Clamp(speed, 0.25f, 1.75f);
                if (next == _current)
                {
                    _currentLoops = loop;
                    if (!_currentHeld) _playables[next].SetSpeed(speed);
                    return;
                }

                _current = next;
                _currentLoops = loop;
                _currentHeld = false;
                if ((requested == BasePose.Action || requested == BasePose.StandUp) && !loop)
                    _playables[next].SetTime(0d);
                _playables[next].SetSpeed(speed);
            }

            public void SetOverlay(bool on, bool restart = false, float speed = 1f)
            {
                if (!_overlay.IsValid()) return;
                _overlayTarget = on ? 1f : 0f;
                if (!on) return;
                if (restart)
                {
                    _overlay.SetTime(0d);
                    _overlayHeld = false;
                }
                if (!_overlayHeld) _overlay.SetSpeed(Mathf.Clamp(speed, 0.25f, 1.75f));
            }

            public void Tick(float dt)
            {
                if (!_graph.IsValid() || !_mixer.IsValid()) return;
                for (int i = 0; i < Slots; i++)
                {
                    float target = i == _current ? 1f : 0f;
                    _weights[i] = Mathf.MoveTowards(_weights[i], target, BlendRate * dt);
                    _mixer.SetInputWeight(i, _weights[i]);
                }

                if (_layers.IsValid())
                {
                    _overlayWeight = Mathf.MoveTowards(_overlayWeight, _overlayTarget,
                                                       BlendRate * dt);
                    _layers.SetInputWeight(1, _overlayWeight);
                }

                for (int i = 0; i < 3; i++) Wrap(i);
                if (_current == (int)BasePose.Action || _current == (int)BasePose.StandUp)
                {
                    if (_currentLoops) Wrap(_current);
                    else HoldAtEnd(_current);
                }
                if (_overlay.IsValid() && _overlayClip != null && !_overlayHeld &&
                    _overlay.GetTime() >= _overlayClip.length)
                {
                    _overlay.SetTime(Mathf.Max(0f, _overlayClip.length - 0.0001f));
                    _overlay.SetSpeed(0d);
                    _overlayHeld = true;
                }
            }

            void Wrap(int slot)
            {
                if (!_playables[slot].IsValid() || _clips[slot] == null) return;
                float length = _clips[slot].length;
                if (length <= 0.01f) return;
                double time = _playables[slot].GetTime();
                if (time >= length) _playables[slot].SetTime(time % length);
            }

            void HoldAtEnd(int slot)
            {
                if (_currentHeld || !_playables[slot].IsValid() || _clips[slot] == null) return;
                float length = _clips[slot].length;
                if (length <= 0.01f || _playables[slot].GetTime() < length) return;
                _playables[slot].SetTime(length - 0.0001f);
                _playables[slot].SetSpeed(0d);
                _currentHeld = true;
            }

            public void Play()
            {
                if (_graph.IsValid()) _graph.Play();
            }

            public void Pause()
            {
                if (_graph.IsValid()) _graph.Stop();
            }

            public void Dispose()
            {
                if (_graph.IsValid()) _graph.Destroy();
            }

            static AvatarMask SeatedTalkMask()
            {
                if (_seatedTalkMask != null) return _seatedTalkMask;
                var mask = new AvatarMask { name = "Ambient seated conversation upper body" };
                foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
                    if (part != AvatarMaskBodyPart.LastBodyPart)
                        mask.SetHumanoidBodyPartActive(part, false);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
                return _seatedTalkMask = mask;
            }
        }
    }

    /// <summary>
    /// Snapshot of the already composed block's visible solids. It is deliberately an
    /// geometry check rather than physics or navigation: standing figures are nudged off
    /// cars, walls and props while a block is composed/recovered, and panic asks it once for
    /// a short safe polyline before following those fixed points.
    /// </summary>
    internal sealed class AmbientStandingClearance
    {
        readonly List<Bounds> _solids = new List<Bounds>(128);
        readonly Transform _root;
        Bounds _content;
        bool _hasContent;

        public Vector3 Centre => _hasContent
            ? new Vector3(_content.center.x, 0.054f, _content.center.z)
            : Vector3.zero;
        public float Radius => _hasContent
            ? 0.5f * Mathf.Sqrt(_content.size.x * _content.size.x +
                                _content.size.z * _content.size.z)
            : 5f;
        public Rect ContentRect => _hasContent
            ? new Rect(_content.min.x + 0.35f, _content.min.z + 0.35f,
                       Mathf.Max(0.5f, _content.size.x - 0.70f),
                       Mathf.Max(0.5f, _content.size.z - 0.70f))
            : new Rect(-5f, -5f, 10f, 10f);

        public AmbientStandingClearance(Transform root, Transform ignore)
        {
            _root = root;
            if (root == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeSelf ||
                    (ignore != null && renderer.transform.IsChildOf(ignore)))
                    continue;
                Bounds local = LocalBounds(renderer.bounds);
                if (local.size.sqrMagnitude < 0.0001f) continue;
                _solids.Add(local);
                if (!_hasContent)
                {
                    _content = local;
                    _hasContent = true;
                }
                else _content.Encapsulate(local);
            }
        }

        public bool IsClear(Vector3 feet, float radius = 0.38f, float height = 1.72f)
        {
            float low = feet.y + 0.16f;
            float high = feet.y + height;
            for (int i = 0; i < _solids.Count; i++)
            {
                Bounds solid = _solids[i];
                // Paving/floors end at the soles; awnings and roofs begin above the head.
                if (solid.max.y <= low || solid.min.y >= high) continue;
                if (feet.x + radius <= solid.min.x || feet.x - radius >= solid.max.x ||
                    feet.z + radius <= solid.min.z || feet.z - radius >= solid.max.z)
                    continue;
                return false;
            }
            return true;
        }

        public bool TryNearest(Vector3 preferred, Rect allowed, out Vector3 clear,
                               float maxDistance = 3f)
        {
            if (Inside(allowed, preferred) && IsClear(preferred))
            {
                clear = preferred;
                return true;
            }

            const int spokes = 16;
            for (float radius = 0.55f; radius <= maxDistance + 0.01f; radius += 0.55f)
                for (int spoke = 0; spoke < spokes; spoke++)
                {
                    float angle = spoke * Mathf.PI * 2f / spokes;
                    Vector3 candidate = preferred +
                                        new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                    if (!Inside(allowed, candidate) || !IsClear(candidate)) continue;
                    clear = candidate;
                    return true;
                }

            clear = preferred;
            return false;
        }

        public bool IsPathClear(Vector3 from, Vector3 to)
        {
            float length = Vector3.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.45f));
            for (int i = 0; i <= steps; i++)
                if (!IsClear(Vector3.Lerp(from, to, i / (float)steps))) return false;
            return true;
        }

        /// <summary>
        /// Finds a tiny baked polyline around static block props. This is deliberately not
        /// an agent or a live navigator: it runs only while composing/recovering a tableau
        /// or once when panic starts, and the resulting points are then followed verbatim.
        /// Every segment is swept with the same human-sized clearance test.
        /// </summary>
        public bool TryRoute(Vector3 from, Vector3 to, out Vector3[] route)
        {
            if (IsPathClear(from, to))
            {
                route = new[] { from, to };
                return true;
            }

            Vector3 cornerA = new Vector3(from.x, from.y, to.z);
            Vector3 cornerB = new Vector3(to.x, to.y, from.z);
            if (IsClear(cornerA) && IsPathClear(from, cornerA) && IsPathClear(cornerA, to))
            {
                route = new[] { from, cornerA, to };
                return true;
            }
            if (IsClear(cornerB) && IsPathClear(from, cornerB) && IsPathClear(cornerB, to))
            {
                route = new[] { from, cornerB, to };
                return true;
            }

            Vector3 line = to - from;
            line.y = 0f;
            if (line.sqrMagnitude < 0.01f) line = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, line.normalized);
            for (float offset = 0.8f; offset <= 8.01f; offset += 0.8f)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 shift = side * (offset * sign);
                    Vector3 a = from + shift;
                    Vector3 b = to + shift;
                    if (!Inside(ContentRect, a) || !Inside(ContentRect, b) ||
                        !IsClear(a) || !IsClear(b) ||
                        !IsPathClear(from, a) || !IsPathClear(a, b) ||
                        !IsPathClear(b, to))
                        continue;
                    route = new[] { from, a, b, to };
                    return true;
                }

            Vector3 mid = Vector3.Lerp(from, to, 0.5f);
            const int spokes = 24;
            for (float radius = 0.8f; radius <= 8.01f; radius += 0.8f)
                for (int spoke = 0; spoke < spokes; spoke++)
                {
                    float angle = spoke * Mathf.PI * 2f / spokes;
                    Vector3 candidate = mid +
                        new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                    if (!Inside(ContentRect, candidate) || !IsClear(candidate) ||
                        !IsPathClear(from, candidate) || !IsPathClear(candidate, to))
                        continue;
                    route = new[] { from, candidate, to };
                    return true;
                }

            route = null;
            return false;
        }

        static bool Inside(Rect rect, Vector3 point) =>
            point.x >= rect.xMin && point.x <= rect.xMax &&
            point.z >= rect.yMin && point.z <= rect.yMax;

        Bounds LocalBounds(Bounds world)
        {
            Vector3 min = world.min, max = world.max;
            var local = new Bounds(_root.InverseTransformPoint(min), Vector3.zero);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        local.Encapsulate(_root.InverseTransformPoint(new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z)));
            return local;
        }
    }
}
