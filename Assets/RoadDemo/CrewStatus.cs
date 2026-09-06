namespace RoadDemo
{
    /// <summary>
    /// What a crew is doing, in the two or three words a chip has room for.
    ///
    /// The card that used to float over a selected lieutenant said it in a whole
    /// sentence - "On the move, heading north-east" - and it was withdrawn from the
    /// street (2026-09-02, the user's word). His chip on the top bar carries the fact
    /// now, in the same mono caps the chip already set the standing ORDER in, so the
    /// line says what the man IS DOING rather than what he was last told.
    ///
    /// The words are read off the live street - the lieutenant's own state and the car
    /// under him - and not off the order book: an order that has landed and a crew that
    /// has started walking are two different facts, and the chip prints the second.
    /// </summary>
    public static class CrewStatus
    {
        /// <summary>
        /// WHO CARRIES THE BAG, off the street rather than off the books: the man of
        /// this detail his lieutenant marked for the duty, and failing that the man who
        /// leads it - the lieutenant himself, then the first hood on his feet.
        ///
        /// One choke point: the collection sim walks the round through this man and the
        /// HUD names him, and two answers to "who is the collector" is two accounts of
        /// one detail.
        /// </summary>
        public static CrewWalker Carrier(DemoCrews.Unit unit)
        {
            if (unit == null)
                return null;

            var roster = LivingCity.Outfit.Underworld.Current?
                .Of(unit.Faction)?.Roster;
            if (roster != null)
                for (var i = 0; i < unit.Hoods.Count; i++)
                {
                    var hood = unit.Hoods[i];
                    if (hood == null || hood.Dead || hood.Tf == null)
                        continue;
                    // A character id of 0 is a REAL id in this project; a man the roster
                    // does not know is a null lookup, never a zero.
                    var man = roster.Find(hood.CharacterId);
                    if (man != null && !man.Gone &&
                        man.Duty == LivingCity.Personnel.Duty.Collector)
                        return hood;
                }

            // Match DemoCrews.MarchTo's lead choice exactly. A boarded hood may be
            // temporarily hidden before MarchTo unboards him, but he is still the man
            // assigned to this job and the bag appears with him when he steps out.
            if (unit.Boss != null && !unit.Boss.Dead && unit.Boss.Tf != null)
                return unit.Boss;
            for (var i = 0; i < unit.Hoods.Count; i++)
            {
                var hood = unit.Hoods[i];
                if (hood != null && !hood.Dead && hood.Tf != null)
                    return hood;
            }
            return null;
        }

        /// <summary>
        /// The same question for THE BAG DETAIL (GAN-262/273), which has no lieutenant
        /// of its own: <see cref="Short"/> reads the boss and a detachment's boss is
        /// null by construction, so a collector's line came out empty everywhere.
        ///
        /// The round comes first - it is the errand the detail exists for - then the
        /// door it is behind, then the fight it was pulled out for, and only then what
        /// the carrier's feet are doing. The map's tip, the marker over his head and the
        /// collector's own panel on the top bar all print this one word.
        /// </summary>
        public static string Bag(DemoCrews.Unit unit)
        {
            if (unit == null || !unit.IsDetachment)
                return null;
            if (unit.Wiped)
                return "DOWN";
            if (unit.InCustody)
                return "IN CUSTODY";

            if (TerritoryRuntime.Instance != null &&
                TerritoryRuntime.Instance.TryGetRound(unit.CrewId, out _, out _, out _))
                return "ON THE ROUND";

            // Indoors is not a state of his feet: his body is switched off inside one of
            // our own buildings and would otherwise read as OUTSIDE on the pavement he
            // walked in from.
            var indoors = CrewQuarters.Word(unit);
            if (indoors != null)
                return indoors;

            if (unit.TargetUnit != null)
                return "DEFENDING";

            var man = Carrier(unit);
            if (man == null)
                return "OUTSIDE";
            switch (man.State)
            {
                case CrewWalker.Mode.Walking:
                case CrewWalker.Mode.Striding:
                    return "ON THE MOVE";
                case CrewWalker.Mode.Homing:
                    return "ALMOST THERE";
                case CrewWalker.Mode.Engaging:
                    return "IN A FIGHT";
                case CrewWalker.Mode.Fleeing:
                    return "RUNNING";
                case CrewWalker.Mode.Riding:
                    return "IN THE CAR";
                default:
                    return "OUTSIDE";
            }
        }

        /// <summary>The word for this crew, or null for a crew with nobody in it - the
        /// caller then falls back to the standing order, which is all a crew off the
        /// street has.</summary>
        public static string Short(DemoCrews.Unit unit)
        {
            var boss = unit != null ? unit.Boss : null;
            if (boss == null)
                return null;
            if (boss.Dead)
                return "DOWN";

            // Custody keeps the crew's HUD identity after the body crosses the station
            // threshold. A later court/prison convoy supplies its moving position. This
            // is status, never authority: every command still refuses while InCustody.
            if (unit.InCustody)
            {
                var laterTransfer = unit.CustodyTracked && PoliceForce.Instance != null &&
                                    PoliceForce.Instance.CustodyInTransit(boss.CharacterId);
                return boss.Riding || laterTransfer ? "IN POLICE CAR" : "IN CUSTODY";
            }

            // Indoors is not a state of his feet - his body is switched off inside one
            // of our own buildings and would otherwise read as STANDING BY on the
            // pavement he walked in from (CrewQuarters).
            var indoors = CrewQuarters.Word(unit);
            if (indoors != null)
                return indoors;

            // LYING IN WAIT (COVER-006). Read off any man of the crew, not off the
            // lieutenant alone: the boss may be the one man the street had no flank for
            // and be crouched beside somebody else's, which is still an ambush.
            if (DemoCrews.AnyLurking(unit))
                return "LYING IN WAIT";

            var car = unit.Car;
            if (car != null)
                return car.ParkingFailed || car.HasGoal && car.ParkingReason.Length > 0 ? car.ParkingReason
                    : car.State == CrewCar.Mode.DriveBy ? "DRIVE-BY" : "IN THE CAR";
            if (unit.Boarding != null)
                return "GETTING IN";

            switch (boss.State)
            {
                case CrewWalker.Mode.Walking:
                case CrewWalker.Mode.Striding:
                    return "ON THE MOVE";
                case CrewWalker.Mode.Homing:
                    return "ALMOST THERE";
                case CrewWalker.Mode.Engaging:
                    return "IN A FIGHT";
                case CrewWalker.Mode.Fleeing:
                    return "RUNNING";
                case CrewWalker.Mode.Riding:
                    return "IN THE CAR";
                case CrewWalker.Mode.Dead:
                    return "DOWN";
                default:
                    // Standing. A man on his way off the street is running whatever his
                    // feet are doing, and a man who has heard shots is not simply idle.
                    return boss.Retreating ? "RUNNING" : boss.Alert ? "ON ALERT" : "STANDING BY";
            }
        }
    }
}
