using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RoadDemo
{
    /// <summary>A man on an NPC motorcycle - the traffic's rider, the odd mate behind
    /// him. CarOccupant's twin, and deliberately so: a body out of the crowd's prefabs,
    /// parented to the bike, one seated clip looping on a graph of its own, nothing to
    /// tick and nothing to own. Where it parts company with the car is the pose: a man
    /// in a car is a torso and a pair of arms with his legs folded away out of sight,
    /// and a man on a bike is all of him, so the limbs are put where the bike says
    /// (BikePose) instead of left where the clip left them.</summary>
    public sealed class BikeOccupant : MonoBehaviour
    {
        PlayableGraph _graph;

        /// <summary>The pose this body is riding in - the caller sets its speed, its
        /// foot down and its gun (RoadBike does it every frame).</summary>
        public BikePose Pose { get; private set; }

        /// <summary>Put a rider on this bike and, with this chance, a mate behind him.
        /// Returns them, the rider first, so a caller may hide them - a bike stood on
        /// its stand outside a bar has nobody on it. Nothing happens without people or
        /// a clip.</summary>
        public static List<BikeOccupant> Ride(BikeBody bike, IList<GameObject> people, AnimationClip sitLoop,
            float pillionChance = 0f, int layer = -1)
        {
            var riding = new List<BikeOccupant>(2);
            if (bike == null || bike.Tf == null || people == null || people.Count == 0 || sitLoop == null)
                return riding;

            var rider = Seat(bike, people[Random.Range(0, people.Count)], sitLoop, pillion: false, layer: layer);
            if (rider != null) riding.Add(rider);
            if (bike.SeatsTwo && pillionChance > 0f && Random.value < pillionChance)
            {
                var mate = Seat(bike, people[Random.Range(0, people.Count)], sitLoop, pillion: true, layer: layer);
                if (mate != null)
                {
                    if (rider != null) mate.Pose.Rider = rider.Pose;
                    riding.Add(mate);
                }
            }
            return riding;
        }

        /// <summary>One body onto one saddle.</summary>
        public static BikeOccupant Seat(BikeBody bike, GameObject prefab, AnimationClip sitLoop,
            bool pillion, int layer = -1)
        {
            if (bike == null || bike.Tf == null || prefab == null || sitLoop == null) return null;
            var go = Instantiate(prefab, bike.Tf);
            go.name = prefab.name + (pillion ? " (pillion)" : " (rider)");
            // a body, not an actor: nothing of the prefab's own runs here
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            // no shadow, the crowd's rule: a few pixels of rider under a cascade pass is
            // a skinned mesh drawn twice for nothing
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (layer >= 0) SetLayerDeep(go, layer);

            go.transform.localPosition = pillion ? bike.SaddlePillion : bike.SaddleRider;
            go.transform.localRotation = Quaternion.identity;

            var occupant = go.AddComponent<BikeOccupant>();
            occupant.Dress(sitLoop);
            occupant.Pose = go.AddComponent<BikePose>();
            if (!occupant.Pose.Setup(bike, pillion))
            {
                // not a humanoid rig: he would sit through the tank. Better no rider.
                Destroy(go);
                return null;
            }
            return occupant;
        }

        void Dress(AnimationClip sitLoop)
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            // NOT CullCompletely, which is what a car's driver gets: the pose is written
            // in LateUpdate off bone positions the animator has to have refreshed, and a
            // rider frozen at the moment he left the frame comes back with his fists off
            // the bars. Update-off-screen is a pose evaluation, not a draw.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _graph = PlayableGraph.Create("BikeOccupant");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "anim", animator);
            var sit = AnimationClipPlayable.Create(_graph, sitLoop);
            // every man somewhere along the loop, so a row of bikes at a light is not
            // one man copied four times
            sit.SetTime(Random.value * Mathf.Max(0.01f, sitLoop.length));
            output.SetSourcePlayable(sit);
            _graph.Play();
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerDeep(child.gameObject, layer);
        }
    }
}
