using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.City
{
    [RequireComponent(typeof(PathFinding)),RequireComponent(typeof(Rigidbody))]
    public class HumanBehavior : MonoBehaviour
    {
        // PATCH (Living City): this pedestrian's entry in PedestrianRegistry, set by
        // PedestrianAgent on spawn. Null everywhere else - pack demo scenes - and every
        // avoidance line below is gated on it, so behaviour there is unchanged.
        [HideInInspector]
        public PedestrianBody body;

        // PATCH (Living City): isMoving is private and arrival is unobservable, but the
        // interaction layer must not start an activity mid-crosswalk-wait.
        public bool IsMoving => isMoving;

        // PATCH (Living City): a scripted sidewalk destination plus a completion hook, the
        // exact pair CarBehavior already carries. Without it there is no way to command "walk
        // to X": with randomDestination off, Repath ping-pongs destination against the private
        // start, and with it on every re-path rolls a fresh random tile. A beat officer
        // returning to the station sets both fields; each route end then fires routeCompleted
        // BEFORE the re-path is enqueued, so a handler can take the walker over (disable this
        // component) or clear the override in time for the next path. Re-paths still flow
        // through PedestrianRepathQueue - the patch changes what is pathed, never when.
        [HideInInspector] public bool hasScriptedDestination;
        [HideInInspector] public Vector3 scriptedDestination;
        public event System.Action routeCompleted;

        [HideInInspector]
        public List<Path> trajectory = new List<Path>();
        private PathFinding pathFinding;
        private Animator animator;
        public float maxspeed = 5.0f;
        public bool randomDestination;
        private float speed;
        private int activepoint = 0;
        private int activePath = 0;
        bool isMoving = false;
        private Vector3 targetPoint;
        public Vector3 destination;
        private Vector3 start;

        // PATCH (Living City): the avoidance probe runs every ProbeInterval-th fixed step
        // rather than every step, staggered per walker; between probes the cached obstacle is
        // reused with its allowance SPENT DOWN by each step taken, so the total advance
        // between two probes never exceeds what the last probe measured as safe. Overlap
        // stays impossible; the push is merely up to (interval - 1) steps stale.
        private PedestrianObstacle cachedObstacle = PedestrianObstacle.Clear;
        private int probeCountdown;
        private bool probeSeeded;

        // PATCH (Living City): nobody comes to REST on the carriageway.
        //
        // Walking across a road is normal - crossings are ordinary sidewalk paths that run over
        // the asphalt. Stopping on one is not, and this script had four ways to do it: a route
        // that ends mid-crossing (destinations are road TILE CENTRES, so the last node often
        // is), a re-path that finds nothing and leaves the walker standing for the rest of the
        // session, a red light entered while already on the road, and the interaction layer
        // handing control back. A person standing on a crossing holds the car that stopped for
        // them, and TrafficRegistry then queues every car behind it - the jam.
        //
        // So every stand-down first walks to the nearest point clear of the asphalt
        // (RoadSurface.TryNearestOffRoad) and only then does what it was going to do. The walk
        // is an ordinary targetPoint move through the same avoidance loop; the intent says what
        // to do on arrival, because MoveToNextPoint cannot be the arrival handler here - it
        // dereferences a trajectory the failed-path case does not have.
        enum RoadClearIntent { None, RouteEnd, Repath, Failed }

        /// <summary>Attempts before a walker accepts where it is - the clear point can be blocked.</summary>
        const int MaxClearAttempts = 3;

        /// <summary>
        /// Seconds a single kerb walk may take. The widest push on any cross-section is about 8m
        /// at 2-3m/s, so this only ever binds when a crowd is in the way - and then aiming again
        /// from where the walker actually stands beats pushing into the same knot forever.
        /// </summary>
        const float RoadClearTimeout = 8f;

        /// <summary>Seconds before a walker with no route tries to find one again.</summary>
        const float RepathRetrySeconds = 5f;

        /// <summary>
        /// Seconds between the "am I standing in the road?" looks a stopped walker takes. The
        /// stand-down sites above all clear the road themselves; this is the net under them, and
        /// the only reason it is affordable is that it runs solely while stopped, once a second,
        /// staggered by the body's jitter so a crowd that stood down together does not check
        /// together forever.
        /// </summary>
        const float StrandedInterval = 1f;

        private bool clearingRoad;
        private int clearAttempts;
        private RoadClearIntent clearIntent;
        private float clearUntil;
        private float nextStrandedCheck;

        /// <summary>Time.time of a failed re-path; 0 when there is nothing to retry.</summary>
        private float pathFailedAt;

        /// <summary>Retries are on a timer now, so the failure is reported once, not every 5s.</summary>
        private bool loggedPathFailure;

        // PATCH (Living City): the animator's speed float was written with a string key,
        // unconditionally, every fixed step - half a million string hashes a second at 10k
        // walkers. Cached hash, written only when the value actually moves.
        private float lastAnimatorSpeed = float.MinValue;

        // PATCH (Living City): GetComponentInParent on a physics callback is a hierarchy walk
        // per crossing per pedestrian; the crosswalk trigger colliders are few and static, so
        // the lookup is cached process-wide.
        private static readonly Dictionary<Collider, TrafficLight> CrosswalkLights =
            new Dictionary<Collider, TrafficLight>();

        private void Awake()
        {
            pathFinding = GetComponent<PathFinding>();
            animator = GetComponent<Animator>();
        }
        private void OnEnable()
        {
            // PATCH (Living City): PedestrianAgent writes the same animator float while this
            // component is disabled, so the last-written cache is stale on re-enable.
            lastAnimatorSpeed = float.MinValue;
            cachedObstacle = PedestrianObstacle.Clear;
            probeCountdown = 0;
        }
        void Start()
        {
            maxspeed = Random.Range(2f, 3f);
            start = transform.position;
            if (randomDestination)
            {
                //Selects random tile which is at least 60m away and less then 300m
                SetRandomDestination();
            }
            trajectory = pathFinding.GetPath(start,destination,PathType.Sidewalk);
            if (trajectory != null)
            {
                isMoving = true;
                GetClocestPoint();
                targetPoint = trajectory[0].pathPositions[activepoint].transform.position;
                start = transform.position;
            }
            else
            {
                Debug.Log(name + ": Path not found");

                // PATCH (Living City): same as in Repath - a walker that cannot find a route on
                // its first try is not a walker that never walks. Arm the retry.
                loggedPathFailure = true;
                pathFailedAt = Time.time;
            }
        }
        void FixedUpdate()
        {
            if (isMoving)
            {
                // PATCH (Living City): the arrival radius widens while avoidance is active.
                // A walker steered sideways around somebody can otherwise end up orbiting a
                // waypoint it is never allowed to touch.
                if (Vector3.Distance(targetPoint , transform.position) < (body != null ? 0.75f : 0.1f))
                {
                    // PATCH (Living City): a walker clearing the carriageway is not following a
                    // route - it may not even have one - so its arrival is its own handler.
                    if (clearingRoad)
                        FinishRoadClear();
                    else
                        MoveToNextPoint();
                }
                else if (clearingRoad && Time.time > clearUntil)
                {
                    // Never arrived: pushing into the same knot of people forever helps nobody.
                    FinishRoadClear();
                }
                Vector3 direction = targetPoint - transform.position;

                speed = Mathf.Lerp(speed, maxspeed, Time.deltaTime);
                if (speed > maxspeed)
                {
                    speed = Mathf.Lerp(speed, maxspeed, 10 * Time.deltaTime);
                }

                if (body != null)
                {
                    // PATCH (Living City): pedestrian avoidance. Steering bends the walking
                    // direction away from bodies in personal space; the clamp bounds the step
                    // so two people can never occupy the same ground. Both only ever REDUCE
                    // motion - a walker halted at a red light cannot be pushed anywhere.
                    if (!probeSeeded)
                    {
                        // Stagger the first probe by the body's jitter so the population's
                        // probes spread evenly across the stride instead of clumping.
                        probeCountdown = (int)(body.Jitter * PedestrianRegistry.ProbeInterval);
                        probeSeeded = true;
                    }
                    if (probeCountdown <= 0)
                    {
                        cachedObstacle = PedestrianRegistry.Probe(body, direction,
                            Time.deltaTime * PedestrianRegistry.ProbeInterval);
                        probeCountdown = PedestrianRegistry.ProbeInterval;
                    }
                    probeCountdown--;

                    var obstacle = cachedObstacle;
                    var heading = PedestrianSteering.Blend(direction, obstacle.Push);
                    var advance = Mathf.Min(speed * Time.deltaTime, obstacle.AllowedAdvance);

                    // Spend the cached allowance so it cannot be granted twice - see the
                    // field's comment for why this keeps the stride overlap-free.
                    cachedObstacle = new PedestrianObstacle(obstacle.Push,
                        obstacle.AllowedAdvance - advance);

                    var step = heading * advance;

                    // Steering is strictly horizontal; keep the follower's own vertical
                    // component so bridge ramps still climb.
                    step.y = direction.normalized.y * speed * Time.deltaTime;

                    var actual = step.magnitude / Mathf.Max(Time.deltaTime, 1e-5f);
                    body.SpeedMs = actual;

                    // Fold the clamp back into the speed model, so the walk animation tracks
                    // what the feet actually do and a released walker accelerates from rest
                    // instead of lurching off at full speed.
                    speed = Mathf.Min(speed, actual);

                    transform.position = transform.position + step;

                    if (heading != Vector3.zero)
                        transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
                }
                else
                {
                    Vector3 newPosition = transform.position + (direction.normalized * speed * Time.deltaTime);
                    transform.position = newPosition;

                    if (direction != Vector3.zero)
                    {
                        direction.y = 0;
                        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                    }
                }
            }
            else
            {
                speed = 0;
                // PATCH (Living City): a walker waiting at a crossing is still an obstacle,
                // and one that must read as standing to everyone probing around it.
                if (body != null)
                    body.SpeedMs = 0f;

                // PATCH (Living City): a re-path that found nothing used to be terminal - the
                // walker stood on that spot for the rest of the session, and nothing ever asked
                // again. Ask again. One float compare per stopped walker per step; everyone
                // else (waiting at a light, queued for a budget grant) has nothing armed.
                if (pathFailedAt > 0f && Time.time - pathFailedAt > RepathRetrySeconds)
                {
                    pathFailedAt = 0f;
                    QueueRepath();
                }

                TickStranded();
            }
            // PATCH (Living City): cached hash, and only written when the value moves - the
            // resting population (waiting at lights, standing in queue slots) costs nothing.
            var animatorSpeed = speed * 0.8f;
            if (Mathf.Abs(animatorSpeed - lastAnimatorSpeed) > 0.005f)
            {
                lastAnimatorSpeed = animatorSpeed;
                animator.SetFloat(PedestrianAnimation.SpeedHash, animatorSpeed);
            }
        }
        public void MoveToNextPoint()
        {
            if (activePath == trajectory.Count - 1
                && activepoint == trajectory[activePath].pathPositions.Count - 1)
            {
                // PATCH (Living City): "a person pausing at the kerb" is only true if it IS the
                // kerb. Routes end on the last sidewalk node nearest a destination that is a
                // road tile's CENTRE, which on a crossing tile is a node out on the asphalt.
                if (BeginRoadClear(RoadClearIntent.RouteEnd))
                    return;

                CompleteRoute();
                return;
            }

            if (activepoint == trajectory[activePath].pathPositions.Count - 1)
            {
                activePath++;
               /* if (trajectory[activePath].speed < maxspeed)
                {
                    maxspeed = trajectory[activePath].speed;
                }
                else
                {
                    currentMaxSpeed = maxspeed;
                }*/

                activepoint = 1;
            }
            else
            {
                activepoint++;
            }
            if(trajectory != null)
                targetPoint = trajectory[activePath].pathPositions[activepoint].transform.position + (trajectory[activePath].pathPositions[activepoint].transform.right * Random.Range(-0.8f,0.8f));
        }

        /// <summary>
        /// PATCH (Living City): throw the current route away and path afresh from wherever the
        /// transform is standing now.
        ///
        /// The interaction layer's contract is that an off-graph trip walks BACK to its
        /// departure point before handing control over, because targetPoint and the path
        /// cursors survive the disable and would otherwise send the walker striding off toward
        /// a waypoint it is no longer anywhere near. A pedestrian who goes in one building's
        /// door and comes out of another one across the city cannot honour that - there is no
        /// departure point to return to - so it needs this instead. Repath alone will not do:
        /// it declines while isMoving, which is exactly the state a walker interrupted
        /// mid-route is in, and isMoving is private.
        ///
        /// Goes through the central budget queue for the same reason route completion does:
        /// people come out of doors on their own clocks, and a burst of A* searches in one
        /// frame is exactly what the queue exists to spread out. The walker stands on its
        /// doorstep for a frame or two instead - which is what somebody stepping out of a
        /// building does anyway.
        /// </summary>
        public void ResetRoute()
        {
            if (BeginRoadClear(RoadClearIntent.Repath))
                return;

            isMoving = false;
            QueueRepath();
        }

        /// <summary>
        /// PATCH (Living City): route complete - stand down and ask for a new one. Re-pathing
        /// is an A* search plus two Physics.OverlapBox calls, and with thousands of walkers
        /// arriving on their own clocks those bursts land dozens to a frame, so when the spawner
        /// has armed the central queue the walker waits for a budget grant instead; the pack's
        /// demo scenes re-path immediately as they always did.
        ///
        /// Completion fires BEFORE the re-path is queued, and a handler that disabled the
        /// component has taken the walker over - no re-path belongs to it any more.
        /// </summary>
        void CompleteRoute()
        {
            isMoving = false;

            routeCompleted?.Invoke();
            if (!enabled)
                return;

            QueueRepath();
        }

        void QueueRepath()
        {
            if (PedestrianRepathQueue.Active && body != null)
                PedestrianRepathQueue.Enqueue(this);
            else
                Repath();
        }

        // ------------------------------------------------------------------ clearing the road

        /// <summary>
        /// PATCH (Living City): if this walker is standing on the asphalt, send it to the
        /// nearest point that is not, and remember what it was about to do. True when the walk
        /// was started and the caller should stand down; false when there is nothing to clear
        /// (the common case) or nowhere to clear to.
        /// </summary>
        bool BeginRoadClear(RoadClearIntent intent)
        {
            // Already on the way off; whoever asked second owns what happens on arrival.
            if (clearingRoad)
            {
                clearIntent = intent;
                return true;
            }

            if (clearAttempts >= MaxClearAttempts)
                return false;
            if (!RoadSurface.TryNearestOffRoad(transform.position, out var clear))
                return false;

            clearAttempts++;
            clearingRoad = true;
            clearIntent = intent;
            clearUntil = Time.time + RoadClearTimeout;
            targetPoint = clear;
            isMoving = true;
            return true;
        }

        /// <summary>
        /// Arrived at the clear point - or at least at the avoidance arrival radius of it, which
        /// is why this re-tests rather than assuming. Then the deferred stand-down happens.
        /// </summary>
        void FinishRoadClear()
        {
            var intent = clearIntent;
            clearingRoad = false;

            // Still on it - blocked short of the kerb by a crowd, or the push landed somewhere
            // that is road too. Aim again from where we actually stand, up to MaxClearAttempts.
            if (BeginRoadClear(intent))
                return;

            clearAttempts = 0;
            clearIntent = RoadClearIntent.None;

            switch (intent)
            {
                case RoadClearIntent.RouteEnd:
                    CompleteRoute();
                    break;

                case RoadClearIntent.Repath:
                    isMoving = false;
                    QueueRepath();
                    break;

                case RoadClearIntent.Failed:
                    // The retry timer armed by Repath is still running; stand and wait for it.
                    isMoving = false;
                    break;
            }
        }

        /// <summary>
        /// PATCH (Living City): the net under every stand-down. Whatever stopped this walker -
        /// a light whose event never came, an idler that halted on a timer, an activity that
        /// handed control back on the wrong ground - if it is standing on the carriageway, it
        /// walks off. Runs only while stopped, once a second, staggered per body.
        /// </summary>
        void TickStranded()
        {
            if (Time.time < nextStrandedCheck)
                return;

            // The jitter term is not cosmetic: without it a crowd that stood down on the same
            // frame would check on that same frame for the rest of the session.
            nextStrandedCheck = Time.time + StrandedInterval
                                * (1f + (body != null ? body.Jitter : 0f));

            BeginRoadClear(RoadClearIntent.Repath);
        }

        /// <summary>
        /// PATCH (Living City): the route-completion re-path, extracted so the central queue
        /// can grant it on its own budget. Safe to call only while isMoving is false.
        /// </summary>
        public void Repath()
        {
            if (isMoving)
                return;

            // PATCH (Living City): the scripted destination outranks both existing modes -
            // it exists precisely because neither of them can express "go to X".
            if (hasScriptedDestination)
            {
                start = transform.position;
                destination = scriptedDestination;
            }
            else if (randomDestination)
            {
                //Selects random tile which is at least 90m away
                SetRandomDestination();
            }
            else
            {
                destination = start;
                start = transform.position;
            }
            trajectory = pathFinding.GetPath(start, destination, PathType.Sidewalk);
            if (trajectory != null)
            {
                activePath = 0;
                activepoint = 0;
                GetClocestPoint();
                speed = 0;
                isMoving = true;
                clearingRoad = false;
                clearAttempts = 0;
                clearIntent = RoadClearIntent.None;
                pathFailedAt = 0f;
                loggedPathFailure = false;
                targetPoint = trajectory[activePath].pathPositions[activepoint].transform.position + (trajectory[activePath].pathPositions[activepoint].transform.right * Random.Range(-0.8f, 0.8f));
            }
            else
            {
                // PATCH (Living City): once per walker, not once per retry - the retry below
                // runs every RepathRetrySeconds for as long as the graph stays severed, and at
                // crowd scale that is a console flood.
                if (!loggedPathFailure)
                {
                    loggedPathFailure = true;
                    Debug.Log(name + ": Path not found");
                }

                // PATCH (Living City): this used to be the end of that pedestrian. isMoving
                // stayed false, nothing ever asked for a path again, and the walker stood there
                // for the rest of the session - which, standing on a crossing, is a car stopped
                // for the rest of the session too, and a queue behind it. Get off the road, then
                // try again on a timer.
                pathFailedAt = Time.time;
                BeginRoadClear(RoadClearIntent.Failed);
            }
        }
        private void SetRandomDestination()
        {
            start = transform.position;
            destination = start;
            // PATCH (Living City): this loop was unbounded. If no tile satisfies the
            // hardcoded 60-300m window - which is the case on any map smaller than about
            // 7x7 tiles, or for a pedestrian near the centre of a small map - Unity hangs
            // outright with no error rather than logging a failure. Bound the search.
            if (Tile.Tiles.Count == 0)
                return;
            int tries = 0;
            int maxTries = Tile.Tiles.Count * 4;
            while (Vector3.Distance(start, destination) < 60 || Vector3.Distance(start, destination) > 300)
            {
                if (++tries > maxTries)
                {
                    Debug.LogWarning(name + ": no sidewalk destination found in the 60-300m range; staying put.");
                    return;
                }
                Tile t = Tile.Tiles[UnityEngine.Random.Range(0, Tile.Tiles.Count - 1)];
                if (t.tileType == Tile.TileType.Road || t.tileType == Tile.TileType.OnlyPathwalk)
                {
                    if (t.verticalType == Tile.VerticalType.Bridge)
                    {
                        destination = t.transform.position + (Vector3.up * 12);
                    }
                    else
                    {
                        destination = t.transform.position;
                    }
                }
            }
        }
        private void GetClocestPoint()
        {
            float minDistance = Mathf.Infinity;
            for (int i = 0; i < trajectory[activePath].pathPositions.Count; i++)
            {
                float distance = Vector3.Distance(trajectory[activePath].pathPositions[i].position, transform.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    activepoint = i;
                }
            }
        }
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("TrafficLightCrosswalk"))
            {
                TrafficLight trafic = LightFor(other);

                // PATCH (Living City): wait at the kerb, never in the road. The trigger volume
                // reaches from the road centre to about three metres past the kerb (measured off
                // traffic-lights_AI: local x 3.2, span 6.65), so someone walking up the pavement
                // still enters it off the asphalt and stops exactly as before. Someone already
                // out on the carriageway when the light catches them finishes crossing instead of
                // freezing in front of the traffic - which is what a person does, and what stops
                // them from holding a car for a whole light phase.
                if (trafic && trafic.isGreen && !RoadSurface.IsOnRoad(transform.position))
                {
                    isMoving = false;
                    trafic.lightChange += StartMoving;
                }
            }
        }
        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("TrafficLightCrosswalk"))
            {
                TrafficLight trafic = LightFor(other);
                if (trafic)
                    trafic.lightChange -= StartMoving;
            }
        }

        // PATCH (Living City): the crosswalk trigger colliders are few and never move, so the
        // parent walk runs once per collider per process instead of once per crossing per
        // pedestrian. Scene reloads leave dead keys behind; they can never match a live
        // collider again and the count is bounded by the crosswalk count, so no purge.
        private static TrafficLight LightFor(Collider other)
        {
            if (!CrosswalkLights.TryGetValue(other, out TrafficLight light))
                CrosswalkLights[other] = light = other.GetComponentInParent<TrafficLight>();
            return light;
        }
        void StartMoving(bool isGreen)
        {
            if(!isGreen)
                isMoving = true;
        }
    }
}
