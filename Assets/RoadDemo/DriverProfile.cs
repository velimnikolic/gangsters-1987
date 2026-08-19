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
    /// </summary>
    public sealed class DriverProfile
    {
        public string Name = "Traffic";

        // the pace
        public float Cruise = 10f;          // m/s at most (a lane's limit still applies to traffic)
        public bool ObeysLimit = true;      // the lane's own limit caps him
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
        public bool Fearless = false;       // gunfire does not slow him
        public bool Wanders = true;         // no route: random turns at junctions
        public int Priority = 0;            // standoffs: the higher carries on, the lower gives way
        public float StandoffPatience = 0.8f; // seconds nose to nose before the lower yields
        public float GiveWayFor = 4f;       // seconds he holds aside

        /// <summary>Plain traffic: keeps its lane, obeys everything, swings a little
        /// over the crown round a parked car, and only after a long wait behind a jam
        /// uses the far lane or turns round.</summary>
        public static readonly DriverProfile Traffic = new DriverProfile
        {
            Name = "Traffic",
        };

        /// <summary>The outfit's driver on an errand: quicker, less patient, the far
        /// lane when it is clear, a turn in the road when the spot is behind him.</summary>
        public static readonly DriverProfile Gangster = new DriverProfile
        {
            Name = "Gangster", Cruise = 14f, ObeysLimit = false, Accel = 6f, Brake = 7f, LateralG = 3f,
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
            Name = "Hot", Cruise = 18f, ObeysLimit = false, Accel = 9f, Brake = 7f, LateralG = 4.5f,
            TurnSpeed = 8f, UTurnSpeed = 4f, FollowGap = 3f, TimeGap = 0.5f, Patience = 0f,
            UsesCrown = true, UsesOpposite = true, OncomingMargin = 1.2f, UTurnsInRoad = true,
            RunsRed = true, Fearless = true, Priority = 2, StandoffPatience = 0.5f,
        };

        /// <summary>The law: brisk, fearless, the crown and the far lane with its
        /// lights on, a red when the box is clear; everybody else gives way to it.</summary>
        public static readonly DriverProfile Police = new DriverProfile
        {
            Name = "Police", Cruise = 14f, ObeysLimit = false, Accel = 5f, Brake = 7f, LateralG = 3f,
            TurnSpeed = 7f, FollowGap = 3f, TimeGap = 0.8f, Patience = 1f,
            UsesCrown = true, UsesOpposite = true, OncomingMargin = 2f, UTurnsInRoad = true,
            RunsRed = true, Fearless = true, Priority = 3, StandoffPatience = 0.6f,
        };

        /// <summary>A patrol car on its rounds, no call in hand: drives like the
        /// traffic, only without the nerves.</summary>
        public static readonly DriverProfile Patrol = new DriverProfile
        {
            Name = "Patrol", Fearless = true, Priority = 1,
        };

        /// <summary>Something long: slower, wider berths, never the crown.</summary>
        public static readonly DriverProfile Lorry = new DriverProfile
        {
            Name = "Lorry", Cruise = 8f, Accel = 2f, Brake = 5f, LateralG = 1.8f, TurnSpeed = 4f,
            FollowGap = 3f, TimeGap = 1.4f, Patience = 8f, PassesAtKerb = true, OverCrown = 0.5f,
            Reverses = false,
        };
    }
}
