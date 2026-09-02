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
    /// is PoliceFootPatrol.Challenge for a beat man and a plain walk order with the gun
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
        const float AskSeconds = 15f;

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
        /// that rather than forking a second way of having a gun in your hand.</summary>
        const float GunRefresh = 6f;

        /// <summary>A PAPER SCREEN STOPS THE CLOCK. The ledger and the strategic map are
        /// pages: the player behind one can neither see the banner nor give the order
        /// that would overrule the roll, so the seconds are handed back rather than run
        /// down. The turf map is deliberately NOT on this list any more - orders are
        /// given from it (TurfMapHud), so a player watching an arrest from the map is as
        /// able to intervene as one watching it from the street.</summary>
        static bool Blocked =>
            LivingCity.UI.PersonnelAlmanac.IsOpen ||
            LivingCity.UI.StrategicMapHud.InputBlocked;

        Collar _collar = Collar.None;
        Deed _arrestDeed = Deed.Affray;    // what they are being taken for
        CourtCase _arrestCase;             // the docket entry, when there is a city

        /// <summary>Whether the collar OPENED that case. A shooting's file is opened by
        /// the collar and is worth nothing if nobody is taken; a complaint's file was
        /// opened by the telephone call and outlives a failed arrest, because a
        /// complaint nobody was taken for is exactly what becomes a count later.</summary>
        bool _arrestCaseIsOurs;
        PoliceFootPatrol _arrestOfficer;   // the beat man, when it is one
        CrewWalker _arrestLawman;          // a squad's lead, when the car brought him
        DemoCrews.Unit _arrestSquad;       // that lead's squad
        DemoCrews.Unit _arrestCrew;
        CrewWalker _arrestCollar;          // the man of the crew being spoken to
        float _askUntil, _sayAgainAt, _takeAt, _gunAt, _collarBy, _collarAt;
        int _askedIncident = -1;
        float _fightChance;
        bool _willFight;
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
                    if (ArrestOff()) return;
                    KeepGunUp();
                    Banner();
                    // the player can call it a fight before the officer is even there
                    if (PlayerSaysFight()) { Refused(ordered: true); return; }
                    if (!StoodOver()) return;
                    _collar = Collar.Asking;
                    _askUntil = Time.time + AskSeconds;
                    _sayAgainAt = 0f;
                    return;

                case Collar.Asking:
                {
                    if (ArrestOff()) return;
                    KeepGunUp();
                    Banner();
                    if (PlayerSaysFight()) { Refused(ordered: true); return; }
                    if (Blocked)
                    {
                        _askUntil += dt;
                        _sayAgainAt = 0f;   // said again the moment the page is closed
                        return;
                    }
                    if (Time.time >= _sayAgainAt)
                    {
                        _sayAgainAt = Time.time + AskAgain;
                        CrewOverlay.Announce(Question, AskAgain, new Color(0.55f, 0.78f, 1f));
                    }
                    if (Time.time < _askUntil) return;
                    if (_willFight) { Refused(ordered: false); return; }
                    if (_crews.GiveUp(_arrestCrew))
                    {
                        _collar = Collar.Taking;
                        _takeAt = Time.time + TakeSeconds;
                        return;
                    }
                    Refused(ordered: false);
                    return;
                }

                case Collar.Taking:
                {
                    // the crew being wiped out mid-arrest is not an arrest, and the books
                    // are not told about men who were shot where they stood
                    if (_arrestCrew == null || _arrestCrew.Wiped) { Drop(); return; }
                    if (Time.time < _takeAt) return;
                    _crews.TakeIn(_arrestCrew, _arrestDeed,
                        Force != null ? Force.Pipeline : null, _arrestCase);
                    EndChallenge(holster: true);
                    Clear();
                    return;
                }
            }
        }

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

            PoliceFootPatrol foot = null;
            foreach (var u in _units)
                if (!u.Carries && u.OnScene && u is PoliceFootPatrol beat) { foot = beat; break; }

            CrewWalker lawman = null;
            DemoCrews.Unit squadMen = null;
            Vector3 from;
            if (foot != null && foot.Tf != null) from = foot.Tf.position;
            else
            {
                // CONF-001: THE CAR PUTS THE SAME QUESTION. A squad that drove to a
                // shooting, got out and taped the scene off used to stand at it saying
                // nothing, so an arrest only ever happened where a beat officer happened
                // to be walking. Its lead crosses the street exactly as the beat man
                // does - the only difference is which body it is.
                foreach (var squad in _squads)
                {
                    if (squad.State != SquadState.Securing) continue;
                    var lead = Lead(squad);
                    if (lead == null || lead.Tf == null) continue;
                    lawman = lead;
                    squadMen = squad.Men;
                    break;
                }
                if (lawman == null) return;
                from = lawman.Tf.position;
            }

            var crew = GuiltyNear(from);
            if (crew == null) return;

            var man = crew.Boss != null && !crew.Boss.Dead
                ? crew.Boss : DemoCrews.NearestOf(crew, from);
            if (man == null || man.Tf == null) return;

            _askedIncident = StreetAlarm.IncidentNumber;
            _arrestDeed = TheDeed();
            _arrestCase = OpenShootingCase(crew);
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
            _fightChance = FightOdds(crew);
            _willFight = SurrenderRoll.Fights(_fightChance,
                SurrenderRoll.StreamFor(_crews.CitySeed, CrewKey(crew), StreetAlarm.IncidentNumber));

            if (foot != null) foot.Challenge(man, _sidearm);
            else BeginSquadChallenge(man);
            Banner();
            CrewOverlay.Announce("AN OFFICER IS WALKING OVER", 3.5f, new Color(0.55f, 0.78f, 1f));
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

            PoliceFootPatrol foot = call.Unit as PoliceFootPatrol;
            CrewWalker lawman = null;
            if (foot == null)
            {
                lawman = call.Men != null && !call.Men.Wiped ? Lead(call.Men) : null;
                if (lawman == null || lawman.Tf == null) return false;
            }
            var from = foot != null && foot.Tf != null ? foot.Tf.position
                     : lawman != null && lawman.Tf != null ? lawman.Tf.position
                     : call.Call.Pos;

            var crew = AccusedNear(call.Call.Pos, call.Call.Faction);
            if (crew == null) return false;

            var man = crew.Boss != null && !crew.Boss.Dead
                ? crew.Boss : DemoCrews.NearestOf(crew, from);
            if (man == null || man.Tf == null) return false;

            _askedIncident = call.Call.Number;
            _arrestDeed = call.Call.Charge;
            _arrestCase = call.File;
            _arrestCaseIsOurs = false;   // the telephone opened it, not this collar
            _arrestOfficer = foot;
            _arrestLawman = lawman;
            _arrestSquad = call.Men;
            _arrestCrew = crew;
            _arrestCollar = man;
            _collar = Collar.WalkingUp;
            _collarAt = Time.time;
            _collarBy = Time.time + CollarPatience;

            _fightChance = FightOdds(crew);
            _willFight = SurrenderRoll.Fights(_fightChance,
                SurrenderRoll.StreamFor(_crews.CitySeed, CrewKey(crew), call.Call.Number));

            if (foot != null) foot.Challenge(man, _sidearm);
            else BeginSquadChallenge(man);
            Banner();
            CrewOverlay.Announce("AN OFFICER IS WALKING OVER", 3.5f, new Color(0.55f, 0.78f, 1f));
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
                if (unit == null || unit.IsPolice || unit.Wiped || unit.Surrendered) continue;
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

            var outfit = LivingCity.Gameplay.OutfitDirector.Instance;
            var today = outfit != null && outfit.Campaign != null ? outfit.Campaign.Day : 0;
            var file = pipeline.OpenCase(_arrestDeed, crew.Faction, today,
                today > 0 ? today + Sentencing.DaysToCourt : 0);

            // THE PEOPLE WHO WERE THERE WHEN IT HAPPENED - taken when the first round
            // went off (SnapshotTheScene), not now. An arrest can be made a hundred and
            // fifty seconds after the shooting stopped, by which time the men who saw it
            // have walked home and a fresh crowd has drifted over to look at the chalk.
            CopySceneWitnesses(file, StreetAlarm.IncidentNumber);

            // WHAT THE LAW ITSELF SAW, recorded while the squad was actually looking at
            // it (BeginWarning / PickFight). Read at arrest time it was always false:
            // the squad that makes the arrest is by definition Securing a quiet street
            // by then.
            var sawTheAct = _lawSawIncident == StreetAlarm.IncidentNumber;
            file.Witnesses.Add(new Witness
            {
                Kind = sawTheAct ? WitnessKind.PoliceSawIt : WitnessKind.PoliceFoundThem,
                Name = "The arresting officer",
                Seed = StreetAlarm.IncidentNumber,
                X = StreetAlarm.Incident.x, Y = StreetAlarm.Incident.y,
                Z = StreetAlarm.Incident.z,
            });

            LawWire.CaseOpened(file);
            return file;
        }

        /// <summary>The squad's lead walks over with the piece out - the same arm's
        /// length, the same stance, no second mechanism.</summary>
        void BeginSquadChallenge(CrewWalker man)
        {
            var law = _arrestLawman;
            if (law == null || law.Tf == null || man == null || man.Tf == null) return;
            law.Disengage();
            var back = law.Tf.position - man.Tf.position;
            back.y = 0f;
            back = back.sqrMagnitude > 0.04f ? back.normalized : law.Tf.forward;
            var standAt = man.Tf.position + back * SquadGap;
            standAt.y = law.Tf.position.y;
            law.OrderToPoint(standAt);
            _gunAt = 0f;
            KeepGunUp();
        }

        /// <summary>The gun stays out for as long as the question stands. Nothing new:
        /// the man is told there is shooting where his collar is stood, and the
        /// concealment rule does the rest.</summary>
        void KeepGunUp()
        {
            if (_arrestLawman == null || _arrestLawman.Dead || _arrestLawman.Tf == null) return;
            if (_arrestCollar == null || _arrestCollar.Tf == null) return;
            if (Time.time < _gunAt) return;
            _gunAt = Time.time + GunRefresh;
            _arrestLawman.HearShot(_arrestCollar.Tf.position);
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

        /// <summary>ARREST IN PROGRESS, for as long as it is. Pushed every frame; the
        /// banner only touches its labels when the words have actually changed.</summary>
        void Banner()
        {
            if (_hud == null) _hud = ArrestHud.For(gameObject);
            if (_hud == null || _arrestCrew == null) return;
            _hud.Show(_arrestCrew.GangName, SurrenderRoll.Leaning(_fightChance));
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
        float FightOdds(DemoCrews.Unit crew)
        {
            var underworld = LivingCity.Outfit.Underworld.Current;
            var roster = crew != null && underworld != null
                ? underworld.Of(crew.Faction)?.Roster : null;
            if (crew == null || roster == null)
                return SurrenderRoll.FightChance(
                    SurrenderRoll.NoBooks, SurrenderRoll.NoBooks, SurrenderRoll.NoBooks);

            int temper = 0, loyalty = 0, counted = 0, bestLead = -1;
            Character senior = null, lieutenant = null;
            foreach (var man in crew.All())
            {
                if (man == null || man.Dead || man.CharacterId < 0) continue;
                var member = roster.Find(man.CharacterId);
                if (member == null) continue;
                temper += member.Temper;
                loyalty += member.Loyalty;
                counted++;
                if (member.Id == crew.CommandParentId) lieutenant = member;
                var lead = AttributeScale.ValueOf(
                    member.GetHalfSteps(CharacterAttribute.Leadership));
                if (lead > bestLead) { bestLead = lead; senior = member; }
            }
            if (counted == 0)
                return SurrenderRoll.FightChance(
                    SurrenderRoll.NoBooks, SurrenderRoll.NoBooks, SurrenderRoll.NoBooks);

            var speaker = lieutenant ?? senior;
            return SurrenderRoll.FightChance(
                speaker != null ? speaker.Courage : SurrenderRoll.NoBooks,
                temper / counted, loyalty / counted);
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
                if (unit == null || unit.IsPolice || unit.Wiped || unit.Surrendered) continue;
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
            if (StreetAlarm.CivilianDeaths > 0 || StreetAlarm.GangDeaths > 0) return Deed.Murder;
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
                CrewOverlay.Announce("THEY WALKED AWAY FROM IT", 3.5f, new Color(1f, 0.55f, 0.45f));
                Drop();
                return true;
            }
            return false;
        }

        /// <summary>NOT A CHANCE. The gun STAYS out - a man who has just been told no
        /// does not put it back under his coat - and it goes in as a refusal: the heat of
        /// it brings the cars that a lone officer on a pavement cannot be. Where a squad
        /// is stood at the scene the crew turns its guns on THEM, which is what refusing
        /// a man with a gun in his hand actually looks like.</summary>
        void Refused(bool ordered)
        {
            CrewOverlay.Announce(ordered
                    ? "THE CREW OPENS UP ON THE OFFICER"
                    : "THE LIEUTENANT REFUSED - THE OFFICER IS CALLING IT IN",
                4.5f, new Color(1f, 0.55f, 0.45f));
            EndChallenge(holster: false);
            var law = PoliceOnTheScene();
            if (law != null && _arrestCrew != null && !_arrestCrew.Wiped)
                _crews.Sic(_arrestCrew, law);
            Heat = Mathf.Min(120f, Heat + RefusalHeat);
            // AND THE CARS ARE SENT NOW, not left to the escalation check - that one only
            // fires while the shooting is still going on (QuietFor < 12 s), and a refusal
            // is by definition made in the quiet after it. Without this the heat went up
            // and nothing whatever came of it.
            _called = true;
            Send(first: false);
            Clear();
        }

        /// <summary>A squad of the law stood at this scene with somebody still on his
        /// feet, or null - what a crew that refuses has in front of it to shoot at.</summary>
        DemoCrews.Unit PoliceOnTheScene()
        {
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
        void EndChallenge(bool holster)
        {
            if (_arrestOfficer != null)
            {
                _arrestOfficer.EndChallenge(holster);
                if (holster) _arrestOfficer.Release();
            }
            else if (_arrestLawman != null && !_arrestLawman.Dead && _arrestLawman.Tf != null)
            {
                // he stands where he is; the squad's own Securing orders have him again
                if (holster) _arrestLawman.OrderToPoint(_arrestLawman.Tf.position);
            }
        }

        void Clear()
        {
            // A DOCKET ENTRY NOBODY WAS TAKEN FOR IS NOT A CASE (GAN-245). The file is
            // opened when the officer sets off, because the witnesses have to be
            // snapshotted while the people who saw it are still on the pavement - but
            // an arrest that was refused, dropped or walked away from charged nobody,
            // and an empty file left open would quietly become an extra count against
            // these men the next time they were taken (AttachOpenComplaints).
            if (_arrestCaseIsOurs && _arrestCase != null &&
                _arrestCase.Defendants.Count == 0)
                _arrestCase.Status = CaseStatus.Tried;
            _arrestCase = null;
            _arrestCaseIsOurs = false;

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
