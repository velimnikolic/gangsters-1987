using RoadDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BikeDemo
{
    /// <summary>
    /// The sitting scene: a machine with two men on it, saved into the scene file as
    /// plain objects, posed, with nothing running.
    ///
    /// The live bench (BikeDemoBuilder) is the right tool for someone who wants to watch
    /// a number change the pose. It is the wrong tool for someone who just wants to grab
    /// the man and pull him down, because the pose re-seats him every frame and a drag
    /// snaps straight back - and because none of it exists until Play is pressed.
    ///
    /// So this bakes instead. The pose is applied ONCE, in the editor, and then the
    /// components that would keep applying it are removed: what is left is a motorcycle
    /// and two men, sitting, that can be selected and dragged like any other object and
    /// that are there when the scene is opened. Nothing has to be pressed to see them.
    ///
    /// The way back is the second menu item. Drag a man where he belongs, ask for him to
    /// be read back, and the difference between where he is and where the code put him
    /// is printed as the line to paste into BikeBody - and the stand is re-baked with it,
    /// so the pose that follows is a proper one and not a dragged one.
    /// </summary>
    static class BikeSitBaker
    {
        const string ScenePath = "Assets/Scenes/BikeSit.unity";
        const string Driver = "DRIVER";
        const string Shooter = "SHOOTER";

        // Cmd+Alt+B (Ctrl+Alt+B on Windows). A menu three levels deep is one level too
        // many when the whole point of the scene is that it takes no finding.
        [MenuItem("Tools/Bike bench/Sit two men on a bike (no Play) %&b", priority = 20)]
        static void MakeMotorbike() => Make("SM_Veh_Motorbike_01");

        [MenuItem("Tools/Bike bench/Sit them on the moped instead", priority = 21)]
        static void MakeMoped() => Make("SM_Veh_Moped_01");

        [MenuItem("Tools/Bike bench/Sit them on the police tourer instead", priority = 22)]
        static void MakePolice() => Make("SM_Veh_Motorbike_02");

        [MenuItem("Tools/Bike bench/Sit them on the outfit's black tourer", priority = 23)]
        static void MakeGangTourer() => Make("SM_Veh_Motorbike_Tourer_Black");

        // ------------------------------------------------------------------ the baking

        static void Make(string machine)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Ground();
            Sun();
            Eye();
            if (!Stand(machine, Vector3.zero, Vector3.zero, 0f, 0f)) return;

            EditorSceneManager.SaveScene(scene, ScenePath);
            Look();
            Debug.Log("[BikeSit] " + machine + " with two men on it, saved to " + ScenePath +
                      ".\nNothing runs in this scene. Click " + Shooter + " in the Hierarchy, drag " +
                      "him where he belongs (W is the move tool), and then either save the " +
                      "scene - the drag is readable straight out of the file - or use " +
                      "Tools > Bike bench > I moved him - read it back, which also puts his " +
                      "arms back on the machine.\nDRIVER and SHOOTER are at the TOP of the " +
                      "Hierarchy, on their own lines. Click the name, not the picture.");
        }

        /// <summary>The machine and the two men, posed once and then left alone. False
        /// when a piece is missing - the reason is logged, and the scene is left with
        /// whatever did build so it is obvious what turned up.</summary>
        static bool Stand(string machine, Vector3 riderNudge, Vector3 pillionNudge,
                          float riderSize, float pillionSize)
        {
            var prefab = Machine(machine);
            if (prefab == null)
            {
                Debug.LogError("[BikeSit] no two-wheeler called '" + machine + "'.");
                return false;
            }

            var bike = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(bike, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            bike.name = machine;
            bike.transform.position = Vector3.zero;
            foreach (var col in bike.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

            // the pose is derived off the machine's own measurements, and the nudge is
            // part of that derivation - so it goes in before the body is read
            BikeBody.RiderNudge = riderNudge;
            BikeBody.PillionNudge = pillionNudge;
            var body = new BikeBody(bike.transform);

            var tag = bike.AddComponent<BikeSitTag>();
            tag.machine = machine;
            tag.riderNudge = riderNudge;
            tag.pillionNudge = pillionNudge;

            var clips = CrewKit.Clips();
            var sit = CrewKit.Ride != null ? CrewKit.Ride : clips.SitLoop;
            var (driverBody, shooterBody) = Faces();
            if (driverBody == null)
            {
                Debug.LogError("[BikeSit] no gang body to sit on it.");
                return false;
            }

            // the driver first and STRIPPED LAST: the shooter's hands go on the man in
            // front of him, which BikePose asks the rider's own pose for - take the
            // driver's components off before the shooter is posed and the shooter holds
            // on to a saddle point instead of a man
            tag.riderSize = Sit(body, bike.transform, driverBody, sit, false, Driver, riderSize, null);
            tag.pillionSize = Sit(body, bike.transform, shooterBody != null ? shooterBody : driverBody,
                sit, true, Shooter, pillionSize, body.Tf);
            // written down AFTER the pose has moved them: Seat() shifts a man's root by
            // however far his pelvis sat off it in the clip, so where he ends up is not
            // where he was put. That landing spot is what a later drag is measured
            // against - and it is recorded here so the measuring can be done by READING
            // THE SAVED SCENE, with no Unity in the loop at all.
            var driverGo = GameObject.Find(Driver);
            var shooterGo = GameObject.Find(Shooter);
            var driverTf = driverGo != null ? driverGo.transform : null;
            var shooterTf = shooterGo != null ? shooterGo.transform : null;
            // world, which for a scene root IS m_LocalPosition in the file - the same
            // number the reader picks up, so the two never disagree
            if (driverTf != null) tag.riderAtBake = driverTf.position;
            if (shooterTf != null) tag.pillionAtBake = shooterTf.position;
            tag.wheelbase = body.Wheelbase;
            tag.wheelRadius = body.WheelRadius;
            tag.gripY = body.GripRight.y;
            tag.saddleRider = body.SaddleRider;
            tag.saddlePillion = body.SaddlePillion;

            Strip(driverTf);
            Strip(shooterTf);
            Debug.Log("[BikeSit] " + machine + ": wheelbase " + body.Wheelbase.ToString("0.00") +
                      ", wheel r " + body.WheelRadius.ToString("0.00") +
                      ", grip y " + body.GripRight.y.ToString("0.00") +
                      "   saddle driver y " + body.SaddleRider.y.ToString("0.00") +
                      " z " + body.SaddleRider.z.ToString("0.00") +
                      "   shooter y " + body.SaddlePillion.y.ToString("0.00") +
                      " z " + body.SaddlePillion.z.ToString("0.00"));
            return true;
        }

        /// <summary>One man, sat and then set in stone: the seated clip sampled onto him so
        /// his pelvis is down, the pose written over the top of it once, and then every
        /// component that would write it again taken off. What is left is a posed body -
        /// draggable, selectable, saved with the scene, and inert.</summary>
        static float Sit(BikeBody body, Transform bike, GameObject prefab, AnimationClip sit,
                         bool pillion, string label, float size, Transform aimFrom)
        {
            // A ROOT OF THE SCENE, not a child of the machine. Parented under the bike he
            // is buried three levels down a hierarchy nobody wants to open, and a click in
            // the viewport lands on whichever mesh of him it hit rather than on the man -
            // which is why "I still could not drag them along the xyz axes". At the top of
            // the Hierarchy, DRIVER and SHOOTER are two names on their own line: click
            // either, press W, and the handle is on him. The machine stands at the origin
            // unrotated, so nothing is lost by unparenting - local and world are the same
            // frame, which is also what lets the saved scene be read as text.
            var go = Object.Instantiate(prefab);
            go.name = label;
            foreach (var col in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Object.DestroyImmediate(mb);
            go.transform.position = pillion ? body.Saddle(true) : body.Saddle(false);
            go.transform.rotation = bike.rotation;

            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                // The seated base, without Play: SampleAnimation writes one frame of the
                // clip onto the rig then and there. Without it the man is in his bind
                // pose - the pose would still put his fists and boots right, but his
                // pelvis would be a standing man's, and it shows.
                if (sit != null)
                {
                    try { sit.SampleAnimation(go, 0f); }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("[BikeSit] could not sample " + sit.name + " onto " +
                                         label + " (" + e.Message + ") - he sits in his bind pose.");
                    }
                }
            }

            var pose = go.AddComponent<BikePose>();
            float fitted = 1f;
            if (pose.Setup(body, pillion, animator))
            {
                if (size > 0.01f) go.transform.localScale = Vector3.one * size;
                fitted = go.transform.localScale.x;
                pose.Speed = 0f;
                pose.FootDown = !pillion;
                // the shooter with his piece out to the right of the machine, which is
                // the drive-by side and the pose worth looking at
                pose.AimAt = pillion && aimFrom != null
                    ? aimFrom.position + aimFrom.right * 9f + Vector3.up * 1.15f
                    : (Vector3?)null;
                if (pillion)
                {
                    var rider = GameObject.Find(Driver);
                    var riderPose = rider != null ? rider.GetComponent<BikePose>() : null;
                    if (riderPose != null) pose.Rider = riderPose;
                }
                Gun(pose, animator, pillion);
                pose.Apply();
            }
            else
            {
                Debug.LogWarning("[BikeSit] " + prefab.name + " is not a humanoid rig - " +
                                 label + " is left standing.");
            }

            return fitted;
        }

        /// <summary>Nothing writes him again: what is left is a posed body and no more.
        /// This is the whole difference between the live bench and this scene - there, a
        /// drag is undone by the next frame; here there is no next frame.</summary>
        static void Strip(Transform man)
        {
            if (man == null) return;
            foreach (var mb in man.GetComponentsInChildren<MonoBehaviour>()) Object.DestroyImmediate(mb);
        }

        static void Gun(BikePose pose, Animator animator, bool pillion)
        {
            if (!pillion || animator == null) return;
            var weapon = CrewKit.Weapon("SM_Wep_Machine_Pistol_01");
            if (weapon != null) CrewArms.Attach(animator, weapon);
        }

        // ------------------------------------------------------------------ the way back

        [MenuItem("Tools/Bike bench/I moved him - read it back", priority = 30)]
        static void ReadBack()
        {
            var tag = Object.FindAnyObjectByType<BikeSitTag>();
            if (tag == null)
            {
                Debug.LogWarning("[BikeSit] this is not a sitting scene - " +
                                 "Tools > Bike bench > Sit two men on a bike.");
                return;
            }

            var bike = tag.transform;
            // read off the note BEFORE the stand comes down: a field on a destroyed
            // component throws, and the rebuild below needs all three
            string machine = tag.machine;
            var bakedRider = tag.riderNudge;
            var bakedPillion = tag.pillionNudge;
            BikeBody.RiderNudge = tag.riderNudge;
            BikeBody.PillionNudge = tag.pillionNudge;
            var body = new BikeBody(bike);

            // measured off where each man was LEFT at bake time, not off the saddle:
            // that is the same sum anyone reading the saved scene file would do, and two
            // ways of answering one question is one way too many
            var rider = Moved(bike, Driver, tag.riderAtBake, bakedRider, out bool riderMoved);
            var pillion = Moved(bike, Shooter, tag.pillionAtBake, bakedPillion, out bool pillionMoved);

            if (!riderMoved && !pillionMoved)
            {
                Debug.Log("[BikeSit] neither of them has been moved. Select " + Shooter +
                          " in the Hierarchy and drag him (W is the move tool), then ask again.");
                return;
            }

            Debug.Log("[BikeSit] paste this into Assets/RoadDemo/BikeBody.cs, over the line " +
                      "that reads 'public static Vector3 RiderNudge, PillionNudge;':\n" +
                      "public static Vector3 RiderNudge = " + V(rider) +
                      ", PillionNudge = " + V(pillion) + ";\n" +
                      "// A nudge fits THIS machine (" + machine + ") and no other. Where the " +
                      "same error shows on every machine, the honest fix is one of the proportions " +
                      "above it - SaddleAboveWheel first, it is the only one driven by a measured number.");

            // and stood up again properly: a dragged man is a man in the right place with
            // the wrong arms - his fists left the bars when he moved. Re-baking puts the
            // whole pose back together round where he now sits.
            var scene = tag.gameObject.scene;
            Object.DestroyImmediate(tag.gameObject);
            foreach (var name in new[] { Driver, Shooter })
            {
                var stray = GameObject.Find(name);
                if (stray) Object.DestroyImmediate(stray);
            }
            if (Stand(machine, rider, pillion, 0f, 0f))
            {
                EditorSceneManager.SaveScene(scene, ScenePath);
                Look();
            }
        }

        /// <summary>How far a man has been dragged off where the bake left him, added to
        /// the nudge he was baked with. Measured on his ROOT against the recorded landing
        /// spot rather than on his hips against the saddle - the two differ by the clip's
        /// pelvis offset, which cancels exactly when both sides are roots, and only the
        /// root side can be read out of a text file.</summary>
        static Vector3 Moved(Transform bike, string label, Vector3 atBake, Vector3 baked, out bool moved)
        {
            moved = false;
            var go = GameObject.Find(label);
            if (go == null) return baked;
            var delta = go.transform.position - atBake;
            if (delta.sqrMagnitude < 1e-6f) return baked;
            moved = true;
            return baked + delta;
        }

        static string V(Vector3 v) =>
            "new Vector3(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        static string F(float v) => v.ToString("0.###") + "f";

        // ------------------------------------------------------------------ the pieces

        static GameObject Machine(string name)
        {
            foreach (var b in StreetBikes.Bodies())
                if (b != null && b.name == name) return b;
            var police = StreetBikes.PoliceBody();
            if (police != null && police.name == name) return police;
            // and the outfit's own bodies, which are in no pack and on no street: the
            // black tourer is a machine this project cut for itself and the catalogue
            // above has never heard of it (CrewCars asks Assets/Prefabs/Vehicles first).
            return CrewCars.BodyNamed(name);
        }

        static (GameObject driver, GameObject shooter) Faces()
        {
            GameObject driver = null, shooter = null;
            var looks = LivingCity.Gangs.GangLooks.Hoods;
            for (int i = 0; i < looks.Length && (driver == null || shooter == null); i++)
            {
                var body = LivingCity.UI.LedgerModelSet.PersonNamed(looks[i]) ??
                           LivingCity.UI.PortraitStudio.FindPeoplePrefab(looks[i]);
                if (body == null) continue;
                if (driver == null) driver = body;
                else if (body != driver) shooter = body;
            }
            return (driver, shooter);
        }

        static void Ground()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.localScale = Vector3.one * 2f;
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return;
            var mat = new Material(shader) { name = "Bike Sit Ground" };
            mat.SetColor("_BaseColor", new Color(0.24f, 0.25f, 0.27f));
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>A camera looking at them. The scene is not for playing, but an empty
        /// scene with no camera in it says "no cameras rendering" the moment anybody does
        /// press Play, and that reads as a broken scene.</summary>
        static void Eye()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.52f, 0.62f, 0.72f);
            go.transform.SetPositionAndRotation(new Vector3(2.6f, 1.5f, -2.6f),
                Quaternion.Euler(14f, -45f, 0f));
        }

        static void Sun()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, 148f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.44f, 0.46f, 0.50f);
        }

        /// <summary>Point the Scene view at them and select the shooter, because he is the
        /// one being argued with. Half the trouble this scene answers was never the pose -
        /// it was not being able to find it.</summary>
        static void Look()
        {
            var tag = Object.FindAnyObjectByType<BikeSitTag>();
            if (tag == null) return;
            var box = new Bounds(tag.transform.position + Vector3.up * 0.8f, Vector3.one);
            bool any = false;
            // the machine AND the two men - they are roots of their own now, so asking
            // the bike for its children would frame the view on an empty motorcycle
            foreach (var go in new[] { tag.gameObject, GameObject.Find(Driver), GameObject.Find(Shooter) })
            {
                if (go == null) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (any) box.Encapsulate(r.bounds);
                    else { box = r.bounds; any = true; }
                }
            }
            box.Expand(0.7f);
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.Frame(box, false);
            var shooter = GameObject.Find(Shooter);
            if (shooter != null) Selection.activeGameObject = shooter;
        }
    }
}
