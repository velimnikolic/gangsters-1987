# Audio sources that are not the Sonniss bundle

Most of `Assets/Audio` is baked from the Sonniss #GameAudioGDC Bundle Part 9 at
`C:/Users/N/sonnis`. This file records everything that is not.

**No clip in the game currently carries an attribution obligation.** Both libraries
are attribution-free. Keep it that way, or add the row here and a credits line at the
same time - a licence found out later is a licence found out too late.

## The Free Firearm Sound Library

| | |
| --- | --- |
| Local path | `C:/Users/N/free-firearm-library/Prepared SFX Library` |
| Origin | https://opengameart.org/content/the-free-firearm-sound-library |
| Authors | Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney |
| Licence | **CC0** - no royalty, no credit required |
| Size | 194 MB packed, 329 MB extracted, 56 WAVs at 96 kHz / 24 bit |

Not committed: 329 MB of source for 4 MB of output is not worth carrying in git. If
the folder is missing, download the 7z from the link above and extract it to that
path - `import_sounds.py` names the exact files it wants and will tell you which one
it cannot find.

22 weapons, each recorded at two mic distances. The sheet at
`Prepared SFX Library/Prepared Master Sheet.csv` gives the weapon, calibre, action
and mic position behind every filename, which is worth reading before picking a
different take: the codes are opaque and the folder names are not enough.

Used so far: 1911, Smith & Wesson 642, Model 12, Mossberg, Carl Gustav M45, AK-47,
PPSh. Also present and unused: 1917, AR-15, Arisaka, Bersa, Charles Daly, Marlin 336,
Model 1894, Mosin Nagant, Nova, Ruger Mark III, Ruger Single Six, Savage 10, SKS,
Tikka, Walther PPQ.

## Dropped

`lmg_fire01.mp3` - KuraiWolf's light machine gun, OpenGameArt, CC-BY 4.0. It was the
gunshot for one afternoon and is superseded by the CC0 library above, which has the
actual weapons the armoury sells rather than one LMG standing in for all of them.
Deleted rather than kept: it was the only file in the project that needed crediting,
and a licence obligation attached to an unused file is a trap. Re-download from
https://opengameart.org/content/light-machine-gun if you ever want it back.
