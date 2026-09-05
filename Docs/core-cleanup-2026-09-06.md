# CoreDemo cleanup — 2026-09-06

This is a first, verified source-cleanup stage, not a claim that all unused code
has been removed or that gameplay/visual acceptance has passed. CoreDemo is the
game; freeway/expressway, harbor and airport and their dependencies are retained.

## Removed and replaced

- `Assets/Synty/InterfaceModernMenus` and its root meta: approximately 257 MiB.
  Recoverable copy: `/Users/velimirovixxx/Gangsters-cleanup-1A5SGT/InterfaceModernMenus`.
  Tracked originals also remain in Git history; no commit was made by this task.
- `StrategicMapHud`, `CityOverlayHud`, `BlockOverlayHud`, `CityClockHud` and their
  metas: 3,201 C# lines. Their old bootstrap and remaining call sites were removed.
  Current Ledger, personnel, order menus, TurfMap and CoreDemo HUD stay.
- ModernMenus icon/panel loaders and the building-card triangle tail. Shared
  BuildingCardPicker click/veto/highlight behavior remains. Native Ledger textures
  and procedural TurfGlyphs supply replacement icons. The two venue prefabs and
  lettering generator/material now use the existing LiberationSans SDF font/atlas.
- Residential decor's 10,609-line generated C# table became an 87-line typed loader
  and `Assets/Resources/ResidentialDecor.json`. All 1,034 rules, all 30 fields per
  rule, 233 unresolved diagnostics and ordering were preserved by mechanical parse
  and round-trip comparison. Data has moved out of compiled source, not vanished.
  The generator writes JSON; CoreSim embeds the same file. Generation invalidates
  both catalog and facade caches. Reflection-based reads became direct typed reads.

## Agent and verification guardrails

- Shared AGENTS.md, current Docs/runtime-map.md, aligned CLAUDE.md and /feature.
  No task-specific Unity permission means no editor access; commits require the
  user's request. Multiple collaborating classes and task-appropriate acceptance
  replace the old one-class/universal-soak assumptions. Cross-model review stays.
- `Tools/project.py`: offline two-assembly C# compilation, deleted-GUID audit and
  source-size budgets. Existing oversized files/whole partial families may not grow;
  default budgets for new files/families are 1,000/1,500 lines. This is a ratchet,
  not completed architectural decomposition or an assembly-boundary guarantee.
- The mission gate is now offline and requires explicit compile/run evidence tied
  to current source, finished run, stable source and console counts. Missing,
  aborted, malformed and zero-activity evidence cannot pass this gate. Other
  scenario-specific readers still need individual review; this is not a universal
  gameplay oracle. Old implicit run directories/text soak tallies are not accepted.
- Compile console/status parsing rejects unread or lost evidence. Recompile uses
  python3 and requires `--allow-unity` acknowledging the user's permission.
- CI definition runs Python evidence regressions and source-size budgets only.
  No remote CI execution or Unity build is claimed.
- Claude review tooling accepts explicitly scoped frozen inputs and rejects an
  oversized input instead of silently reviewing a truncated asset-heavy diff.

## Verification and limits

- Deleted-asset audit: 1,973 deleted GUIDs checked; no surviving serialized reference.
  C# and JSON search found no remaining ModernMenus loader paths.
- Offline runtime/editor compilation passed (822 / 135 source files), with 99 / 32
  warnings. Output and source stamp:
  `/var/folders/vw/8l6s3m4j09jczzycswr_xqmm0000gn/T/gangsters-compile-tnvuk28h`.
  The final fingerprint includes C# and Assets JSON, Packages and ProjectSettings.
- Facade model regression: 990/990 clean sheets, deterministic repeats, 33/33 named
  contracts and 14/14 deliberate fault-detection cases. After cache invalidation
  changes, a fresh 33-sheet run passed the same contracts.
  The machine lacks the requested .NET10 SDK: these used a separate .NET8
  verification copy of CoreSim's project with identical source inputs and embedded
  JSON, under the recovery directory. The project's .NET10 target is unchanged;
  this is not a .NET10 build verdict.
- Nine offline evidence-regression tests and the source-size budgets passed.
- Claude cross-model review was started with a scoped 256 KiB input (excluding
  the huge package/generated-table deletion), then stopped at the user's request.
  No completed cross-model review verdict is claimed.
- No Unity/editor/Play access, scene regeneration, Player build or visual check.
  In particular, validate the replacement icons, popup layout, venue lettering
  and Unity Resources import when the user authorizes that review.

## Remaining work

Remaining LivingCity code is not safe to delete by namespace: shared personnel,
business, combat and catalog types still have CoreDemo consumers. Further removal
requires tracing each consumer and serialized/string reference. The giant live
classes (PersonnelAlmanac, RoadDemoBuilder, DemoCrews, CrewWalker, RoadCar) need
responsibility/ownership extraction with lifecycle/input regressions, not merely
partial-file splitting. Assembly boundaries and automatic content-based Airport
kit freshness are not implemented in this stage. No frame-time/memory improvement
has been measured in the running game.
