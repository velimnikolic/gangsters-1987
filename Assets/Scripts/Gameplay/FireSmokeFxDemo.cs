using System.Collections.Generic;
using LivingCity.Ambient;
using LivingCity.Personnel;
using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// A deliberately motionless review bench for muzzle fire, gun smoke and exhaust.
    /// Characters never navigate and the car never enters traffic: only the sampled aim
    /// poses and the shared production particle prefabs run. The sliders are local to this
    /// bench, so experiments cannot silently retune the city.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FireSmokeFxDemo : MonoBehaviour
    {
        const string CharacterRoot =
            "Assets/Synty/PolygonGangWarfare/Prefabs/Character/";
        const string GangWeaponRoot =
            "Assets/Synty/PolygonGangWarfare/Prefabs/Weapons/";
        const string PalmWeaponRoot =
            "Assets/Synty/PolygonPalmCity/Prefabs/Weapons/";
        const string CarPath =
            "Assets/Synty/PolygonCity/Prefabs/Vehicles/SM_Veh_Car_Sedan_01.prefab";

        readonly struct ExhibitSpec
        {
            public readonly string Label;
            public readonly string Character;
            public readonly string Weapon;
            public readonly EquipmentKind Kind;
            public readonly float Interval;
            public readonly float Calibre;

            public ExhibitSpec(string label, string character, string weapon,
                               EquipmentKind kind, float interval, float calibre)
            {
                Label = label;
                Character = character;
                Weapon = weapon;
                Kind = kind;
                Interval = interval;
                Calibre = calibre;
            }
        }

        sealed class Shooter
        {
            public string Label;
            public GameObject Person;
            public Transform Muzzle;
            public AnimationClip Pose;
            public Vector3 Home;
            public Quaternion Facing;
            public EquipmentKind Kind;
            public float Interval;
            public float Calibre;
            public float NextShot;
        }

        static readonly ExhibitSpec[] Exhibits =
        {
            new ExhibitSpec("PISTOL", "SM_Chr_Italian_Gangster_01.prefab",
                GangWeaponRoot + "SM_Wep_Pistol_Revolver_01.prefab",
                EquipmentKind.Pistol, 1.05f, 0.17f),
            new ExhibitSpec("MACHINE PISTOL", "SM_Chr_GangMember_Male_01.prefab",
                GangWeaponRoot + "SM_Wep_Machine_Pistol_01.prefab",
                EquipmentKind.MachinePistol, 0.24f, 0.19f),
            new ExhibitSpec("SHOTGUN", "SM_Chr_British_Gangster_Male_01.prefab",
                GangWeaponRoot + "SM_Wep_Shotgun_01.prefab",
                EquipmentKind.Shotgun, 1.6f, 0.32f),
            new ExhibitSpec("RIFLE", "SM_Chr_DEA_Plainclothes_Male_01.prefab",
                PalmWeaponRoot + "SM_Wep_Rifle_01.prefab",
                EquipmentKind.Rifle, 1.35f, 0.27f),
            new ExhibitSpec("TOMMY GUN", "SM_Chr_GangMember_Male_03.prefab",
                GangWeaponRoot + "SM_Wep_SubMachineGun_01.prefab",
                EquipmentKind.TommyGun, 0.18f, 0.23f),
        };

        readonly List<Shooter> _shooters = new List<Shooter>(Exhibits.Length);
        readonly List<Material> _materials = new List<Material>(2);

        Transform _stage;
        GameObject _muzzlePrefab;
        GameObject _gunSmokePrefab;
        ParticleSystem _exhaust;
        Transform _exhaustTransform;
        Camera _camera;
        DemoCamera _cameraRig;

        bool _continuousFire = true;
        bool _showControls = true;
        float _shotTempo = 1f;
        float _flashSize = FireSmokeFx.DefaultWeaponFlash;
        float _gunSmokeAmount = FireSmokeFx.DefaultGunSmokeAmount;
        float _gunSmokeSize = FireSmokeFx.DefaultGunSmokeSize;
        float _gunSmokeLifetime = FireSmokeFx.DefaultGunSmokeLifetime;
        float _exhaustAmount = FireSmokeFx.DefaultExhaustAmount;
        float _exhaustSize = FireSmokeFx.DefaultExhaustSize;
        float _exhaustLifetime = FireSmokeFx.DefaultExhaustLifetime;
        float _exhaustSpeed = FireSmokeFx.DefaultExhaustSpeed;
        float _exhaustOpacity = FireSmokeFx.DefaultExhaustVisibility;

        GUIStyle _titleStyle;
        GUIStyle _sectionStyle;
        GUIStyle _labelStyle;
        GUIStyle _toggleStyle;
        GUIStyle _buttonStyle;
        GUIStyle _sliderStyle;
        GUIStyle _sliderThumbStyle;
        GUIStyle _helpStyle;

        void Awake()
        {
            if (!Application.isPlaying) return;
            BuildStage();
        }

        void BuildStage()
        {
            _stage = new GameObject("FX Review Stage").transform;
            _stage.SetParent(transform, false);

            _muzzlePrefab = CrewKit.MuzzleFlash;
            _gunSmokePrefab = CrewKit.GunSmoke;

            BuildGround();
            BuildLightsAndCamera();
            BuildShooters();
            BuildCarAndExhaust();
        }

        void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Review Ground";
            ground.transform.SetParent(_stage, false);
            ground.transform.localScale = new Vector3(2.2f, 1f, 1.7f);
            var collider = ground.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var material = NewMaterial("FX Demo Ground", new Color(0.105f, 0.11f, 0.12f));
            if (material != null) ground.GetComponent<Renderer>().sharedMaterial = material;
        }

        void BuildLightsAndCamera()
        {
            var key = new GameObject("Key Light").AddComponent<Light>();
            key.transform.SetParent(_stage, false);
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.9f, 0.78f);
            key.intensity = 1.35f;
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

            var fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.transform.SetParent(_stage, false);
            fill.type = LightType.Directional;
            fill.color = new Color(0.52f, 0.68f, 1f);
            fill.intensity = 0.55f;
            fill.transform.rotation = Quaternion.Euler(28f, 155f, 0f);

            var cameraGo = new GameObject("FX Demo Camera") { tag = "MainCamera" };
            cameraGo.transform.SetParent(_stage, false);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.fieldOfView = 49f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 100f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.075f, 0.085f, 0.105f);

            // Every review scene uses the same camera contract as the city: WASD/arrows
            // pan, Q/E or right-drag orbit, and the wheel zooms. This bench has no turf map,
            // so its whole zoom range remains a 3D inspection view.
            _cameraRig = cameraGo.AddComponent<DemoCamera>();
            _cameraRig.pivot = new Vector3(0f, 0.85f, 2.8f);
            _cameraRig.yaw = 0f;
            _cameraRig.ConfigurePitch(28f, 6f);
            _cameraRig.distance = 19f;
            _cameraRig.minDistance = 6f;
            _cameraRig.mapTransition = false;
            _cameraRig.mapCeiling = 65f;
            _cameraRig.showHint = false; // this bench draws a larger, readable help strip

            var cameraRotation = Quaternion.Euler(
                _cameraRig.pitch, _cameraRig.yaw, 0f);
            cameraGo.transform.SetPositionAndRotation(
                _cameraRig.pivot + cameraRotation * new Vector3(0f, 0f, -_cameraRig.distance),
                cameraRotation);
            DemoCamera.ClaimMainCamera(_camera);
        }

        void BuildShooters()
        {
            var clips = CrewKit.Clips();
            var sidearmPose = clips.Aim != null ? clips.Aim : clips.PistolIdle;
            var longGunPose = RifleKit.IdleAiming != null ? RifleKit.IdleAiming : sidearmPose;

            const float spacing = 3.3f;
            float first = -spacing * (Exhibits.Length - 1) * 0.5f;
            for (int i = 0; i < Exhibits.Length; i++)
            {
                var spec = Exhibits[i];
                var characterPrefab = DemoAssetLoad.Load<GameObject>(CharacterRoot + spec.Character);
                var weaponPrefab = DemoAssetLoad.Load<GameObject>(spec.Weapon);
                if (characterPrefab == null || weaponPrefab == null)
                {
                    Debug.LogWarning($"[FireSmokeFxDemo] Missing {spec.Label} character or weapon.", this);
                    continue;
                }

                var person = Instantiate(characterPrefab, _stage);
                person.name = spec.Label;
                var home = new Vector3(first + i * spacing, 0f, 2.45f);
                var facing = Quaternion.LookRotation(Vector3.back, Vector3.up);
                person.transform.SetPositionAndRotation(home, facing);

                foreach (var collider in person.GetComponentsInChildren<Collider>(true))
                    Destroy(collider);
                foreach (var body in person.GetComponentsInChildren<Rigidbody>(true))
                    Destroy(body);

                var animator = person.GetComponent<Animator>();
                if (animator == null || !animator.isHuman)
                {
                    Debug.LogWarning($"[FireSmokeFxDemo] {spec.Label} needs a Humanoid Animator.", person);
                    Destroy(person);
                    continue;
                }

                animator.applyRootMotion = false;
                animator.enabled = false;
                var pose = CrewArms.TwoHanded(spec.Kind) ? longGunPose : sidearmPose;
                SampleLockedPose(person, pose, home, facing);
                var gun = CrewArms.Attach(animator, weaponPrefab);
                var muzzle = CrewArms.MuzzleOf(gun);
                if (muzzle == null)
                {
                    Debug.LogWarning($"[FireSmokeFxDemo] {spec.Label} has no muzzle marker.", person);
                    Destroy(person);
                    continue;
                }

                _shooters.Add(new Shooter
                {
                    Label = spec.Label,
                    Person = person,
                    Muzzle = muzzle,
                    Pose = pose,
                    Home = home,
                    Facing = facing,
                    Kind = spec.Kind,
                    Interval = spec.Interval,
                    Calibre = spec.Calibre,
                    NextShot = Time.time + 0.3f + i * 0.12f,
                });
            }
        }

        void BuildCarAndExhaust()
        {
            var prefab = DemoAssetLoad.Load<GameObject>(CarPath);
            if (prefab == null)
            {
                Debug.LogWarning("[FireSmokeFxDemo] Review car prefab is missing.", this);
                return;
            }

            var car = Instantiate(prefab, new Vector3(0f, 0f, 7.1f), Quaternion.identity, _stage);
            car.name = "Stationary Exhaust Car";
            foreach (var collider in car.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (var body in car.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            PlantOnGround(car.transform);

            var bounds = WorldBounds(car.transform);
            float halfLength = Mathf.Max(1.4f, bounds.extents.z);
            var tailpipe = car.transform.position - car.transform.forward * (halfLength * 0.9f)
                           + car.transform.right * 0.48f + Vector3.up * 0.34f;

            _exhaust = CarSmoke.Tuned(
                CrewKit.Exhaust, _stage, Vector3.zero,
                wide: 0.18f, grow: 1.8f, lifeLo: 0.9f, lifeHi: 1.7f,
                speed: 0.7f, rate: 11f, tint: FireSmokeFx.ExhaustSmoke);
            if (_exhaust == null)
            {
                Debug.LogWarning("[FireSmokeFxDemo] Shared exhaust smoke prefab is missing.", this);
                return;
            }

            _exhaustTransform = _exhaust.transform;
            _exhaustTransform.SetPositionAndRotation(
                tailpipe,
                Quaternion.LookRotation(-car.transform.forward, Vector3.up) *
                Quaternion.Euler(12f, 0f, 0f));
            _exhaust.Play();
            ApplyExhaustTuning();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
                _showControls = !_showControls;

            ApplyExhaustTuning();
            if (!_continuousFire || _shooters.Count == 0) return;

            float now = Time.time;
            float tempo = Mathf.Max(0.1f, _shotTempo);
            for (int i = 0; i < _shooters.Count; i++)
            {
                var shooter = _shooters[i];
                if (now < shooter.NextShot) continue;
                Fire(shooter);
                shooter.NextShot = now + shooter.Interval / tempo;
            }
        }

        void LateUpdate()
        {
            for (int i = 0; i < _shooters.Count; i++)
            {
                var shooter = _shooters[i];
                if (shooter.Person != null)
                    SampleLockedPose(shooter.Person, shooter.Pose, shooter.Home, shooter.Facing);
            }
        }

        void Fire(Shooter shooter)
        {
            if (shooter.Muzzle == null) return;
            var at = shooter.Muzzle.position;
            var forward = shooter.Muzzle.forward;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.back;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);

            if (_muzzlePrefab != null && _flashSize > 0.01f)
            {
                var flash = Instantiate(_muzzlePrefab, at, rotation, _stage);
                var live = FireSmokeFx.TuneMuzzleFlash(
                    flash, shooter.Calibre * 0.72f, _flashSize);
                Destroy(flash, Mathf.Max(0.2f, live));
            }

            if (_gunSmokePrefab != null && _gunSmokeAmount > 0.01f)
            {
                var smoke = Instantiate(_gunSmokePrefab, at, rotation, _stage);
                bool rapid = shooter.Kind == EquipmentKind.MachinePistol ||
                             shooter.Kind == EquipmentKind.TommyGun;
                var live = FireSmokeFx.TuneGunSmoke(
                    smoke, shooter.Calibre, rapid,
                    _gunSmokeAmount, _gunSmokeSize, _gunSmokeLifetime);
                Destroy(smoke, Mathf.Max(0.6f, live));
            }
        }

        void ApplyExhaustTuning()
        {
            if (_exhaust == null) return;
            float amount = Mathf.Clamp(_exhaustAmount, 0f, 4f);
            float size = Mathf.Clamp(_exhaustSize, 0.1f, 4f);
            float lifetime = Mathf.Clamp(_exhaustLifetime, 0.1f, 4f);
            float speed = Mathf.Clamp(_exhaustSpeed, 0.1f, 4f);

            var main = _exhaust.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f * size, 0.324f * size);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f * lifetime, 1.7f * lifetime);
            main.startSpeed = 0.7f * speed;
            var tint = FireSmokeFx.ExhaustSmoke;
            tint.a = Mathf.Clamp01(tint.a * _exhaustOpacity);
            main.startColor = tint;
            main.maxParticles = Mathf.CeilToInt(16f * amount * 1.7f * lifetime) + 8;

            var emission = _exhaust.emission;
            emission.rateOverTime = 11f * amount;
        }

        static void SampleLockedPose(GameObject person, AnimationClip pose,
                                     Vector3 position, Quaternion rotation)
        {
            if (pose != null)
            {
                float sample = pose.length > 0.01f ? pose.length * 0.55f : 0f;
                pose.SampleAnimation(person, sample);
            }
            person.transform.SetPositionAndRotation(position, rotation);
        }

        static Bounds WorldBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(root.position, Vector3.one);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        static void PlantOnGround(Transform root)
        {
            var bounds = WorldBounds(root);
            root.position += Vector3.up * -bounds.min.y;
        }

        Material NewMaterial(string label, Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            var material = new Material(shader) { name = label };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            else material.color = colour;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
            _materials.Add(material);
            return material;
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawCameraHelp();
            if (!_showControls) return;

            float panelWidth = Mathf.Min(920f, Screen.width - 28f);
            var panel = new Rect(14f, 12f, panelWidth, 540f);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 9f, panel.width - 36f, 38f),
                "FIRE / SMOKE FX REVIEW", _titleStyle);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 47f, panel.width - 36f, 26f),
                "Svi ljudi i auto su zaključani u mjestu. H skriva/prikazuje ovaj panel.",
                _labelStyle);

            float y = panel.y + 80f;
            _continuousFire = GUI.Toggle(
                new Rect(panel.x + 18f, y, 250f, 31f), _continuousFire,
                " STALNO PUCAJ", _toggleStyle);
            if (GUI.Button(new Rect(panel.xMax - 138f, y - 1f, 120f, 34f),
                           "RESET", _buttonStyle))
                ResetTuning();

            const float gap = 34f;
            float columnWidth = (panel.width - 36f - gap) * 0.5f;
            float leftX = panel.x + 18f;
            float rightX = leftX + columnWidth + gap;
            float sectionY = y + 44f;

            GUI.Label(new Rect(leftX, sectionY, columnWidth, 28f), "ORUŽJE", _sectionStyle);
            float weaponY = sectionY + 34f;
            weaponY = Slider(ref _shotTempo, "Tempo pucanja", 0.25f, 2.5f,
                             leftX, weaponY, columnWidth);
            weaponY = Slider(ref _flashSize, "Vatra / flash veličina", 0f, 3f,
                             leftX, weaponY, columnWidth);
            weaponY = Slider(ref _gunSmokeAmount, "Dim količina", 0f, 3f,
                             leftX, weaponY, columnWidth);
            weaponY = Slider(ref _gunSmokeSize, "Dim veličina", 0.2f, 3f,
                             leftX, weaponY, columnWidth);
            Slider(ref _gunSmokeLifetime, "Dim trajanje", 0.2f, 3f,
                   leftX, weaponY, columnWidth);

            GUI.Label(new Rect(rightX, sectionY, columnWidth, 28f), "AUSPUH", _sectionStyle);
            float exhaustY = sectionY + 34f;
            exhaustY = Slider(ref _exhaustAmount, "Količina", 0f, 3f,
                              rightX, exhaustY, columnWidth);
            exhaustY = Slider(ref _exhaustSize, "Veličina", 0.2f, 3f,
                              rightX, exhaustY, columnWidth);
            exhaustY = Slider(ref _exhaustLifetime, "Trajanje", 0.2f, 3f,
                              rightX, exhaustY, columnWidth);
            exhaustY = Slider(ref _exhaustSpeed, "Brzina izlaza", 0.2f, 3f,
                              rightX, exhaustY, columnWidth);
            Slider(ref _exhaustOpacity, "Vidljivost", 0.2f, 1.3f,
                   rightX, exhaustY, columnWidth);

            GUI.Label(new Rect(panel.x + 18f, panel.yMax - 31f, panel.width - 36f, 25f),
                "Lijevo → desno: pistol · machine pistol · shotgun · rifle · Tommy Gun" +
                "     |     Vrijednosti važe samo u ovom demu.", _labelStyle);
        }

        float Slider(ref float value, string label, float minimum, float maximum,
                     float x, float y, float width)
        {
            GUI.Label(new Rect(x, y, width - 82f, 25f), label, _labelStyle);
            GUI.Label(new Rect(x + width - 78f, y, 78f, 25f),
                value.ToString("0.00") + "x", _labelStyle);
            value = GUI.HorizontalSlider(
                new Rect(x, y + 28f, width, 28f), value, minimum, maximum,
                _sliderStyle, _sliderThumbStyle);
            return y + 65f;
        }

        void DrawCameraHelp()
        {
            float width = Mathf.Min(1320f, Screen.width - 28f);
            var bar = new Rect(14f, Screen.height - 58f, width, 46f);
            GUI.Box(bar, GUIContent.none);
            GUI.Label(new Rect(bar.x + 14f, bar.y + 7f, bar.width - 28f, 32f),
                "WASD / STRELICE: POMJERI     Q / E: ROTIRAJ     DESNI MIŠ + DRAG: ORBIT     " +
                "WHEEL: ZOOM     H: KONTROLE",
                _helpStyle);
        }

        void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
            };
            _titleStyle.normal.textColor = Color.white;
            _sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
            };
            _sectionStyle.normal.textColor = new Color(1f, 0.72f, 0.28f);
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 17 };
            _labelStyle.normal.textColor = new Color(0.86f, 0.89f, 0.94f);
            _toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
            };
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
            };
            _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = 18f };
            _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 28f,
                fixedHeight = 28f,
            };
            _helpStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
            _helpStyle.normal.textColor = Color.white;
        }

        void ResetTuning()
        {
            _shotTempo = 1f;
            _flashSize = FireSmokeFx.DefaultWeaponFlash;
            _gunSmokeAmount = FireSmokeFx.DefaultGunSmokeAmount;
            _gunSmokeSize = FireSmokeFx.DefaultGunSmokeSize;
            _gunSmokeLifetime = FireSmokeFx.DefaultGunSmokeLifetime;
            _exhaustAmount = FireSmokeFx.DefaultExhaustAmount;
            _exhaustSize = FireSmokeFx.DefaultExhaustSize;
            _exhaustLifetime = FireSmokeFx.DefaultExhaustLifetime;
            _exhaustSpeed = FireSmokeFx.DefaultExhaustSpeed;
            _exhaustOpacity = FireSmokeFx.DefaultExhaustVisibility;
        }

        void OnDestroy()
        {
            for (int i = 0; i < _materials.Count; i++)
                if (_materials[i] != null) Destroy(_materials[i]);
            _materials.Clear();
        }
    }
}
