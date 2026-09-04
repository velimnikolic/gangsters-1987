# Cover first, and the ambush

Design brief, written 2026-09-02 from the conversation that settled it. Linear: EPIC 28 (label Combat, tickets `COVER-`).

Three asks, in the user's words:

1. "kad crew napada treba prvo da stane u zaklon pa zapuca, a ne zapuca i onda traži zaklon" — a crew that attacks gets behind something first and fires from there; it does not fire and then look for cover.
2. "da kliknem na props i da se sakriju iza propsa kao sačekuša: jedan lik se sakrije na taj props a ovi ostali potraže u blizini" — a right click on a piece of street furniture is an ambush order: one man takes that prop, the rest find cover near it, and they lie in wait.
3. "možeš da se kriješ iza tuđih auta" — other people's cars are cover.

## 1. What exists and is reused

| Thing | Where | Used for |
|---|---|---|
| The one cover oracle | `DemoCrews.CoverNear` (`DemoCrews.cs` ~907), wired as `CrewWalker.FindCover` | cars stood still (`StreetTraffic.Users`, `RoadSpeed <= 0.5`) and pavement furniture (`WalkObstacles.PropsNear` → `SidewalkPlan.SolidNear`) compete in one `bestD`; flank = the box face pointing away from the shooter; `Claimed` keeps two men off one flank; the canopy berth keeps a man out of a tree |
| The fight tick | `CrewWalker.TickEngage` (`CrewWalker.cs` ~2705) | the cover look, the walk to the flank (silent: the approach branch returns before the fire block), the duck/pop-up cycle, the closing shot, the squared-up shot |
| Size policy | `PropCoverMinHalf` 0.22 / `PropCoverMaxHalf` 3, `Box.Tall` refused | which boxes are cover; a prop has no identity, only a footprint |
| Cover payoff | `DemoCrews.Resolve` (`DemoCrews.Combat.cs` ~908): `BehindCover` 0.8, `DuckedCover` 0.45 | what a flank is worth against a round |
| Ordered fight | `DemoCrews.OrderAttack` → `SetTarget(ordered: true)` → `CrewWalker.Engage` per man | the KILL; `OrderedFight` reaches across the quarter |
| Picked-up fight | `TickCombat`: rivals `EnemyWithin(AlertRange 24)`, the outfit fights back inside `FightBack` 6 s of `ProvokedAt` | who starts shooting on their own |
| Sight | `InSight` = `WalkObstacles.Sees` (walls only, `EyeLevel` 2.2) | a bin is cover, not a hiding place — today |
| The posted man | `CrewWalker.WatchToward` / `Watching`, `WatchLease` 120 s; `TickCohesion` skips a watching man | the one existing "stand here, face that way, the tether leaves you alone" state |
| Concealed guns | `WantsGunOut` (Engaging, Fleeing, Alert, RidingAim, shouting), `ArmsQuiet` 8 s | when the piece is in his hand |
| Right-click orders | `CrewOverlay.ReadRightClick`: `PickCarAt` (the outfit's `Cars` only), `PickAt` (men), `FrontAt`, `BusinessAt`, then the ground plane | where the prop click goes in |
| Run rule | double right click → `run`; closing on a fight; fleeing | the only three runs there are |
| Indicators | `CombatIntentOverlay` (I key; DemoCrews owns one in every scene), `CoverWatch` in CoverDemo | cyan boxes, amber walk-to-cover, green up, blue down, red in the open |
| The bench | `Assets/Scenes/CoverDemo.unity`, `CoverDemoBuilder`, `BlockDemoMission` for headless | one street, many props, rivals on alternate pavements |
| The yardstick | `k:"cover"` trace rows, `CrewAudit` fault rules, `Tools/play/soak.sh`, `analyze.py --crew` | rates over 30 seeds, never seed identity (the cover code draws from the shared `Random`) |

## 2. What is wrong today, exactly

The cover look already runs on the first tick of an engagement — but it can only find a flank from which the mark is **inside the gun's range** (`toTarget <= man.Ballistics.Range`, pistol 10 m), and it only looks **within `min(CoverReach 10, dist × 0.9)` of the man himself**. A crew sent at a rival forty metres off therefore asks the street, is told "nothing", falls through into the `closing` branch and walks in **firing on the move** (the closing shot). Two to three seconds later the recheck (`_coverRecheckAt`) fires again, by then he is inside range, the bin beside him qualifies, and off he goes to it. That is the order the user sees: fire, then cover.

Second: a man whose held flank has fallen out of range (the mark backed off) drops the spot (`coverShot.magnitude > range` → `_coverSpot = null`) and closes in the open again. There is no "next flank".

Third: nothing lets the player choose the cover. A right click on a prop is a walk order to the ground under it; a right click on a stood civilian car is the same walk (`PickCarAt` only knows the outfit's own `Cars`).

Other people's cars are already cover for the **automatic** search: `CoverNear` takes every `IRoadUser` stood still — parked `StoodCar`s, traffic held at a light, rival crews' cars. Ask 3 is therefore mostly about the click, plus one rule about cars that drive off (below).

## 3. Rule 1 — a flank before the first round

* **`CoverToward(man, target)`** — the same oracle, searched from a different centre: the point on the man→mark line at `range × RangeFactor` (0.8) from the mark, with reach `max(6, range × 0.6)`. A flank qualifies as today (face away from the mark, stood off by a shoulder, canopy berth, `Claimed`, reachable) **and** has the mark inside `[3, range]`. Prefer the least walk from the man; break ties toward the mark. Same size policy, same two sources, no third one.
* `TickEngage` asks `CoverToward` **before** the closing branch. With a spot in hand the closing branch is skipped altogether: he walks (or runs, `RunToCover` 5 m rule unchanged) to the flank, arrives low, pops up, and the first round leaves from there. The approach stays silent on purpose — that is the ask.
* **The closing shot survives only as the fallback**: no flank anywhere on the fire line (an empty road, a park) and he closes in the open exactly as today, firing from the hurried walk.
* Point blank: a mark inside `PointBlank` 4 m is shot, not hidden from. A man does not run past his enemy to reach a bin.
* Shared class, so every crew gets it: the outfit, the rivals, the police squads that come through `AddRival`. No scene fork.

## 4. Rule 1b — leapfrog, never the open

* A man behind a flank whose mark has walked out of range asks `CoverToward` for the **next** flank he can shoot from, keeps the old one through one empty poll, and goes in on the second. **Revised 2026-09-04 (the user's word):** a flank is only ever one he can shoot from - never "local protection" out of range - and KILL with nothing to get behind is a charge.
* **A fight that came to him (2026-09-04, the user's word: "kad smo napadnuti treba da pucamo i nadjemo zaklon sto pre").** A man who was not SENT to his fight (`Unit.OrderedFight` false: shot at, a fight picked up) takes the nearest flank round him whatever the range - a firing one first, a merely shielding one second - and stays behind it while the mark is beyond his gun. Only KILL/Sic men leave a shield to close the range.
* **The pistol waits on the rifles (2026-09-04, the user's word: "lik s pistoljem ne treba da se zalece kao debil ... da ceka da poginu ovi sto imaju puske pa onda").** A man sent to a fight but outranged by a crewmate still standing (`CrewWalker.FightPart.Waits`) takes the nearest flank round him and waits; with nothing to get behind he holds his ground, gun up. He closes once the longer guns are down.
* **Nothing until the shooting starts (2026-09-04, the user's word: "treba da traze zaklon kad krene pucacina").** A crew sent to a fight asks for no cover, and every man of it closes, until its fight is HOT - a round fired by it or at it (the law's included) in the fight it is in now (`Unit.HotFight == TargetUnit`, set by `HeatFight`, never cooled by a lull, cold again on a new target). Then the parts above apply, inside range as well as out.
* **Running for cover is a sprint (2026-09-04).** The flat-out clip, to within `RunToCover` 2.5 m of the flank; the crouch-walk covers the last `CrouchWithin` 2 m under fire.
* **Men coming with their guns out are a provocation (2026-09-04).** A crew with an ordered fight on this one, a man of it on foot and armed, in sight inside `SightRange`, provokes it (`EnemyComing`) - so a rival sees the outfit coming as far as the outfit sees it, not first at the first round.
* **HOLD is the hide order (2026-09-04).** A man on the flank the player put him on (`HeldCover`) never leaves it for range: a mark beyond his gun he waits for, down, and only a breached flank moves him. The player's KILL takes him off it (`LeaveHeldCover`) - the hide order is the hold, the kill order is the charge.
* The rechecks stay on the 2–3 s throttle; the "mark walked 4 m off `_coverFrom`" re-ask stays.

## 5. Rule 2 — the ambush click (sačekuša)

**The pick.** In `ReadRightClick`, after cars, men, fronts and shops and before the ground: the ground-plane point is tested against

* the prop boxes near it (`WalkObstacles.PropsNear(world, 1.5)`): a `Solid`, not `Tall` box that passes the cover size policy and whose footprint contains the point, and
* stood cars (`StreetTraffic.Users` with `RoadSpeed <= 0.5`) whose footprint contains the point. **NPC parked cars are first-class anchors** (user, 2026-09-02: "ne samo rivalskog, no NPC parkiranog auta, imaćemo i njih na mapi"): the kerbside `StoodCar`s `KerbCars` lays, the forecourt cars of `ForecourtSet`, and a `RoadCar` that has parked at the kerb (`RoadCar.Parked`) or is held at a light — all of them one click, no card. The outfit's own cars keep their board/assign meaning; a rival crew's car keeps its bomb card and gains a **HIDE BEHIND IT** row, because that card is already open on it.

A hit is `OrderAmbush(Selected, anchor, run)`. Single click walks, the double click runs, as everywhere.

**The order.** `DemoCrews.OrderAmbush(Unit, CoverAnchor, run)`:

* The **threat direction** decides which face is the safe one: the nearest rival man in sight (a fight already on, or one visible), else the nearest carriageway lane point (enemies come down the street, so the men hide on the pavement side of the bin and watch the road), else the reverse of the crew's own approach.
* **One man takes the clicked anchor** — the nearest man to it (least walk). The rest are dealt flanks by the ordinary oracle searched **around the anchor** within `AmbushSpread` 10 m against the same threat direction, `Claimed` apart. A man the street has no flank for crouches beside the nearest taken flank on its safe side; nobody is left standing in the open.
* The order clears the current fight the way a walk order does; a crew already fighting that is clicked onto a prop treats it as **manual cover**: the same order with the live mark as the threat, and the fight goes on from the new flanks.

**Lying in wait** — a held flank, on `CrewWalker`:

* `HeldCover` (Vector3?) + `Lurking`. At the spot: gun drawn (`WantsGunOut` includes `Lurking`, the 8 s holster never runs), crouched (`VisibleCrouchPose`), turned toward the threat (`WatchToward`). Silent. `TickCohesion` skips him as it skips a watching man.
* `Engage(target)` **keeps `HeldCover`** as `_coverSpot` when the mark is inside range from it; otherwise rule 1b takes over (next flank toward him). Today `Engage` nulls the spot on a new target — that is the one seam that must change.
* `AmbushLease` 240 s: an ambush nobody sprang ends with the men standing up and rejoining the crew, the way a door post ends. Any fresh order ends it at once.
* **The ambush opens fire on its own.** The outfit starts nothing today (`TickCombat`); a lurking crew is the exception the player asked for: a rival family's man (never the law, never a civilian) inside the crew's best gun range and in sight → `SetTarget(unit, rival, ordered: true)`. First rounds leave from the held flanks.
* **Surprise**: a lurking, ducked man is not seen by `EnemyWithin` / `Spotted` / `SeenBy` beyond `LurkSeen` 8 m until his crew fires. After the first round the ordinary sight rules apply. This is the only change to sight and it is a range, not a raycast.

**Marks and indicators**: the walk mark on the clicked prop; a `CombatIntentOverlay` colour for a held flank (waiting); the man's status line reads "Lying in wait behind …"; the crew bar shows the state the way it shows a posted man.

## 6. Rule 3 — other people's cars

* Already cover for the automatic search (cars stood still, whoever's): NPC kerb parkers (`KerbCars`), forecourt cars (`ForecourtSet`), traffic parked or held at a light (`RoadCar.Parked`, a red), rival crews' cars. The click (rule 2) adds every one of them as an anchor — NPC parked cars are the common case on the map, not the rival's car.
* A car that drives off takes the flank with it: the held spot is dropped when the anchor's `RoadSpeed` rises past 0.5 or the user vanishes from `StreetTraffic.Users`; the man re-asks the oracle around where he stands (a fight on) or around the ambush anchor (lying in wait). No man stands in the road guarding the space where a car was.
* A car with men in it is somebody's cover and somebody's target at once; the rule stays what `Resolve` says today (`CarCover` for the men inside, `BehindCover` for the man on its flank).

## 7. Rules that must hold

* **One oracle.** `CoverToward` and the ambush deal are the same search from a different centre; no second cover source, no prop list. The size policy stays a size policy.
* **A running man never fires** and **a crew runs only on the double click, closing, fleeing** — an ambush walk is a walk unless double clicked; the run to a flank inside a fight stays the `RunToCover` rule.
* Anything that chooses where a man stands asks `Occupied` with the canopy berth.
* The concealed-gun split stays: `Carrying` to pick a man, `Armed` to shoot with him.
* Behaviour in the shared classes (`CrewWalker`, `DemoCrews`, `CrewOverlay`); CoverDemo and the city only configure.
* Rates over 30 seeds, never per-seed A/B.

## 8. Out of scope

Peeking round a corner or over a bonnet as a pose (there is no crouch-aim clip); prop height in the hit model (a knee-high planter still gives full cover odds — accepted before, unchanged); civilians hiding (they keep `Cower`/`TryHide`); ambushes on the police; a formation editor (which man takes which prop by hand).

## 9. Acceptance

* CoverDemo, KILL from 40 m: the trace's first `shot` row of every man comes after his first `cover` row with `found:true` whenever the street had a flank on his fire line; no `closing` shot before it. Over 30 runs the share of first rounds fired from cover is reported and is the number tuned.
* CoverDemo, right click on a bin with rivals two streets off: one man at the bin, the rest behind furniture within 10 m, all crouched, guns out, facing the road; a rival crew walking up is fired on from cover before it fires back; the same click with the crew already fighting moves the fight onto the clicked flanks.
* Right click on a parked civilian car: the same; the car pulling away drops the men onto the next flank, not into the road.
* `CrewAudit` gains `openfire` (a round from the open while an unclaimed reachable flank existed on the fire line) and the soak stays green; every fault it raises gets fixed.
* `recompile_status --json` clean; `code-review-unity` before commit.

## 10. Tickets

* COVER-001 — `CoverToward`: the flank on the fire line, asked before the closing shot; the closing shot becomes the fallback; point blank rule
* COVER-002 — Leapfrog: the next firing flank toward a mark out of range; a charge when the street has nothing. Never on a held flank (HOLD).
* COVER-003 — The ambush pick: props and stood cars under the right click, `OrderAmbush`, one man at the anchor and the rest around it, the threat direction
* COVER-004 — Lying in wait: `HeldCover`/`Lurking` on the walker, `Engage` keeps the held flank, the lease, the self-started fight, the surprise sight range
* COVER-005 — Cars that leave: the anchor watch on `RoadSpeed`, re-ask around the anchor; HIDE BEHIND IT on the rival car card
* COVER-006 — Marks, overlay colour, status line; `openfire` audit rule, `first-from-cover` in the cover trace, a CoverDemo ambush scenario, the 30-run tally
