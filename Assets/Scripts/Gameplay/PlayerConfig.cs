using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The player character's own numbers - identity, movement and how his marker reads -
    /// separate from CombatConfig because the two are tuned at different times: combat when
    /// a kill feels wrong, these when the player feels wrong.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerConfig", menuName = "Living City/Player Config")]
    public sealed class PlayerConfig : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("The name on the popup.")]
        public string playerName = "Salvatore";

        [Tooltip("Hit points the player starts with. Nothing damages him in Phase 1; the " +
                 "value exists so the popup has a truth to report.")]
        [Min(1f)] public float maxHealth = 100f;

        [Header("Movement")]
        [Tooltip("Walk speed, m/s. Civilians draw 2-3; the boss walks at the top of that " +
                 "band so following him through a crowd never feels like queueing.")]
        [Min(0.5f)] public float walkSpeed = 3f;

        [Tooltip("Metres from the clicked point at which the walk counts as arrived.")]
        [Min(0.1f)] public float arriveTolerance = 0.35f;

        [Tooltip("Metres of straight-line trip below which no route is asked for at all - " +
                 "crossing the pavement does not need the A*.")]
        [Min(1f)] public float directWalkRange = 15f;

        [Header("Marker")]
        [Tooltip("The marker and popup accent - the red that says 'yours'.")]
        public Color markerColor = new Color(0.85f, 0.16f, 0.12f);

        [Tooltip("Metres above the player's origin the marker floats.")]
        [Min(0f)] public float markerHeight = 2.3f;

        [Tooltip("Marker size relative to the overlay's stock 12px diamond.")]
        [Min(0.5f)] public float markerSizeScale = 1.25f;

        [Tooltip("Seconds per pulse beat.")]
        [Min(0.2f)] public float markerPulsePeriod = 1.2f;

        [Tooltip("How far the pulse swings the scale: 0.2 is plus/minus 20%.")]
        [Range(0f, 0.6f)] public float markerPulseAmplitude = 0.22f;
    }
}
