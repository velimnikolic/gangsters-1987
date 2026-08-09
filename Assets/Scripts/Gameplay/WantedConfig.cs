using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Every number the witness and wanted machinery runs on. Heat is the continuous truth;
    /// the star level is derived from it through the thresholds, so tuning "how fast does
    /// this escalate" never touches code.
    /// </summary>
    [CreateAssetMenu(fileName = "WantedConfig", menuName = "Living City/Wanted Config")]
    public sealed class WantedConfig : ScriptableObject
    {
        [Header("Levels")]
        [Tooltip("Heat needed for each star, 1 to 5. Level = how many thresholds heat clears.")]
        public float[] starThresholds = { 10f, 25f, 45f, 70f, 100f };

        [Header("Heat per crime")]
        [Tooltip("A witnessed murder.")]
        [Min(0f)] public float murderHeat = 30f;

        [Tooltip("A shot heard but not seen - the sound witness's report.")]
        [Min(0f)] public float gunshotHeat = 4f;

        [Tooltip("A body found later. No perpetrator attached.")]
        [Min(0f)] public float bodyDiscoveredHeat = 12f;

        [Tooltip("Killing a police officer. Reported instantly - the force knows.")]
        [Min(0f)] public float assaultOfficerHeat = 35f;

        [Tooltip("Each extra witness multiplies the crime's heat by (1 + count * this).")]
        [Min(0f)] public float witnessMultiplier = 0.25f;

        [Header("Decay")]
        [Tooltip("Seconds without any police or witness contact before heat starts falling.")]
        [Min(1f)] public float decayDelaySeconds = 20f;

        [Tooltip("Heat lost per second once decay is running.")]
        [Min(0.01f)] public float decayPerSecond = 1.5f;

        [Tooltip("Heat gained per second while a police officer can SEE the player.")]
        [Min(0f)] public float contactHeatPerSecond = 0.6f;

        [Header("Witnesses")]
        [Tooltip("Metres within which a crime can be SEEN (cone + clear line required).")]
        [Min(1f)] public float sightRadius = 25f;

        [Tooltip("Metres within which a gunshot is HEARD. No cone, no line needed.")]
        [Min(1f)] public float soundRadius = 50f;

        [Tooltip("Dot(npc.forward, dirToCrime) above which the crime is inside the view cone.")]
        [Range(-1f, 1f)] public float viewConeDot = 0.4f;

        [Tooltip("Seconds a witness fumbles before the report lands. Killing them inside " +
                 "this window cancels the report - that is the mechanic.")]
        public Vector2 reportDelayRange = new Vector2(2f, 5f);

        [Tooltip("Chance a sound-only witness calls it in rather than just fleeing.")]
        [Range(0f, 1f)] public float soundWitnessCallChance = 0.5f;

        [Header("Panic")]
        [Tooltip("Seconds of running away from the crime.")]
        public Vector2 fleeSecondsRange = new Vector2(7f, 12f);

        [Tooltip("Multiplier on the witness's walk speed while fleeing. Capped at 3.4 m/s " +
                 "in code - the pack has only a walk cycle, and faster reads as gliding.")]
        [Min(1f)] public float fleeSpeedMultiplier = 1.45f;

        [Header("Bodies")]
        [Tooltip("Metres within which a passer-by notices a corpse (with a clear line).")]
        [Min(1f)] public float corpseDiscoveryRadius = 6f;
    }
}
