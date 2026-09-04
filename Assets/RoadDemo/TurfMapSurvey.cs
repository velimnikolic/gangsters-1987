using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;

namespace RoadDemo
{
    /// <summary>
    /// The survey: the live city, drawn onto paper for WHATEVER THE CAMERA IS LOOKING
    /// AT.
    ///
    /// This is the one thing about the class worth understanding. The plate is not a
    /// picture of the whole city that gets scaled up and down - that was the first cut
    /// of this map and it was wrong, because a raster magnified eight times at the map
    /// line is a smear where the old plan was sharp. The plate is a VIEW: 960 x 600
    /// pixels drawn for ONE rectangle of ground, redrawn when the boom or the pivot has
    /// moved far enough to be worth it. A pixel is worth a different number of metres
    /// at every zoom, so everything on the sheet - the kerb hairline, the ink round a
    /// footprint, the lettering at eleven pixels - stays the size the design says it
    /// is, all the way in and all the way out. That is what a surveyor does: he draws
    /// the sheet at the scale you asked for. He does not photograph one and enlarge it.
    ///
    /// Two phases, and the split is the performance story:
    ///
    ///   PREPARE - once, when the city is built. Caches the island's heightfield (the
    ///             one genuinely expensive read), measures every street name, and
    ///             collects the streets, footprints and quarters as WORLD rectangles.
    ///             Nothing is drawn. Everything here touches Unity.
    ///   DRAW    - per view. Projects all of that into the plate's authored space and
    ///             lays the three static layers: ground, turf wash, buildings. Tens of
    ///             milliseconds, and it runs ON A WORKER THREAD.
    ///
    /// That last word is why this class touches no Unity object between Prepare and
    /// the plate coming back. A draw is thirty milliseconds and the wheel asks for one
    /// several times a second; run on the main thread that is two dropped frames every
    /// time, which is exactly the stutter this map had. So DRAW reads only plain
    /// arrays, cached name widths and the OWNER SNAPSHOT taken by
    /// <see cref="ReadOwners"/> - never a MonoBehaviour, never a TMP measurement,
    /// never the transform of a building.
    ///
    /// The projection is double-buffered for the same reason. <see cref="Plan"/> is
    /// the one the pixels ON SCREEN were drawn with and is what every hit test and
    /// every crew position goes through; the pass being drawn works against a private
    /// one and the two are swapped by <see cref="Publish"/> on the main thread, when
    /// the finished plate is uploaded. Letting a draw move the public projection is
    /// what would make the crews jump a frame ahead of the paper under them.
    ///
    /// Every scattered mark on the paper - stipple, scrub, swell - is hashed off its
    /// own GROUND rather than rolled in sequence. A sequential roll re-scatters the
    /// texture on every redraw, and the paper then crawls under the city whenever the
    /// player pans. Hashing position means the same patch carries the same fleck every
    /// time it is drawn.
    /// </summary>
    public sealed class TurfMapSurvey
    {
        /// <summary>Metres of country kept round the road grid when the whole city is
        /// in frame.</summary>
        const float GridMargin = 70f;

        /// <summary>How wide a carriageway must be, in AUTHORED units, before it is
        /// given a centre line, and before it is given crossings. The design's two
        /// numbers - and because a unit is worth fewer metres the further in you go,
        /// the same two hand a street its markings as you approach it and take them
        /// away again as you pull back. Nothing else has to know about zoom.
        ///
        /// Four for a crossing is the design's own "MinorSide >= 4": a zebra is drawn
        /// at a FIXED size, and a street too narrow to carry one at that size is given
        /// none rather than a bar wider than its own carriageway.</summary>
        const float LaneMinUnits = 3.6f;
        const float CrossingMinUnits = 4.0f;

        /// <summary>A pedestrian crossing, in authored units, exactly as the design
        /// sheet sets it: bars 2.6 deep, 1.05 wide, every 2.2 along, held back 1.4
        /// from the junction. Fixed sizes and not a fraction of the road - a zebra
        /// derived from the width of the street it crosses grows into a ladder the
        /// closer the boom comes, which is what this map was doing.</summary>
        const float ZebraThick = 2.6f, ZebraStripe = 1.05f;
        const float ZebraGap = 2.2f, ZebraSetback = 1.4f;

        /// <summary>Roughly how far apart the pencil ruling runs, authored units. The
        /// pitch actually drawn is the round NUMBER OF METRES nearest this: a survey
        /// grid that reads 41.3 m is not a survey grid.</summary>
        const float GridPitch = 40f;

        /// <summary>How far apart a street's name repeats along its own length,
        /// authored units. A long avenue on a close plan carries its name several
        /// times, the way a printed plan letters it; with the whole city in frame,
        /// once.</summary>
        const float NameRepeat = 150f;

        /// <summary>Metres between samples of the island's height. Finer than the
        /// shoreline is ever drawn at the wide zooms, and close enough at the tight
        /// ones that the waterline never steps.</summary>
        const float HeightStep = 3f;

        /// <summary>The paper's own seed. The city changes with its seed; the PAPER
        /// does not.</summary>
        const int PaperSeed = 1987;

        /// <summary>One line of the road grid, in world metres, with what the city
        /// calls it.</summary>
        public sealed class Street
        {
            public Rect World;
            public bool Vertical;
            public bool Boulevard;
            public string Name = "";

            /// <summary>The same rectangle in authored units, refreshed every draw.
            /// </summary>
            public Rect Plan;
        }

        readonly struct PrimarySegment
        {
            public readonly bool Vertical;
            public readonly float Axis, From, To, Half;

            public PrimarySegment(bool vertical, float axis, float from, float to, float half)
            {
                Vertical = vertical;
                Axis = axis;
                From = from;
                To = to;
                Half = half;
            }
        }

