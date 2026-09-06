Run the production parking-lane admission code and a passing traffic car offline:

    dotnet run --project Tools/ParkingLaneSim -c Release

Uses the existing RoadSim Unity stand-ins. Checks both road orientations and sides,
streets/boulevards, measured body widths, junction clearance, no-parking/narrow/
elevated/freeway/ramp rejection, occupied gaps, passing traffic and releasing a slot.
This does not instantiate prefabs or verify scene composition, rendering or Play.

CoreDemo and MiniCoreDemo configure the shared spawner through
`CoreDemoBuilder.parkedCarCount` (default maximum 60; zero disables it).
Cars are ambient stationary obstacles, registered for driving and walking, and
released with the scene. They are not campaign-owned vehicles.
They stay wholly inside the parking strip. Generated residential and park
frontages are eligible; authored blocks without a vehicle-access plan stay clear.
The startup tally reports placements and each rejection category, warning if the
requested count cannot be reached.

Offline verification on 2026-09-06: 385 assertions passed. Runtime and editor C#
compilation passed for source snapshot
`24fa21cda2b14ca8154f79054090b43d7257f4e6fcf429bda1bf26390848f13e`
(840 runtime files, 136 editor files; 99 and 32 warnings respectively).
Deleted-asset audit passed (zero deleted GUIDs). The repository-wide size check
reported existing over-budget classes outside this change; the changed files are
within budget and RoadDemoBuilder is unchanged.

Frontage verification (.NET 10, reusing CoreSim's production layout/raster models):

    dotnet run --project Tools/ParkingFrontageSim -c Release -- 30

Passed on 30 generated Core layouts: 2,085–4,126 eligible frontage candidates each
(minimum required: 60), plus fixtures rejecting unknown authored frontage, vehicle
drives, parking/yard/bare/water cells and out-of-raster spans. Seeds are 1987, then
`n * 7919` for `n = 1..29`. These are frontage-capacity counts before prefab fitting
and live obstacle checks, not observed scene spawn counts. This harness covers
CoreLayout/ResidentialLot, not CoreDistrict's amenity pass or MiniCore quarter cut.

The frozen Claude review identified missing frontage evidence and a props-only
clearance query. Repairs add the model checks above, refuse unknown access plans,
keep bodies off the pavement, and use `WalkObstacles.BlocksStanding` to query all
three static obstacle ledgers. Affected model checks and compilation passed after
the repairs; the repaired revision has not received a second external review.

Unity asset import, Player build, actual scene spawn counts, visual placement,
streamed block interaction and Play acceptance remain unverified. No Unity Editor
commands were issued for this task.
