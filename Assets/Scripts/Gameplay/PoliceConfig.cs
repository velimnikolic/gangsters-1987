using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The response force's numbers - who shows up per star, how they move, and how well
    /// they shoot. Deliberately fallible: accuracy falls with distance and against a moving
    /// target, and the chase speed is only a shade over the player's walk, so escape is
    /// won by breaking line of sight, not by out-sprinting a bullet.
    /// </summary>
    [CreateAssetMenu(fileName = "PoliceConfig", menuName = "Living City/Police Config")]
    public sealed class PoliceConfig : ScriptableObject
    {
        [Header("Response strength (index = wanted level 0-5)")]
        [Tooltip("Foot officers on the street at each level.")]
        public int[] officersPerLevel = { 0, 1, 2, 2, 4, 6 };

        [Tooltip("Response cars dispatched at each level.")]
        public int[] carsPerLevel = { 0, 0, 0, 1, 2, 2 };

        [Tooltip("Officers each arriving car deploys.")]
        [Min(1)] public int officersPerCar = 2;

        [Header("Movement")]
        [Tooltip("Chase speed, m/s. Player walks 3 - a shade faster closes slowly, and " +
                 "corners still work.")]
        [Min(0.5f)] public float chaseSpeed = 3.25f;

        [Tooltip("Speed while responding to a report or searching, m/s.")]
        [Min(0.5f)] public float respondSpeed = 2.8f;

        [Header("Engagement")]
        [Tooltip("Metres at which a chasing officer stops and draws.")]
        [Min(2f)] public float engageRange = 10f;

        [Tooltip("Seconds of 'hands up!' before the shooting starts. The player's window " +
                 "to surrender, flee, or fight.")]
        [Min(0.5f)] public float warningSeconds = 3f;

        [Tooltip("Seconds between police shots.")]
        [Min(0.3f)] public float fireInterval = 1.1f;

        [Tooltip("Damage per police hit.")]
        [Min(1f)] public float damagePerShot = 16f;

        [Tooltip("Hit chance at point blank against a standing target.")]
        [Range(0f, 1f)] public float baseAccuracy = 0.9f;

        [Tooltip("Hit chance lost per metre of distance.")]
        [Min(0f)] public float accuracyLossPerMeter = 0.05f;

        [Tooltip("Hit chance lost against a moving target.")]
        [Range(0f, 1f)] public float movingTargetPenalty = 0.25f;

        [Tooltip("Metres within which an officer closes in to make the arrest.")]
        [Min(0.5f)] public float arrestRange = 1.8f;

        [Header("Losing the player")]
        [Tooltip("Seconds without line of sight before a chase falls back to investigating.")]
        [Min(1f)] public float loseSightSeconds = 4f;

        [Tooltip("Metres around the last known position an investigating officer searches.")]
        [Min(2f)] public float searchRadius = 12f;

        [Tooltip("Search points visited before giving up.")]
        [Min(1)] public int searchPoints = 3;

        [Tooltip("Seconds spent looking around at each search point.")]
        [Min(0.5f)] public float searchPointSeconds = 3f;

        [Header("Spawning")]
        [Tooltip("Officers appear at least this far from the player - never in his lap.")]
        [Min(5f)] public float spawnMinDistanceFromPlayer = 25f;

        [Tooltip("...and at most this far from the incident.")]
        [Min(10f)] public float spawnMaxDistanceFromIncident = 60f;

        [Tooltip("Metres from the incident a response car counts as arrived and parks.")]
        [Min(5f)] public float carArriveRange = 25f;

        [Tooltip("Seconds a stood-down unit lingers before despawning.")]
        [Min(1f)] public float standDownSeconds = 8f;

        [Header("Cadence")]
        [Tooltip("Seconds between director deployment checks.")]
        [Min(0.1f)] public float responseTickSeconds = 0.5f;

        [Tooltip("Seconds between an officer's line-of-sight checks.")]
        [Min(0.05f)] public float losTickSeconds = 0.25f;
    }
}
