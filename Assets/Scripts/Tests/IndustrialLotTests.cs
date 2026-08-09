using System.Collections.Generic;
using UnityEngine;
using LivingCity.Entities;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// Properties of a works YARD partition - what IndustrialLotPlanner cuts out of the ground
    /// IndustrialLayout left over.
    ///
    /// Same discipline as <see cref="IndustrialLayoutTests"/>: a plain static class holding no
    /// UnityEngine.Object, returning failures as data rather than logging them, so it runs in a
    /// bare .NET host against the built Assembly-CSharp.dll with no Editor and no Play mode.
    ///
    /// The two load-bearing assertions are the overlap ones. A zone that strays onto a
    /// carriageway is not a cosmetic defect: the carriageways are the compound's only circulation
    /// lanes, and props written onto one would sever the route from the gate to the hall doors
    /// that police pathing depends on. There is no NavMesh to catch it later, so it is caught
    /// here.
    /// </summary>
    public static class IndustrialLotTests
    {
        /// <summary>The sizes BlockRect actually produces: one cell 46m, two 76m, three 106m.</summary>
        static readonly float[] BlockSizes = { 46f, 76f, 106f };

        /// <summary>
        /// The works catalogue, width x depth in metres. Copied from IndustrialLayoutTests for
        /// the reason given there: the database is a ScriptableObject and cannot be loaded
        /// outside Unity, and these are properties of the art rather than of the config.
        /// </summary>
        static readonly (string Name, float Width, float Depth)[] Works =
        {
            ("industry-factory",      24.71f, 16.60f),
            ("industry-factory-old",  21.55f, 22.75f),
            ("industry-factory-hall", 12.78f, 20.08f),
            ("industry-warehouse",    17.55f, 17.48f),
            ("industry-storage",       6.00f, 16.35f),
            ("industry-refinery",     22.00f, 15.00f),
            ("industry-building",     16.41f, 12.35f),
        };

        /// <summary>Must match IndustrialDresser's own constants - see Halls.</summary>
        const float HallClearance = 2f;
        const float RoadSetback = 1.5f;

        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            ZonesNeverOverlapALane(failures);
            ZonesNeverOverlapAHall(failures);
            ZonesNeverOverlapEachOther(failures);
            ZonesStayInsideTheWall(failures);
            EveryApronFacesItsHallDoor(failures);
            AHallWithADeepForecourtGetsAnApron(failures);
            CoverageStaysUnderTheCap(failures);
            ARoomyYardIsActuallyZoned(failures);
            SmallBlocksDegradeRatherThanCrowd(failures);
            SameSeedSameZones(failures);

            PropsStayInsideTheirZone(failures);
            PropsNeverStandOnClearGround(failures);
            PropsNeverOverlapEachOther(failures);
            PropsAlignToTheirZone(failures);
            TheLotBudgetIsNeverExceeded(failures);
            AZonedYardActuallyGetsProps(failures);
            SameSeedSameProps(failures);

            return failures;
        }

        // ---------------------------------------------------------------- yard stock

        /// <summary>
        /// Footprints the packer is swept against, in metres.
        ///
        /// Deliberately NOT the measured art, which is the one place this differs from the Works
        /// catalogue above. The storage pack is optional and its Epic City stand-ins are not fixed
        /// either, so pinning these to whatever is on disk today would make the suite fail on a
        /// palette edit rather than on a packing bug. What is being asserted is a property of the
        /// PACKER - that it holds for any size it is handed - and a sweep proves that where one
        /// measured tuple only samples it.
        ///
        /// The range is chosen to bracket the real stock: an ash can at the bottom, a shelving
        /// rack at the top, and one deliberately long thin piece, because a square footprint hides
        /// every axis-mapping bug there is.
        /// </summary>
        static readonly Vector2[] YardStock =
        {
            new Vector2(0.55f, 0.55f),   // barrel / ash can
            new Vector2(1.20f, 0.80f),   // crate
            new Vector2(2.10f, 0.60f),   // shelving rack - long and thin on purpose
            new Vector2(0.90f, 1.60f),   // the same thing across the other axis
        };

        /// <summary>Must match IndustrialLotConfig's shipped default - see IndustrialYardDresser.</summary>
        const int LotBudget = 120;

        /// <summary>
        /// How many yards the prop sweep visits. AllYards() runs to about seventeen hundred, and
        /// packing each of them against four footprints is a different order of work from planning
        /// them once. A hundred covers every block size, every side mask and several seeds, which
        /// is what the properties below actually vary over.
        /// </summary>
        const int PropSweep = 100;

        /// <summary>
        /// The zone's footprint at its own yaw, which is what IndustrialYardProps.Accept uses and
        /// therefore what the assertions have to measure against. Slots carry no size of their
        /// own - one prefab per zone is the arrangement rule, so the size is a property of the
        /// zone rather than of the slot.
        /// </summary>
        static IndustrialLayout.Rect Claim(LotZone zone, Vector2 item, Vector2 centre)
        {
            var yaw = IndustrialYardProps.YawFor(zone);
            var quarter = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 1f
                       || Mathf.Abs(Mathf.DeltaAngle(yaw, -90f)) < 1f;

            var half = (quarter ? new Vector2(item.y, item.x) : item) * 0.5f;
            return new IndustrialLayout.Rect { Min = centre - half, Max = centre + half };
        }

        /// <summary>
        /// Walks the sweep, handing back everything one Plan call needs. The footprint is passed
        /// through PrefabBounds' quarter-turn convention here rather than in each test, so a test
        /// that forgets it cannot pass by accident.
        /// </summary>
        static IEnumerable<(LotZone Zone, IndustrialYardProps.Site Site, Vector2 Item,
                            int Quota, List<IndustrialYardProps.Slot> Slots, int Seed, int Block)>
            AllStock()
        {
            var visited = 0;

            foreach (var (layout, halls, seed, block) in AllYards())
            {
                if (++visited > PropSweep)
                    yield break;

                var zones = Plan(layout, halls, seed, block).ToArray();
                var quotas = IndustrialYardProps.Budgets(zones, LotBudget);
                var site = SiteFor(layout, halls);

                for (var i = 0; i < zones.Length; i++)
                {
                    var item = YardStock[(seed + block + i) % YardStock.Length];
                    var yaw = IndustrialYardProps.YawFor(zones[i]);

                    var quarter = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 1f
                               || Mathf.Abs(Mathf.DeltaAngle(yaw, -90f)) < 1f;

                    var footprint = quarter ? new Vector2(item.y, item.x) : item;

                    var rng = new System.Random(seed * 7919 + block * 397 + i);
                    var slots = IndustrialYardProps.Plan(
                        zones[i], site, footprint, quotas[i], rng);

                    yield return (zones[i], site, item, quotas[i], slots, seed, block);
                }
            }
        }

        /// <summary>
        /// The keep-outs a real yard hands the packer. Obstacles are synthesised rather than left
        /// empty: they are the whole reason WorksYard.Obstacles was added, so a test that never
        /// supplies one would assert nothing about the field it exists to prove.
        /// </summary>
        static IndustrialYardProps.Site SiteFor(
            IndustrialLayout.Layout layout, WorksHall[] halls)
        {
            var obstacles = new List<IndustrialLayout.Rect>();

            // Stands in for IndustrialDresser's stacks: a 4x2 box off one flank of each hall, at
            // roughly the offset BuildStacks uses.
            foreach (var hall in halls)
            {
                var lateral = Vector3.Cross(Vector3.up, hall.Outward);
                var centre = hall.Centre + lateral * (hall.HalfWidth + 1.6f);

                obstacles.Add(new IndustrialLayout.Rect
                {
                    Min = new Vector2(centre.x - 2f, centre.z - 1f),
                    Max = new Vector2(centre.x + 2f, centre.z + 1f),
                });
            }

            return new IndustrialYardProps.Site
            {
                Lanes = IndustrialLotPlanner.Lanes(layout),
                Bays = IndustrialLotPlanner.Bays(layout),
                Obstacles = obstacles.ToArray(),
                Gate = layout.HasGate
                    ? new Vector2(layout.GateCentre.x, layout.GateCentre.z)
                    : layout.Wall.Centre,
            };
        }

        /// <summary>
        /// Nothing may hang over the edge of the zone it was planned for.
        ///
        /// The load-bearing one, and the reason is transitive rather than direct: combined with
        /// ZonesNeverOverlapALane and ZonesNeverOverlapAHall, which already pass, this proves that
        /// no prop ever reaches a carriageway or stands inside a wall. That is the property this
        /// file's own header calls the thing there is no NavMesh to catch later - and the props
        /// are the first thing in the compound numerous enough that finding it by eye would be
        /// hopeless.
        /// </summary>
        static void PropsStayInsideTheirZone(List<string> failures)
        {
            foreach (var (zone, _, item, _, slots, seed, block) in AllStock())
            foreach (var slot in slots)
            {
                var claim = Claim(zone, item, slot.Centre);

                if (claim.Min.x >= zone.Area.Min.x - 0.001f &&
                    claim.Min.y >= zone.Area.Min.y - 0.001f &&
                    claim.Max.x <= zone.Area.Max.x + 0.001f &&
                    claim.Max.y <= zone.Area.Max.y + 0.001f)
                    continue;

                failures.Add($"seed {seed} block {block}: a {zone.Kind} prop at {slot.Centre} " +
                             $"reaches {claim.Min}..{claim.Max}, outside its zone " +
                             $"{zone.Area.Min}..{zone.Area.Max}");
                return;
            }
        }

        /// <summary>
        /// Nor on a lane, a vehicle bay, or anything IndustrialDresser already stood there.
        ///
        /// The lane half is belt and braces over the zone check above. The obstacle half is not:
        /// zones are planned BEFORE the stacks and the back-yard pieces are placed, so a
        /// stockpile is free to have been cut over ground a site hut later took, and only this
        /// list knows.
        /// </summary>
        static void PropsNeverStandOnClearGround(List<string> failures)
        {
            foreach (var (zone, site, item, _, slots, seed, block) in AllStock())
            foreach (var slot in slots)
            {
                var claim = Claim(zone, item, slot.Centre);

                if (Hits(claim, site.Lanes, out var what, "lane") ||
                    Hits(claim, site.Bays, out what, "bay") ||
                    Hits(claim, site.Obstacles, out what, "obstacle"))
                {
                    failures.Add($"seed {seed} block {block}: a {zone.Kind} prop at " +
                                 $"{slot.Centre} stands on a {what}");
                    return;
                }
            }
        }

        static bool Hits(
            IndustrialLayout.Rect claim, IndustrialLayout.Rect[] against,
            out string what, string label)
        {
            what = label;

            if (against == null)
                return false;

            foreach (var other in against)
                if (Overlaps(claim, other))
                    return true;

            return false;
        }

        static void PropsNeverOverlapEachOther(List<string> failures)
        {
            foreach (var (zone, _, item, _, slots, seed, block) in AllStock())
            {
                for (var i = 0; i < slots.Count; i++)
                for (var j = i + 1; j < slots.Count; j++)
                {
                    if (!Overlaps(Claim(zone, item, slots[i].Centre),
                                  Claim(zone, item, slots[j].Centre)))
                        continue;

                    failures.Add($"seed {seed} block {block}: two {zone.Kind} props overlap at " +
                                 $"{slots[i].Centre} and {slots[j].Centre}");
                    return;
                }
            }
        }

        /// <summary>
        /// Everything is square to its zone's facing, and only the scrap corner is allowed to be
        /// near it rather than on it.
        ///
        /// The planner's own words, at the field this reads: "Props align to this - a works stacks
        /// things square to something, never at 17 degrees." A packer that quietly started
        /// rounding yaw differently would look fine in a screenshot of one yard and wrong across
        /// a city, which is exactly the class of thing worth asserting instead of eyeballing.
        /// </summary>
        static void PropsAlignToTheirZone(List<string> failures)
        {
            foreach (var (zone, _, _, _, slots, seed, block) in AllStock())
            {
                var square = IndustrialYardProps.YawFor(zone);
                var allowed = zone.Kind == LotZoneKind.ScrapCorner ? 15.001f : 0.001f;

                foreach (var slot in slots)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(slot.Yaw, square)) <= allowed)
                        continue;

                    failures.Add($"seed {seed} block {block}: a {zone.Kind} prop sits at " +
                                 $"{slot.Yaw:F1} deg against a facing of {square:F1}");
                    return;
                }
            }
        }

        /// <summary>
        /// A yard never places more than IndustrialLotConfig.maxPropsPerLot.
        ///
        /// Asserted per yard rather than per zone, because the ceiling is a lot budget and the
        /// interesting failure is arithmetic in Budgets rather than in one Plan call. Floors
        /// rather than rounds is what makes this hold, and the sum of the floors is what would
        /// break first if that ever changed.
        /// </summary>
        static void TheLotBudgetIsNeverExceeded(List<string> failures)
        {
            var visited = 0;

            foreach (var (layout, halls, seed, block) in AllYards())
            {
                if (++visited > PropSweep)
                    return;

                var zones = Plan(layout, halls, seed, block).ToArray();
                var quotas = IndustrialYardProps.Budgets(zones, LotBudget);
                var site = SiteFor(layout, halls);

                var total = 0;

                for (var i = 0; i < zones.Length; i++)
                {
                    var rng = new System.Random(seed * 7919 + block * 397 + i);
                    var slots = IndustrialYardProps.Plan(
                        zones[i], site, YardStock[i % YardStock.Length], quotas[i], rng);

                    if (slots.Count > quotas[i])
                    {
                        failures.Add($"seed {seed} block {block}: {zones[i].Kind} placed " +
                                     $"{slots.Count} props against a quota of {quotas[i]}");
                        return;
                    }

                    total += slots.Count;
                }

                if (total <= LotBudget)
                    continue;

                failures.Add($"seed {seed} block {block}: yard placed {total} props against a " +
                             $"ceiling of {LotBudget}");
                return;
            }
        }

        /// <summary>
        /// The floor, and the most important of these.
        ///
        /// Every other prop assertion is a ceiling - stay inside, do not overlap, do not exceed -
        /// and a packer that returned an empty list would satisfy all of them. This file already
        /// made that argument once, at ARoomyYardIsActuallyZoned, about a planner that very nearly
        /// did emit almost nothing.
        ///
        /// Deliberately weak in what it demands: SOME zone of a well-zoned yard gets SOME props.
        /// A tighter floor would be a statement about how full a works ought to look, and that is
        /// a tuning question the config owns, not a property of the packer.
        /// </summary>
        static void AZonedYardActuallyGetsProps(List<string> failures)
        {
            // A RATE, per zone kind, over every footprint in the sweep - and all three of those
            // words were bought with a silent test.
            //
            // Per KIND, because the first version aggregated over a yard and passed while truck
            // staging and every boiler house in the city came out completely empty. Loading
            // aprons appear in nearly every yard and carried the total on their own.
            //
            // A RATE rather than "more than nothing", because the second version asked only
            // whether a kind ever placed anything, and a bug that empties a kind for two
            // footprints out of four leaves the other two to satisfy it. Both defects this now
            // guards were re-introduced as mutants and BOTH passed the "more than nothing"
            // form - a band coming out exactly one item deep, so a float's last bit decided
            // whether the row survived, and a budget whose flooring wiped out the smallest zone.
            //
            // Over every FOOTPRINT, because that is the axis the first of those two bugs varies
            // along. One size per zone samples it; the cross product exercises it.
            var placedIn = new Dictionary<LotZoneKind, int>();
            var attempts = new Dictionary<LotZoneKind, int>();
            var visited = 0;

            foreach (var (layout, halls, seed, block) in AllYards())
            {
                if (++visited > PropSweep)
                    break;

                var zones = Plan(layout, halls, seed, block).ToArray();
                var quotas = IndustrialYardProps.Budgets(zones, LotBudget);
                var site = SiteFor(layout, halls);

                for (var i = 0; i < zones.Length; i++)
                foreach (var item in YardStock)
                {
                    var yaw = IndustrialYardProps.YawFor(zones[i]);
                    var quarter = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 1f
                               || Mathf.Abs(Mathf.DeltaAngle(yaw, -90f)) < 1f;

                    var rng = new System.Random(seed * 7919 + block * 397 + i);
                    var placed = IndustrialYardProps.Plan(
                        zones[i], site, quarter ? new Vector2(item.y, item.x) : item,
                        quotas[i], rng).Count;

                    attempts.TryGetValue(zones[i].Kind, out var a);
                    attempts[zones[i].Kind] = a + 1;

                    if (placed <= 0)
                        continue;

                    placedIn.TryGetValue(zones[i].Kind, out var p);
                    placedIn[zones[i].Kind] = p + 1;
                }
            }

            // Enough samples that a rate is a statement about the packer rather than about the
            // sweep happening to miss a shape.
            const int Enough = 40;

            foreach (var kind in attempts.Keys)
            {
                if (attempts[kind] < Enough)
                    continue;

                placedIn.TryGetValue(kind, out var filled);
                var rate = filled / (float)attempts[kind];

                // Two tiers, because a cinder yard is MEANT to be nearly empty - Density 0.15,
                // the lowest the planner emits - and holding it to the same floor as a loading
                // apron would be asserting a look the config owns rather than a property of the
                // packer. Measured healthy rates are 33% for cinder and 91-100% for the rest;
                // the two mutants above sit at 6% and 25-50%, so both tiers clear their failure
                // by a wide margin in both directions.
                var floor = kind == LotZoneKind.CinderYard ? 0.20f : 0.80f;

                if (rate >= floor)
                    continue;

                failures.Add($"{kind} placed props in only {filled} of {attempts[kind]} " +
                             $"zone/footprint pairs ({rate:P0}, under the {floor:P0} floor) - " +
                             $"the arrangement for that kind is rejecting nearly everything");
                return;
            }
        }

        static void SameSeedSameProps(List<string> failures)
        {
            var visited = 0;

            foreach (var (layout, halls, seed, block) in AllYards())
            {
                if (++visited > 20)
                    return;

                var zones = Plan(layout, halls, seed, block).ToArray();
                var quotas = IndustrialYardProps.Budgets(zones, LotBudget);
                var site = SiteFor(layout, halls);

                for (var i = 0; i < zones.Length; i++)
                {
                    var first = IndustrialYardProps.Plan(
                        zones[i], site, YardStock[i % YardStock.Length], quotas[i],
                        new System.Random(seed));

                    var second = IndustrialYardProps.Plan(
                        zones[i], site, YardStock[i % YardStock.Length], quotas[i],
                        new System.Random(seed));

                    if (first.Count != second.Count)
                    {
                        failures.Add($"seed {seed} block {block}: {zones[i].Kind} placed " +
                                     $"{first.Count} then {second.Count} props on identical calls");
                        return;
                    }

                    for (var s = 0; s < first.Count; s++)
                    {
                        if (first[s].Centre == second[s].Centre &&
                            Mathf.Approximately(first[s].Yaw, second[s].Yaw))
                            continue;

                        failures.Add($"seed {seed} block {block}: {zones[i].Kind} prop {s} moved " +
                                     $"between identical calls");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// No zone may touch a carriageway or the corridor inside the gate.
        ///
        /// This is the whole of the brief's "circulation lanes, minimum 5 units wide, kept
        /// completely clear" requirement. The planner carves no lanes because the compound
        /// already has them - so the requirement is not "were lanes built" but "did anything
        /// land on the ones that were already there", and that is what this asserts.
        /// </summary>
        static void ZonesNeverOverlapALane(List<string> failures)
        {
            foreach (var (layout, halls, seed, block) in AllYards())
            {
                var lanes = IndustrialLotPlanner.Lanes(layout);

                foreach (var zone in Plan(layout, halls, seed, block))
                foreach (var lane in lanes)
                {
                    if (!Overlaps(zone.Area, lane))
                        continue;

                    failures.Add($"seed {seed} block {block}: {zone.Kind} at {zone.Area.Centre} " +
                                 $"lies on a lane at {lane.Centre}");
                    return;
                }
            }
        }

        static void ZonesNeverOverlapAHall(List<string> failures)
        {
            foreach (var (layout, halls, seed, block) in AllYards())
            foreach (var zone in Plan(layout, halls, seed, block))
            foreach (var hall in halls)
            {
                if (!Overlaps(zone.Area, Footprint(hall)))
                    continue;

                failures.Add($"seed {seed} block {block}: {zone.Kind} at {zone.Area.Centre} " +
                             $"stands on the hall at {hall.Centre}");
                return;
            }
        }

        static void ZonesNeverOverlapEachOther(List<string> failures)
        {
            foreach (var (layout, halls, seed, block) in AllYards())
            {
                var zones = Plan(layout, halls, seed, block);

                for (var i = 0; i < zones.Count; i++)
                for (var j = i + 1; j < zones.Count; j++)
                {
                    if (!Overlaps(zones[i].Area, zones[j].Area))
                        continue;

                    failures.Add($"seed {seed} block {block}: {zones[i].Kind} and {zones[j].Kind} " +
                                 $"claim the same ground near {zones[i].Area.Centre}");
                    return;
                }
            }
        }

        /// <summary>
        /// A zone outside the wall is ground the compound does not own - it would put crates on
        /// the pavement outside a locked gate.
        /// </summary>
        static void ZonesStayInsideTheWall(List<string> failures)
        {
            foreach (var (layout, halls, seed, block) in AllYards())
            foreach (var zone in Plan(layout, halls, seed, block))
            {
                var wall = layout.Wall;
                if (zone.Area.Min.x >= wall.Min.x - 0.001f && zone.Area.Max.x <= wall.Max.x + 0.001f &&
                    zone.Area.Min.y >= wall.Min.y - 0.001f && zone.Area.Max.y <= wall.Max.y + 0.001f)
                    continue;

                failures.Add($"seed {seed} block {block}: {zone.Kind} reaches outside the wall");
                return;
            }
        }

        /// <summary>
        /// An apron must sit in front of the doors it serves, facing back at them.
        ///
        /// The one assertion that would catch the apron being derived from the wrong side of the
        /// hall. IndustrialDresser pushes halls AWAY from their road rather than towards it, so
        /// "in front" and "behind" are easy to get the wrong way round here, and a loading apron
        /// behind a warehouse is invisible in every screenshot.
        /// </summary>
        static void EveryApronFacesItsHallDoor(List<string> failures)
        {
            foreach (var (layout, halls, seed, block) in AllYards())
            foreach (var zone in Plan(layout, halls, seed, block))
            {
                if (zone.Kind != LotZoneKind.LoadingApron)
                    continue;

                if (zone.HallIndex < 0 || zone.HallIndex >= halls.Length)
                {
                    failures.Add($"seed {seed} block {block}: apron has no hall");
                    return;
                }

                var hall = halls[zone.HallIndex];

                if ((zone.Facing + hall.Outward).sqrMagnitude > 1e-4f)
                {
                    failures.Add($"seed {seed} block {block}: apron faces {zone.Facing}, " +
                                 $"hall door normal is {hall.Outward}");
                    return;
                }

                // The apron's centre must lie on the door side of the hall's own front face.
                var front = hall.Centre + hall.Outward * hall.HalfDepth;
                var centre = new Vector3(zone.Area.Centre.x, 0f, zone.Area.Centre.y);

                if (Vector3.Dot(centre - front, hall.Outward) <= 0f)
                {
                    failures.Add($"seed {seed} block {block}: apron sits behind its hall");
                    return;
                }
            }
        }

        /// <summary>
        /// A hall with plenty of ground in front of its doors must actually get an apron.
        ///
        /// Every other apron assertion is conditional on an apron existing, so all of them pass
        /// vacuously the moment aprons stop being emitted - which is not hypothetical. The first
        /// version of the planner projected onto an axis instead of along a direction, so for the
        /// half of all halls that face -X or -Z it measured the forecourt on the wrong side of
        /// the building, computed a constant 1m, and dropped the apron. Seven of seven mutation
        /// tests still passed. This is the assertion that catches it.
        ///
        /// Two rules, because the guarantee genuinely has two shapes. A hall with a very deep
        /// forecourt is served without exception - measured across the whole fixture, every one
        /// of the 4396 halls with 9m or more gets an apron. Below that the ground is contested by
        /// the lorry stand, the staff bays and the fill zones, and a handful lose: 6 of the 6902
        /// halls with 6m or more. Those are real contention rather than a defect, so the shallow
        /// rule is a rate rather than an absolute - but a rate still catches everything that
        /// matters here, because the failures this guards against are wholesale. The axis bug
        /// took the served rate to roughly half; dropping aprons entirely takes it to zero.
        /// </summary>
        static void AHallWithADeepForecourtGetsAnApron(List<string> failures)
        {
            const float Certain = 10f;
            const float Contested = 6f;
            const float MinServedRate = 0.95f;

            var contested = 0;
            var served = 0;

            foreach (var (layout, halls, seed, block) in AllYards())
            {
                var zones = Plan(layout, halls, seed, block);

                for (var i = 0; i < halls.Length; i++)
                {
                    var forecourt = Forecourt(halls[i]);
                    if (forecourt < Contested)
                        continue;

                    var hasApron = false;
                    foreach (var zone in zones)
                        hasApron |= zone.Kind == LotZoneKind.LoadingApron && zone.HallIndex == i;

                    contested++;
                    if (hasApron)
                        served++;

                    if (hasApron || forecourt < Certain)
                        continue;

                    failures.Add($"seed {seed} block {block}: hall {i} facing {halls[i].Outward} " +
                                 $"has a {forecourt:F1}m forecourt and no apron");
                    return;
                }
            }

            if (contested > 0 && served < contested * MinServedRate)
                failures.Add($"only {served} of {contested} halls with a {Contested}m forecourt " +
                             $"got an apron, under the {MinServedRate:P0} floor");
        }

        /// <summary>
        /// Clear ground between a hall's doors and the road edge of its own pad. Projected ALONG
        /// the dock normal rather than read off an axis, which is the distinction the planner got
        /// wrong - a test that repeated the mistake would agree with the bug.
        /// </summary>
        static float Forecourt(WorksHall hall)
        {
            var padCentre = (hall.PadMin + hall.PadMax) * 0.5f;
            var padSize = hall.PadMax - hall.PadMin;
            var padDepth = Mathf.Abs(hall.Outward.x) > 0.5f ? padSize.x : padSize.y;

            var front = Project(hall.Centre, hall.Outward) + hall.HalfDepth;
            var edge = Project(padCentre, hall.Outward) + padDepth * 0.5f;

            return edge - front;
        }

        static float Project(Vector3 point, Vector3 axis) => point.x * axis.x + point.z * axis.z;
        static float Project(Vector2 point, Vector3 axis) => point.x * axis.x + point.y * axis.z;

        /// <summary>
        /// Discretionary zones must not tile the lot. Open ground is correct - a yard needs
        /// turning room, and the brief's own answer to bare ground is surface treatment rather
        /// than more objects.
        ///
        /// Measured over the OPPORTUNISTIC zones only, against the ground left once the aprons
        /// have taken theirs - which is the contract the planner actually holds, and holds for a
        /// reason. Aprons are derived from doors and are exempt from the cap; letting the cap
        /// reach them made it delete a loading apron off a hall with 9.5m of clear forecourt.
        /// </summary>
        static void CoverageStaysUnderTheCap(List<string> failures)
        {
            var tuning = IndustrialLotPlanner.Tuning.Default;

            foreach (var (layout, halls, seed, block) in AllYards())
            {
                var zoned = 0f;
                var aproned = 0f;

                foreach (var zone in Plan(layout, halls, seed, block))
                {
                    var area = zone.Area.Size.x * zone.Area.Size.y;
                    if (zone.Kind == LotZoneKind.LoadingApron)
                        aproned += area;
                    else
                        zoned += area;
                }

                var free = IndustrialLotPlanner.FreeGround(layout, halls) - aproned;

                // Against FREE ground, not the wall rect. Measured against the wall this cannot
                // fail and is therefore not a test: the compound spends most of its own area on
                // halls and carriageways first, so zones peak at about 18% of it and a half cap
                // sits nowhere near. Deleting the cap outright takes that to 26% - still under a
                // wall-relative half, which is exactly how the first version of this assertion
                // passed under the mutation it existed to catch.
                if (zoned <= free * tuning.maxZoneCoverage + 0.001f)
                    continue;

                failures.Add($"seed {seed} block {block}: fill zones claim {zoned:F0} of " +
                             $"{free:F0} open square metres, over the " +
                             $"{tuning.maxZoneCoverage:P0} cap");
                return;
            }
        }

        /// <summary>
        /// A yard with plenty of open ground must actually get zoned.
        ///
        /// The complement of the coverage cap, and the assertion that corresponds to somebody
        /// looking at the result and saying it is empty. Every other check here is a ceiling -
        /// zones must not overlap, must not tile, must not stray - and a planner that emitted
        /// almost nothing would satisfy all of them. It very nearly did: with one stockpile per
        /// yard, a 106m compound whose pads mostly came out bare zoned 15% of 6700 square metres
        /// and the cap never engaged.
        ///
        /// Modelled on the case that exposed it: halls THINNED to two, because BuildHalls leaves
        /// most pads empty on a real block and the fixture that puts a hall on every pad never
        /// produces this shape of yard at all.
        /// </summary>
        static void ARoomyYardIsActuallyZoned(List<string> failures)
        {
            const float Roomy = 2000f;
            const float MinCoverage = 0.3f;

            for (var seed = 1; seed <= 25; seed++)
            for (var block = 0; block < 4; block++)
            {
                var layout = Layout(106f, 106f, Sides.All, seed, block);
                if (!layout.Usable)
                    continue;

                var halls = Thinned(Halls(layout), 2);

                var free = IndustrialLotPlanner.FreeGround(layout, halls);
                if (free < Roomy)
                    continue;

                var zoned = 0f;
                foreach (var zone in Plan(layout, halls, seed, block))
                    zoned += zone.Area.Size.x * zone.Area.Size.y;

                if (zoned >= free * MinCoverage)
                    continue;

                failures.Add($"seed {seed} block {block}: {free:F0} square metres of open ground " +
                             $"and only {zoned:F0} zoned ({zoned / free:P0}, under the " +
                             $"{MinCoverage:P0} floor)");
                return;
            }
        }

        /// <summary>
        /// The first n halls only. BuildHalls rejects a pad whenever its draw does not fit, so a
        /// real works often stands two or three buildings on a compound planned for eleven - and
        /// that, not the full one, is the yard that looks empty.
        /// </summary>
        static WorksHall[] Thinned(WorksHall[] halls, int n)
        {
            if (halls.Length <= n)
                return halls;

            var kept = new WorksHall[n];
            System.Array.Copy(halls, kept, n);
            return kept;
        }

        /// <summary>
        /// A one-cell block has one road, one row and at most two halls, and after the wall inset
        /// and the gate apron there is very little ground left. It must not answer that by
        /// cramming: a zone that cannot fit is dropped, never shrunk into a token.
        ///
        /// The bound is measured rather than guessed, which is worth saying because the first
        /// version of it was guessed - at four - and then failed the moment the coverage cap
        /// stopped shedding the singleton zones. A 46m compound settles at exactly five: an
        /// apron, truck staging, a boiler plot, its cinders and a scrap corner, filling 57% of
        /// 704 square metres of open ground. What it never gets is a bulk stockpile, because
        /// there is no room for one, and that is the degradation this is really asserting - the
        /// zone that repeats is the zone that vanishes first when the ground runs out.
        /// </summary>
        static void SmallBlocksDegradeRatherThanCrowd(List<string> failures)
        {
            for (var seed = 1; seed <= 30; seed++)
            for (var block = 0; block < 6; block++)
            {
                var layout = Layout(46f, 46f, Sides.All, seed, block);
                if (!layout.Usable)
                    continue;

                var halls = Halls(layout);
                var zones = Plan(layout, halls, seed, block);

                if (zones.Count > 6)
                {
                    failures.Add($"seed {seed} block {block}: a 46m block planned " +
                                 $"{zones.Count} zones");
                    return;
                }

                var stockpiles = 0;
                foreach (var zone in zones)
                    if (zone.Kind == LotZoneKind.RawMaterialYard)
                        stockpiles++;

                if (stockpiles <= 1)
                    continue;

                failures.Add($"seed {seed} block {block}: a 46m block planned {stockpiles} " +
                             $"bulk stockpiles");
                return;
            }
        }

        /// <summary>
        /// Two identical calls must agree exactly. The planner runs from the marker at generation
        /// and could be replayed by anything later, so a partition that drifted between calls
        /// would put the ground pass, the prop pass and the gizmos on three different maps.
        /// </summary>
        static void SameSeedSameZones(List<string> failures)
        {
            for (var seed = 1; seed <= 20; seed++)
            for (var block = 0; block < 6; block++)
            {
                var layout = Layout(76f, 76f, Sides.All, seed, block);
                if (!layout.Usable)
                    continue;

                var halls = Halls(layout);
                var a = Plan(layout, halls, seed, block);
                var b = Plan(layout, halls, seed, block);

                if (a.Count != b.Count)
                {
                    failures.Add($"seed {seed} block {block}: two identical calls planned " +
                                 $"{a.Count} and {b.Count} zones");
                    return;
                }

                for (var i = 0; i < a.Count; i++)
                {
                    if (a[i].Kind == b[i].Kind &&
                        (a[i].Area.Min - b[i].Area.Min).sqrMagnitude < 1e-6f &&
                        (a[i].Area.Max - b[i].Area.Max).sqrMagnitude < 1e-6f)
                        continue;

                    failures.Add($"seed {seed} block {block}: zone {i} moved between two " +
                                 $"identical calls");
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- fixtures

        static IEnumerable<(IndustrialLayout.Layout Layout, WorksHall[] Halls, int Seed, int Block)>
            AllYards()
        {
            var sides = new[] { Sides.North, Sides.East, Sides.North | Sides.South, Sides.All };

            for (var seed = 1; seed <= 12; seed++)
            for (var block = 0; block < 4; block++)
            foreach (var width in BlockSizes)
            foreach (var depth in BlockSizes)
            foreach (var side in sides)
            {
                var layout = Layout(width, depth, side, seed, block);
                if (!layout.Usable)
                    continue;

                yield return (layout, Halls(layout), seed, block);
            }
        }

        static List<LotZone> Plan(
            IndustrialLayout.Layout layout, WorksHall[] halls, int seed, int block) =>
            IndustrialLotPlanner.Plan(layout, halls, IndustrialLotPlanner.Tuning.Default, seed, block);

        static IndustrialLayout.Layout Layout(float width, float depth, Sides sides, int seed, int block) =>
            IndustrialLayout.ForBlock(new Vector2(0f, 0f), new Vector2(width, depth), sides,
                                      IndustrialLayout.DefaultRoadWidth, 1f, seed, block);

        /// <summary>
        /// Stands halls on the pads the way IndustrialDresser.BuildHalls does, including its
        /// setback sign - which puts each hall at the BACK of its pad and leaves the free band in
        /// front of the doors. Reproduced rather than corrected on purpose: the planner has to be
        /// tested against the geometry that actually ships, not against the geometry that was
        /// intended. If BuildHalls is ever fixed, this must be fixed with it and the apron tests
        /// are what will notice.
        ///
        /// Picks the widest piece that fits rather than drawing at random, so the fixture is a
        /// function of the layout alone and the determinism test means what it says.
        /// </summary>
        static WorksHall[] Halls(IndustrialLayout.Layout layout)
        {
            var halls = new List<WorksHall>();

            foreach (var pad in layout.Pads)
            {
                var alongX = Mathf.Abs(pad.Outward.z) > 0.5f;
                var padWidth = alongX ? pad.Area.Size.x : pad.Area.Size.y;
                var padDepth = alongX ? pad.Area.Size.y : pad.Area.Size.x;

                var runDepth = padDepth - RoadSetback;
                if (padWidth < 8f || runDepth < 6f)
                    continue;

                var lateral = Vector3.Cross(Vector3.up, pad.Outward);
                var padCentre = new Vector3(pad.Area.Centre.x, 0f, pad.Area.Centre.y);

                var cursor = HallClearance * 0.5f;
                var remaining = padWidth - HallClearance;

                for (var slot = 0; slot < 2 && remaining >= 6f; slot++)
                {
                    var width = 0f;
                    var depthUsed = 0f;

                    foreach (var piece in Works)
                    {
                        if (piece.Width > remaining || piece.Depth > runDepth || piece.Width <= width)
                            continue;

                        width = piece.Width;
                        depthUsed = piece.Depth;
                    }

                    if (width <= 0f)
                        break;

                    var position = padCentre
                                 + lateral * (cursor + width * 0.5f - padWidth * 0.5f)
                                 - pad.Outward * ((padDepth - depthUsed) * 0.5f - RoadSetback);

                    halls.Add(new WorksHall
                    {
                        Centre = position,
                        Outward = pad.Outward,
                        HalfWidth = width * 0.5f,
                        HalfDepth = depthUsed * 0.5f,
                        PadMin = pad.Area.Min,
                        PadMax = pad.Area.Max,
                    });

                    cursor += width + HallClearance;
                    remaining -= width + HallClearance;
                }
            }

            return halls.ToArray();
        }

        static IndustrialLayout.Rect Footprint(WorksHall hall)
        {
            var lateral = Vector3.Cross(Vector3.up, hall.Outward);
            var half = new Vector2(
                Mathf.Abs(hall.Outward.x) * hall.HalfDepth + Mathf.Abs(lateral.x) * hall.HalfWidth,
                Mathf.Abs(hall.Outward.z) * hall.HalfDepth + Mathf.Abs(lateral.z) * hall.HalfWidth);

            var centre = new Vector2(hall.Centre.x, hall.Centre.z);
            return new IndustrialLayout.Rect { Min = centre - half, Max = centre + half };
        }

        /// <summary>
        /// Tolerant by a millimetre, like IndustrialLayoutTests: two rectangles that share an
        /// edge are abutting, not overlapping, and a zone laid flush against a carriageway kerb
        /// is exactly what is wanted.
        /// </summary>
        static bool Overlaps(IndustrialLayout.Rect a, IndustrialLayout.Rect b) =>
            a.Min.x < b.Max.x - 0.001f && b.Min.x < a.Max.x - 0.001f &&
            a.Min.y < b.Max.y - 0.001f && b.Min.y < a.Max.y - 0.001f;
    }
}
