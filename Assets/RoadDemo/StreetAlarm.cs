using System.Collections.Generic;
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
        public static event System.Action<Vector3, DeathOf> OnDeath;

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

        /// <summary>Seconds since the last shot (large when none).</summary>
        public static float QuietFor => Time.time - LastShotAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            head = count = 0;
            LastShotAt = -1000f;
            IncidentStart = -1000f;
            IncidentShots = CivilianDeaths = GangDeaths = OfficerDeaths = 0;
            IncidentNumber = 0;
            OnShot = null;
            OnDeath = null;
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

        /// <summary>Somebody died of the shooting.</summary>
        public static void Death(Vector3 pos, DeathOf who)
        {
            switch (who)
            {
                case DeathOf.Civilian: CivilianDeaths++; break;
                case DeathOf.Officer: OfficerDeaths++; break;
                default: GangDeaths++; break;
            }
            OnDeath?.Invoke(pos, who);
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
