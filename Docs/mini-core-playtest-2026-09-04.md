# MiniCore live player tests — 2026-09-04







In progress. Acceptance requires five successful live repetitions per scenario.

Traffic recovery policy update, 2026-09-05: the user explicitly permits the game
itself to cheat modestly when resolving traffic jams, especially under fog of war
("igra moze malo i da vara ... pogotovo ispod fog of war gde se ni ne vidi"). This
supersedes the earlier prohibition on production traffic relocation. Live player
harnesses still cannot grant funds, force legal/combat outcomes, teleport crews,
or advance the calendar. Current build 85 retains each car, destination, owner and
passengers while repairing a stall after 45 simulated seconds (retry every 5s).
Visible recovery may move up to 32m; concealed recovery up to 96m. Both complete
vehicle footprints must be hidden for the longer move. Landings must be empty,
and visible translation/rotation is checked against pedestrians. Recovery uses
only the current road or its chosen connector; parked cars, explicit halts,
wrecks, off-road parking, elevated roads and curves are excluded.

Final traffic trials: five distinct seeds each under full visibility, full fog
and mixed fog finished their three grids with zero overlaps and zero vehicles
still frozen for over 60s. Raw legacy grid exit codes still flag collision-guard
refusals; these are not claimed as a completely green legacy stress suite.
Evidence: Temp/recovery81-visible32, Temp/recovery81-final/results.json and
Temp/traffic-checks81/results.json. The latter also contains the recovery safety
and original-destination arrival checks.

Build 85 compiled cleanly and all eight native groups passed at 22:13:59, including
93 MiniCore scenarios. It also fixes routine prisoner drives expiring while their
cars are still moving: 2m net progress renews the 300s idle allowance, with an 1800s
absolute drive ceiling. Five isolated repetitions cover moving/stopped/jittering
carriers, the ceiling, renewed travel after roadblocks, and retained seated bodies.
Evidence: Temp/regression-checks-build85.json. Runtime SHA256:
ac668506dc2df3cf2ac6e80d9ff237d00443e2b27b4ff6973062a11cc0fe762a.
The loaded court flow completed one full live PASS on build 85:
Temp/player-trials/20260905-221445-court-loaded/1. Actual court and prison rides,
12-day sentence, natural day-17 release, cleared custody, and physical player movement
completed in 19231.6 simulated seconds. All active crew and bag bodies were present.
There were no traffic failures and zero actual obstacle-crossing writes across
999129 checked walking segments. Three coarse chord alerts resolve to safe intermediate
movement writes in walker-crossings.jsonl. The campaign stopped at the completed-attempt
boundary to install the independently reproduced block-card ownership fix; this is
one pass, not five. No natural seated drive over 300s occurred in this attempt.

Build 86 preparation: the live block provider exposed two rival collectors' money
as player round data (Temp/foreign-round-view-live85.json). The isolated baseline
Temp/block-round-baseline85.json reproduces all four ownership assertions failing
in each of five repetitions. The fix filters only by player house, preserving real
rival rounds and any own round whose crew responsibility subsequently changes.
Build 86 compiled cleanly. All eight native groups passed at 22:50:15, including
94 MiniCore scenarios (Temp/regression-checks-build86.json). The identical isolated
probe passes all 20 ownership assertions in Temp/block-round-candidate86.json.
Runtime SHA256: 418a756c574b510283cfb090125fa90de96b1caa79683d4cd908a041d5f54894.
Fresh live block_round_view completed 5/5 PASS in
Temp/player-trials/20260905-225024-block_round_view. The first four observed a real
rival collector carrying 8, with player RoundOut false and carried/in-bag zero.
In repetition five the original rival lieutenant died; another crew (1002) sent
collector 100005, who physically collected 14. All five have zero actual/coarse
obstacle alerts, no traffic failures and clean console readings. The batch retains
one template (4cab421e2fed prefix); later observation-only template additions apply
to future batches, not these five attempts. The live death observer also checked
five known nondefensive officer killers across crews 1000, 2000, 4000 and 6000 with
no charge-attribution assertion failures. This observes AI combat; it does not
replace the separate player fight/victory acceptance scenarios.

Additional observation for subsequent campaigns: convoy samples include road endpoints,
the parking goal/heading, elapsed parking search and the response kerb reservation.
AI round endings and individual stop settlements have separate logs. The completed
build-85 court AI trace contains 167 CollectDues intents for house 5 over days 3–16,
far more than the other houses. That is an investigation lead, not yet proof of
repeated failed payments: its earlier memory only serialized player rounds.

Build 84 compiled cleanly; all eight native groups passed at 21:59:05, including
92 MiniCore scenarios. The five broad-prop stance reproductions now all issue clear,
reachable orders and physically reach question distance. Evidence:
Temp/regression-checks-build84.json, Temp/broad-police-stance-candidate84.json.
Runtime SHA256: a217ca9be73b7ceca98bf25308204d800d334bedf51f0c10dcbcfff967cc8fb6.
The initial build 84 physical court/prison/release replay was stopped as a diagnostic;
no full release pass is claimed there. The earlier live AI retry loop still needs a complete
reproduction before this geometry fix can be identified as its cause.

Build 83 compiled cleanly and all eight native Unity groups passed at
2026-09-05 20:51:48, including 91 MiniCore scenarios. The build 82 actual roster-Sync
fixture preserves the original free collector and escort through their leader's
booking, five dealt worlds and three synchronizations each. Build 83 adds five
repetitions each of scoped police charges/wanted grades and death attribution.
The identical baseline probe now passes all 15 assertions after reproducing ten
failures before the patch (Temp/swarm-attribution-candidate83.json).

Five fresh grenade flows passed build 82 (Temp/player-trials/20260905-204129-grenade).
Paid collection with a held leader completed 5/5 live passes on build 83 in
Temp/player-trials/20260905-213241-collection_held_backup-loaded. Each loads the
unmodified real booking, promotes the naturally acquitted hood, restores a racket
through a physical threat visit, and sends the original jailed leader's collector.
The first four physically banked 10 each and the fifth banked 14, with exact ledger
reconciliation, no traffic failures and no recorded obstacle crossings. Earlier failed and
unexercised attempts remain in the evidence. An intermittent AI police WalkingUp
retry loop remains under investigation, without a claimed production fix.
Report: Temp/regression-checks-build83.json. Runtime SHA-256:
484daa257cf2b364282c660c6ead3ce0bb8425cfd08850432534384455190fb3.

Build 79 automatic collection 20260905-190949/1: FAIL, no acceptance credit.
The collector physically deposited 85 at t=8923.73, but a later independent live
Ended observer confirmed Origin=Player for an automatic round at t=19025.752.
TendPendingBagRounds preserved ScheduledDay but lost submittingOrigin when the
bag detail had to leave HQ. Candidate 80 stores/restores the original origin
through the deferred doorway command. The live harness was ended through its own
Finish/cleanup callback at t=20198 after this concrete failure was captured.
Evidence: Temp/auto79-origin.jsonl and the original run folder/result.json.

Build 80 native initial report preserved in Temp/regression-checks-build80-initial.json:
new recovery/three-rider custody fixtures passed all five rotations, but seven
pre-existing walking fixtures failed. Read-only diagnostics found remaining
obstacle bounds spanning (15.81,15.83) to (10008.36,10008.60), beyond WalkRoute's
4,000,000-cell safety cap. Temp/path-isolated80.json reproduced six failures;
Temp/path-clean-navigation80.json passed all seven with the prior obstacle ledgers
saved, temporarily isolated, then restored. MiniCore Run now owns a navigation
scope so a stopped demo/another test group cannot contaminate its 10km fixtures.
No production pedestrian code was changed for this test isolation problem.

The initial Temp/recovery80-mixed/seed-{1..5}.txt experiment accidentally used
ROAD_SIM_SEED instead of the stub's SEED variable; these are repeated seed 1,
NOT five distinct seeds. Corrected varied-seed runs are in
Temp/recovery80-mixed-seeded and are still being evaluated.

Build 80 native verified at 2026-09-05 19:38:36: all eight Unity groups passed,
including 87 MiniCore scenarios and five rotations of recovery with seated
prisoner + two escorts. No captured Unity errors. Report:
Temp/regression-checks-build80.json. Runtime Assembly-CSharp.dll SHA-256:
9d8a4c05d75426316d568b9eaa37dc95a8b4842ffa6f120b135fd3d7a385369a.
Five fresh automatic collection repetitions have been started against this build;
these are not accepted until their live verdicts arrive.

Build 80 fresh collection_auto first LIVE PASS: 20260905-193904/1,
8935.224 simulated seconds. Collector 5, escort 2, block view one collector,
Schedule-origin physical round paid 85/85, banked 85 at 1.53082m from HQ.
Safe and daily ledger reconciliation passed; traffic failures [], actual walker
write crossings 0 (one coarse sample flagged for review). First pass retained; second pass documented below.
Expanded harness kinds bail_refusal and collection_held compile-checked without
executing player commands. The latter requires the real saved booking checkpoint.

Candidate 81 remains offline: permitting a 12m visible recovery and an opposite
lane with a freshly computed route passed five mixed-visibility seeds in all three
grids (zero overlaps and final frozen>60s=0). Ten physical goal-arrival checks
(five rotations, visible/hidden) also passed. Fully revealed grids still expose
residual stalls, so 81 is not installed or accepted yet. Raw stress exits remain 1
because the old driver flags collision-guard refusals (BeltHits), not just overlaps.
A legal-attribution review also found global recent incident OfficerDeaths feeding
Fight/TheDeed and all hunted crews promoted to CopKilling in RaiseSwarm; this needs
a separate multi-crew fixture before changing blame attribution. No fix claimed.

Build 80 fresh collection_auto 20260905-193904/2 also LIVE PASS at 8926.372s,
85 paid/banked, Schedule origin, carrier 1.825337m from HQ, no traffic failures or
actual walker write crossings. Total 2/5; batch stopped between completed attempts
to reproduce collector ownership after custody. Real checkpoint saved after banking:
Temp/player-trials/20260905-193904-collection_auto/2/collection-banked-checkpoint.json
(611623 bytes, checkpoint-status.txt empty = successful save).

Build 80 collection_held baseline LIVE FAIL: 20260905-200116-collection_held-loaded/1,
1037.00537 elapsed simulated seconds, day 4 at 09:00. An active collector with a
14-dollar compliant account was refused as Unknown tactical group because his
lieutenant's held main unit had been retired. Source checkpoint hash
1a802e533c7de073de0a771ee39b54e65070cd378da87e0ecb94d0c6291a8a5b.
Candidate 81 addresses CollectDues to the independent bag detail and preserves
house/availability validation; the block action asks the same availability helper.
Starting collection no longer deletes the line's unrelated pending doorstep order.
Five native ownership/custody/death refusal fixtures added; live replay still pending.

Candidate 81 recovery source is installed, awaiting native verification. The final
policy repairs a 45-second net stall, retries every 5 seconds, allows up to 32m in
revealed space and 96m only with both full footprints concealed. A clear opposite
lane may be used; route and exact car/riders remain owned by the same objects.
Visible exceptional moves may bypass vehicle deadlock, but require a clear landing
and a pedestrian-free translation/rotation sweep. Earlier 2.5m/12m/18m experiments
left permanent visible stalls; do not mistake them for the final policy. Five
fully-visible seeds passed all three grids in Temp/recovery81-visible32; final
hidden/mixed tests are in Temp/recovery81-final/results.json. Guard refusals still
make the old stress runner exit 1; no overlap/final-stall failures is the explicit
recovery criterion, not a clean verdict for that old all-purpose stress program.
80 existing targeted RoadSim checks, 50 recovery safety checks, and ten goal-arrival
checks passed from the installed source: Temp/traffic-checks81/results.json.

Build 81 native verified at 2026-09-05 20:09:33: all eight Unity groups passed,
including 88 MiniCore scenarios. The custody recovery fixture carries the same
prisoner + two escorts in five hidden and five visible rotations, then drives
physically to the original destination. No captured Unity errors.
Report Temp/regression-checks-build81.json; runtime SHA-256:
cf47af96cd7e268c8a3df8b623300b618ce0686ed840fa3d87fc85befbba07e3.
Five loaded collector-with-jailed-leader repetitions now started; not accepted yet.

Build 79 collection 184809/1 failed at 6078.32031s: lieutenant 4 died fighting the
police after the harness ordered him home during an active arrest question. The
record shows a real corpse and health zero, not an invisible living body. The
harness now waits for an ongoing police encounter before issuing more collection
orders. Follow-up 185906/1 reached a normal quiet physical booking at 3461.57861s;
the collector branch was not completed. Its real campaign is saved as
Temp/player-trials/20260905-185906-collection/1/collector-leader-held-checkpoint.json
for an expanded collector-with-jailed-leader scenario. Neither run earns collection
acceptance. Automatic scheduled collection 190949/1 is now running on build 79.

The offline road stub's Quaternion-vector operator was discovered to return its
input unchanged. It now applies the planar rotation used by road fixtures. Older
offline outputs labelled five rotations did not in fact vary geometry; native
Unity rotated fixtures used real Quaternion and remain distinct. Targeted road
checks must be rerun against the corrected stub before accepting candidate 80.


Build 79 fixes recognition losing the docket after a street crew is rebuilt:
loaded build-78 run 175051-bail_skip-loaded/1 acquitted AI 100006 on case 148 at
344.42s, then booked him again with CaseId=-1 at 1097.8s as part of wanted leader
100001's crew. This is not proof that 100006 himself retained an old wanted mark;
his source-save wanted level was zero. Leaders 100001 and 100003 had level two.
CaseForArrest now finds an unresolved personal docket before a valid remembered
crew file, or opens a fresh charge reflecting the actual wanted grade. Adjudicated
files, including a prior bail-forfeit verdict, cannot be reused for a new verdict.
A recaptured bail skipper carries the old charge as a count onto a new hearing;
other unresolved defendants remain on their original case. An overdue case date
cannot date a new booking earlier than tomorrow.

