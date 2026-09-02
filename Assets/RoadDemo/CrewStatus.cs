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

            // Indoors is not a state of his feet - his body is switched off inside one
            // of our own buildings and would otherwise read as STANDING BY on the
            // pavement he walked in from (CrewQuarters).
            var indoors = CrewQuarters.Word(unit);
            if (indoors != null)
                return indoors;

            var car = unit.Car;
            if (car != null)
                return car.State == CrewCar.Mode.DriveBy ? "DRIVE-BY" : "IN THE CAR";
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
