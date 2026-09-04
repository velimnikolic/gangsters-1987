using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The fighting half of the crews: who a crew goes for and how it
    /// closes, the chase, the retreat, the surrender - and the round itself, from
    /// the muzzle to whoever or whatever it hits. Moved out of DemoCrews.cs whole;
    /// nothing changed in the move.</summary>
    public partial class DemoCrews
    {
        // A KILL is dealt across the crew one man at a time. Kept here and cleared for
        // every deal/retarget pass: no per-frame HashSet garbage, and no ownership of
        // combat policy in whichever overlay happened to issue the order.
        readonly HashSet<CrewWalker> _orderedMarks = new HashSet<CrewWalker>();

        List<AudioClip> _combatAudioPrewarm;
        int _combatAudioPrewarmAt;
        List<GameObject> _combatFxPrewarm;
        int _combatFxPrewarmAt;
        Dictionary<GameObject, GameObject> _prewarmedCombatFx;

        /// <summary>Queues the expensive first-use pieces while the scene is settling,
        /// long before a crew can board a car and reach its first firing window. One FX
        /// hierarchy or one short clip is touched per frame, so setup does not merely move
        /// the hitch from the trigger pull into one monolithic initialization frame.</summary>
        void PrepareCombatPrewarm()
        {
            EnsureShotAudioSources();

            _combatFxPrewarm ??= new List<GameObject>(4);
            _combatFxPrewarm.Clear();
            AddPrewarmFx(MuzzleFlashPrefab);
            AddPrewarmFx(GunSmokePrefab);
            AddPrewarmFx(ImpactPrefab);
            _combatFxPrewarmAt = 0;
            _prewarmedCombatFx ??= new Dictionary<GameObject, GameObject>();
            _prewarmedCombatFx.Clear();

            _combatAudioPrewarm ??= new List<AudioClip>();
            _combatAudioPrewarm.Clear();
            if (GunshotSets != null)
                foreach (var set in GunshotSets)
                    if (set?.Clips != null)
                        foreach (var clip in set.Clips)
                            if (clip != null && !_combatAudioPrewarm.Contains(clip))
                                _combatAudioPrewarm.Add(clip);
            if (CrackClip != null && !_combatAudioPrewarm.Contains(CrackClip))
                _combatAudioPrewarm.Add(CrackClip);
            _combatAudioPrewarmAt = 0;
        }

        void AddPrewarmFx(GameObject prefab)
        {
            if (prefab != null && !_combatFxPrewarm.Contains(prefab))
                _combatFxPrewarm.Add(prefab);
        }

        void TickCombatPrewarm()
        {
            // Instantiate only one hierarchy this frame. The inactive instance becomes
            // the first real effect, so component initialization is not repeated at fire.
            if (_combatFxPrewarm != null && _combatFxPrewarmAt < _combatFxPrewarm.Count)
            {
                var prefab = _combatFxPrewarm[_combatFxPrewarmAt++];
                if (prefab != null && !_prewarmedCombatFx.ContainsKey(prefab))
                {
                    var warmed = Instantiate(prefab, _root);
                    warmed.SetActive(false);
                    _prewarmedCombatFx[prefab] = warmed;
                }
                return;
            }

            // These imports deliberately do not preload their sample data. Ask for one
            // short report per frame so the first PlayOneShot never owns decompression.
            if (_combatAudioPrewarm == null ||
                _combatAudioPrewarmAt >= _combatAudioPrewarm.Count) return;
            var clip = _combatAudioPrewarm[_combatAudioPrewarmAt++];
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
        }

        void EnsureShotAudioSources()
        {
            if (_shots == null)
            {
                _shots = gameObject.AddComponent<AudioSource>();
                _shots.spatialBlend = 0f;
                _shots.playOnAwake = false;
            }
            if (_cracks == null)
            {
                _cracks = gameObject.AddComponent<AudioSource>();
                _cracks.spatialBlend = 0f;
                _cracks.playOnAwake = false;
            }
        }

        GameObject CombatFx(GameObject prefab, Vector3 position, Quaternion rotation,
                            Transform parent = null)
        {
            GameObject effect = null;
            if (prefab != null && _prewarmedCombatFx != null &&
                _prewarmedCombatFx.TryGetValue(prefab, out effect))
                _prewarmedCombatFx.Remove(prefab);

            if (effect == null)
                effect = parent != null
                    ? Instantiate(prefab, position, rotation, parent)
                    : Instantiate(prefab, position, rotation);
            else
            {
                effect.transform.SetParent(parent, true);
                effect.transform.SetPositionAndRotation(position, rotation);
                effect.SetActive(true);
                foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Clear(true);
                    ps.Play(true);
                }
            }
            return effect;
        }

        void SetTarget(Unit unit, Unit target) => SetTarget(unit, target, ordered: false);

        /// <summary>How far round an ordered job the traffic is thinned, and for how
        /// long. Wide enough to take in the street the job is on and the mouths of the
        /// ones either side of it; long enough for a crew to walk in, do it and leave.
        /// See StreetTraffic.Quiet - it is a stage direction and nothing more.</summary>
        public static float QuietRadius = 80f, QuietSeconds = 90f;

        void SetTarget(Unit unit, Unit target, bool ordered)
        {
            if (ordered) { CallOffRaids(unit, "an attack order"); NoteRetask(unit); }
            // an ordered job clears its own street: the player asked for this fight, so
            // the town is not left putting a bus between him and it
            if (ordered && target != null)
                StreetTraffic.Quiet(target.Position, QuietRadius, QuietSeconds);
            unit.TargetUnit = target;
            unit.OrderedFight = ordered;
            unit.SawEnemyAt = Time.time;   // the fight starts with them in sight
            // a new enemy is not the old one's track: what this crew remembers of where
            // somebody went is about the man it was watching, and it does not carry over
            unit.HasLastSeen = false;
            unit.LastSeenDir = Vector3.zero;
            unit.Searching = false;
            unit.LookUntil = 0f;
            // AN ORDERED JOB REACHES; A FIGHT PICKED UP DOES NOT. The player's KILL
            // carries across the quarter, but a crew that has just been shot at squares
            // up only on what it can SEE - handing every man the nearest of them at any
            // range was how one pass of a drive-by put a mob on a car it had lost.
            float reach = ordered ? float.MaxValue : SightRange;
            if (ordered) _orderedMarks.Clear();
            foreach (var man in unit.All())
            {
                // riding - in a car's seat or on a machine's saddle - is not a man who
                // walks up to somebody and opens fire; his gun is the vehicle's business
                // (TickRiders, CrewBike.TickGuns)
                if (!CanEngageOnFoot(man)) continue;
                var mark = ordered
                    ? ClaimOrderedMark(target, man.Tf.position, reach, sighted: false)
                    : BestMark(target, man.Tf.position, reach, sighted: true);
                if (mark != null) man.Engage(mark);
            }
        }

        bool CanEngageOnFoot(CrewWalker man) =>
            man != null && man.Tf != null && !man.Dead && man.Carrying && !man.Panicked &&
            // A MAN WHO IS NOT ON THE STREET IS NOT IN THE FIGHT. His body is switched
            // off inside a building - a doorstep visit's few seconds, or a crew moved in
            // and standing there (CrewQuarters) - and a shot fired from inside a wall is
            // not a shot anybody could have taken. A man in a car IS on the street: he
            // is sat in it, visible, and shoots from the window.
            man.Tf.gameObject.activeInHierarchy &&
            !IsAboard(man) && !man.Riding && !OnRaid(man);

        /// <summary>Give this shooter an enemy nobody in the ordered crew has yet,
        /// while one exists. Once every valid enemy already has a gun on him, fall back
        /// to the ordinary nearest-man rule: that is the end of the fight, when all of
        /// ours are allowed to close on the one or two still standing.</summary>
        CrewWalker ClaimOrderedMark(Unit target, Vector3 from, float within, bool sighted)
        {
            var mark = BestMark(target, from, within, sighted, _orderedMarks);
            if (mark == null) mark = BestMark(target, from, within, sighted);
            if (mark != null) _orderedMarks.Add(mark);
            return mark;
        }

        static CrewWalker NearestStanding(Unit unit, Vector3 from) =>
            NearestStanding(unit, from, float.MaxValue);

        /// <summary>The man of that crew this one goes for: the nearest who is still
        /// IN the fight. A man who has broken and run (or is retreating off the map)
        /// is nobody's first mark - a crew that turns, to a man, and chases its routed
        /// enemies while the one still shooting stands his ground gets picked apart by
        /// him, which is what the player watched. Only with nobody of them left
        /// fighting does the nearest runner do.</summary>
        static CrewWalker BestMark(Unit unit, Vector3 from, float within) =>
            BestMark(unit, from, within, sighted: true);

        /// <summary>The same, saying whether the man has to be able to SEE him. He
        /// does, everywhere the crew picked the fight up by watching - a wall between
        /// them and he is not a mark, whatever the range says. The one exception is a
        /// job the PLAYER ordered: the crew was given an address and closes on it, and
        /// asking a man to see through the block he is walking round would cancel the
        /// order at the first corner.</summary>
        static CrewWalker BestMark(Unit unit, Vector3 from, float within, bool sighted,
                                   HashSet<CrewWalker> excluded = null)
        {
            CrewWalker fighting = null, running = null;
            float limit = within * within;
            float fd = limit, rd = limit;
            bool fightingAvailable = false;
            foreach (var m in unit.All())
            {
                if (m.Dead || !m.Tf) continue;
                // Nor is a man INDOORS a mark. Nobody aims at a body that has gone
                // through a door: the line would be drawn to a point inside the
                // building (DoorBeat, CrewQuarters).
                if (!m.Tf.gameObject.activeInHierarchy) continue;
                float d = (m.Tf.position - from).sqrMagnitude;
                bool runner = m.Panicked || m.Retreating;
                // the range first and the walls after: a look down the sight line is
                // several cells of the ground walked, and most men are out of range
                if (d >= limit) continue;
                if (sighted && !InSight(from, m.Tf.position)) continue;
                if (!runner) fightingAvailable = true;
                if (excluded != null && excluded.Contains(m)) continue;
                if (d >= (runner ? rd : fd)) continue;
                if (runner) { rd = d; running = m; }
                else { fd = d; fighting = m; }
            }
            // Do not take an unclaimed runner while somebody still fighting is already
            // claimed. The old combat priority stands: duplicate a live threat before
            // streaming off after a man who has broken.
            return fighting ?? (fightingAvailable ? null : running);
        }

        /// <summary>Is there anything but air between these two - can the first see the
        /// second? The city's walls only (WalkObstacles.Sees): a bin is cover, not a
        /// hiding place.</summary>
        static bool InSight(Vector3 eye, Vector3 mark) => WalkObstacles.Sees(eye, mark);

        /// <summary>Does this particular man still have his own live mark in sight?
        /// Usually it belongs to Unit.TargetUnit. Return fire may deliberately give
        /// just the man a shooter from a second crew, so callers which move bodies
        /// must ask his eyes as well as the unit's strategic target.</summary>
        static bool SeesPersonalTarget(CrewWalker man) =>
            man != null && man.Tf != null && man.Target != null &&
            !man.Target.Dead && man.Target.Tf != null &&
            man.Target.Tf.gameObject.activeInHierarchy &&
            (man.Target.Tf.position - man.Tf.position).sqrMagnitude <
                SightRange * SightRange &&
            InSight(man.Tf.position, man.Target.Tf.position);

        // ------------------------------------------------ the closer threat (EPIC 33)

        /// <summary>The street distance between two points - HORIZONTAL, both marks
        /// measured the same way (D8). A man's transform is at his feet and his chest is
        /// a metre and a bit above it; comparing a 3D distance to one mark against a 3D
        /// distance to another on a kerb reads a rise the fight does not care about.
        /// BestMark's own nearest-man pick is left on its 3D measure, unchanged.</summary>
        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// IS SOMEBODY ELSE THE MORE IMMEDIATE THREAT? The whole of the user's rule:
        /// a man aiming at somebody fifteen metres off, with somebody else twelve
        /// metres off and in his eyeline, is aiming at the wrong man - the shorter shot
        /// is the one about to kill him.
        ///
        /// This asks about NOW and nothing else. Where the candidate came from, where
        /// he spawned, how far he has walked: none of it is read and none of it is
        /// stored (acceptance 2). Two current distances and a margin.
        ///
        /// The one asymmetry, and it is deliberate: the current mark may be a man he
        /// CANNOT see - an ordered KILL is an address, and a crew closing on it round a
        /// block keeps the job (BestMark's `sighted: false`). A candidate may never be.
        /// A man may hold an address he cannot see; he may only be pulled off it by
        /// somebody he can see.
        ///
        /// Returns the nearest qualifying candidate, or null when nobody beats the mark
        /// he has. Nothing is allocated: the crew's members are walked in place.
        /// </summary>
        static CrewWalker CloserThreatThan(Unit enemies, CrewWalker man, CrewWalker mark,
            float within, out float markDistXZ, out float candidateDistXZ)
        {
            markDistXZ = 0f;
            candidateDistXZ = 0f;
            if (enemies == null || man == null || man.Tf == null) return null;
            if (mark == null || mark.Dead || mark.Tf == null) return null;

            var eye = man.Tf.position;
            markDistXZ = FlatDistance(eye, mark.Tf.position);
            float margin = CrewSkill.ThreatMargin(man.CombatHalfSteps);
            // A MARK STILL IN THE FIGHT IS NOT GIVEN UP FOR A MAN RUNNING AWAY. The old
            // combat priority, unchanged: only a shooter whose own mark has already
            // broken may be turned onto another runner, and then only when nobody of
            // them is still fighting (acceptance 6).
            bool markRunning = mark.Panicked || mark.Retreating;

            CrewWalker fighting = null, running = null;
            float fd = float.MaxValue, rd = float.MaxValue;
            bool fightingQualifies = false;
            foreach (var m in enemies.All())
            {
                if (m == mark || m == man || m.Dead || !m.Tf) continue;
                if (!m.Tf.gameObject.activeInHierarchy) continue;   // indoors: not there
                float d = FlatDistance(eye, m.Tf.position);
                if (d >= within) continue;
                // the cheap comparison before the walls, as everywhere in this file
                if (d + margin > markDistXZ) continue;
                if (!InSight(eye, m.Tf.position)) continue;
                bool runner = m.Panicked || m.Retreating;
                if (!runner)
                {
                    fightingQualifies = true;
                    if (d < fd) { fd = d; fighting = m; }
                }
                else if (d < rd) { rd = d; running = m; }
            }
            if (fighting != null) { candidateDistXZ = fd; return fighting; }
            if (markRunning && !fightingQualifies && running != null)
            {
                candidateDistXZ = rd;
                return running;
            }
            return null;
        }

        /// <summary>
        /// One shooter's frame of it: watch the closer man, and turn onto him once the
        /// advantage has HELD for as long as his hands need to notice it.
        ///
        /// The dwell is a held condition, not a timer phase (D2). The stamp lives on the
        /// man; a candidate who lapses for a single frame, or a different candidate
        /// crossing the margin, puts it back to zero. That is why the same geometry gives
        /// the same answer every run, and why two men stood nearly level cannot flicker
        /// the aim between them - after A gives way to B, B is what the next candidate
        /// has to beat by the whole margin all over again.
        /// </summary>
        void TickCloserThreat(Unit unit, CrewWalker man, float reach)
        {
            var candidate = CloserThreatThan(unit.TargetUnit, man, man.Target, reach,
                                             out float markDist, out float candDist);
            if (candidate == null) { man.ForgetThreat(); return; }
            man.WatchThreat(candidate, Time.time);
            float heldFor = Time.time - man.ThreatHeldSince;
            if (!CrewSkill.ShouldSwitch(markDist, candDist, man.CombatHalfSteps, heldFor))
                return;

            var left = man.Target;
            bool wasInCover = man.InCover;
            man.Engage(candidate, closerThreat: true);
            // HIS SURVIVAL MAY DUPLICATE A MARK. An ordered fight deals one shooter per
            // enemy, and this is the one thing allowed to break that - the man he left
            // is picked back up by the uncovered pass a frame or two later (AIM-004).
            if (unit.OrderedFight) _orderedMarks.Add(candidate);
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", man.DisplayName);
                DriveTrace.Int(sb, "combat", man.CombatHalfSteps);
                DriveTrace.Str(sb, "left", left != null ? left.DisplayName : "nobody");
                DriveTrace.Str(sb, "onto", candidate.DisplayName);
                DriveTrace.Num(sb, "was", markDist);
                DriveTrace.Num(sb, "now", candDist);
                DriveTrace.Num(sb, "margin", CrewSkill.ThreatMargin(man.CombatHalfSteps));
                DriveTrace.Num(sb, "dwell", CrewSkill.ThreatDwell(man.CombatHalfSteps));
                DriveTrace.Num(sb, "held", heldFor);
                DriveTrace.Bool(sb, "kept", wasInCover && man.InCover);
                DriveTrace.Bool(sb, "ordered", unit.OrderedFight);
                DriveTrace.Row("switch", sb.ToString());
            }
        }

        /// <summary>Is this man's mark held by somebody else of his crew who is
        /// actually fighting? A rider's stale mark is nobody's gun: he is shooting out
        /// of a window at whatever the vehicle's own rules give him, and counting him
        /// here would move a man off a mark that in truth has only one gun on it.</summary>
        bool Duplicated(Unit unit, CrewWalker man)
        {
            foreach (var other in unit.All())
                if (other != man && other.Target == man.Target && CanEngageOnFoot(other))
                    return true;
            return false;
        }

        /// <summary>Is this man worth a gun at all - alive, out on the street, and in
        /// the fight? The mark test BestMark applies, without the range or the walls.</summary>
        static bool ValidMark(CrewWalker m) =>
            m != null && !m.Dead && m.Tf != null && m.Tf.gameObject.activeInHierarchy;

        /// <summary>
        /// THE MAN HE LEFT DOES NOT GET A FREE FIGHT (D4).
        ///
        /// A closer-threat switch is allowed to double up on a mark, which leaves an
        /// enemy with nobody's gun on him - and today's retarget only fires when
        /// somebody dies or breaks, so he would be free to shoot until one of ours went
        /// down. After the ordered rebuild, an enemy nobody has is offered to the
        /// nearest shooter whose own mark is a duplicate.
        ///
        /// Two refusals, and they are the point of the pass rather than exceptions to
        /// it: the man who switched to save himself is never the one moved back off the
        /// threat, and a shooter whose own closer-threat rule holds him where he is does
        /// not take the offer - it would put him on a man the margin says is materially
        /// farther than one he can see.
        ///
        /// One reassignment a crew a frame. The next frame does the next: at sixty
        /// frames a second a five-man crew is dealt out again inside a tenth of a second,
        /// and the alternative is a rebuild that walks every man against every enemy
        /// every frame for a case that arises once a fight.
        /// </summary>
        void CoverTheUncovered(Unit unit)
        {
            var enemies = unit.TargetUnit;
            if (enemies == null) return;

            bool fightersRemain = false;
            foreach (var enemy in enemies.All())
                if (ValidMark(enemy) && !enemy.Panicked && !enemy.Retreating)
                {
                    fightersRemain = true;
                    break;
                }

            CrewWalker uncovered = null;
            foreach (var enemy in enemies.All())
            {
                if (!ValidMark(enemy)) continue;
                // As long as one enemy is still fighting, every available gun belongs
                // on that fight. A runner is considered only after no fighter remains,
                // never merely because all fighters already have one shooter.
                if (fightersRemain && (enemy.Panicked || enemy.Retreating)) continue;
                bool taken = false;
                foreach (var man in unit.All())
                    if (man.Target == enemy && CanEngageOnFoot(man)) { taken = true; break; }
                if (!taken) { uncovered = enemy; break; }
            }
            if (uncovered == null) return;

            CrewWalker mover = null;
            float best = float.MaxValue;
            foreach (var man in unit.All())
            {
                if (!CanEngageOnFoot(man) || man.Target == null) continue;
                if (man.SwitchedForThreat) continue;          // never off his own threat
                if (!Duplicated(unit, man)) continue;         // he is the only gun on his
                float d = FlatDistance(man.Tf.position, uncovered.Tf.position);
                if (d >= best) continue;
                // would this offer put him on a man farther off than a visible threat?
                if (CloserThreatThan(enemies, man, uncovered, float.MaxValue,
                                     out _, out _) != null) continue;
                best = d;
                mover = man;
            }
            if (mover == null) return;
            mover.Engage(uncovered);
            _orderedMarks.Add(uncovered);
            if (DriveTrace.On)
                DriveTrace.Event("uncovered", mover.DisplayName,
                    "picked up " + uncovered.DisplayName + " " + best.ToString("F1") + " m off");
        }

        /// <summary>Can anybody of this crew see that man, at all? What a crew SHOT AT
        /// is asked before it is handed the shooter's whole crew as its enemy: a round
        /// out of a car going past the end of the street is a bang and a man down, not
        /// an address. Without this, one pass of a drive-by gave every mob in the
        /// quarter a live handle on the outfit and they came across the city for it.</summary>
        static bool Spotted(Unit unit, CrewWalker man)
        {
            if (unit == null || man == null || man.Tf == null) return false;
            foreach (var a in unit.All())
            {
                if (a.Dead || a.Tf == null) continue;
                if ((a.Tf.position - man.Tf.position).sqrMagnitude > SightRange * SightRange) continue;
                if (Concealed(man, a.Tf.position)) continue;   // he is down behind a bin waiting for them
                if (InSight(a.Tf.position, man.Tf.position)) return true;
            }
            return false;
        }

        internal static bool AnswerCrossUnitAttackerModel(bool hasCurrentEnemyUnit,
            bool sameEnemyUnit, bool attackerVisible, bool canEngage) =>
            hasCurrentEnemyUnit && !sameEnemyUnit && attackerVisible && canEngage;

        internal static bool OrderedAddressAppliesModel(bool unitOrderedFight,
            bool personalTargetBelongsToStrategicUnit) =>
            unitOrderedFight && personalTargetBelongsToStrategicUnit;

        /// <summary>The enemy crew nearest THIS MAN that he can actually see - his own
        /// eyes, not his crew's. What a man handed back into the street on his own asks
        /// (a rider whose machine went down under him): the crew he belongs to may be
        /// two hundred metres away and know nothing about it.
        ///
        /// The law is left out. A man does not pick a fight with the police because he
        /// can see one - that is PoliceDispatch's business and the warning shout's.</summary>
        Unit SeenBy(CrewWalker man)
        {
            if (man == null || man.Tf == null || man.Dead) return null;
            var mine = UnitOf(man);
            Unit best = null;
            float bestD = SightRange * SightRange;
            foreach (var other in Units)
            {
                if (other == mine || other.Wiped || other.IsPolice) continue;
                if (mine != null && other.Faction == mine.Faction) continue;
                foreach (var b in other.All())
                {
                    if (b.Dead || b.Tf == null || IsAboard(b)) continue;
                    float d = (b.Tf.position - man.Tf.position).sqrMagnitude;
                    if (d >= bestD) continue;
                    if (Concealed(b, man.Tf.position)) continue;   // lying in wait: not there yet
                    if (!InSight(man.Tf.position, b.Tf.position)) continue;
                    bestD = d;
                    best = other;
                }
            }
            return best;
        }

        /// <summary>The nearest man of that crew to this point, out to a limit. Past the
        /// limit he is not "far away", he is NOT THERE - see <see cref="SightRange"/>.</summary>
        static CrewWalker NearestStanding(Unit unit, Vector3 from, float within)
        {
            CrewWalker best = null;
            float bestD = within * within;
            foreach (var m in unit.All())
            {
                if (m.Dead || !m.Tf) continue;
                if (!m.Tf.gameObject.activeInHierarchy) continue;   // indoors: not there
                float d = (m.Tf.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = m; }
            }
            return best;
        }

        // Keeps every crew's fight honest each frame: a crew whose enemy is wiped
        // lowers its guns; a man whose target fell picks the next one; a rival crew
        // that sees the outfit walk up opens fire on its own.
        void TickCombat()
        {
            foreach (var unit in Units)
            {
                // A CREW WITH ITS HANDS UP IS NOT IN THE FIGHT. Not its own, not the
                // one going on round it: it stands where it stood and waits to be
                // taken. Skipped whole rather than disarmed man by man, because every
                // branch below - the retarget, the watch, the shot-back rule - would
                // otherwise put a gun back in its hands the same frame.
                if (unit.Surrendered) continue;
                if (unit.TargetUnit != null && unit.TargetUnit.Wiped)
                {
                    // End the dead strategic fight through the same exit that knows
                    // how to promote a still-visible personal return-fire target.
                    // Clearing every man here made a second live attacker disappear
                    // from their minds merely because the first crew fell.
                    unit.OrderedFight = false;
                    EndSearch(unit);
                }

                // A BEATEN CREW FIGHTS ON - EVERY HOUSE'S ALIKE (D23, row 1, the
                // user's word of 2026-09-03). A crew whose lieutenant is dead and
                // whose last man is standing does not leave the fight because it is
                // losing it: no house's does. A rival's used to walk home at exactly
                // that point while ours stood and died, which was the fight going
                // differently for the player than for anybody else. The only way off
                // the pavement now is the same for both: an order (RUN FOR IT), or a
                // man's own nerve breaking.
                if (unit.Retreated) { TakeOffRetreated(unit); continue; }

                // THE AMBUSH SPRINGS ITSELF (COVER-004). Whatever the stance below
                // makes of the pair, a crew the player put behind a bin and told to
                // wait is the one fight he asked it to start. A rival family's man
                // inside the crew's best gun reach and in sight of one of the waiting
                // men, and they open up from where they are lying. Never the law and
                // never a civilian; ordered, because the player ordered the wait.
                if (unit.TargetUnit == null && AnyLurking(unit))
                {
                    var sprung = LurkQuarry(unit);
                    if (sprung != null) SetTarget(unit, sprung, ordered: true);
                }

                // WHO FIGHTS WHOM IS THE STANCE BETWEEN THE TWO HOUSES, and nothing
                // else (D13). It is the same rule for every pair in the city, the
                // player's included, and it is the three sentences the FAMILIES card
                // prints: war on sight, truce on the ground the engager leads, peace
                // never - except that a man being shot at turns and returns fire, and
                // except the ambush above, which the player laid with his own hands.
                //
                // Until this, a rival crew watched for the outfit only and the outfit
                // started nothing. That made the player the only family anybody could
                // fall out with, which is the whole of what this epic is against.
                //
                // The law is not answered here: a warning shout is PoliceWarning's
                // business, and a crew is not put at war with the police by a stray round.
                if (unit.TargetUnit == null && !unit.IsPolice)
                {
                    // MEN COMING AT YOU WITH THEIR GUNS OUT ARE A PROVOCATION (the
                    // user's word, 2026-09-04: "neprijateljski crew vidi manje od
                    // nasih"). A crew SENT at this one - the player's KILL, a Sic -
                    // walks up with the pieces drawn; the first round used to be the
                    // first thing this crew noticed, at any distance, while the
                    // outfit had closed on it from the whole of its sight. Now a
                    // drawn gun in sight, inside the same SightRange the outfit
                    // works with, is a shot as far as this crew's nerve goes.
                    var coming = EnemyComing(unit);
                    if (coming != null) unit.ProvokedAt = Time.time;
                    var provoked = Time.time - unit.ProvokedAt < FightBack &&
                                   Time.time - unit.OrderedAt > HoldFireAfterOrder;
                    var seen = coming != null && provoked
                        ? coming
                        : EnemyWithin(
                            unit, provoked ? DefendRange : AlertRange, provoked,
                            noPolice: true);
                    if (seen != null) SetTarget(unit, seen);
                }

                if (unit.TargetUnit == null) continue;
                // Riders fire from the windows, not on foot - and they are skipped man
                // by man below. The UNIT is only stood down when there is nobody of it
                // left on the pavement: a crew whose car pulled away with one or two men
                // still outside it is not a carload, and skipping it whole left those
                // men standing in the street for the rest of the run with no fight, no
                // walk and no tether (the tether lets a crew with a target alone). Men
                // still walking to their doors are not "on foot" for this - their order
                // is the door, and a fight would pull them off it.
                if (unit.Car != null && !AnyOnFoot(unit)) continue;

                // Each man goes for the nearest of them HE CAN SEE. Nobody in sight is
                // not the same as nobody left: the crew holds its ground with its guns
                // up, and if the enemy stays out of sight the fight is given up
                // altogether rather than walked across the quarter after.
                bool anySeen = false;
                var seenAt = Vector3.zero;
                float seenNear = float.MaxValue;
                // AN ORDERED JOB IS NOT A SIGHTING. The player clicked the man, so the
                // crew has his address and closes on it from any distance - the
                // out-of-sight drop below must never cancel the job it was ordered to
                // do (a KILL given across the quarter was one frame of engagement and
                // then a crew stood still for good, because the outfit does not chase).
                // Fights a crew picked up by WATCHING keep SightRange: that drop is
                // what stopped a man tracking a motorcycle three streets off.
                float reach = unit.OrderedFight ? float.MaxValue : SightRange;
                if (unit.OrderedFight)
                {
                    _orderedMarks.Clear();
                    foreach (var man in unit.All())
                        if (CanEngageOnFoot(man) && man.Target != null &&
                            !man.Target.Dead && man.Target.Tf != null)
                            _orderedMarks.Add(man.Target);
                }
                foreach (var man in unit.All())
                {
                    if (!CanEngageOnFoot(man)) continue;
                    // Keep the ordered ADDRESS separate from what this man can SEE.
                    // An ordered job may quite properly hand him a mark round a corner,
                    // but that mark is not evidence that his eyes are still on anybody.
                    // Conversely, seeing another member of the enemy crew must not keep
                    // the timestamp on his hidden current mark fresh forever.
                    var visibleMark = BestMark(unit.TargetUnit, man.Tf.position,
                                               SightRange, sighted: true);
                    var mark = unit.OrderedFight
                        ? BestMark(unit.TargetUnit, man.Tf.position, reach, sighted: false)
                        : visibleMark;
                    if (visibleMark != null && visibleMark.Tf != null)
                    {
                        anySeen = true;
                        float d = (visibleMark.Tf.position - man.Tf.position).sqrMagnitude;
                        if (d < seenNear)
                        {
                            seenNear = d;
                            seenAt = visibleMark.Tf.position;
                        }
                    }
                    bool seesTarget = SeesPersonalTarget(man);
                    if (seesTarget) man.NoteTargetSeen();
                    // A MAN OUT RUNNING AFTER HIM STILL HAS EYES. He counts toward what
                    // the crew can see (above - he is usually the NEAREST of them to the
                    // enemy, being the one who went after him), but his feet are the
                    // chase's to order, not this loop's: TickChase turns his run into a
                    // fight the moment he lays eyes on anybody.
                    if (Chasing(man)) continue;
                    // A MARK HE CANNOT SEE ANY MORE IS NOT HIS MARK. This used to be
                    // asked only of a mark who had DIED - so a man engaged on a rider
                    // kept him while the machine rode the length of the quarter, and
                    // TickEngage walked him at the live transform of a motorcycle three
                    // streets away that he could not possibly have eyes on. Out of
                    // sight, the gun comes down; what the CREW does about it is the
                    // chase below, and that is laid against a remembered place.
                    bool ownsOrderedAddress = OrderedAddressAppliesModel(
                        unit.OrderedFight,
                        man.Target != null && UnitOf(man.Target) == unit.TargetUnit);
                    if (!ownsOrderedAddress && man.Target != null &&
                        !man.Target.Dead && !seesTarget)
                    {
                        // not on the frame he disappears. A fight runs past parked vans
                        // and round the corners of buildings, and a man whose gun came
                        // down the instant a mark stepped behind one and up again the
                        // instant he stepped out is a man twitching, not fighting.
                        if (Time.time - man.SawMarkAt < BlindGrace) continue;
                        // If somebody else is plainly in front of him, turn onto that
                        // live threat instead of standing idle beside the firefight.
                        // Only the strategic ordered mark stays an unseen address. A
                        // personal return-fire target from another crew never inherits
                        // that privilege merely because the unit has an order on.
                        if (visibleMark != null) man.Engage(visibleMark);
                        else if (!man.GuardCoverAfterLostSight(LoseSight))
                            man.Disengage();
                        continue;
                    }
                    // a mark that has since broken and run is dropped for one still
                    // fighting - the whole crew does not stream off after the runners
                    // while the man who stood his ground shoots them in the back
                    bool chasingRunner = man.Target != null && !man.Target.Dead &&
                        (man.Target.Panicked || man.Target.Retreating);
                    if (man.Target == null || man.Target.Dead ||
                        (chasingRunner && mark != null && !mark.Panicked && !mark.Retreating))
                    {
                        var next = unit.OrderedFight
                            ? ClaimOrderedMark(unit.TargetUnit, man.Tf.position, reach,
                                               sighted: false)
                            : mark;
                        if (next != null) man.Engage(next);
                        else if (!man.RenewCoverGuard(LoseSight)) man.Disengage();
                        continue;
                    }
                    // HE KEEPS HIS MARK - unless somebody else has become the more
                    // immediate threat (EPIC 33). Asked last on purpose: the dead, the
                    // vanished and the broken are all dealt with above and stay
                    // immediate, and the skill's dwell governs only a VOLUNTARY switch
                    // between two otherwise valid living marks.
                    TickCloserThreat(unit, man, reach);
                }

                // and the man somebody left to save himself gets a gun put back on him
                if (unit.OrderedFight) CoverTheUncovered(unit);

                if (anySeen)
                {
                    // the way he is going, kept only while it is a real movement - a man
                    // shuffling on the spot must not spin the remembered heading round.
                    // The FIRST sighting sets the place and no heading at all: there is
                    // nothing to have moved from yet.
                    if (unit.HasLastSeen)
                    {
                        var moved = seenAt - unit.LastSeenPos;
                        moved.y = 0f;
                        if (moved.sqrMagnitude > 1f) unit.LastSeenDir = moved.normalized;
                    }
                    else unit.HasLastSeen = true;
                    unit.LastSeenPos = seenAt;
                    unit.SawEnemyAt = Time.time;
                    continue;
                }

                if (unit.Car != null) continue;   // a crew in a car does not chase, and does not lose the job
                if (StartChase(unit)) continue;
                if (unit.OrderedFight) continue;   // a job stands until it is done
                if (Time.time - unit.SawEnemyAt < LoseSight) continue;
                // MEN ARE OUT LOOKING FOR HIM. The fight is not written off under their
                // feet - it is what they are asking BestMark about every frame they run,
                // and dropping it eight seconds in was what left them running at nothing
                // and stopping the moment somebody went behind a building. The search
                // ends it, when it ends (EndSearch).
                //
                // Unless there is no search left to end it: nobody out on their feet and
                // the last leg longer ago than a leg can last (a crew that got into a car
                // mid-search, one whose runners were all shot). Without this the fight
                // stands for the rest of the scene on a crew that is not looking for
                // anybody.
                if (unit.Searching && !AnyChasing(unit) &&
                    Time.time - unit.ChasedAt > ChaseSeconds)
                {
                    EndSearch(unit);
                    continue;
                }
                if (unit.Searching) continue;
                // The same exit owns both cleanup and promotion of any still-visible
                // personal return-fire target. Duplicating half of it here used to
                // erase that promotion in the very same frame.
                EndSearch(unit);
            }
        }

        /// <summary>Send a few of them running to THE PLACE THEY LAST SAW HIM.
        ///
        /// The whole discipline of this is in what it is NOT given: the enemy's crew is
        /// never asked where it is. The men are sent to a point, worked out once, from
        /// the last place anybody actually laid eyes on him - so if the machine took a
        /// turning they never saw, they run to the mouth of it and stand there looking,
        /// which is what the player asked for.
        ///
        /// A search is made of LEGS. They run to the point; if they see him from there
        /// the fight starts again and, when they lose him again, the next leg is laid
        /// from the new last-seen place - round the corner, down the next street, as
        /// long as he keeps showing himself. If they get there and there is nothing to
        /// see, they stand a moment and go home. Nothing carries them past
        /// <see cref="SearchRange"/> from the door they started at.</summary>
        bool StartChase(Unit unit)
        {
            if (unit == null || unit.TargetUnit == null) return false;
            // THE MOBS RUN; THE OUTFIT AND THE LAW DO NOT. A chase is the arena moving
            // men on its own initiative, and the player's crews are the player's to
            // move - a lieutenant sent to hold a corner who took himself forty metres
            // down the street after a passing machine would be the game playing itself.
            // The police have a dispatcher of their own (PoliceDispatch) and answer to
            // that, exactly as they do everywhere else in this loop.
            if (unit.Faction == 0 || unit.IsPolice) return false;
            if (unit.Retreated || unit.Car != null) return false;
            if (!unit.HasLastSeen) return false;                         // never laid eyes on him
            if (Time.time - unit.SawEnemyAt < ChaseAfter) return false;  // he may only be behind a van
            if (Time.time - unit.ChasedAt < ChaseAgainAfter) return false;
            if (AnyChasing(unit)) return false;                          // a leg is already running

            // where this search started - the door they are to end up back at
            if (!unit.Searching)
            {
                unit.SearchHome = unit.Position;
                unit.Searching = true;
                // A cover-only search has no running leg to stamp this below. Give it
                // the same finite lifetime now, or an all-covered crew would either
                // end immediately off the default zero or search forever.
                unit.ChasedAt = Time.time;
                unit.ChaseUntil = Time.time + ChaseSeconds;
            }

            var after = unit.LastSeenPos;
            var reach = after - unit.SearchHome;
            reach.y = 0f;
            // too far from their own door: the search is over, whatever they think they
            // saw. They are walked back by the tether the moment nobody is chasing.
            if (reach.magnitude > SearchRange) { EndSearch(unit); return true; }

            int sent = 0;
            bool defenderStayed = false;
            foreach (var man in unit.All())
            {
                if (sent >= Chasers) break;
                if (man == null || man.Tf == null || man.Dead || !man.Carrying) continue;
                if (man.Panicked || man.Retreating || man.Riding || IsAboard(man)) continue;
                if (OnRaid(man) || Chasing(man)) continue;
                // A search party is drawn from the men who are free to move. A man
                // whose mark only just crossed a wall is still doing useful work:
                // guarding the shielding flank he reached. Pulling him out after
                // ChaseAfter (1.5 s) silently defeated the longer lost-sight lease and
                // recreated the stand-up-and-run behaviour that lease exists to stop.
                if (!ChaseCandidateModel(
                        ordinarilyEligible: true,
                        guardingCover: man.GuardingCover,
                        hasVisiblePersonalTarget: SeesPersonalTarget(man)))
                {
                    defenderStayed = true;
                    continue;
                }
                man.Disengage();
                // spread out across the street rather than running in one another's
                // footprints - three men on one point is a queue, not a chase. Across
                // the way he was last going when there is one, else across the way they
                // are running.
                var along = unit.LastSeenDir.sqrMagnitude > 0.01f
                    ? unit.LastSeenDir
                    : (after - man.Tf.position).normalized;
                var across = Vector3.Cross(Vector3.up, along).normalized;
                var spot = after + across * ((sent - 1) * 1.6f);
                // the stagger is COUNTED, not drawn. A draw here would be three more
                // pulls on the shared stream every time a machine went past a door,
                // which moves every later draw in the run and makes two soaks of the
                // same seed two different runs (see the prop bags' single stream).
                man.OrderToPoint(WalkObstacles.FreeSpot(spot, WalkObstacles.Radius, 6f),
                                 sent * 0.12f);
                man.Hustle = true;      // at a run, with the ground to cover for one
                man.Urgent = true;
                _chasers.Add(man);
                sent++;
            }
            // If everybody available is still guarding cover, the fight and search
            // remain alive. They can reacquire from the flank while the finite search
            // runs; its EndSearch releases them. A man with a visible personal target
            // stays in that fight and may become available after it ends. With nobody
            // available for either reason, the old end-search behaviour still applies.
            if (sent == 0)
            {
                if (!defenderStayed) { EndSearch(unit); return true; }
                return false;
            }
            unit.LookUntil = 0f;
            unit.ChaseUntil = Time.time + ChaseSeconds;
            unit.ChasedAt = Time.time;
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", unit.GangName);
                DriveTrace.Int(sb, "men", sent);
                DriveTrace.Vec(sb, "after", after);
                DriveTrace.Num(sb, "from", reach.magnitude);
                DriveTrace.Row("chase", sb.ToString());
            }
            return false;
        }

        internal static bool ChaseCandidateModel(bool ordinarilyEligible,
            bool guardingCover, bool hasVisiblePersonalTarget) =>
            ordinarilyEligible && !guardingCover && !hasVisiblePersonalTarget;

        internal static bool DropAtEndSearchModel(bool dead, bool chasing,
            bool guardingCover, bool hasPersonalTarget, bool personalTargetProtected) =>
            !dead && !chasing &&
            (guardingCover || (hasPersonalTarget && !personalTargetProtected));

        internal static bool ActiveChaserAtSearchEndModel(bool registeredChaser,
            bool queuedForRemoval) => registeredChaser && !queuedForRemoval;

        /// <summary>Has this crew anybody out on their feet after somebody?</summary>
        bool AnyChasing(Unit unit)
        {
            if (unit == null) return false;
            foreach (var man in unit.All()) if (Chasing(man)) return true;
            return false;
        }

        /// <summary>The search is over: the fight is written off, the guns come down,
        /// and the crew WALKS BACK to the door it set off from.
        ///
        /// The march home is not the tether's to do. The tether hangs a crew on its own
        /// lieutenant, and he runs with them - so a crew that had chased sixty metres
        /// down the street was simply moored there, and three mobs ended a run standing
        /// wherever the last thing they saw had gone. The search remembers the door
        /// (SearchHome) precisely so somebody can send them back to it.</summary>
        void EndSearch(Unit unit)
        {
            if (unit == null) return;
            bool searched = unit.Searching;
            unit.Searching = false;
            unit.LookUntil = 0f;
            unit.ChaseUntil = 0f;
            unit.HasLastSeen = false;
            unit.LastSeenDir = Vector3.zero;
            if (!unit.OrderedFight) unit.TargetUnit = null;
            Unit visiblePersonalEnemy = null;
            foreach (var man in unit.All())
            {
                bool visiblePersonal = SeesPersonalTarget(man);
                // TickChase removes in a second pass to keep its HashSet enumerator
                // valid. A reacquirer already queued in _chaseDone is nevertheless
                // finished NOW for this cleanup and must be eligible for promotion.
                bool activeChaser = ActiveChaserAtSearchEndModel(
                    Chasing(man), _chaseDone.Contains(man));
                bool ownsOrderedAddress = OrderedAddressAppliesModel(
                    unit.OrderedFight,
                    man.Target != null && UnitOf(man.Target) == unit.TargetUnit);
                if (DropAtEndSearchModel(
                        man.Dead, activeChaser, man.GuardingCover,
                        man.Target != null, visiblePersonal || ownsOrderedAddress))
                    man.Disengage();
                else if (!man.Dead && !activeChaser && man.Target != null &&
                         visiblePersonal && visiblePersonalEnemy == null)
                    visiblePersonalEnemy = UnitOf(man.Target);
            }
            // A visible personal return-fire target must not become an orphan after
            // the old strategic search ends: TickCombat is unit-driven and would skip
            // its future LOS/drop lifecycle with TargetUnit null. Promote that live
            // fight to the unit now that the old one is over.
            if (!unit.OrderedFight && visiblePersonalEnemy != null)
            {
                SetTarget(unit, visiblePersonalEnemy);
                return;
            }
            if (!searched || unit.OrderedFight || unit.Retreated || unit.Car != null) return;
            var strayed = unit.Position - unit.SearchHome;
            strayed.y = 0f;
            // a crew that never left its door is left where it stands: a march order laid
            // on men who are already home is three men shuffling on the spot
            if (strayed.sqrMagnitude > 25f) MarchTo(unit, unit.SearchHome);
        }

        /// <summary>The search, frame by frame. A leg ends four ways: they see him
        /// (and it is a fight again, and the next loss of sight lays the next leg), they
        /// get to the place and nothing is there (they stand looking, then go home), the
        /// walk never arrives (ChaseSeconds), or they have been drawn SearchRange from
        /// their own door and stop wherever they are.
        ///
        /// Nothing here reads the enemy's position. The only question asked of him is
        /// whether he is IN SIGHT of the man running - which is the same question every
        /// other man of the crew is asked, through the same door (BestMark), walls and
        /// all.</summary>
        void TickChase()
        {
            if (_chasers.Count == 0) return;
            _chaseDone.Clear();
            foreach (var man in _chasers)
            {
                if (man == null || man.Tf == null || man.Dead || man.Panicked ||
                    man.Retreating || man.Riding || IsAboard(man))
                { _chaseDone.Add(man); continue; }

                var unit = UnitOf(man);
                // the search was given up this frame - by another of them getting to the
                // end of it, or by the fight being dropped from under them
                if (unit == null || !unit.Searching) { _chaseDone.Add(man); continue; }

                var drawn = man.Tf.position - unit.SearchHome;
                drawn.y = 0f;
                if (drawn.magnitude > SearchRange)
                {
                    _chaseDone.Add(man);
                    EndSearch(unit);
                    continue;
                }

                var mark = unit.TargetUnit != null
                    ? BestMark(unit.TargetUnit, man.Tf.position, SightRange) : null;
                if (mark != null)
                {
                    // there he is: this leg is over and a fight has started. The search
                    // itself is not - lose him again and the next leg goes from wherever
                    // he was standing when they last had eyes on him.
                    unit.SawEnemyAt = Time.time;
                    unit.LookUntil = 0f;
                    man.Engage(mark);
                    man.NoteTargetSeen();
                    _chaseDone.Add(man);
                    continue;
                }

                // got there (or the walk gave out): he stands in the road looking down
                // it. The crew gets ChaseLook of that between them, and if nobody has
                // seen anything by the end of it the search is over and the tether walks
                // them back to their own door.
                if (!man.HasOrder || Time.time > unit.ChaseUntil)
                {
                    man.Hustle = false;
                    man.Urgent = false;
                    if (unit.LookUntil <= 0f) unit.LookUntil = Time.time + ChaseLook;
                    if (Time.time < unit.LookUntil) continue;
                    _chaseDone.Add(man);
                    EndSearch(unit);
                }
            }
            foreach (var man in _chaseDone) EndChase(man);
            _chaseDone.Clear();
        }

        readonly List<CrewWalker> _chaseDone = new List<CrewWalker>();

        /// <summary>This man is not running after anybody any more. His feet go back to
        /// his own pace; where he goes next is the tether's business, not the chase's -
        /// a man given a fresh order here would be re-ordered by it a frame later.</summary>
        void EndChase(CrewWalker man)
        {
            if (man == null) return;
            _chasers.Remove(man);
            if (man.Tf == null || man.Dead) return;
            man.Hustle = false;
            man.Urgent = false;
        }

        // A retreating man who has stopped running is out of sight: gone.
        // Scratch for the men taken off this tick, kept: it is asked every frame per
        // retreated crew, and a fresh list each time was garbage for nothing.
        readonly List<CrewWalker> _gone = new List<CrewWalker>();

        /// <summary>
        /// Off the street the way a family's men are meant to leave it: home, through
        /// their own door. The billet does the rest - the march, the file-in, and the
        /// holding - and while they are held the crew is neither a target nor a threat.
        /// Answers false when the family has no door to reach, which is the only case
        /// left where a beaten crew simply runs.
        /// </summary>
        void TakeOffRetreated(Unit unit)
        {
            // A man walking home is not gone - he is on his way to his own door, and
            // the billet will take him in when he gets there.
            if (CrewQuarters.Billeted(unit))
                return;

            _gone.Clear();
            foreach (var man in unit.All())
                if (!man.Dead && man.Retreating && man.State == CrewWalker.Mode.Standing) _gone.Add(man);
            foreach (var man in _gone)
            {
                if (man.Tf) man.Tf.gameObject.SetActive(false);
                if (unit.Boss == man) unit.Boss = null;
                unit.Hoods.Remove(man);
                man.Dispose();
                if (man.Tf) Destroy(man.Tf.gameObject);
            }
        }

        /// <summary>The law has shouted its warning at the scene, AT EVERY HOUSE IN
        /// EARSHOT ALIKE (D23, row 3, the user's word of 2026-09-03) - and nobody at
        /// war lowers their guns for it. Mid-fight a crew stays on the enemy it has
        /// (police rounds pull it round later, through the shot-back rule); guns free,
        /// it turns them on the squad now. A rival crew used to roll two in five to
        /// break off and run, which is the one thing no house does any more: leaving a
        /// fight is an order or a man's own nerve, never a shout. The law is a third
        /// side of the war, not a referee.</summary>
        public void PoliceWarning(Vector3 from, Unit police)
        {
            foreach (var unit in Units)
            {
                if (unit.IsPolice || unit.Wiped || unit.Surrendered) continue;
                if ((unit.Position - from).sqrMagnitude > 45f * 45f) continue;
                if (unit.TargetUnit == null && police != null) SetTarget(unit, police);
            }
        }

        float _chatScan = 3f;

        // Two men of one crew stood near each other with nothing on will stop for a
        // word - a crew on a corner is company, not a rank. Never mid-fight, never
        // across crews (a hood does not chat up another lieutenant's man on the
        // street), and never the same two again straight after.
        void PairChats()
        {
            foreach (var unit in Units)
            {
                if (unit.IsPolice || unit.InCustody || unit.Surrendered ||
                    unit.TargetUnit != null) continue;
                // one word at a time per crew: the rest keep watch
                bool talking = false;
                foreach (var m in unit.All()) if (m.Chatting) { talking = true; break; }
                if (talking) continue;
                var men = new List<CrewWalker>();
                foreach (var m in unit.All())
                    if (m.Loitering && m.Tf && !IsAboard(m)) men.Add(m);
                for (int i = 0; i < men.Count; i++)
                {
                    var a = men[i];
                    if (a.Chatting) continue;
                    for (int j = i + 1; j < men.Count; j++)
                    {
                        var b = men[j];
                        if (b.Chatting) continue;
                        if ((a.Tf.position - b.Tf.position).sqrMagnitude > 3.4f * 3.4f) continue;
                        if (Random.value > 0.45f) continue;
                        float seconds = Random.Range(8f, 16f);
                        a.BeginChat(b, seconds, speaksFirst: true);
                        b.BeginChat(a, seconds, speaksFirst: false);
                        talking = true;
                        break;
                    }
                    if (talking) break;
                }
            }
        }

        // ------------------------------------------------------------------ the round

        /// <summary>A crew with an ORDER on this one, one of whose men is on foot with
        /// his gun out, in sight of one of these men inside SightRange. Never the
        /// law, never a car going by, never a man lying in wait.</summary>
        Unit EnemyComing(Unit unit)
        {
            float r2 = SightRange * SightRange;
            foreach (var other in Units)
            {
                if (other == unit || other.Faction == unit.Faction || other.Wiped) continue;
                if (other.IsPolice || other.TargetUnit != unit || !other.OrderedFight) continue;
                foreach (var a in unit.All())
                {
                    if (a.Dead) continue;
                    foreach (var b in other.All())
                        // on his feet and in the fight: not a rider, not a passenger,
                        // not a man off on a raid, not one with his hands up or running
                        if (CanEngageOnFoot(b) && b.Armed && !b.Surrendered &&
                            !b.Retreating &&
                            (a.Tf.position - b.Tf.position).sqrMagnitude < r2 &&
                            !Concealed(b, a.Tf.position) &&
                            InSight(a.Tf.position, b.Tf.position))
                            return other;
                }
            }
            return null;
        }

        Unit EnemyWithin(Unit unit, float range, bool provoked, bool noPolice = false)
        {
            float r2 = range * range;
            foreach (var other in Units)
            {
                if (other == unit || other.Faction == unit.Faction || other.Wiped) continue;
                if (noPolice && other.IsPolice) continue;
                if (!MayEngage(unit, other, provoked)) continue;
                foreach (var a in unit.All())
                {
                    if (a.Dead) continue;
                    // a man in a car is just a car going by until somebody shoots
                    foreach (var b in other.All())
                        // close enough AND in view: a crew on the far side of a block of
                        // flats has not "seen the outfit walk up", whatever the tape says -
                        // and a man LYING IN WAIT is not walking up at all (COVER-004)
                        if (!b.Dead && !IsAboard(b) &&
                            (a.Tf.position - b.Tf.position).sqrMagnitude < r2 &&
                            !Concealed(b, a.Tf.position) &&
                            InSight(a.Tf.position, b.Tf.position))
                            return other;
                }
            }
            return null;
        }

        /// <summary>
        /// MAY THESE MEN START SOMETHING? The pair's stance decides it, read from the
        /// city's one relations book, with the ground under the OTHER crew's feet as the
        /// truce's test - a truce is territorial, so the question is whose street they
        /// are standing in.
        ///
        /// A scene with no underworld and no territory falls back on peace, which is
        /// what a bench with two crews and no city ought to be.
        /// </summary>
        static bool MayEngage(Unit unit, Unit other, bool provoked)
        {
            if (unit.Faction < 0 || other.Faction < 0)
                return false;

            var relations = LivingCity.Outfit.Underworld.Current?.Relations;
            if (relations == null)
                return provoked;

            var stance = relations.StanceBetween(unit.Faction, other.Faction);
            var runtime = TerritoryRuntime.Instance;
            var ours = runtime != null &&
                       runtime.LeaderAt(other.Position).Value == unit.Faction;
            return LivingCity.Outfit.Engagement.May(stance, ours, provoked);
        }

        /// <summary>A shot left this man's gun: the flash, the bang, and the roll for
        /// the man he was aiming at. Being shot at is provocation enough - the target's
        /// crew answers if it has nobody else on its hands.</summary>
        void OnFired(CrewWalker shooter)
        {
            if (DriveTrace.On) CrewAudit.ShotFired(shooter);
            // the round is what springs an ambush, not the order that gave him the mark
            SpringAmbush(shooter);
            Resolve(shooter, shooter.Target, shooter.MuzzlePosition, shooter.Tf.position,
                CrewArms.MuzzleOf(shooter.Weapon) ?? shooter.Tf);
        }

        /// <summary>One shot, from a shooter the arena is not itself driving: the same
        /// roll, the same wound, the same flash, the same report on the street. What it
        /// is for is the pillion of a motorcycle (CrewBike) - a drive-by whose muzzle is
        /// nowhere near a car window, and which would otherwise have had to grow a
        /// second copy of the ballistics to fire a round.</summary>
        public void FireFrom(CrewWalker shooter, CrewWalker mark, Transform follow = null)
        {
            if (shooter == null || shooter.Dead || shooter.Tf == null) return;

            // THE CURSING IS THE THINNEST THING IN THE GAME. A man empties a magazine in
            // seconds and forty of them are firing at once; one shot in six, and never
            // twice from the same mouth inside eight seconds, is the difference between a
            // street fight and a football crowd.
            if (Random.value < 0.16f)
                CrewSpeech.Cry(shooter, LivingCity.Data.VoiceLines.FightCurse, cooldown: 8f);

            Resolve(shooter, mark, shooter.MuzzlePosition, shooter.Tf.position,
                follow != null ? follow : (CrewArms.MuzzleOf(shooter.Weapon) ?? shooter.Tf));
        }

        /// <summary>The nearest man of this crew still on his feet - who a drive-by
        /// shoots at as it comes past.</summary>
        public static CrewWalker NearestOf(Unit unit, Vector3 from) =>
            unit == null ? null : NearestStanding(unit, from);

        /// <summary>Nothing left of this crew worth shooting: wiped, or every man of it
        /// down or running. The drive-by is over.</summary>
        public static bool Finished(Unit unit) => unit == null || unit.Wiped || Beaten(unit);

        /// <summary>
        /// THE ONE HIT PROBABILITY, and it stays the only thing that decides a hit
        /// (EPIC 33: the scatter must never roll a second chance). Lifted out of
        /// Resolve whole so the roll can be made BEFORE the flash is pointed - nothing
        /// in the arithmetic changed in the move.
        ///
        /// The gun's accuracy holds to half its reach and falls to half of itself at
        /// the edge; a lieutenant is a better shot; a man in a car has the door and the
        /// sill, and a moving car speed on top of it; a man crouched behind a bin has
        /// its flank. Nothing is ever certain, and a shotgun in a man's face very
        /// nearly is.
        /// </summary>
        float HitChance(CrewWalker shooter, CrewWalker target, Vector3 from, float dist)
        {
            var stats = shooter.Ballistics;
            float reach = Mathf.Max(stats.Range, 1f);
            float falloff = dist <= reach * 0.5f ? 1f : Mathf.Lerp(1f, 0.5f, (dist / reach - 0.5f) / 0.5f);
            // THE MAN BEHIND THE GUN. Until the ledger's Combat stat reached this
            // line a five-star shot and a man who had never held a pistol put the same
            // rounds into the same door, and the whole attribute sheet decided nothing
            // but a warning on a job card. 0.82 of the gun's own accuracy at one star,
            // 1.30 at five - wide enough to feel, narrow enough that a shotgun in a
            // man's face is still a shotgun in a man's face.
            float p = stats.Accuracy * falloff * CrewSkill.Aim(shooter.CombatHalfSteps);
            if (shooter.IsLieutenant) p += 0.08f;
            // A man in a car has the door and the sill between him and the round - and if
            // the car is MOVING he has speed as well: hitting a rider going past at ten
            // metres a second, from a pavement, with a pistol, is a different shot from
            // hitting one sat still in traffic. Nothing else in the fight rewards keeping
            // the car rolling; this does, and it is why a crew that is left standing in
            // the road under fire is wiped and one making passes is not.
            if (IsAboard(target))
            {
                p *= CarCover;
                var carriage = CarWith(target);
                if (carriage != null)
                    p *= Mathf.Lerp(1f, MovingCarCover, Mathf.InverseLerp(1.5f, 11f, Mathf.Abs(carriage.Speed)));
            }
            else if (target.InCover) p *= target.Ducked ? DuckedCover : BehindCover;
            return Mathf.Clamp(p, 0.04f, 0.98f);
        }

        /// <summary>
        /// WHERE A MISSED ROUND ACTUALLY WENT. The aim line turned by a random angle
        /// inside the cone - yaw across the street and a narrower pitch up and down,
        /// because a round that misses a man goes past his shoulder rather more often
        /// than over his head.
        ///
        /// An angle and not an offset (D6). Range is not fed into it and must not be:
        /// distance widens a cone on its own, which is exactly the reading the user
        /// asked for - the same bad shot who puts a round a hand's breadth wide at five
        /// metres puts it well past a man at twenty-five.
        /// </summary>
        static Vector3 Scatter(Vector3 aim, float degrees)
        {
            // The two angles come off CrewSkill so the cone is BOUNDED and provable
            // offline: yaw and pitch drawn independently at their own maxima would put
            // a corner round outside the cone the table advertises and the trace
            // reports, which is a shooter quietly worse than his sheet says he is.
            CrewSkill.MissAngles(degrees, Random.value, Random.value,
                                 out float yaw, out float pitch);
            var right = Vector3.Cross(Vector3.up, aim);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            var up = Vector3.Cross(aim, right).normalized;
            return (aim + right * Mathf.Tan(yaw * Mathf.Deg2Rad)
                        + up * Mathf.Tan(pitch * Mathf.Deg2Rad)).normalized;
        }

        /// <summary>The direction from the muzzle to a point, falling back on the aim
        /// line when the two are on top of one another - a normalize of nothing is a
        /// flash pointing at the horizon.</summary>
        static Vector3 Toward(Vector3 muzzle, Vector3 at, Vector3 aim)
        {
            var line = at - muzzle;
            return line.sqrMagnitude < 1e-6f ? aim : line.normalized;
        }

        /// <summary>
        /// How far the round travelled and where it struck, following the direction it
        /// was actually fired down. A wall stops it (the city's fixed geometry, the same
        /// map the sight lines are drawn against); the pavement stops it when it is
        /// dipping; and a round that meets neither inside the gun's spent range is spent
        /// where it stops being anybody's business - a puff on the ground at the end of
        /// its run, so a miss is always seen to be a miss.
        /// </summary>
        float MissImpact(Vector3 muzzle, Vector3 line, float reach, out Vector3 spot)
        {
            float span = reach * 1.5f;
            float travel = span;
            bool struck = false;

            var flat = new Vector3(line.x, 0f, line.z);
            float flatLen = flat.magnitude;
            if (flatLen > 1e-3f)
            {
                float ahead = span * flatLen;
                float run = WalkObstacles.ClearOfWalls(muzzle, flat, ahead);
                if (run < ahead - 0.05f) { travel = run / flatLen; struck = true; }
            }
            if (line.y < -1e-3f)
            {
                float toGround = (GroundY + 0.02f - muzzle.y) / line.y;
                if (toGround > 0f && toGround < travel) { travel = toGround; struck = true; }
            }
            spot = muzzle + line * travel;
            if (!struck) spot.y = GroundY + 0.02f;
            return travel;
        }

        /// <summary>A puff of dust where a round ended up.</summary>
        void Puff(Vector3 at)
        {
            if (!ImpactPrefab) return;
            var puff = CombatFx(ImpactPrefab, at, Quaternion.LookRotation(Vector3.up));
            Destroy(puff, 2f);
        }

        /// <summary>One shot, wherever it left from: a man's gun on the pavement, or a
        /// car window on a pass. <paramref name="from"/> is where the shooter stands
        /// for the range - the man, or the car he is in.</summary>
        void Resolve(CrewWalker shooter, CrewWalker target, Vector3 muzzle, Vector3 from, Transform follow)
        {
            // where he is pointing: at the man, whatever the last centimetre of the grip
            // does to the barrel
            var aim = target != null ? (target.ChestPosition - muzzle).normalized : shooter.MuzzleForward;
            var stats = shooter.Ballistics;
            bool live = target != null && !target.Dead;
            float dist = live ? Vector3.Distance(from, target.Tf.position) : 0f;
            float p = live ? HitChance(shooter, target, from, dist) : 0f;
            // THE ROLL COMES FIRST AND THE FLASH FOLLOWS IT (AIM-003). It used to be the
            // other way round, which is why three things about one round could disagree:
            // the flash pointed at the man, the puff landed past him a metre to one side,
            // and the bystander check ran down the centreline through both. One roll, one
            // direction, and everything the round does afterwards uses it.
            bool hit = live && Random.value < p;

            // A ROUND THAT MISSED A MAN IN A CAR MOSTLY WENT INTO THE CAR. It was going
            // at the car - it is what he is sitting in - so most of the misses are a hole
            // in a door rather than a puff off the road ten metres past him. This is the
            // whole of the damage model's input: shoot at men in a car for long enough
            // and the car is what you hit (CrewCar.TakeRound).
            //
            // DECIDED HERE, BEFORE THE FLASH, because "the tin is their direction" is
            // part of the one-path rule and not an exception to it (EPIC 33). The hole
            // is chosen first; the flash is then pointed at the hole, the puff is struck
            // there, and no scatter cone is applied at all - the round did not go wide,
            // it went into the thing the man is sitting in. A round that misses BOTH the
            // man and the tin falls through to the cone below.
            CrewCar tin = null;
            CrewBike machine = null;
            var hole = Vector3.zero;
            if (live && !hit)
            {
                var carriage = IsAboard(target) ? CarWith(target) : null;
                var bike = carriage == null && target.Riding ? BikeWith(target) : null;
                if (carriage != null && carriage.Tf != null && Random.value < RoundsIntoTheTin)
                {
                    tin = carriage;
                    hole = TinHole(carriage, muzzle);
                }
                else if (bike != null && bike.Tf != null && Random.value < RoundsIntoTheMachine)
                {
                    machine = bike;
                    hole = MachineHole(bike, muzzle);
                }
            }
            // and the man shooting up an empty car has the same one path: every round
            // finds it, so the hole is where he is firing
            if (!live && target == null && shooter.CarMark != null && shooter.CarMark.Tf != null)
            {
                tin = shooter.CarMark;
                hole = TinHole(tin, muzzle);
            }

            bool intoTin = tin != null || machine != null;
            float cone = live && !hit && !intoTin
                ? CrewSkill.MissConeDegrees(stats.Accuracy, shooter.CombatHalfSteps) : 0f;
            var line = cone > 0f ? Scatter(aim, cone)
                     : intoTin ? Toward(muzzle, hole, aim)
                     : aim;
            Flash(muzzle, line, follow, shooter != null ? shooter.WeaponKind
                                                       : EquipmentKind.Pistol);
            // the street hears it: the crowd, the traffic, the police - and every man of
            // every crew in earshot with nothing on his hands turns and draws
            StreetAlarm.Report(muzzle, shooter, shooter.Faction, stats.Loudness);
            float ear2 = stats.Loudness * stats.Loudness;
            // a copy again, and of the crews as well as their men: hearing a shot can put
            // a man on the run (and the law on the street), and either changes these lists
            _heard.Clear();
            foreach (var unit in Units)
                foreach (var man in unit.All())
                    if (man != shooter && !man.Dead && man.Tf && (man.Tf.position - muzzle).sqrMagnitude < ear2)
                        _heard.Add(man);
            foreach (var man in _heard) man.HearShot(muzzle);
            if (!live)
            {
                // NOBODY TO ROLL AGAINST AND STILL A MARK. A man put on a machine has
                // no man to hit or miss - the tin IS the target, so every round finds
                // it, and the damage model reads exactly the rounds it reads from a
                // miss into a door (PutRoundIntoTin, CrewCar.TakeRound).
                if (tin != null) PutRoundIntoTin(tin, muzzle, hole);
                return;
            }

            float reach = Mathf.Max(stats.Range, 1f);
            // a crew shot at shoots back - unless it has just been ordered off (it can be
            // pulled back); a crew shot at IN ITS CAR always does, from the windows,
            // wherever the car is going: the order stands, the guns come out anyway
            var victimUnit = UnitOf(target);
            var shooterUnit = UnitOf(shooter);
            // Rounds are coming at this crew, hit or miss: it has a fight now whether it
            // went looking for one or not. Police used to be excluded here, so a crew
            // already fighting a gang simply absorbed police fire without answering.
            if (victimUnit != null && shooterUnit != null)
            {
                victimUnit.ProvokedAt = Time.time;
                var fightingGang = victimUnit.TargetUnit != null &&
                    !victimUnit.TargetUnit.IsPolice;
                if (!victimUnit.IsPolice &&
                    LivingCity.Police.PoliceProcedure.PoliceInterventionCreatesDefence(
                        shooterUnit.IsPolice, fightingGang))
                    victimUnit.PoliceAttackedIncident = StreetAlarm.IncidentNumber;
            }
            // and both crews' fights are hot now - the victim's whoever fired, the
            // law's rounds included: a crew sent at the police walks in cold until
            // the first round lands round it, not until it fires back
            HeatFight(shooterUnit);
            HeatFight(victimUnit);

            var policeOpenedFireOnVictim = victimUnit != null &&
                LivingCity.Police.PoliceProcedure.IsDefensivePoliceReturn(
                    victimUnit.PoliceAttackedIncident, StreetAlarm.IncidentNumber);
            bool mayAnswer = victimUnit != null && shooterUnit != null &&
                LivingCity.Police.PoliceProcedure.CrewMayAnswerAttacker(
                    shooterUnit.IsPolice, policeOpenedFireOnVictim) &&
                (IsAboard(target) || Time.time - victimUnit.OrderedAt > HoldFireAfterOrder);
            bool shooterSpotted = mayAnswer && Spotted(victimUnit, shooter);
            // AND THE FIGHT ITSELF IS ONLY EVER PICKED UP OFF SOMEBODY IN SIGHT. Being
            // shot at is provocation (above) and provocation is answered by looking
            // round for whoever is there to answer - it is not knowledge of who fired.
            // A car that shot up a doorway and turned the corner is gone; the crew it
            // shot at keeps its guns up and its temper, and finds nobody.
            if (victimUnit != null && victimUnit.TargetUnit == null && shooterSpotted)
                SetTarget(victimUnit, shooterUnit);

            // A UNIT HAS ONE STRATEGIC ENEMY; A MAN STILL HAS EYES. CoverDemo commonly
            // has three player crews in the same street. If this victim's crew is
            // already fighting one of them, replacing TargetUnit would make every mate
            // abandon that fight - but ignoring this visible shooter leaves the man who
            // was just hit facing away like a clay pigeon. Give only the victim the
            // immediate threat. The ordinary closer-threat/allocation passes can fold
            // him back into the crew fight after the danger has passed.
            bool targetSeesShooter = mayAnswer && CanEngageOnFoot(target) &&
                target.Tf != null && shooter != null && shooter.Tf != null &&
                (target.Tf.position - shooter.Tf.position).sqrMagnitude <=
                    SightRange * SightRange &&
                !Concealed(shooter, target.Tf.position) &&
                InSight(target.Tf.position, shooter.Tf.position);
            if (AnswerCrossUnitAttackerModel(
                    victimUnit != null && victimUnit.TargetUnit != null,
                    victimUnit != null && victimUnit.TargetUnit == shooterUnit,
                    targetSeesShooter, CanEngageOnFoot(target)))
            {
                target.Engage(shooter, closerThreat: true);
                target.NoteTargetSeen();
                if (DriveTrace.On)
                    DriveTrace.Event("shotback", target.DisplayName,
                        "turned on " + shooter.DisplayName + " across the crew fight");
            }

            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "from", shooter.DisplayName);
                DriveTrace.Int(sb, "fac", shooter.Faction);
                DriveTrace.Str(sb, "gun", shooter.WeaponKind.ToString());
                DriveTrace.Str(sb, "at", target.DisplayName);
                DriveTrace.Int(sb, "atfac", target.Faction);
                DriveTrace.Num(sb, "dist", dist);
                DriveTrace.Num(sb, "p", p, "F3");
                DriveTrace.Bool(sb, "aboard", IsAboard(target));
                DriveTrace.Bool(sb, "cover", target.InCover);
                DriveTrace.Bool(sb, "ducked", target.Ducked);
                // and the SHOOTER's own: `cover` above has always been about the man
                // being shot at, and EPIC 28's whole question is about the man pulling
                // the trigger - did this round leave from behind something
                DriveTrace.Bool(sb, "fromcover", shooter.InCover);
                DriveTrace.Str(sb, "state", shooter.State.ToString());
                DriveTrace.Vec(sb, "muzzle", muzzle);
                // ONE PATH, WRITTEN DOWN (acceptance 10). `hit` is the roll that had
                // already been made when the flash was pointed; `cone` is the widest
                // this man's miss could have been with this gun; `ray` is the direction
                // the round actually took, and it is the one the flash, the impact and
                // the bystander check all use. A run of these rows is the proof, and it
                // is also how the scatter is measured without a screenshot: `off` is how
                // far the round left the aim line, in degrees.
                DriveTrace.Bool(sb, "hit", hit);
                DriveTrace.Bool(sb, "tin", intoTin);
                DriveTrace.Int(sb, "combat", shooter.CombatHalfSteps);
                DriveTrace.Num(sb, "cone", cone);
                DriveTrace.Num(sb, "off", Vector3.Angle(aim, line));
                DriveTrace.Vec(sb, "ray", line);
                DriveTrace.Row("shot", sb.ToString());
            }

            if (!hit)
            {
                // THE TIN, DECIDED AND POINTED AT ABOVE. Its path ends at the door it
                // holed: nothing carries on past it down the street, so the bystander
                // check is not asked and no second impact is struck.
                if (tin != null)
                {
                    PutRoundIntoTin(tin, muzzle, hole);
                    target.UnderFire();
                    return;
                }
                if (machine != null)
                {
                    PutRoundIntoMachine(machine, muzzle, hole);
                    target.UnderFire();
                    return;
                }
                // ONE RESOLVED DIRECTION, AND EVERYTHING THE ROUND DOES FOLLOWS IT: the
                // flash above was pointed down it, the puff is struck where it meets the
                // ground or a wall, and the bystander in its way is the one standing in
                // ITS path rather than on the line to a man it never went near.
                float travel = MissImpact(muzzle, line, reach, out var spot);
                Puff(spot);
                target.UnderFire();
                StrayRound(muzzle, line, travel, from);
                return;
            }
            // RANK-003. A round that would put the Don down is spent on his detail
            // first, man by man: whoever is in front of him takes it, or loses his
            // nerve and is not in front of him at all. Only when there is nobody left
            // does the round reach him - which is what makes permadeath a consequence
            // of how thin he left the detail rather than a coin that came up wrong.
            if (DetailStoppedIt(target, stats.Damage))
            {
                CrewSkill.Landed(shooter.CharacterId);
                target.UnderFire();
                return;
            }

            target.TakeHit(stats.Damage, shooter);
            // A round that found its mark is the only shooting practice the game
            // recognises - firing off a magazine into a wall teaches nobody anything.
            CrewSkill.Landed(shooter.CharacterId);
            if (DriveTrace.On)
                DriveTrace.Event("hit", shooter.DisplayName, target.DisplayName,
                    $"\"dist\":{dist:F1},\"dead\":{(target.Dead ? "true" : "false")}");
            // a man hit in the car bleeds in the car - nothing on the road outside it,
            // and the same for a man on a moving motorcycle: what lands on the road
            // lands where he does (TickBikes, when his spill settles)
            CrewGore.Hit(target, from, GroundY, floor: !IsAboard(target) && !target.Riding);
            // a man one hit from the ground may lose his nerve and run - not all do
            // (not out of a car: a rider has nowhere to run to)
            if (!target.Dead)
            {
                if (!IsAboard(target))
                {
                    if (target.MaybePanic(shooter, PanicChance))
                        OnFled(target, from);
                }
            }
            else
            {
                CrewGore.Death(target, GroundY, floor: !IsAboard(target) && !target.Riding);
                _deaths.Add((target, Time.time + DeathReportDelay));
                var officer = target.Faction == StreetAlarm.PoliceFaction;
                var defensivePoliceReturn = officer && shooterUnit != null &&
                    LivingCity.Police.PoliceProcedure.IsDefensivePoliceReturn(
                        shooterUnit.PoliceAttackedIncident,
                        StreetAlarm.IncidentNumber);
                StreetAlarm.Death(target.Tf.position,
                    officer ? StreetAlarm.DeathOf.Officer : StreetAlarm.DeathOf.Gangster,
                    target.Faction, defensivePoliceReturn);
                // a friend going down beside a man may break him: he runs, and comes
                // back when his nerve does (the law does not run)
                if (victimUnit != null && !victimUnit.IsPolice && !IsAboard(target))
                {
                    // A COPY of the crew: a man who breaks at the sight of it runs, and
                    // the running takes him off his crew's books on the spot - the list
                    // cannot be walked while that is happening to it.
                    _mates.Clear();
                    foreach (var mate in victimUnit.All()) _mates.Add(mate);
                    foreach (var mate in _mates)
                    {
                        if (mate == target || mate.Dead || mate.Panicked || IsAboard(mate) || !mate.Tf) continue;
                        if ((mate.Tf.position - target.Tf.position).sqrMagnitude > NerveRange * NerveRange) continue;
                        if (Random.value < (mate.IsLieutenant ? BossNerve : HoodNerve))
                        {
                            // A nearby death is a temporary break: `comeBack` is the
                            // contract. Only the critical-wound panic above is handed
                            // to OnFled and allowed to become permanent desertion.
                            mate.PanicFrom(
                                shooter, from, 15f, 25f, comeBack: true);
                        }
                    }
                }
            }
        }

        // A round that missed its man carries on: a bystander stood in its way past him
        // may take it - the same wounds as anyone, and a killing the police weigh heaviest.
        //
        // ALONG THE ROUND THAT WAS ACTUALLY FIRED (D5). This used to be asked down the
        // centreline to the man, so the visible puff went one way and the dangerous path
        // another, and a bystander two metres off the aim line was safe from every round
        // in the fight. Now it is the scattered ray, out to where the round stopped -
        // and the consequence is deliberate: a poor shot with an automatic hits people
        // down the street, the police weigh those bodies as they weigh any, and that is
        // the price of putting a machine pistol in the hands of a man who cannot shoot.
        void StrayRound(Vector3 muzzle, Vector3 line, float travel, Vector3 from)
        {
            var civ = CivilianAgent.InLine(muzzle, line, travel, 0.7f);
            if (civ == null || Random.value >= StrayChance) return;
            if (DriveTrace.On) DriveTrace.Event("stray", "round", "a civilian was hit");
            civ.TakeHit(1, from);
            CrewGore.Hit(civ, from, GroundY);
            if (civ.Dead) CrewGore.Death(civ, GroundY);
        }

        /// <summary>Of the rounds that miss a man sat in a car, this many hit the car.
        /// Most of them: the car is the thing being aimed at, near enough.</summary>
        const float RoundsIntoTheTin = 0.72f;

        /// <summary>And of the rounds that miss a man on a MOTORCYCLE, this many hit the
        /// machine. Far fewer than a car takes, and it is the honest reason: a car is a
        /// wall behind the man and a motorcycle is a frame between his knees, so most of
        /// what goes past him goes past everything.</summary>
        const float RoundsIntoTheMachine = 0.38f;

        /// <summary>Which machine this man is riding, or null. A short list walked, like
        /// CarWith: there are two or three of these in a city, not two hundred.</summary>
        CrewBike BikeWith(CrewWalker man)
        {
            if (man == null) return null;
            for (int i = 0; i < Bikes.Count; i++)
            {
                var bike = Bikes[i];
                if (bike != null && (bike.Rider == man || bike.Pillion == man)) return bike;
            }
            return null;
        }

        /// <summary>A round that missed the man and found the machine under him. Where
        /// along it decides what it cost - the middle is the tank and the engine, and
        /// enough of those and it is alight (CrewBike.TakeRound).
        ///
        /// The trace carries the INPUT and not only the outcome, which is the lesson the
        /// car's engine model had to be taught twice: a threshold nothing ever reaches
        /// looks exactly like a rule that is working.</summary>
        Vector3 MachineHole(CrewBike bike, Vector3 muzzle)
        {
            var local = bike.Tf.InverseTransformPoint(muzzle);
            float side = local.x >= 0f ? 1f : -1f;
            // THE BODY, NOT THE BOX IT DRIVES IN. HalfWide and HalfLen are what the ROAD
            // takes a machine to be - deliberately smaller than the mesh, so a bike takes
            // a bike's room at a kerb (RoadBike.RoadBodyWide) - and a hole placed off
            // them lands a third of the way INSIDE the bodywork instead of on its flank.
            float flank = bike.Body != null ? bike.Body.HalfWidth : bike.HalfWide;
            float along = bike.Body != null ? bike.Body.HalfLength : bike.HalfLen;
            return bike.Tf.TransformPoint(new Vector3(
                side * flank, Random.Range(0.35f, 0.95f),
                Random.Range(-along * 0.9f, along * 0.9f)));
        }

        /// <summary>And the round put through it. The hole is chosen by the caller
        /// BEFORE the flash is lit, because the flash has to point down the path the
        /// round actually took (AIM-003) - and for a round into the tin, the hole is
        /// that path.</summary>
        void PutRoundIntoMachine(CrewBike bike, Vector3 muzzle, Vector3 at)
        {
            if (bike == null || bike.Tf == null) return;
            int before = bike.TankHits;
            bike.TakeRound(at, muzzle);
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "bike", bike.DisplayName);
                DriveTrace.Bool(sb, "tank", bike.TankHits > before);
                DriveTrace.Int(sb, "hits", bike.TankHits);
                DriveTrace.Bool(sb, "burning", bike.Burning);
                DriveTrace.Row("tin", sb.ToString());
            }
            if (ImpactPrefab)
            {
                var puff = CombatFx(ImpactPrefab, at, Quaternion.LookRotation(muzzle - at));
                Destroy(puff, 1.2f);
            }
        }

        /// <summary>Where on the body a round that missed its man went in: the flank
        /// facing the shooter, somewhere along its length, at about the height of a door.
        ///
        /// Not a raycast. A car is a box and the round came from a known direction, so
        /// the side it went into is the side the shooter is on and the only question left
        /// is where along it - which nothing can tell from a miss anyway, and which the
        /// eye reads as scatter. What it must get right is the LENGTHWISE part, because
        /// that is what decides whether the engine took it (CrewCar.TakeRound).</summary>
        Vector3 TinHole(CrewCar car, Vector3 muzzle)
        {
            var local = car.Tf.InverseTransformPoint(muzzle);
            float side = local.x >= 0f ? 1f : -1f;
            // more of them land forward of the middle: a man shooting at a car shoots at
            // the part of it he can see coming
            float along = Random.Range(-car.HalfLength * 0.85f, car.HalfLength * 0.95f);
            // A CAR IS NOT A BOX AT ITS ENDS. HalfWidth is the widest the body ever gets,
            // which is true across the doors and a lie over the bonnet and the boot - a
            // round placed on that plane at the nose sits in mid-air beside the wing,
            // and that is the hole the player watched float. Pulled in towards the
            // centreline as the body runs out.
            float station = Mathf.Abs(along) / Mathf.Max(car.HalfLength, 1e-3f);
            float flank = car.HalfWidth * Mathf.Lerp(1f, 0.7f, Mathf.InverseLerp(0.5f, 1f, station));
            return car.Tf.TransformPoint(new Vector3(
                side * flank, Random.Range(0.55f, 1.15f), along));
        }

        /// <summary>And the round put through it. The hole is chosen by the caller
        /// BEFORE the flash is lit: for a round into the tin the hole IS the round's
        /// direction, and the flash, the impact and the trace all have to agree with
        /// it (AIM-003).</summary>
        void PutRoundIntoTin(CrewCar car, Vector3 muzzle, Vector3 at)
        {
            if (car == null || car.Tf == null) return;
            int before = car.EngineHits;
            car.TakeRound(at, muzzle);
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "car", car.DisplayName);
                DriveTrace.Bool(sb, "engine", car.EngineHits > before);
                DriveTrace.Int(sb, "hits", car.EngineHits);
                DriveTrace.Row("tin", sb.ToString());
            }
            if (ImpactPrefab)
            {
                var puff = CombatFx(ImpactPrefab, at, Quaternion.LookRotation(muzzle - at));
                Destroy(puff, 1.2f);
            }
        }

        // The flash rides whatever fired it - the gun in the hand, the car under
        // the window - so it stays on the muzzle of a moving car; the particles the
        // pack simulates in world space (the smoke) trail behind, as smoke does.
        void Flash(Vector3 muzzle, Vector3 forward, Transform follow, EquipmentKind kind)
        {
            var rotation = Quaternion.LookRotation(forward);
            float calibre = MuzzleCalibre(kind);
            if (MuzzleFlashPrefab)
            {
                var flash = CombatFx(MuzzleFlashPrefab, muzzle,
                    rotation, follow);

                // ONE TRIGGER PULL IS ONE FLASH. Gallery muzzle effects are often authored
                // as LOOPING systems so they can be inspected - the flash emitter runs and
                // re-bursts every cycle - and it is instantiated as a CHILD of the gun's
                // muzzle in local simulation space. Left as it comes, one round strobed
                // about twenty flashes and a point light over two full seconds, out of
                // whatever direction the barrel happened to be pointing by then.
                //
                // That is the whole of the bug the player kept reporting as "puca u
                // zemlju" and "ubiju ih i onda nastave da pucaju u prazno": the fight
                // ends, AimGun stops writing the arm and blends out in a sixth of a
                // second, the arm falls back onto the raw pistol clip - which aims at
                // the horizon of the rig it was authored on and so puts the barrel in
                // the pavement - the tether then walks him off, and the flash from the
                // LAST round is still firing out of his lowered gun for another second
                // and a half. No round is leaving the barrel at all.
                //
                // It is also why every gate added inside TickEngage did nothing for it:
                // those gates govern the ROUND. What the player watches is this object,
                // which is bound by none of them and outlives the fight that made it.
                float live = LivingCity.Ambient.FireSmokeFx.TuneMuzzleFlash(
                    flash, calibre * 0.72f);
                Destroy(flash, Mathf.Max(0.2f, live));
            }
            if (GunSmokePrefab)
            {
                var smoke = CombatFx(GunSmokePrefab, muzzle, rotation, follow);
                bool rapid = kind == EquipmentKind.MachinePistol ||
                             kind == EquipmentKind.TommyGun;
                float live = LivingCity.Ambient.FireSmokeFx.TuneGunSmoke(
                    smoke, calibre, rapid);
                Destroy(smoke, Mathf.Max(0.6f, live));
            }
            var shots = ShotsFor(kind);
            if (shots.Length > 0)
            {
                // one 2D source, pitch-jittered: the shot has to be heard from the
                // demo's camera height, where a 3D one-shot at default rolloff is a whisper
                EnsureShotAudioSources();
                // Several recorded reports per weapon, so the variation comes from the
                // files and the pitch only has to keep two shots in a burst from being
                // identical - a transposition wide enough to fake variety also changes
                // the calibre, which is the one thing these files get right for free.
                _shots.pitch = Random.Range(0.94f, 1.07f);
                _shots.PlayOneShot(shots[Random.Range(0, shots.Length)],
                    DemoSounds.GunVolume);
                if (CrackClip)
                {
                    _cracks.pitch = Random.Range(0.92f, 1.12f);
                    _cracks.PlayOneShot(CrackClip, DemoSounds.BulletCrackVolume);
                }
            }
        }

        static float MuzzleCalibre(EquipmentKind kind) => kind switch
        {
            EquipmentKind.Shotgun => 0.32f,
            EquipmentKind.Rifle => 0.27f,
            EquipmentKind.TommyGun => 0.23f,
            EquipmentKind.MachinePistol => 0.19f,
            EquipmentKind.TwinPistols => 0.18f,
            _ => 0.17f,
        };

        // ------------------------------------------------------------- the detail

        static readonly List<int> DetailBefore = new List<int>();
        // One stream per house: who stands and who runs when they come for a Don has
        // to be the same twice, and one family's attempt must not shift another's roll.
        readonly Dictionary<int, System.Random> _attemptRng = new Dictionary<int, System.Random>();

        /// <summary>
        /// Somebody has come for A Don - ANY HOUSE'S (D23, row 4, the user's word of
        /// 2026-09-03). A family's Don is worth exactly what ours is: his own detail
        /// stands between him and the round, off his own family's books.
        ///
        /// (RANK-003). Asked only when the round in the
        /// air would actually put him down, because the detail is what stands between
        /// him and DEATH, not what soaks up every graze - and because every ask spends
        /// a man, so a burst of fire eats the detail one guard at a time exactly as
        /// <see cref="Bodyguards.Attempt"/> describes.
        ///
        /// The roster half is the pure layer's: who stood, who ran, who died and who
        /// went to a bed. This does the street's half - the body of a man the books
        /// have just struck off falls where he stood rather than blinking out on the
        /// next re-deal.
        /// </summary>
        /// <returns>True when the round was stopped and never reached him.</returns>
        bool DetailStoppedIt(CrewWalker target, int damage)
        {
            if (target == null || target.Dead ||
                target.Health - Mathf.Max(1, damage) > 0)
                return false;

            var his = HouseOf(target.Faction);
            var roster = his?.Roster;
            var boss = roster?.FindBoss();
            if (boss == null || boss.Id != target.CharacterId)
                return false;

            var detail = Bodyguards.DetailOf(roster);
            if (detail == null || detail.HoodIds.Count == 0)
                return false;

            DetailBefore.Clear();
            for (var i = 0; i < detail.HoodIds.Count; i++)
            {
                var guard = roster.Find(detail.HoodIds[i]);
                if (guard != null && guard.Status == CharacterStatus.Active)
                    DetailBefore.Add(guard.Id);
            }
            if (DetailBefore.Count == 0)
                return false;

            var runner = his.Runner;
            var day = runner != null ? runner.Campaign.Day : 1;
            var where = BlockNameAt(target.Tf != null ? target.Tf.position : Vector3.zero);
            // Off the city's own seed, like every other stream here, and off the house's
            // own number so no two families share a roll.
            if (!_attemptRng.TryGetValue(target.Faction, out var rng))
            {
                var underworld = LivingCity.Outfit.Underworld.Current;
                int seed = underworld != null ? underworld.CitySeed : 0;
                rng = new System.Random(seed * 31 + 977 + target.Faction * 7919);
                _attemptRng[target.Faction] = rng;
            }

            var outcome = Bodyguards.Attempt(roster, rng, day, where,
                runner != null ? runner.Incidents : null);

            // The books moved under the street: every man who is no longer standing was
            // spent on this, and his body has to answer for it here.
            for (var i = 0; i < DetailBefore.Count; i++)
            {
                var guard = roster.Find(DetailBefore[i]);
                if (guard == null || guard.Status == CharacterStatus.Active)
                    continue;
                // A man the books know but the street never stood a body for - a scene
                // with no city, or a guard already taken off - is simply not here to
                // fall. Tf is checked because everything below writes through it.
                if (!_byCharacter.TryGetValue(DetailBefore[i], out var body) ||
                    body == null || body.Dead || body.Tf == null)
                    continue;

                if (guard.Status == CharacterStatus.Dead)
                {
                    // He goes down in front of the Don, and the street hears it: the
                    // ordinary death path, minus the roster call the books already made.
                    CrewGore.Death(body, GroundY, floor: !IsAboard(body) && !body.Riding);
                    body.Kill();
                    _deaths.Add((body, Time.time + DeathReportDelay));
                    StreetAlarm.Death(
                        body.Tf.position, StreetAlarm.DeathOf.Gangster, body.Faction);
                }
                else
                {
                    // Hit but living, or gone over the wall: he is off the street either
                    // way, and the next re-deal takes his body with him.
                    body.Tf.gameObject.SetActive(false);
                }
            }

            // Only OUR pages are repainted off this - a rival's detail spends itself
            // on his own books and the FAMILIES page reads it on the next pass.
            if (target.Faction == LivingCity.Gameplay.PlayerCommands.House.Value)
                PersonnelDirector.Instance?.Touch();
            return !outcome.ReachedTheBoss;
        }

        /// <summary>The street a thing happened on, in the words the books use, or an
        /// empty line where the city has no canonical block under it.</summary>
        static string BlockNameAt(Vector3 world)
        {
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null || !runtime.TryGetBlockAtWorld(world, out var blockId) ||
                runtime.Geography == null ||
                !runtime.Geography.TryGetBlock(blockId, out var definition))
                return "";
            return definition.DisplayName;
        }

        /// <summary>The reports for a weapon, falling back to the sidearm's: a kind with
        /// no set of its own is a gun nobody recorded, not a gun that makes no noise.</summary>
        AudioClip[] ShotsFor(EquipmentKind kind)
        {
            AudioClip[] fallback = System.Array.Empty<AudioClip>();
            foreach (var set in GunshotSets)
            {
                if (set == null || set.Clips.Length == 0) continue;
                if (set.Kind == kind) return set.Clips;
                if (set.Kind == EquipmentKind.Pistol) fallback = set.Clips;
            }
            return fallback;
        }
    }
}
