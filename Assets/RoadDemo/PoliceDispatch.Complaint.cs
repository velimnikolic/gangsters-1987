using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Police;
using LivingCity.Territory;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE COMPLAINT (GAN-245). A shopkeeper who has been leaned on picks up the
    /// telephone, and for the first time something comes of it.
    ///
    /// This is the whole of the law's answer to a call with no shots in it, and it is
    /// deliberately the SMALLEST answer the department has: one unit, no siren, no
    /// warning phase - "DROP THE GUNS" needs guns - and no city-wide anything. A man
    /// walks to the door and either finds somebody standing there or takes a statement.
    ///
    /// The 1987 telephone is the whole tempo of it: the call lands
    /// <see cref="ComplaintDelay"/> seconds after the demand or threat, which is the head
    /// start the crew is given. A crew that leans on a shop and walks away has almost always
    /// gone by the time anybody arrives - and that is the intended play, not a
    /// shortcoming: what it leaves behind is a shop the racket cannot touch for the
    /// rest of the day and a count on the docket for the next time these men are taken.
    ///
    /// The arrest itself is NOT re-implemented here. A man of the accused faction still
    /// standing at the door hands straight to the EPIC 17 window (PoliceDispatch.Arrest)
    /// with the deed set to Extortion - the same walk-up, the same surrender roll, the
    /// same ARREST IN PROGRESS banner and the same FLEE.
    /// </summary>
    public sealed partial class PoliceDispatch
    {
        /// <summary>Seconds between the receiver coming off the hook and a unit being
        /// given the call. A 1987 switchboard, a desk sergeant and a radio: the band is
        /// rolled per complaint off its own stream, never per frame.</summary>
        const float ComplaintDelayLow = PoliceProcedure.ComplaintDelayMinimum;
        const float ComplaintDelayHigh = PoliceProcedure.ComplaintDelayMaximum;

        /// <summary>Metres from the door a man of the accused faction has to be for the
        /// officer to have somebody to speak to. An arm of the street: he has to be AT
        /// the shop, not on the block.</summary>
        const float ComplaintReach = 30f;

        /// <summary>Seconds the officer spends at a door with nobody at it, taking the
        /// shopkeeper's statement.</summary>
        const float StatementSeconds = 12f;

        /// <summary>Seconds a call waits for a unit before the precinct gives up on it.
        /// Nobody near, nobody comes - the station-LOCAL rule holds for a complaint
        /// exactly as it does for a shooting.</summary>
        const float ComplaintPatience = 90f;

        /// <summary>What a statement puts on the block. Small: a uniform standing in a
        /// doorway with a notebook is not a raid.</summary>
        const float StatementAttention = 12f;

        enum CallStage { Ringing, Walking, Closing, AtTheDoor, Statement, Arresting, Boarding, Done }

        /// <summary>Metres from the door the officer has to be for the door to count as
        /// answered. ComplaintReach is how far a call REACHES; this is where a man
        /// stands when he is speaking to somebody.</summary>
        const float DoorstepReach = 6f;

        /// <summary>The longest last leg that is walked by hand. The pavement graph gets
        /// the officer to the nearest corner and the leg from there to the door is a
        /// straight line, exactly as DoorBeat's own threshold is - which is only honest
        /// over a few metres. A shop further from its pavement than this is answered
        /// from the corner, as it always was.</summary>
        const float ClosingMax = 20f;

        /// <summary>Seconds the last leg is given before the door is called answered
        /// wherever he got to.</summary>
        const float ClosingSeconds = 14f;

        sealed class CallOut
        {
            public StreetAlarm.Complaint Call;
            public float RingAt;
            public float GiveUpAt;
            public IPoliceUnit Unit;
            /// <summary>The car sent out beside a far pair. Whichever of the two pulls
            /// up first becomes <see cref="Unit"/>; the other is turned round.</summary>
            public IPoliceUnit Backup;
            public DemoCrews.Unit Men;      // the men a CAR brought, when a car answered
            public CallStage Stage = CallStage.Ringing;
            public float StatementBy;
            public bool StatementVisit;
            public bool StatementRecorded;
            public bool StatementEntered;
            public bool StatementInterviewed;
            public CourtCase File;

            // A block shakedown can put several calls in the switchboard at once.
            // Each call keeps the people who saw ITS visit; a single dispatcher-wide
            // snapshot would let the next telephone overwrite the previous door.
            public readonly List<SceneWitness> Witnesses = new List<SceneWitness>();

            /// <summary>The backstop on the hand-walked last leg to the door.</summary>
            public float ClosedBy;

            /// <summary>Whether the officer is actually ON the doorstep, as opposed to
            /// standing at the nearest corner of the pavement graph. Only a man at the
            /// door may put a question to anybody; a man at the corner takes a statement
            /// and that is all he can honestly do.</summary>
            public bool AtTheDoorstep;

            /// <summary>The men were asked and would not go. NOT the same disposition as
            /// a door with nobody at it, and never filed as one.</summary>
            public bool MenRefused;
            public bool MenRan;
            public bool MenFought;
            public DemoCrews.Unit Accused;
            public Custody Transfer;

            /// <summary>Whether this call has already asked a station to put a car out.
            /// Once is once: a ringing telephone does not empty a garage.</summary>
            public bool TurnedOut;

            /// <summary>The backstop on going home. A squad that cannot reach its car -
            /// a body in the way, a torn pavement - must not hold a unit off the road
            /// for the rest of the campaign.</summary>
            public float HomeBy;
        }

        readonly List<CallOut> _calls = new List<CallOut>();
        static readonly List<CivilianAgent> _sawIt = new List<CivilianAgent>();

        /// <summary>
        /// WHO WAS THERE WHEN IT HAPPENED - frozen the moment an incident opens.
        ///
        /// A `Witness` is pure data on purpose (Police.CourtCase): it has to outlive the
        /// body. So the body, the name, the seed and the POSITION AT THE TIME are all
        /// taken at once and kept until a case wants them - which can be a hundred and
        /// fifty seconds later, when half of these people are indoors.
        /// </summary>
        struct SceneWitness
        {
            public CivilianAgent Body;
            public string Name;
            public int Seed;
            public Vector3 At;
        }

        readonly List<SceneWitness> _sceneWitnesses = new List<SceneWitness>();

        /// <summary>The incident the list above belongs to; -1 when there is none.</summary>
        int _sceneIncident = -1;

        /// <summary>The last incident a squad of the law was actually LOOKING at while it
        /// was going on - shouting the warning, or in the fight. Recorded there rather
        /// than read at arrest time, when every squad is Securing a quiet street.</summary>
        int _lawSawIncident = -1;

        /// <summary>An incident just opened: take the names down.</summary>
        void SnapshotTheScene(Vector3 where)
        {
            _sceneIncident = StreetAlarm.IncidentNumber;
            SnapshotTheScene(where, _sceneWitnesses);
        }

        /// <summary>Freeze the people who reacted to one act into the list owned by
        /// that act. Shooting incidents use the dispatcher-wide slot above; telephone
        /// calls use their own CallOut slot because several can wait concurrently.</summary>
        static void SnapshotTheScene(Vector3 where, List<SceneWitness> into)
        {
            if (into == null) return;
            into.Clear();
            CivilianAgent.SnapshotWitnesses(where, CivilianAgent.SightRadius,
                CivilianAgent.MaxEyewitnesses, _sawIt);
            for (var i = 0; i < _sawIt.Count; i++)
            {
                var saw = _sawIt[i];
                if (saw == null || saw.Tf == null) continue;
                into.Add(new SceneWitness
                {
                    Body = saw,
                    Name = saw.PersonName,
                    Seed = saw.WitnessSeed,
                    At = saw.Tf.position,
                });
            }
        }

        /// <summary>This squad is looking at the act as it happens.</summary>
        void NoteLawWatchedIt(Squad squad)
        {
            if (squad?.Men == null || squad.Men.Wiped) return;
            if ((squad.Men.Position - StreetAlarm.Incident).sqrMagnitude >
                LawEyes * LawEyes) return;
            _lawSawIncident = StreetAlarm.IncidentNumber;
        }

        /// <summary>Copies the frozen scene onto a case. A man already dead by the time
        /// the case is opened is simply not written down - he is off it either way, and
        /// a corpse on a witness list is a line the paper would not print.</summary>
        void CopySceneWitnesses(CourtCase file, int incident)
        {
            if (file == null || _sceneIncident != incident) return;
            CopySceneWitnesses(file, _sceneWitnesses);
        }

        static void CopySceneWitnesses(CourtCase file, List<SceneWitness> witnesses)
        {
            if (file == null || witnesses == null) return;
            for (var i = 0; i < witnesses.Count; i++)
            {
                var seen = witnesses[i];
                if (seen.Body != null && seen.Body.Dead) continue;
                var witness = new Witness
                {
                    Kind = WitnessKind.Eyewitness,
                    Name = seen.Name,
                    Seed = seen.Seed,
                    X = seen.At.x, Y = seen.At.y, Z = seen.At.z,
                };
                file.Witnesses.Add(witness);
                if (seen.Body != null) WitnessWatch.Register(file, witness, seen.Body);
            }
        }

        /// <summary>Somebody rang. Nothing moves yet - the call has to reach a car.</summary>
        void OnComplaint(StreetAlarm.Complaint call)
        {
            var wait = ComplaintDelayLow +
                       (ComplaintDelayHigh - ComplaintDelayLow) * DelayRoll(call);
            var queued = new CallOut
            {
                Call = call,
                RingAt = Time.time + wait,
                GiveUpAt = Time.time + wait + ComplaintPatience,
            };
            // The act a complaint is about is the EXTORTION VISIT, and it happened just
            // now - so this call owns the pavement read taken now, not the people standing
            // there when the officer finally walks up half a minute later.
            SnapshotTheScene(call.Pos, queued.Witnesses);
            _calls.Add(queued);
            // The banner is the PLAYER'S news. A shopkeeper who rang about somebody
            // else's family is a thing that happened in the city, not a thing that
            // happened to him - the officer still turns out for it either way.
            CrewOverlay.AnnounceOurs(call.Faction, "COMPLAINT — " +
                (string.IsNullOrEmpty(call.Where)
                    ? "A SHOPKEEPER" : call.Where.ToUpperInvariant()),
                4.5f, new Color(1f, 0.85f, 0.55f));
            LawWire.ComplaintRung(call);
        }

        /// <summary>The delay, 0..1, off the complaint's own stream. Deterministic like
        /// everything else that decides an outcome: the same city on the same morning
        /// gives the crew the same head start.</summary>
        static float DelayRoll(StreetAlarm.Complaint call)
        {
            var stream = ComplaintRoll.StreamFor(
                LivingCity.Business.BusinessRuntime.Instance != null
                    ? LivingCity.Business.BusinessRuntime.Instance.CitySeed : 1987,
                call.BusinessId, (int)(call.GameHour / 24.0), call.Number);
            unchecked
            {
                var h = (uint)stream * 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFF) / 65535f;
            }
        }

        void TickCalls(float dt)
        {
            for (var i = _calls.Count - 1; i >= 0; i--)
            {
                var call = _calls[i];
                switch (call.Stage)
                {
                    case CallStage.Ringing:
                        if (Time.time < call.RingAt) break;
                        if (SendToDoor(call)) break;
                        // NOTHING LEFT ON THE STREET. Not "everybody is busy" - that is
                        // what the switchboard's own patience is for - but every man and
                        // every car of the city gone. The nearest house that is still
                        // authorised a car puts one out, once per call, and the next tick
                        // sends it.
                        if (!call.TurnedOut && NothingLeftOnTheStreet())
                        {
                            call.TurnedOut = true;
                            if (Force != null && Force.TurnOutACar(call.Call.Pos) != null) break;
                        }
                        if (Time.time <= call.GiveUpAt) break;
                        // NOBODY WAS SENT, AND THE PLAYER IS TOLD SO. A call that dies
                        // in the switchboard used to leave no trace at all - no line, no
                        // case, nothing - and read as the shopkeeper's telephone being
                        // broken rather than as the precinct being out of men.
                        LawWire.NobodyCame(call.Call);
                        CrewOverlay.AnnounceOurs(call.Call.Faction, "NOBODY CAME", 4f,
                            new Color(1f, 0.85f, 0.55f));
                        call.Stage = CallStage.Done;
                        break;

                    case CallStage.Walking: TickWalking(call); break;
                    case CallStage.Closing: TickClosing(call); break;
                    case CallStage.AtTheDoor: AtTheDoor(call); break;
                    case CallStage.Statement:
                        // DoorBeat owns a real shop visit until the officer has crossed
                        // back onto the pavement. Its callbacks record the statement
                        // inside and release this call only after the outward crossing -
                        // but the backstop this stage is given is the same one the collar
                        // below has, and for the same reason: a passage that somehow
                        // never answers must not hold a unit off the road for the rest of
                        // the campaign.
                        if (call.StatementVisit && Time.time < call.HomeBy) break;
                        if (call.StatementVisit) { StatementVisitFailed(call); break; }
                        if (Time.time < call.StatementBy) break;
                        StatementTaken(call);
                        break;

                    // THE CALL OUTLIVES THE ARREST. The collar is the dispatcher's own
                    // machinery and knows nothing about which car brought the officer,
                    // so the call goes on holding its unit until the question has been
                    // answered one way or the other - and only then takes it home.
                    case CallStage.Arresting:
                        // The backstop is the collar's own patience and a little over:
                        // a window that somehow never closes must not hold a car off
                        // the road for the rest of the campaign.
                        if (_collar != Collar.None && Time.time < call.HomeBy) break;
                        if (call.Transfer != null && !call.Transfer.Finished) break;
                        if (call.MenRan && call.Accused != null &&
                            call.Accused.Fleeing && !call.Accused.Wiped &&
                            (call.Accused.Position - call.Call.Pos).sqrMagnitude <
                            ComplaintReach * ComplaintReach) break;
                        if (call.MenFought && call.Accused != null &&
                            !call.Accused.Wiped && call.Accused.TargetUnit != null &&
                            !call.Accused.TargetUnit.Wiped) break;
                        // NOBODY WAS TAKEN, SO THE CALL IS NOT ANSWERED. The men walked
                        // off the question, or the window ran out - and the officer is
                        // still stood at a door somebody rang about. He does what he
                        // would have done had he found the pavement empty: he takes the
                        // statement. Without this the shop that telephoned got a case on
                        // the docket and then silence - no line in the paper, and none of
                        // the day's protection a statement is worth.
                        // A REFUSAL IS ITS OWN ANSWER. The men were found, asked and
                        // said no: printing "an officer found nobody to take in" over
                        // that is a lie, and the shop gets no quiet day out of a crew
                        // that faced the law down on its step. The case stays open,
                        // which is what makes it a count next time (Clear leaves a
                        // complaint's file alone).
                        if (call.MenRefused)
                        {
                            Close(call);
                            break;
                        }
                        if (!AnybodyTaken(call) && StillAtTheDoor(call) &&
                            StreetAlarm.QuietFor > StatementQuiet)
                        {
                            BeginStatement(call);
                            break;
                        }
                        Close(call);
                        break;

                    case CallStage.Boarding: TickBoarding(call); break;
                }
                if (call.Stage == CallStage.Done) _calls.RemoveAt(i);
            }
        }

        /// <summary>
        /// ONE UNIT, AND IT IS THE NEAREST ONE THERE IS (the user's rule, 2026-09-03).
        ///
        /// The response range is gone from the telephone. It was written as the same
        /// "station-LOCAL" rule a shooting gets, and on a city of any size that meant a
        /// shop with no beat inside 150 m of it was rung about, waited a minute and a
        /// half, and got nobody - deterministically, for the whole campaign, because a
        /// beat pair walks its own block and never happens to be somewhere else. A
        /// complaint is not a gunfight: nobody is running, and a car that takes four
        /// minutes to cross the city is still an answer.
        ///
        /// Nearest is the shortest X/Z chord on the map, and it is the nearest PAIR
        /// (the user's rule, 2026-09-04): a car never jumps a beat pair, however far the
        /// pair is. Past PoliceProcedure.FootResponseCarRange the nearest car goes out
        /// beside the pair and whichever pulls up first answers the door; a city with
        /// nobody free on foot sends the car alone. The call is then given the patience
        /// that the pair's trip actually needs.
        /// </summary>
        bool SendToDoor(CallOut call)
        {
            var door = call.Call.Pos;
            var unit = NearestToAnswer(door, carries: false, out var trip, out var footD);
            IPoliceUnit backup = null;
            if (unit == null)
                unit = NearestToAnswer(door, carries: true, out trip, out _);
            else if (PoliceProcedure.CarJoinsFootResponse(anyFootFree: true, footD))
                backup = NearestToAnswer(door, carries: true, out _, out _);
            if (unit == null) return false;

            unit.RouteTo(door, 5f);
            call.Unit = unit;
            if (backup != null)
            {
                backup.RouteTo(door, 5f);
                call.Backup = backup;
                if (_lights.TryGetValue(backup, out var backupLights))
                    backupLights.Set(true, siren: false);
            }
            call.Stage = CallStage.Walking;
            // THE CLOCK STARTS WHEN HE DOES, AND IT IS AS LONG AS THE TRIP. GiveUpAt was
            // set at the ring and was a flat minute and a half, so a unit sent from the
            // other side of the city was cancelled halfway to a door it was walking to.
            // The switchboard's wait and the journey are two different patiences.
            call.GiveUpAt = Time.time + ComplaintPatience + trip;
            // NO SIREN. Lights and a siren for a complaint would empty the street the
            // officer was sent to look at, and the whole point of the call is that he
            // arrives to find men standing in a doorway.
            if (_lights.TryGetValue(unit, out var lights)) lights.Set(true, siren: false);
            CrewOverlay.AnnounceOurs(call.Call.Faction, "A MAN OF THE LAW AT THE DOOR", 4f,
                new Color(0.55f, 0.78f, 1f));
            return true;
        }

        /// <summary>Metres a second used only to size the selected unit's timeout. It
        /// never participates in selection; nearest always means nearest in space.</summary>
        const float FootPace = 2.6f;
        const float CarPace = 8f;

        /// <summary>The physically nearest free unit of one kind - on foot or in a car -
        /// to this door by overhead-map straight line. Returns the selected unit's
        /// expected trip duration and its distance squared.</summary>
        IPoliceUnit NearestToAnswer(Vector3 door, bool carries, out float trip,
            out float distanceSquared)
        {
            IPoliceUnit best = null;
            float bestDistance = float.MaxValue;
            for (var i = 0; i < _units.Count; i++)
            {
                var unit = _units[i];
                if (unit == null || unit.Carries != carries || !unit.Available ||
                    unit.Tf == null) continue;
                var at = unit.Position;
                var distance = PoliceProcedure.AirDistanceSquared(
                    at.x, at.z, door.x, door.z);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = unit;
            }
            // The road/obstacle detour estimate is only a timeout for the winner. It
            // must never change who wins the dispatch race.
            trip = best != null
                ? Mathf.Sqrt(bestDistance) * 1.35f /
                  (best.Carries ? CarPace : FootPace)
                : 0f;
            distanceSquared = best != null ? bestDistance : float.MaxValue;
            return best;
        }

        /// <summary>Whether the city has ANY law left standing - a body on the street or
        /// a car on the road, busy or not. Nothing here is about who is free: it is the
        /// difference between "they are all out on other calls" and "they are all dead",
        /// and only the second one gets a car turned out of the station.</summary>
        bool NothingLeftOnTheStreet()
        {
            for (var i = 0; i < _units.Count; i++)
            {
                var unit = _units[i];
                if (unit == null || unit.Tf == null) continue;
                if (unit is PolicePatrolCar car && car.Wrecked) continue;
                return false;
            }
            return true;
        }

        void TickWalking(CallOut call)
        {
            var unit = call.Unit;
            if (unit == null || unit.Tf == null) { Close(call); return; }
            if (Time.time > call.GiveUpAt) { Close(call); return; }
            // THE RACE. The car sent out beside a far pair answers the door if it pulls
            // up first, and the pair is turned round; the pair arriving first sends the
            // car back. Either way one unit holds the call from here (the user's rule).
            if (call.Backup != null)
            {
                if (call.Backup.Tf == null) call.Backup = null;
                else if (call.Backup.OnScene && !unit.OnScene)
                {
                    HandBack(unit);
                    call.Unit = unit = call.Backup;
                    call.Backup = null;
                }
                else if (unit.OnScene)
                {
                    HandBack(call.Backup);
                    call.Backup = null;
                }
            }
            // Passing inside the complaint's broad 30 m search radius is not arrival.
            // The old radius shortcut advanced the call while the beat was still
            // Responding; from one approach it happened to be beside the doorstep, and
            // from another it stood at the corner and skipped the collar entirely.
            // Wait for the unit's own route state to prove that it physically arrived.
            if (!PoliceProcedure.CanProcessComplaintArrival(unit.OnScene)) return;

            // A car has to put a man on the pavement before there is anybody to speak to.
            if (unit.Carries && call.Men == null)
            {
                if (unit is PoliceCruiser cruiser)
                {
                    if (cruiser.Men != null && !cruiser.Men.Wiped)
                    {
                        if (cruiser.Men.Car != null) { _crews.LeaveCar(cruiser.Men); return; }
                        call.Men = cruiser.Men;
                    }
                }
                else
                {
                    var t = unit.Tf;
                    var toDoor = call.Call.Pos - unit.Position;
                    toDoor.y = 0f;
                    var side = Vector3.Dot(toDoor, t.right) >= 0f ? 1f : -1f;
                    call.Men = SpawnSquad(unit.Position + t.right * side * 2.4f,
                        toDoor.sqrMagnitude > 0.01f ? toDoor.normalized : t.forward,
                        2, aboardOf: null);
                }
                if (call.Men == null) { Close(call); return; }
            }

            // THE LAST FEW METRES. Arriving is measured against the pavement GRAPH, and
            // the corner nearest a shop can be most of a block from its door - the
            // officer was then declared to be at a door he was nowhere near, which is
            // what a collar that never reached its man and a statement taken across a
            // car park both came out of. He walks the rest by hand, the same short
            // straight leg DoorBeat uses for a threshold.
            if (call.Unit is PoliceBeat foot && foot.Tf != null)
            {
                var toDoor = foot.Tf.position - call.Call.Pos;
                toDoor.y = 0f;
                float gap = toDoor.magnitude;
                if (gap > DoorstepReach && gap <= ClosingMax)
                {
                    var doorstep = call.Call.Pos + toDoor / gap * 2f;
                    foot.BeginDoorway(doorstep);
                    call.Stage = CallStage.Closing;
                    call.ClosedBy = Time.time + ClosingSeconds;
                    return;
                }
                call.AtTheDoorstep = gap <= DoorstepReach;
            }
            else
            {
                // A car puts its pair down at the kerb. They still run the physical last
                // leg together, over the same WalkRoute as a crew, before anybody can be
                // challenged or any statement visit can begin.
                var lead = Lead(call.Men);
                if (lead?.Tf != null)
                {
                    var toDoor = lead.Tf.position - call.Call.Pos;
                    toDoor.y = 0f;
                    var gap = toDoor.magnitude;
                    if (gap > DoorstepReach)
                    {
                        var doorstep = call.Call.Pos + toDoor / gap * 2f;
                        _crews.MarchTo(call.Men, doorstep,
                            run: PoliceProcedure.RunToScene,
                            keepOffRoad: false, allowCustody: true);
                        call.Stage = CallStage.Closing;
                        call.ClosedBy = Time.time + ClosingSeconds;
                        return;
                    }
                    call.AtTheDoorstep = gap <= DoorstepReach;
                }
            }

            call.Stage = CallStage.AtTheDoor;
        }

        /// <summary>Walking the last leg. He is at the door when he is on the doorstep or
        /// when the leg has had long enough; either way the officer is handed back to the
        /// call before anything else is asked of him.</summary>
        void TickClosing(CallOut call)
        {
            var foot = call.Unit as PoliceBeat;
            var body = foot != null ? foot.Lead : Lead(call.Men);
            if (body == null || body.Tf == null) { Close(call); return; }
            var toDoor = body.Tf.position - call.Call.Pos;
            toDoor.y = 0f;
            bool there = toDoor.sqrMagnitude <= DoorstepReach * DoorstepReach;
            if (!there && Time.time < call.ClosedBy) return;
            // A CLOCK RUNNING OUT IS NOT AN ARRIVAL. The leg is a straight line and a
            // shop behind a wall, a body in the way or a torn pavement will stop it; the
            // officer is then where he is, and the door was not answered by him standing
            // there. He may still take a statement - he is on the block, the shopkeeper
            // will speak to him - but nobody is asked to put his hands up by a man who
            // never arrived.
            foot?.EndDoorway();
            call.AtTheDoorstep = there;
            call.Stage = CallStage.AtTheDoor;
        }

        /// <summary>
        /// HE IS AT THE DOOR. A collar may open a case once a real accused man is found;
        /// otherwise the case waits for the officer to cross the shop threshold and take
        /// the statement. Merely reaching this dispatcher stage is not evidence.
        /// </summary>
        void AtTheDoor(CallOut call)
        {
            if (TryComplaintCollar(call))
            {
                // The EPIC 17 window has the CREW from here; the call keeps the UNIT,
                // because nothing in that window knows how to send a police car home.
                call.Stage = CallStage.Arresting;
                call.HomeBy = Time.time + CollarPatience + AskSeconds + TakeSeconds;
                return;
            }

            BeginStatement(call);
        }

        /// <summary>Seconds of quiet a statement needs behind it. An officer in a
        /// gunfight is not writing anything down, and the crew that refused him is what
        /// the shooting is: the call is simply closed and the wire says nothing.</summary>
        const float StatementQuiet = 12f;

        /// <summary>Whether the collar this call opened actually charged anybody. The
        /// complaint's file is the telephone's, not the arrest's, so the defendants on it
        /// are the one honest answer to "was anyone taken".</summary>
        static bool AnybodyTaken(CallOut call) =>
            call?.File != null && call.File.Defendants.Count > 0;

        /// <summary>He is still the officer this call sent, and still within arm of the
        /// street of the door it sent him to.</summary>
        bool StillAtTheDoor(CallOut call) =>
            call?.Unit != null && call.Unit.Tf != null &&
            (call.Unit.Position - call.Call.Pos).sqrMagnitude <=
            ComplaintReach * ComplaintReach;

        /// <summary>The statement stage: the shop passage where there is a shop to walk
        /// into, and a stand at the door where there is not.</summary>
        void BeginStatement(CallOut call)
        {
            call.Stage = CallStage.Statement;
            if (BeginStatementVisit(call))
                return;
            // A named business has a real doorway. If that passage cannot even start,
            // no statement is fabricated on the pavement and no case is opened.
            if (!string.IsNullOrEmpty(call.Call.BusinessId))
            {
                StatementVisitFailed(call);
                return;
            }
            call.StatementBy = Time.time + StatementSeconds;
        }

        /// <summary>The officer does not interview a shopkeeper through the glass. The
        /// lead body uses the same DoorBeat.VisitBusiness passage as the racketeer: a
        /// foot unit sends its lead while the partner covers the pavement; a car sends
        /// the lead of the crew it put down. Calls without a business (witness matters)
        /// retain the at-scene statement because there is no shop to enter.</summary>
        bool BeginStatementVisit(CallOut call)
        {
            if (call == null || string.IsNullOrEmpty(call.Call.BusinessId))
                return false;

            var businessId = new TerritoryBusinessId(call.Call.BusinessId);
            if (!businessId.IsValid)
                return false;

            call.StatementVisit = true;
            // The passage is a walk-up, a threshold, the counter and the walk back out.
            // ComplaintPatience is longer than all of it together and is what the stage
            // above gives up after.
            call.HomeBy = Time.time + ComplaintPatience;
            System.Action recorded = () =>
            {
                call.StatementEntered = true;
                call.StatementInterviewed = true;
                StatementTaken(call, close: false);
            };
            System.Action outside = () =>
            {
                StatementTaken(call, close: false);
                Close(call);
            };
            System.Action failed = () => StatementVisitFailed(call);

            if (call.Unit is PoliceBeat foot)
            {
                var officer = foot.Lead;
                if (officer != null && officer.Tf != null)
                {
                    if (DoorBeat.TryVisitBusiness(officer, businessId, call.Call.Pos,
                            whenInside: recorded, whenOut: outside,
                            insideSeconds: StatementSeconds, whenFailed: failed))
                        return true;
                }
            }
            else
            {
                var officer = Lead(call.Men);
                if (officer != null)
                {
                    if (DoorBeat.TryVisitBusiness(officer, businessId, call.Call.Pos,
                            whenInside: recorded, whenOut: outside,
                            insideSeconds: StatementSeconds, whenFailed: failed))
                        return true;
                }
            }

            call.StatementVisit = false;
            return false;
        }

        void StatementVisitFailed(CallOut call)
        {
            if (call == null || call.Stage == CallStage.Done) return;
            call.StatementVisit = false;
            CrewOverlay.AnnounceOurs(call.Call.Faction,
                "THE OFFICER COULD NOT REACH THE SHOP", 4f,
                new Color(1f, 0.85f, 0.55f));
            Close(call);
        }

        /// <summary>Nobody to speak to but the man behind the counter. The shop is out
        /// of the racket's reach for the rest of the day, the block has been looked at,
        /// and the complaint sits on the docket waiting for the next time these men are
        /// taken for anything.</summary>
        void StatementTaken(CallOut call, bool close = true)
        {
            if (call == null || call.Stage != CallStage.Statement ||
                call.StatementRecorded)
                return;
            if (!string.IsNullOrEmpty(call.Call.BusinessId) &&
                !PoliceProcedure.CanRecordShopStatement(
                    call.StatementEntered, call.StatementInterviewed))
                return;
            call.StatementRecorded = true;
            call.File ??= OpenComplaintCase(call);
            var runtime = TerritoryRuntime.Instance;
            if (runtime != null && !string.IsNullOrEmpty(call.Call.BusinessId))
            {
                var businessId = new TerritoryBusinessId(call.Call.BusinessId);
                runtime.MarkUnderTheLaw(businessId);
                runtime.NotePoliceAttentionAt(businessId, StatementAttention);
            }
            CrewOverlay.AnnounceOurs(call.Call.Faction, "A STATEMENT WAS TAKEN", 4f,
                new Color(1f, 0.85f, 0.55f));
            LawWire.StatementTaken(call.Call);
            if (close) Close(call);
        }

        /// <summary>
        /// DONE HERE. The men a car put out have to be back IN it before the car is
        /// released, exactly as a squad leaving a shooting does (TickSquad's Leaving) -
        /// released first, the cruiser drives off with its officers still walking to the
        /// door, and the pair are lost to the road for good.
        ///
        /// A beat officer has nothing to climb into and goes back on his beat at once.
        /// </summary>
        void Close(CallOut call)
        {
            // CLOSED IS CLOSED. A shop visit that outlives its call - the statement stage
            // gave up on it and took the statement itself - still answers its callbacks
            // afterwards, and a second Release here would hand back a unit the dispatcher
            // has since sent somewhere else, taking the officer off that call instead.
            if (call == null || call.Stage == CallStage.Done)
                return;
            // A LEG HAS TO BE ENDED BY WHOEVER STARTED IT. An officer left in the
            // doorway walk is driven by hand and answers nothing else - Release refuses
            // him outright - so a call that gives up mid-leg must hand him back first or
            // the man stands in a shop doorway for the rest of the campaign.
            if (call.Unit is PoliceBeat walking &&
                walking.State == PoliceBeat.Mode.Doorway)
                walking.EndDoorway();
            if (call.Men != null && !call.Men.Wiped && call.Unit is PoliceCruiser cruiser)
            {
                _crews.BoardCar(call.Men, cruiser.Car);
                call.Stage = CallStage.Boarding;
                call.HomeBy = Time.time + BoardingPatience;
                return;
            }
            if (call.Men != null && !call.Men.Wiped)
                _crews.RemoveUnit(call.Men);
            call.Men = null;
            Release(call);
        }

        /// <summary>Seconds a car waits for its own men before it goes without them.
        /// The same backstop the collar has, and for the same reason.</summary>
        const float BoardingPatience = 45f;

        void TickBoarding(CallOut call)
        {
            var cruiser = call.Unit as PoliceCruiser;
            if (cruiser == null || call.Men == null || call.Men.Wiped)
            {
                call.Men = null;
                Release(call);
                return;
            }
            if (call.Men.Car == cruiser.Car || Time.time > call.HomeBy)
            {
                call.Men = null;
                Release(call);
            }
        }

        /// <summary>The unit goes back to whatever it was doing, lights off.</summary>
        void Release(CallOut call)
        {
            if (call.Unit != null) HandBack(call.Unit);
            if (call.Backup != null) { HandBack(call.Backup); call.Backup = null; }
            call.Stage = CallStage.Done;
        }

        /// <summary>Back to its beat or its round, lights off.</summary>
        void HandBack(IPoliceUnit unit)
        {
            if (_lights.TryGetValue(unit, out var lights)) lights.Set(false, siren: false);
            unit.Release();
        }

        /// <summary>
        /// The docket entry. The complainant is on it by name whether or not anybody is
        /// arrested - he is what a complaint IS - and everybody on the pavement who
        /// reacted to what they saw goes on beside him, snapshotted once.
        /// </summary>
        CourtCase OpenComplaintCase(CallOut call)
        {
            var pipeline = Force != null ? Force.Pipeline : null;
            if (pipeline == null) return null;

            var today = Today();
            var file = pipeline.OpenCase(call.Call.Charge, call.Call.Faction, today,
                today > 0 ? today + Sentencing.DaysToCourt : 0,
                call.Call.BusinessId, call.Call.Where);

            file.Witnesses.Add(new Witness
            {
                Kind = WitnessKind.Complainant,
                Name = ComplainantName(call.Call),
                Seed = ComplaintRoll.StreamFor(0, call.Call.BusinessId, 0, 0),
                BusinessId = call.Call.BusinessId,
                X = call.Call.Pos.x, Y = call.Call.Pos.y, Z = call.Call.Pos.z,
            });

            CopySceneWitnesses(file, call.Witnesses);

            LawWire.CaseOpened(file);
            return file;
        }

        /// <summary>The man behind the counter, named off his shop. A business the
        /// directory knows has an owner's name on it; anything else is "the owner of
        /// [shop]", which is what a charge sheet says when the clerk has the trade and
        /// not the man.</summary>
        static string ComplainantName(StreetAlarm.Complaint call)
        {
            var business = LivingCity.Business.BusinessRuntime.Instance;
            if (business != null && business.Populated &&
                !string.IsNullOrEmpty(call.BusinessId) &&
                business.Directory.TryGet(new TerritoryBusinessId(call.BusinessId),
                    out var record) &&
                business.Directory.TryGetOwner(record.OwnerId, out var owner) &&
                !string.IsNullOrEmpty(owner.DisplayName))
                return owner.DisplayName;
            return string.IsNullOrEmpty(call.Where)
                ? "The shopkeeper" : "The owner of " + call.Where;
        }

        /// <summary>The campaign day, or 0 in a scene with no campaign behind it.</summary>
        static int Today()
        {
            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            return outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
        }
    }
}
