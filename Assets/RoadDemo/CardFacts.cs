using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What a click card should say about this thing, written by whoever BUILT it.
    ///
    /// <c>BuildingCardPicker</c> can only read a transform's name and measure its
    /// renderers, which is the right answer for a catalog bake standing on a shelf: the
    /// name is the building and the size is the fact worth knowing. It is a poor answer
    /// for anything composed, because the composer knows what it made and the picker can
    /// only see what it left behind - an industrial parcel is a stockyard with two sheds
    /// and three ranks of containers on it, and none of that is recoverable from
    /// "yard-07" and a bounding box.
    ///
    /// So the builder leaves the card here and the picker prefers it. Nothing that does
    /// not carry one behaves any differently.
    /// </summary>
    public sealed class CardFacts : MonoBehaviour
    {
        [Tooltip("The card's heading. Left empty, the picker falls back to the object's name.")]
        public string Title;

        [TextArea(2, 6)]
        [Tooltip("The lines under it. Left empty, the picker falls back to footprint and height.")]
        public string Body;

        /// <summary>
        /// The box the card anchors to, in the object's OWN space, taken when it was built.
        ///
        /// Measured up front rather than at the click, because the host's perf pass merges
        /// renderers out from under a district afterwards: by the time anybody clicks, the
        /// parcel's own transform may own no renderer at all and measuring it would put the
        /// card on the world origin.
        ///
        /// And kept LOCAL rather than in world space, because a quarter is stood at the
        /// origin and moved into place afterwards - a world box taken during the build is a
        /// box for wherever the quarter was standing at the time, which is not where it ends
        /// up. <see cref="WorldBox"/> does the conversion at the moment of asking.
        /// </summary>
        public Bounds Box;

        /// <summary>Whether <see cref="Box"/> was filled in. A default Bounds is a real
        /// value - the unit box at the origin - so it cannot speak for itself.</summary>
        public bool HasBox;

        /// <summary>
        /// <see cref="Box"/> where it actually is: the eight corners carried into world
        /// space and re-boxed, so a quarter turn of the parcel gives an upright box round
        /// the turned one rather than the old box with its sides swapped.
        /// </summary>
        public Bounds WorldBox()
        {
            var centre = transform.TransformPoint(Box.center);
            var world = new Bounds(centre, Vector3.zero);
            var e = Box.extents;
            for (int k = 0; k < 8; k++)
                world.Encapsulate(transform.TransformPoint(Box.center + new Vector3(
                    (k & 1) == 0 ? e.x : -e.x,
                    (k & 2) == 0 ? e.y : -e.y,
                    (k & 4) == 0 ? e.z : -e.z)));
            return world;
        }

        /// <summary>Writes the card and the box in one go.</summary>
        public static CardFacts On(GameObject go, string title, string body, Bounds box)
        {
            var facts = go.GetComponent<CardFacts>() ?? go.AddComponent<CardFacts>();
            facts.Title = title;
            facts.Body = body;
            facts.Box = box;
            facts.HasBox = true;
            return facts;
        }
    }
}
