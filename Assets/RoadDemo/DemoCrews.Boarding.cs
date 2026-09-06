using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The cars half of the crews: the ledger's cars stood at the kerb,
    /// the walk to a door, the seat behind it, the ride and the getting out, and
    /// the tow. Moved out of DemoCrews.cs whole; nothing changed in the move.</summary>
    public partial class DemoCrews
    {
        /// <summary>Stand a body for the ledger's car: parked here, bound to the
        /// roster's first vehicle on the next deal, owned by whoever the book says.</summary>
        public CrewCar AddCar(GameObject prefab, Vector3 position, Quaternion rotation, float roadY)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, new Vector3(position.x, roadY, position.z), rotation);
            go.name = "Outfit Car";
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) if (!CarBody.IsVisualRig(mb)) Destroy(mb);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            // the roads it drives: the scene's network (or the one the builder set as
            // active); off any road it stands on open ground
            var car = new CrewCar { RoadY = roadY, Net = Net ?? LaneNet.Active };
            // and WHAT it is: everywhere else a car reads its own machine off the name of
            // its body (RoadCar.Machine), but this one has just been renamed for the
            // hierarchy's sake and the prefab is still in hand, so hand it over
            car.Machine = LivingCity.Gameplay.VehiclePerformance.For(prefab.name);
            car.Attach(go.transform, prefab.name); // keep model identity after the hierarchy rename
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

        /// <summary>A man on his way to a car door he has been given a seat at - his
        /// order is the handle, and nothing else may be laid over it.</summary>
        static bool WalkingToDoor(Unit unit, CrewWalker man) =>
            unit != null && unit.Boarding != null && man != null &&
            unit.Boarding.SeatOf.ContainsKey(man);

        /// <summary>Is any man of this crew ON THE PAVEMENT - up, out of the car, and not
        /// walking to a door of it? What tells a crew that is riding from one that has
        /// left men behind.</summary>
        bool AnyOnFoot(Unit unit)
        {
            if (unit == null) return false;
            foreach (var man in unit.All())
                if (man != null && !man.Dead && man.Tf != null &&
                    !IsAboard(man) && !man.Riding && !WalkingToDoor(unit, man)) return true;
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
            if (CustodyRefuses(Selected)) { CarRefusal = InCustodyRefusal; return false; }
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
            if (CustodyRefuses(Selected)) { CarRefusal = InCustodyRefusal; return false; }
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
                CrewSpeech.Say(Selected, LivingCity.Data.VoiceLines.OrdOut);
                return true;
            }
            if (car.Occupant != null && car.Occupant != Selected)
            {
                CarRefusal = "The car is taken";
                return false;
            }
            Board(Selected, car);
            CrewSpeech.Say(Selected, LivingCity.Data.VoiceLines.OrdBoard);
            return true;
        }

        /// <summary>The selected crew out of its car, wherever the pointer is - the
        /// middle button's order: the car pulls in and lets them out; a crew still
        /// walking to its doors turns back. False when there is no car in it.</summary>
        public bool OrderOut()
        {
            if (Selected == null) return false;
            if (CustodyRefuses(Selected)) { CarRefusal = InCustodyRefusal; return false; }
            if (Selected.Car != null)
            {
                Disembark(Selected);
                CrewSpeech.Say(Selected, LivingCity.Data.VoiceLines.OrdOut);
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
            foreach (var man in unit.All()) _boardingDoor.Remove(man);
            unit.Boarding = null;
            // and with it whatever was deferred against the walk: a drive-by that was
            // to start when the last man was in has no last man now
            unit.PendingAttack = null;
        }

        /// <summary>How far off a door has to be before the walk to it is ROUTED rather
        /// than walked straight at. Nearer than this is across the pavement to the
        /// handle, and a route drawn for that only takes a man round his own car - the
        /// door is on the asphalt, so a way that is charged for the asphalt would rather
        /// go round it.</summary>
        const float DoorRouteFrom = 20f;

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
        void SendToCarDoor(CrewWalker man, CrewCar car, int seat, float delay = 0f, bool graph = false)
        {
            var door = car.DoorPoint(BoardingDoor(car, man, seat));
            SendToDoor(man, door, delay, graph);
        }

        /// <summary>The authoritative crew walk to a physical vehicle door, also used
        /// by police custody once it has locked the boarder's nearest door. Keeping the
        /// long-route/final-stride decision here prevents the two boarding systems from
        /// drifting back into different car approaches.</summary>
        internal void SendToVehicleDoor(CrewWalker man, Vector3 door,
            float delay = 0f, bool graph = false)
        {
            if (man == null || man.Dead || man.Tf == null) return;
            SendToDoor(man, door, delay, graph);
        }

        void SendToDoor(CrewWalker man, Vector3 door, float delay = 0f, bool graph = false)
        {
            var gap = door - man.Tf.position;
            gap.y = 0f;
            if ((graph || gap.sqrMagnitude > 8f * 8f) && (man.OnGraph || graph) && !FreeRoam &&
                gap.sqrMagnitude > 3f * 3f && NearestSidewalk(door, out var link, out float t))
            {
                Reseat(man);
                if (man.OnGraph)
                {
                    man.OrderTo(link, t, delay);
                    return;
                }
            }
            // OFF THE GRAPH, a long walk to a door used to be ONE STRAIGHT LEG. A mob
            // dealt at a shopfront never had a link (CrewWalker.OnGraph is false for
            // every man the scene stood rather than seated), so every graph branch above
            // was skipped and the men walked eighty-six metres diagonally down the middle
            // of a street to their car - which is the scene the pavements exist to stop,
            // and what the audit now calls "roadwalk". Routed instead, and the route is
            // charged for the carriageway (WalkRoute): pavement all the way, across at
            // the end. Close in it is still a straight leg - the door itself IS in the
            // road, and there is no walking round to a handle.
            if (gap.sqrMagnitude > DoorRouteFrom * DoorRouteFrom ||
                WalkObstacles.BlocksStanding(man.Tf.position, door, WalkRoute.ClearanceRadius))
            { man.OrderAcross(door, delay); return; }
            man.OrderToPoint(door, delay);
        }

        /// <summary>How many times a man has been sent to his door and not got there.
        /// Neither kind of order can be the only one tried: a straight leg wedges against
        /// whatever is between him and the handle (a man stood seven metres from his own
        /// car for 47 seconds, on the same paving stone, being sent again every second),
        /// and the graph puts him on the nearest pavement, which need not be the one the
        /// car is at. So they alternate, and one of the two always gets him there.</summary>
        readonly Dictionary<CrewWalker, int> _doorTries = new Dictionary<CrewWalker, int>();
        readonly Dictionary<CrewWalker, int> _boardingDoor = new Dictionary<CrewWalker, int>();

        /// <summary>The physical door a man uses to enter, which need not be the door
        /// beside the seat he will occupy. A driver dealt on the passenger side used to
        /// walk straight at the driver's handle THROUGH the car; live avoidance quite
        /// correctly kept him four metres away forever. He now takes the nearest door
        /// and moves across the cabin as the boarding pose hides him, while SeatOf still
        /// keeps the lieutenant in seat zero and every rider in the right riding pose.</summary>
        int BoardingDoor(CrewCar car, CrewWalker man, int seat)
        {
            if (_boardingDoor.TryGetValue(man, out int door) && door >= 0 && door < car.Seats)
                return door;
            door = Mathf.Clamp(seat, 0, Mathf.Max(0, car.Seats - 1));
            float best = float.MaxValue;
            for (int i = 0; i < car.Seats; i++)
            {
                var d = car.DoorPoint(i) - man.Tf.position;
                d.y = 0f;
                float ds = d.sqrMagnitude;
                if (ds >= best) continue;
                best = ds;
                door = i;
            }
            _boardingDoor[man] = door;
            return door;
        }

        void Board(Unit unit, CrewCar car)
        {
            CallOffRaids(unit, "a car order");
            NoteRetask(unit);
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
            unit.PendingAttack = null;
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
                _boardingDoor.Remove(man);
                SendToCarDoor(man, car, seat);
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
            if (!car.Moving && !car.ParkingFailed) return;
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
            if (FreeRoam || man == null || man.Tf == null || man.Dead || man.Riding) return;
            // walking the graph he IS where it has him; and a man within a pavement's
            // width of his metre is only on his own side of the walk
            if (man.GraphDriven) return;
            if (man.OnGraph && man.CurrentLink != null)
            {
                var cur = man.CurrentLink;
                var here = Vector3.Lerp(cur.From.Pos, cur.To.Pos,
                    Mathf.Clamp01(man.CurrentT / Mathf.Max(cur.Length, 0.01f)));
                var gap = man.Tf.position - here;
                gap.y = 0f;
                if (gap.sqrMagnitude < 2f * 2f) return;
            }
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
                ForgetRunDown(car);
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
                if (car.Occupant != null && !car.Occupant.Leaving &&
                    (car.Moving || car.ParkingFailed) && DriverDead(car))
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
                                _boardingDoor.Remove(man);
                                SendToCarDoor(man, car, seat);
                                anyOut = true;
                                continue;
                            }
                            int doorSeat = BoardingDoor(car, man, seat);
                            var door = car.DoorPoint(doorSeat);
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
                            if (dist <= 1.8f || atDoor) car.OpenDoorFor(doorSeat);
                            if (DriveTrace.On)
                            {
                                var sb = DriveTrace.Take();
                                DriveTrace.Str(sb, "who", man.DisplayName);
                                DriveTrace.Int(sb, "seat", seat);
                                DriveTrace.Num(sb, "toDoor", dist);
                                DriveTrace.Bool(sb, "order", man.HasOrder);
                                DriveTrace.Bool(sb, "open", car.DoorOpenFor(doorSeat));
                                DriveTrace.Bool(sb, "in", atDoor && car.DoorOpenFor(doorSeat));
                                DriveTrace.Str(sb, "state", man.State.ToString());
                                DriveTrace.Vec(sb, "p", man.Tf.position);
                                DriveTrace.Vec(sb, "door", door);
                                DriveTrace.Row("board", sb.ToString());
                            }
                            if (atDoor && car.DoorOpenFor(doorSeat))
                            {
                                car.Aboard.Add(man);
                                _doorTries.Remove(man);
                                _boardingDoor.Remove(man);
                                man.Disengage();
                                man.SetRiding(true);
                                car.CloseDoorFor(doorSeat);
                                car.Occupant = unit;
                                unit.Car = car;
                                TakeCar(unit, car);
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
                                    SendToCarDoor(man, car, seat, Random.Range(0.2f, 0.6f), graph: (tries & 1) == 1);
                                }
                            }
                        }
                        if (!anyOut)
                        {
                            var mark = unit.PendingAttack;   // Unboard clears it
                            Unboard(unit, "everybody in");
                            // everybody in: the drive the player ordered while they were
                            // still climbing aboard goes now
                            if (unit.PendingDrive.HasValue && unit.Car == car && !DriverDead(car))
                            {
                                unit.Leaving = false;
                                car.DriveTo(unit.PendingDrive.Value);
                            }
                            unit.PendingDrive = null;

                            // and the KILL ordered while they were climbing aboard: the
                            // drive-by starts now, with the whole crew in it. A mark that
                            // died in the meantime is no job at all; no driver, and the
                            // job is done on foot, which is what an attack order does to
                            // a crew whose man at the wheel is dead.
                            if (mark != null && !mark.Wiped && unit.Car == car)
                            {
                                unit.TargetUnit = mark;
                                unit.OrderedFight = true;
                                unit.SawEnemyAt = Time.time;
                                if (DriverDead(car)) Disembark(unit);
                                else { unit.Leaving = false; car.DriveBy(mark); }
                            }
                        }
                    }
                }

                // a crew told to get out waits for the kerb, then each man for his door
                if (car.Occupant != null && car.Occupant.Leaving && !car.Moving &&
                    (!car.ParkingFailed || car.EngineDead || DriverDead(car)))
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
                                // Driver-loss shock is temporary too; the explicit
                                // comeBack run must not be rewritten as desertion.
                                man.PanicFrom(man.LastAttacker, car.Position,
                                    15f, 25f, comeBack: true);
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
                    man.PlaceInCar(car.Body, seat, rot);
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

        // The ordinary street fight is over when every survivor has broken, but an
        // ordered drive-by is not: a living runner is still a man the riders can see and
        // shoot on the next pass. It ends only when nobody remains on the crew's books;
        // a retreat that really gets off the street is removed by TakeOffRetreated and
        // therefore satisfies the same rule. Distance is deliberately not in it: a
        // drive-by ordered on a crew at the far end is a long drive, not a finished job.
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
            if (target == null || target.Wiped)
            {
                car.TargetDone(); // the job is done: on a little and in at the kerb
                foreach (var man in car.Aboard) man.RidingAim = false;
                car.CloseAllWindows();
                return;
            }
            foreach (var man in car.Aboard)
            {
                car.SeatOf.TryGetValue(man, out int seat);
                if (man.Dead || !man.Carrying || !car.CanFireFromSeat(seat)) { man.RidingAim = false; car.SetWindow(seat, false); continue; }
                // guns out for the whole run-in, not at the instant a window comes down:
                // the car has a crew to shoot at, and men who draw as the glass drops
                // read as men conjuring pieces out of the air. It also keeps their own
                // tick from putting them away between one pass and the next.
                man.DrawGun();
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
                    DriveTrace.Bool(sb, "armed", man.Carrying);
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
                var due = GunCadence.Advance(ref timer, dt, man.Ballistics.Interval);
                _windowTimers[man] = timer;
                for (int i = 0; i < due.Count; i++)
                    QueueRound(man, mark, man.MuzzlePosition, car.Position,
                        CrewArms.MuzzleOf(man.Weapon) ?? car.Tf, due.At(i));
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
                        owner = Units.Find(u => u.Faction == 0 && !u.IsDetachment && u.CrewId == crew.Id);
                }
                if (owner != car.Owner && car.Occupant != null && car.Occupant != owner)
                    Disembark(car.Occupant); // the book took the keys away mid-ride
                car.Owner = owner;
            }

            StandLedgerCars(roster);
        }

        /// <summary>A CAR BELONGS TO WHOEVER IS SITTING IN IT. The moment a man of one
        /// outfit closes the door of another's motor, the motor is that outfit's - which
        /// is what taking a car means, and the only thing that makes a car left at a kerb
        /// worth anything to anybody.
        ///
        /// The ledger has to be told, or it undoes the theft within the second. A rival
        /// driving off our car while the book still lists it is a book that hands the keys
        /// straight back (BindCars re-derives car.Owner from the item's holder) AND stands
        /// a brand new one outside our front to replace it (StandLedgerCars) - the same
        /// pair of wrongs a burnt-out machine used to cause (BurntOut). So a car taken
        /// FROM us is struck off, and a car taken BY us is written on, under the
        /// catalogue's own name for it so the book and the armory agree.</summary>
        void TakeCar(Unit unit, CrewCar car)
        {
            if (unit == null || car == null || car.Owner == unit) return;
            var from = car.Owner;
            car.Owner = unit;
            if (car.Civic) return;   // the law's own car is not property anybody books

            var director = PersonnelDirector.Instance;
            var roster = director != null ? director.Roster : null;
            var taken = SeizedAs(car);

            if (car.ItemId >= 0 && unit.Faction != 0)
            {
                // THEY HAVE IT NOW. Off the books, out of the standing list.
                if (roster != null) RosterOps.LoseItem(roster, car.ItemId);
                car.ItemId = -1;
                _ledgerCars.Remove(car);
                if (from == null || from.Faction == 0)
                    CrewOverlay.Announce(
                        $"{unit.GangName.ToUpperInvariant()} HAVE TAKEN THE {taken.DisplayName.ToUpperInvariant()}",
                        4f, new Color(1f, 0.55f, 0.45f));
            }
            else if (car.ItemId < 0 && unit.Faction == 0 && roster != null)
            {
                // AND NOW IT IS OURS. On the books, in the hands of the crew that took it.
                var crew = roster.FindCrew(unit.CrewId);
                if (crew != null)
                {
                    var item = RosterOps.AddEquipment(
                        roster, EquipmentKind.Vehicle, taken.DisplayName, taken.Price);
                    item.OwnerId = crew.LieutenantId;
                    item.HolderId = crew.LieutenantId;
                    car.ItemId = item.Id;
                    car.DisplayName = taken.DisplayName;
                    _ledgerCars.Add(car);
                    CrewOverlay.Announce(
                        $"{taken.DisplayName.ToUpperInvariant()} TAKEN" +
                        (from != null ? " FROM " + from.GangName.ToUpperInvariant() : ""),
                        4f, new Color(0.72f, 0.95f, 0.72f));
                }
            }

            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "who", unit.GangName);
                DriveTrace.Str(sb, "what", "took a car" + (from != null ? " off " + from.GangName : ""));
                DriveTrace.Str(sb, "car", taken.DisplayName);
                DriveTrace.Int(sb, "item", car.ItemId);
                DriveTrace.Row("crewcar", sb.ToString());
            }
        }

        /// <summary>What a car standing in the street is called and worth once somebody
        /// puts it on his books: the catalogue listing whose name is in the body's own
        /// (SM_Veh_Sedan_01 is a Sedan), or the jalopy - which is what a car taken off a
        /// kerb is worth when nobody can say what it is.</summary>
        static LivingCity.Outfit.ArmoryItem SeizedAs(CrewCar car)
        {
            var listings = LivingCity.Outfit.ArmoryCatalog.Vehicles;
            var body = car != null && car.DisplayName != null ? car.DisplayName : "";
            for (int i = 0; i < listings.Length; i++)
            {
                // the LAST word of the listing is the body: "Panel Van" is a van,
                // "Armoured Wagon" a wagon - which is what a prefab's name carries
                var words = listings[i].DisplayName.Split(' ');
                var key = words[words.Length - 1];
                if (body.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0) return listings[i];
            }
            return listings[0];
        }

        /// <summary>Which crew's man drives a vehicle on the books, or null.</summary>
        Unit OwnerFor(Roster roster, RosterEquipment item)
        {
            int keeper = CrewCars.KeeperOf(item);
            if (keeper < 0) return null;
            var crew = roster.CrewOf(keeper);
            return crew == null ? null : Units.Find(u => u.Faction == 0 && !u.IsDetachment && u.CrewId == crew.Id);
        }

        /// <summary>The outfit's own door - the kerb every car on the books is parked
        /// at. Null before the families are seated, and in the demo scenes that stand no
        /// fronts at all; the caller then falls back to the man who holds the keys.</summary>
        public static GangFront PlayerFront() =>
            FrontOf(LivingCity.Gameplay.PlayerCommands.House.Value);

        /// <summary>One family's own door. Null before the families are seated, in the
        /// demo scenes that stand no fronts at all, and for anybody who is not a family
        /// (the law).</summary>
        public static GangFront FrontOf(int gangId)
        {
            if (gangId < 0) return null;
            var all = GangFront.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].GangId == gangId)
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
                if (front != null) anchor = front.Outside;
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
                if (unit.Car == car) { unit.Car = null; unit.PendingDrive = null; unit.PendingAttack = null; }
                if (unit.Boarding == car) Unboard(unit, "the car was taken off the street");
            }
            car.Despawn();
            StreetTraffic.Users.Remove(car);
            Cars.Remove(car);
            _ledgerCars.Remove(car);
            ForgetRunDown(car);
            if (car.Tf) Destroy(car.Tf.gameObject);
        }
    }
}
