using System.Collections.Generic;
using LivingCity.Police;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE FORCE, as an institution rather than a set of props.
    ///
    /// Until this the police were dealt once at scene build and never again. Nothing
    /// rotated, nothing was replaced: kill every officer and every car crew near you
    /// and there was no law in the city for the rest of the session, because Send found
    /// no unit and quietly did nothing. That is not a police force, it is a fixed number
    /// of lives.
    ///
    /// So each station keeps a ROSTER (PoliceRoster, pure and testable): what the city
    /// authorised, what is missing, and the absolute campaign day the department fills
    /// each hole. The bodies on the street are VIEWS of it -
    ///
    ///  * an officer killed (heard on StreetAlarm, like every other death) takes a man
    ///    off the roster of the precinct nearest the shooting;
    ///  * a wrecked car takes a car off it;
    ///  * on the campaign day tick, every hole whose day has come is filled - and the
    ///    men that fills reach the street only through the door, at the next handover;
    ///  * the WATCH decides how many of them are out at all: by day the beat walks and
    ///    half the cars stand in the yard, by night the reverse.
    ///
    /// Nothing is ever conjured mid-incident or dropped onto a pavement. A replacement
    /// car appears PARKED in a forecourt stall (the spread of resting cars over the
    /// city's kerbs gridlocked the ambient traffic - SpreadPatrolHomes' SPREAD = false
    /// lesson - and this must not reintroduce it).
    ///
    /// WHAT IS ON A ROSTER: everything. The cars docked on the forecourt, the crews they
    /// carry, the pair that rests behind the station door, AND the beat pairs dealt over
    /// the blocks all over the map. One rule, decided by the user on 2026-09-02: a
    /// precinct's strength is all the law this city has, so the plaque's number is the
    /// number, and the watch thins the beat across the whole map at night rather than
    /// only outside the station.
    ///
    /// A pair with a door stands its watch down INSIDE it; a block pair has no door, so
    /// it holds its corner instead - the long stand at a corner is already what the end
    /// of its round looks like. Either way it comes off the dispatcher's books, which is
    /// what "fewer men by night" has to mean if it is to mean anything.
    /// </summary>
    public sealed class PoliceForce : MonoBehaviour
    {
        /// <summary>One station house and everything the city gave it.</summary>
        public sealed class Precinct
        {
            public PoliceRoster Roster;

            /// <summary>Where the house stands - what "the nearest precinct" is measured
            /// from.</summary>
            public Vector3 At;

            /// <summary>Its door: where a replacement appears and where the watch
            /// changes.</summary>
            public Vector3 Door;

            public readonly List<PolicePatrolCar> Cars = new List<PolicePatrolCar>();

            /// <summary>The LEADS of its beat pairs; a wingman goes where his lead
            /// goes.</summary>
            public readonly List<PoliceFootPatrol> Leads = new List<PoliceFootPatrol>();

            /// <summary>Where a transfer is driven to: the far end of the road network.
            /// The county court and the state prison are not on this map and nothing
            /// invents them (the project's own rule about map data) - what the player
            /// sees is a police car with a man in the back leaving town, which in 1987
            /// is exactly what a transfer to the county seat looked like.</summary>
            public Vector3 CountyLine;
        }

        /// <summary>One transfer on the road.</summary>
        sealed class Convoy
        {
            public PolicePatrolCar Car;
            public Precinct From;
            public readonly List<Prisoner> Riders = new List<Prisoner>();
            public float By;          // the backstop: a drive that never arrives
            public string WasCalled;  // the car's own name, put back when it docks
        }

        /// <summary>Every dial in one place (replacement days, the watch hours and the
        /// share of the precinct each watch puts out).</summary>
        public readonly PoliceRosterConfig Config = new PoliceRosterConfig();

        /// <summary>Everyone the city is holding, and what becomes of them (GAN-219).
        /// Pure state; the drives below are its only body.</summary>
        public readonly PrisonPipeline Pipeline = new PrisonPipeline();

        readonly List<Precinct> _precincts = new List<Precinct>();
        readonly List<PoliceLossRecord> _filled = new List<PoliceLossRecord>();
        readonly List<Prisoner> _forTransfer = new List<Prisoner>();
        readonly List<Convoy> _convoys = new List<Convoy>();

        PoliceDispatch _dispatch;
        int _day = -1;
        bool _watchKnown;
        PoliceWatch _watch;

        /// <summary>A replacement car for this precinct, parked in a free stall and
        /// registered with the dispatcher - or null when the scene cannot make one. The
        /// builder owns every piece of knowledge this needs (the prefabs, the stall
        /// geometry, the lane the fleet undocks onto), so it hands the FORCE a way to
        /// ask rather than the force reaching into the builder.</summary>
        public System.Func<Precinct, PolicePatrolCar> MakeCar;

        public IReadOnlyList<Precinct> Precincts => _precincts;

        /// <summary>The city's one force. The ledger's own pages need to reach the
        /// docket - a man's file has to be able to say what he is charged with and what
        /// bail would cost - and everything else in this scene that owns city-wide
        /// state is reachable the same way (TerritoryRuntime.Instance).</summary>
        public static PoliceForce Instance { get; private set; }

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Init(PoliceDispatch dispatch) => _dispatch = dispatch;

        /// <summary>A station house, with the strength the city authorised it.</summary>
        public Precinct Add(int stationId, string name, Vector3 at, Vector3 door,
            Vector3 countyLine, int cars, int officers)
        {
            var precinct = new Precinct
            {
                Roster = new PoliceRoster(stationId, name, cars, officers),
                At = at,
                Door = door,
                CountyLine = countyLine,
            };
            _precincts.Add(precinct);
            return precinct;
        }

        /// <summary>Whose end of town this is. One station makes this trivial; it is
        /// written as a search because the day there are several, a loss has to land on
        /// the right books and not on the first ones in the list.</summary>
        public Precinct Nearest(Vector3 where)
        {
            Precinct best = null;
            var bestD = float.MaxValue;
            for (var i = 0; i < _precincts.Count; i++)
            {
                var d = (_precincts[i].At - where).sqrMagnitude;
                if (d < bestD) { bestD = d; best = _precincts[i]; }
            }
            return best;
        }

        /// <summary>The first precinct - what a scene with one station means by "the
        /// station", and what the plaque reads off.</summary>
        public Precinct Station => _precincts.Count > 0 ? _precincts[0] : null;

        /// <summary>An officer went down. Told through the dispatcher, which already
        /// hears every death on StreetAlarm - no second channel for the same fact.</summary>
        public void OfficerDown(Vector3 where)
        {
            var precinct = Nearest(where);
            if (precinct == null) return;
            var loss = precinct.Roster.Lose(PoliceLoss.Officer, Today(), Config);
            if (loss == null) return;
            CrewOverlay.Announce(
                precinct.Roster.Empty
                    ? "NO LAW LEFT AT " + Plain(precinct.Roster.Name)
                    : "AN OFFICER DOWN — " + Plain(precinct.Roster.Name) + " IS A MAN SHORT",
                5f, new Color(0.55f, 0.78f, 1f));
        }

        void Update()
        {
            TickWrecks();
            TickWatch();
            TickDay();
            TickConvoys();
        }

        // ------------------------------------------------------------------ transfers

        /// <summary>
        /// THE TRANSFER, on the road (GAN-219, PIPE-002). A car of the precinct's own -
        /// off its roster, never conjured - takes the men whose day in court has come and
        /// drives them out of town. A precinct with no car free sends nobody today and
        /// the transfer waits for tomorrow, which is a thing the player can arrange.
        ///
        /// Wreck it - shoot it up, put a charge under it - and the escort is dead and the
        /// men in the back are on the pavement (PIPE-003). Killing the escort is killing
        /// police: both deaths go down StreetAlarm like any other, so the heat, the
        /// roster and the swarm all hear it through the one channel they already listen
        /// on.
        /// </summary>
        void RunTransfers()
        {
            for (var i = 0; i < _forTransfer.Count; i++)
            {
                var prisoner = _forTransfer[i];
                var precinct = Station;
                if (precinct == null) continue;

                var car = FreeCar(precinct);
                if (car == null)
                {
                    // no car on the roster today: he waits, and the pipeline gives him
                    // tomorrow rather than losing him
                    Pipeline.BackToTheCells(prisoner, Today());
                    continue;
                }

                var convoy = Riding(precinct, car);
                convoy.Riders.Add(prisoner);
                Pipeline.Away(prisoner);
            }
            _forTransfer.Clear();
        }

        Convoy Riding(Precinct precinct, PolicePatrolCar car)
        {
            for (var i = 0; i < _convoys.Count; i++)
                if (_convoys[i].Car == car) return _convoys[i];

            var convoy = new Convoy
            {
                Car = car,
                From = precinct,
                By = Time.time + TransferPatience,
                WasCalled = car.Tf != null ? car.Tf.name : "",
            };
            _convoys.Add(convoy);
            car.RouteTo(precinct.CountyLine, 0f);
            if (car.Tf != null) car.Tf.name = "Prisoner Transfer";
            CrewOverlay.Announce("A PRISONER TRANSFER IS ON THE ROAD",
                5f, new Color(0.55f, 0.78f, 1f));
            return convoy;
        }

        /// <summary>Seconds a transfer is given to get out of town before the force
        /// stops waiting on it. A jammed road must not strand a man in a state nothing
        /// ever leaves - he goes back in the cells and rides again tomorrow.</summary>
        const float TransferPatience = 300f;

        void TickConvoys()
        {
            for (var i = _convoys.Count - 1; i >= 0; i--)
            {
                var convoy = _convoys[i];
                var car = convoy.Car;
                // A CAR THAT VANISHED IS NOT AN AMBUSH. Only a WRECK frees the men -
                // somebody has to have done it. A body destroyed for any other reason (a
                // scene torn down, a rebuild) would otherwise open the doors of every
                // transfer in the city and report two officers killed that nobody killed.
                if (car == null || car.Tf == null)
                {
                    for (var r = 0; r < convoy.Riders.Count; r++)
                        Pipeline.BackToTheCells(convoy.Riders[r], Today());
                    _convoys.RemoveAt(i);
                    continue;
                }
                if (car.Wrecked) { Ended(convoy, wrecked: true); _convoys.RemoveAt(i); continue; }
                if (((IPoliceUnit)car).OnScene) { Ended(convoy, wrecked: false); _convoys.RemoveAt(i); continue; }
                if (Time.time > convoy.By)
                {
                    // it never got there: back to the cells, and the car goes home
                    for (var r = 0; r < convoy.Riders.Count; r++)
                        Pipeline.BackToTheCells(convoy.Riders[r], Today());
                    Release(convoy);
                    _convoys.RemoveAt(i);
                }
            }
        }

        void Ended(Convoy convoy, bool wrecked)
        {
            var roster = Roster();
            var today = Today();
            if (wrecked)
            {
                // THE ESCORT IS DEAD. Two officers, down the one channel every other
                // death in the city goes down, so the heat, the arrest window, the
                // roster and the swarm all hear it exactly once and in the same way.
                var where = convoy.Car != null && convoy.Car.Tf != null
                    ? convoy.Car.Tf.position : convoy.From.At;
                StreetAlarm.Death(where, StreetAlarm.DeathOf.Officer);
                StreetAlarm.Death(where, StreetAlarm.DeathOf.Officer);

                var freed = 0;
                for (var r = 0; r < convoy.Riders.Count; r++)
                    if (Pipeline.Freed(roster, convoy.Riders[r], today) != null) freed++;
                if (freed > 0)
                {
                    var director = LivingCity.Gameplay.PersonnelDirector.Instance;
                    if (director != null) director.Touch();
                    CrewOverlay.Announce(
                        freed == 1 ? "A MAN IS OUT OF THE BACK OF IT"
                                   : freed + " MEN ARE OUT OF THE BACK OF IT",
                        6f, new Color(0.95f, 0.9f, 0.6f));
                }
                return;
            }

            // THE TRIAL, not a sentencing (GAN-245). What comes back per man is one of
            // three things, and the street is told which - a case thrown out for want
            // of a witness reads nothing like a sentence, and the player who spent five
            // days leaning on that witness has to see it.
            var sentenced = 0;
            var walked = 0;
            var dismissed = false;
            for (var r = 0; r < convoy.Riders.Count; r++)
            {
                var rider = convoy.Riders[r];
                var file = rider.CaseId >= 0 ? Pipeline.FindCase(rider.CaseId) : null;
                Pipeline.Tried(roster, rider, today);
                if (rider.Stage == PrisonStage.Sentenced) sentenced++;
                else walked++;
                if (file != null && file.Status == CaseStatus.Dismissed) dismissed = true;
                // The wire through the one door; the BANNER is the aggregate below,
                // because a car brings several men at once and one line per man would
                // say the same thing four times.
                AnnounceVerdict(roster, rider,
                    file != null ? file.Status : CaseStatus.Tried, banner: false);
            }
            if (convoy.Riders.Count > 0)
            {
                var director = LivingCity.Gameplay.PersonnelDirector.Instance;
                if (director != null) director.Touch();
                if (sentenced > 0)
                    CrewOverlay.Announce("THE COURT HAS PASSED SENTENCE",
                        5f, new Color(0.55f, 0.78f, 1f));
                if (dismissed)
                    CrewOverlay.Announce("CASE DISMISSED — NOBODY WOULD GIVE EVIDENCE",
                        5f, new Color(0.75f, 0.95f, 0.7f));
                else if (walked > 0)
                    CrewOverlay.Announce(
                        walked == 1 ? "ACQUITTED" : walked + " MEN ACQUITTED",
                        5f, new Color(0.75f, 0.95f, 0.7f));
            }
            Release(convoy);
        }

        void Release(Convoy convoy)
        {
            if (convoy.Car == null) return;
            if (convoy.Car.Tf != null && convoy.WasCalled.Length > 0)
                convoy.Car.Tf.name = convoy.WasCalled;
            convoy.Car.Release();
        }

        PolicePatrolCar FreeCar(Precinct precinct)
        {
            for (var i = 0; i < precinct.Cars.Count; i++)
            {
                var car = precinct.Cars[i];
                if (car == null || car.Wrecked) continue;
                if (!((IPoliceUnit)car).Available) continue;
                return car;
            }
            return null;
        }

        static LivingCity.Personnel.Roster Roster()
        {
            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            return director != null ? director.Roster : null;
        }

        /// <summary>A car blown up or shot to pieces is off the roster. Polled rather
        /// than pushed: a wreck is a state the traffic model already keeps (RoadCar.
        /// Wrecked), and a second notification channel for it would be one more thing
        /// that can be forgotten at a call site.</summary>
        void TickWrecks()
        {
            for (var p = 0; p < _precincts.Count; p++)
            {
                var precinct = _precincts[p];
                for (var i = precinct.Cars.Count - 1; i >= 0; i--)
                {
                    var car = precinct.Cars[i];
                    if (car == null) { precinct.Cars.RemoveAt(i); continue; }
                    if (!car.Wrecked) continue;
                    precinct.Cars.RemoveAt(i);
                    precinct.Roster.Lose(PoliceLoss.Car, Today(), Config);
                }
            }
        }

        /// <summary>The handover. Read off the city's own clock - there is one clock and
        /// this does not add another - and applied only when the watch actually turns,
        /// so nothing is re-ordered every frame.</summary>
        void TickWatch()
        {
            // THE city's clock, off the registry the clocks post themselves to - the
            // force is founded a pass before the day/night stack is built, so it starts
            // watchless and picks the hour up the frame there is one.
            var clock = LivingCity.Ambient.DayClock.Current;
            if (clock == null) return;
            var watch = PoliceShifts.At(clock.Hour, Config);
            if (_watchKnown && watch == _watch) return;
            var first = !_watchKnown;
            _watchKnown = true;
            _watch = watch;
            for (var i = 0; i < _precincts.Count; i++) ApplyWatch(_precincts[i], first);
            if (!first)
                CrewOverlay.Announce(
                    watch == PoliceWatch.Night ? "THE NIGHT WATCH IS COMING ON"
                                               : "THE DAY WATCH IS COMING ON",
                    4f, new Color(0.55f, 0.78f, 1f));
        }

        /// <summary>Who is out on this watch. The list order is the seniority: the first
        /// cars and the first pairs hold the watch, the rest stand down. A man or a car
        /// stood down finishes what it is doing first - nothing is yanked off the street
        /// mid-leg, because a car that vanished at seven o'clock would read as a car
        /// that had been deleted.</summary>
        void ApplyWatch(Precinct precinct, bool first)
        {
            var cars = PoliceShifts.CarsOnDuty(precinct.Roster, _watch, Config);
            for (var i = 0; i < precinct.Cars.Count; i++)
            {
                var car = precinct.Cars[i];
                if (car == null) continue;
                if (i < cars) car.StandTo(first ? 0f : Random.Range(2f, Config.HandoverSeconds));
                else car.StandDown();
            }

            var men = PoliceShifts.FootOnDuty(precinct.Roster, _watch, Config);
            var pairs = men / 2;
            for (var i = 0; i < precinct.Leads.Count; i++)
            {
                var lead = precinct.Leads[i];
                if (lead == null) continue;
                if (i < pairs) lead.StandTo(first ? 0f : Random.Range(2f, Config.HandoverSeconds));
                else lead.StandDown();
            }
        }

        /// <summary>Midnight in the books. The campaign's day is the only day there is,
        /// and it moves in exactly one place (Campaign.DayTick through OutfitDirector) -
        /// so watching the number is the day boundary, and no new event is needed to
        /// hang a replacement off.</summary>
        readonly List<Prisoner> _forfeited = new List<Prisoner>();
        readonly List<Prisoner> _paperTried = new List<Prisoner>();

        /// <summary>What the court did to one man, said once - on the wire and over the
        /// street. ONE door for it, because a verdict reached on paper at the day tick
        /// and a verdict reached off the back of a convoy are the same fact and used to
        /// be announced by only one of the two.</summary>
        void AnnounceVerdict(LivingCity.Personnel.Roster roster, Prisoner prisoner,
            CaseStatus status, bool banner = true)
        {
            if (prisoner == null) return;
            var man = roster != null ? roster.Find(prisoner.CharacterId) : null;
            LawWire.Verdict(man, prisoner.Stage, status);
            if (!banner) return;

            if (prisoner.Stage == PrisonStage.Sentenced)
                CrewOverlay.Announce("THE COURT HAS PASSED SENTENCE",
                    5f, new Color(0.55f, 0.78f, 1f));
            else if (status == CaseStatus.Dismissed)
                CrewOverlay.Announce("CASE DISMISSED — NOBODY WOULD GIVE EVIDENCE",
                    5f, new Color(0.75f, 0.95f, 0.7f));
            else
                CrewOverlay.Announce("ACQUITTED", 5f, new Color(0.75f, 0.95f, 0.7f));
        }

        /// <summary>
        /// THE FEAR GATE. Whether the shopkeeper who rang is still willing on the
        /// morning of the trial - the one thing about a case only the street can
        /// answer, so the pipeline asks through this rather than reaching into the
        /// Territory layer itself.
        ///
        /// A Connected owner always turns up; anybody else keeps quiet once the family
        /// has frightened him past Verdict.TestifyFearCap. There is no separate button
        /// for silencing him: the crew leans on his SHOP, exactly as it always did, and
        /// this reads what that did to him.
        /// </summary>
        static bool StillTalks(CourtCase file)
        {
            if (file == null || string.IsNullOrEmpty(file.BusinessId))
                return true;
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null)
                return true;

            var businessId = new LivingCity.Territory.TerritoryBusinessId(file.BusinessId);
            if (runtime.OwnerProfileOf(businessId).Trait ==
                LivingCity.Territory.TerritoryOwnerTrait.Connected)
                return true;
            return runtime.BusinessFearOf(businessId,
                new LivingCity.Territory.TerritoryGangId(file.GangId)) < Verdict.TestifyFearCap;
        }

        void TickDay()
        {
            var today = Today();
            if (today <= 0 || today == _day) return;
            var known = _day > 0;
            _day = today;

            // the day in court: whoever is due goes out in a car of the precinct's own,
            // and anybody the roster released leaves the pipe (GAN-219)
            var roster = Roster();
            if (roster != null)
            {
                if (Pipeline.RosterSeed == 0) Pipeline.RosterSeed = roster.Seed;
                Pipeline.ComplainantStillTalks ??= StillTalks;
                Pipeline.Discharged(roster);
                CoolTheWanted(roster, today);

                // BAIL IS SPENT ON THE DAY, whichever way it goes (GAN-245): a man who
                // turns up is tried on paper with the rest of his case, and one who is
                // hidden, out of town or told to skip forfeits the money and is looked
                // for. Here rather than in the convoy because a bailed man is not in
                // the back of a car - he is on the street, or he is not.
                _forfeited.Clear();
                _paperTried.Clear();
                if (Pipeline.TryOnPaper(roster, today, _forfeited, _paperTried) > 0)
                {
                    for (var i = 0; i < _forfeited.Count; i++)
                        LawWire.BailForfeit(roster.Find(_forfeited[i].CharacterId));
                    for (var i = 0; i < _paperTried.Count; i++)
                    {
                        var paper = _paperTried[i];
                        var file = paper.CaseId >= 0 ? Pipeline.FindCase(paper.CaseId) : null;
                        AnnounceVerdict(roster, paper,
                            file != null ? file.Status : CaseStatus.Tried);
                    }
                    var director = LivingCity.Gameplay.PersonnelDirector.Instance;
                    if (director != null) director.Touch();
                }
            }
            Pipeline.DayTick(today, _forTransfer);
            RunTransfers();

            if (!known) return;   // the first day merely learns what day it is

            for (var i = 0; i < _precincts.Count; i++)
            {
                var precinct = _precincts[i];
                if (precinct.Roster.Replace(today, _filled) == 0) continue;

                var cars = 0;
                for (var f = 0; f < _filled.Count; f++)
                    if (_filled[f].Kind == PoliceLoss.Car) cars++;

                // A REPLACEMENT CAR NEEDS A BODY; A REPLACEMENT MAN DOES NOT. The men a
                // precinct loses are the crews that get out of its cars - they are dealt
                // for a call and gone again (PoliceDispatch.SpawnSquad), so restoring
                // the NUMBER restores them. A wrecked car left a hole on the forecourt,
                // and that hole has to be filled with a car.
                for (var c = 0; c < cars && MakeCar != null; c++)
                {
                    var car = MakeCar(precinct);
                    if (car == null) break;
                    car.Precinct = precinct.Roster.StationId;
                    precinct.Cars.Add(car);
                    if (_dispatch != null) _dispatch.Register(car);
                }

                // and whatever came back reports for the next watch, not this second
                _watchKnown = false;
                CrewOverlay.Announce(Plain(precinct.Roster.Name) + " IS BACK UP TO STRENGTH",
                    5f, new Color(0.55f, 0.78f, 1f));
            }
        }

        /// <summary>
        /// THE ONLY CURE (GAN-222, FLEE-004). A day the city did not see him is a day
        /// off a wanted man's grade; a day it did resets him to nothing. A cop-killer's
        /// grade never comes off, whatever he does with his time.
        ///
        /// And the man sent out of town comes home: the roster's own discharge puts him
        /// back on his feet, and the payroll starts again with him.
        /// </summary>
        static void CoolTheWanted(LivingCity.Personnel.Roster roster, int today)
        {
            var cleared = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.OutOfTown &&
                    member.Status == LivingCity.Personnel.CharacterStatus.Active)
                    member.OutOfTown = false;
                if (WantedLevels.DayTick(member, today)) cleared++;
            }
            if (cleared > 0)
                CrewOverlay.Announce(cleared == 1
                        ? "ONE OF OURS IS OFF THE WANTED LIST"
                        : cleared + " OF OURS ARE OFF THE WANTED LIST",
                    5f, new Color(0.95f, 0.9f, 0.6f));
        }

        static int Today()
        {
            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            return outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
        }

        static string Plain(string name) =>
            string.IsNullOrEmpty(name) ? "THE PRECINCT" : name.ToUpperInvariant();
    }
}
