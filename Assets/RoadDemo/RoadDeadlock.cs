using UnityEngine;

namespace RoadDemo
{
    /// <summary>One pair-scoped escape lease for a measured reciprocal traffic wait.
    /// Never a global collision switch; parking and intentional stops are ineligible.</summary>
    internal sealed class RoadDeadlock
    {
        internal const float Delay = 6f, Pace = 1f;
        readonly RoadCar owner;
        RoadCar blocker;
        float since, seen = -999f, retryAfter;
        Vector3 anchor;
        Lease lease;
        internal int Escapes { get; private set; }
        internal RoadDeadlock(RoadCar car) { owner = car; }

        sealed class Lease
        {
            internal RoadCar Driver, Waiting;
            internal RoadCar Pinned;
            internal Vector3 Start;
            internal float Until, NextChoice;
            internal bool Finishing, Holding;
        }

        internal bool Active => lease != null;
        internal int BlockerId => Fresh && blocker != null ? blocker.Id : -1;
        internal int PeerId => lease == null ? -1 : (lease.Driver == owner ? lease.Waiting.Id : lease.Driver.Id);
        internal bool Waiting => lease != null && (lease.Holding || lease.Waiting == owner);
        internal bool Ignores(RoadCar other) => lease != null && lease.Driver == owner && lease.Waiting == other &&
            !lease.Holding && owner.CanEaseTraffic && (other.CanEaseTraffic ||
                lease.Finishing && other.OnRoad && !other.Gone && Touching(lease));
        internal bool Mutual => blocker != null && Fresh && blocker.Deadlock.Fresh &&
            blocker.Deadlock.blocker == owner && owner.CanEaseTraffic && blocker.CanEaseTraffic &&
            Mathf.Abs(owner.Speed) < .2f && Mathf.Abs(blocker.Speed) < .2f &&
            RoadCarSimulation.Now >= retryAfter && RoadCarSimulation.Now >= blocker.Deadlock.retryAfter && Compatible;
        bool Compatible => Vector3.Dot(owner.Forward, blocker.Forward) <= .5f &&
            !((owner.Via ?? owner.PlannedCrossing)?.From == (blocker.Via ?? blocker.PlannedCrossing)?.From &&
                owner.Lane == blocker.Lane) &&
            (owner.Position - blocker.Position).sqrMagnitude <
                (owner.HalfLen + blocker.HalfLen + 3f) * (owner.HalfLen + blocker.HalfLen + 3f);
        bool Fresh => RoadCarSimulation.Now - seen <= .25f &&
            (owner.Position - anchor).sqrMagnitude < .25f;

        internal void BlockedBy(RoadCar other)
        {
            if (lease != null || other == null || other == owner || !owner.CanEaseTraffic || !other.CanEaseTraffic) return;
            float now = RoadCarSimulation.Now;
            if (blocker != other || !Fresh)
            { blocker = other; since = now; anchor = owner.Position; }
            seen = now;
        }

        internal void Cancel(bool detach = false)
        {
            var previous = lease;
            if (!detach && previous != null && Touching(previous))
            {
                // An order may end the manoeuvre, but cannot delete existing contact.
                // Hold the commanded car while its peer finishes separating from it.
                previous.Pinned = owner;
                previous.Finishing = previous.Holding = true;
                previous.NextChoice = 0f;
                return;
            }
            Release(previous);
        }

        void Release(Lease previous)
        {
            lease = null; blocker = null; seen = -999f; retryAfter = 0f;
            if (previous == null) return;
            var peer = previous.Driver == owner ? previous.Waiting : previous.Driver;
            if (peer.Deadlock.lease == previous)
            { peer.Deadlock.lease = null; peer.Deadlock.blocker = null; peer.Deadlock.seen = -999f; }
        }

        static bool Touching(Lease pair) => RoadSpace.Overlap(pair.Driver.Position, pair.Driver.Forward,
            pair.Driver.HalfLen, pair.Driver.HalfWide, pair.Waiting.Position, pair.Waiting.Forward,
            pair.Waiting.HalfLen, pair.Waiting.HalfWide, .15f, out _);

