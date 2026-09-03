# Headquarters

GAN-263 makes the player's front a real headquarters: the outfit's cash, unissued stock, front
locker and guards' issued gear are all read as things on those premises. The bank, laundering,
raids and armory flats remain later systems.

## Authorities

| Fact | Authority |
|---|---|
| total cash in the HQ safe | `Accounts.Safe` |
| dirty share / clean share | `Accounts.RiskyMoney` / `Safe - RiskyMoney` |
| receipts, payments, refunds, seizure | `BalanceMath.Receive`, `Pay`, `Refund`, `Seize` |
| desk and guards | `Roster.FrontId` and active pooled hoods |
| stock, locker and guard hands | `RosterEquipment.OwnerId` / `HolderId` |
| crews actually inside HQ | `CrewQuarters`, exposed through `IHeadquartersPhysicalSource` |
| complete UI reading | `HeadquartersReport.For(accounts, roster, inside)` |
| blocks where gear may change groups | `ArmorySites` |
| a physical crew's current block | `IOrganizationPhysicalSource.TryLocateGroup` |

`Safe` and `RiskyMoney` are not additive. The invariant is `0 <= RiskyMoney <= Safe`; clean cash
is the remainder. Illegal money is marked dirty when received, and every payment spends dirty
first. The future HQ raid calls `BalanceMath.Seize`, which takes dirty cash only.

## Surfaces

The player's `FrontOverlay` keeps LEGIT and renames the other tab THE HOUSE. It and the ledger's
THE BOSS card both read `HeadquartersReport`, including safe/dirty/clean, desk, guards, crews
inside, armory, hands and vehicles. Rival fronts retain THE BUSINESS. Finances calls the same
dirty share “Dirty cash in the safe”.

The Armory GIVE picker and lieutenant personnel card ask `PersonnelDirector` for the shared
gate. Stock-to-crew, crew-to-crew, crew-to-stock and crew-to-front transfers require the relevant
crew on an `ArmorySites` block. Front/pool operations, `NormalizeArms`, seeding and hosts with no
physical source are not gated. SEND FOR THEM issues the existing march to the HQ doorstep; it
does not queue an equipment transfer.

## Verification

Run `unity command gangsters_hq_tests --json` in the editor. The suite covers the single-safe
invariant, dirty-first spending and exact refunds, seizure, report splits/text, and all four
armory transfer gates both away from and on the HQ block. Play verification must still compare
the HQ popup, THE BOSS card and Finances after collection and midnight, then exercise refusal,
SEND FOR THEM and a successful GIVE after arrival.
