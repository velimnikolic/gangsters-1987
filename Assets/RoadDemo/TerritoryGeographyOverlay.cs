using System.Collections.Generic;
using System.Text;
using LivingCity.Territory;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>
    /// F9: the canonical geography drawn on the ground it describes. Every fact the
    /// geography API serves is meant to be inspectable here rather than taken on trust -
    /// block boundaries and ids, which blocks the graph calls neighbours, which businesses
    /// belong to which block, and where the men actually resolve to this tick.
    ///
    /// Failures are drawn in their own colour and never hidden: a block with no neighbour,
    /// a business that resolved to no block, a man standing on road space that belongs to
    /// nobody. Off, it costs one key read a frame - no lines exist and nothing is surveyed.
    ///
    /// It installs itself beside TerritoryRuntime (RoadDemoBuilder.BuildTerritoryFoundation),
    /// so a session where no editor menu was run still has it.
    /// </summary>
    public sealed class TerritoryGeographyOverlay : MonoBehaviour
    {
        const float RefreshSeconds = 0.35f;
        const float LineWidth = 0.55f;
        const float Height = 0.35f;

        /// <summary>How far from the camera's focus the overlay draws. The whole city is
        /// six hundred blocks; a survey of all of them every third of a second would cost
        /// more than the thing it is meant to measure.</summary>
        const float Reach = 260f;

        static readonly Color BlockInk = new Color(0.35f, 0.85f, 0.95f, 1f);
        static readonly Color NeighbourInk = new Color(0.45f, 0.95f, 0.5f, 1f);
        static readonly Color BusinessInk = new Color(1f, 0.82f, 0.25f, 1f);
        static readonly Color ActorInk = new Color(0.92f, 0.48f, 1f, 1f);
        static readonly Color FaultInk = new Color(1f, 0.3f, 0.28f, 1f);

        TerritoryRuntime runtime;
        Transform root;
        Camera worldCamera;
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        readonly Dictionary<Color, Material> inks = new Dictionary<Color, Material>();
        readonly List<TerritoryBlockId> near = new List<TerritoryBlockId>();
        readonly HashSet<TerritoryBlockId> occupied = new HashSet<TerritoryBlockId>();
        readonly StringBuilder legend = new StringBuilder(256);
        int used;
        bool shown;
        float nextSurvey;
        GUIStyle labelStyle;

        public bool IsVisible => shown;

        public void Init(TerritoryRuntime territoryRuntime) => runtime = territoryRuntime;

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
            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame &&
                !LivingCity.UI.PersonnelAlmanac.IsOpen)
                SetVisible(!shown);

            if (!shown)
                return;

            if (Time.unscaledTime < nextSurvey)
                return;
            nextSurvey = Time.unscaledTime + RefreshSeconds;
            Draw();
        }

        void SetVisible(bool visible)
        {
            shown = visible;
            nextSurvey = 0f;
            if (visible)
                return;

            used = 0;
            HideUnused();
            near.Clear();
            occupied.Clear();
        }

        void Draw()
        {
            used = 0;
            near.Clear();
            occupied.Clear();
            legend.Clear();

            var geography = runtime?.Geography;
            if (geography == null || geography.BlockIds.Count == 0)
            {
                HideUnused();
                legend.Append("no canonical geography in this scene");
                return;
            }

            var focus = FocusPoint();
            var ids = geography.BlockIds;
            for (var i = 0; i < ids.Count; i++)
            {
                if (!geography.TryGetBlock(ids[i], out var block))
                    continue;
                if (block.WorldBounds.DistanceTo(focus) > Reach)
                    continue;
                near.Add(ids[i]);
            }

            // Which blocks currently hold men. Read off the Presence sampling the runtime
            // already did this tick - asking the truth query block by block would walk
            // every crew in the city once per block on screen.
            for (var i = 0; i < near.Count; i++)
                if (runtime.Occupied(near[i]))
                    occupied.Add(near[i]);

            var faults = 0;
            for (var i = 0; i < near.Count; i++)
            {
                if (!geography.TryGetBlock(near[i], out var block))
                    continue;

                var neighbours = geography.Neighbours(near[i]);
                var lonely = neighbours.Count == 0;
                if (lonely)
                    faults++;

                var ink = lonely ? FaultInk : occupied.Contains(near[i]) ? ActorInk : BlockInk;
                Outline(block.WorldBounds, ink);

                // One link per pair, drawn from the lower id, so an edge is not painted
                // twice and the picture matches the graph's symmetry.
                var centre = Ground(block.Center);
                for (var n = 0; n < neighbours.Count; n++)
                {
                    if (string.CompareOrdinal(near[i].Value, neighbours[n].Value) >= 0 ||
                        !geography.TryGetBlock(neighbours[n], out var other))
                        continue;
                    Line(centre, Ground(other.Center), NeighbourInk);
                }

                // Businesses: a spur from the block's centre to each doorstep it owns.
                var businesses = geography.BusinessesOf(near[i]);
                for (var b = 0; b < businesses.Count; b++)
                {
                    if (!LivingCity.Business.BusinessRuntime.Instance ||
                        !LivingCity.Business.BusinessRuntime.Instance.TryGetSite(
                            businesses[b].BusinessId, out var site))
                        continue;
                    Line(centre, Ground(site.Approach), BusinessInk);
                }
            }

            var unplaced = geography.UnplacedBusinesses;
            for (var i = 0; i < unplaced.Count; i++)
            {
                if (!LivingCity.Business.BusinessRuntime.Instance ||
                    !LivingCity.Business.BusinessRuntime.Instance.TryGetSite(
                        unplaced[i].BusinessId, out var site))
                    continue;
                var point = Ground(site.Approach);
                if ((point - Ground(focus)).sqrMagnitude > Reach * Reach)
                    continue;
                Cross(point, FaultInk);
            }

            HideUnused();

            legend.Append("blocks in reach ").Append(near.Count)
                  .Append(" of ").Append(ids.Count)
                  .Append("  ·  occupied ").Append(occupied.Count)
                  .Append("  ·  lone blocks ").Append(faults)
                  .Append("  ·  unplaced businesses ").Append(unplaced.Count)
                  .Append("  ·  blockless men ").Append(runtime.BlocklessActors);
        }

        TerritoryPoint FocusPoint()
        {
            if (worldCamera == null)
                worldCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (worldCamera == null)
                return new TerritoryPoint(0f, 0f);

            var origin = worldCamera.transform.position;
            var forward = worldCamera.transform.forward;
            if (Mathf.Abs(forward.y) > 0.001f)
            {
                var distance = -origin.y / forward.y;
                if (distance > 0f)
                {
                    var hit = origin + forward * distance;
                    return new TerritoryPoint(hit.x, hit.z);
                }
            }

            return new TerritoryPoint(origin.x, origin.z);
        }

        static Vector3 Ground(TerritoryPoint point) => new Vector3(point.X, Height, point.Z);

        void Outline(TerritoryBounds bounds, Color ink)
        {
            var a = new Vector3(bounds.XMin, Height, bounds.ZMin);
            var b = new Vector3(bounds.XMax, Height, bounds.ZMin);
            var c = new Vector3(bounds.XMax, Height, bounds.ZMax);
            var d = new Vector3(bounds.XMin, Height, bounds.ZMax);
            var line = Next(ink, 5);
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.SetPosition(2, c);
            line.SetPosition(3, d);
            line.SetPosition(4, a);
        }

        void Line(Vector3 from, Vector3 to, Color ink)
        {
            var line = Next(ink, 2);
            line.SetPosition(0, from);
            line.SetPosition(1, to);
        }

        void Cross(Vector3 at, Color ink)
        {
            const float arm = 3f;
            var one = Next(ink, 2);
            one.SetPosition(0, at + new Vector3(-arm, 0f, -arm));
            one.SetPosition(1, at + new Vector3(arm, 0f, arm));
            var two = Next(ink, 2);
            two.SetPosition(0, at + new Vector3(-arm, 0f, arm));
            two.SetPosition(1, at + new Vector3(arm, 0f, -arm));
        }

        LineRenderer Next(Color ink, int points)
        {
            if (root == null)
            {
                root = new GameObject("Geography Overlay").transform;
                root.SetParent(transform, false);
            }

            LineRenderer line;
            if (used < lines.Count)
            {
                line = lines[used];
            }
            else
            {
                var go = new GameObject("line");
                go.transform.SetParent(root, false);
                line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.widthMultiplier = LineWidth;
                lines.Add(line);
            }

            used++;
            line.enabled = true;
            line.positionCount = points;
            line.material = Ink(ink);
            return line;
        }

        Material Ink(Color colour)
        {
            if (inks.TryGetValue(colour, out var material))
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Sprites/Default");
            material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", colour);
            material.color = colour;
            inks.Add(colour, material);
            return material;
        }

        void HideUnused()
        {
            for (var i = used; i < lines.Count; i++)
                if (lines[i].enabled)
                    lines[i].enabled = false;
        }

        void OnGUI()
        {
            if (!shown)
                return;

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                    richText = false,
                };
                labelStyle.normal.textColor = new Color(0.95f, 0.96f, 0.9f);
            }

            GUI.Label(new Rect(10f, Screen.height - 26f, Screen.width - 20f, 20f),
                      "GEOGRAPHY [F9]  " + legend, labelStyle);

            var geography = runtime?.Geography;
            if (geography == null || worldCamera == null)
                return;

            // The ids, over the blocks they name. Only what is on screen and in reach:
            // the label is for reading one block, not for wallpapering the city.
            for (var i = 0; i < near.Count; i++)
            {
                if (!geography.TryGetBlock(near[i], out var block))
                    continue;

                var screen = worldCamera.WorldToScreenPoint(Ground(block.Center));
                if (screen.z <= 0f)
                    continue;

                var rect = new Rect(screen.x - 90f, Screen.height - screen.y - 9f, 180f, 18f);
                var businesses = geography.BusinessesOf(near[i]).Count;
                GUI.Label(rect, block.DisplayName + "  ·  " + businesses + " biz", labelStyle);
            }
        }

        void OnDestroy()
        {
            foreach (var pair in inks)
                if (pair.Value != null)
                    Destroy(pair.Value);
            inks.Clear();
        }
    }
}
