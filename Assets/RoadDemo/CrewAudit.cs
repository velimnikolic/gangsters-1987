using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The crews measured against their own rules, every frame of a watched run.
    //
    // The faults this hunts were all seen by eye first - a man snapping a metre
    // sideways when an order lands, a crew threading a street in single file, a
    // round fired with the barrel in the pavement, a hood stood alone on a zebra,
    // the whole mob chasing its routed enemies while the one still shooting picks
    // them apart. An eye at the glass does not scale to thirty seeded runs; an
    // inequality in the code does. Each rule here is one of those sights turned
    // into a measurement, and a breach goes down as a "fault" row in the trace
    // (DriveTrace) for Tools/play/analyze.py --crew to fail the run over.
    //
    // Runs ONLY while the black box is recording (DemoCrews ticks it behind
    // DriveTrace.On), so a normal Play session pays one bool test.
    public static class CrewAudit
    {
        // -------------------------------------------------------------- thresholds

        /// <summary>Metres a man on foot may cover in one frame before it is not a
        /// step but a snap. Roomy: the fastest jog (~3.9 m/s), the Separate ease
        /// (half an elbow) and a long frame all together stay well under it.</summary>
        const float TeleportBound = 1.0f;

        /// <summary>Seconds off the scene's floor before it is called. The stride
        /// slides along the hem, so even a frame out is wrong - the grace only
        /// keeps a boundary-rounding jitter from counting.</summary>
        const float OffCityAfter = 0.5f;

        /// <summary>Metres off his crew, and seconds at it, that make a hood a
        /// stray. The tether (DemoCrews.TickCohesion) starts hauling at 7 m and
        /// hurries at 14 - but a 480 m leg threads four signals, and a front man
        /// held at the far kerb while the boss walks his own lights legitimately
        /// sits near 30 m for a spell. The pathologies this exists for measured
        /// 44-93 m and GREW; 32 m held for twelve seconds clears the lights and
        /// still catches those with room to spare.</summary>
        const float StrayGap = 32f, StrayAfter = 12f;

        /// <summary>Three or more men of one crew walking one stretch spread across
        /// less than this for this long are a queue, not a gang - the dealt lanes
        /// (FormationLane) exist to keep the spread above it.</summary>
        const float FileSpread = 0.8f;
        // long enough that a legitimate funnel passes before the clock runs out: a
        // bin row's one clear line, or a commuter surge on the clock hour that the
        // crew threads in file for ten seconds (same sim-second, different
        // quarters - it was the crowd, not the street). The fault this exists for -
        // the crew WALKING single file because everyone held one lane - held for
        // whole streets, minutes at a time.
        const float FileAfter = 14f;

        /// <summary>Degrees the muzzle may be off the line to the mark's chest on
        /// the frame a round leaves it. The aim overlay fires at blend >= 0.5, so
        /// half a raise is the worst that is legal; past this the man is shooting
        /// the pavement.</summary>
        const float AimOffLimit = 35f;

        /// <summary>Seconds stood at a light before the wait itself is a fault. The
        /// tether crosses a straggler after his crew, so only a whole crew waiting
        /// together waits at all - and no light holds a green this long.</summary>
        const float LightAfter = 40f;

        /// <summary>Seconds STOOD (not waiting - arrived, order done) on a zebra.
        /// No destination is ever dealt on a gated link, so standing on one means
        /// an order ended where none should.</summary>
        const float ZebraStandAfter = 8f;

        /// <summary>Seconds a man may keep shooting at a runner while a man of the
        /// same enemy crew still stands his ground in sight. TickCombat retargets
        /// every frame, so anything past a beat of it is the rule not holding.</summary>
        const float ChaseAfter = 2.5f;

        /// <summary>Ground pace below which a playing jog is a skate, and seconds of
        /// it before the fault is called. The gait gates drop a braked runner to the
        /// walk at RunRateMin x the clip's own pace (~2.7 m/s), so anything held a
        /// while under this is a gate not holding - the crossfade out of the jog and
        /// one man squeezing a car's flank both pass well inside the grace.</summary>
        const float SkatePace = 1.8f, SkateAfter = 1.5f;

        // -------------------------------------------------------------- the ledger

        class Watch
        {
            public Vector3 Last;
            public bool Seen, WasCarried;
            public float OffFor, StrayFor, LightFor, ZebraFor, ChaseFor, SkateFor;
            public float PrevGap = float.MaxValue;
            public bool SaidOff;
        }

        static readonly Dictionary<CrewWalker, Watch> Men = new Dictionary<CrewWalker, Watch>();
        static readonly Dictionary<DemoCrews.Unit, float> FileFor = new Dictionary<DemoCrews.Unit, float>();
        static readonly List<CrewWalker> FiredThisFrame = new List<CrewWalker>();
        static readonly List<CrewWalker> Walkers = new List<CrewWalker>();
        static readonly List<CrewWalker> Stretch = new List<CrewWalker>();
        static readonly List<CrewWalker> Sweep = new List<CrewWalker>();
        static float _sweepIn = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            Men.Clear();
            FileFor.Clear();
            FiredThisFrame.Clear();
            _sweepIn = 5f;
        }

        // -------------------------------------------------------------- the frame

        public static void Tick(DemoCrews arena, float dt)
        {
            if (dt <= 0f) return;
            foreach (var unit in arena.Units)
            {
                TickMen(arena, unit, dt);
                TickStray(arena, unit, dt);
                TickFile(unit, dt);
            }
            if ((_sweepIn -= dt) <= 0f) { _sweepIn = 5f; SweepGone(); }
        }

        /// <summary>A round left this man's gun this frame (DemoCrews.OnFired). The
        /// judgement waits for LateTick - the arm is not posed for the frame until
        /// AimGun has run, and measured before it this would read the raw clip.</summary>
        public static void ShotFired(CrewWalker man)
        {
            if (man != null) FiredThisFrame.Add(man);
        }

        /// <summary>After the frame's arms are posed: every shot fired this frame
        /// must have had its barrel on the mark, near enough.</summary>
        public static void LateTick()
        {
            for (int i = 0; i < FiredThisFrame.Count; i++)
            {
                var man = FiredThisFrame[i];
                if (man == null || man.Tf == null || man.Riding) continue;
                var mark = man.Target;
                if (mark == null || mark.Dead || !mark.Tf) continue;   // dropped by this very round: the aim was good enough
                var to = mark.ChestPosition - man.MuzzlePosition;
                if (to.magnitude < 2f) continue;                       // muzzle at his chest: the angle means nothing
                float off = Vector3.Angle(man.MuzzleForward, to);
                if (off > AimOffLimit)
                    Fault(man, "aimlow",
                        $"fired {off:F0} deg off {mark.DisplayName}, {to.magnitude:F1} m out");
            }
            FiredThisFrame.Clear();
        }

        // -------------------------------------------------------------- per man

        static void TickMen(DemoCrews arena, DemoCrews.Unit unit, float dt)
        {
            foreach (var man in unit.All())
            {
                if (man == null || man.Tf == null) continue;
                if (!Men.TryGetValue(man, out var w)) Men[man] = w = new Watch();

                bool afoot = !man.Dead && man.Tf.gameObject.activeSelf &&
                             !man.Riding && !arena.IsAboard(man);
                var pos = man.Tf.position;

                // THE SNAP. A man on foot moves by steps; a frame that carries him
                // further than the fastest step is a teleport, which is what the
                // player calls a respawn. The frame he is put down out of a seat is
                // the seat's business and excused.
                if (afoot && w.Seen && !w.WasCarried)
                {
                    var moved = pos - w.Last;
                    moved.y = 0f;
                    float bound = Mathf.Max(TeleportBound, 6f * dt + 0.6f);
                    if (moved.magnitude > bound)
                        Fault(man, "teleport", $"{moved.magnitude:F2} m in one frame ({man.State})");

                    // THE SKATE. A man whose legs play the jog covers jog ground -
                    // the gait gates themselves (GearGraphWalk, TickStride) drop him
                    // to the walk under the band, so a jog held over walking pace
                    // for a sustained spell means a gate is not holding.
                    if (man.JoggingPose && dt > 1e-4f && moved.magnitude / dt < SkatePace)
                    {
                        w.SkateFor += dt;
                        if (w.SkateFor > SkateAfter)
                        {
                            Fault(man, "skate", $"jogging at {moved.magnitude / dt:F1} m/s " +
                                                $"for {w.SkateFor:F1}s ({man.State})");
                            w.SkateFor = -30f;   // still watched; said again if it goes on
                        }
                    }
                    else w.SkateFor = 0f;
                }
                w.Last = pos;
                w.Seen = true;
                w.WasCarried = !afoot;
                if (!afoot)
                {
                    w.OffFor = 0f; w.SaidOff = false;
                    w.LightFor = 0f; w.ZebraFor = 0f; w.ChaseFor = 0f;
                    continue;
                }

                // THE FLOOR. The fence (WalkObstacles.City) is enforced at every
                // level; a man out on the void means one of those levels gave.
                if (WalkObstacles.City.Count > 0)
                {
                    if (!WalkObstacles.InCity(pos))
                    {
                        w.OffFor += dt;
                        if (w.OffFor > OffCityAfter && !w.SaidOff)
                        {
                            w.SaidOff = true;
                            Fault(man, "offcity", $"off the floor for {w.OffFor:F1}s ({man.State})");
                        }
                    }
                    else { w.OffFor = 0f; w.SaidOff = false; }
                }

                // THE LIGHT. Waiting is legal; waiting longer than any light holds
                // is not - and the tether crosses a straggler long before this.
                if (man.AtLight && man.State == CrewWalker.Mode.Walking)
                {
                    w.LightFor += dt;
                    if (w.LightFor > LightAfter)
                    {
                        Fault(man, "zebrastuck", $"waiting at a light for {w.LightFor:F0}s");
                        w.LightFor = -30f;   // still watched; said again if it goes on
                    }
                }
                else w.LightFor = 0f;

                // THE ZEBRA. Nobody's order ends on a crossing, so a man STOOD on
                // one finished an order nowhere near where it should have ended.
                // His link may be stale (a stride order leaves it behind), so his
                // feet have to actually be on the crossing - and ON THE ASPHALT, by
                // the same predicate the healer uses (a man at the zebra's kerb
                // mouth is on the pavement and fine; measuring him by the link line
                // alone flagged men the healer rightly left alone).
                if (man.State == CrewWalker.Mode.Standing && man.OnGraph &&
                    man.CurrentLink.Gated && NearLink(pos, man.CurrentLink, 2f) &&
                    CrewWalker.OnCarriageway(pos))
                {
                    w.ZebraFor += dt;
                    if (w.ZebraFor > ZebraStandAfter)
                    {
                        Fault(man, "zebrastuck", $"stood on a crossing for {w.ZebraFor:F0}s");
                        w.ZebraFor = -30f;
                    }
                }
                else w.ZebraFor = 0f;

                // THE ROUT. A man shooting at a runner while one of the same crew
                // still stands and fights in his sight has the wrong mark -
                // TickCombat swaps it the same frame, so any run of this is a fault.
                var tgt = man.Target;
                if (tgt != null && !tgt.Dead && (tgt.Panicked || tgt.Retreating) &&
                    unit.TargetUnit != null && FighterInSight(unit.TargetUnit, pos))
                {
                    w.ChaseFor += dt;
                    if (w.ChaseFor > ChaseAfter)
                    {
                        Fault(man, "runnerchase",
                            $"on a runner ({tgt.DisplayName}) with a fighter in sight for {w.ChaseFor:F1}s");
                        w.ChaseFor = -20f;
                    }
                }
                else w.ChaseFor = 0f;
            }
        }

        static bool FighterInSight(DemoCrews.Unit enemy, Vector3 from)
        {
            foreach (var m in enemy.All())
            {
                if (m == null || m.Dead || !m.Tf || m.Panicked || m.Retreating) continue;
                if ((m.Tf.position - from).sqrMagnitude <= DemoCrews.SightRange * DemoCrews.SightRange)
                    return true;
            }
            return false;
        }

        // -------------------------------------------------------------- the crew

        // A hood far off his crew for long: the tether's whole job is that this
        // never accumulates. Fights, boardings, panics and the law are all theirs
        // to be spread out in and excused.
        static void TickStray(DemoCrews arena, DemoCrews.Unit unit, float dt)
        {
            if (unit.Wiped || unit.IsPolice || unit.TargetUnit != null || unit.Boarding != null) return;
            var lead = unit.Boss != null && !unit.Boss.Dead ? unit.Boss : FirstStanding(unit);
            if (lead == null || lead.Tf == null || lead.Panicked || lead.Riding || arena.IsAboard(lead)) return;
            foreach (var man in unit.Hoods)
            {
                if (man == null || man == lead || man.Dead || man.Tf == null) continue;
                if (man.Panicked || man.Retreating || man.Riding || arena.IsAboard(man)) continue;
                if (!Men.TryGetValue(man, out var w)) continue;   // TickMen has not met him yet
                var gap = man.Tf.position - lead.Tf.position;
                gap.y = 0f;
                float d = gap.magnitude;
                // a man CLOSING the gap is the tether doing its work - clipped off
                // by a light, cut across after the crew, on his way back - and a man
                // the arena is actively REINING (dawdled, lingered) is managed, not
                // lost. So is a man whose ORDER aims at the crew: a post-fight
                // regroup threads city blocks, and around a corner the bare gap
                // grows for a spell while the walk is exactly right (four brawl
                // seeds failed on that alone). Only a gap held or growing on a man
                // nobody is handling is a stray.
                bool closing = d < w.PrevGap - 0.02f;
                w.PrevGap = d;
                var aim = man.Destination - lead.Tf.position;
                aim.y = 0f;
                bool coming = man.HasOrder && aim.sqrMagnitude < 15f * 15f;
                // ...and a man on the SAME ERRAND as his lead - both orders aimed at
                // one far place (a war march re-dealt to the whole crew) - is
                // coordinated, however stretched the column runs mid-march; the
                // holds and hustles are what keep it tight, and the mission's own
                // stall watchdogs catch a march that never arrives
                if (!coming && man.HasOrder && lead.HasOrder)
                {
                    var errand = man.Destination - lead.Destination;
                    errand.y = 0f;
                    coming = errand.sqrMagnitude < 20f * 20f;
                }
                if (d > StrayGap && !closing && !man.ReinedIn && !coming)
                {
                    w.StrayFor += dt;
                    if (w.StrayFor > StrayAfter)
                    {
                        Fault(man, "strayman",
                            $"{d:F0} m off {lead.DisplayName} for {w.StrayFor:F0}s ({man.State})");
                        w.StrayFor = -20f;
                    }
                }
                else if (d <= StrayGap) w.StrayFor = 0f;
            }
        }

        static CrewWalker FirstStanding(DemoCrews.Unit unit)
        {
            foreach (var m in unit.All())
                if (m != null && !m.Dead && m.Tf != null) return m;
            return null;
        }

        // Three or more of one crew walking the same stretch pressed onto one line:
        // a queue. The spread is measured ACROSS the stretch, over the men on it.
        static void TickFile(DemoCrews.Unit unit, float dt)
        {
            Walkers.Clear();
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Tf != null && man.OnGraph && !man.AtLight &&
                    !man.ReinedIn &&   // held level with his crew: stood, not walking any line
                    (man.State == CrewWalker.Mode.Walking || man.State == CrewWalker.Mode.Homing))
                    Walkers.Add(man);

            bool filed = false;
            if (Walkers.Count >= 3)
            {
                for (int i = 0; i < Walkers.Count && !filed; i++)
                {
                    Stretch.Clear();
                    var a = Walkers[i].CurrentLink;
                    for (int j = 0; j < Walkers.Count; j++)
                    {
                        var b = Walkers[j].CurrentLink;
                        if (SameStretch(a, b)) Stretch.Add(Walkers[j]);
                    }
                    if (Stretch.Count < 3) continue;
                    var dir = a.To.Pos - a.From.Pos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) continue;
                    // a stretch that only OFFERS one line is not a crew walking
                    // single file - ask the pavement's own clearance: when the two
                    // flank lanes both come back to the same free line, the
                    // furniture forced the file and the men had no width to hold
                    float tMid = Walkers[i].CurrentT;
                    if (Mathf.Abs(a.FreeLine(tMid, 2f, -1.2f) - a.FreeLine(tMid, 2f, 1.2f)) < FileSpread)
                        continue;
                    var right = new Vector3(dir.z, 0f, -dir.x).normalized;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int j = 0; j < Stretch.Count; j++)
                    {
                        float s = Vector3.Dot(Stretch[j].Tf.position, right);
                        lo = Mathf.Min(lo, s);
                        hi = Mathf.Max(hi, s);
                    }
                    filed = hi - lo < FileSpread;
                }
            }

            FileFor.TryGetValue(unit, out float held);
            held = filed ? held + dt : 0f;
            if (held > FileAfter)
            {
                var front = unit.Boss != null && !unit.Boss.Dead && unit.Boss.Tf != null
                    ? unit.Boss : FirstStanding(unit);
                if (front != null)
                    Fault(front, "singlefile",
                        $"{Stretch.Count} of the crew walking one line for {held:F0}s");
                held = -15f;
            }
            FileFor[unit] = held;
        }

        static bool SameStretch(PedLink a, PedLink b)
        {
            if (a == null || b == null) return false;
            return (a.From == b.From && a.To == b.To) || (a.From == b.To && a.To == b.From);
        }

        // -------------------------------------------------------------- plumbing

        static bool NearLink(Vector3 p, PedLink link, float within)
        {
            var ab = link.To.Pos - link.From.Pos;
            ab.y = 0f;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-4f) return false;
            var rel = p - link.From.Pos;
            rel.y = 0f;
            float t = Mathf.Clamp01(Vector3.Dot(rel, ab) / len2);
            var q = link.From.Pos + ab * t;
            q.y = p.y;
            return (p - q).sqrMagnitude < within * within;
        }

        static void SweepGone()
        {
            Sweep.Clear();
            foreach (var kv in Men)
                if (kv.Key == null || kv.Key.Tf == null) Sweep.Add(kv.Key);
            for (int i = 0; i < Sweep.Count; i++) Men.Remove(Sweep[i]);
        }

        static void Fault(CrewWalker man, string kind, string what)
        {
            var sb = DriveTrace.Take();
            DriveTrace.Int(sb, "id", man.Id);
            DriveTrace.Str(sb, "tag", "crew");
            DriveTrace.Str(sb, "fault", kind);
            DriveTrace.Str(sb, "who", man.DisplayName);
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Vec(sb, "p", man.Tf.position);
            DriveTrace.Row("fault", sb.ToString());
        }
    }
}
