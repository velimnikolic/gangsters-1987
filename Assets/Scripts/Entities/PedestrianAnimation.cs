using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// The contract between the generated People Interaction Controller and the code that
    /// drives it - one int parameter, one value per activity. Shared by the editor bootstrap
    /// that authors the controller and the runtime agent that sets the parameter, so the two
    /// can never drift apart.
    ///
    /// Values are what the agent SETS; states are what the machine is in. Talk and Argue map
    /// one-to-one. Sit is a chain: setting Sit enters Sit Down, exit time rolls it into
    /// Sitting, and withdrawing the value (back to None) plays Stand Up before returning to
    /// idle - so "stop sitting" and "stop talking" are the same call.
    /// </summary>
    public static class PedestrianAnimation
    {
        public const string ActivityParam = "activity";

        public const int None = 0;
        public const int Talk = 1;
        public const int Argue = 2;
        public const int Sit = 3;

        /// <summary>
        /// Shot dead. Terminal, and the only activity with no way back to idle: the machine
        /// enters Die and stays there.
        ///
        /// Safe under AnyState even though it is a one-shot, which Sit Down is not. The
        /// difference is what happens when the clip ends. Sit Down leads to Sitting with the
        /// activity still set, so AnyState would fire again from Sitting and loop forever;
        /// Die leads nowhere, so the only state matching the condition is Die itself and
        /// canTransitionToSelf blocks it. The clip is imported with loopTime 0, so the state
        /// holds its last frame - the body stays down.
        /// </summary>
        public const int Die = 4;

        /// <summary>
        /// Armed and relaxed - the revolver is out but pointed at the ground. Distinct from
        /// None, which is empty-handed: a man holding a gun does not stand like a man who is not.
        /// </summary>
        public const int PistolIdle = 5;

        /// <summary>Weapon up, pointed where the body is facing. Loops until withdrawn.</summary>
        public const int Aim = 6;

        /// <summary>
        /// The shot - a TRIGGER, not an activity value, and that distinction is the whole point.
        ///
        /// It was an activity value first, and that re-fired forever: Shoot returns to Aim on
        /// exit time, the activity int was still Shoot when it got there, so the Aim -> Shoot
        /// condition matched again immediately and the gunman stood there recoiling on a loop.
        /// A trigger is CONSUMED by the transition that takes it, which is exactly the "happens
        /// once per call" semantics a gunshot needs.
        /// </summary>
        public const string ShootParam = "shoot";

        public static readonly int ShootHash = Animator.StringToHash(ShootParam);

        /// <summary>
        /// The aim state's name in the generated controller. Shared because WeaponSocket poses
        /// the rig into it to work out how a weapon has to sit in the hand - the authored aim
        /// pose is the only thing in the project that knows where a held gun should point.
        /// </summary>
        public const string AimState = "Aim";

        public static readonly int ActivityHash = Animator.StringToHash(ActivityParam);
        public static readonly int SpeedHash = Animator.StringToHash("speed");

        /// <summary>
        /// The authored sit pose, measured off Common_Animation_Set.fbx rather than guessed.
        /// The animation pack ships its OWN copy of bench-old, vertex-identical to the Epic
        /// City one this city places - so the clip was authored against exactly the bench the
        /// sitters land on, and its numbers are ground truth.
        ///
        /// In Sitting_Bench_Idle the pelvis rests at character-local (0, 0.587, -0.438) with
        /// the toes still on the ground, and the thigh line (hip 0.565 to knee 0.496) puts the
        /// seat contact at 0.428 - bench-old's seat top to the millimetre. So a sitter is
        /// placed by putting its ROOT where the contact patch has to end up, and the pose does
        /// the rest; the root is neither at the seat nor under the hips.
        ///
        /// Retargeting scales the whole pose by Animator.humanScale, so on a child rig the
        /// contact drops to 0.428 * humanScale - which is why the seat Y is computed per
        /// sitter in PedestrianAgent instead of baked into the seat offsets.
        /// </summary>
        public const float SitContactHeight = 0.428f;

        /// <summary>How far behind the root the seated pelvis sits - sets the seat Z offsets.</summary>
        public const float SitPelvisBack = 0.438f;

        /// <summary>
        /// How long the root glides onto the seat. Idle-Sitting_Bench is 35 keys at 24fps =
        /// 1.42s and the pose is down slightly before the end, so the slide has to last about
        /// this long: the old 0.6s finished while the character was still visibly descending.
        /// </summary>
        public const float SitDescentSeconds = 1.2f;
    }
}
