# Audio: what plays, and where it came from

Every sound in the game now lives in `Assets/Audio`, baked out of the Sonniss
**#GameAudioGDC Bundle Part 9** at `C:/Users/N/sonnis` by
`Tools/audio/import_sounds.py`. The old `Assets/ci/400 Sounds Pack` and
`Assets/ci/Generated Sounds` are deleted; nothing references them any more.

The bundle is royalty-free and needs no attribution, but the vendor is recorded per
clip below anyway - a clip you cannot trace is a clip you cannot replace.

> **One clip does need crediting.** The gunshot is KuraiWolf's light machine gun off
> OpenGameArt, CC-BY 4.0. Terms and the credit line are in
> `Tools/audio/sources/SOURCES.md`, next to the file itself. The game has no credits
> screen yet; it will need one, or a line in whatever stands in for it.

## Where the sources are

`C:/Users/N/sonnis` for the bundle, and `Tools/audio/sources` for anything the
bundle could not supply. The script looks in the library first and in `sources`
second, so a manifest entry does not have to say which it is.

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
one-shots by peak. **A bad cut is fixed by moving an offset in that script, never by
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
| `horn_short/long` | SoundBits Kawasaki / Honda horns, dropped into a car's register |
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

### Weapons

| Clip | Source |
| --- | --- |
| `gunshot_1..4` | KuraiWolf, *Light Machine Gun* (OpenGameArt, **CC-BY 4.0**) |
| `gunshot_far_1..2` | the same take, low-passed to 2.8 kHz and run quiet |
| `bullet_crack` | David Dumais, Melee Weapons 2 - whip crack |
| `punch_1..4` | 344 Audio, Cinematic Fight |
| `bat_hit`, `blade_swing` | David Dumais, Melee Weapons 2 |
| `explosion` | Federico Soler, Effective Trailer Booms 2 |

### Ui - the ledger is paper and bakelite, so nothing here is designed or synthetic

| Clip | Source |
| --- | --- |
| `click` | Epic Stock Media, Board Game - vintage analogue button |
| `toggle_on/off` | Sonic Bat, Vintage Radio - power button, mode wheel |
| `page_turn`, `paper_rustle`, `map_open` | Cinematic Sound Design, Paper Foley |
| `map_close` | 344 Audio, Antique Books |
| `stamp` | Epic Stock Media, HD Lock And Mechanism - deep latch thunk |
| `type_key`, `type_carriage` | 344 Audio, Antique Typewriter |

## The two things the bundle does not have

**No firearm, anywhere in 347 files.** The gunshot is therefore not from the bundle:
it is KuraiWolf's light machine gun off OpenGameArt, committed at
`Tools/audio/sources/lmg_fire01.mp3`. It arrives mono 44.1 kHz - no resampling - and
needs exactly two things done to it. It overshoots full scale (337 clipped samples,
mp3 encoder overshoot), and a fifth of its entire energy sits below 20 Hz as a
sub-sweep nothing in a laptop can move, so a 30 Hz high-pass buys back that headroom
for free. What survives peaks at 101 Hz - the thump - and spreads properly from there
to 2 kHz.

It is one take, so `gunshot_1..4` are four renderings of it at slightly different
speeds. Baking the variation into the files rather than leaving it to runtime pitch
means two shots in a burst differ in body and decay, not only in pitch. `gunshot_far`
is the same take low-passed: distance takes the crack off a report long before it
takes the boom, so a far shot should be this weapon heard badly, not a different
recording pretending.

An LMG is a heavier weapon than the pistols the crews actually carry. It reads as a
gunshot, which the previous firework-and-whip-crack build did not, and that trade is
worth making until a pistol pack turns up.

**No siren of any kind.** `siren_loop` is synthesized: a Federal Signal style wail,
which is the American electronic siren of the period - a 4.8 s sweep between 700 and
1500 Hz with the harmonics a horn driver adds, built from a whole number of cycles so
it seams without a crossfade.

Because the clips are real now, the transpositions that used to carry them are gone:
crew gunshots played at 1.35-1.6x and screams at 1.15-1.5x, and both now play at a
jitter around unity. A recorded scream taken up a third is a cartoon.

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
- **One gun, and it is the wrong one.** An LMG report for a pistol. It is a real
  gunshot, which is the point; a pistol pack is the second thing to buy.
- Nothing in the bundle is dated. These are modern recordings of things that also
  existed in 1987 - the typewriter, the rotary phone, the radio, the V8.

## Not imported, but there if wanted

The whole library is indexed in `Docs/audio-library-index.tsv` (347 rows, vendor /
pack / file / size). Passed over for now, but obvious later: Sonic Bat's bar and
restaurant interiors, InMotionAudio's Grand Central hall (an airport terminal),
Ivo Vicic's church bells, 344 Audio's antique telephone and clocks (a 1987 office),
Jake Fielding's fridge hums (shop interiors), the motorcycle takes, the train and
tram pass-bys, and Epic Stock Media's fake radio advertisements.
