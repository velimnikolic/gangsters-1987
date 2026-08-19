# RoadSim — headless sim for the road core

Runs `Assets/RoadDemo` car logic (LaneNet, RoadCar, DriverProfile, RoadSpace, DriverNerve)
against stub UnityEngine types, no editor needed:

    cd Tools/RoadSim
    dotnet run -c Release -- all        # every scenario
    dotnet run -c Release -- grid       # 100/60/120 cars on a 4x4 grid (signals / none / boulevard)
    dotnet run -c Release -- crew       # the crew demo ring: traffic + parked props + a gangster parking
    dotnet run -c Release -- block|crown|headon|wedged|uturn|standoff
    SEED=5 TRACE=1 TRACEID=12 dotnet run -c Release -- crew   # other seed, per-car trace

Every run reports body overlaps (must be 0), RoadSpace belt hits (must be 0), stalls,
frozen cars and average speed. Keep it at zero before touching RoadCar.
