using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The armory counter's live display case. Each visible catalogue card gets a small
    /// isolated camera and the real merchandise prefab, turning steadily around its
    /// vertical centre. It is deliberately separate from PortraitStudio's developed
    /// photographs: newspaper and dossier pictures remain still, screened prints.
    ///
    /// Rigs live far outside the city and on their own layer. They render only while the
    /// RawImage is active, so a closed book or another ledger page has no camera cost.
    /// </summary>
    public sealed class CatalogueTurntableStudio : MonoBehaviour
    {
        // Layers 30 and 31 belong to the strategic-map ambience and PortraitStudio.
        // 29 is unnamed and otherwise unused by the project.
        const int TurntableLayer = 29;
        const int TextureWidth = 384;
        const int TextureHeight = 128;
        const float FieldOfView = 25f;
        const float RigSpacing = 120f;
        const float DegreesPerSecond = 22f;

        static CatalogueTurntableStudio instance;
        static System.Random showroom = new System.Random();

        sealed class Preview
        {
            public RawImage Target;
            public GameObject Rig;
            public Transform Turntable;
            public Camera Camera;
            public RenderTexture Texture;
        }

        readonly List<Preview> previews = new List<Preview>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            if (instance)
                instance.Strike();
            instance = null;
            showroom = new System.Random();
        }

        /// <summary>
        /// Put the real prefab in a live display case. The target is filled immediately;
        /// when its catalogue card is rebuilt or destroyed, the matching rig follows it.
        /// Twin-pack listings may ask for two copies of the same body.
        /// </summary>
        public static void Show(GameObject prefab, bool vehicle, RawImage target,
            int copies = 1)
        {
            if (!prefab || !target)
                return;

            Require().BuildPreview(prefab, vehicle, target, Mathf.Clamp(copies, 1, 2));
        }

        static CatalogueTurntableStudio Require()
        {
            if (instance)
                return instance;

            var host = new GameObject("Catalogue Turntable Studio");
            host.transform.position = new Vector3(-3500f, -700f, -3500f);
            instance = host.AddComponent<CatalogueTurntableStudio>();
            return instance;
        }

        void OnDestroy()
        {
            Strike();
            if (instance == this)
                instance = null;
        }

        void LateUpdate()
        {
            HideFromWorldCameras();

            for (var i = previews.Count - 1; i >= 0; i--)
            {
                var preview = previews[i];
                if (!preview.Target)
                {
                    Release(preview);
                    previews.RemoveAt(i);
                    continue;
                }

                var visible = preview.Target.isActiveAndEnabled &&
                              preview.Target.gameObject.activeInHierarchy;
                if (preview.Rig.activeSelf != visible)
                    preview.Rig.SetActive(visible);
                if (visible)
                    preview.Turntable.Rotate(Vector3.up,
                        DegreesPerSecond * Time.unscaledDeltaTime, Space.World);
            }
        }

        void BuildPreview(GameObject prefab, bool vehicle, RawImage target, int copies)
        {
            var slot = previews.Count;
            var rig = new GameObject("Turntable " + prefab.name);
            rig.transform.SetParent(transform, false);
            rig.transform.localPosition = Vector3.right * slot * RigSpacing;
            rig.layer = TurntableLayer;

            var stand = new GameObject("Stand").transform;
            stand.SetParent(rig.transform, false);
            stand.gameObject.layer = TurntableLayer;

            var contents = new GameObject("Merchandise").transform;
            contents.SetParent(stand, false);
            contents.gameObject.layer = TurntableLayer;

            // Nothing from a street prefab may wake up in the display case. The same
            // preparation path used by the photographer removes behaviour and physics
            // before the inactive stand is shown.
            stand.gameObject.SetActive(false);
            for (var i = 0; i < copies; i++)
            {
                var subject = Instantiate(prefab, contents);
                subject.transform.localPosition = copies == 1
                    ? Vector3.zero
                    : new Vector3((i == 0 ? -1f : 1f) * 0.16f, 0f, 0f);
                subject.transform.localRotation = copies == 1
                    ? Quaternion.identity
                    : Quaternion.Euler(0f, i == 0 ? -8f : 8f, i == 0 ? -8f : 8f);
                PortraitStudio.StripForDisplay(subject);
                SetLayer(subject.transform);

                if (vehicle)
                    Gameplay.VehiclePaint.Apply(subject, prefab, showroom);
            }
            stand.gameObject.SetActive(true);

            var bounds = BoundsOf(contents.gameObject);
            contents.position += stand.position - bounds.center;
            bounds = BoundsOf(contents.gameObject);

            var texture = new RenderTexture(TextureWidth, TextureHeight, 16,
                RenderTextureFormat.ARGB32)
            {
                name = "Catalogue " + prefab.name,
                antiAliasing = 4,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.Create();

            var cameraGo = new GameObject("Camera");
            cameraGo.transform.SetParent(rig.transform, false);
            cameraGo.layer = TurntableLayer;
            var camera = cameraGo.AddComponent<Camera>();
            camera.cullingMask = 1 << TurntableLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = LedgerStyle.PolaroidDark;
            camera.fieldOfView = FieldOfView;
            camera.aspect = TextureWidth / (float)TextureHeight;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 60f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.targetTexture = texture;

            AddLight(rig.transform, "Key Light", new Color(1f, 0.91f, 0.76f), 2.6f);
            AddLight(rig.transform, "Fill Light", new Color(0.72f, 0.82f, 1f), 1.2f);
            Aim(camera, rig.transform, bounds, vehicle);

            target.texture = texture;
            target.uvRect = new Rect(0f, 0f, 1f, 1f);
            target.color = Color.white;
            target.enabled = true;

            previews.Add(new Preview
            {
                Target = target,
                Rig = rig,
                Turntable = stand,
                Camera = camera,
                Texture = texture,
            });
            HideFromWorldCameras();
        }

        static void AddLight(Transform rig, string name, Color colour, float intensity)
        {
            var lightGo = new GameObject(name);
            lightGo.transform.SetParent(rig, false);
            lightGo.layer = TurntableLayer;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = 30f;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        static void Aim(Camera camera, Transform rig, Bounds bounds, bool vehicle)
        {
            var horizontalRadius = Mathf.Max(0.05f,
                new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
            var verticalRadius = Mathf.Max(0.05f, bounds.extents.y);
            var verticalTan = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            var horizontalTan = verticalTan * camera.aspect;
            var distance = Mathf.Max(verticalRadius / verticalTan,
                horizontalRadius / horizontalTan) + horizontalRadius;
            distance = Mathf.Max(0.35f, distance * (vehicle ? 1.12f : 1.18f));

            var around = vehicle
                ? Quaternion.Euler(-14f, 38f, 0f)
                : Quaternion.Euler(-9f, 58f, 0f);
            camera.transform.position = rig.position + around * Vector3.forward * distance;
            camera.transform.rotation = Quaternion.LookRotation(
                rig.position - camera.transform.position, Vector3.up);

            var key = rig.Find("Key Light");
            if (key)
                key.position = camera.transform.position + camera.transform.up *
                    (verticalRadius + 0.5f) + camera.transform.right * horizontalRadius;
            var fill = rig.Find("Fill Light");
            if (fill)
                fill.position = rig.position - camera.transform.right *
                    (horizontalRadius + 1f) + Vector3.up * verticalRadius;
        }

        static Bounds BoundsOf(GameObject subject)
        {
            var renderers = subject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(subject.transform.position, Vector3.one * 0.1f);

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static void SetLayer(Transform node)
        {
            node.gameObject.layer = TurntableLayer;
            for (var i = 0; i < node.childCount; i++)
                SetLayer(node.GetChild(i));
        }

        void HideFromWorldCameras()
        {
            foreach (var other in Camera.allCameras)
            {
                var ours = false;
                for (var i = 0; i < previews.Count; i++)
                    if (previews[i].Camera == other)
                    {
                        ours = true;
                        break;
                    }
                if (!ours)
                    other.cullingMask &= ~(1 << TurntableLayer);
            }
        }

        void Strike()
        {
            for (var i = previews.Count - 1; i >= 0; i--)
                Release(previews[i]);
            previews.Clear();
        }

        static void Release(Preview preview)
        {
            if (preview.Camera)
                preview.Camera.targetTexture = null;
            if (preview.Texture)
            {
                preview.Texture.Release();
                Destroy(preview.Texture);
            }
            if (preview.Rig)
                Destroy(preview.Rig);
        }
    }
}
