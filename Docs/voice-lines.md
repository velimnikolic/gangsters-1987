# Crew voice lines

Every order the player can give a crew, and what the men say back. One id per line so
audio, subtitles and the trigger site can never drift apart.

## Two axes: the line and the voice

A line id says WHAT is said. It never says who says it. The clip that actually plays is
the pair:

    <bank>.<line id>          e.g. VB03.VOX_ORD_KILL_02
    Assets/Audio/Voice/<bank>/<line id>.wav

`VOX_ORD_KILL_02` is one sentence recorded once per bank, so eight men in the outfit
give the same order in eight different voices, and the same man gives it in the same
voice every time for as long as he lives.

## The banks

Eight street banks and one office voice. Each bank is one actor with a tag the roster
picks by, so a sixty-year-old caporegime is never cast with a twenty-year-old's voice.

| bank | tag | who it sounds like |
|---|---|---|
| `VB01` | young, hot | early twenties, fast, keen to go |
| `VB02` | young, steady | early twenties, quiet, does as told |
| `VB03` | prime, hot | thirties, loud, temper on the surface |
| `VB04` | prime, steady | thirties, flat, professional |
| `VB05` | prime, heavy | thirties, big man, slow and low |
| `VB06` | old, dry | fifties, bored, has done this a thousand times |
| `VB07` | old, hard | fifties, cold, the one nobody argues with |
| `VB08` | rough | any age, gravel, a wreck of a voice |
| `VBOF` | the office | the consigliere at the desk; ledger and paper acts only |

## Which man gets which voice

The bank is a property OF THE MAN, written once and never recomputed:

- `Character.VoiceBank` (a `byte`), assigned in `RosterOps` when the man is created,
  saved with him and loaded back. Derived-on-the-fly would be cheaper, but adding a
  ninth bank later would then re-cast every man in every save; a stored field cannot
  drift.
- The roll is weighted by what the man already is: age band from `BirthYear`
  (young/prime/old), and `PersonalityTrait.Temper` above the midpoint pulls to a "hot"
  bank, `Discipline` above it pulls to a "steady" one. Weighted, never forced - a
  quiet young man in `VB08` is a face the player remembers.
- Legacy saves with no field fall back to `VoiceBank = (Id * 2654435761u) % 8` once, on
  load, and the result is written back so it never moves again.
- `Character.VoicePitch` (a `sbyte`, roughly -6..+6 semitone-hundredths) rides on top,
  rolled off the same stream. Two men on `VB04` are then still two men, which is what
  keeps eight banks covering a family of forty.

## Who does the talking

One man speaks per order, never the whole unit:

| lines | speaker |
|---|---|
| every `SEL` line | the crew's boss (the lieutenant), except `VOX_SEL_FEW_01`, spoken by whoever is left |
| every `ORD` and `RKT` line | the crew's boss |
| `VOX_ORD_AWAY_*` | the man being sent away, not the boss - it is his own skin |
| `VOX_ORD_BAG_*` | the collector who picks the bag up |
| `VOX_SEL_HURT_*` | the wounded man |
| every `JOB` line | `VBOF`, the office - the order is filed at a desk, nobody is on the street yet |
| `VOX_RKT_BUY_01`, `VOX_RKT_REPAIR_01`, `VOX_RKT_HIDEOUT_*` | `VBOF` - money and paper, not men in a doorway |
| every `NO` line | the crew's boss |

## What actually gets recorded

The full grid is 110 lines by 8 banks and nobody needs 880 clips. Record in tiers:

| tier | line ids | banks | clips |
|---|---|---|---|
| core - heard every minute | `SEL_READY`, `ORD_MOVE`, `ORD_RUN`, `ORD_KILL`, `ORD_COVER`, `ORD_BOARD`, `ORD_OUT`, `NO_GENERIC` | all 8 | 8 x 22 = 176 |
| street - heard every session | the rest of `ORD`, `RKT_APPROACH/DEMAND/THREAT/COLLECT`, remaining `SEL` | all 8 | 8 x 30 = 240 |
| trade - one job, once in a while | `RKT_SMASH/TORCH/ROB/GUARD/SHAKE/HOLDOUT`, `NO_ARMS/REACH/MEN` | 4 banks, shared across the other four | 4 x 15 = 60 |
| office | every `JOB` line, `RKT_BUY/REPAIR/HIDEOUT` | `VBOF` only | 27 |

First pass to hear it in the game: core tier on 4 banks (88 clips) plus the office (27).

## Mixing rules

