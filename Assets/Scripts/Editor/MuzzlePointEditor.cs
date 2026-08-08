using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using LivingCity.Entities;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Draws the muzzle marker THROUGH whatever is in front of it.
    ///
    /// The gizmo on MuzzlePoint itself is depth-tested, like every gizmo, so a marker sitting
    /// anywhere inside the weapon or the fist is drawn and then covered by the mesh - invisible
    /// in a way indistinguishable from not existing. That is the failure this fixes: Handles
    /// with zTest Always paint over the geometry, so the marker can be found even when it has
    /// ended up somewhere wrong, which is exactly when finding it matters.
    ///
    /// It also labels it, because a coloured dot on a character covered in coloured dots is not
    /// self-explanatory.
    /// </summary>
    [CustomEditor(typeof(MuzzlePoint))]
    public sealed class MuzzlePointEditor : Editor
    {
        void OnSceneGUI()
        {
            var muzzle = (MuzzlePoint)target;
            var here = muzzle.transform.position;

            var previous = Handles.zTest;
            Handles.zTest = CompareFunction.Always;

            Handles.color = new Color(1f, 0.8f, 0.3f);

            // Sized in screen space, so it stays the same on screen however far the camera is -
            // a world-space radius is either a speck or a beach ball depending on the zoom.
            var size = HandleUtility.GetHandleSize(here);
            Handles.SphereHandleCap(0, here, Quaternion.identity, size * 0.08f, EventType.Repaint);
            Handles.DrawWireDisc(here, muzzle.transform.forward, size * 0.2f);

            Handles.DrawAAPolyLine(3f, here, here + muzzle.transform.forward * size * 1.5f);

            Handles.Label(here + Vector3.up * size * 0.25f, "Muzzle");

            Handles.zTest = previous;
        }
    }
}
