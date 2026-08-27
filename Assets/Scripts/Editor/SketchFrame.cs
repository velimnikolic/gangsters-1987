using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// What every sketch needs once it has drawn: where the rest of the scene stands, so
    /// the drawing can be moved clear of it; a card over it saying what it was meant to
    /// be; and the Scene view turned onto it. One copy, because the four sketches each
    /// grew their own and drifted - the core's measured only the active scene, and with
    /// two scenes loaded (the editor here habitually has the harvest scene open beside a
    /// lab) its drawing stood clear of its own scene and straight through the other's.
    /// </summary>
    internal static class SketchFrame
    {
        /// <summary>
        /// Everything already standing, the drawing itself excepted - across EVERY loaded
        /// scene, not only the active one.
        ///
        /// A particle system is skipped: until it plays, its renderer reports whatever
        /// bounds it happens to hold - usually an empty box at the world origin - and one
        /// plume of chimney smoke would drag the measurement across the whole map.
        /// </summary>
        public static bool Extent(string sketchRoot, out Bounds box)
        {
            box = new Bounds();
            bool any = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (!open.isLoaded) continue;
                foreach (var root in open.GetRootGameObjects())
                {
                    if (root.name == sketchRoot) continue;
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer is ParticleSystemRenderer) continue;
                        if (!any) { box = renderer.bounds; any = true; }
                        else box.Encapsulate(renderer.bounds);
                    }
                }
            }
            return any;
        }

        /// <summary>A card over the drawing, tipped to be read from the south and above -
        /// the way the view is framed.</summary>
        public static void Caption(string name, string text, Vector3 position, Transform parent)
        {
            BlockLotPads.PadLabel(name, text, position, parent);
            var caption = parent.Find(name);
            if (caption) caption.rotation = Quaternion.Euler(35f, 180f, 0f);
        }

        /// <summary>Turns the Scene view onto the drawing, from the south and above.
        /// <paramref name="size"/> is the view's size at the centre; <paramref name="perspective"/>
        /// forces the view out of orthographic, for a drawing whose skyline is the point.</summary>
        public static void Frame(Vector3 centre, float size, float pitch = 55f, bool perspective = false)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            var direction = Quaternion.Euler(pitch, 0f, 0f);
            if (perspective) view.LookAt(centre, direction, size, false);
            else view.LookAt(centre, direction, size);
        }
    }
}
