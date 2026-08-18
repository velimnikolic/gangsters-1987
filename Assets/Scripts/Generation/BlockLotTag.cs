using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// The lot pad a baked block was composed on, stamped onto the bake itself.
    ///
    /// A block is authored against a pad of a definite size - "Lot B2 (85x70)" - and
    /// that pad is the whole reason its buildings stand where they do. The recipe
    /// records it (BlockRecipeData.lot), but recipes live in the editor assembly and
    /// the thing that has to know is the city builder, which reads prefabs. So the
    /// bake carries the answer on its root: no lookup table to keep in step, and a
    /// bake dragged into any scene by hand still says which interior it belongs in.
    ///
    /// No tag = a bake that belongs to no particular lot: the generic terrace that
    /// goes wherever it fits, and the auto-extracted PalmBlock candidates.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockLotTag : MonoBehaviour
    {
        [Tooltip("Catalog pad code the block was composed on, e.g. \"B2\" - a letter " +
                 "for the width column and a number for the depth row.")]
        public string lot;

        [Tooltip("The pad's own size in metres. The bake may measure smaller (it need " +
                 "not fill its lot) but never larger.")]
        public float lotWidth;
        public float lotDepth;
    }
}
