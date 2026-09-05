using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace RoadDemo
{
    public sealed partial class CityBlockRecycler
    {
        readonly Dictionary<string, (ulong Key, SidewalkPlan Plan)> _navigation =
            new Dictionary<string, (ulong, SidewalkPlan)>();

        public int NavigationBlocks => _navigation.Count;
        public long NavigationBakeMs { get; private set; }

        /// <summary>Measure every recipe before walkers are populated. Only compact
        /// obstacle boxes remain resident; the inactive measuring payload is returned
        /// to the prefab pool without ever attaching its renderers. Using the actual
        /// composer keeps cafe choices, furniture and parked props identical to 3D.</summary>
        public void PrepareNavigation()
        {
            if (_model == null) return;
            foreach (var recipe in _model.Blocks) EnsureNavigation(recipe);
        }

        void EnsureNavigation(ResidentialBlockRecipe recipe)
        {
            if (_navigation.TryGetValue(recipe.Id, out var old) && old.Key == recipe.ContentKey) return;
            var watch = Stopwatch.StartNew();
            var view = PrepareView(recipe, visible: false);
            try
            {
                using (var composition = recipe.ComposeIncremental(view.Content,
                    (prefab, parent) => _prefabPool.Acquire(prefab, parent, view.Parts)))
                {
                    while (composition.Step()) { }
                    if (composition.Result.Missing > 0)
                        throw new System.InvalidOperationException(
                            $"Cannot prepare walking geometry for {recipe.Name}: {composition.Result.Missing} missing prefabs");
                }
                var holder = view.Holder.transform;
                holder.SetParent(transform, false);
                holder.localPosition = new Vector3(recipe.LocalBounds.xMin, 0f, recipe.LocalBounds.yMin);
                holder.localRotation = Quaternion.identity;
                // Do not let the old answer suppress identical boxes in its replacement.
                if (old.Plan != null) WalkObstacles.UnregisterPlan(old.Plan);
                var plan = WalkObstacles.ComposedPropPlan(view.Content, transform.position.y);
                WalkObstacles.RegisterPlan(plan);
                _navigation[recipe.Id] = (recipe.ContentKey, plan);
            }
            catch
            {
                if (old.Plan != null) WalkObstacles.RegisterPlan(old.Plan);
                throw;
            }
            finally
            {
                DestroyPayload(view, countEviction: false);
                ReturnHolder(view);
                NavigationBakeMs += watch.ElapsedMilliseconds;
            }
        }

        void RemoveNavigation(string id)
        {
            if (!_navigation.TryGetValue(id, out var entry)) return;
            WalkObstacles.UnregisterPlan(entry.Plan);
            _navigation.Remove(id);
        }

        void ClearNavigation()
        {
            foreach (var entry in _navigation.Values) WalkObstacles.UnregisterPlan(entry.Plan);
            _navigation.Clear();
        }
    }
}
