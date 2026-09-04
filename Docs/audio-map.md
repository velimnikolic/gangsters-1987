# Audio: what plays, and where it came from

Every sound in the game now lives in `Assets/Audio`, baked out of the Sonniss
**#GameAudioGDC Bundle Part 9** at `C:/Users/N/sonnis` by
`Tools/audio/import_sounds.py`. The old `Assets/ci/400 Sounds Pack` and
`Assets/ci/Generated Sounds` are deleted; nothing references them any more.

The bundle is royalty-free and needs no attribution, but the vendor is recorded per
clip below anyway - a clip you cannot trace is a clip you cannot replace.

## Where the sources are

Two libraries, neither in the repo:

- `C:/Users/N/sonnis` - the Sonniss bundle, everything but the guns.
- `C:/Users/N/krotos-gun-pack` - the Krotos Studio free gun pack `KR016`, three
  weapons in twelve takes. The bundle has no firearm in it at all.

The script looks in each in turn, so a manifest entry does not have to say which
one a clip came from. Provenance and licences: `Tools/audio/sources/SOURCES.md`.
**No SOUND in the game carries a credit line today, and the gun pack's terms are the
one thing in that file nobody has read** - settle it before the game ships. The art
is another matter: what the game owes a credit for, and the fact that there is still
no credits screen to print it on, is `Docs/credits.md`.

## Re-baking

```
python Tools/audio/import_sounds.py            # rewrites Assets/Audio/**
python Tools/audio/import_sounds.py --dry      # prints what it would write
```

Then in Unity: **Tools > City > Create or Refresh Sound Database**, which re-points
`Assets/Configs/SoundDatabase.asset` at the clips. The demo scenes read
`Assets/Audio` straight off disk through `RoadDemo.DemoSounds` and need nothing.

Nothing is copied verbatim. Sources are 96-192 kHz, 24 bit and minutes long; the
script cuts each to the useful moment, resamples to 44.1 kHz 16 bit, folds to mono
anything that plays through a 3D source, seams the loops, and levels beds by RMS and
one-shots by peak - the guns excepted, which are driven as well as levelled, see
below. **A bad cut is fixed by moving an offset in that script, never by
hand-editing a WAV** - the next re-bake would overwrite it.

Requires `numpy`, `scipy`, `soundfile`.

## The map

### Ambience - stereo loops, `CompressedInMemory`

| Clip | Source |
| --- | --- |
| `city_day` | Epic Stock Media, Urban Life Exteriors - calm courtyard street |
| `city_night` | Epic Stock Media, Urban Life Exteriors - nightlife street |
| `traffic_hum` | Epic Stock Media, Basic Transportation - downtown traffic, low-passed |
| `crowd_walla` | Epic Stock Media, Crowds Walla - metro entrance hall |
| `crowd_walla_far` | Epic Stock Media, Crowds Walla - distant walla, hard low-passed |
| `rain_city` | The Noisery, City Rain |
| `wind_gusts` | The Noisery, City Rain - gusts and vent rattle |
| `park_trees` | Epic Stock Media, Storms Lakes Parks - wind on foliage |
| `suburb_night` | 344 Audio, East Coast America - Connecticut crickets |
| `neon_hum` | 344 Audio, East Coast America - ballast hum, sped up to 120 Hz mains |
| `harbor_industry` | Victor Ermakov, Ship Repair Factory |
| `harbor_crane` | Victor Ermakov, Ship Repair Factory - crane motors |

### Traffic

| Clip | Source |
| --- | --- |
| `engine_idle_a/b` | SoundBits, Mad Mustang Mercury - steady-RPM window, looped on firing cycles |
| `engine_diesel` | Epic Stock Media, Basic Transportation - diesel idle |
| `car_door_open/close` | SoundBits, Mad Mustang Mercury |
| `tyre_skid` | SoundBits, Mad Mustang Mercury |
| `car_pass_by`, `truck_pass_by` | SoundBits, Pass-By Trains Trucks & Cars 2 |

