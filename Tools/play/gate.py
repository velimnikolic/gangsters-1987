"""Judge a named mission run against a named offline compile of current source.

The runner must record Tools/project.py fingerprint in summary.json as `source`
before starting and confirm it is unchanged after completion. Old/unbound runs
are deliberately unverified. This is not general gameplay or visual acceptance.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("project", HERE.parent / "project.py")
project = importlib.util.module_from_spec(spec)
spec.loader.exec_module(project)


def verify(compile_path, run, expected):
    stamp = json.loads(Path(compile_path).read_text())
    summary = json.loads((Path(run) / "summary.json").read_text())
    if not isinstance(stamp, dict) or stamp.get("passed") is not True or stamp.get("source") != expected:
        raise ValueError("compile is missing, failed or from different source")
    if not isinstance(summary, dict) or summary.get("source") != expected:
        raise ValueError("run is not tied to this source snapshot")
    if summary.get("sourceUnchanged") is not True:
        raise ValueError("runner did not confirm stable source through completion")
    if summary.get("why") != "done" or any(summary.get(k) != 0 for k in ("errors", "exceptions")):
        raise ValueError("run is unfinished, contains errors or lacks console evidence")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compile", required=True, help="explicit project.py result.json")
    parser.add_argument("--run", required=True, type=Path, help="explicit mission run directory")
    args = parser.parse_args()
    try:
        source = project.fingerprint()
        verify(args.compile, args.run, source)
        result = subprocess.run([sys.executable, str(HERE / "analyze.py"), str(args.run), "--verdict"])
        if result.returncode:
            return result.returncode
        if project.fingerprint() != source:
            raise ValueError("source changed while evidence was checked")
    except (OSError, ValueError, TypeError) as error:
        print("UNVERIFIED:", error)
        return 2
    print("PASSED: named mission evidence only; no general UI/visual verdict")
    return 0


if __name__ == "__main__":
    sys.exit(main())
