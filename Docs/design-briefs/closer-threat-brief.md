# The closer threat: retargeting, reaction and bullet scatter

Design brief, written 2026-09-03 from the conversation that settled it and from the code as
built the same day. Linear: EPIC 33 / GAN-309 Done (label Combat, tickets `AIM-001..005` Done). It follows
[the cover brief](cover-and-ambush-brief.md) as part 2 of the combat track.

The ask, in the user's words: a shooter aiming at enemy A must turn onto enemy B when B has
come several metres closer than A, because B's shorter shot is the more immediate danger. His
Combat skill decides how big an advantage he needs and how fast he notices it. The same skill
must also shape where his missed rounds actually go, instead of being only an abstract
hit-chance multiplier.

## 1. The comparison, and what is deliberately not read

`DemoCrews.CloserThreatThan` asks one question: **is somebody else materially nearer to me,
right now, than the man I am aiming at?**

* Both distances are **horizontal street metres** (`FlatDistance`), current mark and candidate
  measured the same way. `BestMark`'s own nearest-man pick keeps its 3D measure, unchanged.
* No candidate's spawn point, starting position or travelled distance is read or stored. There
  is no movement history in the rule at all.
* A candidate must be alive, out on the street (`activeInHierarchy`), of the enemy unit being
  fought, not the current mark, and **visible to this shooter** through `InSight`.
* A runner (panicked or retreating) may not take the aim off a mark who is still fighting, and
  may take it off another runner only when nobody of them is still fighting. That is the old
  combat priority, unchanged.

**The one asymmetry, written down.** The current mark may be a man the shooter cannot see: an
ordered KILL is an address, and a crew closing on it round a block keeps the job
(`sighted: false`). A candidate may never be. A man may hold an address he cannot see; he may
only be pulled off it by somebody he can see.

## 2. The skill, as three numbers and one verdict

The whole policy is pure and lives in `CrewSkill`, so the offline suite drives it with the
editor shut and the live fight supplies nothing but geometry.

| Combat | `ThreatMargin` | `ThreatDwell` | `MissCone` |
|---|---|---|---|
| 1 star (2 half-steps) | 4.0 m | 0.90 s | 1.25x |
| 3 stars (6 half-steps) | 3.0 m | 0.55 s | 1.00x |
| 5 stars (10 half-steps) | 2.0 m | 0.25 s | 0.75x |

Half-stars are straight-lined between those three rows and nothing else. Off the scale in
either direction, `AttributeScale.Clamp` reads the nearest end.

`ShouldSwitch(currentDistXZ, candidateDistXZ, halfSteps, heldFor)` is true only when the
candidate beats the mark by the **whole** margin **and** the advantage has held for the whole
dwell.

**A dwell, not a poll.** The condition is measured continuously. The runtime keeps two fields
per shooter and no more - `CrewWalker.ThreatCandidate` and `ThreatHeldSince` - and a lapse of
a single frame, or a different candidate crossing the margin, puts the clock back to zero. The
same geometry therefore gives the same answer every run.

**Margin plus dwell is the hysteresis.** After A gives way to B, B is what the next candidate
has to beat by the whole margin all over again, so two men stood nearly level cannot flicker
the aim. `TheMarginCannotBeBeatenBothWays` proves this over the whole star scale and a grid
of distances rather than arguing about it.

Nothing is rolled. Skill moves a deterministic threshold, so an identical situation is always
explainable after the fact.

Metres and not a share of the gun's reach: the rule is about who is closing on the shooter, and
a shotgun does not make an enemy less close. The police and anonymous men have no Combat sheet
(`SpawnAt` leaves six half-steps) and fight at 3 stars by design, not by oversight.

## 3. The man he left

Ordered assignment still deals one shooter per valid enemy before duplicates
(`ClaimOrderedMark`, unchanged). A closer-threat switch is a personal survival override and may
duplicate a mark.

`DemoCrews.CoverTheUncovered` then keeps the fight honest. After the per-frame `_orderedMarks`
rebuild, if a valid enemy has nobody's gun on him, he is offered to the nearest duplicate
shooter, one reassignment per crew per frame. Two refusals, and they are the point of the pass:

* the man who switched to save himself is never the one moved back off the threat
  (`CrewWalker.SwitchedForThreat`), and
* a shooter is not moved onto a man his own closer-threat rule says is materially farther than
  somebody he can see - the same selector, asked with the uncovered man as the hypothetical
  mark.

## 4. Cover holds across the switch

A target change passes through `CrewWalker.Engage`, which clears the flank - and that is right
for a fresh fight, but wrong for a switch made at the moment the danger is nearest. So
`Engage(target, closerThreat: true)` keeps the spot he occupies when both of the ambush flank's
old conditions still hold:

