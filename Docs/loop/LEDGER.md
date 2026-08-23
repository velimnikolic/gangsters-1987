# Ledger

One line an attempt: what was tried, and what the harness said about it.

| # | attempt | verdict |
|---|---|---|
| 1 | New freeway written from scratch (`FreewayKit`, `RoadDemoBuilder.Freeway.cs`, `TollPlaza`), old seam corridor left switched off | offline Roslyn build clean; harness proved by injecting a bad line (`error CS0029` reported) |
| 2 | `Docs/motorway.py`: the road's arithmetic asserted without Unity | 26 checks, 0 failures — deck 238..882 m, both decks carry 283..837, plaza at 560 |
| 3 | Link road narrowed to `StreetKit.RoadHalf` and parking turned off (a graph road wider than its own asphalt parks cars on the grass) | compiles clean |
| 4 | Toll arm starts lifting at 40% of the dwell rather than after payment (it was lifting as the car drove under it) | compiles clean |
