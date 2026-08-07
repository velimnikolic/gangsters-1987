using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Ambient
{
    /// <summary>
    /// What kind of chimney a vent is, which is what decides how it smokes. A works stack and a
    /// terrace flue are the same mechanism at completely different scales, and one set of plume
    /// numbers cannot serve both: at the works size a house looks like it is on fire, and at the
    /// house size a factory looks like it has gone out.
    /// </summary>
    public enum VentKind
    {
        /// <summary>A works chimney - the tall stacks on the industrial catalogue.</summary>
        Works,

        /// <summary>A domestic flue on a terrace or a house.</summary>
        House,
    }

    /// <summary>
    /// A chimney mouth, marked during generation and left in the saved scene for
    /// <see cref="SmokeStackSystem"/> to find at run time.
    ///
    /// Why a marker instead of the particle system itself. The city is generated in the editor
    /// and SAVED into the scene, and CityEditorUtils.MarkStaticForBatching then stamps
    /// BatchingStatic on every transform under the generated root. A baked ParticleSystem would
    /// be flagged static and its plume would never leave the chimney. Marking the position
    /// instead keeps generation deterministic and cheap - one component, no renderer, nothing to
    /// serialise - and leaves the moving part to run time, which is the same split CloudSystem
    /// and BirdFlockSystem already use.
    ///
    /// It also means CityConfig.chimneySmoke off costs nothing: the markers sit there inert
    /// rather than a hundred dead particle systems being loaded with the scene.
    ///
    /// The component sits ON the building instance, so a hall that gets moved, rotated or scaled
    /// carries its chimney with it and the world position stays correct without a rebuild.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SmokeVent : MonoBehaviour
    {
        [Tooltip("Chimney mouths in this instance's local space - a prefab may have several, and " +
                 "industry-factory is twin-stacked. Measured by Tools/City/Measure Chimney Vents " +
                 "and stored on the PrefabDatabase; this is the copy that rides the instance.")]
        public Vector3[] mouths = System.Array.Empty<Vector3>();

        [Tooltip("Which plume profile SmokeStackSystem raises here. Set at generation time from " +
                 "which catalogue the prefab came out of.")]
        public VentKind kind = VentKind.Works;

        /// <summary>
        /// Leaves a vent on an instance whose prefab has measured chimney mouths, so the runtime
        /// system can raise a plume there. A no-op for the majority of what gets built - a
        /// warehouse does not smoke and neither does a kiosk - which is why every spawn path can
        /// call it unconditionally.
        ///
        /// Generation only records that a chimney EXISTS. Which ones actually smoke, and how many,
        /// is decided at run time from the seed - see SmokeStackSystem. That split is what keeps a
        /// city of a hundred terraces from baking a hundred particle systems into the scene.
        /// </summary>
        public static void Mark(
            GameObject instance, GameObject prefab, PrefabDatabase prefabs, VentKind kind)
        {
            if (!instance || !prefab || !prefabs)
                return;

            var mouths = new List<Vector3>();
            prefabs.ChimneyVentsFor(prefab, mouths);

            if (mouths.Count == 0)
                return;

            var vent = instance.AddComponent<SmokeVent>();
            vent.mouths = mouths.ToArray();
            vent.kind = kind;
        }

        /// <summary>Where the plumes actually start, in world space.</summary>
        public void MouthsWorld(List<Vector3> into)
        {
            into.Clear();

            if (mouths == null)
                return;

            foreach (var mouth in mouths)
                into.Add(transform.TransformPoint(mouth));
        }
    }
}