Separately, 20 isolated pre-fix dismissal checks left every wanted mark intact
(Temp/wanted-dismissal79-baseline.json). Successful booking/rebooking now ends the
pursuit while retaining the docket and escape history. Trials and release from a
legacy serving record also clear the old pursuit. New marks after release still
work normally. All 20 candidate dismissal checks pass
(Temp/wanted-dismissal79-candidate.json). Four five-repetition Police contracts
cover docket recovery, forfeit/recapture/new verdict, capture versus fresh marks,
and legacy outcomes; the native five-repetition recognition test raises MiniCore
to 85 scenarios. All eight Unity regression groups pass with no captured errors
(Temp/regression-checks-build79.json, 18:46:13). The initial test incorrectly
expected a body-backed cop-killing file to be dismissed; it now checks the actual
verdict and cleared pursuit, while evidence-free files must be dismissed.
An unchanged native blocked-turn fixture failed once in the first full suite,
then passed the full rerun and 20 isolated repetitions (100 delta-time runs,
Temp/arc-repeat79.json). That initial intermittent result is retained in
Temp/regression-checks-build79-initial.json; its cause is not yet established.

Build 78 fresh collection 20260905-182833-collection/1 passed at 8871.438s:
collector 5, escort 2, lieutenant 4 and one responsible block agreed with the
block-details read model. Two empty rounds were followed by a real 14 payment
and banking with the carrier 0.9045m from headquarters. Receipts, illegal income
and safe cash reconcile including daily expenses. Traffic failures and actual
walk-write crossings are zero, with one coarse chord for review. This is one
accepted fresh collection repetition; its batch stopped between completed runs
for build 79. New build-79 collection repetitions use strengthened observations:
all AI rosters include wanted/bail state, and any city prisoner lacking a docket
fails the run.

Runtime build 79 SHA256: 1c24fba7f81841eeeca819e4ff5d34af978ba95ab29d14efae092f147968b72e.


Build 78 addresses the first loaded bail-skip failure in 173330-bail_skip-loaded/1
at 1514.6626s. Lieutenant 3 was physically booked at 1510s, paid 2000, became
Bailed/Active and had no custody pin or pending booking. His visible body was free,
but the normal unit had InCustody=true and Surrendered=true while CustodyTracked
was false. Hood 4 was still walking into the station. post-bail-custody-lock.json
captures those contradictory fields; the campaign was saved normally as
bail-lock-checkpoint.json before stopping Play.

ReassertCustody was unconditionally locking the whole original unit on every
remaining transfer tick. It now derives the command lock from a living commander:
a pending or booked commander remains held, while a bailed commander remains free.
A detachment with no living commander follows its own held members. Pending men
still surrender individually. Arrival of a later car wave uses the same logic.
The new five-repetition fixture books and bails through the real model methods,
then reasserts pending custody for three frames per repetition. Before the change,
all 15 released-leader assertions fail while the 30 other assertions pass
(Temp/pending-bail78-baseline.json). The native fixture additionally covers a
fallen commander and raises MiniCore to 84 scenarios. All 45 external candidate
assertions pass (Temp/pending-bail78-candidate.json), as do all eight Unity
regression groups with zero captured errors (Temp/regression-checks-build78.json,
17:50:24). The initial compile could not verify its console; a repeated real
compile completed cleanly.

Build 78 now has five accepted LIVE bail-skip continuations from the real day-seven
campaign save: 20260905-175051-bail_skip-loaded attempts 1, 2, 3, 5 and 6 at
1443.61353, 1443.024, 1447.71033, 1441.77649 and 1442.20654 seconds. Attempt 4
naturally ran and is NOT_EXERCISED. All five include physical booking, exact 2000
bail payment, duplicate-payment refusal, movement after release, the SkipBail
player order and day-eight BailForfeit with wanted status. No tracked bodies were
missing, no traffic failure was recorded, and all actual walk-write crossing
counts are zero; coarse-chord review counts are 2, 0, 2, 0 and 0. Runtime SHA256:
0e9be4cc0a9594101b2146229bd1a9f331e9b8bbb20703676aa7bf9dcda595ac.
These are loaded campaign continuations, not five fresh recruit-to-lawyer runs.

The earlier build-77 bail-skip failure never reached its SkipBail order, so it earns zero acceptance
credit for that branch. It recorded no traffic failure or actual walk-write
crossings before the custody failure, with two coarse chords kept for review.


Build 77 addresses the permanent leading-car stop in 165317-bail-loaded/8.
Car 294 had no physical overlap, but a failed reverse against the angled car 297
kept increasing its belt counter. CanEnter interpreted that counter as a body
standing inside the car, refused the clear junction, and retried the same reverse
before ordinary driver decisions could run. The crossing gate now confirms actual
body overlap before refusing for that reason; physical movement remains checked.
Five rotated exact-shape fixtures all fail before (zero progress for 30s) and pass
after (over 20m through the junction without collision). Evidence:
Temp/blocked-yield77-{baseline,candidate}. All 75 existing targeted RoadSim checks
pass against the candidate (Temp/traffic-candidate77). MiniCore gains the native
five-repetition fixture, bringing its count to 83. Real Unity compilation and all
eight regression groups pass with zero captured errors
(Temp/regression-checks-build77.json, 17:33:00). The five new RoadSim checks pass
against production source as well.

The mixed-emergency seed-three diagnostic improves its unsignalized grid to zero
stalls/frozen cars, but still leaves 25 frozen cars in the signalized grid and 77
on the boulevard stress grid; all three retain zero overlaps. See
Temp/mixed-emergency-traffic-candidate77-core-trace-overlap-custom-car/seed-3.txt.
This is still a failing stress result, not general traffic acceptance.

Build 76 live continuation 172314-bail-loaded/2 passed at 1445.9906s with new
booking, exact bail payment, duplicate refusal, visible movement, acquittal and
another physical move. It records zero traffic and movement-crossing flags.
Attempt /1 naturally fled and is NOT_EXERCISED. The batch was stopped between
completed attempts to compile build 77. There are now five complete loaded bail
flows across builds 75 and 76 (four plus one), not five on the latest runtime.


Build 76 addresses a reproducible unfinished parking swing observed in the first
loaded bail attempt. Court car 298 sat beside parked motorcycle 292 from about
328s until the convoy expired at 416.93s. It later resumed driving, so that case
is a failed transfer, not a permanent traffic stoppage. With the same dimensions
and kerb arrangement, all five model repetitions remain PullIn for 90s before;
the candidate physically parks in 7.9-8.0s without touching the bike. The car must
already be parallel to the road and within the existing kerb parking reach before
this completion path applies. Evidence: Temp/kerb-completion76-{baseline,candidate}.
All 70 existing targeted RoadSim checks also pass against the candidate
(Temp/traffic-candidate76). A native five-repetition group raises MiniCore to 82
scenarios. Real Unity compilation passed, and all eight Unity regression groups
pass with zero captured errors (Temp/regression-checks-build76.json, 17:22:35).
The five new RoadSim checks also pass against the production source.

Build 75 completed four strict loaded bail flows in 165317-bail-loaded/2, /3,
/5 and /6, each ending in day-eight acquittal and physical movement. All four
have zero actual movement-write crossings and zero traffic failures; /6 has two
coarse observation chords retained for review. Attempts /1, /4 and /7 naturally
chose flight and receive no bail credit. Attempt /8 failed at 1186.38416s because
civilian 157 made no progress for 600s behind police cars 294 and 297 near court.
The player had paid bail and walked physically, but the run was not accepted.
Normal CampaignSave.Write preserved traffic-failure-checkpoint.json; exact car
poses and maneuver state are in court-jam-shapes.json. Car 297's physical yaw
remained about 21 degrees off its logical road pose while reversing, and car 294
kept trying to yield back toward it. This distinct obstruction remains unresolved;
the kerb-completion fix is not yet claimed to resolve it. The final build-75 loaded
batch therefore has four PASS, three NOT_EXERCISED and one FAIL, not five passes.




Build 75 addresses a different bail ordering found in `160905-bail/2`: AI hood

400007 was booked and bailed while his lieutenant 400004 was still physically

walking into custody. The hood was visible at 1011.02s and absent at 1014.45s.

At the day-two verdict the pipeline pinned him as Sentenced, exposing the missing

body and failing the run at 1098.56543s. `missing-body-transitions.json` records

that the loss happened immediately after bail, before the verdict. The failing

campaign was saved normally as `missing-body-checkpoint.json`.



Sync now retains a released member independently while the active crew projection

waits for its leader. The same physical body survives repeated Sync, conviction,

and later release; when the normal crew becomes available it can rejoin that

projection. Releasing a hood also preserves the leader's pending station-entry

custody lock, not only an already-booked pin. Two new five-repetition groups cover

booked and pending leaders. The initial external five-repetition candidate passed

45 assertions (`Temp/bailed-hood75-initial-candidate.json`). It is not a baseline:

Unity had automatically compiled the change when Play stopped. Full 81-scenario

Unity verification is pending.



Build 74 completed one strict fresh bail run: `160905-bail/1` PASS at 9727.655s,

with physical booking, a 2000 bail debit, refusal of a duplicate payment, accepted

movement of the visible freed body, day-eight acquittal, and another physical move.

No traffic failures or actual movement-write crossings were recorded. One coarse

observation chord was retained for review; it is not an actual movement crossing.

The next fresh attempt failed on the distinct AI hood-loss defect above. These

are retained independently, without treating the failed repetition as credit.



The harness also supports explicitly recorded continuations through the real

CampaignSave.LoadFromFile command (--load-save plus --load-leader), starting from

an active lieutenant before a new arrest. Source-save hashes, saved day and leader

are written beside each loaded result, and loaded batches have a separate suffix.

This does not grant money, create counsel or jump time inside a campaign. Python

syntax/CLI checks and compilation of the C# controller without executing it pass;

actual continued play now passes once in 165317-bail-loaded/2 at 1444.242s.
That run loaded day seven, issued a new racket demand, physically booked lieutenant
3, paid exactly 2000, rejected duplicate bail, moved the visible released body,
and reached day-eight acquittal and a further physical move. No traffic failures,
coarse crossing flags, actual movement-write flags, or missing tracked bodies were
recorded. Run /1 was NOT_EXERCISED because the lieutenant naturally chose Run;
it receives no bail acceptance credit. The requested five accepted repetitions
are still in progress. The first loader startup (165127) lost a read-only readiness
reply during scene restoration before gameplay; readiness queries now tolerate
that transient error without replaying the LoadFromFile command.



Build 74 addresses the next strict live failure in `153932-bail/1` at 6620.274s:

civilian 12 made no net progress for 600s behind police car 143. The car's U-turn

had stalled physically against parked police car 142, but TickArc kept advancing

its angle and eventually flipped the logical heading. The body remained about

2m away and 78 degrees from the pose its completed maneuver now expected.

`cancelled-maneuver-shapes.json` preserves full precision, while

`traffic-contact-audit.json` shows no overlapping car bodies at the final stop.

The valid player save is `traffic-failure-checkpoint.json` (unlike the failed

save attempt in the preceding run).



The turning arc now checks the complete body sweep before admission, rolls back

angular progress when physical placement fails, and reverses along the same curve

if traffic blocks it after admission. Completion waits until the rear axle is on

the far straight, avoiding the baseline's 51–115 degree final yaw jumps. Arc

occupancy uses the actual arc pose. All three new groups fail in five repetitions

before and pass after (`Temp/arc-motion-{baseline,candidate}/result.txt`). All 70

targeted RoadSim checks pass against Assets (`Temp/traffic-build74`). The separate

TurnRound diagnostic has no assertions; its existing parking offsets are unchanged

and it is not counted among those 70 checks. Mixed emergency grids remain failing

with queues, although overlaps remain zero; the arc fix is not a general traffic

acceptance claim. The first Unity repetition exposed a stale fixture spatial index

when replacing an equal number of bodies within the same editor frame; its failed

artifact is retained, and the fixture now invalidates that index after registering

its isolated bodies. All eight Unity regression groups now pass with zero captured errors

(`Temp/regression-checks-build74.json`, 16:08:32), and real compilation passed.



In the preceding build-73 live run, the previously stuck AI crew 4001 physically

passed the challenge and booking stages. Four AI prisoners reached Serving through

physical vehicles with no failed prison legs at that point; later AI court rows

recorded both unavailable-car days and traffic-delayed vehicles. Those failures

remain visible in `transfer-failures-detail.json`. The player had not yet hired

counsel or exercised bail when traffic stopped the run; no bail PASS is claimed.



Build 73 (76 MiniCore regression scenarios) fixes an AI foot-beat challenge

starvation found in `151714-bail/1`. Officers -32/-33 kept retrying a stance

inside a fixed prop beside the police station. Lead distance stayed 4.785m,

outside the physical 4.6m question reach; the single arrest slot was reclaimed

by the same AI crew for thousands of simulated seconds while player racket

complaints waited AtTheDoor. `approach-detail.jsonl` and

`challenge-approach-probe.jsonl` preserve positions, empty routes and a clear

alternative approach. This live run was stopped and recorded FAIL at its last

sample, 10080.3428s. The attempted save command failed before Play stopped;

no checkpoint or completed bail outcome is claimed.



PoliceBeat now resolves the nominal stance to a clear standing point within

one metre, then uses the normal walking route. Arrest still requires actual

physical proximity. Five rotated fixtures fail before and pass after the

change (`Temp/challenge73-{baseline,candidate}.json`). All eight Unity regression

groups pass with zero captured errors (`Temp/regression-checks-build73.json`,

15:39:01). Compile verification initially could not read the console; a repeated

real compile completed cleanly. A fresh strict five-pass bail batch follows;

controlled fixtures are not counted as live acceptance.



Build 72 (75 MiniCore regression scenarios) addresses four further reproduced

failures. `144637-bail/2` physically booked lieutenant 3, charged 2,000 for bail,

and returned his visible body and Active roster status, but movement was refused

because the unit remained InCustody. The final booking of hood 4 reasserted the

whole-unit latch after the leader's release. `bailed-custody-flags.json` proves the

leader had neither a custody pin nor an unfinished pickup while his unit stayed

locked; hood 4 still had its own pin. Final booking now derives command custody

from the leader's pin, and Sync derives preservation from booked/pending prisoner

ids rather than treating its old unit latch as independent evidence of custody.



The preceding `144637-bail/1` had no traffic failure over 8,308 seconds, but the

