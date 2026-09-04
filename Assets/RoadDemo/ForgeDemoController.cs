using System;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Playable front end for the residential facade forge. This is deliberately only a
    /// harness: every click rolls <see cref="ResidentialFacade"/> and stands the answer
    /// through <see cref="ResidentialBlocks.StandSheet"/>. Nothing is baked, harvested or
    /// handed to either city demo.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ForgeDemoController : MonoBehaviour
    {
        public const string ContentRootName = "FORGE SHOWROOM";
        public const string GeneratedRootName = "FORGE GENERATED";

        const int MinLength = 3;
        const int MaxLength = 13;
        const int MinFloors = 3;
        const int MaxFloors = 5;
        const float GalleryGap = 12f;
        const float GalleryRowGap = 16f;
        const string PropsPolicy =
            "0% removes optional dressing; fire escapes + roof access always remain.";
        const string GroundMaterial =
            "Assets/Synty/PolygonGeneric/Materials/Generic_Concrete.mat";
        const string DoorMaterialAsset =
            "Assets/Synty/PolygonPalmCity/Materials/Buildings/Plaster_Red_01.mat";

        static readonly int[] GalleryLengths = { 4, 8, 11, 13 };
        static readonly Color DoorRed = new Color(0.95f, 0.12f, 0.10f);

        [Header("Next block")]
        public int seed = 1987;
        [Range(MinLength, MaxLength)] public int length = 8;
        [Range(MinFloors, MaxFloors)] public int floors = 4;
        [Range(ResidentialFacade.MinPropsPercent, ResidentialFacade.MaxPropsPercent)]
        public int propsPercent = ResidentialFacade.DefaultPropsPercent;

        string _status = "Choose a seed, length, floors and optional-props density, then generate. " +
                         PropsPolicy;
        GUIStyle _titleStyle;
        GUIStyle _labelStyle;
        GUIStyle _valueStyle;
        GUIStyle _buttonStyle;
        GUIStyle _statusStyle;
        GUIStyle _sliderStyle;
        GUIStyle _sliderThumbStyle;
        GUIStyle _tickStyle;
        Material _doorPaint;
        bool _ownsDoorPaint;
        [SerializeField, HideInInspector]
        string[] _standingSignatures = Array.Empty<string>();

        void Awake()
        {
            length = Mathf.Clamp(length, MinLength, MaxLength);
            floors = Mathf.Clamp(floors, MinFloors, MaxFloors);
            propsPercent = Mathf.Clamp(propsPercent, ResidentialFacade.MinPropsPercent,
                                       ResidentialFacade.MaxPropsPercent);
            if (transform.Find(GeneratedRootName) != null)
                _status = $"The saved forge result is standing at {propsPercent}% optional props. " +
                          "Generate a block or a fresh gallery. " + PropsPolicy;
        }

        void Start() => Reframe();

        void OnDestroy()
        {
            ForgetStandingUnits();
            if (_doorPaint == null || !_ownsDoorPaint) return;
            if (Application.isPlaying) Destroy(_doorPaint);
            else DestroyImmediate(_doorPaint);
        }

        /// <summary>Replace the display with exactly one sheet selected by the controls.</summary>
        public ResidentialFacade.Sheet GenerateBlock(
            Func<GameObject, Transform, GameObject> raise = null)
        {
            length = Mathf.Clamp(length, MinLength, MaxLength);
            floors = Mathf.Clamp(floors, MinFloors, MaxFloors);
            propsPercent = Mathf.Clamp(propsPercent, ResidentialFacade.MinPropsPercent,
                                       ResidentialFacade.MaxPropsPercent);
            var sheet = ResidentialFacade.Roll(seed, length, floors, propsPercent);
            var root = FreshGeneratedRoot();
            _standingSignatures = new[] { sheet.Signature };
            var doorPaint = DoorMaterial();
            var building = Stand(sheet, root, way: PositiveMod(seed, 3), raise);
            if (building != null)
            {
                Align(building, 0f);
                DoorTiles(building, sheet.Unit, doorPaint);
                Caption(building, sheet);
            }
            FinishGround(root);
            _status = SheetStatus(sheet);
            Reframe();
            return sheet;
        }

        /// <summary>
        /// Replace the display with the acceptance gallery: four lengths at each of the
        /// three permitted heights. The base seed is advanced per sheet, visibly in every
        /// caption, so the 4 x 3 grid exercises twelve deterministic rolls rather than
        /// cloning one.
        /// </summary>
        public ResidentialFacade.Sheet[] GenerateGallery(
            Func<GameObject, Transform, GameObject> raise = null)
        {
            propsPercent = Mathf.Clamp(propsPercent, ResidentialFacade.MinPropsPercent,
                                       ResidentialFacade.MaxPropsPercent);
            var sheets = new ResidentialFacade.Sheet[GalleryLengths.Length * 3];
            int at = 0;
            int faults = 0;
            int placedProps = 0;
            for (int n = MinFloors; n <= MaxFloors; n++)
            {
                for (int i = 0; i < GalleryLengths.Length; i++)
                {
                    int sheetSeed = unchecked(seed + at);
                    var sheet = ResidentialFacade.Roll(
                        sheetSeed, GalleryLengths[i], n, propsPercent);
                    sheets[at] = sheet;
                    faults += sheet.Faults != null ? sheet.Faults.Length : 0;
                    placedProps += sheet.Props != null ? sheet.Props.Length : 0;
                    at++;
                }
            }

            var root = FreshGeneratedRoot();
            _standingSignatures = Array.ConvertAll(sheets, sheet => sheet?.Signature);
            var doorPaint = DoorMaterial();
            float rowZ = 0f;
            for (at = 0; at < sheets.Length; at++)
            {
                int column = at % GalleryLengths.Length;
                if (column == 0 && at > 0)
                    rowZ += 10f + GalleryRowGap;
                var sheet = sheets[at];
                var building = Stand(sheet, root, way: PositiveMod(at, 3), raise);
                if (building == null) continue;
                float columnX = 0f;
                for (int i = 0; i < column; i++)
                    columnX += GalleryLengths[i] * 5f + GalleryGap;
                Align(building, columnX, rowZ);
                DoorTiles(building, sheet.Unit, doorPaint);
                Caption(building, sheet);
            }

            FinishGround(root);
            _status = $"Gallery: {sheets.Length} sheets from seed {seed}; OPTIONAL PROPS " +
                      $"{propsPercent}%; {placedProps} total placed props; {faults} fault(s). " +
                      PropsPolicy;
            Reframe();
            return sheets;
        }

        static GameObject Stand(ResidentialFacade.Sheet sheet, Transform parent, int way,
                                Func<GameObject, Transform, GameObject> raise) =>
            raise == null
                ? ResidentialBlocks.StandSheet(sheet, parent, way)
                : ResidentialBlocks.StandSheet(sheet, parent, way, raise);

        Transform FreshGeneratedRoot()
        {
            ForgetStandingUnits();
            var previous = transform.Find(GeneratedRootName);
            if (previous != null)
            {
                previous.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(previous.gameObject);
                else DestroyImmediate(previous.gameObject);
            }

            var go = new GameObject(GeneratedRootName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        /// <summary>Release only the generated identities owned by this showroom instance.</summary>
        public void ForgetStandingUnits()
        {
            if (_standingSignatures != null && _standingSignatures.Length > 0)
                ResidentialUnits.ForgetGenerated(_standingSignatures);
            _standingSignatures = Array.Empty<string>();
        }

        /// <summary>Put the visible minimum at (cursor, 0) after the sheet has stood.</summary>
        static Bounds Align(GameObject building, float cursor)
            => Align(building, cursor, 0f);

        static Bounds Align(GameObject building, float cursorX, float cursorZ)
        {
            if (!WorldBounds(building.transform, out var bounds))
                return new Bounds(new Vector3(cursorX, 0f, cursorZ), Vector3.one);
            building.transform.position +=
                new Vector3(cursorX - bounds.min.x, 0f, cursorZ - bounds.min.z);
            WorldBounds(building.transform, out bounds);
            return bounds;
        }

        static void DoorTiles(GameObject building, ResidentialUnit unit, Material material)
        {
            if (building == null || unit?.ShopBays == null) return;
            for (int i = 0; i < unit.ShopBays.Length; i++)
            {
                var door = unit.ShopBays[i].Door;
                if (door.Leaves <= 0 || door.Width <= 0.01f) continue;

                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = $"door tile {i + 1:00}";
                tile.transform.SetParent(building.transform, false);
                tile.transform.localPosition = new Vector3(door.X, 0.04f, door.Z);
                tile.transform.localRotation = Quaternion.Euler(0f, door.Yaw, 0f);
                tile.transform.localScale = new Vector3(door.Width, 0.08f, 0.60f);
                RemoveCollider(tile);

                var renderer = tile.GetComponent<MeshRenderer>();
                if (renderer != null && material != null) renderer.sharedMaterial = material;
            }
        }

        Material DoorMaterial()
        {
            if (_doorPaint != null) return _doorPaint;
            _doorPaint = DemoAssetLoad.Load<Material>(DoorMaterialAsset);
            if (_doorPaint != null) return _doorPaint;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            _doorPaint = new Material(shader) { name = "Forge door tiles" };
            _ownsDoorPaint = true;
            if (_doorPaint.HasProperty("_BaseColor")) _doorPaint.SetColor("_BaseColor", DoorRed);
            else _doorPaint.color = DoorRed;
            if (_doorPaint.HasProperty("_Smoothness")) _doorPaint.SetFloat("_Smoothness", 0.08f);
            return _doorPaint;
        }

        static void Caption(GameObject building, ResidentialFacade.Sheet sheet)
        {
            if (building == null || sheet == null) return;
            if (!WorldBounds(building.transform, out var bounds))
                bounds = new Bounds(building.transform.position, Vector3.one);
            int bays = sheet.Unit?.ShopBays?.Length ?? 0;
            int pieces = sheet.Unit?.Pieces ??
                         ((sheet.Pieces?.Length ?? 0) + (sheet.Props?.Length ?? 0));
            int props = sheet.Props?.Length ?? 0;
            int faults = sheet.Faults?.Length ?? 0;

            var go = new GameObject("forge caption");
            go.transform.SetParent(building.transform, true);
            go.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.max.y + 0.8f, bounds.min.z - 0.25f),
                Quaternion.Euler(30f, 0f, 0f));
            var text = go.AddComponent<TextMesh>();
            text.text = $"L {sheet.Length}   N {sheet.Floors}   seed {sheet.Seed}\n" +
                        $"{sheet.Length * 5} x 10 x {3 + sheet.Floors * 3} m   " +
                        $"{bays} shop bays   {bays * sheet.Floors} flats   " +
                        $"{pieces} pieces   {props} placed props\n" +
                        $"OPTIONAL PROPS {sheet.PropsPercent}%   {faults} faults";
            text.fontSize = 48;
            text.characterSize = 0.075f;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = faults == 0
                ? new Color(0.85f, 0.93f, 1f)
                : new Color(1f, 0.48f, 0.30f);
        }

        static void FinishGround(Transform generated)
        {
            if (generated == null || !WorldBounds(generated, out var bounds)) return;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "forge review ground";
            floor.transform.SetParent(generated, false);
            floor.transform.position = new Vector3(bounds.center.x, -0.15f, bounds.center.z);
            floor.transform.localScale = new Vector3(
                Mathf.Max(20f, bounds.size.x + 20f), 0.25f,
                Mathf.Max(24f, bounds.size.z + 20f));
            RemoveCollider(floor);
            var renderer = floor.GetComponent<Renderer>();
            var material = DemoAssetLoad.Load<Material>(GroundMaterial);
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            // Keep the slab first in the hierarchy so the buildings remain easy to inspect.
            floor.transform.SetAsFirstSibling();
        }

        static void RemoveCollider(GameObject go)
        {
            var collider = go != null ? go.GetComponent<Collider>() : null;
            if (collider == null) return;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }

        static bool WorldBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.GetComponent<TextMesh>() != null) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        void Reframe()
        {
            if (!Application.isPlaying) return;
            var rig = ReviewSceneCamera.Install(gameObject.scene);
            if (rig != null)
            {
                rig.hintTopPx = 14f;
                rig.hint = "FORGE DEMO   slider: optional props 0-200%   " +
                           "buttons: generate real sheets   WASD/arrows: move   " +
                           "Q/E or right-drag: rotate   wheel: zoom";
            }
        }

        static string SheetStatus(ResidentialFacade.Sheet sheet)
        {
            if (sheet == null) return "Forge returned no sheet.";
            int faults = sheet.Faults?.Length ?? 0;
            string status = $"{sheet.Signature}: {sheet.Pieces?.Length ?? 0} modules + " +
                            $"{sheet.Props?.Length ?? 0} placed props; OPTIONAL PROPS " +
                            $"{sheet.PropsPercent}%; {faults} fault(s). " + PropsPolicy;
            if (faults > 0)
            {
                int shown = Mathf.Min(3, faults);
                var first = new string[shown];
                for (int i = 0; i < shown; i++)
                    first[i] = Convert.ToString(sheet.Faults[i]) ?? "unknown fault";
                status += "  " + string.Join("; ", first) +
                          (faults > shown ? $"; +{faults - shown} more" : string.Empty);
            }
            return status;
        }

        static int PositiveMod(int value, int divisor) =>
            (int)((((long)value % divisor) + divisor) % divisor);

        void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.78f, 1.35f);
            var oldMatrix = GUI.matrix;
            int oldDepth = GUI.depth;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.depth = -50;

            const float x = 14f;
            const float y = 76f;
            const float width = 610f;
            const float height = 400f;
            GUI.Box(new Rect(x, y, width, height), GUIContent.none);
            GUI.Label(new Rect(x + 18f, y + 10f, width - 36f, 30f),
                "THE FORGE — RESIDENTIAL BLOCK GENERATOR", _titleStyle);

            float row = y + 50f;
            Stepper("SEED", ref seed, int.MinValue, int.MaxValue, x + 18f, row, 180f);
            Stepper("LENGTH", ref length, MinLength, MaxLength, x + 210f, row, 180f);
            Stepper("FLOORS", ref floors, MinFloors, MaxFloors, x + 402f, row, 180f);

            propsPercent = Mathf.Clamp(propsPercent, ResidentialFacade.MinPropsPercent,
                                       ResidentialFacade.MaxPropsPercent);
            GUI.Label(new Rect(x + 18f, y + 116f, 210f, 26f),
                "OPTIONAL PROPS", _labelStyle);
            GUI.Label(new Rect(x + 230f, y + 114f, 170f, 28f),
                $"{propsPercent}%", _valueStyle);
            if (GUI.Button(new Rect(x + 466f, y + 112f, 116f, 30f),
                           "RESET 100%", _buttonStyle))
                propsPercent = ResidentialFacade.DefaultPropsPercent;

            const float sliderX = x + 18f;
            const float sliderY = y + 146f;
            const float sliderWidth = width - 36f;
            propsPercent = Mathf.RoundToInt(GUI.HorizontalSlider(
                new Rect(sliderX, sliderY, sliderWidth, 30f), propsPercent,
                ResidentialFacade.MinPropsPercent, ResidentialFacade.MaxPropsPercent,
                _sliderStyle, _sliderThumbStyle));
            GUI.Label(new Rect(sliderX - 2f, y + 174f, 44f, 20f), "0%", _tickStyle);
            GUI.Label(new Rect(sliderX + sliderWidth * 0.5f - 26f, y + 174f, 52f, 20f),
                "100%", _tickStyle);
            GUI.Label(new Rect(sliderX + sliderWidth - 46f, y + 174f, 48f, 20f),
                "200%", _tickStyle);
            GUI.Label(new Rect(x + 18f, y + 194f, width - 36f, 28f),
                PropsPolicy, _tickStyle);

            if (GUI.Button(new Rect(x + 18f, y + 230f, 274f, 42f),
                           "GENERATE BLOCK", _buttonStyle))
                GenerateFromButton(gallery: false);
            if (GUI.Button(new Rect(x + 306f, y + 230f, 276f, 42f),
                           "GENERATE 12-SHEET GALLERY", _buttonStyle))
                GenerateFromButton(gallery: true);

            GUI.Label(new Rect(x + 18f, y + 282f, width - 36f, 102f), _status, _statusStyle);
            GUI.matrix = oldMatrix;
            GUI.depth = oldDepth;
        }

        void GenerateFromButton(bool gallery)
        {
            try
            {
                if (gallery) GenerateGallery();
                else GenerateBlock();
            }
            catch (Exception exception)
            {
                _status = "Forge failed: " + exception.Message;
                Debug.LogException(exception, this);
            }
        }

        void Stepper(string label, ref int value, int minimum, int maximum,
                     float x, float y, float width)
        {
            GUI.Label(new Rect(x, y, width, 22f), label, _labelStyle);
            if (GUI.Button(new Rect(x, y + 24f, 38f, 30f), "−", _buttonStyle) && value > minimum)
                value--;
            GUI.Label(new Rect(x + 44f, y + 24f, width - 88f, 30f), value.ToString(), _valueStyle);
            if (GUI.Button(new Rect(x + width - 38f, y + 24f, 38f, 30f), "+", _buttonStyle) &&
                value < maximum)
                value++;
        }

        void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
            _titleStyle.normal.textColor = new Color(1f, 0.80f, 0.32f);
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _labelStyle.normal.textColor = new Color(0.70f, 0.86f, 1f);
            _valueStyle = new GUIStyle(_labelStyle) { fontSize = 17 };
            _valueStyle.normal.textColor = Color.white;
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
            };
            _statusStyle.normal.textColor = new Color(0.90f, 0.93f, 0.96f);
            _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 18f,
            };
            _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 30f,
                fixedHeight = 30f,
            };
            _tickStyle = new GUIStyle(_labelStyle)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
            };
            _tickStyle.normal.textColor = new Color(0.72f, 0.76f, 0.82f);
        }
    }
}
