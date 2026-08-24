using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The receipt the merge leaves behind, so that one building can still be taken out
    /// of a city that draws in chunks.
    ///
    /// <see cref="ScenePerf"/> folds a block's hundred-odd renderers into a handful of
    /// combined meshes and switches the originals off - that is the whole point of it,
    /// and it also means there is no longer any such thing as "this building's
    /// renderer" to hide: the facade the camera wants out of the way is a run of
    /// triangles in the middle of a combined buffer, and the sources' CPU copies were
    /// released the moment the merge was over, so the chunk cannot be rebuilt without
    /// them either.
    ///
    /// So the chunk keeps the receipt: which pieces it swallowed, and which merged
    /// meshes stand for them. Ask it to <see cref="Hold"/> and it switches the merged
    /// meshes off and its own pieces back on - the block draws exactly as it did before
    /// the merge, at the draw-call price the merge exists to avoid - and while it stands
    /// in pieces anything in it can be hidden one renderer at a time. Release the last
    /// hold and it folds back up. Only the few blocks under a close boom are ever held
    /// at once, which is what makes paying their draw calls back for a moment
    /// affordable.
    /// </summary>
    public sealed class MergedChunk : MonoBehaviour
    {
        readonly List<MeshRenderer> _pieces = new List<MeshRenderer>();
        readonly List<MeshRenderer> _merged = new List<MeshRenderer>();

        /// <summary>Which chunk swallowed a given source renderer. Only the pieces worth
        /// asking about are in here - a city merge is a hundred thousand renderers and
        /// all but the buildings among them are things nobody will ever ask to hide.</summary>
        static readonly Dictionary<Renderer, MergedChunk> Owner = new Dictionary<Renderer, MergedChunk>();

        int _holds;

        /// <summary>Set once the merge has built every group this chunk is owed. Until
        /// then the chunk is half folded - some pieces switched off, their mesh not yet
        /// standing in for them - and pulling it apart would leave a hole. A caller that
        /// arrives early is simply told no and asks again next sweep.</summary>
        public bool Ready { get; internal set; }

        /// <summary>Whether the chunk is currently drawing as its own pieces.</summary>
        public bool Standing => _holds > 0;

        /// <summary>The chunk that swallowed this renderer, or null: never merged, merged
        /// by an older build that has since been destroyed, or not a piece worth
        /// registering.</summary>
        public static MergedChunk Of(Renderer piece)
        {
            if (piece == null || !Owner.TryGetValue(piece, out var chunk)) return null;
            return chunk ? chunk : null;
        }

        /// <summary>A source renderer this chunk is about to swallow. <paramref name="findable"/>
        /// asks for the reverse lookup as well - kept for buildings only.</summary>
        internal void Adopt(MeshRenderer piece, bool findable)
        {
            _pieces.Add(piece);
            if (findable) Owner[piece] = this;
        }

        /// <summary>A merged mesh that now draws part of what this chunk swallowed.</summary>
        internal void StandsFor(MeshRenderer merged) => _merged.Add(merged);

        /// <summary>Take the chunk apart - or join a hold already standing. False when
        /// the merge has not finished with it and it must be left alone.</summary>
        public bool Hold()
        {
            if (!Ready) return false;
            if (_holds++ == 0) Draw(inPieces: true);
            return true;
        }

        /// <summary>Give up one hold; the last one folds the chunk back up.</summary>
        public void Release()
        {
            if (_holds == 0) return;
            if (--_holds == 0) Draw(inPieces: false);
        }

        void Draw(bool inPieces)
        {
            for (int i = 0; i < _pieces.Count; i++)
                if (_pieces[i]) _pieces[i].enabled = inPieces;
            for (int i = 0; i < _merged.Count; i++)
                if (_merged[i]) _merged[i].enabled = !inPieces;
        }

        void OnDestroy()
        {
            // A destroyed renderer is a live C# reference with a dead object behind it,
            // so the dictionary can still be cleaned by it - and must be, or a rebuilt
            // city would answer lookups with the last one's chunks.
            for (int i = 0; i < _pieces.Count; i++)
                if (!ReferenceEquals(_pieces[i], null)) Owner.Remove(_pieces[i]);
        }
    }
}