lieutenant naturally ran and bail was NOT_EXERCISED. Its AI records exposed two

boarding defects: a static-prop adjustment chose a point inside the parked-car

clearance (400007, both prison pickup attempts), and a fresh pickup retained an

old walk toward the prison exit while its escort was still joining (100007).

Boarding approach selection now includes live vehicle clearance; starting a new

pickup cancels the prisoner's previous walk at his actual position. The physical

door radius and escort seating conditions are unchanged.



RoadCar also validates the recomputed rollback pose after a rejected placement,

preventing a cancelled lateral maneuver from rotating/translating into another

body. All four new groups fail on the previous code in five repetitions and pass

afterward (`Temp/new72-baseline.json`, `Temp/new72-candidate.json`). All 55 targeted

RoadSim checks pass (`Temp/traffic-build72`), as do all eight Unity regression groups

with zero captured errors (`Temp/regression-checks-build72.json`, 15:16:16).

The first compile verification could not read the console; the repeated real

compile completed cleanly. A new strict five-pass bail batch starts with lieutenant

3 to revisit the observed failure, then varies the lieutenant. No live acceptance

is claimed for build 72 yet. The mixed emergency-traffic candidate has zero body

overlaps across 15 models but still has queues that fail; experimental pass-claim

withdrawal and offset junction entry remain unapplied.



Build 71 fixes two further observed traffic failures. In `141934-bail/1`, commuter

3 stopped 1.5828 metres beyond its parking entrance: road arrival accepts up to

three metres of overshoot, but ParkingCar required less than 1.5 metres and refused

to replan a halted returner. It retained the driveway and blocked car 4 for 600

simulated seconds. `parking-return-deadlock.json` records the physical pose, target,

halted state and driveway owner. Entry now accepts the road arrival tolerance,

retains physical sweep collision checks, and replans a stopped returner outside it.

Five arrival offsets also test distant-stop recovery and no entrance teleport.

The same run exposed prison transports falsely following a parked patrol 4.1 metres

beside their straight exit. Only turning connectors now inflate the projected

corner width. Five timesteps clear three metres within 1.6 seconds without contact;

the baseline took about 17 seconds in four cases and never cleared in the fifth

(`Temp/straight-exit-{baseline,candidate}/result.txt`). All 50 targeted RoadSim

checks pass (`Temp/traffic-build71`), as do all eight Unity regression groups with

zero captured errors (`Temp/regression-checks-build71.json`).

The initial parking fixture accidentally retained spawn speed; its failed output

is preserved separately. The corrected full suite was rerun after the editor reload.



A fresh diagnostic bail run, `144637-bail/1`, uses actual 16x play, hires an available

lawyer before crime, and continues the legal flow if an unrelated traffic failure

occurs. Such a flow receives no acceptance credit. Build 70 had no accepted bail

pass; its eight regression groups were clean (`Temp/regression-checks-build70.json`).

Mixed emergency-traffic models still fail. A further, unapplied candidate prevents

Place from writing a colliding recomputed pose after cancelling a lateral maneuver:

five rotated reproductions fail before and pass after the guard, including movement

resuming after the blocker leaves (`Temp/recomputed-pose-{baseline,candidate}`).



Build 70 fixes a confirmed live traffic deadlock in `140259-bail/1`: car 148's

physical reverse was refused because collision checking applied its future heading

at its old position. Both actual endpoint poses were clear. Car 4 and the upstream

queue waited over 600 simulated seconds, so this run failed before bail was exercised.

Exact poses, connector samples and clearance probes are retained in that run's

`reverse-shapes.json`, `reverse-clearance.json` and `reverse-gridlock.json`.

RoadSpace now sweeps position and heading together and checks intermediate rotation

even when the centre is stationary. The exact reverse clears in all five rotated

replays; another five fixtures reject a true collision midway through a stationary

turn. Both groups fail 5/5 on the previous implementation. All 45 targeted RoadSim

checks pass against Assets (`Temp/traffic-build70`). The expanded mixed police

traffic models remain failing; no general traffic acceptance is claimed.



The 69-case candidate adds an available standing approach within the unchanged

physical car-door entry radius, plus reverse-step rollback of road coordinates

when a physical blocker refuses movement. All 35 targeted RoadSim cases pass

(`Temp/traffic-build69`); the reverse reproduction fails all five timesteps against

the preceding code (`Temp/reverse-rollback-baseline/result.txt`). Unity compilation

and all eight regression groups pass with zero captured errors

(`Temp/regression-checks-build69.json`, 14:02:59). Fresh live bail verification

started at 14:03; no live pass is yet claimed for this build.

The preceding 68-case suite has eight clean groups and zero captured errors:

`Temp/regression-checks-build68.json`. Its live `134141-bail/1` was stopped as

NOT_EXERCISED after a harness scheduling error missed the lawyer advertisement

before the lieutenant was convicted. The next run hires counsel through the

available advert before racketeering. No outcome or calendar was forced.

That run independently exposed occupied door targets: rival 400006 had no route

to (58.63,188.72), which is inside a fixed prop; the existing standing-spot helper

finds (57.64,188.84) and the real planner reaches it via three clear segments.

Prisoner and officer boarding share the corrected approach. AI transfer 300003

also records an idle officer at a blocked door in `convoy-boarding-detail.jsonl`.

The player lieutenant had two days with no available transfer car, then a paper

court verdict. This is not accepted as a physical court flow. The opponent's

4001 arrest remained stuck; no complete AI pipeline acceptance is claimed.



The 67-case live court run `130722-court/3` passed the full player flow at

22689.84 simulated seconds: physical booking, conviction, prison, release, then

physical movement with all active crew and bag bodies present. Traffic failures

and actual obstacle-crossing writes were zero. Two rival arrests remained stuck

boarding; this player-flow pass does not certify those AI flows.

The 68-case build adds routing around fixed props even on short vehicle boarding

approaches, with five rotated regression fixtures. Compilation passed. A review

of captured regression errors found five edit-mode cleanup errors in the preceding

67-case evidence despite all eight groups returning empty failure lists. That is

not a clean suite pass. The fixture cleanup is corrected and the suite is being

rerun; original evidence remains in `Temp/regression-checks-build67.json` and

`Temp/regression-checks-build68-cleanup-error.json`.

The 65-case live court run `125024-court/2` physically booked both player members,

transported them to court and then prison, with no missing tracked bodies. It failed

at 4599.62 simulated seconds when car 12 waited 600 seconds for an emergency

response itself blocked behind traffic near the station. The captured geometry is

in that run's `emergency-gridlock.json`. The new reservation rule lets stationary

queues drain until the response moves or reaches the stop line. The two-queue

RoadSim reproduction fails 5/5 against the previous code and passes 5/5 now,

retaining priority for an approaching response. All 30 targeted RoadSim cases pass.

That live run also exposed a final boarding gap: prisoner and escort could each

finish their own approaches while remaining outside mutual seating reach. The

escort now closes the remaining gap; five rotated fixtures cover the real command.

Hidden held bodies could still be moved by ordinary crowd separation and street

ticks. Both now exclude inactive bodies; five fixtures protect hidden and visible

neighbors. Live acceptance now also rejects a hidden prisoner drifting over 8 m

from its held doorway, and records precise boarding-door and escort-post geometry.

The 65-case regression evidence remains in `Temp/regression-checks-build65.json`.

The expanded mixed-traffic model is still failing: adding responding police cars

exposes reverse-motion desynchronization, overlaps and persistent queues across

all five initial seeds (`Temp/mixed-emergency-traffic`). Matching MiniCore street

widths does not remove those failures. Experimental reverse rollback removes the

large overlaps in seed 1 but still leaves long queues. Those experiments have not

been applied to Assets and do not count as accepted traffic behavior.

Live `130722-court/3` also records AI prisoner 400002 stuck on a short boarding

approach (`stuck-boarder.jsonl`): its direct chord crosses fixed geometry, while

the actual route planner finds two clear segments. A narrow routing fix and five

rotated fixtures were applied after the legal run completed.

The preceding 64-case build failed `124221-court/2` at 748.19 simulated seconds:

AI prisoner 200003 was Held with no registered body. The new all-faction custody

guard caught this. Once the leader was booked, projection could retire members

still crossing the station threshold before their individual booking pin existed.

The 65th case checks five player/rival fixtures: pending physical arrest bodies

survive leader booking and then transition to their own persistent custody pins.

Production projection now also queries the dispatcher's unfinished prisoner list.

The 64-case regression evidence remains in `Temp/regression-checks-build64.json`.

The orphaned-custody reproduction lost all five bodies before the fix and preserves

all five afterward (`Temp/orphan-custody-probe-before.json` and `-after.json`).

Held members now keep an independent custody unit when their former active crew

is rebuilt. Repeated projection also preserves unique unit ownership.

Traffic now rolls blocked connector movement back, reverses physically while the

queue yields room, and serializes conflicting turns from one incoming lane.

All 25 targeted RoadSim cases pass. Across five seeds and three broad traffic

models each, there were zero overlaps and zero collision-belt interventions.

One seed still fails the strict end-of-run waiting check: four cars waited more

than 60 simulated seconds, with a maximum of 90 seconds. A follow-up of the same

seed against Assets confirms all four move at least 15 m within another 58.9 seconds.

At 3720 seconds the model has no terminal waits over 60 seconds, no overlaps and

no collision-belt interventions (`Temp/seed2-clearance/result.txt`). The original

strict failure remains recorded; this follow-up distinguishes a queue from deadlock.

Collection 114316-collection/1..5 on the preceding 63-case build physically banked

14 in every flow. Only /1, /3 and /4 are accepted passes; /2 and /5 recorded traffic

failures. The safe and receipts reconcile, with zero observed actual obstacle

crossings. Aggregate sampling chords are retained separately in the trial records.

AI 200001 and 100001 traversed the recorded InTransit/Prison stage to Serving;

other convictions are still being checked for physical transport rather than paper fallback.

The preceding 111909-collection/1 failed after car 132 stood for 600 seconds against

turning car 58. Its memory and traffic-gridlock.json preserve the actual geometry.

The captured car 132/58 jam and the adjacent-node 47/150 jam are now permanent

RoadSim regression scenarios; both pass five timestep variants against Assets.

The optional --continue-on-traffic-failure runner records FLOW_PASS_TRAFFIC_FAIL

when the gameplay flow completes despite a traffic failure; that is not acceptance.

The 62-case build's eight groups passed at 2026-09-05 10:51:06.

The new case covers a player move after a completed book job.

Live beating, physical exit and subsequent move now pass 105118-beating/1..5

(60.75, 65.55, 57.75, 59.42 and 60.31 simulated seconds).

On that 62-case build, owner killing, succession, case, exit and next movement

passed 105552-killing/1..5, with clean consoles and no observed obstacle crossings.

Collection 110043-collection/1 failed at 6036.36 simulated seconds: the injured

lieutenant was killed after another demand. The initial block view correctly showed

collector 5, one collector, Sundays, and did not label escort 2 as a collector.

Several physically completed rounds returned zero with recorded owner excuses.

The next collection strategy withdraws after the first paying relationship.



That collection run also exposed an AI bail-conviction bug: prisoner 100001 was

convicted on day 2, his body disappeared before the day 3 van, and two transports

failed before the paper fallback. Evidence: its ai-prisoner-100001.json and memory.

The 63-case fix reclaims custody tracking at conviction before roster projection

can delete the body. Transfers now resolve and retain the prisoner's actual exit

as their pickup/source, including when a different precinct supplies the vehicle.

The regression checks player/rival body preservation, command refusal and the

actual doorway five times. Fresh live collection/legal verification is in progress.

The 61-case build was compiled and verified at 2026-09-05 10:14:47.

Its loaded damaged-shop demand passes: 102228-racket_rendered/1..5.

Its overlapping smash/demand jobs, including exit and next move: 102741-racket_overlap/1..5.

Its physical arson jobs and business closure: 103303-arson/1..5.

All three batches had clean gameplay consoles and zero recorded obstacle crossings.

Beating attempt 103758-beating/1 failed: an accepted move after the physical

exit was overwritten by the completed job's pending automatic walk home.

The direct retask path now drops that obsolete dispatch stamp; all five fresh

beating runs passed with clean gameplay consoles and zero obstacle crossings.

`python Tools/play/trial_report.py` lists only trials whose recorded runtime DLL

hash matches the current runtime. Older-build passes below retain their original

attribution and do not certify changes made afterward.



On the preceding 60-case build, flight passes: 094411-flee/4,8 and 095300-flee/1..5 (seven total).

These cover an explicit RUN during the challenge and an explicit RUN redirect

after a natural flight response. The live campaign continues with the damage and

racket ladder before combat, collection and full legal flows.



On that same 60-case build, smash/demand passes: 095956-racket/1..5. All five owners accepted

after smashing, so those runs do not exercise arson. Separate rendered-door,

overlapping-job and arson scenarios are queued to cover those branches explicitly.

Actions use the production player command methods in the open Unity editor. No UI



automation, screenshots, forced police answers, granted cash, health overrides or



teleports. Each attempt starts a fresh Play session. All phases now run at 16× at the



player's request. Startup CLI timeouts are preserved separately from gameplay errors.







## Acceptance







| Scenario | Successful recorded live repetitions | Evidence |



| --- | ---: | --- |



| Change collector/escort indoors, promote escort, move his physical body, validate organization | **5/5** | `Temp/player-trials/20260905-032806-roles/1..5` |



| Three shop demands, physical exits, subsequent movement | **5/5** | `Temp/player-trials/20260905-032959-doorstep/1..5` |



| Load a saved court campaign: same books/verdicts, prisoner bodies present, no obsolete car blockers | **5/5 state checks; continuation pending** | `Temp/player-trials/20260904-235142-court-load/1..5-result.json` |



| Quiet arrest through physical station booking | **5/5 on braking fix; late campaign still pending** | `Temp/player-trials/20260905-041101-arrest/1..5`, 379?451 s; original late-campaign failure retained |



| Bail refusal without counsel, hire counsel, pay once, physical release, court outcome | 0/5 | `20260905-011909-bail/1` naturally chose Run (NOT_EXERCISED); attempt 2 chose Quiet but pickup failed after 900 simulated seconds |



