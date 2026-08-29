using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    [Flags]
    public enum ResidentialBlockChange
    {
        None = 0,
        Visual = 1,
        Structure = 2,
        Removed = 4,
    }

    /// <summary>
    /// The adapter between ResidentialLot's generated data and a scene view. A recipe owns
    /// no GameObject. It can therefore outlive any number of recycled views, and changing a
    /// future generator means replacing/invalidation here rather than teaching the camera,
    /// map or gameplay about another hierarchy.
    /// </summary>
    public sealed class ResidentialBlockRecipe
    {
        /// <summary>
        /// Bump when the meaning of the same ResidentialLot input changes. The plan hash
        /// catches data changes; this catches an optimiser/composer interpretation change.
        /// </summary>
        public const int GeneratorVersion = 1;

        public string Id { get; private set; }
        public string Name { get; private set; }
        public Rect LocalBounds { get; private set; }
        public int Seed { get; private set; }
        public ResidentialLot.Plan Plan { get; private set; }
        public int Revision { get; private set; }
        public ulong PlanHash { get; private set; }
        /// <summary>Conservative height used by the camera-frustum recycler. It is
        /// derived from recipe data, so a tilted camera never needs a live view merely
        /// to know whether the future view could reach into the picture.</summary>
        public float VisualHeight { get; private set; }

        public ulong ContentKey
        {
            get
            {
                ulong h = PlanHash;
                Mix(ref h, GeneratorVersion);
                Mix(ref h, Revision);
                return h;
            }
        }

        public event Action<ResidentialBlockRecipe, ResidentialBlockChange> Changed;

        public ResidentialBlockRecipe(string id, string name, Rect localBounds,
                                      ResidentialLot.Plan plan, int seed)
        {
            Id = string.IsNullOrEmpty(id) ? name : id;
            Name = string.IsNullOrEmpty(name) ? Id : name;
            Replace(localBounds, plan, seed, notify: false);
        }

        /// <summary>Replace a generated answer in place; an on-screen view is rebound next frame.</summary>
        public void Replace(Rect localBounds, ResidentialLot.Plan plan, int seed,
                            ResidentialBlockChange change = ResidentialBlockChange.Structure)
            => Replace(localBounds, plan, seed, notify: true, change);

        void Replace(Rect localBounds, ResidentialLot.Plan plan, int seed, bool notify,
                     ResidentialBlockChange change = ResidentialBlockChange.Structure)
        {
            LocalBounds = localBounds;
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            Seed = seed;
            Revision++;
            PlanHash = Hash(plan, localBounds, seed);
            VisualHeight = MeasureVisualHeight(plan);
            if (notify) Changed?.Invoke(this, change);
        }

        /// <summary>
        /// Force a rebind after an external visual dependency changes (material catalogue,
        /// optimiser version, generated prefab bake) even when the plan data did not.
        /// </summary>
        public void Invalidate(ResidentialBlockChange change = ResidentialBlockChange.Visual)
        {
            Revision++;
            Changed?.Invoke(this, change);
        }

        public ResidentialBlocks.Stood Compose(Transform root)
            => Compose(root, (prefab, parent) => UnityEngine.Object.Instantiate(prefab, parent));

        internal ResidentialBlocks.Stood Compose(
            Transform root, Func<GameObject, Transform, GameObject> raise)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (raise == null) throw new ArgumentNullException(nameof(raise));
            return ResidentialBlocks.Compose(Plan, root, new System.Random(Seed),
                raise);
        }

        public ResidentialBlocks.IncrementalComposition ComposeIncremental(Transform root)
            => ComposeIncremental(root,
                (prefab, parent) => UnityEngine.Object.Instantiate(prefab, parent));

        internal ResidentialBlocks.IncrementalComposition ComposeIncremental(
            Transform root, Func<GameObject, Transform, GameObject> raise)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (raise == null) throw new ArgumentNullException(nameof(raise));
            return ResidentialBlocks.ComposeIncremental(Plan, root, new System.Random(Seed),
                raise);
        }

        /// <summary>Stable FNV-1a over every layout decision that changes the stood block.</summary>
        public static ulong Hash(ResidentialLot.Plan plan, Rect bounds, int seed)
        {
            ulong h = 14695981039346656037UL;
            Mix(ref h, GeneratorVersion);
            Mix(ref h, seed);
            Mix(ref h, Mathf.RoundToInt(bounds.xMin * 1000f));
            Mix(ref h, Mathf.RoundToInt(bounds.yMin * 1000f));
            Mix(ref h, Mathf.RoundToInt(bounds.width * 1000f));
            Mix(ref h, Mathf.RoundToInt(bounds.height * 1000f));
            if (plan == null) return h;

            Mix(ref h, plan.W); Mix(ref h, plan.D); Mix(ref h, (int)plan.Klass);
            Mix(ref h, plan.YardBlock ? 1 : 0); Mix(ref h, plan.Artery); Mix(ref h, plan.Seed);
            Mix(ref h, plan.Lone ? 1 : 0);
            Mix(ref h, plan.FeaturedDiner);
            for (int side = 0; side < 4; side++)
            {
                Mix(ref h, plan.Street != null && side < plan.Street.Length && plan.Street[side] ? 1 : 0);
                Mix(ref h, plan.Role != null && side < plan.Role.Length ? (int)plan.Role[side] : -1);
            }

            if (plan.Spots != null)
                foreach (var spot in plan.Spots)
                {
                    Mix(ref h, spot?.Unit?.Name);
                    if (spot == null) continue;
                    Mix(ref h, spot.Yaw); Mix(ref h, spot.I); Mix(ref h, spot.J);
                    Mix(ref h, spot.CW); Mix(ref h, spot.CD); Mix(ref h, spot.Side);
                    Mix(ref h, spot.SideB); Mix(ref h, spot.AccessSide); Mix(ref h, spot.EntranceAt);
                    Mix(ref h, spot.Shop ? 1 : 0);
                }

            if (plan.Gaps != null)
                foreach (var gap in plan.Gaps) Mix(ref h, gap);
            Mix(ref h, plan.Cafe);
            if (plan.Cafes != null)
                foreach (var gap in plan.Cafes) Mix(ref h, gap);
            if (plan.Accesses != null)
                foreach (var access in plan.Accesses) Mix(ref h, access);
            Mix(ref h, plan.Subway);
            Mix(ref h, plan.SubwayAt);

            if (plan.Ground != null)
                for (int j = 0; j < plan.D; j++)
                    for (int i = 0; i < plan.W; i++) Mix(ref h, (int)plan.Ground[i, j]);
            return h;
        }

        static float MeasureVisualHeight(ResidentialLot.Plan plan)
        {
            // Palms, lamps and other dressing can stand above a low house. Twenty-four
            // metres is the conservative floor; measured unit heights raise it when a
            // future generator introduces a taller residential asset.
            float height = 24f;
            if (plan?.Spots == null) return height;
            foreach (var spot in plan.Spots)
                if (spot?.Unit != null) height = Mathf.Max(height, spot.Unit.MaxH + 3f);
            return height;
        }

        static void Mix(ref ulong h, ResidentialLot.Gap gap)
        {
            if (gap == null) { Mix(ref h, -1); return; }
            Mix(ref h, gap.Side); Mix(ref h, gap.At); Mix(ref h, gap.Run);
            Mix(ref h, gap.Depth); Mix(ref h, (int)gap.Use);
        }

        static void Mix(ref ulong h, ResidentialLot.Access access)
        {
            if (access == null) { Mix(ref h, -1); return; }
            Mix(ref h, access.Side); Mix(ref h, access.At);
            Mix(ref h, access.Vehicle ? 1 : 0); Mix(ref h, access.Purpose);
        }

        static void Mix(ref ulong h, string text)
        {
            if (text == null) { Mix(ref h, -1); return; }
            Mix(ref h, text.Length);
            for (int i = 0; i < text.Length; i++) Mix(ref h, text[i]);
        }

        static void Mix(ref ulong h, int value)
        {
            unchecked
            {
                uint v = (uint)value;
                for (int n = 0; n < 4; n++)
                {
                    h ^= (byte)(v >> (n * 8));
                    h *= 1099511628211UL;
                }
            }
        }
    }

    /// <summary>A mutable catalogue whose views can subscribe once, RecyclerView-style.</summary>
    public sealed class ResidentialBlockModel
    {
        readonly List<ResidentialBlockRecipe> _blocks = new List<ResidentialBlockRecipe>();
        readonly Dictionary<string, ResidentialBlockRecipe> _byId =
            new Dictionary<string, ResidentialBlockRecipe>(StringComparer.Ordinal);

        public IReadOnlyList<ResidentialBlockRecipe> Blocks => _blocks;
        public int Count => _blocks.Count;
        public event Action<ResidentialBlockRecipe, ResidentialBlockChange> Changed;

        public void Add(ResidentialBlockRecipe recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (_byId.ContainsKey(recipe.Id))
                throw new InvalidOperationException($"Two residential block recipes share id '{recipe.Id}'.");
            _blocks.Add(recipe);
            _byId.Add(recipe.Id, recipe);
            recipe.Changed += OnRecipeChanged;
            Changed?.Invoke(recipe, ResidentialBlockChange.Structure);
        }

        public bool Remove(string id)
        {
            if (id == null || !_byId.TryGetValue(id, out var recipe)) return false;
            recipe.Changed -= OnRecipeChanged;
            _byId.Remove(id);
            _blocks.Remove(recipe);
            Changed?.Invoke(recipe, ResidentialBlockChange.Removed);
            return true;
        }

        public bool TryGet(string id, out ResidentialBlockRecipe recipe)
            => _byId.TryGetValue(id, out recipe);

        public void Clear()
        {
            for (int i = 0; i < _blocks.Count; i++) _blocks[i].Changed -= OnRecipeChanged;
            _blocks.Clear();
            _byId.Clear();
            Changed?.Invoke(null, ResidentialBlockChange.Removed);
        }

        void OnRecipeChanged(ResidentialBlockRecipe recipe, ResidentialBlockChange change)
            => Changed?.Invoke(recipe, change);
    }
}
