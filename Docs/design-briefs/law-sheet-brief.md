# The Law Sheet — the docket, the cells, the wanted, the counsel and the verdicts on one tab

Design brief, written 2026-09-03 from the conversation that settled it and revised the same day against the contrarian's findings. Linear: EPIC 33 (tickets `LAWUI-001..006`).

Everything the law holds against the outfit already exists in the model, but the book scatters it: a held man's case sits on HIS file, the lawyer's record on HIS, a wanted man's level nowhere on paper at all, and a closed case survives only as one free-text line on a rap sheet and a slip on the wire. The user's words: "razmišljam se dal imamo dovoljno informacija da to odvojimo u jedan tab da imamo suđenja itd" — do we have enough to give the law its own tab, with the trials and all. We do, with two model repairs first: cases are not saved today at all, and a case does not know how it closed.

## 1. What exists and is reused

| Thing | Where | Used for |
|---|---|---|
| The pipeline | `Police/PrisonPipeline.cs` — `Inside` (every `Prisoner`), `Cases` (every `CourtCase`, never pruned), `OpenCases(gang)`, `Find`, `CaseOf`, `BailPrice`, `BailRefusal`, `TryOnPaper`, `Tried`, `Forfeit`, `CutLoose`, `RestoreFrom`, `ComplainantStillTalks` | the sheet reads it; the verdict record is written by it |
| The case | `Police/CourtCase.cs` — `Deed`, `Where`, `BusinessId`, `Defendants`, `Witnesses` (`Kind`, `Standing`, `Name`, `Seed`, position, `BusinessId`), `Counts`, `OpenedDay`, `CourtDay`, `LayerId`→`LawyerId`, `Status`, `AnyTried` | the DOCKET card and the VERDICTS archive |
| The prisoner | `Prisoner` — `CharacterId`, `Deed`, `TakenOnDay`, `CourtDay`, `PrisonDay`, `Leg`, `SentenceDays`, `OutOnDay`, `Stage`, `CaseId`, `BailPaid`, `SkipOrdered` | the INSIDE column; five of these fields are not saved today |
| The trial | `Verdict.ConvictionChance(...)` / `Verdict.Decide`; `Verdict.Leaning(chance)` (four bands, no caller yet) | counsel's read of the state's case, in words |
| The fear gate | `PoliceForce.StillTalks` → `PrisonPipeline.ComplainantStillTalks` (`Func<CourtCase,bool>`): a Connected owner testifies whatever his fear; otherwise fear under `Verdict.TestifyFearCap` | the complainant's line; the ONE oracle for "will he talk" |
| Wanted | `Police/WantedLevels.cs` — level, `HiddenDays`, `DaysToCool`, out of town, `Word(level)` (no caller yet) | the WANTED column |
| The lawyer | `Personnel/Lawyer.cs` — `Skill`, `Counsel(roster)`, `BailSkill`, `MaxSkill`; `Character.CasesWon/CasesLost` | the COUNSEL box |
| The desk | `RoadDemo/LawDesk.cs` — `PostBail`, `SkipBail`, `Skipping`, `CutLoose`, `CanCutLoose`, `BailRefusal`; every one calls `director.Touch()` | the three keys; the sheet calls the same desk the man's file calls |
| The man's file | `UI/PersonnelAlmanac.Personnel.cs` ~1587–1650 — HELD / ON BAIL band, witness counts, COUNSEL, BAIL, the three keys | stays; the sheet is the second door to the same desk, and both doors repaint from the pipeline on `dirty` / `director.Version` |
| The lawyer's file | same file ~1242 — IN COURT, HIS RECORD | stays |
| The column | `UI/PersonnelAlmanac.Classified.cs` — the lawyer's ad, `HireFromAd` | the COUNSEL box points at it when there is no lawyer |
| The wire | `RoadDemo/LawWire.cs`, `UI/WireBook.cs` | unchanged; the rail keeps printing the slips |
| The rap sheet | `Personnel/RapSheet.cs` — free-text priors, `Priors(member)` | unchanged; the archive is per CASE, the rap sheet stays per MAN |
| The book | `UI/PersonnelAlmanac.cs` — `LedgerPage`, `TabNames`, `TabFolios`, `pageRoots` (hard-coded 9), `NewPageRoot`, `LedgerV2.PageHead`, `Particular`, `LedgerV2.Button`, the scroll switch, `ComposePageTelex`, `lastRefusal` (one book-level telex line) | the ninth tab is built the way the other eight are |
| Book → map | `UI/MapTargeting.cs` `IMapTargetingSurface` (`Summon`, `Dismiss`, `CanSummon`), used by BLOCKS (`PersonnelAlmanac.Blocks.cs` ~887: `Close()` then `map.Summon()`) and by the wire's jump; `TurfMapHud` and `StrategicMapHud` both register; `DemoCamera.Ride(Func<Vector3?>)` centres the boom on a point | the jump to a witness goes through this surface, never through `TurfMapHud` by name |
| Witness pressure on the street | `RoadDemo/CrewOverlay.cs` ~1282 LEAN ON THE WITNESS, `WitnessWatch`, turf-map witness markers | the sheet JUMPS there; it files no order of its own |
| Save | `Save/CampaignSave.cs` — `PrisonerDto` (seven fields), `escaped`, `prisonRosterSeed`; `SaveTests.cs` SAVE-001 | gains cases and the five missing prisoner fields |

