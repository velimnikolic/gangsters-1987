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
            AddPrewarmFx(BloodPrefab);
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
                    unit.TargetUnit = null;
                    unit.OrderedFight = false;
                    unit.Searching = false;
                    unit.LookUntil = 0f;
                    foreach (var man in unit.All()) man.Disengage();
                }

                // a beaten crew - boss down, one man left - gets off the street; the men
                // who have run out of sight are taken off it
                if (unit.Faction != 0 && !unit.IsPolice && !unit.Retreated &&
                    (unit.Boss == null || unit.Boss.Dead) && unit.Standing() <= 1 && unit.Standing() > 0)
                {
                    unit.Retreated = true;
                    var threat = unit.TargetUnit != null ? unit.TargetUnit.Position : StreetAlarm.Incident;
                    unit.TargetUnit = null;
                    unit.Searching = false;
                    unit.LookUntil = 0f;
                    foreach (var man in unit.All())
                        if (!man.Dead && !IsAboard(man)) man.Retreat(threat);
                }
                if (unit.Retreated) { TakeOffRetreated(unit); continue; }

                // THE AMBUSH SPRINGS ITSELF (COVER-004). The outfit starts nothing
                // (below) - except a crew the player put behind a bin and told to wait,
                // which is the one fight he asked it to start. A rival family's man
                // inside the crew's best gun reach and in sight of one of the waiting
                // men, and they open up from where they are lying. Never the law and
                // never a civilian; ordered, because the player ordered the wait.
                if (unit.TargetUnit == null && AnyLurking(unit))
                {
                    var sprung = LurkQuarry(unit);
                    if (sprung != null) SetTarget(unit, sprung, ordered: true);
                }

                // a rival crew watches for the OUTFIT only - the mobs are not at war with
                // each other here, and two rival crews stood a street apart must not
                // open up on one another before the player has taken a single look;
                // the police pick their own fights (PoliceDispatch)
                if (unit.TargetUnit == null && unit.Faction != 0 && !unit.IsPolice)
                {
                    var seen = EnemyWithin(unit, AlertRange, outfitOnly: true);
                    if (seen != null) SetTarget(unit, seen);
                }

                // THE OUTFIT STARTS NOTHING - and walks through nothing either. A crew of
                // ours with no fight of its own that is BEING SHOT AT turns and returns
                // fire on whoever is nearest. Without this a crew sent across the quarter
                // is target practice: the mobs open up on it at twenty-four metres and it
                // walks on into the fire, because nothing had told it to shoot back (a
                // whole outfit, fifteen men, was lost that way for three of theirs).
                // The law is not answered here - a warning shout is PoliceWarning's
                // business, and a crew is not put at war with the police by a stray round.
                if (unit.TargetUnit == null && unit.Faction == 0 &&
                    Time.time - unit.ProvokedAt < FightBack &&
                    Time.time - unit.OrderedAt > HoldFireAfterOrder)
                {
                    var seen = EnemyWithin(unit, DefendRange, outfitOnly: false, noPolice: true);
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
                    var mark = BestMark(unit.TargetUnit, man.Tf.position, reach,
                                        sighted: !unit.OrderedFight);
                    if (mark != null && mark.Tf != null)
                    {
                        anySeen = true;
                        man.SawMarkAt = Time.time;
                        float d = (mark.Tf.position - man.Tf.position).sqrMagnitude;
                        if (d < seenNear) { seenNear = d; seenAt = mark.Tf.position; }
                    }
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
                    if (man.Target != null && !man.Target.Dead && mark == null)
                    {
                        // not on the frame he disappears. A fight runs past parked vans
                        // and round the corners of buildings, and a man whose gun came
                        // down the instant a mark stepped behind one and up again the
                        // instant he stepped out is a man twitching, not fighting.
                        if (Time.time - man.SawMarkAt < BlindGrace) continue;
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
                        else man.Disengage();
                    }
                }

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
                StartChase(unit);
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
                    Time.time - unit.ChasedAt > ChaseSeconds) EndSearch(unit);
                if (unit.Searching) continue;
                unit.TargetUnit = null;
                foreach (var man in unit.All()) if (!Chasing(man)) man.Disengage();
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
        void StartChase(Unit unit)
        {
            if (unit == null || unit.TargetUnit == null) return;
            // THE MOBS RUN; THE OUTFIT AND THE LAW DO NOT. A chase is the arena moving
            // men on its own initiative, and the player's crews are the player's to
            // move - a lieutenant sent to hold a corner who took himself forty metres
            // down the street after a passing machine would be the game playing itself.
            // The police have a dispatcher of their own (PoliceDispatch) and answer to
            // that, exactly as they do everywhere else in this loop.
            if (unit.Faction == 0 || unit.IsPolice) return;
            if (unit.Retreated || unit.Car != null) return;
            if (!unit.HasLastSeen) return;                          // never laid eyes on him
            if (Time.time - unit.SawEnemyAt < ChaseAfter) return;   // he may only be behind a van
            if (Time.time - unit.ChasedAt < ChaseAgainAfter) return;
            if (AnyChasing(unit)) return;                           // a leg is already running

            // where this search started - the door they are to end up back at
            if (!unit.Searching)
            {
                unit.SearchHome = unit.Position;
                unit.Searching = true;
            }

            var after = unit.LastSeenPos;
            var reach = after - unit.SearchHome;
            reach.y = 0f;
            // too far from their own door: the search is over, whatever they think they
            // saw. They are walked back by the tether the moment nobody is chasing.
            if (reach.magnitude > SearchRange) { EndSearch(unit); return; }

            int sent = 0;
            foreach (var man in unit.All())
            {
                if (sent >= Chasers) break;
                if (man == null || man.Tf == null || man.Dead || !man.Carrying) continue;
                if (man.Panicked || man.Retreating || man.Riding || IsAboard(man)) continue;
                if (OnRaid(man) || Chasing(man)) continue;
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
            if (sent == 0) { EndSearch(unit); return; }
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
        }

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
            foreach (var man in unit.All())
                if (!man.Dead && !Chasing(man) && man.Target != null) man.Disengage();
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
                if (drawn.magnitude > SearchRange) { EndSearch(unit); _chaseDone.Add(man); continue; }

                var mark = unit.TargetUnit != null
                    ? BestMark(unit.TargetUnit, man.Tf.position, SightRange) : null;
                if (mark != null)
                {
                    // there he is: this leg is over and a fight has started. The search
                    // itself is not - lose him again and the next leg goes from wherever
                    // he was standing when they last had eyes on him.
                    unit.SawEnemyAt = Time.time;
                    unit.LookUntil = 0f;
                    man.SawMarkAt = Time.time;
                    man.Engage(mark);
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
                    EndSearch(unit);
                    _chaseDone.Add(man);
                }
            }
            foreach (var man in _chaseDone) EndChase(man);
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

        void TakeOffRetreated(Unit unit)
        {
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

        /// <summary>The law has shouted its warning at the scene: nobody at war lowers
        /// their guns. The outfit stands its ground - it keeps the fight it has, and a
        /// crew with its guns free answers the warning with them (retreat stays the
        /// player's call); a rival crew in earshot either turns them on the police or
        /// gets out. The law is a third side of the war, not a referee.</summary>
        public void PoliceWarning(Vector3 from, Unit police)
        {
            foreach (var unit in Units)
            {
                if (unit.IsPolice || unit.Wiped || unit.Surrendered) continue;
                if ((unit.Position - from).sqrMagnitude > 45f * 45f) continue;
                if (unit.Faction == 0)
                {
                    // the war does not pause for the law: mid-fight the crew stays on
                    // its enemy (police rounds pull it round later, through the
                    // shot-back rule); guns free, it turns them on the squad now
                    if (unit.TargetUnit == null && police != null) SetTarget(unit, police);
                    continue;
                }
                if (Random.value < 0.4f)
                {
                    unit.Retreated = true;
                    unit.TargetUnit = null;
                    foreach (var man in unit.All())
                        if (!man.Dead && !IsAboard(man)) man.Retreat(from);
                }
                else if (police != null) SetTarget(unit, police);
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
                if (unit.TargetUnit != null) continue;
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

        Unit EnemyWithin(Unit unit, float range, bool outfitOnly, bool noPolice = false)
        {
            float r2 = range * range;
            foreach (var other in Units)
            {
                if (other == unit || other.Faction == unit.Faction || other.Wiped) continue;
                if (outfitOnly && other.Faction != 0) continue;
                if (noPolice && other.IsPolice) continue;
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

        /// <summary>One shot, wherever it left from: a man's gun on the pavement, or a
        /// car window on a pass. <paramref name="from"/> is where the shooter stands
        /// for the range - the man, or the car he is in.</summary>
        void Resolve(CrewWalker shooter, CrewWalker target, Vector3 muzzle, Vector3 from, Transform follow)
        {
            // the flash points where the shot goes - at the man, whatever the last
            // centimetre of the grip does to the barrel
            var line = target != null ? (target.ChestPosition - muzzle).normalized : shooter.MuzzleForward;
            Flash(muzzle, line, follow, shooter != null ? shooter.WeaponKind
                                                       : EquipmentKind.Pistol);
            var stats = shooter.Ballistics;
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
            if (target == null || target.Dead)
            {
                // NOBODY TO ROLL AGAINST AND STILL A MARK. A man put on a machine has
                // no man to hit or miss - the tin IS the target, so every round finds
                // it, and the damage model reads exactly the rounds it reads from a
                // miss into a door (PutRoundIntoTin, CrewCar.TakeRound).
                if (target == null && shooter.CarMark != null) PutRoundIntoTin(shooter.CarMark, muzzle);
                return;
            }

            float dist = Vector3.Distance(from, target.Tf.position);
            // the gun's accuracy holds to half its reach and falls to half of itself at
            // the edge; a lieutenant is a better shot; nothing is ever certain, and a
            // shotgun in a man's face very nearly is
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
            // a man in a car has the door and the sill between him and the round; a man
            // crouched behind one has its flank
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
            p = Mathf.Clamp(p, 0.04f, 0.98f);

            // a crew shot at shoots back - unless it has just been ordered off (it can be
            // pulled back); a crew shot at IN ITS CAR always does, from the windows,
            // wherever the car is going: the order stands, the guns come out anyway
            var victimUnit = UnitOf(target);
            var shooterUnit = UnitOf(shooter);
            // rounds are coming at this crew, hit or miss: it has a fight now whether it
            // went looking for one or not, and TickCombat turns it round when the order
            // it is under has stopped holding its fire
            if (victimUnit != null && shooterUnit != null && !shooterUnit.IsPolice)
                victimUnit.ProvokedAt = Time.time;
            // AND THE FIGHT ITSELF IS ONLY EVER PICKED UP OFF SOMEBODY IN SIGHT. Being
            // shot at is provocation (above) and provocation is answered by looking
            // round for whoever is there to answer - it is not knowledge of who fired.
            // A car that shot up a doorway and turned the corner is gone; the crew it
            // shot at keeps its guns up and its temper, and finds nobody.
            if (victimUnit != null && shooterUnit != null && victimUnit.TargetUnit == null &&
                (IsAboard(target) || Time.time - victimUnit.OrderedAt > HoldFireAfterOrder) &&
                Spotted(victimUnit, shooter))
                SetTarget(victimUnit, shooterUnit);

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
                DriveTrace.Row("shot", sb.ToString());
            }

            if (Random.value >= p)
            {
                // A ROUND THAT MISSED A MAN IN A CAR MOSTLY WENT INTO THE CAR. It was
                // going at the car - it is what he is sitting in - so most of the misses
                // are a hole in a door rather than a puff off the road ten metres past
                // him. This is the whole of the damage model's input: shoot at men in a
                // car for long enough and the car is what you hit (CrewCar.TakeRound).
                var carriage = IsAboard(target) ? CarWith(target) : null;
                var machine = carriage == null && target.Riding ? BikeWith(target) : null;
                if (carriage != null && Random.value < RoundsIntoTheTin)
                    PutRoundIntoTin(carriage, muzzle);
                else if (machine != null && Random.value < RoundsIntoTheMachine)
                    PutRoundIntoMachine(machine, muzzle);
                else
                    Miss(muzzle, target);
                target.UnderFire();
                StrayRound(muzzle, line, reach, from);
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
                    target.MaybePanic(shooter, PanicChance);
                    if (target.State == CrewWalker.Mode.Fleeing) OnFled(target, from);
                }
            }
            else
            {
                CrewGore.Death(target, GroundY, floor: !IsAboard(target) && !target.Riding);
                _deaths.Add((target, Time.time + DeathReportDelay));
                StreetAlarm.Death(target.Tf.position,
                    target.Faction == StreetAlarm.PoliceFaction ? StreetAlarm.DeathOf.Officer : StreetAlarm.DeathOf.Gangster);
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
                            mate.Flee(from, 15f, 25f, comeBack: true);
                            OnFled(mate, from);
                        }
                    }
                }
            }
            if (BloodPrefab)
            {
                var blood = CombatFx(BloodPrefab, target.ChestPosition,
                    Quaternion.LookRotation(-line));
                Destroy(blood, 4f);
            }
        }

        // A round that missed its man carries on: a bystander stood in its way past him
        // may take it - the same wounds as anyone, and a killing the police weigh heaviest.
        void StrayRound(Vector3 muzzle, Vector3 line, float reach, Vector3 from)
        {
            var civ = CivilianAgent.InLine(muzzle, line, reach * 1.5f, 0.7f);
            if (civ == null || Random.value >= StrayChance) return;
            if (DriveTrace.On) DriveTrace.Event("stray", "round", "a civilian was hit");
            civ.TakeHit(1, from);
            CrewGore.Hit(civ, from, GroundY);
            if (civ.Dead) CrewGore.Death(civ, GroundY);
            if (BloodPrefab)
            {
                var blood = CombatFx(BloodPrefab, civ.Tf.position + Vector3.up * 1.2f,
                    Quaternion.LookRotation(-line));
                Destroy(blood, 4f);
            }
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
        void PutRoundIntoMachine(CrewBike bike, Vector3 muzzle)
        {
            if (bike == null || bike.Tf == null) return;
            var local = bike.Tf.InverseTransformPoint(muzzle);
            float side = local.x >= 0f ? 1f : -1f;
            // THE BODY, NOT THE BOX IT DRIVES IN. HalfWide and HalfLen are what the ROAD
            // takes a machine to be - deliberately smaller than the mesh, so a bike takes
            // a bike's room at a kerb (RoadBike.RoadBodyWide) - and a hole placed off
            // them lands a third of the way INSIDE the bodywork instead of on its flank.
            float flank = bike.Body != null ? bike.Body.HalfWidth : bike.HalfWide;
            float along = bike.Body != null ? bike.Body.HalfLength : bike.HalfLen;
            var at = bike.Tf.TransformPoint(new Vector3(
                side * flank, Random.Range(0.35f, 0.95f),
                Random.Range(-along * 0.9f, along * 0.9f)));
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
        void PutRoundIntoTin(CrewCar car, Vector3 muzzle)
        {
            if (car == null || car.Tf == null) return;
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
            var at = car.Tf.TransformPoint(new Vector3(
                side * flank, Random.Range(0.55f, 1.15f), along));
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

        /// <summary>A round that went wide lands somewhere past the man - a puff off the
        /// ground beyond him, a little to one side, so a miss is seen to be a miss.</summary>
        void Miss(Vector3 muzzle, CrewWalker target)
        {
            if (!ImpactPrefab) return;
            var line = target.ChestPosition - muzzle;
            float dist = line.magnitude;
            if (dist < 0.1f) return;
            var dir = line / dist;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            float beyond = dist + Random.Range(1.5f, 6f);
            float wide = Random.Range(0.4f, 1.6f) * (Random.value < 0.5f ? -1f : 1f);
            var spot = muzzle + dir * beyond + side * wide;
            spot.y = GroundY + 0.02f;
            var puff = CombatFx(ImpactPrefab, spot, Quaternion.LookRotation(Vector3.up));
            Destroy(puff, 2f);
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
        System.Random _attemptRng;

        /// <summary>
        /// Somebody has come for the Don (RANK-003). Asked only when the round in the
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
            if (target == null || target.Faction != 0 || target.Dead ||
                target.Health - Mathf.Max(1, damage) > 0)
                return false;

            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
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

            var outfit = OutfitDirector.Instance;
            var day = outfit != null ? outfit.Campaign.Day : 1;
            var where = BlockNameAt(target.Tf != null ? target.Tf.position : Vector3.zero);
            // Off the city's own seed, like every other stream here: who stands and who
            // runs when they come for the Don has to be the same twice.
            _attemptRng ??= new System.Random(director.Seed * 31 + 977);

            var outcome = Bodyguards.Attempt(roster, _attemptRng, day, where,
                outfit != null ? outfit.Incidents : null);

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
                    StreetAlarm.Death(body.Tf.position, StreetAlarm.DeathOf.Gangster);
                }
                else
                {
                    // Hit but living, or gone over the wall: he is off the street either
                    // way, and the next re-deal takes his body with him.
                    body.Tf.gameObject.SetActive(false);
                }
            }

            director.Touch();
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
