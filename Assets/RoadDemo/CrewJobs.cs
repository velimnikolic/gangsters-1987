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

        /// <summary>Which job each crew has already acted the door beat for. A robbery
        /// is not all arithmetic: the lead man STEPS INSIDE the place he is turning
        /// over, once, the same beat the racket's conversations use.</summary>
        static readonly Dictionary<int, int> Entered = new Dictionary<int, int>();

        /// <summary>The wrecking beats already acted per crew: which job, how many
        /// rounds of swinging, and when the next one is due.</summary>
        static readonly Dictionary<int, (int JobId, int Count, float NextAt)> Swings =
            new Dictionary<int, (int, int, float)>();

        /// <summary>One Molotov leaves one hand per torch job: the bottle in the air, and
        /// whether it ever landed. A bottle lost before impact - the man carrying it shot
        /// down, the premises streamed out - leaves the job unworked, so the crew throws
        /// again rather than standing at a shop the order says to burn.</summary>
        static readonly Dictionary<int, (int JobId, MolotovProjectile Shot, bool Lit)>
            Torched = new Dictionary<int, (int, MolotovProjectile, bool)>();

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
                        // The clock does not decide whether they have arrived - the
                        // pavement does. A crew standing at the door with travel hours
                        // still on the book is a crew waiting for arithmetic.
                        if (AtPlace(crews, unit, job))
                            outfit.ReportArrived(job.Id);
                        break;
                    case JobStage.Working:
                        Work(crews, outfit, unit, job);
                        break;
                }
            }
        }

        /// <summary>How near the address a man has to be for the crew to count as
        /// there: the same reach the door beats use to find the man who acts.</summary>
        public const float ArrivedWithin = 9f;

        static bool AtPlace(DemoCrews crews, DemoCrews.Unit unit, Job job)
        {
            if (!job.HasPlace)
                return false;
            var door = new Vector3(job.TargetX, crews.GroundY, job.TargetZ);
            return LeadAt(unit, door, ArrivedWithin) != null;
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
            if (job.Type == OrderType.Raid)
                EnterOnce(crews, outfit, unit, job);
            else if (job.Type == OrderType.SmashUp)
                SwingBeat(crews, outfit, unit, job);
            else if (job.Type == OrderType.Torch)
                TorchBeat(crews, outfit, unit, job);

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

        /// <summary>The robbery's one visible beat: whichever man of the crew is at the
        /// door goes in through it. Marked done whether the beat played or not -
        /// DoorBeat refuses a man under fire, and then the fight at the door IS the
        /// scene, not a man popping calmly indoors in the middle of it.</summary>
        static void EnterOnce(
            DemoCrews crews, OutfitDirector outfit, DemoCrews.Unit unit, Job job)
        {
            if (!job.HasPlace)
                return;
            if (Entered.TryGetValue(unit.CrewId, out var did) && did == job.Id)
                return;

            var door = new Vector3(job.TargetX, crews.GroundY, job.TargetZ);
            CrewWalker lead = null;
            var best = 8f * 8f; // he must actually be AT the door, not walking up
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null ||
                    !man.Tf.gameObject.activeInHierarchy)
                    continue;
                var to = man.Tf.position - door;
                to.y = 0f;
                var sqr = to.sqrMagnitude;
                if (sqr >= best)
                    continue;
                best = sqr;
                lead = man;
            }

            if (lead == null)
                return;

            Entered[unit.CrewId] = job.Id;

            // HE WENT IN, AND THEN IT WAS ROBBED. That is the robbery, and the book is
            // told so from INSIDE - the same rule the demand and the collection keep. It
            // used to be reported in the same breath as the order to walk in, so a shop
            // was turned over while the man was still on the pavement outside it. (A
            // raid used to report nothing at all unless a rival crew happened to be
            // standing there and got wiped, which left the order - and every order behind
            // it - open for its whole span.)
            var told = outfit;
            var done = job;
            // No word at this door - a robbery goes straight in. When the canonical
            // premises is known, resolve its real streamed entrance and use the full
            // physical passage instead of the old hide-at-the-pavement fallback.
            if (!string.IsNullOrEmpty(job.TargetBusinessId))
            {
                DoorBeat.VisitBusiness(
                    lead,
                    new LivingCity.Territory.TerritoryBusinessId(job.TargetBusinessId),
                    door,
                    whenInside: () => Done(told, done));
            }
            else
            {
                DoorBeat.Visit(lead, door, talk: 0f, whenInside: () => Done(told, done));
            }
        }

        /// <summary>
        /// The street's answer for a job whose DEED is the whole of it - the front put
        /// in, the bottle thrown. Reported once: a second call while the same answer
        /// stands would be the same order finishing twice.
        /// </summary>
        static void Done(OutfitDirector outfit, Job job)
        {
            if (outfit == null || job == null || job.StreetOutcome.HasValue)
                return;
            outfit.ReportStreetOutcome(job.Id, OrderOutcome.Completed);
        }

        /// <summary>A smash-up is two clear blows, then the frontage is visibly broken.
        /// Keeping this just over two seconds makes it read as an action rather than a man
        /// mechanically beating the same pane for half the job.</summary>
        public const int PremisesSmashRounds = 2;
        public const float PremisesSmashEvery = 1.15f;
        public const float PremisesSmashFor = 0.9f;

        static void SwingBeat(
            DemoCrews crews, OutfitDirector outfit, DemoCrews.Unit unit, Job job)
        {
            if (!job.HasPlace)
                return;

            // He swings at the SHOPFRONT, not at the pavement he was marched to. The
            // job's target is the doorstep - a walkable spot on the kerb - and a bat
            // aimed there is a man beating the air a couple of metres short of the glass
            // that is about to be shattered. The torch has always thrown at the real
            // frontage; the bat now goes to the same place.
            var door = new Vector3(job.TargetX, crews.GroundY, job.TargetZ);
            var businessId = new LivingCity.Territory.TerritoryBusinessId(
                job.TargetBusinessId);
            if (businessId.IsValid &&
                ShopDamage.TryBusinessFrontage(businessId, out var frontage, out _))
                door = frontage;

            if (Swings.TryGetValue(unit.CrewId, out var swung) && swung.JobId == job.Id)
            {
                if (swung.Count >= PremisesSmashRounds)
                {
                    if (swung.Count > PremisesSmashRounds || ArmBeat.Acting(LeadAt(unit, door, 9f)))
                        return;

                    if (businessId.IsValid)
                        ShopDamage.SmashBusiness(businessId);
                    else
                        ShopDamage.SmashAt(
                            door, Vector3.forward, "JOB " + job.Id, crews.GroundY);

                    Swings[unit.CrewId] = (
                        job.Id, PremisesSmashRounds + 1, float.PositiveInfinity);
                    // THE DEED IS THE ANSWER. A smash-up used to report nothing at all
                    // unless a rival crew happened to be standing there and got wiped -
                    // so with nobody to fight, the front went in and the job sat open for
                    // its full hours with the men standing by, the NEXT order queued
                    // behind it, and the shop never told it had been wrecked (the racket
                    // only hears about a job that COMPLETES). The window is broken: that
                    // is the order carried out, and the book is told so now.
                    Done(outfit, job);
                    return;
                }
                if (Time.time < swung.NextAt)
                    return;
            }
            else
            {
                swung = default;
            }

            var lead = LeadAt(unit, door, 9f);
            if (lead == null)
                return;

            if (!ArmBeat.Swing(lead, door, PremisesSmashFor))
                return;
            Swings[unit.CrewId] = (
                job.Id, swung.Count + 1, Time.time + PremisesSmashEvery);
        }

        /// <summary>A torch is not a bat routine. The nearest hood at the premises throws
        /// one real Molotov model; the bottle's impact starts ShopDamage that same frame.</summary>
        static void TorchBeat(
            DemoCrews crews, OutfitDirector outfit, DemoCrews.Unit unit, Job job)
        {
            if (!job.HasPlace)
                return;
            if (Torched.TryGetValue(unit.CrewId, out var thrown) && thrown.JobId == job.Id &&
                (thrown.Lit || thrown.Shot != null))
                return;

            var door = new Vector3(job.TargetX, crews.GroundY, job.TargetZ);
            var outward = Vector3.forward;
            var businessId = new LivingCity.Territory.TerritoryBusinessId(
                job.TargetBusinessId);
            if (businessId.IsValid &&
                ShopDamage.TryBusinessFrontage(
                    businessId, out var frontageDoor, out var frontageOutward))
            {
                door = frontageDoor;
                outward = frontageOutward;
            }

            var lead = LeadAt(unit, door, 9f);
            if (lead == null)
                return;

            MolotovProjectile projectile;
            var impact = door + Vector3.up * 0.85f;
            var crewId = unit.CrewId;
            var jobId = job.Id;
            void Lit(Transform _)
            {
                Torched[crewId] = (jobId, null, true);
                // Same rule as the bat: the bottle landed and the front is alight, so
                // the order was carried out - the book is not left waiting on a fight.
                Done(outfit, job);
            }

            if (businessId.IsValid)
            {
                projectile = MolotovProjectile.ThrowAtBusiness(
                    lead, impact, businessId, Lit);
            }
            else
            {
                var towardStreet = lead.Tf.position - door;
                towardStreet.y = 0f;
                if (towardStreet.sqrMagnitude > 0.001f)
                    outward = towardStreet.normalized;
                projectile = MolotovProjectile.ThrowAt(
                    lead,
                    impact,
                    door,
                    outward,
                    "JOB " + job.Id,
                    crews.GroundY,
                    Lit);
            }

            if (projectile != null)
                Torched[crewId] = (jobId, projectile, false);
        }

        static CrewWalker LeadAt(DemoCrews.Unit unit, Vector3 point, float radius)
        {
            CrewWalker lead = null;
            var best = radius * radius;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null ||
                    !man.Tf.gameObject.activeInHierarchy)
                    continue;
                var to = man.Tf.position - point;
                to.y = 0f;
                var sqr = to.sqrMagnitude;
                if (sqr >= best)
                    continue;
                best = sqr;
                lead = man;
            }
            return lead;
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
            Torched.Remove(unit.CrewId);

            // Men the player has put INSIDE one of our own buildings are already home,
            // and a march would only walk them back out of it (CrewQuarters).
            if (CrewQuarters.Billeted(unit))
                return;

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
            Entered.Clear();
            Swings.Clear();
            Torched.Clear();
        }
    }
}
