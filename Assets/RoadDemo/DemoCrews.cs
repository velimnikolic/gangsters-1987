using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    // The outfit's crews out on the demo's streets: every lieutenant in the ledger
    // stands with his hoods behind him, wearing the same Synty face his mugshot in
    // the book wears and carrying the gun the book's armory dealt him. The player
    // commands the lieutenant only - left-click selects him (or any man of his; the
    // crew answers as one), a right-click on the map sends him there - and his
    // hoods take the same order a step behind, so the crew arrives as a crew.
    // A right-click on a rival's man sends the crew at that rival, guns up.
    //
    // Two grounds: the city, where the men move over the sidewalk graph, and the
    // empty demo floor, where they stride straight to the point. Rival crews are
    // no ledger's business - the arena deals them in by hand (AddRival) and they
    // keep their own counsel: a rival crew fires on the outfit when it is fired
    // upon or when the outfit walks up to it.
    //
    // The roster is read, never written: this is a picture of the books on the
    // ground. Every time the ledger's Version moves (a promotion makes a new crew,
    // a hood is moved between crews, a man goes to the pool or the front, a gun
    // changes hands) the figures are re-dealt to match - new men walk in, gone men
    // walk off, a hood handed to another lieutenant walks over to him.
    public class DemoCrews : MonoBehaviour
    {
        /// <summary>One lieutenant, his root object, and his men.</summary>
        public class Unit
        {
            public int CrewId;
            public int Faction;              // 0 the outfit, else a rival mob
            public string GangName = "";     // "The Outfit", "Falcone"...
            public Transform Root;
            public CrewWalker Boss;
            public readonly List<CrewWalker> Hoods = new List<CrewWalker>();
            public string Name = "";
            public int Loyalty;

            /// <summary>The crew this one is shooting it out with, or null.</summary>
            public Unit TargetUnit;

            /// <summary>When the player last gave this crew a move - for a few seconds
            /// after, being shot at does not turn it round (a crew can be pulled back).</summary>
            public float OrderedAt = -100f;

            public IEnumerable<CrewWalker> All()
            {
                if (Boss != null) yield return Boss;
                foreach (var h in Hoods) yield return h;
            }

            public int Standing()
            {
                int n = 0;
                foreach (var m in All()) if (!m.Dead) n++;
                return n;
            }

            public int Size()
            {
                int n = 0;
                foreach (var _ in All()) n++;
                return n;
            }

            public bool Wiped => Standing() == 0;

            /// <summary>Where the crew "is" - the lieutenant, or the first man still up.</summary>
            public Vector3 Position
            {
                get
                {
                    if (Boss != null && Boss.Tf) return Boss.Tf.position;
                    foreach (var m in All()) if (m.Tf) return m.Tf.position;
                    return Vector3.zero;
                }
            }
        }

        // A lieutenant walks like a man who is expected; his hoods keep up, each at
        // his own pace - no two the same, none of them dawdling.
        const float BossPace = 1.75f;
        float HoodPace() => 1.8f + (float)_variety.NextDouble() * 0.35f;
        const float Spacing = 1.7f;   // metres between men along the sidewalk
        const float MinSpawnLink = 12f;
        const float AlertRange = 24f; // a rival crew opens up on the outfit this close
        const int BossHealth = 4, HoodHealth = 3;
        const float HoldFireAfterOrder = 4f;

        public readonly List<Unit> Units = new List<Unit>();
        public Unit Selected { get; private set; }

        /// <summary>Off the sidewalk graph: straight strides over open floor.</summary>
        public bool FreeRoam { get; private set; }

        /// <summary>The floor's height, for the right-click pick.</summary>
        public float GroundY { get; private set; } = 0.1f;

        /// <summary>The arena's rule: a man the ledger left unarmed still draws the
        /// default sidearm here. Off, and he stands empty-handed as the book says.</summary>
        public bool EveryoneArmed = true;

        /// <summary>The bang, the flash and the blood - set by the scene builder;
        /// missing pieces are simply silent.</summary>
        public GameObject MuzzleFlashPrefab, BloodPrefab, ImpactPrefab;
        public AudioClip GunshotClip, CrackClip;

        /// <summary>Reference pixels from the top of the screen to the crew bar - the
        /// road demo sets it under its top bar. Read at Init.</summary>
        public float BarTopInset = 8f;

        List<PedLink> _links;
        List<PedLink> _sidewalks;
        PedClips _clips;
        List<GameObject> _fallbackPrefabs;
        Transform _root;
        int _seenVersion = -1;
        readonly Dictionary<int, CrewWalker> _byCharacter = new Dictionary<int, CrewWalker>();
        System.Random _rng;
        readonly System.Random _variety = new System.Random(4242); // gaits, falls, paces
        Vector3 _outfitAnchor, _outfitFacing = Vector3.forward;
        float _outfitSpread = 9f;
        int _rivalIds = -1;
        AudioSource _shots, _cracks;

        // ------------------------------------------------------------------ setup

        /// <summary>The city: crews dealt onto the sidewalk graph.</summary>
        public void Init(List<PedLink> links, PedClips clips, List<GameObject> fallbackPrefabs)
        {
            _links = links;
            _sidewalks = links.FindAll(l => !l.Gated && l.Length >= MinSpawnLink);
            if (_sidewalks.Count == 0) _sidewalks = links.FindAll(l => !l.Gated);
            _clips = clips;
            _fallbackPrefabs = fallbackPrefabs;
            FreeRoam = false;
            Common();
        }

        /// <summary>The empty floor: crews dealt in a row at the anchor, facing
        /// <paramref name="facing"/>, <paramref name="spread"/> metres apart.</summary>
        public void InitFree(PedClips clips, List<GameObject> fallbackPrefabs,
            Vector3 anchor, Vector3 facing, float spread, float groundY)
        {
            _clips = clips;
            _fallbackPrefabs = fallbackPrefabs;
            _outfitAnchor = anchor;
            _outfitFacing = facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward;
            _outfitSpread = spread;
            GroundY = groundY;
            FreeRoam = true;
            Common();
        }

        void Common()
        {
            _root = new GameObject("Crews").transform;
            gameObject.AddComponent<CrewOverlay>().Init(this);
            gameObject.AddComponent<CrewBar>().Init(this, BarTopInset);
        }

        /// <summary>A rival crew, dealt by hand: its lieutenant and hoods stood at the
        /// anchor facing <paramref name="facing"/>, all carrying <paramref name="weapon"/>.</summary>
        public Unit AddRival(int faction, string gangName, string bossName, GameObject bossPrefab,
            IList<string> hoodNames, IList<GameObject> hoodPrefabs, Vector3 anchor, Vector3 facing,
            GameObject weapon, EquipmentKind weaponKind)
        {
            var unit = new Unit
            {
                CrewId = _rivalIds--,
                Faction = faction,
                GangName = gangName,
                Name = bossName,
                Root = new GameObject("Rival · " + gangName + " · " + bossName).transform,
            };
            unit.Root.SetParent(_root, false);
            var rot = Quaternion.LookRotation(facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.back);

            var boss = SpawnAt(bossPrefab, bossName, _rivalIds--, anchor, rot, BossPace);
            if (boss != null)
            {
                boss.IsLieutenant = true;
                boss.Faction = faction;
                boss.MaxHealth = boss.Health = BossHealth;
                boss.Arm(weapon, weaponKind);
                boss.Tf.SetParent(unit.Root, true);
                unit.Boss = boss;
            }
            for (int k = 0; k < hoodNames.Count; k++)
            {
                var prefab = hoodPrefabs.Count > 0 ? hoodPrefabs[k % hoodPrefabs.Count] : bossPrefab;
                var pos = anchor + rot * FormationOffset(k);
                var hood = SpawnAt(prefab, hoodNames[k], _rivalIds--, pos, rot, HoodPace());
                if (hood == null) continue;
                hood.Faction = faction;
                hood.MaxHealth = hood.Health = HoodHealth;
                hood.Arm(weapon, weaponKind);
                hood.Tf.SetParent(unit.Root, true);
                unit.Hoods.Add(hood);
            }
            Units.Add(unit);
            return unit;
        }

        void Update()
        {
            var director = PersonnelDirector.Instance;
            if (director != null && director.Roster != null &&
                (FreeRoam || (_sidewalks != null && _sidewalks.Count > 0)) &&
                director.Version != _seenVersion)
            {
                _seenVersion = director.Version;
                _rng ??= new System.Random(director.Seed * 7919 + 13);
                Sync(director.Roster);
            }

            float dt = Time.deltaTime;
            TickCombat();
            foreach (var unit in Units)
                foreach (var man in unit.All())
                    man.TickCrew(dt);
            if (FreeRoam) Separate();

            // men with time on their hands find each other for a word
            _chatScan -= dt;
            if (_chatScan <= 0f)
            {
                _chatScan = 2f;
                PairChats();
            }

            if (Selected != null && Selected.Wiped)
                Selected = null;
        }

        void OnDestroy()
        {
            foreach (var unit in Units)
                foreach (var man in unit.All())
                    man.Dispose();
        }

        // ------------------------------------------------------------------ orders

        public void Select(Unit unit) => Selected = unit != null && unit.Faction == 0 ? unit : null;

        /// <summary>The unit a screen pick landed on, by the man it hit.</summary>
        public Unit UnitOf(CrewWalker man)
        {
            foreach (var unit in Units)
                if (unit.Boss == man || unit.Hoods.Contains(man)) return unit;
            return null;
        }

        /// <summary>Send the selected lieutenant toward a world point - the nearest
        /// sidewalk to it in the city, the point itself on open floor. Returns where
        /// he will stand, or false when nothing is selected.</summary>
        public bool OrderSelected(Vector3 world, out Vector3 destination)
        {
            destination = world;
            if (Selected == null || Selected.Boss == null || Selected.Boss.Dead) return false;
            Selected.TargetUnit = null;
            Selected.OrderedAt = Time.time;

            if (FreeRoam)
            {
                world.y = GroundY;
                var boss = Selected.Boss;
                var dir = world - boss.Tf.position;
                dir.y = 0f;
                var rot = Quaternion.LookRotation(dir.sqrMagnitude > 0.25f ? dir.normalized : boss.Tf.forward);
                boss.OrderToPoint(world);
                for (int k = 0; k < Selected.Hoods.Count; k++)
                    Selected.Hoods[k].OrderToPoint(world + rot * FormationOffset(k));
                destination = world;
                return true;
            }

            if (!NearestSidewalk(world, out var link, out float t)) return false;
            Dispatch(Selected, link, t);
            destination = Selected.Boss.Destination;
            return true;
        }

        /// <summary>Send the selected crew at that one: every man closes and shoots.</summary>
        public bool OrderAttack(Unit target)
        {
            if (Selected == null || target == null || target == Selected || target.Wiped) return false;
            SetTarget(Selected, target);
            return true;
        }

        void Dispatch(Unit unit, PedLink link, float t)
        {
            unit.Boss.OrderTo(link, t);
            for (int k = 0; k < unit.Hoods.Count; k++)
                unit.Hoods[k].OrderTo(link, FormationT(link, t, k));
        }

        /// <summary>Hood k's spot on a sidewalk: behind the lieutenant, then in front,
        /// alternating outward - so a short stretch still seats the whole crew.</summary>
        static float FormationT(PedLink link, float bossT, int k)
        {
            int rank = k / 2 + 1;
            float offset = (k % 2 == 0 ? -1f : 1f) * rank * Spacing;
            return Mathf.Clamp(bossT + offset, 0.4f, link.Length - 0.4f);
        }

        /// <summary>Hood k's spot on open ground, in the lieutenant's frame: a wedge
        /// behind him, left and right by turns.</summary>
        static Vector3 FormationOffset(int k)
        {
            int rank = k / 2 + 1;
            float side = k % 2 == 0 ? -1f : 1f;
            return new Vector3(side * 1.6f * rank, 0f, -1.5f * rank);
        }

        bool NearestSidewalk(Vector3 p, out PedLink best, out float bestT)
        {
            best = null;
            bestT = 0f;
            float bestD = float.MaxValue;
            foreach (var l in _links)
            {
                if (l.Gated) continue;
                var ab = l.To.Pos - l.From.Pos;
                float len2 = ab.sqrMagnitude;
                if (len2 < 1e-4f) continue;
                float t = Mathf.Clamp01(Vector3.Dot(p - l.From.Pos, ab) / len2);
                var q = l.From.Pos + ab * t;
                float d = (q - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = l; bestT = t * l.Length; }
            }
            return best != null;
        }

        // ------------------------------------------------------------------ combat

        void SetTarget(Unit unit, Unit target)
        {
            unit.TargetUnit = target;
            foreach (var man in unit.All())
                if (!man.Dead) man.Engage(NearestStanding(target, man.Tf.position));
        }

        static CrewWalker NearestStanding(Unit unit, Vector3 from)
        {
            CrewWalker best = null;
            float bestD = float.MaxValue;
            foreach (var m in unit.All())
            {
                if (m.Dead || !m.Tf) continue;
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
                if (unit.TargetUnit != null && unit.TargetUnit.Wiped)
                {
                    unit.TargetUnit = null;
                    foreach (var man in unit.All()) man.Disengage();
                }

                // a rival crew watches for the OUTFIT only - the mobs are not at war with
                // each other here, and two rival crews stood a street apart must not
                // open up on one another before the player has taken a single look
                if (unit.TargetUnit == null && unit.Faction != 0)
                {
                    var seen = EnemyWithin(unit, AlertRange, outfitOnly: true);
                    if (seen != null) SetTarget(unit, seen);
                }

                if (unit.TargetUnit == null) continue;
                foreach (var man in unit.All())
                {
                    if (man.Dead || !man.Armed) continue;
                    if (man.Target == null || man.Target.Dead)
                        man.Engage(NearestStanding(unit.TargetUnit, man.Tf.position));
                }
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
                    if (m.Loitering && m.Tf) men.Add(m);
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

        /// <summary>Metres two men keep between them on open ground - shoulder room.</summary>
        const float Elbow = 1.0f;

        readonly List<CrewWalker> _standing = new List<CrewWalker>();

        // Nobody stands inside anybody else: men who converge on the same spot -
        // a crew closing on one target, hoods falling in on a boss who has stopped
        // - are eased apart, half the overlap each, on the flat. Two dozen men at
        // most, so the pair scan is nothing. The fallen are left where they lie.
        void Separate()
        {
            _standing.Clear();
            foreach (var unit in Units)
                foreach (var man in unit.All())
                    if (!man.Dead && man.Tf) _standing.Add(man);

            for (int i = 0; i < _standing.Count; i++)
            {
                var a = _standing[i].Tf;
                for (int j = i + 1; j < _standing.Count; j++)
                {
                    var b = _standing[j].Tf;
                    var d = b.position - a.position;
                    d.y = 0f;
                    float dist = d.magnitude;
                    if (dist >= Elbow) continue;
                    // dead-on top of each other: pick a side rather than divide by zero
                    var dir = dist > 1e-3f ? d / dist
                        : new Vector3(Mathf.Cos(i * 2.4f), 0f, Mathf.Sin(i * 2.4f));
                    float push = (Elbow - dist) * 0.5f;
                    a.position -= dir * push;
                    b.position += dir * push;
                }
            }
        }

        Unit EnemyWithin(Unit unit, float range, bool outfitOnly)
        {
            float r2 = range * range;
            foreach (var other in Units)
            {
                if (other == unit || other.Faction == unit.Faction || other.Wiped) continue;
                if (outfitOnly && other.Faction != 0) continue;
                foreach (var a in unit.All())
                {
                    if (a.Dead) continue;
                    foreach (var b in other.All())
                        if (!b.Dead && (a.Tf.position - b.Tf.position).sqrMagnitude < r2)
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
            var target = shooter.Target;
            var muzzle = shooter.MuzzlePosition;
            // the flash points where the shot goes - at the man, whatever the last
            // centimetre of the grip does to the barrel
            var line = target != null ? (target.ChestPosition - muzzle).normalized : shooter.MuzzleForward;
            Flash(muzzle, line);
            if (target == null || target.Dead) return;

            var stats = shooter.Ballistics;
            float dist = Vector3.Distance(shooter.Tf.position, target.Tf.position);
            // the gun's accuracy holds to half its reach and falls to half of itself at
            // the edge; a lieutenant is a better shot; nothing is ever certain, and a
            // shotgun in a man's face very nearly is
            float reach = Mathf.Max(stats.Range, 1f);
            float falloff = dist <= reach * 0.5f ? 1f : Mathf.Lerp(1f, 0.5f, (dist / reach - 0.5f) / 0.5f);
            float p = stats.Accuracy * falloff;
            if (shooter.IsLieutenant) p += 0.08f;
            p = Mathf.Clamp(p, 0.04f, 0.98f);

            var victimUnit = UnitOf(target);
            var shooterUnit = UnitOf(shooter);
            if (victimUnit != null && shooterUnit != null && victimUnit.TargetUnit == null &&
                Time.time - victimUnit.OrderedAt > HoldFireAfterOrder)
                SetTarget(victimUnit, shooterUnit);

            if (Random.value >= p)
            {
                Miss(muzzle, target);
                return;
            }
            target.TakeHit(stats.Damage, shooter);
            if (BloodPrefab)
            {
                var blood = Instantiate(BloodPrefab, target.ChestPosition,
                    Quaternion.LookRotation(-shooter.MuzzleForward));
                Destroy(blood, 4f);
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
            var puff = Instantiate(ImpactPrefab, spot, Quaternion.LookRotation(Vector3.up));
            Destroy(puff, 2f);
        }

        void Flash(Vector3 muzzle, Vector3 forward)
        {
            if (MuzzleFlashPrefab)
            {
                var flash = Instantiate(MuzzleFlashPrefab, muzzle, Quaternion.LookRotation(forward));
                Destroy(flash, 2f);
            }
            if (GunshotClip)
            {
                // one 2D source, pitch-jittered: the shot has to be heard from the
                // demo's camera height, where a 3D one-shot at default rolloff is a whisper
                if (_shots == null)
                {
                    _shots = gameObject.AddComponent<AudioSource>();
                    _shots.spatialBlend = 0f;
                    _shots.playOnAwake = false;
                }
                // the pack's only shot is a muffled thud; pitched up it is a report, and
                // a slap pitched to a snap on top gives it the crack a pistol has
                _shots.pitch = Random.Range(1.35f, 1.6f);
                _shots.PlayOneShot(GunshotClip, 0.5f);
                if (CrackClip)
                {
                    if (_cracks == null)
                    {
                        _cracks = gameObject.AddComponent<AudioSource>();
                        _cracks.spatialBlend = 0f;
                        _cracks.playOnAwake = false;
                    }
                    _cracks.pitch = Random.Range(1.8f, 2.3f);
                    _cracks.PlayOneShot(CrackClip, 0.3f);
                }
            }
        }

        // ------------------------------------------------------------------ the deal

        // Re-deals the outfit's figures to the books. Men are keyed by roster id so
        // a hood moved between crews keeps his body and simply walks over. Rival
        // crews are not on the books and are left alone.
        void Sync(Roster roster)
        {
            var wanted = new Dictionary<int, (Crew crew, bool boss)>();
            foreach (var crew in roster.Crews)
            {
                var lt = roster.Find(crew.LieutenantId);
                if (lt == null || lt.Status != CharacterStatus.Active) continue;
                wanted[lt.Id] = (crew, true);
                foreach (int id in crew.HoodIds)
                {
                    var hood = roster.Find(id);
                    if (hood != null && hood.Status == CharacterStatus.Active)
                        wanted[id] = (crew, false);
                }
            }

            // men no longer on a crew leave the street (the fallen stay where they fell)
            var gone = new List<int>();
            foreach (var kv in _byCharacter)
                if (!wanted.ContainsKey(kv.Key) && !kv.Value.Dead) gone.Add(kv.Key);
            foreach (int id in gone) RemoveMan(id);

            // units follow the crews; membership is rebuilt from scratch below
            var previousUnitOf = new Dictionary<CrewWalker, Unit>();
            foreach (var unit in Units)
                if (unit.Faction == 0)
                    foreach (var man in unit.All()) previousUnitOf[man] = unit;

            var liveUnits = new List<Unit>();
            foreach (var crew in roster.Crews)
            {
                if (!wanted.TryGetValue(crew.LieutenantId, out var w) || w.crew != crew) continue;
                var unit = Units.Find(u => u.Faction == 0 && u.CrewId == crew.Id)
                           ?? new Unit { CrewId = crew.Id, Faction = 0, GangName = OutfitNames.Player };
                unit.Boss = null;
                unit.Hoods.Clear();
                liveUnits.Add(unit);

                var lt = roster.Find(crew.LieutenantId);
                unit.Name = lt.FullName;
                unit.Loyalty = lt.Loyalty;
                if (unit.Root == null)
                    unit.Root = new GameObject("Crew").transform;
                unit.Root.name = "Crew · " + lt.FullName;
                unit.Root.SetParent(_root, false);
            }

            var rivals = Units.FindAll(u => u.Faction != 0);
            foreach (var unit in Units)
                if (unit.Faction == 0 && !liveUnits.Contains(unit))
                {
                    if (Selected == unit) Selected = null;
                    // whoever is still under it moves crews below; get them out first
                    foreach (var man in unit.All())
                        if (man.Tf) man.Tf.SetParent(_root, true);
                    if (unit.Root) Destroy(unit.Root.gameObject);
                }
            Units.Clear();
            Units.AddRange(liveUnits);
            Units.AddRange(rivals);

            // lieutenants first, so a hood dealt in afterwards has a boss to stand behind
            foreach (var kv in wanted)
                if (kv.Value.boss) Place(roster, kv.Key, kv.Value.crew, true, previousUnitOf);
            foreach (var kv in wanted)
                if (!kv.Value.boss) Place(roster, kv.Key, kv.Value.crew, false, previousUnitOf);
        }

        void Place(Roster roster, int id, Crew crew, bool boss,
            Dictionary<CrewWalker, Unit> previousUnitOf)
        {
            var unit = Units.Find(u => u.Faction == 0 && u.CrewId == crew.Id);
            if (unit == null) return;
            var member = roster.Find(id);

            bool fresh = !_byCharacter.TryGetValue(id, out var man);

            // a fallen man is on the books until the ledger strikes him; his body
            // stays on the ground and takes no part in the crew's business
            if (!fresh && man.Dead)
            {
                if (boss) unit.Boss = man; else unit.Hoods.Add(man);
                return;
            }

            // the book recasts a man when his rank changes (a lieutenant sits for
            // his photograph in a suit) - the same face must walk the street, so
            // the body is swapped on the spot
            var cast = LivingCity.UI.PersonnelAlmanac.MemberModel(member);
            if (!fresh && cast != null && man.SourcePrefab != cast)
            {
                var link = man.CurrentLink;
                float t = man.CurrentT;
                var pos = man.Tf.position;
                var rot = man.Tf.rotation;
                RemoveMan(id);
                float pace = boss ? BossPace : HoodPace();
                man = FreeRoam ? SpawnMember(member, pos, rot, pace) : SpawnMember(member, link, t, pace);
                if (man == null) return;
                _byCharacter[id] = man;
            }

            if (fresh)
            {
                // a new man walks in beside his boss; a new crew opens up on ground
                // of its own, apart from the others
                if (FreeRoam)
                {
                    var rot = Quaternion.LookRotation(_outfitFacing);
                    Vector3 pos;
                    if (unit.Boss != null)
                        pos = unit.Boss.Tf.position + unit.Boss.Tf.rotation * FormationOffset(unit.Hoods.Count);
                    else
                        pos = OutfitSpawnPoint(unit);
                    man = SpawnMember(member, pos, rot, boss ? BossPace : HoodPace());
                }
                else
                {
                    PedLink link;
                    float t;
                    if (unit.Boss != null)
                    {
                        link = unit.Boss.CurrentLink;
                        t = FormationT(link, unit.Boss.CurrentT, unit.Hoods.Count);
                    }
                    else
                    {
                        link = PickSpawnLink();
                        t = link.Length * 0.5f;
                    }
                    man = SpawnMember(member, link, t, boss ? BossPace : HoodPace());
                }
                if (man == null) return;
                _byCharacter[id] = man;
            }

            man.IsLieutenant = boss;
            man.DisplayName = member.FullName;
            man.Faction = 0;
            int health = boss ? BossHealth : HoodHealth;
            if (fresh || man.MaxHealth != health)
            {
                man.MaxHealth = health;
                man.Health = fresh ? health : Mathf.Min(man.Health, health);
            }
            man.Tf.SetParent(unit.Root, true);
            if (boss) unit.Boss = man;
            else unit.Hoods.Add(man);

            ArmFromLedger(roster, man);

            // a hood who changed crews - or just arrived - falls in on his boss
            previousUnitOf.TryGetValue(man, out var was);
            if (!boss && unit.Boss != null && (fresh || was != unit))
                FallIn(unit, man, unit.Hoods.Count - 1);
        }

        /// <summary>The gun the ledger says he holds - re-checked on every deal, so a
        /// pistol handed over on the armory page changes hands on the street too.</summary>
        void ArmFromLedger(Roster roster, CrewWalker man)
        {
            var item = CrewArms.FirearmOf(roster, man.CharacterId);
            var prefab = CrewArms.ModelFor(item);
            var kind = item != null ? item.Kind : EquipmentKind.Pistol;
            if (prefab == null && EveryoneArmed)
            {
                prefab = CrewKit.Weapon(CrewArms.DefaultSidearm);
                kind = EquipmentKind.Pistol;
            }
            if (man.WeaponPrefab != prefab || man.WeaponKind != kind)
                man.Arm(prefab, kind);
        }

        Vector3 OutfitSpawnPoint(Unit unit)
        {
            // crews in a row across the facing, in book order, centred on the anchor
            int index = 0, count = 0;
            foreach (var u in Units)
            {
                if (u.Faction != 0) continue;
                if (u == unit) index = count;
                count++;
            }
            var right = Vector3.Cross(Vector3.up, _outfitFacing);
            float x = (index - (count - 1) * 0.5f) * _outfitSpread;
            var p = _outfitAnchor + right * x;
            p.y = GroundY;
            return p;
        }

        void FallIn(Unit unit, CrewWalker hood, int k)
        {
            var boss = unit.Boss;
            if (FreeRoam)
            {
                var facing = boss.HasOrder ? (boss.Destination - boss.Tf.position) : boss.Tf.forward;
                facing.y = 0f;
                var rot = Quaternion.LookRotation(facing.sqrMagnitude > 1e-3f ? facing.normalized : Vector3.forward);
                var spot = boss.Destination + rot * FormationOffset(k);
                if ((hood.Tf.position - spot).sqrMagnitude > 0.35f * 0.35f)
                    hood.OrderToPoint(spot);
                return;
            }
            if (boss.HasOrder)
            {
                hood.OrderTo(boss.DestinationLink, FormationT(boss.DestinationLink, boss.DestinationT, k));
                return;
            }
            var link = boss.CurrentLink;
            if (link == null || link.Gated) return;
            float t = FormationT(link, boss.CurrentT, k);
            // freshly dealt in on his spot already - no need to shuffle
            if (hood.CurrentLink == link && Mathf.Abs(hood.CurrentT - t) < 0.35f) return;
            hood.OrderTo(link, t);
        }

        void RemoveMan(int id)
        {
            if (!_byCharacter.TryGetValue(id, out var man)) return;
            _byCharacter.Remove(id);
            man.Dispose();
            if (man.Tf) Destroy(man.Tf.gameObject);
        }

        // ------------------------------------------------------------------ bodies

        GameObject CastFor(Character member)
        {
            // The very prefab the ledger photographs for his mugshot - same face on
            // the street as in the book. Only when that cannot be resolved (the cast
            // asset not baked, the pack missing) does a crowd body stand in, and it
            // says so, so a stranger on the corner is never mistaken for the design.
            var prefab = LivingCity.UI.PersonnelAlmanac.MemberModel(member);
            if (prefab == null && _fallbackPrefabs != null && _fallbackPrefabs.Count > 0)
            {
                prefab = _fallbackPrefabs[member.Id % _fallbackPrefabs.Count];
                Debug.LogWarning("[RoadDemo] No ledger model for " + member.FullName +
                                 " - a crowd body (" + prefab.name + ") stands in.");
            }
            return prefab;
        }

        CrewWalker SpawnMember(Character member, PedLink link, float t, float pace)
        {
            var prefab = CastFor(member);
            if (prefab == null) return null;
            var go = Body(prefab, member.FullName);
            var man = new CrewWalker
                { Speed = pace, CharacterId = member.Id, SourcePrefab = prefab };
            man.Init(go.transform, CrewKit.Draw(_clips, _variety), link, Mathf.Clamp(t, 0.3f, link.Length - 0.3f));
            man.Fired = OnFired;
            man.RangeFactor = Random.Range(0.55f, 0.85f);
            man.SetJog(Random.Range(2.7f, 3.5f));
            return man;
        }

        CrewWalker SpawnMember(Character member, Vector3 pos, Quaternion rot, float pace)
        {
            var prefab = CastFor(member);
            if (prefab == null) return null;
            var go = Body(prefab, member.FullName);
            var man = new CrewWalker
                { Speed = pace, CharacterId = member.Id, SourcePrefab = prefab };
            man.InitAt(go.transform, CrewKit.Draw(_clips, _variety), pos, rot);
            man.Fired = OnFired;
            man.RangeFactor = Random.Range(0.55f, 0.85f);
            man.SetJog(Random.Range(2.7f, 3.5f));
            return man;
        }

        CrewWalker SpawnAt(GameObject prefab, string name, int id, Vector3 pos, Quaternion rot, float pace)
        {
            if (prefab == null) return null;
            var go = Body(prefab, name);
            var man = new CrewWalker
                { Speed = pace, CharacterId = id, SourcePrefab = prefab, DisplayName = name };
            man.InitAt(go.transform, CrewKit.Draw(_clips, _variety), pos, rot);
            man.Fired = OnFired;
            man.RangeFactor = Random.Range(0.55f, 0.85f);
            man.SetJog(Random.Range(2.7f, 3.5f));
            return man;
        }

        GameObject Body(GameObject prefab, string name)
        {
            var go = Instantiate(prefab, _root);
            go.name = name;
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            // the name may resolve to an "_AI" street copy out of the PrefabDatabase,
            // carrying the city's crowd scripts, a NavMeshAgent and an animator
            // controller; the walker drives the body itself, so all of that goes
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            foreach (var nav in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>()) Destroy(nav);
            foreach (var animator in go.GetComponentsInChildren<Animator>())
                animator.runtimeAnimatorController = null;
            return go;
        }

        /// <summary>A sidewalk for a new crew: of a handful of draws, the one farthest
        /// from every man already out - so the outfit is spread over the city rather
        /// than piled on one corner. Deterministic off the roster seed.</summary>
        PedLink PickSpawnLink()
        {
            PedLink best = null;
            float bestScore = -1f;
            for (int i = 0; i < 10; i++)
            {
                var link = _sidewalks[_rng.Next(_sidewalks.Count)];
                var mid = (link.From.Pos + link.To.Pos) * 0.5f;
                float nearest = float.MaxValue;
                foreach (var man in _byCharacter.Values)
                    if (man.Tf != null)
                        nearest = Mathf.Min(nearest, (man.Tf.position - mid).sqrMagnitude);
                if (nearest > bestScore) { bestScore = nearest; best = link; }
            }
            return best;
        }
    }

    /// <summary>The names the arena prints - the outfit's from the gang catalogue.</summary>
    static class OutfitNames
    {
        public static string Player => LivingCity.Gangs.GangCatalog.Names[LivingCity.Gangs.GangCatalog.PlayerGangId];
    }
}
