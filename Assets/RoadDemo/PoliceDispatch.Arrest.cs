using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoadDemo
{
    /// <summary>
    /// THE ARREST. What the beat is FOR, and what it had been missing: an officer who
    /// answers a shooting, walks up to the men who did it and stands there is not the
    /// law, he is scenery. ("Policija nije došla da proba da nas uhapsi nego su samo
    /// stali tu.")
    ///
    /// So: once the shooting has stopped, the officer holding the scene picks out the
    /// crew that did it, walks over with his sidearm out, and puts the question. The
    /// answer is the PLAYER'S - his lieutenant either goes quietly or he does not - and
    /// that is deliberately the whole of the mechanism: an arrest the game could make
    /// on its own would be a punishment, and an arrest the player consents to is a
    /// decision (his men are off the street for three days and on the books as held,
    /// against a refusal that leaves an officer stood in front of him with a gun out and
    /// cars on the way).
    ///
    /// Y goes quietly, N refuses, and saying nothing at all is a refusal - a man who
    /// stands there saying nothing with his crew's guns in their coats has refused,
    /// whatever he meant by it.
    ///
    /// The officer's half of this - the walk over, the piece in his fist, the stance -
    /// is PoliceFootPatrol.Challenge; the crew's half is DemoCrews.GiveUp / TakeIn.
    /// Nothing about it is a demo's: the city's beat does this the moment the city's
    /// scene gives it beat officers.
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

        /// <summary>Seconds the question stands before silence is taken for a no.</summary>
        const float AskSeconds = 15f;

        /// <summary>Seconds between one telling of it and the next, so a player who
        /// looked away still sees what he is being asked.</summary>
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

        Collar _collar = Collar.None;
        PoliceFootPatrol _arrestOfficer;
        DemoCrews.Unit _arrestCrew;
        float _askUntil, _sayAgainAt, _takeAt;
        int _askedIncident = -1;

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
                    if (!_arrestOfficer.StoodOver) return;
                    _collar = Collar.Asking;
                    _askUntil = Time.time + AskSeconds;
                    _sayAgainAt = 0f;
                    return;

                case Collar.Asking:
                {
                    if (ArrestOff()) return;
                    if (Time.time >= _sayAgainAt)
                    {
                        _sayAgainAt = Time.time + AskAgain;
                        CrewOverlay.Announce(
                            "\"POLICE! HANDS UP - YOU'RE UNDER ARREST\"   [Y] GO QUIETLY   [N] NOT A CHANCE",
                            AskAgain, new Color(0.55f, 0.78f, 1f));
                    }
                    var keys = Keyboard.current;
                    bool yes = keys != null && keys.yKey.wasPressedThisFrame;
                    bool no = keys != null && keys.nKey.wasPressedThisFrame;
                    if (yes && _crews.GiveUp(_arrestCrew))
                    {
                        _collar = Collar.Taking;
                        _takeAt = Time.time + TakeSeconds;
                        return;
                    }
                    if (no || Time.time >= _askUntil) Refused();
                    return;
                }

                case Collar.Taking:
                {
                    // the crew being wiped out mid-arrest is not an arrest, and the books
                    // are not told about men who were shot where they stood
                    if (_arrestCrew == null || _arrestCrew.Wiped) { Drop(); return; }
                    if (Time.time < _takeAt) return;
                    _crews.TakeIn(_arrestCrew, TheCharge());
                    if (_arrestOfficer != null)
                    {
                        _arrestOfficer.EndChallenge();
                        _arrestOfficer.Release();
                    }
                    Clear();
                    return;
                }
            }
        }

        /// <summary>An officer stood at a quiet scene, and the men who made it stood in
        /// front of him. One arrest per incident: a crew that talked its way out of one
        /// is not asked again over the same bodies.</summary>
        void LookForACollar()
        {
            if (_crews == null || StreetAlarm.QuietFor < QuietBefore) return;
            if (StreetAlarm.QuietFor > ArrestWindow) return;
            if (StreetAlarm.IncidentNumber == _askedIncident) return;

            PoliceFootPatrol officer = null;
            foreach (var u in _units)
                if (!u.Carries && u.OnScene && u is PoliceFootPatrol foot) { officer = foot; break; }
            if (officer == null || officer.Tf == null) return;

            var crew = GuiltyNear(officer.Tf.position);
            if (crew == null) return;

            var man = crew.Boss != null && !crew.Boss.Dead
                ? crew.Boss : DemoCrews.NearestOf(crew, officer.Tf.position);
            if (man == null || man.Tf == null) return;

            _askedIncident = StreetAlarm.IncidentNumber;
            _arrestOfficer = officer;
            _arrestCrew = crew;
            _collar = Collar.WalkingUp;
            officer.Challenge(man, _sidearm);
            CrewOverlay.Announce("AN OFFICER IS WALKING OVER", 3.5f, new Color(0.55f, 0.78f, 1f));
        }

        /// <summary>Whose shooting this was: of the crews that fired at this incident,
        /// the nearest one still standing within reach - the outfit's first, because a
        /// player watching his own men taken is the point of the thing and a rival mob
        /// being led away in front of him is a bonus, not a substitute.</summary>
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
                // the outfit's men come first whatever the metres say
                float score = d + (unit.Faction == 0 ? 0f : 1000f);
                if (score < bestScore) { bestScore = score; best = unit; }
            }
            return best;
        }

        /// <summary>What they are being taken for, off the incident's own tally. A
        /// charge is not decoration: it goes on the man's rap sheet and stays there.</summary>
        static string TheCharge()
        {
            if (StreetAlarm.OfficerDeaths > 0) return "Murder of a police officer";
            if (StreetAlarm.CivilianDeaths > 0) return "Murder";
            if (StreetAlarm.GangDeaths > 0) return "Murder - gangland";
            return "Affray - discharging firearms in the street";
        }

        /// <summary>The arrest cannot go on: the crew is gone, wiped, or has walked off
        /// while the question stood. Not a refusal - nothing was answered.</summary>
        bool ArrestOff()
        {
            if (_arrestOfficer == null || _arrestOfficer.Tf == null ||
                _arrestCrew == null || _arrestCrew.Wiped)
            { Drop(); return true; }

            if (Vector3.Distance(_arrestCrew.Position, _arrestOfficer.Tf.position) > WalksOff)
            {
                CrewOverlay.Announce("THEY WALKED AWAY FROM IT", 3.5f, new Color(1f, 0.55f, 0.45f));
                Drop();
                return true;
            }
            return false;
        }

        /// <summary>NOT A CHANCE. The gun STAYS out - a man who has just been told no
        /// does not put it back under his coat - he backs off the crew, and it goes in
        /// as a refusal: the heat of it brings the cars that a lone officer on a
        /// pavement cannot be.</summary>
        void Refused()
        {
            CrewOverlay.Announce("THE LIEUTENANT REFUSED - THE OFFICER IS CALLING IT IN",
                4.5f, new Color(1f, 0.55f, 0.45f));
            // he does NOT go back to his beat: he stands at the scene with the gun out
            // and waits for the cars, which is what one man on a pavement can do about a
            // crew that will not go with him. The dispatcher takes him home in its own
            // time (TickFoot), as it does any officer holding a scene.
            if (_arrestOfficer != null) _arrestOfficer.EndChallenge(holster: false);
            Heat = Mathf.Min(120f, Heat + RefusalHeat);
            // AND THE CARS ARE SENT NOW, not left to the escalation check - that one only
            // fires while the shooting is still going on (QuietFor < 12 s), and a refusal
            // is by definition made in the quiet after it. Without this the heat went up
            // and nothing whatever came of it.
            _called = true;
            Send(first: false);
            Clear();
        }

        /// <summary>What refusing an arrest is worth in heat: a level of it. Enough on
        /// its own to put a car on the road, which is the answer to a man who will not
        /// go quietly for one officer.</summary>
        const float RefusalHeat = 25f;

        /// <summary>The arrest is off; nobody was taken and nobody refused.</summary>
        void Drop()
        {
            if (_arrestOfficer != null) _arrestOfficer.EndChallenge();
            if (_arrestCrew != null && _arrestCrew.Surrendered) _crews.LetGo(_arrestCrew);
            Clear();
        }

        void Clear()
        {
            _arrestOfficer = null;
            _arrestCrew = null;
            _collar = Collar.None;
        }
    }
}
