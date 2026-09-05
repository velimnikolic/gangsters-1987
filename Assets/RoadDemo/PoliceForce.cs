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
            public readonly List<PoliceBeat> Leads = new List<PoliceBeat>();

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

            /// <summary>Which drive this is: the station to the court, or the court out
            /// of town (GAN-237). Both are real roads and both can be taken.</summary>
            public PrisonLeg Leg;

            /// <summary>Where it is going - the courthouse forecourt or the county line.
            /// </summary>
            public Vector3 To;

            /// <summary>Where it must call FIRST, to collect the man: the station that
            /// sent it for a court run, the courthouse for a prison run. A car free to
            /// take a transfer may be resting in its stall or half-way across the city,
            /// so a drive that started at the car rather than at the prisoner would have
            /// teleported him out of the cells (GAN-237).</summary>
            public Vector3 Pickup;
            public Vector3? OriginDoor;

            /// <summary>He is actually in the back. Until then the escort can be killed
            /// but nobody walks out of it - he was never in the car.</summary>
            public bool Loaded;

            /// <summary>The exact prisoner body and real escort riding this car.</summary>
            public PrisonerCarriage Carriage;
            public DemoCrews.Unit Attacker;
            public PolicePatrolCar Recovery;
            public string RecoveryWasCalled;
            public bool Dismounted;
            public int SwarmRaises;
            public bool Blocked;
            public CrewCar Blockade;
            public Carriageway BlockedRoad;
            public int BlockedHeading;
            public bool AwaitingCourtExit;
            public bool Closed;
            public bool LeaveCarStood;
            public float HaltedAt;
            public float RecoveryRetryAt;

            public readonly List<Prisoner> Riders = new List<Prisoner>();
            public float By;          // the backstop: a drive that never arrives
            public Vector3 DrivingAnchor;
            public float DrivingHardBy;
            public float HardBy;      // absolute ceiling: retries never extend this state
            public string WasCalled;  // the car's own name, put back when it docks
        }

        sealed class LostBeat
        {
            public Precinct Precinct;
            public PoliceBeat Beat;
            public int BackOnDay;
        }

        /// <summary>
        /// THE COURTHOUSE FORECOURT, when the city stands one (GAN-237). Set by whoever
        /// built the city; left invalid where no court was placed, and then the first leg
        /// drives out of town like the second - because the project's rule is that a leg
        /// does not pretend to drive to a building nobody put on the map.
        /// </summary>
        public Vector3 CourthouseKerb { get; private set; }
        public Vector3 CourthouseDoor { get; private set; }

        public bool HasCourthouse { get; private set; }

        /// <summary>What the court is called on the ledger and the map.</summary>
        public string CourthouseName { get; private set; } = "";

        public void StandCourthouse(Vector3 kerb, Vector3 door, string name)
        {
            CourthouseKerb = kerb;
            CourthouseDoor = door;
            CourthouseName = string.IsNullOrEmpty(name) ? "the County Courthouse" : name;
            HasCourthouse = true;
        }

        /// <summary>The streamed city no longer has a court. Transfer scheduling then
        /// falls back to the county line; no caller is allowed to keep using a stale
        /// doorway from an unloaded district.</summary>
        public void ClearCourthouse()
        {
            CourthouseKerb = default;
            CourthouseDoor = default;
            CourthouseName = "";
            HasCourthouse = false;
        }

        /// <summary>How many transfers are on the road right now. Two men due on the
        /// same day ride in two cars, and the map has to draw both of them.</summary>
        public int Transfers => _convoys.Count;

        /// <summary>One transfer on the road: the car to draw and the leg it is running.
        /// False for an index past the end, so a caller can walk it without a lock.
        /// </summary>
        public bool TryGetTransfer(int index, out PolicePatrolCar car, out PrisonLeg leg)
        {
            car = null;
            leg = PrisonLeg.None;
            if (index < 0 || index >= _convoys.Count)
                return false;
            car = _convoys[index].Car;
            leg = _convoys[index].Leg;
            return true;
        }

        public bool TryGetTransfer(int index, out PolicePatrolCar car,
            out PrisonLeg leg, out CarriageStage stage)
        {
            stage = CarriageStage.Calling;
            if (!TryGetTransfer(index, out car, out leg)) return false;
            var convoy = _convoys[index];
            if (convoy.Carriage != null) stage = convoy.Carriage.Stage;
            return true;
        }

        /// <summary>The night watch and any focused HUD ask for the convoy carrying one
        /// named person, never merely the first transfer in the city.</summary>
        public bool TryGetPrisonerTransfer(int characterId, out PolicePatrolCar car,
            out PrisonLeg leg, out CarriageStage stage, out DemoCrews.Unit escort,
            out int swarmRaises)
        {
            car = null;
            leg = PrisonLeg.None;
            stage = CarriageStage.Calling;
            escort = null;
            swarmRaises = 0;
            for (var i = 0; i < _convoys.Count; i++)
            {
                var convoy = _convoys[i];
                if (convoy == null) continue;
                var owns = false;
                for (var r = 0; r < convoy.Riders.Count; r++)
                    if (convoy.Riders[r]?.CharacterId == characterId)
                    {
                        owns = true;
                        break;
                    }
                if (!owns) continue;
                car = convoy.Car;
                leg = convoy.Leg;
                if (convoy.Carriage != null)
                {
                    stage = convoy.Carriage.Stage;
                    escort = convoy.Carriage.Escort;
                }
                swarmRaises = convoy.SwarmRaises;
                return true;
            }
            return false;
        }

        /// <summary>The car the scheduler would take next, without reserving or moving
        /// it. The night-watch driver uses this read to put a charge under the exact
        /// empty carrier before it is called; production scheduling still owns the car.</summary>
        public bool TryGetFreeTransferCar(out PolicePatrolCar car)
        {
            car = null;
            for (var i = 0; i < _precincts.Count && car == null; i++)
                car = FreeCar(_precincts[i]);
            return car != null;
        }

        public bool IsTransfer(RoadCar car)
        {
            for (var i = 0; i < _convoys.Count; i++)
                if (_convoys[i].Car == car || _convoys[i].Recovery == car)
                    return true;
            return false;
        }

        /// <summary>The wire has told the player this carriage is on the road. From this
        /// edge its map mark and pointer target are public even beyond ordinary fog.</summary>
        public bool IsAnnouncedTransfer(RoadCar car)
        {
            for (var i = 0; i < _convoys.Count; i++)
            {
                var convoy = _convoys[i];
                if (convoy == null || convoy.Car != car || convoy.Carriage == null)
                    continue;
                var stage = convoy.Carriage.Stage;
                return stage == CarriageStage.Riding || stage == CarriageStage.Halted ||
                       stage == CarriageStage.WalkingIn || stage == CarriageStage.Delivered;
            }
            return false;
        }

        /// <summary>The HUD position of one man while the city physically holds him.
        /// Release transitions retire this pin before the active roster is projected
        /// back onto the street.</summary>
        public void PinCustody(int characterId, Vector3 at)
        {
            if (characterId >= 0) _custodyKeepAlive.Add(characterId);
        }

        public void KeepCustodyAlive(int characterId)
        {
            if (characterId >= 0) _custodyKeepAlive.Add(characterId);
        }

        public bool KeepsCustodyAlive(int characterId)
        {
            // The explicit release edge owns this lifetime. In particular, an acquitted
            // man's pipeline row is removed when the judge speaks, several seconds before
            // his body has walked back through the courthouse door; deriving liveness
            // from the paper stage would let Sync delete him halfway through that exit.
            return _custodyKeepAlive.Contains(characterId);
        }

        internal bool KeepsUnbookedBody(int characterId) =>
            _dispatch != null && _dispatch.KeepsUnbookedBody(characterId);

        public bool TryCustodyPosition(int characterId, out Vector3 at)
        {
            if (KeepsCustodyAlive(characterId))
            {
                var body = DemoCrews.Active?.BodyOf(characterId);
                if (body?.Tf != null)
                {
                    at = body.Tf.position;
                    return true;
                }
            }
            at = default;
            return false;
        }

        /// <summary>End the physical custody lifetime. With no relocation the held
        /// station body comes back through its own door; court and wreck exits supply
        /// the actual point at which the man was released.</summary>
        public bool ReleaseCustodyTracking(int characterId, Vector3 at,
            bool relocate)
        {
            var wasTracked = _custodyKeepAlive.Remove(characterId);
            DemoCrews.Active?.ReleaseCustodyTracking(characterId, at, relocate);
            return wasTracked;
        }

        public bool ReleaseCustodyTracking(int characterId)
        {
            TryCustodyPosition(characterId, out var at);
            return ReleaseCustodyTracking(characterId, at, relocate: false);
        }

        /// <summary>True only after the man has actually been loaded into a later
        /// court/prison transfer. A car travelling to collect him does not count.</summary>
        public bool CustodyInTransit(int characterId)
        {
            for (var i = 0; i < _convoys.Count; i++)
            {
                var convoy = _convoys[i];
                if (convoy == null || !convoy.Loaded ||
                    convoy.Carriage == null || !convoy.Carriage.PrisonerSeated) continue;
                for (var r = 0; r < convoy.Riders.Count; r++)
                    if (convoy.Riders[r].CharacterId == characterId)
                        return true;
            }
            return false;
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

        /// <summary>The men whose leg goes on paper today (AI-006): the convoy failed
        /// to run for them TransferFailsBeforePaper days running.</summary>
        readonly List<Prisoner> _onPaper = new List<Prisoner>();

        /// <summary>
        /// THE COURT SITS WITHOUT A CAR (AI-006, ruling A16). A man the road has
        /// failed twice is tried, or delivered, on paper this morning - the player's
        /// men and the twenty houses' alike - and the wire prints the verdict as it
        /// would off a convoy. The one thing lost is the road, and the chance at it.
        /// </summary>
        void CarryOnPaper(int today)
        {
            for (var i = 0; i < _onPaper.Count; i++)
            {
                var prisoner = _onPaper[i];
                var roster = RosterOf(prisoner);
                if (roster == null)
                {
                    Pipeline.BackToTheCells(prisoner, today);
                    continue;
                }
                var wasCourt = prisoner.Leg == PrisonLeg.Court;
                Pipeline.OnPaper(roster, prisoner, today);
                // ONLY A MAN WHO IS OUT OF THE CITY'S HANDS LOSES HIS PIN. A paper
                // conviction leaves him SENTENCED and waiting on the van, and the
                // player must still be able to follow that drive - releasing the
                // tracking there unlocked a jailed body and took its position off the
                // map (Codex adversarial review, 2026-09-04).
                if (prisoner.Stage == PrisonStage.Cleared ||
                    prisoner.Stage == PrisonStage.Serving ||
                    Pipeline.Find(prisoner.CharacterId) == null)
                    ReleaseCustodyTracking(prisoner.CharacterId);
                if (!wasCourt)
                    continue;
                var file = prisoner.CaseId >= 0 ? Pipeline.FindCase(prisoner.CaseId) : null;
                AnnounceVerdict(roster, prisoner,
                    file != null ? file.Status : CaseStatus.Tried);
                if (roster.GangId == LivingCity.Gangs.GangCatalog.PlayerGangId)
                    CrewOverlay.Announce("NO CAR COULD BE SPARED · HE WAS TRIED WITHOUT ONE",
                        5f, new Color(0.55f, 0.78f, 1f));
            }
            _onPaper.Clear();
        }
        readonly List<Convoy> _convoys = new List<Convoy>();
        readonly List<PolicePatrolCar> _recoveryCars = new List<PolicePatrolCar>();
        readonly List<Vector3> _transferRouteWarm = new List<Vector3>();
        readonly HashSet<int> _custodyKeepAlive = new HashSet<int>();
        readonly List<LostBeat> _lostBeats = new List<LostBeat>();

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
        public System.Func<Precinct, PoliceBeat> MakeBeat;
        public System.Action<PoliceBeat> RetireBeat;

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

        /// <summary>Six prisoners in each eight-person pickup; keep a reserve only when
        /// more than one car is actually free.</summary>
        public static int CarsForPrisoners(int prisoners, int carsOnDuty)
            => CustodyPlan.CarsForPrisoners(prisoners, carsOnDuty);

        /// <summary>The nearest free custody cars in the whole force, measured by the
        /// overhead-map chord to the prisoners. Registration/list order and road-route
        /// length must never send a farther car past an available one beside the scene.</summary>
        public void CollectCustodyCars(Vector3 near, int prisoners,
            List<PolicePatrolCar> into)
        {
            into?.Clear();
            if (into == null) return;
            for (var p = 0; p < _precincts.Count; p++)
                for (var i = 0; i < _precincts[p].Cars.Count; i++)
                {
                    var car = _precincts[p].Cars[i];
                    if (car != null && car.Available && car.Tf != null)
                        into.Add(car);
                }

            into.Sort((left, right) =>
            {
                var a = left.Tf.position;
                var b = right.Tf.position;
                var da = PoliceProcedure.AirDistanceSquared(
                    a.x, a.z, near.x, near.z);
                var db = PoliceProcedure.AirDistanceSquared(
                    b.x, b.z, near.x, near.z);
                return da.CompareTo(db);
            });

            var count = CarsForPrisoners(prisoners, into.Count);
            if (into.Count > count)
                into.RemoveRange(count, into.Count - count);
        }

        /// <summary>An officer went down. Told through the dispatcher, which already
        /// hears every death on StreetAlarm - no second channel for the same fact.</summary>
        public void OfficerDown(Vector3 where)
        {
            var precinct = WhoLost(where);
            if (precinct == null) return;
            var beat = _dispatch != null ? _dispatch.BeatNear(where, OfficerReach) : null;
            var loss = precinct.Roster.Lose(PoliceLoss.Officer, Today(), Config);
            if (loss == null) return;

            // A pair is one permanent street unit. Only a death which actually wiped
            // that unit creates a replacement body; car-squad casualties restore the
            // roster number but must not conjure extra beat pairs onto the pavement.
            if (beat != null && beat.Unit != null && beat.Unit.Wiped)
            {
                var known = false;
                for (var i = 0; i < _lostBeats.Count; i++)
                    if (_lostBeats[i].Beat == beat) { known = true; break; }
                if (!known)
                {
                    _lostBeats.Add(new LostBeat
                    {
                        Precinct = precinct,
                        Beat = beat,
                        BackOnDay = loss.BackOnDay,
                    });
                    precinct.Leads.Remove(beat);
                    RetireBeat?.Invoke(beat);
                }
            }
            CrewOverlay.Announce(
                precinct.Roster.Empty
                    ? "NO LAW LEFT AT " + Plain(precinct.Roster.Name)
                    : "AN OFFICER DOWN — " + Plain(precinct.Roster.Name) + " IS A MAN SHORT",
                5f, new Color(0.55f, 0.78f, 1f));
        }

        /// <summary>Metres within which a unit of the law standing near a body IS the man
        /// who fell. A pair walks together and a car's crew gets out beside it, so the
        /// answer is a few strides rather than a street.</summary>
        const float OfficerReach = 30f;

        /// <summary>The precinct the force itself knows it is about to lose a man from -
        /// its own escort, killed by a wrecked transfer. Set for the length of the two
        /// deaths it raises and cleared again, so the channel stays a place and a kind
        /// for everybody else.</summary>
        Precinct _losing;

        /// <summary>
        /// Whose roster this death comes off. The force's own escort first (it knows), then
        /// the unit standing where the body is (it knows its precinct), and only then the
        /// nearest station house - which is a guess, and the one the whole city used to
        /// make (GAN-236).
        /// </summary>
        Precinct WhoLost(Vector3 where)
        {
            if (_losing != null) return _losing;
            // A halted carrier is deliberately removed from dispatch's working-fleet
            // book before the roadside fight ends. Its temporary escort still belongs
            // to the exact sending precinct recorded by the convoy, so resolve those
            // physical bodies before falling back to nearby permanent units.
            var escortOwner = CarriageEscortOwner(where, 4f);
            if (escortOwner != null) return escortOwner;
            var id = _dispatch != null ? _dispatch.PrecinctNear(where, OfficerReach) : -1;
            if (id >= 0)
                for (var i = 0; i < _precincts.Count; i++)
                    if (_precincts[i].Roster != null && _precincts[i].Roster.StationId == id)
                        return _precincts[i];
            return Nearest(where);
        }

        Precinct CarriageEscortOwner(Vector3 where, float reach)
        {
            var best = reach * reach;
            Precinct owner = null;
            for (var i = 0; i < _convoys.Count; i++)
            {
                var convoy = _convoys[i];
                var escort = convoy?.Carriage?.Escort;
                if (escort == null || convoy.From == null) continue;
                foreach (var officer in escort.All())
                {
                    if (officer?.Tf == null || !officer.Dead) continue;
                    var delta = officer.Tf.position - where;
                    delta.y = 0f;
                    var distance = delta.sqrMagnitude;
                    if (distance > best) continue;
                    best = distance;
                    owner = convoy.From;
                }
            }
            return owner;
        }

        void Update()
        {
            TracePrecincts();
            TickWrecks();
            TickWatch();
            TickDay();
            TickConvoys();
        }

        bool _traced;

        /// <summary>One row per house, once the trace is open (the harness opens it a
        /// few seconds in, so this cannot be written at founding): where the house is
        /// and how many cars it owns against how many stand in its yard. The reader
        /// puts a police-on-police belt refusal on the yard's tab when it happened
        /// within the yard's reach of one of these, and on the road's otherwise.
        /// </summary>
        void TracePrecincts()
        {
            if (_traced || !DriveTrace.On) return;
            _traced = true;
            for (var i = 0; i < _precincts.Count; i++)
            {
                var precinct = _precincts[i];
                if (precinct.Roster == null) continue;
                var sb = DriveTrace.Take();
                DriveTrace.Int(sb, "id", precinct.Roster.StationId);
                DriveTrace.Str(sb, "name", precinct.Roster.Name);
                DriveTrace.Int(sb, "cars", precinct.Roster.Cars);
                DriveTrace.Int(sb, "bodies", precinct.Cars.Count);
                // WHICH cars, by the id the belt names: a car that rests in its bay for
                // the whole trace never writes a car row, and the reader would count a
                // civilian driving into it as a civilian matter (Codex review, DEPOT-004)
                var units = new System.Text.StringBuilder();
                for (var c = 0; c < precinct.Cars.Count; c++)
                {
                    if (precinct.Cars[c] == null) continue;
                    if (units.Length > 0) units.Append(',');
                    units.Append(precinct.Cars[c].Id);
                }
                DriveTrace.Str(sb, "units", units.ToString());
                DriveTrace.Vec(sb, "p", precinct.At);
                DriveTrace.Row("precinct", sb.ToString());
            }
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

                // ANY HOUSE CAN RUN THE VAN. With several precincts the city is not one
                // station wide (GAN-236), so the transfer goes out of the first house
                // with a car to spare rather than always out of the first on the list.
                Precinct precinct = null;
                PolicePatrolCar car = null;
                for (var p = 0; p < _precincts.Count && car == null; p++)
                {
                    car = FreeCar(_precincts[p]);
                    if (car != null) precinct = _precincts[p];
                }
                if (car == null)
                {
                    // no car on any roster today: he waits, and the pipeline gives him
                    // tomorrow rather than losing him
                    Pipeline.BackToTheCells(prisoner, Today());
                    continue;
                }

                var convoy = Riding(precinct, car, prisoner);
                convoy.Riders.Add(prisoner);
                prisoner.Carriage = CarriageStage.Calling;
                PinCustody(prisoner.CharacterId,
                    convoy.Loaded ? convoy.Car.Position : convoy.Pickup);
            }
            _forTransfer.Clear();
        }

        /// <summary>Schedule every prisoner whose absolute day has come. Normally the
        /// day edge calls this once; save/load recovery and deterministic Play missions
        /// may create a due row after that edge and enter through the same scheduler.</summary>
        public void ScheduleDueTransfers(int today)
        {
            if (today <= 0) return;
            Pipeline.DayTick(today, _forTransfer);
            RunTransfers();
        }

        /// <summary>Rebuild the physical half of custody after CampaignSave restored
        /// the paper. Any pre-load carriage is retired first; every restored prisoner
        /// then owns his exact existing body behind the appropriate source door, ready
        /// for the next scheduler call rather than stranded in an unserialized car.</summary>
        public void RestoreCustodyFromSave()
        {
            for (var i = _convoys.Count - 1; i >= 0; i--)
            {
                var convoy = _convoys[i];
                if (convoy == null) continue;
                var source = SourceDoor(convoy);
                convoy.Carriage?.Restore(source);
                CleanupConvoy(convoy, releaseCar: convoy.Car != null &&
                    convoy.Car.Fleetworthy);
            }
            _convoys.Clear();
            _forTransfer.Clear();
            _custodyKeepAlive.Clear();

            var crews = DemoCrews.Active;
            for (var i = 0; i < Pipeline.Inside.Count; i++)
            {
                var prisoner = Pipeline.Inside[i];
                prisoner.Carriage = null;
                if (!CustodyPlan.TracksStage(prisoner.Stage)) continue;
                KeepCustodyAlive(prisoner.CharacterId);

                var source = prisoner.Stage == PrisonStage.Sentenced && HasCourthouse
                    ? CourthouseDoor
                    : Station != null ? Station.Door : Vector3.zero;
                var body = crews?.RestoreCustodyBody(prisoner.GangId, prisoner.CharacterId, source);
                if (body == null || body.Tf == null) continue;
                var unit = crews.UnitOf(body);
                if (unit != null)
                {
                    unit.InCustody = true;
                    unit.CustodyTracked = true;
                    unit.Surrendered = true;
                }
                body.Disarm();
                body.Surrendered = true;
                DoorBeat.RestoreInside(body, source);
            }
        }

        Convoy Riding(Precinct precinct, PolicePatrolCar car, Prisoner prisoner)
        {
            var leg = prisoner.Leg;
            for (var i = 0; i < _convoys.Count; i++)
                if (_convoys[i].Car == car && _convoys[i].Leg == leg && !_convoys[i].Loaded)
                    return _convoys[i];

            // WHERE IT IS ACTUALLY DRIVING. The first leg goes to the courthouse when the
            // city stands one and out of town when it does not - nothing here invents a
            // building. The second always leaves town: the state prison is not on this
            // map, and the user's call (GAN-237, 2026-09-02) is that it stays off it.
            var to = leg == PrisonLeg.Court && HasCourthouse
                ? CourthouseKerb
                : precinct.CountyLine;

            // AND WHERE THE MAN ACTUALLY IS. A man waiting on a judge is in the cells of
            // the house that runs the van; a man already sentenced is at the court. The
            // car calls there first and only then carries anybody - which is also what
            // makes the drive worth ambushing, because before it he is not in the car.
            var pickup = leg == PrisonLeg.Prison && HasCourthouse
                ? CourthouseKerb
                : precinct.Door;
            var source = leg == PrisonLeg.Prison && HasCourthouse
                ? CourthouseDoor : precinct.Door;
            var body = DemoCrews.Active?.BodyOf(prisoner.CharacterId);
            if (body?.Tf != null)
            {
                source = PrisonerDoor(body, source);
                pickup = HasCourthouse && FlatDistance(source, CourthouseDoor) < 2f
                    ? CourthouseKerb : source;
            }

            var convoy = new Convoy
            {
                Car = car,
                From = precinct,
                Leg = leg,
                To = to,
                Pickup = pickup,
                OriginDoor = source,
                By = Time.time + TransferPatience,
                WasCalled = car.Tf != null ? car.Tf.name : "",
            };
            _convoys.Add(convoy);
            car.RouteTo(pickup, 0f);
            if (car.Tf != null) car.Tf.name = "Prisoner Transfer - going for him";
            return convoy;
        }

        /// <summary>
        /// HE IS IN THE BACK NOW. The car reached the place he was being held, the men
        /// whose transfer this is go on the road, and only from here is there anything in
        /// the car for somebody to take. The patience clock starts again: the drive is a
        /// journey of its own and must not inherit what the collection spent.
        /// </summary>
        bool BeginLoad(Convoy convoy)
        {
            if (convoy == null || convoy.Riders.Count == 0 || convoy.Car == null)
                return false;
            if (convoy.Carriage != null)
                return true;
            var rider = convoy.Riders[0];
            var crews = DemoCrews.Active;
            var body = crews?.BodyOf(rider.CharacterId);
            if (body == null || body.Tf == null)
            {
                // Sync may still be finishing the booking projection. Keep the car at
                // the kerb and try again; never replace the person with a coordinate.
                return false;
            }

            var toward = body.Tf.position - convoy.Car.Position;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.01f) toward = convoy.Car.Forward;
            var escort = _dispatch?.SpawnCarriageEscort(
                convoy.Car.Position + convoy.Car.Tf.right * 2.4f,
                toward.normalized);
            if (escort == null || escort.Standing() < 2)
            {
                _dispatch?.RetireCarriageEscort(escort);
                return false;
            }
            convoy.Carriage = new PrisonerCarriage(rider.CharacterId, body, escort,
                convoy.Car, crews, _dispatch?.CarriageSitLoop);
            convoy.Carriage.BeginWalkingOut(SourceDoor(convoy));
            rider.Carriage = CarriageStage.WalkingOut;
            convoy.By = Time.time + TransferPatience;
            CrewOverlay.Announce("THE PRISONER IS WALKING TO THE CAR", 5f,
                new Color(0.55f, 0.78f, 1f));
            return true;
        }

        /// <summary>The custody paper leaves the cells on the same edge as the body: the
        /// instant the prisoner reaches his seat. Officers may still be walking round to
        /// the front doors, so departure is a separate edge below.</summary>
        void MarkPrisonerSeated(Convoy convoy)
        {
            if (convoy == null || convoy.Loaded) return;
            convoy.Loaded = true;
            for (var r = 0; r < convoy.Riders.Count; r++)
            {
                if (convoy.Riders[r].Stage == PrisonStage.ForTransfer)
                    Pipeline.Away(convoy.Riders[r]);
                convoy.Riders[r].Carriage = convoy.Carriage != null
                    ? convoy.Carriage.Stage : CarriageStage.Boarding;
                PinCustody(convoy.Riders[r].CharacterId, convoy.Car.Position);
            }
            TouchPersonnel();
        }

        void Depart(Convoy convoy)
        {
            MarkPrisonerSeated(convoy);
            SetCarriageStage(convoy, CarriageStage.Riding);
            BeginDrivingDeadline(convoy, Time.time);
            convoy.Car.RouteTo(convoy.To, 0f);
            // Prime the sampled map route on the RouteTo edge. If gunfire halts the car
            // before the player next opens TurfMap, RoadCar can still retain this last
            // valid plan instead of discovering only a dropped goal at draw time.
            convoy.Car.CopyPlannedRoute(_transferRouteWarm);
            if (convoy.Car.Tf != null)
                convoy.Car.Tf.name = convoy.Leg == PrisonLeg.Prison
                    ? "Prisoner Transfer - to the prison"
                    : "Prisoner Transfer - to the court";
            CrewOverlay.Announce(
                convoy.Leg == PrisonLeg.Prison
                    ? "A VAN IS TAKING HIM OUT OF TOWN"
                    : HasCourthouse
                        ? "A PRISONER TRANSFER IS ON THE ROAD TO " +
                          CourthouseName.ToUpperInvariant()
                        : "A PRISONER TRANSFER IS ON THE ROAD",
                5f, new Color(0.55f, 0.78f, 1f));
        }

        /// <summary>Allowance for pickup/boarding, or for a drive without net progress,
        /// before retrying tomorrow. Deliberate roadblocks and escorted walks own
        /// explicit states and never fall through this routine backstop.</summary>
        const float TransferPatience = 300f;
        const float TransferDriveCeiling = 1800f;
        const float RoadblockReadAhead = 65f;
        const float EscortQuiet = 20f;

        static void BeginDrivingDeadline(Convoy convoy, float now)
        {
            convoy.DrivingAnchor = convoy.Car.Position;
            convoy.By = now + TransferPatience;
            convoy.DrivingHardBy = now + TransferDriveCeiling;
        }

        static bool DrivingWithinDeadline(Convoy convoy, float now)
        {
            if (now > convoy.By || now > convoy.DrivingHardBy) return false;
            // A moving carrier must not return its seated prisoner to the cells just
            // because traffic made the trip longer than five minutes. Require net
            // movement, and retain an absolute ceiling for a route that never ends.
            if ((convoy.Car.Position - convoy.DrivingAnchor).sqrMagnitude >= 4f)
            {
                convoy.DrivingAnchor = convoy.Car.Position;
                convoy.By = now + TransferPatience;
            }
            return true;
        }

        void TickConvoys()
        {
            for (var i = _convoys.Count - 1; i >= 0; i--)
            {
                var convoy = _convoys[i];
                if (convoy == null)
                {
                    _convoys.RemoveAt(i);
                    continue;
                }

                if (convoy.Closed)
                {
                    if (!ClosedFightIsOver(convoy)) continue;
                    CleanupConvoy(convoy, releaseCar: !convoy.LeaveCarStood);
                    _convoys.RemoveAt(i);
                    continue;
                }

                // Once the judge has spoken, the walk back through the door is still
                // physical but it is no longer a prisoner transfer. A death on that
                // pavement must not rewrite an acquittal as a transfer killing.
                if (convoy.AwaitingCourtExit)
                {
                    if (!TickCourtExit(convoy)) continue;
                    CleanupConvoy(convoy, releaseCar: !convoy.LeaveCarStood);
                    _convoys.RemoveAt(i);
                    continue;
                }

                var car = convoy.Car;
                // A CAR THAT VANISHED IS NOT AN AMBUSH. Only a WRECK frees the men -
                // somebody has to have done it. A body destroyed for any other reason (a
                // scene torn down, a rebuild) would otherwise open the doors of every
                // transfer in the city and report two officers killed that nobody killed.
                if (car == null || car.Tf == null)
                {
                    if (convoy.Carriage?.Prisoner != null &&
                        convoy.Carriage.Prisoner.Dead)
                        RecordKilled(convoy);
                    else
                        ReturnToSource(convoy);
                    convoy.LeaveCarStood = true;
                    CleanupConvoy(convoy, releaseCar: false);
                    _convoys.RemoveAt(i);
                    continue;
                }

                if (car.Wrecked)
                {
                    EndWrecked(convoy);
                    convoy.LeaveCarStood = true;
                    CleanupConvoy(convoy, releaseCar: false);
                    _convoys.RemoveAt(i);
                    continue;
                }

                var carriage = convoy.Carriage;
                var provokedBy = carriage?.ReadProvocation();
                if (provokedBy != null) convoy.Attacker = provokedBy;
                if (carriage?.Prisoner != null && carriage.Prisoner.Dead)
                {
                    if (RecordKilled(convoy)) continue;
                    CleanupConvoy(convoy, releaseCar: !convoy.LeaveCarStood);
                    _convoys.RemoveAt(i);
                    continue;
                }

                var stage = carriage != null ? carriage.Stage : CarriageStage.Calling;
                switch (stage)
                {
                    case CarriageStage.Calling:
                        if (((IPoliceUnit)car).OnScene)
                            BeginLoad(convoy);
                        if (Time.time <= convoy.By) break;
                        ReturnToSource(convoy);
                        CleanupConvoy(convoy, releaseCar: true);
                        _convoys.RemoveAt(i);
                        break;

                    case CarriageStage.WalkingOut:
                    case CarriageStage.Boarding:
                        if (carriage.EscortWiped)
                        {
                            var released = false;
                            if (carriage.PrisonerSeated)
                            {
                                MarkPrisonerSeated(convoy);
                                released = RecordFreed(convoy);
                            }
                            else
                                released = RecordPickupSpring(convoy);
                            if (!released) ReturnToSource(convoy);
                            convoy.Car.HaltTransfer();
                            convoy.LeaveCarStood = true;
                            CleanupConvoy(convoy, releaseCar: false);
                            _convoys.RemoveAt(i);
                            break;
                        }
                        var readyToDepart = carriage.TickBoarding();
                        if (carriage.PrisonerSeated && !convoy.Loaded)
                            MarkPrisonerSeated(convoy);
                        if (readyToDepart)
                        {
                            Depart(convoy);
                            break;
                        }
                        SetCarriageStage(convoy, carriage.Stage);
                        if (Time.time <= convoy.By) break;
                        ReturnToSource(convoy);
                        CleanupConvoy(convoy, releaseCar: true);
                        _convoys.RemoveAt(i);
                        break;

                    case CarriageStage.Riding:
                        if (ReadRoadblock(convoy)) break;
                        if (((IPoliceUnit)car).OnScene)
                        {
                            if (ArriveAtDestination(convoy))
                            {
                                CleanupConvoy(convoy, releaseCar: true);
                                _convoys.RemoveAt(i);
                            }
                            break;
                        }
                        if (DrivingWithinDeadline(convoy, Time.time)) break;
                        ReturnToSource(convoy);
                        CleanupConvoy(convoy, releaseCar: true);
                        _convoys.RemoveAt(i);
                        break;

                    case CarriageStage.Halted:
                        if (!TickHalted(convoy)) break;
                        CleanupConvoy(convoy, releaseCar: !convoy.LeaveCarStood);
                        _convoys.RemoveAt(i);
                        break;

                    case CarriageStage.WalkingIn:
                        if (!TickWalking(convoy)) break;
                        CleanupConvoy(convoy, releaseCar: !convoy.LeaveCarStood);
                        _convoys.RemoveAt(i);
                        break;

                    case CarriageStage.Delivered:
                        CleanupConvoy(convoy, releaseCar: !convoy.LeaveCarStood);
                        _convoys.RemoveAt(i);
                        break;
                }
            }
        }

        bool ReadRoadblock(Convoy convoy)
        {
            var car = convoy?.Car;
            if (car == null) return false;
            if (CrewCar.RoadblockAhead(car, RoadblockReadAhead, out var blockade))
            {
                if (!convoy.Blocked)
                {
                    convoy.Blockade = blockade;
                    convoy.BlockedRoad = car.Road;
                    convoy.BlockedHeading = car.Heading;
                }
                convoy.Blocked = true;
                convoy.By = float.PositiveInfinity;
                // Fast cars first brake behind the obstruction; as soon as the sweep is
                // available EscapeBarricade turns or reverses without the normal
                // near-junction jam gate. If neither is possible, stand and wait for
                // MOVE ON rather than silently cancelling tomorrow's trial.
                car.EscapeBarricade();
                return true;
            }
            if (!convoy.Blocked) return false;

            // A reverse can carry the obstacle beyond the read-ahead without actually
            // escaping it. As long as the same body still spans the same road ahead of
            // the original heading, keep trying the turn/reverse ladder and keep the
            // ordinary transfer timeout disarmed.
            if (convoy.Blockade != null && convoy.Blockade.IsRoadblock &&
                car.Road == convoy.BlockedRoad && car.Heading == convoy.BlockedHeading)
            {
                convoy.By = float.PositiveInfinity;
                car.EscapeBarricade();
                return true;
            }

            convoy.Blocked = false;
            BeginDrivingDeadline(convoy, Time.time);
            convoy.Blockade = null;
            convoy.BlockedRoad = null;
            convoy.BlockedHeading = 0;
            // A successful turn kept its original goal and its forced long-way re-plan.
            // Reissuing RouteTo here would erase that decision and point it back at the
            // barricade. A car which merely stood waiting does need waking once MOVE ON
            // clears the street.
            if (!car.HasGoal || car.Halted)
                car.RouteTo(convoy.To, 0f);
            return true;
        }

        /// <summary>A round into a police body is an attack on the law even though the
        /// shooter's strategic mark is a car rather than an officer unit. For a loaded
        /// transfer the first such round also turns the carriage into a foot fight.</summary>
        public void RoundIntoPoliceTin(RoadCar car, CrewWalker shooter)
        {
            if (car == null || shooter == null) return;
            Convoy active = null;
            for (var i = 0; i < _convoys.Count; i++)
            {
                var candidate = _convoys[i];
                if (candidate == null || candidate.Car != car ||
                    candidate.Carriage == null) continue;
                active = candidate;
                break;
            }
            var precinct = PrecinctOf(car);
            var civic = car is CrewCar crewCar && crewCar.Civic;
            // A halted carrier leaves the working-fleet book as soon as it becomes a
            // derelict, but remains police tin until its live carriage is resolved.
            if (precinct == null && !civic && active == null) return;
            var raised = _dispatch?.ShotAtPoliceCar(car, shooter) ?? false;

            if (active == null) return;
            var crews = DemoCrews.Active;
            if (raised) active.SwarmRaises++;
            active.Attacker = crews?.UnitOf(shooter) ?? active.Attacker;
            if (CustodyPlan.ShouldHalt(active.Carriage.Stage,
                    active.Carriage.PrisonerSeated, firstRoundIntoTin: true))
            {
                active.Carriage.BeginHalt();
                SetCarriageStage(active, CarriageStage.Halted);
                active.HaltedAt = Time.time;
                active.HardBy = Time.time + CustodyPlan.StrandedBackstopSeconds;
                active.By = active.HardBy;
                active.Car.HaltTransfer();

                // SHOOT IT UP ends here. The next exchange is men against men;
                // leaving one marksman on an endless tin order would keep drilling
                // the shell after its escort got out.
                if (crews != null)
                    foreach (var unit in crews.Units)
                        if (unit != null)
                            foreach (var man in unit.All())
                                if (man != null && man.CarMark == car)
                                    man.Disengage();

                LawWire.TransferHalted(RosterOf(FirstRider(active))?.Find(
                    FirstRider(active)?.CharacterId ?? -1));
                CrewOverlay.Announce("THE TRANSFER IS UNDER FIRE", 6f,
                    new Color(1f, 0.55f, 0.45f));
            }

            // While the bodies are still seated, later rounds may make another bounded
            // roll. The carriage itself enforces one roll per second and the fixed
            // per-engagement ceiling; dismount ends the window.
            if (active.Carriage.Jeopardy(Time.time, Random.value,
                    CustodyPlan.OccupantHitChance))
                crews?.KilledInTransfer(active.Carriage.Prisoner, shooter);
        }

        /// <summary>For the duration of an explosion, real escort deaths are charged to
        /// the precinct which sent this exact carrier. A thrown bomb has no explicit
        /// car, so its blast point is matched to the loaded carrier it actually reaches.
        /// EndExplosion always clears the hint; every death still travels through
        /// StreetAlarm exactly once.</summary>
        public void BeginExplosion(RoadCar car, Vector3 blastAt)
        {
            _losing = null;
            Convoy nearest = null;
            var nearestDistance = Explosion.Radius * Explosion.Radius;
            for (var i = 0; i < _convoys.Count; i++)
            {
                var convoy = _convoys[i];
                if (convoy == null || convoy.Car == null) continue;
                if (convoy.Car == car)
                {
                    _losing = convoy.From;
                    return;
                }
                if (car != null || convoy.Car.Tf == null) continue;
                var distance = convoy.Car.Position - blastAt;
                distance.y = 0f;
                if (distance.sqrMagnitude > nearestDistance) continue;
                nearestDistance = distance.sqrMagnitude;
                nearest = convoy;
            }
            if (nearest != null)
            {
                _losing = nearest.From;
                return;
            }
            _losing = PrecinctOf(car);
        }

        public void EndExplosion(RoadCar car) => _losing = null;

        Precinct PrecinctOf(RoadCar car)
        {
            if (car == null) return null;
            for (var p = 0; p < _precincts.Count; p++)
                for (var i = 0; i < _precincts[p].Cars.Count; i++)
                    if (_precincts[p].Cars[i] == car)
                        return _precincts[p];
            return null;
        }

        bool TickHalted(Convoy convoy)
        {
            var carriage = convoy.Carriage;
            if (carriage == null)
            {
                convoy.LeaveCarStood = true;
                return AbortStalledTransfer(convoy,
                    "THE DAMAGED TRANSFER IS CALLED OFF");
            }
            if (carriage.EscortWiped)
            {
                RecordFreed(convoy);
                convoy.LeaveCarStood = true;
                return true;
            }
            // Relief attempts have their own shorter By clock below, but none may keep
            // extending this hostile scene forever. A roadblock-only delay never enters
            // Halted and retains its separate no-return-to-cells contract.
            if (CustodyPlan.BackstopExpired(Time.time, convoy.HardBy))
            {
                convoy.LeaveCarStood = true;
                return AbortStalledTransfer(convoy,
                    "THE ESCORT TAKES HIM BACK UNDER GUARD");
            }

            if (!convoy.Dismounted)
            {
                if (!carriage.DismountHalted(convoy.Car.Position)) return false;
                convoy.Dismounted = true;
                var crews = DemoCrews.Active;
                if (crews != null && convoy.Attacker != null && !convoy.Attacker.Wiped)
                {
                    crews.Sic(carriage.Escort, convoy.Attacker);
                    crews.Sic(convoy.Attacker, carriage.Escort);
                }
                CrewOverlay.Announce("THE ESCORT IS OUT OF THE CAR", 5f,
                    new Color(0.55f, 0.78f, 1f));
                return false;
            }

            if (carriage.EscortWiped)
            {
                RecordFreed(convoy);
                convoy.LeaveCarStood = true;
                return true;
            }
            if (StreetAlarm.QuietFor < EscortQuiet) return false;

            if (convoy.Recovery != null)
            {
                var fresh = convoy.Recovery;
                if (fresh == null || fresh.Tf == null || !fresh.Fleetworthy)
                {
                    convoy.Recovery = null;
                    convoy.RecoveryWasCalled = "";
                    convoy.RecoveryRetryAt = Time.time + 1.25f;
                    return false;
                }
                if (((IPoliceUnit)fresh).OnScene)
                {
                    convoy.Car = fresh;
                    convoy.WasCalled = convoy.RecoveryWasCalled;
                    convoy.Recovery = null;
                    convoy.RecoveryWasCalled = "";
                    convoy.Dismounted = false;
                    carriage.ChangeCar(fresh);
                    SetCarriageStage(convoy, CarriageStage.Boarding);
                    convoy.By = Time.time + TransferPatience;
                    convoy.HardBy = 0f;
                    return false;
                }
                // A recovery which itself could not reach the scene is released, but
                // the prisoner is never teleported back to the cells from this state.
                if (Time.time > convoy.By)
                {
                    RestoreCarName(fresh, convoy.RecoveryWasCalled);
                    fresh.Release();
                    convoy.Recovery = null;
                    convoy.RecoveryWasCalled = "";
                    convoy.RecoveryRetryAt = Time.time + 1.25f;
                }
                return false;
            }

            if (Time.time < convoy.RecoveryRetryAt) return false;
            convoy.RecoveryRetryAt = Time.time + 1.25f;
            _recoveryCars.Clear();
            CollectCustodyCars(convoy.Car.Position, 1, _recoveryCars);
            if (_recoveryCars.Count > 0)
            {
                var fresh = _recoveryCars[0];
                convoy.Recovery = fresh;
                convoy.RecoveryWasCalled = fresh.Tf != null ? fresh.Tf.name : "";
                convoy.By = Time.time + TransferPatience;
                fresh.RouteTo(convoy.Car.Position, 0f);
                if (fresh.Tf != null) fresh.Tf.name = "Prisoner Transfer - relief car";
                return false;
            }

            var remaining = FlatDistance(carriage.Prisoner.Tf.position, convoy.To);
            if (!CustodyPlan.WalkTheRest(freshCarrierAvailable: false,
                    metresRemaining: remaining))
                return false;
            carriage.BeginFootMarch(convoy.To);
            SetCarriageStage(convoy, CarriageStage.WalkingIn);
            convoy.LeaveCarStood = true;
            convoy.HardBy = Time.time + CustodyPlan.WalkingBackstopSeconds;
            convoy.By = convoy.HardBy;
            CrewOverlay.Announce("THE ESCORT IS WALKING HIM THE REST OF THE WAY", 6f,
                new Color(0.55f, 0.78f, 1f));
            return false;
        }

        bool TickWalking(Convoy convoy)
        {
            var carriage = convoy.Carriage;
            if (carriage == null)
                return AbortStalledTransfer(convoy,
                    "THE STALLED TRANSFER IS CALLED OFF");
            if (carriage.EscortWiped)
            {
                RecordFreed(convoy);
                convoy.LeaveCarStood = true;
                return true;
            }
            if (CustodyPlan.BackstopExpired(Time.time, convoy.HardBy))
                return AbortStalledTransfer(convoy,
                    "THE STALLED WALK RETURNS UNDER GUARD");

            if (carriage.FootMarching)
            {
                if (!carriage.TickFootMarch()) return false;
                carriage.FinishFootMarch();
                if (convoy.Leg == PrisonLeg.Court && HasCourthouse)
                {
                    carriage.BeginWalkingIn(CourthouseDoor);
                    SetCarriageStage(convoy, CarriageStage.WalkingIn);
                    convoy.HardBy = Time.time + CustodyPlan.WalkingBackstopSeconds;
                    convoy.By = convoy.HardBy;
                    return false;
                }
                return convoy.Leg == PrisonLeg.Prison
                    ? CompletePrison(convoy)
                    : CompleteCountyCourt(convoy);
            }

            if (!carriage.TickThreshold(CourthouseDoor)) return false;
            return CompleteCourtThreshold(convoy);
        }

        bool ArriveAtDestination(Convoy convoy)
        {
            if (convoy.Leg == PrisonLeg.Prison)
                return CompletePrison(convoy);
            if (!HasCourthouse)
                return CompleteCountyCourt(convoy);
            convoy.Carriage.BeginWalkingIn(CourthouseDoor);
            SetCarriageStage(convoy, CarriageStage.WalkingIn);
            convoy.HardBy = Time.time + CustodyPlan.WalkingBackstopSeconds;
            convoy.By = convoy.HardBy;
            return false;
        }

        bool CompletePrison(Convoy convoy)
        {
            var rider = FirstRider(convoy);
            if (rider == null || convoy.Carriage == null) return false;
            if (!CustodyPlan.CanDeliver(convoy.Carriage.Stage,
                    thresholdCrossed: convoy.Carriage.FootMarching == false,
                    countyLineLeg: true))
                return false;
            convoy.Carriage.DeliverOffMap(convoy.To);
            Pipeline.Delivered(rider);
            rider.Carriage = null;
            TouchPersonnel();
            CrewOverlay.Announce("THE STATE HAS HIM NOW", 5f,
                new Color(0.55f, 0.78f, 1f));
            return true;
        }

        bool CompleteCountyCourt(Convoy convoy)
        {
            var rider = FirstRider(convoy);
            var roster = RosterOf(rider);
            if (roster == null || rider == null || convoy.Carriage == null) return false;
            convoy.Carriage.DeliverOffMap(convoy.To);
            var file = rider.CaseId >= 0 ? Pipeline.FindCase(rider.CaseId) : null;
            Pipeline.Tried(roster, rider, Today());
            AnnounceVerdict(roster, rider,
                file != null ? file.Status : CaseStatus.Tried);
            if (rider.Stage == PrisonStage.Sentenced)
            {
                // The county court is off-map. A convicted man's body therefore comes
                // back behind the sending house's real threshold while the paper waits
                // for tomorrow's prison leg; otherwise DeliverOffMap leaves an inactive
                // body at the county line which SendOut cannot walk from next morning.
                DoorBeat.RestoreInside(convoy.Carriage.Prisoner, convoy.From.Door);
            }
            else
                ReleaseCustodyTracking(rider.CharacterId, convoy.To, relocate: true);
            rider.Carriage = null;
            TouchPersonnel();
            return true;
        }

        bool CompleteCourtThreshold(Convoy convoy)
        {
            var rider = FirstRider(convoy);
            var roster = RosterOf(rider);
            if (roster == null || rider == null || convoy.Carriage == null) return false;
            var man = roster.Find(rider.CharacterId);
            var file = rider.CaseId >= 0 ? Pipeline.FindCase(rider.CaseId) : null;
            LawWire.WalkedIn(man);
            CrewOverlay.Announce("HE IS AT THE COURTHOUSE DOOR", 4f,
                new Color(0.55f, 0.78f, 1f));
            Pipeline.Tried(roster, rider, Today());
            AnnounceVerdict(roster, rider,
                file != null ? file.Status : CaseStatus.Tried);
            TouchPersonnel();

            convoy.Carriage.MarkDelivered();
            if (rider.Stage == PrisonStage.Sentenced)
            {
                rider.Carriage = null;
                return true;
            }

            // The verdict is already in the book, but the exact body remains protected
            // by the keep-alive set until DoorBeat completes the reverse crossing.
            convoy.AwaitingCourtExit = true;
            rider.Carriage = CarriageStage.Delivered;
            DoorBeat.SendOut(convoy.Carriage.Prisoner);
            convoy.HardBy = Time.time + CustodyPlan.CourtExitBackstopSeconds;
            convoy.By = convoy.HardBy;
            return false;
        }

        bool TickCourtExit(Convoy convoy)
        {
            var rider = FirstRider(convoy);
            var body = convoy.Carriage?.Prisoner;
            if (rider == null) return true;
            if (body == null || body.Tf == null || body.Dead)
            {
                ReleaseCustodyTracking(rider.CharacterId);
                rider.Carriage = null;
                TouchPersonnel();
                return true;
            }
            if (DoorBeat.Active(body) || !body.Tf.gameObject.activeInHierarchy)
            {
                if (!CustodyPlan.BackstopExpired(Time.time, convoy.HardBy))
                    return false;
                // The verdict is already final. If the reverse doorway choreography
                // cannot finish, cancel that stale call and put the exact released body
                // on the real courthouse pavement rather than retaining it forever.
                ReleaseCustodyTracking(rider.CharacterId, CourthouseDoor, relocate: true);
                rider.Carriage = null;
                TouchPersonnel();
                CrewOverlay.Announce("HE IS RELEASED AT THE COURTHOUSE", 4f,
                    new Color(0.75f, 0.95f, 0.7f));
                return true;
            }
            ReleaseCustodyTracking(rider.CharacterId, body.Tf.position, relocate: false);
            rider.Carriage = null;
            TouchPersonnel();
            return true;
        }

        /// <summary>An exceptional physical step failed its long ceiling. This never
        /// decrees an arrival: the exact body is detached from any stale doorway/car
        /// choreography and the paper remains held for a later run.</summary>
        bool AbortStalledTransfer(Convoy convoy, string banner)
        {
            if (convoy == null) return true;
            DoorBeat.Evict(convoy.Carriage?.Prisoner);
            ReturnToSource(convoy);
            convoy.HardBy = 0f;
            CrewOverlay.Announce(banner, 5f, new Color(0.55f, 0.78f, 1f));
            return true;
        }

        void EndWrecked(Convoy convoy)
        {
            var where = convoy.Car != null && convoy.Car.Tf != null
                ? convoy.Car.Position : convoy.From.At;

            // A scene without officer prefabs still owes the old two bodies. Once real
            // escort walkers exist their own blast deaths have already used the shared
            // StreetAlarm channel, so decreeing two more here would double-count them.
            var fallbackDeaths = CustodyPlan.FallbackOfficerDeaths(
                convoy.Carriage?.EscortBodies ?? 0);
            if (fallbackDeaths > 0)
            {
                _losing = convoy.From;
                for (var i = 0; i < fallbackDeaths; i++)
                    StreetAlarm.Death(where, StreetAlarm.DeathOf.Officer);
                _losing = null;
            }

            if (convoy.Carriage?.Prisoner != null)
                convoy.Carriage.Restore(where);
            if (convoy.Carriage?.Prisoner != null && convoy.Carriage.Prisoner.Dead)
            {
                RecordKilled(convoy);
                return;
            }
            if (convoy.Loaded && RecordFreed(convoy)) return;
            ReturnToSource(convoy);
        }

        bool RecordFreed(Convoy convoy)
        {
            var rider = FirstRider(convoy);
            if (rider == null) return false;
            var at = convoy.Carriage?.Prisoner?.Tf != null
                ? convoy.Carriage.Prisoner.Tf.position
                : convoy.Car != null ? convoy.Car.Position : convoy.Pickup;
            convoy.Carriage?.Restore(at);
            if (Pipeline.Freed(RosterOf(rider), rider, Today()) == null) return false;
            ReleaseCustodyTracking(rider.CharacterId, at, relocate: false);
            rider.Carriage = null;
            TouchPersonnel();
            CrewOverlay.Announce("A MAN IS OUT OF THE BACK OF IT", 6f,
                new Color(0.95f, 0.9f, 0.6f));
            return true;
        }

        /// <summary>The escort was beaten while the man was physically out of the cells
        /// but had never sat in the carrier. This is a spring, not Freed: the latter is
        /// intentionally tried and refused by the pipeline contract for ForTransfer.</summary>
        bool RecordPickupSpring(Convoy convoy)
        {
            var rider = FirstRider(convoy);
            if (rider == null || rider.Stage != PrisonStage.ForTransfer) return false;
            var body = convoy.Carriage?.Prisoner;
            var at = body?.Tf != null
                ? body.Tf.position
                : convoy.Car != null ? convoy.Car.Position : convoy.Pickup;
            convoy.Carriage?.Restore(at);
            if (!Pipeline.Sprung(RosterOf(rider), rider.CharacterId, Today())) return false;
            ReleaseCustodyTracking(rider.CharacterId, at, relocate: false);
            rider.Carriage = null;
            convoy.Carriage?.MarkDelivered();
            LawWire.Sprung(null, DemoCrews.Active?.UnitOf(body));
            TouchPersonnel();
            CrewOverlay.Announce("THE PRISONER IS SPRUNG AT THE STATION DOOR", 6f,
                new Color(1f, 0.72f, 0.35f));
            return true;
        }

        /// <summary>Returns true while a surviving escort must be allowed to finish the
        /// firefight before its temporary unit is retired.</summary>
        bool RecordKilled(Convoy convoy)
        {
            var rider = FirstRider(convoy);
            if (rider == null) return false;
            var wasHalted = convoy.Carriage?.Stage == CarriageStage.Halted;
            // A round can kill the seated body on the frame which starts braking.
            // Keep the body parented to the carrier until it has physically stopped;
            // the regular convoy tick will return here on the next frames.
            if (wasHalted && convoy.Car != null &&
                Mathf.Abs(convoy.Car.Speed) >= 0.05f)
                return true;
            var at = convoy.Carriage?.Prisoner?.Tf != null
                ? convoy.Carriage.Prisoner.Tf.position
                : convoy.Car != null ? convoy.Car.Position : convoy.Pickup;
            convoy.Carriage?.Restore(at);
            if (wasHalted)
            {
                convoy.Dismounted = true;
                var crews = DemoCrews.Active;
                if (crews != null && convoy.Attacker != null && !convoy.Attacker.Wiped)
                {
                    crews.Sic(convoy.Carriage?.Escort, convoy.Attacker);
                    crews.Sic(convoy.Attacker, convoy.Carriage?.Escort);
                }
            }
            var killed = Pipeline.Killed(RosterOf(rider), rider.CharacterId, Today());
            ReleaseCustodyTracking(rider.CharacterId, at, relocate: false);
            rider.Carriage = CarriageStage.Delivered;
            convoy.Carriage?.MarkDelivered();
            if (killed != null)
            {
                LawWire.Killed(RosterOf(rider)?.Find(rider.CharacterId));
                TouchPersonnel();
                CrewOverlay.Announce("THE PRISONER IS DEAD IN THE CAR", 6f,
                    new Color(1f, 0.45f, 0.4f));
            }

            convoy.LeaveCarStood = convoy.Car == null || convoy.Car.Wrecked ||
                                   wasHalted || convoy.Dismounted;
            var fightContinues = convoy.Dismounted && convoy.Carriage?.Escort != null &&
                                 !convoy.Carriage.Escort.Wiped &&
                                 convoy.Attacker != null && !convoy.Attacker.Wiped &&
                                 !convoy.Attacker.Retreated;
            convoy.Closed = fightContinues;
            return fightContinues;
        }

        bool ClosedFightIsOver(Convoy convoy) =>
            convoy.Car == null || convoy.Car.Wrecked ||
            convoy.Carriage?.Escort == null || convoy.Carriage.Escort.Wiped ||
            convoy.Attacker == null || convoy.Attacker.Wiped || convoy.Attacker.Retreated ||
            StreetAlarm.QuietFor >= EscortQuiet;

        void ReturnToSource(Convoy convoy)
        {
            var source = SourceDoor(convoy);
            if (convoy.Carriage?.Prisoner != null &&
                !convoy.Carriage.Prisoner.Dead)
            {
                convoy.Carriage.Restore(convoy.Pickup);
                convoy.Carriage.Prisoner.Surrendered = true;
                DoorBeat.MoveIn(convoy.Carriage.Prisoner, source);
            }
            for (var r = 0; r < convoy.Riders.Count; r++)
            {
                Pipeline.BackToTheCells(convoy.Riders[r], Today());
                convoy.Riders[r].Carriage = null;
                KeepCustodyAlive(convoy.Riders[r].CharacterId);
            }
            TouchPersonnel();
        }

        Vector3 SourceDoor(Convoy convoy) =>
            convoy.OriginDoor ?? (convoy.Leg == PrisonLeg.Prison && HasCourthouse
                ? CourthouseDoor : convoy.From.Door);

        static Vector3 PrisonerDoor(CrewWalker body, Vector3 fallback)
        {
            if (body?.Tf == null) return fallback;
            return DoorBeat.TryGetOutside(body, out var outside) ? outside : body.Tf.position;
        }

        static Prisoner FirstRider(Convoy convoy) =>
            convoy != null && convoy.Riders.Count > 0 ? convoy.Riders[0] : null;

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static void SetCarriageStage(Convoy convoy, CarriageStage stage)
        {
            for (var i = 0; i < convoy.Riders.Count; i++)
                convoy.Riders[i].Carriage = stage;
        }

        static void TouchPersonnel() =>
            LivingCity.Gameplay.PersonnelDirector.Instance?.Touch();

        void CleanupConvoy(Convoy convoy, bool releaseCar)
        {
            if (convoy == null) return;
            if (convoy.Recovery != null && convoy.Recovery != convoy.Car)
            {
                RestoreCarName(convoy.Recovery, convoy.RecoveryWasCalled);
                convoy.Recovery.Release();
            }
            _dispatch?.RetireCarriageEscort(convoy.Carriage?.Escort);
            if (releaseCar) Release(convoy);
        }

        static void RestoreCarName(PolicePatrolCar car, string name)
        {
            if (car?.Tf != null && !string.IsNullOrEmpty(name)) car.Tf.name = name;
        }

        void Release(Convoy convoy)
        {
            if (convoy.Car == null) return;
            RestoreCarName(convoy.Car, convoy.WasCalled);
            convoy.Car.Release();
        }

        PolicePatrolCar FreeCar(Precinct precinct)
        {
            for (var i = 0; i < precinct.Cars.Count; i++)
            {
                var car = precinct.Cars[i];
                if (car == null || !car.Fleetworthy) continue;
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

        /// <summary>The roster named by the prisoner's own row or docket. Character
        /// ids stay opaque; ownership is never decoded from their numeric span.</summary>
        LivingCity.Personnel.Roster RosterOf(Prisoner prisoner)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            if (prisoner != null && underworld != null)
            {
                if (prisoner.GangId >= 0)
                {
                    var named = underworld.Of(prisoner.GangId)?.Roster;
                    if (named != null) return named;
                }

                var file = prisoner.CaseId >= 0
                    ? Pipeline.FindCase(prisoner.CaseId) : null;
                if (file != null)
                {
                    prisoner.GangId = file.GangId;
                    var named = underworld.Of(file.GangId)?.Roster;
                    if (named != null) return named;
                }

                for (var gang = 0; gang < underworld.Count; gang++)
                {
                    var roster = underworld.Of(gang)?.Roster;
                    if (roster?.Find(prisoner.CharacterId) == null) continue;
                    prisoner.GangId = roster.GangId;
                    return roster;
                }
            }
            return Roster();
        }

        /// <summary>A wreck, dead engine or deliberately driven-round transfer car is
        /// off the roster. Its RoadCar body remains in the builder's street list as
        /// scenery; only the precinct and dispatcher books release it here.</summary>
        void TickWrecks()
        {
            var changed = false;
            for (var p = 0; p < _precincts.Count; p++)
            {
                var precinct = _precincts[p];
                for (var i = precinct.Cars.Count - 1; i >= 0; i--)
                {
                    var car = precinct.Cars[i];
                    if (car == null)
                    {
                        precinct.Cars.RemoveAt(i);
                        changed = true;
                        continue;
                    }
                    if (car.Fleetworthy) continue;
                    precinct.Cars.RemoveAt(i);
                    _dispatch?.Unregister(car);
                    precinct.Roster.Lose(PoliceLoss.Car, Today(), Config);
                    changed = true;
                }
            }
            // Re-number the still-working bodies on this same watch; otherwise a car
            // which was off duty behind the lost list entry can remain off duty until
            // the next handover.
            if (changed) _watchKnown = false;
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
            var working = 0;
            for (var i = 0; i < precinct.Cars.Count; i++)
            {
                var car = precinct.Cars[i];
                if (car == null || !car.Fleetworthy) continue;
                if (working++ < cars)
                    car.StandTo(first ? 0f : Random.Range(2f, Config.HandoverSeconds));
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
        readonly List<int> _discharged = new List<int>();

        /// <summary>What the court did to one man, said once - on the wire and over the
        /// street. ONE door for it, because a verdict reached on paper at the day tick
        /// and a verdict reached off the back of a convoy are the same fact and used to
        /// be announced by only one of the two.</summary>
        void AnnounceVerdict(LivingCity.Personnel.Roster roster, Prisoner prisoner,
            CaseStatus status, bool banner = true)
        {
            if (prisoner == null) return;
            if (prisoner.Stage == PrisonStage.Sentenced)
            {
                // Bail released the original custody pin. A conviction at the
                // daily hearing must reclaim the same body before roster Sync
                // removes inactive men, or tomorrow's van has nobody to collect.
                KeepCustodyAlive(prisoner.CharacterId);
                var body = DemoCrews.Active?.BodyOf(prisoner.CharacterId);
                if (body != null && !body.Dead)
                {
                    body.Disengage();
                    body.Disarm();
                    body.Surrendered = true;
                    if (!DoorBeat.Active(body) && !body.Riding && body.Tf != null)
                        body.OrderAcross(body.Tf.position);
                }
            }
            var man = roster != null ? roster.Find(prisoner.CharacterId) : null;
            var file = prisoner.CaseId >= 0 ? Pipeline.FindCase(prisoner.CaseId) : null;
            LawWire.Verdict(man, prisoner.Stage, status, prisoner, file);
            if (!banner ||
                prisoner.GangId != LivingCity.Gangs.GangCatalog.PlayerGangId)
                return;

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
            var underworld = LivingCity.Outfit.Underworld.Current;
            if (underworld != null)
            {
                for (var gang = 0; gang < underworld.Count; gang++)
                    ProcessRosterDay(underworld.Of(gang)?.Roster, today);
            }
            else
            {
                ProcessRosterDay(Roster(), today);
            }
            Pipeline.DayTick(today, _forTransfer, _onPaper);
            RunTransfers();
            CarryOnPaper(today);

            if (!known) return;   // the first day merely learns what day it is

            for (var i = 0; i < _precincts.Count; i++)
            {
                var precinct = _precincts[i];
                var restored = precinct.Roster.Replace(today, _filled);

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

                var beats = 0;
                for (var b = _lostBeats.Count - 1; b >= 0 && MakeBeat != null; b--)
                {
                    var lost = _lostBeats[b];
                    if (lost.Precinct != precinct || lost.BackOnDay <= 0 ||
                        lost.BackOnDay > today) continue;
                    var beat = MakeBeat(precinct);
                    if (beat == null) continue;
                    if (lost.Beat != null) beat.UnitNumber = lost.Beat.UnitNumber;
                    beat.Precinct = precinct.Roster.StationId;
                    precinct.Leads.Add(beat);
                    _lostBeats.RemoveAt(b);
                    beats++;
                }

                if (restored == 0 && beats == 0) continue;

                // and whatever came back reports for the next watch, not this second
                _watchKnown = false;
                CrewOverlay.Announce(Plain(precinct.Roster.Name) + " IS BACK UP TO STRENGTH",
                    5f, new Color(0.55f, 0.78f, 1f));
            }
        }

        /// <summary>Discharge, bail and court are applied to the house that owns each
        /// prisoner. PrisonPipeline filters its shared rows by Roster.GangId.</summary>
        void ProcessRosterDay(LivingCity.Personnel.Roster roster, int today)
        {
            if (roster == null)
                return;
            if (Pipeline.RosterSeed == 0) Pipeline.RosterSeed = roster.Seed;
            Pipeline.ComplainantStillTalks ??= StillTalks;

            _discharged.Clear();
            Pipeline.Discharged(roster, _discharged);
            for (var i = 0; i < _discharged.Count; i++)
                ReleaseCustodyTracking(_discharged[i]);
            CoolTheWanted(roster, today);

            _forfeited.Clear();
            _paperTried.Clear();
            var tried = Pipeline.TryOnPaper(
                roster, today, _forfeited, _paperTried);
            for (var i = 0; i < _forfeited.Count; i++)
            {
                var prisoner = _forfeited[i];
                var file = prisoner.CaseId >= 0
                    ? Pipeline.FindCase(prisoner.CaseId) : null;
                LawWire.BailForfeit(roster.Find(prisoner.CharacterId), prisoner, file);
            }
            for (var i = 0; i < _paperTried.Count; i++)
            {
                var prisoner = _paperTried[i];
                var file = prisoner.CaseId >= 0
                    ? Pipeline.FindCase(prisoner.CaseId) : null;
                AnnounceVerdict(roster, prisoner,
                    file != null ? file.Status : CaseStatus.Tried);
            }

            if (_discharged.Count == 0 && tried == 0)
                return;
            var director = LivingCity.Gameplay.PersonnelDirector.Instance;
            if (director != null && roster.GangId ==
                LivingCity.Gangs.GangCatalog.PlayerGangId)
                director.Touch();
        }

        /// <summary>
        /// THE LAST CAR IN THE HOUSE (2026-09-03, the user's rule). A telephone is
        /// ringing and there is nothing on the street to send - every beat pair dead,
        /// every car wrecked - so the nearest house that is still authorised a car puts
        /// one on its forecourt and it answers the call itself.
        ///
        /// Never more than the roster says the house HAS: a station that has lost its
        /// whole fleet stays lost until the department fills it (the replacement day),
        /// and a city with no precinct at all answers nothing, which is the honest
        /// outcome and the one the wire now prints.
        /// </summary>
        public PolicePatrolCar TurnOutACar(Vector3 near)
        {
            if (MakeCar == null) return null;
            var precinct = Nearest(near);
            if (precinct?.Roster == null) return null;

            var bodies = 0;
            for (var i = 0; i < precinct.Cars.Count; i++)
            {
                var body = precinct.Cars[i];
                if (body == null) continue;
                // Update order is not a fleet transaction. If a loss became visible
                // before TickWrecks booked it, wait one frame rather than spawning
                // against the old roster number and then recording the loss as well.
                if (!body.Fleetworthy) return null;
                bodies++;
            }
            if (bodies >= precinct.Roster.Cars) return null;

            var car = MakeCar(precinct);
            if (car == null) return null;
            car.Precinct = precinct.Roster.StationId;
            precinct.Cars.Add(car);
            _dispatch?.Register(car);
            car.StandTo();
            CrewOverlay.Announce("A CAR IS COMING OUT OF " + Plain(precinct.Roster.Name),
                4.5f, new Color(0.55f, 0.78f, 1f));
            return car;
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
