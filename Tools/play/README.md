# Evidence checks

These commands do not play the game. Do not equate source compilation, a named
mission verdict, a screenshot or a code review with general gameplay acceptance.

Default, offline:

```sh
python3 Tools/project.py audit
python3 Tools/project.py sizes --check
python3 Tools/project.py compile
python3 -m unittest discover -s Tools/play -p 'test_*.py' -v
```

Compilation reuses this checkout's existing Unity compiler references and defines.
It compiles current default runtime/editor assembly sources into a temporary
directory, not Library. New assembly definitions require an assembly-aware check.
It is not a Unity import or Player build. The result is stamped with a hash of
C# source, Assets JSON data, Packages and ProjectSettings, not proof of generated
prefab/mesh freshness or the assets loaded into an editor.

`gate.sh --compile /absolute/result.json --run /absolute/run` checks a named
mission trace against that compile and the current source. The runner must record
`source` (the pre-run `project.py fingerprint`), `sourceUnchanged: true` only after
checking the post-run hash, `why: "done"`, `errors: 0`, `exceptions: 0` in its summary.
That source must also be the source actually loaded into the authorized editor;
a runner cannot infer that from an offline compile alone.
The gate does not query Unity. Old summaries without this evidence fail closed.
Legacy text soak tallies and implicit `Temp/play/loop` are no longer accepted.
No general-purpose `run.sh` or `soak.sh` exists here: use the actual task-specific
runner, with a unique run directory, and its scenario-specific analyzer.

Only after task-specific user permission:

```sh
Tools/play/recompile.sh --allow-unity
```

This refreshes the open editor and can stop Play. The flag is an acknowledgement
of permission, not a way for an agent to authorize itself. It requires a readable
console with no lost entries. Read Docs/unity-cli.md before editor work.

The offline tests deliberately exercise missing, stale, dropped and aborted
evidence. CI runs these and size budgets; CI does not currently build Unity or
validate scenes, serialized imports, UI/input or visual behavior.