| Held defendant: court convoy, verdict, prison transfer/release | **1/5** | `20260905-025800-court/1`: complete fresh chain, 24128.67 s, clear console; remaining four repetitions pending |



| Ordered bail skip: court-day forfeiture and wanted record | 0/5 | Driver prepared |



| Flee arrest and preserve physical movement/wanted status | **5/5 on 05:01 build** | `20260905-050414-flee` attempts 1/2/4/7/9; explicit player command, wanted record, both bodies fully inside, flight/swarm cleared |

| Defeat by police, corpse cleanup, command rejection, swarm ends | **5/5 on 05:01 build** | `20260905-051131-fight/1`, `20260905-051502-fight/1,3,9`, `20260905-052447-fight/1` |

| Kill arresting police, survive, reach shelter, swarm ends | **5/5 on 05:50 build** | `20260905-055446-fight_win` attempts 2/3/5/6/7; immediate return to owned building, every survivor physically hidden, triggered swarm cleared |

| Collector assignment, dues, collection, return and safe reconciliation | 0/5 | Exploratory trips recorded; not accepted |



| Automatic weekly collection after assigning a block | 0/5 | Separate driver prepared; verifies schedule origin and physical payment |



| Purchase car, board, drive, park, exit, walk onward | **5/5** | `Temp/player-trials/20260905-035209-car/1..5`, includes previous stuck sedan flank |



| Smash/torch chain; beat owner; kill owner/succession | 0/5 | Drivers prepared |



| Sustained traffic recovery and pedestrian obstacle clearance | 0/5 | More failures found during long collection wait |







## Reproduced problems and changes







- **Parking maneuver redesign compiled; live failures still being resolved.** Fixed



  in-bay curves were replaced by incremental steering/reverse planning, bounded to



  256 expansions per rendered frame. A dense five-car fixture exposed insufficient



  aisle space; the shared generator now gives an 8 m aisle and 1 m rear margin,



  retaining authored vehicle scale. All five bays can plan arrival and departure.



  Live `20260905-025800-court/1` then exposed tiny overlaps between sampled path



  poses (cars 4/1 and 7/6), plus departures losing turns to recurring returns.



  Denser chord clearance, extra planning margin and a shared departure/return FIFO



  compiled at 03:27. These are not accepted parking repetitions.



- **Interrupted station walks now retry.** In `20260905-021950-court/1`, four AI



  Dons stood unbooked outside the station with no active doorway visit. The shared



  custody code now retries an inactive visit after five seconds, requiring actual



  threshold arrival before booking. The focused regression passes without errors;



  the following fresh campaign is being observed for AI continuation.



- **Released hood remained in his old custody unit after body removal.** Fresh



  `20260905-021950-court/1` reached day 18 without reload, after physical court and



  prison trips, but failed at 24120.875 s on missing hood 3. The same-session



  `release-recheck.json` confirmed all four bodies were restored on the next



  observed update and LT4 physically completed a move by 24127.455 s. The original



  FAIL is preserved. `RemoveMan` now also clears unit membership, preventing a



  retired body from becoming commandable at release; the regression passes.



- **Next fresh court run:** `20260905-025800-court/1`, booking 443.19 s,



  physical court transit 1214.75 s, conviction 1326.67 s, physical prison transit



  2569.14 s, serving 2672.03 s, release due day 18. Completed at 24128.67 s without reload; roster, LT4/hood3 and collector/escort bodies were present in the final memory sample.







- **All three parking lots deadlocked inbound and outbound traffic.** In



  `20260905-011909-bail/2/parking-deadlock.json`, cars 5, 9 and 15 each held



  their driveway from inside the exit. Returners parked across their path while



  waiting for that same driveway. Car 14 became derelict in the running lane;



  police car 145 could not reach the player, and two rival custodies also stalled.



  The ordinary campaign save is preserved as `parking-checkpoint.json` (ambient



  vehicle positions are diagnostic evidence, not necessarily serialized state).



  Parking now reserves admission before issuing the return route; unadmitted



  cars keep circulating and return requests take FIFO priority over departures.



  Admitted cars stop at the entrance instead of searching nearby kerb spaces.



  Parking motions now share road simulation substeps and collision checks, and



  publish the actual body heading. This redesign is under validation, not accepted.



- **A destination issued during a junction crossing could keep a null route forever.**



  In `20260905-020002-bail/1`, parking returners 2 and 12 drove around the city for



  thousands of simulated seconds with `HasGoal=true` and `Route=null`. An order



  during crossing cannot plan from a current road; subsequent approaches only



  repaired non-null tables. They now build a missing table too. A physical



  crossing/order/arrival regression reproduces this shared routing failure.



- **Parking curves turned a long car into its adjacent stall.** New body checks



  stopped car 6 at the start of its departure beside car 7 (both half length 3.081 m,



  half width 1.148 m, bays 2.7 m apart). Departures now leave the stall straight



  before turning in the aisle. A regression verifies the entire departure with



  the neighbour present. The exploratory bail run was interrupted before its legal



  chain to compile both fixes; it is not an acceptance repetition.



- **Some catalogue cars physically exceeded the generated bays.** Live pickups



  measured 6.86–7.48 m long in 5.6 m stalls. The lot now filters the weighted



  catalogue by the actual renderer footprint, preserving authored scale. Simultaneous



  translation/yaw is sampled together so a clear moving tail is not refused by



  testing its future yaw at its previous position. Both cases have regressions.



- **Drifting formation endpoints bypassed the exact failed-route cache.** The



  `20260905-020725-bail/1/cpu-profile.json` again measured 1017.28 ms in one route.



  Before full A*, a bounded flood from the goal now detects a small disconnected



  component; exhausting it proves failure, while exceeding 256 explored cells



  falls back to the unchanged full search. The regression varies both endpoints



  inside/outside a sealed enclosure and then verifies recovery when its walls go.



  The same live attempt completed at least five full parking cycles for every car



  in one lot, but two other lots stalled and the legal attempt was interrupted.







- **The full exploratory legal chain reached release on day 18.** The original



  arrest and court evidence is in `20260904-230419-court/1`; the loaded lieutenant



  physically rode to prison in `20260905-000021-court-load/continuation` at 1920.37 s.



  A natural day-9 checkpoint resumed in `20260905-002704-court-day9-resume` and reached



  release at 11083.75 s. The observer first mistook the one-update gap between roster



  discharge and physical custody cleanup for a persistent refusal. The subsequent



  `release-check.json` confirms a visible body walking after release. The driver now



  waits for the custody projection with a bounded deadline. This is exploratory



  evidence, not five accepted fresh court runs.



- **AI court completion used the player's roster.** Rival 200007 reached court but



  stayed in transit because the judgment was applied to the wrong house. Physical



  court, rescue and casualty completions now resolve the prisoner's own roster.



  The next live replay gave that rival an acquittal on day 3.



- **Short custody approaches skipped routing around props.** The escort repeatedly



  circled at roughly `(630, 234)` beside the station's parked pickups, while prisoner



  4 stood at `(640, 236.58)`. Short approaches now use the same proved route as longer



  ones. A geometry regression covers the previously direct approach.



- **Wanted recognition repeatedly targeted a hidden AI lieutenant.** Rival 400006



  was held indoors while a patrol stood outside and retried its 45-second walk-up.



  Recognition and arrest selection now require an exposed body and sight; a suspect



  going indoors ends an unfinished approach. Beat officers also route around fixed



  props when approaching their suspect.



- **AI march orders dereferenced a retired hood's transform.** Five exceptions in



  the day-9 continuation came from `DispatchAcross`. Dispatch ignores retired views,



  and a missing active roster view now triggers repair without waiting for a later



  roster mutation. Covered by the nineteenth MiniCore regression.



- **Repeated impossible formation routes caused the sustained slowdown.** In



  `20260905-010645-bail/1`, AI members 500003 and 500004 repeatedly tried to reach



  formation slots beside a stationary leader. Its `cpu-profile.json` records



  2008.54 ms in cohesion, including two route searches of 1007.08 and 1000.29 ms.



  Failed exact-endpoint routes are now cached until fixed geometry changes; a hood



  whose formation slot is unreachable also tries the leader's own position. A



  regression verifies repeat-query reuse and success after removing the enclosure.



  A natural `slow-checkpoint.json` preserves the affected campaign. Fresh long-run



  validation remains required. The next fresh attempt (`20260905-011909-bail/1`)



  advanced normally through day 7; a subsequent CPU sample measured 27.69 ms/frame.



  That lieutenant naturally chose Run at the police challenge, so the attempt is



  NOT_EXERCISED for bail, rather than an accepted legal chain.



- **Long-session performance history.** Before the day-9 reload,



  `DemoCrews.Update` reached about 2252 ms/frame. After reload it was initially below



  5 ms; a later 109 ms walker spike contained repeated route planning. Named profiler



  samples and automatic capture now identify sustained slowdowns. This is not yet



  accepted as a fixed long-session performance issue.







- **Loading MiniCore erased the player's roster and safe.** Null entries for



  undealt houses were serialized by Unity as empty gang-0 records, overwriting the



  real player later in the array. Snapshots now include only dealt houses; restore



  accepts the first record for each gang, including old files containing placeholders.



- **Scene load omitted the gameplay directors and delayed restoration.** The shared



  bootstrap now installs on subsequent scene loads as well as first Play. Pending



  campaign data is restored before the first territory update, instead of waiting



  for the four-hour business cadence. Scheduler time is anchored to the saved hour.



- **Load manufactured body evidence.** A gang-5 cop-killing case changed its explicit



  false evidence flag to true. The saved flag now survives without inferring evidence



  from the charge. Save regression checks cover both true and false for lethal charges.



- **Old traffic survived load as invisible collision blockers.** Five reloads left



  815 road users for 152 current cars. Scene owners now retire vehicle lane claims;



  pruning a destroyed view also removes its collision registration. Repeated loads



  now retain 152 users and no missing car bodies.



- **Saved prisoners had no bodies to board their transport.** The saved roster



  correctly excluded jailed men from the active crew projection, but custody only



  preserved already-existing walkers. Prison pickups timed out twice waiting for



  nonexistent men. Loading now restores per-person custody bodies behind the source



  door without taking command of any still-active crew.



- **Stalled foot responders repeatedly rebuilt impossible routes.** Two beats held



  the response indefinitely and consumed roughly two seconds per frame in the live



  CPU profile. Stalled routes stop retrying and the dispatcher releases those beats.



- **AI combat inspected a destroyed transform.** The long loaded run logged four



  exceptions in `EnemyWithin`; enemy observation now requires an active physical



  body on both sides. A focused regression exercises a destroyed observer.







- **Accelerated driving used an entire 16x frame as one movement step.** At a



  simulated frame of 0.533 s, the 100-car grid had 2,064 overlapping observations,



  15,458 collision-belt refusals and 83 cars frozen for over a minute. Road users



  now advance together in steps of at most 1/30 s, with signal/tactic time advancing



  per step and the spatial index refreshed. Per-car subdivision alone was not



  sufficient. The ordinary grids now pass at accelerated frame sizes; dense



  boulevard turn starvation remains under investigation across seeds.



- **The first legal replay after the coordinated driving change reached booking.**



  In `Temp/player-trials/20260904-230419-court/1`, player members 3 and 4 physically



  entered the precinct by 352.68 s and are held on case 3 for court day 2. This is



  partial evidence, not yet an accepted complete court/release repetition.



  Rival faction 1 subsequently posted two separate 2,000 bails through its AI.







- **A simultaneous rival arrest made police skip the player's present crew.** In



  `Temp/player-trials/20260904-224949-arrest/1`, complaint 5 reached Lindqvist Travel



  at 50.67 seconds, became Statement at 52.74, and closed without challenging the



  player's crew standing at `(226.46, 555.43)`. The global collar was occupied by



  faction 1. Doorstep calls now wait for that collar instead of treating it as an



  absent suspect; hidden indoor crews are excluded from doorstep suspect selection.



  A focused simultaneous-call regression passes; live legal replay is in progress.







- **Two custodies used patrol 141 simultaneously.** One carried prisoners while



  another waited for the same car. Returning cars were considered available.



  Custody now reserves a car through all waves and unloading, and carrier assignment



  rejects ownership by another active custody.



- **Shop exit left the lieutenant inside the subway entrance.** Grimaldi Grocery's



  outside point `(226.2, 547.5)` overlapped the subway prop. The subsequent eighteen



  movement/demand orders were refused. Per player direction, residential generation



  no longer adds subway entrances. New live shop chains can leave that door.



- **A patrol's pull-out stayed pending indefinitely behind a parked shoulder.**



  Added a checked forward recovery before another merge attempt.



- **A police pickup parked roughly 300 m from its prisoners.** Generic parking



  timeout could replace the response destination with the car's current street.



  Replacement parking remains near the originally selected response kerb.



- **Stationary passing/merging reservations blocked one another without touching



  the collision belt.** During the long run, cars 7, 143 and 148 waited for hundreds



  of simulated seconds. A stationary lateral sweep now relinquishes its projected



  reservation while preserving the physical position and ordinary collision checks.



  Long live recovery still needs verification.



- **Destroyed walker view repeatedly aborted the crew update.** Tick now ignores a



  destroyed transform; roster synchronization preserves bodies across root changes



  and can rebuild an unexpectedly missing living view. Further lifecycle testing



  remains required.







Earlier changes also address orphan indoor holds, late escorts, civilian obstacle



clearance, swarm cleanup and collector/escort labels. They are not all accepted merely



because the focused regressions pass.







## Exploratory collection observations







An earlier multi-door trip banked 105 with exact account reconciliation. It predates



the latest fixes and does not satisfy current acceptance. A later single-door trial



completed two physical trips with zero payment; the proprietor refused and the



arrangement lapsed after two misses. This is not evidence of a frozen collector.



The driver is being expanded to handle refusals and multiple protected doors.







Focused Edit-mode checks pass thirty-three MiniCore regressions plus the existing



pedestrian, traffic, police, organization and rounds model suites, with a scoped



error capture returning empty (`Temp/regression-checks-latest.json`). Earlier parking



fixture cleanup logged Edit-mode Destroy errors; cleanup now destroys its test



