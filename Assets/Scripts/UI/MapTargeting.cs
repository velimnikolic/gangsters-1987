using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>
    /// A page that is waiting for the player to point at ground. While one is set, the
    /// map's own click (inspect a building, select a crew) stands down and the pointer
    /// belongs to the consumer.
    /// </summary>
    public interface IMapTargetingConsumer
    {
        /// <summary>True = this order type drags a box; false = one click, one target.</summary>
        bool WantsArea { get; }

        /// <summary>Fires every frame the box is being dragged - blocks highlight as
        /// the box captures them, before anything is selected.</summary>
        void OnAreaPreview(Rect worldXZ);

        /// <summary>The button came up on a dragged box.</summary>
        void OnAreaSelected(Rect worldXZ);

        /// <summary>The button came up on a click - the block under it, or -1 on a
        /// street outside every slab and the nearest block id alongside.</summary>
        void OnPointClicked(Vector2 worldXZ, int blockId);
    }

    /// <summary>
    /// A map the ledger can pick ground on. There are two of them and they are not the
    /// same kind of thing: the turf plate IS a zoom level of the street camera and can
    /// never be summoned by a button (that would be a second truth about where the
    /// player is looking), while the older generated city's strategic map is a screen
    /// the ledger opens and closes. <see cref="CanSummon"/> is that difference, and the
    /// ledger reads it rather than assuming either shape.
    /// </summary>
    public interface IMapTargetingSurface
    {
        bool IsShowing { get; }
        bool CanSummon { get; }

        /// <summary>Bring the map up. False when this map cannot be summoned at all.</summary>
        bool Summon();

        /// <summary>Put it away again. A map that cannot be summoned ignores this.</summary>
        void Dismiss();

        /// <summary>What to tell the player when the map has to be reached by hand.</summary>
        string SummonHint { get; }

        /// <summary>
        /// PUT THE VIEW ON THIS PLACE (GAN-302). The book sends the player to a spot -
        /// a witness's marker, a door - and the map is whichever one is registered, so
        /// the jump is written once here rather than against a named HUD.
        /// </summary>
        void FocusOn(Vector3 at);

        /// <summary>World-XZ rectangles the picking page wants lit while it waits.</summary>
        void SetTargetHighlights(List<Rect> worldRects, Color colour);
    }

    /// <summary>
    /// The seam between a ledger page that needs ground pointed at and whichever map
    /// this scene actually has. It exists because the game has two maps of different
    /// shapes and the book must not know which one it is talking to.
    ///
    /// Maps register themselves with a rank and the highest-ranked one serves the picks:
    /// where the turf plate exists it is THE map, and the camera map stands down.
    /// </summary>
    public static class MapTargeting
    {
        /// <summary>The turf plate - the game's own map, on the wheel.</summary>
        public const int PlateRank = 10;

        /// <summary>The generated city's top-down camera map, on M.</summary>
        public const int CameraMapRank = 0;

        static readonly List<Registration> Surfaces = new List<Registration>();

        /// <summary>The page waiting for a pick, or null.</summary>
        public static IMapTargetingConsumer Consumer { get; private set; }

        /// <summary>The map that will serve it.</summary>
        public static IMapTargetingSurface Surface { get; private set; }

        public static bool Available => Surface != null;

        /// <summary>Fires when the consumer or the serving map changes, so a map can
        /// repaint its own hint line without polling.</summary>
        public static event Action Changed;

        public static void Register(IMapTargetingSurface surface, int rank)
        {
            if (surface == null)
                return;
            for (var i = 0; i < Surfaces.Count; i++)
                if (ReferenceEquals(Surfaces[i].Surface, surface))
                    return;

            Surfaces.Add(new Registration(surface, rank));
            Elect();
        }

        public static void Unregister(IMapTargetingSurface surface)
        {
            for (var i = 0; i < Surfaces.Count; i++)
            {
                if (!ReferenceEquals(Surfaces[i].Surface, surface))
                    continue;
                Surfaces.RemoveAt(i);
                break;
            }

            Elect();
        }

        public static void Set(IMapTargetingConsumer consumer)
        {
            Consumer = consumer;
            Changed?.Invoke();
        }

        /// <summary>Only the consumer that holds the seam gives it back - a page
        /// closing must not knock another's targeting out from under it.</summary>
        public static void Clear(IMapTargetingConsumer consumer)
        {
            if (Consumer != consumer)
                return;
            Consumer = null;
            Changed?.Invoke();
        }

        static void Elect()
        {
            IMapTargetingSurface best = null;
            var bestRank = int.MinValue;
            for (var i = 0; i < Surfaces.Count; i++)
            {
                if (Surfaces[i].Rank <= bestRank)
                    continue;
                bestRank = Surfaces[i].Rank;
                best = Surfaces[i].Surface;
            }

            if (ReferenceEquals(best, Surface))
                return;
            Surface = best;
            Changed?.Invoke();
        }

        /// <summary>Statics outlive Play when domain reload is off - a map registered by
        /// the last session would otherwise be served picks it can never draw.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Surfaces.Clear();
            Surface = null;
            Consumer = null;
            Changed = null;
        }

        readonly struct Registration
        {
            public Registration(IMapTargetingSurface surface, int rank)
            {
                Surface = surface;
                Rank = rank;
            }

            public IMapTargetingSurface Surface { get; }
            public int Rank { get; }
        }
    }
}
