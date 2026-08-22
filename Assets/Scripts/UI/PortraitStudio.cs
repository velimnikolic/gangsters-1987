using System;
using System.Collections.Generic;
using LivingCity.Data;
using LivingCity.Generation;
using UnityEngine;
using UnityEngine.UI;

namespace LivingCity.UI
{
    /// <summary>
    /// The ledger's photographer: a hidden rig far off the map that photographs the SAME
    /// prefabs the city fields - a member's street model for the mugshot, the armory's
    /// guns and cars for the catalogue - into cached textures a RawImage shows.
    ///
    /// One camera, one job per frame: Stage instantiates the subject on the stand and
    /// enables the camera; next LateUpdate the RenderTexture holds the exposure, Develop
    /// reads it into a cached Texture2D, hands it to every waiting RawImage and strikes
    /// the set. Consumers therefore call Request and forget - the print lands a frame or
    /// two later, or instantly when the subject was photographed before. A null prefab
    /// is a no-op: the caller's placeholder (the mugshot initials, an empty slot) simply
    /// stays.
    ///
    /// The studio must not leak into the world, in either direction:
    /// - It lives on layer 31 (unnamed and unused; 30 is the strategic map's ambient
    ///   cull), every other camera gets that bit stripped at each stage, and the rig
    ///   also sits 2.5km off the map corner - outside every frustum the project has.
    /// - The key light is a RANGE-LIMITED point light, never a directional - a
    ///   directional would light the whole city from here.
    /// - Instantiated subjects are stripped of every script, collider, light and audio
    ///   source BEFORE their first active frame (the stand is toggled inactive around
    ///   Instantiate, so no Awake ever runs): a photographed pedestrian must not walk,
    ///   register, honk or cast.
    /// </summary>
    public sealed class PortraitStudio : MonoBehaviour
    {
        public enum Framing
        {
            /// <summary>Head and shoulders, near enough straight on - the mugshot.</summary>
            Bust,

            /// <summary>The whole object, side-on with a hint of front - a gun on the board.</summary>
            Item,

            /// <summary>The whole object, front three-quarter from above - the lot photo.</summary>
            Vehicle,

            /// <summary>The whole object dead straight on, tight - a sign, not a
            /// photograph. The icon packs' little models are modelled facing +Z and
            /// read at 20 pixels only if nothing is turned away from the lens.</summary>
            Icon,
        }

        /// <summary>
        /// How the exposure is developed. The ledger wants the colour print it always
        /// had; the newspaper wants the same negative pushed through a halftone screen
        /// (see <see cref="News.Newsprint"/>). Part of the cache key, so one man can
        /// hang in the ledger and in the paper at once without either restaging him.
        /// </summary>
        public enum Treatment
        {
            Colour,
            Newsprint,
        }

        /// <summary>Unnamed in the layer table and unused by the project - see the class
        /// comment for why 30 is taken.</summary>
        const int StudioLayer = 31;

        const int PrintSize = 256;
        const float Fov = 25f;

        static PortraitStudio instance;

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            instance = null;
            warned.Clear();
        }

        sealed class Job
        {
            public string Key;
            public GameObject Prefab;
            public Framing Framing;
            public Treatment Treatment;
            public readonly List<RawImage> Targets = new List<RawImage>();
        }

        readonly Dictionary<string, Texture2D> prints = new Dictionary<string, Texture2D>();
        readonly List<Job> queue = new List<Job>();

        Camera cam;
        Light keyLight;
        RenderTexture film;
        Transform stand;
        GameObject staged;

        /// <summary>
        /// Show this prefab in this RawImage. Instant when cached; otherwise the image
        /// stays untouched (leave it disabled - Show enables it) until the print lands.
        /// Null prefab or target: no-op.
        /// </summary>
        public static void Request(GameObject prefab, Framing framing, RawImage target,
            Treatment treatment = Treatment.Colour)
        {
            if (!prefab || !target)
                return;

            var studio = Require();
            var key = treatment + ":" + framing + ":" + prefab.name;

            if (studio.prints.TryGetValue(key, out var print))
            {
                Show(target, print);
                return;
            }

            foreach (var pending in studio.queue)
                if (pending.Key == key)
                {
                    pending.Targets.Add(target);
                    return;
                }

            var job = new Job { Key = key, Prefab = prefab, Framing = framing, Treatment = treatment };
            job.Targets.Add(target);
            studio.queue.Add(job);
        }

        // ------------------------------------------------------- prefab resolution

