using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RoadDemo
{
    /// <summary>A man sat in an NPC car - the traffic's driver, the cruiser's officer,
    /// the odd front passenger. A body out of the crowd's prefabs, parented to the car
    /// at its seat (CarBody's seat table, the crews' riders' own), legs folded away
    /// under the sill the way a riding crewman's are, the bench sit loop playing on a
    /// graph of its own. Nothing to tick: the car carries him, the graph keeps its own
    /// time, and the component takes the graph down with the body. Cheap enough for a
    /// hundred cars - the animator is culled whole when the renderers are, and a body
    /// on the crowd's layer stops drawing with the crowd.</summary>
    public sealed class CarOccupant : MonoBehaviour
    {
        PlayableGraph _graph;

        /// <summary>Sit a driver in seat 0 and, with this chance, a passenger beside him.
        /// Returns the bodies (the driver first) so a caller may hide them - a cruiser
        /// stood in its stall is empty. Nothing happens without people or the clip.</summary>
        public static List<CarOccupant> Crew(Transform car, IList<GameObject> people, AnimationClip sitLoop,
            float passengerChance = 0f, int layer = -1)
        {
            var seated = new List<CarOccupant>(2);
            if (car == null || people == null || people.Count == 0 || sitLoop == null) return seated;
            // the seats of THIS body: behind its own steering wheel (CarBody.MeasureSeats)
            var seats = CarBody.MeasureSeats(car);
            var driver = Seat(car, people[Random.Range(0, people.Count)], sitLoop, seats[0], layer);
            if (driver != null) seated.Add(driver);
            if (passengerChance > 0f && Random.value < passengerChance)
            {
                var mate = Seat(car, people[Random.Range(0, people.Count)], sitLoop, seats[1], layer);
                if (mate != null) seated.Add(mate);
            }
            return seated;
        }

        /// <summary>One body into one seat of this car - the seat in the car's own frame
        /// (CarBody.MeasureSeats, or a CarBody's SeatLocalPoint).</summary>
        public static CarOccupant Seat(Transform car, GameObject prefab, AnimationClip sitLoop, Vector3 seatLocal,
            int layer = -1, float yaw = 0f)
        {
            if (car == null || prefab == null || sitLoop == null) return null;
            var go = Instantiate(prefab, car);
            go.name = prefab.name;
            // a body, not an actor: nothing of the prefab's own runs here
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            // no shadow: a few pixels under a hundred drivers is a cascade pass of
            // skinned meshes for nothing (the crowd's rule)
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (layer >= 0) SetLayerDeep(go, layer);

            // on the cushion, facing the bonnet; the sit clip carries the pelvis up off
            // the root - the same seats the crews' riders are set into
            go.transform.localPosition = seatLocal;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var occupant = go.AddComponent<CarOccupant>();
            occupant.Dress(sitLoop);
            return occupant;
        }

        void Dress(AnimationClip sitLoop)
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;

            // the pack's men are a size too big for the pack's cars: sat on the cushion
            // their shins come out under the sills, so the thighs are scaled to nothing
            // and the shins and feet go with them (CrewWalker.HideLegs, the same trick)
            if (animator.avatar != null && animator.avatar.isHuman)
            {
                var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var r = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                if (l) l.localScale = Vector3.one * 0.001f;
                if (r) r.localScale = Vector3.one * 0.001f;
            }

            _graph = PlayableGraph.Create("CarOccupant");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "anim", animator);
            var sit = AnimationClipPlayable.Create(_graph, sitLoop);
            // every man somewhere along the loop, not all breathing in step
            sit.SetTime(Random.value * sitLoop.length);
            output.SetSourcePlayable(sit);
            _graph.Play();
            if (transform.parent && transform.parent.TryGetComponent<VehicleSeatRig>(out var cabin))
            {
                // Evaluate before measuring bones, including cars initially offscreen.
                // No per-frame fitting or extra update component is needed.
                var culling = animator.cullingMode;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _graph.Evaluate(0f);
                cabin.FitSeated(transform, animator, transform.localPosition);
                animator.cullingMode = culling;
            }
        }

        /// <summary>Shown or not - a parked cruiser's officer is indoors, not sat in it.</summary>
        public void Show(bool on)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
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
