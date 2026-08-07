using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;

namespace PolyPerfect.City
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
                    MoveToNextPoint();
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
                // PATCH (Living City): route complete. Re-pathing here is an A* search plus
                // two Physics.OverlapBox calls, and with thousands of walkers arriving on
                // their own clocks those bursts land dozens to a frame. When the spawner has
                // armed the central queue, stand down and wait for a budget grant instead -
                // a person pausing at the kerb; the pack's demo scenes re-path immediately
                // as they always did.
                isMoving = false;
                if (PedestrianRepathQueue.Active && body != null)
                    PedestrianRepathQueue.Enqueue(this);
                else
                    Repath();
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
        /// PATCH (Living City): the route-completion re-path, extracted so the central queue
        /// can grant it on its own budget. Safe to call only while isMoving is false.
        /// </summary>
        public void Repath()
        {
            if (isMoving)
                return;

            if (randomDestination)
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
                targetPoint = trajectory[activePath].pathPositions[activepoint].transform.position + (trajectory[activePath].pathPositions[activepoint].transform.right * Random.Range(-0.8f, 0.8f));
            }
            else
            {
                Debug.Log(name + ": Path not found");
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
                if (trafic && trafic.isGreen)
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