objects with DestroyImmediate before deregistering the cars. The fairness



regression and denser playback collision checks also pass. Model results are



reported separately from live acceptance.







Both role and doorstep 5/5 runs have been repeated on the 03:27 build. Five accelerated grid seeds pass with the retained



turn-yield fix (`Temp/roadsim-latched-fast-1..5.txt`), but the broader red-light scenarios



exposed a fast vehicle entering an occupied crossing. Production now corrects



premature crossing commitment while braking, prevents a passing car returning into



an occupied lane, and allows a static blocker to be passed before a distant stop



line. The broader crew-ring failure fell from 93 collision-belt interventions and



one frozen car to zero. Five accelerated complete model seeds pass



(`Temp/roadsim-static-pass-all-1..5.txt`). Ordinary-speed boulevard seeds still fail;



crossing-window and yielding experiments in `Temp/roadsim-candidate` remain unapplied.



The older accelerated runs still held the red-light/turn-round sections at their own



fixed timestep. All sections now share `ROAD_SIM_DT` and coordinated vehicle steps;



five full accelerated production seeds also pass (`Temp/roadsim-unified-fast-1..5.txt`).







An additional live observer in `20260905-011909-bail/2` checks actual outdoor movement



segments for crews and walking civilians. At 3,000 sampled frames it tracked about



490 bodies with no fixed-obstacle crossing between free endpoints. This is exploratory



coverage, not five completed obstacle-clearance runs.







Runner: `Tools/play/player_trials.py`; scenario script: `Tools/play/player_trial.cs.txt`.



Each attempt saves actions, memory samples, console evidence and its verdict.







A further isolated road candidate corrected the sign of the lateral connector offset



for reverse road headings and replaced changing wait-duration comparisons with stable



arrival timestamps. `Temp/roadsim-signed-offset-stable-*` passes five accelerated



seeds and ordinary seeds 3?5; ordinary seeds 1?2 still have long boulevard queues.



The signed offset alone is retained; the wait-priority experiment is not applied.



The stopped-car crossing-window candidate failed comparison; isolated model experiments do not change the running court game.







AI custody `2001` in `20260905-025800-court/1` remained boarding from roughly



5158 s through 8381 s. `custody-boarding.json` shows live prisoners/escorts walking,



with the prisoner's destination repeatedly replaced by the old lieutenant's spot.



`TickCohesion` had no custody/surrender exemption. The pending shared fix gives



custody exclusive control of these walking orders; a reproduction was added.



Custody release also invalidates the cached roster projection so releasing a pin



after the roster update cannot leave active members absent until an unrelated edit.



The pending suite is now 31 scenarios; 29 were previously compiled and passed cleanly.







The retained signed-offset road correction alone passes the five accelerated model



seeds (`Temp/roadsim-signed-offset-only-fast-1..5.txt`); ordinary seeds 3?5 pass and



1?2 still have long queues. The sign correction and its two-heading continuity



regression compiled and passed in the 03:27 build.



Changing green-window eligibility, reserving a starved axis, or simply lengthening



all greens failed the five-seed comparison and remains isolated in Temp. A separate



candidate waits for junction occupants to clear before starting the next green.







The fresh court run `20260905-025800-court/1` passed the complete player legal



chain with no console errors. While LT4 served his sentence, the spare hood 1 was



promoted and two hoods 6/7 recruited as a separate collector/escort detail. That



crew was assigned the existing Lindqvist Travel block and manually collected 111



from 214 owed. The original backup verdict is FAIL because its observer used



`DoorBeat.Held`, which means a permanent indoor hold, not an ordinary shop visit.



Memory proves collector 6 physically walked in, was hidden inside during payment,



and walked back out; future checks use `PhaseOf == Inside`.







This observation also exposed a real early-banking bug: the collection's 18 m home



radius booked the 111 while the collector was still at the neighbouring shop's



exit. The pending fix uses the headquarters' actual pavement approach and a 2.5 m



arrival radius, with a test proving no banking 14 m away and banking on arrival.



The compiled suite has 33 passing scenarios. The backup collection is not accepted



as a completed physical-return repetition.







At 03:27 on September 5 the 33-scenario MiniCore suite and the traffic, pedestrian,



police, organization and rounds suites all passed; scoped errors were empty. This



compiled build includes the shared parking FIFO, denser maneuver clearance, custody



cohesion exemption, release projection invalidation, signed connector offset and



physical headquarters return. A fresh sequence now runs five live repetitions of



each prepared scenario, stopping immediately on a failed verdict.







The first car scenario setup attempted to assign equipment in the same evaluation



as promotion, before the physical source knew the new crew. Armory correctly refused



an unlocated recipient. The driver now purchases normally, waits for the physical



crew/location projection, and then assigns through the same public armory command;



this also applies to the strong-combat loadout. No gameplay bypass was introduced.







Car trial `20260905-033443-car/1` passed, with 29,831 observed movement segments and



no obstacle flags. Attempt 2 requested z=645.77 beyond the road network's maximum



z=575; the car could not complete that off-map free-drive request. The original



FAIL remains. The intended road-trip driver now chooses real road destinations



at least 70 m away, varies direction, and checks both routed and free-drive goals



before calling arrival. The attempt also recorded one pedestrian-segment flag for



inspection; that observation is kept separately from the out-of-map trial input.







- Car attempt 2 walker review: AI character 400004 frame chord (510,152) -> (508.61,149.81), 2.59 m, intersects two perpendicular SM_Env_Fence_01 footprints at corner (509.45,150.55). Memory-only frame observer cannot distinguish a corner-following route within a fast frame from actual penetration. Geometry saved in walker-crossing-review.json; remains unaccepted pending finer movement evidence.







### 03:55 — live car exit exposes pedestrian oscillation



- `20260905-034340-car/1` passed 82.23 sim seconds on a valid road goal. Attempt 2 drove and parked at (147.4,579.23), exited at 105.67s, then accepted a normal walk at 108.05s. Lieutenant 4 oscillated along the south flank of his sedan and abandoned the order; hood 3 reached the opposite side. Original FAIL and memory are retained.



- A normal repeat from a slightly different position succeeded; walking back to the original starting point and requesting the same destination reproduced the oscillation. `walk-retry.jsonl` and `walk-reproduce.jsonl` sample every runtime frame, with actual destination and steering state. This is a production movement defect, distinct from the earlier off-map car test input.



- New shared `ParkedCarWalkRoute` computes bounded short visibility routes around parked vehicle footprints, retaining static obstacle and city clearance. Routed crew strides follow its corners while live collision steering still checks every step. Focused test covers both sides, four longitudinal starting offsets, complete segment clearance and refusal of an endpoint inside the car. Compile and live repetition pending.



- Main live observer now runs in the runtime PlayerLoop rather than editor callbacks, preventing skipped-frame chords from being mistaken for single movement segments. Earlier fence flags remain unresolved, not accepted as either confirmed clipping or clean passage.







- 03:51 build COMPILED; all 34 MiniCore cases and Traffic/Pedestrians/Police/Organization/Rounds suites pass; scoped errors empty. Corrected live car set restarted.







- New parked-car walking route: **5/5 live car flows PASS** on build 03:51; durations 76.39?86.04 simulation seconds. Runtime-frame observer checked 86032 outdoor walk segments with zero flags; this short set does not certify all-city obstacle clearance.







### 04:09 — custody transport blocked at a tight turn



- Fresh arrest `20260905-035415-arrest/1` physically booked the lieutenant at 367.47s. Attempt 2 boarded the lieutenant and hood correctly but failed at 1033.91s while riding in car 146, behind the junction at (462.5,352.5).



- Memory pins the chain: long vehicle 108 turns north; patrol 144 stopped with its nose only 1.73m short of the box although the authored setback is 5.7m. Sampling the actual rear-axle connector pose shows overlap with patrol 144 in the final portion of the turn. Emergency car 145 queued behind 144 also makes conflicting approaches wait. Parking vehicles 1 and 7 consequently remain away from their lots; this is not a parking maneuver planner failure.



- Patrol 144 entered the 35m street at emergency speed, then reverted to patrol driving. Added shared exit stopping-speed anticipation on junction approach and throughout its connector, using the ordinary braking allowance so a cancelled emergency response still has stopping distance on the next street. No signal or stop-line geometry changes were made.



- RoadSim current candidate: normal seeds 1/2 still exceed long-queue criterion, 3/4/5 PASS; accelerated seeds 1–5 PASS, `Temp/roadsim-short-exit-*`. Does not certify all traffic.



- Recompile script returned stale `up_to_date`: reflection found neither new ExitStopSpeed nor new test loaded. Forced asset import exposed a test-only HardStop API typo, fixed to Halt(true). Two existing route fixtures also depended on an empty city fence after Play; Fixture now registers/restores its own test ground. Fresh compile, loaded-method check and all focused suites pending.



- Two runtime-frame walker flags for police IDs -89 and -14 near station preserved in attempt 2; finer geometry review is still pending.







- 04:09 forced-import build COMPILED; reflection verifies ExitStopSpeed and the new test are loaded. All 35 focused MiniCore cases and five companion suites PASS, scoped errors empty.



- Isolated before/after proof using the shared RoadSim code: at 16x old code entered the blocked crossing (nose -1.588m past the boundary), new code stayed out with a 5.698m setback. At ordinary frame size new setback is 5.697m. `Temp/short-exit-proof-{before,after}-*.txt`. Focused fixture extended to both frame sizes on disk; test-only change awaits next safe compile.







- 04:19: five fresh arrests PASS on the exit-braking build, `20260905-041101-arrest/1..5`. CLI connection loss before attempt 5 was resumed without replaying player commands or overwriting completed evidence.



- One civilian frame flag in attempt 2 cuts a lamp-pole clearance corner. Code review independently found that `SpendJoin` sidesteps checked only endpoint occupancy; both free endpoints can straddle a pole corner. Added full fixed-obstacle segment proof and the existing pedestrian per-frame distance cap, plus the pole-corner fixture. Exact attribution of the earlier frame flag remains uncertain because streamed geometry may have changed in that frame; observer now records both geometry versions. Compile and focused checks pending.







### 04:26 — 16x walking ceiling was not scaled



- `20260905-042201-flee/1` failed: the lieutenant accepted a flee command at 122.11s, moved only about 5.25m over the next 19 simulated seconds, and was shot dead; the hood died at 152.09s. Police attacker -75 is recorded in death-audit.json. No forced damage or outcome.



- Crew stride, graph catch-up and sidestep all shared a hard 0.75m rendered-frame cap. At the observed 2–3 simulated seconds per frame on 16x, it throttled feet to about 0.3m/s while combat and game time advanced normally.



- The real-frame hitch ceiling now scales with Time.timeScale; ordinary-speed protection remains 0.75m, all fixed-obstacle chord proofs remain active. New fixture checks the same 3m/s simulated graph pace at 1x, 4x and 16x. The earlier car/arrest/role/doorstep passes are retained as evidence on their stated builds; the walking-rate change requires live revalidation. Build/checks/flee repetition pending.







- New-rate flee `20260905-042737-flee/1`: crew covered 34m with full health after the flee command. The test then explicitly ordered them back toward headquarters and their pursuers at 708.35s; they died on that return. This failed player strategy is preserved, not called a failed speed fix. The flee driver now lets the actual pursuit/escape/shelter logic run after the escape command instead of reversing after 20 seconds.



- That run also records a civilian pole flag when the geometry version changed in the same frame (3542?3543), plus two police chair crossings with unchanged geometry, IDs -8/-9. Those police flags still require movement-owner diagnosis.





### 04:49 - flight continuation and actual corner clipping

- Build 04:42 passed 38 focused cases and all five companion suites, scoped errors empty. It compiled both the full-chord crowd Nudge guard and continuation after a fleeing crew finishes its first movement leg, without resetting flight/sighting history or overwriting an existing move.

- Fresh `20260905-044219-flee/1` survived with full health but failed at 938.41 s: the crew ran to the southern city fence, still followed by a foot patrol 13.46 m away. Straight distance from the pursuer is insufficient escape strategy. Original FAIL and `flight-final.json` remain.

- New `FlightRoute` chooses reachable nearby destinations, preferring those behind a building; it rejects a route that first walks back into the pursuer and rejects negligible clamped movement at the city fence. Both initial flight and subsequent legs use the same chooser. The live driver now remembers having moved 20 m, so returning to a nearby home does not erase the observed escape distance.

- A separate actual-transform reproduction confirms the routed-corner defect: with a chair one metre ahead and the corner two metres ahead, steering chose a safe diagonal, but the final corner snap crossed the chair. The snap now requires the proved heading to be the direct heading. `Temp/corner-step-proof-before.json` reports blocked=true; the same case after reports blocked=false and a real two-metre diagonal step.

- Build 04:49: all 40 MiniCore cases plus Traffic/Pedestrians/Police/Organization/Rounds pass, errors empty. Fresh flee repetitions restarted. Earlier live acceptance counts remain associated with their builds; the substantial movement changes still require live revalidation.



- Follow-up: `20260905-044947-flee/1` reported PASS at 538.53 s, full health and no swarm, but the final memory shows lieutenant 4 still visible in the entry crossing. `DoorBeat.Held` used `Indoors`, whose cleanup meaning includes Entering/Exiting. This prematurely completed quarters and custody arrival. That run is retained as exploratory, not accepted. Attempt 2 was interrupted before exercising the player's arrest to fix the shared state.

- Held now requires the actual Inside phase. A focused fixture covers the visible entry phase and the completed hidden occupant. The live flee verifier additionally requires every surviving body to be hidden in Inside phase before accepting shelter. Fresh compile/repetition pending.



- Build 04:54: all 41 targeted cases and five companion suites pass; errors empty. Fresh `20260905-045457-flee/1` failed the 900 s pursuit deadline at 941.13 s, still alive and wanted. The paused frame happened to have both walkers at a route corner. Follow-up runtime-frame observation (`stall-followup.jsonl`, 941.95-1001.42 s) shows normal continued movement; that corner was NOT a confirmed permanent stall.

