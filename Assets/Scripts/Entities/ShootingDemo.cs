using System.Collections;
using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Drives the one thing the shooting demo exists to answer: does a low-poly gunman raising
    /// a revolver and dropping someone read as a killing, or as two mannequins twitching.
    ///
    /// Deliberately standalone. No city, no NavMesh, no HumanBehavior, no PedestrianRegistry -
    /// those need generated tiles and checkpoint routes, and none of them change how the beat
    /// looks. Two characters on a plane, an animator each, and a timeline.
    ///
    /// The timing is the whole design. A shot that lands the instant the arm comes up has no
    /// menace; the pause between aiming and firing is what the scene is for, and it is the
    /// first number to play with.
    /// </summary>
    public sealed class ShootingDemo : MonoBehaviour
    {
        [SerializeField] GunmanAim gunman;
        [SerializeField] WeaponSocket weapon;
        [SerializeField] PedestrianDeath victim;

        [Header("Beats (seconds)")]
        [Tooltip("Both stand there before anything happens - long enough to see the idle.")]
        [SerializeField] float beforeAiming = 1.5f;

        [Tooltip("Weapon up, held on the target. The menace lives here.")]
        [SerializeField] float aimHold = 1.2f;

        [Tooltip("Between the trigger and the body going down. Short - it is a revolver, not " +
                 "a rifle at range - but not zero.")]
        [SerializeField] float hitDelay = 0.12f;

        [Tooltip("Weapon stays up after the shot, then drops.")]
        [SerializeField] float afterShot = 1.5f;

        [Tooltip("How long the body lies there before the demo runs again. Negative never " +
                 "repeats, which is the default: a killing on a four-second loop, with the " +
                 "victim standing back up each time, reads as a puppet show rather than as a " +
                 "shooting. Set a positive value while tuning the beats.")]
        [SerializeField] float repeatAfter = -1f;

        [Header("Muzzle flash")]
        [SerializeField, Min(0f)] float flashSeconds = 0.06f;
        [SerializeField] float flashRange = 4f;
        [SerializeField] float flashIntensity = 30f;
        [SerializeField] Color flashColour = new(1f, 0.85f, 0.55f);

        Light flash;
        Vector3 victimStart;
        Quaternion victimStartRotation;

        IEnumerator Start()
        {
            if (!gunman || !victim)
            {
                Debug.LogWarning("[ShootingDemo] Needs a gunman and a victim.", this);
                yield break;
            }

            victimStart = victim.transform.position;
            victimStartRotation = victim.transform.rotation;

            // Built rather than authored so the demo scene has no prefab dependency of its own.
            // Off until the shot: a point light left on at 30 intensity lights the whole street.
            flash = new GameObject("Muzzle Flash").AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = flashColour;
            flash.intensity = flashIntensity;
            flash.range = flashRange;
            flash.enabled = false;

            while (enabled)
            {
                yield return Run();

                if (repeatAfter < 0f)
                    yield break;

                yield return new WaitForSeconds(repeatAfter);
                StandVictimUp();
            }
        }

        IEnumerator Run()
        {
            // Gun out and lowered before anything else, so the opening beat is a man standing
            // there holding a revolver rather than a man with empty hands who suddenly has one.
            gunman.Ready();
            yield return new WaitForSeconds(beforeAiming);

            gunman.AimAt(victim.transform);
            yield return new WaitForSeconds(aimHold);

            gunman.Fire();

            // The victim drops a beat after the trigger, not on the same frame. Simultaneous
            // reads as a scripted pair; the gap reads as cause and effect.
            Flash();
            yield return new WaitForSeconds(hitDelay);
            victim.Kill();

            yield return new WaitForSeconds(afterShot);

            // The weapon comes down but the target is NOT cleared until the body is back up:
            // GunmanAim keeps his head turned to where the man fell, which is where a shooter
            // would be looking. Re-aiming at a corpse is what the loop used to do, and it was
            // the thing that made the whole beat read wrong.
            gunman.LowerWeapon();
        }

        void Flash()
        {
            if (!flash)
                return;

            // Read at the moment of firing, not cached: the muzzle rides the hand bone, so
            // where it is depends on the pose the aim clip has the arm in right now.
            flash.transform.position = weapon ? weapon.Muzzle : gunman.transform.position;
            StartCoroutine(FlashOnce());
        }

        IEnumerator FlashOnce()
        {
            flash.enabled = true;
            yield return new WaitForSeconds(flashSeconds);
            flash.enabled = false;
        }

        /// <summary>
        /// Stands the victim back up for another run. The death clip has root motion baked in
        /// but plays with root motion off, so the body has not actually travelled - still, the
        /// start pose is restored explicitly rather than assumed, because that stops being true
        /// the moment anyone turns root motion on for the death state.
        ///
        /// Not called Reset: that is a MonoBehaviour message Unity sends in the Editor when the
        /// component is added or reset from the inspector, and a method that both Unity and the
        /// loop call is a method that runs at times nobody intended.
        ///
        /// The gunman is disengaged FIRST. He keeps watching the body after the shot, which is
        /// what a shooter does - but he must let go before the next run, or he starts the beat
        /// already squared up and the turn that sells the shot never happens.
        /// </summary>
        void StandVictimUp()
        {
            gunman.Disengage();
            victim.transform.SetPositionAndRotation(victimStart, victimStartRotation);
            victim.Revive();
        }
    }
}
