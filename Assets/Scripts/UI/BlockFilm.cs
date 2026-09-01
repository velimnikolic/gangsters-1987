using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LivingCity.UI
{
    /// <summary>
    /// The ledger's second camera: one lens that hangs over a block of the REAL city and
    /// films it into a texture the block file shows.
    ///
    /// It photographs nothing of its own. There is no stand, no stunt double and no
    /// rebuilt copy of the block - the lens is put over the ground the file is about and
    /// what comes back is the pavement, the buildings, the people walking on it, the cars
    /// at the kerb and the hour of the day, exactly as the street has them. A block whose
    /// look changes on the street changes in the book the same second.
    ///
    /// It films ONE block, not the city round it. The lens stands at the city's own
    /// street pitch and closes its near and far planes onto the block's own volume, so
    /// the ground in front of it and the city behind it are never exposed at all; the
    /// picture is then drawn only inside the block's silhouette - see
    /// <see cref="BlockFilmView"/>, which takes <see cref="Volume"/> for the shape.
    ///
    /// Cheap on purpose: no shadows, no post, no anti-aliasing, and the camera is
    /// switched OFF the moment the file closes. It also never renders a frame while the
    /// city has that ground put away - see <see cref="RoadDemo.CityBlockRecycler"/>,
    /// which the file asks to hold the block up while it is open.
    /// </summary>
    public sealed class BlockFilm : MonoBehaviour
    {
        static BlockFilm instance;

        Camera lens;
        RenderTexture frame;

        /// <summary>Everything the street shows, and none of the private layers: the
        /// book's own canvas, the strategic map's ambient cull (30) and the portrait
        /// studio (31) must never appear over a block.</summary>
        const int Hidden = (1 << 5) | (1 << 30) | (1 << 31);

        /// <summary>Vertical angle of view. Narrow, because a wide lens on a city block
        /// bends the street: this is a survey photograph, not a fish-eye.</summary>
        const float FieldOfView = 26f;

        /// <summary>Air left round the block inside the frame.</summary>
        const float Margin = 1.16f;

        /// <summary>How far past its own plot a block is still itself: the kerb, the
        /// pavement and the parked car against it. Cut on the plot line alone and the
        /// block floats in the air with its pavement left behind in the street.</summary>
        const float Kerb = 6f;

        /// <summary>Assumed rise where nothing is standing yet, so a block the city has
        /// only just streamed back in is not cut off at its own eaves.</summary>
        const float BareRise = 14f;

        /// <summary>The block's own volume in world metres - plot, kerb and rooflines.
        /// The picture is cut to this and to nothing else.</summary>
        public Bounds Volume { get; private set; }

        static RoadDemo.DemoCamera streetCamera;
        static bool streetCameraSought;

        /// <summary>The angle the city itself is seen at. The book does not invent a
        /// second isometric: the block stands in it exactly as it stands under the
        /// player's own camera, which is why there is no tilt to drag.</summary>
        public static float CityPitch
        {
            get
            {
                if (!streetCameraSought)
                {
                    streetCamera = FindFirstObjectByType<RoadDemo.DemoCamera>();
                    streetCameraSought = true;
                }
                return streetCamera != null
                    ? Mathf.Clamp(streetCamera.pitch,
                        RoadDemo.CityViewConfig.MinimumStreetPitch,
                        RoadDemo.CityViewConfig.MaximumStreetPitch)
                    : RoadDemo.CityViewConfig.DefaultStreetPitch;
            }
        }

        /// <summary>What the file is looking at, so the caller can ask whether the lens
        /// is already over the right ground.</summary>
        public Rect Ground { get; private set; }

        public Camera Lens => lens;
        public RenderTexture Frame => frame;

        /// <summary>Puts the lens away WITHOUT standing a rig up to do it - the closing
        /// path must not build a camera it is about to switch off.</summary>
        public static void StopIfRunning()
        {
            if (instance != null)
                instance.Stop();
        }

        public static BlockFilm Get()
        {
            if (instance != null)
                return instance;
            var go = new GameObject("Block Film");
            go.hideFlags = HideFlags.DontSave;
            instance = go.AddComponent<BlockFilm>();
            instance.Build();
            return instance;
        }

        void Build()
        {
            lens = gameObject.AddComponent<Camera>();
            lens.fieldOfView = FieldOfView;
            lens.cullingMask = ~Hidden;
            lens.clearFlags = CameraClearFlags.SolidColor;
            lens.backgroundColor = new Color(0.141f, 0.118f, 0.102f);
            lens.nearClipPlane = 0.4f;
            // Solved per block in Look: a lens that reached across the city would film
            // the whole of it a second time every frame for one block's sake.
            lens.farClipPlane = 400f;
            // Under every other camera in the project, so nothing of this ever lands on
            // the player's screen even for a frame.
            lens.depth = -80f;
            lens.enabled = false;

            var data = lens.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.antialiasing = AntialiasingMode.None;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
            }
        }

        /// <summary>Makes (or remakes) the film the lens exposes onto. The book's plate
        /// is very wide and its pixel size follows the window, so the texture follows the
        /// plate rather than a constant nobody can see.</summary>
        public RenderTexture Reel(int width, int height)
        {
            // The plate is very wide, so a ceiling on the long side has to take the short
            // side down with it: a negative clamped on width alone comes back at the
            // wrong shape and the street leans.
            var aspect = height > 0 ? (float)width / height : 1f;
            if (width > 2048)
            {
                width = 2048;
                height = Mathf.RoundToInt(width / Mathf.Max(0.05f, aspect));
            }
            width = Mathf.Clamp(width, 64, 2048);
            height = Mathf.Clamp(height, 32, 1024);
            if (frame != null && frame.width == width && frame.height == height)
                return frame;

            if (frame != null)
            {
                lens.targetTexture = null;
                frame.Release();
                Destroy(frame);
            }
            frame = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Block Film",
                antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp,
            };
            lens.targetTexture = frame;
            return frame;
        }

        /// <summary>
        /// Puts the lens over one block. YAW turns it round; the angle off the ground is
        /// the city's own street pitch and is not negotiable here - the book shows the
        /// isometric the player already reads the city in.
        ///
        /// The distance is solved from the block's own volume, so a long quay block and a
        /// small corner one both fill the plate, and the near and far planes are then
        /// closed onto that volume: the street in front of the block and the city behind
        /// it fall outside the frustum and are never drawn.
        /// </summary>
        public void Look(Rect groundWorld, float groundY, float yaw, float rise)
        {
            Ground = groundWorld;

            var plot = Rect.MinMaxRect(
                groundWorld.xMin - Kerb, groundWorld.yMin - Kerb,
                groundWorld.xMax + Kerb, groundWorld.yMax + Kerb);
            var height = Mathf.Max(BareRise, rise);
            var centre = new Vector3(plot.center.x, groundY + height * 0.5f, plot.center.y);
            Volume = new Bounds(centre, new Vector3(plot.width, height, plot.height));

            var radius = 0.5f * Mathf.Sqrt(
                plot.width * plot.width + plot.height * plot.height + height * height);

            var aspect = frame != null && frame.height > 0
                ? (float)frame.width / frame.height
                : 1f;
            var halfV = FieldOfView * 0.5f * Mathf.Deg2Rad;
            var halfH = Mathf.Atan(Mathf.Tan(halfV) * aspect);
            var distance = Mathf.Max(radius / Mathf.Tan(halfV), radius / Mathf.Tan(halfH)) *
                           Margin;

            var turn = Quaternion.Euler(CityPitch, yaw, 0f);
            transform.SetPositionAndRotation(
                centre - turn * Vector3.forward * distance, turn);

            // The slab. Everything nearer than the block's front corner and everything
            // further than its back one is another block's business.
            lens.farClipPlane = distance + radius + 2f;
            lens.nearClipPlane = Mathf.Max(0.3f, distance - radius - 2f);
            lens.enabled = true;
        }

        /// <summary>Switches the lens off. Called the moment the file closes, so an open
        /// book is the only thing that ever costs a second render of the city.</summary>
        public void Stop()
        {
            if (lens != null)
                lens.enabled = false;
        }

        /// <summary>What is standing under a point of the picture, in the picture's own
        /// 0..1 coordinates. The ray goes into the REAL city, so what comes back is the
        /// actual collider the player would have clicked in the street.</summary>
        public bool TryPick(Vector2 viewport, out RaycastHit hit)
        {
            hit = default;
            if (lens == null || !lens.enabled)
                return false;
            if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                return false;
            var ray = lens.ViewportPointToRay(viewport);
            return Physics.Raycast(ray, out hit, 900f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Where a world point lands in the picture, 0..1. Used to hang a
        /// premise's ownership mark over its own door rather than over a guess.</summary>
        public bool TryPlace(Vector3 world, out Vector2 viewport)
        {
            viewport = default;
            if (lens == null)
                return false;
            var point = lens.WorldToViewportPoint(world);
            if (point.z <= 0f)
                return false;
            viewport = new Vector2(point.x, point.y);
            return true;
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
            if (frame == null)
                return;
            if (lens != null)
                lens.targetTexture = null;
            frame.Release();
            Destroy(frame);
            frame = null;
        }
    }
}
