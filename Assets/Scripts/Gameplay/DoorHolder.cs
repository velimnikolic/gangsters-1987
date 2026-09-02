using LivingCity.Business;
using LivingCity.Entities;
using LivingCity.Gangs;
using LivingCity.Outfit;
using LivingCity.Territory;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// Who holds one door, read from the three books that can answer it: the deed, the
    /// street's own fronts, and the racket. Paper first, because the deed book survives a
    /// block being streamed out; the street next, because a house standing in its own
    /// premises is a house whatever the paperwork has caught up to; the racket last, for
    /// a door nobody owns but somebody is being paid by.
    ///
    /// The block file worked this out inline and the order book's map did not work it out
    /// at all - it read the deed and nothing else. Both now ask here, so the sheet and the
    /// map cannot disagree about who a shop belongs to.
    /// </summary>
    public static class DoorHolder
    {
        /// <summary>
        /// Where this door stands with ONE HOUSE - the player's unless another is named.
        /// <paramref name="holderGang"/> comes back as the other house that holds it, or
        /// -1 when nobody else does.
        ///
        /// Tenure is a RELATION, not a property of the door: the same shop is Ours to
        /// the family whose paper it is on and Rival to everybody else, and that is why
        /// DoorOrders.Refusal can say "our own paper" for all twenty-one.
        /// </summary>
        /// <param name="marker">The live view when the caller already has one; null is
        /// fine and the binding is looked up here.</param>
        public static DoorTenure Read(
            TerritoryBusinessId id, BusinessMarker marker, out int holderGang,
            TerritoryGangId asking = default)
        {
            holderGang = -1;
            if (!asking.IsValid)
                asking = PlayerCommands.House;
            if (!id.IsValid)
                return DoorTenure.Open;

            var deedGang = BusinessDeeds.GangOf(id);
            if (deedGang < 0)
            {
                if (marker == null)
                    BusinessViewBindings.TryGet(id, out marker);
                if (marker != null)
                    deedGang = marker.GangId;
            }

            // EVERY HOUSE HAS PAPER NOW, and paper is public the moment it is written.
            // A rival's premises is meant to stay a rumour until a crew of ours has
            // stood outside it, so the deed we may not have seen is not read at all:
            // the door comes back Open, exactly as it did when their paper was simply
            // never written (FrontDeeds).
            if (!Learned(id, deedGang))
                deedGang = -1;

            var tenure = DoorTenure.Open;
            if (deedGang == asking.Value)
            {
                tenure = DoorTenure.Ours;
            }
            else if (deedGang >= 0)
            {
                tenure = DoorTenure.Rival;
                holderGang = deedGang;
            }
            else
            {
                var racket = RoadDemo.TerritoryRuntime.Instance?.Racket;
                if (racket != null && racket.TryGetProtector(id, out var protector))
                {
                    if (protector == asking)
                    {
                        tenure = DoorTenure.Paying;
                    }
                    else
                    {
                        tenure = DoorTenure.Rival;
                        holderGang = protector.Value;
                    }
                }
            }

            // A house's own premises is a house's, whatever the deed book has caught up
            // to - the street said so and the street is the authority on where a family
            // sits. Only a front that has actually been SEEN counts, so the map cannot
            // tell the player about a house he has never laid eyes on.
            var front = FrontOn(id);
            if (front != null)
            {
                if (front.GangId == asking.Value)
                {
                    tenure = DoorTenure.Ours;
                    holderGang = -1;
                }
                else if (tenure == DoorTenure.Open)
                {
                    tenure = DoorTenure.Rival;
                    holderGang = front.GangId;
                }
            }

            return tenure;
        }

        public static DoorTenure Read(TerritoryBusinessId id) =>
            Read(id, null, out _);

        /// <summary>Where this door stands with a NAMED house.</summary>
        public static DoorTenure Read(TerritoryBusinessId id, TerritoryGangId asking) =>
            Read(id, null, out _, asking);

        /// <summary>
        /// Whether the player may be TOLD that this house holds this door. Our own is
        /// always ours to know; another family's is theirs to keep until one of our men
        /// has stood within <see cref="RoadDemo.TurfKnowledge.LearnRange"/> of it.
        ///
        /// A door with no family's front standing on it carries no secret - nothing in
        /// the city writes a rival deed except a front, so there is nothing to hide.
        /// </summary>
        public static bool Learned(TerritoryBusinessId id, int gangId)
        {
            if (gangId < 0 || gangId == PlayerCommands.House.Value)
                return true;

            var all = RoadDemo.GangFront.All;
            for (var i = 0; i < all.Count; i++)
            {
                var front = all[i];
                if (front == null || front.BusinessId != id)
                    continue;
                return RoadDemo.TurfKnowledge.IsKnown(front);
            }

            return true;
        }

        static RoadDemo.GangFront FrontOn(TerritoryBusinessId id)
        {
            var all = RoadDemo.GangFront.All;
            for (var i = 0; i < all.Count; i++)
            {
                var front = all[i];
                if (front == null || front.BusinessId != id)
                    continue;
                return RoadDemo.TurfKnowledge.IsKnown(front) ? front : null;
            }

            return null;
        }
    }
}
