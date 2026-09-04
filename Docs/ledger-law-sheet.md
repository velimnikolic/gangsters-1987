# THE LAW — the ledger's ninth sheet

What the state has against the outfit, on one page. Built for EPIC 33 (GAN-302, tickets `LAWUI-001..006` = GAN-303..308) on 2026-09-03. The design brief it was built from is `Docs/design-briefs/law-sheet-brief.md`; the layer it reads is EPIC 26 (GAN-245, `Docs`-less, the epic itself is the brief).

Before this sheet the same facts were scattered over the men's own files: a held man's case on HIS page, the lawyer's record on HIS, a wanted man's grade nowhere at all, and a closed case surviving only as a line of prose on a rap sheet and a slip on the wire.

## Where it is

`P` opens the book; **THE LAW** is the last tab after FAMILIES, folio 17. The page is `Assets/Scripts/UI/PersonnelAlmanac.Law.cs`, a partial like every other sheet.

## What it reads, and the one rule about it

Everything comes from `LawSheet.Collect` (`Assets/Scripts/Police/LawSheet.cs`) at paint. That collector is pure and UnityEngine-free, contracted in `PoliceTests` and printed by `gangsters_law_sheet` — the `WireBook.Collect` shape, so **what a contract proves is what the player is shown**.

The page holds no state the model does not, and writes only through `RoadDemo.LawDesk` — the same desk the man's own file calls. Two doors, one desk.

| Region | Rows | Source |
|---|---|---|
| THE DOCKET (left, ~3/5) | one card per open case of ours, soonest court day first, complaints with nobody taken last | `PrisonPipeline.Cases` where `Status == Open` |
| INSIDE (right, top) | every prisoner of ours, his charge and where in the pipe he stands | `PrisonPipeline.Inside` |
| WANTED (right, middle) | every man with a level, what for, and how long he has been out of sight | `Character.WantedLevel` + `WantedLevels` |
| COUNSEL (right, foot) | the lawyer, his stars, his record, his wage — or the want of one and a key to the column | `Lawyer.Counsel` |
| VERDICTS (foot, full width) | every closed case of ours, newest first, a line per man | `CourtCase.Verdicts` |

## Two rules worth knowing before changing anything

**The complainant's nerve is asked through the pipeline's own gate.** `PrisonPipeline.ComplainantStillTalks` (behind it `PoliceForce.StillTalks`) is the ONE oracle for "will he talk on the morning": a `Connected` owner testifies whatever the street has done to him, and only then is fear compared to `Verdict.TestifyFearCap`. A sheet that read fear alone would print `may not testify — frightened` about a man who turns up and puts the crew away.

**The counsel's read is taken on the witnesses the court will actually hear** — the silenced complainant removed first — so the sheet and the courtroom agree. A read taken on the raw list is a read of a trial that is not going to happen.

## One word table per thing

Two vocabularies for the same state is how a player reads `on the road` on one page, clicks the name, and is told `HELD` on the next with nothing wrong in the data.

| Thing | The one table | Who calls it |
|---|---|---|
| where a man stands in the pipe | `LedgerText.StageLabel` / `StageBand` | the law sheet AND the man's file's THE LAW band |
| what the city wants him for | `WantedLevels.Word` | the WANTED column AND the man's file |
| how a case looks | `Verdict.Leaning` (four bands) plus `Verdict.NoWitnessesLeft` and `Verdict.NoCounselToAsk` | the counsel's read AND the street banner |
| what became of a man | `LedgerText.CaseOutcomeLine` | the VERDICTS archive |

No odds are ever printed as a number. The player is meant to expect the court, not to know it — and the draw is fixed per man per court day, so reading the words tells him nothing about where it fell.

## What the model gained (and what was broken)

**`CaseVerdict` on the case** (`CourtCase.Verdicts`): `CharacterId`, `Outcome` (`Convicted` / `Acquitted` / `Dismissed` / `BailForfeit` / `CutLoose`), `Days`, `OutOnDay`, `Day`. Written by `PrisonPipeline` at every close and by nothing else, one line per man per case. The rap sheet keeps its free text unchanged: the rap sheet is the MAN's book, this is the CASE's.

**`CaseStatus.Folded`**: a case whose counts were folded into a later one (`AttachOpenComplaints`, `ReBook`), or every man of which was taken off it before a judge saw one, is `Folded` — not `Tried`. Before this, three code paths stamped `Tried` on cases nobody had been tried on, and the archive would have printed them as trials that happened.

