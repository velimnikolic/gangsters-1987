using UnityEngine;

namespace BikeDemo
{
    /// <summary>
    /// A note left on a baked sitting scene: which machine it is, and what nudge the two
    /// men were sat with when the pose was written into the scene.
    ///
    /// It does nothing - no Update, no Awake, nothing at Play. It exists so the men can
    /// be DRAGGED. A man moved by hand has to be measured against where he was put, and
    /// where he was put depends on numbers that live in code and may have been changed
    /// since; the note is the only way to know the difference between "he was baked at
    /// zero and dragged 12 cm down" and "he was baked 12 cm down already".
    /// </summary>
    public sealed class BikeSitTag : MonoBehaviour
    {
        [Tooltip("The machine this stand was baked around.")]
        public string machine = "";
        [Tooltip("The nudge each man was baked with, in the machine's frame - what " +
                 "BikeBody.RiderNudge / PillionNudge held at the time.")]
        public Vector3 riderNudge, pillionNudge;
        [Tooltip("The size each man was baked at - BikePose fits them to the saddle-to-peg " +
                 "span, and a man scaled by hand afterwards is measured from his own hips " +
                 "either way.")]
        public float riderSize = 1f, pillionSize = 1f;

        /// <summary>WHERE EACH MAN'S ROOT WAS LEFT when the pose was baked, in the
        /// machine's own frame. This is what a drag is measured against, and it is
        /// written down rather than recomputed for one reason: it makes the answer
        /// READABLE FROM THE SAVED SCENE FILE without Unity. Drag a man, save, and the
        /// arithmetic is
        ///
        ///     nudge = (riderNudge or pillionNudge) + (his m_LocalPosition now - this)
        ///
        /// because the machine stands at the origin unrotated, so local is world, and a
        /// drag moves his hips by exactly what it moves his root by. Recomputing it
        /// instead would mean measuring the machine's meshes again, which is the one
        /// thing that cannot be done by reading a text file.</summary>
        public Vector3 riderAtBake, pillionAtBake;

        [Tooltip("What the machine measured when the stand was baked - the numbers that " +
                 "decide where the saddle lands. 'wheel r' is the one to check first when a " +
                 "man floats: the saddle may never sink below it plus SaddleAboveWheel.")]
        public float wheelbase, wheelRadius, gripY;
        [Tooltip("Where the code put the two saddles, in the machine's frame - the hips " +
                 "targets the pose reached for.")]
        public Vector3 saddleRider, saddlePillion;
    }
}