- The men sit UNDER the street, at half volume (`CrewVoice.Volume` = 0.5) - one number,
  and the only place to tune how loud the outfit is. Fight lines ride 10% over it.
- One voice at a time. A second order inside 0.6 s cuts the first off rather than
  stacking two men over each other.
- Priority when they collide: `NO` beats `ORD` beats `SEL`. A refusal that gets talked
  over is a refusal the player never hears.
- Never the same variant twice running for one man; keep the last index per bank.
- Only one ORDINARY line in five is spoken at all (`CrewVoice.SpeakOneRoutineIn`), counted
  over the whole outfit: being picked, walking, running, taking cover, getting in and out of
  a car and going in and out of a building. Those are what the player does all day, and a
  man answering every one of them turns his own voice into a click sound. The thinned list
  is `CrewVoice.Routine`, and a key that is not in it is spoken every time - the kill, the
  grenade, the drive-by, the charge under the car, the run for it, the racket at the door
  and every refusal, because each of those is an event the player wants confirmed.
- The count is spent at the last moment, on a line that was otherwise about to be heard:
  an order given off-screen, or one that lost to a refusal talking over it, does not eat a
  turn - or the player would click five times for a man who never says anything.
- Distance: a crew off the far edge of the screen speaks quieter, or not at all under
  the camera's own cut-off, so a wide zoom is not a chorus.

## Id scheme

    VOX_<GROUP>_<KEY>_<nn>

| group | what it covers | trigger site |
|---|---|---|
| `SEL` | the crew is picked | `DemoCrews.Select` |
| `ORD` | a street order, given by right click or by a card row | `DemoCrews.Order*`, `CrewQuarters` |
| `RKT` | a racket order at a shopkeeper's door | `TerritoryRacketOrders` rows, through the gateway |
| `JOB` | work filed with the office in the ledger | `OrderType` in `Assets/Scripts/Outfit/Orders.cs` |
| `NO` | the order was refused | `CrewOverlay.Refuse` |

Variants inside one key are interchangeable; pick at random, never the same one twice in
a row. A line is spoken by the crew's boss (the lieutenant), not by the whole unit.

## SEL - the crew is picked

| id | when | line |
|---|---|---|
| `VOX_SEL_READY_01` | crew picked, on their feet | "Yeah, boss." |
| `VOX_SEL_READY_02` | crew picked, on their feet | "Talk to me." |
| `VOX_SEL_READY_03` | crew picked, on their feet | "We're right here." |
| `VOX_SEL_READY_04` | crew picked, on their feet | "Say the word." |
| `VOX_SEL_CAR_01` | picked while in a car | "We're in the car." |
| `VOX_SEL_CAR_02` | picked while in a car | "Behind the wheel." |
| `VOX_SEL_INSIDE_01` | picked while quartered indoors | "We're off the street." |
| `VOX_SEL_INSIDE_02` | picked while quartered indoors | "Sitting inside, boss." |
| `VOX_SEL_HURT_01` | picked with a wounded man | "We're beat up, but we're up." |
| `VOX_SEL_HURT_02` | picked with a wounded man | "We took a few. Still standing." |
| `VOX_SEL_ROUND_01` | picked while on the collection round | "We're walking the round." |
| `VOX_SEL_FEW_01` | one man left standing | "It's just me now." |

## ORD - street orders

