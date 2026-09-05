# Gangsters: shared working rules

CoreDemo is the game. MiniCoreDemo and focused scenes exercise the same systems.
Keep freeway/expressway, harbor and airport and their dependencies for integration.
Read `Docs/runtime-map.md` before choosing a system to change.

## Authority

- Only the user authorizes Unity Editor access, for the current task. Without it,
  do not send editor commands, stop/start Play, open scenes, run editor tests,
  rebuild assets or launch/close an Editor. Offline source checks are allowed.
  Another session's editor permission does not transfer to this task.
- Commit and push only when explicitly requested. Completing/reviewing a task does
  not authorize a commit. Do not stage, stash, rebase or switch the shared checkout
  as incidental cleanup. Work on main unless the user requests a branch.
- Preserve other sessions' edits. Coordinate ownership before overlapping edits;
  whole-file staging cannot separate two authors' changes.
- Choose No at Unity scene recovery. Never copy backups into Assets/_Recovery.

## Implementation

- Trace intent, admission, physical execution, completion, cleanup and persistence.
  Several collaborating classes are valid; one owner per rule/state is required.
- Scene adapters configure shared behavior. Do not fork simulation rules in a demo.
- Streamed GameObjects are views, not owners of business/personnel/campaign truth.
- Preserve asset GUIDs and serialized field compatibility when moving code.
- Before deletion, check serialized GUID references AND code/string asset loading.
  Keep a recoverable copy or tracked original. Names alone do not prove inactivity.
- Edit generated catalogs through their generators/inputs and validate freshness.
  Compilation does not update generated assets or already-loaded scenes.
- Split by responsibility and state ownership, not arbitrary line counts or partials.
  Use `python3 Tools/project.py sizes` for file and partial-class size budgets.

## Verification

- `python3 Tools/project.py audit`: dangling references to deleted tracked assets.
- `python3 Tools/project.py compile`: current runtime/editor C# compiled offline to
  a separate temporary directory using existing Unity compiler inputs. It never
  contacts Unity. This is NOT an asset-import, Player-build or Play verdict.
- Use python3. Read Docs/unity-cli.md before authorized editor work.
- Pending compile, missing/old summary, unread console, zero executed scenarios and
  incomplete runs are not success. Record the exact source/run being judged.
- Select model, integration, lifecycle and UI/input checks according to risk.
  A universal number of repeated runs cannot prove unrelated behavior.
- Interactive review scenes use RoadDemo.DemoCamera and a readable controls hint.
- Report implemented, verified and unverified scope separately. Preserve manual
  acceptance when the user reserves Play/visual review.

## Adversarial review

- Codex invokes Claude via `Tools/review/adversarial-claude/review.sh`.
- Claude invokes the Codex plugin's `codex:adversarial-review` companion.
- Review a fixed change. Moving HEAD or a truncated asset diff is not full review.
  Include changed source, deleted-asset reference evidence and validation results.
- Review is read-only and does not authorize Git writes. Repair confirmed findings
  within the implementation task and repeat affected checks.
