using System.Collections.Generic;
using LivingCity.Territory;

namespace RoadDemo
{
    /// <summary>
    /// ONE HOUSE, ONE DOOR, ONE ERRAND AT A TIME.
    ///
    /// Two crews of the same family could be sent at the same shopkeeper at once, and the
    /// door answered both of them: two leans filed against the shop, two Fear acts on the
    /// block, and - because the owner's telephone is claimed per MAN and not per counter
    /// (DoorBeat.ClaimTelephone) - two separate rolls at ringing the precinct for what the
    /// street saw as one conversation. A family calls on a shopkeeper once; the second
    /// crew is told to wait its turn.
    ///
    /// The claim is what a crew HOLDS while it has an errand at a door, and it comes from
    /// two places:
    ///
    /// * the walk - a pending approach CARRYING a demand or a threat is already an errand
    ///   at that door, so the list of pending approaches is the claim while the men cross
    ///   the city (it is tended and dropped by the same rules a walk is). A bare GO TO THE
    ///   DOOR carries no question and reserves nothing: standing men are not a
    ///   conversation, and a crew walking up to watch a street must not lock the counter
    ///   against the crew that is going to work it;
    /// * the conversation - taken as a man steps into the shop and held for exactly as
    ///   long as that doorway visit lives. A collection round's stop and a block
    ///   shakedown's stop take it too: those men ARE at that counter.
    ///
    /// ONE OWNER, ONE LIFETIME. A claim is alive while DoorBeat still has that man's visit
    /// and dead the moment it does not - there is no timer of its own to drift from the
    /// beat. That is what makes the awkward cases come out right without a special case
    /// each: a second order given while the man is already inside answers itself on the
    /// spot (his body is hidden, so the visit cannot be played twice) and its release must
    /// NOT take the live visit's claim with it; a visit cancelled from outside - an arrest
    /// takes the crew - never runs its whenOut at all, and the claim has to go anyway; and
    /// a paused game freezes the beat, which is exactly when a clock of ours would have
    /// expired underneath it. The one thing a caller owes this file is that ClaimDoor is
    /// followed immediately by the visit it belongs to.
    ///
    /// WHOSE DOOR: the claim binds one house only. A rival family pressing the same shop
    /// is the game, not a double order, and is never refused by this.
    ///
    /// WHO CHECKS IT: the orders that put a man at a counter - the demand, the threat, and
    /// a walk that carries either. Work already in hand is never interrupted; only a NEW
    /// order is refused. A house working on paper has no bodies and no counters, so
    /// TerritoryRuntime.Paper never asks.
    /// </summary>
    public sealed partial class TerritoryRuntime
    {
        readonly List<DoorClaim> doorClaims = new List<DoorClaim>();

        /// <summary>No crew of ours at all - a character the street cannot place. He
        /// matches nobody's claim, so every one of them refuses him.</summary>
        const int NoCrew = int.MinValue;

        /// <summary>This crew has an errand at this door and holds it until the man's
        /// visit is over. Taken before the visit starts, because a visit that cannot be
        /// played answers - and releases - inside the same call.</summary>
        void ClaimDoor(
            TerritoryGangId house, int crewId, TerritoryBusinessId businessId,
            CrewWalker man)
        {
            if (!house.IsValid || !businessId.IsValid)
                return;

            for (var i = 0; i < doorClaims.Count; i++)
            {
                var held = doorClaims[i];
                if (held.CrewId != crewId || held.BusinessId != businessId)
                    continue;
                // A second order onto a counter this crew is already working rides the
                // visit that is playing. The man carrying it stays the claim's man: he is
                // the one whose way out ends it.
                if (Working(held.Man))
                    return;
                doorClaims[i] = new DoorClaim(businessId, house, crewId, man);
                return;
            }

            doorClaims.Add(new DoorClaim(businessId, house, crewId, man));
        }

        /// <summary>He is done at that counter. The door is anybody's again - unless the
        /// claim's own man is still in there, which is the second order that answered
        /// itself the instant it was given: it releases what it never really held.</summary>
        void ReleaseDoor(int crewId, TerritoryBusinessId businessId)
        {
            for (var i = doorClaims.Count - 1; i >= 0; i--)
            {
                var held = doorClaims[i];
                if (held.CrewId != crewId || held.BusinessId != businessId ||
                    Working(held.Man))
                    continue;
                doorClaims.RemoveAt(i);
            }
        }

        /// <summary>
        /// Is one of this house's OTHER crews already working this door - walking to it
        /// with a question in hand, or in there now? The refusal names them, because "no"
        /// without a reason is the thing a player cannot argue with.
        /// </summary>
        bool DoorTaken(
            TerritoryGangId house, int crewId, TerritoryBusinessId businessId,
            out string refusal)
        {
            refusal = null;
            if (!house.IsValid || !businessId.IsValid)
                return false;

            for (var i = doorClaims.Count - 1; i >= 0; i--)
            {
                var held = doorClaims[i];
                // The visit IS the claim. When the beat no longer has that man - he came
                // out, he was killed, the police took him mid-word - there is nothing
                // left to hold the door with.
                if (!Working(held.Man))
                {
                    doorClaims.RemoveAt(i);
                    continue;
                }

                if (held.BusinessId != businessId || held.House != house ||
                    held.CrewId == crewId)
                    continue;

                refusal = DoorHeldBy(held.CrewId);
                return true;
            }

            // The walk carries the errand: men sent at a door with a demand or a threat in
            // hand hold it from the moment the order is given, or the second crew would
            // simply arrive first and the race would decide who asked.
            for (var i = 0; i < pendingApproaches.Count; i++)
            {
                var pending = pendingApproaches[i];
                if (pending.BusinessId != businessId || pending.CrewId == crewId ||
                    pending.FollowUp == TerritoryRacketIntent.Approach)
                    continue;
                var unit = StandingUnitOfCrew(pending.CrewId);
                if (unit == null || unit.Faction != house.Value)
                    continue;
                refusal = DoorHeldBy(pending.CrewId);
                return true;
            }

            return false;
        }

        /// <summary>The beat still has this man at a door. DoorBeat.OnAVisit, never
        /// Active: a doorstep word that crosses no threshold keeps its phase at None for
        /// the whole of its life, and a claim measured by the phase would be dead the
        /// moment it was taken at every shop the city could not measure a passage for.</summary>
        static bool Working(CrewWalker man) => man != null && DoorBeat.OnAVisit(man);

        /// <summary>What the second crew is told, in the name of the crew that has it.</summary>
        string DoorHeldBy(int crewId)
        {
            var unit = StandingUnitOfCrew(crewId);
            var name = unit != null && !string.IsNullOrEmpty(unit.Name)
                ? unit.Name.ToUpperInvariant()
                : "CREW #" + crewId;
            return name + " is already working that door.";
        }

        readonly struct DoorClaim
        {
            public DoorClaim(
                TerritoryBusinessId businessId, TerritoryGangId house, int crewId,
                CrewWalker man)
            {
                BusinessId = businessId;
                House = house;
                CrewId = crewId;
                Man = man;
            }

            public TerritoryBusinessId BusinessId { get; }
            public TerritoryGangId House { get; }

            /// <summary>The crew, for the same-crew exemption - organization data, and
            /// deliberately not what the claim's life is measured by: a bag detail shares
            /// its line's number and outlives a line that is wiped under it.</summary>
            public int CrewId { get; }

            /// <summary>The man at that counter. His visit is the claim's whole life.</summary>
            public CrewWalker Man { get; }
        }
    }
}