| id | order | line |
|---|---|---|
| `VOX_ORD_MOVE_01` | walk (one right click) | "On our way." |
| `VOX_ORD_MOVE_02` | walk | "We'll walk it." |
| `VOX_ORD_MOVE_03` | walk | "Moving." |
| `VOX_ORD_RUN_01` | run (double right click) | "Double time!" |
| `VOX_ORD_RUN_02` | run | "Move, move!" |
| `VOX_ORD_RUN_03` | run | "We're running it." |
| `VOX_ORD_COVER_01` | HIDE BEHIND IT / cover click | "We'll sit behind this." |
| `VOX_ORD_COVER_02` | cover | "Down, out of sight." |
| `VOX_ORD_COVER_03` | cover | "We'll wait for them here." |
| `VOX_ORD_KILL_01` | KILL | "Consider him gone." |
| `VOX_ORD_KILL_02` | KILL | "We'll take him." |
| `VOX_ORD_KILL_03` | KILL | "He's finished." |
| `VOX_ORD_DRIVEBY_01` | MOTO DRIVE-BY | "Warm up the bike." |
| `VOX_ORD_DRIVEBY_02` | MOTO DRIVE-BY | "One pass, that's all it takes." |
| `VOX_ORD_DRIVEBY_03` | MOTO DRIVE-BY | "We ride by, he goes down." |
| `VOX_ORD_GRENADE_01` | BOMBA at a rival crew | "Fire in the hole!" |
| `VOX_ORD_GRENADE_02` | BOMBA at a rival crew | "Everybody down!" |
| `VOX_ORD_GRENADE_03` | BOMBA at a rival crew | "Say goodnight." |
| `VOX_ORD_DOORBOMB_01` | BOMBA on a rival's doorstep | "Knock knock." |
| `VOX_ORD_DOORBOMB_02` | BOMBA on a rival's doorstep | "We'll light up their door." |
| `VOX_ORD_CARBOMB_01` | PLANT A BOMB | "Charge goes under the fender." |
| `VOX_ORD_CARBOMB_02` | PLANT A BOMB | "He turns the key, it's over." |
| `VOX_ORD_CARBOMB_03` | PLANT A BOMB | "Wiring it now. Keep them off me." |
| `VOX_ORD_SHOOTCAR_01` | SHOOT IT UP | "We'll fill it with holes." |
| `VOX_ORD_SHOOTCAR_02` | SHOOT IT UP | "Nothing left of that motor." |
| `VOX_ORD_BOARD_01` | GET IN | "Everybody in the car." |
| `VOX_ORD_BOARD_02` | GET IN | "Get in, get in." |
| `VOX_ORD_OUT_01` | GET OUT | "Everybody out." |
| `VOX_ORD_OUT_02` | GET OUT | "Out of the car." |
| `VOX_ORD_FLEE_01` | RUN FOR IT | "Cops! Break off!" |
| `VOX_ORD_FLEE_02` | RUN FOR IT | "Scatter, now!" |
| `VOX_ORD_FLEE_03` | RUN FOR IT | "We're gone." |
| `VOX_ORD_AWAY_01` | SEND HIM OUT OF TOWN | "I'll lay low a while." |
| `VOX_ORD_AWAY_02` | SEND HIM OUT OF TOWN | "Put me on the bus, boss." |
| `VOX_ORD_BAG_01` | TAKE THE BAG | "I got the bag." |
| `VOX_ORD_BAG_02` | TAKE THE BAG | "Money's ours." |
| `VOX_ORD_WITNESS_01` | LEAN ON THE WITNESS | "We'll have a word with him." |
| `VOX_ORD_WITNESS_02` | LEAN ON THE WITNESS | "He saw nothing. He'll remember that." |
| `VOX_ORD_INSIDE_01` | TAKE THEM INSIDE | "Inside, off the street." |
| `VOX_ORD_INSIDE_02` | TAKE THEM INSIDE | "We'll sit this one out indoors." |
| `VOX_ORD_OUTSIDE_01` | BRING THEM OUT | "Back on the street." |
| `VOX_ORD_OUTSIDE_02` | BRING THEM OUT | "We're coming out." |

## RKT - the racket, at the door

| id | order | line |
|---|---|---|
| `VOX_RKT_APPROACH_01` | GO TO THE DOOR | "We'll pay him a visit." |
| `VOX_RKT_APPROACH_02` | GO TO THE DOOR | "Walking up to his door now." |
| `VOX_RKT_DEMAND_01` | DEMAND PROTECTION | "Time he paid for the neighborhood." |
| `VOX_RKT_DEMAND_02` | DEMAND PROTECTION | "We'll make him the offer." |
| `VOX_RKT_THREAT_01` | THREATEN THE OWNER | "He'll listen this time." |
| `VOX_RKT_THREAT_02` | THREATEN THE OWNER | "We'll lean on him a little." |
| `VOX_RKT_COLLECT_01` | COLLECT THE TAKE | "Time to collect." |
| `VOX_RKT_COLLECT_02` | COLLECT THE TAKE | "Walking the round, boss." |
| `VOX_RKT_SHAKE_01` | SHAKE DOWN THE BLOCK | "We'll work the whole block." |
| `VOX_RKT_SHAKE_02` | SHAKE DOWN THE BLOCK | "Door to door, every one of them." |
| `VOX_RKT_HOLDOUT_01` | LEAN ON THE HOLDOUTS | "The stubborn ones get a visit." |
| `VOX_RKT_HOLDOUT_02` | LEAN ON THE HOLDOUTS | "We'll change their minds." |
| `VOX_RKT_SMASH_01` | SMASH IT UP | "We'll wreck the place." |
| `VOX_RKT_SMASH_02` | SMASH IT UP | "Break everything in there." |
| `VOX_RKT_TORCH_01` | TORCH IT | "It burns tonight." |
| `VOX_RKT_TORCH_02` | TORCH IT | "Bring the gasoline." |
| `VOX_RKT_ROB_01` | ROB IT | "We'll clean out the register." |
| `VOX_RKT_ROB_02` | ROB IT | "In and out with the cash." |
| `VOX_RKT_GUARD_01` | SIT ON IT | "We'll sit on it." |
| `VOX_RKT_GUARD_02` | SIT ON IT | "Nobody touches the place." |
| `VOX_RKT_BUY_01` | BUY IT OUTRIGHT | "The papers get signed today." |
| `VOX_RKT_REPAIR_01` | PAY FOR REPAIRS | "Glass and paint, it's covered." |
| `VOX_RKT_HIDEOUT_01` | MAKE THIS THE HIDEOUT | "This is where we go to ground." |
| `VOX_RKT_HIDEOUT_02` | NO LONGER THE HIDEOUT | "We don't run here any more." |

