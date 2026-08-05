using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PolyPerfect.City
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
        bool drivingBihindCar = false;
        bool drivingTrafficLights = false;
        int randomPathTries = 10;

        private Vector3 targetDrivePoint;
        private Vector3 frontPathDirection;
        private Vector3 rearPathDirection;
        private Vector3 destination;

        public List<Vector3> checkpoints = new List<Vector3>();
        private Vector3 start;
        private CarBehavior carInFront;

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


        private void Awake()
        {
            pathFinding = GetComponent<PathFinding>();
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
                            Debug.Log(name + ": Target Tile not found farther then " + minDistance + "m");
                            return;
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
                    transform.position = Vector3.Lerp(trajectory[frontWheelsPath].pathPositions[frontWheelsCheckpoint - 1].transform.position, trajectory[frontWheelsPath].pathPositions[frontWheelsCheckpoint].transform.position, Random.Range(0f, 0.9f));
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
                            currentMaxSpeed = Mathf.Min(trajectory[path].speed, maxspeed);
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

                    if (drivingBihindCar && trajectory[frontWheelsPath].Vehicles.Count > 0)
                    {
                        if (carInFront.speed < speed)
                            speed = Mathf.Lerp(speed, carInFront.speed * 0.8f, 12 * Time.deltaTime);
                    }
                    else
                    {
                        float targetSpeed = currentMaxSpeed * Mathf.Clamp((1f - Mathf.Abs(Vector3.SignedAngle(transform.forward, (targetDrivePoint - frontWheelsMiddlePoint.position).normalized, Vector3.up)) / maxTurnAngle), 0.65f, 1f);

                        if (trajectory[frontWheelsPath].pathShape == PathShape.LaneChange || trajectory[frontWheelsPath].pathShape == PathShape.RampExit)
                            speed = Mathf.Lerp(speed, targetSpeed * 0.8f, 4 * Time.deltaTime);
                        else if (speed > targetSpeed)
                            speed = Mathf.Lerp(speed, targetSpeed, 3 * Time.deltaTime);
                        else
                            speed = Mathf.Lerp(speed, maxspeed, acceleration * Time.deltaTime);

                    }


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

                    float positionDelta = speed * KMHTOMS * Time.deltaTime;

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
            }
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
                        isMoving = false;
                        trafic.lightChange += StartMoving;
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
                    isMoving = false;
                }

            }
            //Handles traffic level crossing
            else if (other.CompareTag("LevelCrossing"))
            {
                LevelCrossingController levelCrossing = other.GetComponent<LevelCrossingController>();
                if (levelCrossing.trainCrossing)
                {
                    levelCrossing.stateChange += LevelCrossingChange;
                    isMoving = false;
                }

            }
            // Primitive car avoidence
            else if (other.CompareTag("Car") && !other.isTrigger && frontWheelsPath > 0)
            {
                float direction = Vector3.Angle(transform.forward, other.transform.forward);
                float carDirection = Vector3.Angle(transform.right, (other.transform.position - transform.position).normalized);
                if (direction < 60)
                {
                    carInFront = other.GetComponentInParent<CarBehavior>();
                    if (trajectory[frontWheelsPath].Vehicles.Contains(carInFront) || direction < 45)
                        drivingBihindCar = true;
                }
                if (direction > 40 && carDirection < 80 && carDirection > 45 && !drivingBihindCar && direction < 110)
                {
                    isMoving = false;
                }

            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Car") && !other.isTrigger)
            {
                StopCoroutine(StartMovingAfterWait(0.2f));
                StartCoroutine(StartMovingAfterWait(0.2f));
                drivingBihindCar = false;
            }
            else if (other.CompareTag("TrafficLight"))
            {
                TrafficLight trafic = other.GetComponent<TrafficLight>();
                trafic.lightChange -= StartMoving;
                drivingTrafficLights = false;
            }
            else if (other.CompareTag("Crosswalk"))
            {
                other.GetComponent<Crosswalk>().stateChange -= CrosswalkChange;
            }
            else if (other.CompareTag("LevelCrossing"))
            {
                other.GetComponent<LevelCrossingController>().stateChange -= LevelCrossingChange;
            }
        }
        void StartMoving(bool isGreen)
        {
            if (isGreen)
            {
                drivingTrafficLights = false;
                isMoving = true;
            }
        }
        void CrosswalkChange(bool crossing)
        {
            if (!crossing && !drivingTrafficLights)
            {
                isMoving = true;
            }
        }
        void LevelCrossingChange(bool crossing)
        {
            if (!crossing)
            {
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