### People

| Clip | Source |
| --- | --- |
| `footstep_concrete_1..6` | TheWorkRoom, Flip Flops - slaps only, high-passed |
| `footstep_gravel_1..4` | TheWorkRoom, Flip Flops (gravel take) |
| `whistle`, `laugh_f`, `pant`, `cry_f` | SoundBits, Vox Hominis |
| `cough`, `laugh_m`, `panic_gasp` | Epic Stock Media, AAA Character Police Officer |
| `panic_yell_m`, `panic_scream_f` | 344 Audio, Anime Fight Voices - cut to the shout, sheen rolled off |
| `hurt_f` | Epic Stock Media, AAA Character British Female Detective |
| `hurt_m` | 344 Audio, Cinematic Fight - the strain off the choking take |
| `dog_bark` | 344 Audio, Dog Vocalisations |
| `door_open/close` | InMotionAudio, USA Hotel - stairwell door |

### Police

| Clip | Source |
| --- | --- |
| `siren_loop` | **synthesized** - see below |
| `cop_shots_fired` | Epic Stock Media, AAA Character Police Officer |
| `radio_call_1..3` | 344 Audio, British Police Radio - band-limited to 400-2800 Hz |
| `radio_squelch`, `radio_static` | Epic Stock Media, Fake Advertisements |

### Interface

| Clip | Source |
| --- | --- |
| `newspaper_slap` | Sonniss, Cinematic Sound Design - Newspaper Static Foley Rummage; short hard phrase for the 06:00 sheet |

### Weapons - one set per gun the armoury sells, and they are meant to be LOUD

The guns are the one thing in `Assets/Audio` that is not simply levelled. A report
carries about 20 dB of crest, so a peak-normalised one is heard at its RMS and ends
up quieter than the traffic it is fired over. `import_sounds.slam` drives each shot
through a soft knee before normalising it, which flattens the transient, lifts the
body about 8 dB and folds some of the pack's enormous low end into harmonics a
laptop speaker can move. The demo fires them at full volume (`DemoSounds.GunVolume`)
and the city's `SoundDatabase.gunshotVolume` was already 1.

| Clip | Armoury kind | Weapon |
| --- | --- | --- |
| `pistol_1..12` | Pistol, TwinPistols | 9mm (5), the pack's light pistol (4), Desert Eagle (3) |
| `shotgun_1..8` | Shotgun | SPAS-12 - the single power blast (1), the shootout (4), the suppressive string (3) |
| `machinepistol_1..4` | MachinePistol | the pack's rapid fire, pistol calibre |
| `rifle_1..7` | Rifle | AK-47, single shots |
| `tommygun_1..4` | TommyGun | the same rapid fire at 0.86 speed - two semitones down, and a .45's bark |
| `gunshot_far_1..2` | - | the AK and the SPAS-12, low-passed to 1.8 kHz and left unslammed |

| Clip | Source |
| --- | --- |
| `bullet_crack` | David Dumais, Melee Weapons 2 - whip crack |
| `punch_1..4` | 344 Audio, Cinematic Fight |
| `bat_hit`, `blade_swing` | David Dumais, Melee Weapons 2 |
| `explosion` | Federico Soler, Effective Trailer Booms 2 |

`CrewKit.Gunshots(EquipmentKind)` maps a man's weapon to his set and
`DemoCrews.Flash` draws from it, falling back to the pistol's for anything without
one. The city's own `SoundDatabase.gunshots` stays generic and takes the pistols.

The sets are found by counting - `pistol_1`, `pistol_2` ... until one is missing -
so adding a usable report to the bake needs no C# edit.

### Ui - the ledger is paper and bakelite, so nothing here is designed or synthetic

