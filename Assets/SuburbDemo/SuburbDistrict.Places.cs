using System.Collections.Generic;
using UnityEngine;

namespace SuburbDemo
{
    // The places a suburb has besides houses - each once, each on a stretch of
    // frontage the carve reserved for it, in blocks of their own: the church on its
    // lawn, the gas station, the hardware store and the corner shop each on a
    // forecourt, the pocket park with the playground. Laid out by hand in the lot
    // frame (u along the kerb, v in from it).
    public partial class SuburbDistrict
    {
        GameObject _church, _shopPad, _shopSign, _fountain, _fountainBase, _bikeStand, _vending, _gaspump, _gaspumpBase, _gaspumpCover,
            _fort, _slide, _roundabout, _playTable, _iceFreezer, _propane;
        List<GameObject> _shops, _boxes;

        void LoadPlaceKit()
        {
            _church = TownKit.Load(TownKit.Church);
            _shops = TownKit.LoadAll(TownKit.Shops);
            _shopPad = TownKit.Load(TownKit.ShopPad);
            _shopSign = TownKit.Load(TownKit.ShopSign);
            _fountain = TownKit.Load(TownKit.Fountain);
            _fountainBase = TownKit.Load(TownKit.FountainBase);
            _bikeStand = TownKit.Load(TownKit.BikeStand);
            _vending = TownKit.Load(TownKit.Vending);
            _gaspump = TownKit.Load(TownKit.Gaspump);
            _gaspumpBase = TownKit.Load(TownKit.GaspumpBase);
            _gaspumpCover = TownKit.Load(TownKit.GaspumpCover);
            _fort = TownKit.Load(TownKit.Fort);
            _slide = TownKit.Load(TownKit.Slide);
            _roundabout = TownKit.Load(TownKit.Roundabout);
            _playTable = TownKit.Load(TownKit.PlayTable);
            _iceFreezer = TownKit.Load(TownKit.IceFreezer);
            _propane = TownKit.Load(TownKit.Propane);
            _boxes = TownKit.LoadAll(TownKit.Boxes);
        }

        readonly List<(Lot lot, float u)> _busStops = new List<(Lot, float)>();

        void ComposePlaces()
        {
            LoadPlaceKit();
            foreach (var lot in _lots)
            {
                switch (lot.Use)
                {
                    case LotUse.Church: ComposeChurch(lot); break;
                    case LotUse.GasStation: ComposeGasStation(lot); break;
                    case LotUse.Hardware: ComposeHardware(lot); break;
                    case LotUse.Shop: ComposeShop(lot); break;
                    case LotUse.Park: ComposePark(lot); break;
                }
            }
        }

        // ------------------------------------------------------------ church

        void ComposeChurch(Lot lot)
        {
            float W = lot.W, D = lot.D;
            var lotRect = Rect.MinMaxRect(lot.Cx0 * Cell, lot.Cz0 * Cell, lot.Cx1 * Cell, lot.Cz1 * Cell);
            int wc = lot.WidthCells;
            int iPath = wc / 2;
            float uMid = iPath * Cell + Cell * 0.5f;
            float vFront = 2f * Cell;
            int frontYaw = TownKit.YawFacing(lot.Front);

            // the path in, a concrete forecourt before the doors
            SetSurface(lot, iPath, 0, Surface.Path, frontYaw);
            SetFrontPavement(lot, iPath, SwVariant.Path);
            for (int i = iPath - 1; i <= iPath + 1; i++) SetSurface(lot, i, 1, Surface.Concrete, 0);
            lot.Taken.Add(CellRect(lot, iPath - 1, 0, 3, 2));

            if (_church != null)
            {
                int yaw = TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front);
                var frontPoint = lot.P(uMid, vFront);
                var pivot = PivotForFront(_church, yaw, frontPoint, lot.Front, lot.Along);
                lot.House = TownKit.Prop(_church, pivot, yaw, _placeRoot, "Church");
                lot.Taken.Add(TownKit.Footprint(_church, pivot, yaw));
                PlaceClusterAt(TownClusters.Church, pivot, yaw);
                lot.DoorPos = frontPoint;
                lot.DoorOut = lot.Front;
                lot.HasDoor = true;
                if (debugFronts) DebugMarker(frontPoint + Vector3.up * 0.5f, Color.red);
            }

