using UnityEditor;
using UnityEngine;
using LivingCity.Entities;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Lets a weapon be placed by eye instead of by argument.
    ///
    /// Working out how a gun sits in a fist took several passes of deriving it from first
    /// principles, each of which was wrong in a new way - the hand bone's axes, the forearm
    /// line, the mesh's authored axis. The derivation is worth keeping because it gets any
    /// weapon onto any rig pointing roughly the right way, but the last centimetre is a
    /// judgement about how a hand looks closed around a grip, and that is not computable.
    ///
    /// So: Attach puts a real prefab instance in the scene, under the hand bone, saved with the
    /// scene. While Auto Place is on, the sliders drive it and you can watch it move. Turn Auto
    /// Place off and it is yours - drag it with the ordinary gizmo, and nothing will move it
    /// again.
    /// </summary>
    [CustomEditor(typeof(WeaponSocket))]
    public sealed class WeaponSocketEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var socket = (WeaponSocket)target;

            DrawDefaultInspector();

            // Deliberately NOT live. The grip values are the authored answer, not a slider to
            // ride: re-placing on every inspector change is what used to snap a hand-placed
            // weapon back the moment anything in this component was touched. Re-place is a
            // button now, so it only happens when it is asked for.
            EditorGUILayout.Space();

            if (!socket.Weapon)
                DrawAttach(socket);
            else
                DrawAttached(socket);
        }

        void DrawAttach(WeaponSocket socket)
        {
            using (new EditorGUI.DisabledScope(!socket.Prefab))
            {
                if (GUILayout.Button("Attach weapon", GUILayout.Height(24f)))
                    Attach(socket);
            }

            if (!socket.Prefab)
                EditorGUILayout.HelpBox("Assign a weapon prefab first.", MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "Puts the weapon in the right hand as a real scene object, so you can see " +
                    "and move it without pressing Play.", MessageType.None);
        }

        void DrawAttached(WeaponSocket socket)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select in scene", GUILayout.Height(22f)))
                    Selection.activeGameObject = socket.Weapon.gameObject;

                if (GUILayout.Button("Re-place", GUILayout.Height(22f)))
                {
                    Undo.RecordObject(socket.Weapon, "Re-place weapon");
                    socket.Reposition();
                    Mark(socket);
                }

                if (GUILayout.Button("Remove", GUILayout.Height(22f)))
                    Remove(socket);
            }

            EditorGUILayout.HelpBox(
                "The weapon is yours: drag it in the Scene view and it stays, in the Editor and " +
                "in Play alike.\n\nRe-place snaps it back to the Grip Position and Grip Rotation " +
                "above - use it to start over, or after copying new values in.",
                MessageType.None);

            EditorGUILayout.Space();
            DrawMuzzle(socket);

            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox(
                    "You are in Play mode. Unity throws away every scene change made here when " +
                    "you stop - that is Unity, not this component. Adjust the weapon with Play " +
                    "OFF, or copy the values out before stopping.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox(
                    "Where you leave the weapon now is where it will be in Play. Save the " +
                    "scene to keep it.", MessageType.None);
        }

        /// <summary>
        /// The muzzle is a point in mid-air, so it gets the same treatment the weapon got: a
        /// real object you can grab. The measured tip is only ever an approximation - it comes
        /// from mesh bounds, which put it a few centimetres under the bore on this revolver -
        /// and no amount of cleverness beats dragging it once.
        /// </summary>
        void DrawMuzzle(WeaponSocket socket)
        {
            if (!socket.Weapon)
                return;

            if (GUILayout.Button(socket.MuzzleMarker ? "Select muzzle point" : "Create muzzle point"))
            {
                if (socket.MuzzleMarker)
                    Reveal(socket.MuzzleMarker);
                else
                    CreateMuzzle(socket);
            }

            // The measured tip, printed. If this reads all zeroes the mesh measurement found
            // nothing and the marker was created down in the grip, inside the fist - which
            // looks exactly like "the marker is invisible" and is worth being able to tell
            // apart at a glance.
            socket.Measure();
            var local = socket.Weapon.InverseTransformPoint(socket.MeasuredMuzzle);

            EditorGUILayout.LabelField("Measured barrel tip", local.ToString("F4"));

            if (local == Vector3.zero)
                EditorGUILayout.HelpBox(
                    "The barrel tip measured to zero, so the weapon's mesh could not be read " +
                    "and any marker will sit in the grip, buried in the hand. Check the weapon " +
                    "prefab actually has a mesh.", MessageType.Warning);

            EditorGUILayout.HelpBox(
                socket.MuzzleMarker
                    ? "The flash comes from the marker. Select it and drag it to the end of the " +
                      "barrel - it is labelled 'Muzzle' in the Scene view and draws over the " +
                      "geometry, so it stays findable even if it ends up inside the model."
                    : "No marker, so the flash uses the barrel tip measured off the mesh. That " +
                      "is a few centimetres under the bore. Create one to place it by hand.",
                MessageType.None);
        }

        /// <summary>
        /// Selects a marker, frames it, and gives it its gizmo if it has not got one - markers
        /// made before MuzzlePoint existed are bare GameObjects, and a bare GameObject is
        /// invisible in the Scene view. Repairing in place beats telling someone to delete the
        /// thing they just positioned.
        /// </summary>
        static void Reveal(Transform marker)
        {
            if (!marker.GetComponent<MuzzlePoint>())
            {
                Undo.AddComponent<MuzzlePoint>(marker.gameObject);
                EditorUtility.SetDirty(marker.gameObject);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
            }

            Selection.activeGameObject = marker.gameObject;

            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.Frame(
                    new Bounds(marker.position, Vector3.one * 0.35f), instant: false);
        }

        static void CreateMuzzle(WeaponSocket socket)
        {
            // Measured first: in edit mode Start has never run, so without this the marker
            // would be created at the weapon's origin - down in the grip.
            socket.Measure();

            // MuzzlePoint draws the marker. Without it this is a bare GameObject, which has no
            // renderer and no gizmo - visible in the Hierarchy and nowhere else.
            var marker = new GameObject("Muzzle", typeof(MuzzlePoint));
            Undo.RegisterCreatedObjectUndo(marker, "Create muzzle point");

            marker.transform.SetParent(socket.Weapon, worldPositionStays: false);
            marker.transform.position = socket.MeasuredMuzzle;
            marker.transform.rotation = Quaternion.LookRotation(socket.BarrelDirection);

            socket.AdoptMuzzle(marker.transform);

            Mark(socket);
            Selection.activeGameObject = marker;

            // Frames it in the Scene view. A 25cm revolver on a 1.8m character parented three
            // levels down a skeleton is findable in the Hierarchy and hopeless by eye, so the
            // view is taken there rather than left as an instruction to press F.
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.Frame(
                    new Bounds(marker.transform.position, Vector3.one * 0.35f), instant: false);
        }

        static void Attach(WeaponSocket socket)
        {
            var hand = socket.Hand;
            if (!hand)
                return;

            // A prefab INSTANCE rather than a plain copy, so the link back to the asset
            // survives - re-importing the model then updates the weapon in every scene.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(socket.Prefab, hand);
            Undo.RegisterCreatedObjectUndo(instance, "Attach weapon");

            socket.Adopt(instance.transform);

            Mark(socket);
            Selection.activeGameObject = instance;
        }

        static void Remove(WeaponSocket socket)
        {
            var instance = socket.Weapon.gameObject;
            socket.Release();
            Undo.DestroyObjectImmediate(instance);
            Mark(socket);
        }

        /// <summary>
        /// Marks BOTH the component and the scene. The component alone is not enough: the
        /// weapon's transform lives on another object, and a scene that is not dirty is a scene
        /// Unity will not offer to save - which is how careful placement gets lost on the next
        /// domain reload.
        /// </summary>
        static void Mark(WeaponSocket socket)
        {
            EditorUtility.SetDirty(socket);
            if (socket.Weapon)
                EditorUtility.SetDirty(socket.Weapon);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(socket.gameObject.scene);
        }
    }
}