| Clip | Source |
| --- | --- |
| `click` | Epic Stock Media, Board Game - vintage analogue button |
| `toggle_on/off` | Sonic Bat, Vintage Radio - power button, mode wheel |
| `page_turn`, `paper_rustle`, `map_open` | Cinematic Sound Design, Paper Foley |
| `map_close` | 344 Audio, Antique Books |
| `stamp` | Epic Stock Media, HD Lock And Mechanism - deep latch thunk |
| `type_key`, `type_carriage` | 344 Audio, Antique Typewriter |

## The guns, and the one thing still missing

**No firearm anywhere in the Sonniss bundle**, which is why there is a second
library. It used to be the Free Firearm Sound Library - real outdoor recordings, two
mic distances, a rifle still ringing at -20 dB a second after the report. Since
2026-09-02 it is the Krotos pack instead, and the two could not be less alike: these
are *designed* sounds, dry, over in a third of a second, with no room on them at all
and most of their weight under 120 Hz. What they have that the recordings did not is
size - they are built to be the loudest thing on a soundtrack, and the bake keeps
them that way.

Each take is a string of fire rather than one report. The individual shots are found
by transient search and read off by hand into the manifest, together with a window
short of the next shot's attack - 0.19 s for the rapid fire, 0.5 s for the SPAS-12 -
so no variant carries a second report in its tail. A shot with only 70 ms behind it
cannot be lifted out of its burst and is left in it, which is why the rapid fire
yields four variants out of twenty-two rounds.

The crews carry pistols in the demo today, so most of what you hear is the handgun
pool. The other four sets are wired and waiting on the armoury.

**Still no siren of any kind.** `siren_loop` is synthesized: a Federal Signal style wail,
which is the American electronic siren of the period - a 4.8 s sweep between 700 and
1500 Hz with the harmonics a horn driver adds, built from a whole number of cycles so
it seams without a crossfade.

Because the clips are real now, the transpositions that used to carry them are gone:
crew gunshots played at 1.35-1.6x and screams at 1.15-1.5x, and both now play at a
jitter around unity. A recorded scream taken up a third is a cartoon, and a
transposition wide enough to fake variety changes the calibre - which is the one
thing per-weapon recordings get right for free.

## Known compromises

- **Footsteps are flip-flops.** The bundle has no shoe pack. The window is short
  enough that the sandal's second flap never gets in and the body is high-passed off,
  so what is left is the slap - but a proper leather-sole pack is the first thing to
  buy.
- **Dispatch chatter is British.** The only radio voice in the bundle. The 400-2800 Hz
  passband is where a 1987 set lives anyway and it takes most of the accent with it,
  but the lines are not American. Nothing is wired to them yet.
- **`city_night` and `crowd_walla` are recorded abroad.** Both are walla under a
  street or a hall; at bed level no word survives, and `crowd_walla_far` is
  low-passed until certainly none does.
- Nothing in the bundle is dated. These are modern recordings of things that also
  existed in 1987 - the typewriter, the rotary phone, the radio, the V8.

## Removed on purpose

**Car horns.** They were a timer: every 8-20 seconds the nearest stopped car sounded
one, and at 4x speed it sounded four times as often. A city that honks on a timer
honks at nothing, and it read as a fault rather than as traffic. The clips, the
`DemoAudio.EmitHorns` pass and the `DriverNerve` panic honk are all gone; the takes
are still in the Sonniss library if a real reason to sound one ever exists.

## Not imported, but there if wanted

The whole library is indexed in `Docs/audio-library-index.tsv` (347 rows, vendor /
pack / file / size). Passed over for now, but obvious later: Sonic Bat's bar and
restaurant interiors, InMotionAudio's Grand Central hall (an airport terminal),
Ivo Vicic's church bells, 344 Audio's antique telephone and clocks (a 1987 office),
Jake Fielding's fridge hums (shop interiors), the motorcycle takes, the train and
tram pass-bys, and Epic Stock Media's fake radio advertisements.
