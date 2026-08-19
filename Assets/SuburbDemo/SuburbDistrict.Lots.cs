using System.Collections.Generic;
using UnityEngine;

namespace SuburbDemo
{
    // One house lot, in the lot's own frame (u along the street from the left, v in
    // from the kerb): the preset house set back behind a lawn with its door on the
    // path, the driveway down one side to a garage or the house, a letterbox at the
    // kerb, a picket fence or hedge out front, wood fences between neighbours and
    // across the back, and a back yard with a few of the things a back yard has.
    // Everything is picked off the seed; props are laid with a footprint check so
    // nothing stands in anything else.
    public partial class SuburbDistrict
    {
        GameObject[] _houseGo;
        GameObject _cottage, _garage, _shed, _fenceWood, _fenceWoodPost, _fenceWhite, _fenceWhitePost, _fenceWhiteGate, _gardenBox,
            _forSale, _hoop, _sprinkler, _hoseReel, _barbeque, _doghouse, _trampoline, _sandpit, _poolIn, _poolAbove, _poolChild, _poolLadder,
            _compost, _wheelbarrow, _swing, _satDish, _antenna, _solar;
        List<GameObject> _hedges, _letterBoxes, _outdoorTables, _chairs, _deckchairs, _washingLines, _vegetables, _poolFloats, _carPrefabs, _trashBags, _outdoorLights;
        readonly List<Material> _palettes = new List<Material>();
        string _lastHouse;
        int _lastPalette = -1;

        void LoadLotKit()
        {
            _houseGo = new GameObject[TownKit.Houses.Length];
            for (int i = 0; i < _houseGo.Length; i++) _houseGo[i] = TownKit.Load(TownKit.Houses[i].Path);
            _cottage = TownKit.Load(TownKit.Cottage);
            _garage = TownKit.Load(TownKit.Garage);
            _shed = TownKit.Load(TownKit.Shed);
            _fenceWood = TownKit.Load(TownKit.FenceWood);
            _fenceWoodPost = TownKit.Load(TownKit.FenceWoodPost);
            _fenceWhite = TownKit.Load(TownKit.FenceWhite);
            _fenceWhitePost = TownKit.Load(TownKit.FenceWhitePost);
            _fenceWhiteGate = TownKit.Load(TownKit.FenceWhiteGate);
            _hedges = TownKit.LoadAll(TownKit.Hedges);
            _letterBoxes = TownKit.LoadAll(TownKit.LetterBoxes);
            _gardenBox = TownKit.Load(TownKit.GardenBox);
            _vegetables = TownKit.LoadAll(TownKit.Vegetables);
            _forSale = TownKit.Load(TownKit.ForSale);
            _hoop = TownKit.Load(TownKit.Hoop);
            _sprinkler = TownKit.Load(TownKit.Sprinkler);
            _hoseReel = TownKit.Load(TownKit.HoseReel);
            _barbeque = TownKit.Load(TownKit.Barbeque);
            _outdoorTables = TownKit.LoadAll(TownKit.OutdoorTables);
            _chairs = TownKit.LoadAll(TownKit.Chairs);
            _deckchairs = TownKit.LoadAll(TownKit.Deckchairs);
            _doghouse = TownKit.Load(TownKit.Doghouse);
            _washingLines = TownKit.LoadAll(TownKit.WashingLines);
            _trampoline = TownKit.Load(TownKit.Trampoline);
            _sandpit = TownKit.Load(TownKit.Sandpit);
            _poolIn = TownKit.Load(TownKit.PoolInground);
            _poolAbove = TownKit.TryLoad(TownKit.PoolAbove);
            _poolChild = TownKit.Load(TownKit.PoolChild);
            _poolLadder = TownKit.Load(TownKit.PoolLadder);
            _poolFloats = TownKit.LoadAll(TownKit.PoolFloats);
            _compost = TownKit.Load(TownKit.Compost);
            _wheelbarrow = TownKit.Load(TownKit.Wheelbarrow);
            _swing = TownKit.Load(TownKit.Swing);
            _satDish = TownKit.Load(TownKit.SatDish);
            _antenna = TownKit.Load(TownKit.Antenna);
            _solar = TownKit.Load(TownKit.SolarPanel);
            _carPrefabs = TownKit.LoadAll(TownKit.Cars);
            _trashBags = TownKit.LoadAll(TownKit.TrashBags);
            _outdoorLights = TownKit.LoadAll(TownKit.OutdoorLights);
            foreach (var path in TownKit.Palettes)
            {
                var m = TownKit.LoadMaterial(path);
                if (m != null) _palettes.Add(m);
            }
        }

        /// <summary>One of the pack's twelve palettes, never the one next door.</summary>
        Material PickPalette()
        {
            if (_palettes.Count == 0) return null;
            int k = Rnd(_palettes.Count);
            if (_palettes.Count > 1 && k == _lastPalette) k = (k + 1 + Rnd(_palettes.Count - 1)) % _palettes.Count;
            _lastPalette = k;
            return _palettes[k];
        }

        void ComposeLots()
        {
            foreach (var lot in _lots)
                if (lot.Use == LotUse.House) ComposeHouseLot(lot);
        }

        // ------------------------------------------------------------ the recipe

        // The Town demo's own proportions, measured off its houses: the facade stands
        // 2-5 m behind the pavement, the neighbours 1-5 m apart, the drive one tile
        // from the kerb to the house, the yard what is left behind - a suburb packed
        // the way the pack's author packed it.
        const float Setback = 3.5f;

