---
description: Implement game behavior with scoped evidence and cross-model review
argument-hint: <what should happen in CoreDemo>
---

Read AGENTS.md and Docs/runtime-map.md. `$ARGUMENTS` is the requested behavior.

1. Establish an observable before/after example, scope and existing game rules.
2. Trace intent, admission, physical execution, completion, cleanup and save/load.
   Multiple cooperating classes are expected; do not force everything into one class.
3. Select model contracts, integration scenarios, actual UI/input and visual review
   as appropriate. Include interruption/failure paths. Visual criteria are acceptance.
4. Implement in shared owners and keep scene adapters thin. Preserve unrelated work.
5. Run authorized checks. Offline source checks need no editor. Unity access needs
   explicit user authorization for this task. Otherwise leave import/Play/visual
   acceptance explicitly unverified; do not call the task fully verified.
6. Review the fixed change with Codex adversarial-review, repair confirmed findings
   and repeat affected checks. Review does not require a commit.
7. Report changes, passed checks, unverified scope and reproduction instructions.

No automatic commits/pushes, universal thirty-run gate or shared GOAL.md. Use separate
per-task/run artifacts, not Temp/play/loop that another session can overwrite.
