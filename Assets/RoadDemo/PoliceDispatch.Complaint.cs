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
    /// <see cref="ComplaintDelay"/> seconds after the threat, which is the head start
    /// the crew is given. A crew that leans on a shop and walks away has almost always
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
        const float ComplaintDelayLow = 20f;
        const float ComplaintDelayHigh = 40f;

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

        enum CallStage { Ringing, Walking, AtTheDoor, Statement, Arresting, Boarding, Done }

        sealed class CallOut
        {
            public StreetAlarm.Complaint Call;
            public float RingAt;
            public float GiveUpAt;
            public IPoliceUnit Unit;
            public DemoCrews.Unit Men;      // the men a CAR brought, when a car answered
            public CallStage Stage = CallStage.Ringing;
            public float StatementBy;
            public CourtCase File;

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
            _sceneWitnesses.Clear();
            CivilianAgent.SnapshotWitnesses(where, CivilianAgent.SightRadius,
                CivilianAgent.MaxEyewitnesses, _sawIt);
            for (var i = 0; i < _sawIt.Count; i++)
            {
                var saw = _sawIt[i];
                if (saw == null || saw.Tf == null) continue;
                _sceneWitnesses.Add(new SceneWitness
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
            for (var i = 0; i < _sceneWitnesses.Count; i++)
            {
                var seen = _sceneWitnesses[i];
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
            // The act a complaint is about is the LEAN, and it happened just now - so
            // the pavement is read now, not when the officer finally walks up half a
            // minute later and finds a different set of people standing there.
            SnapshotTheScene(call.Pos);
            _calls.Add(new CallOut
            {
                Call = call,
                RingAt = Time.time + wait,
                GiveUpAt = Time.time + wait + ComplaintPatience,
            });
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
                        if (Time.time > call.GiveUpAt) call.Stage = CallStage.Done;
                        break;

                    case CallStage.Walking: TickWalking(call); break;
                    case CallStage.AtTheDoor: AtTheDoor(call); break;
                    case CallStage.Statement:
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
                        Close(call);
                        break;

                    case CallStage.Boarding: TickBoarding(call); break;
                }
                if (call.Stage == CallStage.Done) _calls.RemoveAt(i);
            }
        }

        /// <summary>
        /// ONE UNIT, and the nearest one that is free. A beat officer for choice - a man
        /// on foot is what a telephone call about a shopkeeper actually gets - and a car
        /// only when there is no beat to send. Nothing city-wide: the ordinary response
        /// range, so a complaint from the far side of the island reaches nobody.
        /// </summary>
        bool SendToDoor(CallOut call)
        {
            var unit = Nearest(call.Call.Pos, carries: false) ??
                       Nearest(call.Call.Pos, carries: true);
            if (unit == null) return false;

            unit.RouteTo(call.Call.Pos, 5f);
            call.Unit = unit;
            call.Stage = CallStage.Walking;
            // NO SIREN. Lights and a siren for a complaint would empty the street the
            // officer was sent to look at, and the whole point of the call is that he
            // arrives to find men standing in a doorway.
            if (_lights.TryGetValue(unit, out var lights)) lights.Set(true, siren: false);
            CrewOverlay.AnnounceOurs(call.Call.Faction, "A MAN OF THE LAW AT THE DOOR", 4f,
                new Color(0.55f, 0.78f, 1f));
            return true;
        }

        void TickWalking(CallOut call)
        {
            var unit = call.Unit;
            if (unit == null || unit.Tf == null) { Close(call); return; }
            if (Time.time > call.GiveUpAt) { Close(call); return; }
            if (!unit.OnScene &&
                (unit.Position - call.Call.Pos).sqrMagnitude > ComplaintReach * ComplaintReach)
                return;

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
                        1, aboardOf: null);
                }
                if (call.Men == null) { Close(call); return; }
            }

            call.Stage = CallStage.AtTheDoor;
        }

        /// <summary>
        /// HE IS AT THE DOOR. The case is opened here whichever way it goes - the
        /// shopkeeper who rang is its first witness, and whoever was on the pavement
        /// when the officer walked up is snapshotted with him, once and for good.
        /// </summary>
        void AtTheDoor(CallOut call)
        {
            call.File = OpenComplaintCase(call);

            if (TryComplaintCollar(call))
            {
                // The EPIC 17 window has the CREW from here; the call keeps the UNIT,
                // because nothing in that window knows how to send a police car home.
                call.Stage = CallStage.Arresting;
                call.HomeBy = Time.time + CollarPatience + AskSeconds + TakeSeconds;
                return;
            }

            call.Stage = CallStage.Statement;
            call.StatementBy = Time.time + StatementSeconds;
        }

        /// <summary>Nobody to speak to but the man behind the counter. The shop is out
        /// of the racket's reach for the rest of the day, the block has been looked at,
        /// and the complaint sits on the docket waiting for the next time these men are
        /// taken for anything.</summary>
        void StatementTaken(CallOut call)
        {
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
            Close(call);
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
            if (call.Unit != null)
            {
                if (_lights.TryGetValue(call.Unit, out var lights)) lights.Set(false, siren: false);
                call.Unit.Release();
            }
            call.Stage = CallStage.Done;
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

            CopySceneWitnesses(file, call.Call.Number);

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
