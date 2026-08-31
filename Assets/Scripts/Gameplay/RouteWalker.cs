using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The hand-steered walk the player and the response officers share: one
    /// Probe/Blend/clamp loop over a waypoint list, the wall slide that keeps the
    /// off-graph legs honest, the entry-node pick, and the animator parameter probe.
    /// Stateless on purpose - the callers own transform, body and animator and hand
    /// them in - so the loop exists once. The code is the PlayerMafioso original,
    /// unchanged; the response officer carried a verbatim copy until this file.
    ///
    /// Deliberately NOT shared with the pack's own walkers (PedestrianAgent,
    /// PoliceOfficerAgent): those keep their copies per the copy-not-share convention
    /// in PoliceOfficerAgent's class comment. This only folds the project's own two
    /// copies into one.
    /// </summary>
    public static class RouteWalker
    {
        /// <summary>Metres from a route node at which the walk rolls on to the next one -
        /// loose, because nodes are waypoints, not destinations.</summary>
        public const float NodeTolerance = 1.2f;

        static readonly WaitForFixedUpdate FixedStep = new WaitForFixedUpdate();

        /// <summary>
        /// Parameter lists per controller. animator.parameters copies the whole array on
        /// every read, and a street of panicking witnesses asks twice each; the answer
        /// depends only on the controller, so one copy per controller is kept.
        /// </summary>
        static readonly Dictionary<RuntimeAnimatorController, AnimatorControllerParameter[]>
            ParametersByController = new Dictionary<RuntimeAnimatorController, AnimatorControllerParameter[]>();

        /// <summary>
        /// ONE steering loop over the whole waypoint list - the speed carries across nodes.
        /// The first version walked each node as its own loop, and every loop began at
        /// speed zero: with route nodes a few metres apart the walker never reached full
        /// stride before resetting, and surged node to node like a broken toy. The loop is
        /// otherwise the PedestrianAgent Probe/Blend/clamp step, plus the wall slide - the
        /// registry only steers around PEOPLE, and the off-graph legs need walls too.
        /// </summary>
        public static IEnumerator FollowRoute(Transform walker, PedestrianBody body,
                                              List<Vector3> points, float finalTolerance,
                                              float speedCap, int wallMask,
                                              System.Func<bool> abortWhen,
                                              System.Action<float> setSpeed)
        {
            if (points.Count == 0)
                yield break;

            var speed = 0f;
            var index = 0;
            var deadline = Time.time + 15f + points.Count * 4f;

            while (index < points.Count && Time.time < deadline)
            {
                if (abortWhen != null && abortWhen())
                    break;

                var last = index == points.Count - 1;
                var target = points[index];
                var toTarget = target - walker.position;
                var remaining = PedestrianSteering.Flat(toTarget).magnitude;
                if (remaining <= (last ? finalTolerance : NodeTolerance))
                {
                    index++;
                    continue;
                }

                speed = Mathf.MoveTowards(speed, speedCap, 4f * Time.deltaTime);

                var obstacle = PedestrianRegistry.Probe(body, toTarget);
                var heading = WallSlide(walker.position,
                                        PedestrianSteering.Blend(toTarget, obstacle.Push), wallMask);
                var advance = Mathf.Min(speed * Time.deltaTime,
                              Mathf.Min(obstacle.AllowedAdvance, remaining));

                var step = heading * advance;
                step.y = Mathf.Clamp(toTarget.y, -advance, advance);
                walker.position += step;

                if (heading != Vector3.zero)
                    walker.rotation = Quaternion.LookRotation(heading, Vector3.up);

                var actual = advance / Mathf.Max(Time.deltaTime, 1e-5f);
                body.SpeedMs = actual;
                setSpeed(actual);

                yield return FixedStep;
            }

            body.SpeedMs = 0f;
            setSpeed(0f);
        }

        /// <summary>
        /// A knee-height ray one stride ahead; a hit projects the heading along the wall.
        /// This is what keeps the short off-graph legs honest - the avoidance registry
        /// knows people, not architecture.
        /// </summary>
        public static Vector3 WallSlide(Vector3 position, Vector3 heading, int wallMask)
        {
            if (heading == Vector3.zero)
                return heading;

            if (!Physics.Raycast(position + Vector3.up * 0.9f, heading,
                                 out var hit, 1.1f, wallMask, QueryTriggerInteraction.Ignore))
                return heading;

            var slide = Vector3.ProjectOnPlane(heading, hit.normal);
            slide.y = 0f;
            return slide.sqrMagnitude > 1e-4f ? slide.normalized : Vector3.zero;
        }

        /// <summary>Nearest node of the first route path - entry point onto the graph.</summary>
        public static int NearestNodeIndex(List<Transform> nodes, Vector3 from)
        {
            var best = 0;
            var bestSqr = float.MaxValue;
            for (var i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i])
                    continue;

                var sqr = (nodes[i].position - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>True when the animator's controller declares this parameter.</summary>
        public static bool HasParameter(Animator animator, int nameHash)
        {
            if (!animator)
                return false;
            var controller = animator.runtimeAnimatorController;
            if (!controller)
                return false;

            if (!ParametersByController.TryGetValue(controller, out var parameters))
            {
                parameters = animator.parameters;
                // An animator that has not initialised yet answers with an empty list,
                // and that answer is about the animator, not the controller - only a
                // real list is worth keeping for everyone else on the same controller.
                if (parameters.Length > 0)
                    ParametersByController[controller] = parameters;
            }

            foreach (var parameter in parameters)
                if (parameter.nameHash == nameHash)
                    return true;

            return false;
        }

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => ParametersByController.Clear();
    }
}
