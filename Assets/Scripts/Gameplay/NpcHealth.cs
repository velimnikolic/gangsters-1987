using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Hit points in front of PedestrianDeath's binary Kill. This is the whole difference
    /// between "clicked and he fell over" and combat: damage accumulates here, and only the
    /// shot that empties the bar goes through to the death machinery, which already knows
    /// how to shut a pedestrian down in the right order.
    ///
    /// Sits on every spawned pedestrian, so it holds no per-frame logic and no config
    /// reference - the max is read from GameplayRuntime on first use and falls back to the
    /// class default when no config is in the scene.
    /// </summary>
    [RequireComponent(typeof(PedestrianDeath))]
    public sealed class NpcHealth : MonoBehaviour
    {
        const float DefaultMaxHealth = 100f;

        /// <summary>Sentinel: initialised on first touch, not in Awake - a pedestrian can be
        /// spawned before GameplayRuntime's own Awake has run.</summary>
        float current = -1f;
        float max = -1f;

        PedestrianDeath death;

        public float Current { get { EnsureInitialised(); return current; } }
        public float Max { get { EnsureInitialised(); return max; } }

        public bool IsDead => death && death.IsDead;

        void Awake()
        {
            death = GetComponent<PedestrianDeath>();
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            EnsureInitialised();
            current = Mathf.Max(0f, current - amount);
            if (current > 0f)
                return;

            death.Kill();

            // The corpse starts its own clock the moment it becomes one.
            if (!GetComponent<CorpseEvidence>())
                gameObject.AddComponent<CorpseEvidence>();
        }

        void EnsureInitialised()
        {
            if (max > 0f)
                return;

            var combat = GameplayRuntime.Combat;
            max = combat ? combat.npcMaxHealth : DefaultMaxHealth;
            current = max;
        }
    }
}