- The sustained pursuit exposes incomplete gait wiring: unit-level Fleeing used a shared Striding order, never selected the existing sprint gait, and was still subject to crew-cohesion slowing (only individual Mode.Fleeing was exempt). Ordered flight now selects the existing sprint, preserves it on a player redirect, and ends it with EndFlight; the formation tether exempts fleeing units. The obstacle lookahead now covers the intended speed-limited frame step; its old fixed 3/6 m extent otherwise imposed another hidden speed ceiling at 16x. New/extended fixtures cover sprint wiring, redirects/end, cohesion and actual clear-ground strides at large simulated frame duration. Compilation pending.



- Build 05:01: all 42 MiniCore cases and five companion suites pass, errors empty. `20260905-050156-flee/1` took a natural Run at 31.84 s between editor action callbacks. Later memory confirms wanted level 1, full health for both men, and both physically hidden Inside headquarters with flight/swarm cleared. The requested explicit-command branch was missed, so the original timeout result is retained and the successful natural escape is supplementary evidence only.

- Player actions now run after every runtime frame in the same installed PlayerLoop as the walk observer. A non-Quiet natural answer that predates the explicit command window becomes NOT_EXERCISED instead of an unproductive timeout. All commands still use the same public production player seams; no outcomes or character stats are forced.



- Explicit flee set `20260905-050414-flee` has four accepted runs so far: attempts 1/2/4/7 at 1460.82, 860.40, 268.44 and 817.42 simulation seconds. All had player-issued RUN during an observed arrest, wanted record, living physical crew fully Inside shelter, and cleared flight/swarm. Natural Run before command observation (3/5/6) is NOT_EXERCISED, not a failed production escape or acceptance credit.



- Explicit fleeing accepted **5/5** on the 42-case build: `20260905-050414-flee` attempts 1/2/4/7/9, 1460.82 / 860.40 / 268.44 / 817.42 / 158.71 s. Attempts 3/5/6/8 naturally ran before the command window and are NOT_EXERCISED. The batch continued to combat.

- Longer unchanged-code road observation: `Temp/roadsim-longqueue/seed-1..5.txt`, same original grid sequence with boulevard duration extended from 300 to 900 s. All five finish with zero overlaps/belt hits/frozen>60s; earlier flagged seed-1 vehicles resume by 304.2 s and seed-2 vehicles by 329.1 s. They were long queues, not permanent locks. The approximately 100-second worst observed wait remains a responsiveness concern. This is model evidence, not five accepted long live traffic sessions.

- Combat defeat has four accepted live repetitions on the same 05:01 production build: `20260905-051131-fight/1` (215.19 s; 17 recorded active swarm samples), plus `20260905-051502-fight/1,3,9` (832.72 / 205.46 / 856.08 s). Defeated units reject movement after the cleanup interval and swarm clears. The latter driver also requires an actually observed swarm before PASS.

- The first combat batch's attempt 2 timed out after a natural Run bypassed the intended fight command. Its original FAIL is retained; the later driver classifies natural non-Quiet answers before the command window as NOT_EXERCISED for combat too. A prepared next driver issues an ordinary attack as the officers answering our own complaint approach within 15 m, so combat does not depend on a fleeting arrest phase. No police responses or outcomes are set.

- Defeat cleanup is accepted **5/5**: the prior four plus `20260905-052447-fight/1` at 202.08 s, using a normal attack on the approaching response. Swarm must have been observed active, then clear; wiped crews refuse movement after the corpse interval. Some earlier attempts naturally ran before input and are not fights.

- First victory attempt `20260905-052527-fight_win/1` failed at 246.12 s. Five Tommy Guns were bought and assigned through the armory; player attacked the approaching officers at 26.11 s. A new arrest began at 37.24 s and the lieutenant naturally ran at 45.51 s; members 2/3/5 deserted, then remaining 1/4 died. The driver had treated the earlier attack as the only decision and did not reaffirm during the later challenge. The next player strategy reissues the ordinary attack once for each fresh arrest stamp; no answer/state is forced. Per-member weapon, targeting and panic/cover state are now included in memory samples.



### Reinforcements after a player victory (05:40 investigation)



`20260905-052840-fight_win/1` killed the first police unit at 53.31 s.

At 67.89–70.43 s the remaining crew took hits from officers -86/-87/-75

while every personal target was null. All five crew members had real Tommy Guns.

The return-fire gate permitted police targets only for the separate defensive

intervention exemption; an outfit that started the police fight lost permission

when its original target died. The new incident record preserves permission to

answer visible incoming fire without granting that exemption. Fresh movement,

flight, surrender and a later incident remain distinct cases. Compilation and

live acceptance of this change are pending; earlier 5/5 results remain attributed

to their recorded builds.



### Per-write movement diagnosis and attack after flight (05:50)



43-case build passed all focused and companion checks at 05:43:55. Optional editor

movement events now journal each actual transform write in PedestrianAgent and

CrewWalker. They preserve the movement operation; frame endpoint flags include the

underlying writes and each write's geometry version. Initial body placement is

excluded from travel verdicts.



`20260905-054507-fight_win/1` proves a genuine pole crossing at 27.976 s:

GraphStepBlocked moved officer -8 from (112.05, 482.14) to (112.65, 482.94),

across the pole at (112.50, 482.07), half-size .17 m, on geometry version 3536.

The start was clear at the crew's travel radius but overlapped the wider graph

clearance. The old recovery selected a destination beyond the pole without proving

the relocation chord. Graph recovery now uses connected route-start repair for

centre-clear bodies and preserves their existing travel clearance along the chord.

A reproduced graph recovery fixture covers the real method and retains its order.

The same run's civilian bench flag was different: movement used geometry 3535;

the bench only existed in geometry 3536 when the end-of-frame observer ran.



Victory attempts 1 and 2 in that batch both lost. Memory also showed Fleeing still

true after later accepted attack orders. AttackOrder now explicitly ends flight;

a plain move still redirects flight. The sprint fixture tests RUN, redirect,

STOP, RUN, then KILL. These changes are awaiting fresh live repetitions.



- 05:50:13: 44 focused scenarios plus Traffic/Pedestrians/Police/Organization/Rounds

  passed, scoped errors empty. Loaded GraphRecoveryPole verified before play.

- `20260905-055024-fight_win/1`: first police unit killed at 705.23 s; player

  withdrew at 725.76 s; all crew eventually lost by 1055.77 s. Failed survival,

  no acceptance credit. Zero actual-write obstacle crossings across 295 observed

  frames and 101014 checked frame segments. All five frame-only flags remain

  recorded; the sampled examples crossed geometry introduced after their writes.

- Same run: AI gang 4 retained counsel and posted two bails; memory records

  characters 400003 and 400001 progressing Held -> Bailed. Eleven parked cars

  completed one full return cycle. This is observation evidence, not five-run AI

  court or traffic acceptance.



### Victory acceptance on 05:50 build



`20260905-055446-fight_win` passed attempts 2, 3, 5, 6, 7:

863.755432 / 827.9741 / 188.023254 / 411.474945 / 301.0668 simulated seconds.

Ordinary attacks defeated the responding police, survivors were immediately sent

into the nearest owned building, all remaining living bodies completed Inside and

were hidden, and the triggered swarm cleared. All five scoped consoles are clean;

each has zero individual-write obstacle crossings. Failed attempts 1 and 4 remain

recorded and receive no credit. The prior batch `20260905-055024-fight_win/3` was

interrupted to change player withdrawal tactics; it is not a completed trial.



A separate accelerated-fire cadence concern remains open: the on-foot, car-window

and bike firing gates currently emit at most one round per runtime frame. TommyGun

has a .14 s configured interval while these 16x runs contain >1 s simulated frames.

Shot event logging was added to the subsequent collection driver to measure actual

cadence. No gun statistics or damage have been altered to make victory easier.



### Long collection exploration (06:17, no acceptance)



`20260905-060207-collection/1` ran 14000.47 simulated seconds and failed its driver

deadline. It checked 4,552,540 frame movement segments and 14,893 runtime frames,

with zero individual-write obstacle crossings. The six frame-only flags remain.

The first real collections were at 1620.98 and 1729.12 s, not at the end of the

week: both owners refused, and repeating the same deterministic daily answer made

both protections lapse. The driver then wrongly waited for a permanent indoor

hold to end before asking new shops. This is a driver failure, not a cash payment

pass. The driver now permits a new demand from completed quarters and waits for a

new trading day after an empty round. Actual dues accrue daily.



Additional ordinary player actions are preserved in cadence-player-actions.txt

and replacement-player-actions.txt. Spare hood 1 was promoted, a Tommy Gun bought

and assigned, and he attacked nearby police. The bag detail came out to defend

home, as explicitly required by GAN-273; collectors 5 and escort 2 deserted after

being wounded (they were initially mistaken for dead). Lieutenant 4 automatically

replaced the collector with hood 3; new hood 6 was recruited and posted as escort.

A redundant NameCollector(1,3) was refused because 3 already held the bag, which

explains that command's refusal. None of these extra actions grants money or

forces game state.



The original parking observer included deliberate parked dwell in its stationary

timer. The corrected phase audit resets that timer on each mode transition.

It confirms one actual 566.45 s stationary exit wait, from the exit phase spanning

13178.33 to 13774.79 s. Other examined exit phases stayed below approximately 80 s.

The nearby parked patrol remained in the lane list, so LaneGate forecast it at road

speed. LaneGate now checks parked/wrecked/derelict bodies at the actual join and

retains acceleration reservations for stopped traffic that can resume driving.



### Collection observer correction and AI eligibility



`20260905-061838-collection/1` used ordinary new demands after protection lapsed

and waited for new daily payment answers. It collected 14 at the counter and

banked at 8856.07 s. The verdict remains FAIL: its observer wrongly assumed the

collector was BagUnit.Boss after a roster refresh. The living collector was in

Hoods instead; a paused memory query found character 5 at (215.43, 0.10, 560.52),

2.2682 m from the actual headquarters approach, beginning OpeningEntry. The

observer now resolves the round's CollectorId among all detail members. This

attempt receives no acceptance credit; fresh repetitions are required.



HouseMind candidate selection now requires an active lieutenant as well as free

hoods. The long AI journal contained approaches refused for custody and missing

tactical groups. A focused fixture checks jailed versus released leadership;

other physical refusals remain observable rather than presumed fixed.



- 06:36:53: loaded gate/AI regression methods verified; all 46 MiniCore cases and Traffic, Pedestrians, Police, Organization, Rounds, HouseMind passed with scoped errors empty. Evidence: Temp/regression-checks-20260905-063653.json. Fresh collection batch: 20260905-063704-collection.





### 06:59 build and additional reproductions



- All 48 MiniCore cases plus Traffic, Pedestrians, Police, Organization, Rounds,

  and HouseMind passed at 06:59:33, scoped errors empty. Loaded new spawn/recruit

  methods were verified. Evidence: Temp/regression-checks-20260905-065933.json.

- 20260905-063704-collection/1 passed at 8859.263 s, physically banked 14 with

  receipts and safe reconciled, zero individual-write crossings, clean console.

  Attempt 2 lost the crew at 6060.41 s and receives no credit. Its controller was

  stopped before the verdict to prevent an automatic restart with pending edits;

  the runtime player driver itself completed and wrote the retained failure.

- Attempt 1 repeatedly tried to arrest rival 400009, who stood at (221.82,29.04)

  inside blocked geometry. Police stopped 6.8 m away. All 24 tested approaches

  within 4 m of him were blocked. No arrest reach/remote-completion workaround was

  added. Recruit placement beside a hidden lieutenant was corrected instead.

- Attempt 2 also exposed original rival 600004/600005/600006 standing inside fixed

  geometry on the diagonal pedestrian link (577.5,305) -> (567.5,327.5). New crew

  bodies now validate the actual spawn after seating and fall back to clear nearby

  ground before they are admitted to the unit; an impossible spawn is discarded.

  Live embedding checks now include rival and police units as well as the player.

- Isolated Edit-mode gun fixture (not a live acceptance): actual TickShootUp,

  TommyGun .14 s interval, continuously aimed at a nearby stationary target,

  5.6 simulated seconds. .05 s steps produced 37 rounds; .8 s steps produced 7.

  Evidence: Temp/prove_gun_cadence.cs and Temp/cadence-step-proof.json. Cadence

  dependence on frame size is now reproduced, but has not yet been fixed.



### 07:41 extended court run and pending next build



- 20260905-070450-court/10: PASS 28448.01 simulated seconds at 16x. Ordinary

  quiet arrest, physical court transfer, conviction, physical prison transfer,

  sentence through day 21, release, then a visible body obeying a movement order.

  Active line and bag bodies were reconciled; manual scoped console reading clean.

  This is one fresh court pass on the 48-case 06:59 build, not five.

- The same run exposed a separate traffic failure: long vehicle 69 oscillated near

  the end of a right turn at (464.54,229.07), holding emergency patrol 143 and two

  queues for thousands of seconds. Evidence: traffic-gridlock.json. A few cm of

  reverse cleared the wedge timer, which allowed forward motion into the same

  obstruction again. Pending change latches the already-started junction reverse.

- Correct parking-phase audit on this run: longest completed exit 99.28 s;

  longest continuous maneuver wait 78.11 s. Long Driving/Returning queues above

  are real failures, separate from deliberate parked dwell.

- Late geometry was directly observed enclosing a civilian at (222.61,372.08):

  his Move ran at geometry version 3537, then version 3538 published a tree cage

  centred (222.34,372.50), half extents (.47,.47). The pending navigation change

  measures all 31 block recipes before population and keeps compact plans through

  renderer eviction. Startup CPU/memory cost still needs measurement.

- Pending next build also validates all generic pedestrian Init graph seats,

  preserves automatic-fire cooldowns across coarse steps, and interleaves foot,

  car-window and motorcycle rounds before resolving damage. None of these pending

  changes receives live acceptance credit yet. Added recruit_inside and streaming

  player scenarios, plus 53 total focused regression cases.

- Court run walker-crossings.jsonl contains two aggregate-frame observations:

  500002 moved again after its captured TickStride; that later position write is

  not yet attributed. The civilian pole crossing coincided with geometry changing

  after Move. Zero captured individual-write crossings is not proof that every

  external movement writer is safe.



### 07:52 broader checks rejected the first junction-reverse change



The latched junction reverse passed its isolated progress fixture, but the expanded

