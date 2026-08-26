namespace LivingCity.Gameplay
{
    /// <summary>
    /// What a body can DO, as against who is driving it. The two were the same thing
    /// for as long as there has been traffic in this city: every car on the road read
    /// its pace, its pull and its grip off a DriverProfile, so a delivery lorry and a
    /// supercar with the same commuter at the wheel pulled away from a light together,
    /// took the same bend at the same speed, and stopped in the same distance.
    ///
    /// A DriverProfile is thresholds and permissions - how fast he MEANS to go, what
    /// he is willing to do to get past you. This is the machine underneath him, and it
    /// is three multipliers on what the profile asks for:
    ///
    ///   Top   the pace, scaled. A LANE LIMIT STILL APPLIES to anyone who keeps to
    ///         limits, and on a 9 m/s high street that is the binding number for
    ///         everything ordinary - which is correct, and is why the quick end of
    ///         this table is carried by Pull and Grip rather than by Top. A quick
    ///         body shows its top end where the road allows one: the belt, a deck,
    ///         a boulevard, and in the hands of anybody who ignores limits.
    ///   Pull  the acceleration. NOTHING caps this, so it is the difference the
    ///         player actually sees: the lorry still gathering pace two lengths
    ///         after the muscle car has finished.
    ///   Grip  the brakes and the lateral acceleration together - how late it can
    ///         stop and how hard it may take a bend. Kept as ONE number on purpose:
    ///         they are the same four contact patches, and a body that corners like
    ///         a barge does not stop like a sports car either.
    ///
    /// Engine-free, and keyed by pack prefab name exactly as <see cref="VehicleCatalog"/>
    /// is, so the headless suite can hold the table to its own rules and so a car
    /// reaches its machine without anybody having to remember to hand it one.
    /// </summary>
    public static class VehiclePerformance
    {
        /// <summary>One body's envelope, as multipliers on the driver's own numbers.</summary>
        public readonly struct Machine
        {
            public readonly float Top;
            public readonly float Pull;
            public readonly float Grip;

            public Machine(float top, float pull, float grip) { Top = top; Pull = pull; Grip = grip; }
        }

        /// <summary>What an unlisted body is: the driver's numbers, unaltered. Every
        /// pace in DriverProfile was tuned against a saloon, so a saloon must come out
        /// of this table driving exactly as it drove before the table existed.</summary>
        public static readonly Machine Ordinary = new Machine(1f, 1f, 1f);

        /// <summary>The bounds every entry is held inside. The top end is DELIBERATELY
        /// tighter than the pull: stopping distance goes as Top² over Grip, and the
        /// getaway profiles already run close to what the belt will accept (see
        /// DriverProfile.Getaway, which records the run where a motorcycle went into
        /// the back of a car). A body may be a third quicker off the mark than a
        /// saloon; it may not be a third faster into a junction.</summary>
        public const float MinTop = 0.30f, MaxTop = 1.25f;
        public const float MinPull = 0.30f, MaxPull = 1.70f;
        public const float MinGrip = 0.70f, MaxGrip = 1.30f;

        /// <summary>One row of the table.</summary>
        public readonly struct Entry
        {
            public readonly string Name;
            public readonly Machine Machine;

            public Entry(string name, float top, float pull, float grip)
            {
                Name = name;
                Machine = new Machine(top, pull, grip);
            }
        }

        /// <summary>Every body the game puts on a road, and what it is worth.
        ///
        /// Read down the Pull column rather than the Top one - that is the column the
        /// street shows. The heavy end is a lorry taking four seconds to reach a pace
        /// a saloon reaches in two; the light end is a moped that never reaches it at
        /// all, because its Top runs out first.
        ///
        /// A name matches whole, or as the stem of a preset ("SM_Veh_Sedan_01" answers
        /// for "SM_Veh_Sedan_01_Preset_Taxi"), so the presets need no rows of their own
        /// unless they differ - the two that do are the police pair, which are the same
        /// shells with the engines the force pays for.
        ///
        /// THE ORDINARY BAND RUNS DOWN FROM 1, and that is not modesty. The commuter
        /// profile means to do 10 m/s and a city street allows 9, so a top end ABOVE 1
        /// is invisible in the traffic - it is capped away at every kerb in the city.
        /// A body under 0.9 is not: it is a van sitting at eight where the saloons sit
        /// at nine, which is the picture the player asked for. So the quick end of the
        /// table earns its keep in the pull and the grip, and the top column is written
        /// as "how far under the limit does this body sit", with 1 meaning "at it".
        /// </summary>
        public static readonly Entry[] Table =
        {
            // --------------------------------------------------------- the heavy end
            new Entry("SM_Veh_Truck_01",            0.70f, 0.42f, 0.75f),
            new Entry("SM_Veh_Truck_Delivery_01",   0.72f, 0.45f, 0.75f),
            new Entry("SM_Veh_Truck_Garbage_01",    0.66f, 0.40f, 0.72f),
            new Entry("SM_Veh_Bus_01",              0.75f, 0.50f, 0.75f),
            new Entry("SM_Veh_SchoolBus_01",        0.73f, 0.48f, 0.75f),
            new Entry("SM_Veh_Firetruck_01",        0.80f, 0.55f, 0.72f),
            new Entry("SM_Veh_Forklift_01",         0.30f, 0.40f, 0.80f),

            // --------------------------------------------------------- working bodies
            new Entry("SM_Veh_Van_01",              0.82f, 0.72f, 0.88f),
            new Entry("SM_Veh_Car_Van_01",          0.82f, 0.72f, 0.88f),
            new Entry("SM_Veh_Pickup_01",           0.88f, 0.85f, 0.90f),
            new Entry("SM_Veh_Suv_01",              0.92f, 0.88f, 0.90f),

            // --------------------------------------------------------- ordinary cars
            // Left at 1.0 by name rather than by omission: a table that says nothing
            // about the commonest car in the city is a table nobody can check.
            new Entry("SM_Veh_Sedan_01",            1.00f, 1.00f, 1.00f),
            new Entry("SM_Veh_Car_Sedan_01",        1.00f, 1.00f, 1.00f),
            new Entry("SM_Veh_Car_01",              1.00f, 1.00f, 1.00f),
            new Entry("SM_Veh_Car_02",              1.00f, 1.00f, 1.00f),
            new Entry("SM_Veh_Car_Medium_01",       0.96f, 0.95f, 1.00f),
            new Entry("SM_Veh_Car_Taxi_01",         0.94f, 0.95f, 0.98f),
            new Entry("SM_Veh_Car_Small_01",        0.86f, 0.85f, 1.00f),
            new Entry("SM_Veh_Convertable_01",      1.02f, 1.10f, 1.05f),

            // --------------------------------------------------------- the quick end
            new Entry("SM_Veh_LowCar_01",           1.10f, 1.20f, 1.10f),
            new Entry("SM_Veh_LowCar_02",           1.10f, 1.20f, 1.10f),
            new Entry("SM_Veh_Car_Muscle_01",       1.18f, 1.45f, 1.05f),
            new Entry("SM_Veh_Supercar_01",         1.25f, 1.65f, 1.28f),
            new Entry("SM_Veh_Supercar_02",         1.25f, 1.70f, 1.30f),
            new Entry("SM_Veh_Buggy_01",            0.92f, 1.15f, 1.15f),

            // --------------------------------------------------------- the law's own
            // Faster than the shell they are built on, and no faster than a supercar:
            // a cruiser catches ordinary traffic, not a man who has bought his way out
            // of being caught. What the force really has over the street is the crown,
            // the far lane and the red, and those are the driver's (DriverProfile.Police).
            new Entry("SM_Veh_Sedan_01_Preset_Police",  1.08f, 1.18f, 1.08f),
            new Entry("SM_Veh_Pickup_01_Preset_Police", 0.96f, 0.95f, 0.92f),
            new Entry("SM_Veh_Car_Police_01",           1.08f, 1.18f, 1.08f),

            // --------------------------------------------------------- two wheels
            // A motorcycle's honest advantages in this model are the pull and the
            // stopping - it does NOT filter between lanes here (RoadBike says so where
            // it is built), so a high top speed would buy it nothing but a longer
            // stopping distance behind the same queue. Grip is where a bike is worth
            // having, and Getaway's own brake figure was chosen against a car's.
            new Entry("SM_Veh_Motorbike_01",        1.12f, 1.50f, 1.25f),
            new Entry("SM_Veh_Motorbike_02",        1.08f, 1.30f, 1.20f),
            new Entry("SM_Veh_Moped_01",            0.60f, 0.62f, 1.05f),
            new Entry("SM_Veh_Scooter_01",          0.55f, 0.55f, 1.05f),
            new Entry("SM_Veh_Quad_Bike_01",        0.78f, 0.95f, 1.00f),
            new Entry("SM_Veh_Bike_01",             0.32f, 0.30f, 1.00f),

            // --------------------------------------------------- the outfit's own bodies
            // Three listings on the armory counter play a body this project MADE out of
            // a pack one (ArmoryCatalog.BodyFor), and two of them need rows of their own
            // or the stem match answers with the wrong machine.
            //
            // The wagon is Palm City's SUV with plate on the doors, bars on the glass and
            // a ram on the nose. It would otherwise be answered by "SM_Veh_Suv_01" and
            // drive like the school-run car it was, which is the one thing six thousand
            // dollars of armour cannot be: it is the slowest thing the outfit can buy and
            // the hardest to stop, and the counter's own line says so ("every eye on the
            // street").
            new Entry("SM_Veh_Suv_01_Armoured",     0.78f, 0.62f, 0.80f),
            // The tourer is the police pack's big machine with the force cut off it. It
            // matches NO stem at all ("SM_Veh_Motorbike_Tourer_Black" is not
            // "SM_Veh_Motorbike_01_..."), so without this row the machine the counter
            // sells for a job across town drove like a saloon.
            new Entry("SM_Veh_Motorbike_Tourer_Black", 1.10f, 1.25f, 1.18f),
            // The boxless moped needs no row: it IS the pack's moped with the delivery
            // box taken off, and "SM_Veh_Moped_01_NoBox" is answered by the moped's stem,
            // which is the right answer rather than a lucky one.
        };

        /// <summary>The machine this prefab is - by asset path, by bare name, or by the
        /// name a scene gave an instance of it ("(Clone)" and the frame suffix a bike
        /// pivot carries are both taken off). An unlisted body comes back
        /// <see cref="Ordinary"/>, which drives exactly as everything drove before this
        /// table existed - so a pack nobody has written a row for is never broken by it,
        /// only unremarkable.</summary>
        public static Machine For(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return Ordinary;

            var name = VehicleCatalog.BareName(nameOrPath);

            // whole name first: a preset with a row of its own must beat its own stem
            foreach (var entry in Table)
                if (name == entry.Name) return entry.Machine;

            // then the stem, longest first, so "SM_Veh_Sedan_01_Preset_Police" is not
            // answered by "SM_Veh_Sedan_01" while its own row is sitting in the table
            var best = Ordinary;
            int bestLength = 0;
            foreach (var entry in Table)
                if (entry.Name.Length > bestLength && name.StartsWith(entry.Name + "_"))
                {
                    best = entry.Machine;
                    bestLength = entry.Name.Length;
                }
            return best;
        }

        /// <summary>Whether this body has a row of its OWN, as against being answered by
        /// a stem or falling through to <see cref="Ordinary"/>. What it is for is the
        /// test that holds the table to <see cref="VehicleCatalog"/>: every body the game
        /// names as drivable - the outfit's cars, the two-wheelers, the law's fleet - must
        /// be written down here rather than merely not break.</summary>
        public static bool Lists(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return false;
            var name = VehicleCatalog.BareName(nameOrPath);
            foreach (var entry in Table)
                if (name == entry.Name) return true;
            return false;
        }

        /// <summary>Whether every row sits inside the bands above: the one rule the
        /// table has to keep, and the reason a stopping distance cannot quietly double
        /// because somebody liked the sound of a number.</summary>
        public static bool InBand(in Machine machine) =>
            machine.Top >= MinTop && machine.Top <= MaxTop &&
            machine.Pull >= MinPull && machine.Pull <= MaxPull &&
            machine.Grip >= MinGrip && machine.Grip <= MaxGrip;
    }
}
