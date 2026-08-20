# Motorcycles, and the men on them

Written 2026-08-20, after the first pass. Everything below is in the code and
compiles; the headless runs behind each claim are named at the end. Nobody has
looked at it in the editor yet, so **every number here is a measurement, not a
judgement** — the proportions are the ones to push about first.

## The one idea

There is no riding animation in the project and none was made. A rider's pose is
**derived**, exactly the way `CrewArms` derives a pistol in a fist: whatever clip
the body is already playing runs first, and then `BikePose` writes the four limbs
and the spine over the top of it in `LateUpdate`, reaching for points the bike
itself was measured for.

That is what makes it worth doing at all. A clip made for one machine is wrong on
every other machine; a derived pose fits the bike it is actually on, so the same
code seats a hood on a stolen moped and a patrolman on a police tourer. It also
sidesteps the thing that would otherwise have blocked the whole job: the men here
are animated by bare `PlayableGraph`s with no `AnimatorController` (`PedestrianAgent`,
`CarOccupant`), and Unity's humanoid IK pass needs a controller to hang off.
Writing bones in `LateUpdate` needs nothing.

The base clip still earns its place. It is the library's `Driving_Loop`
(`Assets/Animations/UAL1_Standard.fbx`, CC0, Quaternius) — made for a steering
wheel, and it does, because what is wanted from it is the one thing the pose does
not touch: a pelvis that is sitting down, and a man who goes on breathing while he
sits. `CrewKit.Ride` falls back to the bench sit if the take is ever missing.

## The pieces

| | |
|---|---|
| `BikeBody` | Reads a pack prefab: wheelbase, bars, grips, saddle, pegs, the pillion's saddle and pegs. Turns the bars and rolls the wheels. |
| `BikePose` | The rider. Hips to the saddle, fists to the grips, boots to the pegs, a foot down at the lights, a gun arm on a drive-by. |
| `BikeOccupant` | The dumb rider: a crowd body, a looping clip, a pose. `CarOccupant`'s twin. |
| `RoadBike` | A bike on the lane network. `RoadCar` underneath; the lean and the riders on top. |
| `StreetBikes` | Which bodies may be ridden, and where they go — riding, or stood on a stand. |
| `CrewBike` | The outfit's: a hood at the bars, his mate behind him with the gun. |

Plus a `PoseRide` in `PedestrianAgent`'s mixer, `CrewWalker.SetRiding(on, astride)`
so a crew man's legs are **not** folded away (a car seat hides them under the sill;
a saddle cannot), and `DemoCrews.AddBike` / `FireFrom` so a pillion's round is the
same round every other gun in the arena fires.

## Nothing is authored except proportions

Measured off the prefab: the wheelbase, the wheel radius, the bars (`HandleBars`,
or `Steering_Wheel` on the mopeds), the grips at the ends of the bar, the whole
footprint.

Authored, as statics on `BikeBody` so they can be pushed about during Play: how far
back of the bars the hips sit, how far below, where the pegs go, where the pillion
goes. Those are the same on every motorcycle ever built.

One of them was wrong in the first pass and is worth remembering: the pegs were
first set a hand's breadth above the **tyre**, which is how a real bike is built —
and the Synty city bike measures a 2.45 m wheelbase on wheels 1.1 m across, so the
peg landed level with the seat and folded the rider up like a jockey. The packs
draw a machine a size up to match the men who ride it. Peg height is now a share of
the **saddle** height (`PegHeightOfSaddle`), and peg width a share of the flank, not
the bar width — the widest thing on a bike is its handlebars, and a boot out at bar
width is a boot in mid-air.

What the two city machines actually measure:

```
SM_Veh_Motorbike_01  wheelbase 2.45  wheel r 0.57  grip (0.45, 1.20, 0.70)  saddle y 0.99  peg (0.28, 0.42, 0.40)
SM_Veh_Moped_01      wheelbase 1.49  wheel r 0.35  grip (0.33, 0.97, 0.59)  saddle y 0.76  peg (0.21, 0.32, 0.29)
```

`BikeBody` logs that line once per body, so a rider sitting wrong can be read off
the console before anybody opens a prefab.

