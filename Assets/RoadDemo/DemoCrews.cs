using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Entities;
using LivingCity.Personnel;
using LivingCity.Outfit;
using LivingCity.Police;
using LivingCity.Territory;
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
    public partial class DemoCrews : MonoBehaviour, IOrganizationPhysicalSource,
                                     IHeadquartersPhysicalSource, IMapVisionAreaSource
    {
        /// <summary>One lieutenant, his root object, and his men.</summary>
        public class Unit
        {
            static int _nextCrowdGroupId;

            /// <summary>Runtime-only identity shared by exactly the walkers in this
            /// physical unit. CrewId is organization data and can overlap across
            /// factions; this token cannot, so close-range crowd steering never treats
            /// another gang or a police detail as a crewmate.</summary>
            public readonly int CrowdGroupId = ++_nextCrowdGroupId;
            public int CrewId;
            /// <summary>The real Lieutenant Character this temporary physical group
            /// answers to. Moving figures never rewrites this organization parent.</summary>
            public int CommandParentId = -1;
            public int Faction;              // 0 the outfit, else a rival mob
            public string GangName = "";     // "The Outfit", "Falcone"...
            public Transform Root;
            public CrewWalker Boss;
            public readonly List<CrewWalker> Hoods = new List<CrewWalker>();
            public string Name = "";
            public int Loyalty;

            /// <summary>THE BAG MAN'S OWN UNIT (GAN-262): the one hood of a crew marked
            /// for the collection bag, held inside HQ between rounds and walking them
            /// with his escorts. Same CrewId as the crew he belongs to; Parent is the
            /// crew's line. Every surface that LISTS or PICKS a crew skips these - the
            /// books name a crew, and the crew is the parent - while everything that
            /// samples bodies (presence, arrivals, combat) sees him like any man.</summary>
            public bool IsDetachment;
            public Unit Parent;

            /// <summary>Grenades the crew is carrying - what it can throw at a shopfront
            /// or a rival, or lay under a car (DemoCrews.Bomb). Spent one at a time; at
            /// nought the order is refused. Stocked at BombsPerCrew when the crew is
            /// dealt onto the street.</summary>
            public int Bombs;

            /// <summary>The crew this one is shooting it out with, or null.</summary>
            public Unit TargetUnit;

            /// <summary>When any man of this crew last had one of its enemy in sight -
            /// what a fight is given up on (TickCombat, SightRange).</summary>
            public float SawEnemyAt = -100f;

            /// <summary>Where the enemy WAS the last time anybody of this crew laid eyes
            /// on him, and which way he was going. Not where he is: that is the whole
            /// point of them (TickChase).
            ///
            /// A motorcycle rides past the door, empties a gun at it and is round the
            /// corner in four seconds. What the men it shot at know afterwards is the
            /// stretch of street it went down - not the turning it took at the end of it,
            /// and not the kerb it is standing at now. So the chase is laid against these
            /// two and never against the machine, however easy the machine would be to
            /// read from here.</summary>
            public Vector3 LastSeenPos;
            public Vector3 LastSeenDir;

            /// <summary>Whether <see cref="LastSeenPos"/> is a place anybody has actually
            /// been seen, as against the zero it starts life at. Without it the FIRST
            /// sighting reads as a move from the world origin to wherever the man is
            /// standing, and the crew sets off chasing a direction that is nothing but
            /// the bearing of the map's own corner.</summary>
            public bool HasLastSeen;

            /// <summary>The backstop on the leg of a search running now: the moment the
            /// men give up walking to the place, whether they got there or not. Zero when
            /// no leg is out.</summary>
            public float ChaseUntil;

            /// <summary>When this crew last laid a leg of a search.</summary>
            public float ChasedAt = -1000f;

            /// <summary>Where the crew stood when the SEARCH began - its own door. Every
            /// leg is measured from here (SearchRange), and it is where the men are
            /// walked back to when the search is given up. Set on the first leg and kept
            /// for all of them: measuring from wherever the last leg ended would let one
            /// glimpse after another carry a crew across the town in fifty-metre
            /// steps.</summary>
            public Vector3 SearchHome;

            /// <summary>Whether <see cref="SearchHome"/> is a place and not the origin -
            /// a search is running.</summary>
            public bool Searching;

            /// <summary>While the men who got to the place stand there looking: the
            /// moment the search is given up. Zero while they are still running.</summary>
            public float LookUntil;

            /// <summary>Whether this fight was ORDERED rather than picked up.
            ///
            /// A crew forgets a fight it wandered into once the other side has been out
            /// of sight a while - that is what stops a mob marching across the quarter
            /// at a lieutenant it has never seen. It must NOT forget one it was sent on:
            /// an order stands until the job is done or it is countermanded, and a car
            /// driving two hundred metres to reach its mark is out of sight of him for
            /// most of the trip. (It was forgetting: the crew car arrived with no target
            /// at all, which is also why it would not run anybody down - the run-down
            /// asks who the crew is fighting and the answer had become "nobody".)</summary>
            public bool OrderedFight;

            /// <summary>The car this crew is walking to get into, or null.</summary>
            public CrewCar Boarding;

            /// <summary>A drive ordered while the crew was still climbing in: the car
            /// goes there the moment the last man is in, not before - nobody is left
            /// on the kerb by a click that came a second early.</summary>
            public Vector3? PendingDrive;

            /// <summary>A KILL ordered while the crew was still climbing in: the drive-by
            /// starts the moment the last man is in, not before. The same rule as
            /// <see cref="PendingDrive"/>, and for the same reason - the first man into
            /// his seat is what puts a car under the crew, so a kill clicked a second
            /// early used to pull the car away from the two who were still walking to
            /// their doors and leave them standing in the road with nothing to do
            /// (their walk called off, their unit riding, so no fight of their own).</summary>
            public Unit PendingAttack;

            /// <summary>Told to get out - waiting on the car to pull in and the doors to open.</summary>
            public bool Leaving;

            /// <summary>The car this crew is riding in, or null.</summary>
            public CrewCar Car;

            /// <summary>When the player last gave this crew a move - for a few seconds
            /// after, being shot at does not turn it round (a crew can be pulled back).</summary>
            public float OrderedAt = -100f;

            /// <summary>When a man of this crew was last shot at. A crew of the outfit
            /// with no fight of its own answers one it is given (TickCombat).</summary>
            public float ProvokedAt = -100f;

            /// <summary>A rival crew that has had enough - its boss down and one man
            /// left - and is getting off the street.</summary>
            public bool Retreated;

            /// <summary>Hands up. The crew has given itself up to the law and is stood
            /// still with its guns away - out of every fight, its own and anybody's,
            /// until it is taken in (DemoCrews.TakeIn) or the arrest falls through.</summary>
            public bool Surrendered;

            /// <summary>The latest answer this physical crew gave the law, and the
            /// open paper that follows it until station, flight or trial resolves it.</summary>
            public bool HasDoorAnswer;
            public DoorAnswer LastDoorAnswer;
            public CourtCase ArrestCase;
            public Deed ArrestDeed = Deed.Affray;

            /// <summary>From hands-up until the station threshold. The crew stays in
            /// the organization and on every map, but no player order may move it.</summary>
            public bool InCustody;

            /// <summary>A first-leg custody was broken. Kept until the next booking so
            /// the prisoner's saved record and eventual verdict retain the fact.</summary>
            public bool CustodySprung;

            /// <summary>RUNNING. The player told this crew to break off and get away
            /// from the law (GAN-222). It is a state and not just an order because two
            /// other things read it: a running man never fires (the existing rule), and
            /// a crew is only allowed to go to ground once the pursuit is BROKEN, which
            /// is a thing that has to be watched over seconds.</summary>
            public bool Fleeing;

            /// <summary>When the run began, and when the law was last near enough to see
            /// them. Nobody vanishes in front of a patrol: the clock to going to ground
            /// starts at the last sighting, not at the order.</summary>
            public float FledAt = -1000f;
            public float SeenByLawAt = -1000f;
            public Vector3 FlightFrom;

            /// <summary>When the PLAYER last told this crew to shoot at the law.
            ///
            /// The arrest reads it and nothing else does (PoliceDispatch.Arrest,
            /// CONF-003): while an officer stands over the crew with the question put,
            /// an explicit attack order on a police unit is the player overruling the
            /// men's own answer. It is a stamp rather than a flag because the arrest has
            /// to tell an order given DURING its window from one given before it - a
            /// crew that was already at war with a squad has not answered anything.</summary>
            public float PoliceFightOrderedAt = -1000f;

            /// <summary>The tether's waiting ledger: seconds the boss has stood for a
            /// strung-out man without the gap improving, and the worst gap when he
            /// last checked. A wait that helps is free; one that does not is paid
            /// off after a few seconds and the crew walks on (TickCohesion).</summary>
            public float LingerDebt;
            public float WorstSeen = float.MaxValue;

            /// <summary>The man at the wheel was shot: the car is rolling to a stop and
            /// the crew is getting out of it.</summary>
            public bool DriverLost;

            /// <summary>The law: dealt in by the dispatcher, not a mob.</summary>
            public bool IsPolice => Faction == StreetAlarm.PoliceFaction;

            /// <summary>The boss and then his hoods. A struct walk rather than an
            /// iterator: it is read a dozen times a frame per unit, and the yield
            /// version put an enumerator on the heap for every one of them.</summary>
            public Members All() => new Members(this);

            public readonly struct Members : IEnumerable<CrewWalker>
            {
                readonly Unit _unit;
                public Members(Unit unit) { _unit = unit; }
                public Enumerator GetEnumerator() => new Enumerator(_unit);
                IEnumerator<CrewWalker> IEnumerable<CrewWalker>.GetEnumerator() => GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public struct Enumerator : IEnumerator<CrewWalker>
            {
                readonly Unit _unit;
                // -2 before the boss, -1 on the boss, then the index of the hood
                int _index;
                public Enumerator(Unit unit) { _unit = unit; _index = -2; Current = null; }
                public CrewWalker Current { get; private set; }
                object System.Collections.IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    if (_index == -2)
                    {
                        _index = -1;
                        if (_unit.Boss != null) { Current = _unit.Boss; return true; }
                    }
                    if (_index + 1 >= _unit.Hoods.Count) { Current = null; return false; }
                    _index++;
                    Current = _unit.Hoods[_index];
                    return true;
                }

                public void Reset() { _index = -2; Current = null; }
                public void Dispose() { }
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
                    if (Boss != null && !Boss.Dead && Boss.Tf) return Boss.Tf.position;
                    foreach (var m in All())
                        if (m != null && !m.Dead && m.Tf) return m.Tf.position;
                    return Vector3.zero;
                }
            }
        }

        // A lieutenant walks like a man who is expected; his hoods keep up, each at
        // his own pace - no two the same, none of them dawdling.
        // Metres a second at the walk. Up from 1.75/1.8-2.15: the outfit reads as men
        // with somewhere to be rather than as a stroll, and the ledger's own men keep
        // ahead of the citizens they push past (the crowd walks 1.25-1.85). It stops
        // here and not higher because the walk clip is keyed to the pace and past about
        // half again its own rate the feet read as a wind-up toy - the SPEED of a crew
        // getting somewhere comes from the run now (CrewWalker.Running), not from
        // winding the walk up.
        const float BossPace = 1.9f;
        float HoodPace() => 1.9f + (float)_variety.NextDouble() * 0.3f;
        const float Spacing = 1.7f;   // metres between men along the sidewalk
        const float MinSpawnLink = 12f;
        const float AlertRange = 24f; // a rival crew opens up on the outfit this close
        // a crew of ours answers fire this far off (a rifle's own reach and a little
        // more), and keeps its guns up this long after the last round came at it
        const float DefendRange = 30f, FightBack = 6f;

        /// <summary>How far a man can pick a mark out of the crew he is at war with.
        ///
        /// A crew's TARGET is a crew; the man he walks at was, until now, simply the
        /// nearest of them - and "nearest" was measured over the whole map. So a mob
        /// that traded shots with a motorcycle going past its own door turned, to a man,
        /// and set off across the quarter at a lieutenant three hundred metres away
        /// standing behind a building, whom none of them had ever laid eyes on. The
        /// player's words: the enemy crew should not automatically know where my
        /// lieutenant is standing.
        ///
        /// Generous - a long street, well past any gun's reach - because this is not a
        /// line of sight, it is the difference between fighting the men in front of you
        /// and knowing where everybody in the city is.</summary>
        internal const float SightRange = 70f;

        /// <summary>Seconds a crew keeps its guns up with nobody of its enemy in sight
        /// before it gives the fight up. Long enough to sit out a man ducking behind a
        /// car, short enough that a machine which has ridden off is gone.</summary>
        const float LoseSight = 8f;

        // ---------------------------------------------------------------- the chase
        //
        // WHAT A MAN SHOT AT FROM A PASSING MOTORCYCLE DOES NEXT. He runs after it -
        // a little way, down the street it went, shouting - and then he stops, because
        // he cannot see it any more and a man cannot chase what he cannot see.
        //
        // Which is not what happened. A man engaged on a rider kept the rider's LIVE
        // transform as his mark and ran at it with no limit of range at all, straight
        // through walls of the city, for as long as the crew held the fight - so a
        // machine that had ridden two streets away was still being run at, exactly, by
        // men who could not possibly know where it had gone. Then the fight timed out
        // and they stopped mid-street, all at once, like a switch. The player asked for
        // the opposite of both: "neprijatelji treba malo da trce za njim al ne da znaju
        // tacnu lokaciju gde je otisao."
        //
        // So: the mark is dropped the moment he is out of sight (SightRange, in
        // TickCombat), and what the crew is left holding is the last place it saw him
        // and the way he was pointing. A few of them run at THAT.

        /// <summary>Seconds a MAN keeps his gun on a mark he can no longer see. Long
        /// enough to carry him past a van or the corner of a building, short enough that
        /// he is never walking at somebody he lost - the crew's own memory of where the
        /// man went is the chase below, and that is a remembered point, not a live one.
        /// </summary>
        const float BlindGrace = 0.6f;

        /// <summary>Seconds out of sight before the crew sets off after him. Not nought:
        /// a machine going past at fifty flickers in and out of sight behind every van
        /// on the street, and a chase that starts on the first flicker starts six times
        /// in a firefight.</summary>
        public static float ChaseAfter = 1.5f;

        /// <summary>The longest ONE LEG of a search may take - a backstop and nothing
        /// more. A leg ends when the men get where they are going; this ends one whose
        /// walk never arrives (a point behind a shutter, a man wedged on a kerb).</summary>
        public static float ChaseSeconds = 25f;

        /// <summary>How long a man stands at the place he last saw somebody, looking,
        /// before the search is given up and he turns for home.</summary>
        public static float ChaseLook = 2.5f;

        /// <summary>How far from where the search STARTED it is allowed to get. Each
        /// fresh sighting lays a new leg from wherever the men have got to, so a crew
        /// that keeps catching glimpses can be drawn a long way down the quarter - but
        /// not across the town: past this they stop, whatever they can see, and go back
        /// to their own door.</summary>
        public static float SearchRange = 150f;

        /// <summary>How many of them go. Some run, the rest hold the door - a crew does
        /// not move as one for this.</summary>
        public static int Chasers = 3;

        /// <summary>Seconds before the same crew lays another leg. Short: this is the
        /// gap between the legs OF ONE SEARCH - they lose him behind a corner, run to
        /// the corner, see him again, and go again - and not a cooling-off. What stops
        /// a search going on for ever is <see cref="SearchRange"/> and nothing else.
        /// </summary>
        public static float ChaseAgainAfter = 2f;

        /// <summary>The men out running after somebody nobody can see. Short-lived and
        /// held here rather than on the man, exactly as a raid is (DemoCrews.DriveBy):
        /// a chase is the ARENA's idea, and a walker carries no memory of one.</summary>
        readonly HashSet<CrewWalker> _chasers = new HashSet<CrewWalker>();

        /// <summary>Is this man running after somebody out of sight? The tether must not
        /// touch him - hauling him "back to his crew" every scan is what turned the
        /// raid's own walk to the machine into a stall (see DemoCrews.DriveBy.OnRaid).</summary>
        bool Chasing(CrewWalker man) => man != null && _chasers.Contains(man);
        const int BossHealth = 4, HoodHealth = 3;
        const float HoldFireAfterOrder = 4f;
        /// <summary>Of the men shot down to their last hit, this many run. A field and
        /// not a const so the lab can turn it up (BlockDemoMission.panic) and make the
        /// runners it wants to watch the crews fight around appear every run.</summary>
        public float PanicChance = 0.4f;
        const float DeathReportDelay = 5f; // the skull stands this long, then the books are told
        const float CarCover = 0.55f;      // what the car's tin does to a round aimed at a rider
        const float BehindCover = 0.8f;    // what a car's flank does for a man crouched behind it
        const float MovingCarCover = 0.3f; // and what SPEED does: a rider going past at pace
        // What being DOWN behind it does, while he holds his fire. It was 0.25 and that
        // was a fight nobody could finish: cover on both sides halved every round's
        // chance (0.43 -> 0.20 measured), a firefight ran four times the rounds, and the
        // soak lost five seeds in thirty to crews wiped out and missions timed out
        // around it. A bin is protection, not a wall.
        const float DuckedCover = 0.45f;
        // A man leaning out of a window with his arm across the street is not shooting
        // the way he does stood on the pavement: he takes it further out and further
        // round. Without this a pass gave the guns about a second of the mark and a
        // drive-by went by without a round fired - five windows in two minutes of passes.
        // The accuracy still falls off with the range (Resolve), so the long ones mostly miss.
        const float RidingReach = 1.4f;
        const float RidingArc = 0.3f;      // how far round from abeam he can bring it (~72 deg)
        const float StrayChance = 0.12f;   // a round that missed its man, with a bystander in its way
        const float NerveRange = 8f;       // a friend going down this close may break a man
        const float HoodNerve = 0.25f, BossNerve = 0.05f;

        // Men down and not yet written off: after the delay the ledger strikes an
        // outfit man through (RosterOps.Kill - his crew passes on, his chip frees for a
        // recruit) and a rival is simply taken off his crew's roll. The body stays.
        readonly List<(CrewWalker man, float at)> _deaths = new List<(CrewWalker, float)>();

        // The outfit's men who ran from a fight: struck off the books as deserters the
        // moment they bolted, they keep running until they are out of sight, then go.
        readonly List<CrewWalker> _deserters = new List<CrewWalker>();

        /// <summary>A hood of the outfit who has broken and run: no man of the outfit runs
        /// and comes back - he is a deserter, off the crew and off the books (the ledger
        /// strikes him through), and he keeps running until he is gone. Lieutenants and
        /// rivals are not the books' business here: they run and stop, or come back.</summary>
        void OnFled(CrewWalker man, Vector3 from)
        {
            if (man == null || man.Dead || man.IsLieutenant || man.Retreating) return;
            var his = HouseOf(man.Faction);
            if (his == null) return;
            var unit = UnitOf(man);
            if (unit != null) unit.Hoods.Remove(man);
            _byCharacter.Remove(man.CharacterId);
            _deserters.Add(man);
            if (man.Tf) man.Tf.SetParent(_root, true);
            man.Retreat(from);
            // Only OUR deserters are news on our wire - a man walking out on the
            // Falcones is their trouble, and it lands on their books all the same.
            if (man.Faction == LivingCity.Gameplay.PlayerCommands.House.Value)
            {
                CrewOverlay.Announce(
                    Surname(man.DisplayName).ToUpperInvariant() + " DESERTED", 4f,
                    new Color(1f, 0.7f, 0.4f));
                var director = PersonnelDirector.Instance;
                if (director != null && director.Roster != null)
                    director.Desert(man.CharacterId);
            }
            else
            {
                LivingCity.Outfit.HouseOps.Desert(his, man.CharacterId);
            }
        }

        // ------------------------------------------------------------------ the bikes
        //
        // The outfit's two-wheelers, and the pairs of hoods on them. They are kept
        // apart from the crews on purpose. A Unit is a boss, his hoods, a stretch of
        // pavement he stands on and a car the ledger sold him - and a motorcycle is
        // none of those: the book does not sell one, two men is not a crew, and the
        // whole thing is a machine somebody took. So a bike is its own small thing with
        // its own two men, ticked here, and everything it needs from the arena - who to
        // shoot at, and what a bullet does - it asks for through the same doors any
        // other shot uses (FireFrom, NearestOf, Finished).

        /// <summary>The outfit's bikes on the street.</summary>
        public readonly List<CrewBike> Bikes = new List<CrewBike>();

        readonly List<CrewWalker> _bikeMen = new List<CrewWalker>();

        /// <summary>A motorcycle stood here with a hood at the bars and, if a second
        /// body is given and the machine has room, his mate behind him with the gun.
        /// Both are armed with the weapon named; either may be left null to have the
        /// pack's own cast picked. Null when there is no body for it.</summary>
        public CrewBike AddBike(GameObject prefab, Vector3 pos, Quaternion rot, float roadY,
            GameObject riderPrefab, GameObject pillionPrefab, GameObject weapon, EquipmentKind kind,
            string riderName = "Rider", string pillionName = "Pillion")
        {
            if (prefab == null || riderPrefab == null) return null;
            var net = Net ?? LaneNet.Active;
            // NOT simply where the caller pointed. A kerb is a queue of things already
            // standing on it, and two builders that both say "the south kerb, about
            // here" put a motorcycle inside the outfit's car - which is what the first
            // headless run of this did, for the whole run, the belt shoving at a bike
            // nobody was ever going to move. So the same search a man's car gets: out
            // from the point given, nearest first, to the first length of kerb nothing
            // else has claimed (CrewCars.KerbSlotNear reads the lane net's occupancy).
            if (net != null && CrewCars.MeasurePrefab(prefab, out float hl, out float hw))
            {
                hw = Mathf.Max(0.42f, hw);
                if (CrewCars.KerbSlotNear(net, pos, hl, hw, out var slot, out var facing))
                {
                    pos = new Vector3(slot.x, roadY, slot.z);
                    rot = facing;
                }
            }
            var bike = RoadBike.Build<CrewBike>(prefab, _root, pos, rot, roadY, net);
            if (bike == null) return null;
            bike.Arena = this;
            if (!bike.PlaceAt(pos, rot * Vector3.forward)) bike.GoFree(pos);

            // straight onto the machine: Mount puts him in the saddle, so the ground he
            // is instantiated over is nobody's business
            var rider = SpawnAt(riderPrefab, riderName, -1, pos, rot, Random.Range(1.35f, 1.55f), afoot: false);
            if (rider == null) { Destroy(bike.Tf.gameObject); return null; }
            Arm(rider, weapon, kind);
            _bikeMen.Add(rider);
            if (!bike.Mount(rider, pillion: false))
            {
                // the body would not take a rider's pose: better no bike than a man
                // standing on the tank
                Debug.LogWarning("[Crews] " + prefab.name + " would not seat a rider (not a humanoid body?)");
                _bikeMen.Remove(rider);
                rider.Dispose();
                Destroy(rider.Tf.gameObject);
                Destroy(bike.Tf.gameObject);
                return null;
            }

            if (pillionPrefab != null && bike.Body != null && bike.Body.SeatsTwo)
            {
                var mate = SpawnAt(pillionPrefab, pillionName, -1, pos, rot,
                    Random.Range(1.35f, 1.55f), afoot: false);
                if (mate != null)
                {
                    Arm(mate, weapon, kind);
                    if (bike.Mount(mate, pillion: true)) _bikeMen.Add(mate);
                    else { mate.Dispose(); Destroy(mate.Tf.gameObject); }
                }
            }

            bike.Halt(hard: true);
            bike.SettleStand();
            // on the street's own books, like every other vehicle on it - see
            // AddEmptyBike for what an unregistered one costs.
            StreetTraffic.Users.Add(bike);
            Bikes.Add(bike);
            return bike;
        }

        void Arm(CrewWalker man, GameObject weapon, EquipmentKind kind)
        {
            if (man == null || weapon == null) return;
            man.Arm(weapon, kind);
        }

        void TickBikes(float dt)
        {
            for (int i = 0; i < _bikeMen.Count; i++)
            {
                var man = _bikeMen[i];
                if (man != null && man.Tf != null) man.TickCrew(dt);
            }
            for (int i = 0; i < Bikes.Count; i++)
            {
                var bike = Bikes[i];
                if (bike == null || bike.Tf == null) continue;
                // A machine whose rider is shot goes DOWN, and everybody on it goes with
                // it - the man behind him does not take the bars (the raid ends the same
                // frame, DemoCrews.Over). Two men on one machine is one bullet from
                // being two men on the road, and that is the price of the order.
                //
                // It used to be done here, and wrongly: DismountAll set them both on
                // their feet beside a motorcycle that then stood upright in the middle of
                // the street with nobody holding it, and the dead one was hidden where he
                // stood a few seconds later (ReportDeaths). The machine owns the whole
                // business now - who is thrown, who dies, whether it goes over and
                // whether it burns - because it is the only thing that knows how fast it
                // was going (CrewBike.TickRiders).
                bike.Tick(dt);

                // and the tank goes, a few seconds after it caught. Not the machine's to
                // set off: an explosion is a thing that happens to whoever is standing
                // near it, and only the arena knows who that is.
                if (bike.TakeBlast())
                {
                    if (DriveTrace.On)
                        DriveTrace.Event("crewbike", bike.DisplayName, "the tank went up");
                    CrewOverlay.Announce("THE TANK'S GONE UP", 4f, new Color(1f, 0.55f, 0.3f));
                    Explosion.Blow(bike.Position + Vector3.up * 0.4f, this, null,
                        bike.Owner != null ? bike.Owner.Faction : 0, GroundY);
                    BurntOut(bike);
                }

                // a man the spill has finished with: on his feet in the road, or lying
                // where he stopped. Either way he is the crew's again and not the bike's.
                for (var landed = bike.TakeLanded(); landed != null; landed = bike.TakeLanded())
                {
                    if (landed.Dead)
                    {
                        // the pool goes where he ACTUALLY lies. At the moment the round
                        // landed he was on a moving machine, so it was held back then
                        // (Resolve, floor: false) - laid there it would be a stain in the
                        // middle of a carriageway thirty metres from the body.
                        CrewGore.Death(landed, GroundY);
                        continue;
                    }
                    Rejoin(landed);
                }
            }
        }

        /// <summary>A machine that has burnt out is off the outfit's books: struck out
        /// of the ledger, and the wreck left lying in the road as the scene's own.
        ///
        /// Without the first half the book still says the outfit owns a motorcycle, and
        /// StandLedgerBikes - which stands one machine per line of the book, outside the
        /// front - quietly puts a brand new one on its stand within the second. The
        /// player watches his machine explode and sees another appear at his door.</summary>
        void BurntOut(CrewBike bike)
        {
            if (bike == null) return;
            var roster = PersonnelDirector.Instance != null ? PersonnelDirector.Instance.Roster : null;
            if (roster != null && bike.ItemId >= 0) RosterOps.LoseItem(roster, bike.ItemId);
            bike.ItemId = -1;
            bike.Owner = null;
            _ledgerBikes.Remove(bike);
        }

        // Deserters run their own course: ticked here, off the street once they have
        // stopped running (out of sight), or once a round has finished them and the
        // body has been taken away.
        void TickDeserters(float dt)
        {
            for (int i = _deserters.Count - 1; i >= 0; i--)
            {
                var man = _deserters[i];
                if (man == null || man.Tf == null) { _deserters.RemoveAt(i); continue; }
                man.TickCrew(dt);
                bool gone = man.Dead ? !man.Tf.gameObject.activeSelf : man.State == CrewWalker.Mode.Standing;
                if (!gone) continue;
                _deserters.RemoveAt(i);
                man.Dispose();
                Destroy(man.Tf.gameObject);
            }
        }

        void ReportDeaths()
        {
            for (int i = _deaths.Count - 1; i >= 0; i--)
            {
                var (man, at) = _deaths[i];
                if (Time.time < at) continue;
                // NOT WHILE HE IS STILL COMING OFF A MOTORCYCLE. The books are told five
                // seconds after a man dies and the body is hidden with them - which,
                // for a man shot off a pillion, used to fire while he was in the air or
                // still sliding. He simply vanished, mid-fall, and the chalk was drawn
                // wherever he happened to be at that instant.
                if (man != null && man.Spilling) continue;
                _deaths.RemoveAt(i);
                if (man == null) continue;
                // the body is taken away; the police's chalk stays where it lay (a man
                // who died in a car leaves no chalk - the car took him)
                if (man.Tf != null)
                {
                    if (man.Tf.gameObject.activeSelf && !IsAboard(man)) CrewGore.Chalk(man, GroundY);
                    man.Tf.gameObject.SetActive(false);
                    foreach (var car in Cars) { car.Aboard.Remove(man); car.SeatOf.Remove(man); }
                }
                // Every house's book is told, not only ours. A rival shot dead is a
                // man struck off HIS family's roster - his gun back in their safe, his
                // crew passed to whoever was most loyal - and the FAMILIES page reads
                // one man fewer.
                var his = HouseOf(man.Faction);
                if (his != null)
                {
                    if (man.Faction == 0)
                    {
                        var director = PersonnelDirector.Instance;
                        if (director != null && director.Roster != null)
                            director.Kill(man.CharacterId);
                    }
                    else
                    {
                        LivingCity.Outfit.HouseOps.Kill(his, man.CharacterId);
                    }
                }
                else
                {
                    var unit = UnitOf(man);
                    if (unit == null) continue;
                    if (unit.Boss == man) unit.Boss = null;
                    unit.Hoods.Remove(man);
                }
            }
        }

        public readonly List<Unit> Units = new List<Unit>();
        public Unit Selected { get; private set; }

        /// <summary>The outfit's cars on the street - one per vehicle in the ledger the
        /// scene chose to stand a body for (AddCar). Empty in a scene without one.</summary>
        public readonly List<CrewCar> Cars = new List<CrewCar>();

        /// <summary>The street's centre line (along X) a car keeps to on its passes;
        /// NaN when the ground has no street.</summary>
        public float StreetZ = float.NaN;

        /// <summary>Off the sidewalk graph: straight strides over open floor.</summary>
        public bool FreeRoam { get; private set; }

        /// <summary>The floor's height, for the right-click pick.</summary>
        public float GroundY { get; private set; } = 0.1f;

        /// <summary>The arena's rule: a man the ledger left unarmed still draws the
        /// default sidearm here. Off, and he stands empty-handed as the book says.</summary>
        public bool EveryoneArmed = true;

        /// <summary>The roads the cars drive - the scene's lane network (LaneNet: the
        /// crew demo's four streets, the city's grid). Null: open ground, straight lines.</summary>
        public LaneNet Net;

        /// <summary>Give a man a car on the armory page and it is standing at the kerb
        /// beside him the moment the book closes. Off: a scene stands its own cars and
        /// the ledger only says whose they are (which is all it used to do).</summary>
        public bool LedgerCarsStand = true;

        /// <summary>The asphalt's height - where a car sits, as against GroundY, which
        /// is the pavement a man walks on. The city's roads are at zero; a scene that
        /// sinks its street says so here.</summary>
        public float CarRoadY = 0f;

        /// <summary>The bang, flash, muzzle smoke and blood - set by the scene builder;
        /// missing pieces are simply silent.</summary>
        public GameObject MuzzleFlashPrefab, GunSmokePrefab, BloodPrefab, ImpactPrefab;
        /// <summary>One weapon's reports. An array of these rather than a dictionary
        /// because Unity serialises the one and not the other.</summary>
        [System.Serializable]
        public sealed class WeaponSounds
        {
            public EquipmentKind Kind;
            public AudioClip[] Clips = System.Array.Empty<AudioClip>();
        }

        public WeaponSounds[] GunshotSets = System.Array.Empty<WeaponSounds>();
        public AudioClip CrackClip;

        /// <summary>Reference pixels from the top of the screen to the crew bar - the
        /// road demo sets it under its top bar. Read at Init.</summary>
        public float BarTopInset = 8f;

        /// <summary>The old live-feed crew row is retired in the city, but small test
        /// scenes can opt back into the same shared selection surface. Default false
        /// keeps every existing city/demo presentation unchanged.</summary>
        public bool ShowCrewBar;

        /// <summary>Deal the imported masculine/feminine and sidearm locomotion by
        /// body in this scene. CoverDemo opts in; the live city remains on its current
        /// Synty wardrobe until that migration is requested explicitly.</summary>
        public bool UseMixamoLocomotion;

        List<PedLink> _links;
        List<PedLink> _sidewalks;
        PedClips _clips;
        List<GameObject> _fallbackPrefabs;
        Transform _root;
        int _seenVersion = -1;
        // PersonnelDirector and the street are dealt on different lifecycle beats.
        // Select the first playable outfit crew once it actually exists so an opening
        // right-click is an order, not a silent no-op. Later explicit deselection stays
        // respected across ordinary roster refreshes.
        bool _initialPlayerSelectionMade;
        readonly Dictionary<int, CrewWalker> _byCharacter = new Dictionary<int, CrewWalker>();
        System.Random _rng;
        readonly System.Random _variety = new System.Random(4242); // gaits, falls, paces
        // Fog of war is block-shaped, not a dark screen-space effect. Each living outfit
        // member contributes the block he occupies; duplicate crew members in one block
        // collapse into this small shared set once per frame.
        // The widest Core road is its 35 m boulevard. A point inside another block is
        // rejected before this reach is used, so widening the street does not reveal
        // the neighbouring parcel.
        const float MapStreetVisionDepth = 35f;
        const float FreeRoamVisionRadius = 60f;
        readonly List<CityBlocks.BlockInfo> _mapVisionBlocks =
            new List<CityBlocks.BlockInfo>();
        readonly HashSet<int> _mapVisionBlockIds = new HashSet<int>();
        int _mapVisionFrame = -1;

        sealed class FogRenderGroup
        {
            public readonly Renderer[] Renderers;
            public readonly bool[] ForcedBeforeFog;
            public bool Hidden;
            public int SeenFrame;

            public FogRenderGroup(Transform root)
            {
                Renderers = root.GetComponentsInChildren<Renderer>(true);
                ForcedBeforeFog = new bool[Renderers.Length];
            }
        }

        readonly Dictionary<Transform, FogRenderGroup> _worldFog =
            new Dictionary<Transform, FogRenderGroup>();
        readonly List<Transform> _worldFogPrune = new List<Transform>();
        int _worldFogPruneAt;
        DemoParkedCarGlow _worldFogParkedCars;
        Vector3 _outfitAnchor, _outfitFacing = Vector3.forward;
        float _outfitSpread = 9f;
        /// <summary>
        /// Bodies on NOBODY'S BOOKS: the law's squads and the bench scenes' hand-dealt
        /// mobs. Every FAMILY's man carries his own house's character id now - he is a
        /// Character with a name, a temper and a wage - so the only negatives left on
        /// the street belong to people no ledger anywhere knows about.
        /// </summary>
        int _streetIds = -1;
        int _anonymousCharacterId = -100000;

        /// <summary>
        /// The families that have BODIES in this city. The player's outfit always has;
        /// a rival joins when the city seats him a front and posts his crews
        /// (RoadDemoBuilder.SpawnRivals). A house nobody seated is still a house - its
        /// books, its safe and its wage bill all run - it simply has nobody standing on
        /// the pavement, which is what the paper clock is for (RIVAL-008).
        /// </summary>
        readonly List<int> _houses =
            new List<int> { LivingCity.Gameplay.PlayerCommands.House.Value };

        /// <summary>Where a crew that has never stood on this street is put the first
        /// time the books ask for it: outside its family's own door, or on a corner of
        /// its own. Posted by whoever laid the city out; read once per crew and then
        /// never again, because after that the men are simply where they are.</summary>
        readonly Dictionary<int, (Vector3 anchor, Vector3 facing)> _postings =
            new Dictionary<int, (Vector3, Vector3)>();

        /// <summary>This family stands men in this city.</summary>
        public void SeatHouse(int gangId)
        {
            if (gangId >= 0 && !_houses.Contains(gangId))
                _houses.Add(gangId);
        }

        /// <summary>Where this crew opens up, the first time it is dealt.</summary>
        public void PostCrew(int crewId, Vector3 anchor, Vector3 facing) =>
            _postings[crewId] = (anchor, facing.sqrMagnitude > 1e-4f
                ? facing.normalized : Vector3.forward);

        bool Stands(int gangId) => _houses.Contains(gangId);

        /// <summary>A unit that answers to a book: a family's crew, and not the law's
        /// squad or a bench scene's hand-dealt mob.</summary>
        bool OnTheBooks(Unit unit) =>
            unit != null && unit.Faction >= 0 && Stands(unit.Faction);
        int _anthropometrySeed = 1987;
        AudioSource _shots, _cracks;

        /// <summary>The shared view of targeting and cover decisions. It is attached
        /// wherever DemoCrews is used and toggled with I.</summary>
        public CombatIntentOverlay IntentOverlay { get; private set; }

        // ------------------------------------------------------------------ setup

        /// <summary>The seed the whole city was dealt from. Anything that has to roll
        /// the same answer twice for the same street - the arrest's fight-or-surrender
        /// among them - mixes its own stream off this rather than reaching for
        /// UnityEngine.Random.</summary>
        public int CitySeed => _anthropometrySeed;

        /// <summary>Why the crew would not take the order just given it. Null when it
        /// took it. Read by whoever offered the order (CrewOverlay.Refuse) so the reason
        /// reads in the words of the system that refused, exactly as CarRefusal does.</summary>
        public string OrderRefusal { get; private set; }

        /// <summary>A crew at gunpoint with its hands up takes no orders at all - not a
        /// move, not a fight. It is the one state where the player has genuinely lost
        /// the men until the officer is done with them (or somebody shoots him).</summary>
        const string HandsUpRefusal = "Hands up at gunpoint - he takes no orders";

        /// <summary>Once seated for the station he is still on the map and in the
        /// organization, but no player command reaches him.</summary>
        public const string InCustodyRefusal = "In police custody - he takes no orders";

        static bool CustodyRefuses(Unit unit) =>
            unit != null && CustodyPlan.RefusesOrders(unit.InCustody);

        /// <summary>Shared gate for command surfaces that live outside DemoCrews, such
        /// as a premises' TAKE THEM INSIDE row.</summary>
        internal bool AcceptsPlayerOrder(Unit unit)
        {
            OrderRefusal = null;
            if (!CustodyRefuses(unit)) return true;
            OrderRefusal = InCustodyRefusal;
            return false;
        }

        /// <summary>The city: crews dealt onto the sidewalk graph.</summary>
        public void Init(List<PedLink> links, PedClips clips, List<GameObject> fallbackPrefabs,
            int citySeed = 1987)
        {
            _anthropometrySeed = citySeed;
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
            Vector3 anchor, Vector3 facing, float spread, float groundY, int citySeed = 1987)
        {
            _anthropometrySeed = citySeed;
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
            // The scene's crews, for whoever is handed one MAN and needs the rest of his
            // (DemoCrews.Active - the doorway beat brings a crew to the door with its
            // lieutenant).
            Active = this;

            // the fence is a scene's duty, and forgetting it must be loud: with
            // WalkObstacles.City empty, "anywhere" is legal ground and men wander,
            // flee and are stood out on the bare backdrop. The behaviour is one
            // (CrewWalker/WalkObstacles enforce it everywhere); the scene only says
            // where its floor ends - before this Init, like every builder does.
            if (WalkObstacles.City.Count == 0)
                Debug.LogWarning("[Crews] No city fence laid (WalkObstacles.City is empty) - " +
                                 "men may be sent or stood ANYWHERE, including off the set. " +
                                 "The builder should fence its floor before the crews are dealt.");
            _root = new GameObject("Crews").transform;
            gameObject.AddComponent<CrewOverlay>().Init(this);
            gameObject.AddComponent<CrewBar>().Init(this, BarTopInset, ShowCrewBar);
            // The screen-edge paperwork: the picked lieutenant's file, the wire and the
            // key that opens the book. It reads this instance and never writes to it.
            gameObject.AddComponent<StreetHud>().Init(this);
            // The one thing the campaign cannot say for itself: that it is over
            // (RANK-002). It watches the runner and paints nothing until it has to.
            gameObject.AddComponent<OutfitEnd>();
            // last onto the click chain, so the front card is asked first and hands the
            // click straight back to the crews if a man was standing in front of the door
            gameObject.AddComponent<FrontOverlay>().Init();
            // The paint on the pavement outside every place we hold, and outside every
            // rival door a crew of ours has found. It polls the fronts rather than being
            // handed them: the families are seated after the crews are stood up.
            gameObject.AddComponent<TurfMarks>().Init(this);
            gameObject.AddComponent<FrontDeeds>();
            IntentOverlay = gameObject.AddComponent<CombatIntentOverlay>();
            IntentOverlay.Init(this);
            MapVisionRegistry.RegisterArea(this);
            PersonnelDirector.Instance?.SetOrganizationPhysicalSource(this);
            CrewWalker.FindCover = CoverNear;
            // and the ambush's own question - a flank round THERE, facing THAT way -
            // which a man asks again for himself when the car he was behind drives off
            CrewWalker.FindFlankAround = FlankAround;
            PrepareCombatPrewarm();
        }

        // After the animation has posed every man for the frame, the fighters' gun
        // arms are turned onto their marks - the one bone write that makes a shot
        // read as a shot at a man and not at the pavement (CrewWalker.AimGun).
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            foreach (var unit in Units)
                foreach (var man in unit.All())
                    man.AimGun(dt);
            ApplyWorldFog();
            // after the arms are posed: were this frame's shots actually ON their marks?
            if (DriveTrace.On) CrewAudit.LateTick();
        }

        /// <summary>Take a unit off the street - its men gone (the police driving
        /// away). Nothing for the outfit's own: the ledger owns those.</summary>
        public void RemoveUnit(Unit unit)
        {
            // A crew on somebody's books is never simply deleted: its men are struck off
            // through that house's own roster and the deal takes their bodies away.
            if (unit == null || OnTheBooks(unit)) return;
            foreach (var man in unit.All())
            {
                _chasers.Remove(man);
                man.Dispose();
                if (man.Tf) Destroy(man.Tf.gameObject);
            }
            _deaths.RemoveAll(d => d.man != null && (unit.Boss == d.man || unit.Hoods.Contains(d.man)));
            unit.Boss = null;
            unit.Hoods.Clear();
            if (unit.Root) Destroy(unit.Root.gameObject);
            Units.Remove(unit);
            if (Selected == unit) Selected = null;
        }

        /// <summary>A crew on NOBODY'S BOOKS, dealt by hand - the law's squad, a bench
        /// scene's mob. A family's crew is not dealt here any more: it comes off its own
        /// house's roster through Sync, like the player's.
        ///
        /// The old doc, still true of what is left: its lieutenant and hoods stood at the
        /// anchor facing <paramref name="facing"/>, all carrying <paramref name="weapon"/> -
        /// unless <paramref name="armsFor"/> is given, which is asked man by man (0 the
        /// lieutenant, 1.. his hoods) and lets a mob carry a piece each rather than five
        /// copies of one gun.</summary>
        public Unit AddRival(int faction, string gangName, string bossName, GameObject bossPrefab,
            IList<string> hoodNames, IList<GameObject> hoodPrefabs, Vector3 anchor, Vector3 facing,
            GameObject weapon, EquipmentKind weaponKind, bool lineUp = false,
            System.Func<int, (GameObject weapon, EquipmentKind kind)> armsFor = null,
            PedLink spawnLink = null, float spawnT = 0f)
        {
            var unit = new Unit
            {
                CrewId = _streetIds--,
                Faction = faction,
                GangName = gangName,
                Name = bossName,
                Root = new GameObject("Rival · " + gangName + " · " + bossName).transform,
            };
            unit.Root.SetParent(_root, false);
            var rot = Quaternion.LookRotation(facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.back);

            var anthropometrySalt = faction == StreetAlarm.PoliceFaction
                ? PedestrianAnthropometry.PoliceSalt
                : PedestrianAnthropometry.GangSalt;

            var boss = spawnLink != null
                ? SpawnAt(bossPrefab, bossName, _streetIds--, spawnLink, spawnT,
                    BossPace, anthropometrySalt)
                : SpawnAt(bossPrefab, bossName, _streetIds--, anchor, rot, BossPace,
                    anthropometrySalt: anthropometrySalt);
            if (boss != null)
            {
                boss.IsLieutenant = true;
                boss.Faction = faction;
                boss.CrowdGroupId = unit.CrowdGroupId;
                boss.MaxHealth = boss.Health = BossHealth;
                boss.RoamsAlone = false;   // a crew holds its ground - its boss too
                boss.RoamReach = 14f;
                boss.Post = anchor;
                var (bossGun, bossKind) = armsFor != null ? armsFor(0) : (weapon, weaponKind);
                boss.Arm(bossGun, bossKind);
                boss.Tf.SetParent(unit.Root, true);
                unit.Boss = boss;
            }
            for (int k = 0; k < hoodNames.Count; k++)
            {
                var prefab = hoodPrefabs.Count > 0 ? hoodPrefabs[k % hoodPrefabs.Count] : bossPrefab;
                // a crew loafing on a pavement strings out along it rather than
                // wedging back into the shopfront behind
                var pos = anchor + rot * (lineUp ? LineOffset(unit.CrewId, k) : FormationOffset(unit.CrewId, k));
                var hood = spawnLink != null
                    ? SpawnAt(prefab, hoodNames[k], _streetIds--, spawnLink,
                        Mathf.Clamp(spawnT - (k + 1) * Spacing, 0.3f,
                            spawnLink.Length - 0.3f), HoodPace(), anthropometrySalt)
                    : SpawnAt(prefab, hoodNames[k], _streetIds--, pos, rot, HoodPace(),
                        anthropometrySalt: anthropometrySalt);
                if (hood == null) continue;
                hood.Faction = faction;
                hood.CrowdGroupId = unit.CrowdGroupId;
                hood.MaxHealth = hood.Health = HoodHealth;
                hood.RoamsAlone = false;
                hood.HoldLane(FormationLane(unit.CrewId, k));
                var (hoodGun, hoodKind) = armsFor != null ? armsFor(k + 1) : (weapon, weaponKind);
                hood.Arm(hoodGun, hoodKind);
                hood.Tf.SetParent(unit.Root, true);
                unit.Hoods.Add(hood);
            }
            Units.Add(unit);
            return unit;
        }

        void Update()
        {
            TickCombatPrewarm();
            var director = PersonnelDirector.Instance;
            director?.SetOrganizationPhysicalSource(this);
            var underworld = LivingCity.Outfit.Underworld.Current;
            // EVERY standing house is re-dealt off one key: the player's own dirty
            // count plus the sum of the houses' own, so a man recruited, killed or
            // struck off anywhere in the city re-deals the street once.
            var books = underworld != null
                ? (director != null ? director.Version : 0) + underworld.Version
                : -1;
            if (director != null && underworld != null && director.Roster != null &&
                (FreeRoam || (_sidewalks != null && _sidewalks.Count > 0)) &&
                books != _seenVersion)
            {
                _seenVersion = books;
                _rng ??= new System.Random(director.Seed * 7919 + 13);
                Sync(underworld);
            }

            float dt = Time.deltaTime;
            ReportDeaths();
            // BEFORE the fight: the ledger's orders are what put a crew somewhere and
            // what sets it on somebody, so a job that starts this frame must have its
            // march and its mark in hand before TickCombat reads either.
            CrewJobs.Tick(this);
            // And the crews that were told to go indoors: the walk to the door, the men
            // filing through it, and the street left without them (CrewQuarters).
            CrewQuarters.Tick(this);
            TickCombat();
            // AFTER the fight is settled for the frame: TickCombat is what starts a
            // chase and what ends one by seeing the man again, so asking this first
            // would tick a chase a frame stale every time.
            TickChase();
            // the traffic's picture of who is on foot in the road this frame
            StreetTraffic.Bodies.Clear();
            foreach (var unit in Units)
                foreach (var man in unit.All())
                    // A MAN ON A MOTORCYCLE IS NOT A BODY IN THE ROAD. IsAboard is the
                    // car's question and answers no for a rider, so the two men on a
                    // machine were posted here as two men stood in the carriageway - at
                    // the machine's own position, moving with it. The traffic gave way
                    // to them, and so did the machine they were sitting on: it crawled
                    // the whole run at the walking pace a driver holds behind a body he
                    // cannot get round (1.5 m/s, for two hundred seconds, in the first
                    // headless run of a drive-by). Riding is the test, not aboard.
                    if (!man.Dead && man.Tf && man.Tf.gameObject.activeSelf &&
                        !IsAboard(man) && !man.Riding)
                        StreetTraffic.Bodies.Add(
                            new StreetTraffic.Body(man.Tf.position, man.Faction));
            foreach (var man in _deserters)
                if (!man.Dead && man.Tf && man.Tf.gameObject.activeSelf)
                    StreetTraffic.Bodies.Add(
                        new StreetTraffic.Body(man.Tf.position, man.Faction));
            TickDeserters(dt);
            foreach (var unit in Units)
                foreach (var man in unit.All())
                    man.TickCrew(dt); // riders too: their pose (the seat, the gun out of the window) lives here
            TickBikes(dt);
            TickDriveBy(dt);
            TickCars(dt);
            TickRunDown();
            Separate(dt);
            _cohesionScan -= dt;
            if (_cohesionScan <= 0f)
            {
                _cohesionScan = 0.7f;
                TickCohesion();
            }

            // men with time on their hands find each other for a word
            _chatScan -= dt;
            if (_chatScan <= 0f)
            {
                _chatScan = 2f;
                PairChats();
            }

            if (Selected != null && Selected.Wiped)
                Selected = null;

            // the watchdog: when a run is being watched, the crews' own rules are
            // measured against them every frame and broken ones go down as fault rows
            if (DriveTrace.On) CrewAudit.Tick(this, dt);
        }

        void OnDestroy()
        {
            MapVisionRegistry.UnregisterArea(this);
            ClearWorldFog();
            foreach (var unit in Units)
                foreach (var man in unit.All())
                    man.Dispose();
            // the cover hook is a static: left pointing here it keeps a destroyed arena
            // alive and answers the next scene's walkers with this one's floor
            if (CrewWalker.FindCover != null && ReferenceEquals(CrewWalker.FindCover.Target, this))
                CrewWalker.FindCover = null;
            if (CrewWalker.FindFlankAround != null &&
                ReferenceEquals(CrewWalker.FindFlankAround.Target, this))
                CrewWalker.FindFlankAround = null;
            if (Active == this) Active = null;
        }

        /// <summary>The crews standing in this scene, for the systems that are handed a
        /// MAN and have to find the men around him - the doorway beat, which sends a
        /// lieutenant into a shop and has to bring his crew to the door with him. Set in
        /// Init and dropped with the scene, the way every other runtime here does it.</summary>
        public static DemoCrews Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetActiveForPlay() => Active = null;

        // ------------------------------------------------------------------ the door

        /// <summary>How far out from the doorstep the nearest pair of guards stand, and
        /// how much further out each pair behind them. Close enough to be HIS men and
        /// not a queue; far enough off the door that the man going in walks through a
        /// gap and not through them.</summary>
        const float GuardSpread = 1.7f;
        const float GuardStandOff = 0.7f;

        /// <summary>
        /// THE CREW COMES TO THE DOOR. One man of a crew is sent into a shop - to lean on
        /// the owner, to take the week's money, to turn the place over - and until this
        /// existed he walked there alone: the lieutenant crossing a street on his own
        /// while three hoods stood where the last order left them, which is not a family
        /// calling on a shopkeeper, it is a man running an errand.
        ///
        /// So the rest of the crew walks to the same doorstep and stands off it in pairs,
        /// EYES ON THE STREET - the doorway is his business and the pavement is theirs.
        /// They keep that heading while they wait (CrewWalker.WatchToward), so a crew
        /// waiting at a door reads as a crew waiting at a door and not as four men who
        /// happen to be standing near one.
        /// </summary>
        /// <param name="man">The one going in. He is left alone - the beat owns his walk.</param>
        /// <param name="doorstep">Where he will stand to go in.</param>
        /// <param name="outward">Which way is out to the street, from the shop's front.</param>
        public bool GuardDoor(CrewWalker man, Vector3 doorstep, Vector3 outward)
        {
            var unit = UnitOf(man);
            if (unit == null || unit.Faction != 0 || unit.Wiped)
                return false;

            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f)
            {
                outward = man != null && man.Tf != null
                    ? man.Tf.position - doorstep
                    : Vector3.forward;
                outward.y = 0f;
                if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;
            }
            outward.Normalize();
            var lateral = Vector3.Cross(Vector3.up, outward);

            var posted = 0;
            var k = 0;
            foreach (var other in unit.All())
            {
                if (other == null || other == man || other.Dead || other.Tf == null) continue;
                if (other.Riding || IsAboard(other) || other.Panicked) continue;
                // A man in a fight is not a doorman. The beat itself refuses a visit
                // under fire, and a hood already shooting is left to it.
                if (other.Target != null) continue;

                var side = (k % 2 == 0) ? 1f : -1f;
                var rank = k / 2;
                var want = doorstep
                           + lateral * (side * (GuardSpread + rank * 1.1f))
                           + outward * (GuardStandOff + rank * 0.5f);
                var spot = WalkObstacles.ClearSpot(want, WalkObstacles.Radius, 4f);
                Unwedge(other);
                // A rejected collision-aware route is a real refusal. Falling back to
                // the raw point order here let a doorman cut through the very cafe or
                // furniture which made OrderAcross fail.
                if (!other.OrderAcross(spot)) continue;
                // The order clears the last watch; the new one is set behind it, so it
                // survives the walk and takes hold when he stops.
                other.WatchToward(outward);
                other.Post = doorstep;
                k++;
                posted++;
            }

            return posted > 0;
        }

        // ---------------------------------------------------------- map visibility

        /// <summary>
        /// The same intelligence rule used by both paper maps, applied to 3D actors.
        /// forceRenderingOff hides meshes and shadows without touching activity, physics,
        /// animation time, traffic occupancy or streamed-holder lifetime.
        /// </summary>
        void ApplyWorldFog()
        {
            var walkers = PedestrianAgent.Everyone;
            for (var i = 0; i < walkers.Count; i++)
                TouchWorldFog(walkers[i]?.Tf);

            var cars = RoadCar.All;
            for (var i = 0; i < cars.Count; i++)
                TouchWorldFog(cars[i]?.Tf);

            var stoodCars = StoodCar.All;
            for (var i = 0; i < stoodCars.Count; i++)
                TouchWorldFog(stoodCars[i]?.Tf);

            if (_worldFogParkedCars == null && Time.frameCount >= _worldFogPruneAt)
                _worldFogParkedCars = FindFirstObjectByType<DemoParkedCarGlow>();
            if (_worldFogParkedCars != null)
                foreach (var car in _worldFogParkedCars.VisualCars)
                    TouchWorldFog(car);

            var cityWalkers = LivingCity.Entities.PedestrianAgent.Agents;
            for (var i = 0; i < cityWalkers.Count; i++)
                if (cityWalkers[i] != null)
                    TouchWorldFog(cityWalkers[i].transform);

            // The generated-city specialists (officers, gang members, school children,
            // visitors and buses) deliberately stay outside both pedestrian lists but
            // share the moving-subject overlay registry. Squares are places/buildings
            // and remain visible; diamonds are people or vehicles.
            var subjects = LivingCity.UI.OverlayRegistry.Subjects;
            for (var i = 0; i < subjects.Count; i++)
            {
                var subject = subjects[i];
                if (subject == null ||
                    subject.MarkerShape != LivingCity.UI.OverlayShape.Diamond)
                    continue;
                TouchWorldFog(subject.OverlayAnchor);
            }

            var ambient = ResidentialBlockLife.ActivePopulations;
            for (var i = 0; i < ambient.Count; i++)
            {
                var life = ambient[i];
                if (life == null)
                    continue;
                for (var actor = 0; actor < life.VisionActorCount; actor++)
                    TouchWorldFog(life.VisionActorAt(actor));
            }

            if (Time.frameCount < _worldFogPruneAt)
                return;
            _worldFogPruneAt = Time.frameCount + 60;
            RoadCar.PruneRegistered();
            _worldFogPrune.Clear();
            foreach (var pair in _worldFog)
                if (pair.Key == null || pair.Value.SeenFrame != Time.frameCount)
                    _worldFogPrune.Add(pair.Key);
            for (var i = 0; i < _worldFogPrune.Count; i++)
            {
                var root = _worldFogPrune[i];
                if (_worldFog.TryGetValue(root, out var group))
                    SetWorldFog(group, false);
                _worldFog.Remove(root);
            }
        }

        void TouchWorldFog(Transform root)
        {
            if (root == null || !root.gameObject.activeInHierarchy)
                return;

            if (!_worldFog.TryGetValue(root, out var group))
            {
                group = new FogRenderGroup(root);
                _worldFog.Add(root, group);
            }
            group.SeenFrame = Time.frameCount;
            SetWorldFog(group, !MapVisionRegistry.IsVisible(root.position));
        }

        static void SetWorldFog(FogRenderGroup group, bool hidden)
        {
            if (group == null)
                return;

            if (group.Hidden == hidden)
            {
                if (hidden)
                    for (var i = 0; i < group.Renderers.Length; i++)
                        if (group.Renderers[i] != null)
                            group.Renderers[i].forceRenderingOff = true;
                return;
            }

            for (var i = 0; i < group.Renderers.Length; i++)
            {
                var renderer = group.Renderers[i];
                if (renderer == null)
                    continue;
                if (hidden)
                    group.ForcedBeforeFog[i] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = hidden || group.ForcedBeforeFog[i];
            }
            group.Hidden = hidden;
        }

        void ClearWorldFog()
        {
            foreach (var group in _worldFog.Values)
                SetWorldFog(group, false);
            _worldFog.Clear();
            _worldFogPrune.Clear();
        }

        bool IMapVisionAreaSource.VisionActive => isActiveAndEnabled;

        bool IMapVisionAreaSource.IsVisible(Vector3 worldPosition)
        {
            RefreshMapVisionBlocks();

            var target = new Vector2(worldPosition.x, worldPosition.z);
            if (_mapVisionBlocks.Count == 0)
                return FreeRoamMapVisible(target);

            // Another block never leaks through an expanded rectangle. The expansion
            // below is only for a person or car standing on the road around a revealed
            // block, not for intelligence from inside the neighbouring block.
            var occupied = CityBlocks.At(target);
            if (occupied != null)
                return _mapVisionBlockIds.Contains(occupied.Id);

            float streetSqr = MapStreetVisionDepth * MapStreetVisionDepth;
            for (var i = 0; i < _mapVisionBlocks.Count; i++)
                if (SqrDistanceTo(_mapVisionBlocks[i].Union, target) <= streetSqr)
                    return true;
            return false;
        }

        void RefreshMapVisionBlocks()
        {
            if (_mapVisionFrame == Time.frameCount)
                return;

            _mapVisionFrame = Time.frameCount;
            _mapVisionBlocks.Clear();
            _mapVisionBlockIds.Clear();

            var blocks = CityBlocks.Blocks;
            if (blocks.Count == 0)
                return;

            foreach (var unit in Units)
            {
                if (unit == null || unit.Faction != 0)
                    continue;

                // MEN INDOORS STILL HOLD THE STREET THEY ARE ON. A crew moved into one
                // of our own buildings is switched off where it stands (CrewQuarters),
                // and reading the fog off standing bodies alone would put the block the
                // outfit is actually sitting in back into the dark.
                if (CrewQuarters.TryGetDoorstep(unit, out var billet))
                {
                    var doorstep = new Vector2(billet.x, billet.z);
                    var held = CityBlocks.At(doorstep) ?? ClosestBlock(doorstep, blocks);
                    if (held != null && _mapVisionBlockIds.Add(held.Id))
                        _mapVisionBlocks.Add(held);
                }

                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null ||
                        !man.Tf.gameObject.activeInHierarchy)
                        continue;

                    var at = new Vector2(man.Tf.position.x, man.Tf.position.z);
                    var block = CityBlocks.At(at) ?? ClosestBlock(at, blocks);
                    if (block != null && _mapVisionBlockIds.Add(block.Id))
                        _mapVisionBlocks.Add(block);
                }
            }
        }

        bool FreeRoamMapVisible(Vector2 target)
        {
            float radiusSqr = FreeRoamVisionRadius * FreeRoamVisionRadius;
            foreach (var unit in Units)
            {
                if (unit == null || unit.Faction != 0)
                    continue;
                // The same for the open-floor scenes: a crew indoors still sees out.
                if (CrewQuarters.TryGetDoorstep(unit, out var billet) &&
                    (target - new Vector2(billet.x, billet.z)).sqrMagnitude <= radiusSqr)
                    return true;
                foreach (var man in unit.All())
                {
                    if (man == null || man.Dead || man.Tf == null ||
                        !man.Tf.gameObject.activeInHierarchy)
                        continue;
                    var at = new Vector2(man.Tf.position.x, man.Tf.position.z);
                    if ((target - at).sqrMagnitude <= radiusSqr)
                        return true;
                }
            }
            return false;
        }

        static CityBlocks.BlockInfo ClosestBlock(
            Vector2 point, IReadOnlyList<CityBlocks.BlockInfo> blocks)
        {
            CityBlocks.BlockInfo closest = null;
            float closestSqr = float.MaxValue;
            for (var i = 0; i < blocks.Count; i++)
            {
                float sqr = SqrDistanceTo(blocks[i].Union, point);
                if (sqr >= closestSqr)
                    continue;
                closestSqr = sqr;
                closest = blocks[i];
            }
            return closest;
        }

        static float SqrDistanceTo(Rect rect, Vector2 point)
        {
            float dx = point.x < rect.xMin ? rect.xMin - point.x
                : point.x > rect.xMax ? point.x - rect.xMax : 0f;
            float dy = point.y < rect.yMin ? rect.yMin - point.y
                : point.y > rect.yMax ? point.y - rect.yMax : 0f;
            return dx * dx + dy * dy;
        }

        // ------------------------------------------------------------------ orders

        public void Select(Unit unit)
        {
            Selected = unit != null && unit.Faction == 0 &&
            !unit.IsDetachment ? unit : null;
            CrewSpeech.Selected(Selected);
        }

        /// <summary>The house a body belongs to, or null when it belongs to none -
        /// the law, a bench scene's mob, a passer-by.</summary>
        LivingCity.Outfit.House HouseOf(int faction)
        {
            if (faction < 0 || !Stands(faction))
                return null;
            var underworld = LivingCity.Outfit.Underworld.Current;
            return underworld?.Of(faction);
        }

        /// <summary>The unit a screen pick landed on, by the man it hit.</summary>
        public Unit UnitOf(CrewWalker man)
        {
            foreach (var unit in Units)
                if (unit.Boss == man || unit.Hoods.Contains(man)) return unit;
            return null;
        }

        /// <summary>The outfit's crew carrying this crew number, if it is still standing
        /// on the street. The books name a crew and the surfaces that order one about -
        /// the block file, the paper map, the billet - have to find its men.</summary>
        public Unit UnitOfCrew(int crewId)
        {
            for (int i = 0; i < Units.Count; i++)
            {
                var unit = Units[i];
                if (unit == null || !OnTheBooks(unit) || unit.IsDetachment) continue;
                if (unit.CrewId == crewId) return unit;
            }
            return null;
        }

        /// <summary>The crew's bag man, in a unit of his own (GAN-262), while he is on
        /// his feet; null when the crew has no bag man on the street.</summary>
        public Unit BagUnitOf(int crewId)
        {
            for (int i = 0; i < Units.Count; i++)
            {
                var unit = Units[i];
                if (unit == null || unit.Faction != 0 || !unit.IsDetachment) continue;
                if (unit.CrewId == crewId) return unit.Wiped ? null : unit;
            }
            return null;
        }

        /// <summary>Send the selected lieutenant toward a world point over open ground.
        /// Returns where he will stand, or false when nothing is selected.
        ///
        /// <paramref name="run"/> is the player asking for it twice (CrewOverlay's
        /// double right click) and is the ONLY thing that puts a crew into a run over
        /// an order. Left off, and off is the default, the crew walks - which is what
        /// it did before there was a run at all.</summary>
        public bool OrderSelected(Vector3 world, out Vector3 destination, bool run = false)
            => OrderUnit(Selected, world, out destination, run);

        /// <summary>The selected-order path for an explicit unit. TurfMap can gather more
        /// than one crew, but every one must still get the street's exact move semantics:
        /// finish boarding, drive when already in a car, or walk across open ground.</summary>
        public bool OrderUnit(Unit unit, Vector3 world, out Vector3 destination, bool run = false)
            => OrderUnit(unit, world, out destination, run, speak: true);

        /// <summary>
        /// The same order, with the crew's answer switched off.
        ///
        /// A move is the one order the game gives itself: the collection round walks its
        /// own doors, a filed job marches the men to the address, the bag detail goes home
        /// (CrewJobs, BagCarry). Those are not orders the player gave this second, and a
        /// lieutenant announcing "on our way" to nobody, all day, would make the street
        /// unlistenable. So the voice hangs off THIS method - one place, every caller,
        /// street card and paper map alike - and the automatic callers pass speak: false.
        /// </summary>
        public bool OrderUnit(Unit unit, Vector3 world, out Vector3 destination, bool run,
            bool speak)
        {
            var ordered = MoveUnit(unit, world, out destination, run);
            if (ordered && speak)
                CrewSpeech.Say(unit, run ? LivingCity.Data.VoiceLines.OrdRun
                                         : LivingCity.Data.VoiceLines.OrdMove);
            return ordered;
        }

        bool MoveUnit(Unit unit, Vector3 world, out Vector3 destination, bool run)
        {
            destination = world;
            OrderRefusal = null;
            if (unit == null || unit.Boss == null || unit.Boss.Dead) return false;
            if (CustodyRefuses(unit)) { OrderRefusal = InCustodyRefusal; return false; }
            if (unit.Surrendered) { OrderRefusal = HandsUpRefusal; return false; }
            // an ordinary move order ends a run: the player has told them to go
            // somewhere, which is not the same as telling them to get away
            unit.Fleeing = false;
            CallOffRaids(unit, "a move order");
            NoteRetask(unit);
            unit.TargetUnit = null;
            unit.OrderedAt = Time.time;

            // half in the car - the first men in their seats, the rest still walking to
            // their doors: the drive waits for them. The order is kept and the car goes
            // the moment the last man is in (TickCars).
            if (unit.Boarding != null && unit.Car == unit.Boarding && StillBoarding(unit))
            {
                world.y = unit.Car.RoadY;
                unit.PendingDrive = world;
                destination = world;
                return true;
            }
            Unboard(unit, "a walk order"); // a walk order cancels a walk to the car
            unit.PendingDrive = null;

            // in the car: the car goes there, the crew with it - unless the man at the
            // wheel is dead, when the order is to get out
            if (unit.Car != null)
            {
                if (DriverDead(unit.Car))
                {
                    Disembark(unit);
                    destination = unit.Car.Position;
                    return true;
                }
                world.y = unit.Car.RoadY;
                unit.Leaving = false;
                unit.Car.DriveTo(world);
                destination = world;
                return true;
            }

            world = WalkObstacles.ClampToCity(world);
            if (!WalkObstacles.TryClearStandingSpot(
                    world, WalkObstacles.Radius, unit.Boss.Tf.position,
                    out world, 30f)) return false;
            world.y = GroundY;
            if (!DispatchAcross(unit, unit.Boss, world, run, keepOffRoad: false))
                return false;
            destination = world;
            return true;
        }

        /// <summary>March a crew on foot over physical ground. Static obstacles shape
        /// the route through WalkRoute; <paramref name="keepOffRoad"/> optionally makes
        /// that direct route prefer crossings over walking along the carriageway.</summary>
        public bool MarchTo(Unit unit, Vector3 world, bool run = false,
            bool keepOffRoad = false, bool allowCustody = false)
        {
            if (unit == null) return false;
            if (!allowCustody && CustodyRefuses(unit))
            {
                OrderRefusal = InCustodyRefusal;
                return false;
            }
            // A CREW WHOSE LIEUTENANT IS DOWN IS STILL A CREW. His hoods are on their
            // feet and they can still be sent - somebody at the front picks up the walk.
            // Refusing the order because the man who used to give it is dead left three
            // hoods standing in the street for the rest of the run.
            // Out of the building first, if that is where they are: a march is the one
            // order every system gives (the book's jobs, the collection round, the paper
            // map), and it has to find men on the pavement (CrewQuarters).
            CrewQuarters.Retasked(unit);
            var boss = unit.Boss != null && !unit.Boss.Dead ? unit.Boss : Standing(unit);
            if (boss == null || boss.Tf == null) return false;
            CallOffRaids(unit, "a march order");
            unit.TargetUnit = null;
            unit.OrderedAt = Time.time;
            Unboard(unit, "a march order");
            unit.PendingDrive = null;
            world = WalkObstacles.ClampToCity(world);
            // THE SPOT MUST BE A SPOT A MAN CAN STAND ON. The hoods' places have always
            // gone through ClearSpot; the leader's never did, so an order aimed at a
            // doorway, a wall or a parked car gave the man who leads the crew nowhere to
            // put his feet - and a crew whose leader cannot move homes back to where it
            // came from while the order that sent it reports success.
            if (!WalkObstacles.TryClearStandingSpot(
                    world, WalkObstacles.Radius, boss.Tf.position,
                    out world, 30f)) return false;
            world.y = GroundY;

            return DispatchAcross(unit, boss, world, run, keepOffRoad);
        }

        /// <summary>Move a whole crew across physical ground instead of the pedestrian
        /// graph. Static obstacles shape the planned route; traffic is avoided live by
        /// each walker.</summary>
        bool DispatchAcross(Unit unit, CrewWalker boss, Vector3 world, bool run, bool keepOffRoad)
        {
            Unwedge(boss);
            var dir = world - boss.Tf.position;
            dir.y = 0f;
            // ONE CREW, ONE WAY. Independent A* searches can choose opposite sides of
            // the same block, which makes men given one order set off in unrelated
            // directions. Plan the leader's corridor once; each member copies its
            // corners and replaces only the final marker with his formation slot.
            bool hasSharedWay = WalkRoute.Plan(
                boss.Tf.position, world, _dispatchRoute, keepOffRoad);
            CopyMemberCorridor(hasSharedWay ? _dispatchRoute : null,
                _dispatchMemberRoute);
            if (hasSharedWay && _dispatchRoute.Count > 1)
            {
                var arrival = _dispatchRoute[_dispatchRoute.Count - 1] -
                              _dispatchRoute[_dispatchRoute.Count - 2];
                arrival.y = 0f;
                if (arrival.sqrMagnitude > 0.25f) dir = arrival;
            }
            var rot = Quaternion.LookRotation(
                dir.sqrMagnitude > 0.25f ? dir.normalized : boss.Tf.forward);

            // WHO STANDS WHERE IS DECIDED BY WHO IS NEAREST IT, not by the order the
            // hoods happen to sit in the list. Handing a man the place his index names
            // sends two of them across each other behind the lieutenant to swap ends -
            // from outside that reads as a crew setting off in the wrong direction and
            // then turning round, which is exactly what it is.
            _dispatchMen.Clear();
            _dispatchSpots.Clear();
            for (int k = 0; k < unit.Hoods.Count; k++)
            {
                var man = unit.Hoods[k];
                if (man == null || man.Dead || man == boss || man.Riding) continue;
                Unwedge(man);
                _dispatchMen.Add(man);
                // spread behind him, so three men arrive as a crew and not as a column
                var wanted = world + rot * FormationOffset(unit.CrewId, k);
                if (!WalkObstacles.TryConnectedStandingSpot(
                        wanted, world, WalkObstacles.Radius, out var spot, 6f))
                {
                    // Keep the fallback on the leader's connected side and compact the
                    // wedge; never ring-search a slot through the wall to its far side.
                    var compact = world + (wanted - world) * 0.5f;
                    if (!WalkObstacles.TryConnectedStandingSpot(
                            compact, world, WalkObstacles.Radius, out spot, 4f))
                        spot = world;
                }
                _dispatchSpots.Add(spot);
            }

            _dispatchAssignedMen.Clear();
            _dispatchAssignedSpots.Clear();
            while (_dispatchMen.Count > 0)
            {
                var bestMan = 0;
                var bestSpot = 0;
                var best = float.MaxValue;
                for (int m = 0; m < _dispatchMen.Count; m++)
                for (int spot = 0; spot < _dispatchSpots.Count; spot++)
                {
                    var gap = _dispatchSpots[spot] - _dispatchMen[m].Tf.position;
                    gap.y = 0f;
                    var square = gap.sqrMagnitude;
                    if (square >= best) continue;
                    best = square;
                    bestMan = m;
                    bestSpot = spot;
                }

                _dispatchAssignedMen.Add(_dispatchMen[bestMan]);
                _dispatchAssignedSpots.Add(_dispatchSpots[bestSpot]);
                _dispatchMen.RemoveAt(bestMan);
                _dispatchSpots.RemoveAt(bestSpot);
            }

            // ALL OR NONE. Validate every connector before changing anybody's order.
            // Previously the boss and early hoods were already walking when a later
            // hood discovered that its side of the formation was unreachable.
            var bossWay = hasSharedWay ? _dispatchRoute : null;
            var memberWay = hasSharedWay ? _dispatchMemberRoute : null;
            if (!boss.CanOrderAcrossVia(world, bossWay, keepOffRoad)) return false;
            for (int i = 0; i < _dispatchAssignedMen.Count; i++)
            {
                var taking = _dispatchAssignedMen[i];
                var spot = _dispatchAssignedSpots[i];
                if (!taking.CanOrderAcrossVia(spot, memberWay, keepOffRoad))
                {
                    // A locally impossible slot may compact to the leader's proved
                    // destination, but it may not make only this hood choose a private
                    // route around the other side of the block.
                    if (!taking.CanOrderAcrossVia(world, memberWay, keepOffRoad))
                        return false;
                    _dispatchAssignedSpots[i] = world;
                }
            }

            bool bossAccepted = hasSharedWay
                ? boss.OrderAcrossVia(world, _dispatchRoute, keepOffRoad: keepOffRoad)
                : boss.OrderAcross(world, keepOffRoad: keepOffRoad);
            if (!bossAccepted) return false; // same-frame commit of a proved route
            boss.Urgent = run;
            boss.Post = world;

            bool allAccepted = true;
            for (int i = 0; i < _dispatchAssignedMen.Count; i++)
            {
                var taking = _dispatchAssignedMen[i];
                bool accepted = hasSharedWay
                    ? taking.OrderAcrossVia(_dispatchAssignedSpots[i],
                        _dispatchMemberRoute, 0f, keepOffRoad)
                    : taking.OrderAcross(_dispatchAssignedSpots[i], 0f, keepOffRoad);
                if (accepted) taking.Urgent = run;
                else allAccepted = false;
            }
            return allAccepted;
        }

        /// <summary>Scratch for the formation hand-out: the men still without a place and
        /// the places still unclaimed. Fields rather than locals so an order costs no
        /// allocation, the way every other per-order list here is kept.</summary>
        readonly List<CrewWalker> _dispatchMen = new List<CrewWalker>();
        readonly List<Vector3> _dispatchSpots = new List<Vector3>();
        readonly List<Vector3> _dispatchRoute = new List<Vector3>();
        readonly List<Vector3> _dispatchMemberRoute = new List<Vector3>();
        readonly List<CrewWalker> _dispatchAssignedMen = new List<CrewWalker>();
        readonly List<Vector3> _dispatchAssignedSpots = new List<Vector3>();

        /// <summary>The exact last point belongs to the lieutenant. Hoods retain every
        /// obstacle-clearing interior corner and append their own formation slot.
        /// Keeping both endpoints made every hood overshoot the boss by up to four
        /// metres and turn back at the end of even a short order.</summary>
        internal static void CopyMemberCorridor(IReadOnlyList<Vector3> leaderWay,
            List<Vector3> into)
        {
            into.Clear();
            int count = leaderWay != null ? leaderWay.Count : 0;
            for (int i = 0; i + 1 < count; i++) into.Add(leaderWay[i]);
        }

        /// <summary>The fallback for a man who is standing INSIDE something - a wall, a
        /// doorway, a car that parked on him. He cannot take a step from there, so every
        /// order given to him is accepted and then quietly ignored, and the crew that
        /// waits on him goes nowhere either. Lift him onto the nearest ground he can
        /// stand on before he is sent anywhere.
        ///
        /// A fallback, not a habit: a man on clear ground is never touched, and recovery
        /// is deliberately local. If streamed geometry encloses him by more than a small
        /// step, fail closed instead of teleporting him through a cafe or a building.</summary>
        static bool Unwedge(CrewWalker man)
        {
            const float MaxRecoveryStep = 2.5f;
            if (man == null || man.Tf == null || man.Dead) return false;
            var at = man.Tf.position;
            if (!WalkObstacles.Occupied(at, WalkObstacles.Radius)) return false;

            if (!WalkObstacles.TryClearSpot(
                    at, WalkObstacles.Radius, out var free, MaxRecoveryStep)) return false;
            if ((free - at).sqrMagnitude < 0.0001f) return false;
            free.y = at.y;
            man.Tf.position = free;
            // he was PUT there, not walked there. A man standing at a flank has his
            // shoulders against the thing he is behind (EPIC 28), so an order given to
            // him lifts him clear by a stride or so - which the audit reads as a snap
            // unless it is told (CrewAudit.teleport).
            man.NoteRelocated();
            return true;
        }

        /// <summary>A direct order to one of the outfit's crews countermands its
        /// territory errand - the doorstep walk, the collection round. MarchTo stays out
        /// of this on purpose: the round system marches crews leg by leg through it, and
        /// a round that cancelled itself walking its own route banked nothing ever.</summary>
        void NoteRetask(Unit unit)
        {
            if (unit == null || unit.Faction != 0)
                return;
            TerritoryRuntime.Instance?.CallOffErrands(unit.CrewId);
            // And a crew sitting in one of our own buildings comes out for it: an order
            // given to men who are indoors has to reach men who can walk.
            CrewQuarters.Retasked(unit);
        }

        /// <summary>The first man of this crew still on his feet - who leads it when the
        /// lieutenant is down.</summary>
        static CrewWalker Standing(Unit unit)
        {
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Tf != null) return man;
            return null;
        }

        /// <summary>Send the selected crew at that one: every man closes and shoots.</summary>
        public bool OrderAttack(Unit target)
        {
            var crew = Selected;
            var ordered = AttackOrder(target);
            if (ordered)
                CrewSpeech.Say(crew, LivingCity.Data.VoiceLines.OrdKill);
            return ordered;
        }

        bool AttackOrder(Unit target)
        {
            OrderRefusal = null;
            if (Selected == null || target == null || target == Selected || target.Wiped) return false;
            if (CustodyRefuses(Selected)) { OrderRefusal = InCustodyRefusal; return false; }
            if (Selected.Surrendered) { OrderRefusal = HandsUpRefusal; return false; }

            // CONF-003: an attack on the LAW is also the player's answer to an arrest.
            // Stamped here rather than read out of the dispatcher, so the arrest keeps
            // its own counsel and nothing in the crews has to know it exists.
            if (target.IsPolice) Selected.PoliceFightOrderedAt = Time.time;

            // half in the car - the first men in their seats, the rest still walking to
            // their doors: the job waits for them, exactly as a drive order does. The
            // order is kept and the drive-by goes the moment the last man is in
            // (TickCars). The fight itself is NOT set here: a crew with a target breaks
            // off what it is doing and shoots, and what it is doing is getting into a car.
            if (Selected.Boarding != null && Selected.Car == Selected.Boarding && StillBoarding(Selected))
            {
                Selected.PendingAttack = target;
                Selected.PendingDrive = null;
                Selected.OrderedAt = Time.time;
                return true;
            }

            Unboard(Selected, "an attack order");
            // in the car: a drive-by - passes down the street past them, guns out the windows
            // (no driver: out, and at them on foot)
            if (Selected.Car != null)
            {
                if (DriverDead(Selected.Car))
                {
                    Disembark(Selected);
                    Selected.TargetUnit = target;
                    Selected.OrderedFight = true;
                    Selected.SawEnemyAt = Time.time;
                    return true;
                }
                Selected.TargetUnit = target;
                Selected.OrderedFight = true;
                Selected.SawEnemyAt = Time.time;
                Selected.Leaving = false;
                Selected.Car.DriveBy(target);
                return true;
            }
            SetTarget(Selected, target, ordered: true);
            return true;
        }

        /// <summary>
        /// RUN (GAN-222, FLEE-001). The crew breaks off whatever it is doing and gets
        /// away from <paramref name="from"/> - the law, normally - on its feet or in its
        /// car, at a run.
        ///
        /// Running is a sanctioned run case (the crews' own rule: a crew runs on a
        /// double right click, when it is closing, or when it is fleeing), and it is the
        /// player's alternative to watching his lieutenant taken. What it costs is on the
        /// other side of it: a man who ran is a marked man (WantedLevels), and the only
        /// cure is time spent off the street.
        /// </summary>
        public bool OrderFlee(Unit unit, Vector3 from)
        {
            OrderRefusal = null;
            if (unit == null || unit.Wiped) return false;
            if (CustodyRefuses(unit)) { OrderRefusal = InCustodyRefusal; return false; }
            if (unit.Surrendered) { OrderRefusal = HandsUpRefusal; return false; }

            var away = unit.Position - from;
            away.y = 0f;
            away = away.sqrMagnitude > 1f ? away.normalized : Vector3.forward;
            var run = unit.Position + away * FleeDistance;

            // a man running is a man with his gun down (CrewWalker's own rule); the
            // disengage is what takes the fight off him, and OrderUnit does the rest
            foreach (var man in unit.All())
                if (man != null && !man.Dead) man.Disengage();

            // the walk order FIRST: an ordinary move ends a flight (below), so the flags
            // are set on the far side of it or they would be cleared by the very order
            // that starts the run
            if (!OrderUnit(unit, run, out _, run: true, speak: false)) return false;
            CrewSpeech.Say(unit, LivingCity.Data.VoiceLines.OrdFlee);
            unit.Fleeing = true;
            unit.FledAt = Time.time;
            unit.SeenByLawAt = Time.time;
            unit.FlightFrom = unit.Position;
            CrewOverlay.Announce(unit.GangName.ToUpperInvariant() + " ARE RUNNING FOR IT",
                4f, new Color(0.95f, 0.9f, 0.6f));
            return true;
        }

        /// <summary>How far a break-off run is laid, in metres. Far enough to leave the
        /// street the trouble is on rather than to cross the town: what actually ends
        /// the run is the pursuit being broken, not the distance.</summary>
        const float FleeDistance = 70f;

        /// <summary>The run is over - they went inside, they were caught, or the player
        /// gave them something else to do.</summary>
        public void EndFlight(Unit unit)
        {
            if (unit != null) unit.Fleeing = false;
        }

        /// <summary>Why the crew will not shoot up that car, for the row that offers
        /// it. Null when it will.</summary>
        public string ShootCarRefusal { get; private set; }

        /// <summary>Can this crew shoot a car up? A man of it up, armed, and on his
        /// feet - and something left of the car to shoot at.</summary>
        public bool CanShootCar(Unit unit, RoadCar car)
        {
            if (car == null || car.Tf == null || car.Wrecked)
            { ShootCarRefusal = "Nothing left of it to shoot"; return false; }
            if (car is not CrewCar)
            { ShootCarRefusal = "Not a machine the crew can be put on"; return false; }
            if (unit == null || unit.Wiped)
            { ShootCarRefusal = "Nobody to give it to"; return false; }
            if (CustodyRefuses(unit))
            { ShootCarRefusal = InCustodyRefusal; return false; }
            foreach (var man in unit.All())
                if (!man.Dead && man.Carrying && !man.Riding && !IsAboard(man))
                { ShootCarRefusal = null; return true; }
            ShootCarRefusal = "Nobody of the crew is up and armed";
            return false;
        }

        /// <summary>Put the whole crew on a car: they walk up to it and empty their guns
        /// into it. Not an attack on the men who own it - the machine is the mark, and a
        /// crew given this stands there shooting tin whether anybody is sat in it or not.
        ///
        /// The crew's own fight is dropped: a man cannot be on a rival and on a car at
        /// once, and the car is what he was just told to do.</summary>
        public bool OrderShootCar(RoadCar car)
        {
            if (!CanShootCar(Selected, car)) return false;
            var machine = (CrewCar)car;
            Unboard(Selected, "a car to shoot up");
            Selected.TargetUnit = null;
            Selected.OrderedFight = false;
            foreach (var man in Selected.All())
                if (!man.Dead && !IsAboard(man) && !man.Riding) man.ShootUp(machine);

            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", Selected.GangName);
                DriveTrace.Str(sb, "car", machine.DisplayName);
                DriveTrace.Int(sb, "hits", machine.EngineHits);
                DriveTrace.Row("shootup", sb.ToString());
            }
            CrewSpeech.Say(Selected, LivingCity.Data.VoiceLines.OrdShootCar);
            return true;
        }

        void Dispatch(Unit unit, PedLink link, float t, bool run = false) =>
            Dispatch(unit, unit.Boss, link, t, run);

        /// <summary>The same walk, led by a named man. A crew whose lieutenant is down is
        /// still a crew and can still be sent (MarchTo says so), so the man at the front
        /// is passed in rather than assumed to be the lieutenant.</summary>
        void Dispatch(Unit unit, CrewWalker lead, PedLink link, float t, bool run = false)
        {
            // a man who walked off his stretch (to a car door he never got into) sets
            // off from where he stands
            if (lead != null && !lead.Dead && !lead.Riding)
            {
                Reseat(lead);
                lead.OrderTo(link, t);
                // THIS is how a player sends a crew anywhere in this town, and it is a
                // walk down the pavements on purpose - the men keep their lanes and
                // their formation. It stays a walk unless he asked twice, and then it
                // is run most of the way and walked the last few metres in
                // (CrewWalker.GearGraphWalk) - the sidewalks can run, but only when
                // told to.
                lead.Urgent = run;
            }
            for (int k = 0; k < unit.Hoods.Count; k++)
            {
                // the two away on the machine keep their own order (the raid's); a walk
                // given to the crew is given to the crew that is standing in the street
                var man = unit.Hoods[k];
                if (man == null || man == lead || man.Dead || man.Riding) continue;
                Reseat(man);
                // No beat: when the man in front steps off, they step off WITH him.
                man.OrderTo(link, FormationT(link, t, unit.CrewId, k));
                man.Urgent = run;
            }
        }

        /// <summary>
        /// A crew steps off TOGETHER. There used to be a beat here - up to nine tenths
        /// of a second, each man his own - so that a crew read as men rather than as one
        /// machine. From outside it read as the lieutenant walking away from hoods who
        /// had not been told, and it was removed on the user's word (2026-09-01).
        /// </summary>
        const float HoodBeatSeconds = 0f;

        /// <summary>A man's own nudge off the formation's lattice, in -1..1, settled
        /// for the run: crew, his place in it, and which of his nudges is asked for.
        ///
        /// Every formation below is a LATTICE - rank by rank, left and right by turns,
        /// the same steps for everybody - and five men stood on one lattice read as a
        /// diagram rather than as men ("stoje ovako u trouglu nekom glupom"). Each man
        /// is pushed off his lattice point by his own fixed amount instead, small
        /// enough that he is still where the formation put him and nowhere near
        /// anybody's shoulder (the tightest pair in any of them keeps 0.9 m, two
        /// walking radii).
        ///
        /// A HASH, not a draw. The arena shares one UnityEngine.Random stream, so a
        /// draw taken here would relay every seed in the lab - the trap behind the
        /// spawn-clearance and prop-bag work. Same crew, same man, same spot on every
        /// run, and no walker anywhere else moves an inch because of it.</summary>
        static float Scatter(int crew, int k, int salt)
        {
            unchecked
            {
                uint h = (uint)(crew * 73856093) ^ (uint)((k + 1) * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 16; h *= 2246822519u;
                h ^= h >> 13; h *= 3266489917u;
                h ^= h >> 16;
                return (h & 0xFFFFu) / 32767.5f - 1f;
            }
        }

        /// <summary>Metres a crew's own men keep between them when they are stood
        /// about rather than marching - room enough that nobody clips a shoulder,
        /// loose enough that five men still fit round one lieutenant.</summary>
        const float StandGap = 1.25f;

        /// <summary>Hood k's spot round his lieutenant on open ground, in the
        /// lieutenant's frame.
        ///
        /// Every man is dealt his own candidates - his angle round the boss, his
        /// distance from him - and takes the first that leaves everybody already
        /// stood there their StandGap. Men behind and beside him, never in front of
        /// his gun, and if twelve candidates all crowd somebody (five men on one
        /// small arc) he falls back on the old wedge point, which is always clear.
        ///
        /// The wedge WAS the whole answer, and a wedge is a lattice: one step aside
        /// and one step back per rank, the same for every man of every crew, which
        /// is a diagram rather than a gang standing about ("stoje ovako u trouglu
        /// nekom glupom"). Note the loop deals every man before him too - a spot is
        /// only clear with respect to the men already stood - so this is O(k) work
        /// on a k of five, asked when a crew is placed or re-formed.</summary>
        static Vector3 FormationOffset(int crew, int k)
        {
            System.Span<Vector3> taken = stackalloc Vector3[Mathf.Min(k, 15) + 2];
            taken[0] = Vector3.zero;                     // the lieutenant's own feet
            int n = 1;
            Vector3 spot = Wedge(k);
            for (int j = 0; j <= k && j < 16; j++)
            {
                float best = -1f;
                bool room = false;
                for (int a = 0; a < 12 && !room; a++)
                {
                    // his own arc behind the boss (a man in front of the guns is a
                    // fault of its own) and his own distance out
                    float ang = Scatter(crew, j, 10 + a) * 130f * Mathf.Deg2Rad;
                    float r = 1.4f + (Scatter(crew, j, 40 + a) + 1f) * 0.5f * 2.5f;
                    var p = new Vector3(Mathf.Sin(ang) * r, 0f, -Mathf.Cos(ang) * r);
                    float g = Gap(taken, n, p);
                    room = g >= StandGap;
                    if (room || g > best) { spot = p; best = g; }
                }
                // nothing of his own was roomy enough: the lattice point if IT is
                // roomier, his best candidate if it is not
                if (!room)
                {
                    var fallback = Wedge(j);
                    if (Gap(taken, n, fallback) > best) spot = fallback;
                }
                if (n < taken.Length) taken[n++] = spot;
            }
            return spot;
        }

        /// <summary>The old lattice: one step aside and one step back per rank, left
        /// and right by turns. Nothing stands on it now unless the scatter above can
        /// find no room, but it is always clear, which is what a fallback is for.</summary>
        static Vector3 Wedge(int k)
        {
            int rank = k / 2 + 1;
            float side = k % 2 == 0 ? -1f : 1f;
            return new Vector3(side * 1.6f * rank, 0f, -1.5f * rank);
        }

        /// <summary>How near the closest man already stood there is - the room a
        /// candidate spot would leave. The whole crew is dealt against this: the first
        /// candidate with StandGap to spare is taken, and if a man's twelve all crowd
        /// somebody (five men on one small arc, or a fallback lattice point somebody
        /// has already scattered onto) he takes the roomiest of them rather than
        /// standing in a shoulder.</summary>
        static float Gap(System.Span<Vector3> taken, int n, Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var d = p - taken[i];
                best = Mathf.Min(best, d.x * d.x + d.z * d.z);
            }
            return n == 0 ? 99f : Mathf.Sqrt(best);
        }

        /// <summary>Hood k's spot on a pavement, in the lieutenant's frame: metres
        /// along the walk from him (x, forward along the link) and his lane across it
        /// (y, what HoldLane holds). Dealt exactly like the open-ground spot - his own
        /// candidates, the first with shoulder room - because a crew stood on a kerb
        /// read as the same diagram: a man a stride behind, a man a stride in front,
        /// on and on, alternating outward. The lane is inside HoldLane's own +-1.6 m
        /// clamp, so a man is never dealt a lane the walk cannot hold.</summary>
        static Vector2 SidewalkSlot(int crew, int k)
        {
            System.Span<Vector3> taken = stackalloc Vector3[Mathf.Min(k, 15) + 2];
            taken[0] = Vector3.zero;
            int n = 1;
            Vector3 spot = Stagger(k);
            for (int j = 0; j <= k && j < 16; j++)
            {
                float best = -1f;
                bool room = false;
                for (int a = 0; a < 16 && !room; a++)
                {
                    var p = new Vector3(Scatter(crew, j, 70 + a) * 3.6f, 0f,
                                        Scatter(crew, j, 100 + a) * 1.45f);
                    float g = Gap(taken, n, p);
                    room = g >= StandGap;
                    if (room || g > best) { spot = p; best = g; }
                }
                if (!room)
                {
                    var fallback = Stagger(j);
                    if (Gap(taken, n, fallback) > best) spot = fallback;
                }
                if (n < taken.Length) taken[n++] = spot;
            }
            return new Vector2(spot.x, spot.z);
        }

        /// <summary>The old pavement lattice - a rank behind, a rank in front - kept
        /// as SidewalkSlot's fallback for the same reason as the wedge.</summary>
        static Vector3 Stagger(int k)
        {
            int rank = k / 2 + 1;
            float side = k % 2 == 0 ? -1f : 1f;
            return new Vector3(side * rank * Spacing, 0f,
                               Mathf.Clamp(side * (0.5f + 0.4f * rank), -1.45f, 1.45f));
        }

        /// <summary>Hood k's mark along the link the crew is stood on or walking.</summary>
        static float FormationT(PedLink link, float bossT, int crew, int k)
        {
            return Mathf.Clamp(bossT + SidewalkSlot(crew, k).x, 0.4f, link.Length - 0.4f);
        }

        /// <summary>Hood k's spot along a kerb, in the lieutenant's frame: beside him,
        /// left and right by turns, half a step back - a line, not a wedge.</summary>
        static Vector3 LineOffset(int crew, int k)
        {
            int rank = k / 2 + 1;
            float side = k % 2 == 0 ? -1f : 1f;
            return new Vector3(side * 1.7f * rank + Scatter(crew, k, 4) * 0.4f,
                               0f,
                               -0.6f * rank + Scatter(crew, k, 5) * 0.4f);
        }

        /// <summary>Hood k's LINE across the pavement while the crew walks the graph.
        /// Every walker wants a lane of his own and the crowd's default deals everybody
        /// much the same one (keep right) - so a crew sent down a street threaded it in
        /// single file: five men, one queue, no gang about it. Its men flank across the
        /// walk instead - his own line, dealt with his mark along the walk so the two
        /// agree (SidewalkSlot) - and the same street is walked abreast.</summary>
        static float FormationLane(int crew, int k) => SidewalkSlot(crew, k).y;

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

        // ------------------------------------------------------------------ the tether
        //
        // A LIEUTENANT'S CREW MOVES AS ONE. His men do not stroll off on their own
        // (RoamsAlone is off for a hood - only the boss gets bored, and his walk is a
        // short one), and nobody trails: a man left standing away from his boss walks
        // back to his place in the wedge, a man strung out behind a march gets quicker
        // feet, and a boss whose crew has fallen far behind stands a beat for it. None
        // of it touches a fight, a car being boarded, or the police, who take their
        // orders from their own dispatcher.

        float _cohesionScan = 2f;
        const float TetherNear = 7f;    // farther than this from the boss, a standing man falls in
        const float TetherFar = 14f;    // this far, he hurries
        const float TetherWait = 16f;   // a boss this far ahead of his crew waits a beat

        // Men the unstuck pass has just sent stepping out of something: the tether
        // leaves them alone this scan, or the step out is cancelled before a foot
        // moves and the man spends the run being "unstuck" once a second (185 rows
        // of it in the run that found this) while never actually getting out.
        readonly HashSet<CrewWalker> _unsticking = new HashSet<CrewWalker>();

        void TickCohesion()
        {
            _unsticking.Clear();
            foreach (var unit in Units)
            {
                // a man left standing INSIDE something - a car parked onto the spot he
                // held, a body shoved into a bin - steps calmly out of it (a stride's
                // order: Steer lets a man inside a thing walk straight out). Everyone,
                // the law included; a man down behind cover is Engaging, not Standing,
                // and is left to his fight.
                foreach (var man in unit.All())
                {
                    if (man.Dead || man.Tf == null || IsAboard(man) || man.Riding) continue;
                    if (OnRaid(man) || Chasing(man)) continue;   // the raid's man, and the chaser, are their own business
                    // AND THE MAN IN A DOORWAY. He is walking THROUGH a shopfront
                    // on the beat's own order (DoorBeat), so of course the ground
                    // under him reads as occupied - stepping him back out of it is
                    // stepping him out of the shop he was sent into.
                    if (DoorBeat.Active(man)) continue;
                    if (man.State != CrewWalker.Mode.Standing) continue;
                    // AND THE MAN LYING IN WAIT. The comment above says a man down
                    // behind cover is Engaging and is left to his fight - an ambusher is
                    // that same man, crouched behind that same bin, and he reads as
                    // STANDING because he has no fight yet (EPIC 28). A flank is a spot
                    // a shoulder's width off a box, and the last stride onto it lands
                    // inside the walk's own arrival tolerance, so this pass would step
                    // him off the thing the player put him behind - a metre and a half
                    // in one frame, which the audit rightly calls a teleport.
                    if (man.Lurking) continue;
                    // INSIDE SOMETHING, and only that. The asphalt used to count as a
                    // fault too, and it is what tore a crew apart: told to stand in the
                    // road, the men near enough a kerb were walked off it one by one by
                    // this pass, and the tether - measuring them against a lieutenant who
                    // was still out in the traffic - walked them straight back. Two of
                    // them settled at 7.0 m, a hair off TetherNear, and stepped over the
                    // line and back for the rest of the run: the pacing the player
                    // watched. WHERE A CREW STANDS IS THE PLAYER'S BUSINESS - the road,
                    // a junction, the middle of a square - and the men stand round their
                    // lieutenant wherever he is. The one thing that is nobody's order is
                    // a man with his shoulders in a bin, and he still steps out of it.
                    if (!WalkObstacles.Occupied(man.Tf.position, WalkObstacles.Radius)) continue;
                    var from = man.Tf.position;
                    if (!Unwedge(man)) continue;
                    var free = man.Tf.position;
                    if (DriveTrace.On)
                    {
                        var sb = DriveTrace.Take();
                        DriveTrace.Str(sb, "who", man.DisplayName);
                        DriveTrace.Num(sb, "moved", Vector3.Distance(free, from));
                        DriveTrace.Vec(sb, "from", from);
                        DriveTrace.Row("unstuck", sb.ToString());
                    }
                    _unsticking.Add(man);
                }

                if (unit.Wiped || unit.IsPolice || unit.TargetUnit != null || unit.Boarding != null) continue;
                var lead = unit.Boss != null && !unit.Boss.Dead ? unit.Boss : Standing(unit);
                if (lead == null || lead.Tf == null || IsAboard(lead) || lead.Riding || lead.Panicked) continue;
                // AND NOT WHILE THE MAN AT THE FRONT IS IN A DOORWAY. His body is being
                // walked through a shopfront, or switched off inside one (DoorBeat,
                // CrewQuarters): a tether measured against it hauls his whole crew into
                // the wall after him. The posted doorman had this exemption already
                // (man.Watching below); the men filing in behind their lieutenant need
                // the same one.
                if (DoorBeat.Active(lead)) continue;
                var leadAnchor = lead.HasOrder ? lead.OrderDestination : lead.Tf.position;
                float worst = 0f;
                for (int k = 0; k < unit.Hoods.Count; k++)
                {
                    var man = unit.Hoods[k];
                    if (man == null || man == lead || man.Dead || man.Tf == null) continue;
                    if (IsAboard(man) || man.Riding || man.Panicked || man.Target != null) continue;
                    man.SetPace(1f);   // yesterday's dawdle does not outlive its reason
                    if (OnRaid(man)) continue;   // walking to the machine, or home from it: the raid drives him
                    if (DoorBeat.Active(man)) continue;   // in a doorway: the visit drives him
                    // POSTED ON A DOOR, and standing where he was put. The tether reads
                    // a doorman two metres off a shopfront as a straggler the moment his
                    // lieutenant walks INTO it - the boss's body ends up three or four
                    // metres inside the wall - and hauls the whole guard in after him.
                    if (man.Watching) continue;
                    // AND THE MAN LYING IN WAIT. He was put behind that bin on purpose
                    // and the whole order is that he stays there; the tether reading him
                    // as a straggler and walking him back to his lieutenant is the
                    // ambush dismantling itself. The same exemption from the moment he
                    // is dealt the flank, not from the moment he reaches it - he is on
                    // his way to a place of his own, not trailing the crew.
                    if (man.HeldCover.HasValue) continue;
                    if (Chasing(man)) continue;   // running after somebody: the chase drives him
                    if (_unsticking.Contains(man)) continue;   // let him step out of the bin first
                    var gap = man.Tf.position - lead.Tf.position;
                    gap.y = 0f;
                    float d = gap.magnitude;
                    if (d <= TetherNear)
                    {
                        // BACK WITH HIS CREW, so no longer in a hurry. The hurry used
                        // to be cleared only by his next order, which cost nothing
                        // while it was worth a third off his walk - now that it is
                        // what puts him into a run, a man who has caught up would
                        // jog on at his boss's shoulder for the rest of the errand.
                        man.Hustle = false;
                        continue;
                    }

                    // AHEAD of the crew, not behind it. On a long walk every man was
                    // given the same far destination and walked his own race - and a
                    // hood's feet are quicker than the boss's, so over four hundred
                    // metres the crew strung out to ninety metres of pavement with
                    // the LIEUTENANT at the back. The crew's pace is the boss's: a
                    // man who has pulled ahead is slowed (just ahead - never stopped,
                    // a stopped man is a bollard his own boss brakes behind) or stood
                    // (well ahead, by the TRUE gap - out there he blocks nobody)
                    // until the boss draws level; mid-zebra he finishes the crossing
                    // first, and he is never hauled backward. Ahead-ness is measured
                    // ALONG THE BOSS'S OWN HEADING - not by who stands nearer the
                    // destination, because on a dog-leg route those distances
                    // compress until a man twenty metres up the street reads level
                    // with his boss, falls into the laggard branch, and is dealt the
                    // laggard's quicker feet fifty metres out front (seed 105).
                    float along = Vector3.Dot(gap, lead.Tf.forward);
                    if (lead.HasOrder && along > TetherNear)
                    {
                        bool onZebra = man.OnGraph && man.CurrentLink != null && man.CurrentLink.Gated;
                        if (!onZebra && man.HasOrder)
                        {
                            man.Hustle = false;
                            if (d > TetherFar) man.Linger(1.0f);
                            else man.SetPace(0.55f);
                        }
                        continue;
                    }
                    worst = Mathf.Max(worst, d);

                    if (man.State == CrewWalker.Mode.Standing)
                    {
                        Tether(unit, lead, man, k, hustle: d > TetherFar);
                    }
                    else if (man.State == CrewWalker.Mode.Striding && d > TetherFar)
                    {
                        // walking, but not with the crew: haul him back; walking WITH
                        // it, only strung out: quicker feet. A SHORT stride is neither
                        // - it is a step out of something (the unstuck pass, a spot
                        // eased apart), and cancelling it every scan is how a man
                        // spends a run being freed and never free.
                        var step = man.Destination - man.Tf.position;
                        step.y = 0f;
                        if (step.sqrMagnitude < 4f * 4f) continue;
                        // a stride at the crew's destination OR at the lead himself
                        // is the right stride. Measuring only against the destination
                        // condemned every haul-back and every cut-across-the-light -
                        // both aim at the LEAD - as a stray, and re-ordered it each
                        // scan: the man spent the leg being corrected in place.
                        var stray = man.OrderDestination - leadAnchor;
                        stray.y = 0f;
                        var atLead = man.OrderDestination - lead.Tf.position;
                        atLead.y = 0f;
                        if (stray.sqrMagnitude > 12f * 12f && atLead.sqrMagnitude > 12f * 12f)
                            Tether(unit, lead, man, k, hustle: true);
                        else man.Hustle = true;
                    }
                    else if (man.AtLight && man.State == CrewWalker.Mode.Walking)
                    {
                        // held at a red his crew has already crossed away from: a crew
                        // does not split over a light, and the man it left stood alone
                        // on the zebra mouth is the fault the player reported. He cuts
                        // across after them the way a march does (OrderAcross ignores
                        // the signals; the stride still steers round the cars and the
                        // traffic brakes for a body in the road). A crew waiting at the
                        // light TOGETHER stands inside TetherNear and never gets here.
                        if (DriveTrace.On)
                        {
                            var sb = DriveTrace.Take();
                            DriveTrace.Str(sb, "who", man.DisplayName);
                            DriveTrace.Str(sb, "boss", lead.DisplayName);
                            DriveTrace.Num(sb, "gap", d);
                            DriveTrace.Str(sb, "what", "left at a light: crossing after the crew");
                            DriveTrace.Row("tether", sb.ToString());
                        }
                        OrderFallInAcross(unit, lead, man, k, 0f);
                        man.Urgent = lead.Urgent;   // a run is the crew's, not one man's
                        man.Hustle = d > TetherFar;
                    }
                    else if (d > TetherFar &&
                             (man.State == CrewWalker.Mode.Walking || man.State == CrewWalker.Mode.Homing))
                    {
                        // walking, far off. A walk aimed at NEITHER the lead NOR the
                        // lead's own destination is somebody else's errand - a man
                        // back from a panic walking to the spot he fled FROM while
                        // his crew marched on (measured: 63 m out, mid-war) - and he
                        // is hauled to his place, crew at rest or on the move alike.
                        // On the shared errand and merely BEHIND: quicker feet
                        // (Hustle only gears the free stride - the graph walk
                        // hurries through PaceScale, reset every scan).
                        var strayW = man.OrderDestination - lead.Tf.position;
                        strayW.y = 0f;
                        bool foreign = strayW.sqrMagnitude > 12f * 12f;
                        if (foreign && lead.HasOrder)
                        {
                            var errand = man.OrderDestination - leadAnchor;
                            errand.y = 0f;
                            foreign = errand.sqrMagnitude > 12f * 12f;
                        }
                        if (foreign) Tether(unit, lead, man, k, hustle: true);
                        else if (lead.HasOrder && along < -TetherNear)
                            man.SetPace(1.25f);
                    }
                }
                // the boss does not march off over the horizon while his men thread
                // their way round whatever held them up - but he does not stand for a
                // man who is NOT COMING either. The wait is a debt: free while the
                // worst gap improves, paid off after a few fruitless seconds - a
                // hood wedged somewhere the tether cannot reach froze his whole
                // crew's march for 45 s (brawl seed 107) with this unbounded.
                if (worst > TetherWait)
                {
                    bool helping = worst < unit.WorstSeen - 0.5f;
                    if (helping) unit.WorstSeen = worst;
                    unit.LingerDebt = helping ? 0f : unit.LingerDebt + 0.7f;
                    if (unit.LingerDebt < 8f) lead.Linger(0.8f);
                }
                else
                {
                    unit.LingerDebt = 0f;
                    unit.WorstSeen = float.MaxValue;
                }
            }
        }

        /// <summary>Back to his place in the wedge behind the boss - over the free
        /// floor, or along the pavements when the crew walks the graph.</summary>
        void Tether(Unit unit, CrewWalker lead, CrewWalker man, int k, bool hustle)
        {
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", man.DisplayName);
                DriveTrace.Str(sb, "boss", lead.DisplayName);
                DriveTrace.Num(sb, "gap", Vector3.Distance(man.Tf.position, lead.Tf.position));
                DriveTrace.Bool(sb, "hustle", hustle);
                DriveTrace.Row("tether", sb.ToString());
            }
            // no beat on a corrective order: the polite stagger is for a fresh order
            // to a whole crew, and a man re-tethered each scan WITH a beat spends the
            // scan standing his beat out - he falls further behind the more he is
            // helped (7.99 m grew to 16.59 in the run that measured it)
            // The pavement can only close a gap that runs ALONG it. A hood parted
            // from his boss ACROSS the walk - the wedge's depth, the far kerb - gets
            // a graph slot he is already stood on: the order finishes on the spot,
            // the gap stays, and the scan re-orders him forever (a static 7.9 m gap,
            // re-tethered every 0.7 s for a whole run, measured). If the slot would
            // not actually move him, he cuts across the open ground instead.
            bool walked = false;
            bool leadHasGraphSeat = lead.GraphDriven ||
                (lead.State == CrewWalker.Mode.Standing && lead.OnGraph);
            if (!FreeRoam && leadHasGraphSeat && lead.CurrentLink != null &&
                !lead.CurrentLink.Gated)
            {
                // OrderAcross deliberately detached the old graph link. Attach the man
                // where he actually stands before trying to return him to a leader who
                // is genuinely graph-driven; otherwise OnGraph can never become true
                // again and every later scan repeats another cut-across.
                Reseat(man);
                if (man.OnGraph && man.CurrentLink != null)
                {
                    var link = lead.CurrentLink;
                    float t = FormationT(link, lead.CurrentT, unit.CrewId, k);
                    var slot = Vector3.Lerp(link.From.Pos, link.To.Pos,
                        t / Mathf.Max(link.Length, 0.01f));
                    var pull = slot - man.Tf.position;
                    pull.y = 0f;
                    if (pull.sqrMagnitude > 2f * 2f)
                    {
                        man.OrderTo(link, t);
                        walked = true;
                    }
                }
            }
            if (!walked)
                OrderFallInAcross(unit, lead, man, k, 0f);
            else
                man.MarkFallingIn();
            // A RUN BELONGS TO THE CREW. Every order clears the last one's urgency, and
            // this is an order - so a hood hauled back into place while his crew was
            // running dropped to a walk on the spot and never caught it again. He
            // inherits the lead's: running crew, running man; walking crew, walking man.
            man.Urgent = lead.Urgent;
            man.Hustle = hustle;
        }

        readonly List<Vector3> _cohesionRoute = new List<Vector3>();

        /// <summary>Return a separated hood to his own remaining common way. Progress is
        /// per walker: the leader may already have cleared a corner which this hood has
        /// not reached. It never invents a second cross-block route while the member
        /// still owns a shared one.</summary>
        bool OrderFallInAcross(Unit unit, CrewWalker lead, CrewWalker man, int k,
            float beat)
        {
            if (unit == null || lead == null || man == null || lead.Tf == null ||
                man.Tf == null) return false;
            var anchor = lead.HasOrder ? lead.OrderDestination : lead.Tf.position;
            var facing = lead.HasOrder ? anchor - lead.Tf.position : lead.Tf.forward;
            facing.y = 0f;
            var rot = Quaternion.LookRotation(
                facing.sqrMagnitude > 1e-3f ? facing.normalized : lead.Tf.forward);
            var wanted = anchor + rot * FormationOffset(unit.CrewId, k);
            if (!WalkObstacles.TryConnectedStandingSpot(
                    wanted, anchor, WalkObstacles.Radius, out var spot, 6f))
                spot = anchor;

            _cohesionRoute.Clear();
            bool shared = man.CopyRemainingSharedWay(_cohesionRoute);
            bool ordered = shared
                ? man.OrderAcrossVia(spot, _cohesionRoute, beat)
                : man.OrderAcross(spot, beat);
            if (ordered) man.MarkFallingIn();
            return ordered;
        }

        /// <summary>Metres two men keep between them on open ground - shoulder room.</summary>
        const float Elbow = 1.0f;

        readonly List<CrewWalker> _standing = new List<CrewWalker>();

        /// <summary>Whether the elbow pass must treat this body as the passer. Combat
        /// approaches are Mode.Engaging rather than a normal HasOrder state, but their
        /// routed intent is published by TickCombatStride before Separate runs.</summary>
        internal static bool SeparationMoverModel(bool hasOrder, bool routedStrideIntent) =>
            hasOrder || routedStrideIntent;

        /// <summary>Two active strides already see and steer around one another through
        /// the crowd reader. Easing that pair a second time can put an equal reverse
        /// step on the rear man and cancel his route forever. The elbow pass therefore
        /// owns only standing/standing spacing and active/standing right of way.</summary>
        internal static bool SeparationPairNeedsEaseModel(bool aMoving, bool bMoving) =>
            !(aMoving && bMoving);

        static bool SeparationMover(CrewWalker man) =>
            man != null && SeparationMoverModel(
                man.HasOrder, man.TryRoutedStrideIntent(out _));

        // Nobody stands inside anybody else: men who converge on the same spot -
        // a crew closing on one target, hoods falling in on a boss who has stopped
        // - are eased apart, half the overlap each, on the flat. The fallen are left
        // where they lie.
        //
        // THE CITY GETS THIS TOO, and used not to: the pass ran on the empty-floor
        // scenes only, so on the streets a crew that converged on one spot simply
        // stood inside itself. It is kept OFF the graph's walkers, and that is the
        // whole reason it was ever fenced to the lab - a man walking a stretch has
        // his feet rebuilt from metre-plus-lateral every frame, so an elbow written
        // into his transform is undone by the next Move and reads as a shudder. Such
        // a man is already parted from his neighbours by the crowd reader, which is
        // where the parting belongs for anybody the graph is steering.
        /// <summary>Metres a second the ease may part two men at. It is a LEAN, not a
        /// shove: written straight into the transform, it is on top of whatever his own
        /// stride did this frame, and the watchdog calls anything over a metre in one
        /// frame a teleport (CrewAudit.TeleportBound). A man overlapping three others
        /// used to take all three pushes in one pass and could be flung the best part
        /// of two metres - which nobody ever saw only because the pass was fenced to
        /// the empty-floor scenes. The pushes are added up first and the sum is capped,
        /// so a knot of men opens out over a beat instead of exploding.</summary>
        const float EaseSpeed = 0.8f;

        /// <summary>Overlap this small is left alone: shoulder room is not measured to
        /// the centimetre, and chasing the last of it is what makes a crew fidget.</summary>
        const float SettledEnough = 0.12f;

        /// <summary>How far out a man STOOD ABOUT starts opening up for somebody
        /// walking through. Past the crowd reader's own Notice (1.25 m), so he is
        /// moving aside while the walker is still only easing off his pace, instead
        /// of after the walker has already crawled to a stop in front of him.</summary>
        const float PassReach = 1.5f;

        // Every man's feet read ONCE, and the pair scan run over the copies. Transform.
        // position is a native call, and the scan asks for two of them per pair: over
        // the whole city's crews that is tens of thousands of calls a frame for a pass
        // that only ever needed each man's place once.
        readonly List<Vector3> _at = new List<Vector3>();
        readonly List<Vector3> _ease = new List<Vector3>();
        // men the sidewalk graph is steering: they are given way to, never pushed
        readonly List<Vector3> _passers = new List<Vector3>();

        void Separate(float dt)
        {
            _standing.Clear();
            _at.Clear();
            _ease.Clear();
            _passers.Clear();
            foreach (var unit in Units)
                foreach (var man in unit.All())
                {
                    if (man.Dead || !man.Tf || IsAboard(man) || man.Riding) continue;
                    if (man.GraphDriven)
                    {
                        // A MAN ON THE GRAPH IS A REASON TO MOVE, NEVER SOMEBODY TO
                        // MOVE. His feet are rebuilt from metre-plus-lateral every
                        // frame, so a push written into him is undone by the next Move
                        // and reads as a shudder - which is why this pass has always
                        // left him alone. But leaving him OUT ENTIRELY meant a crew
                        // stood on a pavement never opened up for one of its own
                        // walking through it, and he had to grind past on the crowd
                        // reader's brake alone.
                        _passers.Add(man.Tf.position);
                        continue;
                    }
                    _standing.Add(man);
                    _at.Add(man.Tf.position);
                    _ease.Add(Vector3.zero);
                }

            for (int i = 0; i < _standing.Count; i++)
            {
                var a = _at[i];
                bool aOn = SeparationMover(_standing[i]);
                for (int j = i + 1; j < _standing.Count; j++)
                {
                    bool bOn = SeparationMover(_standing[j]);
                    if (!SeparationPairNeedsEaseModel(aOn, bOn)) continue;
                    // ONE OF THEM WALKING IS A DIFFERENT QUESTION. Two men standing
                    // want shoulder room and nothing more. A man walking THROUGH a
                    // knot of men who are stood about needs them to open up before he
                    // is on top of them - and the crowd reader has already begun
                    // braking him at Notice (1.25 m), which is further out than the
                    // elbow ever reached. So he crawled to a halt at a distance where
                    // nobody had yet been asked to move: two of the outfit "jedva
                    // prosla" through a rival crew. The parting reaches past the brake
                    // now, and it is the man STANDING who gives way - shoving the
                    // walker off the line he chose is not giving way, it is a scuffle.
                    bool passing = aOn != bOn;
                    float reach = passing ? PassReach : Elbow;
                    var d = _at[j] - a;
                    d.y = 0f;
                    float d2 = d.sqrMagnitude;
                    if (d2 >= reach * reach) continue;
                    float dist = Mathf.Sqrt(d2);
                    // dead-on top of each other: pick a side rather than divide by zero
                    var dir = dist > 1e-3f ? d / dist
                        : new Vector3(Mathf.Cos(i * 2.4f), 0f, Mathf.Sin(i * 2.4f));
                    float push = reach - dist;
                    if (!passing) { push *= 0.5f; _ease[i] -= dir * push; _ease[j] += dir * push; }
                    else if (aOn) _ease[j] += dir * push;   // b stands: b gives way
                    else _ease[i] -= dir * push;            // a stands: a gives way
                }
            }

            // and the same for anybody the graph is walking through the middle of them
            float pass2 = PassReach * PassReach;
            for (int i = 0; i < _standing.Count; i++)
            {
                if (_standing[i].LegsMoving) continue;    // his own stride parts him
                for (int k = 0; k < _passers.Count; k++)
                {
                    var d = _passers[k] - _at[i];
                    d.y = 0f;
                    float d2 = d.sqrMagnitude;
                    if (d2 >= pass2) continue;
                    float dist = Mathf.Sqrt(d2);
                    var dir = dist > 1e-3f ? d / dist
                        : new Vector3(Mathf.Cos(i * 2.4f), 0f, Mathf.Sin(i * 2.4f));
                    _ease[i] -= dir * (PassReach - dist);
                }
            }

            float cap = EaseSpeed * dt;
            for (int i = 0; i < _standing.Count; i++)
            {
                var step = _ease[i];
                float want = step.magnitude;
                // A DEAD ZONE, or the pair jitters on the line for ever. The push
                // falls off as they part, so without a floor the last centimetres are
                // spent twitching at each other - a crew that never settles.
                if (want < SettledEnough) continue;
                if (want > cap) step *= cap / want;
                // HE TAKES A STEP. Written into the transform, the ease is a man
                // sliding sideways with his feet still - and this pass runs on men who
                // are STANDING, so there is no stride of his own for it to hide in. He
                // is handed the direction instead and shuffles out of the way on the
                // pack's own standing step (CrewWalker.EaseAside), which carries him at
                // the clip's metres a second and so cannot slide by construction.
                //
                // A man who cannot take one now - already stepping, mid-join, or too
                // far off the camera for any of it to read - is simply left where he
                // is: the overlap is still there next frame and he is asked again. It
                // opens out over a beat instead of in one write, which is what two
                // people giving each other room actually looks like.
                var dir = step;
                dir.y = 0f;
                float metres = dir.magnitude;
                if (metres > 1e-4f) _standing[i].EaseAside(dir / metres, metres);
            }
        }

        // ------------------------------------------------------------------ the deal

        // Re-deals EVERY standing house's figures to its own books. Men are keyed by
        // roster id - unique across all twenty-one by construction - so a hood moved
        // between crews keeps his body and simply walks over, and a family's man is
        // never confused with ours. Only bodies on nobody's books at all (the law, a
        // bench scene's mob) are left alone.
        void Sync(LivingCity.Outfit.Underworld underworld)
        {
            // A collector struck off during a round clears the pure roster node and
            // benches its escorts immediately. The walking body nevertheless owns the
            // round until it banks or is wiped, so keep that exact detachment together
            // long enough for a living escort to pick up the physical bag.
            var activeRoundBags = new Dictionary<int, Unit>();
            var territory = TerritoryRuntime.Instance;
            if (territory != null)
                for (var i = 0; i < Units.Count; i++)
                {
                    var unit = Units[i];
                    if (unit != null && unit.Faction == 0 && unit.IsDetachment &&
                        territory.TryGetRound(unit.CrewId, out _, out _, out _))
                        activeRoundBags[unit.CrewId] = unit;
                }

            // Every man every STANDING house wants on the street, whoever's he is. Ids
            // are unique across all twenty-one books by construction, so one map holds
            // the city.
            var wanted = new Dictionary<int, (LivingCity.Outfit.House house, Crew crew, bool boss)>();
            // crew id -> the hood who carries its bag (GAN-262). Only OUR book is asked
            // for one - no other family walks a collector - so a crew number is key
            // enough here and cannot collide with another house's numbering.
            var bagMen = new Dictionary<int, int>();
            var bagMemberCrew = new Dictionary<int, int>();
            var bagCrews = new HashSet<int>();
            for (var h = 0; h < _houses.Count; h++)
            {
                var house = underworld.Of(_houses[h]);
                if (house?.Roster == null)
                    continue;
                var book = house.Roster;
                var ours = house.IsPlayer;
                foreach (var crew in book.Crews)
                {
                    var lt = book.Find(crew.LieutenantId);
                    if (lt == null || lt.Status != CharacterStatus.Active) continue;
                    wanted[lt.Id] = (house, crew, true);
                    var tacticalHoods = 0;

                    var bagId = ours ? RosterOps.CollectorOf(book, crew.Id) : -1;
                    var bagMan = bagId >= 0 ? book.Find(bagId) : null;
                    if (bagMan != null && bagMan.Status == CharacterStatus.Active)
                    {
                        bagMen[crew.Id] = bagId;
                        bagMemberCrew[bagId] = crew.Id;
                        bagCrews.Add(crew.Id);
                        wanted[bagId] = (house, crew, false);
                    }

                    // The post survives a collector's spell in a bed or a cell. His
                    // active escorts therefore remain a real detachment at the house;
                    // they do not disappear from the street projection merely because
                    // the man they guard cannot walk today's round.
                    if (ours)
                        for (var e = 0; e < crew.EscortIds.Count &&
                             e < Crew.MaxEscorts; e++)
                        {
                            var escort = book.Find(crew.EscortIds[e]);
                            if (escort == null || escort.Status != CharacterStatus.Active)
                                continue;
                            bagMemberCrew[escort.Id] = crew.Id;
                            bagCrews.Add(crew.Id);
                            wanted[escort.Id] = (house, crew, false);
                        }

                    foreach (int id in crew.HoodIds)
                    {
                        var hood = book.Find(id);
                        if (hood != null && hood.Status == CharacterStatus.Active &&
                            tacticalHoods < Crew.MaxTacticalHoods)
                        {
                            wanted[id] = (house, crew, false);
                            tacticalHoods++;
                        }
                    }

                    // Returned escorts may now sit past the line's fourth place. They
                    // still belong to the already-open round until it settles.
                    if (ours && activeRoundBags.TryGetValue(crew.Id, out var walkingBag))
                        foreach (var walker in walkingBag.All())
                        {
                            var hood = walker != null ? book.Find(walker.CharacterId) : null;
                            if (hood != null && hood.Status == CharacterStatus.Active)
                                wanted[hood.Id] = (house, crew, false);
                        }
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
                if (OnTheBooks(unit))
                    foreach (var man in unit.All()) previousUnitOf[man] = unit;

            var liveUnits = new List<Unit>();
            for (var h = 0; h < _houses.Count; h++)
            {
                var house = underworld.Of(_houses[h]);
                if (house?.Roster == null)
                    continue;
                var gangId = house.GangId;
                var book = house.Roster;
                var family = LivingCity.Gangs.GangCatalog.Names[gangId];
                foreach (var crew in book.Crews)
                {
                    if (!wanted.TryGetValue(crew.LieutenantId, out var w) || w.crew != crew) continue;
                    var id = crew.Id;
                    var unit = Units.Find(u => u.Faction == gangId && !u.IsDetachment && u.CrewId == id)
                               ?? new Unit { CrewId = id, Faction = gangId, GangName = family,
                                             Bombs = gangId == 0 ? BombsPerCrew : 0 };
                    unit.CommandParentId = crew.LieutenantId;
                    unit.Boss = null;
                    unit.Hoods.Clear();
                    liveUnits.Add(unit);

                    var lt = book.Find(crew.LieutenantId);
                    unit.Name = lt.FullName;
                    unit.Loyalty = lt.Loyalty;
                    if (unit.Root == null)
                        unit.Root = new GameObject("Crew").transform;
                    unit.Root.name = (gangId == 0 ? "Crew · " : "Rival · " + family + " · ") +
                                     lt.FullName;
                    unit.Root.SetParent(_root, false);
                }
            }

            // THE BAG UNITS (GAN-262): one per crew of OURS with an assigned collector
            // or escort, kept across deals by crew number like the line is. An active
            // escort keeps the post physically present while its collector is laid up;
            // the post disappears only when nobody assigned to it can stand there.
            // Held by crew number for the deal below: BagUnitOf refuses a unit with
            // nobody standing in it, and a bag unit has nobody in it until its man is
            // placed - asking it here would put the bag man back in the line.
            var bagUnits = new Dictionary<int, Unit>();
            var playerBook = underworld.Player?.Roster;
            if (playerBook != null)
                foreach (var crew in playerBook.Crews)
                {
                    var carriesOn = activeRoundBags.TryGetValue(crew.Id, out var walkingBag);
                    if (!bagCrews.Contains(crew.Id) && !carriesOn) continue;
                    var parent = liveUnits.Find(
                        u => u.Faction == 0 && !u.IsDetachment && u.CrewId == crew.Id);
                    if (parent == null) continue;
                    var bag = carriesOn ? walkingBag
                              : Units.Find(u => u.Faction == 0 && u.IsDetachment && u.CrewId == crew.Id)
                              ?? new Unit { CrewId = crew.Id, Faction = 0,
                                            GangName = parent.GangName, IsDetachment = true };
                    bagUnits[crew.Id] = bag;
                    bag.Parent = parent;
                    bag.CommandParentId = crew.LieutenantId;
                    bag.Boss = null;
                    bag.Hoods.Clear();
                    liveUnits.Add(bag);

                    bag.Name = parent.Name + " · the bag";
                    bag.Loyalty = parent.Loyalty;
                    if (bag.Root == null)
                        bag.Root = new GameObject("Bag").transform;
                    var carrier = bagMen.TryGetValue(crew.Id, out var carrierId)
                        ? playerBook.Find(carrierId) : null;
                    bag.Root.name = "Bag · " +
                                    (carrier != null ? carrier.FullName : parent.Name);
                    bag.Root.SetParent(_root, false);
                }

            // the law's squads and whatever a bench scene stood by hand: nobody's books,
            // and none of this pass's business
            var unbooked = Units.FindAll(u => !OnTheBooks(u));
            foreach (var unit in Units)
                if (OnTheBooks(unit) && !liveUnits.Contains(unit))
                {
                    if (Selected == unit) Selected = null;
                    // a crew off the books leaves no billet behind for the next crew to
                    // inherit its number and its hallway
                    CrewQuarters.Forget(unit);
                    // whoever is still under it moves crews below; get them out first
                    foreach (var man in unit.All())
                        if (man.Tf) man.Tf.SetParent(_root, true);
                    if (unit.Root) Destroy(unit.Root.gameObject);
                }
            Units.Clear();
            Units.AddRange(liveUnits);
            Units.AddRange(unbooked);

            // lieutenants first, so a hood dealt in afterwards has a boss to stand behind
            foreach (var kv in wanted)
                if (kv.Value.boss) Place(kv.Value.house, kv.Key, kv.Value.crew, true, previousUnitOf);
            foreach (var kv in wanted)
            {
                if (kv.Value.boss) continue;
                // Collector and escorts are dealt into their own unit, never the line.
                var bag = kv.Value.house.IsPlayer &&
                          bagMemberCrew.TryGetValue(kv.Key, out var bagCrewId) &&
                          bagCrewId == kv.Value.crew.Id &&
                          bagUnits.TryGetValue(kv.Value.crew.Id, out var into)
                    ? into
                    : null;
                if (bag == null &&
                    activeRoundBags.TryGetValue(kv.Value.crew.Id, out var walkingBag) &&
                    _byCharacter.TryGetValue(kv.Key, out var walker) &&
                    previousUnitOf.TryGetValue(walker, out var previous) &&
                    previous == walkingBag)
                    bag = walkingBag;
                Place(kv.Value.house, kv.Key, kv.Value.crew, false, previousUnitOf, bag);
            }

            // A man just handed the bag leaves the line for the front: he was standing
            // in the crew's row a moment ago and has a walk ahead of him. A man dealt
            // in fresh already stands outside the door (Place). A bag unit its man
            // never reached - he was killed, or the deal refused him a body - is not a
            // detachment, it is an empty billet, and it goes.
            for (var i = liveUnits.Count - 1; i >= 0; i--)
            {
                var unit = liveUnits[i];
                if (!unit.IsDetachment) continue;
                if (unit.Standing() == 0)
                {
                    liveUnits.RemoveAt(i);
                    Units.Remove(unit);
                    if (unit.Root) Destroy(unit.Root.gameObject);
                    continue;
                }
                var changed = false;
                foreach (var man in unit.All())
                    if (!man.Dead && (!previousUnitOf.TryGetValue(man, out var was) || was != unit))
                        changed = true;
                var bagRuntime = TerritoryRuntime.Instance;
                var roundAway = bagRuntime != null &&
                    (bagRuntime.TryGetRound(unit.CrewId, out _, out _, out _) ||
                     bagRuntime.BagRoundPending(unit.CrewId));
                var defenceAway =
                    (unit.TargetUnit != null && !unit.TargetUnit.Wiped) ||
                    (bagRuntime != null && bagRuntime.BagDefenceActive(unit.CrewId));
                // Sync owns structural re-deals only. An unchanged idle detail whose
                // billet was lost is TendBagDefence's concern, so its post-fight quiet
                // window cannot be bypassed by an unrelated roster version bump.
                if (BagSyncShouldStationModel(changed, roundAway, defenceAway))
                    StationBagAtHeadquarters(unit);
            }

            if (!_initialPlayerSelectionMade)
            {
                foreach (var unit in liveUnits)
                {
                    if (unit.Faction != 0 || unit.Boss == null || unit.Boss.Dead)
                        continue;
                    _initialPlayerSelectionMade = true;
                    if (Selected == null)
                        Selected = unit;
                    break;
                }
            }

            // D4: a family's Don keeps to his own premises. He is stood up at his front
            // like anybody else and then goes inside it, which is exactly what the
            // player's TAKE THEM INSIDE row does to a crew of ours.
            for (var i = 0; i < liveUnits.Count; i++)
                TakeTheDonInside(liveUnits[i]);

            var player = underworld.Player;
            if (player?.Roster != null)
            {
                BindCars(player.Roster);
                BindBikes(player.Roster);
                BindBombs(player.Roster);
            }
        }

        /// <summary>
        /// A rival Don is kept INSIDE his own front (D4) - the one man of a family the
        /// street cannot simply walk up to. The same call the player's own TAKE THEM
        /// INSIDE row makes, so there is one way in and one way out of a building.
        /// </summary>
        void TakeTheDonInside(Unit unit)
        {
            if (unit == null || unit.Faction <= 0 || unit.Wiped ||
                CrewQuarters.Billeted(unit))
                return;
            var house = HouseOf(unit.Faction);
            if (house?.Roster == null || unit.CommandParentId != house.Roster.BossId)
                return;
            var front = FrontOf(unit.Faction);
            if (front == null)
                return;
            if (front.BusinessId.IsValid)
                CrewQuarters.Station(this, unit, front.BusinessId);
            else
                CrewQuarters.Station(this, unit, front.Outside, front.Role);
        }

        /// <summary>
        /// Read-only organization projection: only the real Characters currently
        /// represented by each small RTS detachment. A 50-man branch still yields at
        /// most one lieutenant plus four hoods here.
        /// </summary>
        public void CollectPhysicalMappings(List<TacticalPersonnelMapping> into)
        {
            if (into == null)
                return;

            for (var i = 0; i < Units.Count; i++)
            {
                var unit = Units[i];
                if (unit == null || unit.Faction != 0)
                    continue;

                var count = (unit.Boss != null ? 1 : 0) + unit.Hoods.Count;
                var ids = new int[count];
                var at = 0;
                if (unit.Boss != null)
                    ids[at++] = unit.Boss.CharacterId;
                for (var h = 0; h < unit.Hoods.Count; h++)
                    if (unit.Hoods[h] != null)
                        ids[at++] = unit.Hoods[h].CharacterId;
                if (at != ids.Length)
                    System.Array.Resize(ref ids, at);

                into.Add(new TacticalPersonnelMapping(
                    unit.CrewId * 2 + (unit.IsDetachment ? 1 : 0),
                    unit.CommandParentId, ids, unit.IsDetachment));
            }
        }

        /// <summary>The roster sync may station only a structurally re-dealt detail.
        /// Ordinary lost-billet recovery belongs to TerritoryRuntime's defence tick.</summary>
        internal static bool BagSyncShouldStationModel(
            bool rosterChanged, bool roundAway, bool defenceAway) =>
            rosterChanged && !roundAway && !defenceAway;

        /// <summary>One route home for every bag-detail caller: roster sync, defence
        /// stand-down and a banked round must all put the men behind the same door.
        ///
        /// EVERY HOUSE HAS ITS OWN DOOR. A rival's bag men go home to the front the city
        /// seated that family, never to the player's headquarters - the fallback below is
        /// the OUTFIT's own office and belongs to house zero alone. A rival trio billeted
        /// at the player's address stands there for good, counts as presence on his home
        /// block, and is picked up by his own headquarters-defence tick as a threat that
        /// his own code put there.</summary>
        internal bool StationBagAtHeadquarters(Unit unit)
        {
            if (unit == null || !unit.IsDetachment)
                return false;
            var front = FrontOf(unit.Faction);
            if (front != null)
            {
                if (front.BusinessId.IsValid &&
                    CrewQuarters.Station(this, unit, front.BusinessId))
                    return true;
                return CrewQuarters.Station(this, unit, front.Outside, "HQ");
            }
            if (unit.Faction != LivingCity.Gameplay.PlayerCommands.House.Value)
                return false;
            var outfit = OutfitDirector.Instance;
            if (outfit != null && outfit.TryGetHeadquarters(out var hq, out _))
                return CrewQuarters.Station(this, unit, hq, "HQ");
            return false;
        }

        public bool TryLocateGroup(int leaderId, out TerritoryBlockId blockId)
        {
            blockId = default;
            var runtime = TerritoryRuntime.Instance;
            if (runtime == null)
                return false;

            for (var i = 0; i < Units.Count; i++)
            {
                var unit = Units[i];
                if (unit == null || unit.Faction != 0 || unit.IsDetachment ||
                    unit.CommandParentId != leaderId)
                    continue;

                var world = unit.Position;
                if (CrewQuarters.Inside(unit) &&
                    CrewQuarters.TryGetDoorstep(unit, out var doorstep))
                    world = doorstep;
                return runtime.TryGetBlockAtWorld(world, out blockId);
            }
            return false;
        }

        public void CollectHeadquartersInside(List<InsideCrew> into)
        {
            if (into == null)
                return;
            var roster = PersonnelDirector.Instance?.Roster;
            if (roster == null)
                return;

            for (var i = 0; i < Units.Count; i++)
            {
                var unit = Units[i];
                if (unit == null || unit.Faction != 0 || unit.IsDetachment ||
                    !CrewQuarters.InsideHeadquarters(unit))
                    continue;
                var leader = roster.Find(unit.CommandParentId);
                if (leader != null)
                    into.Add(new InsideCrew(leader.Id, leader.FullName, unit.Standing()));
            }
        }

        /// <summary>Where the bag man stands between rounds: the outfit's own doorstep,
        /// or the floor's anchor where the scene stands no front.</summary>
        Vector3 FrontDoorstep()
        {
            var front = PlayerFront();
            if (front != null)
                return front.Door;
            var p = _outfitAnchor;
            p.y = GroundY;
            return p;
        }

        void Place(LivingCity.Outfit.House house, int id, Crew crew, bool boss,
            Dictionary<CrewWalker, Unit> previousUnitOf, Unit into = null)
        {
            // <paramref name="into"/> names the bag unit for a bag man (GAN-262);
            // everybody else is dealt into his crew's line, on his family's own books.
            var roster = house.Roster;
            var gangId = house.GangId;
            var crewId = crew.Id;
            var unit = into ??
                       Units.Find(u => u.Faction == gangId && !u.IsDetachment && u.CrewId == crewId);
            if (unit == null) return;
            var member = roster.Find(id);

            bool fresh = !_byCharacter.TryGetValue(id, out var man);

            // a fallen man is on the books until the ledger strikes him; his body
            // stays on the ground and takes no part in the crew's business
            if (!fresh && man.Dead)
            {
                man.CrowdGroupId = unit.CrowdGroupId;
                if (boss) unit.Boss = man; else unit.Hoods.Add(man);
                return;
            }

            // the book recasts a man when his rank changes (a lieutenant sits for
            // his photograph in a suit) - the same face must walk the street, so
            // the body is swapped on the spot
            var cast = CastFor(member, roster);
            // NOT WHILE HE IS INSIDE ONE OF OUR BUILDINGS. The swap destroys the body and
            // stands a new one where it was - which for a man being held indoors
            // (CrewQuarters) is a body standing inside a wall, switched on, with the
            // door beat still holding the one that was destroyed. His new suit waits
            // until he is back on the pavement.
            if (!fresh && cast != null && man.SourcePrefab != cast && !DoorBeat.Active(man))
            {
                var link = man.CurrentLink;
                float t = man.CurrentT;
                var pos = man.Tf.position;
                var rot = man.Tf.rotation;
                bool hadGraphSeat = !FreeRoam && man.OnGraph && link != null &&
                    (man.GraphDriven || man.State == CrewWalker.Mode.Standing);
                var seatCar = CarAboard(man, out int seatHad); // recast in his seat: he keeps it
                RemoveMan(id);
                float pace = boss ? BossPace : HoodPace();
                man = hadGraphSeat ? SpawnMember(member, roster, link, t, pace)
                                   : SpawnMember(member, roster, pos, rot, pace);
                if (man == null) return;
                _byCharacter[id] = man;
                if (seatCar != null && seatHad >= 0)
                {
                    seatCar.Aboard.Add(man);
                    seatCar.SeatOf[man] = seatHad;
                    man.SetRiding(true);
                }
            }

            if (fresh)
            {
                // a new man walks in beside his boss - beside the car, on the kerb side,
                // when the boss is sat in it; a new crew opens up on ground of its own,
                // apart from the others
                bool bossHasGraphSeat = unit.Boss != null && unit.Boss.OnGraph &&
                    unit.Boss.CurrentLink != null &&
                    (unit.Boss.GraphDriven || unit.Boss.State == CrewWalker.Mode.Standing);
                if (FreeRoam || (unit.Boss != null && !bossHasGraphSeat))
                {
                    var rot = unit.Boss != null && unit.Boss.Tf != null
                        ? unit.Boss.Tf.rotation : Quaternion.LookRotation(_outfitFacing);
                    Vector3 pos;
                    if (unit.Car != null && unit.Car.Tf != null)
                        pos = KerbSideOf(unit.Car);
                    else if (unit.Boss != null)
                        pos = unit.Boss.Tf.position + unit.Boss.Tf.rotation * FormationOffset(unit.CrewId, unit.Hoods.Count);
                    else
                        pos = OutfitSpawnPoint(unit);
                    pos = WalkObstacles.ClearSpot(pos, WalkObstacles.Radius);
                    man = SpawnMember(member, roster, pos, rot, boss ? BossPace : HoodPace());
                }
                else
                {
                    PedLink link;
                    float t;
                    if (unit.Boss != null)
                    {
                        link = unit.Boss.CurrentLink;
                        t = FormationT(link, unit.Boss.CurrentT, unit.CrewId, unit.Hoods.Count);
                    }
                    else
                    {
                        // Where this crew was POSTED when the city was laid out - outside
                        // its family's own door, or on a corner of its own - and, for a
                        // crew nobody posted, outside the house's front, and failing that
                        // any pavement far from everybody else.
                        link = PostedSpawnLink(unit, out t);
                        if (link == null)
                        {
                            link = PickSpawnLink();
                            t = link.Length * 0.5f;
                        }
                    }
                    man = SpawnMember(member, roster, link, t, boss ? BossPace : HoodPace());
                }
                if (man == null) return;
                _byCharacter[id] = man;
            }

            man.IsLieutenant = boss;
            man.DisplayName = member.FullName;
            man.Faction = gangId;
            man.CrowdGroupId = unit.CrowdGroupId;
            // a crew HOLDS its ground: nobody wanders, the lieutenant included. The
            // boss used to take a short anchored stroll for life's sake, but from
            // the player's chair that is one man walking off and leaving the crew
            // he posted ("seta okolo i napusta bandu dok samo stoje") - so a posted
            // crew stands. Lone men off any crew's books still roam.
            man.RoamsAlone = false;
            man.RoamReach = 14f;
            if (boss && !man.Post.HasValue && man.Tf != null) man.Post = man.Tf.position;
            if (!boss) man.HoldLane(FormationLane(unit.CrewId, unit.Hoods.Count));
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
            // A body dealt onto a posting has used it; the crew stands where it stands
            // from here on.
            if (fresh && boss)
                _postings.Remove(crewId);

            // a hood who changed crews - or just arrived - falls in on his boss; on a
            // crew that is sat in its car he gets in with them (a seat for him, and the
            // walk to his door - the rest is TickCars), or stands by it when it is full
            previousUnitOf.TryGetValue(man, out var was);
            if (!boss && (fresh || was != unit))
            {
                if (unit.Car != null && !IsAboard(man)) JoinCar(unit, man);
                else if (unit.Boss != null) FallIn(unit, man, unit.Hoods.Count - 1);
            }
        }

        // One man to the car his crew is sat in: a seat if there is one and the walk
        // to its door; the door opens as he arrives and he gets in (TickCars).
        void JoinCar(Unit unit, CrewWalker man)
        {
            var car = unit.Car;
            if (car == null || man.Dead || car.SeatOf.ContainsKey(man)) return;
            int seat = car.FreeSeat();
            if (seat < 0) return;
            car.SeatOf[man] = seat;
            man.Disengage();
            man.OrderToPoint(car.DoorPoint(seat));
            unit.Boarding = car;
        }

        // Beside a car, on the side away from the middle of the road (the kerb, where
        // a man steps off the pavement to it); a stride off its flank, level with its doors.
        Vector3 KerbSideOf(CrewCar car)
        {
            var right = car.Tf.right;
            right.y = 0f;
            // which way is "away from the middle" is the road's own business (the car
            // knows its kerb side on whatever street it stands on)
            float side = Vector3.Dot(right, car.KerbSideDir) >= 0f ? 1f : -1f;
            var p = car.Position + right.normalized * side * (car.HalfWidth + 1.6f);
            p.y = GroundY;
            return p;
        }

        /// <summary>The gun the ledger says he holds - re-checked on every deal, so a
        /// pistol handed over on the armory page changes hands on the street too.</summary>
        void ArmFromLedger(Roster roster, CrewWalker man)
        {
            // NOT WHILE HE IS ON A MACHINE. A saddle caps what a man may carry at the
            // machine pistol (CrewBike.CapArms) and hands his own gun back when he gets
            // off; a deal that lands mid-pass would put the rifle straight back in his
            // fist and the cap would be undone by a page of the book being turned.
            if (man.Riding) return;
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

        /// <summary>
        /// Where a crew opens up the first time the books ask for it. A crew POSTED
        /// when the city was laid out (RoadDemoBuilder: a family's first crew outside
        /// its own door, the rest on corners of their own) stands on its posting; every
        /// other crew stands outside its family's front, in a row with the family's
        /// other crews.
        /// </summary>
        PedLink PostedSpawnLink(Unit unit, out float t)
        {
            t = 0f;
            if (_postings.TryGetValue(unit.CrewId, out var posting) &&
                _sidewalks != null && _sidewalks.Count > 0)
            {
                var link = NearestSidewalk(posting.anchor, out t);
                if (link != null)
                    return link;
            }
            return FrontSpawnLink(unit, out t);
        }

        /// <summary>The sidewalk link nearest a spot, and the point along it closest to
        /// that spot. Null when no pavement is within reach of it at all.</summary>
        PedLink NearestSidewalk(Vector3 at, out float t)
        {
            t = 0f;
            PedLink best = null;
            float bestD = float.MaxValue, bestT = 0f;
            for (int i = 0; i < _sidewalks.Count; i++)
            {
                var link = _sidewalks[i];
                if (link == null || link.Gated) continue;
                var from = link.From.Pos;
                var along = link.To.Pos - from;
                float len2 = along.sqrMagnitude;
                if (len2 < 1e-4f) continue;
                float u = Mathf.Clamp01(Vector3.Dot(at - from, along) / len2);
                var on = from + along * u;
                float d = (on - at).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = link;
                bestT = u * link.Length;
            }
            if (best == null || bestD > 40f * 40f)
                return null;
            t = Mathf.Clamp(bestT, 0.3f, best.Length - 0.3f);
            return best;
        }

        /// <summary>The pavement outside the crew's family's own door: the sidewalk link
        /// nearest the front, at the spot straight out from the doorstep - crews in
        /// a row along it, in book order, a spread apart, the same row the empty
        /// floor deals (OutfitSpawnPoint). Null when the scene stands no fronts (the
        /// demo streets), or no pavement runs anywhere near the door.</summary>
        PedLink FrontSpawnLink(Unit unit, out float t)
        {
            t = 0f;
            var front = FrontOf(unit.Faction);
            if (front == null || _sidewalks == null || _sidewalks.Count == 0) return null;

            PedLink best = front.EntryLink;
            float bestD = 0f, bestT = front.EntryT;
            // Authored/demo fronts do not yet publish an entry link. Keep their old
            // nearest-pavement fallback; Core fronts always take the exact link that was
            // selected with their shop door.
            if (best == null)
            {
                bestD = float.MaxValue;
                foreach (var l in _sidewalks)
                {
                    var along = l.To.Pos - l.From.Pos;
                    float len = l.Length;
                    if (len < 1e-3f) continue;
                    float proj = Mathf.Clamp(Vector3.Dot(front.Door - l.From.Pos, along / len), 0f, len);
                    var p = l.From.Pos + along * (proj / len);
                    float d = (p - front.Door).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = l; bestT = proj; }
                }
            }
            // a door with no pavement near it is a badly seated front, not a spawn point
            if (best == null || bestD > 30f * 30f) return null;

            // A row along the pavement, one crew of THIS family per place in it - two
            // families seated on the same street stand their own men in their own rows.
            int index = 0, count = 0;
            foreach (var u in Units)
            {
                if (u.Faction != unit.Faction) continue;
                if (u == unit) index = count;
                count++;
            }
            t = Mathf.Clamp(bestT + (index - (count - 1) * 0.5f) * _outfitSpread,
                            0.3f, best.Length - 0.3f);
            return best;
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
            const float beat = HoodBeatSeconds;
            bool movingOnGraph = !FreeRoam && boss.GraphDriven &&
                boss.CurrentLink != null && boss.DestinationLink != null;
            bool standingOnGraph = !FreeRoam && boss.State == CrewWalker.Mode.Standing &&
                boss.OnGraph && boss.CurrentLink != null && !boss.CurrentLink.Gated;
            if (!movingOnGraph && !standingOnGraph)
            {
                FallInAcross(unit, boss, hood, k, beat);
                return;
            }

            Reseat(hood);
            if (!hood.OnGraph || hood.CurrentLink == null)
            {
                FallInAcross(unit, boss, hood, k, beat);
                return;
            }

            if (movingOnGraph)
            {
                var destination = boss.DestinationLink;
                hood.OrderTo(destination,
                    FormationT(destination, boss.DestinationT, unit.CrewId, k), beat);
                hood.MarkFallingIn();
                return;
            }

            var link = boss.CurrentLink;
            float t = FormationT(link, boss.CurrentT, unit.CrewId, k);
            // freshly dealt in on his spot already - no need to shuffle
            if (hood.CurrentLink == link && Mathf.Abs(hood.CurrentT - t) < 0.35f) return;
            hood.OrderTo(link, t, beat);
            hood.MarkFallingIn();
        }

        /// <summary>Fall in on a leader who is genuinely off the sidewalk graph. A
        /// moving leader's final order target is the anchor, not whichever A* corner he
        /// happens to be approaching this frame.</summary>
        void FallInAcross(Unit unit, CrewWalker boss, CrewWalker hood, int k, float beat)
        {
            var anchor = boss.HasOrder ? boss.OrderDestination : boss.Tf.position;
            var facing = boss.HasOrder ? anchor - boss.Tf.position : boss.Tf.forward;
            facing.y = 0f;
            var rot = Quaternion.LookRotation(
                facing.sqrMagnitude > 1e-3f ? facing.normalized : boss.Tf.forward);
            var spot = WalkObstacles.ClearSpot(
                anchor + rot * FormationOffset(unit.CrewId, k), WalkObstacles.Radius);
            if ((hood.Tf.position - spot).sqrMagnitude <= 0.35f * 0.35f) return;
            if (FreeRoam)
            {
                hood.OrderToPoint(spot, beat);
                hood.MarkFallingIn();
            }
            else OrderFallInAcross(unit, boss, hood, k, beat);
        }

        void RemoveMan(int id)
        {
            if (!_byCharacter.TryGetValue(id, out var man)) return;
            _byCharacter.Remove(id);
            _chasers.Remove(man);
            // out of whatever car he sat in, or was walking to
            foreach (var car in Cars)
            {
                car.Aboard.Remove(man);
                car.SeatOf.Remove(man);
            }
            man.Dispose();
            if (man.Tf) Destroy(man.Tf.gameObject);
        }

        /// <summary>The car this man sits in right now, and his seat; null if none.</summary>
        CrewCar CarAboard(CrewWalker man, out int seat)
        {
            seat = -1;
            foreach (var car in Cars)
                if (car.Aboard.Contains(man)) { car.SeatOf.TryGetValue(man, out seat); return car; }
            return null;
        }

        // ------------------------------------------------------------------ bodies

        GameObject CastFor(Character member, Roster roster)
        {
            // The very prefab the ledger photographs for his mugshot - same face on
            // the street as in the book. Read against HIS OWN house's roster: a
            // family's coat is dealt onto its men when the family is dealt
            // (RosterSeeder), and asking our book about their man would have re-cast
            // him out of the crowd. Only when that cannot be resolved (the cast asset
            // not baked, the pack missing) does a crowd body stand in, and it says so,
            // so a stranger on the corner is never mistaken for the design.
            var prefab = LivingCity.UI.PortraitStudio.FindPeoplePrefab(
                LivingCity.Gangs.GangLooks.LookFor(member, roster));
            if (prefab == null && _fallbackPrefabs != null && _fallbackPrefabs.Count > 0)
            {
                prefab = _fallbackPrefabs[member.Id % _fallbackPrefabs.Count];
                Debug.LogWarning("[RoadDemo] No ledger model for " + member.FullName +
                                 " - a crowd body (" + prefab.name + ") stands in.");
            }
            return prefab;
        }

        /// <summary>Where this man may actually be left standing, given somebody asked
        /// for that spot. EVERY man of every crew, mob and squad in every scene is stood
        /// up through the three Spawn calls below, and this is why they are worth
        /// funnelling: the spots come from builders that know the pavement's shape and
        /// nothing at all about what was later laid on it. A frontage band, a kerb line,
        /// a lieutenant's shoulder plus one and a half metres - all of them perfectly
        /// sensible, and all of them capable of landing a man in a palm, which is what
        /// the player kept finding. A hood dealt into the trunk cannot walk out either:
        /// off the graph he moves by Steer, and Steer gives a man whose shoulders foul
        /// something on every heading nowhere to step.
        ///
        /// Off the graph only - a man dealt onto a sidewalk link slides to the free
        /// lateral line by himself (PedLink.FreeLine), and a man being put in a seat is
        /// not standing anywhere.</summary>
        Vector3 Clear(Vector3 wanted, string who)
        {
            var spot = WalkObstacles.FreeSpot(wanted, WalkObstacles.Radius);
            if (DriveTrace.On && (spot - wanted).sqrMagnitude > 1e-4f)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", who);
                DriveTrace.Num(sb, "moved", Vector3.Distance(spot, wanted));
                DriveTrace.Vec(sb, "from", wanted);
                DriveTrace.Vec(sb, "to", spot);
                DriveTrace.Row("spawnclear", sb.ToString());
            }
            return spot;
        }

        CrewWalker SpawnMember(Character member, Roster roster, PedLink link, float t,
            float pace)
        {
            var prefab = CastFor(member, roster);
            if (prefab == null) return null;
            var go = Body(prefab, member.FullName, member.Id, PedestrianAnthropometry.GangSalt,
                out var anthropometry);
            var man = new CrewWalker
                { Speed = pace, CharacterId = member.Id, SourcePrefab = prefab,
                  CombatHalfSteps = member.GetHalfSteps(CharacterAttribute.Combat),
                  Anthropometry = anthropometry };
            man.Init(go.transform, ClipsFor(prefab), link, Mathf.Clamp(t, 0.3f, link.Length - 0.3f));
            man.Fired = OnFired;
            man.RangeFactor = Random.Range(0.55f, 0.85f);
            man.SetJog(Random.Range(2.7f, 3.5f));
            return man;
        }

        CrewWalker SpawnMember(Character member, Roster roster, Vector3 pos,
            Quaternion rot, float pace)
        {
            var prefab = CastFor(member, roster);
            if (prefab == null) return null;
            var go = Body(prefab, member.FullName, member.Id, PedestrianAnthropometry.GangSalt,
                out var anthropometry);
            var man = new CrewWalker
                { Speed = pace, CharacterId = member.Id, SourcePrefab = prefab,
                  CombatHalfSteps = member.GetHalfSteps(CharacterAttribute.Combat),
                  Anthropometry = anthropometry };
            man.InitAt(go.transform, ClipsFor(prefab), Clear(pos, member.FullName), rot);
            man.Fired = OnFired;
            man.RangeFactor = Random.Range(0.55f, 0.85f);
            man.SetJog(Random.Range(2.7f, 3.5f));
            return man;
        }

        CrewWalker SpawnAt(GameObject prefab, string name, int id, Vector3 pos, Quaternion rot,
            float pace, bool afoot = true,
            int anthropometrySalt = PedestrianAnthropometry.GangSalt)
        {
            if (prefab == null) return null;
            if (id == -1)
                id = _anonymousCharacterId--;
            var go = Body(prefab, name, id, anthropometrySalt, out var anthropometry);
            var man = new CrewWalker
                { Speed = pace, CharacterId = id, SourcePrefab = prefab, DisplayName = name,
                  Anthropometry = anthropometry };
            man.InitAt(go.transform, ClipsFor(prefab),
                afoot ? Clear(pos, name) : pos, rot);
            man.Fired = OnFired;
            man.RangeFactor = Random.Range(0.55f, 0.85f);
            man.SetJog(Random.Range(2.7f, 3.5f));
            return man;
        }

        CrewWalker SpawnAt(GameObject prefab, string name, int id, PedLink link, float t,
            float pace, int anthropometrySalt)
        {
            if (prefab == null || link == null) return null;
            if (id == -1) id = _anonymousCharacterId--;
            var go = Body(prefab, name, id, anthropometrySalt, out var anthropometry);
            var man = new CrewWalker
            {
                Speed = pace,
                CharacterId = id,
                SourcePrefab = prefab,
                DisplayName = name,
                Anthropometry = anthropometry,
            };
            man.Init(go.transform, ClipsFor(prefab), link,
                Mathf.Clamp(t, 0.3f, link.Length - 0.3f));
            man.Fired = OnFired;
            man.RangeFactor = Random.Range(0.55f, 0.85f);
            man.SetJog(Random.Range(2.7f, 3.5f));
            return man;
        }

        PedClips ClipsFor(GameObject prefab)
        {
            var clips = CrewKit.Draw(_clips, _variety);
            if (!UseMixamoLocomotion || prefab == null) return clips;
            return MixamoLocomotionKit.ForBody(
                clips, PedestrianIdentity.IsFemale(prefab.name));
        }

        GameObject Body(GameObject prefab, string name, int localId, int anthropometrySalt,
            out PedestrianAnthropometryStamp anthropometry)
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
            anthropometry = PedestrianAnthropometry.Apply(
                go,
                PedestrianAnthropometry.Seed(_anthropometrySeed, localId, anthropometrySalt),
                PedestrianIdentity.IsFemale(prefab.name),
                PedestrianAgeCohort.Adult,
                prefab.name);
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
}
