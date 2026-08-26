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

        public readonly List<TurfBuilding> Buildings = new List<TurfBuilding>();
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

        /// <summary>The whole city and a margin - what the map shows when the wheel is
        /// all the way back, and the ground the heightfield was cached over.</summary>
        public Rect CityView { get; private set; }

        /// <summary>The city's own name.</summary>
        public string CityName { get; private set; } = "";

        public bool Ready { get; private set; }

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
            CollectDistricts();

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
        }

        /// <summary>
        /// Who holds what, sampled onto the footprints themselves. The one bridge
        /// between the live city and a draw that runs off the main thread: called here,
        /// on the main thread, immediately before a survey is handed to a worker.
        /// </summary>
        public void ReadOwners()
        {
            for (int i = 0; i < Buildings.Count; i++)
                Buildings[i].Owner = Buildings[i].GangId;
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

            _planView = FitToPlate(view);
            _plan = new TurfProjection(_planView);

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
            DrawGreen();
            DrawQuarters();
            LayRoads();
            InkKerbs();

            // The names are placed AND measured before the road markings, because the
            // crossings pass refuses to lay a zebra across one.
            NameStreets();
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
        }

        /// <summary>Every world rectangle, into the projection just chosen. Done in one
        /// place so no drawing pass has to remember to project.</summary>
        void Project()
        {
            foreach (var street in Streets)
                street.Plan = _plan.ToPlan(street.World);
            foreach (var building in Buildings)
                building.Plan = _plan.ToPlan(building.World);
            foreach (var district in Districts)
                district.Plan = _plan.ToPlan(district.World);
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
        /// own origin quantised to this draw's pixel size, so panning at a fixed zoom
        /// slides the texture with the city instead of re-sprinkling it.
        ///
        /// The quantisation is one add per pixel, not a divide: the ground under pixel
        /// rx is (origin + rx * metres), and dividing that by metres again is just
        /// (origin / metres) + rx - a constant per draw, cached in _fleckAt.</summary>
        int _fleckX, _fleckY;

        void SetFleckOrigin()
        {
            float metres = _plan.MetresPerUnit / TurfPlate.S;
            _fleckX = Mathf.FloorToInt(_plan.Origin.x / metres);
            _fleckY = Mathf.FloorToInt(_plan.Origin.y / metres);
        }

        uint GroundFleck(int rx, int ry, int salt) =>
            Fleck(_fleckX + rx + salt, _fleckY + ry - salt);

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
        /// hatch on the water, a one-pixel ink shoreline, two-by-two scrub blobs out in
        /// the country. No dither, no scanline, no glow.</summary>
        void DrawGround()
        {
            for (int ry = 0; ry < TurfPlate.RH; ry++)
                for (int rx = 0; rx < TurfPlate.RW; rx++)
                {
                    uint fleck = GroundFleck(rx, ry, 0);

                    if (_water[ry * TurfPlate.RW + rx])
                    {
                        Ground.Dot(rx, ry,
                            ry % 4 == 0 && (rx + ry) % 9 < 5 ? TurfInk.Water2 : TurfInk.Water);
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

            // scrub, in the country only - the ground the city has not taken. Inside
            // the grid the green comes off the lots actually left as parks.
            var town = _plan.ToPlan(TownWorld());
            int tx0 = PxX(town.xMin), tx1 = PxX(town.xMax);
            int ty0 = PxY(town.yMin), ty1 = PxY(town.yMax);
            for (int ry = 0; ry < TurfPlate.RH; ry += 2)
                for (int rx = 0; rx < TurfPlate.RW; rx += 2)
                {
                    if (Water(rx, ry))
                        continue;
                    if (rx >= tx0 && rx < tx1 && ry >= ty0 && ry < ty1)
                        continue;

                    uint fleck = GroundFleck(rx, ry, 7919);
                    if ((fleck & 0x3F) >= 6)
                        continue;
                    Ground.Px(rx, ry, 2, 2, (fleck & 0x40) != 0 ? TurfInk.Tree : TurfInk.Grass2);
                }
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
                Scatter(plan, 0x2FA1, 8, TurfInk.Tree, TurfInk.Grass2);
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

                var plan = _plan.ToPlan(district.World);
                if (!OnSheet(plan))
                    continue;

                Ground.Fill(plan, TurfInk.Concrete);
                Scatter(plan, 0x51B7, 20, TurfInk.Concrete2, TurfInk.Concrete2);
            }
        }

        /// <summary>Flecks over one rectangle, hashed off the ground so a pan does not
        /// re-sprinkle them.</summary>
        void Scatter(Rect plan, int salt, int odds, Color32 a, Color32 b)
        {
            int x0 = Mathf.Max(0, PxX(plan.xMin)), x1 = Mathf.Min(TurfPlate.RW, PxX(plan.xMax));
            int y0 = Mathf.Max(0, PxY(plan.yMin)), y1 = Mathf.Min(TurfPlate.RH, PxY(plan.yMax));

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
                        if (ry % 12 >= 7 || !Road(cx, ry))
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
                        if (rx % 12 >= 7 || !Road(rx, cy))
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

            for (float wx = Mathf.Ceil(_plan.Origin.x / pitch) * pitch;
                 wx < _plan.Origin.x + _plan.World.width; wx += pitch)
            {
                int rx = PxX(_plan.Units(wx - _plan.Origin.x));
                for (int ry = 0; ry < TurfPlate.RH; ry += 3)
                    Ground.Dot(rx, ry, TurfInk.Pencil);
            }

            for (float wz = Mathf.Ceil(_plan.Origin.y / pitch) * pitch;
                 wz < _plan.Origin.y + _plan.World.height; wz += pitch)
            {
                int ry = PxY(_plan.Units(wz - _plan.Origin.y));
                for (int rx = 0; rx < TurfPlate.RW; rx += 3)
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
            var roll = new TurfPlate.Roll(PaperSeed);
            int id = 0;

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
                        bounds.size.y, tf.GetComponentInParent<BusinessMarker>(), ref roll);
                }
            }

            foreach (var (area, rise, _) in _builder.QuarterRoofs)
                Add(++id, null, area, rise, null, ref roll);

            // Biggest first, so a shed against a tower block still takes its own click:
            // the picker walks the list backwards and the small footprint is on top.
            Buildings.Sort((a, b) =>
                (b.World.width * b.World.height).CompareTo(a.World.width * a.World.height));
        }

        void Add(int id, Transform tf, Rect world, float rise, BusinessMarker business,
            ref TurfPlate.Roll roll)
        {
            if (world.width <= 0.01f || world.height <= 0.01f)
                return;

            int floors = Mathf.Max(1, Mathf.RoundToInt(rise / 3.2f));
            var type = TypeOf(world, rise, floors, business);

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
                    : TurfTypeStyle.Of(type).Label + " " + (100 + id % 900),
                District = "OUTSKIRTS",
                BlockId = business != null ? business.BlockId : LotOf(world.center),
                Occupants = 2 + roll.Next(28),
                Rent = business != null && business.WeeklyIncome > 0
                    ? business.WeeklyIncome
                    : (1 + roll.Next(9)) * 40,
                File = "B-" + (1300 + id),
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

            foreach (var district in _builder.DistrictPlans)
                Districts.Add(new TurfDistrict
                {
                    Name = string.IsNullOrEmpty(district.Name)
                        ? district.Kind.ToString().ToUpperInvariant()
                        : district.Name.ToUpperInvariant(),
                    World = district.World,
                });

            // the town, in thirds across and halves up: six neighbourhoods, as many
            // names as a city this size carries and few enough that each wash is a
            // shape rather than a stripe
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
        }

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
