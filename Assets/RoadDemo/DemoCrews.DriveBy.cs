using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The motorcycle half of the crews: the machines the LEDGER sold the outfit, stood
    /// at the kerb beside the men they were dealt to, and the one order they exist for.
    ///
    /// This used to be flatly impossible, and the class said so where the bikes are
    /// ticked: "the book does not sell one, two men is not a crew, and the whole thing
    /// is a machine somebody took". The book sells one now
    /// (LivingCity.Outfit.ArmoryCatalog.Motorcycles), which settles the first of those
    /// and leaves the second true and useful. A motorcycle is not a crew's transport
    /// and is never treated as one - a crew still gets to work in its car
    /// (CrewKit.HasVehicle has not moved) - it is a way for TWO of a crew's men to go
    /// past a rival's door with a gun and be somewhere else by the time the answer
    /// comes.
    ///
    /// So the order is deliberately small, and every part of it is a thing the crews
    /// already do. The men walk to the machine the way they walk to a car door
    /// (SendToDoor, with the same alternating straight-leg / pavement-graph fix behind
    /// it). They ride it the way the traffic's riders ride (BikePose). They shoot the
    /// way the men in a car's windows shoot (CrewBike.TickGuns, through this arena's
    /// own FireFrom). And when the pass is spent they come home and get off, because
    /// what the player asked for was ONE pass - past the door and away - not a siege
    /// on two wheels.
    ///
    /// What is new is only the join: who goes, how they get on, and what "home" means
    /// afterwards.
    /// </summary>
    public partial class DemoCrews
    {
        // ------------------------------------------------------------ the book's bikes

        /// <summary>The machines this deal stood, as against any a scene put down -
        /// the same split _ledgerCars keeps, and for the same reason: the book may take
        /// its own property off the street and must never take anybody else's.</summary>
        readonly HashSet<CrewBike> _ledgerBikes = new HashSet<CrewBike>();

        /// <summary>The motorcycle this crew owns per the ledger, or null.</summary>
        public CrewBike BikeOf(Unit unit)
        {
            if (unit == null) return null;

            // A CREW MAY OWN MORE THAN ONE MACHINE, so the question is not "which is the
            // first" but "which one can be sent". CrewDemo keeps a scene bike with two
            // men already on it and hands it to the outfit's crew (OwnTheOutfitBike), so
            // the first-match answer was that permanently occupied machine - and the
            // right-click card read "Somebody is on it" no matter how many the ledger
            // sold him, with the bought machine stood at the kerb doing nothing.
            //
            // Free first, then whatever is owned: the fallback is what lets the card say
            // WHY when every machine the crew has is out or occupied, instead of "no
            // motorcycle" when there plainly is one.
            CrewBike taken = null;
            for (int i = 0; i < Bikes.Count; i++)
            {
                var bike = Bikes[i];
                if (bike == null || bike.Owner != unit) continue;
                if (bike.Rider == null && bike.Pillion == null && RaidOf(bike) == null) return bike;
                if (taken == null) taken = bike;
            }
            return taken;
        }

        /// <summary>The ledger's motorcycles: bound to their crews, stood where the men
        /// who hold them stand, taken away when the book takes the keys back. Line for
        /// line the cars' deal (BindCars / StandLedgerCars) - the differences are that a
        /// bike stands EMPTY until somebody is sent on it, and that a machine whose crew
        /// is out on a raid is left exactly where it is.</summary>
        void BindBikes(Roster roster)
        {
            foreach (var bike in Bikes)
            {
                if (bike == null) continue;
                if (bike.ItemId < 0 && !_ledgerBikes.Contains(bike)) continue;  // the scene's own
                RosterEquipment item = null;
                foreach (var e in roster.Equipment)
                {
                    if (e.Kind != EquipmentKind.Motorcycle) continue;
                    if (bike.ItemId < 0 || e.Id == bike.ItemId) { item = e; break; }
                }
                if (item == null) { bike.Owner = null; continue; }
                bike.ItemId = item.Id;
                bike.DisplayName = string.IsNullOrEmpty(item.DisplayName)
                    ? "Motorcycle" : item.DisplayName;
                bike.Owner = OwnerFor(roster, item);
            }

            StandLedgerBikes(roster);
        }

        /// <summary>The two-wheelers the outfit OWNS, on their stands outside its own
        /// door - the cars' rule exactly (StandLedgerCars): every machine on the books,
        /// whoever holds the keys, parked at home. They queue along the same kerb the
        /// cars do, because it is the same search that finds the length of it.</summary>
        void StandLedgerBikes(Roster roster)
        {
            if (!LedgerCarsStand) return;

            // struck off the books - sold, or lost: off the street. A machine with men
            // ON it is not taken out from under them - the raid is called off first,
            // which puts them on their feet, and it goes on the next deal.
            for (int i = Bikes.Count - 1; i >= 0; i--)
            {
                var bike = Bikes[i];
                if (bike == null || !_ledgerBikes.Contains(bike)) continue;
                bool onBooks = false;
                foreach (var e in roster.Equipment)
                    if (e.Id == bike.ItemId && e.Kind == EquipmentKind.Motorcycle) { onBooks = true; break; }
                if (onBooks) continue;
                if (bike.Rider != null || bike.Pillion != null) { CallOffRaid(bike, "sold"); continue; }
                DropBike(bike);
            }

            var front = PlayerFront();
            foreach (var item in roster.Equipment)
            {
                if (item.Kind != EquipmentKind.Motorcycle) continue;
                if (Bikes.Exists(b => b != null && b.ItemId == item.Id)) continue;

                // outside the outfit's door. Only where there is no door at all - a demo
                // street with no fronts on it - does the old rule stand in: beside the
                // man the book dealt it to, or his lieutenant if that man is not out.
                CrewWalker man = null;
                Vector3 anchor;
                if (front != null) anchor = front.Door;
                else
                {
                    int keeper = CrewCars.KeeperOf(item);
                    if (keeper < 0) continue;                              // in the lock-up
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
                    WarnOnce("bikebody:" + item.DisplayName,
                        $"[Crews] no body for the ledger's '{item.DisplayName}' in any Synty " +
                        "vehicle folder.");
                    continue;
                }

                if (!CrewCars.MeasurePrefab(prefab, out float halfLength, out float halfWidth))
                    halfLength = halfWidth = 0.5f;
                halfWidth = Mathf.Max(0.42f, halfWidth);
                if (!CrewCars.KerbSlotNear(Net ?? LaneNet.Active, anchor,
                        halfLength, halfWidth, out var at, out var facing))
                {
                    WarnOnce("bikekerb:" + item.Id,
                        front != null
                            ? $"[Crews] nowhere to leave the outfit's {item.DisplayName} - " +
                              "no free kerb outside the front."
                            : $"[Crews] nowhere to leave {man.DisplayName}'s {item.DisplayName} - " +
                              "no free kerb near him.");
                    continue;
                }

                var bike = AddEmptyBike(prefab, at, facing, CarRoadY);
                if (bike == null) continue;
                bike.ItemId = item.Id;
                bike.DisplayName = string.IsNullOrEmpty(item.DisplayName)
                    ? "Motorcycle" : item.DisplayName;
                bike.Owner = OwnerFor(roster, item);
                _ledgerBikes.Add(bike);
                Debug.Log(front != null
                    ? $"[Crews] the outfit's {bike.DisplayName} is on its stand outside the front."
                    : $"[Crews] {man.DisplayName}'s {bike.DisplayName} is on its stand beside him.");
            }
        }

        /// <summary>A machine on its stand with nobody on it - what the book's purchase
        /// puts at the kerb. AddBike's opposite number: that one exists to put a rider
        /// and his mate on the road, this one exists to wait for two.</summary>
        public CrewBike AddEmptyBike(GameObject prefab, Vector3 pos, Quaternion rot, float roadY)
        {
            if (prefab == null) return null;
            var bike = RoadBike.Build<CrewBike>(prefab, _root, pos, rot, roadY, Net ?? LaneNet.Active);
            if (bike == null) return null;
            bike.Arena = this;
            if (!bike.PlaceAt(pos, rot * Vector3.forward)) bike.GoFree(pos);
            // STOOD, not merely standing still. StreetBikes.Park - the kerb-parking that
            // already works - halts hard before it settles the lean, and the difference
            // is whether the rest of the street reads the machine as parked or as a live
            // road user idling in a lane with nowhere to be. The second is a thing every
            // driver behind it queues on for ever.
            bike.Halt(hard: true);
            bike.SettleStand();
            // AND ON THE STREET'S OWN BOOKS. A vehicle nobody is told about is a
            // vehicle nobody gives way to: the machine stood at its kerb unregistered
            // and the traffic drove straight over it - a car sat five metres INSIDE the
            // parked machine for ninety-eight seconds while the belt refused its every
            // step. The car does this (AddCar) and so do the traffic's own two-wheelers
            // (StreetBikes); this was the one road user that did not.
            StreetTraffic.Users.Add(bike);
            Bikes.Add(bike);
            return bike;
        }

        void DropBike(CrewBike bike)
        {
            if (bike == null) return;
            CallOffRaid(bike, "taken off the street");
            bike.DismountAll();
            bike.Despawn();
            StreetTraffic.Users.Remove(bike);
            Bikes.Remove(bike);
            _ledgerBikes.Remove(bike);
            if (bike.Tf) Destroy(bike.Tf.gameObject);
        }

        // ------------------------------------------------------------ the order

        /// <summary>One crew's men away on one machine. Short-lived on purpose: it is
        /// made by the order, ticked, and dropped the moment the two of them are back on
        /// their feet - nothing here is state a crew carries about with it.</summary>
        sealed class Raid
        {
            public Unit Crew;
            public Unit Target;
            public CrewBike Bike;
            public CrewWalker Rider, Pillion;
            public Stage Step;
            public float StepAt;

            /// <summary>Seconds this step is allowed, measured off its own distance
            /// when the step began.</summary>
            public float StepBudget;

            /// <summary>Times the ride home has been asked for again after the machine
            /// parked short of the kerb it left.</summary>
            public int HomeTried;

            /// <summary>The nearest the machine has got to home on this leg, and when -
            /// the watchdog on a ride that has stopped getting anywhere.</summary>
            public float HomeNear = float.MaxValue, HomeNearAt;

            /// <summary>Whether it has already been told to give the spot up and pull in
            /// where it stands.</summary>
            public bool GaveUpTheSpot;

            public Vector3 Home;
        }

        enum Stage
        {
            /// <summary>The two of them walking to the machine.</summary>
            Walking,

            /// <summary>Past the door, guns going.</summary>
            Passing,

            /// <summary>The ride back to where it stood.</summary>
            Returning,
        }

        readonly List<Raid> _raids = new List<Raid>();

        // The patiences, and what they are FOR. None of them is a pace anybody is
        // expected to keep: the driving and the walking have their own detectors for a
        // thing that has stopped for good (RoadCar's carstuck, the walkers' walkstall),
        // and a leg that is merely slow is a leg in traffic. These are the outer bound -
        // the point past which a thing has not been slow, it has quietly stopped
        // happening, and two men are somewhere in the city with an order nobody can see.
        //
        // They are MEASURED OFF THE ERRAND, not fixed, because the errand varies by five
        // times inside one quarter: a mark a hundred and thirty metres off and a mark
        // two hundred and seventy-five metres off are the same order and not the same
        // journey. Fixed numbers were tried twice. Set by the arithmetic of distance over
        // top speed they fired on runs where nothing was wrong (a pass sat twenty seconds
        // behind one car at one light, which is what a street IS); set generously flat
        // they still cut the long ride home a hundred and fifty seconds into a
        // two-hundred-and-seventy-five-metre trip.

        /// <summary>Seconds of grace before the metres are counted - the standing about
        /// at either end of any errand.</summary>
        public static float PatienceBase = 60f;

        /// <summary>Metres a second the bound assumes - MEASURED, not guessed, off the
        /// traces of the headless loop, with a third again on top.
        ///
        /// A machine cruises at twelve on the way home and crosses a quarter at seven
        /// tenths of a metre a second of straight-line progress, because it stops at
        /// every light (DriverProfile.Getaway does not run reds - it was tried and it
        /// drove the machine into the back of the queue under them) and a quarter is
        /// mostly lights. Measured three times over, and it fell each time a fix changed
        /// how the machine drives; this is the pace of the machine as it now is. A man walks at one and
        /// eight tenths and covers under half a metre a second of straight-line distance,
        /// because the pavements go round the houses and his route is three sides of a
        /// block. Guessing these at two and eight tenths cut the ride home short at a
        /// hundred and nine seconds on a journey whose outward leg had just taken a
        /// hundred and fourteen - the bound was smaller than the measurement it was
        /// bounding.</summary>
        public static float RidingPace = 0.6f, WalkingPace = 0.35f;

        /// <summary>However far the errand, this is as long as anything waits. A machine
        /// abandoned three hundred metres away is a walk of that length on pavements, and
        /// the cap has to be able to contain one.</summary>
        public static float PatienceCap = 600f;

        static float Budget(float metres, float pace) =>
            Mathf.Min(PatienceCap, PatienceBase + Mathf.Max(0f, metres) / pace);

        /// <summary>Inside this, a man is walked straight at the machine rather than
        /// round the pavements to it - the last stretch, where the graph cannot help
        /// and can only get in the way.</summary>
        public const float StraightLegWithin = 30f;

        /// <summary>How near the machine a man has to be before he can get on it. A
        /// man's reach across a pavement, not a hand on a door handle: a machine is
        /// mounted from beside it and there is no door to open.</summary>
        public const float MountReach = 3f;

        /// <summary>Seconds a man may hold an order to the machine without getting any
        /// nearer to it before he is sent again, the other way. See
        /// <see cref="Arrive"/> - this is the whole difference between a man who walks
        /// to a motorcycle and a man who stands looking at one.</summary>
        public const float MountProgress = 4f;

        /// <summary>The nearest a man has yet got to the machine, and when. Cleared when
        /// he gets on it or the raid ends.</summary>
        readonly Dictionary<CrewWalker, (float near, float at)> _mountBest =
            new Dictionary<CrewWalker, (float, float)>();

        /// <summary>Frames a man has stood AT the machine and been refused a seat -
        /// a different thing from a walk that never arrived, and it was reported as
        /// one ("nobody got on the machine ... 0 m still to go").</summary>
        readonly Dictionary<CrewWalker, int> _mountRefused = new Dictionary<CrewWalker, int>();

        /// <summary>Why the last drive-by order was refused, or null - the overlay's
        /// line, exactly as CarRefusal is.</summary>
        public string DriveByRefusal { get; private set; }

        /// <summary>Whether the last refusal was about MEN rather than about the order.
        /// A crew that has run out of hoods to send is the game being a game; a crew
        /// with no machine, or a machine somebody is already on, is something else. The
        /// headless loop has to tell the two apart - one ends a run, the other fails
        /// it - and a caller must never do that by reading the prose.</summary>
        public bool DriveByShortHanded { get; private set; }

        /// <summary>Whether the selected crew could be sent at that one on the machine
        /// right now - what the right-click menu asks before it offers the row.
        ///
        /// The rule the player set: two hoods go; with only one hood left, the
        /// lieutenant goes with him; with no hoods at all, nobody goes. A crew's boss
        /// is worth more than a pass, and one man on a machine is a man who cannot
        /// shoot without stopping (CrewBike.TickGuns), which is not a drive-by.</summary>
        public bool CanDriveBy(Unit unit, Unit target) => Pick(unit, target, out _, out _) != null;

        /// <summary>Send the selected crew's two men at that crew on the machine. False
        /// with the reason on DriveByRefusal.</summary>
        public bool OrderDriveBy(Unit target)
        {
            DriveByRefusal = null;
            DriveByShortHanded = false;
            if (Selected == null || target == null) return false;

            var bike = Pick(Selected, target, out var rider, out var pillion);
            if (bike == null) return false;

            // A crew ordered onto the machine is a crew no longer walking anywhere as
            // one: the two who go are taken off whatever the crew was doing, and the
            // rest are left exactly as they were. That is the price of the order and it
            // is meant to be felt.
            var raid = new Raid
            {
                Crew = Selected,
                Target = target,
                Bike = bike,
                Rider = rider,
                Pillion = pillion,
                Step = Stage.Walking,
                StepAt = Time.time,
                StepBudget = Budget(Vector3.Distance(rider.Tf.position, bike.Position),
                                    WalkingPace),
                HomeNearAt = Time.time,
                Home = bike.Position,
            };
            _raids.Add(raid);

            bike.ClearShots();
            bike.Hot = false;
            SendToBike(rider, bike);
            SendToBike(pillion, bike);

            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", Selected.GangName);
                DriveTrace.Str(sb, "what", "drive-by ordered on " + target.GangName);
                DriveTrace.Str(sb, "bike", bike.DisplayName);
                DriveTrace.Str(sb, "rider", rider.DisplayName);
                DriveTrace.Str(sb, "pillion", pillion.DisplayName);
                DriveTrace.Num(sb, "toBike",
                    Vector3.Distance(rider.Tf.position, bike.Position));
                DriveTrace.Row("driveby", sb.ToString());
            }
            return true;
        }

        /// <summary>Who goes, on what - or null with the refusal written down. One
        /// function, so the menu's question and the order's answer can never disagree
        /// about whether a thing is possible.</summary>
        CrewBike Pick(Unit unit, Unit target, out CrewWalker rider, out CrewWalker pillion)
        {
            rider = pillion = null;
            // Cleared on the way IN, not only on the way out: the card reads this to
            // word a row it cannot offer, and a reason left over from the last question
            // asked is a reason about somebody else.
            DriveByRefusal = null;
            DriveByShortHanded = false;
            if (unit == null || unit.Faction != 0) return null;
            if (target == null || target == unit || target.Wiped)
            {
                DriveByRefusal = "Nobody to shoot at";
                return null;
            }

            var bike = BikeOf(unit);
            if (bike == null)
            {
                DriveByRefusal = "No motorcycle - buy one in the ledger";
                return null;
            }
            if (RaidOf(bike) != null)
            {
                DriveByRefusal = "The machine is already out";
                return null;
            }
            if (bike.Rider != null || bike.Pillion != null)
            {
                DriveByRefusal = "Somebody is on it";
                return null;
            }

            // Who is available: on his feet, not in a car, not riding anything. A man
            // in the crew's car is not pulled out of it for this - he would have to be
            // let down at a kerb first, and an order that quietly reshuffles a moving
            // car is the kind of thing that ends in a crew stood in the road.
            var hoods = new List<CrewWalker>();
            foreach (var man in unit.Hoods)
                if (Available(man)) hoods.Add(man);

            if (hoods.Count == 0)
            {
                DriveByRefusal = "No hood to send";
                DriveByShortHanded = true;
                return null;
            }

            // The rider is the best hand at the wheel, the pillion the best shot of
            // whoever is left - the same two stats the quartermaster deals guns and
            // wheels by, asked of the two men rather than of the stock.
            var roster = PersonnelDirector.Instance != null
                ? PersonnelDirector.Instance.Roster : null;
            Rank(roster, hoods, CharacterAttribute.Driving);
            rider = hoods[0];
            hoods.RemoveAt(0);

            if (hoods.Count > 0)
            {
                Rank(roster, hoods, CharacterAttribute.Firearms);
                pillion = hoods[0];
            }
            else if (Available(unit.Boss))
            {
                // One hood left on his feet: the boss rides behind him with the gun.
                // Behind him, not at the bars - a lieutenant does not chauffeur, and
                // the pillion is the man the whole machine exists to carry.
                pillion = unit.Boss;
            }
            else
            {
                rider = null;
                DriveByRefusal = "Only one man left standing";
                DriveByShortHanded = true;
                return null;
            }

            // NO SEATS-TWO GATE. There was one, and it refused the order outright for
            // any body whose wheelbase came in under a metre and five - which is how a
            // player who had bought a scooter off the counter and signed it out to a
            // crew got a card with one line on it and no reason given.
            //
            // SeatsTwo is a heuristic written for the TRAFFIC, to decide whether to put
            // a passenger on a passing machine, and its own comment says the packs'
            // bodies all have the room. What it was really standing in for here was the
            // two men sitting inside one another, and that was a geometry bug in the
            // saddle rather than a fact about the machine: the pillion's place is a
            // share of the wheelbase now (BikeBody.PillionBehindOfWheelbase), so a short
            // machine seats them apart instead of on top of each other. The counter does
            // not sell a machine that cannot do the job it is sold for.
            return bike;
        }

        static bool Available(CrewWalker man) =>
            man != null && man.Tf != null && !man.Dead && !man.Riding;

        /// <summary>Best at the stat first, ties by the book's own order - no draws, so
        /// the same crew always sends the same two men.</summary>
        static void Rank(Roster roster, List<CrewWalker> men, CharacterAttribute stat)
        {
            men.Sort((a, b) =>
            {
                int sa = HalfSteps(roster, a), sb = HalfSteps(roster, b);
                return sb != sa ? sb.CompareTo(sa) : a.CharacterId.CompareTo(b.CharacterId);
            });

            int HalfSteps(Roster book, CrewWalker man)
            {
                var member = book?.Find(man.CharacterId);
                return member == null ? 0 : member.GetHalfSteps(stat);
            }
        }

        /// <summary>Is this crew's machine out on a raid right now? What a caller
        /// waiting on one asks - the overlay, to keep the row off the card while the
        /// men are away, and the headless loop, to know when a pass is over.</summary>
        public bool RaidActive(Unit unit)
        {
            for (int i = 0; i < _raids.Count; i++)
                if (_raids[i].Crew == unit) return true;
            return false;
        }

        /// <summary>Is this man one of a raid's two - walking to the machine, riding
        /// it, or walking home from it? The tether must not touch him: hauling a man
        /// "back to his crew" while the raid is sending him to the mount cancelled
        /// the walk every scan, and the moto lane stalled at the kerb (mountstall,
        /// nineteen of the first twenty-seven seeds, the day the tether was born).</summary>
        bool OnRaid(CrewWalker man)
        {
            if (man == null) return false;
            for (int i = 0; i < _raids.Count; i++)
                if (_raids[i].Rider == man || _raids[i].Pillion == man) return true;
            return false;
        }

        /// <summary>Raids ridden out to a finish since the scene opened, of any ending -
        /// the count the loop's tally is taken from.</summary>
        public int RaidsFinished { get; private set; }

        /// <summary>Rounds fired off the machine on the LAST raid to finish, and
        /// whether the two of them ended it on their feet. Read after
        /// <see cref="RaidsFinished"/> moves.</summary>
        public int LastRaidShots { get; private set; }
        public bool LastRaidBothUp { get; private set; }

        Raid RaidOf(CrewBike bike)
        {
            for (int i = 0; i < _raids.Count; i++)
                if (_raids[i].Bike == bike) return _raids[i];
            return null;
        }

        /// <summary>Is this machine out on a raid? TickBikes asks before it decides what
        /// to do about a rider who has been shot.</summary>
        bool OnRaid(CrewBike bike) => RaidOf(bike) != null;

        /// <summary>The walk to the machine. A man gets on from beside it, so the point
        /// he is sent to is off its flank on the kerb side - never its own position,
        /// which would send him into it.</summary>
        void SendToBike(CrewWalker man, CrewBike bike)
        {
            if (man == null || bike == null) return;
            man.Disengage();
            // a man out running after somebody is called off it: he has a machine to get
            // to, and two orders on one man is one order nobody can see
            EndChase(man);
            _doorTries.Remove(man);
            SendToDoor(man, MountPoint(bike));
        }

        Vector3 MountPoint(CrewBike bike)
        {
            // The kerb side, the way a car finds it (CrewCar.KerbSideDir): the road's
            // right where the machine is on a road, its own right off one.
            var side = bike.Tf != null ? bike.Tf.right : Vector3.right;
            var kerb = bike.Road != null
                ? bike.Road.Right * (bike.D >= 0f ? 1f : -1f)
                : side;
            float away = Vector3.Dot(side, kerb) >= 0f ? 1f : -1f;
            var p = bike.Position + side.normalized * away * (bike.HalfWide + 0.9f);
            p.y = GroundY;
            return p;
        }

        // ------------------------------------------------------------ the frame

        void TickDriveBy(float dt)
        {
            for (int i = _raids.Count - 1; i >= 0; i--)
            {
                var raid = _raids[i];
                if (Over(raid)) { _raids.RemoveAt(i); continue; }

                switch (raid.Step)
                {
                    case Stage.Walking: TickWalking(raid); break;
                    case Stage.Passing: TickPassing(raid); break;
                    case Stage.Returning: TickReturning(raid); break;
                }
            }
        }

        /// <summary>The ways a raid stops being one, all of them ends rather than
        /// faults: the machine is gone, the man at the bars is shot (TickBikes puts
        /// them both on the road the same frame), the crew it belongs to is wiped out
        /// with nobody left to come home to.</summary>
        bool Over(Raid raid)
        {
            if (raid.Bike == null || raid.Bike.Tf == null) return true;

            // The mate shot on his way to the machine, before either of them is on it:
            // there is no drive-by left to ride - one man cannot shoot and steer - and
            // waiting out the patience for a dead man is three minutes of nothing.
            if (raid.Step == Stage.Walking && raid.Pillion != null &&
                (raid.Pillion.Tf == null || raid.Pillion.Dead))
            {
                Finish(raid, "his mate was shot on the way to it");
                return true;
            }

            if (raid.Rider != null && raid.Rider.Tf != null && !raid.Rider.Dead)
                return false;

            // THE MAN AT THE BARS IS SHOT AND THE MACHINE GOES DOWN WITH BOTH OF THEM.
            // The mate was made to take the bars for a while, on the reasoning that it
            // was the only ending which did not leave the two of them on the road at the
            // feet of the crew they had come to shoot. That reasoning was the player's to
            // make and he made it the other way: a motorcycle whose rider is shot at
            // fifty kilometres an hour does not change drivers, it falls over, and
            // whoever is on the back of it falls with it. Which is the price of sending
            // two men on one machine, and is meant to be felt.
            Finish(raid, "the rider is shot - the machine goes down");
            return true;
        }

        void TickWalking(Raid raid)
        {
            var bike = raid.Bike;
            var mount = MountPoint(bike);

            Arrive(raid.Rider, bike, mount, pillion: false);
            Arrive(raid.Pillion, bike, mount, pillion: true);

            if (bike.Rider != null && bike.Pillion != null)
            {
                bike.SinglePass = true;
                bike.Hot = false;
                bike.DriveBy(raid.Target);
                Step(raid, Stage.Passing, "riding",
                    Budget(Vector3.Distance(bike.Position, raid.Target.Position) +
                           CrewBike.PassOvershoot, RidingPace));
                return;
            }

            if (Time.time - raid.StepAt < raid.StepBudget) return;

            // Neither of them could get on it in the whole of the walk's budget. That is
            // a fault and is said as one - a silently abandoned order is the thing the
            // play loop exists to catch - and the men are put back on their feet where
            // they stand.
            _mountRefused.TryGetValue(raid.Rider, out int refusedRider);
            int refusedMate = 0;
            if (raid.Pillion != null) _mountRefused.TryGetValue(raid.Pillion, out refusedMate);
            float toGo = Vector3.Distance(raid.Rider.Tf.position, bike.Position);
            Fault(raid, refusedRider + refusedMate > 0 ? "mountrefused" : "mountstall",
                $"nobody got on the machine in {raid.StepBudget:F0}s ({toGo:F0} m still to " +
                $"go, refused {refusedRider}+{refusedMate} frames)");
            Finish(raid, "gave up on the machine");
            _raids.Remove(raid);
        }

        /// <summary>A man who has reached the machine gets on it. He may be stopped
        /// short of it by the crowd round it, and he may have been given no further
        /// order - the same two cases a car door has, answered the same way.</summary>
        void Arrive(CrewWalker man, CrewBike bike, Vector3 mount, bool pillion)
        {
            if (man == null || man.Tf == null || man.Dead || man.Riding) return;
            if (pillion && bike.Rider == null) return;   // the bars first, then behind

            var gap = man.Tf.position - mount;
            gap.y = 0f;
            float dist = gap.magnitude;
            if (dist > MountReach && !(!man.HasOrder && dist <= MountReach * 1.8f))
            {
                // HOLDING AN ORDER IS NOT THE SAME AS GETTING THERE. A man walked to
                // within eight metres of the machine, ran out of pavement (it was
                // abandoned in the carriageway, where a graph walk cannot follow), and
                // stood there Homing for three minutes with an order in hand - so the
                // "he has no order, send him again" rule never fired and the raid timed
                // out with the rider sat on the bike waiting for him. What matters is
                // whether he is getting NEARER; an order that is not doing that is an
                // order to be given again, the other way (SendToDoor alternates the
                // straight leg and the pavement graph, the car doors' own fix).
                if (man.HasOrder && Nearing(man, dist))
                    return;

                _doorTries.TryGetValue(man, out int tries);
                _doorTries[man] = tries + 1;
                _mountBest[man] = (dist, Time.time);

                // THE ALTERNATION HAS TO ACTUALLY ALTERNATE. SendToDoor takes the
                // pavement graph whenever the gap is over eight metres whatever it is
                // asked for, so past eight metres both of these orders were the same
                // order - and a man fourteen metres from a machine standing in the
                // carriageway was walked to the nearest pavement, which was fourteen
                // metres from it, again and again for the whole of his budget. Close
                // in, the straight leg is asked for BY NAME. Far out it is not: a
                // straight line across a quarter walks a man into the first building he
                // meets, which is the fault the pavements were put in to fix.
                if ((tries & 1) == 0 && dist < StraightLegWithin)
                    man.OrderToPoint(mount, Random.Range(0.2f, 0.6f));
                else
                    SendToDoor(man, mount, Random.Range(0.2f, 0.6f), graph: true);
                return;
            }

            if (!bike.Mount(man, pillion))
            {
                _mountRefused.TryGetValue(man, out int refused);
                _mountRefused[man] = refused + 1;
                if (refused == 60)
                    Debug.LogWarning($"[Crews] {man.DisplayName} is at the " +
                        $"{bike.DisplayName.ToLowerInvariant()} and it will not take him " +
                        $"{(pillion ? "on the pillion" : "at the bars")} - rider=" +
                        $"{(bike.Rider != null ? bike.Rider.DisplayName : "none")}, " +
                        $"seatsTwo={(bike.Body != null && bike.Body.SeatsTwo)}.");
                return;
            }
            _doorTries.Remove(man);
            _mountBest.Remove(man);
            _mountRefused.Remove(man);
            if (DriveTrace.On)
                DriveTrace.Event("driveby", man.DisplayName,
                    pillion ? "on the pillion" : "at the bars");
        }

        /// <summary>Is he still closing on the machine? True while his best distance is
        /// improving, or while the last improvement is recent enough to be worth
        /// waiting on.</summary>
        bool Nearing(CrewWalker man, float dist)
        {
            if (!_mountBest.TryGetValue(man, out var best))
            {
                _mountBest[man] = (dist, Time.time);
                return true;
            }
            if (dist < best.near - 0.5f)
            {
                _mountBest[man] = (dist, Time.time);
                return true;
            }
            return Time.time - best.at < MountProgress;
        }

        void TickPassing(Raid raid)
        {
            var bike = raid.Bike;
            if (bike.PassSpent || bike.DriveByTarget == null ||
                Time.time - raid.StepAt > raid.StepBudget)
            {
                if (!bike.PassSpent && bike.DriveByTarget != null)
                    Fault(raid, "passstall", "the pass never ran out in " +
                        $"{raid.StepBudget:F0}s ({Vector3.Distance(bike.Position, raid.Target.Position):F0} m short)");
                bike.SinglePass = false;
                // The ride home is the getaway and is driven as one - quick, in its own
                // lane, with air off the car in front (DriverProfile.Getaway). Cleared
                // again in Finish, so a machine on its stand is never hot.
                bike.Hot = true;
                // AND NOT BACK THE WAY IT CAME. Home is behind the mark, so the shortest
                // route home is a turn in the road and a second ride past the men who
                // have just been shot at - which is not a getaway, it is a second pass
                // nobody ordered. The turn is taken away for this leg only; the machine
                // rides on and comes home round the block.
                bike.NoTurnBack = true;
                bike.RideTo(raid.Home);
                Step(raid, Stage.Returning, "coming back",
                    Budget(Vector3.Distance(bike.Position, raid.Home), RidingPace));
            }
        }

        /// <summary>Seconds the machine may fail to get any nearer home before it is
        /// told to give up the spot it was aiming for. Well past a light cycle: a light
        /// lets go, and a taken parking space does not.</summary>
        public static float StuckSeconds = 45f;

        /// <summary>How near the kerb it set off from counts as back.</summary>
        public const float HomeReach = 25f;

        /// <summary>Times the ride home is asked for again when the machine parks short
        /// of it: a machine parks where there is ROOM, and room moves.</summary>
        public const int HomeTries = 3;

        void TickReturning(Raid raid)
        {
            var bike = raid.Bike;
            var gap = bike.Position - raid.Home;
            gap.y = 0f;

            // A MACHINE THAT HAS PARKED HAS FINISHED ITS JOURNEY. The question used to
            // be "is it within six metres of the exact spot it set off from", and the
            // answer was no even when it had driven the whole way back and pulled in -
            // because RideTo parks at the nearest kerb with ROOM on it, and the six
            // metres the machine left is often somebody else's by the time it returns.
            // So it stood parked, forty to seventy metres from an arbitrary point, until
            // the budget ran out and the raid was written off as a stall. Twenty-two of
            // the twenty-five failures in a thirty-run soak were that, and the driving
            // was not at fault in one of them.
            //
            // Parked short is asked again - a kerb frees up, a route opens - and after
            // three goes the men get off where the machine stopped, which is what men do.
            // A RIDE THAT HAS STOPPED GETTING ANYWHERE. The machine picks a length of
            // kerb to pull in at, and the kerb it picked can be taken by the time it
            // arrives: it then sits in PullIn against the car in the spot - overlapping
            // it, gap negative, speed zero - and holds that for the whole budget. Two
            // hundred and eighteen seconds of it, in the run that found this. A light
            // is not this (a light lets go), so the watchdog is set well past any light
            // cycle, and what it does is tell the machine to give the spot up and pull
            // in where it stands.
            float toHome = gap.magnitude;
            if (toHome < raid.HomeNear - 2f)
            {
                raid.HomeNear = toHome;
                raid.HomeNearAt = Time.time;
            }
            else if (!raid.GaveUpTheSpot && Time.time - raid.HomeNearAt > StuckSeconds)
            {
                raid.GaveUpTheSpot = true;
                raid.HomeNearAt = Time.time;
                bike.RideTo(bike.Position);
                return;
            }
            else if (raid.GaveUpTheSpot && Time.time - raid.HomeNearAt > StuckSeconds)
            {
                // Given up the spot and STILL going nowhere: the machine is wedged in a
                // jam it cannot ride out of. Left in the lane it holds the whole quarter
                // for the rest of the run - a soak found one stood at a red for 786s with
                // a street queued behind it. So it is hauled home onto its own off-street
                // stand and the raid ended there; the men finish on foot. A machine off
                // the carriageway blocks nobody, and the deadlock is broken at its root.
                if (bike != null)
                {
                    var fwd = bike.Tf != null ? bike.Tf.forward : Vector3.forward;
                    if (!bike.PlaceAt(raid.Home, fwd)) bike.GoFree(raid.Home);
                    bike.Halt(hard: true);
                    bike.SettleStand();
                }
                Finish(raid, "hauled off a jam it could not ride out of");
                _raids.Remove(raid);
                return;
            }

            bool settled = Time.time - raid.StepAt > 3f &&
                           bike.State == CrewBike.Mode.Parked;
            bool near = gap.sqrMagnitude < HomeReach * HomeReach;
            if (settled && !near && raid.HomeTried < HomeTries)
            {
                raid.HomeTried++;
                raid.HomeNear = float.MaxValue;
                raid.GaveUpTheSpot = false;
                bike.RideTo(raid.Home);
                return;
            }

            bool home = settled;
            bool late = Time.time - raid.StepAt > raid.StepBudget;
            if (!home && !late) return;

            // Late home is not a failed raid - the pass was ridden and the men are
            // alive - but it IS the driving taking too long over a plain errand, and
            // the loop wants to know about that.
            if (late && !home)
                Fault(raid, "homestall", $"never got back to the kerb in {raid.StepBudget:F0}s " +
                    $"({gap.magnitude:F0} m still to go)");
            Finish(raid, home
                ? (near ? "home" : $"stopped {gap.magnitude:F0} m short of the kerb it left")
                : "put down where it stopped");
            _raids.Remove(raid);
        }

        void Step(Raid raid, Stage step, string what, float budget)
        {
            raid.Step = step;
            raid.StepAt = Time.time;
            raid.StepBudget = budget;
            raid.HomeNear = float.MaxValue;
            raid.HomeNearAt = Time.time;
            raid.GaveUpTheSpot = false;
            if (!DriveTrace.On) return;
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "who", raid.Crew != null ? raid.Crew.GangName : "?");
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Num(sb, "budget", budget, "F0");
            DriveTrace.Row("driveby", sb.ToString());
        }

        /// <summary>Everybody off, the machine on its stand, the raid over. Called for
        /// every ending there is - done, given up, the rider shot - because a man left
        /// parented to a motorcycle is a man who has left the game.</summary>
        void Finish(Raid raid, string why)
        {
            var bike = raid.Bike;
            RaidsFinished++;
            LastRaidShots = bike != null ? bike.ShotsFired : 0;
            var crashed = raid.Rider != null && raid.Rider.Dead;
            if (bike != null)
            {
                bike.SinglePass = false;
                // A MACHINE ON ITS STAND IS NOT UNDER FIRE. Hot is set when the man at
                // the bars is shot and his mate rides out of it, and nothing cleared it
                // again - so from that raid on the machine drove hot for ever: through
                // reds, half a second off the car in front, on every run-up of every
                // later pass. That is the whole HotWithin split undone by one flag left
                // standing (it is what put "prof":"Hot" on a parked bike in the trace).
                bike.Hot = false;
                bike.HardStop();
                bike.DismountAll();
                // Down where it fell, or up on its stand. A machine whose rider was shot
                // off it is not parked; it is lying in the road, and the next two men
                // walk out to wherever that is (StandLedgerBikes leaves it be).
                if (!crashed) bike.SettleStand();
            }
            // AFTER they are off it: Standing asks whether a man is on his own feet,
            // and asking that while he is still sitting on the saddle answered no every
            // single time - a run where both men walked home read as a run that lost one.
            LastRaidBothUp = Standing(raid.Rider) && Standing(raid.Pillion);
            if (raid.Rider != null) { _mountBest.Remove(raid.Rider); _mountRefused.Remove(raid.Rider); }
            if (raid.Pillion != null) { _mountBest.Remove(raid.Pillion); _mountRefused.Remove(raid.Pillion); }
            Rejoin(raid.Rider);
            Rejoin(raid.Pillion);
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", raid.Crew != null ? raid.Crew.GangName : "?");
                DriveTrace.Str(sb, "what", "drive-by over: " + why);
                DriveTrace.Int(sb, "shots", LastRaidShots);
                DriveTrace.Bool(sb, "bothup", LastRaidBothUp);
                DriveTrace.Row("driveby", sb.ToString());
            }
        }

        /// <summary>A man set down off the machine is a man the pavement graph has in
        /// the wrong place - the same Reseat every man let out of a car gets - and a
        /// man with nothing to do, which in a city is a man stood in the road. He is
        /// put back on his own feet where he stands.</summary>
        void Rejoin(CrewWalker man)
        {
            if (man == null || man.Tf == null || man.Dead) return;
            Reseat(man);
            _doorTries.Remove(man);
            if (!man.HasOrder) man.OrderToPoint(man.Tf.position);
        }

        static bool Standing(CrewWalker man) =>
            man != null && man.Tf != null && !man.Dead && !man.Riding;

        void CallOffRaid(CrewBike bike, string why)
        {
            var raid = RaidOf(bike);
            if (raid == null) return;
            Finish(raid, why);
            _raids.Remove(raid);
        }

        void Fault(Raid raid, string kind, string what)
        {
            Debug.LogWarning($"[Crews] drive-by fault ({kind}): {what}");
            if (!DriveTrace.On) return;
            var sb = DriveTrace.Take();
            DriveTrace.Str(sb, "fault", kind);
            DriveTrace.Str(sb, "who", raid.Crew != null ? raid.Crew.GangName : "?");
            DriveTrace.Str(sb, "what", what);
            DriveTrace.Row("fault", sb.ToString());
        }
    }
}
