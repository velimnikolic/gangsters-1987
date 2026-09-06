# CoreSim — the core's layout dealt with no editor

Compiles `Assets/RoadDemo/CoreLayout.cs` and `CoreRoads.cs` against stub UnityEngine
types and deals the city core from seeds, the way `CoreLayout.Arrange` does at Play and
in `Tools/City/Core/Sketch The Core City`. Nothing is stood; the raster and its verdict
are the whole answer.

    cd Tools/CoreSim
    dotnet run -c Release -- --seed 1 --count 30          # thirty seeds: deals needed, faults, areas, roads
    dotnet run -c Release -- --seed 1 --count 30 --stats  # every deal of every seed, and what was wrong with the bad ones
    dotnet run -c Release -- --seed 4 --rows --map        # one seed drawn out: rows, report, raster
    dotnet run -c Release -- --seed 1 --deal 3 --map      # one particular deal of a seed, faults and all
    dotnet run -c Release -- --synty --map                # the demo's own arrangement (the reference)
    dotnet run -c Release -- --seed 1 --deal 0 --trace "NS w3 at -100"   # why each strip at x -100 was or was not a road

Exit code 0 means every seed asked for came out clean. Thirty seeds is the tally that
counts; one seed proves nothing.

`--powerlines --seed 1987 --count 5` exercises the production pole/wire generator
against crossed-road fixtures and generated city rasters. It checks pole clearance
from roads, driveways, parking and water, plus supported, bounded wire spans. These
are offline geometry checks using stub objects; they do not validate imported meshes
or the appearance of an already-loaded Unity scene.

`blocks.txt` is what the editor measured off the block prefabs (name, demo pivot, ground
box, size in cells, mask north row first). Refresh it when a block prefab is re-baked:
run `Tools/City/Core/Sketch The Core City` once, or the `eval` in
`Docs/core-district-plan.md` §2.7, and copy the dump here.
