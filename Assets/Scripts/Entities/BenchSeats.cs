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
    /// Seat offsets are local: X spreads sitters along the slats, Z puts the sit root just in
    /// FRONT of the bench (the pack's sit-down clip carries the hips back and down onto it),
    /// Y stays at ground level - a Humanoid sit pose lowers the hips relative to the root, so
    /// the root itself never leaves the ground. Occupancy is runtime-only on purpose: a seat
    /// claim saved into the scene would be a bench nobody can ever use again.
    /// </summary>
    public sealed class BenchSeats : MonoBehaviour
    {
        [SerializeField] Vector3[] seatOffsets;

        [System.NonSerialized] bool[] occupied;

        public void SetSeats(Vector3[] offsets) => seatOffsets = offsets;

        public int SeatCount => seatOffsets?.Length ?? 0;

        public Vector3 SeatWorld(int seat) => transform.TransformPoint(seatOffsets[seat]);

        /// <summary>Where to stand before turning and sitting down: a step out in front.</summary>
        public Vector3 ApproachWorld(int seat) => SeatWorld(seat) + Facing * 0.7f;

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