        void ComposeHouseLot(Lot lot)
        {
            int wc = lot.WidthCells, dc = lot.DepthCells;
            float W = lot.W, D = lot.D;
            // the demo's facades stand 2-5 m back, not one line: a lot's own setback
            float vFront = Setback + Rnd(-0.5f, 1.5f);
            var lotRect = Rect.MinMaxRect(lot.Cx0 * Cell, lot.Cz0 * Cell, lot.Cx1 * Cell, lot.Cz1 * Cell);

            // the driveway down one side, the door column the other side of centre
            bool driveLeft = Chance(0.5f);
            if (lot.CornerLeft && !lot.CornerRight) driveLeft = false;   // keep the drive off the corner
            else if (lot.CornerRight && !lot.CornerLeft) driveLeft = true;
            int iDrive = driveLeft ? 0 : wc - 1;
            int iDoor = (wc % 2 == 1) ? wc / 2 : (driveLeft ? wc / 2 : wc / 2 - 1);

            // ---- the house
            int h = PickHouse(wc, W, D, vFront);
            GameObject housePrefab = h >= 0 ? _houseGo[h] : _cottage;
            var spec = h >= 0 ? TownKit.Houses[h] : new TownKit.House { Front = TownKit.Side.PlusX, Width = 5.6f, Depth = 5.6f, Height = 4.1f };
            float uHouse = iDoor * Cell + Cell * 0.5f;
            if (uHouse - spec.Width * 0.5f < 0.8f || uHouse + spec.Width * 0.5f > W - 0.8f) uHouse = W * 0.5f;
            // a narrow house leans away from the drive, so the drive has air
            if (spec.Width + 6f < W) uHouse = Mathf.Clamp(uHouse + (driveLeft ? 1f : -1f), spec.Width * 0.5f + 0.8f, W - spec.Width * 0.5f - 0.8f);

            Rect houseRect = Rect.zero;
            float houseBack = vFront + spec.Depth;
            var palette = PickPalette();
            if (housePrefab != null)
            {
                int yaw = TownKit.YawToFace(spec.Front, lot.Front);
                var frontPoint = lot.P(uHouse, vFront);
                var pivot = PivotForFront(housePrefab, yaw, frontPoint, lot.Front, lot.Along);
                // the presets' pivots sit under their foundations: the demo sinks every house
                // half a metre or more so the floor meets the lawn - the same sink here, taken
                // from the very demo instance whose yard this house will wear
                lot.YardIndex = PickYardVariant(housePrefab.name);
                pivot.y = lot.YardIndex >= 0 ? TownClusters.Yards[lot.YardIndex].HouseY : -0.55f;
                lot.House = TownKit.Prop(housePrefab, pivot, yaw, _lotRoot, "House " + lot.Index);
                TownKit.ApplyPalette(lot.House, palette);
                lot.Palette = palette;
                houseRect = TownKit.Footprint(housePrefab, pivot, yaw);
                lot.Taken.Add(houseRect);
                lot.DoorPos = frontPoint;
                lot.DoorOut = lot.Front;
                lot.HasDoor = true;
                houseBack = vFront + Mathf.Max(DepthAlong(houseRect, lot), spec.Depth);
                _lastHouse = housePrefab.name;
                if (debugFronts) DebugMarker(frontPoint + Vector3.up * 0.5f, Color.red);
                lot.HousePivot = pivot;
                lot.HouseYaw = yaw;
                lot.HousePrefab = housePrefab;
            }
            float uHouseL = uHouse - spec.Width * 0.5f, uHouseR = uHouse + spec.Width * 0.5f;

            // ---- the driveway: one tile from the kerb to the house front line; on past
            // the house where the house leaves the column clear, to a garage where the
            // lot is wide enough for one
            int frontYaw = TownKit.YawFacing(lot.Front);
            SetSurface(lot, iDrive, 0, Surface.Driveway, frontYaw);
            SetFrontPavement(lot, iDrive, SwVariant.Driveway);
            float uDrive = iDrive * Cell + Cell * 0.5f;
            lot.Taken.Add(CellRect(lot, iDrive, 0, 1, 1));
            float driveEdgeIn = driveLeft ? Cell : W - Cell;                   // the drive column's inner edge
            bool driveClearOfHouse = driveLeft ? uHouseL > driveEdgeIn + 0.3f : uHouseR < driveEdgeIn - 0.3f;
            bool garage = false;
            if (_garage != null && driveClearOfHouse && W >= spec.Width + 6.5f + 2.2f && D >= vFront + 9f + 1.5f)
            {
                int gYaw = TownKit.YawToFace(TownKit.Side.MinusZ, lot.Front);
                var gFront = lot.P(uDrive, vFront);
                var gPivot = PivotForFront(_garage, gYaw, gFront, lot.Front, lot.Along);
                gPivot.y = -0.6f;
                var gRect = TownKit.Footprint(_garage, gPivot, gYaw);
                if (!Overlaps(gRect, houseRect, 0.3f))
                {
                    var g = TownKit.Prop(_garage, gPivot, gYaw, _lotRoot, "Garage " + lot.Index);
                    TownKit.ApplyPalette(g, palette);
                    lot.Taken.Add(gRect);
                    garage = true;
                }
            }
            bool driveExtended = false;
            if (!garage && driveClearOfHouse && dc >= 2)
            {
                SetSurface(lot, iDrive, 1, Surface.Driveway, frontYaw);
                lot.Taken.Add(CellRect(lot, iDrive, 1, 1, 1));
                driveExtended = true;
            }
            // a car on the drive, where the drive runs on beside the house
            if (_carPrefabs.Count > 0 && driveExtended && Chance(0.55f))
            {
                var car = Pick(_carPrefabs);
                var at = lot.P(uDrive, Cell + 2.6f + Rnd(-0.3f, 0.3f));
                at.y = 0.09f;
                int yaw = TownKit.YawToFace(TownKit.Side.PlusZ, Chance(0.7f) ? lot.In : lot.Front);
                var go = TownKit.Prop(car, at, yaw + Rnd(-4f, 4f), _lotRoot, "Parked " + car.name);
                TownKit.StripForStatic(go);
                lot.Taken.Add(TownKit.Footprint(car, at, yaw));
            }
            if (_hoop != null && driveClearOfHouse && Chance(0.2f))
                TryPlace(_hoop, lot, uDrive + (driveLeft ? 2.3f : -2.3f), vFront + 1.5f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front), lotRect, 0.2f);

