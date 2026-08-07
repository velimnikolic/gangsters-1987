using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Zoom-keyed crowd LOD. The camera is orthographic, so distance to it means nothing -
    /// what shrinks a pedestrian on screen is the orthographic size, and at wide zoom the
    /// whole city is in frustum, which is exactly when the population is at its most
    /// expensive and its least visible. The Animator's own culling can do nothing there:
    /// nothing is offscreen.
    ///
    /// Three tiers, applied per pedestrian by a round-robin sweep:
    ///  - Full (close zoom): animators run normally; the Animator's built-in culling handles
    ///    whatever the close-up frustum clips.
    ///  - Mid: animators are disabled and advanced manually once every lodMidRate frames
    ///    with the real elapsed time, so everyone still animates at the right pace, at a
    ///    third of the cost.
    ///  - Far: animators freeze outright and pedestrian shadows turn off - the transform
    ///    still moves, so the crowd still flows; it just stops pumping arms nobody can see
    ///    on a figure a few pixels tall.
    ///
    /// Also the per-frame driver for <see cref="PedestrianRepathQueue"/> - one Update for
    /// the whole crowd's bookkeeping instead of one per pedestrian.
    ///
    /// Created and configured by PedestrianSpawner; registration is per spawned pedestrian.
    /// </summary>
    public sealed class PedestrianLodSystem : MonoBehaviour
    {
        public static PedestrianLodSystem Instance { get; private set; }

        enum Tier { Full, Mid, Far }

        /// <summary>
        /// Ortho-size slack on downward (toward Full) tier changes, so a zoom hovering on a
        /// boundary does not flip 10k animators on and off every few frames.
        /// </summary>
        const float Hysteresis = 3f;

        struct Entry
        {
            public Animator Animator;
            public SkinnedMeshRenderer Mesh;
            public Tier Applied;
            public float LastTick;
        }

        Data.CityConfig config;
        readonly List<Entry> entries = new List<Entry>();
        Camera cam;
        Tier tier = Tier.Full;
        int cursor;

        public static void Register(GameObject pedestrian)
        {
            if (Instance)
                Instance.Add(pedestrian);
        }

        public void Configure(Data.CityConfig cityConfig)
        {
            config = cityConfig;
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Add(GameObject pedestrian)
        {
            entries.Add(new Entry
            {
                Animator = pedestrian.GetComponent<Animator>(),
                Mesh = pedestrian.GetComponentInChildren<SkinnedMeshRenderer>(true),
                Applied = Tier.Full,
                LastTick = Time.time,
            });
        }

        void Update()
        {
            PedestrianRepathQueue.Drain();

            if (entries.Count == 0 || !config)
                return;

            if (!cam)
            {
                cam = Camera.main;
                if (!cam)
                    return;
            }

            RetierFor(cam.orthographicSize);
            Sweep();
        }

        void RetierFor(float orthoSize)
        {
            var next = orthoSize > config.pedestrianLodFarOrtho ? Tier.Far
                     : orthoSize > config.pedestrianLodMidOrtho ? Tier.Mid
                     : Tier.Full;

            if (next < tier)
            {
                // Downward changes wait out the hysteresis band below the boundary that put
                // us in the current tier.
                var boundary = tier == Tier.Far
                    ? config.pedestrianLodFarOrtho
                    : config.pedestrianLodMidOrtho;
                if (orthoSize > boundary - Hysteresis)
                    return;
            }

            tier = next;
        }

        /// <summary>
        /// The round-robin visit. Sized so that in the Mid tier every animator is advanced
        /// once per lodMidRate frames; in the other tiers the same sweep merely applies
        /// pending tier changes, a comparison per entry.
        /// </summary>
        void Sweep()
        {
            var perFrame = Mathf.Clamp(
                Mathf.Max(64, entries.Count / Mathf.Max(2, config.pedestrianLodMidRate)),
                1, entries.Count);
            var now = Time.time;

            for (var visited = 0; visited < perFrame && entries.Count > 0; visited++)
            {
                cursor++;
                if (cursor >= entries.Count)
                    cursor = 0;

                var entry = entries[cursor];
                if (!entry.Animator)
                {
                    // Pedestrian destroyed - swap-pop and revisit this slot next iteration.
                    entries[cursor] = entries[entries.Count - 1];
                    entries.RemoveAt(entries.Count - 1);
                    cursor--;
                    continue;
                }

                switch (tier)
                {
                    case Tier.Full:
                        if (entry.Applied != Tier.Full)
                        {
                            entry.Animator.enabled = true;
                            SetShadows(entry, true);
                            entry.Applied = Tier.Full;
                        }
                        break;

                    case Tier.Mid:
                        if (entry.Applied != Tier.Mid)
                        {
                            entry.Animator.enabled = false;
                            SetShadows(entry, true);
                            entry.Applied = Tier.Mid;
                            entry.LastTick = now;
                        }
                        else
                        {
                            entry.Animator.Update(now - entry.LastTick);
                            entry.LastTick = now;
                        }
                        break;

                    case Tier.Far:
                        if (entry.Applied != Tier.Far)
                        {
                            entry.Animator.enabled = false;
                            SetShadows(entry, false);
                            entry.Applied = Tier.Far;
                        }
                        break;
                }

                entries[cursor] = entry;
            }
        }

        static void SetShadows(in Entry entry, bool on)
        {
            if (entry.Mesh)
                entry.Mesh.shadowCastingMode = on
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
