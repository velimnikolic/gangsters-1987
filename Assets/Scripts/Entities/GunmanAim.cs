using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Points a gunman at someone: turns his body to face them, holds the authored aim pose,
    /// and pulls the trigger.
    ///
    /// This used to pose the arm through the humanoid IK goal, because neither character pack
    /// ships an aim or a shoot take and an IK guess was better than nothing. It is not better
    /// than an animator: Quaternius' Universal Animation Library has Pistol_Idle_Loop,
    /// Pistol_Aim_Neutral and Pistol_Shoot, CityAssetBootstrap wires them into the controller,
    /// and this drives them by activity value like every other pedestrian behaviour.
    ///
    /// What the clips cannot do is know where the target is - the aim pose points wherever the
    /// character is facing. So the one thing left here is turning the body, which is also what
    /// makes the shot read: a gunman squaring up to someone is the beat before the shot.
    ///
    /// The head is still IK, and only the head. Look-at costs nothing, composes with any pose
    /// instead of fighting it, and a shooter whose eyes are on his target rather than on the
    /// horizon is most of what sells the moment.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class GunmanAim : MonoBehaviour
    {
        [Tooltip("Who is being aimed at. Cleared to lower the weapon.")]
        [SerializeField] Transform target;

        [Tooltip("Aimed at this far up the target's root - chest height, not the ground.")]
        [SerializeField] float targetChestHeight = 1.3f;

        [Tooltip("Degrees per second the body turns to square up. Instant looks like a turret.")]
        [SerializeField, Min(1f)] float turnSpeed = 360f;

        Animator animator;
        WeaponSocket socket;

        void Awake()
        {
            animator = GetComponent<Animator>();
            socket = GetComponentInChildren<WeaponSocket>();
        }

        /// <summary>Weapon up. Separate from having a target - see LowerWeapon.</summary>
        public bool Aiming { get; private set; }

        /// <summary>Engaged with someone, weapon up or not.</summary>
        public bool Engaged => target;

        /// <summary>Where the shot is pointed, for muzzle flash and tracers.</summary>
        public Vector3 AimPoint =>
            target ? target.position + Vector3.up * targetChestHeight : transform.position;

        /// <summary>Draw the revolver and hold it lowered.</summary>
        public void Ready() =>
            animator.SetInteger(PedestrianAnimation.ActivityHash, PedestrianAnimation.PistolIdle);

        /// <summary>Bring the weapon up and square up on this target.</summary>
        public void AimAt(Transform victim)
        {
            target = victim;
            Aiming = true;
            animator.SetInteger(PedestrianAnimation.ActivityHash, PedestrianAnimation.Aim);
        }

        /// <summary>
        /// One shot. A trigger, so the transition consumes it and the gunman cannot end up
        /// recoiling on a loop - which is exactly what an int activity value did.
        /// </summary>
        public void Fire()
        {
            if (!target)
                return;

            animator.SetTrigger(PedestrianAnimation.ShootHash);

            // The bang, from the muzzle when there is one. Chest height otherwise - the shot
            // still has to come from the man, not from under his feet.
            Audio.CityAudioDirector.PlayGunshot(
                socket ? socket.Muzzle : transform.position + Vector3.up * 1.4f);
        }

        /// <summary>
        /// Drop the arm, keep the gun out - and keep watching. The target is deliberately NOT
        /// cleared: a man who has just shot someone goes on looking at where they fell, and
        /// clearing it here snaps his head back to the horizon the instant the arm comes down,
        /// which is the tell that nothing in the scene knows what just happened.
        /// </summary>
        public void LowerWeapon()
        {
            Aiming = false;
            Ready();
        }

        /// <summary>Look away and forget them. The other half of LowerWeapon.</summary>
        public void Disengage()
        {
            target = null;
            Aiming = false;
            Ready();
        }

        void Update()
        {
            if (!target)
                return;

            // Yaw only. Pitching the whole body at a target standing on the same pavement would
            // tip the gunman forward for no gain; the aim pose already covers the elevation a
            // man-height target needs at these ranges.
            var toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-4f)
                return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(toTarget.normalized, Vector3.up),
                turnSpeed * Time.deltaTime);
        }

        void OnAnimatorIK(int layer)
        {
            if (!target)
                return;

            // Head and a little chest, no body weight: the arms belong to the clip.
            animator.SetLookAtWeight(1f, bodyWeight: 0.1f, headWeight: 0.8f);
            animator.SetLookAtPosition(AimPoint);
        }
    }
}
