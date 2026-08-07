using UnityEngine;

namespace LivingCity.Entities
{
    /// <summary>
    /// Data marker on a placed bench: where its seats are, and who is on them. The SmokeVent
    /// pattern - the city is generated in the editor and SAVED, so anything collected in a
    /// runtime list during generation is lost; a component riding on the instance survives
    /// the save and is picked up by FindObjectsByType at Play. Attached by
    /// InteractionMarkers, found by PedestrianInteractionDirector.
    ///
    /// Seat offsets are local, and measured rather than tuned - see InteractionMarkers for the
    /// bench geometry they come from. X spreads sitters along the slats. Y is the seat TOP:
    /// SeatWorld therefore names the contact patch, not a place to stand, and the sitter's own
    /// root goes SitContactHeight below it, scaled to that rig. Z puts the root in front of
    /// the bench because the authored pose keeps the pelvis SitPelvisBack behind the root -
    /// the sitter's feet end up on the pavement and its weight over the slats.
    ///
    /// Occupancy is runtime-only on purpose: a seat claim saved into the scene would be a
    /// bench nobody can ever use again.
    /// </summary>
    public sealed class BenchSeats : MonoBehaviour
    {
        [SerializeField] Vector3[] seatOffsets;

        [System.NonSerialized] bool[] occupied;

        public void SetSeats(Vector3[] offsets) => seatOffsets = offsets;

        public int SeatCount => seatOffsets?.Length ?? 0;

        public Vector3 SeatWorld(int seat) => transform.TransformPoint(seatOffsets[seat]);

        /// <summary>How far out in front the walker stops before turning and sitting down.</summary>
        const float ApproachStep = 0.35f;

        /// <summary>
        /// Where to stand before turning and sitting down: one short step out in front, ON THE
        /// GROUND. The ground part matters - WalkTo climbs towards its target's Y, so handing
        /// it SeatWorld directly would walk the pedestrian up into the air on the way in. The
        /// step is short because the descent glide has to cover it while the sit-down clip
        /// plays, and the clip is already carrying the pelvis SitPelvisBack in the same
        /// direction; the old 0.7 made that a metre of backwards travel.
        /// </summary>
        public Vector3 ApproachWorld(int seat)
        {
            var stand = SeatWorld(seat) + Facing * ApproachStep;
            stand.y = transform.position.y;
            return stand;
        }

        /// <summary>The way a sitter faces - the bench's own front (+Z by pack convention).</summary>
        public Vector3 Facing
        {
            get
            {
                var forward = transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            }
        }

        public bool HasFreeSeat
        {
            get
            {
                for (var i = 0; i < SeatCount; i++)
                    if (occupied == null || !occupied[i])
                        return true;
                return false;
            }
        }

        public bool TryClaim(out int seat)
        {
            occupied ??= new bool[SeatCount];

            for (var i = 0; i < occupied.Length; i++)
            {
                if (occupied[i])
                    continue;
                occupied[i] = true;
                seat = i;
                return true;
            }

            seat = -1;
            return false;
        }

        public void Release(int seat)
        {
            if (occupied != null && seat >= 0 && seat < occupied.Length)
                occupied[seat] = false;
        }
    }
}
