# The newspaper

EPIC 35 gives the city one morning paper built from public facts. `PressDesk` writes
facts; it never writes headlines. The pure `Edition`/`PressText` layer decides what
fits on the sheet, and `NewspaperSheet` is the one compositor used by both the loose
06:00 edition and the ledger archive.

## The rule

An event reaches `Underworld.Press` only after one of the two public-record gates is
open:

| Fact | Publication gate |
| --- | --- |
| Street violence | a death, or at least 3 civilian witnesses within 25 m |
| Arrest, charge, verdict, bail or custody event | the law/court has made a record |
| Statement at a business | police actually took the statement |
| Business shutdown | only `Started` or `Extended`; restore/load/expiry/repair is not news |
| Premises sale or flat raid | the public business/raid event was raised |
| Private AI, orders, targets and suspicions | never published |

Attribution is frozen when the fact is filed. A law/court identification or an
identified body is `Named`. Exactly one non-police shooting faction plus at least 3
witnesses is `Seen` ("men believed tied to ..."). Two shooting factions, too few
witnesses, or otherwise ambiguous evidence is `Unknown`. A dead gangster is named
with his family; secret state is never consulted later to improve an old story.

`StreetAlarm` shots are aggregated by incident number. The record keeps the incident's
opening time, closes after more than 45 seconds of quiet, and is also flushed at the
06:00 press cut and immediately before save. Shots after a cut form a continuation,
so one burst cannot silently move into the wrong edition.

## Book, edition and save

`PressBook` is citywide state on `Underworld`, not state owned by the player's house.
It keeps at most 256 fact records. Arrest and charge rows with the same case ID update
one public collar rather than printing twice.

Each `PressRecord` freezes the incident's `Day` and opening `Hour`, `Kind`, quarter
(`Where`), `Business`, non-police `Factions`, `NamedGangId`, `Attribution`, witness
count, gangster/civilian/officer toll, shot count, resolved `Names` and portrait
`Models`, `Deed`, sentence/outcome, case and incident IDs, and editorial `Weight`.
These are facts/identifiers only; no generated sentence is saved as truth.

An edition covers the half-open window from the previous 06:00 through the current
06:00. Public records are ordered by weight, a sufficiently important record can take
the lead, small police matters are combined under `POLICE BLOTTER`, and the remaining
space comes from the existing deterministic 1987 wire. With no public record, the
old generator produces the same page for the same seed.

Campaign files are version 3 and store `press` plus `lastEditionDay`. Files from
version 2 or earlier migrate to an empty press book and treat their saved campaign
day as already printed, preventing a false day-one popup after load. Prisoners also
store their `gangId`; old files recover it from the docket so rival verdicts resolve
against the correct house roster.

## Player flow

At 06:00, after any pending load has been applied, `NewspaperHud` opens above the city
at sorting order 130 and preserves/restores the clock's previous pause state. A new
campaign gets its day-one sheet. If the ledger or end screen is up, the edition stays
due and opens once that modal is gone.

- Click `X` or press `Esc` to put the loose sheet away.
- Press `P` while it is open to continue directly to ledger tab `THE PAPER`.
- In the ledger, `<` and `>` read retained back issues; `SAVE`, `LOAD`, and `ADS`
  remain available.

The loose sheet claims input from exactly the existing camera, street HUD, turf map,
crew overlay, arrest UI, and strategic map readers. Opening it plays
`Assets/Audio/Ui/newspaper_slap.wav` once.

## Source map

- `Assets/RoadDemo/PressDesk.cs` — scene writer, incident aggregation, witnesses and
  public event adapters.
- `Assets/Scripts/News/PressRecord.cs` — public fact schema, gates and bounded book.
- `Assets/Scripts/News/Edition.cs` and `PressText.cs` — pure selection and copy.
- `Assets/Scripts/UI/NewspaperCompositor.cs` — shared `NewspaperSheet` painter.
- `Assets/Scripts/UI/NewspaperHud.cs` — 06:00 latch, pause and popup controls.
- `Assets/Scripts/Save/PressSnapshot.cs` — v3 persistence and old-file migration.

## Traps this implementation closes

- RoadDemo pedestrians are `CivilianAgent`, so the parked-world `WitnessSystem`
  would always count zero here.
- Shooter attribution comes from every incident's `Shot.Faction`; the territory's
  short-lived private attribution cannot survive the 45-second press close.
- A loaded file remains in `CampaignSave.Pending` while its regenerated city starts;
  the popup must wait or its pause can deadlock the load.
- Restoring a shut business raises a change event, so only real `Started`/`Extended`
  changes may file a story.
- A story is dated by when its incident opened and is flushed before both edition and
  save, preventing popup/archive disagreement and lost mid-shootout facts.
- Modal readers do not share one meaning of "blocked"; only the six affected input
  paths claim this sheet.
- Court prisoners need an explicit house ID. Looking them up only in the player's
  roster silently loses rival verdicts; v2 files recover that ID from the docket.

## Automated proof

Run with the editor idle, not during a player's live Play session:

```text
unity command gangsters_news_tests --json
unity command gangsters_save_tests --json
unity command gangsters_police_tests --json
unity command gangsters_ledger_tests --json
unity command gangsters_underworld_tests --json
unity command gangsters_press --json --seed 7 --stage quiet
unity command gangsters_press --json --seed 7 --stage shootout
unity command gangsters_press --json --seed 7 --stage arrest
unity command gangsters_press --json --seed 7 --stage arson
```

The contract suite covers both gates, attribution, incident/case deduplication, the
06-to-06 window, lead and desk order, pinned historical briefs, copy budgets,
third-person voice, determinism, v3 round-trip and v2 migration. The four press
stages proof real composed output.

## Manual Play proof

These checks need the built RoadDemo world and a human reading the result:

1. Start a new game at 06:00: the day-one sheet appears after load setup, the city is
   paused, `X`/`Esc` closes it, and time resumes.
2. Press `P` on the loose sheet: the ledger opens on `THE PAPER`; back-issue arrows,
   `SAVE`, `LOAD`, and `ADS` still work.
3. Leave the ledger open across the next 06:00: the sheet waits, then opens after the
   ledger closes.
4. Stage a one-house shootout on a busy street. `[Press] FILED` should report at
   least 3 witnesses and 1 faction, and the story may describe that family as seen.
5. Stage two rival houses shooting. The paper must not choose either family.
6. Stage an arrest, an arson, and a private/unwitnessed non-fatal act. The first two
   appear once; the private act does not appear.
7. Load a later-day save at 09:00. No stale morning popup appears and load is not
   blocked.
8. Take a rival from arrest through verdict. The verdict names the rival from that
   rival's roster.
