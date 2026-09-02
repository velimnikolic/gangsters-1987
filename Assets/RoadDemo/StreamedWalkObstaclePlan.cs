using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Owns the obstacle plan measured from one streamed block payload. Keeping the
    /// ownership on the payload itself, rather than only in CityBlockRecycler's managed
    /// View record, lets an already-active payload republish its own plan after an Editor
    /// script/domain reload. A full city reload still requires a fresh Play session so
    /// the builder can restore its static solids and streaming model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreamedWalkObstaclePlan : MonoBehaviour
    {
        [SerializeField] float _groundY;

        SidewalkPlan _plan;
        bool _registered;

        public int Count => _plan?.Count ?? 0;
        public bool Registered => _registered;

        /// <summary>Measure the complete inactive payload once it has been composed.
        /// The navigation plan belongs to the resident payload, not its renderer's
        /// current camera visibility.</summary>
        public void Bind(float groundY)
        {
            Unregister();
            _groundY = groundY;
            _plan = WalkObstacles.ComposedPropPlan(transform, _groundY);
            Register();
        }

        /// <summary>Publish while this streamed view is standing.</summary>
        public void Register()
        {
            // Be defensive about Editor hot-reload restoration: a local flag is not
            // proof that the non-serialised plan survived or remains in the reset global
            // ledger. Normal enable/disable still takes the fast idempotent branch.
            if (_plan != null && Published(_plan))
            {
                _registered = true;
                return;
            }
            _registered = false;
            // Non-serialised plans disappear in a domain reload; the Transform and
            // ground level survive, so OnEnable can reconstruct the exact same answer.
            if (_plan == null)
                _plan = WalkObstacles.ComposedPropPlan(transform, _groundY);
            _registered = WalkObstacles.RegisterPlan(_plan);
        }

        static bool Published(SidewalkPlan plan)
        {
            var plans = WalkObstacles.Props;
            for (int i = 0; i < plans.Count; i++)
                if (object.ReferenceEquals(plans[i], plan)) return true;
            return false;
        }

        /// <summary>Retire only when the payload is rebound, evicted or destroyed.</summary>
        public void Unregister()
        {
            if (_plan != null && (_registered || Published(_plan)))
                WalkObstacles.UnregisterPlan(_plan);
            _registered = false;
        }

        void OnEnable() => Register();
        // Renderer attachment/visibility is not world simulation. Cached block views
        // are disabled off-camera but their cafe/diner geometry remains true, so the
        // navigation ledger stays published until actual eviction.
        void OnDestroy() => Unregister();
    }
}