## JOB - work filed with the office (ledger orders)

One line each; the lieutenant answers when the order is filed, not when it resolves.

| id | `OrderType` | line |
|---|---|---|
| `VOX_JOB_EXTORT_01` | Extort | "We'll put them on the payroll." |
| `VOX_JOB_INTIMIDATE_01` | Intimidate | "They'll learn who runs this street." |
| `VOX_JOB_COLLECT_01` | CollectProtection | "We'll bring the money home." |
| `VOX_JOB_ADJUST_01` | AdjustProtection | "New price, starting this week." |
| `VOX_JOB_ASSAULT_01` | Assault | "Somebody gets hurt today." |
| `VOX_JOB_SMASHUP_01` | SmashUp | "There'll be nothing left standing." |
| `VOX_JOB_RAID_01` | Raid | "We go in heavy." |
| `VOX_JOB_TORCH_01` | Torch | "We'll leave it burning." |
| `VOX_JOB_BOMB_01` | Bomb | "It goes up tonight." |
| `VOX_JOB_KILL_01` | Kill | "He won't see next week." |
| `VOX_JOB_KIDNAP_01` | Kidnap | "We'll take him quiet." |
| `VOX_JOB_PATROL_01` | Patrol | "We'll walk the streets, keep them ours." |
| `VOX_JOB_GUARD_01` | Guard | "Nothing happens on our watch." |
| `VOX_JOB_AMBUSH_01` | Ambush | "We'll be waiting for them." |
| `VOX_JOB_EXPLORE_01` | Explore | "We'll go see what's over there." |
| `VOX_JOB_BUY_01` | BuyPremises | "We'll buy the whole building." |
| `VOX_JOB_SETUP_01` | SetUpBusiness | "We'll open it up ourselves." |
| `VOX_JOB_RUN_01` | RunBusiness | "The place runs, we take our cut." |
| `VOX_JOB_AUDIT_01` | Audit | "I'll go through the books." |
| `VOX_JOB_RECRUIT_01` | Recruit | "I know a few guys." |
| `VOX_JOB_BRIBE_01` | Bribe | "Everybody's got a price." |
| `VOX_JOB_POLICE_01` | EmployPolice | "We'll buy a badge or two." |
| `VOX_JOB_DONATE_01` | Donate | "A little goodwill never hurt." |

## NO - the order is refused

Spoken over the refusal copy the card already shows; keep them generic, the card carries
the reason.

| id | when | line |
|---|---|---|
| `VOX_NO_GENERIC_01` | any refusal | "Can't do that, boss." |
| `VOX_NO_GENERIC_02` | any refusal | "Not from here." |
| `VOX_NO_GENERIC_03` | any refusal | "No chance." |
| `VOX_NO_ARMS_01` | no weapon or no grenade | "We got nothing to do it with." |
| `VOX_NO_REACH_01` | too far, no way through | "We can't get to him." |
| `VOX_NO_MEN_01` | nobody left to send | "There's nobody left to send." |

## The fight

What a man says when it turns into shooting. Written for the street's own events - a
bullet lands, a man goes down, the law arrives - so every line has something that fires it
rather than being atmosphere on a timer. Not yet recorded: the sheet rows are in
`lines.csv` (groups HIT, DOWN, FIGHT, SPOT, DROP, LOSS, PIN, WARN, SURR, LAW, WIN), 48
lines, 384 clips over the eight street banks.

