using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.Police;
using LivingCity.Save;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// ROAD-006's player: it creates one ordinary due case, lets the exact booked body
    /// walk out with the ordinary carriage, then uses the same public orders as the HUD
    /// to plant a charge, shoot the carrier or lay a roadblock. Nothing in here resolves
    /// custody or combat; it only arranges the twelve repeatable scenes and records what
    /// the production systems decide.
    /// </summary>
    public sealed class CourtTransferMission : MonoBehaviour
    {
        public enum Scenario
        {
            None = 0,
            EscortDismount = 1,
            EscortWiped = 2,
            BombAtLoadingKerb = 3,
            BombOnHaltedTransfer = 4,
            BombBeforePickup = 5,
            RoadblockAlone = 6,
            RoadblockAndFire = 7,
            StationDoorAmbush = 8,
            CourthouseDoorAmbush = 9,
            EscortWins = 10,
            NoCourthouse = 11,
            SaveMidTransfer = 12,
        }

        public enum Phase
        {
            Waiting,
            Preparing,
            Positioning,
            Running,
            AwaitingReload,
            Done,
            Failed,
        }

        [Range(1, 12)] public int scenario = 1;
        [Min(0f)] public float startAfter = 10f;
        [Min(30f)] public float patience = 900f;

        public Phase State { get; private set; } = Phase.Waiting;

        PoliceForce _force;
        DemoCrews _crews;
        DemoCrews.Unit _prisonerUnit;
        DemoCrews.Unit _attacker;
        DemoCrews.Unit _blockerCrew;
        DemoCrews.Unit _escort;
        CrewWalker _body;
        Prisoner _prisoner;
        CourtCase _case;
        PolicePatrolCar _transfer;
        PolicePatrolCar _emptyBombCar;
        CrewCar _blocker;
        Carriageway _approachRoad;

        bool _booked;
        bool _inside;
        bool _positioned;
        bool _scheduled;
        bool _acted;
        bool _planted;
        bool _blockOrdered;
        bool _blockEstablished;
        bool _metBlock;
        bool _withdrawing;
        bool _sawWalkOut;
        bool _sawRiding;
        bool _sawHalted;
        bool _sawWalkingIn;
        int _swarmRaises;
        int _approachHeading;
        int _officersAtStart;
        int _today;
        float _beganAt;
        float _phaseAt;
        float _nextOrder;
        float _nextRow;
        float _blockEstablishedAt = -1f;
        string _last = "waiting for the city";
        CarriageStage _stage = CarriageStage.Calling;

        Scenario Kind => (Scenario)Mathf.Clamp(scenario, 1, 12);
        float Now => Time.timeSinceLevelLoad;

        void Start()
        {
            _beganAt = Now;
            _phaseAt = Now;
        }

        void Update()
        {
            Bind();
            Row();
            if (State == Phase.Done || State == Phase.Failed ||
                State == Phase.AwaitingReload)
                return;
            if (Now < startAfter) return;
            if (Now - Mathf.Max(startAfter, _beganAt) > Mathf.Max(30f, patience))
            {
                Fail("the scenario did not reach an ending before its hard ceiling");
                return;
            }
            if (_force == null || _crews == null || _force.Station == null ||
                PersonnelDirector.Instance?.Roster == null)
                return;

            if (!_booked)
            {
                PrepareCase();
                return;
            }
            if (!_inside)
            {
                WaitForCells();
                return;
            }
            if (!_positioned)
            {
                PositionPlayer();
                return;
            }
            if (!_scheduled)
            {
                Schedule();
                return;
            }

            ReadTransfer();
            Act();
            ReadOutcome();
        }

        void Bind()
        {
            _force ??= PoliceForce.Instance ?? FindAnyObjectByType<PoliceForce>();
            _crews ??= DemoCrews.Active ?? FindAnyObjectByType<DemoCrews>();
        }

        // --------------------------------------------------------------- setup

        void PrepareCase()
        {
            if (NeedsRoadblock)
            {
                _blockerCrew = PickRoadblockCrew();
                _attacker = Kind == Scenario.RoadblockAndFire
                    ? PickAttacker(_blockerCrew)
                    : _blockerCrew;
            }
            else
                _attacker = PickAttacker(null);
            _prisonerUnit = PickPrisonerUnit(_attacker, _blockerCrew);
            _body = NamedBody(_prisonerUnit);
            if (_attacker == null || _prisonerUnit == null || _body == null)
            {
                Fail(Kind == Scenario.RoadblockAndFire
                    ? "ROAD-006 needs three live outfit crews: prisoner, blocker and shooter"
                    : "ROAD-006 needs two live outfit crews: one prisoner and one player crew");
                return;
            }
            if (NeedsRoadblock && (_blockerCrew == null ||
                                   _crews.CarOf(_blockerCrew) == null))
            {
                Fail("the roadblock scenario needs an outfit crew with a ledger car");
                return;
            }

            var director = OutfitDirector.Instance;
            _today = director?.Campaign != null ? director.Campaign.Day : 0;
            if (_today <= 0)
            {
                Fail("the campaign has no current day");
                return;
            }

            // Keep the physical person first. TakeInOne touches the roster and can cause
            // a street Sync on the same frame; the latch must already own the body.
            _force.KeepCustodyAlive(_body.CharacterId);
            _prisonerUnit.InCustody = true;
            _prisonerUnit.CustodyTracked = true;
            _prisonerUnit.Surrendered = true;
            _body.Surrendered = true;
            _body.Disarm();

            _case = _force.Pipeline.OpenCase(Deed.Affray,
                _prisonerUnit.Faction, _today, _today + 1,
                "road-006", "THE ROAD TO THE COURTHOUSE");
            _case.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.PoliceFoundThem,
                Name = "The night-watch arresting officer",
                Seed = scenario,
                X = _force.Station.Door.x,
                Y = _force.Station.Door.y,
                Z = _force.Station.Door.z,
            });

            if (!_crews.TakeInOne(_prisonerUnit, _body, Deed.Affray,
                    _force.Pipeline, _case, DoorAnswer.Quiet))
            {
                _force.ReleaseCustodyTracking(_body.CharacterId);
                Fail("the shared booking path refused the scenario prisoner");
                return;
            }

            _prisoner = _force.Pipeline.Find(_body.CharacterId);
            if (_prisoner == null)
            {
                Fail("booking returned without a prisoner row");
                return;
            }

            if (NeedsBomb) StockGrenades(2);
            _officersAtStart = _force.Station.Roster != null
                ? _force.Station.Roster.Officers : 0;
            DoorBeat.MoveIn(_body, _force.Station.Door);
            Set(Phase.Preparing, "the exact prisoner is walking into the station");
        }

        void WaitForCells()
        {
            if (_body == null || _body.Dead)
            {
                Fail("the scenario prisoner died before his transfer was called");
                return;
            }
            if (!DoorBeat.Held(_body)) return;
            _inside = true;
            Set(Phase.Positioning, "the prisoner is inside; positioning the player crew");
        }

        void PositionPlayer()
        {
            if (_attacker == null || _attacker.Wiped)
            {
                Fail("the player crew was wiped before the scenario began");
                return;
            }

            if (NeedsRoadblock)
            {
                _blocker ??= _crews.CarOf(_blockerCrew);
                if (_blocker == null)
                {
                    Fail("the selected roadblock crew lost its car");
                    return;
                }
                _crews.Select(_blockerCrew);
                if (_blockerCrew.Car != _blocker)
                {
                    if (Now >= _nextOrder)
                    {
                        _nextOrder = Now + 4f;
                        _crews.OrderCar(_blocker);
                    }
                    return;
                }
                if (!_force.HasCourthouse)
                {
                    Fail("the roadblock scenarios need the core's real courthouse approach");
                    return;
                }
                if (!_blockOrdered)
                {
                    if (Now < _nextOrder) return;
                    _nextOrder = Now + 8f;
                    var blockAt = _force.CourthouseKerb;
                    if (_crews.OrderSelected(blockAt, out _) &&
                        _blocker.OrderRoadblock(blockAt))
                    {
                        _blockOrdered = true;
                        Note("BLOCK THE ROAD HERE was ordered at the courthouse approach");
                    }
                    return;
                }
                if (!_blocker.IsRoadblock) return;
                _blockEstablished = true;
                // ROADBLOCK + FIRE uses three real crews: the prisoner, the driver who
                // stays behind the wheel, and a different armed crew beside the route.
                if (Kind == Scenario.RoadblockAndFire &&
                    !Near(_attacker, _blocker.Position, 10f))
                {
                    WalkPlayerTo(_blocker.Position);
                    return;
                }
                Ready("the crewed roadblock is standing across the court approach");
                return;
            }

            if (Kind == Scenario.BombBeforePickup)
            {
                if (_emptyBombCar == null || _emptyBombCar.Wrecked ||
                    !_emptyBombCar.Available)
                    _force.TryGetFreeTransferCar(out _emptyBombCar);
                if (_emptyBombCar == null) return;
                if (!Near(_attacker, _emptyBombCar.Position,
                        Mathf.Max(2f, _crews.BombPlantRange - 1f)))
                {
                    WalkPlayerTo(_emptyBombCar.Position);
                    return;
                }
                _crews.Select(_attacker);
                if (!_crews.OrderPlantBomb(_emptyBombCar))
                {
                    if (Now >= _nextOrder)
                    {
                        _nextOrder = Now + 2f;
                        _last = "plant refused: " + (_crews.BombRefusal ?? "unknown");
                    }
                    return;
                }
                _planted = true;
                Ready("a charge is under the empty car the scheduler will call");
                // Assign that exact still-live car before PlantedBomb's next Update can
                // observe its patrol speed and spring. The call remains empty until it
                // reaches the station and the walking-out stage begins.
                Schedule();
                return;
            }

            var at = Kind == Scenario.CourthouseDoorAmbush && _force.HasCourthouse
                ? _force.CourthouseDoor : _force.Station.Door;
            if (!Near(_attacker, at, 10f))
            {
                WalkPlayerTo(at);
                return;
            }
            Ready("the player crew is in position");
        }

        void Ready(string what)
        {
            _positioned = true;
            _last = what;
            Note(what);
        }

        void WalkPlayerTo(Vector3 at)
        {
            if (Now < _nextOrder) return;
            _nextOrder = Now + 5f;
            _crews.Select(_attacker);
            if (!_crews.MarchTo(_attacker, at, run: true, keepOffRoad: false))
                _last = "move refused: " + (_crews.OrderRefusal ?? "unknown");
        }

        void Schedule()
        {
            if (!_force.TryGetFreeTransferCar(out var next))
            {
                if (Kind == Scenario.BombBeforePickup && _planted)
                    Fail("the armed carrier stopped being schedulable; refusing to re-stage a live charge");
                return;
            }
            if (Kind == Scenario.BombBeforePickup && next != _emptyBombCar)
            {
                // The charge already belongs to _emptyBombCar and waits forever until
                // that exact car moves. Never forget it and plant a second charge when
                // another call changes the scheduler's first choice: that would leave
                // an armed orphan in the scene and corrupt ROAD-006's police-loss
                // verdict. Stop with an explicit failed setup instead.
                Fail(_planted
                    ? "the scheduler changed cars after the charge was planted; refusing to abandon the armed car"
                    : "the scheduler changed cars before the chosen carrier could be reserved");
                return;
            }
            if (Kind == Scenario.NoCourthouse) _force.ClearCourthouse();

            _prisoner.CourtDay = _today;
            if (_case != null) _case.CourtDay = _today;
            _force.ScheduleDueTransfers(_today);
            _scheduled = true;
            Set(Phase.Running, "the production scheduler called the transfer");
            ReadTransfer();
            if (_transfer == null)
                Fail("a free car existed but the due prisoner produced no transfer");
        }

        // --------------------------------------------------------------- action

        void ReadTransfer()
        {
            _transfer = null;
            if (_body == null || !_force.TryGetPrisonerTransfer(_body.CharacterId,
                    out var car, out _, out var stage, out var escort,
                    out var swarmRaises) || car == null)
            {
                return;
            }
            _transfer = car;
            var previousStage = _stage;
            _stage = stage;
            _escort = escort ?? _escort;
            _swarmRaises = Mathf.Max(_swarmRaises, swarmRaises);

            if (_transfer == null) return;
            switch (_stage)
            {
                case CarriageStage.WalkingOut: _sawWalkOut = true; break;
                case CarriageStage.Boarding: _sawWalkOut = true; break;
                case CarriageStage.Riding: _sawRiding = true; break;
                case CarriageStage.Halted: _sawHalted = true; break;
                case CarriageStage.WalkingIn: _sawWalkingIn = true; break;
            }
            if (_stage != previousStage) Row(force: true);
        }

        void Act()
        {
            if (_transfer == null) return;
            switch (Kind)
            {
                case Scenario.EscortDismount:
                case Scenario.EscortWiped:
                    ShootMovingTransfer();
                    break;

                case Scenario.BombAtLoadingKerb:
                    if (!_planted && (_stage == CarriageStage.WalkingOut ||
                                      _stage == CarriageStage.Boarding))
                        PlantOn(_transfer);
                    break;

                case Scenario.BombOnHaltedTransfer:
                    if (!_acted) ShootMovingTransfer();
                    if (_stage == CarriageStage.Halted && !_planted)
                        BombHaltedTransfer();
                    break;

                case Scenario.RoadblockAlone:
                case Scenario.RoadblockAndFire:
                    TickRoadblock();
                    break;

                case Scenario.StationDoorAmbush:
                    if (!_acted && (_stage == CarriageStage.WalkingOut ||
                                    _stage == CarriageStage.Boarding))
                        AttackEscort();
                    break;

                case Scenario.CourthouseDoorAmbush:
                    if (!_acted && _stage == CarriageStage.WalkingIn)
                        AttackEscort();
                    break;

                case Scenario.EscortWins:
                    ShootMovingTransfer();
                    if (_sawHalted && !_withdrawing && _body != null && !_body.Riding)
                    {
                        var away = _attacker.Position - _transfer.Position;
                        away.y = 0f;
                        if (away.sqrMagnitude < 1f) away = Vector3.right;
                        away = _attacker.Position + away.normalized * 120f;
                        _crews.MarchTo(_attacker, away, run: true);
                        _withdrawing = true;
                        Note("the attacker is withdrawn; the surviving escort owns the recovery");
                    }
                    break;

                case Scenario.SaveMidTransfer:
                    if (!_acted && _stage == CarriageStage.Riding)
                        SaveRoundTrip();
                    break;
            }
        }

        void ShootMovingTransfer()
        {
            if (_acted || _stage != CarriageStage.Riding) return;
            _crews.Select(_attacker);
            if (!_crews.OrderShootCar(_transfer))
            {
                _last = "SHOOT IT UP refused: " +
                        (_crews.ShootCarRefusal ?? "unknown");
                return;
            }
            _acted = true;
            Note("SHOOT IT UP was ordered on the moving transfer");
        }

        void PlantOn(RoadCar car)
        {
            if (car == null) return;
            _crews.Select(_attacker);
            if (!_crews.CanBombPlant(_attacker, car))
            {
                WalkPlayerTo(car.Position);
                _last = "moving to plant range: " +
                        (_crews.BombRefusal ?? "not yet in position");
                return;
            }
            if (!_crews.OrderPlantBomb(car))
            {
                _last = "plant refused: " + (_crews.BombRefusal ?? "unknown");
                return;
            }
            _planted = true;
            Note("a charge was planted under " + car.DisplayName);
        }

        void BombHaltedTransfer()
        {
            if (_transfer == null || _planted) return;
            _crews.Select(_attacker);
            if (_crews.CanBombThrow(_attacker, _transfer.Position))
            {
                if (!_crews.OrderBombThrowAt(_transfer.Position))
                {
                    _last = "throw refused: " + (_crews.BombRefusal ?? "unknown");
                    return;
                }
                _planted = true;
                Note("a grenade was thrown into the halted transfer");
                return;
            }

            // A planted charge waits for a car to drive off, while this carrier has
            // deliberately become a derelict. Put the throwing crew at a safe, reachable
            // standoff and use the public grenade order; scenarios 3 and 5 still exercise
            // the planted-charge path itself.
            var away = _attacker.Position - _transfer.Position;
            away.y = 0f;
            if (away.sqrMagnitude < 1f)
                away = _transfer.Tf != null ? _transfer.Tf.right : Vector3.right;
            var minimum = Explosion.Radius + 1.75f;
            var maximum = _crews.BombThrowRange - 0.5f;
            if (maximum < minimum)
            {
                Fail("the configured grenade range cannot clear its own blast");
                return;
            }
            var standoff = Mathf.Min(12f, maximum);
            WalkPlayerTo(_transfer.Position + away.normalized * standoff);
            _last = "moving to grenade range: " +
                    (_crews.BombRefusal ?? "not yet in position");
        }

        void AttackEscort()
        {
            if (_escort == null || _escort.Wiped)
                _escort = NearestPolice(
                    _transfer != null ? _transfer.Position : _body.Tf.position, 24f);
            if (_escort == null) return;
            _crews.Select(_attacker);
            if (!_crews.OrderAttack(_escort)) return;
            _acted = true;
            Note("the escort was attacked on foot");
        }

        void TickRoadblock()
        {
            if (_blocker == null || _blocker.Wrecked)
            {
                Fail("the roadblock car was lost before it blocked the transfer");
                return;
            }
            if (_blocker.IsRoadblock && !_blockEstablished)
            {
                _blockEstablished = true;
                Note("the physical roadblock is established");
            }
            if (!_metBlock && _stage == CarriageStage.Riding &&
                CrewCar.RoadblockAhead(_transfer, 65f, out var met) && met == _blocker)
            {
                _metBlock = true;
                _blockEstablishedAt = Now;
                _approachRoad = _transfer.Road;
                _approachHeading = _transfer.Heading;
                Note("the transfer reached the full-width roadblock");
            }
            if (Kind == Scenario.RoadblockAndFire && _metBlock && !_acted &&
                Flat(_transfer.Position, _blocker.Position) <= 70f)
                ShootMovingTransfer();
        }

        void SaveRoundTrip()
        {
            _acted = true;
            var written = CampaignSave.Compose();
            if (written == null)
            {
                Fail("the production save composer returned no campaign");
                return;
            }
            var json = JsonUtility.ToJson(written);
            var read = JsonUtility.FromJson<CampaignFile>(json);
            if (read == null)
            {
                Fail("the production save JSON could not be read back");
                return;
            }
            CampaignSave.Apply(read);
            var back = _force.Pipeline.Find(_body.CharacterId);
            if (back == null || back.Stage != PrisonStage.Held ||
                back.Leg != PrisonLeg.None || back.CourtDay != _today + 1 ||
                back.Carriage.HasValue || _force.Transfers != 0 ||
                !DoorBeat.Held(_body))
            {
                Fail("the production apply did not return the active court leg to tomorrow's cells");
                return;
            }
            Pass("the production JSON/apply path filed the live ride back in tomorrow's cells");
        }

        // -------------------------------------------------------------- verdict

        void ReadOutcome()
        {
            var held = _force.Pipeline.Find(_body.CharacterId);
            var verdict = _case?.VerdictFor(_body.CharacterId);
            var member = PersonnelDirector.Instance?.Roster?.Find(_body.CharacterId);
            var policeLost = _officersAtStart -
                (_force.Station.Roster != null ? _force.Station.Roster.Officers : 0);

            switch (Kind)
            {
                case Scenario.EscortDismount:
                    if (_sawHalted && _body != null && !_body.Riding &&
                        _escort != null && !_escort.Wiped && _swarmRaises == 1 &&
                        (_escort.TargetUnit == _attacker ||
                         _attacker.TargetUnit == _escort))
                        Pass("the carrier halted, raised the swarm once and the living escort came out fighting");
                    break;

                case Scenario.EscortWiped:
                    if (held == null && _prisoner.Stage == PrisonStage.Freed &&
                        _body != null && !_body.Dead && !_body.Carrying &&
                        member?.WantedLevel == WantedLevels.FreedFromTransfer &&
                        _case != null && _case.Status == CaseStatus.Open &&
                        _case.ExtraCharges.Contains(Deed.Resisting))
                        Pass("the wiped escort released a living unarmed W2 man and left Resisting on the open case");
                    break;

                case Scenario.BombAtLoadingKerb:
                case Scenario.BombOnHaltedTransfer:
                    if (verdict?.Outcome == CaseOutcome.Killed && _body.Dead &&
                        member?.Status == CharacterStatus.Dead && policeLost == 2)
                        Pass("the blast struck the roster, closed the case and cost exactly two officers");
                    break;

                case Scenario.BombBeforePickup:
                    if (_emptyBombCar != null && _emptyBombCar.Wrecked &&
                        held != null && held.Stage != PrisonStage.Freed &&
                        held.CourtDay == _today + 1 && verdict == null &&
                        policeLost == 2)
                        Pass("the empty carrier cost exactly two officers and the prisoner stayed for tomorrow");
                    break;

                case Scenario.RoadblockAlone:
                    if (_metBlock && _transfer != null)
                    {
                        var escaped = _transfer.Road != _approachRoad ||
                                      _transfer.Heading != _approachHeading;
                        var heldPastTimeout = _blockEstablishedAt >= 0f &&
                            Now - _blockEstablishedAt > 305f &&
                            _transfer.RoadSpeed < 0.2f &&
                            Flat(_transfer.Position, _blocker.Position) < 70f;
                        if (escaped || heldPastTimeout)
                            Pass(escaped
                                ? "the transfer reversed or re-routed around the physical block"
                                : "the transfer stood at the physical block past its ordinary timeout");
                    }
                    break;

                case Scenario.RoadblockAndFire:
                    if (_metBlock && _sawHalted && _body != null && !_body.Riding)
                        Pass("the block delayed the car and gunfire halted it for the escort fight");
                    break;

                case Scenario.StationDoorAmbush:
                    if (_acted && _sawWalkOut && _force.Transfers == 0 &&
                        held == null && _prisoner.Stage == PrisonStage.Freed &&
                        _prisoner.Sprung && member?.WantedLevel ==
                            WantedLevels.FreedFromTransfer &&
                        _case != null && _case.Status == CaseStatus.Open &&
                        _case.ExtraCharges.Contains(Deed.Resisting) && verdict == null)
                        Pass("the pre-seat ambush used the sprung exit; Freed stayed reserved for riders");
                    break;

                case Scenario.CourthouseDoorAmbush:
                    if (_acted && _sawWalkingIn && held == null &&
                        _prisoner.Stage == PrisonStage.Freed && verdict == null)
                        Pass("the courthouse-door ambush freed him before trial");
                    break;

                case Scenario.EscortWins:
                    if (_withdrawing && verdict != null && _sawWalkingIn)
                        Pass("the surviving escort recovered the leg and the threshold held the trial");
                    break;

                case Scenario.NoCourthouse:
                    if (!_force.HasCourthouse && _sawWalkOut && verdict != null)
                        Pass("without a courthouse the visible transfer reached the county line");
                    break;
            }
        }

        // --------------------------------------------------------------- trace

        void Pass(string what)
        {
            if (State == Phase.Done) return;
            Set(Phase.Done, what);
            Debug.Log("[CourtTransfer] PASS scenario " + scenario + ": " + what);
        }

        void Fail(string what)
        {
            if (State == Phase.Failed) return;
            Set(Phase.Failed, what);
            Debug.LogWarning("[CourtTransfer] FAIL scenario " + scenario + ": " + what);
            if (!DriveTrace.On) return;
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "tag", "court-transfer");
            DriveTrace.Str(sb, "fault", "court-transfer");
            DriveTrace.Int(sb, "scenario", scenario);
            DriveTrace.Str(sb, "state", State.ToString());
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Row("fault", sb.ToString());
        }

        void Set(Phase phase, string what)
        {
            State = phase;
            _phaseAt = Now;
            _last = what;
            Note(what);
            Row(force: true);
        }

        void Note(string what)
        {
            if (DriveTrace.On)
                DriveTrace.Event("mission", "court transfer " + scenario, what,
                    "\"state\":\"" + State + "\"");
        }

        void Row(bool force = false)
        {
            if (!DriveTrace.On || (!force && DriveTrace.Now < _nextRow)) return;
            _nextRow = DriveTrace.Now + 1f;
            var sb = DriveTrace.Take();
            DriveTrace.Int(sb, "scenario", scenario);
            DriveTrace.Str(sb, "name", Kind.ToString());
            DriveTrace.Str(sb, "state", State.ToString());
            DriveTrace.Str(sb, "stage", _stage.ToString());
            DriveTrace.Str(sb, "what", _last);
            DriveTrace.Int(sb, "prisoner", _body != null ? _body.CharacterId : -1);
            DriveTrace.Int(sb, "walker", _body != null ? _body.Id : -1);
            DriveTrace.Int(sb, "car", _transfer != null ? _transfer.Id : -1);
            DriveTrace.Num(sb, "v", _transfer != null ? _transfer.Speed : 0f);
            DriveTrace.Int(sb, "officers", _force?.Station?.Roster != null
                ? _force.Station.Roster.Officers : -1);
            DriveTrace.Str(sb, "pipe", _prisoner != null ? _prisoner.Stage.ToString() : "");
            DriveTrace.Str(sb, "outcome", _case?.VerdictFor(
                _body != null ? _body.CharacterId : -1)?.Outcome.ToString() ?? "");
            DriveTrace.Num(sb, "phaseFor", Now - _phaseAt, "F1");
            DriveTrace.Vec(sb, "p", _transfer != null ? _transfer.Position
                : _body?.Tf != null ? _body.Tf.position : Vector3.zero);
            DriveTrace.Row("mission", sb.ToString());
        }

        DemoCrews.Unit PickAttacker(DemoCrews.Unit except)
        {
            DemoCrews.Unit fallback = null;
            foreach (var unit in _crews.Units)
            {
                if (unit == null || unit == except || unit.Faction != 0 || unit.IsDetachment ||
                    unit.IsPolice || unit.Wiped || unit.InCustody)
                    continue;
                fallback ??= unit;
                if (Armed(unit) && unit.Car == null) return unit;
            }
            return fallback;
        }

        DemoCrews.Unit PickRoadblockCrew()
        {
            foreach (var unit in _crews.Units)
                if (unit != null && unit.Faction == 0 && !unit.IsDetachment &&
                    !unit.IsPolice && !unit.Wiped && !unit.InCustody &&
                    _crews.CarOf(unit) != null)
                    return unit;
            return null;
        }

        DemoCrews.Unit PickPrisonerUnit(DemoCrews.Unit except,
            DemoCrews.Unit alsoExcept)
        {
            foreach (var unit in _crews.Units)
                if (unit != null && unit != except && unit != alsoExcept && unit.Faction == 0 &&
                    !unit.IsDetachment && !unit.IsPolice && !unit.Wiped &&
                    NamedBody(unit) != null)
                    return unit;
            return null;
        }

        DemoCrews.Unit NearestPolice(Vector3 at, float within)
        {
            DemoCrews.Unit best = null;
            var bestD = within * within;
            foreach (var unit in _crews.Units)
            {
                if (unit == null || !unit.IsPolice || unit.Wiped) continue;
                var d = (unit.Position - at).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = unit;
            }
            return best;
        }

        static CrewWalker NamedBody(DemoCrews.Unit unit)
        {
            if (unit == null) return null;
            if (unit.Boss != null && !unit.Boss.Dead && unit.Boss.CharacterId >= 0)
                return unit.Boss;
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.CharacterId >= 0)
                    return man;
            return null;
        }

        static bool Armed(DemoCrews.Unit unit)
        {
            if (unit == null) return false;
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Carrying && !man.Riding)
                    return true;
            return false;
        }

        /// <summary>Deal the mission grenades through the same armory-to-lieutenant
        /// ledger path as ordinary outfit gear. The unit count is only a bare-scene
        /// fallback; BindBombs re-derives it from the ledger whenever a roster exists.</summary>
        void StockGrenades(int need)
        {
            var roster = PersonnelDirector.Instance?.Roster;
            var crew = roster?.FindCrew(_attacker.CrewId);
            if (crew != null)
            {
                var owned = RosterOps.GrenadesOwnedBy(roster, crew.LieutenantId);
                for (var i = owned; i < need; i++)
                {
                    var item = RosterOps.AddEquipment(roster,
                        EquipmentKind.Grenade, "Grenade", 175);
                    item.OwnerId = crew.LieutenantId;
                    item.HolderId = crew.LieutenantId;
                }
            }
            _attacker.Bombs = Mathf.Max(_attacker.Bombs, need);
        }

        static bool Near(DemoCrews.Unit unit, Vector3 at, float metres) =>
            unit != null && Flat(unit.Position, at) <= metres;

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        bool NeedsBomb => Kind == Scenario.BombAtLoadingKerb ||
                          Kind == Scenario.BombOnHaltedTransfer ||
                          Kind == Scenario.BombBeforePickup;

        bool NeedsRoadblock => Kind == Scenario.RoadblockAlone ||
                               Kind == Scenario.RoadblockAndFire;
    }
}
