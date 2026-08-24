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
    public partial class DemoCrews : MonoBehaviour
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
        const float PropCoverMinHalf = 0.22f; // slimmer than this on its short side is a post, not cover
        const float PropCoverMaxHalf = 3f;    // wider than this is a wall or a lot, not furniture
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
            CrewWalker.FindCover = CoverNear;
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

            var boss = SpawnAt(bossPrefab, bossName, _rivalIds--, anchor, rot, BossPace);
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
                var hood = SpawnAt(prefab, hoodNames[k], _rivalIds--, pos, rot, HoodPace());
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
            ReportDeaths();
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
        /// he will stand, or false when nothing is selected.
        ///
        /// <paramref name="run"/> is the player asking for it twice (CrewOverlay's
        /// double right click) and is the ONLY thing that puts a crew into a run over
        /// an order. Left off, and off is the default, the crew walks - which is what
        /// it did before there was a run at all.</summary>
        public bool OrderSelected(Vector3 world, out Vector3 destination, bool run = false)
        {
            destination = world;
            if (Selected == null || Selected.Boss == null || Selected.Boss.Dead) return false;
            Selected.TargetUnit = null;
            Selected.OrderedAt = Time.time;

            // half in the car - the first men in their seats, the rest still walking to
            // their doors: the drive waits for them. The order is kept and the car goes
            // the moment the last man is in (TickCars).
            if (Selected.Boarding != null && Selected.Car == Selected.Boarding && StillBoarding(Selected))
            {
                world.y = Selected.Car.RoadY;
                Selected.PendingDrive = world;
                destination = world;
                return true;
            }
            Unboard(Selected, "a walk order"); // a walk order cancels a walk to the car
            Selected.PendingDrive = null;

            // in the car: the car goes there, the crew with it - unless the man at the
            // wheel is dead, when the order is to get out
            if (Selected.Car != null)
            {
                if (DriverDead(Selected.Car))
                {
                    Disembark(Selected);
                    destination = Selected.Car.Position;
                    return true;
                }
                world.y = Selected.Car.RoadY;
                Selected.Leaving = false;
                Selected.Car.DriveTo(world);
                destination = world;
                return true;
            }

            if (FreeRoam)
            {
                world = WalkObstacles.ClampToCity(world);
                world.y = GroundY;
                var boss = Selected.Boss;
                var dir = world - boss.Tf.position;
                dir.y = 0f;
                var rot = Quaternion.LookRotation(dir.sqrMagnitude > 0.25f ? dir.normalized : boss.Tf.forward);
                boss.OrderToPoint(world);
                boss.Urgent = run;   // asked for twice on the bare floor too
                boss.Post = world;
                for (int k = 0; k < Selected.Hoods.Count; k++)
                {
                    var hood = Selected.Hoods[k];
                    hood.OrderToPoint(WalkObstacles.ClearSpot(
                        world + rot * FormationOffset(Selected.CrewId, k), WalkObstacles.Radius), HoodBeat());
                    hood.Urgent = run;
                }
                destination = world;
                return true;
            }

            if (!NearestSidewalk(world, out var link, out float t)) return false;
            Dispatch(Selected, link, t, run);
            destination = Selected.Boss.Destination;
            Selected.Boss.Post = destination;
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
        public bool MarchTo(Unit unit, Vector3 world, bool run = false)
        {
            if (unit == null) return false;
            // A CREW WHOSE LIEUTENANT IS DOWN IS STILL A CREW. His hoods are on their
            // feet and they can still be sent - somebody at the front picks up the walk.
            // Refusing the order because the man who used to give it is dead left three
            // hoods standing in the street for the rest of the run.
            var boss = unit.Boss != null && !unit.Boss.Dead ? unit.Boss : Standing(unit);
            if (boss == null || boss.Tf == null) return false;
            unit.TargetUnit = null;
            unit.OrderedAt = Time.time;
            Unboard(unit, "a march order");
            unit.PendingDrive = null;
            world = WalkObstacles.ClampToCity(world);
            world.y = GroundY;
            var dir = world - boss.Tf.position;
            dir.y = 0f;
            var rot = Quaternion.LookRotation(dir.sqrMagnitude > 0.25f ? dir.normalized : boss.Tf.forward);
            Reseat(boss);
            boss.OrderAcross(world);
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
                    world + rot * FormationOffset(unit.CrewId, k), WalkObstacles.Radius), HoodBeat());
                man.Urgent = run;
            }
            return true;
        }

        /// <summary>The first man of this crew still on his feet - who leads it when the
        /// lieutenant is down.</summary>
        static CrewWalker Standing(Unit unit)
        {
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Tf != null) return man;
            return null;
        }

        /// <summary>A new man for this crew, off the ledger's recruiting door: paid for
        /// out of the safe, dealt onto the books, and he walks in beside his boss on
        /// the next deal. Refused - crew full, no money - with the reason kept for the bar.</summary>
        public bool Recruit(Unit unit)
        {
            LastRefusal = null;
            if (unit == null || unit.Faction != 0) return false;
            var director = PersonnelDirector.Instance;
            if (director == null || director.Roster == null) return false;
            var result = director.Recruit(unit.CrewId, out _);
            if (!result.Ok)
            {
                LastRefusal = result.Reason;
                Debug.Log("[Crews] Recruit refused: " + result.Reason);
            }
            return result.Ok;
        }

        /// <summary>Why the last recruit was refused, or null.</summary>
        public string LastRefusal { get; private set; }

        /// <summary>Send the selected crew at that one: every man closes and shoots.</summary>
        public bool OrderAttack(Unit target)
        {
            if (Selected == null || target == null || target == Selected || target.Wiped) return false;
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

        void Dispatch(Unit unit, PedLink link, float t, bool run = false)
        {
            // a man who walked off his stretch (to a car door he never got into) sets
            // off from where he stands
            if (!unit.Boss.Riding)
            {
                Reseat(unit.Boss);
                unit.Boss.OrderTo(link, t);
                // THIS is how a player sends a crew anywhere in this town, and it is a
                // walk down the pavements on purpose - the men keep their lanes and
                // their formation. It stays a walk unless he asked twice, and then it
                // is run most of the way and walked the last few metres in
                // (CrewWalker.GearGraphWalk) - the sidewalks can run, but only when
                // told to.
                unit.Boss.Urgent = run;
            }
            for (int k = 0; k < unit.Hoods.Count; k++)
            {
                // the two away on the machine keep their own order (the raid's); a walk
                // given to the crew is given to the crew that is standing in the street
                if (unit.Hoods[k].Riding) continue;
                Reseat(unit.Hoods[k]);
                unit.Hoods[k].OrderTo(link, FormationT(link, t, unit.CrewId, k), HoodBeat());
                unit.Hoods[k].Urgent = run;
            }
        }

        /// <summary>The beat a hood waits before he follows an order the boss got - each
        /// his own, so a crew steps off one man after another, not as one machine.</summary>
        static float HoodBeat() => Random.Range(0.15f, 0.9f);

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

        // ------------------------------------------------------------------ cars

        /// <summary>Stand a body for the ledger's car: parked here, bound to the
        /// roster's first vehicle on the next deal, owned by whoever the book says.</summary>
        public CrewCar AddCar(GameObject prefab, Vector3 position, Quaternion rotation, float roadY)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, new Vector3(position.x, roadY, position.z), rotation);
            go.name = "Outfit Car";
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            // the roads it drives: the scene's network (or the one the builder set as
            // active); off any road it stands on open ground
            var car = new CrewCar { RoadY = roadY, Net = Net ?? LaneNet.Active };
            car.Attach(go.transform); // reads the body: seats, doors, wheels, size; onto the road under it
            StreetTraffic.Users.Add(car);
            Cars.Add(car);
            return car;
        }

        /// <summary>The car this crew owns per the ledger, or null.</summary>
        public CrewCar CarOf(Unit unit)
        {
            if (unit == null) return null;
            foreach (var car in Cars) if (car.Owner == unit) return car;
            return null;
        }

        /// <summary>The car this man is riding in, or null.</summary>
        public CrewCar CarWith(CrewWalker man)
        {
            if (man == null) return null;
            foreach (var car in Cars) if (car.Aboard.Contains(man)) return car;
            return null;
        }

        /// <summary>Riding in a car - hidden, carried, firing from a window if at all.</summary>
        public bool IsAboard(CrewWalker man)
        {
            if (man == null) return false;
            foreach (var car in Cars) if (car.Aboard.Contains(man)) return true;
            return false;
        }

        /// <summary>Is any man of this crew who was given a seat still on his way to it?</summary>
        bool StillBoarding(Unit unit)
        {
            var car = unit?.Boarding;
            if (car == null) return false;
            foreach (var man in unit.All())
                if (!man.Dead && !car.Aboard.Contains(man) && car.SeatOf.ContainsKey(man)) return true;
            return false;
        }

        /// <summary>Why the last car order was refused, or null - the overlay's line.</summary>
        public string CarRefusal { get; private set; }

        /// <summary>A vehicle the OUTFIT owns - one the book put on the street, as
        /// against a rival's, the law's or one a scene stood. What "ours" means to a
        /// click: the keys can be handed over, and the charge cannot be laid.</summary>
        public bool OnTheBooks(CrewCar car) => car != null && !car.Civic && car.ItemId >= 0;

        /// <summary>The keys to one of the outfit's cars, handed to the selected crew's
        /// lieutenant - whoever held them before.
        ///
        /// The ledger does the handing (PersonnelDirector.MoveEquipment): the book is
        /// the truth and the street follows it, so the change arrives back here through
        /// the next deal, which re-owns the car and turns anybody else's men out of it
        /// (BindCars). Nothing is moved - the car is parked outside the front either
        /// way, and the crew walks to it.</summary>
        public bool AssignCar(CrewCar car)
        {
            CarRefusal = null;
            if (Selected == null || car == null) return false;
            if (Selected.Faction != 0) return false;
            if (car.Civic) { CarRefusal = "That is a police car"; return false; }
            if (car.ItemId < 0) { CarRefusal = "That car is not on the books"; return false; }

            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            if (roster == null) { CarRefusal = "No ledger to sign"; return false; }
            var crew = roster.FindCrew(Selected.CrewId);
            if (crew == null) { CarRefusal = "That crew is not in the book"; return false; }

            var result = director.MoveEquipment(car.ItemId, crew.LieutenantId);
            if (!result.Ok)
            {
                CarRefusal = string.IsNullOrEmpty(result.Reason) ? "The keys stay where they are"
                                                                : result.Reason;
                return false;
            }
            return true;
        }

        /// <summary>The selected crew and this car: get in if it is theirs and they are
        /// out; get out if they are in. Anyone else's car refuses, and says whose.</summary>
        public bool OrderCar(CrewCar car)
        {
            CarRefusal = null;
            if (Selected == null || car == null) return false;
            if (car.Owner != Selected)
            {
                CarRefusal = car.Civic ? "That is a police car"
                    : car.Owner == null
                    ? "Nobody's car - assign it in the ledger"
                    : "That is " + Surname(car.Owner.Name) + "'s car";
                return false;
            }
            if (Selected.Car == car)
            {
                Disembark(Selected);
                return true;
            }
            if (car.Occupant != null && car.Occupant != Selected)
            {
                CarRefusal = "The car is taken";
                return false;
            }
            Board(Selected, car);
            return true;
        }

        /// <summary>The selected crew out of its car, wherever the pointer is - the
        /// middle button's order: the car pulls in and lets them out; a crew still
        /// walking to its doors turns back. False when there is no car in it.</summary>
        public bool OrderOut()
        {
            if (Selected == null) return false;
            if (Selected.Car != null)
            {
                Disembark(Selected);
                return true;
            }
            var car = Selected.Boarding;
            if (car == null) return false;
            // nobody in yet: the walk to the doors is called off, each man where he stands
            Unboard(Selected, "an order");
            Selected.PendingDrive = null;
            foreach (var man in Selected.All())
                if (!man.Dead && !IsAboard(man) && car.SeatOf.ContainsKey(man))
                {
                    car.SeatOf.Remove(man);
                    man.OrderToPoint(man.Tf.position);
                }
            if (car.Occupant == null) car.CloseAllDoors();
            return true;
        }

        static string Surname(string full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            int cut = full.LastIndexOf(' ');
            return cut >= 0 ? full.Substring(cut + 1) : full;
        }

        /// <summary>The dispatcher's calls on the same car plumbing: put this unit in
        /// this car (they walk to their doors), let it out at the kerb, and set the
        /// law on a crew.</summary>
        public void BoardCar(Unit unit, CrewCar car) => Board(unit, car);
        public void LeaveCar(Unit unit) => Disembark(unit);
        public void Sic(Unit unit, Unit target) { if (unit != null && target != null && !target.Wiped) SetTarget(unit, target, ordered: true); }

        // As many as there are seats walk each to HIS door - the lieutenant drives,
        // so he goes round to the driver's side; the rest to the nearest free seat's
        // door - and get in when they reach it and it stands open (TickCars); the
        // rest stay on the pavement. A crew already in a fight lowers its guns.
        /// <summary>Give up a walk to the car - and say who did, because a crew that
        /// silently stops walking to its own doors is a very hard thing to see.</summary>
        void Unboard(Unit unit, string why)
        {
            if (unit == null || unit.Boarding == null) return;
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", unit.GangName);
                DriveTrace.Str(sb, "what", "walk to the car called off: " + why);
                DriveTrace.Int(sb, "aboard", unit.Boarding.Aboard.Count);
                DriveTrace.Int(sb, "seats", unit.Boarding.SeatOf.Count);
                DriveTrace.Row("crewcar", sb.ToString());
            }
            unit.Boarding = null;
        }

        /// <summary>Send a man to the door of his seat.
        ///
        /// Which order that is depends on the ground. On the free floor a straight walk
        /// is all there is. In a city the sidewalks ARE the way, and a door a hundred
        /// metres off round two corners is not something a man walks to in a straight
        /// line: he sets off, the first building stops him dead, the leg gives up after
        /// eight seconds of getting no nearer, and he stands there for the rest of the
        /// run while the crew waits for a car nobody ever reaches. (Two of three men,
        /// stood 115 m from their doors for 148 seconds, in the run that found this.)
        /// Close in - across the pavement to the handle - the straight leg is right, and
        /// the graph would only walk him past it.</summary>
        void SendToDoor(CrewWalker man, Vector3 door, float delay = 0f, bool graph = false)
        {
            var gap = door - man.Tf.position;
            gap.y = 0f;
            if ((graph || gap.sqrMagnitude > 8f * 8f) && !FreeRoam && man.OnGraph &&
                gap.sqrMagnitude > 3f * 3f && NearestSidewalk(door, out var link, out float t))
            {
                Reseat(man);
                man.OrderTo(link, t, delay);
                return;
            }
            man.OrderToPoint(door, delay);
        }

        /// <summary>How many times a man has been sent to his door and not got there.
        /// Neither kind of order can be the only one tried: a straight leg wedges against
        /// whatever is between him and the handle (a man stood seven metres from his own
        /// car for 47 seconds, on the same paving stone, being sent again every second),
        /// and the graph puts him on the nearest pavement, which need not be the one the
        /// car is at. So they alternate, and one of the two always gets him there.</summary>
        readonly Dictionary<CrewWalker, int> _doorTries = new Dictionary<CrewWalker, int>();

        void Board(Unit unit, CrewCar car)
        {
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", unit.GangName);
                DriveTrace.Str(sb, "what", "told to get in");
                DriveTrace.Int(sb, "standing", unit.Standing());
                DriveTrace.Int(sb, "aboard", car.Aboard.Count);
                DriveTrace.Int(sb, "seats", car.SeatOf.Count);
                DriveTrace.Bool(sb, "driverdead", DriverDead(car));
                DriveTrace.Bool(sb, "occupied", car.Occupant != null);
                DriveTrace.Row("crewcar", sb.ToString());
            }
            unit.TargetUnit = null;
            unit.OrderedAt = Time.time;
            unit.Boarding = car;
            unit.Leaving = false;
            int given = 0;
            foreach (var man in unit.All())
            {
                if (man.Dead || IsAboard(man) || man.Riding) continue;
                // a seat he was already given (a walk to it cut short) is still his
                if (!car.SeatOf.TryGetValue(man, out int seat)) seat = car.FreeSeat();
                if (seat < 0) break;
                car.SeatOf[man] = seat;
                man.Disengage();
                _doorTries.Remove(man);
                SendToDoor(man, car.DoorPoint(seat));
                given++;
            }
            if (given == 0 && car.SeatOf.Count == 0) Unboard(unit, "nobody could be given a seat");
        }

        // The crew gets out - once the car has pulled in at the kerb and the doors
        // are open (TickCars): a moving car lets nobody out, and a car of the outfit's
        // does not stand in the road while they climb down.
        void Disembark(Unit unit)
        {
            var car = unit.Car;
            if (car == null) return;
            unit.Leaving = true;
            Unboard(unit, "told to get out");
            unit.PendingDrive = null;
            // "Out" is an order to the MEN, and the car does the least driving that can
            // honour it. In a fight it is an emergency - both feet on the brake where it
            // stands, and out into the road (a car is a coffin when the shooting starts).
            // Otherwise it pulls in at the nearest kerb it can actually reach - the
            // stopping distance from here, no further, no turning round - and lets them
            // down onto the pavement: a car of the outfit's is never left in the road.
            if (!car.Moving) return;
            if (car.Hot || car.State == CrewCar.Mode.DriveBy) car.HardStop();
            else car.ParkNear(car.Position);
        }

        // One rider out through his own door onto the ground beside it, standing,
        // facing away from the car. The dead ride out too - a man shot in the car is
        // left by it.
        void LetOut(CrewCar car, CrewWalker man, int seat)
        {
            if (DriveTrace.On)
                DriveTrace.Event("crewcar", man.DisplayName, $"set down out of seat {seat}");
            car.Aboard.Remove(man);
            car.SeatOf.Remove(man);
            var spot = car.DoorPoint(seat);
            if (WalkObstacles.Occupied(spot, WalkObstacles.Radius))
                spot = WalkObstacles.FreeSpot(spot, WalkObstacles.Radius, 3f);
            spot.y = GroundY;
            if (man.Tf)
            {
                man.SetRiding(false);
                if (man.IsLieutenant) man.Post = spot;
                man.Tf.SetPositionAndRotation(spot,
                    Quaternion.LookRotation(car.Tf.right * CrewCar.SeatSide(seat), Vector3.up));
                // in the city he is streets from the stretch he got in on: his next
                // order must start from this kerb, not snap him back to that one
                Reseat(man);
            }
        }

        /// <summary>In the city, a man stood off the sidewalk stretch the graph still
        /// has him on (set down out of a car, walked to a door) is put onto the nearest
        /// stretch where he stands. On the free floor there is no graph: nothing to do.</summary>
        void Reseat(CrewWalker man)
        {
            if (FreeRoam || man == null || man.Tf == null || !man.OnGraph || man.Dead || man.Riding) return;
            // walking the graph he IS where it has him; and a man within a pavement's
            // width of his metre is only on his own side of the walk
            if (man.State == CrewWalker.Mode.Walking || man.State == CrewWalker.Mode.Homing) return;
            var cur = man.CurrentLink;
            var here = Vector3.Lerp(cur.From.Pos, cur.To.Pos, Mathf.Clamp01(man.CurrentT / Mathf.Max(cur.Length, 0.01f)));
            var gap = man.Tf.position - here;
            gap.y = 0f;
            if (gap.sqrMagnitude < 2f * 2f) return;
            if (!NearestSidewalk(man.Tf.position, out var link, out float t)) return;
            man.Reseat(link, t);
        }

        /// <summary>Is the man in the driver's seat of this car dead (or nobody there)?</summary>
        bool DriverDead(CrewCar car)
        {
            foreach (var kv in car.SeatOf)
                if (kv.Value == 0) return kv.Key.Dead;
            return false;
        }

        // Orphaned cars are towed. A car whose engine was shot out, or whose whole
        // crew is dead, never moves again - and it stands where the fight left it:
        // in a lane, or pinched against a junction mouth, where the traffic queued
        // behind it for the rest of the run (340-409 s, measured twice). The city
        // takes its bodies away; it takes the tin they died around too.
        readonly Dictionary<CrewCar, float> _derelictFor = new Dictionary<CrewCar, float>();
        const float TowAfter = 45f;

        void TickCars(float dt)
        {
            for (int c = Cars.Count - 1; c >= 0; c--)
            {
                var car = Cars[c];
                bool orphaned = !car.Civic && car.Tf != null &&
                                (car.EngineDead || (car.Owner != null && car.Owner.Wiped));
                // nobody aboard, dead or alive: the dead are carried out first
                // (ReportDeaths empties the seats), the living keep their car
                if (!orphaned || car.Aboard.Count > 0) { _derelictFor.Remove(car); continue; }
                _derelictFor.TryGetValue(car, out float still);
                still += dt;
                _derelictFor[car] = still;
                if (still < TowAfter) continue;
                if (DriveTrace.On)
                    DriveTrace.Event("crewcar", car.DisplayName,
                        car.EngineDead ? "towed: the engine is gone" : "towed: its crew is dead");
                _derelictFor.Remove(car);
                StreetTraffic.Users.Remove(car);
                if (car.Tf) Destroy(car.Tf.gameObject);
                Cars.RemoveAt(c);
            }

            foreach (var car in Cars)
            {
                // the crew aboard is in a fight - a drive-by, or shot at on the way
                // somewhere: the driver puts his foot down (the law drives its own way)
                var fight = car.Civic ? null : FightOf(car);
                car.Hot = fight != null;
                car.Tick(dt);

                // THE ENGINE HAS STOPPED. A car that will not move is a tin box with
                // men sitting in it being shot at, which is the worst place on the
                // street: they get out and fight on foot, exactly as they do when the
                // driver is hit.
                if (car.TakeEngineDeath())
                {
                    CrewOverlay.Announce("THE ENGINE'S GONE", 4f, new Color(1f, 0.6f, 0.35f));
                    if (DriveTrace.On)
                        DriveTrace.Event("crewcar", car.DisplayName,
                            $"engine dead after {car.EngineHits} rounds into the bonnet");
                    if (car.Occupant != null)
                    {
                        car.Occupant.Leaving = true;
                        Unboard(car.Occupant, "the engine has gone");
                    }
                }

                // the man at the wheel shot: nobody is driving - the car rolls to a stop
                // where it is and the crew gets out of it (and, out, fights on or runs)
                if (car.Occupant != null && !car.Occupant.Leaving && car.Moving && DriverDead(car))
                {
                    if (DriveTrace.On)
                    {
                        var sb = DriveTrace.Take();
                        DriveTrace.Str(sb, "who", car.Occupant.GangName);
                        DriveTrace.Str(sb, "what", "driver down - bailing out");
                        DriveTrace.Int(sb, "aboard", car.Aboard.Count);
                        DriveTrace.Int(sb, "seats", car.SeatOf.Count);
                        DriveTrace.Bool(sb, "boarding", car.Occupant.Boarding == car);
                        DriveTrace.Row("crewcar", sb.ToString());
                    }
                    car.Stop();
                    car.Occupant.Leaving = true;
                    Unboard(car.Occupant, "the driver was shot");
                    car.Occupant.DriverLost = true;
                    if (car.Occupant.Faction == 0)
                        CrewOverlay.Announce("DRIVER DOWN - THE CREW IS BAILING OUT", 4f, new Color(1f, 0.55f, 0.45f));
                }

                // men walking up to it: the door for a man's seat swings open as he
                // arrives, he gets in once it stands open, and it shuts behind him
                if (car.Occupant == null || car.Occupant.Boarding == car)
                {
                    foreach (var unit in Units)
                    {
                        if (unit.Boarding != car) continue;
                        bool anyOut = false;
                        foreach (var man in unit.All())
                        {
                            if (man.Dead || car.Aboard.Contains(man)) continue;
                            if (!car.SeatOf.TryGetValue(man, out int seat))
                            {
                                // No seat of his own. He may never have been given one -
                                // the car was full when the order went out - or he may
                                // have LOST one: the ledger recasts a man when his rank
                                // changes (a lieutenant sits for his photograph in a
                                // suit) and the body is swapped on the spot, so the man
                                // walking to the door is a stranger to the car when he
                                // stands up again, with no seat and no order. The last
                                // man of a crew was promoted on his way back and stood
                                // on that pavement for the rest of the run while the job
                                // waited for him. A seat going spare is his.
                                seat = car.FreeSeat();
                                if (seat < 0) { anyOut = true; continue; }
                                car.SeatOf[man] = seat;
                                _doorTries.Remove(man);
                                SendToDoor(man, car.DoorPoint(seat));
                                anyOut = true;
                                continue;
                            }
                            var door = car.DoorPoint(seat);
                            var d = man.Tf.position - door;
                            d.y = 0f;
                            float dist = d.magnitude;
                            // at the door, or stopped short of it by the crowd right
                            // beside it. The reach matches the door-open reach below:
                            // some bodies' door points sit deep enough under the sill
                            // that the walk is stopped by the car's own flank at
                            // 1.8 m exactly - a man the door already opens for was
                            // being re-sent at a handle he could never touch, one
                            // second at a time, for the rest of the run (seed 109).
                            bool atDoor = dist <= 1.9f || (!man.HasOrder && dist <= 2.8f);
                            // hand on the handle, not from across the road - but a door
                            // that will not open for a man who has ARRIVED leaves him
                            // stood beside his own car for the rest of the run
                            if (dist <= 1.8f || atDoor) car.OpenDoorFor(seat);
                            if (DriveTrace.On)
                            {
                                var sb = DriveTrace.Take();
                                DriveTrace.Str(sb, "who", man.DisplayName);
                                DriveTrace.Int(sb, "seat", seat);
                                DriveTrace.Num(sb, "toDoor", dist);
                                DriveTrace.Bool(sb, "order", man.HasOrder);
                                DriveTrace.Bool(sb, "open", car.DoorOpenFor(seat));
                                DriveTrace.Bool(sb, "in", atDoor && car.DoorOpenFor(seat));
                                DriveTrace.Str(sb, "state", man.State.ToString());
                                DriveTrace.Vec(sb, "p", man.Tf.position);
                                DriveTrace.Vec(sb, "door", door);
                                DriveTrace.Row("board", sb.ToString());
                            }
                            if (atDoor && car.DoorOpenFor(seat))
                            {
                                car.Aboard.Add(man);
                                _doorTries.Remove(man);
                                man.Disengage();
                                man.SetRiding(true);
                                car.CloseDoorFor(seat);
                                car.Occupant = unit;
                                unit.Car = car;
                            }
                            else
                            {
                                anyOut = true;
                                // stopped short of it and given no further order - the
                                // graph walk ended at the kerb, the leg gave up against
                                // a wall, the car moved off while he walked: he is sent
                                // again rather than left standing. The order itself is
                                // what times out (the mission gives up on the crew), not
                                // a man quietly deciding he has arrived.
                                if (!man.HasOrder)
                                {
                                    _doorTries.TryGetValue(man, out int tries);
                                    _doorTries[man] = tries + 1;
                                    SendToDoor(man, door, Random.Range(0.2f, 0.6f), graph: (tries & 1) == 1);
                                }
                            }
                        }
                        if (!anyOut)
                        {
                            Unboard(unit, "everybody in");
                            // everybody in: the drive the player ordered while they were
                            // still climbing aboard goes now
                            if (unit.PendingDrive.HasValue && unit.Car == car && !DriverDead(car))
                            {
                                unit.Leaving = false;
                                car.DriveTo(unit.PendingDrive.Value);
                            }
                            unit.PendingDrive = null;
                        }
                    }
                }

                // a crew told to get out waits for the kerb, then each man for his door
                if (car.Occupant != null && car.Occupant.Leaving && !car.Moving)
                {
                    var unit = car.Occupant;
                    var outNow = new List<CrewWalker>();
                    foreach (var man in car.Aboard)
                    {
                        if (!car.SeatOf.TryGetValue(man, out int seat)) { outNow.Add(man); continue; }
                        car.OpenDoorFor(seat);
                        if (car.DoorOpenFor(seat)) outNow.Add(man);
                    }
                    foreach (var man in outNow)
                    {
                        car.SeatOf.TryGetValue(man, out int seat);
                        LetOut(car, man, seat);
                        car.CloseDoorFor(seat);
                    }
                    if (car.Aboard.Count == 0)
                    {
                        unit.Leaving = false;
                        car.SeatOf.Clear();
                        car.CloseAllDoors();
                        car.Occupant = null;
                        unit.Car = null;
                        // out of it well away from the crew it was trading shots with on
                        // the way: that fight is over - the men do not walk back into it
                        if (unit.TargetUnit != null && (unit.TargetUnit.Position - car.Position).sqrMagnitude > 40f * 40f)
                            unit.TargetUnit = null;
                        // out of a car whose driver was shot: the fight goes on from the
                        // road (TickCombat picks it up) - and some men's nerve goes
                        if (unit.DriverLost)
                        {
                            unit.DriverLost = false;
                            var fled = new List<CrewWalker>();
                            foreach (var man in unit.All())
                            {
                                if (man.Dead || man.IsLieutenant || man.Panicked) continue;
                                if (Random.value < HoodNerve * 1.6f) fled.Add(man);
                            }
                            foreach (var man in fled)
                            {
                                man.Flee(car.Position, 15f, 25f, comeBack: true);
                                OnFled(man, car.Position);
                            }
                        }
                    }
                }

                // riders ride, in sight: each in his seat, carried by the car; a rider
                // with his gun out of the window is turned toward what he is shooting at
                foreach (var man in car.Aboard)
                {
                    if (man.Tf == null) continue;
                    car.SeatOf.TryGetValue(man, out int seat);
                    var rot = car.Tf.rotation;
                    if (man.RidingAim && man.Target != null && man.Target.Tf != null)
                    {
                        var to = man.Target.Tf.position - man.Tf.position;
                        to.y = 0f;
                        if (to.sqrMagnitude > 1e-3f) rot = Quaternion.LookRotation(to.normalized, Vector3.up);
                    }
                    man.Tf.SetPositionAndRotation(car.Seat(seat), rot);
                }

                // the guns out of the windows: on a drive-by, at the crew being driven
                // past; on the way anywhere else, or stood at the kerb, at whoever is
                // shooting at the car - the men in it defend themselves without an order
                if (fight != null) TickRiders(car, fight, dt);
                else
                {
                    foreach (var man in car.Aboard) man.RidingAim = false;
                    car.CloseAllWindows();
                }
            }
        }

        // The crew a car's riders have their guns out for: the drive-by's mark, else
        // the crew its occupant is fighting (shot at, it fights back from the seats).
        static Unit FightOf(CrewCar car)
        {
            if (car.State == CrewCar.Mode.DriveBy) return car.DriveByTarget;
            var unit = car.Occupant;
            if (unit == null || unit.TargetUnit == null || unit.TargetUnit.Wiped) return null;
            if (Beaten(unit.TargetUnit)) return null;   // beaten or gone: the guns go in
            return unit.TargetUnit;
        }

        // The mark is finished as far as the car is concerned: every man of it is down
        // or running off the street. A crew does not keep driving passes at a pavement
        // with nobody left stood on it - that is what looks mad from the kerb, and a man
        // who has turned and run is off the board anyway (TakeOffRetreated). Distance is
        // deliberately not in it: a drive-by ordered on a crew at the far end of the
        // street is a long drive, not a finished job.
        static bool Beaten(Unit target)
        {
            foreach (var man in target.All())
                if (!man.Dead && !man.Retreating) return false;
            return true;
        }

        // The pass, and the answer to being shot at: every armed rider with a living
        // man of the target crew inside his gun's reach on HIS side of the car puts
        // the gun out of the window and fires on his own cadence. Same roll and the
        // same wounds as a shot from the pavement - only the muzzle moved.
        readonly Dictionary<CrewWalker, float> _windowTimers = new Dictionary<CrewWalker, float>();
        readonly List<CrewWalker> _mates = new List<CrewWalker>(), _heard = new List<CrewWalker>();

        void TickRiders(CrewCar car, Unit target, float dt)
        {
            if (target == null || target.Wiped || Beaten(target))
            {
                car.TargetDone(); // the job is done: on a little and in at the kerb
                foreach (var man in car.Aboard) man.RidingAim = false;
                car.CloseAllWindows();
                return;
            }
            foreach (var man in car.Aboard)
            {
                car.SeatOf.TryGetValue(man, out int seat);
                if (man.Dead || !man.Armed) { man.RidingAim = false; car.SetWindow(seat, false); continue; }
                var mark = NearestStanding(target, car.Position);
                if (mark == null) { man.RidingAim = false; car.SetWindow(seat, false); continue; }
                var toMark = mark.Tf.position - car.Position;
                toMark.y = 0f;
                float dist = toMark.magnitude;
                // his own window has to face the man - and the man has to be out of the
                // side of it, not ahead through the windscreen: within sixty degrees of
                // abeam. The window winds down while he has the gun out of it.
                float sideOfMark = Vector3.Dot(toMark, car.Tf.right) >= 0f ? 1f : -1f;
                float abeam = dist > 0.1f ? Vector3.Dot(toMark / dist, car.Tf.right * sideOfMark) : 0f;
                bool canSee = dist <= man.Ballistics.Range * RidingReach &&
                              sideOfMark == CrewCar.SeatSide(seat) && abeam > RidingArc;
                if (DriveTrace.On && dist < 60f)
                {
                    var sb = DriveTrace.Take();
                    DriveTrace.Str(sb, "who", man.DisplayName);
                    DriveTrace.Int(sb, "seat", seat);
                    DriveTrace.Bool(sb, "armed", man.Armed);
                    DriveTrace.Num(sb, "dist", dist);
                    DriveTrace.Num(sb, "range", man.Ballistics.Range * RidingReach);
                    DriveTrace.Num(sb, "abeam", abeam, "F2");
                    DriveTrace.Num(sb, "side", sideOfMark, "F0");
                    DriveTrace.Num(sb, "seatside", CrewCar.SeatSide(seat), "F0");
                    DriveTrace.Bool(sb, "fires", canSee);
                    DriveTrace.Str(sb, "at", mark.DisplayName);
                    DriveTrace.Row("rider", sb.ToString());
                }
                man.RidingAim = canSee;
                man.AimAt(canSee ? mark : null);
                car.SetWindow(seat, canSee);
                if (!canSee) { _windowTimers[man] = 0f; continue; }

                _windowTimers.TryGetValue(man, out float timer);
                timer -= dt;
                if (timer > 0f) { _windowTimers[man] = timer; continue; }
                _windowTimers[man] = man.Ballistics.Interval;
                Resolve(man, mark, man.MuzzlePosition, car.Position, CrewArms.MuzzleOf(man.Weapon) ?? car.Tf);
            }
        }

        // The ledger's car: bound to the first vehicle on the books, owned by the crew
        // whose lieutenant the book has assigned it to (a hood may hold the keys - the
        // lieutenant deals his crew's wheels like its guns - but the crew is his).
        void BindCars(Roster roster)
        {
            foreach (var car in Cars)
            {
                if (car.Civic) continue;
                // A car the BOOK never stood is none of the book's business: one taken
                // off the street, or one a scene put down, keeps whoever it was given to.
                // Claiming it here bound it to whatever vehicle the roster happened to
                // list first, found the owner did not match, and threw the crew out of
                // its own car the moment the first man sat down - which is exactly what
                // the lab watched, twice, and could not explain until the trace said so.
                if (car.ItemId < 0 && !_ledgerCars.Contains(car)) continue;
                RosterEquipment item = null;
                foreach (var e in roster.Equipment)
                {
                    if (e.Kind != EquipmentKind.Vehicle) continue;
                    if (car.ItemId < 0 || e.Id == car.ItemId) { item = e; break; }
                }
                if (item == null) { car.Owner = null; continue; }
                car.ItemId = item.Id;
                car.DisplayName = string.IsNullOrEmpty(item.DisplayName) ? "Car" : item.DisplayName;

                int keeper = item.OwnerId >= 0 ? item.OwnerId : item.HolderId;
                Unit owner = null;
                if (keeper >= 0)
                {
                    var crew = roster.CrewOf(keeper);
                    if (crew != null)
                        owner = Units.Find(u => u.Faction == 0 && u.CrewId == crew.Id);
                }
                if (owner != car.Owner && car.Occupant != null && car.Occupant != owner)
                    Disembark(car.Occupant); // the book took the keys away mid-ride
                car.Owner = owner;
            }

            StandLedgerCars(roster);
        }

        /// <summary>Which crew's man drives a vehicle on the books, or null.</summary>
        Unit OwnerFor(Roster roster, RosterEquipment item)
        {
            int keeper = CrewCars.KeeperOf(item);
            if (keeper < 0) return null;
            var crew = roster.CrewOf(keeper);
            return crew == null ? null : Units.Find(u => u.Faction == 0 && u.CrewId == crew.Id);
        }

        /// <summary>The outfit's own door - the kerb every car on the books is parked
        /// at. Null before the families are seated, and in the demo scenes that stand no
        /// fronts at all; the caller then falls back to the man who holds the keys.</summary>
        public static GangFront PlayerFront()
        {
            var all = GangFront.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].GangId == LivingCity.Gangs.GangCatalog.PlayerGangId)
                    return all[i];
            return null;
        }

        /// <summary>The cars the outfit OWNS, standing outside its own premises.
        ///
        /// A car on the armory page is a line in a book, and the moment the book lists
        /// it the body is at the kerb outside the front - every one of them, whether the
        /// keys are in a lieutenant's pocket, on the front's desk or still in the safe.
        /// The outfit parks at home; who drives what is the ledger's business and no
        /// longer the street's, so dealing a car to another man does not move it.
        /// Sell it and it is gone from the street too: the book is the truth.
        ///
        /// The queue forms itself - KerbSlotNear walks OUT from the door, nearest free
        /// length first, and skips whatever is already standing there, so the second car
        /// lands behind the first rather than inside it.
        ///
        /// A car the SCENE stood (the crew demo's own) is nobody's business here - it
        /// binds to the book the old way and is never taken away.</summary>
        void StandLedgerCars(Roster roster)
        {
            if (!LedgerCarsStand) return;

            // struck off the books - sold, or lost: off the street. The keys changing
            // hands no longer takes a car anywhere; it was never standing beside a man.
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                var car = Cars[i];
                if (!_ledgerCars.Contains(car)) continue;
                bool onBooks = false;
                foreach (var e in roster.Equipment)
                    if (e.Id == car.ItemId && e.Kind == EquipmentKind.Vehicle) { onBooks = true; break; }
                if (!onBooks) DropCar(car);
            }

            var front = PlayerFront();
            foreach (var item in roster.Equipment)
            {
                if (item.Kind != EquipmentKind.Vehicle) continue;
                if (Cars.Exists(c => c.ItemId == item.Id)) continue;   // already on the street

                // outside the outfit's door. Only where there is no door at all - a
                // demo street with no fronts on it - does the old rule stand in: beside
                // the man with the keys, or his lieutenant if the hood is not out.
                CrewWalker man = null;
                Vector3 anchor;
                if (front != null) anchor = front.Door;
                else
                {
                    int keeper = CrewCars.KeeperOf(item);
                    if (keeper < 0) continue;                   // in the lock-up, nobody's
                    if (!_byCharacter.TryGetValue(keeper, out man) || man == null || man.Dead || !man.Tf)
                    {
                        var unit = OwnerFor(roster, item);
                        man = unit?.Boss;
                        if (man == null || man.Dead || !man.Tf) continue;
                    }
                    anchor = man.Tf.position;
                }

                var prefab = CrewCars.BodyFor(item);
                if (prefab == null)
                {
                    WarnOnce("body:" + item.DisplayName,
                        $"[Crews] no body for the ledger's '{item.DisplayName}' in any Synty vehicle folder.");
                    continue;
                }

                CrewCars.MeasurePrefab(prefab, out float halfLength, out float halfWidth);
                if (!CrewCars.KerbSlotNear(Net ?? LaneNet.Active, anchor,
                        halfLength, halfWidth, out var at, out var facing))
                {
                    WarnOnce("kerb:" + item.Id,
                        front != null
                            ? $"[Crews] nowhere to leave the outfit's {item.DisplayName} - no free kerb outside the front."
                            : $"[Crews] nowhere to leave {man.DisplayName}'s {item.DisplayName} - no free kerb near him.");
                    continue;
                }

                var car = AddCar(prefab, at, facing, CarRoadY);
                if (car == null) continue;
                car.ItemId = item.Id;
                car.DisplayName = string.IsNullOrEmpty(item.DisplayName) ? "Car" : item.DisplayName;
                car.Owner = OwnerFor(roster, item);
                _ledgerCars.Add(car);
                Debug.Log(front != null
                    ? $"[Crews] the outfit's {car.DisplayName} is parked outside the front."
                    : $"[Crews] {man.DisplayName}'s {car.DisplayName} is at the kerb beside him.");
            }
        }

        /// <summary>The cars this deal stood, as against the ones the scene put down.</summary>
        readonly HashSet<CrewCar> _ledgerCars = new HashSet<CrewCar>();

        readonly HashSet<string> _warned = new HashSet<string>();

        void WarnOnce(string key, string message)
        {
            if (_warned.Add(key)) Debug.LogWarning(message);
        }

        /// <summary>Take a car off the street for good: anyone riding gets out where it
        /// stands, and the body goes with its claim on the road.</summary>
        void DropCar(CrewCar car)
        {
            if (car == null) return;
            foreach (var man in new List<CrewWalker>(car.Aboard))
                LetOut(car, man, car.SeatOf.TryGetValue(man, out int seat) ? seat : 0);
            car.Occupant = null;
            foreach (var unit in Units)
            {
                if (unit.Car == car) { unit.Car = null; unit.PendingDrive = null; }
                if (unit.Boarding == car) Unboard(unit, "the car was taken off the street");
            }
            car.Despawn();
            StreetTraffic.Users.Remove(car);
            Cars.Remove(car);
            _ledgerCars.Remove(car);
            if (car.Tf) Destroy(car.Tf.gameObject);
        }

        // ------------------------------------------------------------------ combat

        void SetTarget(Unit unit, Unit target) => SetTarget(unit, target, ordered: false);

        /// <summary>How far round an ordered job the traffic is thinned, and for how
        /// long. Wide enough to take in the street the job is on and the mouths of the
        /// ones either side of it; long enough for a crew to walk in, do it and leave.
        /// See StreetTraffic.Quiet - it is a stage direction and nothing more.</summary>
        public static float QuietRadius = 80f, QuietSeconds = 90f;

        void SetTarget(Unit unit, Unit target, bool ordered)
        {
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
            foreach (var man in unit.All())
                // riding - in a car's seat or on a machine's saddle - is not a man who
                // walks up to somebody and opens fire; his gun is the vehicle's business
                // (TickRiders, CrewBike.TickGuns)
                if (!man.Dead && !IsAboard(man) && !man.Riding)
                    man.Engage(BestMark(target, man.Tf.position, reach, sighted: !ordered));
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
        static CrewWalker BestMark(Unit unit, Vector3 from, float within, bool sighted)
        {
            CrewWalker fighting = null, running = null;
            float fd = within * within, rd = within * within;
            foreach (var m in unit.All())
            {
                if (m.Dead || !m.Tf) continue;
                float d = (m.Tf.position - from).sqrMagnitude;
                bool runner = m.Panicked || m.Retreating;
                // the range first and the walls after: a look down the sight line is
                // several cells of the ground walked, and most men are out of range
                if (d >= (runner ? rd : fd)) continue;
                if (sighted && !InSight(from, m.Tf.position)) continue;
                if (runner) { rd = d; running = m; }
                else { fd = d; fighting = m; }
            }
            return fighting ?? running;
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
                if (unit.Car != null) continue; // riders fire from the windows, not on foot

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
                foreach (var man in unit.All())
                {
                    if (man.Dead || !man.Armed || man.Panicked || IsAboard(man) ||
                        man.Riding) continue;
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
                        if (mark != null) man.Engage(mark);
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
                if (man == null || man.Tf == null || man.Dead || !man.Armed) continue;
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
        void TakeOffRetreated(Unit unit)
        {
            var gone = new List<CrewWalker>();
            foreach (var man in unit.All())
                if (!man.Dead && man.Retreating && man.State == CrewWalker.Mode.Standing) gone.Add(man);
            foreach (var man in gone)
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
                if (unit.IsPolice || unit.Wiped) continue;
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
                        // flats has not "seen the outfit walk up", whatever the tape says
                        if (!b.Dead && !IsAboard(b) &&
                            (a.Tf.position - b.Tf.position).sqrMagnitude < r2 &&
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
            Resolve(shooter, shooter.Target, shooter.MuzzlePosition, shooter.Tf.position,
                CrewArms.MuzzleOf(shooter.Weapon) ?? shooter.Tf);
        }

        /// <summary>One shot, wherever it left from: a man's gun on the pavement, or a
        /// car window on a pass. <paramref name="from"/> is where the shooter stands
        /// for the range - the man, or the car he is in.</summary>
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
            if (target == null || target.Dead) return;

            float dist = Vector3.Distance(from, target.Tf.position);
            // the gun's accuracy holds to half its reach and falls to half of itself at
            // the edge; a lieutenant is a better shot; nothing is ever certain, and a
            // shotgun in a man's face very nearly is
            float reach = Mathf.Max(stats.Range, 1f);
            float falloff = dist <= reach * 0.5f ? 1f : Mathf.Lerp(1f, 0.5f, (dist / reach - 0.5f) / 0.5f);
            float p = stats.Accuracy * falloff;
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
            target.TakeHit(stats.Damage, shooter);
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
                var blood = Instantiate(BloodPrefab, target.ChestPosition,
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
                var blood = Instantiate(BloodPrefab, civ.Tf.position + Vector3.up * 1.2f, Quaternion.LookRotation(-line));
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
                var puff = Instantiate(ImpactPrefab, at, Quaternion.LookRotation(muzzle - at));
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
            var at = car.Tf.TransformPoint(new Vector3(
                side * car.HalfWidth, Random.Range(0.55f, 1.15f), along));
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
                var puff = Instantiate(ImpactPrefab, at, Quaternion.LookRotation(muzzle - at));
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
            var puff = Instantiate(ImpactPrefab, spot, Quaternion.LookRotation(Vector3.up));
            Destroy(puff, 2f);
        }

        // The flash rides whatever fired it - the gun in the hand, the car under
        // the window - so it stays on the muzzle of a moving car; the particles the
        // pack simulates in world space (the smoke) trail behind, as smoke does.
        void Flash(Vector3 muzzle, Vector3 forward, Transform follow, EquipmentKind kind)
        {
            if (MuzzleFlashPrefab)
            {
                var flash = Instantiate(MuzzleFlashPrefab, muzzle, Quaternion.LookRotation(forward), follow);

                // ONE TRIGGER PULL IS ONE FLASH. The pack's FX_Gunshot_01 is authored as
                // a LOOPING system - the flash emitter runs on a tenth of a second and
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
                float live = 0.25f;
                foreach (var ps in flash.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = false;
                    live = Mathf.Max(live, main.duration + main.startLifetime.constantMax);
                }
                Destroy(flash, Mathf.Min(live, 1.5f));
            }
            var shots = ShotsFor(kind);
            if (shots.Length > 0)
            {
                // one 2D source, pitch-jittered: the shot has to be heard from the
                // demo's camera height, where a 3D one-shot at default rolloff is a whisper
                if (_shots == null)
                {
                    _shots = gameObject.AddComponent<AudioSource>();
                    _shots.spatialBlend = 0f;
                    _shots.playOnAwake = false;
                }
                // Several recorded reports per weapon, so the variation comes from the
                // files and the pitch only has to keep two shots in a burst from being
                // identical - a transposition wide enough to fake variety also changes
                // the calibre, which is the one thing these files get right for free.
                _shots.pitch = Random.Range(0.94f, 1.07f);
                _shots.PlayOneShot(shots[Random.Range(0, shots.Length)], 0.5f);
                if (CrackClip)
                {
                    if (_cracks == null)
                    {
                        _cracks = gameObject.AddComponent<AudioSource>();
                        _cracks.spatialBlend = 0f;
                        _cracks.playOnAwake = false;
                    }
                    _cracks.pitch = Random.Range(0.92f, 1.12f);
                    _cracks.PlayOneShot(CrackClip, 0.3f);
                }
            }
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
                           ?? new Unit { CrewId = crew.Id, Faction = 0, GangName = OutfitNames.Player, Bombs = BombsPerCrew };
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

            BindCars(roster);
            BindBikes(roster);
            BindBombs(roster);
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

            PedLink best = null;
            float bestD = float.MaxValue, bestT = 0f;
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
            man.InitAt(go.transform, CrewKit.Draw(_clips, _variety), Clear(pos, member.FullName), rot);
            man.Fired = OnFired;
            man.RangeFactor = Random.Range(0.55f, 0.85f);
            man.SetJog(Random.Range(2.7f, 3.5f));
            return man;
        }

        CrewWalker SpawnAt(GameObject prefab, string name, int id, Vector3 pos, Quaternion rot,
            float pace, bool afoot = true)
        {
            if (prefab == null) return null;
            var go = Body(prefab, name);
            var man = new CrewWalker
                { Speed = pace, CharacterId = id, SourcePrefab = prefab, DisplayName = name };
            man.InitAt(go.transform, CrewKit.Draw(_clips, _variety),
                afoot ? Clear(pos, name) : pos, rot);
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
