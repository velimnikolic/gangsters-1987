using LivingCity.Entities;
using LivingCity.Generation;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Where a shop's street door is, for any business standing in the streamed city.
    ///
    /// The old generated city stamped a <see cref="ShopEntrance"/> on every shopfront as
    /// it built it (InteractionMarkers, called from CityBuilder). Nothing does that for
    /// the blocks the recycler streams in, so in the city the game actually plays there
    /// was NOT ONE DOOR: the boards from a smashed front went up at the job's approach
    /// point - a spot on the pavement, sometimes out in the road - and the doorway beat
    /// had no leaves to open, so a man "went inside" a fabricated threshold instead of
    /// through the shop's own door.
    ///
    /// The door is DERIVED, never authored: FacadeFinder reads which side of the
    /// building its front is on out of the building's own meshes, and the door goes at
    /// the centre of that facade, at ground level, pulled in by whatever the finder
    /// measures between the footprint edge and the real wall. Derived once and kept on
    /// the view, so the measuring happens for a shop the first time somebody stands at
    /// it and never again.
    /// </summary>
    public static class ShopDoors
    {
        /// <summary>How much of a shop's frontage the boards and the fire cover: all of
        /// it bar a hand's width at each jamb, so a wrecked front reads as THAT shop's
        /// front and not as planks overrunning its neighbours.</summary>
        public const float FrontageMargin = 0.3f;

        /// <summary>Anything narrower than this is a measuring failure, not a shop.</summary>
        public const float NarrowestFront = 2f;

        /// <summary>The street door of this business's view, deriving it the first time
        /// it is asked for. Null when there is no view to measure.</summary>
        public static ShopEntrance Of(BusinessMarker marker) => Of(marker, out _);

        /// <summary>
        /// The door, and how wide the front it sits in is - the width the boards are cut
        /// to and the fire is strung across. Measured off the building's own meshes, so
        /// a narrow shop is not boarded up to the width of a warehouse.
        /// </summary>
        public static ShopEntrance Of(BusinessMarker marker, out float frontage)
        {
            frontage = 0f;
            if (marker == null)
                return null;

            var found = marker.GetComponentInChildren<ShopEntrance>(true);
            if (found != null)
            {
                frontage = FrontageOf(found);
                return found;
            }

            var view = marker.gameObject;
            var bounds = InteractionMarkers.LocalBounds(view.transform);
            if (bounds.size.sqrMagnitude < 0.01f)
                return null;

            var side = FacadeFinder.FrontOf(view, out _, out var inset);
            Vector3 outward;
            Vector3 at;
            switch (side)
            {
                case FacadeFinder.Side.PlusX:
                    outward = Vector3.right;
                    at = new Vector3(bounds.max.x - inset, bounds.min.y, bounds.center.z);
                    break;
                case FacadeFinder.Side.MinusX:
                    outward = Vector3.left;
                    at = new Vector3(bounds.min.x + inset, bounds.min.y, bounds.center.z);
                    break;
                case FacadeFinder.Side.MinusZ:
                    outward = Vector3.back;
                    at = new Vector3(bounds.center.x, bounds.min.y, bounds.min.z + inset);
                    break;
                default:
                    outward = Vector3.forward;
                    at = new Vector3(bounds.center.x, bounds.min.y, bounds.max.z - inset);
                    break;
            }

            // The marker carries a ShopEntrance on a child TURNED TO THE FACADE rather
            // than on itself: the component reads its own forward as the way out to the
            // street, and a building whose front is its +X side would otherwise report a
            // door facing along the wall.
            var door = new GameObject("Street door");
            door.transform.SetParent(view.transform, false);
            door.transform.localPosition = at;
            door.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);

            var entrance = door.AddComponent<ShopEntrance>();
            entrance.SetDoor(Vector3.zero);

            // The front's own width, along the facade, in world metres. It is kept ON the
            // door, not in a table keyed by it: the recycler streams these views out and
            // back all game, and a static dictionary of destroyed components would grow
            // for as long as the city is played.
            var along = side == FacadeFinder.Side.PlusX || side == FacadeFinder.Side.MinusX
                ? bounds.size.z * Mathf.Abs(view.transform.lossyScale.z)
                : bounds.size.x * Mathf.Abs(view.transform.lossyScale.x);
            entrance.SetFrontage(Mathf.Max(NarrowestFront, along - FrontageMargin * 2f));
            frontage = entrance.Frontage;
            return entrance;
        }

        /// <summary>What was measured for this door, or nothing when it came from the
        /// old city's own stamping and was never measured here.</summary>
        static float FrontageOf(ShopEntrance entrance) =>
            entrance != null ? entrance.Frontage : 0f;

        /// <summary>The same door by business id, for callers that hold the simulation's
        /// id rather than the view.</summary>
        public static ShopEntrance Of(LivingCity.Territory.TerritoryBusinessId id) =>
            Of(id, out _);

        public static ShopEntrance Of(
            LivingCity.Territory.TerritoryBusinessId id, out float frontage)
        {
            frontage = 0f;
            return LivingCity.Business.BusinessViewBindings.TryGet(id, out var marker)
                ? Of(marker, out frontage)
                : null;
        }
    }
}