            // benches either side of the forecourt, lights at the doors, trees in the corners
            if (_bench != null)
            {
                TryPlace(_bench, lot, uMid - 7f, vFront - 1.5f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front), lotRect, 0.2f);
                TryPlace(_bench, lot, uMid + 7f, vFront - 1.5f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front), lotRect, 0.2f);
                TryPlace(_bench, lot, uMid - 9f, vFront + 6f, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Along), lotRect, 0.2f);
                TryPlace(_bench, lot, uMid + 9f, vFront + 6f, TownKit.YawToFace(TownKit.Side.PlusZ, -lot.Along), lotRect, 0.2f);
            }
            if (_outdoorLights.Count > 0)
            {
                TryPlace(_outdoorLights[_outdoorLights.Count - 1], lot, uMid - 4f, vFront - 0.8f, 0, lotRect, 0.1f);
                TryPlace(_outdoorLights[_outdoorLights.Count - 1], lot, uMid + 4f, vFront - 0.8f, 0, lotRect, 0.1f);
            }
            foreach (var (u, v) in new[] { (3.5f, 3.5f), (W - 3.5f, 3.5f), (3.5f, D - 3.5f), (W - 3.5f, D - 3.5f), (uMid - 10f, D - 5f), (uMid + 10f, D - 5f), (uMid - 14f, D * 0.5f), (uMid + 14f, D * 0.5f) })
                if (_tallTrees.Count > 0) TryTree(Pick(_tallTrees), lot, u, v, lotRect);
            for (int k = 0; k < Mathf.RoundToInt(4f * treeDensity); k++)
                if (_yardTrees.Count > 0) TryTree(Pick(_yardTrees), lot, Rnd(2f, W - 2f), Rnd(vFront + 3f, D - 2f), lotRect);
            for (int k = 0; k < 6; k++)
                if (_flowers.Count > 0) TryPlace(Pick(_flowers), lot, Rnd(2f, W - 2f), Rnd(2f, D - 2f), Rnd(360), lotRect, 0.2f);
            for (int k = 0; k < 5; k++)
                if (_bushes.Count > 0) TryPlace(Pick(_bushes), lot, Rnd(1.5f, W - 1.5f), Rnd(vFront + 2f, D - 1.5f), Rnd(360), lotRect, 0.2f);

            // a small car park down one side
            int iPark = lot.CornerLeft ? wc - 2 : 0;
            for (int i = iPark; i < iPark + 2; i++)
                for (int j = 0; j < 3; j++) SetSurface(lot, i, j, Surface.Asphalt, 0);
            SetFrontPavement(lot, iPark, SwVariant.Driveway);
            SetFrontPavement(lot, iPark + 1, SwVariant.Driveway);
            lot.Taken.Add(CellRect(lot, iPark, 0, 2, 3));
            if (_carPrefabs.Count > 0)
                for (int k = 0; k < 2; k++)
                    if (Chance(0.7f))
                    {
                        var car = Pick(_carPrefabs);
                        var at = lot.P(iPark * Cell + 2.5f + k * 5f, 9f);
                        at.y = 0.05f;
                        var go = TownKit.Prop(car, at, TownKit.YawToFace(TownKit.Side.PlusZ, lot.In) + Rnd(-3f, 3f), _placeRoot);
                        TownKit.StripForStatic(go);
                    }

            // a picket fence round the lawn, open at the path and the car park
            var skip = new List<(float, float)> { (iPath * Cell + 0.5f, iPath * Cell + Cell - 0.5f), (iPark * Cell, iPark * Cell + 2f * Cell) };
            LayFence(_fenceWhite, _fenceWhitePost, lot, 0f, 0.3f, W, 0.3f, skip);
            LayFence(_fenceWhite, _fenceWhitePost, lot, 0.3f, 0.3f, 0.3f, D - 0.3f, null);
            LayFence(_fenceWhite, _fenceWhitePost, lot, W - 0.3f, 0.3f, W - 0.3f, D - 0.3f, null);
            LayFence(_fenceWhite, _fenceWhitePost, lot, 0f, D - 0.3f, W, D - 0.3f, null);
        }

        // ------------------------------------------------------------ the demo's clusters

        /// <summary>Stands one of the demo's dressed clusters on the lot: the anchor at lot
        /// point (u, v) turned so the cluster's front faces the street, every piece at its
        /// measured offset and turn. Pieces that would land off the lot (past the back
        /// fence, on a neighbour, further onto the pavement than its grass verge) are left
        /// out. Ground tiles in the cluster are snapped to the 5 m grid and the lot's own
        /// tile under them withheld. Returns the anchor's instance.</summary>
        GameObject PlaceCluster(TownClusters.Cluster c, Lot lot, float u, float v, string anchorName, bool pickable, System.Collections.Generic.Dictionary<string, string> pieceNames = null)
        {
            var anchorPrefab = TownKit.LoadByName(c.Anchor);
            if (anchorPrefab == null) return null;
            int anchorYaw = TownKit.YawToFace(c.Front, lot.Front);
            var rot = Quaternion.Euler(0f, anchorYaw, 0f);
            var anchorPos = lot.P(u, v);

            // snap: the first ground tile in the cluster lands on the cell grid
            foreach (var p in c.Pieces)
            {
                if (!IsGroundTile(p.Name)) continue;
                var wp = anchorPos + rot * new Vector3(p.X, 0f, p.Z);
                var corner = TileMinCorner(wp, Mathf.RoundToInt(anchorYaw + p.Yaw));
                var snapped = new Vector3(Mathf.Round(corner.x / Cell) * Cell, 0f, Mathf.Round(corner.z / Cell) * Cell);
                anchorPos += snapped - corner;
                break;
            }

            float W = lot.W, D = lot.D;
            bool Inside(Vector3 world, out float lu, out float lv)
            {
                var d = world - lot.Origin;
                lu = Vector3.Dot(d, lot.Along);
                lv = Vector3.Dot(d, lot.In);
                return lu >= 0.3f && lu <= W - 0.3f && lv >= -2.2f && lv <= D - 0.3f;
            }

            var anchorGo = TownKit.Prop(anchorPrefab, anchorPos, anchorYaw, _placeRoot, anchorName);
            lot.Taken.Add(TownKit.Footprint(anchorPrefab, anchorPos, anchorYaw, 0.3f));
            // the buildings of the cluster stand level at their own ground; whatever stands on
            // or in one (its interior, roof gear, signs) goes at that building's ground too
            var buildings = new List<(Rect at, float ground)>();
            if (c.Anchor.StartsWith("SM_Bld_")) buildings.Add((TownKit.Footprint(anchorPrefab, anchorPos, anchorYaw, 0.5f), Ground(anchorPos.x, anchorPos.z)));
            foreach (var p in c.Pieces)
            {
                if (!p.Name.StartsWith("SM_Bld_")) continue;
                var prefab = TownKit.LoadByName(p.Name);
                if (prefab == null) continue;
                var world = anchorPos + rot * new Vector3(p.X, p.Y, p.Z);
                buildings.Add((TownKit.Footprint(prefab, world, anchorYaw + p.Yaw, 0.5f), Ground(world.x, world.z)));
            }
            float? GroundOf(Vector3 world, string name)
            {
                if (name.StartsWith("SM_Bld_")) return null;
                foreach (var b in buildings) if (b.at.Contains(new Vector2(world.x, world.z))) return b.ground;
                return null;
            }
            int placed = 0, skipped = 0;
            foreach (var p in c.Pieces)
            {
                var world = anchorPos + rot * new Vector3(p.X, p.Y, p.Z);
                if (!Inside(world, out _, out _)) { skipped++; continue; }
                if (IsFlora(p.Name) && !OnLawn(world)) { skipped++; continue; }
                var prefab = TownKit.LoadByName(p.Name);
                if (prefab == null) continue;
                float yaw = anchorYaw + p.Yaw;
                if (IsGroundTile(p.Name))
                {
                    var corner = TileMinCorner(world, Mathf.RoundToInt(yaw));
                    int cx = CellOf(corner.x + 1f), cz = CellOf(corner.z + 1f);
                    if (InGrid(cx, cz) && (_kind[cx, cz] == CellKind.Lot || _kind[cx, cz] == CellKind.Free)) _surface[cx, cz] = Surface.None;
                    else { skipped++; continue; }   // a tile that would land on the street
                }
                var go = TownKit.Prop(prefab, world, rot * p.Rot, _placeRoot, pieceNames != null && pieceNames.TryGetValue(p.Name, out var nm) ? nm : null, GroundOf(world, p.Name));
                if (p.Name.StartsWith("SM_Veh_")) TownKit.StripForStatic(go);
                // the demo hangs the station's 24/7 sign at 5 m on a 2.6 m street-sign pole and
                // leaves the air between; the pole is stretched into a proper pylon here
                if (c.Label == "GasStation" && p.Name == "SM_Prop_StreetSign_Pole_01") go.transform.localScale = new Vector3(1.3f, 1.95f, 1.3f);
                var b = TownKit.PrefabBounds(prefab);
                if (b.size.x > 0.6f || b.size.z > 0.6f) lot.Taken.Add(TownKit.Footprint(prefab, world, yaw));
                placed++;
            }
            if (pickable && anchorGo != null) { /* named for the card picker; BuildPickables boxes it */ }
            Debug.Log($"[SuburbDemo] cluster {c.Label}: {placed} pieces placed, {skipped} off the lot");
            return anchorGo;
        }

        static bool IsGroundTile(string name) => name == "SM_Env_Concrete_Base_01" || name == "SM_Env_Road_01" || name == "SM_Env_Grass_01" || name == "SM_Env_DirtPatch_01";

        /// <summary>Plants and tufts: in the demo they stood on lawns; here they may only stand on
        /// a lawn cell, never on the forecourt asphalt or a concrete slab.</summary>
        static bool IsFlora(string name) =>
            name.StartsWith("SM_Env_Grass_Patch") || name.StartsWith("SM_Gerneric_Grass") || name.StartsWith("SM_Env_Grass_Edge") ||
            name.StartsWith("SM_Env_FlowerPatch") || name.StartsWith("SM_Env_Bush") || name.StartsWith("SM_Env_Tree") ||
            name.StartsWith("SM_Generic_Tree") || name.StartsWith("SM_Env_Hedge") || name.StartsWith("SM_Env_Plant") ||
            name.StartsWith("SM_Env_Gardenbox") || name.StartsWith("SM_Env_DirtPatch") || name.StartsWith("SM_Generic_Small_Rocks") ||
            name.StartsWith("SM_Generic_TreeDead") || name.StartsWith("SM_Generic_TreeStump");

        bool OnLawn(Vector3 world)
        {
            int cx = CellOf(world.x), cz = CellOf(world.z);
            if (!InGrid(cx, cz)) return false;
            if (_kind[cx, cz] != CellKind.Lot && _kind[cx, cz] != CellKind.Free) return false;
            return _surface[cx, cz] == Surface.Grass || _surface[cx, cz] == Surface.Grass2;
        }

        /// <summary>Inverse of TownKit.Tile: the cell a corner-pivot 5 m tile at this pivot and yaw covers.</summary>
        static Vector3 TileMinCorner(Vector3 pivot, int yaw)
        {
            switch (((yaw % 360) + 360) % 360)
            {
                case 0: return new Vector3(pivot.x - Cell, 0f, pivot.z - Cell);
                case 90: return new Vector3(pivot.x - Cell, 0f, pivot.z);
                case 180: return new Vector3(pivot.x, 0f, pivot.z);
                default: return new Vector3(pivot.x, 0f, pivot.z - Cell);
            }
        }

        /// <summary>A cluster at an exact world pose (the church's own pews inside the
        /// church) - every piece at the anchor's ground, level with the building.</summary>
        void PlaceClusterAt(TownClusters.Cluster c, Vector3 anchorPos, int anchorYaw)
        {
            var rot = Quaternion.Euler(0f, anchorYaw, 0f);
            float ground = Ground(anchorPos.x, anchorPos.z);
            foreach (var p in c.Pieces)
            {
                var prefab = TownKit.LoadByName(p.Name);
                if (prefab == null) continue;
                TownKit.Prop(prefab, anchorPos + rot * new Vector3(p.X, p.Y, p.Z), rot * p.Rot, _placeRoot, null, ground);
            }
        }

        // ------------------------------------------------------------ shops

        // The Town demo's three places of business, each on a lot of its own somewhere
        // in the suburb (never side by side): the gas station (canopy, pumps, the pickup
        // at the pump, the open store behind it), the hardware store on its concrete
        // apron, and the corner shop - each with every prop the pack's author stood
        // around it, on an asphalt forecourt with dropped kerbs, a car or two, a tree
        // line along the back and fences down the sides.
        void ComposeGasStation(Lot lot)
        {
            float W = lot.W;
            Forecourt(lot);
            // the demo's whole: canopy centred on the asphalt, the pumps and the pickup under
            // it, the pole sign at the kerb, and the store (Shop_01) 11 m behind the canopy
            // with its interior and its roof gear
            PlaceCluster(TownClusters.GasStation, lot, W * 0.5f + 1.3f, 6.5f, "Gas Station", false,
                new System.Collections.Generic.Dictionary<string, string> { { "SM_Bld_Shop_01", "Shop 0" } });
            lot.IdleSpots.Add(lot.P(W * 0.5f + 3.8f, 7.5f));
            var store = _placeRoot.Find("Shop 0");
            if (store != null)
            {
                // its glass doors are on the +Z face, 4.7 m from the pivot (read off the mesh)
                lot.DoorPos = store.position + store.rotation * new Vector3(0f, 0f, 4.7f);
                lot.DoorPos.y = 0f;
                lot.DoorOut = lot.Front;
                lot.HasDoor = true;
                if (debugFronts) DebugMarker(lot.DoorPos + Vector3.up * 0.5f, Color.red);
            }
            Customers(lot, new[] { W * 0.5f - 12f, W * 0.5f + 13f });
            BackOfPlace(lot);
        }

        void ComposeHardware(Lot lot)
        {
            float W = lot.W;
            Forecourt(lot);
            // an L, its glass doors in the notch on the +Z side (read off the mesh), the
            // wing's face 8 m behind the kerb, the concrete apron under and about it
            var hardware = PlaceCluster(TownClusters.Hardware, lot, W * 0.5f + 0.7f, 8f + 7.1f, "Shop 1", true);
            if (hardware != null)
            {
                // the doors sit 6 m into the notch, 4 m left of the centre line
                lot.DoorPos = hardware.transform.position + hardware.transform.rotation * new Vector3(-4f, 0f, 0.6f);
                lot.DoorPos.y = 0f;
                lot.DoorOut = lot.Front;
                lot.HasDoor = true;
                if (debugFronts) DebugMarker(lot.DoorPos + Vector3.up * 0.5f, Color.red);
                TownKit.ApplyPalette(hardware, PickPalette());
            }
            Customers(lot, new[] { W * 0.5f - 10f, W * 0.5f + 10f });
            BackOfPlace(lot);
        }

        void ComposeShop(Lot lot)
        {
            float W = lot.W;
            Forecourt(lot);
            var shop = PlaceCluster(TownClusters.Shop, lot, W * 0.5f - 0.4f, 11.5f + 5.5f, "Shop 2", true);
            if (shop != null)
            {
                lot.DoorPos = lot.P(W * 0.5f - 0.4f, 11.5f);
                lot.DoorOut = lot.Front;
                lot.HasDoor = true;
                TownKit.ApplyPalette(shop, PickPalette());
            }
            Customers(lot, new[] { W * 0.5f - 5f, W * 0.5f + 5.5f });
            // the bus stop out front, by the shop
            _busStops.Add((lot, W - 6f));
            BackOfPlace(lot);
        }

        /// <summary>Asphalt over the front two rows, the whole width, dropped kerbs at
        /// both ends where the cars turn in.</summary>
        void Forecourt(Lot lot)
        {
            int wc = lot.WidthCells;
            for (int i = 0; i < wc; i++)
                for (int j = 0; j < 2; j++) SetSurface(lot, i, j, Surface.Asphalt, 0);
            foreach (int i in new[] { 0, 1, wc - 2, wc - 1 }) SetFrontPavement(lot, i, SwVariant.Driveway);
        }

        /// <summary>Customers' cars nosed in on the forecourt at the given u's, where they clash with nothing.</summary>
        void Customers(Lot lot, float[] us)
        {
            if (_carPrefabs.Count == 0) return;
            foreach (float u in us)
            {
                if (!Chance(0.7f)) continue;
                var at = lot.P(u + Rnd(-0.5f, 0.5f), 5.5f);
                at.y = 0.05f;
                var car = Pick(_carPrefabs);
                float yaw = TownKit.YawToFace(TownKit.Side.PlusZ, lot.In) + Rnd(-4f, 5f);
                var fp = TownKit.Footprint(car, at, yaw);
                bool clash = false;
                foreach (var t in lot.Taken) if (Overlaps(fp, t, 0.3f)) clash = true;
                if (clash) continue;
                var go = TownKit.Prop(car, at, yaw, _placeRoot);
                TownKit.StripForStatic(go);
                lot.Taken.Add(fp);
            }
        }

        /// <summary>A tree line along the back, fences down the sides (where no street runs) and across the back.</summary>
        void BackOfPlace(Lot lot)
        {
            float W = lot.W, D = lot.D;
            var lotRect = Rect.MinMaxRect(lot.Cx0 * Cell, lot.Cz0 * Cell, lot.Cx1 * Cell, lot.Cz1 * Cell);
            for (float t = 3f; t < W - 2f; t += Rnd(5f, 8f))
                if (_tallTrees.Count > 0) TryTree(Pick(_tallTrees), lot, t, D - 2f + Rnd(-1f, 0.5f), lotRect);
            if (!lot.CornerLeft) LayFence(_fenceWood, _fenceWoodPost, lot, 0f, 12f, 0f, D, null);
            if (!lot.CornerRight) LayFence(_fenceWood, _fenceWoodPost, lot, W, 12f, W, D, null);
            LayFence(_fenceWood, _fenceWoodPost, lot, 0f, D, W, D, null);
        }

        // ------------------------------------------------------------ park

        void ComposePark(Lot lot)
        {
            float W = lot.W, D = lot.D;
            var lotRect = Rect.MinMaxRect(lot.Cx0 * Cell, lot.Cz0 * Cell, lot.Cx1 * Cell, lot.Cz1 * Cell);
            int wc = lot.WidthCells, dc = lot.DepthCells;
            int iPath = wc / 2, jPath = dc / 2;
            int frontYaw = TownKit.YawFacing(lot.Front);
            int sideYaw = TownKit.YawFacing(lot.Along);

            // a cross of paths, the long one from kerb to kerb (or to the back)
            for (int j = 0; j < dc; j++) SetSurface(lot, iPath, j, Surface.Path, frontYaw);
            for (int i = 0; i < wc; i++) if (i != iPath) SetSurface(lot, i, jPath, Surface.Path, sideYaw);
            SetFrontPavement(lot, iPath, SwVariant.Path);
            // the far end: if the park reaches the opposite pavement, drop that kerb too
            var far = lot.P(iPath * Cell + Cell * 0.5f, D + Cell * 0.5f);
            int fcx = CellOf(far.x), fcz = CellOf(far.z);
            if (InGrid(fcx, fcz) && _kind[fcx, fcz] == CellKind.Sidewalk && _swVariant[fcx, fcz] == SwVariant.Plain) _swVariant[fcx, fcz] = SwVariant.Path;
            lot.Taken.Add(CellRect(lot, iPath, 0, 1, dc, 1.2f));
            lot.Taken.Add(CellRect(lot, 0, jPath, wc, 1, 1.2f));

            // the fountain at the crossing
            float uMid = iPath * Cell + Cell * 0.5f, vMid = jPath * Cell + Cell * 0.5f;
            for (int i = iPath - 1; i <= iPath + 1; i++)
                for (int j = jPath - 1; j <= jPath + 1; j++) if (i >= 0 && j >= 0 && i < wc && j < dc) SetSurface(lot, i, j, Surface.Concrete, 0);
            if (_fountainBase != null) TownKit.Prop(_fountainBase, lot.P(uMid, vMid), 0, _placeRoot);
            if (_fountain != null) TownKit.Prop(_fountain, lot.P(uMid, vMid) + Vector3.up * 0.1f, 0, _placeRoot);
            lot.Taken.Add(Rect.MinMaxRect(lot.P(uMid, vMid).x - 2.5f, lot.P(uMid, vMid).z - 2.5f, lot.P(uMid, vMid).x + 2.5f, lot.P(uMid, vMid).z + 2.5f));

            // benches along the paths, lights at the corners of the crossing
            if (_bench != null)
            {
                foreach (var (u, v, dir) in new[]
                {
                    (uMid - 3.3f, vMid - 6f, lot.Along), (uMid + 3.3f, vMid + 6f, -lot.Along),
                    (uMid - 7f, vMid - 3.3f, lot.In), (uMid + 7f, vMid + 3.3f, lot.Front),
                    (uMid + 3.3f, 4f, -lot.Along), (uMid - 3.3f, D - 4f, lot.Along),
                })
                    TryPlace(_bench, lot, u, v, TownKit.YawToFace(TownKit.Side.PlusZ, dir), lotRect, 0.2f);
            }
            if (_outdoorLights.Count > 0)
                foreach (var (u, v) in new[] { (uMid - 3.2f, vMid - 3.2f), (uMid + 3.2f, vMid + 3.2f), (uMid - 3.2f, vMid + 3.2f), (uMid + 3.2f, vMid - 3.2f) })
                    TryPlace(_outdoorLights[0], lot, u, v, 0, lotRect, 0.05f);
            if (_bins.Count > 0) { TryPlace(Pick(_bins), lot, uMid + 3.6f, 1.5f, 0, lotRect, 0.05f); TryPlace(Pick(_bins), lot, uMid - 3.6f, vMid + 3.6f, 0, lotRect, 0.05f); }
            if (_bikeStand != null) TryPlace(_bikeStand, lot, uMid + 4.5f, 2.5f, TownKit.YawToFace(TownKit.Side.PlusX, lot.In), lotRect, 0.1f);

            // the playground in the front-left quarter
            float pu0 = 2f, pu1 = uMid - 4f, pv0 = 3f, pv1 = vMid - 4f;
            if (pu1 - pu0 > 8f && pv1 - pv0 > 8f)
            {
                TryPlace(_fort, lot, (pu0 + pu1) * 0.5f, (pv0 + pv1) * 0.5f, Rnd(4) * 90, lotRect, 0.6f);
                TryPlace(_slide, lot, pu0 + 3f, pv1 - 2.5f, Rnd(360), lotRect, 0.5f);
                TryPlace(_swing, lot, pu1 - 3f, pv0 + 2.5f, TownKit.YawToFace(TownKit.Side.PlusX, lot.Along), lotRect, 0.5f);
                TryPlace(_roundabout, lot, pu0 + 3f, pv0 + 3f, Rnd(360), lotRect, 0.5f);
                TryPlace(_sandpit, lot, pu1 - 3f, pv1 - 3f, Rnd(4) * 90, lotRect, 0.4f);
                TryPlace(_playTable, lot, (pu0 + pu1) * 0.5f, pv1 - 1f, Rnd(360), lotRect, 0.3f);
                lot.IdleSpots.Add(lot.P((pu0 + pu1) * 0.5f + 2.5f, (pv0 + pv1) * 0.5f));
            }
            // trees and bushes everywhere else - a park is mostly trees
            for (int k = 0; k < Mathf.RoundToInt(16f * treeDensity); k++)
                if (_tallTrees.Count > 0) TryTree(Pick(_tallTrees), lot, Rnd(2f, W - 2f), Rnd(2f, D - 2f), lotRect);
            for (int k = 0; k < Mathf.RoundToInt(10f * treeDensity); k++)
                if (_yardTrees.Count > 0) TryTree(Pick(_yardTrees), lot, Rnd(2f, W - 2f), Rnd(2f, D - 2f), lotRect);
            for (int k = 0; k < 10; k++)
                if (_bushes.Count > 0) TryPlace(Pick(_bushes), lot, Rnd(1.5f, W - 1.5f), Rnd(1.5f, D - 1.5f), Rnd(360), lotRect, 0.3f);
            for (int k = 0; k < 8; k++)
                if (_flowers.Count > 0) TryPlace(Pick(_flowers), lot, Rnd(1.5f, W - 1.5f), Rnd(1.5f, D - 1.5f), Rnd(360), lotRect, 0.2f);
            _busStops.Add((lot, W - 4f));
        }

        // ------------------------------------------------------------ bus stops

        // A sign at the kerb and a bench against the back of the pavement, on the
        // pavement cell in front of the given lot point.
        void BuildBusStops()
        {
            foreach (var (lot, u) in _busStops)
            {
                if (_busStopSign != null) TownKit.Prop(_busStopSign, lot.P(u, -Walk + 0.7f) + Vector3.up * WalkY, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front), _streetRoot);
                if (_bench != null) TownKit.Prop(_bench, lot.P(u + 2.2f, -1.0f) + Vector3.up * WalkY, TownKit.YawToFace(TownKit.Side.PlusZ, lot.Front), _streetRoot);
                if (_bins.Count > 0) TownKit.Prop(Pick(_bins), lot.P(u - 1.2f, -1.0f) + Vector3.up * WalkY, 0, _streetRoot);
            }
        }
    }
}
