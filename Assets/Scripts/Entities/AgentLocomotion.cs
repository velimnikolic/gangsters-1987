using System.Collections;
using UnityEngine;
using LivingCity.City;

namespace LivingCity.Entities
{
    /// <summary>
    /// The off-graph walk and the small animator plumbing every scripted agent shares:
    /// civilians, beat officers, schoolchildren, dockers and gang members all step toward a
    /// point through the same Probe/Blend/clamp loop as the patched follower, so detouring
    /// people steer around each other and can never overlap.
    ///
    /// A static helper rather than a base class on purpose. PedestrianAgent is the 10k-crowd
    /// hot path and the five agents differ in every line AROUND these thirty; what they share
    /// is arithmetic over the same four handles, and that is all this takes. Each agent keeps
    /// its own thin wrappers so its call sites read as before.
    ///
    /// Crowd invariants: every move goes through Probe/Blend and the AllowedAdvance clamp, and
    /// the body's SpeedMs is kept truthful for the frame so others read it correctly.
    /// </summary>
    public static class AgentLocomotion
    {
        /// <summary>Matches HumanBehavior's own animator scaling (speed * 0.8).</summary>
        public const float AnimatorSpeedScale = 0.8f;

        static readonly WaitForFixedUpdate FixedStep = new WaitForFixedUpdate();

        /// <summary>
        /// Straight toward <paramref name="target"/> until within <paramref name="stopWithin"/>
        /// or the timeout runs out. Leaves the body moving-flagged-off and the speed at zero;
        /// whether it actually arrived is the caller's to judge from where it stands.
        /// </summary>
        public static IEnumerator WalkTo(Transform tf, HumanBehavior human, PedestrianBody body,
                                         Animator animator, bool hasSpeedParam,
                                         Vector3 target, float stopWithin, float timeout)
        {
            SetStationary(body, animator, hasSpeedParam, false);
            var speed = 0f;
            var deadline = Time.time + timeout;

            while (Time.time < deadline)
            {
                var toTarget = target - tf.position;
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
                tf.position += step;

                if (heading != Vector3.zero)
                    tf.rotation = Quaternion.LookRotation(heading, Vector3.up);

                var actual = advance / Mathf.Max(Time.deltaTime, 1e-5f);
                body.SpeedMs = actual;
                SetSpeed(animator, hasSpeedParam, actual);

                yield return FixedStep;
            }

            body.SpeedMs = 0f;
            SetSpeed(animator, hasSpeedParam, 0f);
        }

        /// <summary>Turn on the spot to look at <paramref name="point"/> over <paramref name="seconds"/>.</summary>
        public static IEnumerator Face(Transform tf, Vector3 point, float seconds)
        {
            var direction = PedestrianSteering.Flat(point - tf.position);
            if (direction.sqrMagnitude < 1e-4f)
                yield break;

            var from = tf.rotation;
            var to = Quaternion.LookRotation(direction.normalized, Vector3.up);
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                tf.rotation = Quaternion.Slerp(from, to, t / seconds);
                yield return null;
            }
            tf.rotation = to;
        }

        public static void SetStationary(PedestrianBody body, Animator animator, bool hasSpeedParam,
                                         bool stationary)
        {
            if (body == null)
                return;

            body.Stationary = stationary;
            if (stationary)
            {
                body.SpeedMs = 0f;
                SetSpeed(animator, hasSpeedParam, 0f);
            }
        }

        public static void SetSpeed(Animator animator, bool hasSpeedParam, float metresPerSecond)
        {
            if (hasSpeedParam)
                animator.SetFloat(PedestrianAnimation.SpeedHash, metresPerSecond * AnimatorSpeedScale);
        }

        /// <summary>
        /// Whether the controller the spawner assigned carries this parameter. Checked, never
        /// assumed: with the master toggle half-on (agent added, controller missing) an agent
        /// must degrade to walking and idling rather than spam warnings.
        /// </summary>
        public static bool HasParameter(Animator animator, int nameHash)
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