**The lapse.** A man who skips his bail stays a defendant — that is the GAN-245 ruling, and it is what lets a re-arrest fold the old charge in as a count. But `DayTick` now folds an open case whose court day is more than `ComplaintMemoryDays` behind and whose every remaining defendant is out of the pipe. Otherwise the card sat on the docket for the rest of the campaign, its witness markers on the map, for a trial that could not be listed.

**The file is version 3; versions 1 and 2 are migrated.** Version 1 predates the docket. `PrisonSnapshot.MigrateFromBeforeTheDocket` puts each man still awaiting court onto a docket of his own with the one witness such a record actually amounts to: `PoliceFoundThem`, the weakest thing on it. Version 2 has the docket but no proprietor generations, so every counter replays generation zero. Version 3 stores only the small per-business generation integers; names and owner profiles are deterministically re-dealt from them.

**Cases are saved.** They were not, at all: `PrisonPipeline.RestoreFrom` restored the prisoners and nothing else, and `PrisonerDto` carried seven of twelve fields. A man saved HELD came back with `CaseId = -1`, which the trial reads as "no docket, no defence" and converts to a conviction with no roll — every witness the player had leaned on counted for nothing the moment he loaded. `Save/PrisonSnapshot.cs` is now the ONE conversion, called by `CampaignSave` and by the contract that guards it; the fixture used to hand-roll its own copy of the DTO fields, which is exactly why nobody noticed.

**A civilian body opens a murder file without a collar.** It names a house only when one recent
shooter is uniquely attributable (six seconds, forty metres); an ambiguous, unattributed or
police killing invents no case and no defendant. The same frozen pavement witnesses used by an
arrest go on that file. Indoor owner beatings carry only the complainant — nobody outside becomes
an eyewitness through a wall.

**A dead witness is gone everywhere.** Killing a proprietor marks his willing complainant name
dead on every open case for that business, including another family's. A complaint with nobody
willing can neither be tried nor folded onto a later arrest. Counts that can be folded keep their
deed's weight: `floor(BandLow / 3)`, minimum one day, with three days only as the legacy fallback.

## The jumps

* A **name** anywhere on the sheet opens that man's PERSONNEL file.
* **LEAN ON HIM** on a witness closes the book and puts the map on him. The order itself is given on the street, through the crew's existing LEAN ON THE WITNESS card — the sheet files nothing. The jump goes through `IMapTargetingSurface.FocusOn`, never through `TurfMapHud` by name, and it is **one-way**: the return trip is deliberately torn up, or opening the ledger again would call `Dismiss` and drop the map out from under the order the player came to give.
* On that witness's street card, **SHOOT HIM** walks and repaths to the moving body, plants the gunman before he draws, and resolves one round through `DemoCrews.Combat`; the ordinary death sweep removes the name from the docket.
* **THE COLUMN** on an empty counsel box turns to THE PAPER's classified tape.

## Traps this sheet cost

* **A GameObject holds one Image.** `AddComponent<Image>` on a TMP text returns **null** rather than throwing, and the caller dies on the next line, far from the cause — it took the whole paint down after the first INSIDE row and left three boxes empty. Every clickable name here gets its own surface rect through `LedgerKit.ClickSurface`, which documents the same trap in as many words.
* **`pageRoots` was a hard-coded 9.** A tenth page threw `IndexOutOfRange` at `NewPageRoot`. It is sized off the enum now.
* **Four windows, four wheel positions.** Every Law pane once fell through to the generic branch and wrote `ordersScroll`: scroll a long docket, put the pointer on a short INSIDE, and the short pane's clamp dragged the long one back to the top — and the ORDERS page inherited whatever the law sheet was last left at. Each region has its own field and its own branch, and `LawSettle` clamps each to its own run at every repaint.
* **A seeded roll is not a fixture.** The conviction contract picks a court day the stream is known to convict on and then asserts the record against the sentence the prisoner actually got, so a stream that changed its mind fails rather than passing quietly.

## Checking it

    unity command gangsters_police_tests --json     # the collector's contracts and the model's
    unity command gangsters_save_tests --json       # the docket survives a file
    unity command gangsters_law_sheet --json        # the sheet's own rows, printed and judged

The bench stages a docket, prints every row the page would paint, saves and loads between the two readings, and judges both. It is the sheet's own oracle and is deliberately not coupled to NIGHT-009, which is a street trace.
