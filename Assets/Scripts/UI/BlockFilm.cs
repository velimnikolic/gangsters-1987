using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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
    /// the ground in front of it and the city behind it are never exposed at all. The
    /// empty stage around it fills the whole block-file plate, letting the camera's grade
    /// and vignette span the full element instead of ending at the block's fitted box.
    ///
    /// Bounded on purpose: shadows and the city grade run only while the file is open,
    /// the render texture matches the plate's visible pixel size, and the camera is
    /// switched OFF the moment the file closes. It also never renders while the city has
    /// that ground put away - see <see cref="RoadDemo.CityBlockRecycler"/>, which the file
    /// asks to hold the block up while it is open.
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

        /// <summary>How far clear of the block the lens stands. The lens is ORTHOGRAPHIC,
        /// so the standoff buys nothing but depth clearance and costs nothing to keep
        /// short - and a short one is the whole point. A perspective lens narrow enough
        /// not to bend the street had to stand a quarter of a kilometre back, and URP
        /// culls shadow casters by distance FROM THE CAMERA: at that range the block sat
        /// outside the pipeline's shadow volume and could not have a shadow on it even
        /// with shadows switched on. Eight metres clear of the block's own sphere puts
        /// the whole of it inside a hundred-odd metres of range.</summary>
        const float Standoff = 8f;

        /// <summary>Air left round the block inside the frame. None: the plate is cut to
        /// the block's own volume, and every per cent of margin here is a per cent of the
        /// picture spent on the road the block stands next to.</summary>
        const float Margin = 1f;

        /// <summary>
        /// How far past its own bounds a block is still itself. Almost nothing, because
        /// the block's bounds ALREADY END AT THE KERB. Measured on core block
        /// -22:15:20:18 (100 x 90 m): inside its bounds are 149 sidewalk pieces and no
        /// road markings at all; in the six metres outside are 46 yellow-line pieces and
        /// 8 crossings. The old six-metre skirt was not the pavement, it was the
        /// carriageway - the picture is of a block, not of the street round it.
        ///
        /// What is left is for the kerb stone itself, which straddles the line.
        /// </summary>
        const float Kerb = 0.6f;

        /// <summary>Assumed rise where nothing is standing yet, so a block the city has
        /// only just streamed back in is not cut off at its own eaves.</summary>
        const float BareRise = 14f;

        // ------------------------------------------------- three in the afternoon
        //
        // A block file is a SURVEY PHOTOGRAPH. Two readings of the same ground taken
        // at different hours have to be comparable, and a block photographed at two
        // in the morning is a black rectangle with some lit windows in it - which
        // says nothing about the ground and everything about the clock. So the plate
        // is always shot at three in the afternoon, whatever the city's own hour.
        //
        // The angle and the colour are the city's own sun at that hour, taken off
        // DemoSky's day: sunrise at 6 and sunset at 20 put 15:00 at t = 9/14, so the
        // elevation is sin(t*pi) * 62 and the azimuth runs -75 to 75 across the same
        // t. The sun stands still in the WORLD, not behind the reader, so turning the
        // block moves the light round it the way it would on the street.
        const float AfternoonElevation = 55.9f;
        const float AfternoonAzimuth = 21.4f;
        static readonly Color AfternoonSun = new Color(1f, 0.916f, 0.772f);
        const float AfternoonIntensity = 0.98f;

        // Ambient exactly as the city grades a day, so the shaded side of a wall on
        // the plate is the shaded side of the same wall in the street.
        static readonly Color AfternoonAmbientSky = new Color(0.40f, 0.48f, 0.60f);
        static readonly Color AfternoonAmbientEquator = new Color(0.31f, 0.33f, 0.36f);
        static readonly Color AfternoonAmbientGround = new Color(0.17f, 0.16f, 0.15f);

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

        // The lens is a still camera. These fields distinguish a repaint of the paper
        // around the plate from a shot that actually changed and must be exposed again.
        bool shotValid;
        bool frameChanged = true;
        Rect shotGround;
        float shotGroundY;
        float shotYaw;
        float shotRise;
        bool waitingForStream;

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

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OpenShutter;
            RenderPipelineManager.endCameraRendering += CloseShutter;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OpenShutter;
            RenderPipelineManager.endCameraRendering -= CloseShutter;
            StrikeStage();
            RestoreShadowRange();
        }

        void Build()
        {
            lens = gameObject.AddComponent<Camera>();
            // Orthographic, because the city itself is: the block now stands in the plate
            // in exactly the projection the player reads the street in, with no
            // perspective bend to correct for and no long lens to correct it with.
            lens.orthographic = true;
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
                // The block is graded and lit the way the street is. Post gives it the
                // city's own tone rather than a raw buffer; shadows give it the shape a
                // flat-lit model does not have. Both cost a pass, and both are only ever
                // paid while a block file is actually open - the lens is disabled the
                // moment it shuts.
                data.renderPostProcessing = true;
                data.renderShadows = true;
                // The plate is already rendered at its visible pixel size; it does not
                // pay for a second antialiasing-sized buffer.
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
            // A very large game window can still run past the texture ceiling. Both sides
            // are brought down TOGETHER by one factor: a negative clamped on either side
            // alone comes back at the wrong shape and the street leans.
            const int MaxWidth = 4096;
            const int MaxHeight = 2048;
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            var fit = Mathf.Min(1f,
                Mathf.Min(MaxWidth / (float)width, MaxHeight / (float)height));
            width = Mathf.Clamp(Mathf.RoundToInt(width * fit), 64, MaxWidth);
            height = Mathf.Clamp(Mathf.RoundToInt(height * fit), 32, MaxHeight);
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
                // Keep fractional canvas scaling smooth when a plate lands between pixels.
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            lens.targetTexture = frame;
            frameChanged = true;
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
            if (shotValid && !frameChanged && stage != null &&
                Same(shotGround, groundWorld) &&
                Mathf.Approximately(shotGroundY, groundY) &&
                Mathf.Approximately(shotYaw, yaw) &&
                Mathf.Approximately(shotRise, rise))
                return;

            Ground = groundWorld;

            var plot = Rect.MinMaxRect(
                groundWorld.xMin - Kerb, groundWorld.yMin - Kerb,
                groundWorld.xMax + Kerb, groundWorld.yMax + Kerb);
            var height = Mathf.Max(BareRise, rise);
            // The block is stood up on the stage first, because everything below aims at
            // the copy standing down there and not at the ground it was copied from.
            RaiseStage(plot);
            var centre = new Vector3(plot.center.x, groundY + height * 0.5f, plot.center.y)
                         + StageOffset;
            Volume = new Bounds(centre, new Vector3(plot.width, height, plot.height));

            var radius = 0.5f * Mathf.Sqrt(
                plot.width * plot.width + plot.height * plot.height + height * height);

            var turn = Quaternion.Euler(CityPitch, yaw, 0f);

            // The block's own BOX, measured on the plate's two axes. A sphere round it
            // leaves the corners of a long quay block as street on every side; the box is
            // what is actually being photographed.
            var extents = Volume.extents;
            var plate = Extents(extents, yaw);
            var aspect = frame != null && frame.height > 0
                ? (float)frame.width / frame.height
                : 1f;

            // CONTAIN the whole block in the full-width plate. The stage is empty outside
            // this box, so the extra horizontal room is only the film's own clear colour;
            // no street can enter it. Max preserves the block's proportions and gives it
            // exactly the same fitted size whether the plate is wider or taller than it.
            lens.orthographicSize =
                Mathf.Max(plate.y, plate.x / Mathf.Max(0.05f, aspect)) * Margin;

            // Orthographic, so the standoff buys depth clearance and nothing else: the
            // lens stands just clear of the block instead of a quarter of a kilometre
            // back, which is what brings the whole of it inside the pipeline's shadow
            // range and lets the block cast on itself.
            var distance = radius + Standoff;
            transform.SetPositionAndRotation(
                centre - turn * Vector3.forward * distance, turn);

            // The slab, cut to the BOX and not to a sphere round it. This is what keeps
            // the road out of the picture, and the sphere could not do it: a sphere round
            // a hundred-metre block stands eight metres proud of the block's own back
            // corner, and eight metres past the back corner is the far carriageway.
            //
            // The block's deepest point is a bottom-far corner - looking down at the
            // city's pitch, the higher a point is the NEARER it is - so the depth of that
            // corner is exactly |forward . extents|, and a far plane there clips the
            // street beyond the block while keeping every roof on it.
            //
            // Without this the silhouette lets the far street through: the hull is the
            // union of the ground quad and the roof quad, the roof quad is offset up the
            // screen, and the band between the two is the road behind the block.
            var forward = turn * Vector3.forward;
            var halfDeep = Mathf.Abs(forward.x) * extents.x +
                           Mathf.Abs(forward.y) * extents.y +
                           Mathf.Abs(forward.z) * extents.z;
            // A quarter of a metre, so the kerb standing exactly on the plane is not
            // clipped by half a texel of rounding.
            lens.farClipPlane = distance + halfDeep + 0.25f;
            lens.nearClipPlane = Mathf.Max(0.05f, distance - halfDeep - 0.25f);
            FitShadowRange(lens.farClipPlane);
            GatherDirectionals();
            shotGround = groundWorld;
            shotGroundY = groundY;
            shotYaw = yaw;
            shotRise = rise;
            shotValid = true;
            frameChanged = false;
            // A held residential block may still be composing or attaching when its file
            // first opens. Keep the first quick exposure, then replace it once with the
            // complete block; never leave that partial startup frame frozen on the page.
            waitingForStream = !RoadDemo.CityBlockRecycler.HeldReady(groundWorld);
            // One enabled frame is one exposure. CloseShutter switches the lens back off
            // after URP has filled the texture.
            lens.enabled = true;
        }

        static bool Same(Rect a, Rect b) =>
            Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y) &&
            Mathf.Approximately(a.width, b.width) && Mathf.Approximately(a.height, b.height);

        void Update()
        {
            if (!waitingForStream ||
                !RoadDemo.CityBlockRecycler.HeldReady(shotGround))
                return;

            // RaiseStage deliberately reuses a stage for an unchanged rectangle. This is
            // the one time it must not: the old stage was copied while the recycler was
            // still attaching the block, so rebuild it from the now-complete view and take
            // one final exposure.
            waitingForStream = false;
            StrikeStage();
            shotValid = false;
            Look(shotGround, shotGroundY, shotYaw, shotRise);
        }

        /// <summary>What the pipeline's shadow range was before this lens widened it, or
        /// -1 when it has not been touched.</summary>
        float authoredShadowRange = -1f;

        /// <summary>
        /// URP takes the shadow range off the PIPELINE ASSET and caps it at the camera's
        /// far plane, so the film's own slab is normally what decides how far shadows
        /// reach on a block - and where the city's isometric rig is running, that asset
        /// already carries hundreds of metres and nothing here writes anything.
        ///
        /// Where no rig is running - a scene opened in the editor, a harness - the asset
        /// still holds its authored fifty, and fifty metres across a block eighty metres
        /// deep is a hard shadow line drawn across the middle of the picture. So the film
        /// raises it, only when it is short, and puts it back when the lens goes off.
        ///
        /// It never lowers the range and it never fights the rig: if the value has moved
        /// since it was written, somebody else is managing it and the film drops its
        /// claim rather than overwriting theirs.
        /// </summary>
        void FitShadowRange(float needed)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null || urp.shadowDistance >= needed)
                return;
            if (authoredShadowRange < 0f)
                authoredShadowRange = urp.shadowDistance;
            urp.shadowDistance = needed;
            wroteShadowRange = needed;
        }

        float wroteShadowRange;

        void RestoreShadowRange()
        {
            if (authoredShadowRange < 0f)
                return;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null && Mathf.Approximately(urp.shadowDistance, wroteShadowRange))
                urp.shadowDistance = authoredShadowRange;
            authoredShadowRange = -1f;
        }

        /// <summary>
        /// How wide and how tall, in metres, the picture has to be to hold this block at
        /// this angle. The full-width film uses this to contain the block without changing
        /// its proportions; the remaining frame is the empty stage, never the street.
        /// </summary>
        public static Vector2 PlateExtents(Rect groundWorld, float rise, float yaw)
        {
            var plot = Rect.MinMaxRect(
                groundWorld.xMin - Kerb, groundWorld.yMin - Kerb,
                groundWorld.xMax + Kerb, groundWorld.yMax + Kerb);
            var height = Mathf.Max(BareRise, rise);
            return Extents(
                new Vector3(plot.width * 0.5f, height * 0.5f, plot.height * 0.5f), yaw);
        }

        static Vector2 Extents(Vector3 extents, float yaw)
        {
            var turn = Quaternion.Euler(CityPitch, yaw, 0f);
            var up = turn * Vector3.up;
            var right = turn * Vector3.right;
            return new Vector2(
                Mathf.Abs(right.x) * extents.x + Mathf.Abs(right.y) * extents.y +
                Mathf.Abs(right.z) * extents.z,
                Mathf.Abs(up.x) * extents.x + Mathf.Abs(up.y) * extents.y +
                Mathf.Abs(up.z) * extents.z);
        }

        // ------------------------------------------------------------- the shutter

        readonly List<Light> dimmed = new List<Light>();

        /// <summary>Every directional in the scene, gathered when the lens is pointed.
        /// There are two of them - a sun and a moon - and finding that out is a scene
        /// sweep, so it is not done on the frames the shot is actually taken.</summary>
        readonly List<Light> directionals = new List<Light>();

        Light key;
        Quaternion keyRotation;
        Color keyColour;
        float keyIntensity;
        LightShadows keyShadows;
        bool keyEnabled;

        AmbientMode heldAmbientMode;
        Color heldAmbientSky, heldAmbientEquator, heldAmbientGround;
        float heldAmbientIntensity;
        bool heldFog;
        bool shutterOpen;

        /// <summary>
        /// Puts the city at three in the afternoon for the length of ONE camera's render
        /// and nothing else. The block's own lens has depth -80, so it draws before every
        /// other camera in the frame; the hour is put back the moment it is done and the
        /// player's view is rendered from the city's real clock as always.
        ///
        /// Written here rather than asked of the sky, because a scene may have DemoSky,
        /// CityWeather or neither, and a survey photograph has to come out the same in all
        /// three. The angle and the colours are the city's own day - see the constants.
        /// </summary>
        void OpenShutter(ScriptableRenderContext context, Camera camera)
        {
            if (camera != lens || lens == null || !lens.enabled || shutterOpen)
                return;
            shutterOpen = true;

            heldAmbientMode = RenderSettings.ambientMode;
            heldAmbientSky = RenderSettings.ambientSkyColor;
            heldAmbientEquator = RenderSettings.ambientEquatorColor;
            heldAmbientGround = RenderSettings.ambientGroundColor;
            heldAmbientIntensity = RenderSettings.ambientIntensity;
            heldFog = RenderSettings.fog;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AfternoonAmbientSky;
            RenderSettings.ambientEquatorColor = AfternoonAmbientEquator;
            RenderSettings.ambientGroundColor = AfternoonAmbientGround;
            RenderSettings.ambientIntensity = 1f;
            // The plate looks at one block over a hundred metres of slab. Haze over that
            // distance is nothing by day and a blue wash at night, so the picture has
            // none: what tints a block on this sheet is the block, not the weather.
            RenderSettings.fog = false;

            // The sun, and every other directional in the scene put out with it - the
            // moon most of all, which is a second key light on a night the plate does
            // not have. The list is gathered when the lens is pointed, not here: this
            // runs on every frame the file is open, and a scene-wide light sweep twice
            // a frame is not a thing a photograph should cost.
            key = RenderSettings.sun;
            dimmed.Clear();
            for (var i = 0; i < directionals.Count; i++)
            {
                var light = directionals[i];
                if (light == null)
                    continue;
                if (key == null || (key != light && light.intensity > key.intensity &&
                                    RenderSettings.sun == null))
                    key = light;
            }
            for (var i = 0; i < directionals.Count; i++)
                if (directionals[i] != null && directionals[i] != key)
                    Put(directionals[i]);

            if (key == null)
                return;
            keyRotation = key.transform.rotation;
            keyColour = key.color;
            keyIntensity = key.intensity;
            keyShadows = key.shadows;
            keyEnabled = key.enabled;

            key.transform.rotation =
                Quaternion.Euler(AfternoonElevation, AfternoonAzimuth, 0f);
            key.color = AfternoonSun;
            key.intensity = AfternoonIntensity;
            key.shadows = LightShadows.Soft;
            key.enabled = true;
        }

        // ------------------------------------------------------------- the stage
        //
        // The block is not photographed where it stands. It is BUILT AGAIN, by itself,
        // a long way under the city, and the lens is pointed at that.
        //
        // Every earlier attempt tried to keep the road out of a picture taken in the
        // middle of the city - by framing, by a depth plane, by a silhouette, by a list
        // of what was road and finally by a list of what was not the block. They all
        // fail on the same shape: the block is a rotated rectangle inside a frustum that
        // is not, and the wedges between the two are street. Every one of them was a
        // rule about what to leave out, and a rule like that is only ever as good as its
        // list, which is why turning the block kept finding the part it forgot.
        //
        // On the stage there is no list. There is nothing down there to leave out. No
        // angle can bring the road back into the picture because the road was never
        // built next to what the lens is looking at.
        //
        // What is lost is the life on the block - the people and the traffic are the
        // city's own and stay in it, so the stage is a still of the ground and what
        // stands on it. That is what a survey photograph is anyway.

        /// <summary>Where the block is stood up to be photographed: far below the city,
        /// where nothing is. Straight down, so the block keeps its own X and Z and every
        /// world position on it moves onto the stage by adding one vector.</summary>
        static readonly Vector3 StageBelow = new Vector3(0f, -5000f, 0f);

        Transform stage;
        Rect staged;

        /// <summary>Zero until a block is standing on the stage, and the drop to it
        /// after. Every world position the file holds - a door, a shopfront - is in the
        /// city's coordinates, so this is what puts it in front of the lens.</summary>
        public Vector3 StageOffset { get; private set; }

        /// <summary>Each piece on the stage against the piece in the city it was copied
        /// from, so a ray that lands on the stage can still answer with the premise the
        /// city knows.</summary>
        readonly Dictionary<Transform, Transform> cityOf =
            new Dictionary<Transform, Transform>();

        public Transform Original(Transform onStage) =>
            onStage != null && cityOf.TryGetValue(onStage, out var real) ? real : null;

        /// <summary>
        /// Stands the block up on the stage. The city is swept once for what is standing
        /// on this block's ground - the same geometric rule as ever, but now it decides
        /// what to COPY rather than what to hide, so anything it misses is a missing
        /// prop and never a road in the frame.
        ///
        /// The copies are made under a root that is switched OFF, which is the only safe
        /// way to copy a piece of a living city: a component that woke up down here would
        /// register a second shopfront, a second obstacle, a second marker. Nothing wakes.
        /// The scripts are taken off while it is still dark and only then is it lit.
        /// </summary>
        void RaiseStage(Rect ground)
        {
            if (stage != null && staged == ground)
                return;
            StrikeStage();
            staged = ground;

            var root = new GameObject("Block Stage") { hideFlags = HideFlags.DontSave };
            root.SetActive(false);
            stage = root.transform;
            stage.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            StageOffset = StageBelow;

            // Inactive included, and the renderer's own enabled flag ignored, because
            // THE BLOCK IS SWITCHED OFF. The city merges its geometry into spatial
            // chunks - Merged/Chunk 105003 and its like - and a chunk is bigger than a
            // block: one over this block measures 120 x 140 metres against its 100 x 90.
            // The merge disables the pieces it swallowed, so reading only what is drawing
            // finds the chunks and never the block, and a chunk brings the street it
            // covers along with it. The authored pieces are still standing under the
            // block's own root; those are what is copied, and the copies are switched
            // back on down on the stage.
            var world = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var onBlock = new HashSet<Transform>();
            for (var i = 0; i < world.Length; i++)
            {
                var piece = world[i];
                if (piece == null || IsMerged(piece.transform) ||
                    LivingCity.Gameplay.PlayerOcclusionHider.IsOcclusionArtifact(piece) ||
                    !piece.gameObject.activeInHierarchy)
                    continue;
                var centre = piece.bounds.center;
                if (centre.x < ground.xMin || centre.x > ground.xMax ||
                    centre.z < ground.yMin || centre.z > ground.yMax)
                    continue;
                onBlock.Add(piece.transform);
            }

            // Rise to the highest thing that is ENTIRELY this block - the block's own
            // root, where the city has one - so the stage is built out of a handful of
            // copies instead of fifteen hundred. An ancestor qualifies when everything
            // under it stands within the block: bounds CONTAIN, so a root that fits
            // inside the block's ground cannot be hiding a piece of street in it.
            //
            // The allowance is for what a block hangs over its own kerb - an awning, a
            // sign, a fire escape - which is the block's and belongs in the picture.
            const float Overhang = 4f;
            var room = Rect.MinMaxRect(
                ground.xMin - Overhang, ground.yMin - Overhang,
                ground.xMax + Overhang, ground.yMax + Overhang);
            var whole = new HashSet<Transform>();
            var reach = new Dictionary<Transform, Bounds>();
            foreach (var piece in onBlock)
            {
                var top = piece;
                for (var up = piece.parent; up != null && up != stage; up = up.parent)
                {
                    if (!reach.TryGetValue(up, out var box))
                    {
                        var any = false;
                        box = new Bounds(up.position, Vector3.zero);
                        var kids = up.GetComponentsInChildren<Renderer>(true);
                        for (var i = 0; i < kids.Length; i++)
                        {
                            // A piece on a switched-off object reports a box nobody can
                            // use - often one anchored at the world origin - and one of
                            // those in the union stretches it across half the city and
                            // stops the copy rising at all.
                            if (kids[i] == null || IsMerged(kids[i].transform) ||
                                !kids[i].gameObject.activeInHierarchy)
                                continue;
                            if (!any) { box = kids[i].bounds; any = true; }
                            else box.Encapsulate(kids[i].bounds);
                        }
                        if (!any)
                            box = new Bounds(up.position, new Vector3(1e9f, 0f, 1e9f));
                        reach[up] = box;
                    }
                    if (box.min.x < room.xMin || box.max.x > room.xMax ||
                        box.min.z < room.yMin || box.max.z > room.yMax)
                        break;
                    top = up;
                }
                whole.Add(top);
            }

            // A piece already inside something being copied is not copied again.
            foreach (var piece in whole)
            {
                var covered = false;
                for (var up = piece.parent; up != null && !covered; up = up.parent)
                    covered = whole.Contains(up);
                if (!covered)
                    Copy(piece.gameObject);
            }

            Strip(root);
            RestoreStageRendering(root);
            Light(root);
            root.SetActive(true);
        }

        /// <summary>The street may currently be looking through one of these buildings.
        /// Its staged ledger copy is not: restore only the COPY to the renderer state it
        /// had before either cutaway system touched the live city.</summary>
        void RestoreStageRendering(GameObject root)
        {
            var copies = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < copies.Length; i++)
            {
                var copy = copies[i];
                if (copy == null || !cityOf.TryGetValue(copy.transform, out var real) ||
                    real == null)
                    continue;
                var source = real.GetComponent<Renderer>();
                if (source == null)
                    continue;

                RoadDemo.BuildingCutaway.RestoreUncutCopy(source, copy);
                if (source is MeshRenderer sourceMesh && copy is MeshRenderer copyMesh &&
                    LivingCity.Gameplay.PlayerOcclusionHider.TryOriginalShadowMode(
                        sourceMesh, out var original))
                    copyMesh.shadowCastingMode = original;
            }
        }

        /// <summary>Whether a piece is the city's merged output rather than the geometry
        /// it was merged from. The merge hangs its chunks off a root of their own.</summary>
        static bool IsMerged(Transform piece)
        {
            var root = piece.root;
            return root != null && root.name == "Merged";
        }

        void Copy(GameObject piece)
        {
            var from = piece.transform;
            var copy = Instantiate(piece, from.position + StageBelow, from.rotation, stage);
            copy.name = piece.name;
            copy.transform.localScale = from.lossyScale;
            Twin(copy.transform, from);
        }

        /// <summary>Walks a copy and its original together. Instantiate keeps the order of
        /// the children, so the two hierarchies are the same shape and can be paired off
        /// in one pass.</summary>
        void Twin(Transform copy, Transform real)
        {
            cityOf[copy] = real;
            var count = Mathf.Min(copy.childCount, real.childCount);
            for (var i = 0; i < count; i++)
                Twin(copy.GetChild(i), real.GetChild(i));
        }

        /// <summary>Takes everything off the copies that is not the picture. The scripts
        /// go because a second copy of a shopfront must never speak to the city; the
        /// lights go because the stage is lit at three in the afternoon and a street lamp
        /// has no business burning in it.</summary>
        static void Strip(GameObject root)
        {
            var scripts = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (var i = 0; i < scripts.Length; i++)
                if (scripts[i] != null)
                    DestroyImmediate(scripts[i]);
            var lamps = root.GetComponentsInChildren<Light>(true);
            for (var i = 0; i < lamps.Length; i++)
                if (lamps[i] != null)
                    DestroyImmediate(lamps[i]);
        }

        /// <summary>Switches the copies on. The pieces they were made from are dark -
        /// the city's merge turned them off when it swallowed them - so a copy that
        /// inherited that would be a stage with nothing standing on it.</summary>
        static void Light(GameObject root)
        {
            var pieces = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < pieces.Length; i++)
                if (pieces[i] != null)
                {
                    // The RENDERER goes back on and nothing else. The city switches
                    // renderers off when it merges them; anything whose whole object it
                    // switched off - a spare level of detail, a fallback shell - it meant
                    // to be gone, and the stage is not the place to overrule that.
                    pieces[i].enabled = true;
                    pieces[i].forceRenderingOff = false;
                }
        }

        void StrikeStage()
        {
            cityOf.Clear();
            staged = default;
            StageOffset = Vector3.zero;
            if (stage == null)
                return;
            if (Application.isPlaying)
                Destroy(stage.gameObject);
            else
                DestroyImmediate(stage.gameObject);
            stage = null;
        }

        void GatherDirectionals()
        {
            directionals.Clear();
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (var i = 0; i < lights.Length; i++)
                if (lights[i] != null && lights[i].type == LightType.Directional)
                    directionals.Add(lights[i]);
        }

        /// <summary>Puts one directional out for the length of the shot, remembering
        /// whether it was on so it can be given back exactly as it was.</summary>
        void Put(Light light)
        {
            if (!light.enabled)
                return;
            light.enabled = false;
            dimmed.Add(light);
        }

        void CloseShutter(ScriptableRenderContext context, Camera camera)
        {
            if (camera != lens || !shutterOpen)
                return;
            shutterOpen = false;
            // This RenderTexture is a still. The next Look call enables the lens only if
            // the block, angle, rise or visible texture size has actually changed.
            if (lens != null)
                lens.enabled = false;

            RenderSettings.ambientMode = heldAmbientMode;
            RenderSettings.ambientSkyColor = heldAmbientSky;
            RenderSettings.ambientEquatorColor = heldAmbientEquator;
            RenderSettings.ambientGroundColor = heldAmbientGround;
            RenderSettings.ambientIntensity = heldAmbientIntensity;
            RenderSettings.fog = heldFog;

            for (var i = 0; i < dimmed.Count; i++)
                if (dimmed[i] != null)
                    dimmed[i].enabled = true;
            dimmed.Clear();

            if (key != null)
            {
                key.transform.rotation = keyRotation;
                key.color = keyColour;
                key.intensity = keyIntensity;
                key.shadows = keyShadows;
                key.enabled = keyEnabled;
            }
            key = null;
            RestoreShadowRange();
        }

        /// <summary>Switches the lens off. Called the moment the file closes, so an open
        /// book is the only thing that ever costs a second render of the city.</summary>
        public void Stop()
        {
            if (lens != null)
                lens.enabled = false;
            // A shot that never closed its shutter - the book shut mid-frame - must not
            // leave the city standing at three in the afternoon.
            if (shutterOpen)
                CloseShutter(default, lens);
            StrikeStage();
            RestoreShadowRange();
            shotValid = false;
            frameChanged = true;
            waitingForStream = false;
        }

        /// <summary>What is standing under a point of the picture, in the picture's own
        /// 0..1 coordinates. The ray goes into the STAGE, where the block is actually
        /// standing, so what comes back is a copy: ask <see cref="Original"/> for the
        /// piece of the city it was made from before reading anything off it.</summary>
        public bool TryPick(Vector2 viewport, out RaycastHit hit)
        {
            hit = default;
            if (lens == null || stage == null)
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
            var point = lens.WorldToViewportPoint(world + StageOffset);
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
