# Claude working on Gangsters

Read `AGENTS.md` first: shared authority for CoreDemo scope, Unity access, Git
permission, implementation and verification. Read `Docs/runtime-map.md` for current
ownership. Historical snapshots are not implementation instructions.

## Claude review route

Claude uses the Codex plugin's `codex:adversarial-review`. Codex uses Claude, as
specified in AGENTS.md. Review before committing; only the user authorizes commits.
Review a fixed SHA/diff and confirm coverage of the intended files.

## Commands

    python3 Tools/project.py audit
    python3 Tools/project.py compile     # offline C#; does not contact Unity
    python3 Tools/project.py sizes

For explicitly authorized Unity access, read Docs/unity-cli.md. Do not interrupt
another session's Play or replace its scene.

## The rest

| what | where |
|---|---|
| the play harness, the trace, the reader | `Docs/play-harness.md` |
| the tactical map (the turf map: survey plate on the wheel, panel, minimap) | `Docs/tactical-map.md` |
| the city's districts | `Docs/city-districts-plan.md` |
| the canonical blocks, neighborhoods and the block graph | `Docs/territory-geography.md` |
| every source of a business, and which provider owns it | `Docs/business-inventory.md` |
| the residential storefront bay, baked Synty leaf and live states | `Docs/design-briefs/storefront-brief.md` |
| the residential module forge, measured tables, sheets, faults and showroom | `Docs/residential-forge.md` |
| the racket: collector duty, the schedule, money on the wire | `Docs/racket-collections.md` |
| the street event book: pots, gates and holds, THE PHONE, STREET TALK, the one day pass | `Docs/street-events.md` |
| the connection: the man who knows the Colombian, the broker, the test buy, the terms, the load | `Docs/connection.md` |
| the table: every word between houses, the guarded write, tribute for all, the pact's midnight | `Docs/diplomacy.md` |
| the closer threat: retargeting, reaction, bullet scatter | `Docs/design-briefs/closer-threat-brief.md` |
| the law sheet: the docket, the cells, the wanted, the counsel, the verdicts | `Docs/ledger-law-sheet.md` |
| the city wire: public-record gates, the 06:00 paper and its archive | `Docs/newspaper.md` |
| THE WIRE tab: the ruled register, the day rail, the slip at the foot | `Docs/design-briefs/wire-register-brief.md` |
| how a campaign ends: the three, and what is not one of them | `Docs/game-over.md` |
| beating the proprietor and ordering a witness killed | `Docs/design-briefs/beat-the-owner-brief.md` |
| killing and replacing the proprietor while the door remembers | `Docs/design-briefs/kill-the-owner-brief.md` |
| what everything costs (1987 dollars, Miami-anchored) | `Docs/economy-prices.md` |
| headquarters safe, stock, report and armory gate | `Docs/headquarters.md` |
| what the port is made of | `Docs/harbor-detail.md` |
| the period | `Docs/1987-period-reference.md` |
| the voices: the banks, who says what, how a man is cast | `Docs/voice-lines.md` |
| what the game owes a credit for | `Docs/credits.md` |
