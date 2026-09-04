using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Who is at the wheel. Every car on the road drives the same way - the same
    /// lane keeping, the same following, the same claims on the road before a
    /// manoeuvre, the same junction discipline (RoadCar) - and the only thing that
    /// tells a plain commuter from a gangster with the guns out or a cruiser with
    /// its siren on is THRESHOLDS AND PERMISSIONS: how fast, how patient, which
    /// bands of the road he may use to get round what is in his way, what margin
    /// he wants off oncoming traffic, whether he turns round in the road, whether
    /// he goes through a red, whether he flinches at gunfire, who gives way to
    /// whom when two cars meet nose to nose. Nothing here lets anyone into road
    /// that is not free or claimed for him.
    ///
    /// WHAT HE IS DRIVING is the other half, and it is not here: a body's own pace,
    /// pull and grip are three multipliers on the numbers below
    /// (LivingCity.Gameplay.VehiclePerformance, applied in RoadCar.Accel / Brake /
    /// HardBrake / LateralG / JunctionSpeed / TopSpeed). So every figure in this file
    /// is what the man ASKS FOR, and what he gets is that figure through a lorry or
    /// through a supercar. They were the same thing until the table was written, which
    /// is why a delivery lorry and a supercar with the same commuter at the wheel used
    /// to pull away from a light together.
    ///
    /// Tune here for a kind of driver and there for a kind of machine. A number moved
    /// here moves every body that hand ever sits in.
    /// </summary>
    public sealed class DriverProfile
    {
        public string Name = "Traffic";

        // the pace
        public float Cruise = 10f;          // m/s at most (a lane's limit still applies to traffic)
        /// <summary>What he drives at on a MOTORWAY, and on a slip road off one. A
        /// commuter who cruises a street at ten does not crawl down a deck at ten: he
        /// drives it at fifty-odd miles an hour, because that is what the road is for.
        /// The old model had one Cruise for every road in the city, so a 25 m/s deck
        /// was driven at the speed of a high street and the freeway carried nobody
        /// faster than the traffic it was meant to be quicker than.
        ///
        /// Left at NaN, both are worked out of Cruise (a deck at two and a bit times
        /// the street pace, a ramp a shade over it), so a profile nobody has thought
        /// about still behaves.</summary>
        public float CruiseFreeway = float.NaN, CruiseRamp = float.NaN;
        public bool ObeysLimit = true;      // the lane's own limit caps him

        /// <summary>What he means to drive at on this kind of road.</summary>
        public float CruiseOn(RoadClass cls)
        {
            switch (cls)
            {
                case RoadClass.Freeway:
                    return float.IsNaN(CruiseFreeway) ? Cruise * 2.3f : CruiseFreeway;
                case RoadClass.Ramp:
                    return float.IsNaN(CruiseRamp) ? Cruise * 1.2f : CruiseRamp;
                default:
                    return Cruise;
            }
        }
        public float Accel = 3.5f;
        public float Brake = 6.5f;
        public float HardBrake = 11f;
        public float LateralG = 2.5f;       // m/s² through a bend / a lane change
        public float TurnSpeed = 6f;        // through a junction
        public float UTurnSpeed = 3f;

        // following
        public float FollowGap = 2.2f;      // metres standing off the car ahead
        public float TimeGap = 0.9f;        // seconds of road kept to it on the move

        // getting round things
        public float Patience = 5f;         // seconds behind something stopped before going round
        public bool PassesAtKerb = true;    // swings round a car pulled in at the kerb (a little over the crown)
        public float OverCrown = 1.0f;      // metres over the crown that swing may go
        public bool UsesCrown = false;      // drives the crown between the lanes to pass a queue
        public bool UsesOpposite = false;   // the far lane, when nothing is coming
        public float OncomingMargin = 3f;   // seconds of air wanted off oncoming before using its band
        public bool UTurnsInRoad = false;   // turns round inside the carriageway (not just at dead ends)
        public bool Reverses = true;        // backs off a few metres when wedged behind something
        public bool GivesWay = true;        // pulls over / holds for a car of higher priority nose to nose
        public bool RunsRed = false;        // a red light, when the box is clear
        public bool EmergencyRightOfWay;    // roof lights on: conflicting approaches hold before the box
        public bool Fearless = false;       // gunfire does not slow him
        public bool Wanders = true;         // no route: random turns at junctions
        public int Priority = 0;            // standoffs: the higher carries on, the lower gives way
        public float StandoffPatience = 0.8f; // seconds nose to nose before the lower yields
        public float GiveWayFor = 4f;       // seconds he holds aside

        /// <summary>Does he treat a QUEUE as something to get past, rather than something
        /// to join? A separate question from <see cref="RunsRed"/>, and it used to be the
        /// same one.
        ///
        /// Running a red and pushing past a queue were both read off RunsRed, on the
        /// reasoning that anybody who does one does the other. That is true of a police
        /// car and false of a motorcycle: the machine does not filter between lanes in
        /// this model (it takes a whole lane like anything else), and with the crown and
        /// the far lane taken away from it - which is what stopped it driving INTO cars -
        /// it has nowhere to go round a queue TO. Told to push past one anyway it sat
        /// against the car in front trying to swing out and never being able to, which
        /// is the "six hundred and nineteen refusals in thirty-one seconds" the Getaway
        /// profile's own comment records.
        ///
        /// So the question is asked of the ability, not of the attitude: a driver pushes
        /// past a queue only if he has a lane to do it in. A red with nothing under it
        /// he still runs.</summary>
        public bool PushesPastQueues => RunsRed && (UsesCrown || UsesOpposite);

        /// <summary>Plain traffic: keeps its lane, obeys everything, swings a little
        /// over the crown round a parked car, and only after a long wait behind a jam
        /// uses the far lane or turns round.</summary>
        public static readonly DriverProfile Traffic = new DriverProfile
        {
            Name = "Traffic", CruiseRamp = 12f, CruiseFreeway = 23f,
        };

        /// <summary>The outfit's driver on an errand: quicker, less patient, the far
        /// lane when it is clear, a turn in the road when the spot is behind him.</summary>
        public static readonly DriverProfile Gangster = new DriverProfile
        {
            Name = "Gangster", Cruise = 14f, CruiseRamp = 15f, CruiseFreeway = 27f, ObeysLimit = false, Accel = 6f, Brake = 7f, LateralG = 3f,
            TurnSpeed = 7f, UTurnSpeed = 3.5f, FollowGap = 3f, TimeGap = 0.7f, Patience = 0.8f,
            UsesCrown = true, UsesOpposite = true, OncomingMargin = 2f, UTurnsInRoad = true,
            Priority = 1, StandoffPatience = 1.2f,
        };

        /// <summary>The guns out: the action pace, the harder bends, no patience at
        /// all, the crown between the lanes for as long as it is open, the far lane
        /// with a thinner margin, a red when the box is clear. Still never into road
        /// somebody else has.</summary>
        public static readonly DriverProfile Hot = new DriverProfile
        {
            Name = "Hot", Cruise = 18f, CruiseRamp = 18f, CruiseFreeway = 31f, ObeysLimit = false, Accel = 9f, Brake = 7f, LateralG = 4.5f,
            TurnSpeed = 8f, UTurnSpeed = 4f, FollowGap = 3f, TimeGap = 0.5f, Patience = 0f,
            UsesCrown = true, UsesOpposite = true, OncomingMargin = 1.2f, UTurnsInRoad = true,
            RunsRed = true, Fearless = true, Priority = 2, StandoffPatience = 0.5f,
        };

        /// <summary>Two wheels leaving a drive-by: quick and steady, and nothing else.
        ///
        /// Both of the things that would make it braver were tried and both put the
        /// machine into the back of a car, which the belt then had to refuse for it -
        /// and a belt refusal is a vehicle that would have driven through another one.
        ///
        /// THE CROWN AND THE FAR LANE, because a motorcycle in this model does not
        /// filter between the lanes (RoadBike says so where it is built): it takes a
        /// whole lane like anything else, so swinging it across the middle of the street
        /// at speed lands it on top of whatever is queued at the next light. Gap already
        /// negative the first frame anybody noticed.
        ///
        /// THE RED, which the player asked for twice ("why do they stop at a light in the
        /// middle of a drive-by?") and which was refused once for a good reason that
        /// turns out to have been about the wrong thing.
        ///
        /// What went wrong the first time was not the red. RunsRed was ALSO the flag that
        /// said "get past a queue rather than join it" (RoadCar, two places), and a
        /// machine with no crown and no far lane has nowhere to get past one TO: it sat
        /// against the car in front trying to swing out and failing, every frame - six
        /// hundred and nineteen refusals in thirty-one seconds, in the soak that tried
        /// it. That flag is now its own question, asked of the ABILITY
        /// (DriverProfile.PushesPastQueues), so the machine can be given the red without
        /// being given the shunt.
        ///
        /// What it runs is an EMPTY red. CanEnter still refuses the box to anybody with
        /// cross traffic coming, anybody whose line crosses a car already inside it, and
        /// - the one that matters here - anybody with no room to LEAVE it ("box: no room
        /// beyond"). So a red with a queue under it still stops the machine, because
        /// that is not a rule, it is a wall of cars; a red over an empty junction no
        /// longer does. Which is the whole of what a man in a hurry actually gains.
        ///
        /// What is left is the useful half: getting away at nearly twice what the traffic
        /// does, no patience to speak of, a turn in the road when home is behind it - and
        /// MORE air off the car in front than the errand profile carries, not less,
        /// because it is the one going fastest. It also STOPS harder than a car does,
        /// which is the one honest advantage a motorcycle has and the reason it can be
        /// given the pace at all: sixteen metres a second on a car's seven-metre brake
        /// needs eighteen metres to stop in and is given fourteen; on eleven it needs
        /// twelve.
        ///
        /// That arithmetic survived the machine table, and was checked rather than
        /// hoped for. Stopping room goes as the pace squared over the brake, so a body
        /// scaled on both needs Top²/Grip of what was worked out here - and the
        /// motorcycle's row is 1.12 and 1.25, which is 1.004. The machine that leaves a
        /// drive-by now runs at eighteen metres a second on a fourteen-metre brake and
        /// stops in the same eleven and a half metres it always did.
        ///
        /// Used for EVERY leg the machine drives, not only the way home. The pass ran on
        /// Hot until the lane work put the machine inside a car three times in nine
        /// runs, one of them five metres in before anybody noticed.</summary>
        public static readonly DriverProfile Getaway = new DriverProfile
        {
            Name = "Getaway", Cruise = 16f, CruiseRamp = 18f, CruiseFreeway = 30f, ObeysLimit = false, Accel = 9f, Brake = 11f,
            LateralG = 4f, TurnSpeed = 8f, UTurnSpeed = 4f, FollowGap = 3.5f, TimeGap = 0.9f,
            Patience = 0.4f, UsesCrown = false, UsesOpposite = false, UTurnsInRoad = true,
            RunsRed = true, Fearless = true, Priority = 2, StandoffPatience = 0.5f,
        };

        /// <summary>The law answering a call: very fast, fearless, the crown and the
        /// far lane with its lights on, and priority through a junction; everybody
        /// else holds before the box. Ordinary rounds use <see cref="Patrol"/>
        /// instead.</summary>
        public static readonly DriverProfile Police = new DriverProfile
        {
            Name = "Police", Cruise = 24f, CruiseRamp = 27f, CruiseFreeway = 38f, ObeysLimit = false, Accel = 10f, Brake = 11f, LateralG = 5f,
            TurnSpeed = 10f, UTurnSpeed = 5f, FollowGap = 4f, TimeGap = 0.65f, Patience = 0f,
            UsesCrown = true, UsesOpposite = true, OncomingMargin = 1.2f, UTurnsInRoad = true,
            RunsRed = true, EmergencyRightOfWay = true, Fearless = true, Priority = 3, StandoffPatience = 0.35f,
        };

        /// <summary>A patrol car on its rounds, no call in hand: drives like the
        /// traffic, only without the nerves.</summary>
        public static readonly DriverProfile Patrol = new DriverProfile
        {
            Name = "Patrol", CruiseRamp = 13f, CruiseFreeway = 25f, Fearless = true, Priority = 1,
        };

        /// <summary>Something long: slower, wider berths, never the crown.</summary>
        public static readonly DriverProfile Lorry = new DriverProfile
        {
            Name = "Lorry", Cruise = 8f, CruiseRamp = 10f, CruiseFreeway = 20f, Accel = 2f, Brake = 5f, LateralG = 1.8f, TurnSpeed = 4f,
            FollowGap = 3f, TimeGap = 1.4f, Patience = 8f, PassesAtKerb = true, OverCrown = 0.5f,
            Reverses = false,
        };
    }
}
