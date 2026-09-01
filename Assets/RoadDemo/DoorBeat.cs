using System.Collections.Generic;
using LivingCity.Business;
using LivingCity.Entities;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The city's shared building visit. Lightweight callers can still use Visit's
    /// doorstep word-and-hide beat; businesses with a live building view use VisitBusiness,
    /// which opens the authored leaves, walks the complete threshold through ordinary
    /// CrewWalker locomotion, hides only after the whole body is inside, and reverses the
    /// passage on return. VisitThrough is the geometry-explicit form used by review scenes.
    ///
    /// Deliberately refused while the man is in a fight: a beat that hid a man from the
    /// bullets aimed at him would be an exploit, not a flourish - and a fight that
    /// starts MID-WORD cancels the visit the same way.
    /// </summary>
    public sealed class DoorBeat : MonoBehaviour
    {
        /// <summary>How long the doorstep call takes inside, wall-clock seconds.</summary>
        public const float InsideSeconds = 2.6f;

        /// <summary>How long the word at the door runs before he goes in.</summary>
        public const float TalkSeconds = 1.7f;

        /// <summary>Near enough the doorstep to start going through it. A stride, no
        /// more: the passage is a man crossing a threshold, not stepping in from the
        /// kerb.</summary>
        public const float AtTheDoor = 1.6f;

        /// <summary>Real seconds a man is given to WALK to the door before the visit is
        /// given up. A pavement he cannot cross - a fight in the way, a lattice that
        /// does not reach - must end the beat, never leave him walking at a wall.</summary>
        public const float WalkPatience = 25f;

        /// <summary>The visible stages of a visit. The viewer reads this shared state;
        /// it does not guess from a hidden body or run its own doorway timer.</summary>
        public enum VisitPhase
        {
            None,

            /// <summary>Walking the last stretch to the doorstep. A visit begins where
            /// the man IS, and he can be half a street away when it is called for; until
            /// this phase existed he was simply put on the door, which is a teleport, or
            /// ordered straight at a point INSIDE the shop, which walks him through the
            /// facade. Neither is a man going through a door.</summary>
            Approaching,

            Talking,
            OpeningEntry,
            Entering,
            Inside,
            OpeningExit,
            Exiting,
            Closing,
        }

        sealed class Call
        {
            public CrewWalker Man;
            public Vector3 Door;

            /// <summary>The pavement he called from, and the spot he comes back out to.
            /// NEVER the door: the door is a point on the line of the facade, and a man
            /// put down on it is standing in the wall - off the walk lattice, unable to
            /// take a step, stuck in the shop for the rest of the game.</summary>
            public Vector3 Home;

            /// <summary>Still standing at the door making his point; hidden inside once
            /// the word is done.</summary>
            public bool Inside;

            public float NextAt;

            /// <summary>The wall-clock backstop. Sim time can crawl (a low timescale,
            /// a hitch) and a man the sim forgot indoors is a man lost to the player -
            /// whatever happens, he is back on the street inside a few real seconds.</summary>
            public float RealNextAt;

            public bool Through;

            /// <summary>What the word at the door is worth, held while he walks to it.</summary>
            public float Talk;

            public VisitPhase Phase;
            public Vector3 Outside;
            public Vector3 Threshold;
            public Vector3 Inner;
            public float PhaseAt;
            public DoorSwing Swing;
        }

        /// <summary>One pair of authored shop leaves, animated about the same measured
        /// outer hinges used by FuelStation.OpenTheShop. It is held only for the visit,
        /// so a hundred closed shops cost no Update and allocate nothing.</summary>
        sealed class DoorSwing
        {
            const float MoveSeconds = 0.55f;
            const float OpenDegrees = 78f;

            sealed class Leaf
            {
                public Transform Tf;
                public Vector3 ClosedPosition;
                public Quaternion ClosedRotation;
                public Vector3 OpenPosition;
                public Quaternion OpenRotation;
            }

            readonly List<Leaf> leaves = new List<Leaf>(2);
            float amount;
            float target;

            public DoorSwing(Transform doorway)
            {
                if (doorway == null)
                    return;

                var right = doorway.right;
                right.y = 0f;
                if (right.sqrMagnitude < 0.001f)
                    right = Vector3.right;
                right.Normalize();

                foreach (var tf in doorway.GetComponentsInChildren<Transform>(true))
                {
                    var left = tf.name.EndsWith("_Door_L");
                    if (!left && !tf.name.EndsWith("_Door_R"))
                        continue;

                    var renderer = tf.GetComponentInChildren<Renderer>(true);
                    if (renderer == null || tf.parent == null)
                        continue;

                    var side = left ? 1f : -1f;
                    var half = Vector3.Dot(renderer.bounds.extents, Abs(right));
                    var pivot = tf.position + right * (side * half);
                    var turn = Quaternion.AngleAxis(side * OpenDegrees, Vector3.up);
                    var openWorldPosition = pivot + turn * (tf.position - pivot);
                    var openWorldRotation = turn * tf.rotation;

                    leaves.Add(new Leaf
                    {
                        Tf = tf,
                        ClosedPosition = tf.localPosition,
                        ClosedRotation = tf.localRotation,
                        OpenPosition = tf.parent.InverseTransformPoint(openWorldPosition),
                        OpenRotation = Quaternion.Inverse(tf.parent.rotation) * openWorldRotation,
                    });
                }
            }

            public bool IsOpen => leaves.Count == 0 || amount >= 0.999f;
            public bool IsClosed => leaves.Count == 0 || amount <= 0.001f;

            public void Open() => target = 1f;
            public void Close() => target = 0f;

            public void Tick(float dt)
            {
                if (leaves.Count == 0 || Mathf.Approximately(amount, target))
                    return;
                amount = Mathf.MoveTowards(amount, target,
                    Mathf.Max(0f, dt) / MoveSeconds);
                Apply(Mathf.SmoothStep(0f, 1f, amount));
            }

            public void SnapClosed()
            {
                amount = target = 0f;
                Apply(0f);
            }

            void Apply(float t)
            {
                for (var i = 0; i < leaves.Count; i++)
                {
                    var leaf = leaves[i];
                    if (leaf.Tf == null)
                        continue;
                    leaf.Tf.localPosition = Vector3.Lerp(
                        leaf.ClosedPosition, leaf.OpenPosition, t);
                    leaf.Tf.localRotation = Quaternion.Slerp(
                        leaf.ClosedRotation, leaf.OpenRotation, t);
                }
            }

            static Vector3 Abs(Vector3 value) => new Vector3(
                Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        static DoorBeat instance;
        readonly List<Call> calls = new List<Call>();

        static bool UnderFire(CrewWalker man) =>
            Time.time - StreetAlarm.LastShotAt < 8f &&
            (StreetAlarm.LastShotPos - man.Tf.position).sqrMagnitude < 60f * 60f;

        public static void Visit(CrewWalker man, Vector3 door, float talk = TalkSeconds)
        {
            if (man == null || man.Dead || man.Tf == null ||
                !man.Tf.gameObject.activeInHierarchy)
                return;
            // A man under fire does not pop indoors for a chat.
            if (UnderFire(man))
                return;

            if (instance == null)
            {
                var go = new GameObject("Door Beat") { hideFlags = HideFlags.DontSave };
                instance = go.AddComponent<DoorBeat>();
            }

            // one visit per man at a time - the second caller's beat is already playing
            for (var i = 0; i < instance.calls.Count; i++)
                if (instance.calls[i].Man == man)
                    return;

            var call = new Call { Man = man, Door = door, Home = man.Tf.position };
            if (!Near(man.Tf.position, door, AtTheDoor))
            {
                // He is not at the door yet. He WALKS there - the beat used to put him
                // on it from wherever he stood, which is the teleport a player sees when
                // a man thirty metres up the street suddenly steps inside a shop.
                man.OrderToPoint(door);
                call.Talk = talk;
                call.Phase = VisitPhase.Approaching;
                call.RealNextAt = Time.unscaledTime + WalkPatience;
                instance.calls.Add(call);
                return;
            }

            Arrive(call, talk);
            instance.calls.Add(call);
        }

        /// <summary>Open a real doorway and use the crew man's ordinary locomotion to
        /// cross it completely before he becomes an interior occupant. The return is the
        /// exact reverse: camera-safe while hidden, doors first, body second, then a full
        /// walk back outside and close. No threshold gesture or demo-authored animation.</summary>
        public static void VisitThrough(
            CrewWalker man,
            Vector3 outside,
            Vector3 threshold,
            Vector3 inside,
            Transform doorway)
        {
            if (man == null || man.Dead || man.Tf == null ||
                !man.Tf.gameObject.activeInHierarchy || UnderFire(man))
                return;

            if (instance == null)
            {
                var go = new GameObject("Door Beat") { hideFlags = HideFlags.DontSave };
                instance = go.AddComponent<DoorBeat>();
            }

            for (var i = 0; i < instance.calls.Count; i++)
                if (instance.calls[i].Man == man ||
                    (doorway != null && instance.calls[i].Swing != null &&
                     instance.calls[i].Door == threshold))
                    return;

            outside.y = threshold.y = inside.y = man.Tf.position.y;
            var swing = new DoorSwing(doorway);
            // The passage starts AT the doorstep. Ordering a man at the inside point
            // from across the street walks him in a straight line through the shopfront,
            // which is what a player reads as a man entering a door from thirty metres.
            var atDoor = Near(man.Tf.position, outside, AtTheDoor);
            if (atDoor)
            {
                swing.Open();
                Face(man, inside);
            }
            else
            {
                man.OrderToPoint(outside);
            }

            instance.calls.Add(new Call
            {
                Man = man,
                Door = threshold,
                Home = man.Tf.position,
                Outside = outside,
                Threshold = threshold,
                Inner = inside,
                Through = true,
                Phase = atDoor ? VisitPhase.OpeningEntry : VisitPhase.Approaching,
                PhaseAt = Time.time,
                RealNextAt = Time.unscaledTime + WalkPatience,
                Swing = swing,
            });
        }

        /// <summary>The racketeering entry used by the live city. Its persistent business
        /// ID resolves to a disposable building binding only for geometry; simulation
        /// remains authoritative when that block is streamed out.</summary>
        public static void VisitBusiness(
            CrewWalker man,
            TerritoryBusinessId businessId,
            Vector3 fallbackOutside)
        {
            if (!BusinessViewBindings.TryGet(businessId, out var marker) || marker == null)
            {
                // No visible building can show a passage. Still skip the old threshold
                // wave: this fallback is intentionally the cheap hidden visit.
                Visit(man, fallbackOutside, talk: 0f);
                return;
            }

            var entrance = marker.GetComponentInChildren<ShopEntrance>(true);
            var facing = entrance != null ? entrance.Facing : marker.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.001f)
                facing = Vector3.forward;
            facing.Normalize();

            var threshold = entrance != null
                ? entrance.DoorWorld
                : fallbackOutside - facing * 1.05f;
            var inside = threshold - facing * 1.15f;
            VisitThrough(
                man, fallbackOutside, threshold, inside, marker.transform);
        }

        public static VisitPhase PhaseOf(CrewWalker man)
        {
            if (instance == null || man == null)
                return VisitPhase.None;
            for (var i = 0; i < instance.calls.Count; i++)
                if (instance.calls[i].Man == man)
                    return instance.calls[i].Phase;
            return VisitPhase.None;
        }

        public static bool Active(CrewWalker man) => PhaseOf(man) != VisitPhase.None;

        /// <summary>The doorway this man's visit is about, for anything measuring how
        /// far off it began. Zero when he is not on a visit.</summary>
        public static Vector3 DoorOf(CrewWalker man)
        {
            if (instance == null || man == null)
                return Vector3.zero;
            for (var i = 0; i < instance.calls.Count; i++)
                if (instance.calls[i].Man == man)
                    return instance.calls[i].Door;
            return Vector3.zero;
        }

        void Update()
        {
            for (var i = calls.Count - 1; i >= 0; i--)
            {
                var call = calls[i];

                if (call.Through)
                {
                    TickThrough(i, call);
                    continue;
                }

                // A man gone from the street mid-word - died, despawned, retasked into a
                // car - takes his visit with him. (Once INSIDE he is inactive by design,
                // so this test belongs to the talking phase only.)
                if (!call.Inside &&
                    (call.Man == null || call.Man.Tf == null || call.Man.Dead ||
                     !call.Man.Tf.gameObject.activeInHierarchy))
                {
                    calls.RemoveAt(i);
                    continue;
                }

                if (call.Phase == VisitPhase.Approaching)
                {
                    if (Near(call.Man.Tf.position, call.Door, AtTheDoor))
                    {
                        // He walked it. From here it is the beat it always was - and
                        // the pavement he goes back out to is where he is NOW, not
                        // where he was standing when the order was given.
                        call.Home = call.Man.Tf.position;
                        Arrive(call, call.Talk);
                        continue;
                    }

                    // He is not getting there. Give the visit up rather than leave a man
                    // walking at a wall for the rest of the game.
                    if (Time.unscaledTime > call.RealNextAt || UnderFire(call.Man))
                        calls.RemoveAt(i);
                    continue;
                }

                // Sim time says this phase is over - or the wall clock does, while the
                // game is not paused. A pause holds the beat: bodies changing in a
                // frozen city read as a glitch, and unpausing moves it on at once.
                if (Time.time < call.NextAt &&
                    (Time.unscaledTime < call.RealNextAt || Time.timeScale <= 0.001f))
                    continue;

                if (!call.Inside)
                {
                    // The word is done: in he goes - unless the street caught fire
                    // around him meanwhile, when the visit is simply off.
                    calls.RemoveAt(i);
                    if (UnderFire(call.Man))
                        continue;
                    StepInside(call);
                    call.NextAt = Time.time + InsideSeconds;
                    call.RealNextAt = Time.unscaledTime + InsideSeconds * 4f;
                    calls.Add(call);
                    continue;
                }

                calls.RemoveAt(i);
                StepOut(call);
            }
        }

        /// <summary>He is at the door: the word, or straight in when there is no word.
        /// Shared by the visit that started on the doorstep and the one that had to walk
        /// there first, so a man who walked gets the same beat as one who was already
        /// standing at it.</summary>
        static void Arrive(Call call, float talk)
        {
            if (talk > 0f)
            {
                ArmBeat.Talk(call.Man, call.Door, talk);
                call.Inside = false;
                call.Phase = VisitPhase.Talking;
                call.NextAt = Time.time + talk;
                call.RealNextAt = Time.unscaledTime + talk * 4f;
                return;
            }

            StepInside(call);
            call.NextAt = Time.time + InsideSeconds;
            call.RealNextAt = Time.unscaledTime + InsideSeconds * 4f;
        }

        /// <summary>All the way in: the body goes off at the door, and while it is off it
        /// stands ON the door - the walk is over the threshold, not a fade on the kerb.
        /// Nothing reads a hidden man's feet, and StepOut always puts him back on the
        /// pavement, so the door line he rests on is never a place he can be stranded.</summary>
        static void StepInside(Call call)
        {
            if (call.Man?.Tf == null)
                return;
            call.Man.Tf.position = call.Door;
            call.Man.Tf.gameObject.SetActive(false);
            call.Inside = true;
            call.Phase = VisitPhase.Inside;
        }

        /// <summary>And back out onto the pavement he called from - never onto the door
        /// itself, which is a point on the wall. A man who died invisibly (a blast, a
        /// purge) is left where the systems put him.</summary>
        static void StepOut(Call call)
        {
            if (call.Man?.Tf == null)
                return;
            if (!call.Man.Dead)
                call.Man.Tf.position = call.Home;
            call.Man.Tf.gameObject.SetActive(true);
        }

        void TickThrough(int index, Call call)
        {
            call.Swing?.Tick(Time.deltaTime);
            if (call.Man == null || call.Man.Tf == null || call.Man.Dead)
            {
                call.Swing?.SnapClosed();
                calls.RemoveAt(index);
                return;
            }

            switch (call.Phase)
            {
                case VisitPhase.Approaching:
                    if (Near(call.Man.Tf.position, call.Outside, AtTheDoor))
                    {
                        call.Swing?.Open();
                        Face(call.Man, call.Inner);
                        call.Phase = VisitPhase.OpeningEntry;
                        call.PhaseAt = Time.time;
                        break;
                    }

                    if (Time.unscaledTime > call.RealNextAt || UnderFire(call.Man))
                    {
                        call.Swing?.SnapClosed();
                        calls.RemoveAt(index);
                    }
                    break;

                case VisitPhase.OpeningEntry:
                    Face(call.Man, call.Inner);
                    if (call.Swing == null || call.Swing.IsOpen)
                    {
                        call.Man.OrderToPoint(call.Inner);
                        call.Phase = VisitPhase.Entering;
                        call.PhaseAt = Time.time;
                    }
                    break;

                case VisitPhase.Entering:
                    if (Near(call.Man.Tf.position, call.Inner, 0.28f))
                    {
                        call.Man.Tf.position = call.Inner;
                        call.Man.Tf.gameObject.SetActive(false);
                        call.Swing?.Close();
                        call.Phase = VisitPhase.Inside;
                        call.NextAt = Time.time + InsideSeconds;
                        call.RealNextAt = Time.unscaledTime + InsideSeconds * 4f;
                    }
                    break;

                case VisitPhase.Inside:
                    if (Time.time < call.NextAt &&
                        (Time.unscaledTime < call.RealNextAt || Time.timeScale <= 0.001f))
                        break;
                    call.Swing?.Open();
                    call.Phase = VisitPhase.OpeningExit;
                    call.PhaseAt = Time.time;
                    break;

                case VisitPhase.OpeningExit:
                    if (call.Swing != null && !call.Swing.IsOpen)
                        break;
                    call.Man.Tf.position = call.Inner;
                    call.Man.Tf.gameObject.SetActive(true);
                    Face(call.Man, call.Outside);
                    call.Man.OrderToPoint(call.Outside);
                    call.Phase = VisitPhase.Exiting;
                    call.PhaseAt = Time.time;
                    break;

                case VisitPhase.Exiting:
                    if (!Near(call.Man.Tf.position, call.Outside, 0.28f))
                        break;
                    call.Man.Tf.position = call.Outside;
                    call.Swing?.Close();
                    call.Phase = VisitPhase.Closing;
                    call.PhaseAt = Time.time;
                    break;

                case VisitPhase.Closing:
                    if (call.Swing != null && !call.Swing.IsClosed)
                        break;
                    calls.RemoveAt(index);
                    break;
            }
        }

        static bool Near(Vector3 a, Vector3 b, float distance)
        {
            a.y = b.y = 0f;
            return (a - b).sqrMagnitude <= distance * distance;
        }

        static void Face(CrewWalker man, Vector3 point)
        {
            if (man?.Tf == null)
                return;
            var direction = point - man.Tf.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                man.Tf.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        void OnDestroy()
        {
            // Never strand an invisible man: whatever ends the beat runner ends the
            // beats, bodies first - and on the pavement, not in the wall.
            for (var i = 0; i < calls.Count; i++)
            {
                var call = calls[i];
                call.Swing?.SnapClosed();
                if (call.Man?.Tf == null)
                    continue;
                if (call.Through)
                    call.Man.Tf.position = call.Outside;
                if (call.Inside || call.Phase == VisitPhase.Inside ||
                    call.Phase == VisitPhase.OpeningExit)
                {
                    if (!call.Through)
                        StepOut(call);
                    else
                        call.Man.Tf.gameObject.SetActive(true);
                }
            }
            calls.Clear();
            if (instance == this)
                instance = null;
        }
    }
}
