using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.Police;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// THE ARREST. What the beat is FOR, and what it had been missing: an officer who
    /// answers a shooting, walks up to the men who did it and stands there is not the
    /// law, he is scenery. ("Policija nije došla da proba da nas uhapsi nego su samo
    /// stali tu.")
    ///
    /// So: once the shooting has stopped, whoever is holding the scene - the beat man
    /// on foot, or the lead of the squad that got out of a car - picks out the crew
    /// that did it, walks over with his sidearm out, and puts the question.
    ///
    /// WHO ANSWERS IT (changed for EPIC 17). It used to be the player, on two keys:
    /// Y went quietly, N refused, and silence was a refusal. That made an arrest a
    /// menu, and it made every lieutenant in the city the same man - a coward and a
    /// hothead answered identically, because the answer was never theirs. The men
    /// answer now (SurrenderRoll: the commanding lieutenant's nerve, the temper of the
    /// men behind him, and what they think of the outfit that would have to get them
    /// out), and the player's one intervention is an ORDINARY ORDER: an explicit attack
    /// on the law while the question stands forces the fight, whatever the roll said.
    /// Once their hands are up he has no orders left to give them - a man covered at
    /// gunpoint does not take instructions (DemoCrews.OrderRefusal).
    ///
    /// The window itself is on the screen the whole time it is open (ArrestHud), which
    /// is what tells the player to intervene now or lose them for the sentence.
    ///
    /// The officer's half of this - the walk over, the piece in his fist, the stance -
    /// is PoliceBeat.Challenge for a beat pair and a plain walk order with the gun
    /// up for a squad's lead; the crew's half is DemoCrews.GiveUp / TakeIn.
    /// </summary>
    public sealed partial class PoliceDispatch
    {
        enum Collar { None, WalkingUp, Asking, Taking }

        /// <summary>Metres an officer will cross to make an arrest.
        ///
        /// Measured, not guessed: he is routed to the pavement CORNER nearest the
        /// shooting (RouteTo), and the men who did it are stood wherever their guns
        /// reached from - a rifle is good for 26 m - so the gap between the two is
        /// routinely forty metres with nobody having done anything wrong. At 35 m two
        /// real fights in a row ended with an officer stood at the corner and the crew
        /// that shot the place up walking off unasked; the whole complaint this was
        /// built for ("samo su stali tu"). He crosses the street for them instead.</summary>
        const float ArrestReach = 45f;

        /// <summary>Seconds the question stands before the men answer it. This is the
        /// PLAYER'S window and nothing else: the answer was settled the moment the
        /// officer set off, and these are the seconds he has to overrule it.</summary>
        const float AskSeconds = 8f;

        /// <summary>Seconds between one telling of it and the next, so a player who
        /// looked away still sees what is being asked.</summary>
        const float AskAgain = 5f;

        /// <summary>Seconds the men are led away in before the books close on them.</summary>
        const float TakeSeconds = 4f;

        /// <summary>Seconds of quiet before an arrest is even thought about. A man does
        /// not put his gun away and start taking names with rounds still in the air.</summary>
        const float QuietBefore = 3f;

        /// <summary>Seconds after the last shot that an arrest can still be made over it.
        /// Deliberately longer than StreetAlarm's own incident window (45 s): the officer
        /// has to RUN there first, and at a jog the far side of a response range is a
        /// minute of running. Judging the arrest by whether the incident was still open
        /// meant the man who ran furthest arrived to find he had no reason to be there.</summary>
        const float ArrestWindow = 150f;

        /// <summary>Metres the crew may drift from the officer before the arrest is off:
        /// men who walk away while the question stands have answered it.</summary>
        const float WalksOff = 22f;

        /// <summary>Where a squad's lead stops in front of the man he is asking - the
        /// same arm's length the beat man's own walk-up uses.</summary>
        const float SquadGap = 3.2f;

        /// <summary>The backstop on the whole collar. An officer who cannot reach his man
        /// - a torn pavement, a body wedged in a doorway, a scene he was routed round -
        /// must not leave the dispatcher holding a question nobody is going to answer.</summary>
        const float CollarPatience = 45f;

        /// <summary>How often the man holding the gun is reminded there is something to
        /// hold it for. The concealment rule (CrewWalker.WantsGunOut) keeps a piece out
        /// while a man is ALERT and puts it away when he is not, so the arrest refreshes
        /// the gunpoint target itself keeps the weapon drawn; this cadence only refreshes
        /// which prisoner a changing squad is covering.</summary>
        const float GunRefresh = 6f;

        /// <summary>A PAPER SCREEN STOPS THE CLOCK. The ledger and the strategic map are
        /// pages: the player behind one can neither see the banner nor give the order
        /// that would overrule the roll, so the seconds are handed back rather than run
        /// down. The turf map is deliberately NOT on this list any more - orders are
        /// given from it (TurfMapHud), so a player watching an arrest from the map is as
        /// able to intervene as one watching it from the street.</summary>
        static bool Blocked => LivingCity.UI.ModalGate.Blocked;

        Collar _collar = Collar.None;
        Deed _arrestDeed = Deed.Affray;    // what they are being taken for
        CourtCase _arrestCase;             // the docket entry, when there is a city

        /// <summary>Whether the collar OPENED that case. A shooting's file is opened by
        /// the collar and is worth nothing if nobody is taken; a complaint's file was
        /// opened by the telephone call and outlives a failed arrest, because a
        /// complaint nobody was taken for is exactly what becomes a count later.</summary>
        bool _arrestCaseIsOurs;

        /// <summary>The telephone call this collar was begun for, or null when it came
        /// out of a shooting. A complaint OWNS its unit from the moment it is sent until
        /// the door has been answered, so the collar must not hand that officer back to
        /// his beat behind the call's back.</summary>
        CallOut _arrestCall;
        PoliceBeat _arrestOfficer;         // the beat pair, when it is one
        CrewWalker _arrestLawman;          // a squad's lead, when the car brought him
        DemoCrews.Unit _arrestSquad;       // that lead's squad
        DemoCrews.Unit _arrestCrew;
        CrewWalker _arrestCollar;          // the man of the crew being spoken to
        float _askUntil, _sayAgainAt, _takeAt, _gunAt, _collarBy, _collarAt;
        int _askedIncident = -1;
        float _refusalOdds, _secondFightOdds;
        bool _answerArmed;
        DoorAnswer _answer;
        ArrestHud _hud;

        static readonly List<CrewWalker> _shotBy = new List<CrewWalker>();

        void TickArrest(float dt)
        {
            switch (_collar)
            {
                case Collar.None:
                    LookForACollar();
                    return;

                case Collar.WalkingUp:
                    if (Blocked)
                    {
                        _collarAt += dt;
                        _collarBy += dt;
                        Banner();
                        return;
                    }
                    if (PlayerSaysFight()) { Fight(ordered: true); return; }
                    if (PlayerSaysRun()) { Run(ordered: true); return; }
                    if (ArrestOff()) return;
                    KeepGunUp();
                    Banner();
                    if (!StoodOver()) return;
                    if (_answer == DoorAnswer.Run) { Run(ordered: false); return; }
                    _collar = Collar.Asking;
                    _askUntil = Mathf.Max(Time.time, _collarAt + AskSeconds);
                    _sayAgainAt = 0f;
                    return;

                case Collar.Asking:
                {
                    if (PlayerSaysFight()) { Fight(ordered: true); return; }
                    if (PlayerSaysRun()) { Run(ordered: true); return; }
                    if (Blocked)
                    {
                        _askUntil += dt;
                        _sayAgainAt = 0f;   // said again the moment the page is closed
                        return;
                    }
                    if (ArrestOff()) return;
                    KeepGunUp();
                    Banner();
                    if (Time.time >= _sayAgainAt)
                    {
                        _sayAgainAt = Time.time + AskAgain;
                        AnnounceArrest(Question, AskAgain,
                            new Color(0.55f, 0.78f, 1f));
                    }
                    if (Time.time < _askUntil) return;
                    // GiveUp returns false for a crew whose hands are already up. That
                    // is still a quiet arrest, never evidence that prisoners opened fire.
                    if (_arrestCrew != null &&
                        (_arrestCrew.InCustody || _arrestCrew.Surrendered))
                    {
                        if (_arrestCrew.InCustody)
                        {
                            EndChallenge(holster: true);
                            Clear(preserveCase: true);
                            return;
                        }
                        _collar = Collar.Taking;
                        _takeAt = Time.time;
                        return;
                    }
                    if (_answer == DoorAnswer.Fight) { Fight(ordered: false); return; }
                    if (_crews.GiveUp(_arrestCrew))
                    {
                        _collar = Collar.Taking;
                        _takeAt = Time.time;
                        return;
                    }
                    Drop();
                    return;
                }

                case Collar.Taking:
                {
                    // the crew being wiped out mid-arrest is not an arrest, and the books
                    // are not told about men who were shot where they stood
                    if (_arrestCrew == null || _arrestCrew.Wiped) { Drop(); return; }
                    if (Time.time < _takeAt) return;
                    // A quiet answer is not a booking. Keep it on the physical crew,
                    // but do not put names on the docket until the station threshold.
                    var answer = RememberAnswer(DoorAnswer.Quiet, _arrestDeed,
                        attachDefendants: false);
                    // The question is over, but the arresting officers stay over the
                    // hands-up crew with their pieces trained on them. Custody takes
                    // ownership of that cover and releases it only after boarding.
                    BeginCustody(_arrestCrew, _arrestDeed, _arrestCase,
                        _arrestCall, _arrestOfficer, _arrestSquad,
                        answer);
                    Clear(preserveCase: true);
                    return;
                }
            }
        }

        /// <summary>Whether a responder's scene is this incident's: within an arrest's
        /// reach of where the shooting is, which drifts with the later shots.</summary>
        static bool AtThisScene(Vector3 scene, Vector3 incident) =>
            LivingCity.Police.PoliceProcedure.AirDistanceSquared(
                scene.x, scene.z, incident.x, incident.z) <= ArrestReach * ArrestReach;

        /// <summary>What the officer says, once the gun is out and he is stood over his
        /// man. No keys after it any more: the men answer it themselves.</summary>
        const string Question = "\"POLICE! HANDS UP - YOU'RE UNDER ARREST\"";

        /// <summary>An officer stood at a quiet scene, and the men who made it stood in
        /// front of him. One arrest per incident: a crew that talked its way out of one
        /// is not asked again over the same bodies.</summary>
        void LookForACollar()
        {
            if (_crews == null || StreetAlarm.QuietFor < QuietBefore) return;
            if (StreetAlarm.QuietFor > ArrestWindow) return;
            if (StreetAlarm.IncidentNumber == _askedIncident) return;

            // WHOEVER GOT THERE FIRST PUTS THE QUESTION (the user's rule, 2026-09-04).
            // The pair and the car both come to a scene now, and the one that has been
            // stood at it longer makes the arrest - not the pair by preference.
            // And only the law stood at THIS scene. A pair or a squad still holding an
            // older scene across town - the scene hold outlives the incident gap - is
            // not a candidate, or it would win the question on its earlier arrival and
            // then find nobody to put it to.
            var here = StreetAlarm.Incident;
            PoliceBeat foot = null;
            var footAt = float.MaxValue;
            foreach (var u in _units)
                if (!u.Carries && u.OnScene && u is PoliceBeat beat && beat.Tf != null &&
                    AtThisScene(beat.Scene, here) && beat.ArrivedAt < footAt)
                { foot = beat; footAt = beat.ArrivedAt; }

            // CONF-001: THE CAR PUTS THE SAME QUESTION. A squad that drove to a
            // shooting, got out and taped the scene off used to stand at it saying
            // nothing, so an arrest only ever happened where a beat officer happened
            // to be walking. Its lead crosses the street exactly as the beat man
            // does - the only difference is which body it is.
            CrewWalker lawman = null;
            DemoCrews.Unit squadMen = null;
            var squadAt = float.MaxValue;
            foreach (var squad in _squads)
            {
                if (squad.State != SquadState.Securing) continue;
                if (!AtThisScene(squad.Scene, here)) continue;
                var lead = Lead(squad);
                if (lead == null || lead.Tf == null) continue;
                if (squad.SecuringAt >= squadAt) continue;
                lawman = lead;
                squadMen = squad.Men;
                squadAt = squad.SecuringAt;
            }
            if (foot != null && lawman != null)
            {
                if (LivingCity.Police.PoliceProcedure.FootArrivedFirst(footAt, squadAt))
                { lawman = null; squadMen = null; }
                else foot = null;
            }
            if (foot == null && lawman == null) return;
            var from = foot != null ? foot.Tf.position : lawman.Tf.position;

            var crew = GuiltyNear(from);
            if (crew == null || crew.InCustody || crew.Surrendered) return;

            var man = crew.Boss != null && !crew.Boss.Dead
                ? crew.Boss : DemoCrews.NearestOf(crew, from);
            if (man == null || man.Tf == null) return;

            _askedIncident = StreetAlarm.IncidentNumber;
            _arrestDeed = TheDeed();
            _arrestCase = OpenShootingCase(crew);
            if (_arrestCase != null) _arrestDeed = _arrestCase.Deed;
            _arrestCaseIsOurs = true;
            _arrestOfficer = foot;
            _arrestLawman = lawman;
            _arrestSquad = squadMen;
            _arrestCrew = crew;
            _arrestCollar = man;
            _collar = Collar.WalkingUp;
            _collarAt = Time.time;
            _collarBy = Time.time + CollarPatience;

            // CONF-002: THE ANSWER IS ROLLED HERE, not when the question is finally put.
            // It is a fact about the men - the nerve of whoever is commanding them, the
            // temper of the men behind him - and not about the second the officer
            // arrives, so it can be READ while he walks: the banner has to tell the
            // player which way this is going while he still has time to change it. The
            // DECISION still lands at the end of the ask window, which is the window.
            RollAnswer(crew, StreetAlarm.IncidentNumber);

            if (foot != null) foot.Challenge(man);
            else BeginSquadChallenge(man);
            Banner();
            AnnounceArrest("AN OFFICER IS WALKING OVER", 3.5f,
                new Color(0.55f, 0.78f, 1f));
        }

        /// <summary>
        /// A COMPLAINT AT THE DOOR (GAN-245). The officer walked up to a shop somebody
        /// rang about and there are men of the accused family still standing in it.
        ///
        /// Nothing new happens from here: the whole EPIC 17 window opens exactly as it
        /// does after a shooting - the walk-up, the surrender roll, ARREST IN PROGRESS,
        /// the player's attack order, the FLEE - and the only differences are what
        /// opened it and what they are charged with.
        ///
        /// False when there is nobody to speak to, which is the ordinary case and the
        /// one that ends in a statement instead.
        /// </summary>
        bool TryComplaintCollar(CallOut call)
        {
            if (_collar != Collar.None || _crews == null || call == null) return false;

            PoliceBeat foot = call.Unit as PoliceBeat;
            CrewWalker lawman = null;
            if (foot == null)
            {
                lawman = call.Men != null && !call.Men.Wiped ? Lead(call.Men) : null;
                if (lawman == null || lawman.Tf == null) return false;
            }
            var from = foot != null && foot.Tf != null ? foot.Tf.position
                     : lawman != null && lawman.Tf != null ? lawman.Tf.position
                     : call.Call.Pos;

            // HE HAS TO BE ON THE DOORSTEP HIMSELF. Arriving is measured against the
            // pavement graph, and the corner nearest a shop can be the length of a block
            // from it; the walk-up is a hand-driven straight line and cannot cross that.
            // The call says whether he actually got there (CallOut.AtTheDoorstep) - a
            // clock running out is not an arrival - and a man who did not is a man who
            // takes a statement, not one who puts anybody's hands up.
            if (!call.AtTheDoorstep) return false;

            var crew = AccusedNear(call.Call.Pos, call.Call.Faction);
            if (crew == null || crew.InCustody || crew.Surrendered) return false;
            call.Accused = crew;

            var man = crew.Boss != null && !crew.Boss.Dead
                ? crew.Boss : DemoCrews.NearestOf(crew, from);
            if (man == null || man.Tf == null) return false;

            // AND HIS MAN HAS TO BE A WALK AWAY. The crew is found within the whole
            // reach of the call, but the walk-up itself is a few metres of hand-lerped
            // pavement (PoliceBeat.Challenge). Started from further off it walks
            // through whatever stands between them and ends in the patience running out.
            if ((man.Tf.position - from).sqrMagnitude > WalksOff * WalksOff) return false;

            // Finding the accused at the actual doorstep is the first honest point at
            // which this telephone call can become a case. If nobody is here, the file
            // waits for a physically completed shop interview instead.
            call.File ??= OpenComplaintCase(call);

            _askedIncident = call.Call.Number;
            _arrestDeed = call.Call.Charge;
            _arrestCase = call.File;
            _arrestCaseIsOurs = false;   // the telephone opened it, not this collar
            _arrestCall = call;
            _arrestOfficer = foot;
            _arrestLawman = lawman;
            _arrestSquad = call.Men;
            _arrestCrew = crew;
            _arrestCollar = man;
            _collar = Collar.WalkingUp;
            _collarAt = Time.time;
            _collarBy = Time.time + CollarPatience;

            RollAnswer(crew, call.Call.Number);

            if (foot != null) foot.Challenge(man);
            else BeginSquadChallenge(man);
            Banner();
            AnnounceArrest("AN OFFICER IS WALKING OVER", 3.5f,
                new Color(0.55f, 0.78f, 1f));
            return true;
        }

        /// <summary>The men of the complained-of family standing at the door, if any -
        /// the nearest crew of that faction inside <see cref="ComplaintReach"/>. Nobody
        /// is taken for a complaint about somebody else's family.</summary>
        DemoCrews.Unit AccusedNear(Vector3 door, int faction)
        {
            DemoCrews.Unit best = null;
            float bestD = ComplaintReach * ComplaintReach;
            foreach (var unit in _crews.Units)
            {
                if (unit == null || unit.IsPolice || unit.Wiped ||
                    unit.InCustody || unit.Surrendered) continue;
                if (unit.Faction != faction) continue;
                if (unit.Retreated || unit.Car != null) continue;   // gone, or driving off
                float d = (unit.Position - door).sqrMagnitude;
                if (d > bestD) continue;
                bestD = d;
                best = unit;
            }
            return best;
        }

        /// <summary>
        /// The docket entry for an arrest made over a SHOOTING. Its witnesses are the
        /// people who were on the pavement when the incident opened plus whatever the
        /// law itself saw: an officer who watched the act is the strongest thing on a
        /// charge sheet, and the man who merely found them at the scene is the weakest.
        ///
        /// Null in a scene with no city behind it, which is what makes a crew-demo
        /// arrest the old flat conviction it always was.
        /// </summary>
        CourtCase OpenShootingCase(DemoCrews.Unit crew)
        {
            var pipeline = Force != null ? Force.Pipeline : null;
            if (pipeline == null || crew == null) return null;
            if (crew.ArrestCase != null && crew.ArrestCase.Status == CaseStatus.Open)
                return crew.ArrestCase;

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            var today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            var reusedBodyFile = _arrestDeed == Deed.Murder &&
                                 _civilianDeathIncident == StreetAlarm.IncidentNumber &&
                                 _civilianDeathCase != null &&
                                 _civilianDeathCase.Status == CaseStatus.Open &&
                                 _civilianDeathCase.GangId == crew.Faction;
            var file = reusedBodyFile
                ? _civilianDeathCase
                : pipeline.OpenCase(_arrestDeed, crew.Faction, today,
                    today > 0 ? today + Sentencing.DaysToCourt : 0);

            // THE PEOPLE WHO WERE THERE WHEN IT HAPPENED - taken when the first round
            // went off (SnapshotTheScene), not now. An arrest can be made a hundred and
            // fifty seconds after the shooting stopped, by which time the men who saw it
            // have walked home and a fresh crowd has drifted over to look at the chalk.
            if (!reusedBodyFile)
                CopySceneWitnesses(file, StreetAlarm.IncidentNumber);

            // WHAT THE LAW ITSELF SAW, recorded while the squad was actually looking at
            // it (BeginWarning / PickFight). Read at arrest time it was always false:
            // the squad that makes the arrest is by definition Securing a quiet street
            // by then.
            var sawTheAct = _lawSawIncident == StreetAlarm.IncidentNumber;
            var officerKind = sawTheAct
                ? WitnessKind.PoliceSawIt : WitnessKind.PoliceFoundThem;
            if (!file.Has(officerKind))
                file.Witnesses.Add(new Witness
                {
                    Kind = officerKind,
                    Name = "The arresting officer",
                    Seed = StreetAlarm.IncidentNumber,
                    X = StreetAlarm.Incident.x, Y = StreetAlarm.Incident.y,
                    Z = StreetAlarm.Incident.z,
                });

            if (!reusedBodyFile)
                LawWire.CaseOpened(file);
            return file;
        }

        /// <summary>The squad's lead walks over with the piece out - the same arm's
        /// length, the same stance, no second mechanism.</summary>
        void BeginSquadChallenge(CrewWalker man)
        {
            var law = _arrestLawman;
            if (law == null || law.Tf == null || man == null || man.Tf == null) return;
            var back = law.Tf.position - man.Tf.position;
            back.y = 0f;
            back = back.sqrMagnitude > 0.04f ? back.normalized : law.Tf.forward;
            var side = Vector3.Cross(Vector3.up, back);
            var index = 0;
            var squad = _arrestSquad;
            if (squad != null)
                foreach (var officer in squad.All())
                {
                    if (officer == null || officer.Dead || officer.Tf == null) continue;
                    officer.Disengage();
                    var standAt = man.Tf.position + back * (SquadGap + index * 0.8f) +
                                  side * (index == 0 ? -1f : 1f) * 1.2f;
                    standAt.y = officer.Tf.position.y;
                    if (!officer.OrderAcross(standAt, index * 0.12f))
                        officer.OrderToPoint(standAt, index * 0.12f);
                    officer.HoldAtGunpoint(man);
                    index++;
                }
            else
            {
                law.Disengage();
                var standAt = man.Tf.position + back * SquadGap;
                standAt.y = law.Tf.position.y;
                if (!law.OrderAcross(standAt)) law.OrderToPoint(standAt);
                law.HoldAtGunpoint(man);
            }
            _gunAt = 0f;
            KeepGunUp();
        }

        /// <summary>The gun stays out for as long as the question stands. Nothing new:
        /// the man is told there is shooting where his collar is stood, and the
        /// concealment rule does the rest.</summary>
        void KeepGunUp()
        {
            if (_arrestOfficer != null && _arrestCollar != null)
            {
                _arrestOfficer.HoldAtGunpoint(_arrestCollar);
                return;
            }
            if (_arrestLawman == null || _arrestLawman.Dead || _arrestLawman.Tf == null) return;
            if (_arrestCollar == null || _arrestCollar.Tf == null) return;
            if (Time.time < _gunAt) return;
            _gunAt = Time.time + GunRefresh;
            if (_arrestSquad != null)
                foreach (var officer in _arrestSquad.All())
                    if (officer != null && !officer.Dead)
                        officer.HoldAtGunpoint(_arrestCollar);
            else
                _arrestLawman.HoldAtGunpoint(_arrestCollar);
        }

        /// <summary>He is stood over his man with the gun out - the question can be put
        /// to him now, and not a step before.</summary>
        bool StoodOver()
        {
            if (_arrestOfficer != null) return _arrestOfficer.StoodOver;
            if (_arrestLawman == null || _arrestLawman.Tf == null) return false;
            if (_arrestCollar == null || _arrestCollar.Tf == null) return false;
            float d = Vector3.Distance(_arrestLawman.Tf.position, _arrestCollar.Tf.position);
            return d <= SquadGap + 1.4f || !_arrestLawman.HasOrder;
        }

        /// <summary>CONF-003: the player's ONE word in this, and it is an ordinary order
        /// rather than a key of its own - the crew was told to shoot at the law while the
        /// question stood. Anything he ordered BEFORE the officer set off is not an
        /// answer to a question nobody had asked yet.</summary>
        bool PlayerSaysFight() =>
            _arrestCrew != null && _arrestCrew.PoliceFightOrderedAt >= _collarAt;

        bool PlayerSaysRun() =>
            _arrestCrew != null &&
            _arrestCrew.Faction == LivingCity.Gameplay.PlayerCommands.House.Value &&
            _arrestCrew.OrderedAt >= _collarAt &&
            !PlayerSaysFight();

        /// <summary>ARREST IN PROGRESS, for as long as it is. Pushed every frame; the
        /// banner only touches its labels when the words have actually changed.</summary>
        void Banner()
        {
            if (_arrestCrew == null ||
                _arrestCrew.Faction != LivingCity.Gangs.GangCatalog.PlayerGangId)
            {
                ClearBanner();
                return;
            }
            if (_hud == null) _hud = ArrestHud.For(gameObject);
            if (_hud == null) return;
            _hud.Show(_arrestCrew.GangName,
                SurrenderRoll.Leaning(_refusalOdds, _secondFightOdds, _answerArmed));
        }

        /// <summary>The arrest state machine runs for every family; its toast is the
        /// player's intervention window and therefore only speaks for his crew.</summary>
        void AnnounceArrest(string text, float seconds, Color tint)
        {
            if (_arrestCrew == null)
                return;
            CrewOverlay.AnnounceOurs(_arrestCrew.Faction, text, seconds, tint);
        }

        void ClearBanner()
        {
            if (_hud != null) _hud.Clear();
        }

        /// <summary>
        /// CONF-002: what the men are likely to do, off the men themselves.
        ///
        /// The lieutenant answers for the crew when he is stood there; when he is not -
        /// jailed, dead, or simply somewhere else - the senior man present answers, and
        /// senior means the best commander of the ones who are actually there. EVERY
        /// family's men are read off their own family's book; only a body on nobody's
        /// books at all - the law, a bench scene's mob - stands at the middle of the
        /// band.
        /// </summary>
        void RollAnswer(DemoCrews.Unit crew, int incident)
        {
            AnswerOdds(crew, out _refusalOdds, out _secondFightOdds, out _answerArmed);
            _answer = SurrenderRoll.Answer(_refusalOdds, _secondFightOdds, _answerArmed,
                SurrenderRoll.StreamFor(_crews.CitySeed, CrewKey(crew), incident));
        }

        void AnswerOdds(DemoCrews.Unit crew, out float refusal, out float fight,
            out bool armed)
        {
            refusal = 0.5f;
            fight = 0.5f;
            armed = false;
            var underworld = LivingCity.Outfit.Underworld.Current;
            var roster = crew != null && underworld != null
                ? underworld.Of(crew.Faction)?.Roster : null;
            if (crew == null || roster == null)
            {
                if (crew != null)
                    foreach (var man in crew.All())
                        if (man != null && !man.Dead && man.Carrying) armed = true;
                return;
            }

            int temper = 0, loyalty = 0, counted = 0, bestLead = -1;
            Character senior = null, lieutenant = null;
            foreach (var man in crew.All())
            {
                if (man == null || man.Dead || man.CharacterId < 0) continue;
                var member = roster.Find(man.CharacterId);
                if (member == null) continue;
                if (man.Carrying) armed = true;
                temper += member.Temper;
                loyalty += member.Loyalty;
                counted++;
                if (member.Id == crew.CommandParentId) lieutenant = member;
                var lead = AttributeScale.ValueOf(
                    member.GetHalfSteps(CharacterAttribute.Leadership));
                if (lead > bestLead) { bestLead = lead; senior = member; }
            }
            if (counted == 0)
                return;

            var speaker = lieutenant ?? senior;
            refusal = SurrenderRoll.FightChance(
                speaker != null ? speaker.Courage : SurrenderRoll.NoBooks,
                temper / counted, loyalty / counted);
            fight = SurrenderRoll.FightAfterRefusal(
                speaker != null ? speaker.Temper : SurrenderRoll.NoBooks,
                speaker != null ? speaker.Courage : SurrenderRoll.NoBooks,
                speaker != null ? speaker.Discipline : SurrenderRoll.NoBooks);
        }

        /// <summary>The stream key of one crew: its branch on the books when it has one,
        /// and otherwise the runtime token every man of it shares.</summary>
        static int CrewKey(DemoCrews.Unit crew) =>
            crew == null ? 0 : crew.CrewId != 0 ? crew.CrewId : crew.CrowdGroupId;

        /// <summary>Whose shooting this was: of the crews that fired at this incident,
        /// the NEAREST one still standing within reach, whoever's it is. The outfit used
        /// to be preferred by a thousand metres of score, which meant a Falcone crew
        /// shooting up a street in front of a patrol was taken in only when nobody of
        /// ours was anywhere near it. The law arrests who it can reach.</summary>
        DemoCrews.Unit GuiltyNear(Vector3 from)
        {
            _shotBy.Clear();
            StreetAlarm.ShootersSince(ArrestWindow, _shotBy);
            DemoCrews.Unit best = null;
            float bestScore = float.MaxValue;
            foreach (var man in _shotBy)
            {
                if (man == null || man.Dead || man.Tf == null) continue;
                var unit = _crews.UnitOf(man);
                if (unit == null || unit.IsPolice || unit.Wiped ||
                    unit.InCustody || unit.Surrendered) continue;
                if (unit.Retreated || unit.Car != null) continue;   // gone, or driving off
                float d = Vector3.Distance(unit.Position, from);
                if (d > ArrestReach) continue;
                if (d < bestScore) { bestScore = d; best = unit; }
            }
            return best;
        }

        /// <summary>What they are being taken for, off the incident's own tally. Not
        /// decoration: the deed decides the charge on the rap sheet AND how long they
        /// get (Sentencing) - guns in the street and a dead policeman used to cost the
        /// outfit exactly the same three days.</summary>
        static Deed TheDeed()
        {
            if (StreetAlarm.OfficerDeaths > 0) return Deed.CopKilling;
            var since = Mathf.Max(0.1f, Time.time - StreetAlarm.IncidentStart + 0.1f);
            var lawFired = StreetAlarm.FactionFiredSince(StreetAlarm.PoliceFaction, since);
            if (StreetAlarm.CivilianDeaths > 0 ||
                (StreetAlarm.GangDeaths > 0 && !lawFired)) return Deed.Murder;
            if (lawFired) return Deed.AssaultOnOfficer;
            return Deed.Affray;
        }

        /// <summary>The arrest cannot go on: the officer is down, the crew is gone, wiped
        /// or has walked off while the question stood, or the walk-up has taken so long
        /// that nobody is going to get there. Not a refusal - nothing was answered.</summary>
        bool ArrestOff()
        {
            bool noLaw = _arrestOfficer != null
                ? _arrestOfficer.Tf == null
                : _arrestLawman == null || _arrestLawman.Dead || _arrestLawman.Tf == null;
            if (noLaw || _arrestCrew == null || _arrestCrew.Wiped) { Drop(); return true; }

            if (_collar == Collar.WalkingUp && Time.time > _collarBy) { Drop(); return true; }

            var lawAt = _arrestOfficer != null ? _arrestOfficer.Tf.position : _arrestLawman.Tf.position;
            if (Vector3.Distance(_arrestCrew.Position, lawAt) > WalksOff)
            {
                AnnounceArrest("THEY WALKED AWAY FROM IT", 3.5f,
                    new Color(1f, 0.55f, 0.45f));
                Run(ordered: true);
                return true;
            }
            return false;
        }

        /// <summary>NOT A CHANCE. The gun STAYS out - a man who has just been told no
        /// does not put it back under his coat - and it goes in as a refusal: the heat of
        /// it brings the cars that a lone officer on a pavement cannot be. Where a squad
        /// is stood at the scene the crew turns its guns on THEM, which is what refusing
        /// a man with a gun in his hand actually looks like.</summary>
        void Fight(bool ordered)
        {
            if (_arrestCall != null)
            {
                _arrestCall.MenRefused = true;
                _arrestCall.MenFought = true;
            }
            AnnounceArrest(ordered
                    ? "THE CREW OPENS UP ON THE OFFICER"
                    : "THE LIEUTENANT CHOOSES TO FIGHT",
                4.5f, new Color(1f, 0.55f, 0.45f));
            var original = _arrestDeed;
            var fresh = StreetAlarm.OfficerDeaths > 0
                ? Deed.CopKilling : Deed.AssaultOnOfficer;
            _arrestDeed = Sentencing.PrimaryCharge(original, fresh);
            if (_arrestCase != null)
            {
                // Every act lands, but firing at an officer must not turn an existing
                // murder into the lesser assault charge. The graver deed leads; the
                // other one remains a typed count on the same file.
                var extra = _arrestDeed == fresh ? original : fresh;
                if (extra != Deed.Affray && extra != _arrestDeed)
                    PrisonPipeline.AttachCharge(_arrestCase, extra);
                _arrestCase.Deed = _arrestDeed;
            }
            RememberAnswer(DoorAnswer.Fight, _arrestDeed);
            LawWire.FiredOnTheOfficer(_arrestCall?.Call, _arrestCrew);
            EndChallenge(holster: false);
            var law = PoliceOnTheScene();
            if (law != null && _arrestCrew != null && !_arrestCrew.Wiped)
                _crews.Sic(_arrestCrew, law);
            Heat = Mathf.Min(120f, Heat + RefusalHeat);
            Clear();
        }

        void Run(bool ordered)
        {
            if (_arrestCall != null)
            {
                _arrestCall.MenRefused = true;
                _arrestCall.MenRan = true;
            }
            AnnounceArrest(ordered ? "THE CREW BREAKS FOR IT" : "THEY RUN",
                4.5f, new Color(1f, 0.72f, 0.35f));
            PrisonPipeline.AttachCharge(_arrestCase, Deed.Resisting);
            RememberAnswer(DoorAnswer.Run, _arrestDeed);
            MarkCrewWanted(_arrestCrew, WantedLevels.Fled);
            LawWire.RanFromTheOfficer(_arrestCall?.Call, _arrestCrew);
            var law = PoliceOnTheScene();
            var from = _arrestOfficer != null ? _arrestOfficer.Position
                : _arrestLawman != null && _arrestLawman.Tf != null
                    ? _arrestLawman.Tf.position : _arrestCrew.Position;
            EndChallenge(holster: false);
            var moved = _crews.OrderFlee(_arrestCrew, from);
            // Walking away from a live gunpoint arrest is the resisting act itself.
            // The law does not holster and watch him leave; every officer on the scene
            // immediately turns the held aim into a real combat target.
            if (PoliceProcedure.ShouldOpenFireOnFlight(
                    arrestInProgress: law != null && !law.Wiped,
                    suspectMoved: moved) &&
                _arrestCrew != null && !_arrestCrew.Wiped)
                _crews.Sic(law, _arrestCrew);
            Clear();
        }

        DoorAnswer RememberAnswer(DoorAnswer answer, Deed deed,
            bool attachDefendants = true)
        {
            if (_arrestCrew == null) return answer;
            if (_arrestCrew.HasDoorAnswer)
                answer = SurrenderRoll.MostSerious(_arrestCrew.LastDoorAnswer, answer);
            _arrestCrew.HasDoorAnswer = true;
            _arrestCrew.LastDoorAnswer = answer;
            _arrestCrew.ArrestCase = _arrestCase;
            _arrestCrew.ArrestDeed = deed;
            if (_arrestCase == null || !attachDefendants) return answer;
            foreach (var man in _arrestCrew.All())
                if (man != null && !man.Dead && man.CharacterId >= 0 &&
                    !_arrestCase.Defendants.Contains(man.CharacterId))
                    _arrestCase.Defendants.Add(man.CharacterId);
            return answer;
        }

        static void MarkCrewWanted(DemoCrews.Unit crew, int level)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            var roster = crew != null ? underworld?.Of(crew.Faction)?.Roster : null;
            if (roster == null) return;
            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            var today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            foreach (var man in crew.All())
            {
                if (man == null || man.CharacterId < 0) continue;
                WantedLevels.Mark(roster.Find(man.CharacterId), level, today);
            }
            LivingCity.Gameplay.PersonnelDirector.Instance?.Touch();
        }

        /// <summary>A squad of the law stood at this scene with somebody still on his
        /// feet, or null - what a crew that refuses has in front of it to shoot at.</summary>
        DemoCrews.Unit PoliceOnTheScene()
        {
            if (_arrestOfficer != null && _arrestOfficer.Unit != null &&
                !_arrestOfficer.Unit.Wiped) return _arrestOfficer.Unit;
            if (_arrestSquad != null && !_arrestSquad.Wiped) return _arrestSquad;
            if (_arrestCrew == null) return null;
            // and it has to be a squad they can actually see. Any squad in the list would
            // do for the arithmetic and would have sent a crew across the quarter after
            // men it has never laid eyes on.
            var at = _arrestCrew.Position;
            foreach (var squad in _squads)
                if (squad.Men != null && !squad.Men.Wiped &&
                    (squad.Men.Position - at).sqrMagnitude < ArrestReach * ArrestReach)
                    return squad.Men;
            return null;
        }

        /// <summary>What refusing an arrest is worth in heat: a level of it. Enough on
        /// its own to put a car on the road, which is the answer to a man who will not
        /// go quietly for one officer.</summary>
        const float RefusalHeat = 25f;

        /// <summary>The arrest is off; nobody was taken and nobody refused.</summary>
        void Drop()
        {
            EndChallenge(holster: true);
            if (_arrestCrew != null && _arrestCrew.Surrendered) _crews.LetGo(_arrestCrew);
            Clear();
        }

        /// <summary>The piece goes away (or stays out after a refusal) and the man who
        /// held it goes back to holding the scene, whichever body he was.</summary>
        void EndChallenge(bool holster, bool release = true)
        {
            if (_arrestOfficer != null)
            {
                _arrestOfficer.EndChallenge(holster);
                // NOT WHILE A TELEPHONE CALL STILL HOLDS HIM. A complaint keeps its unit
                // until the door has been answered one way or the other; releasing the
                // beat man here put him back on his round the instant a collar fell
                // through, so the call went on holding an officer who had already walked
                // off - and the shop it was rung about got nothing at all. EndChallenge
                // leaves him OnScene instead, and the call takes the statement and then
                // sends him home itself.
                if (holster && release && _arrestCall == null) _arrestOfficer.Release();
            }
            else if (_arrestLawman != null && !_arrestLawman.Dead && _arrestLawman.Tf != null)
            {
                // he stands where he is; the squad's own Securing orders have him again
                if (_arrestSquad != null)
                    foreach (var officer in _arrestSquad.All())
                    {
                        if (officer == null || officer.Dead || officer.Tf == null) continue;
                        officer.LowerGunpoint();
                        if (holster) officer.Holster();
                    }
                else
                {
                    _arrestLawman.LowerGunpoint();
                    if (holster) _arrestLawman.Holster();
                }
            }
        }

        void Clear(bool preserveCase = false)
        {
            // A DOCKET ENTRY NOBODY WAS TAKEN FOR IS NOT A CASE (GAN-245). The file is
            // opened when the officer sets off, because the witnesses have to be
            // snapshotted while the people who saw it are still on the pavement - but
            // an arrest that was refused, dropped or walked away from charged nobody,
            // and an empty file left open would quietly become an extra count against
            // these men the next time they were taken (AttachOpenComplaints).
            if (!preserveCase && _arrestCaseIsOurs && _arrestCase != null &&
                _arrestCase.Defendants.Count == 0)
                _arrestCase.Status = CaseStatus.Tried;
            _arrestCase = null;
            _arrestCaseIsOurs = false;
            _arrestCall = null;

            ClearBanner();
            _arrestOfficer = null;
            _arrestLawman = null;
            _arrestSquad = null;
            _arrestCrew = null;
            _arrestCollar = null;
            _collar = Collar.None;
        }
    }
}
