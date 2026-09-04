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
    /// The police pickup carries at most eight people: two officers in the cab and up
    /// to six prisoners secured in the rear. When several cars are free one remains
    /// available for the next call; when only one is free, the custody already waiting
    /// is that call and must not be left at the kerb forever.</summary>
    public static class CustodyPlan
    {
        public const int PickupOccupantLimit = 8;
        public const int EscortSeats = 2;
        public const int PrisonersPerPickup = PickupOccupantLimit - EscortSeats;

        public static bool RefusesOrders(bool inCustody) => inCustody;

        /// <summary>The HUD/body anchor exists only while the city physically holds
        /// the man. Bail, an acquittal, a broken transfer and a missed appearance all
        /// put him back on the street; a served sentence is removed from the pipeline
        /// altogether and is handled by the same release edge.</summary>
        public static bool TracksStage(PrisonStage stage) =>
            stage == PrisonStage.Held ||
            stage == PrisonStage.ForTransfer ||
            stage == PrisonStage.InTransit ||
            stage == PrisonStage.Sentenced ||
            stage == PrisonStage.Serving;

        /// <summary>A transfer may remove a man from the street roster only after his
        /// own body has crossed the precinct threshold. A choreography timeout is not
        /// booking and must leave him visible and trackable in custody.</summary>
        public static bool CanBook(bool crossedStationThreshold) =>
            crossedStationThreshold;

        /// <summary>A live leg owns its steering memory until it finishes. The custody
        /// controller retries only an idle or genuinely stalled man who is still short
        /// of his destination; a timer alone never resets a route making progress.</summary>
        public static bool ShouldRetryBoarding(
            bool hasOrder, bool atDestination, bool retryElapsed,
            bool routeStalled = false) =>
            !atDestination && retryElapsed && (!hasOrder || routeStalled);

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

        public static int CarsNeeded(int prisoners)
        {
            if (prisoners <= 0) return 0;
            return (prisoners + PrisonersPerPickup - 1) / PrisonersPerPickup;
        }

        public static int CarsForPrisoners(int prisoners, int carsOnDuty)
        {
            if (prisoners <= 0 || carsOnDuty <= 0)
                return 0;
            var wanted = CarsNeeded(prisoners);
            var usable = carsOnDuty == 1 ? 1 : carsOnDuty - 1;
            return System.Math.Min(wanted, usable);
        }

        /// <summary>How many men fit in the cars actually assigned to this trip.  Any
        /// remainder stays with the arresting beat and takes the next run.</summary>
        public static int PrisonersThisTrip(int prisoners, int assignedCars)
        {
            if (prisoners <= 0 || assignedCars <= 0) return 0;
            return System.Math.Min(prisoners, assignedCars * PrisonersPerPickup);
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
        public const float CustodyCarStandOff = 3f;
        public const float CustodyEscortCarClearance = 3f;
        public const float CustodyStoppedDoorReach = 2.8f;
        public const bool ResponseCarsParkAtKerb = true;
        public const bool RunToScene = true;

        /// <summary>A response car may unload only after its road goal is complete and
        /// its body is genuinely at the kerb. Distance back to the shop is deliberately
        /// absent: the selected slot is already the closest free one and the officers
        /// finish the approach on foot.</summary>
        public static bool ResponseCarArrived(bool goalComplete, bool parkedAtKerb) =>
            goalComplete && parkedAtKerb;

        /// <summary>The dispatcher's meaning of nearest: the shortest overhead-map
        /// chord in X/Z. Roads, estimated vehicle speed and elevation never bias which
        /// free unit gets the call; walkers can cut directly across a street.</summary>
        public static float AirDistanceSquared(
            float ax, float az, float bx, float bz)
        {
            var x = ax - bx;
            var z = az - bz;
            return x * x + z * z;
        }

        /// <summary>A dispatched complaint cannot advance merely because its unit
        /// passed through a broad radius around the shop. The vehicle or beat must have
        /// completed its response route and reported a physical on-scene arrival.</summary>
        public static bool CanProcessComplaintArrival(bool unitOnScene) => unitOnScene;

        /// <summary>THE NEAREST PAIR COMES, WHEREVER IT IS (the user's rule, 2026-09-04).
        /// There is no reach a pair has to be inside to answer; past this many metres a
        /// car is sent with it, and a city with nobody free on foot sends the car alone.</summary>
        public const float FootResponseCarRange = 150f;

        /// <summary>Whether a car goes out beside the pair: the pair is farther than
        /// <see cref="FootResponseCarRange"/>, or there is no pair to send at all.</summary>
        public static bool CarJoinsFootResponse(bool anyFootFree, float footDistanceSquared) =>
            !anyFootFree ||
            footDistanceSquared > FootResponseCarRange * FootResponseCarRange;

        /// <summary>WHOEVER GETS THERE FIRST MAKES THE ARREST (the same rule). The pair
        /// and the car both stood at the scene: the one that arrived earlier puts the
        /// question, and a tie goes to the men on foot.</summary>
        public static bool FootArrivedFirst(float footArrivedAt, float squadArrivedAt) =>
            footArrivedAt <= squadArrivedAt;

        /// <summary>A PAIR THAT STOPS GETTING NEARER. Seconds without a metre of progress
        /// towards the scene after which the response is judged stuck: a scene on a
        /// roadway among parked cars or inside a yard has no spot to stand on at the
        /// point itself, and a pair retrying the same route for the rest of the campaign
        /// was a pair the city had lost.</summary>
        public const float FootResponseStallSeconds = 25f;

        /// <summary>Metres from the scene inside which a stuck pair counts as stood at it
        /// - as near as it can get, and near enough to put the question; farther out it
        /// is sent back and the next nearest pair is sent.</summary>
        public const float FootResponseCloseEnough = 45f;

        public static bool StalledOnTheWay(float secondsSinceProgress) =>
            secondsSinceProgress > FootResponseStallSeconds;

        public static bool StalledPairIsAtTheScene(float gapMetres) =>
            gapMetres <= FootResponseCloseEnough;

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