        static void FinishContact(Lease pair)
        {
            if (RoadCarSimulation.Now < pair.NextChoice) return;
            pair.NextChoice = RoadCarSimulation.Now + .5f;
            var driver = pair.Driver; var waiting = pair.Waiting;
            bool CanFinish(RoadCar car, RoadCar peer) => car != pair.Pinned && car.CanEaseTraffic &&
                EscapePathClear(car, peer);
            // A late obstacle may seal the first exit. Recheck the other car's
            // complete path before handing it the same pair's walking-speed turn.
            if (CanFinish(driver, waiting)) pair.Holding = false;
            else if (CanFinish(waiting, driver))
            {
                pair.Driver = waiting;
                pair.Waiting = driver;
                pair.Holding = false;
            }
            else pair.Holding = true;
        }

        internal void Tick()
        {
            if (lease != null)
            {
                var driver = lease.Driver; var waiting = lease.Waiting;
                if (!driver.OnRoad || !waiting.OnRoad || driver.Gone || waiting.Gone)
                { Release(lease); return; }
                bool clear = !Touching(lease);
                if (!driver.CanEaseTraffic || !waiting.CanEaseTraffic || RoadCarSimulation.Now > lease.Until ||
                    (driver.Position - lease.Start).sqrMagnitude > 400f)
                    lease.Finishing = true;
                if (clear && (lease.Finishing ||
                    Vector3.Dot(waiting.Position - driver.Position, driver.Forward) < -driver.HalfLen - waiting.HalfLen))
                { Release(lease); return; }
                // Expiry stops authorizing new contact. Existing contact retains
                // its owner until the bodies separate; third parties remain strict.
                if (lease.Finishing) FinishContact(lease);
                return;
            }
            if (!Mutual || RoadCarSimulation.Now - Mathf.Max(since, blocker.Deadlock.since) < Delay) return;
            var other = blocker;
            var first = owner.Via != null && other.Via == null ? owner :
                other.Via != null && owner.Via == null ? other :
                owner.Profile.Priority > other.Profile.Priority ||
                owner.Profile.Priority == other.Profile.Priority && owner.Id < other.Id ? owner : other;
            var second = first == owner ? other : owner;
            if (!EscapePathClear(first, second))
            {
                // A third obstacle must not lock out ordinary reverse/replanning.
                retryAfter = other.Deadlock.retryAfter = RoadCarSimulation.Now + 5f;
                return;
            }
            var permission = new Lease { Driver = first, Waiting = second, Start = first.Position,
                Until = RoadCarSimulation.Now + 20f };
            first.Deadlock.lease = second.Deadlock.lease = permission;
            first.Deadlock.Escapes++;
            if (DriveTrace.On) DriveTrace.Event("man", "car " + first.Id,
                "easing past mutually blocked car " + second.Id, "");
        }

        static bool EscapePathClear(RoadCar driver, RoadCar waiting)
        {
            // Admit only a complete short escape with room beyond the other body.
            // Live next-step checks still protect a third vehicle arriving later.
            float distance = driver.HalfLen + waiting.HalfLen +
                (driver.Position - waiting.Position).magnitude + 1f;
            if (distance > 18f) return false;
            for (float s = 0f; s <= distance + .1f; s += .2f)
            {
                if (!driver.TrafficPoseAhead(s, out var position, out var forward)) return false;
                foreach (var user in StreetTraffic.Users)
                {
                    if (ReferenceEquals(user, driver) || ReferenceEquals(user, waiting)) continue;
                    if (RoadSpace.Overlap(position, forward, driver.HalfLen, driver.HalfWide,
                        user.RoadPosition, user.RoadForward, user.HalfLength, user.HalfWidth, .3f, out _)) return false;
                }
            }
            return true;
        }
    }
}
