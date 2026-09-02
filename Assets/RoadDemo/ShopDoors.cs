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

        /// <summary>
        /// WHERE THIS SHOP'S FRONT WALL IS, needing no view at all: the business's own
        /// ground and the doorstep the crew walks to are enough.
        ///
        /// This exists because in the streamed city there IS no view to measure - not one
        /// business in the quarter is bound to a marker, so everything that wanted a
        /// shopfront fell back on the job's approach point, which is a spot on the
        /// PAVEMENT. That is why the boards and the fire stood in the road.
        ///
        /// The site is an axis-aligned footprint. The doorstep lies off one of its four
        /// sides; that side is the front, its outward normal is the way out to the
        /// street, and the door is the point on it level with the doorstep. The width is
        /// that side's own length, so the boards are cut to this shop and no other.
        /// </summary>
        public static bool TryStreetFront(
            LivingCity.Territory.TerritoryBusinessId id,
            out Vector3 door, out Vector3 outward, out float frontage)
        {
            door = default;
            outward = Vector3.forward;
            frontage = 0f;

            var runtime = TerritoryRuntime.Instance;
            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (runtime == null || business == null || !id.IsValid ||
                !runtime.TryGetBusinessApproach(id, out var approach) ||
                !business.TryGetSite(id, out var site) || site == null)
                return false;

            var ground = site.Footprint;
            if (ground.IsEmpty)
                return false;

            // Which side the doorstep is off. Measured from the footprint's own edges, so
            // a shop whose door is on the short side is not given the long one.
            var west = approach.x - ground.XMin;
            var east = ground.XMax - approach.x;
            var south = approach.z - ground.ZMin;
            var north = ground.ZMax - approach.z;
            var nearest = Mathf.Min(Mathf.Min(west, east), Mathf.Min(south, north));

            if (nearest == west)
            {
                outward = Vector3.left;
                door = new Vector3(ground.XMin, approach.y,
                    Mathf.Clamp(approach.z, ground.ZMin, ground.ZMax));
                frontage = ground.Depth;
            }
            else if (nearest == east)
            {
                outward = Vector3.right;
                door = new Vector3(ground.XMax, approach.y,
                    Mathf.Clamp(approach.z, ground.ZMin, ground.ZMax));
                frontage = ground.Depth;
            }
            else if (nearest == south)
            {
                outward = Vector3.back;
                door = new Vector3(
                    Mathf.Clamp(approach.x, ground.XMin, ground.XMax), approach.y,
                    ground.ZMin);
                frontage = ground.Width;
            }
            else
            {
                outward = Vector3.forward;
                door = new Vector3(
                    Mathf.Clamp(approach.x, ground.XMin, ground.XMax), approach.y,
                    ground.ZMax);
                frontage = ground.Width;
            }

            frontage = Mathf.Max(NarrowestFront, frontage - FrontageMargin * 2f);
            return true;
        }

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