            // ---- the path from the kerb to the door
            if (lot.HasDoor && iDoor != iDrive)
            {
                SetSurface(lot, iDoor, 0, _pathDoor != null ? Surface.PathDoor : Surface.Path, frontYaw);
                SetFrontPavement(lot, iDoor, SwVariant.Path);
                lot.Taken.Add(CellRect(lot, iDoor, 0, 1, 1, 1.5f));
            }

            // ---- the kerb line: letterbox, bin, sign
            float uBox = driveLeft ? Cell + 0.5f : W - Cell - 0.5f;
            if (_letterBoxes.Count > 0)
                TryPlace(Pick(_letterBoxes), lot, uBox, 0.5f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front), lotRect, 0.1f);
            if (_bins.Count > 0 && Chance(0.3f))
                TryPlace(Pick(_bins), lot, uBox + (driveLeft ? 1f : -1f), 0.7f, Rnd(360), lotRect, 0.1f);
            if (_forSale != null && Chance(0.05f))
                TryPlace(_forSale, lot, uHouse + (driveLeft ? 3f : -3f), 1.2f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front), lotRect, 0.1f);

            // ---- the front boundary
            float frontRoll = Rnd();
            var frontFence = frontRoll < fencedFronts * 0.6f ? "white" : frontRoll < fencedFronts ? "hedge" : "open";
            var skip = new List<(float, float)> { (iDrive * Cell, iDrive * Cell + Cell) };
            if (lot.HasDoor && iDoor != iDrive) skip.Add((iDoor * Cell + 1.25f, iDoor * Cell + 3.75f));
            if (frontFence == "white")
            {
                LayFence(_fenceWhite, _fenceWhitePost, lot, 0f, 0.3f, W, 0.3f, skip);
                if (lot.HasDoor && iDoor != iDrive && _fenceWhiteGate != null)
                    PlaceRun(_fenceWhiteGate, lot.P(iDoor * Cell + 1.25f, 0.3f), lot.Along, 2.5f);
            }
            else if (frontFence == "hedge" && _hedges.Count > 0)
            {
                for (int i = 0; i < wc; i++)
                {
                    if (i == iDrive || (lot.HasDoor && i == iDoor)) continue;
                    var at = lot.P(i * Cell + Cell * 0.5f, 0.9f);
                    TownKit.Prop(Pick(_hedges), at, TownKit.YawToFace(TownKit.Side.PlusX, lot.Along), _lotRoot);
                }
            }

            // ---- the sides and the back
            bool leftStreet = lot.CornerLeft, rightStreet = lot.CornerRight;
            if (leftStreet) LaySideBoundary(lot, 0f, 0.3f, D, frontFence == "hedge");
            else LayFence(_fenceWood, _fenceWoodPost, lot, 0f, vFront, 0f, D, null);
            var rightNeighbour = LotAt(lot.P(W + Cell * 0.5f, vFront + Cell * 0.5f));
            if (rightStreet) LaySideBoundary(lot, W, 0.3f, D, frontFence == "hedge");
            else if (rightNeighbour == null || rightNeighbour.Use != LotUse.House || Vector3.Dot(rightNeighbour.Front, lot.Front) < 0.9f)
                LayFence(_fenceWood, _fenceWoodPost, lot, W, vFront, W, D, null);
            var behind = LotAt(lot.P(W * 0.5f, D + Cell * 0.5f));
            bool drawsBack = behind == null || behind.Use != LotUse.House || lot.Front.z < -0.5f || lot.Front.x > 0.5f;
            if (drawsBack) LayFence(_fenceWood, _fenceWoodPost, lot, 0f, D, W, D, null);

            // ---- the strip out front: flowers under the windows, a bush by the door, the
            // odd tuft; a tree only where a side gap is wide enough to hold one
            if (lot.House != null)
            {
                if (_flowers.Count > 0 && Chance(0.8f)) TryPlace(Pick(_flowers), lot, uHouseL + 1.2f, vFront - 0.8f, Rnd(360), lotRect, 0.05f);
                if (_flowers.Count > 0 && Chance(0.7f)) TryPlace(Pick(_flowers), lot, uHouseR - 1.2f, vFront - 0.8f, Rnd(360), lotRect, 0.05f);
                if (_bushes.Count > 0 && Chance(0.6f)) TryPlace(Pick(_bushes), lot, uHouse + Rnd(-2.5f, 2.5f), vFront - 0.9f, Rnd(360), lotRect, 0.1f);
                if (_flowers.Count > 0 && Chance(0.5f)) TryPlace(Pick(_flowers), lot, Rnd(1f, W - 1f), 1.0f, Rnd(360), lotRect, 0.05f);
            }
            float gapL = uHouseL, gapR = W - uHouseR;
            if (!driveLeft && gapL >= 3f && _yardTrees.Count > 0 && Chance(0.7f * treeDensity)) TryTree(Pick(_yardTrees), lot, gapL * 0.5f, Rnd(2f, Mathf.Max(2.5f, vFront + 3f)), lotRect);
            if (driveLeft && gapR >= 3f && _yardTrees.Count > 0 && Chance(0.7f * treeDensity)) TryTree(Pick(_yardTrees), lot, W - gapR * 0.5f, Rnd(2f, Mathf.Max(2.5f, vFront + 3f)), lotRect);
            if (_sprinkler != null && Chance(0.1f)) TryPlace(_sprinkler, lot, Rnd(1f, W - 1f), 1.5f, Rnd(360), lotRect, 0.1f);
            for (int k = 0; k < 2; k++)
                if (_tufts.Count > 0 && Chance(0.5f)) TryPlace(Pick(_tufts), lot, Rnd(1f, W - 1f), Rnd(0.8f, vFront - 0.5f), Rnd(360), lotRect, 0f);

            // ---- the yard: the demo's own dressing for this preset where it has one
            // (trees, beds, pool, shed, garage, toys, the dish on the roof, all at their
            // measured places), else the generic yard
            int yardPieces = lot.HousePrefab != null ? PlaceYard(lot, lotRect, spec) : 0;
            if (yardPieces < 4 && lot.HousePrefab != null) RoofProps(lot, lot.HousePrefab, lot.HousePivot, lot.HouseYaw);
            BackYard(lot, lotRect, vFront, houseBack, uHouse, spec.Width, garage, driveLeft);
        }

        /// <summary>One of the demo's yards for this preset, or -1 where the demo has none.</summary>
        int PickYardVariant(string prefabName)
        {
            var variants = new List<int>();
            for (int i = 0; i < TownClusters.Yards.Length; i++)
                if (TownClusters.Yards[i].Preset == prefabName) variants.Add(i);
            return variants.Count > 0 ? Pick(variants) : -1;
        }

        /// <summary>One of the demo's hand-dressed yard corners (vegetable garden, pool yard,
        /// barbecue corner, patio, junk yard...) dropped into the free back yard region, turned
        /// a random quarter, pieces that would clash with what stands left out. Returns how
        /// many pieces stood.</summary>
        int PlaceFill(Lot lot, Rect lotRect, float u0, float u1, float v0, float v1)
        {
            float rw = u1 - u0, rd = v1 - v0;
            if (rw < 4f || rd < 4f) return 0;
            var fits = new List<(int fill, int turn)>();
            for (int i = 0; i < TownClusters.Fills.Length; i++)
            {
                var f = TownClusters.Fills[i];
                for (int k = 0; k < 4; k++)
                {
                    float ex = (k % 2 == 0) ? f.HalfX : f.HalfZ, ez = (k % 2 == 0) ? f.HalfZ : f.HalfX;
                    if (2f * ex <= rw + 1.5f && 2f * ez <= rd + 1.5f) fits.Add((i, k));
                }
            }
            if (fits.Count == 0) return 0;
            // the fullest that fits, mostly
            fits.Sort((a, b) => TownClusters.Fills[b.fill].Pieces.Length.CompareTo(TownClusters.Fills[a.fill].Pieces.Length));
            var pick = fits[Mathf.Min(fits.Count - 1, Rnd(Mathf.Min(3, fits.Count)))];
            var fill = TownClusters.Fills[pick.fill];
            int turn = pick.turn * 90;
            float uc = (u0 + u1) * 0.5f + Rnd(-0.8f, 0.8f), vc = (v0 + v1) * 0.5f + Rnd(-0.8f, 0.8f);
            int lotYaw = TownKit.YawToFace(TownKit.Side.PlusX, lot.Along);
            float W = lot.W, D = lot.D;
            int placed = 0;
            foreach (var p in fill.Pieces)
            {
                // the piece's offset turned within the lot frame
                float a = turn * Mathf.Deg2Rad;
                float rx = p.X * Mathf.Cos(a) + p.Z * Mathf.Sin(a);
                float rz = -p.X * Mathf.Sin(a) + p.Z * Mathf.Cos(a);
                float lu = uc + rx, lv = vc + rz;
                if (lu < 0.5f || lu > W - 0.5f || lv < 0.5f || lv > D - 0.5f) continue;
                var prefab = TownKit.LoadByName(p.Name);
                if (prefab == null) continue;
                var world = lot.P(lu, lv);
                if (IsFlora(p.Name) && !OnLawn(world)) continue;
                world.y = p.Y + (p.Name == "SM_Env_Concrete_Base_01" ? 0.025f : 0f);
                float yaw = lotYaw + turn + p.Yaw;
                bool isTree = p.Name.Contains("Tree");
                var fp = isTree ? Rect.MinMaxRect(world.x - 0.6f, world.z - 0.6f, world.x + 0.6f, world.z + 0.6f) : TownKit.Footprint(prefab, world, yaw);
                bool clash = false;
                foreach (var t in lot.Taken) if (Overlaps(fp, t, -0.15f)) { clash = true; break; }
                if (clash) continue;
                TownKit.Prop(prefab, world, Quaternion.Euler(0f, lotYaw + turn, 0f) * p.Rot, _lotRoot);
                if (fp.width > 0.5f || fp.height > 0.5f) lot.Taken.Add(fp);
                if (p.Name.StartsWith("SM_Prop_Pool_Inground") || p.Name.StartsWith("SM_Prop_Pool_Aboveground")) lot.HasPool = true;
                placed++;
            }
            return placed;
        }

        /// <summary>Stands the demo's yard for the lot's preset round the house: every piece at
        /// its measured offset in the house's frame. Pieces that would land off the lot, on the
        /// drive or the path, or on a neighbour's things are left out; pieces on the house
        /// itself (shutters, chimney, dish, steps) always go. Returns how many stood.</summary>
        int PlaceYard(Lot lot, Rect lotRect, TownKit.House spec)
        {
            var variants = new List<int>();
            if (lot.YardIndex >= 0) variants.Add(lot.YardIndex);
            bool borrowed = false;
            if (variants.Count == 0)
            {
                // no demo yard for this preset: borrow one of a house about the same size, without
                // anything that sits on the house
                for (int i = 0; i < TownClusters.Yards.Length; i++)
                {
                    var pn = TownClusters.Yards[i].Preset;
                    if (pn == "SM_Bld_House_Preset_06" || pn == "SM_Bld_House_Preset_04") variants.Add(i);
                }
                borrowed = true;
            }
            if (variants.Count == 0) return 0;
            var yard = TownClusters.Yards[Pick(variants)];
            var rot = Quaternion.Euler(0f, lot.HouseYaw, 0f);
            var hb = TownKit.PrefabBounds(lot.HousePrefab);
            float W = lot.W, D = lot.D;
            int placed = 0;
            foreach (var p in yard.Pieces)
            {
                bool onHouse = p.X >= hb.min.x - 0.6f && p.X <= hb.max.x + 0.6f && p.Z >= hb.min.z - 0.6f && p.Z <= hb.max.z + 0.6f;
                if (borrowed && (onHouse || p.Y > 2.5f)) continue;
                // heights are ground-absolute (the demo's houses sit sunk, its yards on the lawn)
                var world = lot.HousePivot + rot * new Vector3(p.X, 0f, p.Z);
                world.y = p.Y;
                var d = world - lot.Origin;
                float lu = Vector3.Dot(d, lot.Along), lv = Vector3.Dot(d, lot.In);
                if (lu < 0.4f || lu > W - 0.4f || lv < 0.4f || lv > D - 0.4f) continue;
                if (IsFlora(p.Name) && !OnLawn(world)) continue;   // no tufts in the drive
                var prefab = TownKit.LoadByName(p.Name);
                if (prefab == null) continue;
                float yaw = lot.HouseYaw + p.Yaw;
                bool tile = p.Name == "SM_Env_Concrete_Base_01";
                if (tile) world.y += 0.025f;   // a patio slab just proud of the lawn tile
                // a tree is checked by its trunk, not its canopy; a little overlap is allowed,
                // the demo packed its beds right up against the walls
                bool isTree = p.Name.Contains("Tree") && !p.Name.Contains("Framed");
                var fp = isTree ? Rect.MinMaxRect(world.x - 0.6f, world.z - 0.6f, world.x + 0.6f, world.z + 0.6f) : TownKit.Footprint(prefab, world, yaw);
                if (!onHouse)
                {
                    bool clash = false;
                    foreach (var t in lot.Taken) if (Overlaps(fp, t, -0.25f)) { clash = true; break; }
                    if (clash) continue;
                }
                // a piece on the house (dormer, dish, panel, steps) stands at the house's own ground, level with it
                var go = TownKit.Prop(prefab, world, rot * p.Rot, _lotRoot, null, onHouse ? Ground(lot.HousePivot.x, lot.HousePivot.z) : (float?)null);
                if (p.Name == "SM_Bld_House_Preset_Garage_01") TownKit.ApplyPalette(go, lot.Palette);
                if (!onHouse && (fp.width > 0.5f || fp.height > 0.5f)) lot.Taken.Add(fp);
                if (p.Name.StartsWith("SM_Prop_Pool_Inground") || p.Name.StartsWith("SM_Prop_Pool_Aboveground")) lot.HasPool = true;
                placed++;
            }
            return placed;
        }

        int PickHouse(int wc, float W, float D, float vFront)
        {
            var fits = new List<int>();
            var bigFits = new List<int>();
            for (int i = 0; i < TownKit.Houses.Length; i++)
            {
                var h = TownKit.Houses[i];
                if (_houseGo[i] == null) continue;
                if (h.Width + 1.6f > W) continue;
                if (vFront + h.Depth + 1.5f > D) continue;
                if (_houseGo[i].name == _lastHouse) continue;
                if (h.Big) bigFits.Add(i); else fits.Add(i);
            }
            if (wc >= 6 && bigFits.Count > 0 && Chance(0.7f)) return Pick(bigFits);
            if (fits.Count == 0) fits.AddRange(bigFits);
            return fits.Count > 0 ? Pick(fits) : -1;
        }

        // Where to stand a prefab so that its far side along `front` lies through
        // frontPoint and it is centred on frontPoint across - whatever its pivot.
        static Vector3 PivotForFront(GameObject prefab, int yaw, Vector3 frontPoint, Vector3 front, Vector3 along)
        {
            var rect = TownKit.Footprint(prefab, Vector3.zero, yaw);
            float fr = float.MinValue, aMin = float.MaxValue, aMax = float.MinValue;
            foreach (var c in new[] { new Vector3(rect.xMin, 0f, rect.yMin), new Vector3(rect.xMax, 0f, rect.yMin), new Vector3(rect.xMin, 0f, rect.yMax), new Vector3(rect.xMax, 0f, rect.yMax) })
            {
                fr = Mathf.Max(fr, Vector3.Dot(c, front));
                float a = Vector3.Dot(c, along);
                aMin = Mathf.Min(aMin, a); aMax = Mathf.Max(aMax, a);
            }
            return frontPoint - front * fr - along * ((aMin + aMax) * 0.5f);
        }

        static float DepthAlong(Rect r, Lot lot)
        {
            // extent of a world rect along the lot's In axis
            var inAbs = new Vector2(Mathf.Abs(lot.In.x), Mathf.Abs(lot.In.z));
            return inAbs.x > 0.5f ? r.width : r.height;
        }

        void RoofProps(Lot lot, GameObject housePrefab, Vector3 pivot, int yaw)
        {
            if (!Chance(0.3f)) return;
            var b = TownKit.PrefabBounds(housePrefab);
            float top = pivot.y + b.max.y - 0.25f;
            var rect = TownKit.Footprint(housePrefab, pivot, yaw);
            var centre = new Vector3(rect.center.x, top, rect.center.y) + lot.Along * Rnd(-1.5f, 1.5f) + lot.In * Rnd(0.5f, 2f);
            float roll = Rnd();
            var prefab = roll < 0.4f ? _satDish : roll < 0.7f ? _antenna : _solar;
            if (prefab != null) TownKit.Prop(prefab, centre, Rnd(360), _lotRoot);
        }

        /// <summary>A pool behind the house, whichever of the pack's three the yard has
        /// room for: the in-ground one (7.5 x 10 m) lengthwise in a deep yard or sideways
        /// in the usual shallow one, with its ladder, a float on the water, a deckchair and
        /// somebody to lie on it; else the above-ground one; else the paddling pool.</summary>
        bool PlacePool(Lot lot, Rect lotRect, float uHouse, float vBack0, float vBack1)
        {
            float W = lot.W, depth = vBack1 - vBack0, vMid = (vBack0 + vBack1) * 0.5f;
            if (_poolIn != null && W >= 12f && depth >= 8.5f)
            {
                // lengthwise (the pool's long side along v) where the yard is deep enough, else across
                bool lengthwise = depth >= 11f && Chance(0.6f);
                float margin = lengthwise ? 5.5f : 6.5f;
                float u = Mathf.Clamp(uHouse + Rnd(-2f, 2f), margin, W - margin);
                int yaw = TownKit.YawToFace(TownKit.Side.PlusZ, lengthwise ? lot.In : lot.Along);
                if (TryPlace(_poolIn, lot, u, vMid, yaw, lotRect, 0.5f))
                {
                    // the ladder on a long side, a float, a deckchair at the foot, the idler by it
                    var side = lengthwise ? lot.Along : lot.In;
                    if (_poolLadder != null) TownKit.Prop(_poolLadder, lot.P(u, vMid) + side * 3.6f, TownKit.YawToFace(TownKit.Side.PlusZ, -side), _lotRoot);
                    if (_poolFloats.Count > 0) TownKit.Prop(Pick(_poolFloats), lot.P(u + Rnd(-1.5f, 1.5f), vMid + Rnd(-1.5f, 1.5f)) + Vector3.up * 0.05f, Rnd(360), _lotRoot);
                    float uChair = lengthwise ? u - 5.2f : u - 6.2f;
                    if (_deckchairs.Count > 0) TryPlace(Pick(_deckchairs), lot, uChair, vMid + 1f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Along), lotRect, 0.2f);
                    lot.IdleSpots.Add(lot.P(uChair + 0.4f, vMid - 1.5f));
                    lot.HasPool = true;
                    return true;
                }
            }
            if (_poolAbove != null && W >= 9f && depth >= 6.5f && TryPlace(_poolAbove, lot, Rnd(3.5f, W - 3.5f), Rnd(vBack0 + 3f, vBack1 - 3f), Rnd(360), lotRect, 0.4f))
            {
                lot.HasPool = true;
                return true;
            }
            if (_poolChild != null && TryPlace(_poolChild, lot, Rnd(2f, W - 2f), Rnd(vBack0 + 1.5f, vBack1 - 1.5f), Rnd(360), lotRect, 0.3f))
            {
                lot.HasPool = true;
                return true;
            }
            return false;
        }

        void BackYard(Lot lot, Rect lotRect, float vFront, float houseBack, float uHouse, float houseWidth, bool garage, bool driveLeft)
        {
            float W = lot.W, D = lot.D;
            float vBack0 = houseBack + 0.8f;           // behind the house
            float vBack1 = D - 1.0f;                   // up to the back fence
            // trees in the back corners and along the back fence, bushes along the fence -
            // even a yard too small for anything else gets these
            if (vBack1 - vBack0 >= 2.5f)
            {
                if (_tallTrees.Count > 0 && Chance(0.9f * treeDensity)) TryTree(Pick(_tallTrees), lot, Rnd(1.2f, 3.5f), Rnd(vBack1 - 2.5f, vBack1 - 1.2f), lotRect);
                if (_tallTrees.Count > 0 && Chance(0.75f * treeDensity)) TryTree(Pick(_tallTrees), lot, Rnd(W - 3.5f, W - 1.2f), Rnd(vBack1 - 2.5f, vBack1 - 1.2f), lotRect);
                if (_tallTrees.Count > 0 && W >= 20f && Chance(0.5f * treeDensity)) TryTree(Pick(_tallTrees), lot, Rnd(W * 0.35f, W * 0.65f), vBack1 - 1.5f, lotRect);
                for (float u = 1.5f; u < W - 1f; u += Rnd(2.5f, 4.5f))
                    if (_bushes.Count > 0 && Chance(0.5f)) TryPlace(Pick(_bushes), lot, u, vBack1 - 0.6f + Rnd(-0.2f, 0.3f), Rnd(360), lotRect, 0.05f);
            }
            if (vBack1 - vBack0 < 4f) return;
            // a pool in a good share of the yards - the pack has three, and the demo's own
            // yards are full of them: the in-ground one lengthwise where the yard is deep,
            // sideways across the usual shallow yard, the above-ground one where that will
            // not fit, the paddling pool in the smallest
            if (!lot.HasPool && Chance(poolChance)) PlacePool(lot, lotRect, uHouse, vBack0, vBack1);
            // then the demo's own yard corners - a vegetable garden, a barbecue, a patio,
            // the junk - where the yard has the room; the generic list only if none fit
            int filled = PlaceFill(lot, lotRect, 1f, W - 1f, vBack0, vBack1);
            if (filled >= 5) return;
            if (_yardTrees.Count > 0 && vBack1 - vBack0 >= 6f && Chance(0.6f * treeDensity)) TryTree(Pick(_yardTrees), lot, Rnd(2f, W - 2f), Rnd(vBack0 + 1.5f, vBack1 - 1.5f), lotRect);
            var picks = new List<string> { "shed", "pool", "trampoline", "washing", "bbq", "dog", "veg", "swing", "sandpit", "deck" };
            for (int k = picks.Count - 1; k > 0; k--) { int r = _rng.Next(k + 1); (picks[k], picks[r]) = (picks[r], picks[k]); }
            int features = Mathf.Clamp(Mathf.RoundToInt((vBack1 - vBack0) * W / 110f), 1, 3);
            int done = 0;
            foreach (var p in picks)
            {
                if (done >= features) break;
                bool ok = false;
                switch (p)
                {
                    case "shed":
                        {
                            var shed = _shed != null ? TryPlaceGo(_shed, lot, Chance(0.5f) ? 2.2f : W - 2.2f, vBack1 - 1.9f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front), lotRect, 0.4f) : null;
                            if (shed != null) { TownKit.ApplyPalette(shed, lot.Palette); ok = true; }
                        }
                        break;
                    case "pool":
                        ok = !lot.HasPool && PlacePool(lot, lotRect, uHouse, vBack0, vBack1);
                        break;
                    case "trampoline":
                        ok = _trampoline != null && TryPlace(_trampoline, lot, Rnd(3f, W - 3f), Rnd(vBack0 + 2.5f, vBack1 - 2.5f), Rnd(360), lotRect, 0.4f);
                        break;
                    case "washing":
                        ok = _washingLines.Count > 0 && TryPlace(Pick(_washingLines), lot, Rnd(2.5f, W - 2.5f), Rnd(vBack0 + 1.5f, vBack1 - 1.5f), Rnd(2) * 90 + TownKit.YawToFace(TownKit.Side.PlusX, lot.Along), lotRect, 0.3f);
                        break;
                    case "bbq":
                        {
                            float u = Mathf.Clamp(uHouse + Rnd(-3f, 3f), 3f, W - 3f);
                            float v = vBack0 + 1.8f;
                            var table = _outdoorTables.Count > 0 ? Pick(_outdoorTables) : null;
                            ok = table != null && TryPlace(table, lot, u, v, Rnd(360), lotRect, 0.3f);
                            if (ok)
                            {
                                if (_chairs.Count > 0) { TryPlace(Pick(_chairs), lot, u - 1.5f, v + 0.3f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Along), lotRect, 0.1f); TryPlace(Pick(_chairs), lot, u + 1.5f, v - 0.3f, TownKit.YawToFace(TownKit.Side.PlusZ, -lot.Along), lotRect, 0.1f); }
                                if (_barbeque != null) TryPlace(_barbeque, lot, u + 3.2f, v + 0.5f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front), lotRect, 0.2f);
                                lot.IdleSpots.Add(lot.P(u + 3.2f, v - 0.9f));
                            }
                        }
                        break;
                    case "dog":
                        ok = _doghouse != null && TryPlace(_doghouse, lot, Chance(0.5f) ? 1.6f : W - 1.6f, vBack0 + Rnd(1f, 3f), TownKit.YawToFace(TownKit.Side.PlusZ, Chance(0.5f) ? lot.Along : -lot.Along), lotRect, 0.2f);
                        break;
                    case "veg":
                        if (_gardenBox != null)
                        {
                            float u = Chance(0.5f) ? 3f : W - 3f;
                            float v = vBack1 - 2.2f;
                            ok = TryPlace(_gardenBox, lot, u, v, TownKit.YawToFace(TownKit.Side.PlusX, lot.Along), lotRect, 0.2f);
                            if (ok && _vegetables.Count > 0)
                                for (int k = 0; k < 4; k++)
                                    TownKit.Prop(Pick(_vegetables), lot.P(u - 1.4f + k * 0.95f, v + (k % 2 == 0 ? -0.35f : 0.35f)) + Vector3.up * 0.35f, Rnd(360), _lotRoot);
                        }
                        break;
                    case "swing":
                        ok = _swing != null && TryPlace(_swing, lot, Rnd(3f, W - 3f), Rnd(vBack0 + 2f, vBack1 - 2f), TownKit.YawToFace(TownKit.Side.PlusX, lot.Along), lotRect, 0.4f);
                        break;
                    case "sandpit":
                        ok = _sandpit != null && TryPlace(_sandpit, lot, Rnd(2.5f, W - 2.5f), Rnd(vBack0 + 2f, vBack1 - 2f), Rnd(4) * 90, lotRect, 0.3f);
                        break;
                    case "deck":
                        ok = _deckchairs.Count > 0 && TryPlace(Pick(_deckchairs), lot, Rnd(2f, W - 2f), Rnd(vBack0 + 1.5f, vBack1 - 1.5f), Rnd(360), lotRect, 0.2f);
                        break;
                }
                if (ok) done++;
            }
            // the odd barrow and heap
            if (_compost != null && Chance(0.2f)) TryPlace(_compost, lot, Chance(0.5f) ? 1.2f : W - 1.2f, vBack1 - 1.2f, Rnd(360), lotRect, 0.1f);
            if (_wheelbarrow != null && Chance(0.2f)) TryPlace(_wheelbarrow, lot, Rnd(2f, W - 2f), Rnd(vBack0, vBack1), Rnd(360), lotRect, 0.1f);
            if (_hoseReel != null && Chance(0.25f) && lot.House != null) TryPlace(_hoseReel, lot, uHouse + (driveLeft ? -houseWidth * 0.5f - 0.4f : houseWidth * 0.5f + 0.4f), vFront + 2f, Rnd(360), lotRect, 0.05f);
            if (_outdoorLights.Count > 0 && Chance(0.25f)) TryPlace(Pick(_outdoorLights), lot, uHouse + Rnd(-2f, 2f), vBack0 - 0.5f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.In), lotRect, 0.05f);
            if (lot.IdleSpots.Count == 0 && Chance(0.3f)) lot.IdleSpots.Add(lot.P(Rnd(3f, W - 3f), Rnd(vBack0 + 1f, vBack1 - 1f)));
        }

        // ------------------------------------------------------------ helpers

        Lot LotAt(Vector3 world)
        {
            int cx = CellOf(world.x), cz = CellOf(world.z);
            if (!InGrid(cx, cz)) return null;
            int id = _lotOf[cx, cz];
            return id >= 0 ? _lots[id] : null;
        }

        Rect CellRect(Lot lot, int i, int j, int wCells, int dCells, float shrink = 0f)
        {
            var a = lot.P(i * Cell, j * Cell);
            var b = lot.P((i + wCells) * Cell, (j + dCells) * Cell);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x) + shrink, Mathf.Min(a.z, b.z) + shrink, Mathf.Max(a.x, b.x) - shrink, Mathf.Max(a.z, b.z) - shrink);
        }

        static bool Overlaps(Rect a, Rect b, float pad)
            => a.xMin < b.xMax + pad && a.xMax > b.xMin - pad && a.yMin < b.yMax + pad && a.yMax > b.yMin - pad;

        bool TryPlace(GameObject prefab, Lot lot, float u, float v, float yaw, Rect lotRect, float pad)
            => TryPlaceGo(prefab, lot, u, v, yaw, lotRect, pad) != null;

        /// <summary>A prop on the lot at (u, v) if its footprint clears the lot edge and
        /// everything already standing; the pack keeps its pivots at the feet.</summary>
        GameObject TryPlaceGo(GameObject prefab, Lot lot, float u, float v, float yaw, Rect lotRect, float pad)
        {
            if (prefab == null) return null;
            var at = lot.P(u, v);
            var fp = TownKit.Footprint(prefab, at, yaw);
            if (fp.xMin < lotRect.xMin + 0.2f || fp.xMax > lotRect.xMax - 0.2f || fp.yMin < lotRect.yMin + 0.2f || fp.yMax > lotRect.yMax - 0.2f) return null;
            foreach (var t in lot.Taken) if (Overlaps(fp, t, pad)) return null;
            var go = TownKit.Prop(prefab, at, yaw, _lotRoot);
            lot.Taken.Add(fp);
            return go;
        }

        void TryTree(GameObject tree, Lot lot, float u, float v, Rect lotRect)
        {
            // a trunk is thin: check a 1.2 m square, not the canopy
            var at = lot.P(u, v);
            var fp = Rect.MinMaxRect(at.x - 0.6f, at.z - 0.6f, at.x + 0.6f, at.z + 0.6f);
            if (fp.xMin < lotRect.xMin || fp.xMax > lotRect.xMax || fp.yMin < lotRect.yMin || fp.yMax > lotRect.yMax) return;
            foreach (var t in lot.Taken) if (Overlaps(fp, t, 0.4f)) return;
            PlaceTree(tree, at, _floraRoot);
            lot.Taken.Add(fp);
        }

        /// <summary>A fence from lot point (u0,v0) to (u1,v1) in 2.5 m modules with a post
        /// at every joint, skipping the given u spans (for a run along the front).</summary>
        void LayFence(GameObject straight, GameObject post, Lot lot, float u0, float v0, float u1, float v1, List<(float, float)> skipU)
        {
            if (straight == null) return;
            var a = lot.P(u0, v0);
            var b = lot.P(u1, v1);
            var d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;
            d /= len;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.5f));
            float step = len / n;
            int yaw = TownKit.YawToFace(TownKit.Side.MinusX, d);
            bool alongU = Mathf.Abs(u1 - u0) > Mathf.Abs(v1 - v0);
            bool lastDrawn = false;
            for (int k = 0; k < n; k++)
            {
                float t0 = k * step, t1 = t0 + step;
                bool skipThis = false;
                if (skipU != null && alongU)
                {
                    float ua = u0 + (u1 - u0) * (t0 / len), ub = u0 + (u1 - u0) * (t1 / len);
                    float lo = Mathf.Min(ua, ub) + 0.05f, hi = Mathf.Max(ua, ub) - 0.05f;
                    foreach (var (s0, s1) in skipU) if (lo < s1 && hi > s0) skipThis = true;
                }
                var p = a + d * t0;
                if (skipThis)
                {
                    if (lastDrawn && post != null) TownKit.Prop(post, p, yaw, _lotRoot);
                    lastDrawn = false;
                    continue;
                }
                if (post != null && (k == 0 || !lastDrawn)) TownKit.Prop(post, p, yaw, _lotRoot);
                var go = TownKit.Prop(straight, p, yaw, _lotRoot);
                if (Mathf.Abs(step - 2.5f) > 0.01f) go.transform.localScale = new Vector3(step / 2.5f, 1f, 1f);
                lastDrawn = true;
            }
            if (post != null && lastDrawn) TownKit.Prop(post, b, yaw, _lotRoot);
        }

        // a corner lot's side along the street: picket or hedge, kept a little in
        void LaySideBoundary(Lot lot, float u, float v0, float v1, bool hedge)
        {
            float uIn = u < 0.01f ? 0.3f : u - 0.3f;
            if (hedge && _hedges.Count > 0)
            {
                for (float v = v0 + 2.5f; v < v1 - 2f; v += 5f)
                    TownKit.Prop(Pick(_hedges), lot.P(uIn + (u < 0.01f ? 0.6f : -0.6f), v), TownKit.YawToFace(TownKit.Side.PlusX, lot.In), _lotRoot);
            }
            else LayFence(_fenceWhite, _fenceWhitePost, lot, uIn, v0, uIn, v1, null);
        }

        /// <summary>One fence-like module set down with its pivot at `from`, running `len` along `dir`.</summary>
        void PlaceRun(GameObject piece, Vector3 from, Vector3 dir, float len)
        {
            if (piece == null) return;
            var go = TownKit.Prop(piece, from, TownKit.YawToFace(TownKit.Side.MinusX, dir), _lotRoot);
            var b = TownKit.PrefabBounds(piece);
            float own = Mathf.Max(0.5f, b.size.x);
            if (Mathf.Abs(own - len) > 0.05f) go.transform.localScale = new Vector3(len / own, 1f, 1f);
        }

        void DebugMarker(Vector3 at, Color colour)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(cube.GetComponent<Collider>());
            cube.transform.SetParent(_lotRoot, false);
            at.y += Ground(at.x, at.z);
            cube.transform.position = at;
            cube.transform.localScale = Vector3.one * 0.8f;
            cube.GetComponent<MeshRenderer>().sharedMaterial = TownKit.Flat("Debug", colour);
        }
    }
}
