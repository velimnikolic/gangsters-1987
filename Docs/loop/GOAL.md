# Goal

> *treba nam scena autoput koji spaja dva jako udaljena bloka, autoput treba da je uzdignut
> i da ima delove za ukljucenje iskljucenje sa istog, naplatne rampe pravi autoput*
>
> and, mid-build: *autoput ne valja, kreni iz nova* — *ako ne postoje putici za ukljucivanje
> kreiraj ih od postojecih*

A scene in which an **elevated** motorway is the road between two quarters that stand a long
way apart: you get onto it out of one quarter, ride a deck nine metres over open ground, pay
at a toll plaza, and come off it in the other. The ramps are built from the pieces the packs
already have — the PalmCity highway deck, pitched and turned.

**Not** the freeway the city used to roll. That one (`RoadDemoBuilder.Seams.cs` +
`.Interchange.cs`) rides a corridor over the whole grid and is switched off in the city by the
`NoFreeways` pass; it is left where it is and nothing here calls it.

## Where the behaviour lives

| part | file |
|---|---|
| the pieces and how they are laid (measured off the prefabs, not guessed) | `Assets/RoadDemo/FreewayKit.cs` |
| the road itself: decks, ramps, link roads, its own junctions | `Assets/RoadDemo/RoadDemoBuilder.Freeway.cs` |
| the toll gate, its arm, and what it flags | `Assets/RoadDemo/TollPlaza.cs` |
| whether a car may cross a line | `Assets/RoadDemo/RoadCar.cs` (`CanEnter`) |
| the rig | `Assets/FreewayDemo/FreewayDemoBuilder.cs` + `Assets/Scenes/FreewayDemo.unity` |

## The choke points

1. **`RoadDemoBuilder.BuildFreeway` / `WireFreeway`** — one pass lays the whole road and one
   call puts it into the lane graph, so the geometry and the graph cannot disagree: the same
   `across`/`along` frame, the same node boxes, the same stations. A route is a serialized
   field (`freewayRoute`); the scene sets it and the city builds it.
2. **`RoadNode.Toll` asked in `RoadCar.CanEnter`** — the single place a driver asks whether he
   may cross a line. One `if`, and every car in the city pays.
3. **`Carriageway.Elevated`** — the flag that makes the road observable: `RoadCar` writes a
   `deck` row when it joins or leaves one, and that is what a run is judged on.

## The road, as built

```
   west quarter                 600 m of open ground                 east quarter
        |                                                                  |
        |   ramp up ====== toll plaza (x 560) ==============  ramp down    |
        +--- link ---o------o----------------------------o------ link -----+
                   foot   gore                          gore   foot
```

- two one-way decks (11.4 m each) side by side on piers, **level from end to end** at 9 m;
- at each interchange a ramp down off one deck and a ramp up onto the other, climbing 9 m
  over 160 m (one in eighteen), tapering from 30 m off the line to 17 m as it climbs;
- a **link road** out of the quarter's own last junction, under the deck, to the far foot:
  what makes the whole thing a road rather than a wall with cars on top;
- a **barrier plaza** on the mainline half way along: an apron outboard of each deck, a booth
  on it, and a boom over each lane that lifts when the driver has paid.

## How it is measured

New rows in the trace, read by `python3 Tools/play/analyze.py <run> --freeway`:

| row | written by | fields |
|---|---|---|
| `deck` | `RoadCar.TraceStep`, on joining/leaving an elevated carriageway | `id`, `what` = `on`/`off`, `v`, `p` |
| `toll` | `TollGate`, when a car is let through | `id`, `gate`, `wait`, `q` |

New `fault` kinds: `tollrun` (crossed the barrier with the arm down), `tollstuck` (stood at
the window over 30 s).

## Conditions (a script checks every one)

1. `python3 Docs/motorway.py` — the road's arithmetic: **26 checks, 0 failures**.
2. The compile is clean (`error CS` count 0), offline and in the editor.
3. `analyze.py --freeway` on a 480 s run of `Assets/Scenes/FreewayDemo.unity`:
   - `tollrun` faults: **0**; `tollstuck` faults: **0**;
   - `carstuck` / `nopark`: **0**; belt refusals: **0**; exceptions: **0**;
   - worst ordinary car stood still: **< 90 s**;
   - at least one **crossing**: a car that joined the deck and left it 300 m or more away;
   - at least one **payment**;
   - **both** ways on used (joins bucketed at 200 m along the line give two places).
4. `Tools/play/soak.sh --runs 30 --freeway`: 30 of 30 passed.
