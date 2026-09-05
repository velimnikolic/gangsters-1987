using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public partial class DemoCrews
    {
        readonly struct PendingRound
        {
            public readonly CrewWalker Shooter, Target;
            public readonly RoadCar Car;
            public readonly Vector3 Muzzle, From;
            public readonly Transform Follow;
            public readonly float At;
            public readonly int Sequence;
            public readonly bool Foot, Voice;

            public PendingRound(CrewWalker shooter, CrewWalker target, Vector3 muzzle,
                Vector3 from, Transform follow, float at, int sequence, bool foot, bool voice)
            {
                Shooter = shooter; Target = target; Car = shooter.CarMark;
                Muzzle = muzzle; From = from; Follow = follow; At = at;
                Sequence = sequence; Foot = foot; Voice = voice;
            }
        }

        readonly List<PendingRound> _pendingRounds = new List<PendingRound>();
        bool _collectingRounds;

        void BeginRounds()
        {
            _pendingRounds.Clear();
            _collectingRounds = true;
        }

        void QueueRound(CrewWalker shooter, CrewWalker target, Vector3 muzzle,
            Vector3 from, Transform follow, float at, bool foot = false, bool voice = false)
        {
            var round = new PendingRound(shooter, target, muzzle, from, follow, at,
                _pendingRounds.Count, foot, voice);
            if (_collectingRounds) _pendingRounds.Add(round);
            else CommitRound(round);
        }

        // A large clock step can contain several automatic rounds. Interleave all
        // shooters, including riders, before applying wounds; unit list order must
        // not let one man empty the whole step into an opponent waiting to tick.
        void FlushRounds()
        {
            _collectingRounds = false;
            _pendingRounds.Sort((a, b) =>
            {
                int time = a.At.CompareTo(b.At);
                return time != 0 ? time : a.Sequence.CompareTo(b.Sequence);
            });
            try
            {
                for (int i = 0; i < _pendingRounds.Count; i++) CommitRound(_pendingRounds[i]);
            }
            finally { _pendingRounds.Clear(); }
        }

        void CommitRound(PendingRound round)
        {
            var shooter = round.Shooter;
            if (shooter == null || shooter.Tf == null || shooter.Dead ||
                shooter.Surrendered || shooter.Panicked || !shooter.Carrying) return;
            if (round.Target != null && (round.Target.Dead || round.Target.Tf == null)) return;
            if (round.Car != null && (round.Car.Wrecked || round.Car.Tf == null)) return;
            if (round.Foot && (shooter.Target != round.Target || shooter.CarMark != round.Car)) return;
            if (round.Foot)
            {
                if (DriveTrace.On) CrewAudit.ShotFired(shooter);
                SpringAmbush(shooter);
            }
            if (round.Voice && Random.value < .16f)
                CrewSpeech.Cry(shooter, LivingCity.Data.VoiceLines.FightCurse, cooldown: 8f);
            Resolve(shooter, round.Target, round.Muzzle, round.From, round.Follow);
        }
    }
}
