using System.Collections.Generic;
using LivingCity.Generation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingCity.UI
{
    /// <summary>
    /// Press O in Play mode: a label appears over every block naming its zone and id
    /// ("Residential #12", "Park #3"). Press O again to hide them.
    ///
    /// Where the data comes from is the interesting part. CityGrid - the thing that actually
    /// knows each block's zone - is not serialized, so in a scene that was generated and saved
    /// it is null at Play (CityBuilder.Build spells this out). The one trace of block identity
    /// that survives into the saved scene is the ground slab names GroundPlacer writes:
    /// "ground_{zone}_{blockId}" (one slab per block, positioned at the block centre) and
    /// "ground_{zone}_{blockId}_{x}_{y}" for the park's per-cell tiles. Parsing those names
    /// means the overlay works on any already-saved city without regenerating it, and Rebuild()
    /// is the single place that depends on the format.
    ///
    /// Labels are screen-space TMP on the HUD canvas, repositioned from the block's world
    /// centre each frame. The canvas is ScreenSpaceOverlay, where a UI element's
    /// transform.position IS screen pixels, so WorldToScreenPoint feeds it directly and the
    /// CanvasScaler never enters into it. World-space text would need a billboard component
    /// this project does not have; screen-space needs nothing and stays crisp at any zoom.
    ///
    /// The canvas this lives on is built by Tools/City/Add Clock HUD; see CityHudSetup.
    /// </summary>
    public sealed class BlockOverlayHud : MonoBehaviour
    {
        [Tooltip("Left empty, this is found in the scene on Awake.")]
        [SerializeField] Camera worldCamera;

        [SerializeField] float fontSize = 20f;

        struct Entry
        {
            public Vector3 centre;
            public TextMeshProUGUI label;
        }

        readonly List<Entry> entries = new();

        // The "BlockOverlay" child under the canvas that owns every label; toggling the
        // overlay is one SetActive on this, not a loop over a hundred labels.
        GameObject labelRoot;

        // One outline material shared by every label. Setting TMP's .outlineWidth per label
        // would silently instance a material each - a draw call per block. Zone colour rides
        // on vertex colour instead, which is per-label and material-independent.
        Material labelMaterial;

        bool visible;
        bool warnedEmpty;

        void Awake()
        {
            // Same self-heal as CityClockHud: the setup menu wires this, but a component added
            // by hand deserialises with the field empty.
            if (!worldCamera)
                worldCamera = Camera.main;
            if (!worldCamera)
                worldCamera = FindAnyObjectByType<Camera>();

            if (worldCamera)
                return;

            Debug.LogWarning("[BlockOverlayHud] No camera in the scene - the block overlay is off.", this);
            enabled = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return;

            if (kb.oKey.wasPressedThisFrame)
                Toggle();
        }

        void Toggle()
        {
            if (!labelRoot)
                Rebuild();

            if (!labelRoot)
                return;

            visible = !visible;
            labelRoot.SetActive(visible);
        }

        /// <summary>
        /// Tears down and re-scans. Called lazily on the first toggle; call it again after
        /// regenerating the city in Play, otherwise the labels describe the previous one.
        /// </summary>
        public void Rebuild()
        {
            if (labelRoot)
                Destroy(labelRoot);
            entries.Clear();

            var blocks = CollectBlocks();
            if (blocks == null)
                return;

            // Built ACTIVE, hidden at the end. A TextMeshProUGUI only loads its font and
            // shared material in OnEnable, which never runs under an inactive parent - reading
            // any material off it before that throws. All of this happens inside one Update
            // call, so nothing renders in between and there is no one-frame flash.
            labelRoot = new GameObject("BlockOverlay", typeof(RectTransform));
            labelRoot.transform.SetParent(transform, false);

            foreach (var block in blocks.Values)
                entries.Add(new Entry
                {
                    centre = block.sum / block.count,
                    label = BuildLabel(block.zone, block.id),
                });

            labelRoot.SetActive(false);
            visible = false;
        }

        void LateUpdate()
        {
            if (!visible)
                return;

            var width = Screen.width;
            var height = Screen.height;

            foreach (var entry in entries)
            {
                var screen = worldCamera.WorldToScreenPoint(entry.centre);

                // Off the viewport (or behind the camera, z < 0) - disable the Graphic rather
                // than SetActive, which would rebuild the canvas layout every time the camera
                // pans a label across the edge.
                var on = screen.z > 0f &&
                         screen.x >= 0f && screen.x <= width &&
                         screen.y >= 0f && screen.y <= height;

                if (entry.label.enabled != on)
                    entry.label.enabled = on;

                if (!on)
                    continue;

                screen.z = 0f;
                entry.label.transform.position = screen;
            }
        }

        struct BlockInfo
        {
            public int id;
            public BlockZone zone;
            public Vector3 sum;
            public int count;
        }

        /// <summary>
        /// One sweep over the Ground category, grouping slabs by block id. Ordinary blocks have
        /// a single slab already centred on the block; the park lays one tile per cell, so its
        /// label sits on the average of them. Null when there is no generated city to read.
        /// </summary>
        Dictionary<int, BlockInfo> CollectBlocks()
        {
            var builder = FindAnyObjectByType<CityBuilder>();
            var root = builder ? builder.GeneratedRoot : null;
            var ground = root ? root.Find("Ground") : null;

            if (!ground)
            {
                if (!warnedEmpty)
                {
                    warnedEmpty = true;
                    Debug.LogWarning("[BlockOverlayHud] No generated city found - " +
                                     "nothing for the O overlay to label.", this);
                }
                return null;
            }

            var blocks = new Dictionary<int, BlockInfo>();

            foreach (Transform child in ground)
            {
                // "ground_{zone}_{blockId}" or "ground_{zone}_{blockId}_{x}_{y}" (park tiles).
                // The paint/patch decorations ("ground_paint_dark_{zone}_{blockId}") fail the
                // enum parse at parts[1] and drop out without needing their own prefix check.
                var parts = child.name.Split('_');
                if (parts.Length < 3 || parts[0] != "ground")
                    continue;
                if (!System.Enum.TryParse(parts[1], out BlockZone zone))
                    continue;
                if (!int.TryParse(parts[2], out var blockId))
                    continue;

                blocks.TryGetValue(blockId, out var info);
                info.id = blockId;
                info.zone = zone;
                info.sum += child.position;
                info.count++;
                blocks[blockId] = info;
            }

            return blocks;
        }

        TextMeshProUGUI BuildLabel(BlockZone zone, int blockId)
        {
            var go = new GameObject($"label_{zone}_{blockId}", typeof(RectTransform));
            go.transform.SetParent(labelRoot.transform, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.color = ZoneColour(zone);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            // The text never changes after this, so the label is allocation-free from here on -
            // LateUpdate only moves it. One interpolated string at build time is not the
            // per-frame allocation SetText exists to avoid (and SetText's format args only
            // take numbers anyway).
            text.text = $"{DisplayName(zone)} #{blockId}";

            // fontSharedMaterial, not fontMaterial: the fontMaterial getter quietly clones an
            // instance material for that one label, which is exactly the per-block draw call
            // this shared material exists to avoid.
            if (!labelMaterial && text.fontSharedMaterial)
            {
                labelMaterial = new Material(text.fontSharedMaterial);
                labelMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.25f);
                labelMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            }
            if (labelMaterial)
                text.fontSharedMaterial = labelMaterial;

            return text;
        }

        static string DisplayName(BlockZone zone) => zone switch
        {
            BlockZone.ResidentialHigh => "Residential",
            _ => zone.ToString(),
        };

        static Color ZoneColour(BlockZone zone) => zone switch
        {
            BlockZone.ResidentialHigh => new Color(0.95f, 0.95f, 0.95f),
            BlockZone.Industrial => new Color(1.00f, 0.62f, 0.25f),
            BlockZone.Park => new Color(0.45f, 0.95f, 0.45f),
            BlockZone.Parking => new Color(0.75f, 0.75f, 0.78f),
            BlockZone.Hospital => new Color(1.00f, 0.45f, 0.45f),
            BlockZone.School => new Color(1.00f, 0.90f, 0.35f),
            BlockZone.Bank => new Color(1.00f, 0.85f, 0.45f),
            _ => Color.white,
        };

        void OnDestroy()
        {
            if (labelMaterial)
                Destroy(labelMaterial);
        }
    }
}
