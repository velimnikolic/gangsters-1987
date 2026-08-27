namespace RoadDemo
{
    /// <summary>
    /// Which way the street kit's kerb corner is turned. The piece carries its stone on
    /// +X and +Z at yaw 0, so a corner is named by the corner of its cell it wraps - NE 0,
    /// SE 90, SW 180, NW 270 - and every kit that lays one (the core's pavement, the
    /// industrial parcel, the residential ring) asks the same question here.
    /// </summary>
    public static class KerbYaw
    {
        /// <summary>The plain corner and the inner corner read straight off this; the
        /// core pavement's dipped corner is a quarter further round.</summary>
        public static int Corner(bool north, bool east) => north ? (east ? 0 : 270) : (east ? 90 : 180);
    }
}