| id | fired by | line |
|---|---|---|
| `VOX_HIT_TAKE_01..06` | `CrewWalker.TakeHit`, survivor | "Agh!" / "Unh! Damn it!" / "I'm hit!" / "He got me!" / "Son of a bitch!" / "Agh! That's blood!" |
| `VOX_HIT_BAD_01..03` | `TakeHit`, one point of health left | "I'm bleeding bad!" / "I can't take another one!" / "I'm hurt, I'm hurt bad!" |
| `VOX_DOWN_CRY_01..04` | `CrewWalker.Kill`, spoken as he goes down | "Aaagh!" / "No... not like this..." / "Unh... you bastards..." / "Agh... Mother of God..." |
| `VOX_FIGHT_CURSE_01..08` | `DemoCrews.FireFrom`, one shot in six | "Come on, you bastards!" / "Eat it!" / "That all you got?" / "Son of a bitch, stay down!" / "You want some? Here!" / "Die already!" / "Damn you!" / "Nowhere to run now!" |
| `VOX_SPOT_CONTACT_01..04` | `CrewWalker.Engage`, on a new mark | "They're on us!" / "Guns! Get down!" / "We got company!" / "Over there! Shooters!" |
| `VOX_DROP_GOT_01..04` | `CrewSpeech.Fell`, the killer | "He's down!" / "Got him!" / "That's one." / "Finished. Next." |
| `VOX_LOSS_MAN_01..04` | `CrewSpeech.Fell`, his nearest crewmate | "They got him!" / "He's down! He's down!" / "We lost one!" / "Bastards killed him!" |
| `VOX_PIN_HELD_01..03` | `TakeHit` while holding cover | "We're pinned!" / "Too much fire!" / "Keep your head down!" |
| `VOX_WARN_CALL_01..03` | `BombProjectile.Throw`, nearest man of another family | "Grenade!" / "Take cover!" / "Look out!" |
| `VOX_SURR_HANDS_01..03` | `DemoCrews.GiveUp` | "Don't shoot! Don't shoot!" / "We're done! Hands up!" / "All right, all right! You got us!" |
| `VOX_LAW_HEAT_01..03` | NOT WIRED YET - no clean seam for the law arriving on a fight | "Cops!" / "The law! Move!" / "Heat's here! Break it off!" |
| `VOX_WIN_OVER_01..03` | `CrewSpeech.Fell`, when that death wipes the crew | "That's the last of them." / "Street's ours." / "It's over. Let's move." |

Recorded and in the project. Generate more with the same pipeline: `generate.py --only-group HIT DOWN FIGHT SPOT DROP
LOSS PIN WARN SURR LAW WIN`, copy `voice/` into `Assets/Audio/Voice/`, re-run the bake.

### How the fight is kept quiet enough to listen to

An order is one click and one answer. A fight is forty men firing at once, so these lines
are paced by three rules on top of each other, and the first is the one that matters:

- **THE WHOLE FIGHT SPEAKS THROUGH ONE MOUTH.** One combat line every 1.5 s (`CombatGap`),
  whoever gets there first; every other man hit in that window says nothing at all. Fifty
  men each shouting once is still fifty shouts, and per-man pacing does nothing about it.
  A death may come closer - 0.67 s (`DeathGap`) - and nothing else may talk over one.
- **A fight is a tenth louder** (`CombatVolume` = 0.55 against 0.5): a man giving an order
  is talking to the boss, a man hit is shouting over gunfire.
- **Per man, per kind**, under that budget: a hit every 2.5 s, a curse every 8 s and only
  on one shot in six, "they're on us" once in 25 s, a kill brag every 3 s, a grenade
  warning every 5 s and only from the nearest man inside 22 m.
- **Priorities**: `Death` > `Refusal` > `Combat` > `Order` > `Selection`. The refusal sits
  above the fight on purpose - a firefight must not swallow the reason an order did
  nothing - and the dying cry sits above everything, because it is how the player is told
  he has lost somebody.

Everybody on the street cries out, not only our men: a rival and an officer have no roster
entry, so their bank is dealt off their own body number (`VoiceCasting.BankForSeed`) rather
than written down. One of our own screams in the voice his ledger entry was cast in - the
man who says "on our way" is the man who yells when he is hit.

## What is in the project

The recordings are in, and the game speaks them. 667 clips: 80 lines on each of the eight
street banks, 27 on the office.

