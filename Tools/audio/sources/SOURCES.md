# Audio sources that are not the Sonniss bundle

Most of `Assets/Audio` is baked from the Sonniss #GameAudioGDC Bundle Part 9, which
is royalty-free and needs no attribution. This folder holds the exceptions: files
found elsewhere because the bundle had nothing for the role.

They are committed rather than downloaded on demand so that
`Tools/audio/import_sounds.py` reproduces `Assets/Audio` without a network.

**Read this before shipping.** These licences are not all attribution-free.

| File | Used for | Author | Licence | Origin |
| --- | --- | --- | --- | --- |
| `lmg_fire01.mp3` | `Weapons/gunshot_1..4`, `Weapons/gunshot_far_1..2` | KuraiWolf | **CC-BY 4.0** | https://opengameart.org/content/light-machine-gun |

## CC-BY 4.0 obligation

`lmg_fire01.mp3` requires credit. The game has no credits screen yet; when one
exists it needs a line to the effect of:

> Gunshot sound effect by KuraiWolf (opengameart.org/content/light-machine-gun),
> licensed under CC BY 4.0.

The clips derived from it are modifications (high-passed, speed-varied, trimmed,
levelled), which CC-BY permits and which the credit should not have to spell out.

Nothing else in `Assets/Audio` carries an attribution obligation.
