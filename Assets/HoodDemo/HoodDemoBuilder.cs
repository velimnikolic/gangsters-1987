using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Gangs;
using LivingCity.Personnel;
using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HoodDemo
{
    /// <summary>
    /// A live wall of every animation a hood or lieutenant can currently enter.
    ///
    /// The wall owns no animation rules. Every clip comes from <see cref="CrewKit"/>,
    /// every body is one of <see cref="GangLooks"/>' approved gang looks, guns come
    /// through <see cref="CrewArms"/>, and each figure is driven by the same playable
    /// graph as a real <see cref="CrewWalker"/>. This class only lays those things out,
    /// repeats one-shots so they can be inspected, and pins each root to its mark.
    /// </summary>
    public sealed class HoodDemoBuilder : MonoBehaviour
    {
        [Header("Layout")]
        [Range(3, 10)] public int perRow = 6;
        [Min(3.5f)] public float columnSpacing = 4.8f;
        [Min(3f)] public float rowSpacing = 4.4f;
        [Min(1f)] public float sectionGap = 2.6f;

        [Header("Playback")]
        [Tooltip("Seconds a completed one-shot holds its final frame before it repeats.")]
        [Min(0f)] public float repeatPause = 0.75f;
        [Tooltip("All figures face south when the scene starts. Their labels turn with the camera.")]
        public float facing = 180f;

        const string StageName = "Hood Animation Wall";

        readonly List<Exhibit> _exhibits = new List<Exhibit>();
        readonly List<Transform> _billboards = new List<Transform>();
        readonly List<Material> _materials = new List<Material>();

        Transform _stage;
        Camera _camera;
        PedClips _wardrobe;
        Bounds _field;
        GUIStyle _titleStyle, _noteStyle;
        bool _paused;
        float _resumeScale = 1f;

        /// <summary>One visible entry. Clip is the full-body take; Upper is the real
        /// long-gun arms layer used over a run, when this is that weapon variant.</summary>
        public readonly struct AnimationEntry
        {
            public readonly string Section, State;
            public readonly AnimationClip Clip, Upper;
            public readonly bool Loop;
            public readonly EquipmentKind? Weapon;
            public readonly float Pause;
            public readonly bool LongGunFire;
            public readonly bool GunLow;

            public AnimationEntry(string section, string state, AnimationClip clip, bool loop,
                EquipmentKind? weapon, AnimationClip upper, float pause, bool longGunFire,
                bool gunLow)
            {
                Section = section;
                State = state;
                Clip = clip;
                Loop = loop;
                Weapon = weapon;
                Upper = upper;
                Pause = pause;
                LongGunFire = longGunFire;
                GunLow = gunLow;
            }
        }

        /// <summary>
        /// The current crew wardrobe as an inspectable list. Duplicated references are
        /// collapsed (CrewKit deliberately weights its plain idle twice), while the
        /// same combat clip with a different visible weapon remains a separate entry.
        /// Transition takes are admitted through PedestrianAgent's live join gates, so a
        /// clip imported into the pack but rejected by a walker is not advertised here.
        /// </summary>
        public static List<AnimationEntry> Catalogue(float oneShotPause = 0.75f)
        {
            var list = new List<AnimationEntry>();
            var seen = new HashSet<(
                AnimationClip clip, AnimationClip upper, EquipmentKind? weapon)>();
            var clips = CrewKit.Clips();

            void Add(string section, string state, AnimationClip clip, bool loop,
                EquipmentKind? weapon = null, AnimationClip upper = null, float pause = -1f,
                bool longGunFire = false, bool gunLow = false)
            {
                if (clip == null) return;
                if (!seen.Add((clip, upper, weapon))) return;
                list.Add(new AnimationEntry(section, state, clip, loop, weapon, upper,
                    pause >= 0f ? pause : oneShotPause, longGunFire, gunLow));
            }

            void AddPool(string section, string state, IReadOnlyList<AnimationClip> pool,
                bool loop, float pause = -1f)
            {
                if (pool == null) return;
                for (int i = 0; i < pool.Count; i++)
                    Add(section, state, pool[i], loop, pause: pause);
            }

            // Poses wired onto every real CrewWalker.
            AddPool("STANCE & LOCOMOTION", "IDLE", CrewKit.Idles, loop: true);
            Add("STANCE & LOCOMOTION", "WALK", clips.Walk, loop: true);
            Add("STANCE & LOCOMOTION", "RUN", clips.Jog, loop: true);
            Add("STANCE & LOCOMOTION", "SPRINT / FLEE", clips.Sprint, loop: true);
            Add("STANCE & LOCOMOTION", "CROUCH", clips.Crouch, loop: true);
            Add("STANCE & LOCOMOTION", "CROUCH WALK", clips.CrouchWalk, loop: true);
            Add("STANCE & LOCOMOTION", "TALK", clips.Talk, loop: true);
            Add("STANCE & LOCOMOTION", "SIT IN CAR", clips.SitLoop, loop: true);
            Add("STANCE & LOCOMOTION", "RIDE MOTORCYCLE", clips.Ride, loop: true);

            // CrewWalker uses one authored low/aim/shoot set, but the held model and
            // two-hand solve make each real weapon's resulting pose worth inspecting.
            foreach (var arm in new (EquipmentKind kind, string label)[]
            {
                (EquipmentKind.Pistol, "PISTOL"),
                (EquipmentKind.MachinePistol, "MACHINE PISTOL"),
                (EquipmentKind.Shotgun, "SHOTGUN"),
                (EquipmentKind.Rifle, "RIFLE"),
                (EquipmentKind.TommyGun, "TOMMY GUN"),
            })
            {
                Add("COMBAT", "GUN LOW / " + arm.label, clips.PistolIdle, loop: true,
                    weapon: arm.kind, gunLow: true);
                Add("COMBAT", "AIM / " + arm.label, clips.Aim, loop: true,
                    weapon: arm.kind, longGunFire: CrewArms.TwoHanded(arm.kind));
                Add("COMBAT", "SHOOT / " + arm.label, clips.Shoot, loop: false,
                    weapon: arm.kind, pause: 0.45f,
                    longGunFire: CrewArms.TwoHanded(arm.kind));
            }
            AddPool("COMBAT", "HIT", CrewKit.Hits, loop: false, pause: 0.55f);
            AddPool("COMBAT", "DEATH", CrewKit.Deaths, loop: false, pause: 1.25f);

            // This is the one weapon-dependent animation path in CrewWalker: long guns
            // lay a two-handed arms-only hold over the same run/sprint legs.
            if (clips.LongGunRunUpper != null)
            {
                foreach (var arm in new (EquipmentKind kind, string label)[]
                {
                    (EquipmentKind.Shotgun, "SHOTGUN"),
                    (EquipmentKind.Rifle, "RIFLE"),
                    (EquipmentKind.TommyGun, "TOMMY GUN"),
                })
                {
                    Add("LONG-GUN VARIANTS", "RUN / " + arm.label, clips.Jog, loop: true,
                        weapon: arm.kind, upper: clips.LongGunRunUpper);
                    Add("LONG-GUN VARIANTS", "SPRINT / " + arm.label, clips.Sprint, loop: true,
                        weapon: arm.kind, upper: clips.LongGunRunUpper);
                }
            }

            // Starts and stops. The imported run starts currently carry no translation
            // and the shared gate rejects them; if a pack update fixes them they appear
            // here automatically.
            var headings = new (string name, float angle)[]
            {
                ("FORWARD", 0f), ("LEFT 90", -90f), ("RIGHT 90", 90f),
                ("LEFT 180", -180f), ("RIGHT 180", 180f),
            };
            foreach (var heading in headings)
            {
                var walk = CrewKit.StartMove(walk: true, heading.angle);
                if (RoadDemo.PedestrianAgent.CanCarryLinearJoin(walk))
                    Add("START & STOP", "START WALK / " + heading.name, walk, loop: false);

                var run = CrewKit.StartMove(walk: false, heading.angle);
                if (RoadDemo.PedestrianAgent.CanCarryLinearJoin(run))
                    Add("START & STOP", "START RUN / " + heading.name, run, loop: false);
            }
            foreach (bool leftFoot in new[] { true, false })
            {
                var walk = CrewKit.StopMove(walk: true, leftFoot);
                if (RoadDemo.PedestrianAgent.CanCarryLinearJoin(walk))
                    Add("START & STOP", "STOP WALK / " + (leftFoot ? "LEFT FOOT" : "RIGHT FOOT"),
                        walk, loop: false);

                var run = CrewKit.StopMove(walk: false, leftFoot);
                if (RoadDemo.PedestrianAgent.CanCarryLinearJoin(run))
                    Add("START & STOP", "STOP RUN / " + (leftFoot ? "LEFT FOOT" : "RIGHT FOOT"),
                        run, loop: false);
            }

            // Turns and the four standing shuffles are also the shared join layer.
            foreach (var turn in new (string name, float angle)[]
            {
                ("TURN LEFT 90", -90f), ("TURN RIGHT 90", 90f),
                ("TURN LEFT 180", -180f), ("TURN RIGHT 180", 180f),
            })
            {
                var clip = CrewKit.TurnOnSpot(turn.angle);
                if (RoadDemo.PedestrianAgent.CanCarryTurn(clip, turn.angle))
                    Add("TURN & SIDESTEP", turn.name, clip, loop: false);
            }
            foreach (var step in new (string name, Vector3 local)[]
            {
                ("SIDESTEP FORWARD", Vector3.forward), ("SIDESTEP BACK", Vector3.back),
                ("SIDESTEP LEFT", Vector3.left), ("SIDESTEP RIGHT", Vector3.right),
            })
            {
                var clip = CrewKit.Shuffle(step.local);
                if (RoadDemo.PedestrianAgent.CanCarryLinearJoin(clip))
                    Add("TURN & SIDESTEP", step.name, clip, loop: false);
            }

            // Everything CrewWalker can choose while it stands, talks, argues, waits or
            // checks behind itself after a run. Civilian-only plead/pray/snack poses are
            // deliberately absent: a hood never asks CrewKit for them.
            AddPool("CREW LIFE", "FIDGET", CrewKit.CrewFidgets, loop: false);
            Add("CREW LIFE", "GREETING", CrewKit.Greet, loop: false);
            AddPool("CREW LIFE", "SPEAKING GESTURE", CrewKit.SpeakGestures, loop: false);
            AddPool("CREW LIFE", "LISTENING GESTURE", CrewKit.ListenGestures, loop: false);
            AddPool("CREW LIFE", "GOODBYE", CrewKit.Waves, loop: false);
            Add("CREW LIFE", "ARGUING", CrewKit.AggressiveLoop, loop: true);
            AddPool("CREW LIFE", "LOOK BACK AFTER FLEEING", CrewKit.BackLooks, loop: false);
            Add("MOTORCYCLE SPILL", "FALL THROUGH AIR", CrewKit.Fall, loop: true);
            Add("MOTORCYCLE SPILL", "LAND / GET UP", CrewKit.Land, loop: false, pause: 1f);

            return list;
        }

        void Awake()
        {
#if UNITY_EDITOR
            // Unity keeps timeScale when Play stops. A demo entered after another
            // paused bench must still start with a running animation wall.
            Time.timeScale = 1f;
            Build();
#else
            Debug.LogError("[HoodDemo] This scene resolves demo assets through the editor " +
                           "catalogue and only runs in the Unity editor.");
#endif
        }

        void Update()
        {
            if (_stage == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                TogglePause();

            float dt = Time.deltaTime;
            for (int i = 0; i < _exhibits.Count; i++) _exhibits[i].Tick(dt);
        }

        void TogglePause()
        {
            if (_paused)
            {
                _paused = false;
                Time.timeScale = Mathf.Max(0.01f, _resumeScale);
            }
            else
            {
                _resumeScale = Mathf.Max(0.01f, Time.timeScale);
                _paused = true;
                Time.timeScale = 0f;
            }
        }

        void LateUpdate()
        {
            if (_stage == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _exhibits.Count; i++)
            {
                _exhibits[i].ApplyLatePose(dt);
                _exhibits[i].LockRoot();
            }
            FaceLabels();
        }

        void OnDisable()
        {
            if (_paused) Time.timeScale = Mathf.Max(0.01f, _resumeScale);
            _paused = false;
            if (!gameObject.scene.isLoaded) { Forget(); return; }
            Clear();
        }

        void Build()
        {
            Clear();
            _stage = new GameObject(StageName).transform;
            DemoScratch.Unsaved(_stage.gameObject);
            _wardrobe = CrewKit.Clips();

            var entries = Catalogue(repeatPause);
            var hoods = Bodies(GangLooks.Hoods, "HOOD");
            var lieutenants = Bodies(GangLooks.Lieutenants, "LIEUTENANT");
            if (entries.Count == 0 || (hoods.Count == 0 && lieutenants.Count == 0))
            {
                Debug.LogError("[HoodDemo] The animation wardrobe or gangster cast is empty. " +
                               "Check CrewKit and LedgerModelSet.", this);
                return;
            }

            var plate = Material("Hood Demo Label", new Color(0.055f, 0.065f, 0.08f), 0.04f);
            int cursor = 0;
            float z = 0f;
            int exhibitNumber = 0;
            while (cursor < entries.Count)
            {
                string section = entries[cursor].Section;
                int end = cursor + 1;
                while (end < entries.Count && entries[end].Section == section) end++;

                Header(section, z - 1.55f, SectionColor(section));
                int inSection = end - cursor;
                int rows = Mathf.CeilToInt(inSection / (float)Mathf.Max(1, perRow));
                for (int local = 0; local < inSection; local++)
                {
                    int row = local / perRow;
                    int col = local % perRow;
                    float x = (col - (perRow - 1) * 0.5f) * columnSpacing;
                    var at = new Vector3(x, 0f, z + row * rowSpacing);
                    var body = PickBody(exhibitNumber, hoods, lieutenants);
                    Stand(entries[cursor + local], body, at, exhibitNumber, plate);
                    exhibitNumber++;
                }

                cursor = end;
                z += rows * rowSpacing + sectionGap;
            }

            float width = Mathf.Max(12f, perRow * columnSpacing);
            float depth = Mathf.Max(12f, z - sectionGap + 2f);
            _field = new Bounds(new Vector3(0f, 1.25f, (depth - 4f) * 0.5f),
                new Vector3(width + 5f, 3.5f, depth + 4f));
            Ground();
            LightTheWall();
            BuildCamera();
            Debug.Log($"[HoodDemo] {_exhibits.Count} current hood/lieutenant animations and " +
                      "weapon variants are standing in place. One figure, one labelled entry.", this);
        }

        readonly struct Body
        {
            public readonly GameObject Prefab;
            public readonly string Role;

            public Body(GameObject prefab, string role) { Prefab = prefab; Role = role; }
        }

        static List<Body> Bodies(string[] names, string role)
        {
            var result = new List<Body>();
            var seen = new HashSet<GameObject>();
            if (names == null) return result;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || PedestrianIdentity.IsFemale(name)) continue;
                var prefab = TestBench.Cast(name);
                if (prefab != null && seen.Add(prefab)) result.Add(new Body(prefab, role));
            }
            return result;
        }

        static Body PickBody(int index, List<Body> hoods, List<Body> lieutenants)
        {
            // A suit every fourth figure keeps both ranks visible without turning the
            // wall into seventy copies of one lieutenant.
            if (lieutenants.Count > 0 && (index % 4 == 0 || hoods.Count == 0))
                return lieutenants[(index / 4) % lieutenants.Count];
            return hoods[index % hoods.Count];
        }

        void Stand(AnimationEntry entry, Body source, Vector3 at, int index, Material plate)
        {
            var go = Instantiate(source.Prefab, _stage);
            go.name = (index + 1).ToString("00") + " " + entry.State;
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, facing, 0f));

            // The baked cast is plain, but the fallback may be an old street prefab.
            // Disable every inherited behaviour before destroying it so none gets one
            // frame to wander, navigate or rebuild itself under the animation wall.
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                behaviour.enabled = false;
                Destroy(behaviour);
            }
            foreach (var nav in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
            {
                nav.enabled = false;
                Destroy(nav);
            }
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (var body in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            DemoScratch.Unsaved(go);

            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[HoodDemo] " + source.Prefab.name +
                                 " has no humanoid Animator and cannot show " + entry.State + ".");
                Destroy(go);
                return;
            }
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var walker = new ExhibitWalker
            {
                DisplayName = entry.State,
                IsLieutenant = source.Role == "LIEUTENANT",
                SourcePrefab = source.Prefab,
            };
            walker.InitAt(go.transform, _wardrobe, at, Quaternion.Euler(0f, facing, 0f));

            if (entry.Weapon.HasValue)
            {
                var weapon = CrewArms.ModelForKind(entry.Weapon.Value);
                if (weapon != null)
                {
                    walker.Arm(weapon, entry.Weapon.Value);
                    walker.DrawGun();
                }
                else Debug.LogWarning("[HoodDemo] No model for " + entry.Weapon.Value +
                                      "; " + entry.State + " shows an empty hand.");
            }

            float phase = entry.Loop
                ? Mathf.Repeat(index * 0.381966f, 1f)
                : Mathf.Repeat(index * 0.271828f, 0.68f);
            var exhibit = new Exhibit(walker, entry, at, Quaternion.Euler(0f, facing, 0f), phase);
            _exhibits.Add(exhibit);
            Label(entry, source.Role, at, plate, SectionColor(entry.Section));
        }

        sealed class ExhibitWalker : CrewWalker
        {
            /// <summary>Only the shared graph is ticked; no orders, roaming or combat
            /// simulation exist on this wall. The optional upper layer is CrewWalker's
            /// real long-gun layer, driven to the same full weight it reaches in a run.</summary>
            public void TickExhibit(float dt, bool longGunRun)
            {
                if (longGunRun) BlendLongGunRun(true, 1f);
                TickBlend(dt);
            }
        }

        sealed class Exhibit
        {
            readonly ExhibitWalker _walker;
            readonly AnimationEntry _entry;
            readonly Vector3 _anchor;
            readonly Quaternion _facing;
            bool _waiting;
            float _left;

            public Exhibit(ExhibitWalker walker, AnimationEntry entry, Vector3 anchor,
                Quaternion facing, float phase)
            {
                _walker = walker;
                _entry = entry;
                _anchor = anchor;
                _facing = facing;
                Replay(phase * entry.Clip.length);
            }

            void Replay(float at = 0f)
            {
                _waiting = false;
                _left = 0f;
                _walker.PlayTake(_entry.Clip, _entry.Loop, 1f, at);
            }

            public void Tick(float dt)
            {
                _walker.TickExhibit(dt, _entry.Upper != null);
                LockRoot();
                if (_entry.Loop) return;

                if (!_waiting && _walker.TakeFinished)
                {
                    _waiting = true;
                    _left = _entry.Pause;
                }
                if (!_waiting) return;
                _left -= dt;
                if (_left <= 0f) Replay();
            }

            public void LockRoot()
            {
                if (_walker.Tf != null)
                    _walker.Tf.SetPositionAndRotation(_anchor, _facing);
            }

            public void ApplyLatePose(float dt)
            {
                if (_entry.GunLow)
                    _walker.PoseGunLow(1f);
                if (_entry.LongGunFire && _walker.Tf != null)
                    _walker.PoseLongGunFire(
                        _walker.Tf.position + _walker.Tf.forward * 20f + Vector3.up * 1.3f,
                        1f);
                _walker.PoseLongGun(dt);
            }

            public void Dispose() => _walker.Dispose();
        }

        void Label(AnimationEntry entry, string role, Vector3 at, Material plate, Color ink)
        {
            var root = new GameObject(entry.State + " label").transform;
            root.SetParent(_stage, false);
            root.position = at + new Vector3(0.62f, 1.35f, 0.32f);
            DemoScratch.Unsaved(root.gameObject);

            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Plate";
            back.transform.SetParent(root, false);
            back.transform.localPosition = new Vector3(1.42f, 0f, 0.04f);
            back.transform.localScale = new Vector3(3.05f, 1.18f, 0.055f);
            back.GetComponent<MeshRenderer>().sharedMaterial = plate;
            Destroy(back.GetComponent<Collider>());

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(root, false);
            textGo.transform.localPosition = new Vector3(0.03f, 0f, 0f);
            var text = textGo.AddComponent<TextMesh>();
            text.text = EntryText(entry, role);
            text.fontSize = 64;
            text.characterSize = 0.028f;
            text.lineSpacing = 0.88f;
            text.anchor = TextAnchor.MiddleLeft;
            text.alignment = TextAlignment.Left;
            text.fontStyle = FontStyle.Bold;
            text.color = ink;
            _billboards.Add(root);
        }

        static string EntryText(AnimationEntry entry, string role)
        {
            string line = entry.State + "\n" + ClipName(entry.Clip);
            if (entry.Upper != null) line += "\n+ " + ClipName(entry.Upper) + " (ARMS)";
            else line += "\n" + role + (entry.Weapon.HasValue ? "  ·  " + entry.Weapon.Value : "");
            return line;
        }

        static string ClipName(AnimationClip clip)
        {
            if (clip == null) return "MISSING";
            int bar = clip.name.LastIndexOf('|');
            return bar >= 0 ? clip.name.Substring(bar + 1) : clip.name;
        }

        void Header(string section, float z, Color ink)
        {
            float left = -(perRow - 1) * columnSpacing * 0.5f - 0.4f;
            var root = new GameObject(section + " header").transform;
            root.SetParent(_stage, false);
            root.position = new Vector3(left, 2.8f, z);
            DemoScratch.Unsaved(root.gameObject);
            var text = root.gameObject.AddComponent<TextMesh>();
            text.text = section;
            text.fontSize = 72;
            text.characterSize = 0.055f;
            text.anchor = TextAnchor.MiddleLeft;
            text.alignment = TextAlignment.Left;
            text.fontStyle = FontStyle.Bold;
            text.color = ink;
            _billboards.Add(root);
        }

        static Color SectionColor(string section) => section switch
        {
            "STANCE & LOCOMOTION" => new Color(0.63f, 0.88f, 1f),
            "COMBAT" => new Color(1f, 0.55f, 0.44f),
            "LONG-GUN VARIANTS" => new Color(1f, 0.78f, 0.35f),
            "START & STOP" => new Color(0.66f, 0.95f, 0.62f),
            "TURN & SIDESTEP" => new Color(0.74f, 0.72f, 1f),
            "CREW LIFE" => new Color(0.95f, 0.75f, 1f),
            _ => new Color(0.85f, 0.88f, 0.93f),
        };

        Material Material(string name, Color color, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            _materials.Add(material);
            return material;
        }

        void Ground()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Animation Wall Floor";
            ground.transform.SetParent(_stage, false);
            ground.transform.position = new Vector3(_field.center.x, -0.08f, _field.center.z);
            ground.transform.localScale = new Vector3(_field.size.x, 0.15f, _field.size.z);
            ground.GetComponent<MeshRenderer>().sharedMaterial =
                Material("Hood Demo Floor", new Color(0.16f, 0.175f, 0.20f), 0.08f);
            Destroy(ground.GetComponent<Collider>());
        }

        void LightTheWall()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.49f);

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_stage, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.95f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(48f, 205f, 0f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_stage, false);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.55f, 0.68f, 1f);
            fillGo.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
        }

        void BuildCamera()
        {
            var cameraGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            cameraGo.transform.SetParent(_stage, false);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.fieldOfView = 43f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 500f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.36f, 0.43f, 0.54f);
            _camera.GetUniversalAdditionalCameraData().antialiasing =
                AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraGo.AddComponent<AudioListener>();

            var rig = cameraGo.AddComponent<DemoCamera>();
            rig.pivot = new Vector3(_field.center.x, 1.1f, _field.center.z);
            rig.yaw = 0f;
            rig.pitch = 47f;
            rig.mapAt = 10000f;
            rig.minDistance = 5f;
            rig.FrameSpan(Mathf.Max(_field.size.x, _field.size.z), fill: 0.9f, floor: 36f);
            rig.showHint = true;
            rig.hintTopPx = 38f;
            rig.hint = "SPACE: pause/resume animations   WASD/arrows: move   " +
                       "Q/E or right-drag: rotate   wheel: zoom   every root is locked";
        }

        void FaceLabels()
        {
            if (_camera == null) return;
            for (int i = 0; i < _billboards.Count; i++)
            {
                var label = _billboards[i];
                if (label == null) continue;
                var toCamera = _camera.transform.position - label.position;
                toCamera.y = 0f;
                if (toCamera.sqrMagnitude > 0.01f)
                    label.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
        }

        void OnGUI()
        {
            if (_exhibits.Count == 0) return;
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                };
                _titleStyle.normal.textColor = Color.white;
                _noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                _noteStyle.normal.textColor = new Color(0.83f, 0.87f, 0.93f);
            }
            GUI.Box(new Rect(8f, 7f, 520f, 56f), GUIContent.none);
            GUI.Label(new Rect(18f, 10f, 500f, 28f), "HOOD / LIEUTENANT ANIMATION WALL", _titleStyle);
            _noteStyle.normal.textColor = _paused
                ? new Color(1f, 0.78f, 0.32f)
                : new Color(0.83f, 0.87f, 0.93f);
            string state = _paused
                ? "PAUSED  ·  SPACE: RESUME"
                : "SPACE: PAUSE / RESUME";
            GUI.Label(new Rect(18f, 37f, 500f, 20f),
                state + "  ·  " + _exhibits.Count + " clips / variants", _noteStyle);
        }

        void Clear()
        {
            for (int i = 0; i < _exhibits.Count; i++) _exhibits[i].Dispose();
            if (_stage != null) Destroy(_stage.gameObject);
            TestBench.DestroyAll(_materials);
            Forget();
        }

        void Forget()
        {
            _exhibits.Clear();
            _billboards.Clear();
            _stage = null;
            _camera = null;
            _field = default;
        }
    }
}
