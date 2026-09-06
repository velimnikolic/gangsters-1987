using System.Collections.Generic;
using LivingCity.Gameplay;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE FOG ON THE STREET ITSELF. The paper maps hide what the outfit has no
    /// intelligence of; this applies the same rule to the 3D bodies - every citizen,
    /// every car on the road and at the kerb, the ambient people - by forcing their
    /// renderers off. forceRenderingOff hides meshes and shadows without touching
    /// activity, physics, animation time, traffic occupancy or streamed-holder lifetime.
    ///
    /// Owned and driven by DemoCrews (LateUpdate), which also owns the revealed-block
    /// set the judgement reads through MapVisionRegistry. Split out of the arena for
    /// what it is: a view of the fog, with no say in who fights whom.
    /// </summary>
    sealed class WorldFogView
    {
        sealed class FogRenderGroup
        {
            public readonly Renderer[] Renderers;
            public readonly bool[] ForcedBeforeFog;
            public bool Hidden;
            public int SeenFrame;
            /// <summary>Which frame in four this actor is judged on, and which frame in
            /// sixty-four a hidden one has its renderers re-asserted on: dealt from a
            /// running count so the work is spread level across frames.</summary>
            public readonly int Turn, Reassert;
            /// <summary>The revealed-block set the last judgement was made against.</summary>
            public int VisionVersion = -1;
            /// <summary>The body was switched on at the last judgement. A body coming
            /// back on (a pooled view stood up again) has its fog re-asserted on its next
            /// turn - within three frames - rather than waiting for its sixty-four-frame
            /// slot. Nothing in the project writes forceRenderingOff on an actor, so this
            /// is a guard; asking activeInHierarchy of every root every frame to make it
            /// instant would cost 0.66 ms a frame (measured 2026-09-06).</summary>
            public bool ActiveSeen;

            public FogRenderGroup(Transform root, int salt)
            {
                Renderers = root.GetComponentsInChildren<Renderer>(true);
                ForcedBeforeFog = new bool[Renderers.Length];
                Turn = salt & 3;
                Reassert = salt & 63;
            }
        }

        readonly Dictionary<Transform, FogRenderGroup> _groups =
            new Dictionary<Transform, FogRenderGroup>();
        readonly List<Transform> _prune = new List<Transform>();
        int _pruneAt;
        int _salt;
        DemoParkedCarGlow _parkedCars;

        /// <summary>Every body on the street, judged against the paper maps' rule.
        /// <paramref name="visionVersion"/> is the arena's count of changes to the
        /// revealed blocks; a change re-judges everybody at once.</summary>
        public void Apply(int visionVersion)
        {
            var walkers = PedestrianAgent.Everyone;
            for (var i = 0; i < walkers.Count; i++)
                Touch(walkers[i]?.Tf, visionVersion);

            var cars = RoadCar.All;
            for (var i = 0; i < cars.Count; i++)
                Touch(cars[i]?.Tf, visionVersion);

            var stoodCars = StoodCar.All;
            for (var i = 0; i < stoodCars.Count; i++)
                Touch(stoodCars[i]?.Tf, visionVersion);

            if (_parkedCars == null && Time.frameCount >= _pruneAt)
                _parkedCars = Object.FindFirstObjectByType<DemoParkedCarGlow>();
            if (_parkedCars != null)
                foreach (var car in _parkedCars.VisualCars)
                    Touch(car, visionVersion);

            var cityWalkers = LivingCity.Entities.PedestrianAgent.Agents;
            for (var i = 0; i < cityWalkers.Count; i++)
                if (cityWalkers[i] != null)
                    Touch(cityWalkers[i].transform, visionVersion);

            // The generated-city specialists (officers, gang members, school children,
            // visitors and buses) deliberately stay outside both pedestrian lists but
            // share the moving-subject overlay registry. Squares are places/buildings
            // and remain visible; diamonds are people or vehicles.
            var subjects = LivingCity.UI.OverlayRegistry.Subjects;
            for (var i = 0; i < subjects.Count; i++)
            {
                var subject = subjects[i];
                if (subject == null ||
                    subject.MarkerShape != LivingCity.UI.OverlayShape.Diamond)
                    continue;
                Touch(subject.OverlayAnchor, visionVersion);
            }

            var ambient = ResidentialBlockLife.ActivePopulations;
            for (var i = 0; i < ambient.Count; i++)
            {
                var life = ambient[i];
                if (life == null)
                    continue;
                for (var actor = 0; actor < life.VisionActorCount; actor++)
                    Touch(life.VisionActorAt(actor), visionVersion);
            }

            if (Time.frameCount < _pruneAt)
                return;
            _pruneAt = Time.frameCount + 60;
            RoadCar.PruneRegistered();
            _prune.Clear();
            foreach (var pair in _groups)
                if (pair.Key == null || pair.Value.SeenFrame != Time.frameCount)
                    _prune.Add(pair.Key);
            for (var i = 0; i < _prune.Count; i++)
            {
                var root = _prune[i];
                if (_groups.TryGetValue(root, out var group))
                    Set(group, false);
                _groups.Remove(root);
            }
        }

        // ONE ACTOR IN FOUR A FRAME. Close to three thousand roots go through here every
        // frame in the full city - every citizen, every car on the road and at the kerb -
        // and the answer for nearly all of them is the one they had last frame. Each is
        // judged on its own turn, one frame in four, unless the revealed blocks changed
        // since it was last judged, in which case it is judged now. A hidden actor's
        // renderers used to be forced off again on every frame as a guard against
        // something switching them back on; that is now done once in sixty-four frames
        // per actor, spread level, which keeps the guard and drops the 24,000 native
        // writes a frame it cost (5.5 ms of every frame, measured 2026-09-06).
        void Touch(Transform root, int visionVersion)
        {
            if (root == null)
                return;

            int frame = Time.frameCount;
            if (!_groups.TryGetValue(root, out var group))
            {
                if (!root.gameObject.activeInHierarchy)
                    return;
                group = new FogRenderGroup(root, _salt++);
                _groups.Add(root, group);
            }
            group.SeenFrame = frame;
            if ((frame & 3) != group.Turn && group.VisionVersion == visionVersion)
                return;
            if (!root.gameObject.activeInHierarchy)
            {
                group.ActiveSeen = false;
                return;
            }
            group.VisionVersion = visionVersion;
            bool hidden = !MapVisionRegistry.IsVisible(root.position);
            bool reassert = hidden && (!group.ActiveSeen || (frame & 63) == group.Reassert);
            group.ActiveSeen = true;
            Set(group, hidden, reassert);
        }

        static void Set(FogRenderGroup group, bool hidden, bool reassert = false)
        {
            if (group == null)
                return;

            if (group.Hidden == hidden)
            {
                if (hidden && reassert)
                    for (var i = 0; i < group.Renderers.Length; i++)
                        if (group.Renderers[i] != null)
                            group.Renderers[i].forceRenderingOff = true;
                return;
            }

            for (var i = 0; i < group.Renderers.Length; i++)
            {
                var renderer = group.Renderers[i];
                if (renderer == null)
                    continue;
                if (hidden)
                    group.ForcedBeforeFog[i] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = hidden || group.ForcedBeforeFog[i];
            }
            group.Hidden = hidden;
        }

        /// <summary>Hidden by the fog at its last judgement - nobody is drawing him.</summary>
        public bool Hidden(Transform root) =>
            root != null && _groups.TryGetValue(root, out var group) && group.Hidden;

        public void Clear()
        {
            foreach (var group in _groups.Values)
                Set(group, false);
            _groups.Clear();
            _prune.Clear();
        }
    }
}
