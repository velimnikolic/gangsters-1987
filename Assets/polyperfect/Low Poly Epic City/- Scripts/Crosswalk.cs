using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PATCH (Living City): the crossing now reports who is CROSSING it, not who is standing on it.
///
/// The original was a bare counter: any Human collider entering set PedestriansAreCrossing true,
/// and only the decrement back to zero cleared it. Two consequences, both of which the player
/// sees as a jam. First, a pedestrian who comes to rest inside the box - a walker whose re-path
/// failed, one frozen at a light mid-carriageway, one held still by the avoidance clamp - stops
/// the car that is holding for this crossing FOREVER: CarBehavior's watchdog explicitly declines
/// to release while the flag is true, and TrafficRegistry then queues every car behind it.
/// Second, the counter leaks: a collider destroyed or deactivated inside the trigger never fires
/// OnTriggerExit, so the crossing latches true for the rest of the scene.
///
/// So keep the occupants themselves, and hold each one to the only question that matters - when
/// did you last move? Somebody who just stepped on is crossing by definition; somebody who has
/// not moved for StaleAfter is not crossing, they are standing there. A body that has vanished
/// is dropped on sight rather than waited for. Movement is sampled from the transform rather
/// than read off PedestrianBody.SpeedMs: the body is a plain class, unreachable from a collider,
/// and sampling also covers beat officers and the pack's own demo humans.
///
/// The event now fires on BOTH edges. CarBehavior.CrosswalkChange ignores the rising one, so
/// this stays backward compatible, and the falling edge releases held cars through the path that
/// already exists.
///
/// One Update per crossing, early-outing while empty; a large map has of the order of a hundred.
/// If that ever matters, the migration is a static list ticked from PedestrianLodSystem.Update,
/// which already drives PedestrianRepathQueue.Drain for the same reason.
/// </summary>
public class Crosswalk : MonoBehaviour
{
    public delegate void StateChange(bool crossing);
    public StateChange stateChange;
    public bool PedestriansAreCrossing = false;
    public bool CanCross = true;

    /// <summary>
    /// Seconds an occupant may stand motionless and still be treated as crossing. It is the
    /// yield a driver owes somebody stepping out; past it they are not crossing, they are
    /// standing in the road, and a car that waits for them waits for ever.
    /// </summary>
    public const float StaleAfter = 3f;

    /// <summary>
    /// Displacement that counts as having moved. A millimetre, because LastPosition only
    /// advances when the threshold is crossed - so movement accumulates across samples and even
    /// a walker inching through a crowd registers, while a parked one never does.
    /// </summary>
    public const float MovedEpsilon = 0.001f;

    /// <summary>Seconds between movement samples.</summary>
    public const float SweepInterval = 0.25f;

    struct Occupant
    {
        public Collider Collider;
        public Transform Tf;
        public Vector3 LastPosition;
        public float LastMovedAt;
    }

    readonly List<Occupant> occupants = new List<Occupant>();
    float nextSweep;

    /// <summary>The rule itself, pure so the model tests can hold it to account.</summary>
    public static bool CountsAsCrossing(float now, float lastMovedAt) =>
        now - lastMovedAt <= StaleAfter;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Human") || IndexOf(other) >= 0)
            return;

        occupants.Add(new Occupant
        {
            Collider = other,
            Tf = other.transform,
            LastPosition = other.transform.position,
            LastMovedAt = Time.time,
        });
        Apply();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Human"))
            return;

        // By identity, never a bare decrement: a re-entry that lost its exit used to desync the
        // count permanently, and a count that can never reach zero again is the latch itself.
        var i = IndexOf(other);
        if (i < 0)
            return;

        SwapPop(i);
        Apply();
    }

    private void Update()
    {
        if (occupants.Count == 0 || Time.time < nextSweep)
            return;

        nextSweep = Time.time + SweepInterval;

        var now = Time.time;
        for (var i = occupants.Count - 1; i >= 0; i--)
        {
            var o = occupants[i];
            if (IsGone(o))
            {
                SwapPop(i);
                continue;
            }

            var at = o.Tf.position;
            if ((at - o.LastPosition).sqrMagnitude > MovedEpsilon * MovedEpsilon)
            {
                o.LastPosition = at;
                o.LastMovedAt = now;
                occupants[i] = o;
            }
        }

        Apply();
    }

    void Apply()
    {
        var now = Time.time;
        var crossing = false;

        for (var i = occupants.Count - 1; i >= 0; i--)
        {
            if (IsGone(occupants[i]))
            {
                SwapPop(i);
                continue;
            }

            crossing |= CountsAsCrossing(now, occupants[i].LastMovedAt);
        }

        if (crossing == PedestriansAreCrossing)
            return;

        PedestriansAreCrossing = crossing;
        if (stateChange != null)
            stateChange.Invoke(crossing);
    }

    /// <summary>Destroyed, or its collider switched off - either way, no longer crossing.</summary>
    static bool IsGone(in Occupant o) =>
        !o.Collider || !o.Collider.enabled || !o.Collider.gameObject.activeInHierarchy;

    int IndexOf(Collider c)
    {
        for (var i = 0; i < occupants.Count; i++)
            if (occupants[i].Collider == c)
                return i;
        return -1;
    }

    void SwapPop(int i)
    {
        occupants[i] = occupants[occupants.Count - 1];
        occupants.RemoveAt(occupants.Count - 1);
    }
}
