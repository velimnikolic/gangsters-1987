using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Entities;
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
    public partial class DemoCrews : MonoBehaviour, IOrganizationPhysicalSource
    {
        /// <summary>One lieutenant, his root object, and his men.</summary>
        public class Unit
        {
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
                    if (Boss != null && Boss.Tf) return Boss.Tf.position;
                    foreach (var m in All()) if (m.Tf) return m.Tf.position;
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
        const float CoverReach = 10f;         // the furthest he will go to get behind something
        const float CoverApart = 0.8f;        // two men do not share one flank
        // Slimmer than MinHalf on its short side is a post, not cover; wider than
        // MaxHalf is a wall or a lot, not furniture. CoverDemo reads the same pair.
        internal const float PropCoverMinHalf = 0.22f;
        internal const float PropCoverMaxHalf = 3f;
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
            if (man == null || man.Dead || man.Faction != 0 || man.IsLieutenant || man.Retreating) return;
            var unit = UnitOf(man);
            if (unit != null) unit.Hoods.Remove(man);
            _byCharacter.Remove(man.CharacterId);
            _deserters.Add(man);
            if (man.Tf) man.Tf.SetParent(_root, true);
            man.Retreat(from);
            CrewOverlay.Announce(Surname(man.DisplayName).ToUpperInvariant() + " DESERTED", 4f, new Color(1f, 0.7f, 0.4f));
            var director = PersonnelDirector.Instance;
            if (director != null && director.Roster != null) director.Desert(man.CharacterId);
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
                if (man.Faction == 0)
                {
                    var director = PersonnelDirector.Instance;
                    if (director != null && director.Roster != null)
                        director.Kill(man.CharacterId);
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

        /// <summary>The bang, the flash and the blood - set by the scene builder;
        /// missing pieces are simply silent.</summary>
        public GameObject MuzzleFlashPrefab, BloodPrefab, ImpactPrefab;
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
        Vector3 _outfitAnchor, _outfitFacing = Vector3.forward;
        float _outfitSpread = 9f;
        int _rivalIds = -1;
        int _anonymousCharacterId = -100000;
        int _anthropometrySeed = 1987;
        AudioSource _shots, _cracks;

        /// <summary>The shared view of targeting and cover decisions. It is attached
        /// wherever DemoCrews is used and toggled with I.</summary>
        public CombatIntentOverlay IntentOverlay { get; private set; }

        // ------------------------------------------------------------------ setup

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
            gameObject.AddComponent<CrewBar>().Init(this, BarTopInset);
            // last onto the click chain, so the front card is asked first and hands the
            // click straight back to the crews if a man was standing in front of the door
            gameObject.AddComponent<FrontOverlay>().Init();
            IntentOverlay = gameObject.AddComponent<CombatIntentOverlay>();
            IntentOverlay.Init(this);
            PersonnelDirector.Instance?.SetOrganizationPhysicalSource(this);
            CrewWalker.FindCover = CoverNear;
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
            // after the arms are posed: were this frame's shots actually ON their marks?
            if (DriveTrace.On) CrewAudit.LateTick();
        }

        /// <summary>Take a unit off the street - its men gone (the police driving
        /// away). Nothing for the outfit's own: the ledger owns those.</summary>
        public void RemoveUnit(Unit unit)
        {
            if (unit == null || unit.Faction == 0) return;
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

        // Somewhere for a pressed man to get behind: the far flank of a car stood still,
        // or of a bin, a planter, a phone box - anything of the street's furniture that
        // stands on the far side of him from the man shooting, and leaves the target
        // still in his gun's reach. Never further off than the fight itself: nobody
        // sprints eight metres to a bin with an enemy stood four away. Null: nothing
        // near enough.
        static readonly List<SidewalkPlan.Box> _coverBoxes = new List<SidewalkPlan.Box>();
        static readonly List<Vector3> _claimed = new List<Vector3>();

        Vector3? CoverNear(CrewWalker man, Vector3 target)
        {
            var p = man.Tf.position;
            Vector3? best = null;
            float distToTarget = Vector3.Distance(p, target);
            float cap = Mathf.Min(CoverReach, Mathf.Max(3f, distToTarget * 0.9f));
            float bestD = cap * cap;

            // what the rest of the street is already behind: two men crowding one flank
            // is one man in cover and one stood in the open beside him
            _claimed.Clear();
            foreach (var unit in Units)
                foreach (var m in unit.All())
                    if (m != null && m != man && !m.Dead && m.CoverSpot.HasValue) _claimed.Add(m.CoverSpot.Value);

            foreach (var u in StreetTraffic.Users)
            {
                if (u.RoadSpeed > 0.5f) continue;
                var c = u.RoadPosition;
                var f = u.RoadForward;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) continue;
                f.Normalize();
                var right = Vector3.Cross(Vector3.up, f);
                float side = Vector3.Dot(c - target, right) >= 0f ? 1f : -1f;
                float along = Mathf.Clamp(Vector3.Dot(p - c, f), -u.HalfLength + 0.6f, u.HalfLength - 0.6f);
                // off the flank by a shoulder and a bit - clear of the body the walk
                // keeps out of (WalkObstacles), whatever its width
                var spot = c + right * side * (u.HalfWidth + WalkObstacles.Radius + 0.4f) + f * along;
                spot.y = p.y;
                float d = (spot - p).sqrMagnitude;
                if (d > bestD) continue;
                // a flank he cannot stand at (the next car over, a wall) is no cover -
                // and NOT a flank that puts him under a palm. A COVER SPOT IS A SPOT A
                // MAN IS SENT TO STAND AT, so it takes the canopy berth every other
                // chosen spot in the town takes (WalkObstacles.CanopyBerth). Without it
                // the trunk's knee-high box says the ground beside a kerbside palm is
                // free, the car flank lands right under the fronds, and the player
                // watches a man take cover in a tree and put his gun through it.
                if (WalkObstacles.Occupied(spot, WalkObstacles.Radius, WalkObstacles.CanopyBerth)) continue;
                float toTarget = Vector3.Distance(spot, target);
                if (toTarget < 3f || toTarget > man.Ballistics.Range * 1.2f) continue;
                if (Claimed(spot)) continue;
                bestD = d;
                best = spot;
            }

            // and the same of the pavement's furniture. A prop is a box on the ground
            // (SidewalkPlan): take the face pointing away from the shooter, stand him
            // off it by a shoulder, and slide him along that face toward where he
            // already is - the car's `along`, in the box's own frame.
            WalkObstacles.PropsNear(p, cap, _coverBoxes);
            var t2 = new Vector2(target.x, target.z);
            var p2 = new Vector2(p.x, p.z);
            for (int i = 0; i < _coverBoxes.Count; i++)
            {
                var b = _coverBoxes[i];
                // big enough to put between himself and a round, small enough to be
                // furniture. The plan keeps no height, so this is all the sorting there
                // is: a grate is not solid at all, a lamp post is too slim to hide a man.
                // A TRUNK IS NOT COVER. A palm's box is the slice of it at knee height,
                // so a man sent to its far flank ends up a metre from the pivot - under
                // the canopy, inside the fronds, looking for all the world like a man
                // stuck in a tree, which is exactly what the player saw. Same for a lamp
                // post. Neither hides a man anyway.
                if (b.Tall) continue;
                if (Mathf.Min(b.H.x, b.H.y) < PropCoverMinHalf) continue;
                if (Mathf.Max(b.H.x, b.H.y) > PropCoverMaxHalf) continue;
                var away = b.C - t2;
                if (away.sqrMagnitude < 1e-4f) continue;
                float ax = Vector2.Dot(away, b.Ax), az = Vector2.Dot(away, b.Az);
                Vector2 n, slide;
                float ext, slideHalf;
                if (Mathf.Abs(ax) >= Mathf.Abs(az)) { n = b.Ax * Mathf.Sign(ax); ext = b.H.x; slide = b.Az; slideHalf = b.H.y; }
                else                                { n = b.Az * Mathf.Sign(az); ext = b.H.y; slide = b.Ax; slideHalf = b.H.x; }
                float room = Mathf.Max(0f, slideHalf - 0.2f);
                float along = Mathf.Clamp(Vector2.Dot(p2 - b.C, slide), -room, room);
                var s2 = b.C + n * (ext + WalkObstacles.Radius + 0.35f) + slide * along;
                var spot = new Vector3(s2.x, p.y, s2.y);
                float d = (spot - p).sqrMagnitude;
                if (d > bestD) continue;
                if (WalkObstacles.Occupied(spot, WalkObstacles.Radius, WalkObstacles.CanopyBerth)) continue;
                float toTarget = Vector3.Distance(spot, target);
                if (toTarget < 3f || toTarget > man.Ballistics.Range * 1.2f) continue;
                if (Claimed(spot)) continue;
                bestD = d;
                best = spot;
            }
            return best;
        }

        /// <summary>Is another man already behind this very flank?</summary>
        static bool Claimed(Vector3 spot)
        {
            for (int i = 0; i < _claimed.Count; i++)
                if ((_claimed[i] - spot).sqrMagnitude < CoverApart * CoverApart) return true;
            return false;
        }

        /// <summary>A rival crew, dealt by hand: its lieutenant and hoods stood at the
        /// anchor facing <paramref name="facing"/>, all carrying <paramref name="weapon"/> -
        /// unless <paramref name="armsFor"/> is given, which is asked man by man (0 the
        /// lieutenant, 1.. his hoods) and lets a mob carry a piece each rather than five
        /// copies of one gun.</summary>
        public Unit AddRival(int faction, string gangName, string bossName, GameObject bossPrefab,
            IList<string> hoodNames, IList<GameObject> hoodPrefabs, Vector3 anchor, Vector3 facing,
            GameObject weapon, EquipmentKind weaponKind, bool lineUp = false,
            System.Func<int, (GameObject weapon, EquipmentKind kind)> armsFor = null)
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

            var anthropometrySalt = faction == StreetAlarm.PoliceFaction
                ? PedestrianAnthropometry.PoliceSalt
                : PedestrianAnthropometry.GangSalt;

            var boss = SpawnAt(bossPrefab, bossName, _rivalIds--, anchor, rot, BossPace,
                anthropometrySalt: anthropometrySalt);
            if (boss != null)
            {
                boss.IsLieutenant = true;
                boss.Faction = faction;
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
                var hood = SpawnAt(prefab, hoodNames[k], _rivalIds--, pos, rot, HoodPace(),
                    anthropometrySalt: anthropometrySalt);
                if (hood == null) continue;
                hood.Faction = faction;
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
            if (director != null && director.Roster != null &&
                (FreeRoam || (_sidewalks != null && _sidewalks.Count > 0)) &&
                director.Version != _seenVersion)
            {
                _seenVersion = director.Version;
                _rng ??= new System.Random(director.Seed * 7919 + 13);
                Sync(director.Roster);
            }

            float dt = Time.deltaTime;
            ReportDeaths();
            // BEFORE the fight: the ledger's orders are what put a crew somewhere and
            // what sets it on somebody, so a job that starts this frame must have its
            // march and its mark in hand before TickCombat reads either.
            CrewJobs.Tick(this);
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
            foreach (var unit in Units)
                foreach (var man in unit.All())
                    man.Dispose();
            // the cover hook is a static: left pointing here it keeps a destroyed arena
            // alive and answers the next scene's walkers with this one's floor
            if (CrewWalker.FindCover != null && ReferenceEquals(CrewWalker.FindCover.Target, this))
                CrewWalker.FindCover = null;
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

        /// <summary>Send the selected lieutenant toward a world point over any open
        /// city ground. Returns where he will stand, or false when nothing is selected.
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
        {
            destination = world;
            if (unit == null || unit.Boss == null || unit.Boss.Dead) return false;
            CallOffRaids(unit, "a move order");
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
            world.y = GroundY;
            DispatchAcross(unit, unit.Boss, world, run, keepOffRoad: false);
            destination = world;
            return true;
        }

        /// <summary>Be over there - ON FOOT, and never mind the pavements.
        ///
        /// The crowd keeps to the sidewalk graph, goes round the blocks and waits at the
        /// lights, because that is what a city looks like from the outside. A crew told
        /// to be somewhere does not: it cuts over the lot, across the road against the
        /// light, down the gap between two buildings, and it walks the length of the
        /// quarter to do it. The only ground it will not cross is ground a man cannot
        /// stand on - a wall, a lot, a parked car - and the fields outside the city,
        /// which are not the city (WalkRoute).
        ///
        /// This is the order behind a march; the walking itself, the corners and the
        /// steering past whatever has moved into the way since, is CrewWalker's.</summary>
        public bool MarchTo(Unit unit, Vector3 world, bool run = false, bool keepOffRoad = false)
        {
            if (unit == null) return false;
            // A CREW WHOSE LIEUTENANT IS DOWN IS STILL A CREW. His hoods are on their
            // feet and they can still be sent - somebody at the front picks up the walk.
            // Refusing the order because the man who used to give it is dead left three
            // hoods standing in the street for the rest of the run.
            var boss = unit.Boss != null && !unit.Boss.Dead ? unit.Boss : Standing(unit);
            if (boss == null || boss.Tf == null) return false;
            CallOffRaids(unit, "a march order");
            unit.TargetUnit = null;
            unit.OrderedAt = Time.time;
            Unboard(unit, "a march order");
            unit.PendingDrive = null;
            world = WalkObstacles.ClampToCity(world);
            world.y = GroundY;

            DispatchAcross(unit, boss, world, run, keepOffRoad);
            return true;
        }

        /// <summary>Move a whole crew across physical ground instead of the pedestrian
        /// graph. Static obstacles shape the planned route; traffic is avoided live by
        /// each walker.</summary>
        void DispatchAcross(Unit unit, CrewWalker boss, Vector3 world, bool run, bool keepOffRoad)
        {
            var dir = world - boss.Tf.position;
            dir.y = 0f;
            var rot = Quaternion.LookRotation(dir.sqrMagnitude > 0.25f ? dir.normalized : boss.Tf.forward);
            bool stagger = SettledTogether(unit, boss);
            Reseat(boss);
            boss.OrderAcross(world, keepOffRoad: keepOffRoad);
            // A walk unless the player asked for it twice. The run exists (CrewWalker.
            // Running) but nothing reaches for it on its own: men who break into a jog
            // because some rule inside the game decided they should read as a town in
            // a panic, and the player who wants them there quicker can say so.
            boss.Urgent = run;
            boss.Post = world;
            for (int k = 0; k < unit.Hoods.Count; k++)
            {
                var man = unit.Hoods[k];
                if (man == null || man.Dead || man == boss || man.Riding) continue;
                Reseat(man);
                // spread behind him, so three men arrive as a crew and not as a column
                man.OrderAcross(WalkObstacles.ClearSpot(
                    world + rot * FormationOffset(unit.CrewId, k), WalkObstacles.Radius),
                    stagger ? HoodBeat() : 0f, keepOffRoad);
                man.Urgent = run;
            }
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
            if (Selected == null || target == null || target == Selected || target.Wiped) return false;

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
            return true;
        }

        void Dispatch(Unit unit, PedLink link, float t, bool run = false) =>
            Dispatch(unit, unit.Boss, link, t, run);

        /// <summary>The same walk, led by a named man. A crew whose lieutenant is down is
        /// still a crew and can still be sent (MarchTo says so), so the man at the front
        /// is passed in rather than assumed to be the lieutenant.</summary>
        void Dispatch(Unit unit, CrewWalker lead, PedLink link, float t, bool run = false)
        {
            bool stagger = SettledTogether(unit, lead);
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
                man.OrderTo(link, FormationT(link, t, unit.CrewId, k),
                    stagger ? HoodBeat() : 0f);
                man.Urgent = run;
            }
        }

        /// <summary>The beat a hood waits before he follows an order the boss got - each
        /// his own, so a crew steps off one man after another, not as one machine.</summary>
        static float HoodBeat() => Random.Range(0.15f, 0.9f);

        /// <summary>A stagger belongs only to a crew setting off together. If the last
        /// order has not finished for every man, or even one standing hood is still
        /// outside the tether's definition of "back with his crew", the replacement
        /// order moves everybody at once. Repeating clicks can therefore never keep
        /// restarting the hoods' wait while the lieutenant walks away.</summary>
        static bool SettledTogether(Unit unit, CrewWalker lead)
        {
            if (unit == null || lead == null || lead.Tf == null ||
                lead.State != CrewWalker.Mode.Standing)
                return false;
            for (int k = 0; k < unit.Hoods.Count; k++)
            {
                var hood = unit.Hoods[k];
                if (hood == null || hood == lead || hood.Dead || hood.Tf == null || hood.Riding)
                    continue;
                if (hood.State != CrewWalker.Mode.Standing) return false;
                var gap = hood.Tf.position - lead.Tf.position;
                gap.y = 0f;
                if (gap.sqrMagnitude > TetherNear * TetherNear) return false;
            }
            return true;
        }

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
                    if (man.State != CrewWalker.Mode.Standing) continue;
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
                    var free = WalkObstacles.ClearSpot(man.Tf.position, WalkObstacles.Radius, 6f);
                    if ((free - man.Tf.position).sqrMagnitude < 0.3f * 0.3f) continue;
                    if (DriveTrace.On)
                    {
                        var sb = DriveTrace.Take();
                        DriveTrace.Str(sb, "who", man.DisplayName);
                        DriveTrace.Num(sb, "moved", Vector3.Distance(free, man.Tf.position));
                        DriveTrace.Vec(sb, "from", man.Tf.position);
                        DriveTrace.Row("unstuck", sb.ToString());
                    }
                    man.OrderToPoint(free);
                    _unsticking.Add(man);
                }

                if (unit.Wiped || unit.IsPolice || unit.TargetUnit != null || unit.Boarding != null) continue;
                var lead = unit.Boss != null && !unit.Boss.Dead ? unit.Boss : Standing(unit);
                if (lead == null || lead.Tf == null || IsAboard(lead) || lead.Riding || lead.Panicked) continue;
                var leadAnchor = lead.HasOrder ? lead.Destination : lead.Tf.position;
                float worst = 0f;
                for (int k = 0; k < unit.Hoods.Count; k++)
                {
                    var man = unit.Hoods[k];
                    if (man == null || man == lead || man.Dead || man.Tf == null) continue;
                    if (IsAboard(man) || man.Riding || man.Panicked || man.Target != null) continue;
                    man.SetPace(1f);   // yesterday's dawdle does not outlive its reason
                    if (OnRaid(man)) continue;   // walking to the machine, or home from it: the raid drives him
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
                        var stray = man.Destination - leadAnchor;
                        stray.y = 0f;
                        var atLead = man.Destination - lead.Tf.position;
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
                        man.OrderAcross(WalkObstacles.ClearSpot(
                            lead.Tf.position + lead.Tf.rotation * FormationOffset(unit.CrewId, k),
                            WalkObstacles.Radius));
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
                        var strayW = man.Destination - lead.Tf.position;
                        strayW.y = 0f;
                        bool foreign = strayW.sqrMagnitude > 12f * 12f;
                        if (foreign && lead.HasOrder)
                        {
                            var errand = man.Destination - leadAnchor;
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
            if (!FreeRoam && man.OnGraph && lead.OnGraph && lead.CurrentLink != null && !lead.CurrentLink.Gated)
            {
                var link = lead.CurrentLink;
                float t = FormationT(link, lead.CurrentT, unit.CrewId, k);
                var slot = Vector3.Lerp(link.From.Pos, link.To.Pos, t / Mathf.Max(link.Length, 0.01f));
                var pull = slot - man.Tf.position;
                pull.y = 0f;
                if (pull.sqrMagnitude > 2f * 2f)
                {
                    Reseat(man);
                    man.OrderTo(link, t);
                    walked = true;
                }
            }
            if (!walked)
            {
                var spot = WalkObstacles.ClearSpot(
                    lead.Tf.position + lead.Tf.rotation * FormationOffset(unit.CrewId, k), WalkObstacles.Radius);
                man.OrderAcross(spot);
            }
            // A RUN BELONGS TO THE CREW. Every order clears the last one's urgency, and
            // this is an order - so a hood hauled back into place while his crew was
            // running dropped to a walk on the spot and never caught it again. He
            // inherits the lead's: running crew, running man; walking crew, walking man.
            man.Urgent = lead.Urgent;
            man.Hustle = hustle;
        }

        /// <summary>Metres two men keep between them on open ground - shoulder room.</summary>
        const float Elbow = 1.0f;

        readonly List<CrewWalker> _standing = new List<CrewWalker>();

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
                bool aOn = _standing[i].HasOrder;
                for (int j = i + 1; j < _standing.Count; j++)
                {
                    bool bOn = _standing[j].HasOrder;
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
                var tacticalHoods = 0;
                foreach (int id in crew.HoodIds)
                {
                    var hood = roster.Find(id);
                    if (hood != null && hood.Status == CharacterStatus.Active &&
                        tacticalHoods < Crew.MaxTacticalHoods)
                    {
                        wanted[id] = (crew, false);
                        tacticalHoods++;
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
                if (unit.Faction == 0)
                    foreach (var man in unit.All()) previousUnitOf[man] = unit;

            var liveUnits = new List<Unit>();
            foreach (var crew in roster.Crews)
            {
                if (!wanted.TryGetValue(crew.LieutenantId, out var w) || w.crew != crew) continue;
                var unit = Units.Find(u => u.Faction == 0 && u.CrewId == crew.Id)
                           ?? new Unit { CrewId = crew.Id, Faction = 0, GangName = OutfitNames.Player, Bombs = BombsPerCrew };
                unit.CommandParentId = crew.LieutenantId;
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

            if (!_initialPlayerSelectionMade)
            {
                foreach (var unit in liveUnits)
                {
                    if (unit.Boss == null || unit.Boss.Dead)
                        continue;
                    _initialPlayerSelectionMade = true;
                    if (Selected == null)
                        Selected = unit;
                    break;
                }
            }

            BindCars(roster);
            BindBikes(roster);
            BindBombs(roster);
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
                    unit.CrewId, unit.CommandParentId, ids));
            }
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
                var seatCar = CarAboard(man, out int seatHad); // recast in his seat: he keeps it
                RemoveMan(id);
                float pace = boss ? BossPace : HoodPace();
                man = FreeRoam ? SpawnMember(member, pos, rot, pace) : SpawnMember(member, link, t, pace);
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
                if (FreeRoam)
                {
                    var rot = Quaternion.LookRotation(_outfitFacing);
                    Vector3 pos;
                    if (unit.Car != null && unit.Car.Tf != null)
                        pos = KerbSideOf(unit.Car);
                    else if (unit.Boss != null)
                        pos = unit.Boss.Tf.position + unit.Boss.Tf.rotation * FormationOffset(unit.CrewId, unit.Hoods.Count);
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
                        t = FormationT(link, unit.Boss.CurrentT, unit.CrewId, unit.Hoods.Count);
                    }
                    else
                    {
                        // outside the outfit's own door, like a rival capo's crew stands
                        // outside his - only where the scene stands no fronts does a crew
                        // still open up on a corner of its own
                        link = FrontSpawnLink(unit, out t);
                        if (link == null)
                        {
                            link = PickSpawnLink();
                            t = link.Length * 0.5f;
                        }
                    }
                    man = SpawnMember(member, link, t, boss ? BossPace : HoodPace());
                }
                if (man == null) return;
                _byCharacter[id] = man;
            }

            man.IsLieutenant = boss;
            man.DisplayName = member.FullName;
            man.Faction = 0;
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

        /// <summary>The pavement outside the outfit's own door: the sidewalk link
        /// nearest the front, at the spot straight out from the doorstep - crews in
        /// a row along it, in book order, a spread apart, the same row the empty
        /// floor deals (OutfitSpawnPoint). Null when the scene stands no fronts (the
        /// demo streets), or no pavement runs anywhere near the door.</summary>
        PedLink FrontSpawnLink(Unit unit, out float t)
        {
            t = 0f;
            var front = PlayerFront();
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

            int index = 0, count = 0;
            foreach (var u in Units)
            {
                if (u.Faction != 0) continue;
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
            float beat = HoodBeat();
            if (FreeRoam)
            {
                var facing = boss.HasOrder ? (boss.Destination - boss.Tf.position) : boss.Tf.forward;
                facing.y = 0f;
                var rot = Quaternion.LookRotation(facing.sqrMagnitude > 1e-3f ? facing.normalized : Vector3.forward);
                var spot = WalkObstacles.ClearSpot(
                    boss.Destination + rot * FormationOffset(unit.CrewId, k), WalkObstacles.Radius);
                if ((hood.Tf.position - spot).sqrMagnitude > 0.35f * 0.35f)
                    hood.OrderToPoint(spot, beat);
                return;
            }
            Reseat(hood);
            if (boss.HasOrder)
            {
                hood.OrderTo(boss.DestinationLink, FormationT(boss.DestinationLink, boss.DestinationT, unit.CrewId, k), beat);
                return;
            }
            var link = boss.CurrentLink;
            if (link == null || link.Gated) return;
            float t = FormationT(link, boss.CurrentT, unit.CrewId, k);
            // freshly dealt in on his spot already - no need to shuffle
            if (hood.CurrentLink == link && Mathf.Abs(hood.CurrentT - t) < 0.35f) return;
            hood.OrderTo(link, t, beat);
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

        CrewWalker SpawnMember(Character member, PedLink link, float t, float pace)
        {
            var prefab = CastFor(member);
            if (prefab == null) return null;
            var go = Body(prefab, member.FullName, member.Id, PedestrianAnthropometry.GangSalt,
                out var anthropometry);
            var man = new CrewWalker
                { Speed = pace, CharacterId = member.Id, SourcePrefab = prefab,
                  FirearmsHalfSteps = member.GetHalfSteps(CharacterAttribute.Firearms),
                  Anthropometry = anthropometry };
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
            var go = Body(prefab, member.FullName, member.Id, PedestrianAnthropometry.GangSalt,
                out var anthropometry);
            var man = new CrewWalker
                { Speed = pace, CharacterId = member.Id, SourcePrefab = prefab,
                  FirearmsHalfSteps = member.GetHalfSteps(CharacterAttribute.Firearms),
                  Anthropometry = anthropometry };
            man.InitAt(go.transform, CrewKit.Draw(_clips, _variety), Clear(pos, member.FullName), rot);
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
            man.InitAt(go.transform, CrewKit.Draw(_clips, _variety),
                afoot ? Clear(pos, name) : pos, rot);
            man.Fired = OnFired;
            man.RangeFactor = Random.Range(0.55f, 0.85f);
            man.SetJog(Random.Range(2.7f, 3.5f));
            return man;
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

    /// <summary>The names the arena prints - the outfit's from the gang catalogue.</summary>
    static class OutfitNames
    {
        public static string Player => LivingCity.Gangs.GangCatalog.Names[LivingCity.Gangs.GangCatalog.PlayerGangId];
    }
}
