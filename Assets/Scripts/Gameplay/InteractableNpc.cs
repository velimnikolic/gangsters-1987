using UnityEngine;
using PolyPerfect.City;
using LivingCity.Entities;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The gameplay layer's handle on one spawned pedestrian: what the context menu targets,
    /// what the hover highlight brightens, what a kill order damages. Added by
    /// PedestrianSpawner right after the layer pass, so it is on every civilian - and only
    /// on civilians: officers, bank customers and school children are spawned by their own
    /// directors and deliberately stay out of Phase 1's reach.
    ///
    /// Passive on purpose - no Update, no tick, no registry. It caches lookups the
    /// interaction layer would otherwise repeat per hover, and everything it caches is
    /// null-safe: a pedestrian without HumanBehavior is still a valid target.
    /// </summary>
    [RequireComponent(typeof(NpcHealth))]
    [RequireComponent(typeof(PedestrianDeath))]
    public sealed class InteractableNpc : MonoBehaviour
    {
        HumanBehavior human;
        NpcHealth health;
        PedestrianDeath death;
        Renderer[] renderers;

        /// <summary>The pack walker, or null - never assumed present.</summary>
        public HumanBehavior Human => human;

        public NpcHealth Health => health;

        public bool IsDead => death && death.IsDead;

        /// <summary>
        /// Cached lazily rather than in Awake: GetComponentsInChildren allocates, and most
        /// pedestrians are never hovered.
        /// </summary>
        public Renderer[] Renderers => renderers ??= GetComponentsInChildren<Renderer>(true);

        void Awake()
        {
            human = GetComponent<HumanBehavior>();
            health = GetComponent<NpcHealth>();
            death = GetComponent<PedestrianDeath>();
        }
    }
}
