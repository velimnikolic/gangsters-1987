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
    SEED=5 TRACE=1 TRACEID=12 dotnet run -c Release -- crew   # other seed, per-car trace

Every run reports body overlaps (must be 0), RoadSpace belt hits (must be 0), stalls,
frozen cars and average speed. Keep it at zero before touching RoadCar.

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
