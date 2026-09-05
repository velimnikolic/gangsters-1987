using System.Collections.Generic;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The one channel a shot goes out on. Everything that fires reports here (the
    /// arena, on every round from a pavement or a car window); everything that has
    /// to react - the crowd, the traffic, the crews' own nerves, the police - reads
    /// from here. What it keeps: the last few dozen shots (where, when, how loud, who),
    /// the incident they add up to (its centre, when it began, how many rounds, who
    /// has died in it), and cheap queries on top: how dangerous a spot is right now,
    /// whether a shot was heard from a spot lately, how long the street has been quiet.
    /// Static, like StreetTraffic's road users: one street, one alarm.
    /// </summary>
    public static class StreetAlarm
    {
        public struct Shot
        {
            public Vector3 Pos;
            public float Time;
            public float Loudness;   // metres it carries
            public int Faction;      // who fired: 0 the outfit, >0 a mob, PoliceFaction the law
            public CrewWalker Shooter;
        }

        /// <summary>
        /// A TELEPHONE CALL (GAN-245). Not a shot: the shopkeeper who was leaned on has
        /// picked up the receiver, and the only thing on the street is a crew standing
        /// in a doorway. Everything a dispatcher needs to answer one is here.
        /// </summary>
        public struct Complaint
        {
            /// <summary>The door it was rung about.</summary>
            public Vector3 Pos;

            public float Time;

            /// <summary>Who is being complained about: 0 the outfit, >0 a mob.</summary>
            public int Faction;

            public string BusinessId;

            /// <summary>What the shop is called, for the banner and the paper.</summary>
            public string Where;

            /// <summary>The game hour it was rung, for the Fear ledger's clock.</summary>
            public double GameHour;

            /// <summary>The call's own number - the complaint sequence, NOT the
            /// shooting incident's (see <see cref="ComplaintNumber"/>).</summary>
            public int Number;

            /// <summary>What the men would be charged with if the officer finds them
            /// standing there: Extortion for a shopkeeper's call, WitnessTampering when
            /// the man who rang was a witness somebody came to see.</summary>
            public Deed Charge;

            /// <summary>The act happened beyond the threshold. People on the pavement
            /// heard the aftermath, but did not see the act itself.</summary>
            public bool Indoors;
        }

        /// <summary>What KIND of incident the current number is. A complaint is not a
        /// shooting and must not be read as one - a squad answering one carries no
        /// warning phase, because "DROP THE GUNS" needs guns.</summary>
        public enum IncidentKind { Shooting, Complaint }

        /// <summary>The faction number the police carry in the arena.</summary>
        public const int PoliceFaction = -1;

        /// <summary>Seconds of quiet after which the next shot opens a NEW incident.</summary>
        public const float IncidentGap = 45f;

        const int Keep = 48;
        static readonly Shot[] ring = new Shot[Keep];
        static int head, count;

        /// <summary>Raised for every shot, after it is recorded.</summary>
        public static event System.Action<Shot> OnShot;

        /// <summary>Raised when somebody dies of a shot: where, and whether a bystander.</summary>
        public static event System.Action<Vector3, DeathOf, int> OnDeath;

        /// <summary>Raised when a shopkeeper rings the precinct. ONE channel: a
        /// complaint goes down the same wire as a shot rather than growing a second
        /// static beside it.</summary>
        public static event System.Action<Complaint> OnComplaint;

        public enum DeathOf { Gangster, Civilian, Officer }

        public static float LastShotAt { get; private set; } = -1000f;
        public static Vector3 LastShotPos { get; private set; }

        // ---------------------------------------------------------------- incident

        /// <summary>The running incident: where the shooting is (a running centre of
        /// the rounds fired), when it opened, and its toll.</summary>
        public static bool IncidentOpen => count > 0 && Time.time - LastShotAt < IncidentGap;
        public static Vector3 Incident { get; private set; }
        public static float IncidentStart { get; private set; } = -1000f;
        public static int IncidentShots { get; private set; }
        public static int CivilianDeaths { get; private set; }
        public static int GangDeaths { get; private set; }
        public static int OfficerDeaths { get; private set; }
        public static int IncidentNumber { get; private set; }

        /// <summary>Whether the latest officer death came from a crew returning police
        /// fire in this same incident. PoliceDispatch reads this synchronously from the
        /// death event; the death still counts everywhere else.</summary>
        public static bool LastOfficerDeathWasDefensiveReturn { get; private set; }

        /// <summary>The known attacker for the current death callback. A missing
        /// attacker must not make every recent shooter responsible for the death.</summary>
        public static CrewWalker LastDeathAttacker { get; private set; }

        /// <summary>The telephone's own counter. A complaint used to take the next
        /// SHOOTING incident number, which made every consumer of that number - the
        /// dispatcher's "this is a new incident" test, the one-arrest-per-incident
        /// guard - believe a fresh gunfight had begun the moment a shopkeeper three
        /// blocks away picked up a receiver: the response state reset mid-firefight and
        /// a crew that had already talked its way out could be asked again. The two
        /// sequences count separately, and a complaint leaves the shooting one exactly
        /// where it was.</summary>
        public static int ComplaintNumber { get; private set; }

        /// <summary>What the current incident number is: shots, or a telephone.</summary>
        public static IncidentKind Kind { get; private set; } = IncidentKind.Shooting;

        /// <summary>The last complaint rung, whether or not anybody answered it.</summary>
        public static Complaint LastComplaint { get; private set; }

        /// <summary>Seconds since the last shot (large when none).</summary>
        public static float QuietFor => Time.time - LastShotAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            head = count = 0;
            LastShotAt = -1000f;
            IncidentStart = -1000f;
            IncidentShots = CivilianDeaths = GangDeaths = OfficerDeaths = 0;
            LastOfficerDeathWasDefensiveReturn = false;
            LastDeathAttacker = null;
            IncidentNumber = 0;
            ComplaintNumber = 0;
            Kind = IncidentKind.Shooting;
            LastComplaint = default;
            OnShot = null;
            OnDeath = null;
            OnComplaint = null;
        }

        /// <summary>
        /// SOMEBODY RANG. A number of the call's OWN sequence, and nothing else about
        /// the street changes: LastShotAt is deliberately untouched, so
        /// <see cref="IncidentOpen"/>, <see cref="QuietFor"/> and <see cref="Danger"/> go
        /// on meaning exactly what they meant - a complaint is not gunfire and must not
        /// put a scene into the state gunfire puts it in, nor take the number a gunfight
        /// is being counted by.
        ///
        /// The 1987 telephone's own delay is the dispatcher's (PoliceDispatch): what is
        /// recorded here is the moment the receiver came off the hook.
        /// </summary>
        public static Complaint Complain(Vector3 pos, int faction, string businessId,
            string where, double gameHour, Deed charge = Deed.Extortion,
            bool indoors = false)
        {
            ComplaintNumber++;
            Kind = IncidentKind.Complaint;
            var call = new Complaint
            {
                Pos = pos,
                Time = Time.time,
                Faction = faction,
                BusinessId = businessId ?? "",
                Where = where ?? "",
                GameHour = gameHour,
                Number = ComplaintNumber,
                Charge = charge,
                Indoors = indoors,
            };
            LastComplaint = call;
            OnComplaint?.Invoke(call);
            return call;
        }

        /// <summary>A round left a gun here.</summary>
        public static void Report(Vector3 pos, CrewWalker shooter, int faction, float loudness)
        {
            float now = Time.time;
            if (now - LastShotAt >= IncidentGap)
            {
                // a fresh incident
                IncidentStart = now;
                IncidentShots = 0;
                CivilianDeaths = GangDeaths = OfficerDeaths = 0;
                Incident = pos;
                IncidentNumber++;
                Kind = IncidentKind.Shooting;
            }
            IncidentShots++;
            // the centre drifts with the shooting: mostly where it is now
            Incident = IncidentShots == 1 ? pos : Vector3.Lerp(Incident, pos, 0.25f);
            LastShotAt = now;
            LastShotPos = pos;

            var shot = new Shot { Pos = pos, Time = now, Loudness = Mathf.Max(5f, loudness), Faction = faction, Shooter = shooter };
            ring[head] = shot;
            head = (head + 1) % Keep;
            if (count < Keep) count++;
            OnShot?.Invoke(shot);
        }

        /// <summary>Somebody died of the shooting. <paramref name="victimFaction"/>
        /// is the house he belonged to, or -1 for a civilian and anybody nobody can
        /// name: a grudge has to have somebody to belong to.</summary>
        public static void Death(Vector3 pos, DeathOf who, int victimFaction = -1,
            bool defensivePoliceReturn = false, CrewWalker attacker = null)
        {
            LastDeathAttacker = attacker;
            LastOfficerDeathWasDefensiveReturn =
                who == DeathOf.Officer && defensivePoliceReturn;
            switch (who)
            {
                case DeathOf.Civilian: CivilianDeaths++; break;
                case DeathOf.Officer: OfficerDeaths++; break;
                default: GangDeaths++; break;
            }
            OnDeath?.Invoke(pos, who, victimFaction);
        }

        public static int Deaths => CivilianDeaths + GangDeaths + OfficerDeaths;

        // ---------------------------------------------------------------- queries

        /// <summary>How dangerous this spot is, 0..1: the recent shots weighted by how
        /// close and how fresh - a round in the last few seconds close by is 1, one
        /// heard far off a minute ago is nothing.</summary>
        public static float Danger(Vector3 pos)
        {
            const float Fade = 12f;
            float now = Time.time;
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                var s = ring[(head - 1 - i + Keep) % Keep];
                float age = now - s.Time;
                if (age > Fade) break; // newest first: the rest are older still
                float d = Vector3.Distance(pos, s.Pos);
                if (d > s.Loudness) continue;
                float near = 1f - d / s.Loudness;
                sum += (1f - age / Fade) * near * near;
            }
            return Mathf.Clamp01(sum);
        }

        /// <summary>Was a shot heard from here in the last <paramref name="seconds"/> -
        /// within its loudness - and where was the nearest one.</summary>
        public static bool HeardSince(Vector3 pos, float seconds, out Vector3 where)
        {
            float now = Time.time;
            float bestD = float.MaxValue;
            where = default;
            for (int i = 0; i < count; i++)
            {
                var s = ring[(head - 1 - i + Keep) % Keep];
                if (now - s.Time > seconds) break;
                float d = Vector3.Distance(pos, s.Pos);
                if (d > s.Loudness) continue;
                if (d < bestD) { bestD = d; where = s.Pos; }
            }
            return bestD < float.MaxValue;
        }

        /// <summary>The men who fired in the last <paramref name="seconds"/> - the
        /// police's list of who to shout at.</summary>
        public static void ShootersSince(float seconds, List<CrewWalker> into)
        {
            float now = Time.time;
            for (int i = 0; i < count; i++)
            {
                var s = ring[(head - 1 - i + Keep) % Keep];
                if (now - s.Time > seconds) break;
                if (s.Shooter != null && !s.Shooter.Dead && !into.Contains(s.Shooter)) into.Add(s.Shooter);
            }
        }

        /// <summary>Did this faction fire in the last <paramref name="seconds"/>?</summary>
        public static bool FactionFiredSince(int faction, float seconds)
        {
            float now = Time.time;
            for (int i = 0; i < count; i++)
            {
                var s = ring[(head - 1 - i + Keep) % Keep];
                if (now - s.Time > seconds) break;
                if (s.Faction == faction) return true;
            }
            return false;
        }

        /// <summary>Did this man fire in the last <paramref name="seconds"/>?</summary>
        public static bool FiredSince(CrewWalker man, float seconds)
        {
            float now = Time.time;
            for (int i = 0; i < count; i++)
            {
                var s = ring[(head - 1 - i + Keep) % Keep];
                if (now - s.Time > seconds) break;
                if (s.Shooter == man) return true;
            }
            return false;
        }
    }
}
