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
    /// (BikePose) instead of left where the clip left them.
    ///
    /// Not ExecuteAlways either: nothing here is driven by a Unity message off Play -
    /// Seat() and Dress() are called outright - so running in edit mode would buy nothing
    /// and would drag BikePose back in with it.</summary>
    public sealed class BikeOccupant : MonoBehaviour, RiderSpill.IBody
    {
        PlayableGraph _graph;

        // The clip player: a two-input mixer, never a bare clip playable.
        //
        // A rider used to have exactly one thing to do - sit - and one clip playing for
        // ever was the whole of it. He now also comes off the machine (RiderSpill): he
        // falls, he hits the road, and then he either dies there or picks himself up,
        // which is four clips one after another with a blend between each. So the seat
        // is simply the first clip played on a mixer that can hold two, and the second
        // input is whatever is fading out.
        AnimationMixerPlayable _mix;
        AnimationClipPlayable _now, _was;
        AnimationClip _clip;
        bool _loop, _held;
        float _blend, _blendRate;

        /// <summary>The pose this body is riding in - the caller sets its speed, its
        /// foot down and its gun (RoadBike does it every frame).</summary>
        public BikePose Pose { get; private set; }

        /// <summary>RiderSpill.IBody: the transform a fall throws, and whether his
        /// death is already running. It never is for one of these - a body out of the
        /// traffic has no life of its own to be killed in, so the spill decides.</summary>
        public Transform Root => transform;

        public bool AlreadyDying => false;

        /// <summary>What is playing on him this instant, and how far into it.</summary>
        public AnimationClip Playing => _clip;

        public float ClipTime => _now.IsValid() ? (float)_now.GetTime() : 0f;

        /// <summary>A one-shot clip that has run out and is being held on its last
        /// frame - what "he has finished dying" means. Always false while a loop is
        /// playing.</summary>
        public bool Finished => _held;

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
            _mix = AnimationMixerPlayable.Create(_graph, 2);
            output.SetSourcePlayable(_mix);
            // every man somewhere along the loop, so a row of bikes at a light is not
            // one man copied four times
            Play(sitLoop, loop: true, fade: 0f, at: Random.value * Mathf.Max(0.01f, sitLoop.length));
            _graph.Play();
        }

        /// <summary>Play this clip on him, fading off whatever was playing. A clip that
        /// does not loop is HELD on its last frame when it runs out rather than snapping
        /// back to the start - a man who has finished dying stays dead.</summary>
        public void Play(AnimationClip clip, bool loop = true, float fade = 0.15f,
            float speed = 1f, float at = 0f)
        {
            if (clip == null || !_graph.IsValid() || !_mix.IsValid()) return;

            // the one that was already fading out has had its turn
            if (_was.IsValid()) { _mix.DisconnectInput(1); _graph.DestroyPlayable(_was); _was = default; }
            if (_now.IsValid()) { _mix.DisconnectInput(0); _was = _now; _mix.ConnectInput(1, _was, 0); }

            _now = AnimationClipPlayable.Create(_graph, clip);
            _now.SetApplyFootIK(false);
            _now.SetTime(at);
            _now.SetSpeed(speed);
            _mix.ConnectInput(0, _now, 0);

            _clip = clip;
            _loop = loop;
            _held = false;
            _blend = fade > 0.001f && _was.IsValid() ? 0f : 1f;
            _blendRate = fade > 0.001f ? 1f / fade : 1000f;
            _mix.SetInputWeight(0, _blend);
            _mix.SetInputWeight(1, 1f - _blend);
        }

        void Update()
        {
            if (!_graph.IsValid() || !_now.IsValid()) return;

            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + _blendRate * Time.deltaTime);
                _mix.SetInputWeight(0, _blend);
                _mix.SetInputWeight(1, 1f - _blend);
                if (_blend >= 1f && _was.IsValid())
                {
                    _mix.DisconnectInput(1);
                    _graph.DestroyPlayable(_was);
                    _was = default;
                }
            }

            // The wrap is done here rather than left to the clip's own loop flag: half
            // the wardrobe is FBX takes whose import settings nobody has set to loop,
            // and a "loop" that quietly plays once is the sort of fault that reads as a
            // man freezing mid-stride.
            float len = _clip != null ? _clip.length : 0f;
            if (len <= 0.01f || _held) return;
            double t = _now.GetTime();
            if (t < len) return;
            if (_loop) _now.SetTime(t - len);
            else
            {
                _now.SetTime(len - 0.0001f);
                _now.SetSpeed(0f);
                _held = true;
            }
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
