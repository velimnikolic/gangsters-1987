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

        public static readonly int ActivityHash = Animator.StringToHash(ActivityParam);
        public static readonly int SpeedHash = Animator.StringToHash("speed");
    }
}
