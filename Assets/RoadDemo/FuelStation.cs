using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A filling station as the traffic and the crowd have to see it: not the pile of
    /// props (that is the Town pack's cluster, stood by <see cref="Stand"/>), but the
    /// handful of PLACES a car and a man need to know about - which bays are free,
    /// where a car stands in one, where the man who got out of it puts the nozzle in,
    /// where the shop door is, and where the forecourt meets the road.
    ///
    /// Every number below is measured off the cluster itself (TownClusters.GasStation)
    /// and written in the CLUSTER'S OWN FRAME, whose +Z faces the road: the canopy's
    /// centre is the origin, the two pumps stand at x = +-1.85 on an island bar 9.8 m
    /// wide and 2.2 m deep, and the store is 11 m behind with its glass doors 4.7 m
    /// out of its pivot. A station is therefore an anchor and a rotation, and every
    /// place it answers with is that frame turned into the world.
    ///
    /// It is deliberately NOT a MonoBehaviour and knows nothing about who is using it:
    /// the wayside station on the city's own roads (RoadDemoBuilder.Wayside) can be
    /// handed one of these later without a line of it changing.
    /// </summary>
    public sealed class FuelStation
    {
        // ------------------------------------------------------- the measured cluster

        /// <summary>Local x of the two pumps (SM_Prop_Gaspump_01 at -1.89 and +1.80,
        /// read as one number since the difference is under a hand's width).</summary>
        public const float PumpX = 1.85f;

        /// <summary>The island bar the pumps stand on: two Gaspump_Base_01 (4.73 x 2.17
        /// each) at x = +-2.5, so one bar 9.8 m by 2.2 m across the middle.</summary>
        public const float IslandHalfX = 4.9f, IslandHalfZ = 1.09f;

        /// <summary>The canopy over it (SM_Prop_Gaspump_Cover_01, measured 13.38 x 7.01).</summary>
        public const float CanopyHalfX = 6.69f, CanopyHalfZ = 3.51f;

        /// <summary>The store behind (SM_Bld_Shop_01, 10.57 x 10.40) - its pivot 11 m
        /// back of the canopy, its glass doors 4.7 m out of that pivot toward the road.</summary>
        public const float ShopZ = -11f, ShopDoorOut = 4.7f;
        public const float ShopHalfX = 5.28f, ShopHalfZ = 5.2f;

        /// <summary>Where a car stands to be filled: alongside the island, half a body
        /// clear of it, one bay per pump. A pack car is up to 2.57 m across and 6.9 m
        /// long, so the two stand nose to tail under the canopy's 13.4 m.
        ///
        /// BOTH ARE ON THE ROAD SIDE, and the far side of the island carries nothing.
        /// The cluster's store stands 11 m behind the canopy - its wall 5.8 m back -
        /// and a car standing on that side leaves 1.5 m between its flank and the shop
        /// front, which is the only way anybody has of reaching the door. A bay there
        /// would be a bay bought by walling the shop off, and the shop is where half
        /// the life on a forecourt is.</summary>
        public const float BayZ = 2.95f, BayX = 3.8f;

        /// <summary>Where the man filling it stands: in the gap between the two bays,
        /// clear of the island's own footprint by more than his shoulders (the island
        /// is 1.09 m deep from its centre line and a walker is 0.35 m across).</summary>
        public const float NozzleX = 1.15f, NozzleZ = 1.7f;

        /// <summary>How far the apron reaches back past the store, and out past the
        /// canopy toward the road, from the anchor. The road edge is not a constant -
        /// it is wherever the builder put the station - so it is passed in.</summary>
        public const float ApronBack = 12f, ApronHalfX = 15f;

        /// <summary>How far back off the PAVEMENT'S OUTER EDGE the canopy stands. It is
        /// a constant of the station and not of a scene: the drive from the kerb to a
        /// bay, the depth of a crossover and the reach of the apron are all measured
        /// from it, and a station set a different distance back on one road would be a
        /// station whose forecourt was cut differently there.</summary>
        public const float SetBack = 10f;

        /// <summary>The two crossovers - the only places a car may leave the road and
        /// cross the footway. Between them and outside them the pavement runs on, which
        /// is what makes the frontage read as a filling station's rather than as the
        /// edge of a car park: cars have ways in and out, people have a pavement.
        /// A mouth is <see cref="MouthHalf"/> either side of +-MouthX in the station's
        /// own frame; entry is the local -X one, since every bay points along local +X
        /// and the forecourt is driven through.</summary>
        public const float MouthX = 9.5f, MouthHalf = 4f;

        /// <summary>Room for cars that are NOT being filled: three spaces down the side
        /// of the store, nose in to its flank, clear of the canopy and of both mouths.
        /// A station has a parking bay as well as pumps - the customer who only wants
        /// cigarettes does not leave his car standing at a pump to buy them, and a
        /// forecourt with nowhere to stand is a forecourt nobody stops at.
        ///
        /// The line stands at -10.8 and not the round -10.5 it was first written at: the
        /// cluster carries a hedge down the store's west flank (two SM_Env_Hedge_01 at
        /// x = -6.5, six metres each, so one wall from z = -7.9 back to -20.6) whose face
        /// is at -7.13, and the longest pack car is 6.9 m, so a line at -10.5 put its
        /// nose eight centimetres INSIDE that hedge. Three tenths of a metre west and the
        /// longest body in the pack clears it.</summary>
        public const float ParkX = -10.8f, ParkZ0 = -8f, ParkPitch = 3.1f;
        public const int ParkSpaces = 4;

        /// <summary>Which of them is the blue one. The nearest to the shop door, which
        /// is where it goes.</summary>
        public const int DisabledSpace = 0;

        /// <summary>Where the tanker stands when it comes to fill the station's own
        /// tanks - the far side of the forecourt from the parking, out of everyone's
        /// way and off the drive-through lane.</summary>
        public const float LorryX = 11f, LorryZ = -16f;

        // --------------------------------------------------------------- the dressing
        //
        // What stands on the forecourt BESIDES the cluster: the green strip along the
        // frontage, the field of covers over the buried tanks, the gas cage at the east
        // edge and the fence that closes the back. All of it is why a forecourt reads as
        // a place of business rather than as two pumps on a slab, and none of it is the
        // pack's - the Town cluster was authored for a lot with no parking row, no
        // tanker stand and no drive-through, so everything those need is laid here.
        //
        // Every figure is measured (gangsters_measure) and every one is a MESH CENTRE
        // in the station's own frame. What turns a centre into a transform is the
        // prefab's own pivot, which is the one thing the plan does not know and the
        // <see cref="Kind"/> table does.

        /// <summary>The green strip across the frontage, between the two crossovers: a
        /// low hedge on the apron with the price pole standing behind it (the cluster
        /// puts its pole at z = 8) and a bush at either end. It is the one piece of the
        /// frontage no car ever crosses - the mouths are out at x = +-5.5 and beyond -
        /// so it is the only place on the road side that will hold anything at all.</summary>
        public const float GreenZ = 9.2f, GreenBushX = 4f;

        /// <summary>The hedge run down the east edge, between the drive-out sweep and
        /// the apron's own boundary. Two lengths, laid along the station's Z.</summary>
        public const float EastHedgeX = 14.2f, EastHedgeZ0 = 5f, EastHedgeZ1 = -0.4f;

        /// <summary>The gas cage: two bottled-gas tanks on a concrete pad behind a wire
        /// fence, at the east edge between the drive-out sweep (which never reaches back
        /// past z = 2.9) and the tanker's stand. A filling station's petrol is under the
        /// ground - that is what the covers below are - and what stands ABOVE it in the
        /// open is the bottled gas, which is why this is a cage and not a tank farm.
        ///
        /// It is open to the south, toward the tanker, because that is the side the
        /// bottles are carried off from.</summary>
        /// The span is two fence lengths each way (2 x 2.607) and it starts at 9.5 so
        /// that the east run lands inside the apron's own 15 m rather than a hand's
        /// width past it.
        public const float CageXMin = 9.5f, CageXMax = 14.71f;
        public const float CageZFront = -4.4f, CageZBack = -9.61f;

        /// <summary>The covers over the buried tanks: two rows of three between the cage
        /// and the tanker's stand, which is the ground a hose would actually have to
        /// reach from where the tanker parks.</summary>
        public const float CoverZ0 = -11.3f, CoverZ1 = -12.9f;
        public const float CoverX0 = 9f, CoverPitch = 2f;
        public const int CoverColumns = 3;

        /// <summary>The painted box the tanker stands in, round <see cref="LorryX"/>.
        /// Sized to the delivery truck (7.63 by 2.75) with a hand's room either side and
        /// held inside the apron's own 15 m.</summary>
        public const float LorryBoxLong = 8f, LorryBoxWide = 3.4f;

        /// <summary>The fence along the back of the forecourt, and how far in front of
        /// the deepest thing standing it goes. The back edge is not a figure - it is
        /// wherever the cluster's own trees and outbuildings ended up (Footprint) - so
        /// the fence is laid off that and not off ApronBack.</summary>
        public const float BackFenceXMin = 4f, BackFenceXMax = 14.4f, BackFenceInset = 1.2f;

        /// <summary>Where the two lane arrows lie, and where a car leaving has to stop.
        ///
        /// The arrow is a 5 m road plate, so at 9 it covers z 6.5 to 11.5: mostly the
        /// drive just inside the mouth, with its far end running out onto the crossover.
        /// Laid a metre and a half further out it reached across the stop bar and hid
        /// half of it, which is the one line on the forecourt that has to be read.
        ///
        /// The bar stands short of the walk line at 13.25, because what a car coming off
        /// a forecourt meets first is people.</summary>
        public const float ArrowZ = 9f, StopLineZ = 12.6f, StopLineWide = 0.5f;

        /// <summary>The stop sign at the exit: on the pavement just clear of the
        /// crossover, on the OUTGOING driver's right. He leaves along local +Z and his
        /// right hand is local +X, so the sign stands a pace east of the mouth's own
        /// edge.
        ///
        /// Its z is what it is because of the CROWD and not the driver. A walk carries
        /// its people two metres either side of its centre line (PedLink.Slots), and the
        /// footway's line runs at 13.25 - so anything standing between 11.25 and 15.25 is
        /// standing where somebody is going to walk. Street furniture belongs in the
        /// strip outside that, which is where this is: a metre and a quarter back from
        /// the near edge of it, hard against the kerb, exactly where a real one goes.</summary>
        public const float StopSignX = MouthX + MouthHalf + 0.7f, StopSignZ = 10.7f;

        // ------------------------------------------------------------------ the place

        public Vector3 Anchor { get; private set; }
        public Quaternion Rot { get; private set; }
        public float GroundY { get; private set; }

        /// <summary>Local point to world.</summary>
        public Vector3 At(float x, float z)
        {
            var p = Anchor + Rot * new Vector3(x, 0f, z);
            p.y = GroundY;
            return p;
        }

        /// <summary>Local direction to world.</summary>
        public Vector3 Way(float x, float z) => Rot * new Vector3(x, 0f, z);

        // -------------------------------------------------------------------- the bays

        /// <summary>One car's worth of forecourt: where the body stands, which way it
        /// points, and where the man filling it puts his feet.</summary>
        public struct Bay
        {
            public Vector3 Stand;     // the car body's centre
            public Vector3 Forward;   // the way it points, which is the way it drives out
            public Vector3 Nozzle;    // where the driver stands, at the island beside his car
            public Vector3 Pump;      // the pump serving it, for him to face
            public float X, Z;        // the same stand, in the station's own frame
        }

        public Bay[] Bays { get; private set; }
        bool[] _taken;

        /// <summary>The store's threshold and the step in front of it, where a man
        /// stands before he goes in.</summary>
        public Vector3 ShopDoor { get; private set; }
        public Vector3 ShopStep { get; private set; }

        /// <summary>The apron's two mouths, where it meets the pavement: a car turns in
        /// at one and leaves by the other, which is what makes a forecourt read as a
        /// drive-through rather than a car park.</summary>
        public Vector3 MouthIn { get; private set; }
        public Vector3 MouthOut { get; private set; }

        /// <summary>The way a car is pointing as it crosses the footway, in and out.</summary>
        public Vector3 IntoApron { get; private set; }
        public Vector3 OutToRoad { get; private set; }

        /// <summary>The parking spaces, nose-in toward the store.</summary>
        public Vector3[] Parking { get; private set; }

        /// <summary>Every piece of the station, boxed. The forecourt is laid to cover
        /// THIS and not to a figure written down somewhere: the cluster's hedges and its
        /// tree line stand seventeen metres behind the canopy, so an apron sized by eye
        /// left half of them standing on grass with the asphalt cut across their trunks.
        /// Empty until the station has actually been stood (Wire alone measures nothing).</summary>
        public Bounds Footprint { get; private set; }

        static Bounds Measure(Transform root)
        {
            var renderers = root != null ? root.GetComponentsInChildren<Renderer>() : null;
            if (renderers == null || renderers.Length == 0) return new Bounds();
            var box = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) box.Encapsulate(renderers[i].bounds);
            return box;
        }

        /// <summary>Which way a car parked in one of them faces - nose at the store.</summary>
        public Vector3 ParkFacing => Way(1f, 0f);

        // ------------------------------------------------------------------ the paint
        //
        // What is painted on the forecourt, as a plan rather than as geometry: the
        // station says WHERE the lines go and whoever builds it decides what a line
        // looks like. It is the same split as everything else here - this class holds
        // the place, the scene holds the materials.

        public enum Paint
        {
            /// <summary>Plain white, the width of a brush.</summary>
            Line,
            /// <summary>Blue: the space by the door.</summary>
            Blue,
        }

        /// <summary>One painted rectangle, flat on the apron.</summary>
        public readonly struct Mark
        {
            public readonly Vector3 At;
            public readonly float Yaw;
            public readonly Vector2 Size;
            public readonly Paint Kind;

            public Mark(Vector3 at, float yaw, Vector2 size, Paint kind)
            {
                At = at; Yaw = yaw; Size = size; Kind = kind;
            }
        }

        const float Brush = 0.14f;      // how wide a painted line is

        /// <summary>Everything painted on the ground: the parking bays and the blue one,
        /// the box round the pump island, the lane down the side of it with a throat at
        /// each mouth, the stroke closing each bay at its pump, and the stop bar at the
        /// exit. The two lane ARROWS are not here - they are road plates off the street's
        /// own kit rather than paint (ForecourtSet.LayArrows).</summary>
        public List<Mark> Marks()
        {
            var marks = new List<Mark>(32);
            float yaw = Rot.eulerAngles.y;

            // the parking bays: one line between each pair and one at each end, running
            // the length of a space (a car is up to 6.9 m long, the bay is 5.6 m of paint)
            const float bayLong = 5.6f;
            for (int i = 0; i <= ParkSpaces; i++)
            {
                float z = ParkZ0 + ParkPitch * 0.5f - i * ParkPitch;
                marks.Add(new Mark(At(ParkX, z), yaw, new Vector2(bayLong, Brush), Paint.Line));
            }
            // and the back of them, so the row reads as a row
            marks.Add(new Mark(At(ParkX - bayLong * 0.5f, ParkZ0 - ParkPitch * (ParkSpaces - 1) * 0.5f),
                               yaw, new Vector2(Brush, ParkPitch * ParkSpaces), Paint.Line));
            // the blue one by the door, filled rather than outlined - a painted square
            // reads as a disabled bay from above and costs one quad
            marks.Add(new Mark(At(ParkX, ParkZ0 - DisabledSpace * ParkPitch), yaw,
                               new Vector2(bayLong - 0.6f, ParkPitch - 0.5f), Paint.Blue));

            // the box round the island: nobody stands a car on the island's ends
            const float boxX = IslandHalfX + 0.8f, boxZ = IslandHalfZ + 0.7f;
            marks.Add(new Mark(At(0f, boxZ), yaw, new Vector2(boxX * 2f, Brush), Paint.Line));
            marks.Add(new Mark(At(0f, -boxZ), yaw, new Vector2(boxX * 2f, Brush), Paint.Line));
            marks.Add(new Mark(At(boxX, 0f), yaw, new Vector2(Brush, boxZ * 2f), Paint.Line));
            marks.Add(new Mark(At(-boxX, 0f), yaw, new Vector2(Brush, boxZ * 2f), Paint.Line));

            // ------------------------------------------------ the way in to the pumps
            //
            // A forecourt is not a yard a driver crosses however he likes: it is a lane
            // off the road, a lane past the pumps, and a lane back. The paint is what
            // says so from the driver's seat, and without it the apron read as a car
            // park with a canopy in the middle of it.

            // the far edge of the pump lane, carried the WHOLE way across the forecourt
            // rather than stopping at the island's ends: it is the one line that runs
            // from the mouth a car comes in at to the mouth it leaves by, and it is
            // what turns the drive into a lane with the pumps down one side of it
            const float laneEdge = BayZ + 1.6f;
            marks.Add(new Mark(At(0f, laneEdge), yaw, new Vector2((MouthX + MouthHalf) * 2f, Brush),
                               Paint.Line));

            // the two throats, where the drive leaves the footway. A line down each side
            // of a mouth, from the crossover's own asphalt back to the lane: it is the
            // width of the opening made plain on the ground, so a car turning in has
            // something to aim between instead of the whole frontage. The far end runs a
            // little PAST the apron's edge on purpose - the crossover is laid a fifth of
            // a metre into it (ForecourtSet.Overlap), and a throat that stopped short of
            // that seam would leave the paint ending in mid-driveway.
            const float throatFrom = 10.2f, throatTo = laneEdge + 1.4f;
            foreach (float side in new[] { -1f, 1f })
                foreach (float edge in new[] { MouthX - MouthHalf, MouthX + MouthHalf })
                    marks.Add(new Mark(At(side * edge, (throatFrom + throatTo) * 0.5f), yaw,
                                       new Vector2(Brush, throatFrom - throatTo), Paint.Line));

            // and the end of each bay, closing it at the pump: the island's box already
            // marks the inner side and the lane edge the outer, so one stroke across the
            // far end makes each bay a stall a driver pulls INTO rather than a stretch of
            // asphalt beside a pump
            const float bayEnd = 7.6f, islandEdge = IslandHalfZ + 0.7f;
            foreach (float side in new[] { -1f, 1f })
                marks.Add(new Mark(At(side * bayEnd, (islandEdge + laneEdge) * 0.5f), yaw,
                                   new Vector2(Brush, laneEdge - islandEdge), Paint.Line));

            // The way round is not painted here. It is two arrows, one in each mouth,
            // and they are road PLATES off the same kit the street is made of rather
            // than quads built out of a shaft and two barbs - see ForecourtSet.LayArrows
            // for where they go and ArrowZ for how far in.

            // the exit gets a stop bar across it, short of the footway. A car coming
            // off a forecourt crosses a pavement before it reaches the road, so it has
            // two things to give way to and stops once for both: the line is where it
            // stops, and the sign beside it (Dressing) is what tells him to.
            marks.Add(new Mark(At(MouthX, StopLineZ), yaw,
                               new Vector2(MouthHalf * 2f, StopLineWide), Paint.Line));

            // and the tanker's stand, boxed and struck through: ground that is empty for
            // most of the week and must stay empty, because the one vehicle that needs
            // it cannot be asked to come back later
            Box(marks, At(LorryX, LorryZ), yaw, LorryBoxLong, LorryBoxWide);
            for (int i = -1; i <= 1; i++)
                marks.Add(new Mark(At(LorryX + i * LorryBoxLong * 0.3f, LorryZ), yaw + 52f,
                                   new Vector2(LorryBoxWide * 1.3f, Brush), Paint.Line));
            return marks;
        }

        /// <summary>Four lines round a rectangle, in the station's own frame.</summary>
        void Box(List<Mark> into, Vector3 at, float yaw, float length, float width)
        {
            var along = Rot * new Vector3(1f, 0f, 0f);
            var across = Rot * new Vector3(0f, 0f, 1f);
            into.Add(new Mark(at + across * (width * 0.5f), yaw, new Vector2(length, Brush), Paint.Line));
            into.Add(new Mark(at - across * (width * 0.5f), yaw, new Vector2(length, Brush), Paint.Line));
            into.Add(new Mark(at + along * (length * 0.5f), yaw, new Vector2(Brush, width), Paint.Line));
            into.Add(new Mark(at - along * (length * 0.5f), yaw, new Vector2(Brush, width), Paint.Line));
        }

        // ------------------------------------------------------------- what is stood
        //
        // The dressing, as a list of places and the names of what goes in them. It is
        // the same split the paint keeps: the plan says WHERE the hedge is and what a
        // hedge is called, and never touches a material or an AssetDatabase.

        const string TownEnv = "Assets/Synty/PolygonTown/Prefabs/Environment/";
        const string TownProps = "Assets/Synty/PolygonTown/Prefabs/Props/";
        const string ClubProps = "Assets/Synty/PolygonNightclubs/Prefabs/Props/";

        /// <summary>One thing that can be stood, and the one fact about it the plan
        /// cannot work out for itself: where its PIVOT sits out of its own mesh centre
        /// on the ground. Every place below is a centre, because a centre is what can be
        /// reasoned about - two tanks 1.4 m apart, a hedge ending 30 cm short of the
        /// asphalt - and the pivot is what turns one into a transform.
        ///
        /// All of it measured with gangsters_measure against the prefabs themselves. It
        /// is worth saying why the wire fence comes out of the Nightclubs pack and not
        /// the identically named SM_Bld_Fence_Wire_01 in Gang Warfare: that one's pivot
        /// stands 3.17 m BELOW its mesh, so a run of it laid on the ground hangs in the
        /// air over the forecourt.</summary>
        readonly struct Kind
        {
            public readonly string Path;
            public readonly Vector3 Pivot;     // pivot - centre, in the prefab's own frame
            public readonly float Long, Deep;  // the mesh's own footprint

            public Kind(string path, float pivotX, float pivotZ, float length, float deep)
            {
                Path = path;
                Pivot = new Vector3(pivotX, 0f, pivotZ);
                Long = length;
                Deep = deep;
            }
        }

        static readonly Kind LowHedge = new Kind(TownEnv + "SM_Env_Hedge_03.prefab", 0f, 0f, 5.356f, 1.043f);
        static readonly Kind Bush = new Kind(TownEnv + "SM_Env_Bush_03.prefab", 0f, 0f, 1.341f, 1.374f);
        static readonly Kind GasBottle = new Kind(TownProps + "SM_Prop_Propane_01.prefab", 0f, 0f, 3.304f, 1.003f);
        static readonly Kind CagePad = new Kind(TownEnv + "SM_Env_Concrete_Base_01.prefab", 2.5f, 2.5f, 5f, 5f);
        static readonly Kind CageWire = new Kind(ClubProps + "SM_Prop_Fence_Wire_01.prefab", 1.25f, 0f, 2.607f, 0.48f);
        static readonly Kind TankCover = new Kind(TownProps + "SM_Prop_Manhole_01.prefab", 0f, 0f, 1.017f, 0.995f);
        static readonly Kind StopSign = new Kind(TownProps + "SM_Prop_Sign_Stop_01.prefab", 0f, 0f, 0.769f, 0.125f);

        static readonly string[] TreeKinds =
        {
            TownEnv + "SM_Env_Tree_Tall_01.prefab", TownEnv + "SM_Env_Tree_Tall_02.prefab",
            TownEnv + "SM_Env_Tree_01.prefab", TownEnv + "SM_Env_Tree_Pine_01.prefab",
        };

        /// <summary>One piece of dressing, placed: which prefab, where its MESH CENTRE
        /// goes in the world, which way it faces, and what a man on foot has to walk
        /// round (zero for a manhole cover, which he walks over).</summary>
        readonly struct Stood
        {
            public readonly Kind What;
            public readonly float X, Z;    // the station's own frame, mesh centre
            public readonly float Turn;    // degrees off the station's own rotation
            public readonly bool Solid;

            public Stood(Kind what, float x, float z, float turn, bool solid)
            {
                What = what; X = x; Z = z; Turn = turn; Solid = solid;
            }
        }

        /// <summary>Everything the forecourt carries besides the cluster: the green strip
        /// on the frontage, the hedge down the east edge, the gas cage and its pad, the
        /// covers over the buried tanks, and the fence closing the back.
        ///
        /// <paramref name="backZ"/> is the local z of the deepest thing standing - the
        /// cluster's own trees and outbuildings - so the back fence lands in front of
        /// them however deep the cluster turned out to be.</summary>
        List<Stood> Dressing(float backZ)
        {
            var stood = new List<Stood>(32);

            // the frontage: a low hedge across the middle of it with the price pole
            // behind, and a bush at each end short of the crossovers
            stood.Add(new Stood(LowHedge, 0f, GreenZ, 0f, true));
            stood.Add(new Stood(Bush, -GreenBushX, GreenZ + 0.1f, 0f, true));
            stood.Add(new Stood(Bush, GreenBushX, GreenZ + 0.1f, 0f, true));

            // the east edge, between the drive-out sweep and the boundary
            stood.Add(new Stood(LowHedge, EastHedgeX, EastHedgeZ0, 90f, true));
            stood.Add(new Stood(LowHedge, EastHedgeX, EastHedgeZ1, 90f, true));

            // the stop sign at the exit, turned to face the man leaving. He drives along
            // local +Z, so the sign's own front - the Synty convention this whole cluster
            // is placed by, TownKit.Side.PlusZ - has to look back down local -Z at him,
            // which is a half turn.
            stood.Add(new Stood(StopSign, StopSignX, StopSignZ, 180f, true));

            // the gas cage: a concrete pad, two bottles laid across it, and a wire fence
            // on three sides of it. The fourth is left open toward the tanker.
            float cageMidX = (CageXMin + CageXMax) * 0.5f, cageMidZ = (CageZFront + CageZBack) * 0.5f;
            stood.Add(new Stood(CagePad, cageMidX, cageMidZ, 0f, false));
            stood.Add(new Stood(GasBottle, cageMidX - 0.7f, cageMidZ, 90f, true));
            stood.Add(new Stood(GasBottle, cageMidX + 0.7f, cageMidZ, 90f, true));
            for (int i = 0; i < 2; i++)
            {
                float x = CageXMin + CageWire.Long * (i + 0.5f);
                stood.Add(new Stood(CageWire, x, CageZFront, 0f, true));
                float z = CageZFront - CageWire.Long * (i + 0.5f);
                stood.Add(new Stood(CageWire, CageXMin, z, 90f, true));
                stood.Add(new Stood(CageWire, CageXMax, z, 90f, true));
            }

            // the covers over the buried tanks, on the ground a tanker's hose reaches
            for (int col = 0; col < CoverColumns; col++)
            {
                float x = CoverX0 + col * CoverPitch;
                stood.Add(new Stood(TankCover, x, CoverZ0, 0f, false));
                stood.Add(new Stood(TankCover, x, CoverZ1, 0f, false));
            }

            // and the back fence, in front of whatever the cluster left standing deepest
            float fenceZ = backZ + BackFenceInset;
            int bays = Mathf.Max(1, Mathf.RoundToInt((BackFenceXMax - BackFenceXMin) / CageWire.Long));
            for (int i = 0; i < bays; i++)
                stood.Add(new Stood(CageWire, BackFenceXMin + CageWire.Long * (i + 0.5f), fenceZ, 0f, true));
            return stood;
        }

        /// <summary>The local z of the deepest thing the cluster left standing, off the
        /// footprint it was actually measured at. Falls back to the written apron depth
        /// when nothing has been stood (a station wired but not built).</summary>
        public float BackEdge()
        {
            var box = Footprint;
            if (box.size.sqrMagnitude < 1f) return -ApronBack;
            float deepest = 0f;
            var turn = Quaternion.Inverse(Rot);
            for (int corner = 0; corner < 4; corner++)
            {
                var world = new Vector3((corner & 1) == 0 ? box.min.x : box.max.x, Anchor.y,
                                        (corner & 2) == 0 ? box.min.z : box.max.z);
                deepest = Mathf.Min(deepest, (turn * (world - Anchor)).z);
            }
            return Mathf.Min(deepest, -ApronBack);
        }

        /// <summary>Stand the dressing, and plant the tree line outside it. Called after
        /// <see cref="Stand"/>, because the back of the forecourt is measured off what
        /// that actually put on the ground.
        ///
        /// <paramref name="rng"/> is the caller's own stream: the city rolls its wayside
        /// stations off the district's seed, so the same seed grows the same trees.</summary>
        public void Dress(Transform under, System.Random rng)
        {
            Dress(under, rng, plantTreeLine: true);
        }

        /// <summary>Stand the working forecourt dressing, optionally omitting the outer
        /// wayside tree line. An urban block is surrounded by generated pavement, so its
        /// perimeter planting belongs to that generator rather than to the rural skirt.</summary>
        public void Dress(Transform under, System.Random rng, bool plantTreeLine)
        {
            if (under == null) return;
            float yaw = Rot.eulerAngles.y;
            var loaded = new Dictionary<string, GameObject>();

            GameObject Load(string path)
            {
                if (loaded.TryGetValue(path, out var cached)) return cached;
                var prefab = RoadDemo.DemoAssetLoad.Load<GameObject>(path);
                if (prefab == null) Debug.LogWarning("[Fuel] the forecourt is missing " + path);
                loaded[path] = prefab;
                return prefab;
            }

            foreach (var piece in Dressing(BackEdge()))
            {
                var prefab = Load(piece.What.Path);
                if (prefab == null) continue;
                var facing = Rot * Quaternion.Euler(0f, piece.Turn, 0f);
                // the place is the MESH's centre; the transform goes wherever that puts
                // the prefab's own pivot
                var at = At(piece.X, piece.Z) + facing * piece.What.Pivot;
                SuburbDemo.TownKit.Prop(prefab, at, facing, under, null, 0f);
                if (!piece.Solid) continue;
                // a footprint turned by the piece's own turn: half its length across the
                // way it lies, half its depth the other
                bool acrossZ = Mathf.Abs(Mathf.Sin(piece.Turn * Mathf.Deg2Rad)) > 0.5f;
                var half = acrossZ
                    ? new Vector2(piece.What.Deep * 0.5f, piece.What.Long * 0.5f)
                    : new Vector2(piece.What.Long * 0.5f, piece.What.Deep * 0.5f);
                WalkObstacles.Block(At(piece.X, piece.Z), yaw, half);
            }

            if (plantTreeLine) PlantTreeLine(under, rng, Load);
        }

        /// <summary>A tree line down both sides of the forecourt and along the back, on
        /// the ground OUTSIDE the asphalt: what a station on a road out of town has
        /// instead of neighbours. It is the station's and not a scene's, because both
        /// the bench and the city's wayside want the same skirt of trees round the same
        /// apron, and an apron is the one thing that knows how wide it is.</summary>
        void PlantTreeLine(Transform under, System.Random rng, System.Func<string, GameObject> load)
        {
            const float Edge = ApronHalfX + 2.5f, Pitch = 7f;
            float back = BackEdge() - 5.5f;
            float front = 6f;   // clear of the frontage, which is pavement and crossovers

            void Plant(float x, float z)
            {
                var prefab = load(TreeKinds[rng.Next(TreeKinds.Length)]);
                if (prefab == null) return;
                var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                SuburbDemo.TownKit.Prop(prefab, At(x, z), rot, under, null, 0f);
                WalkObstacles.Block(At(x, z), 0f, new Vector2(1f, 1f));
            }

            float Jitter(float much) => ((float)rng.NextDouble() * 2f - 1f) * much;

            for (float z = front; z > back; z -= Pitch)
            {
                Plant(-Edge + Jitter(1f), z + Jitter(1.5f));
                Plant(Edge + Jitter(1f), z + Jitter(1.5f));
            }
            for (float x = -Edge; x <= Edge; x += Pitch)
                Plant(x + Jitter(1.5f), back - Jitter(1f));
        }

        /// <summary>The road outside, filled in by whoever built the street: the lane a
        /// customer comes down, and the kerb points either side of the forecourt where
        /// it leaves that lane and comes back to it.</summary>
        public RoadEdge Lane;
        public float KerbInS, KerbOutS;
        public Vector3 KerbIn, KerbOut;

        /// <summary>The next lane to take, from any lane on the network, to get onto the
        /// one the station stands on. A driver who wants petrol drives TOWARD it rather
        /// than wandering until he happens to pass it - which on a small circuit is a
        /// lap or four - and this is the map he steers by (FuelCustomer.PickNext).
        /// Null until the roads are mapped.</summary>
        public Dictionary<RoadEdge, RoadEdge> Route;

        /// <summary>Work that map out, once, off the whole network. The patrol cars'
        /// own BFS: same graph, same U-turn rule, same shape of answer.</summary>
        public void MapRoads(LaneNet net)
        {
            if (net == null || Lane == null) return;
            var edges = new List<RoadEdge>();
            foreach (var road in net.Roads) edges.AddRange(road.Lanes);
            Route = PolicePatrolCar.RouteToward(edges, Lane);
        }

        public bool AnyFree
        {
            get
            {
                for (int i = 0; i < _taken.Length; i++) if (!_taken[i]) return true;
                return false;
            }
        }

        /// <summary>Book a bay. The order is fixed and not drawn: the near pair fills
        /// first, so a station with one customer on it has him where he can be seen.</summary>
        public bool TryTake(out int bay)
        {
            for (int i = 0; i < _taken.Length; i++)
            {
                if (_taken[i]) continue;
                _taken[i] = true;
                bay = i;
                return true;
            }
            bay = -1;
            return false;
        }

        public void Give(int bay)
        {
            if (bay >= 0 && bay < _taken.Length) _taken[bay] = false;
        }

        public bool Taken(int bay) => bay >= 0 && bay < _taken.Length && _taken[bay];

        // ------------------------------------------------------------------- the apron

        object _crossing;

        /// <summary>The apron itself is one-at-a-time. The two legs across it are
        /// hand-driven curves, outside the belt that keeps cars off each other on the
        /// road, so nothing but this stops one car turning in through another that is
        /// pulling out. It reads as courtesy, which is what it is on a real forecourt.</summary>
        public bool TryCross(object who)
        {
            if (_crossing != null && !ReferenceEquals(_crossing, who)) return false;
            _crossing = who;
            return true;
        }

        public void DoneCrossing(object who)
        {
            if (ReferenceEquals(_crossing, who)) _crossing = null;
        }

        object _approaching;

        /// <summary>And ONE car at a time drives up to the kerb outside meaning to turn
        /// in. Two of them sent to the same metre of lane is a car stopped at the spot
        /// and a car stopped behind it, and the one behind can never reach the spot: it
        /// re-plans, drives round the block and comes back, for ever. A driver who sees
        /// somebody already waiting to turn in carries on and comes round again, which
        /// is both what happens and what the lane graph can actually express.</summary>
        public bool TryApproach(object who)
        {
            if (_approaching != null && !ReferenceEquals(_approaching, who)) return false;
            _approaching = who;
            return true;
        }

        public void DoneApproaching(object who)
        {
            if (ReferenceEquals(_approaching, who)) _approaching = null;
        }

        // ------------------------------------------------------------------- the wiring

        /// <summary>Work out every place from the anchor and the rotation. The cluster
        /// itself may or may not be standing - a caller measuring a station it did not
        /// build (the city's wayside one) wires the same numbers to the same ground.</summary>
        public static FuelStation Wire(Vector3 anchor, Quaternion rot, float groundY, float crossZ)
        {
            var station = new FuelStation { Anchor = anchor, Rot = rot, GroundY = groundY };

            // the four bays, all pointing the same way down the forecourt (local +X):
            // a car turns in at one mouth, stands, and drives on out of the other, the
            // way it does on a real forecourt - nobody reverses off a pump island
            var bays = new List<Bay>(2);
            foreach (float bz in new[] { BayZ })
                foreach (float bx in new[] { BayX, -BayX })
                {
                    float side = bz > 0f ? 1f : -1f;
                    bays.Add(new Bay
                    {
                        Stand = station.At(bx, bz),
                        Forward = station.Way(1f, 0f),
                        // AT THE PUMP, not against the car's flank. The canopy is 7 m
                        // deep and the island 2.2 m of it, which leaves 2.4 m a side -
                        // a car's width exactly - so there is no standing room between
                        // a car and the island at all, and a nozzle point put there is
                        // a point the walker can never reach: it is inside the island's
                        // own footprint once his shoulders are allowed for, and he
                        // walks at it for ever. He stands where a man actually stands,
                        // in the gap between the two cars with the pump at his elbow.
                        Nozzle = station.At(bx > 0f ? NozzleX : -NozzleX, side * NozzleZ),
                        Pump = station.At(bx > 0f ? PumpX : -PumpX, 0f),
                        X = bx, Z = bz,
                    });
                }
            station.Bays = bays.ToArray();
            station._taken = new bool[bays.Count];

            station.ShopDoor = station.At(0f, ShopZ + ShopDoorOut);
            // clear of the shop's own wall (5.8 m back of the canopy), not on it
            station.ShopStep = station.At(0f, ShopZ + ShopDoorOut + 1.7f);

            // the crossovers, halfway across the footway: a car aims for the mouth
            // first and for its bay second, so it crosses the pavement where the
            // pavement has been left out for it and nowhere else.
            // crossZ is the LOCAL z of the kerb line - the builder knows where it put
            // the station, and a station right on the road has a shorter driveway than
            // one set back behind a pavement.
            station.MouthIn = station.At(-MouthX, crossZ);
            station.MouthOut = station.At(MouthX, crossZ);
            station.IntoApron = station.Way(0f, -1f);
            station.OutToRoad = station.Way(0f, 1f);

            station.Parking = new Vector3[ParkSpaces];
            for (int i = 0; i < ParkSpaces; i++)
                station.Parking[i] = station.At(ParkX, ParkZ0 - i * ParkPitch);
            return station;
        }

        /// <summary>Stand the Town demo's whole gas station on this ground - canopy,
        /// pumps, pole sign, the store with its lit interior - and wire it. The pack's
        /// static pickup at the pump is LEFT OUT: this station has live cars in its
        /// bays and a prop standing in one of them is a bay nobody can use.</summary>
        public static FuelStation Stand(Transform under, Vector3 anchor, Quaternion rot,
                                        float groundY, float crossZ)
        {
            return Stand(under, anchor, rot, groundY, crossZ, blockWalkers: true);
        }

        /// <summary>Stand and wire the shared station, optionally leaving obstacle ownership
        /// to the caller. FuelStationBlock is composed at the origin and moved afterwards, so
        /// its runtime registers the precise blockers only after the finished block is placed;
        /// fixed PumpDemo and wayside stations may register them immediately.</summary>
        public static FuelStation Stand(Transform under, Vector3 anchor, Quaternion rot,
                                        float groundY, float crossZ, bool blockWalkers)
        {
            var station = Wire(anchor, rot, groundY, crossZ);
            var seat = anchor;
            seat.y = groundY;
            var canopy = SuburbDemo.TownKit.LoadByName(SuburbDemo.TownClusters.GasStation.Anchor);
            // groundY is given rather than sampled: TownKit.Ground is a static hook the
            // suburb sets for its own hills, and a scene that never set it would inherit
            // whatever the last one left behind
            if (canopy != null) SuburbDemo.TownKit.Prop(canopy, seat, rot, under, null, 0f);
            foreach (var piece in SuburbDemo.TownClusters.GasStation.Pieces)
            {
                if (piece.Name.StartsWith("SM_Veh")) continue;
                if (InParking(piece.X, piece.Z)) continue;
                var prefab = SuburbDemo.TownKit.LoadByName(piece.Name);
                if (prefab == null) continue;
                var go = SuburbDemo.TownKit.Prop(prefab, seat + rot * new Vector3(piece.X, piece.Y, piece.Z),
                                                 rot * piece.Rot, under, null, 0f);
                // the same touch the suburb gives it: the pole sign stretched tall. It
                // stays where the pack's author put it - on the frontage line midway
                // between the two crossovers, which is exactly where a station's price
                // sign goes. (Moving the POLE alone leaves the two boards it carries -
                // Shop_Sign_01, at the same local z, five metres up - hanging in the air
                // over the forecourt. They are one sign in three pieces.)
                if (piece.Name == "SM_Prop_StreetSign_Pole_01")
                    go.transform.localScale = new Vector3(1.3f, 1.95f, 1.3f);
                if (piece.Name == "SM_Bld_Shop_01")
                {
                    OpenTheShop(go.transform);
                    BuildingCutaway.Prepare(go);
                }
            }
            station.Footprint = Measure(under);
            if (blockWalkers) station.BlockWalkers();
            return station;
        }

        /// <summary>Does this piece of the cluster stand on a parking space?
        ///
        /// The Town pack authored its gas station for a lot with no parking row: the
        /// west flank carries a little garden - four bushes and a tall tree, at x = -8
        /// and between z = -8.9 and -15.5 - and the row of spaces this class adds runs
        /// straight through it, so the first build had a saloon standing in a bush.
        /// The row is the plan and the garden was laid without one, so the garden gives
        /// way; what it costs in green comes back on the frontage, where the dressing
        /// puts a hedge and two bushes on ground the pack left bare (Dressing).</summary>
        static bool InParking(float x, float z)
        {
            // a body's length either side of the line, which is what a car standing on
            // it actually covers
            if (x < ParkX - 3.6f || x > ParkX + 3.6f) return false;
            for (int i = 0; i < ParkSpaces; i++)
                if (Mathf.Abs(z - (ParkZ0 - i * ParkPitch)) < ParkPitch * 0.5f + 0.4f) return true;
            return false;
        }

        /// <summary>What a man on foot has to go round: the island under the canopy and
        /// the store behind it. The canopy ITSELF is not blocked - it is a roof on four
        /// posts and the whole point of a forecourt is that people walk under it.</summary>
        public void BlockWalkers()
        {
            float yaw = Rot.eulerAngles.y;
            WalkObstacles.Block(At(0f, 0f), yaw, new Vector2(IslandHalfX, IslandHalfZ));
            WalkObstacles.Block(At(0f, ShopZ), yaw, new Vector2(ShopHalfX, ShopHalfZ));

            // and the parking spaces, which are ground a car stands on all day and
            // nobody has any business walking over.
            //
            // THE PUMP BAYS ARE NOT BLOCKED, and that is deliberate. The only person who
            // goes near them is the driver of the car standing in one, and the two
            // places he has to reach - the corner off his own back bumper and the gap at
            // the pump - are a metre apart with the bay between them: box the bay and he
            // walks at a nozzle he can never get to and the errand times out at the pump.
            // Nobody else is on that ground - the forecourt's walk is a graph of its own
            // that runs along the shop front and nowhere near the canopy.
            foreach (var space in Parking)
                WalkObstacles.Block(space, yaw + 90f, new Vector2(1.3f, 3.4f));
        }

        /// <summary>Stand the store's glass doors open. They are two leaves on the front
        /// of SM_Bld_Shop_01 (Door_L / Door_R) and their pivots are not their hinges, so
        /// each is swung about its own OUTER edge, read off its renderer - which is right
        /// whatever the pack meant the pivot to be.</summary>
        public static void OpenTheShop(Transform shop, float degrees = 78f)
        {
            if (shop == null) return;
            foreach (var door in shop.GetComponentsInChildren<Transform>())
            {
                bool left = door.name.EndsWith("_Door_L");
                if (!left && !door.name.EndsWith("_Door_R")) continue;
                var renderer = door.GetComponentInChildren<Renderer>();
                if (renderer == null) continue;
                float half = Vector3.Dot(renderer.bounds.extents, Abs(shop.right));
                float side = left ? 1f : -1f;
                door.RotateAround(door.position + shop.right * (side * half), Vector3.up, side * degrees);
            }
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
