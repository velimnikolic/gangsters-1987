# Police car lifecycle checks

Run the production patrol, road, parking and passenger code offline:

```sh
/Applications/Unity/Hub/Editor/6000.5.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet run --project Tools/PoliceCarSim -c Release
```

Sixteen patrol scenarios run in both headings at 30 Hz and with 0.2-second frames:

- Failed parking followed by a patrol deadline or dispatch release.
- A deadline during entry, and deadline/release during an actual reversing retreat.
- A later ordinary reverse after the abandoned retreat has been cleared.
- Rest ending, a stop deferred through a real junction, and cancellation while
  another car blocks the parked patrol's exit.
- Custody retaining its return route and reservation.
- Disabled transfers braking safely despite release, including a release after retirement.
- Engines disabled by actual bonnet hits, wrecks and halted transfers disappearing
  after 30 simulated seconds. New route/release requests cannot reset that deadline.
- Healthy kerb rests surviving the same deadline.

Resumed cars must move, regain their lane, release old goals and kerb claims, and
preserve rear-axle/heading continuity without traffic recovery or deletion. Removal
checks require road occupancy, fleet and swing leases, map/dispatch transforms and
callbacks to be released. The owner's repeated later ticks must be harmless.

Three additional checks link the complete production `PrisonerCarriage`:

- Unseat one carrier from a mixed passenger list before destruction, retaining
  passenger identity, surrender, parent, scale and renderer state. Another carrier
  keeps its passengers seated and can unload normally later.
- A loaded riding transfer waits for its carrier to stop before dismounting under
  the same escort.
- The same dismount contract holds when the engine is lost during boarding after
  the prisoner has already been seated.

Failed-entry/retreat tests drive the real parking curve, then inject private failure
or deadline boundaries. Carriage setup injects the already-seated stage. Unity
presentation, transforms, navigation and campaign services are stand-ins; these
checks do not validate rendered motion, pathfinding for the dismounted men, dispatch
scheduling, multi-wave custody, asset import or a Player build.

Validation on 2026-09-06: **64/64 patrol + 3/3 passenger checks passed**. The shared
RoadSim `recovery` suite also passed **50/50**. Offline runtime/editor compilation
passed for source snapshot
`6926b21c15dd4217f7d60c4bf6e4e0857255e5f8058b3b30a4844ea4231c3791`
(836 runtime and 136 editor sources; 99/32 existing warnings). Asset-reference audit
and scoped whitespace checks passed. The changed source files and whole partial
classes fit their size budgets; the repository-wide size check still reports
unrelated concurrent changes.

An earlier frozen comparison of the original stall fix passed 20/40 patrol cases
before the fix and 40/40 after it. A separate kerb-approach snapshot passed 30/44
with and without that fix with identical output; it is not a global parking pass.

No Unity Editor or Play checks were run. The photographed stall and live police
response/custody sequences still need in-game verification.