        /// <summary>GangDirector's read-only name scan of the shipped pedestrian groups,
        /// for callers outside a spawn pass. Null when the scene has no city yet.</summary>
        public static GameObject FindPeoplePrefab(string prefabName)
        {
            var prefabs = Database();
            if (!prefabs || prefabs.pedestrianGroups == null)
            {
                var stray = LedgerModelSet.PersonNamed(prefabName) ?? EditorFallback(prefabName);
                if (stray)
                    return stray;
                WarnOnce("people:" + prefabName, "[PortraitStudio] No CityBuilder with " +
                    "pedestrian groups in the scene - no mugshot for '" + prefabName + "'.");
                return null;
            }

            foreach (var group in prefabs.pedestrianGroups)
            {
                if (group?.prefabs == null)
                    continue;

                foreach (var prefab in group.prefabs)
                    if (prefab && prefab.name == prefabName)
                        return prefab;
            }

            // The officer rides no crowd group - the patrol fleet is the only source of
            // one - and the newspaper's drug-war desk asks for him by name.
            if (prefabs.policeOfficerPrefab && prefabs.policeOfficerPrefab.name == prefabName)
                return prefabs.policeOfficerPrefab;

            // The Synty cast baked into LedgerModelSet answers by name (with or without
            // the "_AI" suffix) - the book's portraits never wait on the crowd slots.
            var fallback = LedgerModelSet.PersonNamed(prefabName) ?? EditorFallback(prefabName);
            if (fallback)
                return fallback;
            WarnOnce("people:" + prefabName, "[PortraitStudio] '" + prefabName +
                "' is not in the PrefabDatabase pedestrian groups - no mugshot.");
            return null;
        }

        /// <summary>
        /// The dev-time safety net under both scans: when the PrefabDatabase cannot
        /// answer (its people and car slots are being re-dealt - the working tree has
        /// had them empty), photograph the pack's own prefab of that name, or of the
        /// same name without the "_AI" suffix the converted street copies carry. Editor
        /// only: a build has no AssetDatabase and keeps the initials, which is what an
        /// empty database deserves. Logged once per name so the gap stays visible.
        /// </summary>
        static GameObject EditorFallback(string prefabName, bool vehicle = false)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(prefabName))
                return null;

            var bare = prefabName.EndsWith("_AI")
                ? prefabName.Substring(0, prefabName.Length - 3)
                : null;
            foreach (var candidate in new[] { prefabName, bare })
            {
                if (candidate == null)
                    continue;
                foreach (var guid in RoadDemo.DemoAssetLoad.Find(candidate + " t:Prefab"))
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) != candidate)
                        continue;
                    // A name alone is not enough to pick a body with. Two packs ship a
                    // "SM_Veh_Motorbike_01" and one of them is the police pack's, where
                    // EVERY body wears a livery and the names give nothing away
                    // (VehicleCatalog.PoliceFleetFolder) - so the armory counter would
                    // have photographed a patrol machine and sold it as the outfit's.
                    // The folder is the rule, exactly as it is for the street scans.
                    if (vehicle && Gameplay.VehicleCatalog.IsMarkedService(path))
                        continue;
                    var prefab = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
                    if (!prefab)
                        continue;
                    WarnOnce("fallback:" + prefabName, "[PortraitStudio] '" + prefabName +
                        "' is not in the PrefabDatabase - photographing " + path +
                        " instead (editor only; a build keeps the initials).");
                    return prefab;
                }
            }
