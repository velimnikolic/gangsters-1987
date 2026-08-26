using System.Collections.Generic;
using UnityEngine;
using LivingCity.Gameplay;
using LivingCity.Outfit;

namespace RoadDemo
{
    /// <summary>
    /// The wire between the ledger's order book and the men in the street. Until this
    /// class existed the two halves of the game did not touch: the player issued a job
    /// and the crew that was supposed to do it went on standing outside the front while
    /// an arithmetic somewhere decided what they had achieved.
    ///
    /// It does three things and deliberately no more. It sends a crew to its job when
    /// the job starts, it sets them on somebody when the job is a violent one and there
    /// is somebody there to set them on, and it sends them home when they are done.
    /// Everything else - who wins, who runs, who gets shot - belongs to DemoCrews, which
    /// already knows how to do all of it; this class never simulates a fight, it only
    /// reports what came of one.
    ///
    /// The reporting is one-way and optional by design: a job whose street never answers
    /// falls back to the director's own roll (OrderResolution), so a scene with no crew
    /// simulation - the standalone ledger - still plays the game.
    /// </summary>
    public static class CrewJobs
    {
        /// <summary>How far from the job's door the crew will look for somebody to hit.
        /// A block or so: the mark is at the address, not across the district.</summary>
        public const float MarkRadius = 70f;

        /// <summary>Which job each crew has already been sent on, and whom it was set
        /// on. Keyed by crew id, so a crew that loses its lieutenant and reforms under
        /// an heir keeps its orders - the crew is the unit of command, not the man.</summary>
        static readonly Dictionary<int, int> Dispatched = new Dictionary<int, int>();
        static readonly Dictionary<int, int> Sicced = new Dictionary<int, int>();
        static readonly Dictionary<int, DemoCrews.Unit> Marks =
            new Dictionary<int, DemoCrews.Unit>();

        public static void Tick(DemoCrews crews)
        {
            var outfit = OutfitDirector.Instance;
            if (crews == null || outfit == null)
                return;

            foreach (var unit in crews.Units)
            {
                if (unit == null || unit.Faction != 0 || unit.IsPolice || unit.Wiped)
                    continue;

                var job = outfit.Book.CurrentFor(unit.CrewId);
                if (job == null)
                {
                    SendHome(crews, outfit, unit);
                    continue;
                }

                switch (job.Stage)
                {
                    case JobStage.Travelling:
                        March(crews, unit, job);
                        break;
                    case JobStage.Working:
                        Work(crews, outfit, unit, job);
                        break;
                }
            }
        }

        /// <summary>Sends them once, not every frame: MarchTo clears the crew's target
        /// and unboards its car, so re-issuing it each tick would cancel the walk it
        /// had just ordered and leave them shuffling on the spot forever.</summary>
        static void March(DemoCrews crews, DemoCrews.Unit unit, Job job)
        {
            if (Dispatched.TryGetValue(unit.CrewId, out var sent) && sent == job.Id)
                return;

            Dispatched[unit.CrewId] = job.Id;
            Sicced.Remove(unit.CrewId);
            Marks.Remove(unit.CrewId);

            if (job.HasPlace)
                crews.MarchTo(unit, new Vector3(job.TargetX, 0f, job.TargetZ));
        }

        static void Work(DemoCrews crews, OutfitDirector outfit, DemoCrews.Unit unit, Job job)
        {
            var spec = OrderTable.SpecOf(job.Type);
            if (spec.Resolution != JobResolution.Street || job.StreetOutcome.HasValue)
                return;

            if (!Sicced.TryGetValue(unit.CrewId, out var on) || on != job.Id)
            {
                Sicced[unit.CrewId] = job.Id;
                var mark = NearestRival(crews, unit, job);
                if (mark != null)
                {
                    Marks[unit.CrewId] = mark;
                    crews.Sic(unit, mark);
                }
                return;
            }

            if (!Marks.TryGetValue(unit.CrewId, out var target) || target == null)
                return;

            // Nobody left standing on their side is the job done; nobody left standing
            // on ours is the job failed. Anything still unsettled is left alone - the
            // hours are still running, and the director's own roll is waiting behind
            // this if the fight never resolves either way.
            if (DemoCrews.Finished(target))
                outfit.ReportStreetOutcome(job.Id, OrderOutcome.Completed);
            else if (DemoCrews.Finished(unit))
                outfit.ReportStreetOutcome(job.Id, OrderOutcome.Failed);
        }

        static DemoCrews.Unit NearestRival(DemoCrews crews, DemoCrews.Unit unit, Job job)
        {
            if (!job.HasPlace)
                return null;

            var place = new Vector3(job.TargetX, 0f, job.TargetZ);
            DemoCrews.Unit best = null;
            var bestSqr = MarkRadius * MarkRadius;

            foreach (var other in crews.Units)
            {
                if (other == null || other == unit || other.Faction == 0 ||
                    other.IsPolice || other.Wiped)
                    continue;

                var to = other.Position - place;
                to.y = 0f;
                var sqr = to.sqrMagnitude;
                if (sqr >= bestSqr)
                    continue;
                bestSqr = sqr;
                best = other;
            }
            return best;
        }

        /// <summary>Back to the front, once, when the book empties. A crew with nothing
        /// to do standing at the last address it was sent to reads as a bug; a crew
        /// walking home reads as an outfit.</summary>
        static void SendHome(DemoCrews crews, OutfitDirector outfit, DemoCrews.Unit unit)
        {
            if (!Dispatched.ContainsKey(unit.CrewId))
                return;

            Dispatched.Remove(unit.CrewId);
            Sicced.Remove(unit.CrewId);
            Marks.Remove(unit.CrewId);

            if (outfit.TryGetHeadquarters(out var hq, out _))
                crews.MarchTo(unit, hq);
        }

        // Static state outlives Play when domain reload is off - the same trap
        // OverlayRegistry and DayClock reset against. A stale crew id here would send
        // next session's first crew to last session's address.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Dispatched.Clear();
            Sicced.Clear();
            Marks.Clear();
        }
    }
}
