using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Hands out building pieces for one lot, drawing from EVERY group in a zone's palette at
    /// once.
    ///
    /// This is the fix for the city's most obvious tell. The old builder rolled a single group
    /// per block and then emptied one ShuffleBag around its whole perimeter, so a block came out
    /// uniformly 4-storey or uniformly 5-storey and - with only two groups in the database -
    /// every block in the city was one of two things. Here the roll happens per SLOT, so one
    /// street wall carries a mix, and a low-weight shop group in a residential palette puts the
    /// odd cafe between the flats rather than quarantining shops in a zone of their own.
    ///
    /// Two biases sit on top of the plain weighted roll, and both exist to stop the mixing
    /// producing something worse than what it replaced:
    ///
    /// - Alley-facing runs steer away from terrace kits that ship no -back variant. The 5-floor
    ///   kit is one, and PiecesFor falls back to its street pieces, so an unbiased draw would
    ///   line the service alley with shopfronts.
    /// - The slot beside a street corner steers TOWARD groups marked cornerPreferred. In the
    ///   period this is where the tavern and the corner store went, and it is the single detail
    ///   that stops a residential block reading as barracks.
    /// </summary>
    public sealed class LotKit
    {
        /// <summary>
        /// How hard an alley-facing run avoids a kit with no rear elevation. Not zero: a lot can
        /// have nothing else on offer, and a repeated front is still better than a hole.
        /// </summary>
        const float NoRearPenalty = 0.08f;

        /// <summary>Weight multiplier for a cornerPreferred group when the corner slot asks for one.</summary>
        const float CornerBoost = 12f;

        readonly List<PrefabDatabase.WeightedGroup> groups = new();
        readonly List<ShuffleBag> streetBags = new();
        readonly List<ShuffleBag> rearBags = new();
        readonly List<ShuffleBag> cornerBags = new();
        readonly System.Random rng;
        readonly float[] weights;

        public LotKit(PrefabDatabase.WeightedGroup[] source, System.Random rng)
        {
            this.rng = rng;

            if (source != null)
                foreach (var group in source)
                {
                    if (group == null || !group.IsUsable)
                        continue;

                    groups.Add(group);
                    streetBags.Add(new ShuffleBag(group.PiecesFor(true), rng));
                    rearBags.Add(new ShuffleBag(group.PiecesFor(false), rng));
                    cornerBags.Add(new ShuffleBag(group.cornerPrefabs, rng));

                    TotalPieces += group.prefabs.Length;
                    HasCornerPreferred |= group.cornerPreferred;
                }

            weights = new float[groups.Count];
        }

        public bool IsEmpty => groups.Count == 0;

        /// <summary>Every street piece across every group - the natural budget for a fit search.</summary>
        public int TotalPieces { get; }

        public bool HasCornerPreferred { get; }

        public PrefabDatabase.WeightedGroup GroupAt(int index) => groups[index];

        /// <summary>
        /// Index of the group this slot should draw from, or -1 when the kit is empty.
        /// </summary>
        public int PickGroup(bool facesStreet, bool preferCorner)
        {
            if (groups.Count == 0)
                return -1;

            var total = 0f;
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                var weight = group.weight;

                // Only a terrace can have a blank-less rear problem - a detached model is
                // finished on all four elevations, so an alley is a fine place for one.
                if (!facesStreet
                    && group.layout == PrefabDatabase.PieceLayout.Terrace
                    && !group.HasRear)
                    weight *= NoRearPenalty;

                if (preferCorner && group.cornerPreferred)
                    weight *= CornerBoost;

                weights[i] = weight;
                total += weight;
            }

            return WeightedRoll.Index(weights, total, rng);
        }

        /// <summary>
        /// A Terrace group carrying corner pieces, or -1. Detached zones have none by design -
        /// their models are finished all round and have no corner variant to place.
        /// </summary>
        public int PickCornerGroup()
        {
            var total = 0f;
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                var usable = group.layout == PrefabDatabase.PieceLayout.Terrace
                          && group.cornerPrefabs != null
                          && group.cornerPrefabs.Length > 0;

                weights[i] = usable ? group.weight : 0f;
                total += weights[i];
            }

            return WeightedRoll.Index(weights, total, rng);
        }

        public GameObject Peek(int index, bool facesStreet) =>
            (facesStreet ? streetBags : rearBags)[index].Peek();

        public void Advance(int index, bool facesStreet) =>
            (facesStreet ? streetBags : rearBags)[index].Advance();

        public GameObject PeekCorner(int index) => cornerBags[index].Peek();

        public void AdvanceCorner(int index) => cornerBags[index].Advance();
    }
}
