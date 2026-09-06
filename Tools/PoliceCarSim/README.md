# Police car lifecycle checks

Run the production patrol, road, parking and passenger code offline:

```sh
/Applications/Unity/Hub/Editor/6000.5.6f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet run --project Tools/PoliceCarSim -c Release
```

Twenty-seven patrol scenarios run in both headings at 30 Hz and with 0.2-second frames:

- Failed parking followed by a patrol deadline or dispatch release.
- A deadline during entry, and deadline/release during an actual reversing retreat.
- A later ordinary reverse after the abandoned retreat has been cleared.
- Autonomous rest/response retries after an angled parking failure, including a
  person blocking the retreat initially or interrupting it after reversing starts.
- New response orders during entry/retreat, and custody returning during retreat.
- Failed departures from a rest while patrolling without an active parking goal.
- Permanent rear blockers (walker and parked car) with bounded forward recovery,
  and failed parking while driving an actual overtaking curve.
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
or deadline boundaries. Ordinary retry tests require a completed park within 30 seconds,
and constrain random rest selection to the fixture's street. The initial blocked
retreat case starts at zero speed; an interrupted retreat uses the driven reverse.
Permanent-blocker and overtaking cases require resumed motion and a retained response
goal within 25 seconds, with no reverse, overlap, relocation, pose jump or abrupt
braking. They do not prove eventual arrival after taking another route through a city.
Carriage setup injects the already-seated stage. Unity
presentation, transforms, navigation and campaign services are stand-ins; these
checks do not validate rendered motion, pathfinding for the dismounted men, dispatch
scheduling, multi-wave custody, asset import or a Player build.

Validation on 2026-09-06 for the angled-retry fix: **108/108 patrol + 3/3 passenger
checks passed**. A frozen comparison using the same tests passed **76/108 before**
the fix; all 32 failed retry cases passed afterward. Shared RoadSim checks passed:
`kerbapproach` **44/44**, `kerbdeparture` **27/27**, `kerbcompletion` **5/5**, and
`recovery` **50/50**. The harness ran offline with .NET 10.0.11 using
`dotnet --roll-forward Major` on the built .NET 8 assemblies.

Offline runtime/editor compilation passed for source snapshot
`f24cced41335a20973099fa3753015fce551f24fc715c4a01b64fc73fa1fca98`
(840 runtime and 136 editor sources; 99/32 existing warnings). Asset-reference audit
found no deleted asset GUIDs; scoped whitespace checks passed. Changed source files
and the complete RoadCar partial class fit their size budgets. The repository-wide
size check still reports unrelated files/classes.

Verified runtime file SHA-256 hashes:

- `PolicePatrolCar.cs`: `46d07eee355532a019bc0405931b035df7c8dc34edd9bd127651b339e31d3788`
- `RoadCar.Parking.cs`: `76e490b64b8474e2d49fd71386a633209490bc068d57ab5a958f1bd8647ebcfb`
- `RoadCar.cs`: `3521982c1dbbcda877e28948d9b7b0cfcf13efdaf968c41d7bdbc83cd9fac4e7`

Adversarial follow-up confirmed the failed-retry deadline, manoeuvre/speed guards,
and permanent-blocker coverage. Its overall verdict remains **NEEDS ATTENTION**
for the pre-existing unbounded number of response attempts: a car that repeatedly
fails different attempts could keep a shots-fired squad waiting. That multi-cycle
dispatch scenario is not reproduced or verified by this harness, and this change
does not introduce an incident-abandonment policy or claim to resolve it.

No Unity Editor or Play checks were run. The photographed stall and live police
response/custody sequences still need in-game verification.
