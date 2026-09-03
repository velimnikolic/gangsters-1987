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
        /// <summary>How long the man is INSIDE before what he came for happens, and how
        /// long he stays after it. The conversation is the wait: the demand, the lean and
        /// the collection are all settled at the end of this, never at the threshold, so
        /// the wire cannot say what the owner answered while the door is still swinging.
        /// </summary>
        public const float InsideSeconds = 3.5f;

        /// <summary>How much of the stay is over BEFORE the act - the rest of it is him
        /// finishing up and turning for the door.</summary>
        public const float BeforeTheActShare = 0.7f;

        /// <summary>Sim seconds one threshold crossing is given. He is walking two or
        /// three metres through an open front on a straight line, so this is generous -
        /// but a passage that cannot finish must not leave a man switched off inside a
        /// wall for the rest of the game.</summary>
        public const float CrossPatience = 8f;

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

        /// <summary>Real seconds of getting no nearer the doorstep before the man is
        /// taken to have arrived where he stands. He is not walking any more - the
        /// pavement in front of that door is dressed, or the lattice will not draw the
        /// last few metres - and standing him there for the whole of WalkPatience is the
        /// freeze a player reads as an order the game ignored. The crossing starts from
        /// where he is instead: the doorway passage walks through what is in the way,
        /// which is exactly what it is for.</summary>
        public const float StallPatience = 4.5f;

        /// <summary>How much closer counts as still walking, and how near the doorstep he
        /// must already be for a stall to count as arrival rather than as a walk that
        /// never began.</summary>
        public const float StallProgress = 0.6f;
        public const float StallReach = 9f;

        /// <summary>How far to one side of a doorstep a standing spot may be looked
        /// for.</summary>
        const float StandableReach = 4f;

        /// <summary>The nearest spot beside a doorstep on which a man can actually STAND.
        /// A doorstep is a point on the plan's line and the pavement in front of it is
        /// dressed: the cafe terrace whose tables and umbrellas stand across the gym's
        /// door is the seed the player is on. A man ordered at a point inside that
        /// furniture stops short of it, never comes within a stride, and the visit gives
        /// up at the end of its patience with him standing in the street. Half a metre to
        /// the side is the same doorstep as far as the beat is concerned.</summary>
        static Vector3 Standable(Vector3 point)
        {
            if (!WalkObstacles.Standing(point, WalkObstacles.CrewTravelRadius))
                return point;
            var spot = WalkObstacles.ClearSpot(
                point, WalkObstacles.CrewTravelRadius, StandableReach);
            spot.y = point.y;
            return spot;
        }

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

            /// <summary>When what he came in for actually happens. Not the moment he
            /// crosses the threshold: the conversation takes its seconds first.</summary>
            public float ActAt;
            public float ActRealAt;

            /// <summary>The wall-clock backstop. Sim time can crawl (a low timescale,
            /// a hitch) and a man the sim forgot indoors is a man lost to the player -
            /// whatever happens, he is back on the street inside a few real seconds.</summary>
            public float RealNextAt;

            public bool Through;

            /// <summary>HE IS NOT COMING STRAIGHT BACK OUT. A visit is a beat with a
            /// clock on it - in, the word, out; a man MOVED IN stays where he was put
            /// until somebody sends him out again (CrewQuarters). Nothing else about the
            /// passage changes: he walks the same threshold, the same leaves open for
            /// him, and the same reverse walk takes him back to the pavement.</summary>
            public bool Hold;

            /// <summary>What the word at the door is worth, held while he walks to it.</summary>
            public float Talk;

            /// <summary>What happens WHEN HE IS IN, AND HAS BEEN A WHILE. The demand used
            /// to be settled the moment the men came within reach of the door, so the wire
            /// announced what the owner had said before anybody had opened it; then it was
            /// settled on the threshold, which is a man answering his own question as he
            /// steps through. It happens at the counter now, after the conversation has
            /// had its seconds.</summary>
            public System.Action WhenInside;

            /// <summary>And what waits for him to be back on the pavement - the round's
            /// next door, the walk home. Never fired while he is switched off inside.
            /// </summary>
            public System.Action WhenOut;

            public bool Told;
            public bool Left;

            /// <summary>The closest he has come to the doorstep on this walk, and when.
            /// A walk that stops closing has stopped, whatever its patience says.</summary>
            public float Nearest;
            public float NearestAt;

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
            readonly Storefront storefront;
            float amount;
            float target;

            public DoorSwing(Transform doorway)
            {
                if (doorway == null)
                    return;

                storefront = doorway.GetComponent<Storefront>() ??
                             doorway.GetComponentInParent<Storefront>();
                if (storefront != null)
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

            public bool IsOpen => storefront != null ? storefront.IsOpen
                : leaves.Count == 0 || amount >= 0.999f;
            public bool IsClosed => storefront != null ? storefront.IsClosed
                : leaves.Count == 0 || amount <= 0.001f;

            public void Open()
            {
                if (storefront != null) storefront.Open();
                else target = 1f;
            }

            public void Close()
            {
                if (storefront != null) storefront.Close();
                else target = 0f;
            }

            public void Tick(float dt)
            {
                if (storefront != null) return;
                if (leaves.Count == 0 || Mathf.Approximately(amount, target))
                    return;
                amount = Mathf.MoveTowards(amount, target,
                    Mathf.Max(0f, dt) / MoveSeconds);
                Apply(Mathf.SmoothStep(0f, 1f, amount));
            }

            public void SnapClosed()
            {
                if (storefront != null)
                {
                    storefront.SnapClosed();
                    return;
                }
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

        public static void Visit(
            CrewWalker man, Vector3 door, float talk = TalkSeconds,
            System.Action whenInside = null, System.Action whenOut = null,
            bool hold = false)
        {
            // A visit that cannot be played still owes its caller the thing the visit was
            // FOR: the demand is the order, the walk through the door is the show of it.
            if (man == null || man.Dead || man.Tf == null ||
                !man.Tf.gameObject.activeInHierarchy)
            {
                whenInside?.Invoke();
                whenOut?.Invoke();
                return;
            }
            // A man under fire does not pop indoors for a chat.
            if (UnderFire(man))
            {
                whenInside?.Invoke();
                whenOut?.Invoke();
                return;
            }

            if (instance == null)
            {
                var go = new GameObject("Door Beat") { hideFlags = HideFlags.DontSave };
                instance = go.AddComponent<DoorBeat>();
            }

            // One visit per man at a time - and a second order given while the first is
            // still playing RIDES it rather than answering out of the blue. Answering it
            // on the spot is how a player got a threat settled while his man was still
            // walking up to the door he had been sent to.
            for (var i = 0; i < instance.calls.Count; i++)
                if (instance.calls[i].Man == man)
                {
                    Chain(instance.calls[i], whenInside, whenOut);
                    return;
                }

            // AND HIS CREW COMES WITH HIM. One man walking off alone to call on a
            // shopkeeper is an errand; the family calling is the lieutenant at the door
            // and his hoods stood off it with their eyes on the street. Not when the
            // whole crew is MOVING IN: every man of it is walking through this door
            // himself, and posting them round it would be posting them against
            // themselves.
            if (!hold)
                Escort(man, door, man.Tf.position - door);

            // The doorstep he is sent to is one he can stand on.
            door = Standable(door);

            var call = new Call
            {
                Man = man, Door = door, Home = man.Tf.position, WhenInside = whenInside,
                WhenOut = whenOut, Hold = hold,
            };
            if (!Near(man.Tf.position, door, AtTheDoor))
            {
                // He is not at the door yet. He WALKS there - the beat used to put him
                // on it from wherever he stood, which is the teleport a player sees when
                // a man thirty metres up the street suddenly steps inside a shop.
                WalkTo(man, door);
                call.Talk = talk;
                call.Phase = VisitPhase.Approaching;
                call.RealNextAt = Time.unscaledTime + WalkPatience;
                call.Nearest = Vector3.Distance(man.Tf.position, door);
                call.NearestAt = Time.unscaledTime;
                instance.calls.Add(call);
                return;
            }

            Arrive(call, talk);
            instance.calls.Add(call);
        }

        /// <summary>A second order for a man who is already on a visit. It waits for the
        /// same doorway: settled from inside if he has not got there yet, settled at once
        /// only when the first one has already been answered.</summary>
        static void Chain(Call call, System.Action whenInside, System.Action whenOut)
        {
            if (whenInside != null)
            {
                if (!call.Told)
                {
                    call.WhenInside += whenInside;
                }
                else if (!call.Left)
                {
                    // He is still in there, and the player has asked for something else
                    // at the same counter - a demand after a threat, most often. It is
                    // another word, not an answer shouted through the window: he stays
                    // a little longer and this one is settled inside as well.
                    call.WhenInside += whenInside;
                    call.Told = false;
                    call.ActAt = Time.time + InsideSeconds * BeforeTheActShare;
                    call.ActRealAt =
                        Time.unscaledTime + InsideSeconds * BeforeTheActShare * 4f;
                    call.NextAt = Mathf.Max(call.NextAt, Time.time + InsideSeconds);
                    call.RealNextAt = Mathf.Max(
                        call.RealNextAt, Time.unscaledTime + InsideSeconds * 4f);
                }
                else
                {
                    whenInside();
                }
            }

            if (whenOut == null)
                return;
            if (call.Left)
                whenOut();
            else
                call.WhenOut += whenOut;
        }

        /// <summary>The walk up to a doorstep. Drawn ROUND the walls when the ground
        /// allows it (OrderAcross), because a man sent to a door on the far side of a
        /// building walks a straight line into its back wall and stops there; the plain
        /// stride is the fallback for ground no route can be drawn over.</summary>
        /// <summary>Has this walk stopped? A man still covering ground gets all the
        /// patience there is; one who has been no nearer his doorstep for StallPatience,
        /// and is already within a few metres of it, is standing there - so the beat gets
        /// on with the passage from where he is rather than leaving him planted in the
        /// street until the whole walk is given up.</summary>
        static bool Stalled(Call call, Vector3 doorstep)
        {
            if (call.Man == null || call.Man.Tf == null)
                return false;
            var gap = Vector3.Distance(call.Man.Tf.position, doorstep);
            if (gap < call.Nearest - StallProgress)
            {
                call.Nearest = gap;
                call.NearestAt = Time.unscaledTime;
                return false;
            }

            return gap <= StallReach &&
                   Time.unscaledTime - call.NearestAt >= StallPatience;
        }

        static void WalkTo(CrewWalker man, Vector3 point)
        {
            if (man == null)
                return;
            if (!man.OrderAcross(point))
                man.OrderToPoint(point);
        }

        /// <summary>The rest of his crew, brought to the same doorstep and posted round
        /// it looking out. Silently nothing in a scene with no crews behind it (the
        /// bench rigs), which is why the beat asks and never requires.</summary>
        static void Escort(CrewWalker man, Vector3 doorstep, Vector3 outward)
        {
            var crews = DemoCrews.Active;
            if (crews != null)
                crews.GuardDoor(man, doorstep, outward);
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
            Transform doorway,
            System.Action whenInside = null,
            System.Action whenOut = null,
            bool hold = false)
        {
            if (man == null || man.Dead || man.Tf == null ||
                !man.Tf.gameObject.activeInHierarchy || UnderFire(man))
            {
                whenInside?.Invoke();
                whenOut?.Invoke();
                return;
            }

            if (instance == null)
            {
                var go = new GameObject("Door Beat") { hideFlags = HideFlags.DontSave };
                instance = go.AddComponent<DoorBeat>();
            }

            for (var i = 0; i < instance.calls.Count; i++)
                if (instance.calls[i].Man == man ||
                    (doorway != null && instance.calls[i].Swing != null &&
                     instance.calls[i].Door == threshold))
                {
                    Chain(instance.calls[i], whenInside, whenOut);
                    return;
                }

            outside.y = threshold.y = inside.y = man.Tf.position.y;
            // The pavement he waits on and comes back out to has to be pavement he can
            // stand on; the threshold and the room behind it are geometry and stay put.
            outside = Standable(outside);
            // The crew walks to the same doorstep and stands off it facing the street,
            // whether he is already on it or has the length of the block to cover. A
            // crew MOVING IN needs no guard on the door it is going through.
            if (!hold)
                Escort(man, outside, outside - threshold);
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
                WalkTo(man, outside);
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
                Nearest = Vector3.Distance(man.Tf.position, outside),
                NearestAt = Time.unscaledTime,
                Swing = swing,
                WhenInside = whenInside,
                WhenOut = whenOut,
                Hold = hold,
            });
        }

        /// <summary>The racketeering entry used by the live city. Its persistent business
        /// ID resolves to a disposable building binding only for geometry; simulation
        /// remains authoritative when that block is streamed out.</summary>
        public static void VisitBusiness(
            CrewWalker man,
            TerritoryBusinessId businessId,
            Vector3 fallbackOutside,
            System.Action whenInside = null,
            System.Action whenOut = null,
            bool hold = false)
        {
            if (!BusinessViewBindings.TryGet(businessId, out var marker))
                marker = null;

            // THE SHOP'S OWN FRONT, WHETHER OR NOT A VIEW STANDS THERE. The measured
            // facade (FacadeFinder, through ShopDoors.Of) answers for the BUILDING, and
            // the building is not the shop: a residential shell wears one measured door
            // for every unit inside it, and an amenity lot's authored front can be the
            // side the block plan deliberately turned its back on - the gym whose east
            // face stands behind a cafe terrace, so the plan walks men to its south gate.
            // Half the bound shops in a dealt quarter measure that door on a DIFFERENT
            // side from the doorstep every order walks to, and a man sent to one side and
            // let in through the other walks the diagonal of the building, into its wall,
            // until the passage's patience gives up and puts him inside anyway - which is
            // the man who stands outside a gym for ten seconds before he goes in. The
            // doorstep the plan chose is the side with the pavement on it, so the whole
            // passage is laid against that side and the leaves, when there are any, swing
            // on the view standing there.
            if (ShopDoors.TryStreetFront(
                    businessId, out var wall, out var outward, out _))
            {
                VisitThrough(
                    man, wall + outward * DoorstepOut, wall,
                    wall - outward * RoomDepth(businessId, outward),
                    marker != null ? marker.transform : null,
                    whenInside, whenOut, hold);
                return;
            }

            if (marker == null)
            {
                // No plan ground and no view to measure: the doorstep beat is all there
                // is, and he says his piece where he stands.
                Visit(man, fallbackOutside, talk: 0f,
                    whenInside: whenInside, whenOut: whenOut, hold: hold);
                return;
            }

            // A view with no simulated ground under it - the bench rigs and the older
            // generated city. The door is measured off its own building, the way every
            // streamed shop's was before the plan could answer for it.
            var entrance = ShopDoors.Of(marker);
            var facing = entrance != null ? entrance.Facing : marker.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.001f)
                facing = Vector3.forward;
            facing.Normalize();

            var threshold = entrance != null
                ? entrance.DoorWorld
                : fallbackOutside - facing * 1.05f;
            var inside = threshold - facing * RoomDepth(businessId, facing);
            VisitThrough(
                man, fallbackOutside, threshold, inside, marker.transform,
                whenInside, whenOut, hold);
        }

        /// <summary>
        /// HE GOES IN AND HE STAYS IN. The same passage as a visit - the walk to the
        /// doorstep, the leaves, the threshold crossed on his own feet - with no clock
        /// on the far side of it: the man is an occupant of that building until
        /// <see cref="SendOut"/> or <see cref="Evict"/> puts him back on the pavement.
        /// The crew-level order that uses it is CrewQuarters.
        /// </summary>
        public static void MoveIn(
            CrewWalker man, TerritoryBusinessId businessId, Vector3 doorstep) =>
            VisitBusiness(man, businessId, doorstep, hold: true);

        /// <summary>The same, for a door the business directory cannot name - an
        /// authored scene's front. He hides at the doorstep rather than walking a
        /// measured threshold, which is all the plain beat has ever done there.</summary>
        public static void MoveIn(CrewWalker man, Vector3 doorstep) =>
            Visit(man, doorstep, talk: 0f, hold: true);

        /// <summary>Is this man an occupant - PAST the threshold, and staying there? A
        /// man still walking up to the door is under the same order and not yet in it,
        /// which is the difference a crew's move-in waits on.</summary>
        public static bool Held(CrewWalker man)
        {
            if (instance == null || man == null)
                return false;
            for (var i = 0; i < instance.calls.Count; i++)
            {
                var call = instance.calls[i];
                if (call.Man == man)
                    return call.Hold && Indoors(call);
            }
            return false;
        }

        /// <summary>
        /// OUT HE COMES, on his feet: the leaves open, he walks the threshold back and
        /// the pavement he came in from is where he ends up. Nothing at all for a man
        /// who is not being held.
        /// </summary>
        public static void SendOut(CrewWalker man)
        {
            if (instance == null || man == null)
                return;
            for (var i = 0; i < instance.calls.Count; i++)
            {
                var call = instance.calls[i];
                if (call.Man != man || !call.Hold)
                    continue;

                // A man who never got in has nothing to walk back out of - he is on the
                // pavement, on his way to a door he is no longer going to. The order is
                // simply off (Evict leaves a man outside where he stands).
                if (!Indoors(call))
                {
                    Evict(man);
                    return;
                }

                // The hold is what was stopping the beat's own exit; letting it go and
                // putting the clock at NOW hands him to the ordinary way out.
                call.Hold = false;
                call.NextAt = Time.time;
                call.RealNextAt = Time.unscaledTime;
                return;
            }
        }

        /// <summary>Is his body past the threshold - inside, or in the middle of the
        /// passage either way? What decides whether ending a beat has to put him back
        /// on the pavement or can simply leave him standing where he is.</summary>
        static bool Indoors(Call call) =>
            call.Inside ||
            call.Phase == VisitPhase.Entering ||
            call.Phase == VisitPhase.Inside ||
            call.Phase == VisitPhase.OpeningExit ||
            call.Phase == VisitPhase.Exiting;

        /// <summary>
        /// OUT HE COMES, NOW. The blunt way out, for the moment an occupant is given
        /// some other order: he is put back on the pavement and switched on in one
        /// frame, because the reverse walk takes seconds and a man mid-passage cannot
        /// also be marching somewhere - the beat would teleport him back to this door
        /// while he was doing it.
        /// </summary>
        public static void Evict(CrewWalker man)
        {
            if (instance == null || man == null)
                return;
            for (var i = instance.calls.Count - 1; i >= 0; i--)
            {
                var call = instance.calls[i];
                if (call.Man != man || !call.Hold)
                    continue;
                instance.calls.RemoveAt(i);
                call.Swing?.SnapClosed();
                Tell(call);
                Left(call);
                if (call.Man?.Tf == null || call.Man.Dead)
                    return;   // his body is the death path's business, not this one
                call.Man.EndDoorway();
                // Only a man who is PAST THE THRESHOLD is put anywhere: one still
                // walking up to the door is standing on ordinary pavement and stays
                // exactly where the cancelled order left him.
                if (Indoors(call))
                    call.Man.Tf.position = call.Through ? call.Outside : call.Home;
                if (!call.Man.Tf.gameObject.activeSelf)
                    call.Man.Tf.gameObject.SetActive(true);
                return;
            }
        }

        /// <summary>Where he stands to knock: a stride off the front, clear of the wall
        /// and off the shopfront itself.</summary>
        const float DoorstepOut = 1.2f;

        /// <summary>Furthest into a shop the beat will ever take a man, and the least it
        /// will settle for. A metre past the glass is not "inside" - it is a man standing
        /// in the window - and it was what the player saw when the passage ended on the
        /// threshold; five metres is a shop floor, and past that he would be out through
        /// somebody's back wall.</summary>
        const float DeepestRoom = 4.5f;
        const float ShallowestRoom = 1.6f;

        /// <summary>How far past the front this shop's own ground runs, so a deep
        /// warehouse is walked into properly and a two-metre kiosk is not walked through
        /// and out the back. Measured off the SIMULATION's site, which every business has
        /// whether or not its block is standing.</summary>
        static float RoomDepth(TerritoryBusinessId businessId, Vector3 outward)
        {
            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (business == null || !businessId.IsValid ||
                !business.TryGetSite(businessId, out var site) || site == null)
                return ShallowestRoom;

            var ground = site.Footprint;
            if (ground.IsEmpty)
                return ShallowestRoom;

            // The run inward is the site's own extent along the way he is walking.
            var across = Mathf.Abs(outward.x) > Mathf.Abs(outward.z)
                ? ground.Width
                : ground.Depth;
            return Mathf.Clamp(across * 0.4f, ShallowestRoom, DeepestRoom);
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
                    // And the order he was carrying is still answered. Every other way a
                    // visit can fail - under fire, a pavement he cannot cross, a beat
                    // that could not start at all - tells the caller; a man lost on the
                    // way is not the one exception that quietly swallows a demand.
                    Tell(call);
                    Left(call);
                    continue;
                }

                if (call.Phase == VisitPhase.Approaching)
                {
                    // A walk that has stopped closing on the door has stopped. He is
                    // taken to be at it - the last couple of metres are the terrace, the
                    // kerb or the lattice, not a man still on his way.
                    if (Near(call.Man.Tf.position, call.Door, AtTheDoor) ||
                        Stalled(call, call.Door))
                    {
                        // He walked it. From here it is the beat it always was - and
                        // the pavement he goes back out to is where he is NOW, not
                        // where he was standing when the order was given.
                        call.Home = call.Man.Tf.position;
                        Arrive(call, call.Talk);
                        continue;
                    }

                    // He is not getting there. Give the visit up rather than leave a man
                    // walking at a wall for the rest of the game - and the order he was
                    // carrying still has to be answered.
                    if (Time.unscaledTime > call.RealNextAt || UnderFire(call.Man))
                    {
                        calls.RemoveAt(i);
                        Tell(call);
                        Left(call);
                    }
                    continue;
                }

                // He is in. What he came for happens PART WAY THROUGH the stay, not on
                // the threshold - the rest of the seconds are him finishing up.
                if (call.Inside && !call.Told && Due(call.ActAt, call.ActRealAt))
                    Tell(call);

                // AN OCCUPANT HAS NO CLOCK. He was moved in, not sent in with a word to
                // say, and he stands there until the crew is called out again. His body
                // going with him - struck off the books while he was in there - ends the
                // hold rather than leaving a call on a man who is gone.
                if (call.Hold && call.Inside)
                {
                    if (call.Man == null || call.Man.Tf == null || call.Man.Dead)
                    {
                        calls.RemoveAt(i);
                        Tell(call);
                        Left(call);
                    }
                    continue;
                }

                // Sim time says this phase is over - or the wall clock does, while the
                // game is not paused. A pause holds the beat: bodies changing in a
                // frozen city read as a glitch, and unpausing moves it on at once.
                if (!Due(call.NextAt, call.RealNextAt))
                    continue;

                if (!call.Inside)
                {
                    // The word is done: in he goes - unless the street caught fire
                    // around him meanwhile, when the visit is simply off.
                    calls.RemoveAt(i);
                    if (UnderFire(call.Man))
                    {
                        Tell(call);
                        Left(call);
                        continue;
                    }
                    StepInside(call);
                    Stay(call);
                    calls.Add(call);
                    continue;
                }

                calls.RemoveAt(i);
                // A stay cut short by nothing at all still owes the caller its answer.
                Tell(call);
                StepOut(call);
                Left(call);
            }
        }

        /// <summary>The clock for one step of the beat: sim time, or the wall clock when
        /// sim time crawls - and neither while the game is paused.</summary>
        static bool Due(float simAt, float realAt) =>
            Time.time >= simAt ||
            (Time.unscaledTime >= realAt && Time.timeScale > 0.001f);

        /// <summary>He is in: how long he is in for, and when the act lands inside it.
        /// </summary>
        static void Stay(Call call)
        {
            call.ActAt = Time.time + InsideSeconds * BeforeTheActShare;
            call.ActRealAt = Time.unscaledTime + InsideSeconds * BeforeTheActShare * 4f;
            call.NextAt = Time.time + InsideSeconds;
            call.RealNextAt = Time.unscaledTime + InsideSeconds * 4f;
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
            Stay(call);
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

        /// <summary>He has been in there long enough. Whatever the visit was for happens
        /// now, once - the demand answered, the money handed over, the place turned over.
        /// </summary>
        static void Tell(Call call)
        {
            if (call == null || call.Told)
                return;
            call.Told = true;
            var payload = call.WhenInside;
            call.WhenInside = null;
            payload?.Invoke();
        }

        /// <summary>And he is back on the pavement: whatever was waiting for a man who
        /// can walk again - the round's next door, the way home - goes now.</summary>
        static void Left(Call call)
        {
            if (call == null || call.Left)
                return;
            call.Left = true;
            var payload = call.WhenOut;
            call.WhenOut = null;
            payload?.Invoke();
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
                // A man killed on his way in still owes the order an answer - the same
                // rule the doorstep beat has always kept.
                Tell(call);
                Left(call);
                return;
            }

            switch (call.Phase)
            {
                case VisitPhase.Approaching:
                    // Either he got there, or he has stopped getting there: a doorstep
                    // behind a cafe's tables is one no route reaches, and the passage
                    // itself walks through what is in the way.
                    if (Near(call.Man.Tf.position, call.Outside, AtTheDoor) ||
                        Stalled(call, call.Outside))
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
                        Tell(call);
                        Left(call);
                    }
                    break;

                case VisitPhase.OpeningEntry:
                    Face(call.Man, call.Inner);
                    if (call.Swing == null || call.Swing.IsOpen)
                    {
                        // THROUGH the front, not round it. A shop's interior is not
                        // walkable ground - the building is one solid box on the walk
                        // map - so an ordinary order at a point inside it is steered
                        // along the wall and stops short, which is the man standing two
                        // metres from a shop he was supposed to have gone into.
                        call.Man.OrderThroughDoorway(call.Inner);
                        call.Phase = VisitPhase.Entering;
                        call.PhaseAt = Time.time;
                    }
                    break;

                case VisitPhase.Entering:
                    // He has to WALK it, but he must never be left walking it. A passage
                    // that has not finished in CrossPatience is finished for him rather
                    // than hung: an order carried out beats a man wedged in a shopfront.
                    if (!Near(call.Man.Tf.position, call.Inner, 0.28f) &&
                        Time.time - call.PhaseAt < CrossPatience)
                        break;
                    call.Man.EndDoorway();
                    call.Man.Tf.position = call.Inner;
                    call.Man.Tf.gameObject.SetActive(false);
                    call.Swing?.Close();
                    call.Phase = VisitPhase.Inside;
                    call.PhaseAt = Time.time;
                    Stay(call);
                    break;

                case VisitPhase.Inside:
                    // The conversation happens IN HERE, and it takes its seconds. The
                    // owner used to answer on the threshold, so the wire spoke while the
                    // man was still in the doorway.
                    if (!call.Told && Due(call.ActAt, call.ActRealAt))
                        Tell(call);
                    // An occupant stays. He is not waiting on anything in here - the
                    // crew was moved in, and only being sent out moves him.
                    if (call.Hold)
                        break;
                    if (!Due(call.NextAt, call.RealNextAt))
                        break;
                    Tell(call);
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
                    call.Man.OrderThroughDoorway(call.Outside);
                    call.Phase = VisitPhase.Exiting;
                    call.PhaseAt = Time.time;
                    break;

                case VisitPhase.Exiting:
                    if (!Near(call.Man.Tf.position, call.Outside, 0.28f) &&
                        Time.time - call.PhaseAt < CrossPatience)
                        break;
                    call.Man.EndDoorway();
                    call.Man.Tf.position = call.Outside;
                    call.Swing?.Close();
                    call.Phase = VisitPhase.Closing;
                    call.PhaseAt = Time.time;
                    // He is out and can walk again: whatever was waiting on that goes now.
                    Left(call);
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
            //
            // A runner going down WITH THE SCENE is a different thing from one going
            // down mid-game: there is nobody left to tell, and calling a demand's
            // resolution into a half-torn-down city is how a clean stop turns into a
            // page of null errors. The orders are answered only while the scene stands.
            var answering = gameObject.scene.isLoaded;
            for (var i = 0; i < calls.Count; i++)
            {
                var call = calls[i];
                call.Swing?.SnapClosed();
                // And never swallow the order either. The act happens at the counter
                // now, part way through the stay, so a beat torn down mid-conversation
                // would lose a demand the player gave; the answer is owed whatever
                // happens to the runner.
                if (answering)
                {
                    Tell(call);
                    Left(call);
                }
                if (call.Man?.Tf == null)
                    continue;
                call.Man.EndDoorway();
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
