using System.Collections.Generic;
using System.Text;
using LivingCity.Territory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>A pluggable read-only section in the developer territory inspector.</summary>
    public interface ITerritoryDiagnosticsSection
    {
        string Title { get; }
        void Append(StringBuilder text, TerritoryBlockTruth block, TerritoryRuntime runtime);
    }

    /// <summary>
    /// F8 developer inspector. Shift-click identifies a Core block from the immutable
    /// territory plan; brackets cycle blocks. It reads debug truth and has no mutation buttons.
    /// </summary>
    public sealed class TerritoryDiagnosticsHud : MonoBehaviour
    {
        const float RefreshSeconds = 0.2f;

        readonly List<ITerritoryDiagnosticsSection> sections =
            new List<ITerritoryDiagnosticsSection>();
        readonly StringBuilder text = new StringBuilder(2048);

        TerritoryRuntime runtime;
        TerritoryBlockId selectedBlockId;
        Camera worldCamera;
        GUIStyle panelStyle;
        GUIStyle textStyle;
        bool shown;
        float nextRefresh;
        string rendered = "";

        public TerritoryBlockId SelectedBlockId => selectedBlockId;

        public void Init(TerritoryRuntime territoryRuntime)
        {
            runtime = territoryRuntime;
            sections.Clear();
            sections.Add(new IdentitySection());
            sections.Add(new GeographySection());
            sections.Add(new ResponsibilitySection());
            sections.Add(new PhysicalActorsSection());
            // The businesses page installs itself rather than waiting to be registered:
            // the business pass runs BEFORE this HUD is built, so a push from that side
            // would depend on component start order.
            sections.Add(new BusinessDiagnosticsSection());
            sections.Add(new EventSection());
        }

        public void RegisterSection(ITerritoryDiagnosticsSection section)
        {
            if (section != null && !sections.Contains(section))
                sections.Add(section);
        }

        void Start()
        {
#if !UNITY_EDITOR
            if (!Debug.isDebugBuild)
            {
                Destroy(this);
                return;
            }
#endif
            if (runtime == null)
                runtime = TerritoryRuntime.Instance;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f8Key.wasPressedThisFrame)
            {
                shown = !shown;
                if (shown && !selectedBlockId.IsValid)
                    SelectIndex(0);
                nextRefresh = 0f;
            }

            if (!shown || runtime?.DebugTruth == null)
                return;

            if (keyboard.leftBracketKey.wasPressedThisFrame)
                Cycle(-1);
            if (keyboard.rightBracketKey.wasPressedThisFrame)
                Cycle(1);

            var mouse = Mouse.current;
            var shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            if (mouse != null && shift && mouse.leftButton.wasPressedThisFrame)
                SelectUnderPointer(mouse.position.ReadValue());
        }

        public bool Select(TerritoryBlockId blockId)
        {
            if (runtime?.DebugTruth == null ||
                !runtime.DebugTruth.TryGetBlock(blockId, out _))
                return false;

            selectedBlockId = blockId;
            nextRefresh = 0f;
            return true;
        }

        public bool SelectAtWorld(Vector3 world)
        {
            return runtime != null && runtime.TryGetBlockAtWorld(world, out var blockId) &&
                   Select(blockId);
        }

        void SelectUnderPointer(Vector2 screen)
        {
            if (LivingCity.UI.PersonnelAlmanac.IsOpen || TurfMapHud.IsOpen ||
                (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
                return;

            if (worldCamera == null)
                worldCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (worldCamera == null)
                return;

            var ray = worldCamera.ScreenPointToRay(screen);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out var distance))
                SelectAtWorld(ray.GetPoint(distance));
        }

        void Cycle(int direction)
        {
            var blocks = runtime.DebugTruth.BlockIds;
            if (blocks.Count == 0)
                return;

            var index = 0;
            for (var i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] != selectedBlockId)
                    continue;
                index = i;
                break;
            }

            index = (index + direction + blocks.Count) % blocks.Count;
            SelectIndex(index);
        }

        void SelectIndex(int index)
        {
            var blocks = runtime?.DebugTruth?.BlockIds;
            if (blocks == null || blocks.Count == 0)
                return;
            selectedBlockId = blocks[Mathf.Clamp(index, 0, blocks.Count - 1)];
            nextRefresh = 0f;
        }

        void OnGUI()
        {
            if (!shown || runtime?.DebugTruth == null)
                return;

            if (panelStyle == null)
            {
                panelStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(14, 14, 12, 12),
                };
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true,
                    richText = false,
                };
                textStyle.normal.textColor = new Color(0.9f, 0.92f, 0.87f);
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                RebuildText();
                nextRefresh = Time.unscaledTime + RefreshSeconds;
            }

            var height = Mathf.Min(760f, Screen.height - 20f);
            GUI.Box(new Rect(10f, 10f, 540f, height), GUIContent.none, panelStyle);
            GUI.Label(new Rect(25f, 22f, 510f, height - 24f), rendered, textStyle);
        }

        void RebuildText()
        {
            text.Clear();
            text.AppendLine("TERRITORY DIAGNOSTICS  [DEV]")
                .AppendLine("F8 close  ·  Shift+click select  ·  [ / ] cycle")
                .AppendLine();

            if (!selectedBlockId.IsValid ||
                !runtime.DebugTruth.TryGetBlock(selectedBlockId, out var block))
            {
                text.Append("No canonical block selected.");
                rendered = text.ToString();
                return;
            }

            for (var i = 0; i < sections.Count; i++)
            {
                text.Append(sections[i].Title.ToUpperInvariant()).AppendLine();
                sections[i].Append(text, block, runtime);
                if (i < sections.Count - 1)
                    text.AppendLine().AppendLine();
            }

            rendered = text.ToString();
        }

        sealed class IdentitySection : ITerritoryDiagnosticsSection
        {
            public string Title => "Identity";

            public void Append(
                StringBuilder text, TerritoryBlockTruth block, TerritoryRuntime runtime)
            {
                var definition = block.Definition;
                var bounds = definition.WorldBounds;
                text.Append("canonical block: ").AppendLine(definition.Id.Value)
                    .Append("legacy plan index: ").AppendLine(definition.LegacyBlockId.ToString())
                    .Append("name: ").AppendLine(definition.DisplayName)
                    .Append("neighborhood: ").Append(definition.NeighborhoodName)
                    .Append("  [").Append(definition.NeighborhoodId.Value).AppendLine("]")
                    .Append("world bounds: ")
                    .Append(bounds.XMin.ToString("0.0")).Append(", ")
                    .Append(bounds.ZMin.ToString("0.0")).Append("  ")
                    .Append(bounds.Width.ToString("0.0")).Append(" x ")
                    .Append(bounds.Depth.ToString("0.0")).AppendLine(" m")
                    .Append("identity source: ").Append(definition.IdentitySource);
            }
        }

        /// <summary>
        /// What the canonical geography says about this block: which neighborhood it
        /// aggregates into, which blocks the graph calls its neighbours, how many
        /// businesses stand on it, and whether the city-wide validation found anything.
        /// Everything here is read from the one geography facade - if this page and the
        /// map ever disagree, one of them is not reading it.
        /// </summary>
        sealed class GeographySection : ITerritoryDiagnosticsSection
        {
            public string Title => "Geography (canonical)";

            public void Append(
                StringBuilder text, TerritoryBlockTruth block, TerritoryRuntime runtime)
            {
                var geography = runtime?.Geography;
                if (geography == null)
                {
                    text.Append("no canonical geography in this scene");
                    return;
                }

                var id = block.Definition.Id;
                var centre = block.Definition.Center;
                text.Append("centre: ").Append(centre.X.ToString("0.0")).Append(", ")
                    .Append(centre.Z.ToString("0.0")).AppendLine();

                if (geography.TryGetNeighborhood(
                        block.Definition.NeighborhoodId, out var hood))
                    text.Append("neighborhood holds ").Append(hood.BlockIds.Count)
                        .Append(" blocks, touches ").Append(hood.Neighbours.Count)
                        .AppendLine(" other neighborhoods");

                var neighbours = geography.Neighbours(id);
                text.Append("block neighbours: ").Append(neighbours.Count);
                for (var i = 0; i < neighbours.Count && i < 6; i++)
                {
                    text.AppendLine();
                    text.Append("  · ");
                    if (geography.TryGetBlock(neighbours[i], out var other))
                        text.Append(other.DisplayName);
                    else
                        text.Append(neighbours[i].Value);
                }

                if (neighbours.Count > 6)
                    text.AppendLine().Append("  · … ").Append(neighbours.Count - 6).Append(" more");

                var businesses = geography.BusinessesOf(id);
                text.AppendLine().Append("businesses on this block: ").Append(businesses.Count);
                for (var i = 0; i < businesses.Count && i < 5; i++)
                    text.AppendLine().Append("  · ").Append(businesses[i].Label)
                        .Append("  [").Append(businesses[i].Binding).Append(']');
                if (businesses.Count > 5)
                    text.AppendLine().Append("  · … ").Append(businesses.Count - 5).Append(" more");

                var report = geography.Report;
                text.AppendLine().Append("city: ").Append(report.Blocks).Append(" blocks · ")
                    .Append(report.Neighborhoods).Append(" neighborhoods · ")
                    .Append(report.Edges).Append(" edges · ")
                    .Append(report.PlacedBusinesses).Append(" businesses placed");
                if (report.UnplacedBusinesses > 0 || report.IsolatedBlocks > 0)
                    text.AppendLine().Append("faults: ").Append(report.UnplacedBusinesses)
                        .Append(" unplaced businesses, ").Append(report.IsolatedBlocks)
                        .Append(" lone blocks (F9 draws them)");
            }
        }

        sealed class ResponsibilitySection : ITerritoryDiagnosticsSection
        {
            public string Title => "Command responsibility (not ownership)";

            public void Append(
                StringBuilder text, TerritoryBlockTruth block, TerritoryRuntime runtime)
            {
                var view = block.Responsibility;
                var value = view.Responsibility;
                if (!value.IsAssigned)
                {
                    text.Append("unassigned");
                    return;
                }

                text.Append("gang: ").AppendLine(value.GangId.ToString())
                    .Append("command node: ").AppendLine(value.CommandNodeId.ToString())
                    .Append("boss: ").AppendLine(value.BossId.IsValid
                        ? Name(view.BossName, value.BossId)
                        : "unassigned")
                    .Append("lieutenant: ").Append(value.LieutenantId.IsValid
                        ? Name(view.LieutenantName, value.LieutenantId)
                        : "unassigned");
            }

            static string Name(string displayName, TerritoryCharacterId id) =>
                string.IsNullOrEmpty(displayName) ? "#" + id.Value : displayName + " (#" + id.Value + ")";
        }

        sealed class PhysicalActorsSection : ITerritoryDiagnosticsSection
        {
            public string Title => "Physical actors";

            public void Append(
                StringBuilder text, TerritoryBlockTruth block, TerritoryRuntime runtime)
            {
                if (block.Actors.Count == 0)
                {
                    text.Append("none currently queryable");
                    return;
                }

                for (var i = 0; i < block.Actors.Count; i++)
                {
                    var actor = block.Actors[i];
                    text.Append(actor.GangName.Length > 0 ? actor.GangName : "gang #" + actor.GangId.Value)
                        .Append("  ·  ")
                        .Append(actor.GroupId)
                        .Append("  ·  ")
                        .Append(actor.DisplayName.Length > 0 ? actor.DisplayName : "actor #" + actor.CharacterId.Value);
                    if (actor.LeadsGroup)
                        text.Append("  [LT]");
                    if (i < block.Actors.Count - 1)
                        text.AppendLine();
                }
            }
        }

        sealed class EventSection : ITerritoryDiagnosticsSection
        {
            public string Title => "Territory events / future sections";

            public void Append(
                StringBuilder text, TerritoryBlockTruth block, TerritoryRuntime runtime)
            {
                var recent = runtime.Events?.Recent;
                var shown = 0;
                if (recent != null)
                {
                    for (var i = recent.Count - 1; i >= 0 && shown < 5; i--)
                    {
                        var record = recent[i];
                        if (record.Value == null || record.Value.BlockId != block.Definition.Id)
                            continue;
                        if (shown > 0)
                            text.AppendLine();
                        text.Append('#').Append(record.Sequence).Append("  ")
                            .Append(record.Value.GetType().Name)
                            .Append("  @ ").Append(record.Value.GameHour.ToString("0.00")).Append('h');
                        shown++;
                    }
                }

                if (shown == 0)
                    text.Append("no territory events recorded for this block");

                text.AppendLine().Append("reserved: Presence · Fear · businesses/compliance · ")
                    .Append("rival influence · derived control");
            }
        }
    }
}
