using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// A body in the street, and its clock. Added by NpcHealth at the moment of death; waits
    /// out the despawn timer and removes the pedestrian.
    ///
    /// Destroy is safe on a spawned pedestrian: PedestrianAgent.OnDestroy unregisters the
    /// avoidance body, and PedestrianLodSystem swap-pops entries whose Animator has died.
    ///
    /// Also the evidence half of the no-witness murder: every corpse sits in the static
    /// <see cref="Corpses"/> list until a passer-by finds it, at which point WitnessSystem
    /// raises the BodyDiscovered crime - smaller heat, no perpetrator. A body that despawns
    /// undiscovered was a perfect crime.
    /// </summary>
    public sealed class CorpseEvidence : MonoBehaviour
    {
        /// <summary>Undiscovered bodies, for WitnessSystem's discovery sweep. Small.</summary>
        public static readonly List<CorpseEvidence> Corpses = new List<CorpseEvidence>();

        const float DefaultLifetime = 120f;

        /// <summary>Time.time at death - Phase 2 reads how cold the trail is.</summary>
        public float DiedAt { get; private set; }

        /// <summary>Set by WitnessSystem when somebody finds the body; one report per corpse.</summary>
        public bool Discovered { get; set; }

        void Start()
        {
            DiedAt = Time.time;

            var combat = GameplayRuntime.Combat;
            StartCoroutine(DespawnAfter(combat ? combat.corpseDespawnSeconds : DefaultLifetime));
        }

        void OnEnable() => Corpses.Add(this);

        void OnDisable() => Corpses.Remove(this);

        IEnumerator DespawnAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Destroy(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Corpses.Clear();
    }
}
