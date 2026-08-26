using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A motorcycle of the outfit's: a hood at the bars, his mate behind him with the
    /// gun. CrewCar's opposite number, and the reason the whole two-wheeler business is
    /// worth the trouble - a drive-by from a car is a gun out of a window, hemmed in by
    /// the seat it is fired from and blind to its own side of the street, and a
    /// drive-by from a pillion is a man who can turn round and shoot down either
    /// pavement and off the back of the bike as it goes.
    ///
    /// So the rules a car's riders fire under are not this one's. A car's man may only
    /// shoot out of his own window, within sixty degrees of abeam and on his own side
    /// (DemoCrews.TickRiders); a pillion may shoot all the way round except through the
    /// man in front of him.
    ///
    /// THE RIDER NEVER FIRES. Both hands are on the bars and they stay there - the
    /// player's ruling, against an earlier branch that let him take one off at walking
    /// pace when he was alone on the machine. And NEITHER man carries a long gun: what
    /// goes on a saddle is capped at the machine pistol, because a rifle is a metre of
    /// barrel on a moving motorcycle (CapArms, CrewArms.FitsASaddle).
    ///
    /// It can also come off. Four endings, and they are the four the bench rides side
    /// by side in Assets/BikeDemo: both ride on, the pillion is shot off the back, the
    /// rider is shot and the whole machine goes down with him, or the tank goes and it
    /// burns and then blows. See <see cref="TickRiders"/>.
    /// </summary>
    public sealed class CrewBike : RoadBike
    {
        public enum Mode { Parked, Riding, DriveBy }

        /// <summary>The crew whose lieutenant owns it; the crew on it. The book sells
        /// motorcycles now (ArmoryCatalog.Motorcycles), so for a machine the ledger
        /// stood these are the book's answer - the crew of the lieutenant the item is
        /// dealt to. A bike a scene put down keeps whoever it was given to.</summary>
        public DemoCrews.Unit Owner, Occupant;

        /// <summary>The stock item this machine IS, or -1 for one the scene stood.
        /// The car's field, for the same reason: the book is the truth and the street
        /// follows it, so a machine has to be able to say which line of the book it
        /// is (DemoCrews.StandLedgerBikes).</summary>
        public int ItemId = -1;

        public string DisplayName = "Motorcycle";

        /// <summary>The arena, for the one thing a bike cannot do for itself: resolve a
        /// shot. Without it the guns come up and nothing is fired, which is a fair way
        /// for an optional wiring to fail.</summary>
        public DemoCrews Arena;

        public CrewWalker Rider { get; private set; }
        public CrewWalker Pillion { get; private set; }

        /// <summary>The crew being shot up, or null.</summary>
        DemoCrews.Unit _driveByTarget;

        /// <summary>The crew this machine is riding at, or null. Setting it is what
        /// opens and closes a pass, so the count of passes running is kept here rather
        /// than by whoever remembers to tell somebody.</summary>
        public DemoCrews.Unit DriveByTarget
        {
            get => _driveByTarget;
            private set
            {
                if (ReferenceEquals(_driveByTarget, value)) return;
                if (_driveByTarget == null) PassesRunning++;
                else if (value == null) PassesRunning = Mathf.Max(0, PassesRunning - 1);
                _driveByTarget = value;
            }
        }

        /// <summary>How many machines are out on a pass right now. A drive-by is the one
        /// time the street's own people are asked to behave differently for it (a chase
        /// that keeps to the pavement rather than spilling onto the main road), and this
        /// is how anybody asks whether one is on without holding a reference to the
        /// bike.</summary>
        public static int PassesRunning { get; private set; }

        /// <summary>Is a drive-by being ridden anywhere in the city this instant?</summary>
        public static bool AnyPassOn => PassesRunning > 0;

        // a static outlives a play, and a machine destroyed mid-pass never gets to put
        // its own count back - so the tally starts from nothing each time the game does
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetPasses() => PassesRunning = 0;

        /// <summary>In a fight on the way somewhere: the rider puts it on and goes round
        /// whatever is in front of him at once. A drive-by is hot on its own.</summary>
        public bool Hot;

        /// <summary>How far past the mark a pass runs before it turns round.</summary>
        public const float PassOvershoot = 44f;

        /// <summary>One pass and away, rather than passes until the crew is down.
        ///
        /// The two are different jobs and not two settings of one. Passes-until-down is
        /// a siege on wheels: it works, and it leaves two men riding up and down the
        /// same fifty metres of street with everybody on it shooting back, which is how
        /// a drive-by turns into the fight it existed to avoid. One pass is the thing
        /// the player asked for - past the door, empty what you have at it, and be
        /// round the corner before the answer comes. What "over" means is then not
        /// "they are all down" but "we are past them", and that is
        /// <see cref="PassSpent"/>.</summary>
        public bool SinglePass;

        /// <summary>True once a single pass has been ridden out - the arena's cue to
        /// take the machine home (DemoCrews.TickDriveBy). Cleared by the next order.</summary>
        public bool PassSpent { get; private set; }

        /// <summary>Rounds fired off this machine since the current order was given.
        /// A pass that fires nothing is the quiet failure of the whole business - the
        /// men rode past and the guns never bore - and it is invisible from outside
        /// unless somebody counts. The headless loop counts (BlockDemoMission).</summary>
        public int ShotsFired { get; private set; }

        /// <summary>Pace past the mark.
        ///
        /// It was eight and a half - the arithmetic of a pistol, which reaches ten metres
        /// and wants half a second of them to fire in. That is thirty kilometres an hour
        /// and it does not read as a drive-by; it reads as a machine having a look. A
        /// drive-by is a thing that GOES PAST. Thirteen is fifty an hour: still slow
        /// enough that the pillion gets his rounds off inside the reach of what he is
        /// carrying, and fast enough that the street does not get a good look at him -
        /// which is the whole reason two men take a motorcycle instead of a car.</summary>
        public const float PassSpeed = 13f;

        /// <summary>How far round himself a pillion may shoot. Nearly everywhere: what
        /// is barred is the cone through the rider's back.</summary>
        public static float PillionBlindArc = 34f;

        /// <summary>What a pillion adds to the reach of what he is carrying.
        ///
        /// A man leaning out of a CAR window gets 1.4 of his gun's range (DemoCrews.
        /// RidingReach) because he takes it further out than a man on the pavement does.
        /// A pillion had 1.3 - LESS than the man hemmed in by a door frame - which was
        /// backwards: he is sitting in the open with nothing round him and can turn all
        /// the way round (PillionBlindArc). It matters because the window is measured in
        /// TIME: at the pass speed, a .38's ten metres is a second and a half of the
        /// mark, which is one round. Eighteen is four seconds.</summary>
        public static float PillionReach = 1.8f;

        /// <summary>And how much faster he empties it than a man on his feet.
        ///
        /// A man standing in the street shoots to hit; a man going past at fifty
        /// kilometres an hour with one chance shoots to EMPTY WHAT HE HAS. Accuracy is
        /// not touched - it still falls off with range in Resolve, so most of these go
        /// wide, which is what a drive-by looks like and why one is not the same thing
        /// as a killing.</summary>
        public static float PillionRate = 0.45f;

        int _passDir = 1;
        BikePose _riderPose, _pillionPose;
        Transform _riderHome, _pillionHome;
        float _pillionShot;

        // ------------------------------------------------------------- what he carries

        /// <summary>The gun each man was carrying before he got on, when what he was
        /// carrying would not ride (see CrewArms.FitsASaddle). Given back the moment he
        /// is off it, on his own feet or in the road - a machine borrows a man's arms for
        /// the length of a ride, it does not confiscate them.</summary>
        GameObject _stowedRider, _stowedPillion;
        EquipmentKind _stowedRiderKind, _stowedPillionKind;

        /// <summary>What goes on a saddle, both saddles.
        ///
        /// The player's rule is about SIZE - "the kalashnikov is too big" - and size is
        /// no more a passenger's problem than a driver's: a man steering a motorcycle
        /// with a metre of rifle in his fist reads exactly as wrong as the man behind him
        /// holding one. So both men are held to it, and CrewArms owns the measurement.</summary>
        void CapArms(CrewWalker man, bool pillion)
        {
            if (man == null || !man.Armed || CrewArms.FitsASaddle(man.WeaponKind)) return;
            var swap = CrewArms.ModelForKind(EquipmentKind.MachinePistol);
            // Nothing to swap TO - the ledger's model set has no machine pistol in it.
            // He keeps the long gun rather than riding out empty-handed: an unarmed pass
            // is the quiet failure the whole drive-by exists to avoid.
            if (swap == null) return;
            if (pillion) { _stowedPillion = man.WeaponPrefab; _stowedPillionKind = man.WeaponKind; }
            else { _stowedRider = man.WeaponPrefab; _stowedRiderKind = man.WeaponKind; }
            man.Arm(swap, EquipmentKind.MachinePistol);
        }

        /// <summary>His own gun back - a dead man's too. He is shot off the pillion
        /// holding the machine pistol the saddle put in his hand, and the body that
        /// lands in the road is then carrying something the books say he never owned.
        /// The slot is cleared either way: the machine has finished borrowing.</summary>
        void GiveArmsBack(CrewWalker man, bool pillion)
        {
            var stowed = pillion ? _stowedPillion : _stowedRider;
            if (stowed == null) return;
            var kind = pillion ? _stowedPillionKind : _stowedRiderKind;
            if (pillion) _stowedPillion = null; else _stowedRider = null;
            if (man == null || man.Tf == null) return;
            // ...unless it has already left his hand. A dead man drops his gun part-way
            // through the fall and it lies where it fell; putting his own back in the
            // fist afterwards would be a second gun out of nowhere.
            if (man.Dead && man.Weapon == null) return;
            man.Arm(stowed, kind);
        }

        // ------------------------------------------------------------- the machine's tin

        /// <summary>Rounds that went into the machine, and into something on it that
        /// matters. A motorcycle is not a health bar any more than a car is (CrewCar):
        /// this counts what went into the tank and the engine, and everything else a
        /// round does to a machine it does to the paint.</summary>
        public int TankHits { get; private set; }

        /// <summary>Of the rounds that go into the machine, this many find the tank or
        /// the engine - and how many of those it takes before it is alight.
        ///
        /// Count the INPUT, not the outcome - the lesson the car's engine had to be
        /// taught twice. MEASURED over four runs of the lab (DemoCrews puts the round in
        /// and traces it as "tin"): four to seven rounds go into the MACHINE in a run of
        /// one to six passes, which is a tenth of what a car standing under fire takes,
        /// because a car is a wall behind the man and a motorcycle is a frame between
        /// his knees. Two finds at four in ten is about five of those rounds - reachable,
        /// which a threshold nothing can meet is not, and rare enough that it stays the
        /// last of the four endings: it burned in one of the four runs.</summary>
        public static float TankChance = 0.4f;
        public static int TankHitsToBurn = 2;

        /// <summary>The tank is gone: it goes down alight and the men jump clear.</summary>
        public bool Burning => TankHits >= TankHitsToBurn;

        /// <summary>A round into the machine. Where it went decides what it cost - the
        /// middle of it is the tank and the engine, the ends are a hole in a mudguard.</summary>
        public void TakeRound(Vector3 at, Vector3 from)
        {
            if (Tf == null) return;
            var local = Tf.InverseTransformPoint(at);
            // measured on the MESH, like the hole itself: HalfLen is the road's smaller
            // idea of the machine, and the tank is a fact about the machine
            float along = Body != null ? Body.HalfLength : HalfLen;
            bool vitals = Mathf.Abs(local.z) < along * 0.6f;
            if (vitals && !Burning && Random.value < TankChance) TankHits++;
            // the flank the round went into: a machine is narrow enough that the side
            // it is nearest IS the side it went through, and the hole has to lie in
            // that plane rather than turn to face whoever fired it
            var outward = Tf.right * Mathf.Sign(local.x == 0f ? (from - at).x >= 0f ? 1f : -1f : local.x);
            if (Tilt != null) CrewGore.Hole(Tilt, at, outward);
        }

        // ------------------------------------------------------------- on its side

        /// <summary>It is down - on its side in the road, sliding or stopped. Nothing
        /// drives it again: RoadCar's whole frame is skipped while this stands, and the
        /// spill owns the transform (BikeSpill).</summary>
        public bool Down => _spill != null;

        /// <summary>It went down BURNING - the tank is gone and it is scrap. The machine
        /// that merely fell over is not this: it is lying in the road and could in
        /// principle be picked up.</summary>
        public bool BurntOut => _spill != null && _spill.Alight;

        BikeSpill _spill;
        RiderSpill _riderSpill, _pillionSpill;
        CrewWalker _riderOff, _pillionOff;

        static RiderSpill.Wardrobe _wardrobe;
        static bool _wardrobeDrawn;

        // Drawn once for the whole game, not once per machine: it is four clips off
        // CrewKit and a pool of deaths, and every one of those is an AssetDatabase load
        // in the editor.
        static RiderSpill.Wardrobe Wardrobe()
        {
            if (!_wardrobeDrawn)
            {
                _wardrobe = RiderSpill.Wardrobe.Stock();
                _wardrobeDrawn = true;
            }
            return _wardrobe;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetWardrobe() => _wardrobeDrawn = false;

        /// <summary>Which way it goes over: on with the lean it already has, and onto
        /// the stand side when it is upright. Never a coin toss - a machine that falls
        /// the other way from the way it was leaning reads as a machine being pushed.</summary>
        float FallSide => Lean > 0.5f ? 1f : -1f;

        /// <summary>The wreck the blast is owed, once. The arena reads it (DemoCrews) -
        /// a machine cannot set off an explosion, because it cannot be asked who was
        /// standing near it.</summary>
        public bool TakeBlast() => _spill != null && _spill.TakeBlast();

        /// <summary>The four things that can come of two men on one machine, decided
        /// here and nowhere in the spill classes - which only know how to fall. They are
        /// the four the bench rides side by side (Assets/BikeDemo, BikeShow.Act):
        ///
        ///   1  BOTH RIDE ON      nothing below fires; the ordinary pass.
        ///   2  THE PILLION SHOT  he goes off the back and stays in the road. The
        ///                        machine rides on with the driver alone - and with
        ///                        nobody to shoot, so the pass is spent.
        ///   3  THE RIDER SHOT    the machine goes down and takes his mate with it. The
        ///                        player's own ruling, made against the alternative: a
        ///                        motorcycle whose rider is shot at fifty kilometres an
        ///                        hour does not change drivers, it falls over.
        ///   4  THE TANK GOES     it goes down alight, both men jump clear, and a few
        ///                        seconds later it blows (BikeSpill.Fuse).
        ///
        /// And the fault this replaced, which is what the player actually watched: a dead
        /// man was set down on his feet beside the machine and then hidden where he stood
        /// (DemoCrews.ReportDeaths - "one of them vanished"), and the machine was left
        /// standing upright in the road with nobody on it, which nothing can do.</summary>
        void TickRiders()
        {
            if (Down) return;
            // the tank first, and before the empty test: a machine shot up until it
            // catches goes over whether or not anybody is still sitting on it
            if (Burning) { GoDown(alight: true); return; }
            if (Rider == null && Pillion == null) return;

            if (Rider != null && Rider.Dead) { GoDown(alight: false); return; }
            if (Pillion != null && Pillion.Dead) ThrowPillion(dies: true);
        }

        /// <summary>Off the back, and the machine never knows about it.</summary>
        void ThrowPillion(bool dies)
        {
            var man = Pillion;
            if (man == null) return;
            _pillionSpill = Throw(man, _pillionPose, dies, FallSide);
            _pillionOff = man;
            Pillion = null;
            _pillionPose = null;
            _pillionShot = 0f;
        }

        /// <summary>Over it goes, and everybody on it with it. A man who was already
        /// dead stays down; a man who was not gets up out of the road.</summary>
        public void GoDown(bool alight)
        {
            if (Down || Tf == null) return;
            float side = FallSide, speed = Mathf.Abs(Speed);

            // The plan is torn up BEFORE the transform changes hands: a machine on its
            // side with a goal still on it is a machine the driving would go on steering
            // if anything ever ticked it again.
            DriveByTarget = null;
            PassSpent = true;
            Hot = false;
            Halt(hard: true);
            // and the street is told what it is now: a thing to plan round, not a
            // vehicle to queue behind. Without it the wreck goes on publishing the speed
            // it had when it lost the road, and the traffic waits for it to move off.
            StandDown();

            if (Pillion != null)
            {
                _pillionSpill = Throw(Pillion, _pillionPose, Pillion.Dead, side);
                _pillionOff = Pillion;
                Pillion = null;
                _pillionPose = null;
            }
            if (Rider != null)
            {
                _riderSpill = Throw(Rider, _riderPose, Rider.Dead, side);
                _riderOff = Rider;
                Rider = null;
                _riderPose = null;
            }

            // the lean goes with the rider: the spill's ninety degrees is the whole of
            // the angle, laid on the same axis, and a lean left under it double-counts
            if (Tilt != null) Tilt.localRotation = Quaternion.identity;

            // AND IT GOES DOWN TOWARD ITS OWN KERB. Where it fell is where it stays -
            // and a machine on its side in the middle of a lane is a lane nobody can use
            // (BikeSpill.Beach has the sums). The fall carries it over instead, and it
            // goes over onto the side it is sliding to, which is the way a machine that
            // has lost the road actually leaves it.
            var road = Road;
            var across = Vector3.zero;
            float far = 0f;
            if (road != null && OnRoad)
            {
                float over = road.KerbDOnSide(D, HalfWide) - D;
                if (Mathf.Abs(over) > 0.05f)
                {
                    across = road.Right * Mathf.Sign(over);
                    far = Mathf.Abs(over);
                    side = Vector3.Dot(across, Tf.right) >= 0f ? 1f : -1f;
                }
            }
            _spill = BikeSpill.Begin(Tf, speed, Forward, side, alight, Tf.position.y);
            if (far > 0f) _spill?.Beach(across, far);
        }

        RiderSpill Throw(CrewWalker man, BikePose pose, bool dies, float side)
        {
            if (man == null || man.Tf == null) return null;
            bool pillion = man == Pillion;
            GiveArmsBack(man, pillion);
            // off the machine's books BEFORE the pose is destroyed, or the list is left
            // holding a component Unity is about to take away
            Drop(pose);
            man.BeginSpill();
            // back under whatever he was parented to before he got on, exactly as
            // Dismount would put him - a man who came off a machine is not a part of it
            var spill = RiderSpill.Throw(man, Forward * Mathf.Abs(Speed), dies, Wardrobe(),
                pillion ? _pillionHome : _riderHome, side, Tf != null ? Tf.position.y : 0f);
            // the pose is the bike's, and he is not on the bike any more. RiderSpill has
            // already switched it off; this is what stops it coming back on him.
            if (pose != null) Object.Destroy(pose);
            return spill;
        }

        /// <summary>A man the spill has finished with - read one at a time, by the
        /// arena, which is the only thing that can put him back on the pavement graph
        /// and give him something to do (DemoCrews.Rejoin). Null when there is none.</summary>
        public CrewWalker TakeLanded() => _landed.Count > 0 ? _landed.Dequeue() : null;

        readonly System.Collections.Generic.Queue<CrewWalker> _landed =
            new System.Collections.Generic.Queue<CrewWalker>();

        void TickSpills()
        {
            Settle(ref _pillionSpill, ref _pillionOff);
            Settle(ref _riderSpill, ref _riderOff);
        }

        /// <summary>Give up whoever this machine still has in the air. It is being taken
        /// off the street - sold off the books, or the scene torn down - and a man in a
        /// spill that nothing will ever tick again is a man who has left the game, which
        /// is the exact fault this whole layer was written to end. He is set down where
        /// he has got to and handed back like any other landing (TakeLanded).</summary>
        public void LetGo()
        {
            LetGo(ref _pillionSpill, ref _pillionOff);
            LetGo(ref _riderSpill, ref _riderOff);
        }

        void LetGo(ref RiderSpill spill, ref CrewWalker man)
        {
            if (spill != null) { Object.Destroy(spill); spill = null; }
            if (man == null) return;
            var down = man;
            man = null;
            down.EndSpill();
            _landed.Enqueue(down);
        }

        void Settle(ref RiderSpill spill, ref CrewWalker man)
        {
            if (spill == null)
            {
                // thrown with no spill at all (no wardrobe, no transform): he is simply
                // off it, and must not be left in the riding state for ever
                if (man == null) return;
                man.EndSpill();
                _landed.Enqueue(man);
                man = null;
                return;
            }
            if (!spill.Settled) return;
            var down = man;
            Object.Destroy(spill);
            spill = null;
            man = null;
            if (down == null) return;
            down.EndSpill();
            _landed.Enqueue(down);
        }

        public CrewBike()
        {
            Profile = DriverProfile.Gangster;
            Tag = "crewbike";
        }

        /// <summary>CrewCar.GivesWayTo, for two wheels. A machine on a pass does not
        /// stop for the crew it has come to shoot at.</summary>
        protected override bool GivesWayTo(int faction)
        {
            if (faction == StreetAlarm.PoliceFaction) return true;
            var unit = Occupant ?? Owner;
            if (unit != null && faction == unit.Faction) return true;
            if (DriveByTarget != null && faction == DriveByTarget.Faction) return false;
            if (unit == null) return true;
            return unit.TargetUnit == null || unit.TargetUnit.Faction != faction;
        }

        public Mode State =>
            DriveByTarget != null ? Mode.DriveBy
            : HasGoal || FreeGoal.HasValue || Mathf.Abs(Speed) > 0.05f ? Mode.Riding
            : Mode.Parked;

        public bool Moving => State != Mode.Parked || Mathf.Abs(Speed) > 0.05f;
        public int FreeSeats => (Rider == null ? 1 : 0) + (Pillion == null && Body != null && Body.SeatsTwo ? 1 : 0);

        // ------------------------------------------------------------------ mounting

        /// <summary>Put this man on it - at the bars if it is free, else behind. False
        /// when there is no room, or the body will not take a rider's pose.</summary>
        public bool Mount(CrewWalker man)
        {
            if (man == null || man.Tf == null || man.Dead) return false;
            if (Rider == null) return Mount(man, pillion: false);
            if (Pillion == null && Body != null && Body.SeatsTwo) return Mount(man, pillion: true);
            return false;
        }

        public bool Mount(CrewWalker man, bool pillion)
        {
            if (man == null || man.Tf == null || man.Dead || Body == null || Tilt == null) return false;
            if (pillion ? Pillion != null : Rider != null) return false;

            var pose = man.Tf.GetComponent<BikePose>();
            if (pose == null) pose = man.Tf.gameObject.AddComponent<BikePose>();
            if (!pose.Setup(Body, pillion))
            {
                Object.Destroy(pose);
                return false;
            }
            // astride, not sat in: his legs stay on and BikePose puts them on the pegs
            man.SetRiding(true, astride: true);
            var home = man.Tf.parent;
            man.Tf.SetParent(Tilt, worldPositionStays: false);
            man.Tf.localPosition = pillion ? Body.SaddlePillion : Body.SaddleRider;
            man.Tf.localRotation = Quaternion.identity;

            if (pillion)
            {
                Pillion = man;
                _pillionPose = pose;
                _pillionHome = home;
                pose.Rider = _riderPose;
            }
            else
            {
                Rider = man;
                _riderPose = pose;
                _riderHome = home;
                if (_pillionPose != null) _pillionPose.Rider = pose;
            }
            CapArms(man, pillion);
            Take(pose);
            return true;
        }

        /// <summary>Off it, stood on the road beside it on the kerb side.</summary>
        public void Dismount(CrewWalker man)
        {
            if (man == null) return;
            bool pillion = man == Pillion;
            if (!pillion && man != Rider) return;

            GiveArmsBack(man, pillion);
            var pose = pillion ? _pillionPose : _riderPose;
            Drop(pose);
            if (pose != null) Object.Destroy(pose);
            if (man.Tf != null)
            {
                man.Tf.localScale = Vector3.one;   // BikePose may have taken him down to fit
                man.Tf.SetParent(pillion ? _pillionHome : _riderHome, worldPositionStays: true);
                var side = Tf != null ? Tf.right : Vector3.right;
                var at = Position + side * (HalfWide + 0.8f);
                man.Tf.SetPositionAndRotation(new Vector3(at.x, RoadY, at.z),
                    Quaternion.LookRotation(Tf != null ? Tf.forward : Vector3.forward, Vector3.up));
            }
            man.SetRiding(false);

            if (pillion) { Pillion = null; _pillionPose = null; }
            else
            {
                Rider = null;
                _riderPose = null;
                if (_pillionPose != null) _pillionPose.Rider = null;
                // nobody steering: it is not going anywhere
                Halt(hard: true);
            }
        }

        public void DismountAll()
        {
            Dismount(Pillion);
            Dismount(Rider);
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Ride there and stop at the kerb nearest it.</summary>
        public void RideTo(Vector3 point)
        {
            DriveByTarget = null;
            PassSpent = false;
            Profile = Hot ? DriverProfile.Hot : DriverProfile.Gangster;   // Tick re-reads it
            if (!OnRoad || Net == null) { GoFree(new Vector3(point.x, RoadY, point.z)); return; }
            if (!GoTo(point, park: true)) GoFree(new Vector3(point.x, RoadY, point.z));
        }

        /// <summary>Shoot the place up: passes along the street past this crew, a turn
        /// at the end of each, until told otherwise or nobody is left standing.</summary>
        public void DriveBy(DemoCrews.Unit target)
        {
            if (target == null || Rider == null) return;
            DriveByTarget = target;
            PassSpent = false;
            ShotsFired = 0;
            NoTurnBack = false;   // the way OUT may turn in the road; the way home may not
            _passCheck = 1.5f;
            var t = target.Position;
            if (Road != null)
            {
                Road.Project(t, out float ts, out _);
                _passDir = (ts - S) * Heading >= 0f ? Heading : -Heading;
            }
            else _passDir = Vector3.Dot(t - Position, Forward) >= 0f ? 1 : -1;
            PlanPass();
        }

        /// <summary>How far off a carriageway the mark may stand and still have a street
        /// the pass can be ridden down, tried in turn: a man on a pavement, a crew at a
        /// frontage, a crew in a yard or a lot.</summary>
        static readonly float[] PassReach = { 14f, 30f, 60f };

        /// <summary>Where the mark stood when this pass was laid against him. A pass is a
        /// line of road chosen for a POINT, and the ride to it takes as long as the traffic
        /// takes - a minute and a half, in the quarter the lab drives. A crew does not
        /// stand still for that, so the machine arrived at a stretch of empty street and
        /// rode home with the guns unfired ("3 of 3 passes ridden, 0 with shots"). The
        /// mark is looked at again on the way in, and the pass re-laid when he has moved
        /// off the one it was drawn for - which is what a rider does with his eyes.</summary>
        Vector3 _passLaidAt;

        /// <summary>Seconds until the mark is looked at again.</summary>
        float _passCheck;

        /// <summary>Metres the mark may drift before the pass is drawn again.</summary>
        public static float PassRelayWithin = 15f;

        /// <summary>How far the gun on the back reaches from the saddle.</summary>
        float PillionRange()
        {
            var man = Pillion ?? Rider;
            return (man != null ? man.Ballistics.Range : 10f) * PillionReach;
        }

        /// <summary>No street near the mark at all - a crew stood in the middle of open
        /// ground. The pass is ridden AT him over that ground rather than dropped: the
        /// mark is kept, so the guns bear as the machine goes by, and the run past ends
        /// the pass the ordinary way (TickFree calls OnArrived).</summary>
        void RideFreePast(Vector3 t)
        {
            var f = t - Position;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Forward;
            f.Normalize();
            GoFree(new Vector3(t.x, RoadY, t.z) + f * PassOvershoot);
        }

        void PlanPass()
        {
            if (DriveByTarget == null) return;
            var t = DriveByTarget.Position;
            if (Net == null || !OnRoad)
            {
                var f = t - Position; f.y = 0f; f.Normalize();
                GoFree(new Vector3(t.x, RoadY, t.z) + f * PassOvershoot);
                return;
            }
            // WHICH STREET IS HIS, and the search widens rather than giving up. Fourteen
            // metres is a man standing on a pavement; a crew at a FRONTAGE stands behind
            // the pavement, further off than that - and every pass ever ordered at one of
            // those was quietly thrown away here: RideTo clears the mark, the arena reads
            // a raid with no mark as a raid that is over, and two men rode out, rode home
            // and fired nothing. Three passes out of three, in every run of the lab, with
            // "3 of 3 passes ridden home, 0 with shots fired" as the only trace of it.
            Carriageway road = null;
            float ts = 0f, td = 0f;
            foreach (float within in PassReach)
            {
                road = Net.Locate(t, out ts, out td, within);
                if (road != null) break;
            }
            if (road == null) { RideFreePast(t); return; }
            // HOW FAR OFF THE STREET HE STANDS DECIDES WHETHER A STREET PASS IS A PASS AT
            // ALL. A pillion's reach is his gun and a bit (PillionReach) - eighteen metres
            // with the .38 every man carries. A crew stood thirty or forty metres back off
            // the carriageway, which is where a crew at a FRONTAGE stands, is not shot at
            // by riding down the street past it: the machine goes by, nothing bears, and
            // the pass is spent. It rides AT them over the ground between instead, which
            // is what a drive-by across a forecourt looks like.
            _passLaidAt = t;
            int dir = Road == road ? _passDir : (td >= 0f ? 1 : -1);
            float endS = Mathf.Clamp(ts + dir * PassOvershoot, 8f, road.Length - 8f);
            var lane = road.LaneFor(dir, td) ?? road.LaneFor(-dir, td);
            if (lane == null) { RideFreePast(t); return; }
            _passDir = lane.Heading;
            GoTo(road.Pose(endS, lane.Offset), park: false, standOff: 0f, stopAtGoal: false,
                 wantHeading: lane.Heading);
        }

        protected override void OnArrived()
        {
            if (DriveByTarget == null) return;
            // The end of a pass. On a single pass that is the whole job: the guns come
            // down where they are, and the machine stands ready for wherever it is sent
            // next - it does NOT pick its own way out of the street, because the men who
            // sent it know where home is and it does not.
            if (SinglePass)
            {
                DriveByTarget = null;
                PassSpent = true;
                Profile = DriverProfile.Gangster;
                return;
            }
            _passDir = -_passDir;
            PlanPass();
        }

        public void EndDriveBy()
        {
            DriveByTarget = null;
            RideTo(Position + Forward * 30f);
        }

        /// <summary>The pass is over where it stands - the mark dropped, the guns down,
        /// the machine still rolling. It does NOT pick its own way out of the street:
        /// the arena is what knows where home is (DemoCrews.TickPassing), and this only
        /// says that there is nothing more to be done here. Called when the crew being
        /// shot at is finished, and when the man on the back is.</summary>
        public void EndPass()
        {
            if (DriveByTarget == null) return;
            DriveByTarget = null;
            PassSpent = true;
            Profile = DriverProfile.Gangster;
        }

        /// <summary>Both wheels stopped, here, now - the plan torn up.</summary>
        public void HardStop()
        {
            DriveByTarget = null;
            Halt(hard: true);
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>How near the mark the pass is measured from - what LimitTarget takes
        /// the pace down over.
        ///
        /// The aggression belongs to the PASS, not to the two hundred metres of getting
        /// there, and the two are different rides. Hot is an eighteen-metre-a-second
        /// cruise with half a second of air off the car in front, on streets whose
        /// traffic runs at nine - a machine crossing a quarter at that pace, changing
        /// lanes at the junction lines, arrives behind a car stopped at a red with three
        /// metres in hand and ten metres of braking to do. (The belt caught it, twice,
        /// in the first headless runs of this: gap 0.0 at twelve metres a second.) Near
        /// the mark the pace is the pass's own anyway - LimitTarget takes it down to
        /// PassSpeed - so nothing about the drive-by itself is lost.</summary>
        public const float HotWithin = 60f;

        public new void Tick(float dt)
        {
            if (Tf == null) return;

            // ON ITS SIDE. The spill owns the transform outright, so RoadCar's entire
            // frame is skipped - anything that steers, brakes, claims a lane or writes a
            // position would be fighting it - and all the machine still owes is wheels
            // that spin down with the slide and a word to the street about where the
            // wreck has actually got to (RoadCar.Slid).
            if (Down)
            {
                Body?.Tick(dt, _spill.Speed, 0f);
                Slid(Tf.position);
                TickSpills();
                return;
            }
            TickRiders();
            // ALWAYS, and not only when the machine is down: the man shot off the back
            // of a machine that RIDES ON is the commonest of the four endings, and his
            // spill has nobody else to tick it. Left unticked he never lands, so he is
            // never handed back, never chalked, and stays marked as riding a machine he
            // came off two streets ago.
            TickSpills();
            if (Down) return;   // it went down on this very frame

            // A MACHINE WITH NOBODY ON IT DOES NOT DRIVE. Every driver profile has
            // Wanders on by default - "no route: random turns at junctions" - which is
            // right for traffic and very wrong for a motorcycle standing at a kerb with
            // its crew back on the pavement: the moment the last raid ended, the empty
            // machine set off round the quarter on its own and spent the rest of the run
            // grinding into a parked car with the belt refusing every step of it. Four
            // hundred and seventy-six seconds, in the run that found it. Nobody at the
            // bars, nowhere to be.
            if (Rider == null && (HasGoal || FreeGoal.HasValue)) Halt(hard: true);
            // ONE WAY OF DRIVING, and the legs differ only in pace (LimitTarget takes
            // the pass down to PassSpeed near the mark).
            //
            // The pass used to be driven on Hot and the run-up on the errand profile,
            // and both of those use the crown and the far lane. A car may swing across
            // the middle of the street to get round a queue; a MOTORCYCLE in this model
            // may not, because it does not filter between the lanes - it takes a whole
            // lane like anything else, and the swing lands it on top of whatever is
            // waiting at the next light. Every belt refusal the machine has ever earned
            // was that, and the last three were all on Hot, one of them five metres
            // inside a car before anybody noticed. Getaway is the profile with that lane
            // work taken out; there is no leg of a drive-by that wants it back.
            Profile = DriverProfile.Getaway;
            // the mark, looked at again on the way in (see _passLaidAt)
            if (DriveByTarget != null && !PassSpent)
            {
                _passCheck -= dt;
                if (_passCheck <= 0f)
                {
                    _passCheck = 1.5f;
                    var now = DriveByTarget.Position;
                    if ((now - _passLaidAt).sqrMagnitude > PassRelayWithin * PassRelayWithin &&
                        !NearMark(PillionRange()))
                        PlanPass();
                }
            }
            base.Tick(dt);
            TickGuns(dt);
        }

        bool NearMark(float metres)
        {
            if (DriveByTarget == null) return false;
            var to = DriveByTarget.Position - Position;
            to.y = 0f;
            return to.sqrMagnitude < metres * metres;
        }

        /// <summary>Forget what the last order fired - the arena's, when a new raid is
        /// given out. Not folded into DriveBy: a raid whose men never reach the machine
        /// never calls that, and it reported the PREVIOUS pass's rounds as its own.</summary>
        public void ClearShots() => ShotsFired = 0;

        /// <summary>Pace through a junction box.
        ///
        /// The straight is where the machine is fast; a box is not a straight. Every belt
        /// refusal left after the lane work came out was this one shape - "box:
        /// following", the machine entering a junction at the full sixteen behind a car
        /// that then stopped inside it, at a range where nothing can be done about it.
        /// Nobody crosses a junction at fifty-eight kilometres an hour behind somebody
        /// else, and a machine that does is not reading as quick, it is reading as a bug.
        /// Off the box it has its pace back inside a second (the profile asks 9, and the
        /// machine under it pulls better than that - VehiclePerformance).</summary>
        public static float BoxSpeed = 9f;

        protected override float LimitTarget(float target)
        {
            // through the junction at a pace a junction can be crossed at, whatever the
            // machine is doing either side of it
            if (Via != null) target = Mathf.Min(target, BoxSpeed);

            if (DriveByTarget == null || Tf == null) return target;
            var to = DriveByTarget.Position - Position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > 45f) return target;
            return Mathf.Min(target, Mathf.Lerp(PassSpeed, target, Mathf.InverseLerp(20f, 45f, dist)));
        }

        // Who is firing, at whom, and whether he can see him past the man in front.
        void TickGuns(float dt)
        {
            var target = DriveByTarget;
            if (target == null)
            {
                Aim(_pillionPose, Pillion, null);
                Aim(_riderPose, Rider, null);
                return;
            }
            if (DemoCrews.Finished(target))
            {
                Aim(_pillionPose, Pillion, null);
                Aim(_riderPose, Rider, null);
                // Nothing left to shoot at, part way down the pass. On a single pass
                // that ends it here rather than thirty metres up the road on the bike's
                // own initiative: the ride home is the arena's order, not the bike's.
                if (SinglePass)
                {
                    DriveByTarget = null;
                    PassSpent = true;
                    Profile = DriverProfile.Gangster;
                    Halt(hard: false);
                }
                else EndDriveBy();
                return;
            }

            var mark = DemoCrews.NearestOf(target, Position);
            if (mark == null || mark.Tf == null) { Aim(_pillionPose, Pillion, null); Aim(_riderPose, Rider, null); return; }

            // THE MAN ON THE BACK IS THE ONLY ONE WHO SHOOTS. The rider had a branch of
            // his own - alone on the machine, at a crawl, one hand off the bar - and the
            // player struck it out: a drive-by is two men and a job each, and a man
            // steering a motorcycle one-handed while he fires down a pavement is not a
            // thing that happens, it is a thing that ends in a shop window. With nobody
            // behind him the machine simply rides past; the pass is spent and it goes
            // home (DemoCrews.TickPassing).
            bool pillionOn = Pillion != null && !Pillion.Dead && Pillion.Armed && Sees(Pillion, mark, blindAhead: true);
            Aim(_pillionPose, Pillion, pillionOn ? mark : null);
            if (pillionOn) Shoot(Pillion, mark, ref _pillionShot, dt);
            else _pillionShot = 0f;
            Aim(_riderPose, Rider, null);
        }

        bool Sees(CrewWalker man, CrewWalker mark, bool blindAhead)
        {
            if (man == null || man.Tf == null || mark == null || mark.Tf == null) return false;
            var to = mark.Tf.position - Position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > man.Ballistics.Range * PillionReach) return false;
            if (!blindAhead || dist < 0.1f) return true;
            // straight up the road is where the man in front of him is sitting
            float ahead = Vector3.Angle(Forward, to / dist);
            return ahead > PillionBlindArc;
        }

        void Aim(BikePose pose, CrewWalker man, CrewWalker mark)
        {
            if (pose != null) pose.AimAt = mark != null && mark.Tf != null ? mark.ChestPosition : (Vector3?)null;
            if (man == null) return;
            man.RidingAim = mark != null;
            man.AimAt(mark);
        }

        void Shoot(CrewWalker man, CrewWalker mark, ref float timer, float dt)
        {
            timer -= dt;
            if (timer > 0f) return;
            timer = man.Ballistics.Interval * PillionRate;
            ShotsFired++;
            if (Arena != null) Arena.FireFrom(man, mark);
            else StreetAlarm.Report(Position, null, 0, man.Ballistics.Loudness);
        }

        public string StatusLine => Down
            ? (BurntOut ? "Burnt out in the road" : "On its side in the road")
            : State switch
        {
            Mode.DriveBy => DriveByTarget != null ? "Drive-by on " + DriveByTarget.GangName : "Drive-by",
            Mode.Riding => Hot ? "On the road, under fire" : "On the road",
            _ => Rider != null ? "Sat on the bike" : "On its stand",
        };
    }
}