#endif
            return null;
        }

        /// <summary>Same scan over the vehicle groups - parked bodies first (no AI
        /// scripts to strip), traffic bodies as the fallback.</summary>
        public static GameObject FindVehiclePrefab(string prefabName)
        {
            // The two-wheelers first, and out of a table of their own: no scan in the
            // project will put one in the PrefabDatabase (every one of them denies
            // "bike", "moped" and "scooter" by name), so asking the database for a
            // motorcycle can only ever fail into the editor fallback - which a build
            // does not have.
            var bike = LedgerModelSet.MotorcycleNamed(prefabName);
            if (bike)
                return bike;

            var prefabs = Database();
            if (!prefabs)
            {
                var stray = EditorFallback(prefabName, vehicle: true);
                if (stray)
                    return stray;
                WarnOnce("vehicle:" + prefabName, "[PortraitStudio] No CityBuilder in " +
                    "the scene - no photo for '" + prefabName + "'.");
                return null;
            }

            var found = ScanVehicles(prefabs.parkedCarGroups, prefabName)
                        ?? ScanVehicles(prefabs.aiCarGroups, prefabName);

            // Same exception as the officer: the patrol car sits outside every traffic
            // bucket, and the paper photographs it at the kerb.
            if (!found && prefabs.policeCarPrefab && prefabs.policeCarPrefab.name == prefabName)
                found = prefabs.policeCarPrefab;

            if (!found)
                found = EditorFallback(prefabName, vehicle: true);
            if (!found)
                WarnOnce("vehicle:" + prefabName, "[PortraitStudio] '" + prefabName +
                    "' is not in the PrefabDatabase car groups - no photo.");
            return found;
        }

        static readonly HashSet<string> warned = new HashSet<string>();

        static void WarnOnce(string key, string message)
        {
            if (warned.Add(key))
                Debug.LogWarning(message);
        }

        /// <summary>Which body plays each armory listing. Display names are the stable
        /// consts in ArmoryCatalog; the bodies are the pack's ordinary street cars.</summary>
        public static string VehicleModelFor(string displayName) => displayName switch
        {
            "Jalopy" => "SM_Veh_Pickup_01",
            "Sedan" => "SM_Veh_Sedan_01",
            "Panel Van" => "SM_Veh_Van_01",
            // The three two-wheelers, Palm City's. Named exactly, and never by a
            // substring: "Motorbike" also names the police pack's liveried tourer, and
            // the outfit does not ride one of those.
            "Motorbike" => "SM_Veh_Motorbike_01",
            "Moped" => "SM_Veh_Moped_01",
            "Scooter" => "SM_Veh_Scooter_01",
            _ => "SM_Veh_Sedan_01",
        };

        static GameObject ScanVehicles(PrefabDatabase.WeightedPrefabs[] groups, string prefabName)
        {
            if (groups == null)
                return null;

            foreach (var group in groups)
            {
                if (group?.prefabs == null)
                    continue;

                foreach (var prefab in group.prefabs)
                    if (prefab && prefab.name == prefabName)
                        return prefab;
            }

            return null;
        }

        static PrefabDatabase Database()
        {
            var builder = FindAnyObjectByType<CityBuilder>();
            if (builder && builder.Prefabs)
                return builder.Prefabs;

            // The city-less scenes (the standalone Ledger menu) still photograph:
            // the same database the city uses reaches Play through LedgerModelSet's
            // Resources bridge, the way the gun bodies already do.
            var bridge = LedgerModelSet.Instance;
            return bridge ? bridge.database : null;
        }

        // ------------------------------------------------------------------ the rig

        static PortraitStudio Require()
        {
            if (instance)
                return instance;

            var go = new GameObject("Portrait Studio");
            go.transform.position = new Vector3(-2500f, -400f, -2500f);
            instance = go.AddComponent<PortraitStudio>();
            instance.BuildRig();
            return instance;
        }

        void BuildRig()
        {
            gameObject.layer = StudioLayer;

            film = new RenderTexture(PrintSize, PrintSize, 16, RenderTextureFormat.ARGB32);

            var standGo = new GameObject("Stand");
            standGo.transform.SetParent(transform, false);
            standGo.layer = StudioLayer;
            stand = standGo.transform;

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(transform, false);
            camGo.layer = StudioLayer;
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 1 << StudioLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            cam.targetTexture = film;

            var lightGo = new GameObject("Key Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.layer = StudioLayer;
            keyLight = lightGo.AddComponent<Light>();
            keyLight.type = LightType.Point;
            keyLight.range = 40f;
            keyLight.intensity = 2.2f;
        }

        void LateUpdate()
        {
            // One bad prefab must never dam the queue: whatever throws, the job is
            // dropped with its reason in the console and the next subject steps up.
            if (staged)
            {
                try
                {
                    Develop();
                }
                catch (Exception e)
                {
                    Abort(e);
                }

                return;
            }

            if (queue.Count == 0)
            {
                if (cam.enabled)
                    cam.enabled = false;
                return;
            }

            try
            {
                Stage(queue[0]);
            }
            catch (Exception e)
            {
                Abort(e);
            }
        }

        void Abort(Exception e)
        {
            var key = queue.Count > 0 ? queue[0].Key : "?";
            Debug.LogWarning("[PortraitStudio] Dropping '" + key + "': " + e, this);
            if (staged)
                Destroy(staged);
            staged = null;
            if (queue.Count > 0)
                queue.RemoveAt(0);
        }

        void Stage(Job job)
        {
            // Inactive around Instantiate so nothing on the prefab ever runs Awake;
            // by the time the stand comes back the scripts are already gone.
            stand.gameObject.SetActive(false);
            staged = Instantiate(job.Prefab, stand);
            staged.transform.localPosition = Vector3.zero;
            staged.transform.localRotation = Quaternion.identity;
            Strip(staged);
            stand.gameObject.SetActive(true);

            SetLayer(staged.transform);
            Pose(staged, job.Framing);
            Aim(BoundsOf(staged), job.Framing);

            // The mugshot gets an opaque wall so the print covers the initials
            // beneath it; merchandise floats on the page through alpha. In newsprint
            // that wall goes light: the ledger's near-black would screen to a solid
            // slab of ink, where a press mugshot stands against pale studio backdrop.
            if (job.Framing != Framing.Bust)
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            else
                cam.backgroundColor = job.Treatment == Treatment.Newsprint
                    ? new Color(0.84f, 0.84f, 0.84f, 1f)
                    : new Color(0.10f, 0.11f, 0.10f, 1f);

            // Belt and braces on top of the 2.5km: no other camera - including ones
            // created after this rig, like the strategic map's - may see the studio.
            foreach (var other in Camera.allCameras)
                if (other != cam)
                    other.cullingMask &= ~(1 << StudioLayer);

            cam.enabled = true;
        }

        void Develop()
        {
            var job = queue[0];

            var print = new Texture2D(PrintSize, PrintSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var previous = RenderTexture.active;
            RenderTexture.active = film;
            print.ReadPixels(new Rect(0f, 0f, PrintSize, PrintSize), 0, 0);
            RenderTexture.active = previous;

            if (job.Treatment == Treatment.Newsprint)
                ScreenToNewsprint(print);

            // Apply LAST and non-readable: the newsprint pass needs the pixels on the
            // CPU, and uploading twice would cost a second copy of every print.
            print.Apply(false, true);

            prints[job.Key] = print;
            foreach (var target in job.Targets)
                Show(target, print);

            Destroy(staged);
            staged = null;
            queue.RemoveAt(0);
        }

        /// <summary>
        /// Turns an exposure into newsprint: composite over paper (a cut-out subject
        /// prints on bare stock, not on a hole), take luminance, and run it through the
        /// halftone screen. The arithmetic lives in News.Newsprint so the headless suite
        /// can proof it; this is only the pixel loop.
        /// </summary>
        static void ScreenToNewsprint(Texture2D print)
        {
            var pixels = print.GetPixels32();
            var width = print.width;

            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var a = p.a / 255f;
                var r = p.r / 255f * a + News.Newsprint.PaperLuminance * (1f - a);
                var g = p.g / 255f * a + News.Newsprint.PaperLuminance * (1f - a);
                var b = p.b / 255f * a + News.Newsprint.PaperLuminance * (1f - a);

                var luminance = 0.299f * r + 0.587f * g + 0.114f * b;
                var shade = News.Newsprint.Shade(luminance, i % width, i / width);

                pixels[i] = new Color32(
                    (byte)(Mathf.Lerp(News.Newsprint.InkR, News.Newsprint.PaperR, shade) * 255f),
                    (byte)(Mathf.Lerp(News.Newsprint.InkG, News.Newsprint.PaperG, shade) * 255f),
                    (byte)(Mathf.Lerp(News.Newsprint.InkB, News.Newsprint.PaperB, shade) * 255f),
                    255);
            }

            print.SetPixels32(pixels);
        }

        static void Show(RawImage target, Texture2D print)
        {
            if (!target)
                return;

            target.texture = print;
            target.enabled = true;
        }

        /// <summary>
        /// Everything that behaves goes; everything that renders stays. Scripts first,
        /// then the physical components they may [RequireComponent] - the other order
        /// makes DestroyImmediate refuse. Scripts themselves go leaves-first for the
        /// same reason: the pack chains PedestrianAgent -> HumanBehavior -> PathFinding,
        /// and a dependency may not die before its dependents. Immediate on purpose: the
        /// subject is inactive and must have nothing left to Awake when the stand
        /// reactivates.
        /// </summary>
        static void Strip(GameObject subject)
        {
            var scripts = new List<MonoBehaviour>(subject.GetComponentsInChildren<MonoBehaviour>(true));
            while (scripts.Count > 0)
            {
                var progress = false;
                for (var i = scripts.Count - 1; i >= 0; i--)
                {
                    var script = scripts[i];
                    if (script && RequiredByAnother(script, scripts))
                        continue;

                    if (script)
                        DestroyImmediate(script);
                    scripts.RemoveAt(i);
                    progress = true;
                }

                if (!progress)
                {
                    // A [RequireComponent] cycle - shouldn't exist, but never hang here.
                    foreach (var script in scripts)
                        if (script)
                            DestroyImmediate(script);
                    break;
                }
            }

            foreach (var collider in subject.GetComponentsInChildren<Collider>(true))
                if (collider)
                    DestroyImmediate(collider);

            foreach (var body in subject.GetComponentsInChildren<Rigidbody>(true))
                if (body)
                    DestroyImmediate(body);

            foreach (var source in subject.GetComponentsInChildren<AudioSource>(true))
                if (source)
                    DestroyImmediate(source);

            // A car's headlights against the 256 ceiling, and any exhaust puffing
            // through the frame - neither belongs in a photograph.
            foreach (var light in subject.GetComponentsInChildren<Light>(true))
                if (light)
                    DestroyImmediate(light);

            foreach (var particles in subject.GetComponentsInChildren<ParticleSystem>(true))
                if (particles)
                    DestroyImmediate(particles.gameObject);
        }

        static bool RequiredByAnother(MonoBehaviour script, List<MonoBehaviour> scripts)
        {
            var type = script.GetType();
            foreach (var other in scripts)
            {
                if (!other || ReferenceEquals(other, script) || other.gameObject != script.gameObject)
                    continue;
                foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                    if (Requires(req.m_Type0, type) || Requires(req.m_Type1, type) || Requires(req.m_Type2, type))
                        return true;
            }

            return false;
        }

        static bool Requires(Type required, Type candidate)
            => required != null && required.IsAssignableFrom(candidate);

        void SetLayer(Transform node)
        {
            node.gameObject.layer = StudioLayer;
            for (var i = 0; i < node.childCount; i++)
                SetLayer(node.GetChild(i));
        }

        /// <summary>A bust is posed: sample the shared pedestrian controller a beat into
        /// its entry state and freeze, so the man stands easy instead of T-posing. No
        /// controller resolvable = the T-pose fallback, cropped to the chest anyway.</summary>
        static void Pose(GameObject subject, Framing framing)
        {
            if (framing != Framing.Bust)
                return;

            var animator = subject.GetComponentInChildren<Animator>();
            if (!animator)
                return;

            if (!animator.runtimeAnimatorController)
            {
                var prefabs = Database();
                if (prefabs && prefabs.pedestrianController)
                    animator.runtimeAnimatorController = prefabs.pedestrianController;
            }

            if (!animator.runtimeAnimatorController)
                return;

            animator.Update(0.35f);
            animator.speed = 0f;
        }

        static Bounds BoundsOf(GameObject subject)
        {
            var bounds = new Bounds(subject.transform.position, Vector3.one * 0.5f);
            var first = true;
            foreach (var renderer in subject.GetComponentsInChildren<Renderer>())
            {
                if (first)
                {
                    bounds = renderer.bounds;
                    first = false;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }

        /// <summary>
        /// Parks the camera for the shot. Subjects stand at identity, facing +Z, so the
        /// offset rotation is read as "where the camera sits relative to the face":
        /// negative pitch puts it above the focus looking down.
        /// </summary>
        void Aim(Bounds bounds, Framing framing)
        {
            Vector3 focus;
            float halfSpan;
            Quaternion around;

            if (framing == Framing.Bust)
            {
                var height = Mathf.Max(bounds.size.y, 0.1f);
                focus = new Vector3(bounds.center.x,
                    bounds.max.y - height * 0.14f, bounds.center.z);
                // Head and shoulders: the top third of the man, framed a little loose.
                halfSpan = height * 0.17f;
                around = Quaternion.Euler(-4f, 12f, 0f);
            }
            else
            {
                focus = bounds.center;
                halfSpan = Mathf.Max(bounds.extents.magnitude, 0.05f) *
                           (framing == Framing.Icon ? 0.92f : 1.06f); // a sign fills its square
                around = framing == Framing.Vehicle
                    ? Quaternion.Euler(-18f, 45f, 0f)   // front three-quarter, from above
                    : framing == Framing.Icon
                    ? Quaternion.identity                // straight on the face of it
                    : Quaternion.Euler(-12f, 75f, 0f);  // near side-on - how a gun reads
            }

            var distance = halfSpan / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) + halfSpan;
            cam.transform.position = focus + around * Vector3.forward * distance;
            cam.transform.rotation =
                Quaternion.LookRotation(focus - cam.transform.position, Vector3.up);

            keyLight.transform.position =
                cam.transform.position + Vector3.up * (halfSpan + 1f) +
                cam.transform.right * halfSpan;
        }
    }
}
