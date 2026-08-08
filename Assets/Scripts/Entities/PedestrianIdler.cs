using System.Collections;
using UnityEngine;
using PolyPerfect.City;

namespace LivingCity.Entities
{
    /// <summary>
    /// Makes a pedestrian stop and stand still now and then.
    ///
    /// HumanBehavior has no idle state at all: it walks continuously and re-paths the instant
    /// it arrives, so there is no window to configure and nothing to hook into. The only safe
    /// intervention is to disable the component outright - anything that drives the transform
    /// or Animator alongside it would fight it for control and produce sliding or stuck agents.
    ///
    /// Two details matter:
    /// - The Animator's "speed" float is written in HumanBehavior.FixedUpdate. Disabling the
    ///   component freezes that value mid-walk-cycle, so the character keeps playing the walk
    ///   animation on the spot with its feet sliding. It has to be zeroed by hand.
    /// - On re-enable, HumanBehavior resumes toward the targetPoint it already had. Its state
    ///   is untouched, so nothing needs restoring.
    ///
    /// This pauses on a timer rather than strictly on arrival, because arrival is private and
    /// unobservable. Visually the result is the same: people stop, stand a while, move on.
    /// </summary>
    [RequireComponent(typeof(HumanBehavior))]
    public sealed class PedestrianIdler : MonoBehaviour
    {
        static readonly int SpeedParam = Animator.StringToHash("speed");

        [SerializeField] Vector2 walkDurationRange = new(8f, 25f);
        [SerializeField] Vector2 idleDurationRange = new(3f, 10f);
        [SerializeField, Range(0f, 1f)] float idleChance = 0.3f;

        HumanBehavior behaviour;
        Animator animator;
        bool hasSpeedParam;

        void Awake()
        {
            behaviour = GetComponent<HumanBehavior>();
            animator = GetComponent<Animator>();
            hasSpeedParam = HasParameter(animator, SpeedParam);
        }

        IEnumerator Start()
        {
            while (enabled)
            {
                yield return new WaitForSeconds(Random.Range(walkDurationRange.x, walkDurationRange.y));

                // The road test is the same one PedestrianAgent.TickOpportunities makes before
                // it starts anything: this halt is on a pure timer and has no idea where the
                // walker is standing, and ten seconds of idling on a crossing holds the car that
                // stopped for it - and everything queued behind that car.
                if (Random.value > idleChance || RoadSurface.IsOnRoad(transform.position))
                    continue;

                behaviour.enabled = false;
                if (hasSpeedParam)
                    animator.SetFloat(SpeedParam, 0f);

                yield return new WaitForSeconds(Random.Range(idleDurationRange.x, idleDurationRange.y));

                behaviour.enabled = true;
            }
        }

        void OnDisable()
        {
            // Never leave the pedestrian frozen if this component is removed mid-idle.
            if (behaviour)
                behaviour.enabled = true;
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
