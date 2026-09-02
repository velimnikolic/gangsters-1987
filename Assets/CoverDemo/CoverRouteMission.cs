using System.Collections.Generic;
using System.Text;
using RoadDemo;
using UnityEngine;

namespace CoverDemo
{
    /// <summary>A thin navigation soak for this furnished street. It issues the same
    /// direct attack order as a player right-click: one outfit crew at Falcone, then
    /// the same survivors at Santoro. It changes no combat or movement rule.</summary>
    public sealed class CoverRouteMission : MonoBehaviour
    {
        const float LegPatience = 90f;
        const int SoakHealth = 1000;

        sealed class MemberProof
        {
            public CrewWalker Man;
            public Vector3 Start;
            public Vector3 Toward;
            public float StartGap;
            public float BestGap;
            public float BestProgress;
            public float ContactWithin;
            public float ProgressNeeded;
            public bool Proved;
            public bool SaidProof;
        }

        DemoCrews _crews;
        DemoCrews.Unit _ours;
        DemoCrews.Unit _target;
        readonly List<MemberProof> _members = new List<MemberProof>();
        float _startAfter;
        float _orderedAt;
        int _faction = 1;
        int _leg;
        bool _finished;

        public void Init(DemoCrews crews, float startAfter)
        {
            _crews = crews;
            // The harness opens DriveTrace after its warm-up (normally three seconds).
            // Starting at five also leaves the scene one settled frame even if a caller
            // configured an unusually short warm-up. Update still explicitly waits for
            // DriveTrace.On below, so no order/proof can fall outside the recorded run.
            _startAfter = Mathf.Max(5f, startAfter);
        }

        void Update()
        {
            if (_finished || !DriveTrace.On || Time.timeSinceLevelLoad < _startAfter) return;
            if (_crews == null)
            {
                Fail("no DemoCrews in CoverDemo");
                return;
            }

            if (_ours == null)
            {
                _ours = FindUnit(0);
                if (_ours == null || !ArmedAndReady(_ours)) return;
                // This is a navigation soak, not a combat-balance roll. Keep the same
                // men alive long enough to traverse both independently furnished legs.
                foreach (var man in _ours.All())
                {
                    if (man == null || man.Dead) continue;
                    man.MaxHealth = Mathf.Max(man.MaxHealth, SoakHealth);
                    man.Health = man.MaxHealth;
                }
                if (!SnapshotAttackers()) return;
                OrderFaction();
                return;
            }

            if (_target == null)
            {
                Fail($"no live faction {_faction} target");
                return;
            }

            ObserveLeg();
            if (_finished) return;

            if (_target.Wiped)
            {
                if (!LegProved())
                {
                    Fail($"leg {_leg} {_target.GangName} went down before every original " +
                         "attacker proved route contact/progress: " + MissingProofs());
                    return;
                }
                DriveTrace.Event("coverroute", _target.GangName, "down");
                if (_faction >= 2)
                {
                    _finished = true;
                    DriveTrace.Event("coverroute", "complete", "2 mobs down");
                    Debug.Log("[CoverRoute] complete: Falcone then Santoro, 2 mobs down");
                    return;
                }
                _faction++;
                _target = null;
                OrderFaction();
                return;
            }

            if (Time.timeSinceLevelLoad - _orderedAt > LegPatience)
                Fail($"leg {_leg} {_target.GangName} still live after {LegPatience:F0}s; " +
                     "missing route proof: " + MissingProofs());
        }

        bool SnapshotAttackers()
        {
            _members.Clear();
            foreach (var man in _ours.All())
            {
                if (man == null || man.Dead || man.Tf == null) continue;
                _members.Add(new MemberProof { Man = man });
                TraceMember(man, "member snapshot", 0, null, 0f, 0f);
            }
            if (_members.Count > 0) return true;
            Fail("outfit has no original standing attacker to audit");
            return false;
        }

        void BeginLeg()
        {
            _leg++;
            for (int i = 0; i < _members.Count; i++)
            {
                var proof = _members[i];
                proof.Proved = false;
                proof.SaidProof = false;
                proof.BestProgress = 0f;
                if (proof.Man == null || proof.Man.Dead || proof.Man.Tf == null)
                {
                    Fail($"original attacker #{MemberId(proof.Man)} unavailable at leg {_leg} start");
                    return;
                }

                proof.Start = proof.Man.Tf.position;
                if (!NearestTarget(proof.Start, out var mark, out proof.StartGap))
                {
                    Fail($"leg {_leg} has no physical target for {MemberName(proof.Man)}");
                    return;
                }
                proof.BestGap = proof.StartGap;
                var toward = mark - proof.Start;
                toward.y = 0f;
                proof.Toward = toward.sqrMagnitude > 0.0001f
                    ? toward.normalized : Vector3.forward;
                proof.ContactWithin = Mathf.Max(4f, proof.Man.Ballistics.Range * 1.15f);

                // A long leg needs substantial forward ground, not a shuffled foot;
                // a man already in his firing envelope proves contact immediately.
                float groundToEnvelope = Mathf.Max(0f, proof.StartGap - proof.ContactWithin);
                proof.ProgressNeeded = Mathf.Clamp(groundToEnvelope * 0.65f, 2f, 12f);
            }
            if (!_finished) ObserveLeg();
        }

