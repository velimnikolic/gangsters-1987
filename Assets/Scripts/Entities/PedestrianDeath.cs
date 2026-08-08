using UnityEngine;
using PolyPerfect.City;

namespace LivingCity.Entities
{
    /// <summary>
    /// Kills a pedestrian: the death take, and the tidying up around it that is easy to forget.
    ///
    /// The animation is the small half. The rest is that a corpse must stop being a pedestrian:
    /// HumanBehavior would keep walking the transform along its route while the body lies down,
    /// the 2m upright capsule would stay standing as an invisible pillar the crowd flows around,
    /// and PedestrianRegistry would keep it as an obstacle other walkers avoid at head height.
    /// All three are switched off here.
    ///
    /// Deregistration reuses the Hidden flag the shop doors already set - a body nobody probes
    /// is exactly what "inside a shop" means to the registry, so there is no second mechanism.
    /// </summary>
    public sealed class PedestrianDeath : MonoBehaviour
    {
        public bool IsDead { get; private set; }

        /// <summary>Fired once, after the body is down and out of the crowd.</summary>
        public event System.Action died;

        public void Kill()
        {
            if (IsDead)
                return;

            IsDead = true;

            var animator = GetComponent<Animator>();
            if (animator)
                animator.SetInteger(PedestrianAnimation.ActivityHash, PedestrianAnimation.Die);

            // Order matters: the agent is disabled BEFORE the behaviour, because the agent's
            // own update reads the behaviour and would spend one more frame steering a corpse.
            var agent = GetComponent<PedestrianAgent>();
            if (agent)
                agent.enabled = false;

            var behaviour = GetComponent<HumanBehavior>();
            if (behaviour)
            {
                if (behaviour.body != null)
                    behaviour.body.Hidden = true;

                behaviour.enabled = false;
            }

            var idler = GetComponent<PedestrianIdler>();
            if (idler)
                idler.enabled = false;

            // The capsule goes, the Rigidbody stays. It is kinematic and gravity-free, so it
            // costs nothing lying there, and removing it would strand any component that holds
            // a reference to it.
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule)
                capsule.enabled = false;

            died?.Invoke();
        }

        /// <summary>
        /// Puts everything Kill switched off back on. Exists for the demo loop, which stands the
        /// same victim up again rather than rebuilding the character - a fresh instance would
        /// re-run every Start in the scene, including the one that equips the gunman.
        ///
        /// The caller still owns the transform: reviving does not move the body back, because
        /// where it should stand is not something this component can know.
        /// </summary>
        public void Revive()
        {
            if (!IsDead)
                return;

            IsDead = false;

            var animator = GetComponent<Animator>();
            if (animator)
            {
                animator.SetInteger(PedestrianAnimation.ActivityHash, PedestrianAnimation.None);

                // Rebind rather than just clearing the parameter: the machine is sitting on the
                // last frame of a one-shot with nowhere to go, so withdrawing the activity
                // leaves it exactly where it is. Rebind returns it to the default state.
                animator.Rebind();

                // Rebind schedules the reset; it does not pose the rig. Without an explicit
                // evaluation the body stays face-down for a frame, and if anything reads the
                // pose in between - a seat test, a screenshot - it reads a corpse.
                animator.Update(0f);
            }

            var agent = GetComponent<PedestrianAgent>();
            if (agent)
                agent.enabled = true;

            var behaviour = GetComponent<HumanBehavior>();
            if (behaviour)
            {
                if (behaviour.body != null)
                    behaviour.body.Hidden = false;

                behaviour.enabled = true;
            }

            var capsule = GetComponent<CapsuleCollider>();
            if (capsule)
                capsule.enabled = true;
        }
    }
}