RoadSim grid batch rejected it on seed 2, boulevard: 249 overlap observations,

max depth 1.90 m, two cars frozen >60 s. A temporary baseline copy with only that

condition restored produced zero overlaps and zero terminal frozen cars on the

same seed (32 temporary >45 s stalls remain). Evidence:

Temp/road-grid-20260905-074747/2.txt and Temp/road-before-reverse/seed2.txt.

The latch was removed from production; its reproduction is retained separately

in Temp/junction_reverse_reproduction.cs.txt. The long live junction jam is OPEN.

Do not count the rejected 53-case build as accepted. Current suite returns to 52.



The broader residential streaming suite also exposed narrow rows exhausting the

12-draw packing budget. The existing 17x7 seeded fixtures need draws 13, 38, 44,

and 69 to reach the unchanged 50% occupancy rule. Row search now allows 128 draws;

other block classes retain 12. No subway returns and no spacing/coverage rule was

weakened. All eight named suites passed at 07:51:25, errors empty, before removing

the rejected traffic condition; a clean check of the resulting build follows.

The live driver now fails ordinary traffic with no net progress for 600 simulated

seconds and captures the neighbouring cars, in addition to the parking-mode audit.



### 07:54 live navigation comparison found a pooled-scale cache bug



52-case build passed MiniCore, Traffic, Pedestrians, Police, Organization, Rounds,

HouseMind and ResidentialStreaming at 07:53:43; scoped errors empty. Evidence:

Temp/regression-checks-20260905-075343.json.



20260905-075353-streaming/1 failed at 478.37 s on the second visited block.

Startup navigation held all 31 blocks (measured bake CPU 1621 ms), nine visual

views had built and one had been evicted. No embedded walkers or captured crossing

writes were observed. One storefront display measured half extents .32 in the

new visual while its permanent plan retained .30 at the exact same position.

PropFootprint cached fitted dimensions only by GameObject identity, so pooled

objects reused in differently sized openings retained stale dimensions. The next

build keys validity on effective scale and includes parent scale in measurement;

a focused scaled/reparented primitive fixture brings the suite to 53 cases again.

This is a different 53-case suite from the rejected junction-reverse build.

The streaming attempt remains FAIL and receives no acceptance credit.



### 08:11 current build verification and further streaming failures



The 54-case assembly was checked by reflection and all eight regression groups

passed with errors empty at 08:11:41. Evidence:

Temp/regression-checks-20260905-081141.json. Earlier delayed check requests had not

executed; their unchanged timestamp was not accepted. An EditorApplication.update

callback produced the fresh result.



20260905-075942-streaming/1 failed at 500.85 s because an ambient resident's carried

rubbish was harvested as static furniture. ResidentialBlockLife exposes its actor

roots to the obstacle collector, which excludes their moving descendants while

retaining actual street bins. The 54th focused reproduction covers both cases.



20260905-081213-streaming/1 then reached the 29th of 31 blocks before failing at

1685.76 s on a parked bicycle. Navigation precomputation took 1419 ms; 46 views had

built and 29 had been evicted. The bicycle had accumulated three marker-light

roots. DemoParkedCarGlow.Unregister removed rigs but left their marker meshes in

pooled hierarchies, and also retained unlit registrations. Those decorative meshes

could enlarge a fresh physical footprint. The next change removes marker roots

before pooled reuse, clears all registrations under an evicted block, and excludes

marker meshes from PropFootprint measurement. Two focused cases exercise five

scale measurements and five lit/unlit registration/recycling cycles. No live

acceptance credit is assigned until the updated build completes the scenario.



This attempt recorded three aggregate frame chords crossing props and zero

instrumented individual-write crossings. Extra transform changes outside the

current instrumented writes remain under investigation; zero recorded writes is

not proof that all NPC movement is clear. Traffic gridlock also remains OPEN.



08:18:58: The 56-case build compiled and passed MiniCore, Traffic, Pedestrians,

Police, Organization, Rounds, HouseMind and ResidentialStreaming, errors empty.

Evidence: Temp/regression-checks-20260905-081858.json. Fresh live repetitions follow.



### 08:19 build: persistent navigation passed five complete live traversals



20260905-081911-streaming/1..5 all PASS on the 56-case build, with clean console

readings. Each visited all 31 blocks through real camera panning and visual

recycling. Simulated durations: 1388.04309, 1593.51208, 1428.35693, 1410.29248,

1441.056 seconds. This accepts the static navigation/visual streaming scenario.

Aggregate frame chords still need investigation (1,3,2,1,1 respectively); captured

individual writes reported zero crossings, which does not cover external writes.



### 08:47 crew recovery and fresh live role/doorstep batches



Recruited beside an indoor lieutenant: 20260905-084234-recruit_inside/1..5 PASS

on the 56-case build (54.24, 56.00, 56.19, 53.11, 54.98 simulated seconds), clean

console and no recorded movement crossings.



Crew recovery had used the wider .45 standing clearance even after a stride

proved at .225 travel clearance. It could then select an unconnected ClearSpot.

Unwedge now preserves a clear travel point, refuses active doorway ownership,

and uses a connected local recovery for shallow overlaps; solid-centre overlaps

are refused. Its transform write now goes through the movement diagnostic hook.

Five rotated pole fixtures cover unchanged clear travel, shallow connected

recovery and refusal inside the pole core. The 58-case build compiled and all

eight regression groups passed at 08:47:24, errors empty; evidence:

Temp/regression-checks-20260905-084724.json.



On that build, 20260905-084745-roles/1..5 and

20260905-085247-doorstep/1..5 PASS with clean console and zero recorded crossings.

The earlier 08:50 doorstep startup stopped on a variable-name collision in the

new read-only junction diagnostic; no player scenario executed and no gameplay

acceptance credit or gameplay failure is assigned to that harness compilation.



### 08:56 isolated reproduction of the long live junction jam



Temp/JunctionSnapshot.cs reproduces the three leading vehicles from the court

jam, including the van's actual physical pose left short of its connector pose.

The current production baseline remains trapped for 120 simulated seconds:

van maximum displacement 1.03 m, zero overlaps. Evidence:

Temp/junction-snapshot/baseline.txt. A normal connector-aligned starting pose did

not reproduce the jam, so that easier setup was rejected as insufficient.



A temporary-source experiment has an uncommitted, stopped approach vehicle

reverse physically to its stop line when it has overshot that line and the box

refuses entry. It uses normal rear clearance (vehicles and pedestrians) and the

collision-limited lane movement. The exact snapshot then clears with zero

overlap. This is still a Temp experiment, not a production fix or live pass;

broader seed tests are in progress. It does not restore the rejected connector

reverse latch or invoke instant BackOutOfBox recovery.



### 09:06 physical stop-line recovery enters the live build



The new YieldBackToLine behavior is now in shared RoadCar. A stopped uncommitted

approach that blocks the junction from beyond its stop line backs up only within

rear clearance; an occupied rear stops the attempt. This compiled and passed all

58 MiniCore cases and seven companion groups at 09:06:18, errors empty. Evidence:

Temp/regression-checks-20260905-090618.json. The CLI reply timed out during this

check, but its callback completed and the result timestamp advanced.



Permanent model reproduction: Tools/RoadSim/JunctionYield.cs, invoked with

`dotnet run --project Tools/RoadSim/RoadSim.csproj -- junctionyield`. All ten cases

passed: five coarse/fine frame steps, with and without a pedestrian temporarily

behind the patrol. No overlap; no movement toward the rear pedestrian before he

leaves; the van then travels >100 m instead of remaining in the junction.



The expanded five-seed grid comparison found identical pre-existing congestion

in the old and new sources: seed 2 boulevard 32 waits/1290 belt hits; seeds 3,4,5

had 4,4,19 vehicles waiting >60 s at the sample end. All grid cases had zero body

overlap. These are not claimed as all-green traffic acceptance; longer boulevard

congestion remains open. Evidence: Temp/yield-back/grid-*.txt versus

Temp/road-before-reverse/grid-{3,4,5}.txt and seed2.txt.



On the preceding 58-case build, 20260905-085614-car/1..5 and

20260905-085943-recruit_inside/1..5 completed PASS with clean console and no

recorded crossings. The campaign sequencer was stopped between batches to load

the new road code. Fresh legal/combat/collection batches now follow.



### 09:18 stable junction waiting priority and varied arrest setups



Fairness compared per-car elapsed wait counters. During a shared simulation step,

one car could yield and increment its counter; the next then treated that same

car as older and yielded back. Fixed arrival timestamps and stable ID ties remove

that cycle. The five-step staggered-update regression passes; this is case 59.

The resulting build compiled and all eight regression groups passed at 09:18:29,

errors empty: Temp/regression-checks-20260905-091829.json.



The temporary five-seed comparison improved boulevard seed 5 from 19 terminal

>60 s waiters to zero, with zero overlaps/belt hits. Seeds 1 and 2 retained their

previous result; seeds 3 and 4 still have four terminal waiters each. Those

remaining cases remain open, rather than being attributed to this fixed cycle.

Evidence: Temp/junction-fairness/grid-*.txt.



20260905-090708-arrest repeatedly produced the lieutenant's natural Run answer;

those NOT_EXERCISED runs are retained and receive no quiet-arrest acceptance.

The next harness varies ordinary player choices: leader 4/3/1, nearby target

business, and a short wait before the first demand. It records recruited traits,

never changes them or the police answer. A stop-file mechanism now lets the

controller stop cleanly between completed attempts when another build is needed.



### 09:31 current-build arrest passes and AI legal observation



20260905-091910-arrest attempts 3, 4, 5, 8 and 10 PASS on the 59-case build:

297.201355, 1159.87708, 896.6175, 284.028168 and 1074.59863 simulated seconds.

All five physically booked the lieutenant, with clean console and zero recorded

aggregate or individual-write obstacle crossings. Natural Run attempts are kept

as NOT_EXERCISED, without quiet-arrest credit.



Attempt 4 observed AI recruitment, rackets, block/duty assignments, equipment,

two counsel hires and five bail payments. Those payments released five distinct

prisoners, not repeated charges for one man. One then progressed Bailed to

Sentenced on day two. Source verification confirms this is the existing shared

TryOnPaper rule for bailed defendants, invoked for each house by ProcessRosterDay;

it does not represent a physical court journey. Physical court transport remains

a separate held-defendant acceptance requirement.



### 09:36 flee failure: shared storefront door stopped admitting the leader



20260905-093140-flee/3 FAIL at 1580.54822 s: the player issued RUN at

680.31 s, the crew escaped and was billeted, and pursuit cleared. Hood 3 entered;

leader 4 remained visible with no movement order in OpeningEntry since 918.963 s.

The live shelter-diagnostic.json records both calls referencing the same closed

Storefront. One arrival's Close cancelled the other's Open. Zero measured prop

crossings; this is a doorway state failure, not an obstacle crossing.



DoorBeat now aggregates passage requests for shared physical leaves and ticks one

swing per door. Distinct visitors keep distinct calls instead of merging by door

coordinates. Strict police statement refusal remains separate. A new focused

five-pair entry/exit case accompanies the change; live acceptance is pending.



The outstanding four terminal waiters in each boulevard seed 3 and 4 were also

observed for 1800 seconds instead of 300. Both extended runs ended with zero

terminal >60 s waiters, no overlaps or belt hits, and continuing average movement

(3.7/3.8 m/s overall). They still show repeated congestion waits, so this is

evidence against permanent deadlock in those snapshots, not a claim of zero

traffic delay. Evidence: Temp/long-grid-current/seed-{3,4}.txt.



09:43:58: the 60-case build compiled and all eight regression groups passed with

errors empty. Evidence: Temp/regression-checks-20260905-094358.json. The new fixture

uses a clear location away from the preceding gunfire fixtures, so the production

under-fire refusal does not prevent its doorway setup. Live campaign 094410 now

repeats the affected flows on this build.



20260905-094411-flee/4 and /8 PASS at 820.184143 and 921.982544 simulated

seconds on the 60-case build, with clean console and zero measured crossings.

Both explicit RUN commands after a Quiet challenge ended with the complete crew

physically inside shelter and pursuit cleared. The controller stopped between

attempts to add a second legitimate input timing: the player may also issue RUN

after the lieutenant's natural Run response at 16x. Such a run still requires an

accepted player command and subsequent travel, wanted status, complete physical

shelter and cleared pursuit; natural reaction alone remains insufficient.



### 10:14 loaded damaged-shop reproduction and repair of its door behavior



095956-racket/1..5 PASS on the 60-case build through the early streamed fallback:

two smash jobs followed by a successful demand. No arson credit: each owner paid

before the probe escalated. These passes did not establish the behavior of a

fully loaded damaged storefront.



100535-racket_rendered/1 FAIL at 515.8042 simulated seconds. The driver waited

until all three target business objects were active and had renderers (317.08 s),

then used the same production orders. The first loaded smashed shop never answered

the subsequent demand. Live door-diagnostic.json records leader 4 stuck in

OpeningEntry since 374.138 s with StorefrontState=Smashed and its door shut.

Storefront.Open had refused every damage state, including broken windows.



Storefront now allows its hinges to open in the Smashed state while retaining the

damage. Five paired entry/exit fixtures cover the damaged entrance as well as the

intact shared door. The fixture also isolates LastShotAt/LastShotPos so gunfire

tests cannot refuse an unrelated later doorway setup in the same stopped editor.



The 61-case build compiled; all eight regression groups passed at 10:14:47 with

errors empty. Evidence: Temp/regression-checks-20260905-101447.json. An immediate

repeat of MiniCore also returned an empty failure list (Temp/minicore-repeat.json).

Fresh live rendered-door, overlapping-job and arson batches start in campaign

101525, followed by the outstanding combat, money, legal and lifecycle scenarios.



### 10:27 five loaded damaged-shop passes; queued-job continuation begins



101525-racket_rendered/1 was a failed player attempt: police killed the unarmed

crew before its intended outcome. That evidence is retained without credit. The

rendered scenario now completes its initial visual warmup during the default

starting pause, before recruitment and the 16x run. It still observes object

bindings/renderers in memory, never screenshots. An intermediate controller

startup failed because eval_file requires a .cs extension; no gameplay scenario

executed there. The template is now copied to Temp/loaded-door-ready.cs first.