## 2. What is broken before the sheet exists (found by the review)

1. **Cases are not saved.** `PrisonPipeline.RestoreFrom` restores `_inside` and `_everEscaped` only; `_cases` and `_nextCaseId` are never written or read; `PrisonerDto` carries seven of twelve fields (no `CaseId`, `Leg`, `PrisonDay`, `BailPaid`, `SkipOrdered`). A man saved HELD with two witnesses loads with `CaseId = -1`, and on his court day `Tried` takes the "NO DOCKET, NO DEFENCE" branch: no roll, convicted regardless of the witnesses the player leaned on; the map's witness markers are gone; a bailed man loads with no bail paid and no skip order. This is a GAN-245 defect with or without the tab.
2. **A case does not know how it closed.** `CaseStatus.Tried` is stamped on cases nobody was tried on (`AttachOpenComplaints`, `ReBook`, `DropDefendant` when the last man was cut loose), and `Forfeit` leaves the case `Open` with the skipped man in `Defendants` forever — nothing resolves him; a re-arrest books him on the new incident's case and the old one dangles. The per-defendant outcome lives only as rap-sheet text and a wire slip.
3. **Three word tables would exist for three things that already have one.** `Verdict.Leaning` (four bands) and `WantedLevels.Word` exist with no caller; the man's file prints HELD for five `PrisonStage` values. Two doors with two vocabularies is the state drift the user asked about.

## 3. The model

