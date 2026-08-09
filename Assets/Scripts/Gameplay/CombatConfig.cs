using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Every number the kill sequence runs on, in one asset, so tuning happens in the
    /// inspector rather than in a recompile. Created by Tools/City/Create or Refresh
    /// Gameplay Assets; GetOrCreate never rewrites an existing asset, so tuned values
    /// survive every refresh.
    ///
    /// The flash values are the Shooting Demo's, kept because they were set by eye against
    /// the revolver and the answer does not change with the scene around it.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatConfig", menuName = "Living City/Combat Config")]
    public sealed class CombatConfig : ScriptableObject
    {
        [Header("Engagement")]
        [Tooltip("Metres within which the player stops approaching and starts aiming.")]
        [Min(1f)] public float engageRange = 4f;

        [Tooltip("Seconds the aim is held before the first shot - the beat that makes the " +
                 "shot read as deliberate rather than as a glitch.")]
        [Min(0f)] public float aimSeconds = 1.2f;

        [Tooltip("Seconds between shots while the target is still standing.")]
        [Min(0.2f)] public float fireInterval = 0.9f;

        [Tooltip("Seconds between the trigger animation starting and the hit landing. A fixed " +
                 "delay rather than an animation event on purpose: the controller is a " +
                 "GENERATED asset and regeneration would silently drop any event added to it.")]
        [Min(0f)] public float hitDelay = 0.12f;

        [Tooltip("Seconds the weapon stays raised after the target goes down.")]
        [Min(0f)] public float lowerWeaponAfter = 1.5f;

        [Header("Damage")]
        [Tooltip("Hit points one shot takes. At the defaults every shot kills - a revolver " +
                 "round at four metres is not a negotiation. Lower below npcMaxHealth to " +
                 "make kills take several shots.")]
        [Min(1f)] public float damagePerShot = 100f;

        [Tooltip("Hit points a pedestrian starts with.")]
        [Min(1f)] public float npcMaxHealth = 100f;

        [Header("Corpse")]
        [Tooltip("Seconds a body lies in the street before it despawns.")]
        [Min(5f)] public float corpseDespawnSeconds = 120f;

        [Header("Muzzle flash")]
        [Tooltip("Seconds the flash light is on. One rendered frame plus change.")]
        [Min(0.01f)] public float flashSeconds = 0.06f;

        [Tooltip("Range of the flash light in metres.")]
        [Min(0.5f)] public float flashRange = 4f;

        [Tooltip("Intensity of the flash light.")]
        [Min(1f)] public float flashIntensity = 30f;

        [Tooltip("Colour of the flash - warm, powder-burn orange.")]
        public Color flashColour = new Color(1f, 0.85f, 0.55f);
    }
}
