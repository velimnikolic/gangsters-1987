# RoadSim — headless sim for the road core

Runs `Assets/RoadDemo` car logic (LaneNet, RoadCar, DriverProfile, RoadSpace, DriverNerve)
against stub UnityEngine types, no editor needed:

Requires .NET 8 or newer. Source references are relative to this checkout. On a Mac
with Unity 6000.5.6f1, its bundled SDK can run the harness without a separate SDK:

    /Applications/Unity/Hub/Editor/6000.5.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet run --project Tools/RoadSim -c Release -- kerbapproach

    cd Tools/RoadSim
    dotnet run -c Release -- all        # every scenario
    dotnet run -c Release -- grid       # 100/60/120 cars on a 4x4 grid (signals / none / boulevard)
    dotnet run -c Release -- crew       # the crew demo ring: traffic + parked props + a gangster parking
    dotnet run -c Release -- block|crown|headon|wedged|uturn|standoff
    dotnet run -c Release -- turnround  # the turn in the road: on this street, and for a mark one street back
    dotnet run -c Release -- crab       # heading vs the rear axle's motion: boxes, slides, pull-ins (must stay ~1 deg)
    dotnet run -c Release -- kerbapproach # park just past a parked car, both headings, widths/sizes and starts
    dotnet run -c Release -- kerbdeparture # full bodies, room to leave, competing slots and late occupancy
    dotnet run -c Release -- junctionpace # clear straight crossings retain cruise; red still stops the car
    dotnet run -c Release -- trafficadmission # missed parking entries, temporary claims, precise junction envelopes
    dotnet run -c Release -- trafficescape # pair-only gridlock escape, exclusions and lifecycle
    SEED=5 TRACE=1 TRACEID=12 dotnet run -c Release -- crew   # other seed, per-car trace

Soaks distinguish unauthorized body overlaps (must be 0) from `permittedPairSamples`:
samples in which an explicitly leased traffic pair overlaps at walking speed.
Parking tests still require zero overlaps, without exceptions. Belt hits, stalls,
frozen cars and average speed remain separate diagnostics; an intentionally injected
deadlock must first exercise the normal guard before its escape may activate.

`kerbapproach` includes 32 approaches immediately beyond a parked car, plus 12
transition cases: leaving a clear kerb, returning to a goal behind on the same
heading, and a controlled obstacle taking the destination during a pull-in.
Forward approaches have a 30-second deadline; the return journey allows 60 seconds.
Each of the 32 approaches then drives the neighbour out past the newly parked car,
back into its lane within 25 seconds, with the same swept-body and no-jump checks.
Success requires the requested kerb and heading near the original target. A taken
destination permits another clear spot within 12 metres. The harness sweeps every
simulation substep for body overlap and checks rear-axle and heading continuity,
as well as rejecting traffic recovery relocations. It includes accelerated frames.
These checks verify shared driving behavior, not rendered motion.

`kerbdeparture` also interrupts four exits with a late blocker: the car must
reverse along its actual curve, find another clear departure and regain the lane.

For a focused diagnostic use `CASE='5/1/False/sedan/0.200' TRACE=1` (road half-width,
heading, parked start, body, frame step) or `CASE='destination taken during pull-in/1/0.200'`.
An unmatched filter fails rather than reporting a zero-case success.

`trafficadmission` covers empty kerbs whose entry was missed near a road end,
temporary reservations, failed orders continuing in traffic, despawned claims,
opposing straight crossings (including wide vehicles), a transitive priority order,
dense body-envelope checks for turns, and cache size/rebuild invalidation. A short
return to a missed entry uses an admitted, continuously checked straight reverse.

`trafficescape` requires reciprocal blocking evidence and no meaningful progress
for six simulation seconds. One vehicle may pass its specific peer at at most
1 m/s, with an initially clear complete escape path. After 20 seconds or a new order,
the lease ends immediately if the pair is clear. Existing contact instead enters a
finishing phase: the other car may take over on a checked clear path if a late
obstacle sealed the first exit. The commanded car waits with its new order intact.
The pair's contact ownership ends after separation; a removed car releases it immediately.
If neither exit is clear, both wait and recheck without bypassing a third party.
Admission excludes
parked/disabled/halted vehicles, parking manoeuvres, same-direction queues, and
intentional roadblocks. A third vehicle or person is never exempt. Tests cover
simulation order, accelerated frames, persistent late vehicles/people and orders or
halts issued after the bodies have already begun overlapping.
`junctionreverse` reports permitted pair samples separately from illegal overlaps;
its progress and no-jump requirements remain enforced.

These are offline source/model checks, not Unity import, scene, UI-layout or Play
acceptance. The user controls Editor and visual validation.