## The lean

A bike holds a corner by falling into it: `atan(v * w / g)`, the pace against the
rate the nose is coming round, capped at 30 degrees and smoothed over about a fifth
of a second. The model hangs one level below the driving transform and rolls about
its own origin, which is the contact line (the packs stand a vehicle on y=0), so a
lean never lifts a tyre. Stood with nobody on it, it goes the other way onto its
stand.

The yaw is taken **from the transform**, not from the steer angle `RoadCar` hands
down. That angle is worked back out of the yaw with a car's 2.6 m wheelbase in it
(`RoadCar.Place`), and using it here with a motorcycle's 1.4 m put the radius at
half its true size and laid the bike on its ear in every gentle bend — the first
version did exactly that, and the table below is what caught it:

```
lean asked for (deg), by corner radius
  3.0 m/s: r8->7   r14->4   r25->2   r60->1
  5.5 m/s: r8->21  r14->12  r25->7   r60->3
  8.0 m/s: r8->30  r14->25  r25->15  r60->6
 12.0 m/s: r8->30  r14->30  r25->30  r60->14
```

## What a pillion may shoot

Not the rules a car's riders fire under. A man in a car may only shoot out of his
own window, within sixty degrees of abeam and on his own side (`DemoCrews.TickRiders`).
A pillion may shoot all the way round **except** through the man in front of him
(`CrewBike.PillionBlindArc`, 34 degrees off the nose) — which is the entire point of
putting a gun on a motorcycle. The rider takes a hand off the bar only when there is
nobody behind him to do it for him and the bike has slowed to walking pace.

## Where they come from

Never a folder scan. Every scan in the project denies `bike`, `moped` and `scooter`
by name (`RoadDemoBuilder`'s `vehicleDeny`) and that denial **stays** — for years a
two-wheeler in the traffic was a thing that slid along the road with nobody on it. A
machine reaches a street the way a marked cruiser does: by being asked for out of
`VehicleCatalog.Motorcycles`, by the code that also knows how to seat a man on it.
`E_Bike` and `E_Scooter` remain barred; they belong to another decade.

## Seeing it

- **CrewDemo** is the bench. `trafficBikes`, `parkedBikes`, `outfitBike` on the
  builder; **B** sends the outfit's machine at the first rival and calls it off
  again, or set `bikeAttackAfter` to have it go on its own for a run nobody is
  sitting at.
- **Game** puts `bikeCount` of them in the city traffic and about half as many again
  on their stands at the kerbs.

## What the runs showed

- `Tools/RoadSim -- bikes`: 20 narrow bodies among 60 cars, 300 s — 0 overlaps,
  0 belt hits, 0 stalls. A body a third the width of a car does not break the lane
  network.
- CrewDemo, 45 s headless: 0 exceptions, 0 belt hits, both riders seated the whole
  run. Two placement bugs came out of the first two runs and are fixed: the outfit's
  bike was parked inside the outfit's car (two builders both saying "the south kerb,
  about here"), and a traffic bike could land on a car because two spawn strides
  cross — `StreetBikes` now asks the road whether the spot is free.
- CrewDemo drive-by, 70 s: pulls out, goes Hot, runs passes with a turn-round at each
  end, and the pillion puts a machine pistol into named rivals from a moving bike.
- Game, 40 s: 0 exceptions. The one stall in that run (a vehicle stuck in a junction
  box with the belt shoving at it for the rest of the run) happens **identically in a
  no-bike baseline** with a traffic car in the same junction — it is the city's own,
  not the bikes'.

## Not done

- **Mounting and dismounting.** A man is put on the saddle and taken off it; there is
  no clip of a leg going over. The library's `Sitting_Enter`/`Exit` are car-shaped.
  Cheapest honest answer is to hide the moment, the way `PendingDrive` does.
- **Filtering between lanes.** A bike is narrow and takes a bike's room, but it queues
  like a car. Lane-splitting is the lane network's business, not a body measurement.
- **The law on two wheels.** `StreetBikes.PoliceBody()` hands back the liveried
  tourer, and nothing asks for it yet.
- **Nobody has looked at it.** All of the above is arithmetic and headless runs.
