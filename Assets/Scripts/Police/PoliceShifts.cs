namespace LivingCity.Police
{
    public enum PoliceWatch
    {
        Day,
        Night,
    }

    /// <summary>
    /// WHO IS OUT, AND WHEN. Two watches on the city's own clock, and the numbers each
    /// of them puts on the street - never more than the precinct actually has.
    ///
    /// The design intent is old (Docs/police-behaviour-plan.md, Faza 1: "smene i
    /// ritam - noću više kola, danju više peške"); nothing had ever read the clock.
    /// The rest of the epic depends on it: a replacement does not appear on the street,
    /// he appears INSIDE the station and walks out at the next handover, and there is
    /// no next handover unless somebody is keeping the watch.
    ///
    /// Pure: hours in, counts out. The walking in and out of doors is the scene's.
    /// </summary>
    public static class PoliceShifts
    {
        /// <summary>Which watch is on at this hour of the day (0-24, fractional).</summary>
        public static PoliceWatch At(float hour, PoliceRosterConfig config)
        {
            var day = config != null ? config.DayShiftHour : 7f;
            var night = config != null ? config.NightShiftHour : 19f;
            var h = Wrap(hour);
            day = Wrap(day);
            night = Wrap(night);

            // The day watch runs from its hour up to the night's. Written as a wrap
            // rather than a simple pair of comparisons because nothing says the day
            // watch has to start before the night one does, and a config that put the
            // handover at 22:00 and 06:00 must still name a watch for every hour.
            if (day <= night)
                return h >= day && h < night ? PoliceWatch.Day : PoliceWatch.Night;
            return h >= day || h < night ? PoliceWatch.Day : PoliceWatch.Night;
        }

        /// <summary>The hour this watch comes on.</summary>
        public static float StartOf(PoliceWatch watch, PoliceRosterConfig config)
        {
            var day = config != null ? config.DayShiftHour : 7f;
            var night = config != null ? config.NightShiftHour : 19f;
            return Wrap(watch == PoliceWatch.Day ? day : night);
        }

        /// <summary>Cars this precinct has on the road on this watch. At least one
        /// while it owns a car at all: a station with a car in the yard and nobody
        /// willing to drive it is not a shift pattern, it is a bug the player would
        /// read as the police having gone home.</summary>
        public static int CarsOnDuty(PoliceRoster roster, PoliceWatch watch, PoliceRosterConfig config)
        {
            if (roster == null)
                return 0;
            var share = watch == PoliceWatch.Night
                ? (config != null ? config.NightCarShare : 1f)
                : (config != null ? config.DayCarShare : 0.5f);
            return Portion(roster.Cars, share);
        }

        /// <summary>Men this precinct has walking on this watch.</summary>
        public static int FootOnDuty(PoliceRoster roster, PoliceWatch watch, PoliceRosterConfig config)
        {
            if (roster == null)
                return 0;
            var share = watch == PoliceWatch.Night
                ? (config != null ? config.NightFootShare : 0.5f)
                : (config != null ? config.DayFootShare : 1f);
            // Beat officers walk in PAIRS (PoliceBeat), so an odd man on duty is
            // a man with nobody beside him. Rounded down to an even number; a single
            // remaining officer stays inside until the pair is back up to strength.
            var out_ = Portion(roster.Officers, share);
            return out_ - (out_ % 2);
        }

        static int Portion(int held, float share)
        {
            if (held <= 0)
                return 0;
            if (share >= 1f)
                return held;
            if (share <= 0f)
                return 0;
            // Rounded UP, so half of three cars is two and not one: the city errs on the
            // side of having police in it.
            var n = (int)System.Math.Ceiling(held * (double)share);
            if (n < 1)
                n = 1;
            return n > held ? held : n;
        }

        static float Wrap(float hour)
        {
            var h = hour % 24f;
            return h < 0f ? h + 24f : h;
        }
    }

    /// <summary>Pure custody-car arithmetic shared by the force and its contracts.
    /// Two prisoners fit in a car, but an arrest may never empty the whole watch:
    /// one working car remains free for the next call.</summary>
    public static class CustodyPlan
    {
        public static bool RefusesOrders(bool inCustody) => inCustody;

        /// <summary>A routine transfer is never an escape because two actors briefly
        /// spread out while walking round a car. Only a destroyed carrier or a wiped
        /// escort breaks custody without an explicit resisting action.</summary>
        public static bool ShouldSpring(bool carrierWrecked, bool escortWiped) =>
            carrierWrecked || escortWiped;

        /// <summary>The stationary surrender pose belongs to a man who is still held,
        /// is not in a seat, and has not yet been led somewhere.</summary>
        public static bool ShouldRaiseHands(bool surrendered, bool riding, bool moving) =>
            surrendered && !riding && !moving;

        /// <summary>A prisoner enters a car only at its rear door and only while his
        /// named escort is physically beside him.</summary>
        public static bool CanSeatPrisoner(bool atRearDoor, bool escortBesideHim) =>
            atRearDoor && escortBesideHim;

        /// <summary>Every unbooked man outside a vehicle remains covered.</summary>
        public static bool MustCoverPrisoner(bool inCustody, bool booked, bool riding) =>
            inCustody && !booked && !riding;

        public static int CarsForPrisoners(int prisoners, int carsOnDuty)
        {
            if (prisoners <= 0 || carsOnDuty <= 1)
                return 0;
            var wanted = (prisoners + 1) / 2;
            return System.Math.Min(wanted, carsOnDuty - 1);
        }

        /// <summary>How many men fit in the cars actually assigned to this trip.  Any
        /// remainder stays with the arresting beat and takes the next run.</summary>
        public static int PrisonersThisTrip(int prisoners, int assignedCars)
        {
            if (prisoners <= 0 || assignedCars <= 0) return 0;
            return System.Math.Min(prisoners, assignedCars * 2);
        }
    }

    /// <summary>The visible procedure around GAN-315, kept engine-free so the exact
    /// live repro has executable contracts instead of being only a manual checklist.</summary>
    public static class PoliceProcedure
    {
        public const string UniformOfficerPrefabName = "SM_Chr_Officer_Male_01";
        public const float ComplaintDelayMinimum = 1.5f;
        public const float ComplaintDelayMaximum = 3.5f;
        public const float OfficerBoardingSeconds = 8f;
        public const bool RunToScene = true;

        /// <summary>A movement order while the arrest window is live is resistance,
        /// and converts the held aim into an ordinary combat engagement.</summary>
        public static bool ShouldOpenFireOnFlight(
            bool arrestInProgress, bool suspectMoved) =>
            arrestInProgress && suspectMoved;

        /// <summary>A shop statement is a physical fact: both the threshold crossing
        /// and the interview at the counter must have happened.</summary>
        public static bool CanRecordShopStatement(
            bool crossedThreshold, bool completedInterview) =>
            crossedThreshold && completedInterview;
    }
}
