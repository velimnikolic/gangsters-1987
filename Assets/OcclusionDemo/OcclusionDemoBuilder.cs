using RoadDemo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OcclusionDemo
{
    /// <summary>
    /// One Residential 01 building, three bright crew stand-ins behind it and no city
    /// generation noise. The scene starts at the live city's occlusion amount and remains
    /// static until manually scrubbed, so transparency/sorting can be judged in isolation.
    /// </summary>
    public sealed class OcclusionDemoBuilder : MonoBehaviour
    {
        const string BuildingPath = "Assets/Prefabs/Residential/residential-01.prefab";
        const float GradientStartHeight = BuildingOpacityGradient.DefaultGradientStartHeight;
        const float GameGradientAmount = BuildingCutaway.DefaultGradientAmount;

        BuildingOpacityGradient _gradient;
        BuildingOpacityGradient.Profile _profile = BuildingOpacityGradient.Profile.Vertical;
        float _amount = GameGradientAmount;
        int _colliderCount;

        GUIStyle _titleStyle;
        GUIStyle _bodyStyle;
        GUIStyle _stateStyle;

        void Awake()
        {
            if (transform.Find("Occlusion Demo Runtime")) return;
            Build();
        }

        void Build()
        {
            var runtime = new GameObject("Occlusion Demo Runtime").transform;
            runtime.SetParent(transform, false);

            BuildStage(runtime);
            var building = BuildBuilding(runtime);
            BuildSubjects(runtime);
            BuildLight(runtime);
            BuildCamera(runtime);

            _gradient = building.AddComponent<BuildingOpacityGradient>();
            for (int i = 0; i < ResidentialUnits.All.Length; i++)
                if (ResidentialUnits.All[i].Name == "residential-01")
                {
                    _gradient.ConfigureFootprint(ResidentialUnits.All[i]);
                    break;
                }
            _gradient.Prepare();
            _gradient.Set(_amount, _profile, GradientStartHeight);
            _colliderCount = building.GetComponentsInChildren<Collider>(true).Length;
        }

        static void BuildStage(Transform root)
        {
            var ground = Cube("Ground", root, new Vector3(0f, -0.15f, 4f),
                new Vector3(58f, 0.2f, 54f));
            ground.GetComponent<MeshRenderer>().sharedMaterial =
                Lit("Demo ground", new Color(0.14f, 0.15f, 0.17f), 0.05f);

            var pavement = Cube("Building pavement", root, new Vector3(0f, 0f, 0.5f),
                new Vector3(27f, 0.22f, 21f));
            pavement.GetComponent<MeshRenderer>().sharedMaterial =
                Lit("Pavement", new Color(0.43f, 0.43f, 0.40f), 0.12f);

            var road = Cube("Road behind building", root, new Vector3(0f, -0.01f, 15.5f),
                new Vector3(58f, 0.16f, 9f));
            road.GetComponent<MeshRenderer>().sharedMaterial =
                Lit("Road", new Color(0.075f, 0.08f, 0.09f), 0.02f);

            var line = Cube("Road centre line", root, new Vector3(0f, 0.085f, 15.5f),
                new Vector3(58f, 0.025f, 0.16f));
            line.GetComponent<MeshRenderer>().sharedMaterial =
                Lit("Road line", new Color(0.93f, 0.74f, 0.22f), 0.05f);
        }

        static GameObject BuildBuilding(Transform root)
        {
            GameObject building = null;
#if UNITY_EDITOR
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BuildingPath);
            if (source) building = Instantiate(source);
#endif
            if (!building)
            {
                building = GameObject.CreatePrimitive(PrimitiveType.Cube);
                building.transform.localScale = new Vector3(20.8f, 15f, 15.7f);
                building.transform.position = new Vector3(0f, 7.5f, 0f);
                building.GetComponent<MeshRenderer>().sharedMaterial =
                    Lit("Fallback building", new Color(0.72f, 0.48f, 0.30f), 0.18f);
            }
            else
            {
                building.transform.position = Vector3.zero;

                // Residential prefabs use the pavement corner as their origin. Centre the
                // lot in this isolated stage while keeping its ground plane at y = 0.
                var footprint = building.GetComponent<Collider>();
                if (footprint)
                {
                    Bounds bounds = footprint.bounds;
                    building.transform.position += new Vector3(
                        -bounds.center.x,
                        -bounds.min.y,
                        -bounds.center.z);
                }
            }

            building.name = "Residential 01 - collider remains solid";
            building.transform.SetParent(root, true);
            return building;
        }

        static void BuildSubjects(Transform root)
        {
            var subjects = new GameObject("Crew Behind Building").transform;
            subjects.SetParent(root, false);
            Person(subjects, "Crew left", new Vector3(-4.2f, 0.2f, 10.3f),
                new Color(0.18f, 0.85f, 0.48f));
            Person(subjects, "Crew centre", new Vector3(0f, 0.2f, 10.3f),
                new Color(1f, 0.72f, 0.18f));
            Person(subjects, "Crew right", new Vector3(4.2f, 0.2f, 10.3f),
                new Color(0.25f, 0.66f, 1f));
        }

        static void Person(Transform root, string name, Vector3 at, Color colour)
        {
            var person = new GameObject(name).transform;
            person.SetParent(root, false);
            person.position = at;
            var material = Lit(name + " material", colour, 0.2f);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Visibility ring";
            ring.transform.SetParent(person, false);
            ring.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            ring.transform.localScale = new Vector3(0.9f, 0.025f, 0.9f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = material;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(person, false);
            body.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            body.transform.localScale = new Vector3(0.52f, 0.78f, 0.42f);
            body.GetComponent<MeshRenderer>().sharedMaterial = material;

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(person, false);
            head.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            head.transform.localScale = Vector3.one * 0.42f;
            head.GetComponent<MeshRenderer>().sharedMaterial =
                Lit("Skin", new Color(0.73f, 0.49f, 0.34f), 0.1f);
        }

        static void BuildLight(Transform root)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.40f, 0.45f);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        }

        static void BuildCamera(Transform root)
        {
            var cameraObject = new GameObject("Demo Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(root, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 43f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.50f, 0.61f, 0.73f);
            camera.GetUniversalAdditionalCameraData().antialiasing =
                AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraObject.AddComponent<AudioListener>();

            var controls = cameraObject.AddComponent<DemoCamera>();
            controls.pivot = new Vector3(0f, 3f, 2f);
            controls.distance = 36f;
            controls.yaw = 0f;
            controls.ConfigurePitch(34f, 24f);
            controls.mapTransition = false;
            controls.mapCeiling = 80f;
            controls.minDistance = 18f;
            controls.showHint = false;

            var rotation = Quaternion.Euler(controls.pitch, controls.yaw, 0f);
            cameraObject.transform.SetPositionAndRotation(
                controls.pivot + rotation * new Vector3(0f, 0f, -controls.distance), rotation);
        }

        void Update()
        {
            if (!_gradient) return;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.gKey.wasPressedThisFrame)
                    ToggleProfile();
                if (keyboard.rKey.wasPressedThisFrame)
                    _amount = GameGradientAmount;
                if (keyboard.oKey.wasPressedThisFrame)
                    _amount = 0f;
                if (keyboard.leftArrowKey.isPressed || keyboard.rightArrowKey.isPressed)
                {
                    float direction = keyboard.rightArrowKey.isPressed ? 1f : -1f;
                    _amount = Mathf.Clamp(_amount + direction * Time.unscaledDeltaTime * 0.5f,
                        0f, 2f);
                }
            }

            _gradient.Set(_amount, _profile, GradientStartHeight);
        }

        void ToggleProfile()
        {
            _profile = _profile == BuildingOpacityGradient.Profile.Vertical
                ? BuildingOpacityGradient.Profile.Uniform
                : BuildingOpacityGradient.Profile.Vertical;
        }

        void OnGUI()
        {
            EnsureStyles();
            const float width = 460f;
            var panel = new Rect(16f, 16f, width, 248f);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(30f, 28f, width - 28f, 30f), "OCCLUSION GRADIENT DEMO", _titleStyle);

            float baseAlpha = _amount <= 1f ? 1f : Mathf.Clamp01(2f - _amount);
            float roofAlpha = _amount < 1f ? 1f - _amount : 0f;
            string profile = _profile == BuildingOpacityGradient.Profile.Vertical
                ? $"VERTICAL: pavement alpha {baseAlpha:0.00}  ->  roof alpha {roofAlpha:0.00}"
                : $"UNIFORM CONTROL: alpha {1f - _amount * 0.5f:0.00} over the whole shell";
            GUI.Label(new Rect(30f, 62f, width - 28f, 24f), profile, _stateStyle);
            GUI.Label(new Rect(30f, 86f, width - 28f, 22f),
                $"Near ground floor stays opaque   |   upper floors still cut",
                _bodyStyle);
            GUI.Label(new Rect(30f, 108f, width - 28f, 22f),
                $"Effect {_amount * 100f:0}%   |   collider ON ({_colliderCount})   |   full shadow ON",
                _bodyStyle);

            float chosen = GUI.HorizontalSlider(new Rect(30f, 136f, width - 60f, 20f),
                _amount, 0f, 2f);
            if (Mathf.Abs(chosen - _amount) > 0.001f)
                _amount = chosen;

            if (GUI.Button(new Rect(30f, 164f, 146f, 28f),
                _profile == BuildingOpacityGradient.Profile.Vertical ? "Show uniform" : "Show gradient"))
                ToggleProfile();
            if (GUI.Button(new Rect(184f, 164f, 118f, 28f),
                $"Game {GameGradientAmount:0.00}"))
                _amount = GameGradientAmount;
            if (GUI.Button(new Rect(310f, 164f, 120f, 28f), "Opaque"))
                _amount = 0f;

            GUI.Label(new Rect(30f, 199f, width - 40f, 42f),
                $"G: compare   arrows: scrub   R: game {GameGradientAmount:0.00}   O: opaque\n" +
                "WASD / Q E / right-drag / wheel: inspect transparent sorting",
                _bodyStyle);
        }

        void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
            };
            _titleStyle.normal.textColor = new Color(0.95f, 0.78f, 0.24f);
            _stateStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
            };
            _stateStyle.normal.textColor = new Color(0.72f, 0.92f, 1f);
            _bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _bodyStyle.normal.textColor = new Color(0.88f, 0.90f, 0.92f);
        }

        static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            return cube;
        }

        static Material Lit(string name, Color colour, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.DontSave,
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            else material.color = colour;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            return material;
        }
    }
}
