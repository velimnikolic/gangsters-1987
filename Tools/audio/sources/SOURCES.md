# Audio sources that are not the Sonniss bundle

Most of `Assets/Audio` is baked from the Sonniss #GameAudioGDC Bundle Part 9 at
`C:/Users/N/sonnis`. This file records everything that is not.

**No clip in the game carries an attribution line today, and the Sonniss bundle needs
none. The gun pack's terms are the one thing here nobody has read** - see below. Keep
the ledger straight: add the row and the credits line at the same time - a licence
found out later is a licence found out too late.

## The Krotos Studio free gun pack (the guns, since 2026-09-02)

| | |
| --- | --- |
| Local path | `C:/Users/N/krotos-gun-pack` |
| Delivered as | `012-Krotos-Studio-Free-Gun-Shot-Sound-Effects.zip`, kept beside the folder |
| Origin | Krotos Studio's free sound packs, https://www.krotosaudio.com |
| Licence | **unread.** No licence, readme or EULA is in the zip - it is twelve WAVs and nothing else. Krotos gives these packs away royalty-free, but whether they ask for a credit is not something this repo can show, and nobody has checked. Settle it before the game ships. |
| Size | 8 MB packed, 17 MB extracted, 12 WAVs at 44.1 kHz / 24 bit stereo |

Not committed, the same rule the library before it had, and the zip sits in that
folder so the extract can be redone from the exact download.

Pack `KR016`, "3 Types of Gun Shot Sound Free - AK47, SPAS12 and 9mm". Three weapons
in twelve takes, and the takes are strings of fire rather than single reports: a
shot every 0.19 to 0.7 s. These are *designed* sounds, not field recordings - dry,
short, over in a third of a second, and mastered onto a subwoofer with a fifth of
one take sitting below 20 Hz. `import_sounds.py` high-passes at 60 and drives them
through a soft knee (`slam`), which is what makes them the loudest thing in the city.

Used: 9mm, Light Pistol, Desert Eagle, AK-47 Single Shots, SPAS12 Power, SPAS12
Shootout, SPAS12 Suppressive Fire, Rapid Fire (twice - once as the machine pistol,
once slowed to 0.86 for the tommy gun). Present and unused: AK-47 Suppressive Fire
and the three reload takes, which no weapon in the game reloads out loud yet.

## The Free Firearm Sound Library (the guns, until 2026-09-02)

| | |
| --- | --- |
| Local path | `C:/Users/N/free-firearm-library/Prepared SFX Library` |
| Origin | https://opengameart.org/content/the-free-firearm-sound-library |
| Authors | Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney |
| Licence | **CC0** - no royalty, no credit required |
| Size | 194 MB packed, 329 MB extracted, 56 WAVs at 96 kHz / 24 bit |

22 weapons at two mic distances, real outdoor recordings with real slapback on them,
and the guns were cut from it until the Krotos pack replaced them wholesale. Nothing
in `Assets/Audio` comes from it any more, so `import_sounds.py` no longer needs the
329 MB folder to run. Kept here because it is the better library of the two on
paper - CC0, in writing, with a `Prepared Master Sheet.csv` naming the weapon,
calibre, action and mic position behind every opaque filename - and because a licence
question over the pack that replaced it would send the guns straight back to it.

It had: 1911, Smith & Wesson 642, Model 12, Mossberg, Carl Gustav M45, AK-47, PPSh,
1917, AR-15, Arisaka, Bersa, Charles Daly, Marlin 336, Model 1894, Mosin Nagant,
Nova, Ruger Mark III, Ruger Single Six, Savage 10, SKS, Tikka, Walther PPQ.

## Dropped

`lmg_fire01.mp3` - KuraiWolf's light machine gun, OpenGameArt, CC-BY 4.0. It was the
gunshot for one afternoon and is superseded by the CC0 library above, which has the
actual weapons the armoury sells rather than one LMG standing in for all of them.
Deleted rather than kept: it was the only file in the project that needed crediting,
and a licence obligation attached to an unused file is a trap. Re-download from
https://opengameart.org/content/light-machine-gun if you ever want it back.
