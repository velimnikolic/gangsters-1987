using UnityEngine;

namespace LivingCity.Ambient
{
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
    /// It also means CityConfig.industrialSmoke off costs nothing: the markers sit there inert
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

        /// <summary>Where the plumes actually start, in world space.</summary>
        public void MouthsWorld(System.Collections.Generic.List<Vector3> into)
        {
            into.Clear();

            if (mouths == null)
                return;

            foreach (var mouth in mouths)
                into.Add(transform.TransformPoint(mouth));
        }
    }
}
