using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;

namespace LivingCity.Entities
{
    /// <summary>
    /// One docker, for the whole of their shift: walk to a work point, stand and work a while,
    /// walk to another. Spawned and bound by PortDirector, persistent, and deliberately NOT a
    /// PedestrianAgent - a docker never leaves the compound, never shops, never joins the chat
    /// pairing, and staying out of PedestrianAgent.Agents is the whole opt-out, exactly as it
    /// is for a schoolchild.
    ///
    /// The port has no path network - its ground is a slab, not tiles - so every leg here is
    /// off-graph, on the walk graph PortLayout built and PortMarker carries: two lanes parallel
    /// to the quay, crossable only at the aisle. PortMarker.Route turns any pair of points into
    /// at most three straight legs, each clear of the container stacks by construction, so
    /// this agent needs no pathfinding and no obstacle knowledge of its own.
    ///
    /// The WalkTo/Face loop is copied from PedestrianAgent via SchoolChildAgent, the fourth
    /// consumer, under that class's own reasoning: the shared thirty lines are arithmetic, and
    /// everything around them differs.
    ///
    /// Crowd invariants honoured: the body registers once for the object's lifetime; every
    /// move goes through Probe/Blend and the AllowedAdvance clamp; Stationary is kept
    /// truthful; no per-instance Update.
    /// </summary>
    [RequireComponent(typeof(HumanBehavior))]
    public sealed class DockWorkerAgent : MonoBehaviour
    {
        /// <summary>Give-up timer on any single leg - the PedestrianAgent value. A leg that
        /// times out just becomes the new place to work from; nothing downstream cares.</summary>
        const float OffGraphTimeout = 20f;

        /// <summary>How long a docker works a point before moving on.</summary>
        const float WorkSecondsMin = 5f;
        const float WorkSecondsMax = 20f;

        /// <summary>Matches HumanBehavior's own animator scaling (speed * 0.8).</summary>
        const float AnimatorSpeedScale = 0.8f;

        static readonly WaitForFixedUpdate FixedStep = new WaitForFixedUpdate();

        HumanBehavior human;
        Animator animator;
        PedestrianBody body;
        bool hasSpeedParam;

        PortMarker port;
        System.Random rng;
        int at;

        readonly List<Vector3> path = new();

        void Awake()
        {
            human = GetComponent<HumanBehavior>();
            animator = GetComponent<Animator>();
        }

        void OnEnable()
        {
            if (body == null)
            {
                body = PedestrianRegistry.Register(transform);
                human.body = body;
            }
        }

        void OnDestroy()
        {
            PedestrianRegistry.Unregister(body);
            body = null;
        }

        /// <summary>Bound by the director, standing at work point <paramref name="start"/>.</summary>
        public void Bind(PortMarker marker, int seed, int start)
        {
            port = marker;
            rng = new System.Random(seed);
            at = start;

            // The follower stays off for life: the compound is not on the path network, and
            // a docker who wandered onto it would walk out the gate and never come back.
            human.enabled = false;
            human.randomDestination = false;

            hasSpeedParam = HasParameter(animator, PedestrianAnimation.SpeedHash);
            SetStationary(true);

            StartCoroutine(ShiftRoutine());
        }

        IEnumerator ShiftRoutine()
        {
            var points = port.WorkPoints;
            if (points == null || points.Length < 2)
                yield break;

            while (true)
            {
                yield return new WaitForSeconds(
                    WorkSecondsMin + (float)rng.NextDouble() * (WorkSecondsMax - WorkSecondsMin));

                var next = rng.Next(points.Length - 1);
                if (next >= at)
                    next++;

                path.Clear();
                port.Route(points[at], points[next], path);

                foreach (var waypoint in path)
                    yield return WalkTo(waypoint, 0.5f);

                at = next;

                // Face along the quay's outward-ish: pick a slight random turn so a rank of
                // dockers does not stand like a parade.
                yield return Face(transform.position
                                  + Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f)
                                  * Vector3.forward);
                SetStationary(true);
            }
        }

        // ------------------------------------------------------------------ movement
        // Copied from PedestrianAgent via SchoolChildAgent - see the class comment for why
        // copied rather than shared.

        IEnumerator WalkTo(Vector3 target, float stopWithin, float timeout = OffGraphTimeout)
        {
            SetStationary(false);
            var speed = 0f;
            var deadline = Time.time + timeout;

            while (Time.time < deadline)
            {
                var toTarget = target - transform.position;
                var remaining = PedestrianSteering.Flat(toTarget).magnitude;
                if (remaining <= stopWithin)
                    break;

                speed = Mathf.MoveTowards(speed, human.maxspeed, 4f * Time.deltaTime);

                var obstacle = PedestrianRegistry.Probe(body, toTarget);
                var heading = PedestrianSteering.Blend(toTarget, obstacle.Push);
                var advance = Mathf.Min(speed * Time.deltaTime,
                              Mathf.Min(obstacle.AllowedAdvance, remaining));

                var step = heading * advance;
                step.y = Mathf.Clamp(toTarget.y, -advance, advance);
                transform.position += step;

                if (heading != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(heading, Vector3.up);

                var actual = advance / Mathf.Max(Time.deltaTime, 1e-5f);
                body.SpeedMs = actual;
                SetSpeed(actual);

                yield return FixedStep;
            }

            body.SpeedMs = 0f;
            SetSpeed(0f);
        }

        IEnumerator Face(Vector3 point, float seconds = 0.35f)
        {
            var direction = PedestrianSteering.Flat(point - transform.position);
            if (direction.sqrMagnitude < 1e-4f)
                yield break;

            var from = transform.rotation;
            var to = Quaternion.LookRotation(direction.normalized, Vector3.up);
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                transform.rotation = Quaternion.Slerp(from, to, t / seconds);
                yield return null;
            }
            transform.rotation = to;
        }

        // ------------------------------------------------------------------ plumbing

        void SetStationary(bool stationary)
        {
            if (body == null)
                return;

            body.Stationary = stationary;
            if (stationary)
            {
                body.SpeedMs = 0f;
                SetSpeed(0f);
            }
        }

        void SetSpeed(float metresPerSecond)
        {
            if (hasSpeedParam)
                animator.SetFloat(PedestrianAnimation.SpeedHash, metresPerSecond * AnimatorSpeedScale);
        }

        static bool HasParameter(Animator animator, int nameHash)
        {
            if (!animator || !animator.runtimeAnimatorController)
                return false;

            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == nameHash)
                    return true;

            return false;
        }
    }
}