        readonly struct FootprintKey : System.IEquatable<FootprintKey>
        {
            readonly int _x0, _y0, _x1, _y1;

            public FootprintKey(Rect world)
            {
                const float precision = 100f;
                _x0 = Mathf.RoundToInt(world.xMin * precision);
                _y0 = Mathf.RoundToInt(world.yMin * precision);
                _x1 = Mathf.RoundToInt(world.xMax * precision);
                _y1 = Mathf.RoundToInt(world.yMax * precision);
            }

            public bool Equals(FootprintKey other) =>
                _x0 == other._x0 && _y0 == other._y0 &&
                _x1 == other._x1 && _y1 == other._y1;

            public override bool Equals(object obj) => obj is FootprintKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _x0;
                    hash = hash * 397 ^ _y0;
                    hash = hash * 397 ^ _x1;
                    return hash * 397 ^ _y1;
                }
            }
        }

        readonly struct ParkSurface
        {
            public readonly Rect World;
            public readonly ParkWalk.Ground Kind;

            public ParkSurface(Rect world, ParkWalk.Ground kind)
            {
                World = world;
                Kind = kind;
            }
        }

        /// <summary>What ground the plate ON SCREEN covers, and the scale that goes
        /// with it. Every hit test runs through this, so it must be the projection the
        /// uploaded pixels were actually drawn with - never the one the camera wants
        /// next, and never the one a worker is drawing right now.</summary>
        public TurfProjection Plan => _shown;

        /// <summary>The world rectangle the plate on screen was drawn for.</summary>
        public Rect DrawnView => _shownView;

        TurfProjection _shown;
        Rect _shownView;

        /// <summary>The projection the pass in flight is drawing against. Worker-side
        /// until <see cref="Publish"/> promotes it.</summary>
        TurfProjection _plan;
        Rect _planView;

        public readonly TurfPlate Ground = new TurfPlate();
        public readonly TurfPlate Turf = new TurfPlate();
        public readonly TurfPlate Built = new TurfPlate();
        /// <summary>Worker-composited upload answers. The full map never uploads the
        /// three source layers separately.</summary>
        public readonly TurfPlate Composite = new TurfPlate();
        public readonly TurfPlate Plain = new TurfPlate();

        public readonly List<TurfBuilding> Buildings = new List<TurfBuilding>();
        public readonly List<TurfLandmark> Landmarks = new List<TurfLandmark>();
        public readonly List<TurfDistrict> Districts = new List<TurfDistrict>();
        public readonly List<Street> Streets = new List<Street>();

        /// <summary>Where the street names go for the plate ON SCREEN. Published, so
        /// the lettering the player is reading always belongs to the paper under it.
        /// </summary>
        public readonly List<TurfLabel> Labels = new List<TurfLabel>();

        /// <summary>The same list for the pass in flight. Worker-side.</summary>
        readonly List<TurfLabel> _drawLabels = new List<TurfLabel>();

        /// <summary>Where a zebra sits, authored units, for the current draw. The
        /// centre line reads this and stops: a white line painted straight through a
        /// crossing is the one mark that makes a junction unreadable.</summary>
        readonly List<Rect> _zebras = new List<Rect>();

        /// <summary>The lettering boxes for the current draw, worked out from the
        /// widths measured at prepare. The crossings pass refuses to lay a zebra
        /// through one of them.</summary>
        readonly List<Rect> _labelBoxes = new List<Rect>();

        /// <summary>How wide each street name sets PER POINT of type, measured once on
        /// the main thread by the face itself. Guessing a width from the character
        /// count is how a zebra ends up half under a street name; measuring it during
        /// the draw would put TextMeshPro on a worker thread.
        ///
        /// Written only by <see cref="MeasureNames"/>, at prepare, and read by every
        /// draw after - which is what makes it safe to hand to a second survey.
        /// </summary>
        Dictionary<string, float> _nameWide = new Dictionary<string, float>();

        /// <summary>Per raster pixel: is this carriageway, and how many carriageways
        /// overlap here. The mask drives the kerb ribbon, the count drives the centre
        /// line - a junction is two roads deep and carries no centre line at all.
        /// </summary>
        byte[] _roadMask, _roadCount, _roadMajor;
        bool[] _water;

        /// <summary>The island's height on a fixed world grid, read once. Every
        /// waterline at every zoom is bilinear off this: the live query is Perlin noise
        /// plus a basin test per district, and half a million of those per redraw would
        /// make the wheel unusable.</summary>
        float[] _height;
        int _heightW, _heightH;
        Rect _heightArea;

        RoadDemoBuilder _builder;
        Transform _blockRoot;
        readonly List<Rect> _residentialGreens = new List<Rect>();
        readonly List<ParkSurface> _parkSurfaces = new List<ParkSurface>();
        readonly List<Rect> _corePaving = new List<Rect>();
        readonly List<Rect> _coreWater = new List<Rect>();
        readonly List<Rect> _corePromenades = new List<Rect>();
        int _residentialGeometryVersion = -1;

        /// <summary>The whole city and a margin - what the map shows when the wheel is
        /// all the way back, and the ground the heightfield was cached over.</summary>
        public Rect CityView { get; private set; }

        /// <summary>The city's own name.</summary>
        public string CityName { get; private set; } = "";

        public bool Ready { get; private set; }

        /// <summary>Generated parks represented directly by recipe data.</summary>
        public int ResidentialGreenCount => _residentialGreens.Count;
        /// <summary>Real kerb, walk and plaza cells published by composed Core park plans.</summary>
        public int ParkSurfaceCount => _parkSurfaces.Count;
        /// <summary>Exact accepted-raster paving strips represented for Core.</summary>
        public int CorePavingCount => _corePaving.Count;
        /// <summary>Plan-owned Core water rectangles represented on this survey.</summary>
        public int CoreWaterCount => _coreWater.Count;
        /// <summary>Plan-owned Core promenade stretches represented on this survey.</summary>
        public int CorePromenadeCount => _corePromenades.Count;

        /// <summary>Refreshes model-derived footprints on the main thread when a future
        /// generator replaces a recipe. A worker draw is never mutated underneath.</summary>
        public bool RefreshGeometryIfNeeded()
        {
            if (!Ready || _builder == null ||
                _residentialGeometryVersion == _builder.ResidentialGeometryVersion)
                return false;

            CollectBuildings(_blockRoot);
            _residentialGeometryVersion = _builder.ResidentialGeometryVersion;
            return true;
        }

        // ---------------------------------------------------------------- prepare

        /// <summary>
        /// Once, at build: the heightfield and every world rectangle the map will ever
        /// draw. Nothing here depends on where the camera is, and everything here that
        /// touches Unity is done now so no draw ever has to.
        ///
        /// <paramref name="shareHeight"/> hands over an already-sampled coastline. The
        /// heightfield is three quarters of a million Perlin reads and the corner
        /// minimap surveys the SAME island as the full plate, so it borrows one rather
        /// than paying for a second.
        /// </summary>
        public void Prepare(RoadDemoBuilder city, Transform blockRoot,
            TurfMapSurvey shareHeight = null)
        {
            _builder = city;
            _blockRoot = blockRoot;
            if (_builder == null)
                return;

            var grid = TownWorld();
            CityView = FitToPlate(new Rect(
                grid.xMin - GridMargin, grid.yMin - GridMargin,
                grid.width + GridMargin * 2f, grid.height + GridMargin * 2f));

            CityName = _builder.Streets != null ? _builder.Streets.City : "";

            _roadMask = new byte[TurfPlate.RW * TurfPlate.RH];
            _roadCount = new byte[TurfPlate.RW * TurfPlate.RH];
            _roadMajor = new byte[TurfPlate.RW * TurfPlate.RH];
            _water = new bool[TurfPlate.RW * TurfPlate.RH];

            if (shareHeight != null && shareHeight._height != null)
            {
                _height = shareHeight._height;
                _heightArea = shareHeight._heightArea;
                _heightW = shareHeight._heightW;
                _heightH = shareHeight._heightH;

                // And the lettering widths with it. Only one of these surveys owns a
                // face to measure with, and both draw the same city: without this the
                // corner card would lay its crossings by different rules from the full
                // sheet, which is exactly the two-documents problem the card exists to
                // end. Read-only from here, so two threads may share the one table.
                _nameWide = shareHeight._nameWide;
            }
            else
            {
                CacheHeight();
            }

            CollectStreets();
            CollectBuildings(blockRoot);
            _residentialGeometryVersion = _builder.ResidentialGeometryVersion;
            CollectDistricts();
            CollectCoreRiver();

            // Every family's ink mixed here, on the main thread, so no drawing pass has
            // to mix one - two surveys draw at once and they would be mixing into the
            // same table.
            TurfHouses.Warm();

            Ready = true;
        }

        /// <summary>
        /// How wide every street name sets, from the face that will actually set it.
        /// Called once, on the main thread, with a ruler the caller owns - this class
        /// must not know what TextMeshPro is, and the draw that uses these numbers runs
        /// where TextMeshPro cannot be asked anything.
        ///
        /// The width is banked PER POINT of type: a glyph's advance and the tracking
        /// with it both scale with the point size, so one measurement covers the
        /// eleven-point street and the twelve-point boulevard both.
        /// </summary>
        public void MeasureNames(System.Func<string, float, float> ruler)
        {
            _nameWide.Clear();
            if (ruler == null)
                return;

            const float At = 100f;
            foreach (var street in Streets)
                if (!string.IsNullOrEmpty(street.Name) && !_nameWide.ContainsKey(street.Name))
                    _nameWide[street.Name] = ruler(street.Name, At) / At;
            foreach (var landmark in Landmarks)
                if (!string.IsNullOrEmpty(landmark.Label) &&
                    !_nameWide.ContainsKey(landmark.Label))
                    _nameWide[landmark.Label] = ruler(landmark.Label, At) / At;
        }

        /// <summary>
        /// Who holds what, sampled onto the footprints themselves. The one bridge
        /// between the live city and a draw that runs off the main thread: called here,
        /// on the main thread, immediately before a survey is handed to a worker.
        /// </summary>
        /// <summary>
        /// What each canonical block currently READS as, taken on the main thread with
        /// the rest of the ownership snapshot. The plate is a picture of the simulation,
        /// so it asks the control ledger rather than counting deeds under a rectangle -
        /// a street with our men, our name and our shops on it is ours on the paper even
        /// if the premises are still in somebody else's name.
        /// </summary>
        public readonly List<TurfBlockReading> BlockReadings = new List<TurfBlockReading>();

        public void ReadOwners()
        {
            for (int i = 0; i < Buildings.Count; i++)
                Buildings[i].Owner = Buildings[i].GangId;

            ReadBlocks();

            // Core territory is campaign data, not an average of building markers. Snapshot it
            // here on the main thread before the survey is handed to its worker.
            for (int i = 0; i < Districts.Count; i++)
            {
                var district = Districts[i];
                if (district.TerritoryId == CoreQuarterId.None)
                    continue;
                var state = _builder.Territories.State(district.TerritoryId);
                district.TerritoryGangId = state == null ? -1
                    : state.Conflict == QuarterConflictState.Contested ? -2
                    : state.OwnerGangId;
            }
        }

        void ReadBlocks()
        {
            BlockReadings.Clear();
            var runtime = TerritoryRuntime.Instance;
            var control = runtime?.Control;
            var geography = runtime?.Geography;
            if (control == null || geography == null)
                return;

            var ids = geography.BlockIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var state = control.StateOf(ids[i]);
                if (state == LivingCity.Territory.TerritoryControlState.Unknown ||
                    state == LivingCity.Territory.TerritoryControlState.Uncontrolled)
                    continue;
                if (!geography.TryGetBlock(ids[i], out var definition))
                    continue;

                var leader = control.LeaderOf(ids[i]);
                var bounds = definition.WorldBounds;
                BlockReadings.Add(new TurfBlockReading
                {
                    World = new Rect(bounds.XMin, bounds.ZMin, bounds.Width, bounds.Depth),
                    State = state,
                    GangId = leader.IsValid ? leader.Value : -1,
                    RivalGangId = RivalOn(ids[i], leader),
                });
            }
        }

        /// <summary>The other house in a fight, so the hatch has two colours to run.</summary>
        int RivalOn(
            LivingCity.Territory.TerritoryBlockId blockId, LivingCity.Territory.TerritoryGangId leader)
        {
            var truth = TerritoryRuntime.Instance?.DebugTruth;
            if (truth == null || !truth.TryGetBlock(blockId, out var block))
                return -1;

            var best = 0f;
            var rival = -1;
            for (int i = 0; i < block.Signals.Gangs.Count; i++)
            {
                var gang = block.Signals.Gangs[i];
                if (gang.GangId == leader)
                    continue;
                var worth = gang.Presence + gang.Fear;
                if (worth <= best)
                    continue;
                best = worth;
                rival = gang.GangId.Value;
            }

            return rival;
        }

        /// <summary>Grows a world rectangle to the plate's own 8:5, so a draw of it
        /// wastes no paper and stretches nothing.</summary>
        public static Rect FitToPlate(Rect want)
        {
            float unit = Mathf.Max(want.width / TurfPlate.AW, want.height / TurfPlate.AH);
            var span = new Vector2(TurfPlate.AW * unit, TurfPlate.AH * unit);
            return new Rect(want.center - span * 0.5f, span);
        }

        /// <summary>The grid's own ground, kerb to kerb, in world metres.</summary>
        Rect TownWorld()
        {
            if (_builder.HasPrimaryStructure)
                return _builder.PrimaryWorldBounds;
            var vx = _builder.verticalRoadX;
            var hz = _builder.horizontalRoadZ;
            return Rect.MinMaxRect(
                vx[0] - _builder.VerticalHalfWidth(0),
                hz[0] - _builder.HorizontalHalfWidth(0),
                vx[vx.Length - 1] + _builder.VerticalHalfWidth(vx.Length - 1),
                hz[hz.Length - 1] + _builder.HorizontalHalfWidth(hz.Length - 1));
        }

        void CacheHeight()
        {
            // A third again round the widest view, so panning at the map line never
            // walks off the edge of the cached coast.
            _heightArea = new Rect(
                CityView.center - CityView.size * 0.65f, CityView.size * 1.3f);

            _heightW = Mathf.CeilToInt(_heightArea.width / HeightStep) + 1;
            _heightH = Mathf.CeilToInt(_heightArea.height / HeightStep) + 1;
            _height = new float[_heightW * _heightH];

            for (int j = 0; j < _heightH; j++)
            {
                float wz = _heightArea.yMin + j * HeightStep;
                for (int i = 0; i < _heightW; i++)
                    _height[j * _heightW + i] =
                        _builder.LandHeight(_heightArea.xMin + i * HeightStep, wz);
            }
        }

        float LandAt(float wx, float wz)
        {
            float u = (wx - _heightArea.xMin) / HeightStep;
            float v = (wz - _heightArea.yMin) / HeightStep;
            int x0 = Mathf.Clamp((int)u, 0, _heightW - 2);
            int y0 = Mathf.Clamp((int)v, 0, _heightH - 2);
            float fx = Mathf.Clamp01(u - x0), fy = Mathf.Clamp01(v - y0);

            float h00 = _height[y0 * _heightW + x0], h10 = _height[y0 * _heightW + x0 + 1];
            float h01 = _height[(y0 + 1) * _heightW + x0], h11 = _height[(y0 + 1) * _heightW + x0 + 1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fy);
        }

        // ------------------------------------------------------------------- draw

        /// <summary>
        /// One whole plate for one rectangle of ground. THREAD SAFE: nothing in here or
        /// below it touches a Unity object, and the only live reading it does is the
        /// owner snapshot <see cref="ReadOwners"/> left on the footprints.
        ///
        /// The result is not on screen when this returns - the caller uploads it and
        /// calls <see cref="Publish"/>, which is what swaps the projection the rest of
        /// the map reads.
        /// </summary>
        public void Draw(Rect view)
        {
            if (!Ready)
                return;

            _plan = new TurfProjection(FitToPlate(view));
            // The projection snapped its origin to the pixel grid; the view the sheet
            // is slid by on screen has to be the ground actually drawn.
            _planView = _plan.World;

            _zebras.Clear();
            _labelBoxes.Clear();
            _drawLabels.Clear();
            System.Array.Clear(_roadMask, 0, _roadMask.Length);
            System.Array.Clear(_roadCount, 0, _roadCount.Length);
            System.Array.Clear(_roadMajor, 0, _roadMajor.Length);

            Project();
            SetFleckOrigin();
            SampleWater();
            DrawGround();
            DrawSeams();
            DrawQuarters();
            DrawCoreRiver();
            // Concrete district ground is the base; intentional parks are printed over it.
            // The old order erased every Core park under the primary district rectangle.
            DrawGreen();
            LayRoads();
            InkKerbs();
            DrawTerritoryLines();

            // The names are placed AND measured before the road markings, because the
            // crossings pass refuses to lay a zebra across one.
            NameStreets();
            NameLandmarks();
            Crossings();
            LaneLines();
            SurveyGrid();

            Repaint();
        }

        /// <summary>
        /// Promotes the plate just drawn to the one on screen: the projection every hit
        /// test runs through, and the lettering the player reads. Main thread, and only
        /// once the pixels themselves have been uploaded - published early, the crews
        /// would be plotted against ground the paper under them does not yet show.
        /// </summary>
        public void Publish()
        {
            _shown = _plan;
            _shownView = _planView;

            Labels.Clear();
            Labels.AddRange(_drawLabels);
        }

        /// <summary>Redraws the two layers that depend on who holds what, at the
        /// projection already drawn. Cheap enough for any ownership change - but never
        /// while a draw is in flight, because it reads the projected rectangles that
        /// draw is rewriting.</summary>
        public void Repaint()
        {
            ScoreDistricts();
            PaintTurf();
            PaintBuildings();
            Composite.ComposePair(Ground, Turf, Built, Plain);
        }

        /// <summary>Every world rectangle, into the projection just chosen. Done in one
        /// place so no drawing pass has to remember to project.</summary>
        void Project()
        {
            foreach (var street in Streets)
                street.Plan = _plan.ToPlan(street.World);
            foreach (var building in Buildings)
                building.Plan = _plan.ToPlan(building.World);
            foreach (var landmark in Landmarks)
                landmark.Plan = _plan.ToPlan(landmark.World);
            foreach (var district in Districts)
                district.Plan = _plan.ToPlan(district.World);
            for (int i = 0; i < BlockReadings.Count; i++)
            {
                var reading = BlockReadings[i];
                reading.Plan = _plan.ToPlan(reading.World);
                BlockReadings[i] = reading;
            }
        }

        // ------------------------------------------------------------------ paper

        /// <summary>
        /// The flecks on the paper, hashed off the sheet itself. A stable number per
        /// pixel of THIS draw, so the same view always prints the same texture and a
        /// redraw at the same scale lands the same marks in the same places.
        /// </summary>
        static uint Fleck(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + PaperSeed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        /// <summary>The fleck for a piece of GROUND rather than a pixel - the sheet's
        /// origin in whole real pixels of the world, so panning at a fixed zoom slides
        /// the texture with the city instead of re-sprinkling it. Exact: the
        /// projection snaps its origin to the pixel grid and hands the integer over.
        /// Every pattern laid on a lattice - the water's hatch, the lane dashes, the
        /// speckle on a yard - is phased off the same two numbers, or the lattice
        /// stands still on the sheet while the city moves under it.</summary>
        int _fleckX, _fleckY;

        void SetFleckOrigin()
        {
            _fleckX = _plan.OriginPx.x;
            _fleckY = _plan.OriginPx.y;
        }

        uint GroundFleck(int rx, int ry, int salt) =>
            Fleck(_fleckX + rx + salt, _fleckY + ry - salt);

        /// <summary>A pixel's place in the world's own pixel grid.</summary>
        int GroundX(int rx) => _fleckX + rx;
        int GroundY(int ry) => _fleckY + ry;

        static int Mod(int a, int n) => ((a % n) + n) % n;

        void SampleWater()
        {
            float metres = _plan.MetresPerUnit / TurfPlate.S;
            for (int ry = 0; ry < TurfPlate.RH; ry++)
            {
                float wz = _plan.Origin.y + (ry + 0.5f) * metres;
                for (int rx = 0; rx < TurfPlate.RW; rx++)
                {
                    float wx = _plan.Origin.x + (rx + 0.5f) * metres;
                    _water[ry * TurfPlate.RW + rx] = LandAt(wx, wz) < RoadDemoBuilder.WaterY;
                }
            }
        }

        bool Water(int rx, int ry) =>
            rx >= 0 && ry >= 0 && rx < TurfPlate.RW && ry < TurfPlate.RH &&
            _water[ry * TurfPlate.RW + rx];

        /// <summary>Land, sea, and the paper texture over both. The vocabulary is
        /// PAPER and not a screen: one-pixel stipple on the ground, ruled four-pixel
        /// hatch on the water, a one-pixel ink shoreline. No dither, no scanline, no
        /// glow - and no scrub: the country used to carry two-by-two blobs of tree and
        /// they were taken off at the player's word, because a wood that is not
        /// stock-still under a pan reads as a fault and not as a wood.</summary>
        void DrawGround()
        {
            for (int ry = 0; ry < TurfPlate.RH; ry++)
                for (int rx = 0; rx < TurfPlate.RW; rx++)
                {
                    uint fleck = GroundFleck(rx, ry, 0);

                    if (_water[ry * TurfPlate.RW + rx])
                    {
                        int gx = GroundX(rx), gy = GroundY(ry);
                        Ground.Dot(rx, ry,
                            Mod(gy, 4) == 0 && Mod(gx + gy, 9) < 5 ? TurfInk.Water2 : TurfInk.Water);
                        // a scratch of swell, about one pixel in five hundred
                        if ((fleck & 0x1FF) < 1)
                            Ground.Px(rx, ry, 3, 1, TurfInk.Wave);
                        continue;
                    }

                    Ground.Dot(rx, ry, (fleck & 0xFF) < 12
                        ? ((fleck & 0x100) != 0 ? TurfInk.Land2 : TurfInk.Stipple)
                        : TurfInk.Land);
                }

            // the shoreline: the last dry pixel before the water, inked all the way
            // round. One pixel, and it is what turns two flat colours into a coast.
            for (int ry = 1; ry < TurfPlate.RH - 1; ry++)
                for (int rx = 1; rx < TurfPlate.RW - 1; rx++)
                    if (!Water(rx, ry) &&
                        (Water(rx + 1, ry) || Water(rx - 1, ry) ||
                         Water(rx, ry + 1) || Water(rx, ry - 1)))
                        Ground.Dot(rx, ry, TurfInk.Ink2);
        }

        /// <summary>The gaps in the grid that are not blocks: the river the bridges
        /// cross, the park's lawn, the wild strip, the deck the elevated highway rides
        /// on. The river marks the water mask as it goes, so the turf wash skips it - a
        /// family's colour must never run across open water.</summary>
        void DrawSeams()
        {
            foreach (var seam in _builder.SeamPlans)
            {
                var plan = _plan.ToPlan(seam.Area);
                if (!OnSheet(plan))
                    continue;

                switch (seam.Kind)
                {
                    case SeamKind.River:
                        Ground.Fill(plan, TurfInk.Water);
                        MarkWater(plan);
                        for (int ry = PxY(plan.yMin); ry < PxY(plan.yMax); ry += 4)
                            Ground.Px(PxX(plan.xMin), ry, PxW(plan.width), 1, TurfInk.Water2);
                        break;
                    case SeamKind.Park:
                    case SeamKind.Wild:
                        Ground.Fill(plan, TurfInk.Grass);
                        break;
                    case SeamKind.Highway:
                        Ground.Fill(plan, TurfInk.Concrete2);
                        break;
                }
            }
        }

        /// <summary>Core's river is plan-owned water, not one of the old grid seams. Draw
        /// it after the primary district's concrete base, then restore the promenade on
        /// its bank; road ink later crosses it only where the plan actually has a bridge.</summary>
        void DrawCoreRiver()
        {
            // The primary Core DistrictPlan is only a rectangular host bound. Print the
            // accepted raster instead, so real city hardstanding remains while its Outside
            // cells do not become a concrete frame around the map.
            for (int i = 0; i < _corePaving.Count; i++)
            {
                var plan = _plan.ToPlan(_corePaving[i]);
                if (OnSheet(plan)) Ground.Fill(plan, TurfInk.Concrete);
            }

            for (int i = 0; i < _coreWater.Count; i++)
            {
                var plan = _plan.ToPlan(_coreWater[i]);
                if (!OnSheet(plan)) continue;
                Ground.Fill(plan, TurfInk.Water);
                MarkWater(plan);
                for (int ry = PxY(plan.yMin); ry < PxY(plan.yMax); ry += 4)
                    Ground.Px(PxX(plan.xMin), ry, PxW(plan.width), 1, TurfInk.Water2);
            }

            for (int i = 0; i < _corePromenades.Count; i++)
            {
                var plan = _plan.ToPlan(_corePromenades[i]);
                if (OnSheet(plan)) Ground.Fill(plan, TurfInk.Concrete);
            }
        }

        void MarkWater(Rect plan)
        {
            int x0 = Mathf.Max(0, PxX(plan.xMin)), x1 = Mathf.Min(TurfPlate.RW, PxX(plan.xMax));
            int y0 = Mathf.Max(0, PxY(plan.yMin)), y1 = Mathf.Min(TurfPlate.RH, PxY(plan.yMax));
            for (int ry = y0; ry < y1; ry++)
                for (int rx = x0; rx < x1; rx++)
                    _water[ry * TurfPlate.RW + rx] = true;
        }

        /// <summary>Pocket parks: a lot the city left as lawn rather than building on.
        /// Drawn as ground and not as a block, or a square of grass comes out as
        /// another row of houses.</summary>
        void DrawGreen()
        {
            foreach (var lot in _builder.LotPlans)
            {
                if (!lot.Green)
                    continue;

                var plan = _plan.ToPlan(lot.Slab);
                if (!OnSheet(plan))
                    continue;

                Ground.Fill(plan, TurfInk.Grass);
            }

            // Core's residential parks are recipe data. Their GameObjects may be
            // detached or recycled, but the plan still owes the paper their lawns.
            for (int i = 0; i < _residentialGreens.Count; i++)
            {
                var plan = _plan.ToPlan(_residentialGreens[i]);
                if (OnSheet(plan))
                    Ground.Fill(plan, TurfInk.Grass);
            }

            // Core park blocks use their exact accepted ParkWalk cells. Their pavement
            // ring, paths and plazas therefore agree with the 3D composition instead of
            // reducing every park to an anonymous green rectangle.
            for (int i = 0; i < _parkSurfaces.Count; i++)
            {
                var surface = _parkSurfaces[i];
                var plan = _plan.ToPlan(surface.World);
                if (!OnSheet(plan))
                    continue;
                var colour = surface.Kind == ParkWalk.Ground.Kerb
                    ? TurfInk.Kerb
                    : surface.Kind == ParkWalk.Ground.Plaza
                        ? TurfInk.Concrete2
                        : TurfInk.Concrete;
                Ground.Fill(plan, colour);
            }
        }

        /// <summary>The quarters that hang off the grid - the harbour, the airfield,
        /// the yards. Their ground is CONCRETE on a plan, speckled, and their own
        /// buildings are drawn over it with everything else.</summary>
        void DrawQuarters()
        {
            foreach (var district in _builder.DistrictPlans)
            {
                if (district.Kind == DistrictKind.Suburb)
                    continue;
                // Core is an irregular plan of blocks, streets, parks and open river.
                // Its primary DistrictPlan is only a hosting/camera bound; filling that
                // rectangle prints a fake pavement frame around the whole city.
                if (_builder.HasPrimaryStructure &&
                    district.Name == _builder.PrimaryCore?.Name)
                    continue;

                var plan = _plan.ToPlan(district.World);
                if (!OnSheet(plan))
                    continue;

                Ground.Fill(plan, TurfInk.Concrete);
                Scatter(plan, 0x51B7, 20, TurfInk.Concrete2, TurfInk.Concrete2);
            }
        }

        /// <summary>
        /// Core's conquerable quarters remain legible before anybody owns them. The line is
        /// printed into the shared survey, so the full map and corner minimap cannot disagree.
        /// Ordinary RoadDemo districts keep their existing borderless wash treatment.
        /// </summary>
        void DrawTerritoryLines()
        {
            for (int i = 0; i < Districts.Count; i++)
            {
                var district = Districts[i];
                if (district.TerritoryId == CoreQuarterId.None || !OnSheet(district.Plan))
                    continue;

                int x0 = PxX(district.Plan.xMin);
                int x1 = PxX(district.Plan.xMax);
                int y0 = PxY(district.Plan.yMin);
                int y1 = PxY(district.Plan.yMax);
                int wide = Mathf.Max(1, x1 - x0);
                int tall = Mathf.Max(1, y1 - y0);

                // Two raster pixels survive the minimap's reduction without turning the
                // full plate into a heavy diagram.
                Ground.Px(x0, y0, wide, 2, TurfInk.Red);
                Ground.Px(x0, y1 - 2, wide, 2, TurfInk.Red);
                Ground.Px(x0, y0, 2, tall, TurfInk.Red);
                Ground.Px(x1 - 2, y0, 2, tall, TurfInk.Red);
            }
        }

        /// <summary>Flecks over one rectangle, hashed off the ground so a pan does not
        /// re-sprinkle them. The two-pixel lattice they sit on is phased off the
        /// ground as well: started on the sheet's own even pixels it would pick a
        /// different set of ground cells every time the origin moved an odd number.</summary>
        void Scatter(Rect plan, int salt, int odds, Color32 a, Color32 b)
        {
            int x0 = Mathf.Max(0, PxX(plan.xMin)), x1 = Mathf.Min(TurfPlate.RW, PxX(plan.xMax));
            int y0 = Mathf.Max(0, PxY(plan.yMin)), y1 = Mathf.Min(TurfPlate.RH, PxY(plan.yMax));
            x0 += GroundX(x0) & 1;
            y0 += GroundY(y0) & 1;

            for (int ry = y0; ry < y1; ry += 2)
                for (int rx = x0; rx < x1; rx += 2)
                {
                    uint fleck = GroundFleck(rx, ry, salt);
                    if ((fleck & 0x3F) >= odds)
                        continue;
                    Ground.Px(rx, ry, 2, 2, (fleck & 0x40) != 0 ? a : b);
                }
        }

        static bool OnSheet(Rect plan) =>
            plan.xMax > 0f && plan.yMax > 0f &&
            plan.xMin < TurfPlate.AW && plan.yMin < TurfPlate.AH;

        // ------------------------------------------------------------------ roads

        void CollectStreets()
        {
            Streets.Clear();
            if (!_builder.HasPrimaryStructure)
            {
                var vx = _builder.verticalRoadX;
                var hz = _builder.horizontalRoadZ;
                var town = TownWorld();

                for (int i = 0; i < vx.Length; i++)
                {
                    float half = _builder.VerticalHalfWidth(i);
                    Streets.Add(new Street
                    {
                        World = Rect.MinMaxRect(vx[i] - half, town.yMin, vx[i] + half, town.yMax),
                        Vertical = true,
                        Boulevard = Boulevard(_builder.verticalIsBoulevard, i),
                        Name = Named(_builder.Streets != null ? _builder.Streets.Vertical(i) : null),
                    });
                }

                for (int j = 0; j < hz.Length; j++)
                {
                    float half = _builder.HorizontalHalfWidth(j);
                    Streets.Add(new Street
                    {
                        World = Rect.MinMaxRect(town.xMin, hz[j] - half, town.xMax, hz[j] + half),
                        Vertical = false,
                        Boulevard = Boulevard(_builder.horizontalIsBoulevard, j),
                        Name = Named(_builder.Streets != null ? _builder.Streets.Horizontal(j) : null),
                    });
                }
            }

            // They are the entire road plan in CoreDemo. The ordinary game's grid map
            // stays on its existing path; only a primary structure substitutes these
            // registered roads for that grid.
            if (_builder.HasPrimaryStructure)
                CollectPrimaryStreets();
        }

        /// <summary>Core publishes one carriageway per junction-to-junction run. Fold
        /// collinear runs into named streets for the paper, while leaving different
        /// widths separate so their true kerbs and lane markings survive.</summary>
        void CollectPrimaryStreets()
        {
            const float AxisTolerance = 0.25f;
            const float JoinTolerance = 0.75f;
            var segments = new List<PrimarySegment>();

            foreach (var road in _builder.QuarterRoads)
            {
                float dx = Mathf.Abs(road.b.x - road.a.x);
                float dy = Mathf.Abs(road.b.y - road.a.y);
                if (Mathf.Max(dx, dy) <= 0.05f)
                    continue;

                bool vertical = dy > dx;
                float axis = vertical
                    ? (road.a.x + road.b.x) * 0.5f
                    : (road.a.y + road.b.y) * 0.5f;
                float from = vertical
                    ? Mathf.Min(road.a.y, road.b.y)
                    : Mathf.Min(road.a.x, road.b.x);
                float to = vertical
                    ? Mathf.Max(road.a.y, road.b.y)
                    : Mathf.Max(road.a.x, road.b.x);
                segments.Add(new PrimarySegment(
                    vertical, axis, from, to, Mathf.Max(0.5f, road.half)));
            }

            var verticalAxes = PrimaryAxes(segments, true, AxisTolerance);
            var horizontalAxes = PrimaryAxes(segments, false, AxisTolerance);

            segments.Sort((a, b) =>
            {
                int order = a.Vertical == b.Vertical ? 0 : (a.Vertical ? -1 : 1);
                if (order != 0) return order;
                order = a.Axis.CompareTo(b.Axis);
                if (order != 0) return order;
                order = a.Half.CompareTo(b.Half);
                return order != 0 ? order : a.From.CompareTo(b.From);
            });

            bool have = false;
            PrimarySegment run = default;
            for (int i = 0; i < segments.Count; i++)
            {
                var next = segments[i];
                bool joins = have && next.Vertical == run.Vertical &&
                    Mathf.Abs(next.Axis - run.Axis) <= AxisTolerance &&
                    Mathf.Abs(next.Half - run.Half) <= AxisTolerance &&
                    next.From <= run.To + JoinTolerance;

                if (joins)
                {
                    run = new PrimarySegment(run.Vertical, run.Axis,
                        Mathf.Min(run.From, next.From), Mathf.Max(run.To, next.To), run.Half);
                    continue;
                }

                if (have)
                    AddPrimaryStreet(run, verticalAxes, horizontalAxes);
                run = next;
                have = true;
            }
            if (have)
                AddPrimaryStreet(run, verticalAxes, horizontalAxes);
        }

        static List<float> PrimaryAxes(
            List<PrimarySegment> segments, bool vertical, float tolerance)
        {
            var axes = new List<float>();
            for (int i = 0; i < segments.Count; i++)
                if (segments[i].Vertical == vertical)
                    axes.Add(segments[i].Axis);
            axes.Sort();

            int write = 0;
            for (int read = 0; read < axes.Count; read++)
            {
                if (write > 0 && Mathf.Abs(axes[read] - axes[write - 1]) <= tolerance)
                    continue;
                axes[write++] = axes[read];
            }
            if (write < axes.Count)
                axes.RemoveRange(write, axes.Count - write);
            return axes;
        }

        void AddPrimaryStreet(PrimarySegment run, List<float> verticalAxes,
                              List<float> horizontalAxes)
        {
            var axes = run.Vertical ? verticalAxes : horizontalAxes;
            int axisIndex = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < axes.Count; i++)
            {
                float distance = Mathf.Abs(axes[i] - run.Axis);
                if (distance < nearest)
                {
                    nearest = distance;
                    axisIndex = i;
                }
            }

            bool boulevard = run.Half > 10f;
            string name = "";
            if (_builder.Streets != null)
                name = run.Vertical
                    ? _builder.Streets.VerticalAny(axisIndex, boulevard)
                    : _builder.Streets.HorizontalAny(axisIndex, boulevard);

            Streets.Add(new Street
            {
                World = run.Vertical
                    ? new Rect(run.Axis - run.Half, run.From,
                        run.Half * 2f, Mathf.Max(0.01f, run.To - run.From))
                    : new Rect(run.From, run.Axis - run.Half,
                        Mathf.Max(0.01f, run.To - run.From), run.Half * 2f),
                Vertical = run.Vertical,
                Boulevard = boulevard,
                Name = Named(name),
            });
        }

        static string Named(string name) =>
            string.IsNullOrEmpty(name) ? "" : name.ToUpperInvariant();

        static bool Boulevard(bool[] flags, int index) =>
            flags != null && index < flags.Length && flags[index];

        /// <summary>
        /// The carriageways, and the occupancy mask everything else about them is
        /// derived from.
        ///
        /// The strips run the whole width and depth of the town - but a street that was
        /// CLOSED across a gap is not a street, and neither is one two blocks grew
        /// over. Rather than re-deriving the builder's own closure rules, the mask is
        /// cut by the block pads and merged yards it already published: a pad standing
        /// on a road line is exactly what a closed segment looks like from above.
        /// </summary>
        void LayRoads()
        {
            foreach (var street in Streets)
                if (OnSheet(street.Plan))
                    Ink(street.Plan, street.Boulevard);

            foreach (var yard in _builder.MergedYards)
                Erase(_plan.ToPlan(yard));
            foreach (var lot in _builder.LotPlans)
                Erase(_plan.ToPlan(lot.Slab));

            for (int ry = 0; ry < TurfPlate.RH; ry++)
                for (int rx = 0; rx < TurfPlate.RW; rx++)
                {
                    int at = ry * TurfPlate.RW + rx;
                    if (_roadMask[at] == 0)
                        continue;
                    Ground.Dot(rx, ry, _roadMajor[at] != 0 ? TurfInk.Road : TurfInk.RoadDark);
                }
        }

        void Ink(Rect plan, bool boulevard)
        {
            int x0 = Mathf.Max(0, PxX(plan.xMin)), x1 = Mathf.Min(TurfPlate.RW, PxX(plan.xMax));
            int y0 = Mathf.Max(0, PxY(plan.yMin)), y1 = Mathf.Min(TurfPlate.RH, PxY(plan.yMax));
            for (int ry = y0; ry < y1; ry++)
                for (int rx = x0; rx < x1; rx++)
                {
                    int at = ry * TurfPlate.RW + rx;
                    _roadMask[at] = 1;
                    if (_roadCount[at] < 255)
                        _roadCount[at]++;
                    if (boulevard)
                        _roadMajor[at] = 1;
                }
        }

        void Erase(Rect plan)
        {
            if (!OnSheet(plan))
                return;
            int x0 = Mathf.Max(0, PxX(plan.xMin)), x1 = Mathf.Min(TurfPlate.RW, PxX(plan.xMax));
            int y0 = Mathf.Max(0, PxY(plan.yMin)), y1 = Mathf.Min(TurfPlate.RH, PxY(plan.yMax));
            for (int ry = y0; ry < y1; ry++)
                for (int rx = x0; rx < x1; rx++)
                    _roadMask[ry * TurfPlate.RW + rx] = 0;
        }

        bool Road(int rx, int ry) =>
            rx >= 0 && ry >= 0 && rx < TurfPlate.RW && ry < TurfPlate.RH &&
            _roadMask[ry * TurfPlate.RW + rx] != 0;

        /// <summary>
        /// The kerb, found rather than drawn. Stroking each road rectangle would put a
        /// kerb straight through every junction; walking the occupancy mask and inking
        /// the pixels that have a neighbour off the road gives ONE continuous ribbon
        /// round the whole network, junctions included, for free.
        ///
        /// A dark pixel goes just outside each kerb pixel: pale ground against a pale
        /// kerb is nothing at all, and that one dark line is what lifts the road off
        /// the paper.
        /// </summary>
        void InkKerbs()
        {
            for (int ry = 0; ry < TurfPlate.RH; ry++)
                for (int rx = 0; rx < TurfPlate.RW; rx++)
                {
                    if (!Road(rx, ry))
                        continue;

                    bool west = !Road(rx - 1, ry), east = !Road(rx + 1, ry);
                    bool south = !Road(rx, ry - 1), north = !Road(rx, ry + 1);
                    if (!west && !east && !south && !north)
                        continue;

                    Ground.Dot(rx, ry, TurfInk.Kerb);
                    if (west) Ground.Dot(rx - 1, ry, TurfInk.RoadInk);
                    if (east) Ground.Dot(rx + 1, ry, TurfInk.RoadInk);
                    if (south) Ground.Dot(rx, ry - 1, TurfInk.RoadInk);
                    if (north) Ground.Dot(rx, ry + 1, TurfInk.RoadInk);
                }
        }

        // -------------------------------------------------------------- lettering

        /// <summary>
        /// Where the street names go for THIS view, and how much room each one takes.
        /// The letters themselves are not drawn here and are not drawn into the plate
        /// at all - they float over it as real type, so they stay a face rather than
        /// becoming pixels when the sheet is held closer. What this pass owes the rest
        /// of the draw is the BOXES: the crossings pass refuses to lay a zebra across a
        /// name, so it has to know where the names are before it starts.
        ///
        /// A name repeats along its own street every <see cref="NameRepeat"/> units, so
        /// a boulevard on a close plan is lettered several times down its length the
        /// way a printed plan letters it, and once when the whole city is in frame.
        /// </summary>
        void NameStreets()
        {
            var sheet = new Rect(0f, 0f, TurfPlate.AW, TurfPlate.AH);

            foreach (var street in Streets)
            {
                if (string.IsNullOrEmpty(street.Name) || !OnSheet(street.Plan))
                    continue;

                var run = Intersect(street.Plan, sheet);
                if (run.width <= 0f || run.height <= 0f)
                    continue;

                float length = street.Vertical ? run.height : run.width;
                if (length < 30f)
                    continue;

                int marks = Mathf.Clamp(Mathf.RoundToInt(length / NameRepeat), 1, 6);
                float size = street.Boulevard ? 12f : 11f;

                // What the face actually sets at this size, in authored units, plus a
                // letter's breath either side of it.
                _nameWide.TryGetValue(street.Name, out float perPoint);
                float wide = (perPoint * size + size) / TurfPlate.S;
                float tall = (size + 4f) / TurfPlate.S;

                for (int i = 0; i < marks; i++)
                {
                    float t = (i + 0.5f) / marks;
                    var at = street.Vertical
                        ? new Vector2(street.Plan.center.x, run.yMin + run.height * t)
                        : new Vector2(run.xMin + run.width * t, street.Plan.center.y);

                    var box = street.Vertical
                        ? new Rect(at.x - tall * 0.5f, at.y - wide * 0.5f, tall, wide)
                        : new Rect(at.x - wide * 0.5f, at.y - tall * 0.5f, wide, tall);

                    _drawLabels.Add(new TurfLabel
                    {
                        Text = street.Name,
                        Plan = at,
                        Vertical = street.Vertical,
                        Size = size,
                        Box = box,
                    });
                    _labelBoxes.Add(box);
                }
            }
        }

        /// <summary>The semantic word sits inside the landmark's real map footprint.
        /// Its point size never changes: zoom changes the building shape around it, not
        /// the type itself.</summary>
        void NameLandmarks()
        {
            const float Size = 10f;
            for (int i = 0; i < Landmarks.Count; i++)
            {
                var landmark = Landmarks[i];
                if (string.IsNullOrEmpty(landmark.Label) || !OnSheet(landmark.Plan))
                    continue;

                bool vertical = landmark.Plan.height > landmark.Plan.width * 1.35f;
                _nameWide.TryGetValue(landmark.Label, out float perPoint);
                float wide = (perPoint * Size + Size) / TurfPlate.S;
                float tall = (Size + 4f) / TurfPlate.S;
                var at = landmark.Plan.center;
                var box = vertical
                    ? new Rect(at.x - tall * 0.5f, at.y - wide * 0.5f, tall, wide)
                    : new Rect(at.x - wide * 0.5f, at.y - tall * 0.5f, wide, tall);

                _drawLabels.Add(new TurfLabel
                {
                    Text = landmark.Label,
                    Plan = at,
                    Vertical = vertical,
                    Size = Size,
                    Box = box,
                });
                _labelBoxes.Add(box);
            }
        }

        // ------------------------------------------------------------- crossings

        /// <summary>
        /// Pedestrian crossings, derived from where two carriageways actually meet -
        /// never placed by hand. Each junction gets four arms, one on each approach;
        /// the bars run the FULL width of the road being stepped over and are centred
        /// on the approach, which is what makes a crossing read as a crossing rather
        /// than as a ladder dropped on the tarmac.
        ///
        /// An arm is skipped when it would land off the carriageway - a street that
        /// stops at this junction has no far approach - and skipped when it would run
        /// through a street name.
        /// </summary>
        void Crossings()
        {
            foreach (var vertical in Streets)
            {
                if (!vertical.Vertical || !OnSheet(vertical.Plan) ||
                    Mathf.Min(vertical.Plan.width, vertical.Plan.height) < CrossingMinUnits)
                    continue;

                foreach (var across in Streets)
                {
                    if (across.Vertical || !OnSheet(across.Plan) ||
                        Mathf.Min(across.Plan.width, across.Plan.height) < CrossingMinUnits)
                        continue;
                    if (!vertical.Plan.Overlaps(across.Plan))
                        continue;

                    // north and south arms: bars laid ACROSS the vertical road
                    Zebra(vertical.Plan.xMin, across.Plan.yMin - ZebraSetback - ZebraThick,
                        vertical.Plan.width, ZebraThick, false);
                    Zebra(vertical.Plan.xMin, across.Plan.yMax + ZebraSetback,
                        vertical.Plan.width, ZebraThick, false);

                    // west and east arms: bars laid across the horizontal road
                    Zebra(vertical.Plan.xMin - ZebraSetback - ZebraThick, across.Plan.yMin,
                        ZebraThick, across.Plan.height, true);
                    Zebra(vertical.Plan.xMax + ZebraSetback, across.Plan.yMin,
                        ZebraThick, across.Plan.height, true);
                }
            }
        }

        /// <summary>One arm of a crossing: bars the full width of the road being
        /// stepped over, at the design's own pitch. <paramref name="along"/> steps them
        /// down the sheet; otherwise they step across it.</summary>
        void Zebra(float x, float y, float w, float h, bool along)
        {
            var box = new Rect(x, y, w, h);
            if (!OnSheet(box) || !OnRoad(box) || !ClearOfLabels(box))
                return;

            _zebras.Add(box);

            var paint = TurfInk.Zebra;
            paint.a = (byte)Mathf.RoundToInt(TurfInk.ZebraStrength * 255f);

            if (along)
                for (float o = 0.8f; o < h - ZebraStripe * 0.5f; o += ZebraGap)
                    Ground.Over(PxX(x), PxY(y + o), PxW(w), PxW(ZebraStripe), paint);
            else
                for (float o = 0.8f; o < w - ZebraStripe * 0.5f; o += ZebraGap)
                    Ground.Over(PxX(x + o), PxY(y), PxW(ZebraStripe), PxW(h), paint);
        }

        /// <summary>Is the whole arm actually on tarmac? Sampled at the corners and the
        /// centre - cheap, and enough to reject an arm hanging off the end of a street
        /// that stops at this junction.</summary>
        bool OnRoad(Rect box)
        {
            const float inset = 0.3f;
            return Road(PxX(box.xMin + inset), PxY(box.yMin + inset)) &&
                   Road(PxX(box.xMax - inset), PxY(box.yMin + inset)) &&
                   Road(PxX(box.xMin + inset), PxY(box.yMax - inset)) &&
                   Road(PxX(box.xMax - inset), PxY(box.yMax - inset)) &&
                   Road(PxX(box.center.x), PxY(box.center.y));
        }

        bool ClearOfLabels(Rect box)
        {
            for (int i = 0; i < _labelBoxes.Count; i++)
                if (_labelBoxes[i].Overlaps(box))
                    return false;
            return true;
        }

        /// <summary>
        /// The centre divider: broken, and only where ONE carriageway lies. Where two
        /// overlap the count is two and the line stops - which keeps a junction clear
        /// without any junction ever being described to this code. It also stops at
        /// every zebra: a white line painted through a crossing is the mark that makes
        /// a junction unreadable.
        /// </summary>
        void LaneLines()
        {
            foreach (var street in Streets)
            {
                if (!OnSheet(street.Plan) ||
                    Mathf.Min(street.Plan.width, street.Plan.height) < LaneMinUnits)
                    continue;

                if (street.Vertical)
                {
                    int cx = PxX(street.Plan.center.x);
                    if (cx < 0 || cx >= TurfPlate.RW)
                        continue;
                    int y0 = Mathf.Max(0, PxY(street.Plan.yMin));
                    int y1 = Mathf.Min(TurfPlate.RH, PxY(street.Plan.yMax));
                    for (int ry = y0; ry < y1; ry++)
                    {
                        if (Mod(GroundY(ry), 12) >= 7 || !Road(cx, ry))
                            continue;
                        if (_roadCount[ry * TurfPlate.RW + cx] != 1 || InZebra(cx, ry))
                            continue;
                        Ground.Dot(cx, ry, TurfInk.Lane);
                    }
                }
                else
                {
                    int cy = PxY(street.Plan.center.y);
                    if (cy < 0 || cy >= TurfPlate.RH)
                        continue;
                    int x0 = Mathf.Max(0, PxX(street.Plan.xMin));
                    int x1 = Mathf.Min(TurfPlate.RW, PxX(street.Plan.xMax));
                    for (int rx = x0; rx < x1; rx++)
                    {
                        if (Mod(GroundX(rx), 12) >= 7 || !Road(rx, cy))
                            continue;
                        if (_roadCount[cy * TurfPlate.RW + rx] != 1 || InZebra(rx, cy))
                            continue;
                        Ground.Dot(rx, cy, TurfInk.Lane);
                    }
                }
            }
        }

        bool InZebra(int rx, int ry)
        {
            float ax = (float)rx / TurfPlate.S, ay = (float)ry / TurfPlate.S;
            for (int i = 0; i < _zebras.Count; i++)
            {
                var b = _zebras[i];
                if (ax >= b.xMin - 0.4f && ax <= b.xMax + 0.4f &&
                    ay >= b.yMin - 0.4f && ay <= b.yMax + 0.4f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The surveyor's own ruling, faint, dotted every third pixel so it reads as
        /// pencil rather than as another road. The pitch is a ROUND NUMBER OF METRES
        /// near the design's forty units - a grid that reads 41.3 m is not a grid - so
        /// it steps 25, 50, 100, 250, 500 as the wheel goes back.
        /// </summary>
        static readonly float[] GridSteps = { 25f, 50f, 100f, 250f, 500f, 1000f, 2500f };

        void SurveyGrid()
        {
            float wants = GridPitch * _plan.MetresPerUnit;
            float pitch = GridSteps[GridSteps.Length - 1];
            for (int i = 0; i < GridSteps.Length; i++)
                if (GridSteps[i] >= wants)
                {
                    pitch = GridSteps[i];
                    break;
                }

            // The lines stand on round metres of the world and the dots along them
            // are phased off the world's own pixels too, so the ruling is the same
            // ruling in every framing - a dot pattern started at the sheet's edge
            // crawled along the line at every pan.
            for (float wx = Mathf.Ceil(_plan.Origin.x / pitch) * pitch;
                 wx < _plan.Origin.x + _plan.World.width; wx += pitch)
            {
                int rx = PxX(_plan.ToPlan(new Vector2(wx, 0f)).x);
                for (int ry = Mod(-_fleckY, 3); ry < TurfPlate.RH; ry += 3)
                    Ground.Dot(rx, ry, TurfInk.Pencil);
            }

            for (float wz = Mathf.Ceil(_plan.Origin.y / pitch) * pitch;
                 wz < _plan.Origin.y + _plan.World.height; wz += pitch)
            {
                int ry = PxY(_plan.ToPlan(new Vector2(0f, wz)).y);
                for (int rx = Mod(-_fleckX, 3); rx < TurfPlate.RW; rx += 3)
                    Ground.Dot(rx, ry, TurfInk.Pencil);
            }
        }

        // -------------------------------------------------------------- buildings

        /// <summary>
        /// Every building in the city, at its true footprint, holding a reference to
        /// the very transform the street's own picker raycasts. Two sources, because
        /// the city has two: the blocks, whose buildings carry footprint colliders, and
        /// the quarters, whose sheds and hangars do not but which the builder reported
        /// by name as it raised them.
        /// </summary>
        void CollectBuildings(Transform blockRoot)
        {
            Buildings.Clear();
            Landmarks.Clear();
            _residentialGreens.Clear();
            _parkSurfaces.Clear();
            int id = 0;
            var seen = new HashSet<FootprintKey>();

            // Model first. If a view is currently alive its colliders are merely one
            // rendering of this data; if it is recycled the footprint remains here.
            foreach (var source in _builder.ResidentialMapSources)
                CollectResidential(source, seen, ref id);

            // Core's composed park blocks have no residential recipe and no stable live
            // GameObject contract. Publish their plan rectangles directly, exactly like
            // the residential model publishes its own park cells.
            var core = _builder.PrimaryCore;
            if (core?.Layout != null)
                for (int i = 0; i < core.Layout.Parks.Count; i++)
                    CollectCorePark(core, core.Layout.Parks[i]);

            CollectCoreLandmarks(core);

            if (blockRoot != null)
            {
                foreach (var collider in blockRoot.GetComponentsInChildren<Collider>(true))
                {
                    var tf = collider.transform;
                    var renderers = tf.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0)
                        continue;

                    var bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);

                    Add(++id, tf,
                        Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z),
                        bounds.size.y, tf.GetComponentInParent<BusinessMarker>(), seen);
                }
            }

            bool hasResidentialModel = _builder.ResidentialMapSources.Count > 0;
            foreach (var (area, rise, name) in _builder.QuarterRoofs)
            {
                // Core reported these to WalkObstacles before the model/map adapter
                // existed. Keep them as blockers, but do not let that build-time copy
                // survive beside a future replacement recipe as a ghost building.
                if (hasResidentialModel && IsResidentialRoof(name))
                    continue;
                Add(++id, null, area, rise, null, seen, name);
            }

            FitLandmarksToBuildings();

            // Biggest first, so a shed against a tower block still takes its own click:
            // the picker walks the list backwards and the small footprint is on top.
            Buildings.Sort((a, b) =>
                (b.World.width * b.World.height).CompareTo(a.World.width * a.World.height));
        }

        void CollectCorePark(CoreDistrict core, CoreLayout.Block block)
        {
            var box = block.Box;
            _residentialGreens.Add(core.Frame.ToWorldRect(box));

            int nx = Mathf.Max(3, Mathf.RoundToInt(box.width / CoreLayout.Cell));
            int nz = Mathf.Max(3, Mathf.RoundToInt(box.height / CoreLayout.Cell));
            int dice = unchecked(core.LayoutSeed * 7919 +
                Mathf.RoundToInt(box.xMin) * 104729 +
                Mathf.RoundToInt(box.yMin) * 1299709);
            var park = ParkWalk.Lay(nx, nz, ParkWalk.Edge.Alone(), new System.Random(dice));
            for (int j = 0; j < park.NZ; j++)
                for (int i = 0; i < park.NX; i++)
                {
                    var kind = park.Cells[i, j];
                    if (kind == ParkWalk.Ground.Grass)
                        continue;
                    var local = new Rect(
                        box.xMin + i * ParkWalk.Cell,
                        box.yMin + j * ParkWalk.Cell,
                        ParkWalk.Cell, ParkWalk.Cell);
                    _parkSurfaces.Add(new ParkSurface(core.Frame.ToWorldRect(local), kind));
                }
        }

        void CollectResidential(ResidentialMapSource source, HashSet<FootprintKey> seen,
                                ref int id)
        {
            if (source.Model == null)
                return;

            foreach (var recipe in source.Model.Blocks)
            {
                var plan = recipe?.Plan;
                if (plan == null)
                    continue;

                if (plan.Spots != null)
                    foreach (var spot in plan.Spots)
                    {
                        var unit = spot?.Unit;
                        if (unit == null)
                            continue;

                        float cell = ResidentialLot.Cell;
                        var local = new Rect(
                            recipe.LocalBounds.xMin + spot.I * cell,
                            recipe.LocalBounds.yMin + spot.J * cell,
                            Mathf.Max(1, spot.CW) * cell,
                            Mathf.Max(1, spot.CD) * cell);
                        var world = source.Frame.ToWorldRect(local);

                        if (unit.Kind == ResidentialKind.Park)
                        {
                            _residentialGreens.Add(world);
                            continue;
                        }

                        if (unit.Kind == ResidentialKind.Amenity)
                        {
                            // A complete gym/car-yard/skatepark owns the block, so its mark
                            // belongs at the block centre. An amenity mixed among houses uses
                            // its own footprint and leaves the surrounding residential survey.
                            seen.Add(new FootprintKey(world));
                            AddLandmark(world, unit.Name, recipe.BlockId,
                                replacesFootprints: true);
                            if (plan.YardBlock)
                                break;
                            continue;
                        }

                        TurfType? type = unit.Kind == ResidentialKind.Storefront || spot.Shop
                                ? TurfType.Shop
                                : (TurfType?)null;
                        Add(++id, null, world, Mathf.Max(2f, unit.MaxH), null, seen,
                            recipe.Name + ": " + unit.Name, type);
                    }

                CollectGroundFeatures(source.Frame, recipe, ResidentialLot.Use.Cafe, "CAFE");
                CollectGroundFeatures(source.Frame, recipe, ResidentialLot.Use.Subway, "SUBWAY");
            }
        }

        /// <summary>Cafes and subway stairs are generated into empty gaps rather than
        /// Plan.Spots. Connected cells are one map footprint, so every generated detail
        /// appears even when its recycled holder is nowhere near the camera.</summary>
        void CollectGroundFeatures(DistrictFrame frame, ResidentialBlockRecipe recipe,
                                   ResidentialLot.Use use, string label)
        {
            var plan = recipe.Plan;
            if (plan?.Ground == null)
                return;

            int width = plan.Ground.GetLength(0);
            int depth = plan.Ground.GetLength(1);
            var visited = new bool[width, depth];
            var open = new Queue<Vector2Int>();
            int group = 0;

            for (int j = 0; j < depth; j++)
                for (int i = 0; i < width; i++)
                {
                    if (visited[i, j] || plan.Ground[i, j] != use)
                        continue;

                    int minI = i, maxI = i, minJ = j, maxJ = j;
                    visited[i, j] = true;
                    open.Enqueue(new Vector2Int(i, j));
                    while (open.Count > 0)
                    {
                        var at = open.Dequeue();
                        minI = Mathf.Min(minI, at.x); maxI = Mathf.Max(maxI, at.x);
                        minJ = Mathf.Min(minJ, at.y); maxJ = Mathf.Max(maxJ, at.y);
                        Visit(at.x - 1, at.y);
                        Visit(at.x + 1, at.y);
                        Visit(at.x, at.y - 1);
                        Visit(at.x, at.y + 1);
                    }

                    float cell = ResidentialLot.Cell;
                    var local = new Rect(
                        recipe.LocalBounds.xMin + minI * cell,
                        recipe.LocalBounds.yMin + minJ * cell,
                        (maxI - minI + 1) * cell,
                        (maxJ - minJ + 1) * cell);
                    string name = recipe.Name + ": " + label + (++group > 1 ? " " + group : "");
                    AddLandmark(frame.ToWorldRect(local), name, recipe.BlockId,
                        replacesFootprints: true);

                    void Visit(int x, int y)
                    {
                        if (x < 0 || y < 0 || x >= width || y >= depth || visited[x, y] ||
                            plan.Ground[x, y] != use)
                            return;
                        visited[x, y] = true;
                        open.Enqueue(new Vector2Int(x, y));
                    }
                }
        }

        /// <summary>Core's fixed and generated amenities are read from their accepted
        /// plans. No icon depends on a collider, renderer or currently streamed view.</summary>
        void CollectCoreLandmarks(CoreDistrict core)
        {
            if (core == null)
                return;

            var territory = core.Territory;
            if (territory != null)
                for (int i = 0; i < territory.Blocks.Count; i++)
                {
                    var block = territory.Blocks[i];
                    string source = block.SourceName ?? "";
                    if (!source.Contains("warehouse") && !source.Contains("police") &&
                        !source.Contains("nightclub"))
                        continue;
                    AddLandmark(core.Frame.ToWorldRect(block.LocalBounds), source,
                        block.Id, replacesFootprints: true);
                }

            for (int i = 0; i < core.ParkingSites.Count; i++)
                AddLandmark(core.Frame.ToWorldRect(core.ParkingSites[i].Box),
                    "PARKING", BlockAt(core.Frame.ToWorldRect(core.ParkingSites[i].Box).center),
                    replacesFootprints: true);

            for (int i = 0; i < core.FuelSites.Count; i++)
            {
                var local = CoreAmenityLayout.FuelSurface(core.FuelSites[i]);
                AddLandmark(core.Frame.ToWorldRect(local), "FILLING STATION",
                    BlockAt(core.Frame.ToWorldRect(local).center), replacesFootprints: true);
            }

            CollectQuayLandmarks(core);
        }

        /// <summary>Copies Core's river geometry into plain world rectangles while still
        /// on the main thread. The draw pass can then print it without reading the live
        /// district or relying on the water plane's renderer.</summary>
        void CollectCoreRiver()
        {
            _corePaving.Clear();
            _coreWater.Clear();
            _corePromenades.Clear();
            var core = _builder != null ? _builder.PrimaryCore : null;
            var layout = core?.Layout;
            if (layout == null || layout.Water.width <= 0.01f || layout.Water.height <= 0.01f)
                return;

            // Compact each raster row into actual paved runs. Water, Spare and remote
            // Outside stay absent; only the single explicit city-edge pavement band is
            // retained before parks/buildings print their more specific ink.
            var raster = core.Raster;
            if (raster != null)
                for (int j = 0; j < raster.NZ; j++)
                {
                    int from = -1;
                    for (int i = 0; i <= raster.NX; i++)
                    {
                        var kind = i < raster.NX ? raster.At(i, j) : CoreRoads.Kind.Outside;
                        bool paved = core.IsCityEdgePavement(i, j) ||
                                     kind != CoreRoads.Kind.Outside &&
                                     kind != CoreRoads.Kind.Water &&
                                     kind != CoreRoads.Kind.Spare;
                        if (paved && from < 0)
                            from = i;
                        else if (!paved && from >= 0)
                        {
                            var strip = new Rect(raster.X(from), raster.Z(j),
                                (i - from) * CoreRoads.Cell, CoreRoads.Cell);
                            _corePaving.Add(core.Frame.ToWorldRect(strip));
                            from = -1;
                        }
                    }
                }

            // Match RiverBridge.Dress: the water continues beyond both ends of the built
            // city, so the survey reads it as a river passing the town rather than a blue
            // rectangle which stops at the last block.
            var water = Rect.MinMaxRect(
                layout.Water.xMin, layout.River.Z0 - RiverBridge.Reach,
                layout.Water.xMax, layout.River.Z1 + RiverBridge.Reach);
            _coreWater.Add(core.Frame.ToWorldRect(water));
            for (int i = 0; i < layout.Quays.Count; i++)
                _corePromenades.Add(core.Frame.ToWorldRect(layout.Quays[i].Box));
        }

        void CollectQuayLandmarks(CoreDistrict core)
        {
            var layout = core.Layout;
            if (layout == null || layout.Quays.Count == 0)
                return;

            var wants = QuayWalk.Cast(layout);
            for (int q = 0; q < layout.Quays.Count; q++)
            {
                var block = layout.Quays[q];
                var box = block.Box;
                int dice = unchecked(core.LayoutSeed * 7919 +
                    Mathf.RoundToInt(box.xMin) * 104729 +
                    Mathf.RoundToInt(box.yMin) * 1299709);
                var walk = QuayWalk.ForQuay(layout, block, wants[q], new System.Random(dice));
                for (int r = 0; r < walk.Rooms.Count; r++)
                {
                    var room = walk.Rooms[r];
                    string label;
                    switch (room.Programme)
                    {
                        case QuayWalk.Programme.Fair: label = "FAIRGROUND"; break;
                        case QuayWalk.Programme.Diner: label = "DINER"; break;
                        case QuayWalk.Programme.Terrace: label = "CAFE TERRACE"; break;
                        case QuayWalk.Programme.Landing: label = "LANDING"; break;
                        default: continue;
                    }

                    float z0 = layout.River.East
                        ? box.yMin + room.Z0 * QuayWalk.Cell
                        : box.yMax - room.Z1 * QuayWalk.Cell;
                    var local = new Rect(box.xMin, z0, box.width,
                        room.Length * QuayWalk.Cell);
                    AddLandmark(core.Frame.ToWorldRect(local), label, block.BlockId,
                        replacesFootprints: true);
                }
            }
        }

        int BlockAt(Vector2 world)
        {
            var block = _builder?.Territories?.BlockAt(new Vector3(world.x, 0f, world.y));
            return block != null ? block.Id : -1;
        }

        void AddLandmark(Rect world, string name, int blockId, bool replacesFootprints)
        {
            if (world.width <= 0.01f || world.height <= 0.01f)
                return;
            var kind = TurfLandmarkKinds.From(name);
            Landmarks.Add(new TurfLandmark
            {
                Kind = kind,
                Name = string.IsNullOrEmpty(name) ? "LANDMARK" : name.ToUpperInvariant(),
                Label = TurfLandmarkKinds.Label(kind),
                World = world,
                BlockId = blockId,
                ReplacesFootprints = replacesFootprints,
            });
        }

        /// <summary>Fixed Core and quay programmes begin with stable plan rectangles.
        /// Once the ordinary survey has collected their real building footprints, tighten
        /// those semantic shapes to the same bounds the map would otherwise draw.</summary>
        void FitLandmarksToBuildings()
        {
            for (int i = 0; i < Landmarks.Count; i++)
            {
                var landmark = Landmarks[i];
                if (landmark.BlockId < 0 || landmark.Kind == TurfLandmarkKind.Parking ||
                    landmark.Kind == TurfLandmarkKind.FuelStation)
                    continue;

                Rect fit = default;
                bool found = false;
                for (int b = 0; b < Buildings.Count; b++)
                {
                    var building = Buildings[b];
                    if (building.BlockId != landmark.BlockId ||
                        !landmark.World.Contains(building.World.center))
                        continue;
                    fit = found ? Encapsulate(fit, building.World) : building.World;
                    found = true;
                }
                if (found)
                    landmark.World = fit;
            }
        }

        static Rect Encapsulate(Rect one, Rect other) => Rect.MinMaxRect(
            Mathf.Min(one.xMin, other.xMin), Mathf.Min(one.yMin, other.yMin),
            Mathf.Max(one.xMax, other.xMax), Mathf.Max(one.yMax, other.yMax));

        static bool IsResidentialRoof(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            int split = name.LastIndexOf(": ", System.StringComparison.Ordinal);
            if (split < 0 || split + 2 >= name.Length)
                return false;
            string unit = name.Substring(split + 2);
            foreach (var candidate in ResidentialUnits.Known)
                if (candidate != null && candidate.Name == unit)
                    return true;
            return false;
        }

        void Add(int id, Transform tf, Rect world, float rise, BusinessMarker business,
                 HashSet<FootprintKey> seen = null, string reportedName = null,
                 TurfType? reportedType = null)
        {
            if (world.width <= 0.01f || world.height <= 0.01f)
                return;
            if (seen != null && !seen.Add(new FootprintKey(world)))
                return;

            int floors = Mathf.Max(1, Mathf.RoundToInt(rise / 3.2f));
            var type = reportedType ?? TypeOf(world, rise, floors, business);
            var district = DistrictAt(world.center);
            int coreBlock = BlockAt(world.center);

            Buildings.Add(new TurfBuilding
            {
                Id = id,
                Tf = tf,
                Business = business,
                World = world,
                Type = type,
                Floors = floors,
                Name = business != null && !string.IsNullOrEmpty(business.BusinessName)
                    ? business.BusinessName.ToUpperInvariant()
                    : !string.IsNullOrEmpty(reportedName)
                        ? reportedName.ToUpperInvariant()
                    : TurfTypeStyle.Of(type).Label + " " + (100 + id % 900),
                District = district.HasValue
                    ? district.Value.Name.ToUpperInvariant()
                    : "OUTSKIRTS",
                BlockId = business != null ? business.BlockId
                    : coreBlock >= 0 ? coreBlock : LotOf(world.center),
                // Only a business the city has priced has a figure; nothing else on
                // this map is rolled, so a footprint without one prints no row.
                Rent = business != null ? Mathf.Max(0, business.WeeklyIncome) : 0,
            });
        }

        /// <summary>
        /// What a footprint IS, decided from what the city already knows about it: the
        /// business marker on it first (the city's own word), then the quarter it
        /// stands in, then its shape. Height alone would call every warehouse a house
        /// and every tenement a tower.
        /// </summary>
        TurfType TypeOf(Rect world, float rise, int floors, BusinessMarker business)
        {
            var district = DistrictAt(world.center);
            if (district.HasValue)
            {
                if (district.Value.Kind == DistrictKind.Airport)
                    return world.width * world.height > 900f ? TurfType.Terminal : TurfType.Hangar;
                if (district.Value.Kind == DistrictKind.Harbor)
                    return TurfType.Warehouse;
            }

            if (business != null)
            {
                switch (business.Category)
                {
                    case LivingCity.Gameplay.BusinessCategory.Industrial:
                        return rise > 12f ? TurfType.Factory : TurfType.Warehouse;
                    case LivingCity.Gameplay.BusinessCategory.Port:
                        return TurfType.Warehouse;
                    default:
                        return floors >= 6 ? TurfType.Tower : TurfType.Shop;
                }
            }

            float area = world.width * world.height;
            if (area > 1200f && floors <= 3)
                return TurfType.Warehouse;
            if (floors >= 7)
                return TurfType.Tower;
            if (floors >= 3)
                return TurfType.Apartment;
            return TurfType.House;
        }

        (string Name, DistrictKind Kind, Rect World)? DistrictAt(Vector2 worldXZ)
        {
            foreach (var district in _builder.DistrictPlans)
                if (district.World.Contains(worldXZ))
                    return (district.Name, district.Kind, district.World);
            return null;
        }

        /// <summary>The block a point stands on, keyed the way the lot grid keys
        /// itself. -1 on a street outside every pad.</summary>
        int LotOf(Vector2 worldXZ)
        {
            foreach (var lot in _builder.LotPlans)
                if (lot.Slab.Contains(worldXZ))
                    return lot.Column * 64 + lot.Row;
            return -1;
        }

        /// <summary>
        /// Every footprint, top-down and flat: the fill, a diagonal hatch on the
        /// industrial types, a lighter core, a one-pixel ink hairline all the way
        /// round, and the owner's tick.
        ///
        /// Strictly plan view. No fake height, no offset copy, no side elevation - the
        /// city itself renders top-down, and a map that drew walls would be lying about
        /// which way the player is looking.
        ///
        /// The OWNER is a tick, not a tint. Colouring the whole footprint by family
        /// makes the plate a heat map and destroys the one thing a survey plate is for:
        /// reading what is actually built where.
        /// </summary>
        void PaintBuildings()
        {
            Built.Clear(new Color32(0, 0, 0, 0));

            foreach (var building in Buildings)
            {
                if (!OnSheet(building.Plan))
                    continue;
                if (FootprintReplaced(building))
                    continue;

                var style = TurfTypeStyle.Of(building.Type);
                int rx = PxX(building.Plan.xMin), ry = PxY(building.Plan.yMin);
                int rw = Mathf.Max(2, PxW(building.Plan.width));
                int rh = Mathf.Max(2, PxW(building.Plan.height));

                Built.Px(rx, ry, rw, rh, style.Fill);
                if (rw > 5 && rh > 5)
                    Built.Px(rx + 2, ry + 2, rw - 4, rh - 4, TurfInk.Core);

                if (style.Hatch)
                    for (int i = -rh; i < rw; i += 4)
                        for (int s = 0; s < rh; s++)
                        {
                            int x = rx + i + s;
                            if (x >= rx && x < rx + rw)
                                Built.Dot(x, ry + s, TurfInk.Hatch);
                        }

                Built.Px(rx, ry, rw, 1, TurfInk.Ink);
                Built.Px(rx, ry + rh - 1, rw, 1, TurfInk.Ink);
                Built.Px(rx, ry, 1, rh, TurfInk.Ink);
                Built.Px(rx + rw - 1, ry, 1, rh, TurfInk.Ink);

                // The owner's tick: ONE 3 x 3 square in the top left corner of the
                // footprint, the design's own mark. A second tick in the far corner
                // reads as two claims on one building.
                int gang = building.Owner;
                if (gang < 0)
                    continue;

                Built.Px(rx + 1, ry + rh - 4, 3, 3, TurfHouses.For(gang).Ink);
            }

            PaintLandmarks();
        }

        bool FootprintReplaced(TurfBuilding building)
        {
            for (int i = 0; i < Landmarks.Count; i++)
            {
                var landmark = Landmarks[i];
                if (!landmark.ReplacesFootprints)
                    continue;
                if (landmark.World.Contains(building.World.center))
                    return true;
            }
            return false;
        }

        /// <summary>The outer mark is the landmark's real surveyed footprint, exactly like
        /// every ordinary building on the plate. Only the TMP word floating over it has a
        /// fixed screen size; the footprint grows and shrinks truthfully with map scale.</summary>
        void PaintLandmarks()
        {
            for (int i = 0; i < Landmarks.Count; i++)
            {
                var landmark = Landmarks[i];
                if (!OnSheet(landmark.Plan))
                    continue;

                int rx = PxX(landmark.Plan.xMin), ry = PxY(landmark.Plan.yMin);
                int rw = Mathf.Max(2, PxW(landmark.Plan.width));
                int rh = Mathf.Max(2, PxW(landmark.Plan.height));
                Built.Px(rx, ry, rw, rh, TurfInk.BlockD);
                Built.Px(rx, ry, rw, 1, TurfInk.Ink);
                Built.Px(rx, ry + rh - 1, rw, 1, TurfInk.Ink);
                Built.Px(rx, ry, 1, rh, TurfInk.Ink);
                Built.Px(rx + rw - 1, ry, 1, rh, TurfInk.Ink);
            }
        }

        // ------------------------------------------------------------- districts

        /// <summary>
        /// The quarters the turf overlay washes. Two sources: the outlying districts
        /// the builder planned, each already a named rectangle, and the town itself cut
        /// into a handful of neighbourhoods off the street namer's own quarter list, so
        /// the wash has something to be about inside the grid.
        /// </summary>
        void CollectDistricts()
        {
            Districts.Clear();

            var territory = _builder.Territories.Plan;
            if (territory != null)
            {
                // Core already owns its six logical, conquerable quarters. The primary
                // DistrictPlan is one full-city rectangle and must not mask these specifics.
                for (int i = 0; i < territory.Quarters.Count; i++)
                {
                    var quarter = territory.Quarters[i];
                    // A quarter the deal never built - a rig keeps two of the six, and
                    // CoreTerritoryPlan still names all six so the ids stay stable - has
                    // no blocks and a zero rectangle sitting at the frame origin. Drawn,
                    // it becomes a place on the map that does not exist in the city.
                    if (quarter.BlockIds.Count == 0 || quarter.LocalBounds.width <= 0f)
                        continue;
                    Districts.Add(new TurfDistrict
                    {
                        Name = quarter.Name.ToUpperInvariant(),
                        World = _builder.Territories.WorldBounds(quarter.Id),
                        TerritoryId = quarter.Id,
                    });
                }
            }
            else
            {
                foreach (var district in _builder.DistrictPlans)
                    Districts.Add(new TurfDistrict
                    {
                        Name = string.IsNullOrEmpty(district.Name)
                            ? district.Kind.ToString().ToUpperInvariant()
                            : district.Name.ToUpperInvariant(),
                        World = district.World,
                    });

                // the ordinary town, in thirds across and halves up: six neighbourhoods,
                // as many names as a city this size carries and few enough to read
                const int Across = 3, Deep = 2;
                var town = TownWorld();
                var names = _builder.Streets;
                for (int i = 0; i < Across; i++)
                    for (int j = 0; j < Deep; j++)
                    {
                        var world = new Rect(
                            town.xMin + town.width * i / Across,
                            town.yMin + town.height * j / Deep,
                            town.width / Across, town.height / Deep);

                        Districts.Add(new TurfDistrict
                        {
                            Name = names != null
                                ? names.Quarter(j * Across + i).ToUpperInvariant()
                                : "QUARTER " + (j * Across + i + 1),
                            World = world,
                        });
                    }
            }

            foreach (var building in Buildings)
            {
                var district = DistrictOf(building.World.center);
                if (district != null)
                    building.District = district.Name;
            }
        }

        public TurfDistrict DistrictOf(Vector2 worldXZ)
        {
            // outlying quarters first: they are the specific claim, the town's own
            // sixths are the fallback
            for (int i = 0; i < Districts.Count; i++)
                if (Districts[i].World.Contains(worldXZ))
                    return Districts[i];
            return null;
        }

        /// <summary>
        /// Who holds each quarter. Ground is taken building by building, so a
        /// district's colour is whoever holds most footprints in it - and a tie is
        /// CONTESTED, never a lean toward one of them. Nobody holding anything leaves
        /// the quarter unclaimed.
        /// </summary>
        void ScoreDistricts()
        {
            var tally = new Dictionary<int, int>();
            foreach (var district in Districts)
            {
                if (district.TerritoryId != CoreQuarterId.None)
                {
                    district.GangId = district.TerritoryGangId;
                    continue;
                }

                tally.Clear();
                foreach (var building in Buildings)
                {
                    int gang = building.Owner;
                    if (gang < 0 || !district.World.Contains(building.World.center))
                        continue;
                    tally.TryGetValue(gang, out int had);
                    tally[gang] = had + 1;
                }

                int best = -1, bestCount = 0;
                bool tied = false;
                foreach (var pair in tally)
                {
                    if (pair.Value > bestCount)
                    {
                        best = pair.Key;
                        bestCount = pair.Value;
                        tied = false;
                    }
                    else if (pair.Value == bestCount)
                    {
                        tied = true;
                    }
                }

                district.GangId = bestCount == 0 ? -1 : (tied ? -2 : best);
            }
        }

        /// <summary>
        /// The turf wash: one flat colour per quarter, clipped to LAND scanline by
        /// scanline. No texture, no hatching, no border, no corner tag - all of those
        /// were tried on this design and rejected, because the moment the wash carries
        /// detail it competes with the plate it is supposed to be read through.
        ///
        /// The run test is what keeps the harbour clean. A rectangle of colour laid
        /// over a quarter that touches the water puts a family's claim on open sea;
        /// walking each row and breaking the run at every wet pixel does not.
        /// </summary>
        void PaintTurf()
        {
            Turf.Clear(new Color32(0, 0, 0, 0));

            foreach (var district in Districts)
            {
                if (district.GangId == -1 || !OnSheet(district.Plan))
                    continue;

                var wash = district.House.Wash;
                int x0 = Mathf.Max(0, PxX(district.Plan.xMin));
                int x1 = Mathf.Min(TurfPlate.RW, PxX(district.Plan.xMax));
                int y0 = Mathf.Max(0, PxY(district.Plan.yMin));
                int y1 = Mathf.Min(TurfPlate.RH, PxY(district.Plan.yMax));

                for (int ry = y0; ry < y1; ry++)
                {
                    int run = -1;
                    for (int rx = x0; rx <= x1; rx++)
                    {
                        bool wet = rx == x1 || _water[ry * TurfPlate.RW + rx];
                        if (!wet && run < 0)
                            run = rx;
                        else if (wet && run >= 0)
                        {
                            Turf.Px(run, ry, rx - run, 1, wash);
                            run = -1;
                        }
                    }
                }
            }

            PaintBlockReadings();
        }

        /// <summary>
        /// What each street READS as, over the quarter wash: the derived state, not a
        /// count of deeds under a rectangle. Five treatments, and none of them is a
        /// progress bar - a contested street is hatched between the two houses, which
        /// says "two names here" without implying a timer or a percentage.
        ///
        ///   influenced  a breath of the family's colour
        ///   controlled  the colour, plainly
        ///   dominated   the colour at full weight, with a ruled edge
        ///   contested   diagonal hatch, one house each way
        /// </summary>
        void PaintBlockReadings()
        {
            for (int i = 0; i < BlockReadings.Count; i++)
            {
                var reading = BlockReadings[i];
                if (!OnSheet(reading.Plan))
                    continue;

                var house = TurfHouses.For(reading.GangId);
                var rival = reading.RivalGangId >= 0
                    ? TurfHouses.For(reading.RivalGangId)
                    : house;

                int x0 = Mathf.Max(0, PxX(reading.Plan.xMin));
                int x1 = Mathf.Min(TurfPlate.RW, PxX(reading.Plan.xMax));
                int y0 = Mathf.Max(0, PxY(reading.Plan.yMin));
                int y1 = Mathf.Min(TurfPlate.RH, PxY(reading.Plan.yMax));
                if (x1 <= x0 || y1 <= y0)
                    continue;

                var contested =
                    reading.State == LivingCity.Territory.TerritoryControlState.Contested;
                var weight =
                    reading.State == LivingCity.Territory.TerritoryControlState.Influenced ? 0.12f
                    : reading.State == LivingCity.Territory.TerritoryControlState.Controlled ? 0.30f
                    : reading.State == LivingCity.Territory.TerritoryControlState.Dominated ? 0.48f
                    : 0.30f;

                for (int ry = y0; ry < y1; ry++)
                {
                    for (int rx = x0; rx < x1; rx++)
                    {
                        if (_water[ry * TurfPlate.RW + rx])
                            continue;

                        // The hatch runs on the diagonal, six pixels on and six off, so
                        // the two names read as one contested street rather than as a
                        // measured share of it.
                        var colour = contested && ((rx + ry) / 6) % 2 == 1
                            ? rival.Wash
                            : house.Wash;
                        Turf.Px(rx, ry, 1, 1, Tint(colour, weight));
                    }
                }

                if (reading.State != LivingCity.Territory.TerritoryControlState.Dominated)
                    continue;

                // Held outright gets a ruled edge - the one line of detail the wash is
                // allowed, because "outright" has to be legible at a glance.
                var edge = Tint(house.Wash, 0.75f);
                Turf.Px(x0, y0, x1 - x0, 1, edge);
                Turf.Px(x0, y1 - 1, x1 - x0, 1, edge);
                Turf.Px(x0, y0, 1, y1 - y0, edge);
                Turf.Px(x1 - 1, y0, 1, y1 - y0, edge);
            }
        }

        static Color32 Tint(Color32 wash, float weight) =>
            new Color32(wash.r, wash.g, wash.b, (byte)Mathf.Clamp(wash.a * weight, 0f, 255f));

        // ----------------------------------------------------------------- picks

        /// <summary>
        /// The footprint under an authored point, half a unit of tolerance so a shed is
        /// clickable. Walked backwards: the list is sorted big first, so the small
        /// footprint drawn on top is the one that answers.
        ///
        /// The test is in METRES against the world rectangle, not in units against the
        /// projected one. The projected rectangles belong to whatever draw is in
        /// flight; a pick that read them would answer out of a half-rewritten list
        /// every time the player clicked while the wheel was still moving.
        /// </summary>
        public TurfBuilding BuildingAt(Vector2 plan)
        {
            var at = _shown.ToWorld(plan);
            float slack = 0.5f * _shown.MetresPerUnit;

            for (int i = Buildings.Count - 1; i >= 0; i--)
            {
                var b = Buildings[i];
                if (at.x >= b.World.xMin - slack && at.x <= b.World.xMax + slack &&
                    at.y >= b.World.yMin - slack && at.y <= b.World.yMax + slack)
                    return b;
            }
            return null;
        }

        public TurfDistrict DistrictAtPlan(Vector2 plan)
        {
            var at = _shown.ToWorld(plan);
            for (int i = 0; i < Districts.Count; i++)
                if (Districts[i].World.Contains(at))
                    return Districts[i];
            return null;
        }

        // ------------------------------------------------------------------ maths

        static int PxX(float authored) => Mathf.RoundToInt(authored * TurfPlate.S);
        static int PxY(float authored) => Mathf.RoundToInt(authored * TurfPlate.S);
        static int PxW(float authored) => Mathf.Max(1, Mathf.RoundToInt(authored * TurfPlate.S));

        static Rect Intersect(Rect a, Rect b) => Rect.MinMaxRect(
            Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin),
            Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
    }
}