102228-racket_rendered/1..5 PASS on the 61-case build with loaded Storefront doors,

clean console and zero recorded movement crossings. Each executes two smash jobs

and successfully demands protection from the damaged first shop. These runs end

on the owner answer; the subsequent overlap/proprietor cases additionally wait

for the physical exit and an accepted player movement command to complete.



102741-racket_overlap/1 passed the complete queued-two-jobs, demand, physical-exit

and subsequent-movement sequence in 124.483719 simulated seconds, clean console

and zero recorded crossings. Remaining repetitions are running.



### Build 81 live collector / held leader: projection failure, not accepted

`Temp/player-trials/20260905-200948-collection_held-loaded/1` failed at 1037.55 elapsed / day 4 09:00: "The crew has no bag detail on the street." The earlier command-address fix now resolves the correct collector node, but `DemoCrews.Sync` skipped the entire crew when its lieutenant was jailed and required an active line unit to recreate its bag detachment. Collector 5 and escort 2 remained Active in the roster while both disappeared from the physical projection. No live pass is claimed for build 81.

The isolated actual-Sync reproduction in `Temp/bag-sync-baseline81.json` fails with the same missing free detachment. Build 82 lets collector/escort bodies project independently of their lieutenant, retaining their original walkers and transforms; ordinary line hoods still require their active lieutenant. The new regression repeats actual booking and three roster synchronizations across five dealt worlds. Compilation and live repetitions are pending.


### Build 82 live detachment survives; paid-round acceptance still pending

`20260905-202259-collection_held-loaded/1` retains collector 5 and escort 2 through loading, court and prison transport. Both physical visits returned, but the harness incorrectly retried the first missed payment on the same trading day, causing two misses and a lapsed agreement. Its own cleanup callback recorded NOT_EXERCISED (no credit); the daily retry guard was corrected. Actual pedestrian-write crossings: 0, traffic failures: none.

`20260905-202812-collection_held-loaded/1` used the corrected daily retry and made two real visits on different days: 0/14 WasRobbed on day 4 and 0/28 PoliceWereRound on day 5. The agreement then legitimately lapsed. NOT_EXERCISED at 3918.42 elapsed; 536124 checked walk segments, zero actual or coarse obstacle crossings, no traffic failures. Lieutenant 4 was physically convicted on day 4 (11 days, due out day 15), hood 3 acquitted. The next campaign varies first collection visits across days 5 and 6, following these observed missed payments; no payment rolls are predicted or forced.

Additional grenade player scenario prepared: buy and give one charge, reject close/out-of-range throws, observe a real projectile/explosion/retirement, reject a second throw, and verify selecting a collector does not bypass ammunition use. Collector detachments are deliberately not selectable; a direct CanBombThrow(detail) probe would not represent a reachable player action and is excluded from acceptance.


### Build 82 grenade acceptance and build 83 scoped police attribution

`Temp/player-trials/20260905-204129-grenade/{1..5}`: five fresh live PASS on build 82. Each purchases/assigns exactly one grenade, refuses too-close/out-of-range throws, launches a real projectile, observes its explosion and retirement, and rejects duplicate throws/collector selection. Every run: 21.333 simulated seconds, zero traffic failures and zero actual/coarse obstacle crossings. These are short grenade flows, not extended traffic stress runs.

`Temp/swarm-attribution-baseline82.json`: all five isolated native repetitions reproduce two failures: a shooter 500m away joins an explicitly attributed swarm, and a previously hunted non-killer is upgraded to CopKilling when another crew kills an officer. This is a confirmed reproduction, not a natural-live attribution verdict. The temporary live death observer saw no officer deaths before its run ended.

Build 83 now threads the actual attacker through bullet, transfer, thrown-grenade and planted-charge deaths. Swarm entry/escalation names that culprit, the arrest answer no longer borrows a global officer-death count, and stand-down writes each crew's own wanted grade. Unattributed deaths do not accuse arbitrary recent shooters. Explosion opens its incident before reporting casualties so a fresh incident cannot clear the deaths just recorded. Two new native scenarios repeat scoped escalation, preserved extra charges, arrest/Fight, wanted grades, known death provenance and unknown-death reset five times. Compilation/native/live verification is pending.

Scope limitation still under review: ordinary civilian/gang shooting classification below the attributed swarm/open-case branch still reads the city's shared incident toll and police-shot history. Build 83 does not claim to resolve that separate multi-incident evidence issue.

### Build 83 verification and live continuation (21:25)

`Temp/regression-checks-build83.json`: all eight native groups have empty failures and errors (91 MiniCore scenarios). Runtime SHA256 `484daa257cf2b364282c660c6ead3ce0bb8425cfd08850432534384455190fb3`. `Temp/swarm-attribution-candidate83.json` passes the same five repetitions that failed on build 82.

`20260905-205250-collection_held-loaded/1` ended NOT_EXERCISED after 5359.05 simulated seconds: real day-5/day-6 visits missed and protection lapsed. Its live death log independently confirms scoped AI officer-killing charges: at t400.55 the crew-4001 detachment killed an officer and only that unit was upgraded; crew-4000's main unit then killed another officer and its non-killing detachment retained AssaultOnOfficer. No attribution assertion failed. Attempt 2 was explicitly stopped before its first collection order to run a distinct weekly-schedule scenario; no acceptance credit.

`20260905-210259-collection_held_auto-loaded/1` ended NOT_EXERCISED at t16340.43 when the lieutenant was naturally released on day 15. Sunday visits on days 7 and 14 returned 0/57 and 0/157 (PoliceWereRound), both genuinely Schedule-origin, the latter lapsed. This does not establish paid collection. The held lieutenant's free collector and escort retained bodies throughout; 2,122,297 walk segments checked, zero actual-write obstacle crossings, two coarse samples retained for review, no traffic or attribution failures. The harness-only deadline was extended from 14001.293 to 40001.293 while running, with exact values and reason preserved in harness-overrides.jsonl; no gameplay state was changed.

Prepared separate `collection_held_backup`: wait for the saved hood's real acquittal, promote him, assign a free supporting hood, change the original collector policy to Strict, threaten local businesses through the replacement lieutenant, and send the original held lieutenant's collector. Money acceptance still requires an actual paid counter visit, physical HQ return and exact ledger reconciliation. Also enabled full physical court/prison/release replays from the unmodified real Held checkpoint. Neither new flow is claimed accepted yet.

First backup attempt `20260905-212750-collection_held_backup-loaded/1` FAIL at t1264.35: harness immediately ordered another shop while the replacement lieutenant was physically Exiting the previous one; memory confirms both members alive, no custody/surrender/flee state, lieutenant door phase Exiting. This is retained as failed evidence. The corrected scenario waits for physical exit and, once a fresh agreement is restored, waits for its next trading day's dues instead of continuing unnecessary enforcement. Grimaldi's actual threat answer was Compliant; original bag's first Nowak visit returned 0/14 WasRobbed. Block-view provider independently showed collector 5/Abe Kelly, crew 1, Sundays and RoundOut true while lieutenant 4 remained Jailed (`held-block-view.json`). Zero actual-write obstacle crossings, one coarse sample, no traffic failures. No paid-round pass yet.

Harness runner now gives all held-collector scenarios the same long wall-clock allowance as weekly collection/legal scenarios. Resume also refuses to aggregate attempts from another runtime SHA or another scenario-template SHA, so five-pass totals cannot silently mix changed builds or criteria.

Build 83 corrected backup sequence `20260905-213241-collection_held_backup-loaded/1` and `/2`: PASS, each physically collected 10/14 (Short, BadWeek) from newly restored Grimaldi on day 5, then banked 10 at HQ while original lieutenant 4 was Serving. Nowak separately missed and lapsed; it did not generate fictitious money. Exact physical/ledger reconciliation passed. Both runs: no traffic failures, zero actual/coarse obstacle crossings. This is 2/5, repetitions continue.

Additional live AI observation in attempt 1: crew 5000 remained stationary while repeated police WalkingUp challenges expired at roughly 45 seconds and immediately restarted. Read-only snapshot at t2298.62 preserves suspect 500001 at (340.41, 0.10, 444.21), idle and exposed, no surrender/custody. The first probe incorrectly typed PoliceBeat as PedestrianAgent, so it did not capture the officer's body; no cause is claimed from that incomplete snapshot. In attempt 2 the same AI crew instead reached Asking/Taking normally. A corrected read-only observer is attached to attempt 2 and auto-cleans on Play exit. A wider-prop isolated stance reproduction is prepared in Temp/broad_police_stance83.cs, not yet executed; it explicitly refuses Play mode. No production stance fix or five-repeat acceptance is claimed for this finding.


### Build 83 held-collector acceptance; build 84 police stance reproduction

`20260905-213241-collection_held_backup-loaded/{1..5}`: five PASS, all on runtime `484daa257cf2b364282c660c6ead3ce0bb8425cfd08850432534384455190fb3` and template `b9461f6c88bc6cf1bb225c872373e08f56b442bea6939c7aca910c1cabe6a496`. First four bank 10 on day 5; fifth banks 14 on day 6. Fifth run had a failed day-4 court dispatch and a real day-5 retry: both men physically reached court, hood 3 was acquitted, lieutenant 4 convicted for 12 days until day 17. The collector then completed the same physical paid round while that lieutenant was Serving. All five: no traffic failures, zero actual/coarse obstacle crossings, exact safe and income reconciliation. Run-folder results and `Temp/held-backup83-progress.json` preserve each verdict. This certifies the loaded collector/replacement-enforcer scenario; it does not certify five full sentence-release or fresh automatic-round flows.

`Temp/broad-police-stance-baseline83.json`: five real rotations reproduce failure to issue any valid approach when a 2.4m-wide solid prop covers the nominal challenge spot. All five ordered/clear/near checks are false; four remain beyond questioning distance through 20 seconds, one drifts into reach without a valid issued approach. This is a confirmed isolated geometry failure; the incomplete earlier live loop snapshot does not establish it as that loop's exact cause.

Build 84 candidate now searches alternative clear positions around the suspect only if its ordinary stance/route fails. The lead's destination stays within questioning distance; the real walker route is proved before ordering. Nobody is teleported. A separate native scenario repeats the wider-prop physical approach at five rotations. Compilation/native verification and live arrest regression are pending.


Build 84 verification completed: `Temp/regression-checks-build84.json` has empty failures for MiniCore/Traffic/Pedestrians/Police/Organization/Rounds/HouseMind/ResidentialStreaming and empty errors, timestamp 21:59:05. The exact baseline fixture rerun now yields ordered/clear/near/arrived=true in all five rotations. Live loaded-court batch started next, with additional memory fields for foot-officer positions, goals, stalled-route flags, questioning-distance state and suspect position. No outcomes or calendar state are forced.

### Build 84 live moving-convoy timeout; build 85 candidate

`20260905-215946-court-loaded/1` exposed a routine 300-second drive timeout cancelling healthy moving carriers. At t943.83 car 300 was travelling 10.44m/s at (100, 0, 511.76); its still-seated prisoner was returned to Held at t947.54. Car 302 likewise remained healthy and drove 9.58m/s shortly before its own cancellation at t984.84. Both used zero traffic recoveries. The trace is preserved in `Temp/court84-transfer-timeout-evidence.json` and the run memory. Each prisoner physically retried on day 5; lieutenant 4 was convicted and reached prison, due out day 17. No physical release pass is claimed: the attempt was explicitly stopped through its scoped cleanup callback at t6318.43, NOT_EXERCISED, to fix this confirmed production failure. Zero actual-write obstacle crossings, one coarse sample, no ordinary-traffic failures.

Build 85 changes only the routine Riding deadline: progress of at least 2m renews the 300-second idle allowance, while a separate 1800-second absolute travel ceiling prevents endless moving loops. Departure and release from a deliberate roadblock initialize fresh travel windows. Pickup/boarding and exceptional ambush/foot-transfer deadlines retain their own rules. This does not move cars or people or declare an arrival. A new isolated native regression checks continued movement past 300 seconds with the same seated prisoner/escort, true idle expiration, sub-metre jitter, the absolute ceiling, and a fresh window after a deliberate blockage, across five orientations. Compilation/native/live verification is pending. Future memory logs include each convoy's idle deadline, drive ceiling and progress anchor.

Build 85 live court attempt 1 continues. Initial day-4 lieutenant dispatch failed before a convoy existed (no car allocated); day-5 court travel physically succeeded. A separate day-6 pickup by car 296 expired while it circled the source block, repeatedly traversing the same roads; raw positions/speeds are preserved in `Temp/court85-pickup-timeout-evidence.json`. This is distinct from a seated prisoner's moving-drive timeout, and no pickup fix is claimed. The next day that car successfully picked up the lieutenant and carried him to prison; sentence release is still pending. The isolated progress deadline does not cover pickup/boarding.

One recorded 32-second foot-police approach at t5595.51 was examined rather than treated as a routing failure. The officer reached its clear ordered point (218.45, 0.10, 390.98), while the AI suspect received another movement order and moved from (215.30, 0.10, 390.41) to around (224, 0.10, 379); the encounter ended with Run at t5632.84. The watcher labels long approaches for review, not automatic bugs. This does not reproduce the earlier stationary suspect retry loop.

Additional build 85 live finding: a read-only `BlockRacketSeam` provider query at 22:33:47 showed rival-house collection rounds as the player's active block round. House 3/crew3000 carried 153 and house 2/crew2001 carried 111; both views had ResponsibleCrewId=-1 and CollectorId=-1 yet RoundOut=true and RoundCarried equal to the rival's bag. `Temp/foreign-round-view-live85.json` explicitly records the observed tool response and its provenance. The provider loop filtered only collection kind and block, while its other fields are player-house figures.

A candidate player-house filter and five isolated checks are prepared in Temp/candidate86, without changing the active runtime. Tests cover a rival-only round, the player's round on another block, the player's round behind a rival entry on the same block, and removal without reviving the rival's figures. Temp/block_round_baseline85.cs compiled in Play but correctly refused to execute its isolated fixture; no baseline verdict is claimed yet. The current live court attempt will finish its real sentence release before this patch is applied. Stopfile requests a pause at that attempt boundary, not cancellation of the current run.
