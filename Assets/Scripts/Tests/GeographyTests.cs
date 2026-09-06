using System.Collections.Generic;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// Headless contracts for GAN-68 / GEO-001 through GEO-010. The city here is
    /// synthetic on purpose: a 3x3 grid of 40 m blocks with 15 m streets between them,
    /// one block across a 35 m boulevard, one block nested inside another (downtown's
    /// shape), and one standing alone in open ground. Every rule the geography claims -
    /// smallest-containing lookup, adjacency across a street but not across a junction,
    /// road-space hysteresis, business membership - is measurable on that plan without a
    /// scene, a prefab or a camera.
    /// </summary>
    public static class GeographyTests
    {
        const float Block = 40f;
        const float Street = 15f;
        const float Step = Block + Street;

        public static List<string> Run()
        {
            var failures = new List<string>();

            FacadeAnswersTheWholeGeography(failures);
            NeighborhoodsAggregateBlocksAndNeverOwnThem(failures);
            BlockModelCarriesGeometryAndNoOwnership(failures);
            EveryBlockIsInExactlyOneNeighborhood(failures);
            IdentityIsUniqueAndOrderIsStable(failures);
            StandingResolvesRoadSpaceDeterministically(failures);
            BusinessesMapToExactlyOneBlock(failures);
            NeighbourGraphIsSymmetricAndGeometric(failures);
            PrecinctsAreTheNearestStationHouseByHops(failures);

            return failures;
        }

        // ------------------------------------------------------------------ the city

        static TerritoryBlockDefinition Definition(
            string id, int legacy, string hood, string hoodName,
            float x, float z, float w, float d, string kind = "res") =>
            new TerritoryBlockDefinition(
                new TerritoryBlockId(id),
                legacy,
                new TerritoryNeighborhoodId(hood),
                hoodName,
                id,
                new TerritoryBounds(x, z, w, d),
                "test",
                kind);

        /// <summary>The plan every case below is measured on.</summary>
        static List<TerritoryBlockDefinition> City()
        {
            var blocks = new List<TerritoryBlockDefinition>();
            var legacy = 0;
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                    blocks.Add(Definition(
                        "grid:" + i + ":" + j, legacy++, "hood:grid", "The Grid",
                        i * Step, j * Step, Block, Block));

            // Across the boulevard: 35 m of carriageway from the east column's kerb.
            blocks.Add(Definition("east:0", legacy++, "hood:east", "Eastside",
                                  2 * Step + Block + 35f, 0f, Block, Block));

            // Downtown's nested block: a small plot standing inside grid:0:0.
            blocks.Add(Definition("nested:0", legacy++, "hood:grid", "The Grid",
                                  10f, 10f, 20f, 20f));

            // Alone in open country, far past any street.
            blocks.Add(Definition("lonely:0", legacy++, "hood:far", "The Flats",
                                  600f, 600f, Block, Block));
            return blocks;
        }

        static TerritoryGeography Build(IEnumerable<TerritoryBlockDefinition> blocks = null,
                                        IEnumerable<TerritoryPrecinctSeat> houses = null) =>
            new TerritoryGeography(
                blocks ?? City(),
                TerritoryGeographySettings.Default,
                new[]
                {
                    new TerritoryOffGridArea(
                        "Harbour", "Harbor", new TerritoryBounds(-400f, -400f, 200f, 200f),
                        "outside the territory plan"),
                },
                houses);

        /// <summary>The middle of one grid block, which is where a station house on it
        /// stands for these cases.</summary>
        static TerritoryPoint GridCentre(int i, int j) =>
            new TerritoryPoint(i * Step + Block * 0.5f, j * Step + Block * 0.5f);

        static TerritoryPrecinctSeat House(int stationId, int i, int j) =>
            new TerritoryPrecinctSeat(stationId, GridCentre(i, j));

        // ------------------------------------------------------------------- GEO-001

        static void FacadeAnswersTheWholeGeography(List<string> failures)
        {
            var geography = Build();

            if (geography.BlockIds.Count != 12)
                failures.Add("GEO-001: the facade did not publish every block.");
            if (geography.NeighborhoodIds.Count != 3)
                failures.Add("GEO-001: the facade did not publish every neighborhood.");
            if (geography.OffGridAreas.Count != 1)
                failures.Add("GEO-001: off-grid areas were not published.");

            if (!geography.TryGetBlock(new TerritoryBlockId("grid:1:1"), out var block))
            {
                failures.Add("GEO-001: a known block could not be fetched.");
                return;
            }

            var centre = block.Center;
            if (centre.X != Step + Block * 0.5f || centre.Z != Step + Block * 0.5f)
                failures.Add("GEO-001: the block centre is not the middle of its bounds.");

            // The same world point, asked twice, is the same block: this is the property
            // the UI and the simulation both depend on and neither can verify alone.
            if (!geography.TryGetBlockAt(new TerritoryPoint(centre.X, centre.Z), out var one) ||
                !geography.TryGetBlockAt(new TerritoryPoint(centre.X, centre.Z), out var two) ||
                one != two || one != block.Id)
                failures.Add("GEO-001: a world position did not resolve to its own block.");

            if (geography.Neighbours(block.Id).Count == 0)
                failures.Add("GEO-001: the facade returned no neighbours for a grid block.");

            var whole = geography.WorldBounds;
            if (whole.XMin > 0f || whole.XMax < 640f)
                failures.Add("GEO-001: the city bounds do not contain every block.");
        }

        // ------------------------------------------------------------------- GEO-002

        static void NeighborhoodsAggregateBlocksAndNeverOwnThem(List<string> failures)
        {
            var geography = Build();
            if (!geography.TryGetNeighborhood(new TerritoryNeighborhoodId("hood:grid"),
                                              out var grid))
            {
                failures.Add("GEO-002: a known neighborhood could not be fetched.");
                return;
            }

            if (grid.Name != "The Grid")
                failures.Add("GEO-002: the neighborhood lost its display name.");
            if (grid.BlockIds.Count != 10)
                failures.Add("GEO-002: the neighborhood did not aggregate its blocks.");
            if (grid.WorldBounds.Width < 2 * Step + Block)
                failures.Add("GEO-002: the neighborhood bounds do not span its blocks.");

            // Adjacency between quarters is derived from the block graph, so a quarter
            // across the boulevard is a neighbour and the far flats are not.
            var touches = false;
            var far = false;
            for (var i = 0; i < grid.Neighbours.Count; i++)
            {
                if (grid.Neighbours[i].Value == "hood:east")
                    touches = true;
                if (grid.Neighbours[i].Value == "hood:far")
                    far = true;
            }

            if (!touches)
                failures.Add("GEO-002: the neighborhood across the boulevard is not a neighbour.");
            if (far)
                failures.Add("GEO-002: a neighborhood with no shared frontage was called a neighbour.");

            var type = typeof(TerritoryNeighborhoodDefinition);
            if (type.GetProperty("OwnerGangId") != null || type.GetProperty("ControlledBy") != null)
                failures.Add("GEO-002: a neighborhood was given a mutable owner.");

            // The same input twice gives the same ids, in the same order.
            var again = Build();
            if (again.NeighborhoodIds.Count != geography.NeighborhoodIds.Count)
                failures.Add("GEO-002: neighborhood identity is not stable across builds.");
            for (var i = 0; i < again.NeighborhoodIds.Count; i++)
                if (again.NeighborhoodIds[i] != geography.NeighborhoodIds[i])
                {
                    failures.Add("GEO-002: neighborhood order is not stable across builds.");
                    break;
                }
        }

        // ------------------------------------------------------------------- GEO-003

        static void BlockModelCarriesGeometryAndNoOwnership(List<string> failures)
        {
            var type = typeof(TerritoryBlockDefinition);
            if (type.GetProperty("Center") == null)
                failures.Add("GEO-003: the block model has no centre.");
            if (type.GetProperty("OwnerGangId") != null ||
                type.GetProperty("ControlledBy") != null ||
                type.GetProperty("CaptureProgress") != null)
                failures.Add("GEO-003: mutable territory authority was added to the block model.");

            var geography = Build();
            if (!geography.TryGetBlock(new TerritoryBlockId("nested:0"), out var nested))
            {
                failures.Add("GEO-003: the nested block is missing.");
                return;
            }

            if (nested.SourceKind != "res")
                failures.Add("GEO-003: the block model lost the plan's own kind.");
            if (nested.LegacyBlockId < 0)
                failures.Add("GEO-003: the block model lost the legacy block id.");
        }

        // ------------------------------------------------------------------- GEO-004

        static void EveryBlockIsInExactlyOneNeighborhood(List<string> failures)
        {
            var geography = Build();
            var seen = new Dictionary<TerritoryBlockId, int>();
            for (var i = 0; i < geography.NeighborhoodIds.Count; i++)
            {
                geography.TryGetNeighborhood(geography.NeighborhoodIds[i], out var hood);
                for (var b = 0; b < hood.BlockIds.Count; b++)
                {
                    seen.TryGetValue(hood.BlockIds[b], out var count);
                    seen[hood.BlockIds[b]] = count + 1;
                }
            }

            for (var i = 0; i < geography.BlockIds.Count; i++)
            {
                if (!seen.TryGetValue(geography.BlockIds[i], out var count) || count != 1)
                {
                    failures.Add("GEO-004: block '" + geography.BlockIds[i].Value +
                                 "' is not in exactly one neighborhood.");
                    break;
                }
            }

            // A block with no neighborhood is a fault, not a silent default.
            var orphaned = new List<TerritoryBlockDefinition>(City())
            {
                Definition("orphan:0", 99, "", "", 900f, 900f, Block, Block),
            };
            var broken = new TerritoryGeography(orphaned, TerritoryGeographySettings.Default);
            if (broken.Report.Passed)
                failures.Add("GEO-004: a block belonging to no neighborhood was not reported.");

            // Off-grid ground is classified out loud rather than left to a failed lookup.
            var notes = geography.Report.Notes;
            var classified = false;
            for (var i = 0; i < notes.Count; i++)
                if (notes[i].Contains("off-grid") && notes[i].Contains("NOT territory"))
                    classified = true;
            if (!classified)
                failures.Add("GEO-004: off-grid ground was not classified as non-territory.");
        }

        // ------------------------------------------------------------------- GEO-005

        static void IdentityIsUniqueAndOrderIsStable(List<string> failures)
        {
            var doubled = new List<TerritoryBlockDefinition>(City());
            doubled.Add(Definition("grid:0:0", 77, "hood:grid", "The Grid", 0f, 0f, Block, Block));
            var geography = new TerritoryGeography(doubled, TerritoryGeographySettings.Default);
            if (geography.BlockIds.Count != 12)
                failures.Add("GEO-005: a duplicate canonical id was accepted.");
            if (geography.Report.Passed)
                failures.Add("GEO-005: a duplicate canonical id was not reported.");

            // Definition order must not reach the enumeration: a pass that added a block
            // would otherwise renumber every consumer downstream of it.
            var shuffled = new List<TerritoryBlockDefinition>(City());
            shuffled.Reverse();
            var reversed = new TerritoryGeography(shuffled, TerritoryGeographySettings.Default);
            var straight = Build();
            for (var i = 0; i < straight.BlockIds.Count; i++)
                if (straight.BlockIds[i] != reversed.BlockIds[i])
                {
                    failures.Add("GEO-005: block enumeration depends on definition order.");
                    break;
                }

            // Every block's own ground resolves back to it, and a point inside the nested
            // block resolves to the SMALLER block, never to the one it stands in.
            for (var i = 0; i < straight.BlockIds.Count; i++)
            {
                straight.TryGetBlock(straight.BlockIds[i], out var block);
                var centre = block.Center;
                if (!straight.TryGetBlockAt(new TerritoryPoint(centre.X, centre.Z), out var at))
                {
                    failures.Add("GEO-005: a block centre resolved to no block.");
                    break;
                }

                if (at != block.Id && block.Id.Value != "grid:0:0")
                {
                    failures.Add("GEO-005: '" + block.Id.Value + "' does not own its own centre.");
                    break;
                }
            }

            if (!straight.TryGetBlockAt(new TerritoryPoint(20f, 20f), out var smallest) ||
                smallest.Value != "nested:0")
                failures.Add("GEO-005: the smallest-containing rule failed on a nested block.");
        }

        // ------------------------------------------------------------------- GEO-006

        static void StandingResolvesRoadSpaceDeterministically(List<string> failures)
        {
            var geography = Build();
            var a = new TerritoryBlockId("grid:0:0");
            var b = new TerritoryBlockId("grid:1:0");

            // On the block: the block, whoever asks and wherever he came from.
            if (!geography.TryResolveStanding(new TerritoryPoint(35f, 35f), default, out var on) ||
                on != a)
                failures.Add("GEO-006: a point inside a block did not resolve to it.");

            // On the pavement outside it: the block he came from.
            if (!geography.TryResolveStanding(new TerritoryPoint(43f, 20f), a, out var kerb) ||
                kerb != a)
                failures.Add("GEO-006: a man on the kerb lost the block he came from.");

            // The same pavement with no history is road space and belongs to nobody -
            // never to the nearest block, which is how a distant random assignment starts.
            if (geography.TryResolveStanding(new TerritoryPoint(43f, 20f), default, out _))
                failures.Add("GEO-006: road space was assigned to a block with no history.");

            // Crossing the street between two blocks makes exactly one transition, and
            // never goes blockless: an ordinary street is narrower than the hysteresis,
            // so the man leaves A exactly as he arrives on B.
            // z = 35 keeps the walk clear of the nested block, which owns z 10..30.
            var walk = new[] { 10f, 25f, 38f, 44f, 50f, 54f, 58f, 70f, 85f };
            var previous = default(TerritoryBlockId);
            var changes = 0;
            var blockless = 0;
            for (var i = 0; i < walk.Length; i++)
            {
                if (!geography.TryResolveStanding(new TerritoryPoint(walk[i], 35f), previous,
                                                  out var current))
                {
                    blockless++;
                    continue;
                }

                if (previous.IsValid && current != previous)
                    changes++;
                previous = current;
            }

            if (changes != 1)
                failures.Add("GEO-006: crossing one street produced " + changes +
                             " block transitions, not one.");
            if (blockless != 0)
                failures.Add("GEO-006: an ordinary street crossing went blockless.");
            if (previous != b)
                failures.Add("GEO-006: the walk did not end on the block opposite.");

            // Crossing the BOULEVARD is wider than the hysteresis, so the middle of it is
            // nobody's ground. What must still hold is that the man visits two blocks and
            // only two - one leave, one enter, no flutter at either kerb.
            var visited = new List<string>();
            previous = default;
            for (var x = 220f; x >= 120f; x -= 5f)
            {
                var resolved = geography.TryResolveStanding(
                    new TerritoryPoint(x, 20f), previous, out var current);
                var mark = resolved ? current.Value : "";
                previous = resolved ? current : default;
                if (visited.Count == 0 || visited[visited.Count - 1] != mark)
                    visited.Add(mark);
            }

            if (visited.Count != 3 || visited[0] != "east:0" || visited[1] != "" ||
                visited[2] != "grid:2:0")
                failures.Add("GEO-006: crossing a boulevard did not read as one leave, " +
                             "open road, one enter.");

            // Open ground far from every block belongs to nobody, whatever the man was
            // standing on before - a distant nearest-block guess is the failure this
            // rule exists to prevent.
            if (geography.TryResolveStanding(new TerritoryPoint(300f, 300f),
                                             new TerritoryBlockId("grid:2:2"), out _))
                failures.Add("GEO-006: open ground was handed to the block behind the man.");
        }

        // ------------------------------------------------------------------- GEO-007

        static void BusinessesMapToExactlyOneBlock(List<string> failures)
        {
            var geography = Build();
            var sites = new SiteBench();

            // 1. the provider's own block hint - the plan's word, taken as it stands
            sites.Add("res|grid:1:1|shop", "biz:1", new TerritoryBlockId("grid:1:1"),
                      new TerritoryBounds(0f, 0f, 0f, 0f), new TerritoryPoint(0f, 0f));
            // 2. no hint, but a footprint standing on a block
            sites.Add("venue|x|club", "biz:2", default,
                      new TerritoryBounds(Step + 5f, Step + 5f, 10f, 10f),
                      new TerritoryPoint(Step + 10f, Step - 3f));
            // 3. no hint, no footprint: only a doorstep, on the pavement as they all are
            sites.Add("venue|y|kiosk", "biz:3", default, new TerritoryBounds(0f, 0f, 0f, 0f),
                      new TerritoryPoint(Block + 4f, 20f));
            // 4. a shop in open country, on no block at all
            sites.Add("venue|z|farm", "biz:4", default,
                      new TerritoryBounds(2000f, 2000f, 10f, 10f),
                      new TerritoryPoint(2005f, 1990f));

            geography.BindBusinesses(sites);

            if (!geography.TryGetBusinessBlock(new TerritoryBusinessId("biz:1"), out var hinted) ||
                hinted.Value != "grid:1:1")
                failures.Add("GEO-007: the provider's block hint was not preferred.");
            if (!geography.TryGetBusinessBlock(new TerritoryBusinessId("biz:2"), out var footprint) ||
                footprint.Value != "grid:1:1")
                failures.Add("GEO-007: a footprint on a block did not resolve to it.");
            if (!geography.TryGetBusinessBlock(new TerritoryBusinessId("biz:3"), out var door) ||
                door.Value != "grid:0:0")
                failures.Add("GEO-007: a doorstep on the pavement did not resolve inward.");
            if (geography.TryGetBusinessBlock(new TerritoryBusinessId("biz:4"), out _))
                failures.Add("GEO-007: a business on no block was guessed onto one.");

            if (geography.UnplacedBusinesses.Count != 1)
                failures.Add("GEO-007: the unmapped business was not reported.");

            var onBlock = geography.BusinessesOf(new TerritoryBlockId("grid:1:1"));
            if (onBlock.Count != 2)
                failures.Add("GEO-007: the block does not list the businesses standing on it.");

            // The order sites arrive in is the streaming order; the answer must not be.
            var reversed = Build();
            reversed.BindBusinesses(sites.Reversed());
            var sameOrder = true;
            for (var i = 0; i < geography.BlockIds.Count && sameOrder; i++)
            {
                var mine = geography.BusinessesOf(geography.BlockIds[i]);
                var theirs = reversed.BusinessesOf(geography.BlockIds[i]);
                if (mine.Count != theirs.Count)
                {
                    sameOrder = false;
                    break;
                }

                for (var b = 0; b < mine.Count; b++)
                    if (mine[b].SiteId != theirs[b].SiteId)
                    {
                        sameOrder = false;
                        break;
                    }
            }

            if (!sameOrder)
                failures.Add("GEO-007: business membership depends on publish order.");

            // An ineligible site (a civic building, a park) is not a business and must
            // not be counted as one, mapped or unmapped.
            var withCivic = Build();
            var civic = new SiteBench();
            civic.Add("venue|civic|hall", "biz:9", new TerritoryBlockId("grid:2:2"),
                      new TerritoryBounds(0f, 0f, 0f, 0f), new TerritoryPoint(0f, 0f),
                      eligible: false);
            withCivic.BindBusinesses(civic);
            if (withCivic.BusinessesOf(new TerritoryBlockId("grid:2:2")).Count != 0 ||
                withCivic.UnplacedBusinesses.Count != 0)
                failures.Add("GEO-007: an ineligible site was treated as a business.");
        }

        // ------------------------------------------------------------------- GEO-008

        static void NeighbourGraphIsSymmetricAndGeometric(List<string> failures)
        {
            var geography = Build();

            for (var i = 0; i < geography.BlockIds.Count; i++)
            {
                var id = geography.BlockIds[i];
                var neighbours = geography.Neighbours(id);
                for (var n = 0; n < neighbours.Count; n++)
                {
                    if (neighbours[n] == id)
                        failures.Add("GEO-008: the graph has a self-edge on '" + id.Value + "'.");
                    if (!geography.AreNeighbours(neighbours[n], id))
                        failures.Add("GEO-008: the graph is not symmetric at '" + id.Value + "'.");
                }
            }

            // Across a street: neighbours. Across a junction (diagonal): not.
            if (!geography.AreNeighbours(new TerritoryBlockId("grid:0:0"),
                                         new TerritoryBlockId("grid:1:0")))
                failures.Add("GEO-008: two blocks facing across a street are not neighbours.");
            if (geography.AreNeighbours(new TerritoryBlockId("grid:0:0"),
                                        new TerritoryBlockId("grid:1:1")))
                failures.Add("GEO-008: two blocks meeting at a junction were called neighbours.");

            // Across a 35 m boulevard: still neighbours - the tolerance comes from the
            // city's own widest street, not from a constant that fits an ordinary one.
            if (!geography.AreNeighbours(new TerritoryBlockId("grid:2:0"),
                                         new TerritoryBlockId("east:0")))
                failures.Add("GEO-008: a boulevard separated two blocks that face each other.");

            // A nested block is adjacent to the one it stands in.
            if (!geography.AreNeighbours(new TerritoryBlockId("nested:0"),
                                         new TerritoryBlockId("grid:0:0")))
                failures.Add("GEO-008: a nested block is not adjacent to its host.");

            // Open country is not adjacency, however the map is coloured.
            if (geography.Neighbours(new TerritoryBlockId("lonely:0")).Count != 0)
                failures.Add("GEO-008: a block alone in open ground was given neighbours.");

            // Same plan, same graph - twice, and with the definitions reversed.
            var shuffled = new List<TerritoryBlockDefinition>(City());
            shuffled.Reverse();
            var again = new TerritoryGeography(shuffled, TerritoryGeographySettings.Default);
            if (again.Report.Edges != geography.Report.Edges)
                failures.Add("GEO-008: the edge count depends on definition order.");
            var stable = true;
            for (var i = 0; i < geography.BlockIds.Count && stable; i++)
            {
                var mine = geography.Neighbours(geography.BlockIds[i]);
                var theirs = again.Neighbours(geography.BlockIds[i]);
                if (mine.Count != theirs.Count)
                {
                    stable = false;
                    break;
                }

                for (var n = 0; n < mine.Count; n++)
                    if (mine[n] != theirs[n])
                    {
                        stable = false;
                        break;
                    }
            }

            if (!stable)
                failures.Add("GEO-008: the neighbour graph is not stable across builds.");

            if (geography.Report.Edges <= 0)
                failures.Add("GEO-008: adjacency counts are not reported.");
        }

        /// <summary>A business site source with nothing behind it but the list.</summary>
        sealed class SiteBench : ITerritoryBusinessSiteSource
        {
            readonly List<TerritoryBusinessSiteRecord> records =
                new List<TerritoryBusinessSiteRecord>();

            public void Add(string siteId, string businessId, TerritoryBlockId hint,
                            TerritoryBounds footprint, TerritoryPoint approach,
                            bool eligible = true) =>
                records.Add(new TerritoryBusinessSiteRecord(
                    siteId, new TerritoryBusinessId(businessId), hint, footprint, approach,
                    eligible, siteId, "test"));

            public IReadOnlyList<TerritoryBusinessSiteRecord> Sites() => records;

            public ITerritoryBusinessSiteSource Reversed()
            {
                var copy = new SiteBench();
                for (var i = records.Count - 1; i >= 0; i--)
                    copy.records.Add(records[i]);
                return copy;
            }
        }

        // ------------------------------------------------------------------- GAN-236

        /// <summary>
        /// THE PRECINCT MAP. A block belongs to the station house it is fewest street
        /// crossings from, and that is measured on the block graph rather than with a
        /// tape: the city here has a block across a 35 m boulevard and one alone in open
        /// country, and both are answered by the rule rather than by their distance.
        ///
        /// The three properties that matter downstream: a city with no house says so
        /// instead of guessing; an equal walk goes to the lower station id, so the answer
        /// does not depend on the order the houses were handed over in; and a house that
        /// stands on no block is a reported fault rather than a silent claim.
        /// </summary>
        static void PrecinctsAreTheNearestStationHouseByHops(List<string> failures)
        {
            var lawless = Build();
            for (var i = 0; i < lawless.BlockIds.Count; i++)
                if (lawless.PrecinctOf(lawless.BlockIds[i]) != TerritoryPrecinctMap.NoPrecinct)
                {
                    failures.Add("GAN-236: a city with no station house claimed a precinct.");
                    break;
                }
            if (lawless.Report.Precincts != 0 ||
                lawless.Report.UnpolicedBlocks != lawless.BlockIds.Count)
                failures.Add("GAN-236: a city with no station house did not report itself " +
                             "unpoliced.");

            var houses = new[] { House(0, 0, 0), House(1, 2, 2) };
            var city = Build(City(), houses);

            if (city.PrecinctSeats.Count != 2)
            {
                failures.Add("GAN-236: two station houses did not seat on two blocks.");
                return;
            }

            if (city.PrecinctSeats[0].Block != new TerritoryBlockId("grid:0:0") ||
                city.PrecinctSeats[1].Block != new TerritoryBlockId("grid:2:2"))
                failures.Add("GAN-236: a station house was seated on the wrong block.");

            if (city.PrecinctOf(new TerritoryBlockId("grid:0:0")) != 0 ||
                city.PrecinctOf(new TerritoryBlockId("grid:2:2")) != 1)
                failures.Add("GAN-236: a station house does not police its own block.");

            if (city.Precincts.HopsToStation(new TerritoryBlockId("grid:0:0")) != 0)
                failures.Add("GAN-236: a station house is not nought crossings from itself.");

            // One crossing from the first house, three from the second.
            if (city.PrecinctOf(new TerritoryBlockId("grid:0:1")) != 0 ||
                city.Precincts.HopsToStation(new TerritoryBlockId("grid:0:1")) != 1)
                failures.Add("GAN-236: the block next door went to the wrong house.");

            // One crossing from the second house, three from the first.
            if (city.PrecinctOf(new TerritoryBlockId("grid:2:1")) != 1)
                failures.Add("GAN-236: the far end of town went to the near house.");

            // Two crossings from each: the lower id takes it, whoever was handed over first.
            if (city.PrecinctOf(new TerritoryBlockId("grid:1:1")) != 0)
                failures.Add("GAN-236: an equal walk did not break on the lower station id.");

            // Inside grid:0:0, so one crossing from the first house and never orphaned.
            if (city.PrecinctOf(new TerritoryBlockId("nested:0")) != 0)
                failures.Add("GAN-236: a nested block is policed by nobody.");

            // Off the graph entirely: the nearest house by open ground takes it rather
            // than the city leaving a block lawless.
            if (city.PrecinctOf(new TerritoryBlockId("lonely:0")) != 1)
                failures.Add("GAN-236: a block off the block graph was left unpoliced.");
            if (city.Report.UnpolicedBlocks != 0)
                failures.Add("GAN-236: the report says a block is unpoliced when none is.");

            // Ground the map covers, walked twice: the same city handed its houses in the
            // other order is the same city.
            var reversed = Build(City(), new[] { House(1, 2, 2), House(0, 0, 0) });
            for (var i = 0; i < city.BlockIds.Count; i++)
                if (city.PrecinctOf(city.BlockIds[i]) != reversed.PrecinctOf(city.BlockIds[i]))
                {
                    failures.Add("GAN-236: the precinct map moved when the houses were " +
                                 "handed over in the other order.");
                    break;
                }

            // A house standing on no block of this city is a fault, not a claim.
            var stray = Build(City(), new[]
            {
                new TerritoryPrecinctSeat(0, new TerritoryPoint(5000f, 5000f)),
            });
            if (stray.Report.Passed)
                failures.Add("GAN-236: a station house on no block was accepted in silence.");
            if (stray.PrecinctOf(new TerritoryBlockId("grid:1:1")) != TerritoryPrecinctMap.NoPrecinct)
                failures.Add("GAN-236: a station house on no block policed a block anyway.");
        }

    }
}
