using System.Collections;
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
    /// The reporting is one-way. Ordinary street work may still fall back to the
    /// director's own roll in a scene with no crew simulation; violence against the
    /// person behind a counter is stricter and waits for this wire, because neither an
    /// assault nor a death may be invented by an office roll.
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
        /// <summary>Which job a crew is currently driving toward. A premises job has
        /// two distinct travel legs when the crew starts aboard: drive to the address,
        /// park and get out, then walk from the kerb to the exact approach point.</summary>
        static readonly Dictionary<int, int> Driving = new Dictionary<int, int>();
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

        /// <summary>
        /// WHO IS SITTING ON WHICH DOOR (D10). A crew working a Guard order is on that
        /// address until the order is taken off it: an attack there is answered by these
        /// men before it is answered by the arithmetic.
        /// </summary>
        static readonly Dictionary<int, (int JobId, LivingCity.Territory.TerritoryBusinessId Door)>
            Guarding = new Dictionary<int, (int, LivingCity.Territory.TerritoryBusinessId)>();

        /// <summary>Which attacking crew each guard crew has already been set on, so
        /// the guards are sicced once per attack and not every frame. Its own book: a
        /// job number is unique in ONE family's book, and these are two families.
        /// </summary>
        static readonly Dictionary<int, int> GuardSicced = new Dictionary<int, int>();

        /// <summary>The crew of some OTHER house that is sitting on this door, or null.
        /// The street asks it before a wrecking beat; the book asks it through
        /// CampaignRunner.GuardOnTheDoor before a paper roll.</summary>
        public static DemoCrews.Unit GuardsAt(
            DemoCrews crews, LivingCity.Territory.TerritoryBusinessId door, int notFaction)
        {
            if (crews == null || !door.IsValid)
                return null;
            foreach (var pair in Guarding)
            {
                if (pair.Value.Door != door)
                    continue;
                var unit = crews.UnitOfCrew(pair.Key);
                if (unit == null || unit.Wiped || unit.Faction == notFaction)
                    continue;
                return unit;
            }
            return null;
        }

        /// <summary>
        /// WHETHER A HOUSE HAS MEN STANDING ON A DOOR OF THIS BLOCK (ruling A22b). A
        /// standing guard answers the incidents on the block it stands on, and only
        /// while it stands - the power ledger asks this before it files one. The block
        /// of each guarded door is the caller's to resolve; this only knows doors.
        /// </summary>
        public static bool HouseGuards(DemoCrews crews, int faction,
            System.Func<LivingCity.Territory.TerritoryBusinessId, bool> onTheBlock)
        {
            if (crews == null || onTheBlock == null)
                return false;
            foreach (var pair in Guarding)
            {
                var unit = crews.UnitOfCrew(pair.Key);
                if (unit == null || unit.Wiped || unit.Faction != faction)
                    continue;
                if (onTheBlock(pair.Value.Door))
                    return true;
            }
            return false;
        }

        /// <summary>Whether this crew has a travel leg out that the street has not yet
        /// answered - marched or driving toward its job. The probe prints it (AI-000).
        /// </summary>
        public static bool MarchOutstanding(int crewId) =>
            Dispatched.ContainsKey(crewId) || Driving.ContainsKey(crewId);

        /// <summary>
        /// SOMETHING ELSE TOOK THESE MEN. The crew's travel stamp is dropped, so the
        /// next tick of its book issues the march again rather than believing the men
        /// are still on their way somewhere they were pulled off. Called when a round
        /// opens over a crew that had a job out.
        /// </summary>
        public static void ForgetDispatch(int crewId)
        {
            Dispatched.Remove(crewId);
            Driving.Remove(crewId);
        }

        public static string PoliceWorkRefusal(int crewId)
        {
            var unit = DemoCrews.Active != null ? DemoCrews.Active.UnitOfCrew(crewId) : null;
            if (!DemoCrews.PoliceStopsWork(unit)) return null;
            return unit.ArrestChallenged
                ? DemoCrews.ArrestChallengeRefusal : DemoCrews.InCustodyRefusal;
        }

        /// <summary>The crew's missions and every one-shot dispatch/watch stamp end
        /// together. Clearing only its route lets the book send it straight back out.</summary>
        public static void Interrupt(DemoCrews.Unit unit)
        {
            if (unit == null || unit.IsDetachment) return;
            var house = Underworld.Current?.Of(unit.Faction);
            if (house != null && house.Runner.InterruptCrew(house.Roster, unit.CrewId) > 0)
                house.Touch();
            ForgetDispatch(unit.CrewId);
            Sicced.Remove(unit.CrewId);
            Marks.Remove(unit.CrewId);
            Entered.Remove(unit.CrewId);
            Swings.Remove(unit.CrewId);
            Torched.Remove(unit.CrewId);
            Guarding.Remove(unit.CrewId);
            GuardSicced.Remove(unit.CrewId);
        }

        /// <summary>The door a crew is standing a watch on, if it is (AI-000).</summary>
        public static bool TryGetWatch(int crewId,
            out LivingCity.Territory.TerritoryBusinessId door)
        {
            door = default;
            if (!Guarding.TryGetValue(crewId, out var watch))
                return false;
            door = watch.Door;
            return true;
        }

        public static void Tick(DemoCrews crews)
        {
            var outfit = OutfitDirector.Instance;
            var underworld = LivingCity.Outfit.Underworld.Current;
            if (crews == null || outfit == null || underworld == null)
                return;

            foreach (var unit in crews.Units)
            {
                if (unit == null || unit.IsPolice || unit.Wiped ||
                    DemoCrews.PoliceStopsWork(unit) || unit.Fleeing)
                    continue;

                // EVERY house's book is worked, off the crew's own house. The order was
                // filed in one family's book and the men who carry it are that family's.
                var house = underworld.Of(unit.Faction);
                if (house == null || house.Finished)
                    continue;

                var job = house.Runner.Book.CurrentFor(unit.CrewId);
                if (job == null)
                {
                    SendHome(crews, house, unit);
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
                            house.Runner.ReportArrived(job.Id);
                        break;
                    case JobStage.Working:
                        Work(crews, house, unit, job);
                        break;
                }
            }
        }

        /// <summary>
        /// The guards on this door are set on the men who came for it (D10 ii/iv), and
        /// the beat waits. Answers true while there is anybody left to get past.
        /// </summary>
        static bool SetUpon(DemoCrews crews, DemoCrews.Unit unit, Job job)
        {
            var door = new LivingCity.Territory.TerritoryBusinessId(job.TargetBusinessId);
            if (!door.IsValid)
                return false;
            var guards = GuardsAt(crews, door, unit.Faction);
            if (guards == null)
                return false;

            if (!GuardSicced.TryGetValue(guards.CrewId, out var on) ||
                on != unit.CrewId)
            {
                GuardSicced[guards.CrewId] = unit.CrewId;
                crews.Sic(guards, unit);
                // THE MEN CAME. Whatever the fight decides, the house that was paid to
                // keep the peace here has answered for it.
                TerritoryRuntime.Instance?.NoteGuardsEngaged(
                    door, new LivingCity.Territory.TerritoryGangId(guards.Faction));
            }
            return true;
        }

        /// <summary>The unit this man is standing in, or null when he is not on the
        /// street at all.</summary>
        static DemoCrews.Unit Holding(DemoCrews crews, int characterId)
        {
            if (crews == null || characterId < 0)
                return null;
            for (var i = 0; i < crews.Units.Count; i++)
            {
                var unit = crews.Units[i];
                if (unit == null || unit.Wiped)
                    continue;
                foreach (var man in unit.All())
                    if (man != null && !man.Dead && man.CharacterId == characterId)
                        return unit;
            }
            return null;
        }

        /// <summary>The watch is off: the order was finished, cancelled or the crew is
        /// gone. Called wherever a job leaves a crew's hands.</summary>
        public static void StandDown(int crewId) => Guarding.Remove(crewId);

        /// <summary>How near the address a man has to be for the crew to count as
        /// there: the same reach the door beats use to find the man who acts.</summary>
        public const float ArrivedWithin = 9f;

        static bool AtPlace(DemoCrews crews, DemoCrews.Unit unit, Job job)
        {
            // A rider passing the address has not arrived for a door job. The car must
            // first finish its ordinary kerb-side parking/exit sequence; only a man on
            // the pavement can advance the book to Working.
            if (!job.HasPlace || unit == null || unit.Car != null || unit.Leaving)
                return false;
            var door = new Vector3(job.TargetX, crews.GroundY, job.TargetZ);
            return LeadAt(unit, door, ArrivedWithin) != null;
        }

        /// <summary>Sends each travel leg once, not every frame. A crew already aboard
        /// drives to the address, parks and gets out through the regular car plumbing;
        /// after the last man is down, the crew walks from the kerb to the approach.</summary>
        static void March(DemoCrews crews, DemoCrews.Unit unit, Job job)
        {
            if (!job.HasPlace)
                return;

            var crewId = unit.CrewId;
            var destination = new Vector3(job.TargetX, crews.GroundY, job.TargetZ);

            if (unit.Car != null)
            {
                // Entering a car midway through a foot leg turns the remainder into a
                // drive. Do not leave the old one-shot foot stamp armed behind it.
                Dispatched.Remove(crewId);
                if (!Driving.TryGetValue(crewId, out var driven) || driven != job.Id)
                {
                    if (!crews.OrderUnit(unit, destination, out _, run: false, speak: false))
                        return;

                    Driving[crewId] = job.Id;
                    Sicced.Remove(crewId);
                    Marks.Remove(crewId);
                    // A BOOK JOB TAKES THE CREW OFF ITS ROUND (AI-002, ruling A2) -
                    // unless the player started that round with a key.
                    TerritoryRuntime.Instance?.BookJobTookTheCrew(crewId);
                }

                // DriveTo already chooses the reachable kerb nearest the address. Once
                // it has stopped, LeaveCar opens the doors and sets everybody down; a
                // later tick will issue the short foot leg.
                if (!unit.Leaving && unit.Car != null && !unit.Car.ParkingFailed && !unit.Car.Moving)
                    crews.LeaveCar(unit);
                return;
            }

            Driving.Remove(crewId);
            if (Dispatched.TryGetValue(crewId, out var sent) && sent == job.Id)
                return;

            // Stamp the leg only after a route was accepted. Stamping a rejected march
            // made this job, and every job queued behind it, wait forever.
            if (!crews.MarchTo(unit, destination))
                return;

            Dispatched[crewId] = job.Id;
            Sicced.Remove(crewId);
            Marks.Remove(crewId);
            // A BOOK JOB TAKES THE CREW OFF ITS ROUND (AI-002, ruling A2) - unless the
            // player started that round with a key. Secondary to the watchdog: the
            // measured rounds died on the way, not under a job.
            TerritoryRuntime.Instance?.BookJobTookTheCrew(crewId);
        }

        static void Work(DemoCrews crews, LivingCity.Outfit.House house,
            DemoCrews.Unit unit, Job job)
        {
            // The book's estimated travel hours can elapse before the physical car or
            // men arrive. Working on paper must not strand them wherever the clock
            // caught up: keep completing the drive, park/exit and foot leg until a man
            // is genuinely at the approach.
            if (!AtPlace(crews, unit, job))
            {
                March(crews, unit, job);
                return;
            }

            if (job.Type == OrderType.Guard)
            {
                // MEN ON THE DOOR. They are there; the address is theirs until the order
                // comes off. Nothing else happens on a Guard - a watch is stood.
                var door = new LivingCity.Territory.TerritoryBusinessId(job.TargetBusinessId);
                var stood = Guarding.TryGetValue(unit.CrewId, out var watch) &&
                            watch.JobId == job.Id;
                Guarding[unit.CrewId] = (job.Id, door);
                // THE MEN ARRIVED (A22b): whatever was still open against the house on
                // this block inside its window is answered the moment the watch is
                // stood, not only what happens after.
                if (!stood)
                    TerritoryRuntime.Instance?.NoteGuardStanding(
                        door, new LivingCity.Territory.TerritoryGangId(unit.Faction));
                return;
            }

            // A DOOR SOMEBODY IS SITTING ON IS NOT WALKED UP TO AND WRECKED. The guards
            // go at the attackers first, and the beat runs only once they are gone.
            if (SetUpon(crews, unit, job))
                return;

            // THESE ORDERS ARE AGAINST THE PERSON BEHIND THE COUNTER. They resolve only
            // after a strict physical threshold crossing and never fall through to the
            // generic street-fight target search below.
            if (job.Type == OrderType.Beating || job.Type == OrderType.KillOwner)
            {
                VisitOwner(crews, house, unit, job);
                return;
            }

            if (job.Type == OrderType.Raid)
                EnterOnce(crews, house, unit, job);
            else if (job.Type == OrderType.SmashUp)
                SwingBeat(crews, house, unit, job);
            else if (job.Type == OrderType.Torch)
                TorchBeat(crews, house, unit, job);

            var spec = OrderTable.SpecOf(job.Type);
            if (spec.Resolution != JobResolution.Street || job.StreetOutcome.HasValue)
                return;

            if (!Sicced.TryGetValue(unit.CrewId, out var on) || on != job.Id)
            {
                Sicced[unit.CrewId] = job.Id;
                // A KILL NAMES A MAN (D16). The crew is set on whichever unit he is
                // standing in, not on whoever happens to be nearest - and if he is
                // nowhere on the street the book takes him instead.
                var mark = Holding(crews, job.TargetCharacterId) ??
                           NearestRival(crews, unit, job);
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
                house.Runner.ReportStreetOutcome(job.Id, OrderOutcome.Completed);
            else if (DemoCrews.Finished(unit))
                house.Runner.ReportStreetOutcome(job.Id, OrderOutcome.Failed);
        }

        /// <summary>The robbery's one visible beat: whichever man of the crew is at the
        /// door goes in through it. Marked done whether the beat played or not -
        /// DoorBeat refuses a man under fire, and then the fight at the door IS the
        /// scene, not a man popping calmly indoors in the middle of it.</summary>
        static void EnterOnce(
            DemoCrews crews, LivingCity.Outfit.House house, DemoCrews.Unit unit,
            Job job)
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
            var told = house;
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
        static void Done(LivingCity.Outfit.House house, Job job)
        {
            if (house == null || job == null || !job.Live || job.StreetOutcome.HasValue)
                return;
            house.Runner.ReportStreetOutcome(job.Id, OrderOutcome.Completed);
        }

        static void Failed(LivingCity.Outfit.House house, Job job)
        {
            if (house == null || job == null || !job.Live || job.StreetOutcome.HasValue)
                return;
            house.Runner.ReportStreetOutcome(job.Id, OrderOutcome.Failed);
        }

        /// <summary>
        /// The proprietor jobs' strict threshold. A missing/unreachable real entrance is
        /// a failed order with no telephone, fear, death, or closure; all consequence is
        /// downstream of the inside callback.
        /// </summary>
        static void VisitOwner(DemoCrews crews, LivingCity.Outfit.House house,
            DemoCrews.Unit unit, Job job)
        {
            if (!job.HasPlace || Entered.TryGetValue(unit.CrewId, out var did) &&
                    did == job.Id)
                return;

            var door = new Vector3(job.TargetX, crews.GroundY, job.TargetZ);
            var lead = LeadAt(unit, door, ArrivedWithin);
            if (lead == null)
                return;

            var businessId = new LivingCity.Territory.TerritoryBusinessId(
                job.TargetBusinessId);
            var told = house;
            var done = job;
            var accepted = DoorBeat.TryVisitBusiness(
                lead, businessId, door,
                whenInside: () =>
                {
                    var business = LivingCity.Business.BusinessRuntime.Instance;
                    var cause = done.Type == OrderType.Beating
                        ? LivingCity.Business.BusinessShutdownCause.Beating
                        : LivingCity.Business.BusinessShutdownCause.Death;
                    if (!done.Live || done.StreetOutcome.HasValue ||
                        DemoCrews.PoliceStopsWork(unit) || business?.Shutdowns == null ||
                        business.Shutdowns.DamageRefusal(
                            businessId, cause, business.CurrentGameHour) != null)
                    {
                        Failed(told, done);
                        return;
                    }
                    if (done.Type == OrderType.Beating)
                    {
                        crews.StartCoroutine(BeatInside(crews, told, done, door));
                        return;
                    }

                    // One point-blank round, emitted by the exact same combat path as a
                    // pavement execution. The unseen body then goes down the public
                    // death wire at this door; every case that depended on him hears it.
                    if (!crews.ExecuteCivilian(lead, null, door))
                    {
                        Failed(told, done);
                        return;
                    }
                    business.RecordOwnerDeath(businessId);
                    StreetAlarm.Death(door, StreetAlarm.DeathOf.Civilian);
                    WitnessWatch.OwnerKilled(done.TargetBusinessId);
                    Done(told, done);
                },
                whenFailed: () => Failed(told, done));

            if (!accepted)
            {
                Failed(house, job);
                return;
            }
            Entered[unit.CrewId] = job.Id;
        }

        static IEnumerator BeatInside(DemoCrews crews,
            LivingCity.Outfit.House house, Job job, Vector3 door)
        {
            var punches = DemoSounds.Punches;
            for (var i = 0; i < punches.Length; i++)
            {
                if (!job.Live) yield break;
                DemoAudio.At(punches[i], door, DemoSounds.PunchVolume, 0.04f);
                yield return new WaitForSeconds(0.22f);
            }
            if (!job.Live) yield break;
            DemoAudio.At(DemoSounds.Pick(DemoSounds.Screams), door,
                DemoSounds.ScreamVolume, 0.035f);
            Done(house, job);
        }

        /// <summary>A smash-up is two clear blows, then the frontage is visibly broken.
        /// Keeping this just over two seconds makes it read as an action rather than a man
        /// mechanically beating the same pane for half the job.</summary>
        public const int PremisesSmashRounds = 2;
        public const float PremisesSmashEvery = 1.15f;
        public const float PremisesSmashFor = 0.9f;

        static void SwingBeat(
            DemoCrews crews, LivingCity.Outfit.House house, DemoCrews.Unit unit,
            Job job)
        {
            if (!job.HasPlace)
                return;

            // He swings at the SHOPFRONT, not at the pavement he was marched to. The
            // job's target is the doorstep - a walkable spot on the kerb - and a bat
            // aimed there is a man beating the air a couple of metres short of the glass
            // that is about to be shattered. The torch has always thrown at the real
            // frontage; the bat now goes to the same place.
            var approach = new Vector3(job.TargetX, crews.GroundY, job.TargetZ);
            var door = approach;
            var businessId = new LivingCity.Territory.TerritoryBusinessId(
                job.TargetBusinessId);
            if (businessId.IsValid &&
                ShopDamage.TryBusinessFrontage(businessId, out var frontage, out _))
                door = frontage;

            if (Swings.TryGetValue(unit.CrewId, out var swung) && swung.JobId == job.Id)
            {
                if (swung.Count >= PremisesSmashRounds)
                {
                    if (swung.Count > PremisesSmashRounds ||
                        ArmBeat.Acting(LeadAt(unit, approach, ArrivedWithin)))
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
                    Done(house, job);
                    return;
                }
                if (Time.time < swung.NextAt)
                    return;
            }
            else
            {
                swung = default;
            }

            var lead = LeadAt(unit, approach, ArrivedWithin);
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
            DemoCrews crews, LivingCity.Outfit.House house, DemoCrews.Unit unit,
            Job job)
        {
            if (!job.HasPlace)
                return;
            if (Torched.TryGetValue(unit.CrewId, out var thrown) && thrown.JobId == job.Id &&
                (thrown.Lit || thrown.Shot != null))
                return;

            var approach = new Vector3(job.TargetX, crews.GroundY, job.TargetZ);
            var door = approach;
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

            // The actor stands at the walkable approach; the projectile still strikes
            // the separately resolved facade. Requiring the actor to stand on the wall
            // made a bad facade guess deadlock this and every queued job behind it.
            var lead = LeadAt(unit, approach, ArrivedWithin);
            if (lead == null)
                return;

            MolotovProjectile projectile;
            var impact = door + Vector3.up * 0.85f;
            var crewId = unit.CrewId;
            var jobId = job.Id;
            void Lit(Transform _)
            {
                if (!job.Live) return;
                Torched[crewId] = (jobId, null, true);
                // Same rule as the bat: the bottle landed and the front is alight, so
                // the order was carried out - the book is not left waiting on a fight.
                Done(house, job);
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

        /// <summary>The crew a job at this address finds to put its hands on: ANY
        /// house's but the one that sent it (D23, row 8, the user's word of
        /// 2026-09-03). It used to skip house zero, so a family at war with another
        /// family could file an Assault at a street the player's crew was standing in
        /// and find nothing to hit - and the player was the one man in the city nobody's
        /// order could name. Families take each other on, and they take us on.</summary>
        static DemoCrews.Unit NearestRival(DemoCrews crews, DemoCrews.Unit unit, Job job)
        {
            if (!job.HasPlace)
                return null;

            var place = new Vector3(job.TargetX, 0f, job.TargetZ);
            DemoCrews.Unit best = null;
            var bestSqr = MarkRadius * MarkRadius;

            foreach (var other in crews.Units)
            {
                if (other == null || other == unit || other.Faction == unit.Faction ||
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
        static void SendHome(DemoCrews crews, LivingCity.Outfit.House house,
            DemoCrews.Unit unit)
        {
            if (!Dispatched.ContainsKey(unit.CrewId) && !Driving.ContainsKey(unit.CrewId))
                return;

            Dispatched.Remove(unit.CrewId);
            Driving.Remove(unit.CrewId);
            Sicced.Remove(unit.CrewId);
            Marks.Remove(unit.CrewId);
            Torched.Remove(unit.CrewId);
            Guarding.Remove(unit.CrewId);
            GuardSicced.Remove(unit.CrewId);

            // Men the player has put INSIDE one of our own buildings are already home,
            // and a march would only walk them back out of it (CrewQuarters).
            if (CrewQuarters.Billeted(unit))
                return;

            if (Home(house, out var hq))
            {
                if (unit.Car != null)
                    crews.OrderUnit(unit, hq, out _, run: false, speak: false);
                else
                    crews.MarchTo(unit, hq);
            }
        }

        /// <summary>A house's own door, to walk a finished crew back to: the player's
        /// headquarters as his director answers it, and every other family's own
        /// front.</summary>
        public static bool Home(LivingCity.Outfit.House house, out Vector3 door)
        {
            door = Vector3.zero;
            if (house == null)
                return false;
            if (house.IsPlayer)
            {
                var outfit = OutfitDirector.Instance;
                return outfit != null && outfit.TryGetHeadquarters(out door, out _);
            }

            var front = DemoCrews.FrontOf(house.GangId);
            if (front == null)
                return false;
            door = front.Outside;
            return true;
        }

        // Static state outlives Play when domain reload is off - the same trap
        // OverlayRegistry and DayClock reset against. A stale crew id here would send
        // next session's first crew to last session's address.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Dispatched.Clear();
            Driving.Clear();
            Sicced.Clear();
            Marks.Clear();
            Entered.Clear();
            Swings.Clear();
            Torched.Clear();
        }
    }
}