        void ObserveLeg()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                var proof = _members[i];
                var man = proof.Man;
                if (man == null || man.Dead || man.Tf == null)
                {
                    Fail($"original attacker #{MemberId(man)} was lost during leg {_leg}");
                    return;
                }

                if (!NearestTarget(man.Tf.position, out _, out float gap))
                {
                    // The target may all have fallen this frame. Their body transforms
                    // normally remain; if they do not, retain the last measured gap and
                    // let the explicit per-member validation decide the leg.
                    gap = proof.BestGap;
                }
                proof.BestGap = Mathf.Min(proof.BestGap, gap);

                var moved = man.Tf.position - proof.Start;
                moved.y = 0f;
                proof.BestProgress = Mathf.Max(proof.BestProgress,
                    Vector3.Dot(moved, proof.Toward));

                bool contact = gap <= proof.ContactWithin;
                bool progress = proof.BestProgress >= proof.ProgressNeeded &&
                                proof.BestGap <= proof.StartGap -
                                    Mathf.Min(1f, proof.ProgressNeeded * 0.25f);
                proof.Proved |= contact || progress;
                if (!proof.Proved || proof.SaidProof) continue;

                proof.SaidProof = true;
                TraceMember(man, "member proved", _leg,
                    contact ? "contact" : "progress", proof.BestProgress, gap);
            }
        }

        bool NearestTarget(Vector3 from, out Vector3 mark, out float gap)
        {
            mark = from;
            gap = float.MaxValue;
            if (_target == null) return false;
            foreach (var enemy in _target.All())
            {
                if (enemy == null || enemy.Tf == null) continue;
                var delta = enemy.Tf.position - from;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance >= gap) continue;
                gap = distance;
                mark = enemy.Tf.position;
            }
            return gap < float.MaxValue;
        }

        bool LegProved()
        {
            for (int i = 0; i < _members.Count; i++)
                if (!_members[i].Proved) return false;
            return _members.Count > 0;
        }

        string MissingProofs()
        {
            var missing = new StringBuilder();
            for (int i = 0; i < _members.Count; i++)
            {
                var proof = _members[i];
                if (proof.Proved) continue;
                if (missing.Length > 0) missing.Append(", ");
                missing.Append(MemberName(proof.Man)).Append('#').Append(MemberId(proof.Man))
                    .Append(" progress ").Append(proof.BestProgress.ToString("F1"))
                    .Append('/').Append(proof.ProgressNeeded.ToString("F1"))
                    .Append(" gap ").Append(proof.BestGap.ToString("F1"));
            }
            return missing.Length > 0 ? missing.ToString() : "none";
        }

        static int MemberId(CrewWalker man) => man != null ? man.Id : -1;

        static string MemberName(CrewWalker man)
            => man != null && !string.IsNullOrEmpty(man.DisplayName)
                ? man.DisplayName : "missing member";

        static void TraceMember(CrewWalker man, string what, int leg, string proof,
            float progress, float gap)
        {
            if (!DriveTrace.On) return;
            var sb = DriveTrace.Take();
            DriveTrace.Int(sb, "id", MemberId(man));
            DriveTrace.Str(sb, "who", MemberName(man));
            DriveTrace.Str(sb, "what", what);
            if (leg > 0) DriveTrace.Int(sb, "leg", leg);
            if (!string.IsNullOrEmpty(proof)) DriveTrace.Str(sb, "proof", proof);
            if (leg > 0)
            {
                DriveTrace.Num(sb, "progress", progress);
                DriveTrace.Num(sb, "gap", gap);
            }
            DriveTrace.Row("coverroute", sb.ToString());
        }

        bool ArmedAndReady(DemoCrews.Unit unit)
        {
            int standing = 0;
            foreach (var man in unit.All())
            {
                if (man == null || man.Dead || man.Tf == null) continue;
                standing++;
                if (!man.Carrying) return false;
            }
            return standing > 0;
        }

        DemoCrews.Unit FindUnit(int faction)
        {
            foreach (var unit in _crews.Units)
                if (unit != null && unit.Faction == faction && !unit.Wiped)
                    return unit;
            return null;
        }

        void OrderFaction()
        {
            _target = FindUnit(_faction);
            if (_target == null)
            {
                Fail($"no live faction {_faction} target");
                return;
            }
            _crews.Select(_ours);
            if (!_crews.OrderAttack(_target))
            {
                Fail("direct attack refused: " + (_crews.OrderRefusal ?? "no reason"));
                return;
            }
            _orderedAt = Time.timeSinceLevelLoad;
            BeginLeg();
            if (_finished) return;
            DriveTrace.Event("coverroute", _target.GangName, "ordered direct attack");
            Debug.Log("[CoverRoute] ordered direct attack on " + _target.GangName);
        }

        void Fail(string what)
        {
            _finished = true;
            Debug.LogWarning("[CoverRoute] failed: " + what);
            if (!DriveTrace.On) return;
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "tag", "coverroute");
            DriveTrace.Str(sb, "fault", "mission");
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Row("fault", sb.ToString());
            DriveTrace.Event("coverroute", "failed", what);
        }
    }
}