| what | where |
|---|---|
| the takes | `Assets/Audio/Voice/<bank>/<line id>.wav` (44.1 kHz, 16 bit, mono) |
| the sheet they were cut from | `Assets/Audio/Voice/lines.csv`, and the run's own log beside it |
| import settings | `SoundPackImportSettings` - Vorbis, forced mono, CompressedInMemory for the voice folder (667 clips decompressed would be about 60 MB of RAM to say "moving") |
| the baked asset | `Assets/Configs/Audio/Resources/VoiceDatabase.asset` |
| re-bake it (it throws the live lookup away as it goes, or the session answers out of the old index) | `Tools/City/Create or Refresh Voice Database`, or `unity command menu --path "Tools/City/Create or Refresh Voice Database"` |
| the keys, by name | `Assets/Scripts/Data/VoiceLines.cs` - nothing else spells a key as a literal |
| who speaks in which voice | `Assets/Scripts/Gameplay/VoiceCasting.cs`, written onto `Character.Voice` and saved |
| the speaker | `Assets/Scripts/Gameplay/CrewVoice.cs` (`Say`, `Office`) |
| the street's end of it | `Assets/RoadDemo/CrewSpeech.cs` - unit to lieutenant to line |

The casting sheet lives in the baker (`VoiceAssetBootstrap.Casting`): actor name, age band,
temper, and which bank is the office. Adding a ninth actor is a folder and a row.

Because the WAVs are 81 MB, `Assets/Audio/Voice/**/*.wav` is tracked with git-lfs.

## Where the lines are actually spoken from

| line | hung off |
|---|---|
| `SEL_*` | `DemoCrews.Select` |
| `ORD_MOVE` / `ORD_RUN` | `DemoCrews.OrderUnit` - and the automatic movers (the round, a filed job, the bag going home) pass `speak: false`, so only orders the player gave are answered |
| `ORD_KILL` | `DemoCrews.OrderAttack` |
| `ORD_DRIVEBY` | `DemoCrews.OrderDriveBy` |
| `ORD_GRENADE` / `ORD_DOORBOMB` / `ORD_CARBOMB` | `DemoCrews.OrderBombThrow` / `OrderBombFront` / `OrderPlantBomb` |
| `ORD_SHOOTCAR` | `DemoCrews.OrderShootCar` |
| `ORD_COVER` | `DemoCrews.OrderAmbush` |
| `ORD_BOARD` / `ORD_OUT` | `DemoCrews.OrderCar` / `OrderOut` |
| `ORD_FLEE` | `DemoCrews.OrderFlee` (its own run order stays quiet) |
| `ORD_AWAY` | the row that puts him on the bus, in HIS voice |
| `ORD_BAG` | `BagCarry.Claim` |
| `ORD_WITNESS` | the witness card (its walk order stays quiet) |
| `ORD_INSIDE` / `ORD_OUTSIDE` | `CrewQuarters.Station` / `BringOut` |
| `RKT_APPROACH/DEMAND/THREAT/COLLECT` | `CrewOverlay.Submit`, which is where the street card and the paper map both submit |
| `RKT_SHAKE/COLLECT/HOLDOUT` (block orders) | `TerritoryRuntime`'s block-racket seam - the ledger's block file and the map's block menu come through it |
| `RKT_SMASH/TORCH/ROB/GUARD` | `CrewOverlay.FileDoorJob` |
| `JOB_*`, `RKT_BUY/REPAIR/HIDEOUT` | `OutfitDirector.IssueOrder` and `DoorMenu` - the desk, in `VBOF` |
| `NO_*` | `CrewOverlay.Refuse`, which picks one of the four off the words of the refusal |

## The shape of the hook

One shared speaker, never per scene: `CrewVoice.Say(lineId, speaker)` - where `speaker`
is the CHARACTER, not the unit, because the bank hangs off `Character.VoiceBank` and the
pitch off `Character.VoicePitch`; the call resolves the clip as `<bank>.<lineId>`, falls
back to `VBOF` for the office lines and to a silent no-op when that bank has not recorded
that line yet, so an unfinished tier never throws. It is called from the order
methods themselves (`DemoCrews.OrderSelected` / `OrderAttack` / `OrderDriveBy` /
`OrderBombThrow` / `OrderPlantBomb` / `OrderShootCar` / `OrderAmbush` / `OrderFlee` /
`OrderCar` / `OrderOut`, `CrewQuarters.Station` / `BringOut`, `BagOnGround.Claim`,
`WitnessWatch.OrderLean`) and from the gateway submit for the racket rows. Hooking the
order methods rather than the card rows means the street card, the paper turf map and the
ledger all speak with one voice, and nothing has to be repeated per scene.
