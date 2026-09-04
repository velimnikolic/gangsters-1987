using System;
using System.Collections.Generic;
using System.Linq;

namespace RoadDemo
{
    /// <summary>
    /// Pure GAN-332 regressions shared by CoreSim and the editor command. Every mutation
    /// first proves a clean deterministic baseline and that its target verdict was absent.
    /// No asset, scene or Unity random state is touched.
    /// </summary>
    public static class ResidentialFacadeTests
    {
        public sealed class Result
        {
            public int Passed, Total, FaultsPassed, FaultsTotal, ContractsPassed, ContractsTotal;
            public ResidentialFacade.FaultKind[] Missing =
                Array.Empty<ResidentialFacade.FaultKind>();
            public string[] MissingContracts = Array.Empty<string>();
            public string Report = string.Empty;
            public bool Clean => Passed == Total && Missing.Length == 0 &&
                                 MissingContracts.Length == 0;
        }

        public static Result Run()
        {
            var caught = new HashSet<ResidentialFacade.FaultKind>();
            var failures = new List<string>();
            int faultTotal = 0, contractTotal = 0, contractPassed = 0;

            ResidentialFacade.Sheet Fresh(int seed = 917, int length = 7, int floors = 3) =>
                ResidentialFacade.Roll(seed, length, floors);

            void Mutation(string name, Func<ResidentialFacade.Sheet> factory,
                          Action<ResidentialFacade.Sheet> damage,
                          ResidentialFacade.FaultKind target)
            {
                faultTotal++;
                try
                {
                    var baseline = factory();
                    var before = ResidentialFacade.Judge(baseline);
                    if (before.Length != 0 || before.Any(f => f.Kind == target))
                    {
                        failures.Add(name + " baseline: " +
                            (before.FirstOrDefault()?.ToString() ?? "target already present"));
                        return;
                    }
                    var broken = factory();
                    damage(broken);
                    if (ResidentialFacade.Judge(broken).Any(f => f.Kind == target))
                        caught.Add(target);
                    else failures.Add(name + " did not raise " + target);
                }
                catch (Exception ex)
                {
                    failures.Add(name + " threw " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            void Contract(string name, bool passed, string detail = null)
            {
                contractTotal++;
                if (passed) contractPassed++;
                else failures.Add(name + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail));
            }

            Mutation("Hole", () => Fresh(), sheet =>
                sheet.Pieces = sheet.Pieces.Skip(1).ToArray(),
                ResidentialFacade.FaultKind.Hole);

            Mutation("Double", () => Fresh(), sheet =>
                sheet.Pieces = sheet.Pieces.Concat(new[] { sheet.Pieces[0] }).ToArray(),
                ResidentialFacade.FaultKind.Double);

            Mutation("CornerOff", () => Fresh(), sheet =>
            {
                int at = Array.FindIndex(sheet.Pieces, p => IsCorner(FindModule(p.Module)?.Kind));
                var p = sheet.Pieces[at];
                sheet.Pieces[at] = new ResidentialFacade.Piece(
                    p.Module, p.I, p.J, p.Floor, p.Yaw + 90);
            }, ResidentialFacade.FaultKind.CornerOff);

            Mutation("RoofPair", () => Fresh(), sheet =>
            {
                int at = Array.FindIndex(sheet.Pieces, p =>
                    FindModule(p.Module)?.Kind == ResidentialModuleKind.Roof);
                var p = sheet.Pieces[at];
                var module = FindModule(p.Module);
                var wrong = ResidentialModules.All.First(m =>
                    m.Kind == ResidentialModuleKind.Roof &&
                    m.RoofPairStyle != module.RoofPairStyle);
                sheet.Pieces[at] = new ResidentialFacade.Piece(
                    wrong.Name, p.I, p.J, p.Floor, p.Yaw);
            }, ResidentialFacade.FaultKind.RoofPair);

            Mutation("ShopAcrossCorner", () => Fresh(), sheet =>
            {
                var wide = ResidentialModules.All.First(m =>
                    m.Kind == ResidentialModuleKind.Shop && m.Cells > 1);
                sheet.Pieces = sheet.Pieces.Concat(new[]
                {
                    new ResidentialFacade.Piece(wide.Name, 0, 0, 0, 180),
                }).ToArray();
            }, ResidentialFacade.FaultKind.ShopAcrossCorner);

            Mutation("LoneGlass", () => Fresh(length: 7), sheet =>
            {
                var glass = ResidentialModules.Find("SM_Bld_Shop_05");
                var door = ResidentialModules.All.First(m =>
                    m.Kind == ResidentialModuleKind.ApartmentDoor);
                var pieces = sheet.Pieces.Where(p => p.Floor != 0 || p.J != 0).ToList();
                for (int i = 0; i < sheet.Length; i++)
                {
                    if (i == 0 || i == sheet.Length - 1)
                    {
                        pieces.Add(sheet.Pieces.First(p => p.Floor == 0 && p.J == 0 && p.I == i));
                        continue;
                    }
                    pieces.Add(new ResidentialFacade.Piece(
                        i == 3 ? glass.Name : door.Name, i, 0, 0, 180));
                }
                sheet.Pieces = pieces.ToArray();
            }, ResidentialFacade.FaultKind.LoneGlass);

            Mutation("NoDoor", () => Fresh(), sheet =>
            {
                int at = Array.FindIndex(sheet.Pieces, p =>
                    FindModule(p.Module)?.Kind == ResidentialModuleKind.ApartmentDoor);
                var p = sheet.Pieces[at];
                var shop = ResidentialModules.All.First(m =>
                    m.Kind == ResidentialModuleKind.Shop && m.Cells == 1 && m.DoorLeaves > 0);
                sheet.Pieces[at] = new ResidentialFacade.Piece(
                    shop.Name, p.I, p.J, p.Floor, p.Yaw);
            }, ResidentialFacade.FaultKind.NoDoor);

            Mutation("NoEscape", () => Fresh(), sheet =>
                sheet.Props = sheet.Props.Where(p =>
                    FindModule(p.Prefab)?.Kind != ResidentialModuleKind.FireEscape ||
                    p.Column / sheet.Length != 0).ToArray(),
                ResidentialFacade.FaultKind.NoEscape);

            Mutation("EscapeOrder", () => Fresh(), sheet =>
            {
                int at = Array.FindIndex(sheet.Props, p =>
                    FindModule(p.Prefab)?.Kind == ResidentialModuleKind.FireEscape);
                var prop = sheet.Props[at];
                int order = FindModule(prop.Prefab).EscapeOrder;
                var wrong = ResidentialModules.All.First(m =>
                    m.Kind == ResidentialModuleKind.FireEscape && m.EscapeOrder != order);
                sheet.Props[at] = new ResidentialFacade.Prop(
                    wrong.Path, prop.X, prop.Y, prop.Z, prop.Yaw, prop.Column);
            }, ResidentialFacade.FaultKind.EscapeOrder);

            Mutation("LineLooseNotReadyEvidence", () => Fresh(), sheet =>
            {
                var line = ResidentialModules.Find("SM_Prop_Washingline_01");
                sheet.Props = sheet.Props.Concat(new[]
                {
                    new ResidentialFacade.Prop(line.Path, 0f, ResidentialFacade.Storey,
                                               0f, 0f, 0),
                }).ToArray();
            }, ResidentialFacade.FaultKind.LineLoose);

            int clashSeed = -1, clashIndex = -1;
            for (int seed = 1; seed <= 80 && clashSeed < 0; seed++)
            {
                var probe = Fresh(seed, 9, 4);
                if (ResidentialFacade.Judge(probe).Length != 0) continue;
                for (int p = 0; p < probe.Props.Length; p++)
                {
                    string family = RuleFamily(probe.Props[p]);
                    if (family != "ShopCover") continue;
                    var duplicate = Fresh(seed, 9, 4);
                    duplicate.Props = duplicate.Props.Concat(new[] { duplicate.Props[p] }).ToArray();
                    if (!ResidentialFacade.Judge(duplicate).Any(f =>
                        f.Kind == ResidentialFacade.FaultKind.Clash)) continue;
                    clashSeed = seed; clashIndex = p; break;
                }
            }
            Mutation("ClashOrderIndependent", () => Fresh(clashSeed < 0 ? 1 : clashSeed, 9, 4),
                sheet => sheet.Props = sheet.Props.Concat(new[]
                    { sheet.Props[clashIndex < 0 ? 0 : clashIndex] }).Reverse().ToArray(),
                ResidentialFacade.FaultKind.Clash);

            Mutation("OutOfBoxFar", () => Fresh(), sheet =>
            {
                var module = ResidentialModules.All.First(m => m.Kind == ResidentialModuleKind.Decor);
                sheet.Props = sheet.Props.Concat(new[]
                {
                    new ResidentialFacade.Prop(module.Path, 10000f, 3f, 10000f, 0f, 0),
                }).ToArray();
            }, ResidentialFacade.FaultKind.OutOfBox);

            Mutation("EmptyRequiredCell", () => Fresh(), sheet =>
            {
                var p = sheet.Pieces[0];
                sheet.Pieces[0] = new ResidentialFacade.Piece(
                    "missing-forge-module", p.I, p.J, p.Floor, p.Yaw);
            }, ResidentialFacade.FaultKind.Empty);

            Mutation("UnitMismatch", () => Fresh(), sheet => sheet.Unit.Name += "-wrong",
                ResidentialFacade.FaultKind.UnitMismatch);

            var samples = new List<ResidentialFacade.Sheet>();
            for (int seed = 1; seed <= 12; seed++)
                for (int length = ResidentialFacade.MinLength;
                     length <= ResidentialFacade.MaxLength; length += 5)
                    for (int floors = ResidentialFacade.MinFloors;
                         floors <= ResidentialFacade.MaxFloors; floors++)
                        samples.Add(Fresh(seed, Math.Min(length, ResidentialFacade.MaxLength), floors));

            Contract("D1_MixedWeightedFamilies", MixedStyles(samples));
            Contract("D2_OneResidentialDoorPerLongRow", samples.All(DoorsRight));
            Contract("D3_FireEscapeMeasuredShare", samples.All(FireShareRight));
            Contract("D4_BrandedLargeSigns", samples.SelectMany(s => s.Props)
                .Any(p => RuleFamily(p) == "LargeSign"));
            Contract("D5_HeightAndRoofBand", HeightContract());
            Contract("D6_OneSyntheticUnit", samples.All(UnitContract));
            Contract("PrimaryFaceVsCornerSubfrontage", samples.All(FaceContract));
            Contract("FreshBaselinesClean", samples.All(s =>
                (s.Faults?.Length ?? 0) == 0));
            Contract("DeterministicRepeatFingerprint", samples.All(s =>
                SameRoll(s, ResidentialFacade.Roll(s.Seed, s.Length, s.Floors))) &&
                SameRoll(ResidentialFacade.Roll(new Random(7721), 8, 4),
                         ResidentialFacade.Roll(new Random(7721), 8, 4)));
            var sampleRates = samples.SelectMany(ResidentialFacade.AuditRates).ToArray();
            Contract("MeasuredFamilyRateRanges", sampleRates.All(rate => rate.Within));
            Contract("RawMeasuredDensityAndRoofAccessPolicy",
                     samples.All(RawRateDenominators));

            var familyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rate in sampleRates)
                familyCounts[rate.Family] = familyCounts.TryGetValue(rate.Family, out int n)
                    ? n + rate.Actual : rate.Actual;
            var absentFamilies = ResidentialFacade.RequiredDecorFamilies()
                .Where(f => !familyCounts.TryGetValue(f, out int n) || n == 0).ToArray();
            Contract("RequiredMeasuredFamiliesUsed", absentFamilies.Length == 0,
                     string.Join(", ", absentFamilies));
            Contract("NoInventedWashingLine", samples.SelectMany(s => s.Props).All(p =>
                !PrefabName(p.Prefab).StartsWith("SM_Prop_Washingline_",
                                                  StringComparison.OrdinalIgnoreCase)));
            Contract("AtomicObservedGroups", AtomicGroupContract(samples));
            Contract("LayerTyping", LayerTypingContract());
            Contract("NearEdgeRendererGeometry", GeometryContract());
            Contract("SupportedFacadeOverhangsPreserved",
                     SupportedOverhangContract(samples));
            Contract("ArbitraryYawBoundsInUnitAndJudge",
                     ArbitraryYawBoundsContract(samples));
            Contract("BillboardBaseSupportedByRoof",
                     BillboardSupportContract());
            Contract("FireEscapeFloorLattice",
                     FireEscapeFloorLatticeContract());
            Contract("RoofAccessSupportedByActualRoof",
                     RoofAccessSupportContract());
            Contract("DetachedPropGetsPhysicalSupportVerdict",
                     PhysicalSupportVerdictContract());
            var density = PropsPercentContracts();
            Contract("PropsPercentConstantsAndOverloadParity", density.OverloadParity);
            Contract("PropsPercentInvalidDoesNotAdvanceRandom", density.InvalidNoAdvance);
            Contract("PropsPercentDeterminismAndSignatures", density.Deterministic);
            Contract("PropsPercentStructureAndRequiredInvariant", density.RequiredInvariant);
            Contract("PropsPercentZeroRequiredOnly", density.ZeroRequiredOnly);
            Contract("PropsPercentDoubleDensity", density.DoubleDensity);
            Contract("PropsPercentJudgeValidation", density.JudgeValidation);
            Contract("PropsPercentCapacityClamp", density.CapacityClamp);

            var all = Enum.GetValues(typeof(ResidentialFacade.FaultKind))
                          .Cast<ResidentialFacade.FaultKind>().ToArray();
            var missing = all.Where(kind => !caught.Contains(kind)).ToArray();
            int faultPassed = all.Length - missing.Length;
            int passed = faultPassed + contractPassed;
            int total = all.Length + contractTotal;
            return new Result
            {
                Passed = passed, Total = total,
                FaultsPassed = faultPassed, FaultsTotal = all.Length,
                ContractsPassed = contractPassed, ContractsTotal = contractTotal,
                Missing = missing,
                MissingContracts = failures.ToArray(),
                Report = missing.Length == 0 && failures.Count == 0
                    ? $"{all.Length}/{all.Length} faults; {contractPassed}/{contractTotal} named contracts"
                    : $"{faultPassed}/{all.Length} faults; {contractPassed}/{contractTotal} contracts; " +
                      string.Join(" | ", failures.Take(8)),
            };
        }

        static bool MixedStyles(IEnumerable<ResidentialFacade.Sheet> sheets)
        {
            var counts = new int[3];
            int total = 0;
            foreach (var sheet in sheets)
            {
                var byColumn = new Dictionary<int, HashSet<int>>();
                foreach (var piece in sheet.Pieces)
                {
                    var module = FindModule(piece.Module);
                    if (module == null || module.Kind != ResidentialModuleKind.Apartment &&
                                          module.Kind != ResidentialModuleKind.ApartmentStack &&
                                          module.Kind != ResidentialModuleKind.ApartmentCorner) continue;
                    if (module.Style < 1 || module.Style > 3) return false;
                    int column = piece.J * sheet.Length + piece.I;
                    if (!byColumn.TryGetValue(column, out var styles))
                    {
                        styles = new HashSet<int>();
                        byColumn[column] = styles;
                    }
                    styles.Add(module.Style);
                }
                foreach (var styles in byColumn.Values)
                {
                    // The source census records one vote per vertical facade column;
                    // a three-storey Stack and three Singles are the same style sample.
                    if (styles.Count != 1) return false;
                    counts[styles.First() - 1]++;
                    total++;
                }
            }
            if (total == 0 || counts.Any(n => n == 0) ||
                ResidentialDecor.StyleWeights == null ||
                ResidentialDecor.StyleWeights.Length != 3 ||
                ResidentialDecor.StyleWeights.Any(w => w <= 0)) return false;
            int weightTotal = ResidentialDecor.StyleWeights.Sum();
            if (weightTotal <= 0) return false;
            for (int i = 0; i < counts.Length; i++)
            {
                double actual = (double)counts[i] / total;
                double measured = (double)ResidentialDecor.StyleWeights[i] / weightTotal;
                if (Math.Abs(actual - measured) > 0.08) return false;
            }
            return true;
        }

        static bool DoorsRight(ResidentialFacade.Sheet sheet)
        {
            for (int row = 0; row < ResidentialFacade.Depth; row++)
                if (sheet.Pieces.Count(p => p.Floor == 0 && p.J == row &&
                    FindModule(p.Module)?.Kind == ResidentialModuleKind.ApartmentDoor) != 1)
                    return false;
            return true;
        }

        static bool FireShareRight(ResidentialFacade.Sheet sheet)
        {
            int possible = sheet.Length - 2;
            int low = Math.Max(1, (int)Math.Ceiling(
                ResidentialDecor.FireEscapeShareMin * possible - 0.0001f));
            int high = Math.Max(low, Math.Min(possible, (int)Math.Floor(
                ResidentialDecor.FireEscapeShareMax * possible + 0.0001f)));
            for (int row = 0; row < ResidentialFacade.Depth; row++)
            {
                int count = sheet.Props.Count(p =>
                    FindModule(p.Prefab)?.EscapeOrder == 1 && p.Column / sheet.Length == row);
                if (count < low || count > high) return false;
            }
            return true;
        }

        static bool RawRateDenominators(ResidentialFacade.Sheet sheet)
        {
            int shops = 0, windows = 0, roofs = 0;
            foreach (var piece in sheet.Pieces)
            {
                var module = FindModule(piece.Module);
                if (module == null) continue;
                if (piece.Floor == 0 && (module.Kind == ResidentialModuleKind.Shop ||
                                         module.Kind == ResidentialModuleKind.ShopCorner))
                    shops += Math.Max(1, module.Cells);
                if (piece.Floor >= 1 && piece.Floor <= sheet.Floors &&
                    (module.Kind == ResidentialModuleKind.Apartment ||
                     module.Kind == ResidentialModuleKind.ApartmentStack ||
                     module.Kind == ResidentialModuleKind.ApartmentCorner))
                    windows += module.Kind == ResidentialModuleKind.ApartmentStack
                        ? Math.Max(1, module.Floors) : 1;
                if (piece.Floor == sheet.Floors + 1 &&
                    (module.Kind == ResidentialModuleKind.Roof ||
                     module.Kind == ResidentialModuleKind.RoofCorner)) roofs++;
            }
            foreach (var rate in ResidentialFacade.AuditRates(sheet))
            {
                int expected;
                if (rate.Relation == "FireEscapeChain") expected = sheet.Length - 2;
                else if (rate.Anchor == "Shop") expected = shops;
                else if (rate.Anchor == "WindowFloor" || rate.Anchor == "FireEscapeColumn" ||
                         rate.Anchor == "WashingLine") expected = windows;
                else if (rate.Anchor == "RoofCell" || rate.Anchor == "RoofAccess" ||
                         rate.Anchor == "Billboard" || rate.Anchor == "Terrace" ||
                         rate.Anchor == "VentChain") expected = roofs;
                else continue;
                if (rate.Eligible != expected) return false;
            }
            return true;
        }

        static bool HeightContract()
        {
            for (int n = ResidentialFacade.MinFloors; n <= ResidentialFacade.MaxFloors; n++)
            {
                var sheet = ResidentialFacade.Roll(331 + n, 8, n);
                int roofs = sheet.Pieces.Count(p =>
                {
                    var kind = FindModule(p.Module)?.Kind;
                    return kind == ResidentialModuleKind.Roof ||
                           kind == ResidentialModuleKind.RoofCorner;
                });
                if (roofs != sheet.Length * ResidentialFacade.Depth ||
                    sheet.Pieces.Any(p =>
                    {
                        var kind = FindModule(p.Module)?.Kind;
                        return (kind == ResidentialModuleKind.Roof ||
                                kind == ResidentialModuleKind.RoofCorner) && p.Floor != n + 1;
                    })) return false;
            }
            bool below = false, above = false;
            try { ResidentialFacade.Roll(1, 8, ResidentialFacade.MinFloors - 1); }
            catch (ArgumentOutOfRangeException) { below = true; }
            try { ResidentialFacade.Roll(1, 8, ResidentialFacade.MaxFloors + 1); }
            catch (ArgumentOutOfRangeException) { above = true; }
            return below && above;
        }

        static bool UnitContract(ResidentialFacade.Sheet sheet) =>
            sheet.Unit != null && sheet.Unit.Name == sheet.Signature &&
            sheet.Unit.CW == sheet.Length && sheet.Unit.CD == ResidentialFacade.Depth &&
            sheet.Unit.Kind == ResidentialKind.Through &&
            sheet.Unit.Pieces == sheet.Pieces.Length + sheet.Props.Length;

        static bool FaceContract(ResidentialFacade.Sheet sheet) =>
            sheet.Unit?.Face?.Length == 4 && sheet.Unit.Face[0] && sheet.Unit.Face[2] &&
            !sheet.Unit.Face[1] && !sheet.Unit.Face[3] &&
            sheet.Unit.ShopBays.Any(b => b.Side == 1) &&
            sheet.Unit.ShopBays.Any(b => b.Side == 3);

        static bool SameRoll(ResidentialFacade.Sheet a, ResidentialFacade.Sheet b)
        {
            if (a == null || b == null || a.Seed != b.Seed || a.Length != b.Length ||
                a.Floors != b.Floors || a.PropsPercent != b.PropsPercent ||
                a.Signature != b.Signature ||
                a.Pieces.Length != b.Pieces.Length || a.Props.Length != b.Props.Length ||
                a.Faults.Length != b.Faults.Length || a.Unit?.Name != b.Unit?.Name ||
                a.Unit?.Pieces != b.Unit?.Pieces) return false;
            for (int i = 0; i < a.Pieces.Length; i++)
                if (a.Pieces[i].Module != b.Pieces[i].Module ||
                    a.Pieces[i].I != b.Pieces[i].I || a.Pieces[i].J != b.Pieces[i].J ||
                    a.Pieces[i].Floor != b.Pieces[i].Floor || a.Pieces[i].Yaw != b.Pieces[i].Yaw)
                    return false;
            for (int i = 0; i < a.Props.Length; i++)
                if (a.Props[i].Prefab != b.Props[i].Prefab ||
                    a.Props[i].X != b.Props[i].X || a.Props[i].Y != b.Props[i].Y ||
                    a.Props[i].Z != b.Props[i].Z || a.Props[i].Yaw != b.Props[i].Yaw ||
                    a.Props[i].Column != b.Props[i].Column) return false;
            for (int i = 0; i < a.Faults.Length; i++)
                if (a.Faults[i].Kind != b.Faults[i].Kind ||
                    a.Faults[i].Column != b.Faults[i].Column ||
                    a.Faults[i].Floor != b.Faults[i].Floor ||
                    a.Faults[i].Detail != b.Faults[i].Detail) return false;
            return true;
        }

        sealed class PropsPercentCheckSet
        {
            public bool OverloadParity, InvalidNoAdvance, Deterministic,
                        RequiredInvariant, ZeroRequiredOnly, DoubleDensity,
                        JudgeValidation, CapacityClamp;
        }

        static PropsPercentCheckSet PropsPercentContracts()
        {
            var checks = new PropsPercentCheckSet();
            try
            {
                var oldSeed = ResidentialFacade.Roll(731, 8, 4);
                var exactSeed = ResidentialFacade.Roll(
                    731, 8, 4, ResidentialFacade.DefaultPropsPercent);
                var oldDice = new Random(991);
                var exactDice = new Random(991);
                var oldRandom = ResidentialFacade.Roll(oldDice, 8, 4);
                var exactRandom = ResidentialFacade.Roll(
                    exactDice, 8, 4, ResidentialFacade.DefaultPropsPercent);
                var golden = ResidentialFacade.Roll(7, 11, 4);
                checks.OverloadParity =
                    ResidentialFacade.MinPropsPercent == 0 &&
                    ResidentialFacade.DefaultPropsPercent == 100 &&
                    ResidentialFacade.MaxPropsPercent == 200 &&
                    SameRoll(oldSeed, exactSeed) && SameRoll(oldRandom, exactRandom) &&
                    oldDice.Next() == exactDice.Next() &&
                    oldSeed.Signature == "forge-L8-N4-731" &&
                    golden.Props.Length == 131;

                checks.InvalidNoAdvance = InvalidPercentLeavesRandom(-1) &&
                                                InvalidPercentLeavesRandom(201);
                checks.Deterministic = true;
                checks.RequiredInvariant = true;
                checks.ZeroRequiredOnly = true;
                checks.DoubleDensity = true;
                long optional100 = 0, optional200 = 0;
                int[] percents = { 0, 50, 100, 200 };
                int[] lengths = { 3, 8, 13 };
                for (int seed = 1; seed <= 12; seed++)
                {
                    int length = lengths[(seed - 1) % lengths.Length];
                    int floors = ResidentialFacade.MinFloors +
                                 (seed - 1) % (ResidentialFacade.MaxFloors -
                                              ResidentialFacade.MinFloors + 1);
                    var sheets = new ResidentialFacade.Sheet[percents.Length];
                    for (int p = 0; p < percents.Length; p++)
                    {
                        int percent = percents[p];
                        var sheet = ResidentialFacade.Roll(seed, length, floors, percent);
                        sheets[p] = sheet;
                        if (!SameRoll(sheet,
                            ResidentialFacade.Roll(seed, length, floors, percent)))
                            checks.Deterministic = false;
                        string signature = percent == ResidentialFacade.DefaultPropsPercent
                            ? $"forge-L{length}-N{floors}-{seed}"
                            : $"forge-L{length}-N{floors}-P{percent}-{seed}";
                        if (sheet.PropsPercent != percent || sheet.Signature != signature)
                            checks.Deterministic = false;
                    }

                    var required = RequiredProps(sheets[0]);
                    for (int p = 1; p < sheets.Length; p++)
                        if (!SamePieces(sheets[0], sheets[p]) ||
                            !SamePropMultiset(required, RequiredProps(sheets[p])))
                            checks.RequiredInvariant = false;

                    var zero = sheets[0];
                    var zeroRates = ResidentialFacade.AuditRates(zero);
                    int roofAccess = zero.Props.Count(prop =>
                        RuleFamily(prop) == "RoofAccess");
                    if (ResidentialFacade.Judge(zero).Length != 0 ||
                        zero.Props.Any(prop => !IsRequiredProp(prop)) ||
                        !zero.Props.Any(prop =>
                            FindModule(prop.Prefab)?.Kind ==
                            ResidentialModuleKind.FireEscape) ||
                        roofAccess < 1 || roofAccess > 2 ||
                        zeroRates.Any(rate => !rate.Within) ||
                        zeroRates.Any(rate => !IsRequiredFamily(rate.Family) &&
                                              rate.Actual != 0))
                        checks.ZeroRequiredOnly = false;

                    var rates100 = ResidentialFacade.AuditRates(sheets[2]);
                    var rates200 = ResidentialFacade.AuditRates(sheets[3]);
                    if (rates100.Any(rate => !rate.Within) ||
                        rates200.Any(rate => !rate.Within))
                        checks.DoubleDensity = false;
                    optional100 += rates100.Where(rate => !IsRequiredFamily(rate.Family))
                                            .Sum(rate => rate.Actual);
                    optional200 += rates200.Where(rate => !IsRequiredFamily(rate.Family))
                                            .Sum(rate => rate.Actual);
                }
                checks.DoubleDensity &= optional100 > 0 && optional200 >= optional100;

                var invalid = ResidentialFacade.Roll(33, 8, 4);
                invalid.PropsPercent = ResidentialFacade.MaxPropsPercent + 1;
                checks.JudgeValidation = ResidentialFacade.Judge(invalid).Any(fault =>
                    fault.Kind == ResidentialFacade.FaultKind.UnitMismatch &&
                    fault.Detail.Contains("invalid optional props"));

                var saturated = ResidentialFacade.AuditRates(
                    ResidentialFacade.Roll(1, 13, 3, 200)).Single(rate =>
                        rate.Family == "Aircon" && rate.Anchor == "WindowFloor" &&
                        rate.Relation == "Single");
                checks.CapacityClamp = saturated.Actual == 36 && saturated.Eligible == 78 &&
                    saturated.RequestedMinimum == 39 && saturated.RequestedMaximum == 78 &&
                    saturated.Capacity == 36 && saturated.Minimum == 36 &&
                    saturated.Maximum == 36 && saturated.Within &&
                    saturated.ToString().Contains("saturated");
            }
            catch
            {
                // Return the individual results collected so the named contract report
                // identifies this API surface without hiding it behind a thrown suite.
            }
            return checks;
        }

        static bool InvalidPercentLeavesRandom(int invalidPercent)
        {
            var tested = new Random(6103);
            var control = new Random(6103);
            bool rejected = false;
            try { ResidentialFacade.Roll(tested, 8, 4, invalidPercent); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            return rejected && tested.Next() == control.Next();
        }

        static bool SamePieces(ResidentialFacade.Sheet a, ResidentialFacade.Sheet b)
        {
            if (a?.Pieces == null || b?.Pieces == null ||
                a.Pieces.Length != b.Pieces.Length) return false;
            for (int i = 0; i < a.Pieces.Length; i++)
                if (a.Pieces[i].Module != b.Pieces[i].Module ||
                    a.Pieces[i].I != b.Pieces[i].I || a.Pieces[i].J != b.Pieces[i].J ||
                    a.Pieces[i].Floor != b.Pieces[i].Floor ||
                    a.Pieces[i].Yaw != b.Pieces[i].Yaw) return false;
            return true;
        }

        static ResidentialFacade.Prop[] RequiredProps(ResidentialFacade.Sheet sheet) =>
            (sheet?.Props ?? Array.Empty<ResidentialFacade.Prop>())
                .Where(IsRequiredProp).ToArray();

        static bool IsRequiredProp(ResidentialFacade.Prop prop) =>
            FindModule(prop.Prefab)?.Kind == ResidentialModuleKind.FireEscape ||
            RuleFamily(prop) == "RoofAccess";

        static bool IsRequiredFamily(string family) =>
            family == "FireEscape" || family == "RoofAccess";

        static bool SamePropMultiset(ResidentialFacade.Prop[] a, ResidentialFacade.Prop[] b)
        {
            if (a.Length != b.Length) return false;
            var unmatched = new List<ResidentialFacade.Prop>(b);
            for (int i = 0; i < a.Length; i++)
            {
                int at = unmatched.FindIndex(prop =>
                    prop.Prefab == a[i].Prefab && prop.X == a[i].X && prop.Y == a[i].Y &&
                    prop.Z == a[i].Z && prop.Yaw == a[i].Yaw &&
                    prop.Column == a[i].Column);
                if (at < 0) return false;
                unmatched.RemoveAt(at);
            }
            return unmatched.Count == 0;
        }

        static bool AtomicGroupContract(IEnumerable<ResidentialFacade.Sheet> sheets)
        {
            string[] families = { "Billboard", "Vent", "Terrace" };
            var required = new HashSet<string>(families.Where(family =>
                ResidentialDecor.All.Any(r => r.Ready && r.Family == family &&
                    r.Relation != ResidentialDecorRelation.Single)), StringComparer.Ordinal);
            var proved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sheet in sheets)
            {
                for (int p = 0; p < sheet.Props.Length; p++)
                {
                    var matching = families.Where(family => RuleHasFamily(sheet.Props[p], family))
                                           .Where(family => required.Contains(family) &&
                                                            !proved.Contains(family))
                                           .ToArray();
                    if (matching.Length == 0) continue;
                    var broken = ResidentialFacade.Roll(sheet.Seed, sheet.Length, sheet.Floors);
                    broken.Props = broken.Props.Where((_, i) => i != p).ToArray();
                    if (ResidentialFacade.Judge(broken).Any(f =>
                        f.Kind == ResidentialFacade.FaultKind.Empty))
                        for (int i = 0; i < matching.Length; i++) proved.Add(matching[i]);
                    if (proved.SetEquals(required)) return true;
                }
            }
            return proved.SetEquals(required);
        }

        static bool LayerTypingContract()
        {
            var sheet = ResidentialFacade.Roll(194, 7, 4);
            if (ResidentialFacade.Judge(sheet).Length != 0) return false;
            int at = Array.FindIndex(sheet.Pieces, p => p.Floor == 1);
            var old = sheet.Pieces[at];
            var shop = ResidentialModules.All.First(m =>
                m.Kind == ResidentialModuleKind.Shop && m.Cells == 1);
            sheet.Pieces[at] = new ResidentialFacade.Piece(
                shop.Name, old.I, old.J, old.Floor, old.Yaw);
            return ResidentialFacade.Judge(sheet).Any(f =>
                f.Kind == ResidentialFacade.FaultKind.Empty &&
                f.Detail.Contains("not valid on this layer"));
        }

        static bool GeometryContract()
        {
            var baseline = ResidentialFacade.Roll(884, 7, 4);
            if (ResidentialFacade.Judge(baseline).Length != 0) return false;
            var largest = ResidentialModules.All
                .Where(m => m.Kind == ResidentialModuleKind.Decor)
                .OrderByDescending(m => Math.Max(m.MaxX - m.MinX, m.MaxZ - m.MinZ))
                .First();
            for (int cells = 1; cells <= 2; cells++)
            {
                var broken = ResidentialFacade.Roll(884, 7, 4);
                broken.Props = broken.Props.Concat(new[]
                {
                    new ResidentialFacade.Prop(largest.Path,
                        -cells * ResidentialFacade.Cell, ResidentialFacade.Storey,
                        ResidentialFacade.Cell, 90f, 0),
                }).ToArray();
                if (ResidentialFacade.Judge(broken).Any(f =>
                    f.Kind == ResidentialFacade.FaultKind.OutOfBox &&
                    f.Detail.Contains("renderer bounds"))) return true;
            }
            return false;
        }

        static bool SupportedOverhangContract(IEnumerable<ResidentialFacade.Sheet> sheets)
        {
            bool escape = false, cover = false;
            foreach (var sheet in sheets)
            {
                if (ResidentialFacade.Judge(sheet).Length != 0) return false;
                foreach (var prop in sheet.Props)
                {
                    var module = FindModule(prop.Prefab);
                    if (module == null || module.Kind != ResidentialModuleKind.FireEscape &&
                                          module.Kind != ResidentialModuleKind.ShopCover)
                        continue;
                    ObjectBounds(module, prop.X, prop.Y, prop.Z, prop.Yaw,
                                 out float minX, out _, out float minZ,
                                 out float maxX, out _, out float maxZ);
                    bool crossesNominalLot = minX < -0.02f || minZ < -0.02f ||
                        maxX > sheet.Length * ResidentialFacade.Cell + 0.02f ||
                        maxZ > ResidentialFacade.Depth * ResidentialFacade.Cell + 0.02f;
                    if (!crossesNominalLot) continue;
                    if (module.Kind == ResidentialModuleKind.FireEscape) escape = true;
                    else cover = true;
                }
            }
            return escape && cover;
        }

        static bool ArbitraryYawBoundsContract(IEnumerable<ResidentialFacade.Sheet> sheets)
        {
            foreach (var sheet in sheets)
            {
                for (int p = 0; p < sheet.Props.Length; p++)
                {
                    var prop = sheet.Props[p];
                    double nearestQuarter = Math.Round(prop.Yaw / 90.0) * 90.0;
                    if (Math.Abs(prop.Yaw - nearestQuarter) < 0.01) continue;
                    if (!ExactSheetBounds(sheet, p, out float withoutMinX, out _,
                                          out float withoutMinZ, out float withoutMaxX,
                                          out _, out float withoutMaxZ) ||
                        !ExactSheetBounds(sheet, -1, out float minX, out float minY,
                                          out float minZ, out float maxX, out float maxY,
                                          out float maxZ)) continue;
                    bool ownsEdge = minX < withoutMinX - 0.001f ||
                                    minZ < withoutMinZ - 0.001f ||
                                    maxX > withoutMaxX + 0.001f ||
                                    maxZ > withoutMaxZ + 0.001f;
                    if (!ownsEdge) continue;
                    var unit = sheet.Unit;
                    if (unit?.Over == null || unit.Over.Length < 4 ||
                        !NearBound(unit.Over[0], Math.Max(0f, -minZ)) ||
                        !NearBound(unit.Over[1], Math.Max(0f,
                            maxX - unit.CW * ResidentialFacade.Cell)) ||
                        !NearBound(unit.Over[2], Math.Max(0f,
                            maxZ - unit.CD * ResidentialFacade.Cell)) ||
                        !NearBound(unit.Over[3], Math.Max(0f, -minX)) ||
                        !NearBound(unit.Floor, minY) || !NearBound(unit.MaxH, maxY))
                        return false;
                    return ResidentialFacade.Judge(sheet).Length == 0;
                }
            }
            return false;
        }

        static bool BillboardSupportContract()
        {
            int[] lengths = { 4, 8, 11, 13 };
            bool any = false, seed1988 = false;
            for (int at = 0; at < 12; at++)
            {
                int seed = 1987 + at;
                var sheet = ResidentialFacade.Roll(seed, lengths[at % lengths.Length], 3 + at / 4);
                var bases = sheet.Props.Where(prop =>
                    FindModule(prop.Prefab)?.Name.StartsWith(
                        "SM_Prop_Billboard_Roof_", StringComparison.Ordinal) == true).ToArray();
                if (seed == 1988) seed1988 = bases.Length > 0;
                for (int b = 0; b < bases.Length; b++)
                {
                    any = true;
                    if (!SupportedByActualRoof(sheet, bases[b])) return false;
                    bool paired = sheet.Props.Any(prop =>
                    {
                        var name = FindModule(prop.Prefab)?.Name;
                        return name != null && name.StartsWith(
                                   "SM_Prop_Billboard_Sign_", StringComparison.Ordinal) &&
                               prop.Column == bases[b].Column &&
                               NearBound(prop.X, bases[b].X) && NearBound(prop.Y, bases[b].Y) &&
                               NearBound(prop.Z, bases[b].Z) &&
                               NearAngle(prop.Yaw, bases[b].Yaw);
                    });
                    if (!paired) return false;
                }
            }
            return any && seed1988;
        }

        static bool FireEscapeFloorLatticeContract()
        {
            var sheet = ResidentialFacade.Roll(1987, 3, 4, 0);
            var chains = sheet.Props
                .Where(prop => FindModule(prop.Prefab)?.Kind ==
                               ResidentialModuleKind.FireEscape)
                .GroupBy(prop => prop.Column).ToArray();
            if (chains.Length != ResidentialFacade.Depth) return false;
            foreach (var chain in chains)
            {
                var props = chain.ToArray();
                if (props.Length != sheet.Floors) return false;
                for (int floor = 1; floor <= sheet.Floors; floor++)
                {
                    int order = floor == 1 ? 1 : floor == sheet.Floors ? 3 : 2;
                    if (props.Count(prop =>
                        FindModule(prop.Prefab)?.EscapeOrder == order &&
                        NearBound(prop.Y, floor * ResidentialFacade.Storey)) != 1)
                        return false;
                }
            }
            return ResidentialFacade.Judge(sheet).Length == 0;
        }

        static bool RoofAccessSupportContract()
        {
            var sheet = ResidentialFacade.Roll(1987, 5, 4, 0);
            var access = sheet.Props.Where(prop =>
                FindModule(prop.Prefab)?.Kind == ResidentialModuleKind.RoofAccess).ToArray();
            return access.Length >= 1 && access.Length <= 2 &&
                   access.All(prop => SupportedByActualRoofCentre(sheet, prop)) &&
                   ResidentialFacade.Judge(sheet).Length == 0;
        }

        static bool PhysicalSupportVerdictContract()
        {
            var sheet = ResidentialFacade.Roll(917, 7, 3, 0);
            var sign = ResidentialModules.Find("SM_Prop_Sign_Parking_01");
            if (sign == null || ResidentialFacade.Judge(sheet).Length != 0) return false;
            sheet.Props = sheet.Props.Concat(new[]
            {
                new ResidentialFacade.Prop(sign.Path, -3.75f, 0.25f, 5f, 0f, 0),
            }).ToArray();
            return ResidentialFacade.Judge(sheet).Any(fault =>
                fault.Kind == ResidentialFacade.FaultKind.OutOfBox &&
                fault.Detail.Contains("physical support"));
        }

        static bool SupportedByActualRoofCentre(ResidentialFacade.Sheet sheet,
                                                ResidentialFacade.Prop footing)
        {
            var module = FindModule(footing.Prefab);
            if (module == null) return false;
            ObjectBounds(module, footing.X, footing.Y, footing.Z, footing.Yaw,
                         out float minX, out float minY, out float minZ,
                         out float maxX, out _, out float maxZ);
            float supportX = (minX + maxX) * 0.5f;
            float supportZ = (minZ + maxZ) * 0.5f;
            const float tolerance = 0.02f;
            foreach (var piece in sheet.Pieces)
            {
                var roof = FindModule(piece.Module);
                if (roof == null || roof.Kind != ResidentialModuleKind.Roof &&
                                    roof.Kind != ResidentialModuleKind.RoofCorner)
                    continue;
                ResidentialFacade.Pivot(piece, roof, out float x, out float z);
                ObjectBounds(roof, x, piece.Floor * ResidentialFacade.Storey,
                             z, piece.Yaw, out float roofMinX,
                             out float roofMinY, out float roofMinZ,
                             out float roofMaxX, out float roofMaxY,
                             out float roofMaxZ);
                if (supportX >= roofMinX - tolerance &&
                    supportX <= roofMaxX + tolerance &&
                    supportZ >= roofMinZ - tolerance &&
                    supportZ <= roofMaxZ + tolerance &&
                    minY >= roofMinY - tolerance && minY <= roofMaxY + tolerance)
                    return true;
            }
            return false;
        }

        static bool SupportedByActualRoof(ResidentialFacade.Sheet sheet,
                                          ResidentialFacade.Prop footing)
        {
            var module = FindModule(footing.Prefab);
            if (module == null) return false;
            float supportY = footing.Y + module.MinY;
            float[] localX = { module.MinX, module.MaxX };
            float[] localZ = { module.MinZ, module.MaxZ };
            const float tolerance = 0.02f;
            for (int ix = 0; ix < localX.Length; ix++)
                for (int iz = 0; iz < localZ.Length; iz++)
                {
                    ExactPoint(localX[ix], localZ[iz], footing.Yaw,
                               out float dx, out float dz);
                    float supportX = footing.X + dx;
                    float supportZ = footing.Z + dz;
                    bool supported = false;
                    foreach (var piece in sheet.Pieces)
                    {
                        var roof = FindModule(piece.Module);
                        if (roof == null || roof.Kind != ResidentialModuleKind.Roof &&
                                            roof.Kind != ResidentialModuleKind.RoofCorner)
                            continue;
                        ResidentialFacade.Pivot(piece, roof, out float x, out float z);
                        ObjectBounds(roof, x, piece.Floor * ResidentialFacade.Storey,
                                     z, piece.Yaw, out float roofMinX,
                                     out float roofMinY, out float roofMinZ,
                                     out float roofMaxX, out float roofMaxY,
                                     out float roofMaxZ);
                        if (supportX >= roofMinX - tolerance &&
                            supportX <= roofMaxX + tolerance &&
                            supportZ >= roofMinZ - tolerance &&
                            supportZ <= roofMaxZ + tolerance &&
                            supportY >= roofMinY - tolerance &&
                            supportY <= roofMaxY + tolerance)
                        {
                            supported = true;
                            break;
                        }
                    }
                    if (!supported) return false;
                }
            return true;
        }

        static void ExactPoint(float x, float z, float yaw,
                               out float turnedX, out float turnedZ)
        {
            double radians = yaw * (Math.PI / 180.0);
            double cosine = Math.Cos(radians), sine = Math.Sin(radians);
            turnedX = (float)(x * cosine + z * sine);
            turnedZ = (float)(-x * sine + z * cosine);
        }

        static void ObjectBounds(ResidentialModule module, float x, float y, float z, float yaw,
                                 out float minX, out float minY, out float minZ,
                                 out float maxX, out float maxY, out float maxZ)
        {
            minX = minY = minZ = float.MaxValue;
            maxX = maxY = maxZ = float.MinValue;
            ExactBounds(module, x, y, z, yaw,
                        ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
        }

        static bool ExactSheetBounds(ResidentialFacade.Sheet sheet, int omitProp,
                                     out float minX, out float minY, out float minZ,
                                     out float maxX, out float maxY, out float maxZ)
        {
            minX = minY = minZ = 0f;
            maxX = sheet.Length * ResidentialFacade.Cell;
            maxY = 0f;
            maxZ = ResidentialFacade.Depth * ResidentialFacade.Cell;
            foreach (var piece in sheet.Pieces)
            {
                var module = FindModule(piece.Module);
                if (module == null) return false;
                ResidentialFacade.Pivot(piece, module, out float x, out float z);
                ExactBounds(module, x, piece.Floor * ResidentialFacade.Storey, z, piece.Yaw,
                            ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            }
            for (int p = 0; p < sheet.Props.Length; p++)
            {
                if (p == omitProp) continue;
                var prop = sheet.Props[p];
                var module = FindModule(prop.Prefab);
                if (module == null) return false;
                ExactBounds(module, prop.X, prop.Y, prop.Z, prop.Yaw,
                            ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            }
            return true;
        }

        static void ExactBounds(ResidentialModule module, float x, float y, float z, float yaw,
                                ref float minX, ref float minY, ref float minZ,
                                ref float maxX, ref float maxY, ref float maxZ)
        {
            double radians = yaw * (Math.PI / 180.0);
            double cosine = Math.Cos(radians), sine = Math.Sin(radians);
            float[] xs = { module.MinX, module.MaxX };
            float[] zs = { module.MinZ, module.MaxZ };
            for (int ix = 0; ix < xs.Length; ix++)
                for (int iz = 0; iz < zs.Length; iz++)
                {
                    float px = (float)(x + xs[ix] * cosine + zs[iz] * sine);
                    float pz = (float)(z - xs[ix] * sine + zs[iz] * cosine);
                    minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                    minZ = Math.Min(minZ, pz); maxZ = Math.Max(maxZ, pz);
                }
            minY = Math.Min(minY, y + module.MinY);
            maxY = Math.Max(maxY, y + module.MaxY);
        }

        static bool NearBound(float a, float b) => Math.Abs(a - b) <= 0.002f;

        static bool NearAngle(float a, float b)
        {
            float delta = Math.Abs((a - b) % 360f);
            return Math.Min(delta, 360f - delta) <= 0.002f;
        }

        static string RuleFamily(ResidentialFacade.Prop prop)
        {
            var module = FindModule(prop.Prefab);
            if (module == null) return string.Empty;
            var rule = ResidentialDecor.All.FirstOrDefault(r =>
                FindModule(r.Prefab)?.Name == module.Name);
            return rule?.Family ?? string.Empty;
        }

        static bool RuleHasFamily(ResidentialFacade.Prop prop, string family)
        {
            var module = FindModule(prop.Prefab);
            return module != null && ResidentialDecor.All.Any(r =>
                r.Family == family && FindModule(r.Prefab)?.Name == module.Name);
        }

        static ResidentialModule FindModule(string prefab)
        {
            var direct = ResidentialModules.Find(prefab);
            if (direct != null) return direct;
            if (string.IsNullOrEmpty(prefab)) return null;
            return ResidentialModules.Find(PrefabName(prefab));
        }

        static string PrefabName(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return string.Empty;
            int slash = Math.Max(prefab.LastIndexOf('/'), prefab.LastIndexOf('\\'));
            string name = slash >= 0 ? prefab.Substring(slash + 1) : prefab;
            return name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 7) : name;
        }

        static bool IsCorner(ResidentialModuleKind? kind) =>
            kind == ResidentialModuleKind.ShopCorner ||
            kind == ResidentialModuleKind.ApartmentCorner ||
            kind == ResidentialModuleKind.RoofCorner;
    }
}
