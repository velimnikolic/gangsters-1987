# Task: an animation review scene

Build one scene that stands the Synty cast up and plays every animation clip in the
project, so a human can look at all of them in one pass instead of clicking through
Import Settings a hundred times.

## What is already true (verified, do not re-derive)

- **The avatar source** is `Assets/Synty/PolygonCity/Models/Characters.fbx`
  (guid `91028badc63f59f43a9c5bda64fbd608`). Its `.meta` has `animationType: 3`
  (Humanoid) and `copyAvatar: 0`, so it builds its own avatar at `fileID 9000000`.
  **Leave this file's import settings alone.** Everything else copies from or
  retargets onto it.
- **The character prefabs** (e.g. `Character_BusinessMan_Shirt.prefab`) already have an
  `Animator` with `m_Avatar` pointing at that avatar and `m_Controller: {fileID: 0}` —
  empty. The controller is the only missing piece.
- Each character is **9 SkinnedMeshRenderers** over one 59-bone skeleton (Synty's
  modular body). Do not assume a single renderer.
- The project already owns `Assets/Synty/AnimationBaseLocomotion` and
  `Assets/Synty/AnimationIdles`.

## Import rules — the two cases are different, and mixing them fails

**Mixamo clips** → Animation Type `Humanoid`, Avatar Definition **`Create From This
Model`**.

Do *not* use Copy From Other Avatar here. Mixamo prefixes every bone
(`mixamorig:Hips`), the Synty avatar expects `Hips`, and the import fails with
`Copied Avatar Rig Configuration mis-match. Transform 'Hips' for human bone 'Hips' not
found`. Retargeting still works — both sides are Humanoid, and a humanoid clip stores
muscle values, not bone names.

```csharp
imp.animationType = ModelImporterAnimationType.Human;
imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
imp.SaveAndReimport();
```

**Synty animation packs** → Animation Type `Humanoid`, Avatar Definition **`Copy From
Other Avatar`**, source = the `Characters.fbx` avatar. These share the Synty skeleton,
so the copy is valid and saves an avatar per file.

## The scene

- A row of character prefabs, one per clip, spaced far enough apart not to overlap,
  each with a world-space label showing the clip name and its source pack.
- One `AnimatorController` per clip, or one controller with a state per clip driven by
  an index — either is fine, pick the one that is less to maintain.
- Group by source (Mixamo / BaseLocomotion / Idles) along one axis so the row reads as
  a contact sheet, and put a camera on it that frames the whole row.
- Loop everything. Clips that are one-shot by nature (death, hit reaction) should still
  loop for review purposes.

## Wire it into the existing pipeline

Add this as a `[CliCommand]` in the style of `PipelineCommands.cs` — the same shape as
`gangsters_measure` — so it is reachable as `unity command gangsters_animreview --json`
without a mouse. A second command that fixes import settings across a folder
(`gangsters_animimport --dir Assets/Animations/Mixamo`) is worth having alongside it;
setting Humanoid by hand on every FBX is the actual time sink.

## Verification — numbers first, picture second

This project's own rule from `Docs/unity-cli.md`: a screenshot shows *that* something
is wrong, never *why*. Before capturing anything, assert and report:

- clip count found vs. characters instantiated (a mismatch is the bug)
- for each clip: `isHumanLegal` / whether the imported clip is actually humanoid
- each character's renderer bounds, so overlapping neighbours are caught numerically
- any clip whose avatar failed to build

Only then `screenshot` (not `capture_game_view --save_path`, which is confined to the
authoring root and imports the file into the project). Write to
`Temp/pipeline-screenshots/`. Do not shoot at `t=0` — the first frames are a flat blue
rectangle before anything is placed.

## Known unrelated issue

`Tools/unity/open-gangsters.command` is macOS-only, and this project now lives on
Windows. `Tools/play/` has `.ps1` twins for every script; the launcher does not. So the
project is being opened from Unity Hub without `-gfx-ring-buffer-size 67108864`, which
`Docs/unity-cli.md` says can freeze the scene rather than warn. A `.ps1` twin of the
launcher is a small, separate fix.
