namespace LivingCity.Police
{
    /// <summary>
    /// Every dial the police force turns on, in ONE place: how long the department
    /// takes to fill a hole, and how much of a precinct is out at which hour.
    ///
    /// A plain class rather than a ScriptableObject on purpose - the roster arithmetic
    /// is headless-testable, and a test that had to load an asset to ask when the night
    /// shift starts would not be. The scene hands its inspector numbers in
    /// (RoadDemoBuilder's Police header); nothing reads a literal off the floor.
    /// </summary>
    public sealed class PoliceRosterConfig
    {
        /// <summary>Days between an officer going down and the man who takes his place
        /// reporting for duty. A night for the paperwork and a day to move somebody
        /// across from another watch - short enough that the player never plays a city
        /// with no law in it, long enough that killing a policeman is felt.</summary>
        public int OfficerDays = 2;

        /// <summary>Days to replace a wrecked or abandoned car. Longer than a man: the
        /// city buys cars once a quarter and a precinct waits its turn.</summary>
        public int CarDays = 3;

        public int ReplacementDays(PoliceLoss kind) =>
            kind == PoliceLoss.Car ? CarDays : OfficerDays;

        // ------------------------------------------------------------------ the watch

        /// <summary>The hour the day watch walks out of the door.</summary>
        public float DayShiftHour = 7f;

        /// <summary>The hour the night watch relieves it.</summary>
        public float NightShiftHour = 19f;

        /// <summary>By day the street is full of people, so the law is on it: most of
        /// the beat out walking, half the cars in the yard.</summary>
        public float DayCarShare = 0.5f;
        public float DayFootShare = 1f;

        /// <summary>By night nobody walks anywhere, so the cars do the work and the
        /// beat is cut to the men who can be spared to stand at the door.</summary>
        public float NightCarShare = 1f;
        public float NightFootShare = 0.5f;

        /// <summary>Seconds a handover takes at the door - the incoming men walk out,
        /// the outgoing walk in. Real seconds, not game hours: it is a SCENE, and a
        /// scene that took a game hour would be over before the player looked up.</summary>
        public float HandoverSeconds = 25f;
    }
}
