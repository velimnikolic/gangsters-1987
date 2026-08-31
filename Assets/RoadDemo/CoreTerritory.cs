using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Stable logical parts of Core. The names describe their position relative to the
    /// river/land side, so the ids stay truthful when a seed moves the river from east to west.
    /// Display names are rolled separately and are what the player sees.
    /// </summary>
    public enum CoreQuarterId
    {
        None = 0,
        Downtown = 1,
        NorthLandward = 2,
        NorthRiverside = 3,
        Landward = 4,
        SouthLandward = 5,
        SouthRiverside = 6,
    }

    /// <summary>Immutable identity of one generated block.</summary>
    public sealed class CoreBlockDefinition
    {
        public int Id { get; }
        public string StableId { get; }
        public string Name { get; }
        public string SourceName { get; }
        public CoreQuarterId QuarterId { get; }
        public Rect LocalBounds { get; }

        /// <summary>What the plan says this block IS - "res", "park", "yard-lot", "quay",
        /// "apron", "bank", or the source block's own name. The same word the StableId
        /// ends in, kept as a field so a consumer never has to take the id apart.</summary>
        public string Kind { get; }

        internal CoreBlockDefinition(int id, string stableId, string name, string sourceName,
                                     CoreQuarterId quarterId, Rect localBounds, string kind)
        {
            Id = id;
            StableId = stableId;
            Name = name;
            SourceName = sourceName;
            QuarterId = quarterId;
            LocalBounds = localBounds;
            Kind = kind ?? "";
        }
    }

    /// <summary>
    /// Immutable definition of one conquerable quarter. Ownership is deliberately not here:
    /// the same generated plan can be loaded with any campaign state.
    /// </summary>
    public sealed class CoreQuarterDefinition
    {
        readonly List<int> _blockIds = new List<int>();
        readonly CoreQuarterId[] _neighbours;

        public CoreQuarterId Id { get; }
        public string Name { get; }
        public Rect LocalBounds { get; internal set; }
        public Vector2 LocalAnchor => LocalBounds.center;
        public IReadOnlyList<int> BlockIds => _blockIds;
        public IReadOnlyList<CoreQuarterId> Neighbours => _neighbours;

        internal CoreQuarterDefinition(CoreQuarterId id, string name, CoreQuarterId[] neighbours)
        {
            Id = id;
            Name = name;
            _neighbours = neighbours ?? Array.Empty<CoreQuarterId>();
        }

        internal void Add(int blockId) => _blockIds.Add(blockId);
    }

    /// <summary>
    /// Pure territory data produced by CoreLayout. It owns no GameObject and is therefore safe
    /// for save/load, the tactical map and combat planning even while block views are recycled.
    /// </summary>
    public sealed class CoreTerritoryPlan
    {
        static readonly CoreQuarterId[] QuarterOrder =
        {
            CoreQuarterId.Downtown,
            CoreQuarterId.NorthLandward,
            CoreQuarterId.NorthRiverside,
            CoreQuarterId.Landward,
            CoreQuarterId.SouthLandward,
            CoreQuarterId.SouthRiverside,
        };

        readonly List<CoreBlockDefinition> _blocks = new List<CoreBlockDefinition>();
        readonly List<CoreQuarterDefinition> _quarters = new List<CoreQuarterDefinition>();
        readonly Dictionary<int, CoreBlockDefinition> _blockById =
            new Dictionary<int, CoreBlockDefinition>();
        readonly Dictionary<CoreQuarterId, CoreQuarterDefinition> _quarterById =
            new Dictionary<CoreQuarterId, CoreQuarterDefinition>();

        public int Seed { get; private set; }
        public IReadOnlyList<CoreBlockDefinition> Blocks => _blocks;
        public IReadOnlyList<CoreQuarterDefinition> Quarters => _quarters;

        public CoreBlockDefinition Block(int id) =>
            _blockById.TryGetValue(id, out var block) ? block : null;

        public CoreQuarterDefinition Quarter(CoreQuarterId id) =>
            _quarterById.TryGetValue(id, out var quarter) ? quarter : null;

        public CoreBlockDefinition BlockAt(Vector2 local)
        {
            CoreBlockDefinition best = null;
            float bestArea = float.MaxValue;
            for (int i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                if (!block.LocalBounds.Contains(local)) continue;
                float area = block.LocalBounds.width * block.LocalBounds.height;
                if (area < bestArea) { best = block; bestArea = area; }
            }
            return best;
        }

        public CoreQuarterId? QuarterAt(Vector2 local)
        {
            // A point on an actual block is unambiguous, including nested downtown blocks.
            var block = BlockAt(local);
            if (block != null) return block.QuarterId;

            // Roads carry no block id, but a battle/order placed in the body of a quarter
            // should still resolve to it. Quarter bounds are disjoint in the current topology.
            for (int i = 0; i < _quarters.Count; i++)
                if (_quarters[i].BlockIds.Count > 0 && _quarters[i].LocalBounds.Contains(local))
                    return _quarters[i].Id;
            return null;
        }

        internal static CoreTerritoryPlan Build(int seed, List<CoreLayout.Block> source)
        {
            var territory = new CoreTerritoryPlan { Seed = seed };
            var names = new StreetNames(unchecked(seed * 31 + 17), Array.Empty<bool>(), Array.Empty<bool>());
            int rolledName = 0;
            for (int i = 0; i < QuarterOrder.Length; i++)
            {
                var id = QuarterOrder[i];
                string name = id == CoreQuarterId.Downtown ? "Downtown" : names.Quarter(rolledName++);
                var quarter = new CoreQuarterDefinition(id, name, NeighboursOf(id));
                territory._quarters.Add(quarter);
                territory._quarterById.Add(id, quarter);
            }

            var unique = new HashSet<CoreLayout.Block>();
            var ordered = new List<CoreLayout.Block>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                {
                    var block = source[i];
                    if (block != null && unique.Add(block)) ordered.Add(block);
                }

            ordered.Sort(CompareBlocks);
            var ordinal = new Dictionary<CoreQuarterId, int>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var block = ordered[i];
                if (block.QuarterId == CoreQuarterId.None) block.QuarterId = CoreQuarterId.Downtown;
                var quarter = territory.Quarter(block.QuarterId);
                if (quarter == null)
                    throw new InvalidOperationException($"Core block '{block.Name}' has unknown quarter {block.QuarterId}.");

                ordinal.TryGetValue(block.QuarterId, out int inQuarter);
                inQuarter++;
                ordinal[block.QuarterId] = inQuarter;

                block.BlockId = i;
                block.StableId = StableId(seed, block);
                block.DisplayName = $"{quarter.Name} Block {inQuarter:00}";

                var definition = new CoreBlockDefinition(
                    block.BlockId, block.StableId, block.DisplayName, block.Name,
                    block.QuarterId, block.Box, Kind(block));
                territory._blocks.Add(definition);
                territory._blockById.Add(definition.Id, definition);
                quarter.Add(definition.Id);
                quarter.LocalBounds = Encapsulate(quarter.LocalBounds, definition.LocalBounds,
                                                  quarter.BlockIds.Count == 1);
            }
            return territory;
        }

        static int CompareBlocks(CoreLayout.Block a, CoreLayout.Block b)
        {
            int byQuarter = a.QuarterId.CompareTo(b.QuarterId);
            if (byQuarter != 0) return byQuarter;
            int byNorth = b.Box.center.y.CompareTo(a.Box.center.y);
            if (byNorth != 0) return byNorth;
            int byEast = a.Box.center.x.CompareTo(b.Box.center.x);
            if (byEast != 0) return byEast;
            int byArea = (a.Box.width * a.Box.height).CompareTo(b.Box.width * b.Box.height);
            return byArea != 0 ? byArea : string.CompareOrdinal(a.Name, b.Name);
        }

        /// <summary>What a block is, in one word. The StableId ends in it and
        /// <see cref="CoreBlockDefinition.Kind"/> carries it, so both come from here.</summary>
        static string Kind(CoreLayout.Block block) =>
            CoreLayout.IsRes(block) ? "res"
                : CoreLayout.IsPark(block) ? "park"
                : CoreLayout.IsYard(block) ? "yard-" + (block.Unit ?? "lot")
                : CoreLayout.IsQuay(block) ? "quay"
                : CoreLayout.IsApron(block) ? "apron"
                : CoreLayout.IsBank(block) ? "bank"
                : block.Name;

        static string StableId(int seed, CoreLayout.Block block)
        {
            var box = block.Box;
            string kind = Kind(block);
            return $"core:{seed}:{(int)block.QuarterId}:" +
                   $"{Mathf.RoundToInt(box.xMin / CoreLayout.Cell)}:" +
                   $"{Mathf.RoundToInt(box.yMin / CoreLayout.Cell)}:" +
                   $"{Mathf.RoundToInt(box.width / CoreLayout.Cell)}:" +
                   $"{Mathf.RoundToInt(box.height / CoreLayout.Cell)}:{kind}";
        }

        static Rect Encapsulate(Rect whole, Rect add, bool first) => first
            ? add
            : Rect.MinMaxRect(Mathf.Min(whole.xMin, add.xMin), Mathf.Min(whole.yMin, add.yMin),
                              Mathf.Max(whole.xMax, add.xMax), Mathf.Max(whole.yMax, add.yMax));

        static CoreQuarterId[] NeighboursOf(CoreQuarterId id)
        {
            switch (id)
            {
                case CoreQuarterId.Downtown:
                    return new[] { CoreQuarterId.NorthRiverside, CoreQuarterId.Landward,
                                   CoreQuarterId.SouthRiverside };
                case CoreQuarterId.NorthLandward:
                    return new[] { CoreQuarterId.NorthRiverside, CoreQuarterId.Landward };
                case CoreQuarterId.NorthRiverside:
                    return new[] { CoreQuarterId.NorthLandward, CoreQuarterId.Downtown };
                case CoreQuarterId.Landward:
                    return new[] { CoreQuarterId.NorthLandward, CoreQuarterId.Downtown,
                                   CoreQuarterId.SouthLandward };
                case CoreQuarterId.SouthLandward:
                    return new[] { CoreQuarterId.Landward, CoreQuarterId.SouthRiverside };
                case CoreQuarterId.SouthRiverside:
                    return new[] { CoreQuarterId.Downtown, CoreQuarterId.SouthLandward };
                default:
                    return Array.Empty<CoreQuarterId>();
            }
        }
    }

    public enum QuarterConflictState { Peaceful, Contested }

    /// <summary>Mutable campaign state kept apart from generated territory definitions.</summary>
    public sealed class CityQuarterState
    {
        public CoreQuarterId Id { get; }
        public int OwnerGangId { get; internal set; } = -1;
        public QuarterConflictState Conflict { get; internal set; }
        public int AttackerGangId { get; internal set; } = -1;
        public float CaptureProgress { get; internal set; }

        internal CityQuarterState(CoreQuarterId id) { Id = id; }
    }

    /// <summary>
    /// Runtime access point for combat, AI, save/load and UI. Core registers one immutable
    /// plan; only the small CityQuarterState objects change during a campaign.
    /// </summary>
    public sealed class CityTerritoryRegistry
    {
        readonly Dictionary<CoreQuarterId, CityQuarterState> _states =
            new Dictionary<CoreQuarterId, CityQuarterState>();
        CoreTerritoryPlan _plan;
        DistrictFrame _frame = DistrictFrame.Identity;

        public CoreTerritoryPlan Plan => _plan;
        /// <summary>A cheap deterministic fingerprint for map redraws. Six quarter states are
        /// small enough to hash on demand and this avoids a second ownership event protocol.</summary>
        public int StateStamp
        {
            get
            {
                int stamp = 17;
                if (_plan == null) return stamp;
                for (int i = 0; i < _plan.Quarters.Count; i++)
                {
                    var state = State(_plan.Quarters[i].Id);
                    if (state == null) continue;
                    stamp = stamp * 31 + state.OwnerGangId + 2;
                    stamp = stamp * 31 + (int)state.Conflict;
                    stamp = stamp * 31 + state.AttackerGangId + 2;
                    stamp = stamp * 31 + Mathf.RoundToInt(state.CaptureProgress * 1000f);
                }
                return stamp;
            }
        }
        public event Action<CityQuarterState, int> QuarterOwnerChanged;

        public void Load(CoreTerritoryPlan plan, DistrictFrame frame)
        {
            _plan = plan;
            _frame = frame;
            _states.Clear();
            if (plan == null) return;
            for (int i = 0; i < plan.Quarters.Count; i++)
                _states.Add(plan.Quarters[i].Id, new CityQuarterState(plan.Quarters[i].Id));
        }

        public CityQuarterState State(CoreQuarterId id) =>
            _states.TryGetValue(id, out var state) ? state : null;

        public CoreQuarterId? QuarterAt(Vector3 world)
        {
            if (_plan == null) return null;
            var local = _frame.ToLocal(world);
            return _plan.QuarterAt(new Vector2(local.x, local.z));
        }

        public CoreQuarterDefinition Quarter(CoreQuarterId id) => _plan?.Quarter(id);

        public CoreBlockDefinition Block(int id) => _plan?.Block(id);

        public CoreBlockDefinition BlockAt(Vector3 world)
        {
            if (_plan == null) return null;
            var local = _frame.ToLocal(world);
            return _plan.BlockAt(new Vector2(local.x, local.z));
        }

        public Rect WorldBounds(CoreQuarterId id)
        {
            var quarter = Quarter(id);
            return quarter != null ? _frame.ToWorldRect(quarter.LocalBounds) : Rect.zero;
        }

        /// <summary>The exact footprint used by map and 3D block indicators.</summary>
        public Rect WorldBounds(int blockId)
        {
            var block = Block(blockId);
            return block != null ? _frame.ToWorldRect(block.LocalBounds) : Rect.zero;
        }

        public Vector3 BattleAnchor(CoreQuarterId id)
        {
            var quarter = Quarter(id);
            return quarter != null
                ? _frame.ToWorld(new Vector3(quarter.LocalAnchor.x, 0f, quarter.LocalAnchor.y))
                : _frame.origin;
        }

        public bool AreNeighbours(CoreQuarterId one, CoreQuarterId other)
        {
            var quarter = Quarter(one);
            if (quarter == null) return false;
            for (int i = 0; i < quarter.Neighbours.Count; i++)
                if (quarter.Neighbours[i] == other) return true;
            return false;
        }

        public bool SetOwner(CoreQuarterId id, int gangId)
        {
            var state = State(id);
            if (state == null || state.OwnerGangId == gangId) return false;
            int previous = state.OwnerGangId;
            state.OwnerGangId = gangId;
            state.Conflict = QuarterConflictState.Peaceful;
            state.AttackerGangId = -1;
            state.CaptureProgress = 0f;
            QuarterOwnerChanged?.Invoke(state, previous);
            return true;
        }

        public bool Contest(CoreQuarterId id, int attackerGangId, float progress)
        {
            var state = State(id);
            if (state == null) return false;
            state.Conflict = QuarterConflictState.Contested;
            state.AttackerGangId = attackerGangId;
            state.CaptureProgress = Mathf.Clamp01(progress);
            return true;
        }

        public bool ClearContest(CoreQuarterId id)
        {
            var state = State(id);
            if (state == null) return false;
            state.Conflict = QuarterConflictState.Peaceful;
            state.AttackerGangId = -1;
            state.CaptureProgress = 0f;
            return true;
        }
    }

    /// <summary>Optional host capability; district demos that do not run gameplay need none.</summary>
    public interface ICityTerritoryHost
    {
        CityTerritoryRegistry Territories { get; }
    }

    public partial class RoadDemoBuilder : ICityTerritoryHost
    {
        public CityTerritoryRegistry Territories { get; } = new CityTerritoryRegistry();
    }
}
