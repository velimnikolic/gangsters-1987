using UnityEngine;

namespace RoadDemo
{
    /// <summary>How much ground the ripped POLYGON City demo town covers, stamped on
    /// the prefab itself by the ripper (SyntyDemoBlockRip).
    ///
    /// Whatever stands the town has to know its span before it instantiates it - to
    /// leave the hole in its own grid, to flatten the ground under it and to keep the
    /// wilderness off it - and measuring a five-thousand-piece prefab at load time
    /// costs a second. The ripper knows the answer; it writes it down.</summary>
    [DisallowMultipleComponent]
    public sealed class SyntyTownMark : MonoBehaviour
    {
        [Tooltip("Width (X) and depth (Z) in metres of the ground the town covers.")]
        public Vector2 span;
    }
}