* the shot from the spot is worth taking (3 m out to the gun's reach), and
* the thing he is behind still stands between the spot and the new man - `CoverStillShields`,
  sixty degrees either side of the direction the anchor sits in, off the anchor position the
  cover oracle recorded (`DemoCrews.LastCoverAnchorAt`).

Then `_coverSpot`, `InCover`, `_ducked`, the duck cycle, the route to a flank he is still
walking to and the fire cadence are all untouched: nothing about him changes but the man he is
pointing at. A spot that fails either test is dropped, and the ordinary recheck finds him the
next flank. Held ambush cover keeps on its own unchanged contract.

## 5. One resolved direction for every round

`DemoCrews.Resolve` used to point the flash at the man, then roll, then put the impact puff a
fixed metre or so to one side past him, and then check for bystanders down the **centreline**.
Three things about one round could disagree. Now:

1. `HitChance` (lifted out of `Resolve` whole, arithmetic unchanged) is still the only thing
   that decides a hit, and the roll is made **before** the flash.
2. A miss into the tin is decided next, also **before** the flash. A round that misses a man in
   a car or on a machine mostly enters the vehicle, and then *the tin is its direction*: the
   hole is picked first (`TinHole` / `MachineHole`, split out of the two `PutRoundInto...`
   methods), the flash is pointed at the hole, the impact is struck there, no scatter cone is
   applied at all, and no bystander check is asked because the round's path ends in that door.
   The man shooting up an empty car takes the same path.
3. Otherwise the round gets one scattered direction: the aim line turned inside
   `MissConeDegrees(weapon accuracy, CombatHalfSteps)`. Range is not fed in; distance widens a
   cone on its own, which is the whole point of using an angle.
4. That direction is used for the flash, for the impact (`MissImpact` - where the ray meets a
   building face via `WalkObstacles.ClearOfWalls`, or the pavement, or the end of its run), for
   the bystander check along the real ray out to where the round stopped, and for the trace.

There is no fourth case: every round leaves down a direction that was resolved before the
flash was lit, and that same direction is what damages, what is drawn, and what is traced.

The gun's own cone comes off its accuracy so a weapon added tomorrow needs no new number:
`4 + 10 x (1 - Accuracy)` degrees. Pistol 8.5, machine pistol 11, Tommy gun 10.5, rifle 5.2,
shotgun 4.3. A 1-star rifleman at 25 m therefore misses up to about 2.8 m wide and a 5-star
about 1.7 m.

**The cone is a real bound, not a nickname for one.** `CrewSkill.MissAngles` draws the miss as
a point in a disc - a radius bounded by the cone, then split into yaw and a pitch squashed to
`MissPitchShare` of it - rather than drawing yaw and pitch independently at their own maxima,
which put corner rounds up to 0.7 degrees outside the cone the table advertises and the trace
reports. `NoRoundLeavesTheCone` sweeps every gun, every half-step and the whole square of
rolls; `TheConeIsFilledToItsEdge` proves the sampler still reaches its own rim, so the bound
was not bought by quietly narrowing every shooter in the game.

**Civilian casualties from bad shooting are a feature.** A poor shot with an automatic hits
people down the street now, because the stray check follows the round that was actually fired.
The police weigh those bodies as they weigh any. The miss cone never feeds back into a second
chance to hit.

## 6. The yardstick

| What | Where |
|---|---|
| The pure core, offline | `unity command gangsters_aim_tests` - 18 contracts, and it prints the table the build actually read. Rides along with `gangsters_crew_audit_tests`. |
| The retarget, live | `k:"switch"` trace rows: `who`, `combat`, `left`, `onto`, `was`, `now`, `margin`, `dwell`, `held`, `kept`, `ordered`. |
| The man picked back up | `k:"uncovered"` rows. |
| Cover kept across a switch | `k:"threatcover"` rows, and the `kept` field on the switch row. |
| One path per round | `k:"shot"` rows gained `hit`, `tin`, `combat`, `cone`, `off` (degrees off the aim line) and `ray` (the resolved direction). The flash, the impact and the stray check all use `ray`. A `tin` round carries a zero cone and is left out of the scatter reading. |
| The soak reading | `analyze.py --crew` prints `the retarget` (switches, flanks kept, flickers) and `the scatter` (mean miss angle per star). A **flicker** - a man turning back onto a mark he held under two seconds ago - fails the run. |

Judged over a soak's thirty runs and never off one of them: the combat code draws from the
shared `Random`, so one seed proves nothing.

What the offline suite cannot judge, and the Play verdict must: that the aim line switches once
at the threshold rather than twitching, that the man in cover visibly stays in it, that miss
impacts follow the resolved shot direction, and that a 1-star rifleman at 25 m reads as wider
than a 5-star. Compile-only evidence is not a gameplay verdict.

## Out of scope, and stays out

A new Shooting or Marksmanship attribute; projectile physics, penetration, drop or leading a
moving target; switching from one enemy crew to another; weapon damage, cadence or range
rebalance; a weapon-relative threshold; a Combat sheet for the police; UI for assigning a
per-man priority target; any rewrite of cover beyond the one seam above, or of panic, surrender
or vehicle combat.
