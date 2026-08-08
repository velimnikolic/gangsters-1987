using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Builds a self-contained scene in which one man shoots another.
    ///
    /// Standalone on purpose. The city needs generated tiles, checkpoint routes and a
    /// PrefabDatabase before a pedestrian can take a single step, and none of that changes how
    /// the shooting READS - which is the only question this scene exists to answer. So: a
    /// plane, a light, a camera, two rigs and a timeline. It runs the moment you press Play.
    ///
    /// The characters are the _Rig prefabs rather than the _AI ones. Same models, same humanoid
    /// avatars, but without HumanBehavior and PathFinding, which would spend the whole scene
    /// logging that they cannot find a route.
    /// </summary>
    public static class ShootingDemoSetup
    {
        const string ScenePath = "Assets/Scenes/Shooting Demo.unity";
        const string ControllerPath = "Assets/Configs/People Interaction Controller.controller";
        const string RevolverPath = "Assets/Weapons/Revolver.obj";
        const string WeaponMaterialDir = "Assets/Materials/Weapons";

        const string Rigs = "Assets/polyperfect/Low Poly Epic City/T/- Prefabs_T/People_T/Rigs_T/";
        const string ShooterPrefab = Rigs + "man-mafia_Rig.prefab";
        const string VictimPrefab = Rigs + "man-casual_Rig.prefab";

        /// <summary>How far apart they stand. Close enough to read as a confrontation.</summary>
        const float Range = 3.2f;

        [MenuItem("Tools/City/Build Shooting Demo Scene", priority = 20)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            var shooterSource = AssetDatabase.LoadAssetAtPath<GameObject>(ShooterPrefab);
            var victimSource = AssetDatabase.LoadAssetAtPath<GameObject>(VictimPrefab);
            var revolver = AssetDatabase.LoadAssetAtPath<GameObject>(RevolverPath);

            if (!controller)
            {
                Debug.LogError("[ShootingDemo] No People Interaction Controller. Run " +
                               "Tools/City/Create or Refresh Config Assets first - it authors " +
                               "the controller, including the Die state this scene needs.");
                return;
            }

            if (!shooterSource || !victimSource)
            {
                Debug.LogError($"[ShootingDemo] Missing character rigs under {Rigs}.");
                return;
            }

            if (!revolver)
                Debug.LogWarning($"[ShootingDemo] No revolver at {RevolverPath} - the gunman " +
                                 "will aim an empty hand. Everything else still runs.");
            else
                RemapRevolverMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);

            BuildGround();
            var (shooter, victim) = PlaceCharacters(shooterSource, victimSource, controller);
            var socket = EquipRevolver(shooter, revolver);
            FrameCamera(shooter.transform.position, victim.transform.position);
            SetUpKeyLight();

            var director = new GameObject("Shooting Demo").AddComponent<ShootingDemo>();
            CityEditorUtils.SetField(director, "gunman", shooter.GetComponent<GunmanAim>());
            CityEditorUtils.SetField(director, "weapon", socket);
            CityEditorUtils.SetField(director, "victim", victim.GetComponent<PedestrianDeath>());

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            Selection.activeGameObject = director.gameObject;
            Debug.Log($"[ShootingDemo] Built {ScenePath}. Press Play: the gunman waits, raises " +
                      "the revolver, fires, and the other man goes down.", director);
        }

        /// <summary>
        /// A dark plane to stand on. Unity's OBJ and primitive materials default to the
        /// built-in pipeline's shader, which renders magenta under URP, so every material this
        /// scene uses is created against the URP Lit shader explicitly.
        /// </summary>
        static void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.GetComponent<MeshRenderer>().sharedMaterial =
                UrpMaterial("Demo Ground", new Color(0.16f, 0.16f, 0.17f), smoothness: 0.1f);
        }

        static (GameObject shooter, GameObject victim) PlaceCharacters(
            GameObject shooterSource, GameObject victimSource, RuntimeAnimatorController controller)
        {
            var shooter = Spawn(shooterSource, "Gunman", new Vector3(0f, 0f, -Range * 0.5f),
                                controller);
            var victim = Spawn(victimSource, "Victim", new Vector3(0f, 0f, Range * 0.5f),
                               controller);

            // Facing each other. The gunman's aim is IK-driven and works from any heading, but
            // a shooter standing side-on to his target reads as an accident.
            shooter.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            victim.transform.rotation = Quaternion.LookRotation(Vector3.back);

            shooter.AddComponent<GunmanAim>();
            victim.AddComponent<PedestrianDeath>();

            return (shooter, victim);
        }

        static GameObject Spawn(GameObject source, string name, Vector3 position,
                                RuntimeAnimatorController controller)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = name;
            instance.transform.position = position;

            var animator = instance.GetComponent<Animator>();
            if (animator)
            {
                animator.runtimeAnimatorController = controller;

                // Same rule as every spawned pedestrian: the transform is driven by script, so
                // root motion would fight it. Here it also keeps the death clip from sliding
                // the body off its mark.
                animator.applyRootMotion = false;
            }

            return instance;
        }

        /// <summary>
        /// Gives the gunman the revolver AND attaches it there and then, so the scene opens with
        /// a visible gun in his hand rather than one that only appears in Play. Which is the
        /// point: the placement is finished by eye, and you cannot eye something you have to
        /// press Play to see.
        /// </summary>
        static WeaponSocket EquipRevolver(GameObject shooter, GameObject revolver)
        {
            var socket = shooter.AddComponent<WeaponSocket>();
            if (!revolver)
                return socket;

            CityEditorUtils.SetField(socket, "weaponPrefab", revolver);
            socket.Adopt(((GameObject)PrefabUtility.InstantiatePrefab(
                revolver, socket.Hand)).transform);

            return socket;
        }

        /// <summary>
        /// Three-quarter view from the gunman's side, low enough that the muzzle is against the
        /// victim's silhouette rather than against the ground.
        /// </summary>
        static void FrameCamera(Vector3 shooter, Vector3 victim)
        {
            var camera = Camera.main;
            if (!camera)
                camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();

            var midpoint = (shooter + victim) * 0.5f + Vector3.up * 1.1f;
            var axis = (victim - shooter).normalized;
            var side = Vector3.Cross(Vector3.up, axis);

            camera.transform.position = midpoint + side * 3.6f - axis * 1.2f + Vector3.up * 0.5f;
            camera.transform.rotation = Quaternion.LookRotation(midpoint - camera.transform.position);
            camera.fieldOfView = 45f;
        }

        static void SetUpKeyLight()
        {
            var light = Object.FindAnyObjectByType<Light>();
            if (!light)
            {
                var host = new GameObject("Directional Light");

                // GetComponent ?? AddComponent silently skips the add for native components in
                // Unity 6 - Light is one - so this stays a plain AddComponent on a fresh object.
                light = host.AddComponent<Light>();
            }

            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
        }

        /// <summary>
        /// Points the OBJ's three material slots at URP materials built from the colours the
        /// glTF carried, so the gun is not magenta.
        ///
        /// The names are the .mtl's newmtl entries, which is what the model importer looks a
        /// remap up by. All three are near-black gunmetal and differ by a few percent - the
        /// distinction is kept rather than collapsed to one material because it costs nothing
        /// and throwing away what the author shipped is not this script's call.
        /// </summary>
        static void RemapRevolverMaterials()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(RevolverPath);
            if (!importer)
                return;

            // Both set explicitly rather than left to the importer's defaults: with
            // materialImportMode None the mesh gets no material slots at all and the remaps
            // below have nothing to attach to, which looks identical to "the remap failed".
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

            var palette = new (string Name, Color Colour)[]
            {
                ("Grey", new Color(0.199f, 0.202f, 0.208f)),
                ("LightMetal", new Color(0.335f, 0.338f, 0.349f)),
                ("Metal", new Color(0.256f, 0.260f, 0.257f)),
            };

            foreach (var (name, colour) in palette)
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), name),
                    UrpMaterial($"Revolver {name}", colour, smoothness: 0.55f, metallic: 0.6f));

            importer.SaveAndReimport();
        }

        static Material UrpMaterial(string name, Color colour, float smoothness,
                                    float metallic = 0f)
        {
            Directory.CreateDirectory(WeaponMaterialDir);
            var path = $"{WeaponMaterialDir}/{name}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing)
                return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
            {
                Debug.LogError("[ShootingDemo] URP Lit shader not found - is the project on URP?");
                return null;
            }

            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
