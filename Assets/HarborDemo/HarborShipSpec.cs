using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    // The shape of a kit-bashed vessel, in its own frame: origin at the hull's centre
    // on the waterline, +Z the bow, +X starboard. The kit-bash (Editor/HarborShipKitBash)
    // builds the prefab from these numbers, and the shipping reads the same numbers
    // back at Play - where the deck is, where the boxes stand, where the funnel smokes,
    // where the captain leans - so the two can never disagree about a ship.
    //
    // Everything that gives a ship her outline lives here rather than in the kit-bash:
    // the raised forecastle, the bridge wings, the cargo gear (deck cranes or the older
    // kingposts and derricks), and the dressing colours - boot-top, strake, trim, mast -
    // that keep a hull from reading as one slab of paint.
    public sealed class HarborShipSpec
    {
        /// <summary>A deck crane on the centreline: where its pedestal stands, how far
        /// the jib reaches, how tall the pedestal is, and which way the jib is stowed
        /// (0 = over the bow, 180 = over the stern).</summary>
        public sealed class DeckCrane
        {
            public float Z;
            public float Reach = 15f;
            public float Pedestal = 3.2f;
            public float Yaw;
        }

        public string Name;
        public float Length, Beam;
        /// <summary>The main deck above the waterline.</summary>
        public float DeckY;
        /// <summary>The bow's taper from full beam to the stem, and the stern's chamfer.</summary>
        public float BowLength, SternChamfer;
        /// <summary>The deckhouse aft: its length, width and storeys; the funnel above it.</summary>
        public float HouseLength, HouseWidth;
        public int Storeys;
        public float FunnelHeight;
        /// <summary>How high the forecastle stands over the main deck - its break is the
        /// line where the bow's taper starts. Zero for a flush-decked hull.</summary>
        public float ForecastleRise = HalfCourse;
        /// <summary>How far the bridge wings reach outboard of the deckhouse's side.</summary>
        public float BridgeWingSpan = 1.8f;
        /// <summary>Hatch openings on deck (x-z rects); the boxes stand on their covers.</summary>
        public readonly List<Rect> Hatches = new List<Rect>();
        /// <summary>Her cargo gear: modern deck cranes, or the older kingpost pairs
        /// (by z) whose derrick booms lie over the hatches. Either, both, or neither.</summary>
        public readonly List<DeckCrane> Cranes = new List<DeckCrane>();
        public readonly List<float> Kingposts = new List<float>();
        public Color HullUpper, HullLower, House, Deck, Funnel;
        /// <summary>The band at the waterline, the rubbing strake up the topsides, the
        /// white of rails and lettering, and the paint on masts and cargo gear.</summary>
        public Color Boot = new Color(0.07f, 0.07f, 0.08f);
        public Color Strake = new Color(0.28f, 0.29f, 0.31f);
        public Color Trim = new Color(0.88f, 0.88f, 0.84f);
        public Color Mast = new Color(0.85f, 0.72f, 0.20f);
        public bool ForeMast = true;

        public const float Course = 3.006f;      // the base kit's storey
        public const float HalfCourse = 1.5057f;
        public const float HatchCoaming = 1.5057f;
        public const float BoxLength = 6f, BoxWidth = 3f, BoxHeight = 3f;   // the kit-bashed container

        public float SternZ => -Length * 0.5f;
        public float BowZ => Length * 0.5f;
        public float HouseZ0 => SternZ + 3f;
        public float HouseZ1 => HouseZ0 + HouseLength;
        public float HouseRoofY => DeckY + Storeys * Course;
        /// <summary>The forecastle: where its break stands, and the deck on top of it.</summary>
        public float ForecastleZ => BowZ - BowLength;
        public float ForecastleY => DeckY + ForecastleRise;
        /// <summary>The bridge deck - the top storey's floor, which the wings hang off.</summary>
        public float BridgeY => HouseRoofY - Course;
        public Vector3 FunnelTop => new Vector3(0f, HouseRoofY + FunnelHeight, HouseZ0 + 2.5f);
        public Vector3 FunnelBase => new Vector3(0f, HouseRoofY, HouseZ0 + 2.5f);
        /// <summary>Where the captain stands: out at the end of a bridge wing.</summary>
        public Vector3 BridgeWing(int side) => new Vector3(side * (HouseWidth * 0.5f + BridgeWingSpan - 0.7f), BridgeY, HouseZ1 - 0.9f);
        /// <summary>The deck rail's inboard line, for the men who walk the deck.</summary>
        public float DeckWalkX => Beam * 0.5f - 1.4f;
        /// <summary>How far forward the main deck runs before the forecastle break - the
        /// men turn back here rather than walk into the step.</summary>
        public float DeckWalkZFore => ForecastleZ - 2f;
        /// <summary>The gangway lands here: just forward of the house, at the deck edge.</summary>
        public float GangwayZ => HouseZ1 + 2.5f;
        /// <summary>The stretch of deck edge fenced with an open railing rather than a
        /// solid bulwark - the working deck between the house and the forecastle break.
        /// Her hands are put to work inside it: behind a bulwark they cannot be seen.</summary>
        public float RailZ0 => HouseZ1 + 0.8f;
        public float RailZ1 => ForecastleZ - 0.8f;

        /// <summary>The hull outline on the waterline plane, anticlockwise from above,
        /// starting at the stem.</summary>
        public List<Vector2> Outline()
        {
            float hb = Beam * 0.5f, sc = SternChamfer;
            return new List<Vector2>
            {
                new Vector2(0f, BowZ),
                new Vector2(-hb, BowZ - BowLength),
                new Vector2(-hb, SternZ + sc),
                new Vector2(-hb + sc, SternZ),
                new Vector2(hb - sc, SternZ),
                new Vector2(hb, SternZ + sc),
                new Vector2(hb, BowZ - BowLength),
            };
        }

        /// <summary>Whether outline edge i (from vertex i to i+1) is one of the two that
        /// taper into the stem - the pair the forecastle stands on.</summary>
        public static bool IsBowEdge(int i, int count) => i == 0 || i == count - 1;

        /// <summary>How high the deck cargo is stacked: three tiers on a freighter,
        /// two on the coaster - a ship that comes in looks laden, not sampled.</summary>
        public int DeckLayers => Length >= 60f ? 3 : 2;

        /// <summary>The box slots on the hatch covers, bottom-centre points in ship
        /// space, hatch by hatch, lowest layer first - the order the deck is stacked
        /// and (reversed) unstacked in. The first HarborShip.DeckBase of them are the
        /// through cargo, stowed at launch and never touched; the berth works what
        /// stands above.</summary>
        public List<Vector3> DeckSlots(int layers = 0)
        {
            if (layers <= 0) layers = DeckLayers;
            var slots = new List<Vector3>();
            float top = DeckY + HatchCoaming;
            for (int layer = 0; layer < layers; layer++)
                foreach (var h in Hatches)
                {
                    int cols = Mathf.Max(1, Mathf.FloorToInt((h.width + 0.5f - 0.3f) / (BoxWidth + 0.5f)));
                    int rows = Mathf.Max(1, Mathf.FloorToInt((h.height + 0.8f - 0.3f) / (BoxLength + 0.8f)));
                    float cx0 = h.center.x - (cols - 1) * 0.5f * (BoxWidth + 0.5f);
                    float cz0 = h.center.y - (rows - 1) * 0.5f * (BoxLength + 0.8f);
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            slots.Add(new Vector3(cx0 + c * (BoxWidth + 0.5f), top + layer * BoxHeight, cz0 + r * (BoxLength + 0.8f)));
                }
            return slots;
        }

        // ------------------------------------------------------------ the fleet
        //
        // Three hulls that read apart at a glance: the big black one working two deck
        // cranes, the blue one still on her old kingposts and derricks, and the little
        // grey coaster with a single mast forward.

        public static readonly HarborShipSpec CargoA = new HarborShipSpec
        {
            Name = "ship-cargo-A", Length = 72f, Beam = 15f, DeckY = Course + HalfCourse,
            BowLength = 13f, SternChamfer = 2.5f, HouseLength = 10f, HouseWidth = 10f, Storeys = 3, FunnelHeight = 5f,
            HullUpper = new Color(0.07f, 0.07f, 0.08f), HullLower = new Color(0.52f, 0.14f, 0.10f),
            House = new Color(0.86f, 0.86f, 0.82f), Deck = new Color(0.17f, 0.25f, 0.19f), Funnel = new Color(0.55f, 0.12f, 0.10f),
            Boot = new Color(0.80f, 0.79f, 0.74f), Strake = new Color(0.19f, 0.19f, 0.21f),
            Trim = new Color(0.89f, 0.89f, 0.85f), Mast = new Color(0.85f, 0.72f, 0.20f),
            Hatches = { new Rect(-5.5f, -18f, 11f, 15f), new Rect(-5.5f, 1f, 11f, 15f) },
            Cranes =
            {
                new DeckCrane { Z = -1f, Reach = 15f, Pedestal = 3.2f, Yaw = 180f },
                new DeckCrane { Z = 19.5f, Reach = 15f, Pedestal = 3.2f, Yaw = 180f },
            },
            ForeMast = false,
        };

        public static readonly HarborShipSpec CargoB = new HarborShipSpec
        {
            Name = "ship-cargo-B", Length = 66f, Beam = 15f, DeckY = Course + HalfCourse,
            BowLength = 11f, SternChamfer = 2.5f, HouseLength = 10f, HouseWidth = 10f, Storeys = 2, FunnelHeight = 5.5f,
            HullUpper = new Color(0.09f, 0.17f, 0.40f), HullLower = new Color(0.52f, 0.14f, 0.10f),
            House = new Color(0.90f, 0.87f, 0.78f), Deck = new Color(0.34f, 0.22f, 0.16f), Funnel = new Color(0.92f, 0.90f, 0.85f),
            Boot = new Color(0.06f, 0.06f, 0.07f), Strake = new Color(0.14f, 0.24f, 0.50f),
            Trim = new Color(0.90f, 0.90f, 0.86f), Mast = new Color(0.88f, 0.86f, 0.78f),
            BridgeWingSpan = 1.6f,
            Hatches = { new Rect(-5.5f, -18f, 11f, 15f), new Rect(-5.5f, 0f, 11f, 15f) },
            Kingposts = { -1.5f, 19f },
            ForeMast = false,
        };

        public static readonly HarborShipSpec Coaster = new HarborShipSpec
        {
            Name = "ship-coaster", Length = 34f, Beam = 10f, DeckY = Course,
            BowLength = 7f, SternChamfer = 1.5f, HouseLength = 7.5f, HouseWidth = 7.5f, Storeys = 2, FunnelHeight = 3.5f,
            HullUpper = new Color(0.34f, 0.36f, 0.37f), HullLower = new Color(0.52f, 0.14f, 0.10f),
            House = new Color(0.88f, 0.88f, 0.84f), Deck = new Color(0.20f, 0.20f, 0.21f), Funnel = new Color(0.10f, 0.10f, 0.10f),
            Boot = new Color(0.09f, 0.09f, 0.10f), Strake = new Color(0.45f, 0.47f, 0.48f),
            Trim = new Color(0.89f, 0.89f, 0.85f), Mast = new Color(0.87f, 0.85f, 0.78f),
            BridgeWingSpan = 1f,
            Hatches = { new Rect(-3.5f, -6f, 7f, 12f) },
            ForeMast = true,
        };

        public static readonly HarborShipSpec[] All = { CargoA, CargoB, Coaster };

        public static HarborShipSpec For(string prefabName)
        {
            foreach (var s in All) if (s.Name == prefabName) return s;
            return null;
        }
    }
}