**Closure and the verdict on the case (LAWUI-001).**
* `CourtCase.Verdicts` — `List<CaseVerdict>`; `CaseVerdict { CharacterId, Outcome, Days, OutOnDay, Day }`; `enum CaseOutcome { Convicted, Acquitted, Dismissed, BailForfeit, CutLoose }`. Written by the pipeline and by nothing else: `Tried` per defendant, the dismissal (one line per defendant, `Dismissed`), `Forfeit` (`BailForfeit`), `CutLoose` (`CutLoose`). The rap sheet keeps its free text exactly as it is.
* `CaseStatus.Folded` (appended): a case whose counts were folded into a later case (`AttachOpenComplaints`, `ReBook`) is `Folded`, not `Tried`. `Tried` means at least one man was tried on it; `Dismissed` means it was thrown out; `Folded` means it lives on as counts elsewhere.
* A forfeited man stays a defendant (the design's ruling: the case stays open against him), but the case cannot dangle: `DayTick` folds an open case whose every remaining defendant is skipped and whose court day is more than `Sentencing.ComplaintMemoryDays` behind (`Folded`, with the `BailForfeit` verdicts already on it), and a re-arrest of a skipped man attaches his old case as a count exactly as `ReBook` does today.
* `DropDefendant` on the last man: the case is `Tried` only if `AnyTried`; otherwise `Folded` — every man cut loose before court is not a trial.
* The archive is every case with `Status != Open`; each prints its verdict lines, and a `Folded` case prints `folded into a later case` or `lapsed — nobody left to try`.
* Contracts: `EveryCloseWritesAVerdict` (tried, dismissed, forfeit, cut loose each leave a record naming the man), `AFoldedCaseIsNotATrial`, `ASkippedManLapsesOffTheDocket`, and `SkippedBailIsWTwoAndTheMoneyIsGone` amended deliberately (the case stays open against him AND carries his `BailForfeit` line).

**Cases survive saving (LAWUI-002).**
* `CourtCaseDto` in `CampaignSave`: `caseId`, `deed`, `gangId`, `businessId`, `where`, `defendants[]`, `witnesses[]` (`kind`, `name`, `seed`, `x`, `y`, `z`, `standing`, `businessId`), `counts[]`, `openedDay`, `courtDay`, `lawyerId`, `status`, `anyTried`, `verdicts[]`.
* `PrisonerDto` gains `caseId`, `leg`, `prisonDay`, `bailPaid`, `skipOrdered`.
* `PrisonPipeline.RestoreFrom(inside, cases, escaped, nextCaseId, rosterSeed)` clears and refills `_cases` and restores `_nextCaseId`; `Capture` writes them.
* A witness restored from disk keeps its position and name; `WitnessWatch` bodies are not restored (the pedestrian is gone) — the marker stands at `Position`, as it does today for a witness who walked indoors.
* Contract in `SaveTests`: `SAVE-00x: a case, its witnesses and its verdicts come back the same`, and `Find(id).CaseId` survives; `Pipeline.Cases.Count` is equal after a round trip.
* No UI ticket starts before this one is green.

**One word table each (inside LAWUI-003).**
* `LedgerText.StageLabel(PrisonStage)` — `in the cells` / `on the road` / `in prison` / `on bail` / `skipped bail`; the man's file (`Personnel.cs` ~1605) becomes its first caller, so HELD stops covering five states.
* `WantedLevels.Word(level)` for the WANTED column and the file's chip alike.
* Counsel's read uses `Verdict.Leaning`'s four bands with the strings moved to `LedgerText`: `THEY HAVE ALMOST NOTHING` (< 0.3) / `IT COULD GO EITHER WAY` (< 0.55) / `THE STATE HAS A CASE` (< 0.8) / `IT LOOKS BAD FOR HIM`; a fifth line `THEY HAVE NOBODY` when no willing witness remains, and `NO COUNSEL TO ASK` with no lawyer on the books. No number is ever printed.

**The collector (LAWUI-003).** `Police/LawSheet.cs` (new, pure, UnityEngine-free): `LawSheet.Collect(pipeline, roster, gangId, today, lawyerSkill, complainantStillTalks, into)` → `DocketEntry` (case, defendant lines, witness lines, counsel read, counsel), `InsideEntry`, `WantedEntry`, `CounselEntry`, `VerdictEntry`. The fear gate is the pipeline's own `Func<CourtCase,bool>` (`ComplainantStillTalks`), never a fear scalar: the complainant's line reads `may not testify — frightened` when it returns false, and the read is computed on the witness list the court will actually hear (the silenced complainant removed), so the sheet and the court agree. This is the `WireBook.Collect` pattern: one pure collector, the page paints it, the bench prints it.

## 4. The sheet (LAWUI-004)

**THE LAW** — the ninth tab, last in the strip after FAMILIES (measured: the strip has 1166 units at reference, eight tabs take 873.5, THE LAW adds 92.4 — it fits; at a narrow aspect the mask clips the last tab first, which is acceptable for the newest sheet). Folio 17; `pageRoots` sized off the enum, not the literal 9. Head: `THE LAW` · `THE DOCKET, THE CELLS AND THE MEN WHO ARE NOT HOME · AS THE PRECINCT HAS IT`. Same chrome as every page. Content frame is the book's (`PageLeft 42`, `PageTop −288`, `PageBottom −970`); column widths are measured against the fixture when built, never guessed.

Left, about three fifths, scrolling — **THE DOCKET**. One card per open case of ours, soonest court day first, complaints with nobody taken last:

* charge (`Sentencing.ChargeFor(deed)`) · where · `OPENED DAY n` · `COURT DAY n (k days)`, or `ON THE DOCKET · nobody taken`; `+N COUNTS` when complaints are attached
* the defendants: name · `StageLabel` or `hiding` (from `Prisoner.Stage` and `WantedLevels`) · bail `$x` or `NO BAIL` · the three keys `POST BAIL` / `SKIP BAIL` / `CUT HIM LOOSE` where `LawDesk` allows them; refusals through the desk's reasons into the book's one `lastRefusal` line
* the witnesses: kind (`the shopkeeper` / `a man on the pavement, NAME` / `the officer who saw it` / `the officer who found them`) · standing (`will testify` / `withdrawn` / `dead` / `may not testify — frightened`) · a key `LEAN ON HIM` on every witness that `CanBePressured` and is still willing
* counsel's read, in words (above); the case's counsel: name and skill, or `NONE`

Right, two fifths, three boxes stacked, each scrolling on its own:

* **INSIDE** — every prisoner of ours: name · charge · `StageLabel` · `COURT DAY n` or `OUT DAY n (k days)`; a click on the name opens his PERSONNEL file. Empty: `NOBODY INSIDE`.
* **WANTED** — every man of ours with a level: name · `WantedLevels.Word` · `hidden k of m days` or `out of town until day n`. Empty: `NOBODY WANTED`.
* **COUNSEL** — name · IN COURT stars · `n kept out · m went down` · wage; without one: `NO COUNSEL ON THE BOOKS · the column runs an ad every 7 days` and a key `THE COLUMN`.

Foot, full width, scrolling — **VERDICTS**: every case of ours with `Status != Open`, newest first: `DAY n` · charge · where · its verdict lines per man, or the folded/lapsed line.

Telex lines (`ComposePageTelex`): `n inside · m on bail · k wanted`; `court day in k days — CHARGE at WHERE` for the soonest; `no counsel on the books` (warn) or `counsel: NAME, n of 5`. The "nothing on this sheet happens at the click" line is NOT printed here: like PERSONNEL, this sheet acts at the click for exactly the desk's three operations.

Empties: `NO CASE AGAINST US`; the three boxes with their own; VERDICTS `nothing has come to court`.

## 5. The jumps (LAWUI-005)

* `IMapTargetingSurface` gains `FocusOn(Vector3 at)`; the turf map implements it with `_rig.Ride(() => at)`, the strategic map with its existing `FocusOn(world, 34f)`.
* `LEAN ON HIM`: the book `Close()`s, then `map.Summon()` and `map.FocusOn(witnessPosition)` — the same shape BLOCKS uses — and the jump is one-way: the summon does not record a return trip, so pressing P afterwards does not drop the map from under the player. The order itself is given on the street, as today.
* A name → `SetPage(Personnel)` with that man selected. `THE COLUMN` → THE PAPER with the classified tape in view.
* No `TurfMapHud` type named from `Assets/Scripts/UI`.

## 6. The bench and the paper (LAWUI-006)

* `gangsters_law_sheet` pipeline command: deal a city, open a case through the pipeline with two witnesses (withdraw one), `Book` a man on it, print the collector's rows and judge them; `TryOnPaper` a bailed man; save and load; print again and judge the archive. Its own oracle — not coupled to NIGHT-009, which is a street trace.
* Ledger.unity render check (the sticky-Pause + `DebugSeedLarge` recipe): the tab, a card with two witnesses, POST BAIL from the sheet, the man's own file showing the same words.
* `Docs/ledger-law-sheet.md`; memory; `Docs/racket-collections.md` untouched.

## 7. Rules that must hold

* Pure logic in `Assets/Scripts/Police`, covered in `Assets/Scripts/Tests/PoliceTests.cs` (the `Contracts` table; `unity command gangsters_police_tests --json`) and `SaveTests.cs`. The page is an edge and holds no state the model does not.
* Type through `LedgerStyle`, dressed through `UiSkin`; every figure derived at paint; `LedgerKit.LineBox` on every truncating line.
* One word table per thing; both doors call it. No odds as numbers.
* The verdict record is written by the pipeline and by nothing else; no second store of cases; no new statics.
* Deterministic: the same save opens to the same sheet — which is now true because the save carries the cases.
* Work on `main`; stage files explicitly; another session shares the checkout. Commit only when the user says commit; `code-review-unity` first.
* Whoever implements a ticket moves that ticket to Done in Linear when it is finished, and moves the epic to Done with the last one.

## 8. Tickets, in order

1. **LAWUI-001 — Closure and the verdict on the case** (model; Police label). `CaseVerdict`, `CaseStatus.Folded`, the lapse in `DayTick`, `DropDefendant`'s rule, the four contracts.
2. **LAWUI-002 — Cases survive saving** (defect; Police label). `CourtCaseDto`, the five prisoner fields, `RestoreFrom` with cases and `_nextCaseId`, the save contract. Gates every UI ticket.
3. **LAWUI-003 — One word each, and the collector.** `StageLabel`, `WantedLevels.Word` wired to the file, `Verdict.Leaning` strings to `LedgerText`, `LawSheet.Collect` with the pipeline's fear gate; contracts `TheDocketListsEveryOpenCaseOfOurs`, `ARivalsCaseIsNotOurBusiness`, `SoonestCourtDayFirstAndTheUntakenLast`, `TheReadIsTakenOnTheWitnessesTheCourtWillHear`, `AConnectedComplainantIsNeverMarkedFrightened`, `AWantedManIsOnTheSheet`, `TheArchiveReadsNewestFirst`, `TheFileAndTheSheetUseOneWord`.
4. **LAWUI-004 — The page.** `LedgerPage.Law`, the tab, folio 17, `pageRoots` off the enum, the head, four regions with their own scrolling, the empties, the telex lines, the three keys through `LawDesk`.
5. **LAWUI-005 — The jumps.** `IMapTargetingSurface.FocusOn`, LEAN ON HIM one-way to the witness, name → file, THE COLUMN → the paper.
6. **LAWUI-006 — The bench and the paper.** `gangsters_law_sheet`, the Ledger.unity render, the doc, memory, everything to Done.

## 9. Acceptance

* `gangsters_police_tests` green with the new contracts; `SaveTests` green with the case round trip; `gangsters_law_sheet` prints a docket with one case, two witnesses (one withdrawn), a HELD man with a bail price, and after `TryOnPaper` plus a save-and-load an archive with one conviction, one dismissal and one bail forfeit.
* In Ledger.unity: THE LAW tab renders those rows; POST BAIL from the sheet moves the man to `on bail` on both the sheet and his file and the safe drops by the bail on FINANCES; LEAN ON HIM lands the map on the witness and P afterwards leaves the map where it is.
* `recompile_status --json` clean; `gangsters_police_tests`, `gangsters_organization_tests`, `gangsters_wage_tests` green.
