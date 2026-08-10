using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.City
{
    [RequireComponent(typeof(PathFinding)), RequireComponent(typeof(Rigidbody))]
    public class CarBehavior : MonoBehaviour
    {
        //[HideInInspector]
        public List<Path> trajectory = new List<Path>();
        public bool randomDestination = true;
        public bool closedCircuit = false;
        private PathFinding pathFinding;
        public float minDistance = 90f;
        public float maxspeed = 5.0f;
        public float maxTurnAngle = 35f;
        public float acceleration = 0.5f;

        public Transform rearWheelsMiddlePoint;
        private int rearWheelsPath = 0;
        private int rearWheelsCheckpoint = 1;
        public Transform frontWheelsMiddlePoint;
        private int frontWheelsPath = 0;
        private int frontWheelsCheckpoint = 1;
        public List<Transform> FrontWheels = new List<Transform>();
        public List<Transform> RearWheels = new List<Transform>();

        private const float KMHTOMS = 5f / 18.0f;
        private float speed;
        private float currentMaxSpeed;
        private float angleDelta = 0;
        private float wheelBase = 0;

        float angle = 0;
        bool isMoving = false;
        bool drivingTrafficLights = false;
        int randomPathTries = 10;

        private Vector3 targetDrivePoint;
        private Vector3 frontPathDirection;
        private Vector3 rearPathDirection;
        private Vector3 destination;

        public List<Vector3> checkpoints = new List<Vector3>();
        private Vector3 start;

        // PATCH (Living City): hooks for the traffic system in Assets/Scripts/Entities. The
        // package assumes a car is born on the road and stays there forever, picking random
        // destinations until the scene ends. This project needs a car to enter at the edge of
        // the map, be told where to go, and be taken away when it gets there.

        /// <summary>Route to a specific point instead of drawing a random tile at least minDistance away.</summary>
        [HideInInspector] public bool hasScriptedDestination;
        [HideInInspector] public Vector3 scriptedDestination;

        /// <summary>
        /// Raised when the car reaches the end of its trajectory, BEFORE the next route is
        /// chosen, so a handler can set scriptedDestination in time for it to be used.
        /// </summary>
        public event System.Action routeCompleted;

        /// <summary>
        /// Set from a routeCompleted handler to take ownership of the car: no new path is
        /// chosen and the car stops. Destroy() only takes effect at the end of the frame, so
        /// without this the car would pathfind once more on its way out.
        /// </summary>
        [HideInInspector] public bool stopHere;

        /// <summary>
        /// One-shot: place the car at the very start of its first lane rather than at a random
        /// point along it. Used when a car enters through a gate on the outline of the map.
        /// </summary>
        [HideInInspector] public bool snapToPathStart;

        /// <summary>How many destinations to try before accepting one that needs the snap below.</summary>
        const int RepathAttempts = 3;

        /// <summary>
        /// How near a lane the car has to be to count as already driving down it. Carriageway
        /// lanes sit 3m apart (x = +1.5 against x = -1.5), so 2m accepts the lane the car is on
        /// and rejects the one coming the other way.
        /// </summary>
        const float LaneMatchRadius = 2f;

        /// <summary>How closely the car has to be pointing along the lane. 0.5 is 60 degrees.</summary>
        const float LaneMatchFacing = 0.5f;

        /// <summary>
        /// Seconds of following distance. Set from CityConfig by VehicleSpawner; the default is
        /// what a car gets in a scene assembled by hand.
        /// </summary>
        [HideInInspector] public float headway = LivingCity.Entities.CarFollowing.DefaultHeadway;

        /// <summary>
        /// This car's entry in the traffic registry - see TrafficRegistry for why the pack's own
        /// trigger-based avoidance had to go. Null only if registration failed.
        /// </summary>
        LivingCity.Entities.TrafficBody trafficBody;

        /// <summary>How many positions along the lane to try before accepting an occupied one.</summary>
        const int ClearSpotAttempts = 8;

        // PATCH (Living City): don't block the box.
        //
        // Nothing else in the system asks whether a junction can be EXITED before it is entered -
        // right of way only compares two cars' distance to each other. So when the street beyond
        // a junction backed up, cars kept flowing into the box until they stood in each other's
        // way from three directions at once, and a cyclic block like that is beyond anything a
        // pairwise rule can undo. The check here is the missing question: if a stationary car
        // leaves no room past the far side of the junction ahead, stop at the entry line, as a
        // comfort-braking target fed to the speed model - never as a hard clamp, because the
        // virtual stop line is a courtesy, not a body.

        /// <summary>How far short of the junction's first lane the car aims to stand, metres.</summary>
        const float JunctionEntryMargin = 0.75f;

        /// <summary>
        /// Seconds after which a car gives up waiting at the entry line and reverts to the old
        /// behaviour. A car held here accrues no stall - it has clear road in front of it - so
        /// without a timeout a false positive (a misread tile, a blocker that deactivated
        /// without moving) would hold it, and everyone behind it, forever. Reverting is safe:
        /// worst case is exactly the pre-rule behaviour, which the escape ladder now bounds.
        /// </summary>
        const float JunctionHoldTimeout = 12f;

        /// <summary>Time.time when the current entry-line hold began; negative when not holding.</summary>
        float junctionHoldSince = -1f;

        // PATCH (Living City): a watchdog over the three trigger stops below.
        //
        // Each of them sets isMoving = false and then waits for a C# event to set it back. There
        // is no timeout, so a single lost event parks the car in a live lane permanently. And the
        // events are genuinely losable: Crosswalk only fires when its pedestrian count decrements
        // to zero, so one pedestrian destroyed or deactivated inside the trigger leaves
        // PedestriansAreCrossing true for the rest of the scene and every car that ever stops
        // there is finished. TrafficLight.ChangeCrosswalk toggles rather than assigns, so one
        // missed invoke desynchronises it the same way.
        //
        // The fix is to stop trusting the event and go and look. Holding the reference that
        // stopped us is what makes that possible - it also keeps the watchdog strictly scoped to
        // trigger stops, so it can never override stopHere or the spawn stagger.

        /// <summary>Seconds held at a trigger before the car re-reads the blocker's actual state.</summary>
        public const float HoldWatchdog = 6f;

        /// <summary>
        /// Hard ceiling on a crosswalk hold, whatever the crossing claims. Crosswalk itself now
        /// stops reporting people who have merely stopped on it, so nothing should ever reach
        /// this - which is the point: it is the backstop that makes "a car is stuck forever"
        /// unreachable no matter what goes wrong in front of it. Not applied to lights (they
        /// cycle) or level crossings (a train legitimately takes a while).
        ///
        /// The asymmetry is the argument for having it at all: releasing a car early clips
        /// through a body there is no physics between anyway, for a frame; never releasing it
        /// queues every car behind it, and those queues merge at junctions until the district is
        /// solid.
        /// </summary>
        public const float CrosswalkPatience = 15f;

        /// <summary>
        /// May a car held by a crossing go? Pure, so the model tests can prove the property this
        /// whole change exists for: for any stream of crossing reports, no hold outlives
        /// <see cref="CrosswalkPatience"/>.
        /// </summary>
        public static bool CrosswalkHoldExpired(bool stillCrossing, float heldFor) =>
            heldFor >= HoldWatchdog && (!stillCrossing || heldFor >= CrosswalkPatience);

        TrafficLight heldByLight;
        Crosswalk heldByCrosswalk;
        LevelCrossingController heldByLevelCrossing;
        float heldSince;

        /// <summary>
        /// The crossing this car's handler is on, which outlives the hold: ReleaseHold drops
        /// heldByCrosswalk while OnTriggerExit still owns the unsubscribe. Kept so OnDisable can
        /// clean up after a car destroyed inside the trigger, which never gets that exit.
        /// </summary>
        Crosswalk subscribedCrosswalk;


        private void Awake()
        {
            pathFinding = GetComponent<PathFinding>();
        }

        // PATCH (Living City): the registry has to see a car for exactly as long as it is in the
        // scene. OnDisable rather than OnDestroy because CarBehavior deactivates itself when it
        // runs out of pathfinding retries, and VehicleSpawner's sweep may not collect it for
        // another five seconds - during which a deactivated car is not an obstacle.
        private void OnEnable()
        {
            trafficBody = LivingCity.Entities.TrafficRegistry.Register(this);
        }

        private void OnDisable()
        {
            LivingCity.Entities.TrafficRegistry.Unregister(trafficBody);
            trafficBody = null;

            // PATCH (Living City): stateChange is a raw multicast field on a component that
            // outlives this car - a car destroyed inside a crossing's trigger never runs
            // OnTriggerExit, and its handler would stay on that crossing's list for the rest of
            // the scene, called on a dead MonoBehaviour every time somebody crosses.
            if (subscribedCrosswalk)
            {
                subscribedCrosswalk.stateChange -= CrosswalkChange;
                subscribedCrosswalk = null;
            }
            heldByCrosswalk = null;
        }

        void Start()
        {
            currentMaxSpeed = maxspeed;
            wheelBase = Vector3.Distance(frontWheelsMiddlePoint.localPosition, rearWheelsMiddlePoint.localPosition);
            if (closedCircuit)
                checkpoints.Add(checkpoints[0]);
            SetNewPath();
        }
        //Finds and sets up new path to follow
        public void SetNewPath()
        {
            isMoving = false;

            // PATCH (Living City): the stop below belongs to re-pathing, not to a trigger, so the
            // watchdog must not see a stale reference from an earlier hold and decide this one is
            // over. It would cut the spawn stagger short and, worse, resume a car whose trajectory
            // is still null.
            ReleaseHold();
            if (randomDestination)
            {
                start = transform.position;
                randomPathTries--;

                // PATCH (Living City): a scripted destination overrides the random draw below.
                // It ignores minDistance on purpose - an exit gate is wherever it is, and a car
                // that has nearly reached one still has to be allowed to finish the trip.
                if (hasScriptedDestination)
                {
                    destination = scriptedDestination;
                    trajectory = pathFinding.GetPath(start, destination, PathType.Road);
                }
                else
                {
                    // PATCH (Living City): try a few destinations and prefer one whose route the
                    // car is ALREADY driving along, which lets the snap further down be skipped.
                    // See AlreadyDriving for why the snap cannot simply be deleted.
                    trajectory = null;
                    for (int attempt = 0; attempt < RepathAttempts; attempt++)
                    {
                        destination = start;
                        int tries = 0;
                        //Selects random tile which is at least minDistance away
                        while (Vector3.Distance(start, destination) < minDistance && tries < Tile.Tiles.Count)
                        {
                            tries++;
                            Tile t = Tile.Tiles[UnityEngine.Random.Range(0, Tile.Tiles.Count - 1)];
                            if (t.tileType == Tile.TileType.Road || t.tileType == Tile.TileType.RoadAndRail)
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
                        if (tries == Tile.Tiles.Count)
                        {
                            // PATCH (Living City): this was a `return`, and it is the quietest way
                            // a car dies. isMoving was set false at the top of SetNewPath and
                            // trajectory was set null just above, so returning here left the car
                            // stationary in a live lane with the GameObject still ACTIVE - which
                            // means VehicleSpawner.SweepRoutine, which only collects deactivated
                            // cars, never replaced it. A permanent wall, and everything behind it
                            // queues up. Fall through instead: trajectory stays null, the else
                            // branch at the bottom retries or deactivates, and the sweep can see
                            // it either way.
                            Debug.Log(name + ": Target Tile not found farther then " + minDistance + "m");
                            continue;
                        }
                        //Path finding
                        List<Path> candidate = pathFinding.GetPath(start, destination, PathType.Road);
                        if (candidate == null)
                            continue;

                        trajectory = candidate;
                        if (AlreadyDriving(candidate, out _))
                            break;
                    }
                }
            }
            else
            {
                //Path finding with checkpoints
                if (!closedCircuit)
                    checkpoints.Reverse();
                trajectory = pathFinding.GetPathWithCheckpoints(checkpoints, PathType.Road);
            }
            if (trajectory != null)
            {
                // PATCH (Living City): randomPathTries decrements on every call, including
                // successful ones, and was never reset. After ~10 destinations it reached 0
                // and the next pathfinding miss deactivated the car permanently, so traffic
                // silently drained away over time. Reset the budget whenever a path is found.
                randomPathTries = 10;
                speed = 0;
                rearWheelsPath = 0;
                rearWheelsCheckpoint = 1;
                frontWheelsPath = 0;
                frontWheelsCheckpoint = 1;

                // PATCH (Living City): currentMaxSpeed is set once in Start() and afterwards only
                // on lane transitions, so a car that re-paths carries the limit of whatever lane it
                // was last on into a road with a different one. Re-seed it from the new route.
                currentMaxSpeed = LaneSpeed(trajectory[0]);

                // PATCH (Living City): where the car ends up standing once it has a route.
                //
                // The package always teleports it - see the Lerp at the bottom of this block.
                // That is not gratuitous: PathFinding.GetPath seeds its open set with EVERY lane
                // of the start tile, not the one the car happens to be on, so trajectory[0] can
                // come back as the opposite carriageway and the snap is what guarantees the car
                // is actually on its own route. Deleting it makes cars cut across the median.
                //
                // But it fires on every re-path, not just at birth, so a car visibly jumps up to
                // a segment length every time it reaches a destination. Both cases below avoid
                // that without giving up the guarantee:
                //
                //   enteringAtGate - the car was placed on the outline of the map by the spawner
                //                    and must start exactly at the head of its first lane.
                //   onRoute        - the car is measurably already driving down trajectory[0],
                //                    so there is nothing to correct and moving it is pure damage.
                //
                // Anything else falls through to the package's teleport unchanged.
                bool enteringAtGate = snapToPathStart && trajectory[0].pathPositions.Count > 1;
                bool onRoute = false;

                if (enteringAtGate)
                {
                    snapToPathStart = false;
                    Vector3 head = trajectory[0].pathPositions[0].position;
                    transform.SetPositionAndRotation(head,
                        Quaternion.LookRotation((trajectory[0].pathPositions[1].position - head).normalized));
                }
                else if (randomDestination && AlreadyDriving(trajectory, out int resumeCheckpoint))
                {
                    onRoute = true;
                    frontWheelsCheckpoint = resumeCheckpoint;
                    rearWheelsCheckpoint = resumeCheckpoint;
                }
                else
                {
                    float closest = float.MaxValue;
                    for (int i = 1; i < trajectory[0].pathPositions.Count; i++)
                    {
                        float tmp = Vector3.Distance(trajectory[0].pathPositions[i].position, transform.position);
                        if (tmp < closest)
                        {
                            closest = tmp;
                            frontWheelsCheckpoint = i;
                            rearWheelsCheckpoint = i;
                        }
                    }
                }
                frontPathDirection = (trajectory[frontWheelsPath].pathPositions[frontWheelsCheckpoint].position - trajectory[frontWheelsPath].pathPositions[frontWheelsCheckpoint - 1].position).normalized;
                rearPathDirection = (trajectory[rearWheelsPath].pathPositions[rearWheelsCheckpoint].position - trajectory[rearWheelsPath].pathPositions[rearWheelsCheckpoint - 1].position).normalized;
                targetDrivePoint = trajectory[frontWheelsPath].pathPositions[frontWheelsCheckpoint].transform.position;
                if (randomDestination && !enteringAtGate && !onRoute)
                {
                    Vector3 segmentStart = trajectory[frontWheelsPath].pathPositions[frontWheelsCheckpoint - 1].transform.position;
                    Vector3 segmentEnd = trajectory[frontWheelsPath].pathPositions[frontWheelsCheckpoint].transform.position;

                    // PATCH (Living City): pick a spot on the segment that is not already taken.
                    //
                    // This teleport is one of only two ways a car can end up INSIDE another one -
                    // it can drop thirty cars onto the network in a single frame at the start of a
                    // scene, and it fires again on every re-path. The clamp in Update deliberately
                    // stands down when bodies overlap, so an overlap created here would be driven
                    // out of rather than prevented. Cheaper to not create it.
                    // PATCH (Living City): every candidate is tested, including the last one. The
                    // earlier loop drew, tested, then redrew - so the value it finally assigned
                    // was always the one draw nobody had looked at, and all the testing bought
                    // nothing on the attempt that mattered. Draw first, test second, keep the
                    // first clear one.
                    Vector3 spot = Vector3.Lerp(segmentStart, segmentEnd, Random.Range(0f, 0.9f));
                    for (int attempt = 0; attempt < ClearSpotAttempts; attempt++)
                    {
                        Vector3 candidate = Vector3.Lerp(segmentStart, segmentEnd, Random.Range(0f, 0.9f));
                        spot = candidate;
                        if (LivingCity.Entities.TrafficRegistry.IsClear(trafficBody, candidate, frontPathDirection))
                            break;
                    }

                    // If none was clear the teleport still happens. The snap is what guarantees
                    // the car is on its OWN carriageway rather than cutting across the median -
                    // see the block above - and a median cut is permanent damage where an overlap
                    // is now recoverable: the pair is left out of each other's allowance and creeps
                    // apart within a couple of seconds.
                    transform.position = spot;
                    transform.rotation = Quaternion.LookRotation(frontPathDirection);
                }

                // PATCH (Living City): a car arriving through a gate pulls away at once. The
                // package's up-to-two-second pause is there to stagger a grid full of cars that
                // all spawned on the same frame; a single car sitting motionless on the outline
                // of the map, in plain view, is the opposite of what it is for.
                StartCoroutine(StartMovingAfterWait(enteringAtGate ? 0f : Random.Range(0.5f, 2f)));
            }
            else
            {
                if (randomDestination && randomPathTries > 0)
                {
                    Debug.Log(name + ": Path not found, End tile: " + destination + " || Trying new path");
                    SetNewPath();
                }
                else
                {
                    Debug.LogWarning(name + ": Path not found, End tile: " + destination);
                    gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// PATCH (Living City): is the car already driving along the first lane of this route,
        /// and if so which checkpoint is it heading for?
        ///
        /// Both tests have to pass. Distance alone would accept the opposite carriageway, which
        /// is only 3m away and running the other way - taking it would send the car up the wrong
        /// side of the street. Facing alone would accept a parallel lane a block over.
        /// </summary>
        private bool AlreadyDriving(List<Path> route, out int checkpoint)
        {
            checkpoint = 1;

            if (route == null || route.Count == 0 || !route[0])
                return false;

            List<Transform> points = route[0].pathPositions;
            if (points == null || points.Count < 2)
                return false;

            // With a longer route the car can sit on the last checkpoint of the first lane and
            // simply carry on into the second. With a one-lane route there is nothing to carry
            // on into, and resuming there would finish the trajectory on the spot and re-path
            // forever, so that case has to fall through to the package's teleport.
            bool routeEndsOnThisLane = route.Count == 1;

            Vector3 position = transform.position;
            float bestDistance = LaneMatchRadius;
            bool found = false;

            for (int i = 1; i < points.Count; i++)
            {
                if (!points[i] || !points[i - 1])
                    continue;

                Vector3 from = points[i - 1].position;
                Vector3 segment = points[i].position - from;
                float lengthSquared = segment.sqrMagnitude;
                if (lengthSquared < 0.01f)
                    continue;

                float along = Mathf.Clamp01(Vector3.Dot(position - from, segment) / lengthSquared);
                if (routeEndsOnThisLane && i == points.Count - 1 && along > 0.999f)
                    continue;

                float distance = Vector3.Distance(position, from + segment * along);
                if (distance > bestDistance)
                    continue;
                if (Vector3.Dot(transform.forward, segment / Mathf.Sqrt(lengthSquared)) < LaneMatchFacing)
                    continue;

                bestDistance = distance;
                checkpoint = i;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// PATCH (Living City): a lane's speed limit in km/h, capped by the car's own.
        ///
        /// Path.speed is 0 on any lane whose limit was never authored - every sidewalk path in the
        /// pack is one, and so is anything hand-built. Feeding that straight into currentMaxSpeed,
        /// as the package did, parks the car mid-road at zero desired speed with a queue building
        /// behind it. An unset limit means "no limit", not "stop".
        /// </summary>
        private float LaneSpeed(Path lane)
        {
            if (!lane || lane.speed <= 0)
                return maxspeed;

            return Mathf.Min(lane.speed, maxspeed);
        }

        private void UpdateCheckpoint(ref int path, ref int checkpoint, ref Vector3 pathDirection, Transform wheelsMidpoint, bool isFront)
        {
            if (!trajectory[path] || !trajectory[path].gameObject.activeInHierarchy)
            {
                SetNewPath();
                if (trajectory == null)
                    return;
            }
            Vector3 target = trajectory[path].pathPositions[checkpoint].position;

            //Is wheel behind the checkpoint
            if (Vector3.Dot((target - wheelsMidpoint.position).normalized, (target - (wheelsMidpoint.position - wheelsMidpoint.forward * 20)).normalized) <= 0.2f && Vector3.Dot(pathDirection, wheelsMidpoint.forward) > -0.5f)
            {
                if (path == trajectory.Count - 1)
                {
                    if (checkpoint == trajectory[path].pathPositions.Count - 1)
                    {
                        if (isFront)
                        {
                            trajectory[path].Vehicles.Remove(this);

                            // PATCH (Living City): the end of a trajectory is the one moment the
                            // traffic system can redirect a car, so it is announced before the
                            // next route is chosen. A handler that sets stopHere is taking the
                            // car away and must not have another path picked underneath it.
                            routeCompleted?.Invoke();
                            if (stopHere)
                            {
                                isMoving = false;
                                return;
                            }

                            SetNewPath();
                        }
                        return;
                    }
                    else
                    {
                        checkpoint++;
                    }
                }
                else
                {
                    if (checkpoint == trajectory[path].pathPositions.Count - 1)
                    {
                        path++;
                        checkpoint = 1;
                        if (!trajectory[path] || !trajectory[path].gameObject.activeInHierarchy || Vector3.Distance(trajectory[path].pathPositions[0].position, trajectory[path - 1].pathPositions[trajectory[path - 1].pathPositions.Count - 1].position) > 1.5f)
                        {
                            SetNewPath();
                            if (trajectory == null)
                                return;
                        }
                        else if (isFront)
                        {
                            currentMaxSpeed = LaneSpeed(trajectory[path]);
                            trajectory[path - 1].Vehicles.Remove(this);
                            trajectory[path].Vehicles.Add(this);
                        }
                    }
                    else
                    {
                        checkpoint++;
                    }
                }
                pathDirection = (trajectory[path].pathPositions[checkpoint].position - trajectory[path].pathPositions[checkpoint - 1].position).normalized;
                if (isFront)
                {
                    targetDrivePoint = trajectory[path].pathPositions[checkpoint].transform.position;
                    if (checkpoint == 2)
                    {
                        angleDelta = Vector3.SignedAngle(transform.forward, pathDirection.normalized, transform.up) * 0.33f;
                    }
                }
            }
        }
        //Update car incline to keep wheels stay at path
        private void UpdateCarIncline()
        {
            float ratio = frontWheelsMiddlePoint.localPosition.z / wheelBase;
            Vector3 frontPoint = Vector3.Project(frontWheelsMiddlePoint.position - trajectory[frontWheelsPath].pathPositions[frontWheelsCheckpoint - 1].position, frontPathDirection) + trajectory[frontWheelsPath].pathPositions[frontWheelsCheckpoint - 1].position;
            Vector3 rearPoint = Vector3.Project(rearWheelsMiddlePoint.position - trajectory[rearWheelsPath].pathPositions[rearWheelsCheckpoint - 1].position, rearPathDirection) + trajectory[rearWheelsPath].pathPositions[rearWheelsCheckpoint - 1].position;

            transform.SetPositionAndRotation(new Vector3(transform.position.x, Vector3.Lerp(frontPoint, rearPoint, ratio).y, transform.position.z),
                Quaternion.LookRotation(new Vector3(transform.forward.x, (frontPoint - rearPoint).normalized.y, transform.forward.z)));
        }

        void Update()
        {
            if (isMoving && trajectory != null)
            {
                if (rearWheelsMiddlePoint && frontWheelsMiddlePoint)
                {
                    //Update path checkpoints for wheel axles
                    UpdateCheckpoint(ref frontWheelsPath, ref frontWheelsCheckpoint, ref frontPathDirection, frontWheelsMiddlePoint, true);
                    if (trajectory == null)
                        return;
                    UpdateCheckpoint(ref rearWheelsPath, ref rearWheelsCheckpoint, ref rearPathDirection, rearWheelsMiddlePoint, false);

                    // PATCH (Living City): longitudinal control, rewritten.
                    //
                    // What stood here matched the speed of the car in front IF it happened to be
                    // slower. That is not a following distance: two cars at the same speed fail the
                    // comparison outright, so the follower kept closing at full relative speed until
                    // it was inside the leader - which is what "cars drive through each other"
                    // looked like. TrafficRegistry has the rest of the diagnosis.
                    //
                    // In its place, two independent things:
                    //
                    //   the Intelligent Driver Model  - how fast the car WANTS to go, which
                    //                                   converges on a gap rather than on a speed
                    //   the clamp further down        - how far it is ALLOWED to move this frame
                    //
                    // The separation is the point. The model can be mistuned, or lose sight of a
                    // car around a bend, and the clamp still makes overlapping impossible.

                    // PATCH (Living City): the cornering derate, retuned - both numbers here were
                    // sized for a car following a lane, not for one pivoting through a junction.
                    //
                    // A junction turn is a polyline with a corner, not an arc, and UpdateCheckpoint
                    // does not retarget until the front axle is ABEAM that corner - so all of the
                    // heading change is spent after it, at whatever speed the car arrived with. A
                    // passenger car needs ~2.9m to reach full lock and then a 5.4m arc at its 3.89m
                    // minimum radius, which is why it used to swing several metres wide of the node
                    // and a bus eight to ten.
                    //
                    // The floor was 0.65: at full lock the car still wanted 65% of the lane limit.
                    // 0.5 halves it instead. And the 0.8 that LaneChange and RampExit get now
                    // covers Turn, which had no shape derate at all despite being the sharpest
                    // thing a car does. Both are deliberately conservative - the turn lanes are
                    // authored at 25-30km/h and CityConfig.carMaxSpeed caps everything at 45, so
                    // this narrows an already-slow band rather than making the city sluggish.
                    float steerAngle = Vector3.SignedAngle(transform.forward, (targetDrivePoint - frontWheelsMiddlePoint.position).normalized, Vector3.up);
                    float cornering = Mathf.Clamp(1f - Mathf.Abs(steerAngle) / maxTurnAngle, 0.5f, 1f);

                    float desiredSpeed = Mathf.Min(currentMaxSpeed, maxspeed) * cornering;
                    if (trajectory[frontWheelsPath].pathShape == PathShape.LaneChange
                        || trajectory[frontWheelsPath].pathShape == PathShape.RampExit
                        || trajectory[frontWheelsPath].pathShape == PathShape.Turn)
                        desiredSpeed *= 0.8f;

                    float speedMs = speed * KMHTOMS;
                    if (trafficBody != null)
                        trafficBody.SpeedMs = speedMs;

                    float lookahead = LivingCity.Entities.CarFollowing.Lookahead(speedMs, headway);
                    var ahead = LivingCity.Entities.TrafficRegistry.Probe(trafficBody, lookahead);

                    // PATCH (Living City): don't block the box - see the constants above. The
                    // stall release outranks the hold: a car the registry is actively trying to
                    // unstick must not be re-stuck by its own caution.
                    float gapForModel = ahead.Gap;
                    float leadForModel = ahead.LeadSpeedMs;
                    if (trafficBody != null && !ahead.ReleaseActive
                        && LivingCity.Entities.JunctionMap.TryFindJunctionAhead(
                               trajectory, frontWheelsPath, frontWheelsCheckpoint,
                               frontWheelsMiddlePoint.position, lookahead,
                               out float distToJunction, out Vector3 junctionExitPos, out Vector3 junctionExitDir))
                    {
                        float requiredRoom = 2f * trafficBody.HalfLength
                                           + LivingCity.Entities.CarFollowing.StandstillGap;
                        if (LivingCity.Entities.TrafficRegistry.IsExitBlocked(
                                trafficBody, junctionExitPos, junctionExitDir, requiredRoom))
                        {
                            if (junctionHoldSince < 0f)
                                junctionHoldSince = Time.time;

                            if (Time.time - junctionHoldSince < JunctionHoldTimeout)
                            {
                                float holdGap = Mathf.Max(0f, distToJunction - JunctionEntryMargin);
                                if (holdGap < gapForModel)
                                {
                                    gapForModel = holdGap;
                                    leadForModel = 0f;
                                }
                            }
                        }
                        else
                        {
                            junctionHoldSince = -1f;
                        }
                    }
                    else
                    {
                        junctionHoldSince = -1f;
                    }

                    // PATCH (Living City): the model, plus the way out of a jam that the model
                    // cannot provide.
                    //
                    // IDM at rest wants a 1.5m gap, so at any smaller gap it returns a negative
                    // acceleration and the Max(0, ...) inside NextSpeed holds the car at zero -
                    // forever, because nothing ever pushes cars apart. Meanwhile the clamp below
                    // deliberately parks a stopped car at 0.6m. Every car the model stopped was
                    // therefore stopped for good, and one of those in a lane queues the whole city
                    // behind it. That is the freeze.
                    //
                    // Two states earn a creep, and only these two: the registry has declared the
                    // car stalled, or the car is genuinely inside another body. Both are already
                    // broken situations that comfort has no opinion about. A normal queue at a red
                    // light is neither, so it still settles at the comfortable 1.5m rather than
                    // compacting to bumper contact.
                    speedMs = LivingCity.Entities.CarFollowing.NextSpeed(
                        speedMs, desiredSpeed * KMHTOMS, gapForModel, leadForModel, headway,
                        ahead.ReleaseActive || ahead.Overlapping, Time.deltaTime);

                    speed = speedMs / KMHTOMS;


                    if (trajectory[frontWheelsPath].pathShape == PathShape.Curve && frontWheelsCheckpoint > 1 && frontWheelsCheckpoint <= 3)
                    {
                        //Calculate turn radius for curve
                        float directionSign = Mathf.Sign(Vector3.SignedAngle(trajectory[frontWheelsPath].pathPositions[2].position - trajectory[frontWheelsPath].pathPositions[1].position, trajectory[frontWheelsPath].pathPositions[3].position - trajectory[frontWheelsPath].pathPositions[1].position, Vector3.up));
                        float radius = Vector3.Distance(trajectory[frontWheelsPath].pathPositions[1].position, trajectory[frontWheelsPath].pathPositions[2].position);
                        float targetAngle = Mathf.Atan(wheelBase / (Mathf.Abs(radius) + wheelBase)) * (180 / Mathf.PI);
                        angle = directionSign * (targetAngle + (directionSign * angleDelta));
                    }
                    else
                    {
                        //Calculate turn delta
                        float targetAngle = Mathf.Clamp(Vector3.SignedAngle(transform.forward, (targetDrivePoint - frontWheelsMiddlePoint.position).normalized, Vector3.up), -maxTurnAngle, maxTurnAngle);
                        float turnDelta = Time.deltaTime * Mathf.Clamp(Mathf.Abs(targetAngle - angle) / maxTurnAngle, 0.35f, 1f) * 150;
                        if ((angle >= 0 && targetAngle < angle) || (angle <= 0 && targetAngle > angle))
                            turnDelta *= 1.25f;

                        if (targetAngle < angle - turnDelta)
                            angle -= turnDelta;
                        else if (targetAngle > angle + turnDelta)
                            angle += turnDelta;
                        else if (targetAngle != angle)
                            angle = targetAngle;

                    }

                    float positionDelta = speedMs * Time.deltaTime;

                    // PATCH (Living City): the guarantee. Whatever the model decided, this frame's
                    // movement may not exceed what the registry allows. The allowance ignores
                    // right of way - being the car with priority is not a licence to occupy
                    // somebody else's bodywork, it only means you are the one who does not have
                    // to slow down gracefully for it.
                    //
                    // It applies unconditionally, including while overlapping something. The
                    // exemption that used to stand here - skip the whole clamp whenever any
                    // blocker overlapped - was the collision: Overlapping was a single flag ORed
                    // over every car in range, so one car crossing the junction ahead let this car
                    // drive straight through the one it was following. The untangling it was
                    // supposed to allow is now handled where it belongs, by leaving overlapped
                    // bodies out of the allowance in TrafficRegistry.Probe, so a pair already
                    // inside each other is unconstrained WITH RESPECT TO EACH OTHER and nothing
                    // else.
                    //
                    // The clearance is subtracted per blocker inside the registry, not here,
                    // because it is no longer one number: a wedged car's clearance against
                    // CROSSING bodies decays with time stuck (see CarFollowing.ClearanceFor), so
                    // a mutually-blocked ring - which this clamp used to hold shut forever, zero
                    // allowance all round, creep achieving nothing - eventually earns the few
                    // tenths of a metre it needs to rotate itself loose.
                    float allowed = ahead.AllowedAdvance;
                    if (allowed < positionDelta)
                    {
                        positionDelta = allowed;

                        // Keep speed, wheel spin and the registry consistent with what actually
                        // happened, or the next frame's model starts from a speed the car never
                        // reached and the wheels spin against a stationary car.
                        speedMs = Time.deltaTime > 0f ? positionDelta / Time.deltaTime : 0f;
                        speed = speedMs / KMHTOMS;
                    }

                    if (trafficBody != null)
                        trafficBody.SpeedMs = speedMs;

                    UpdateCarIncline();

                    if (Mathf.Abs(angle) > 0.2f)
                    {
                        //Updates car position and roatation when turning
                        Vector3 frontWheelsVector = (Quaternion.Euler(0, angle, 0) * (frontWheelsMiddlePoint.right)).normalized;
                        Vector3 rotatePoint = frontWheelsMiddlePoint.position - frontWheelsVector * wheelBase / Vector3.Dot(rearWheelsMiddlePoint.forward, frontWheelsVector);
                        float alpha = Mathf.Sign(angle) * (180 * positionDelta) / (Mathf.PI * Vector3.Distance(rotatePoint, rearWheelsMiddlePoint.position));
                        transform.RotateAround(rotatePoint, transform.up, alpha);
                    }
                    else
                    {
                        //Updates car position and roatation when driving straight
                        Vector3 newPosition = transform.forward * positionDelta;
                        transform.position += newPosition;
                    }

                    //Set correct rotation of car wheels
                    foreach (Transform t in FrontWheels)
                    {
                        t.parent.localRotation = Quaternion.Euler(0, angle, 0);
                        t.Rotate(Vector3.right, speed * Time.deltaTime * 40);
                    }
                    foreach (Transform t in RearWheels)
                    {
                        t.Rotate(speed * Time.deltaTime * 40, 0, 0);
                    }
                }
            }
            else
            {
                speed = 0;

                // PATCH (Living City): a car held at a red light or a crosswalk is still an
                // obstacle, and the cars behind it have to be told it is a stationary one. Without
                // this the registry keeps serving its last moving speed and followers queue up
                // expecting it to pull away.
                if (trafficBody != null)
                    trafficBody.SpeedMs = 0f;

                CheckHoldWatchdog();
            }
        }

        /// <summary>
        /// PATCH (Living City): has the thing that stopped us stopped blocking us?
        ///
        /// Polls the blocker's own state rather than waiting for its event, which is the whole
        /// point - the failure this exists for IS the event not arriving. A destroyed or
        /// deactivated blocker reads as clear, which is correct: it cannot be stopping anyone.
        /// </summary>
        void CheckHoldWatchdog()
        {
            // stopHere means the traffic system is taking the car off the map this frame. It can
            // still be standing on a crosswalk when that happens, and resuming it would drive a
            // car that is mid-Destroy off down the road.
            if (stopHere || trajectory == null)
                return;

            if (!heldByLight && !heldByCrosswalk && !heldByLevelCrossing)
                return;

            if (Time.time - heldSince < HoldWatchdog)
                return;

            if (heldByLight && !heldByLight.isGreen)
                return;
            if (heldByCrosswalk
                && !CrosswalkHoldExpired(heldByCrosswalk.PedestriansAreCrossing, Time.time - heldSince))
                return;
            if (heldByLevelCrossing && heldByLevelCrossing.trainCrossing)
                return;

            ReleaseHold();
            isMoving = true;
        }

        /// <summary>Forget what was holding the car, without touching the event subscriptions -
        /// OnTriggerExit owns those and still fires when the car finally drives clear.</summary>
        void ReleaseHold()
        {
            heldByLight = null;
            heldByCrosswalk = null;
            heldByLevelCrossing = null;
            drivingTrafficLights = false;
        }

        /// <summary>Records which object stopped the car, and when, for the watchdog above.</summary>
        void BeginHold(TrafficLight light, Crosswalk crosswalk, LevelCrossingController levelCrossing)
        {
            if (light)
                heldByLight = light;
            if (crosswalk)
                heldByCrosswalk = crosswalk;
            if (levelCrossing)
                heldByLevelCrossing = levelCrossing;

            heldSince = Time.time;
            isMoving = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            //Handles traffic lights
            if (other.CompareTag("TrafficLight") && !drivingTrafficLights)
            {
                TrafficLight trafic = other.GetComponent<TrafficLight>();
                if (Vector3.Angle(-trafic.transform.forward, transform.forward) < 25)
                {
                    if (!trafic.isGreen)
                    {
                        drivingTrafficLights = true;
                        trafic.lightChange += StartMoving;
                        BeginHold(trafic, null, null);
                    }
                }
            }
            //Handles traffic crosswalks
            if (other.CompareTag("Crosswalk"))
            {
                Crosswalk crosswalk = other.GetComponent<Crosswalk>();
                if (crosswalk.PedestriansAreCrossing)
                {
                    crosswalk.stateChange += CrosswalkChange;
                    subscribedCrosswalk = crosswalk;
                    BeginHold(null, crosswalk, null);
                }

            }
            //Handles traffic level crossing
            else if (other.CompareTag("LevelCrossing"))
            {
                LevelCrossingController levelCrossing = other.GetComponent<LevelCrossingController>();
                if (levelCrossing.trainCrossing)
                {
                    levelCrossing.stateChange += LevelCrossingChange;
                    BeginHold(null, null, levelCrossing);
                }

            }

            // PATCH (Living City): the "Primitive car avoidence" branch that stood here is gone,
            // along with its counterpart in OnTriggerExit. It read the forward trigger box once on
            // entry - about two metres of lookahead on a car doing 100km/h - never re-checked, was
            // disabled outright whenever frontWheelsPath was 0 (which SetNewPath resets, so every
            // re-path blinded the car), and leaned on Path.Vehicles, a list that is never seeded
            // for the first lane and never cleared when a car re-paths or is destroyed. Its
            // crossing rule set isMoving = false with no timeout, so two cars that stopped for
            // each other stayed stopped.
            //
            // Car-to-car spacing now belongs entirely to TrafficRegistry, which is continuous,
            // measures real bodies, and cannot be blinded by a re-path. Traffic lights, crosswalks
            // and level crossings above are untouched.
        }

        private void OnTriggerExit(Collider other)
        {
            // PATCH (Living City): driving clear of the trigger ends the hold too. The car is past
            // it, so whatever its state, it is no longer this car's problem.
            if (other.CompareTag("TrafficLight"))
            {
                TrafficLight trafic = other.GetComponent<TrafficLight>();
                trafic.lightChange -= StartMoving;
                drivingTrafficLights = false;
                heldByLight = null;
            }
            else if (other.CompareTag("Crosswalk"))
            {
                var crosswalk = other.GetComponent<Crosswalk>();
                crosswalk.stateChange -= CrosswalkChange;
                if (subscribedCrosswalk == crosswalk)
                    subscribedCrosswalk = null;
                heldByCrosswalk = null;
            }
            else if (other.CompareTag("LevelCrossing"))
            {
                other.GetComponent<LevelCrossingController>().stateChange -= LevelCrossingChange;
                heldByLevelCrossing = null;
            }
        }
        // PATCH (Living City): each handler now also drops the reference the watchdog holds, so a
        // hold released the normal way leaves nothing behind for it to re-examine. Only the ref
        // it owns - a car can be stopped by a red light and a crosswalk at once, and clearing
        // both because one of them let go is how the watchdog would start waving cars through
        // red lights.
        void StartMoving(bool isGreen)
        {
            if (isGreen)
            {
                drivingTrafficLights = false;
                heldByLight = null;
                isMoving = true;
            }
        }
        void CrosswalkChange(bool crossing)
        {
            if (!crossing && !drivingTrafficLights)
            {
                heldByCrosswalk = null;
                isMoving = true;
            }
        }
        void LevelCrossingChange(bool crossing)
        {
            if (!crossing)
            {
                heldByLevelCrossing = null;
                isMoving = true;
            }
        }

        IEnumerator StartMovingAfterWait(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            isMoving = true;
        }

    }
}