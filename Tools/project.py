#!/usr/bin/env python3
"""Offline project checks. Never connects to or launches a Unity Editor."""
import argparse
from collections import defaultdict
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "Assets"
SERIALIZED = {".unity", ".prefab", ".asset", ".mat", ".meta", ".controller",
              ".anim", ".overrideController", ".shadergraph", ".vfx"}


def git_paths(*args):
    result = subprocess.run(["git", *args, "-z"], cwd=ROOT, check=True,
                            capture_output=True)
    return {p.decode() for p in result.stdout.split(b"\0") if p}


def fingerprint():
    digest = hashlib.sha256()
    # Measured JSON now drives simulation; changing it invalidates source/run proof
    # just as changing the old generated C# did.
    for p in sorted(set(ASSETS.rglob("*.cs")) | set(ASSETS.rglob("*.json"))):
        digest.update(p.relative_to(ROOT).as_posix().encode())
        digest.update(p.read_bytes())
    for folder in ("Packages", "ProjectSettings"):
        for p in sorted((ROOT / folder).rglob("*")):
            if p.is_file():
                digest.update(p.relative_to(ROOT).as_posix().encode())
                digest.update(p.read_bytes())
    return digest.hexdigest()


def audit():
    """Reject surviving serialized references to deleted tracked assets."""
    deleted = git_paths("diff", "--name-only", "--diff-filter=D", "HEAD")
    retired = {}
    for name in sorted(deleted):
        if not name.endswith(".meta"):
            continue
        old = subprocess.run(["git", "show", "HEAD:" + name], cwd=ROOT,
                             check=True, capture_output=True).stdout
        match = re.search(rb"^guid: ([0-9a-f]{32})", old, re.M)
        if match:
            retired[match[1]] = name[:-5]
    problems = []
    for p in ASSETS.rglob("*"):
        if not p.is_file() or p.suffix not in SERIALIZED:
            continue
        data = p.read_bytes()
        if b"\0" in data[:128]:
            continue
        for guid in set(re.findall(rb"guid:\s*([0-9a-f]{32})", data)) & retired.keys():
            problems.append(f"{p.relative_to(ROOT)} -> {retired[guid]}")
    print(f"Checked references to {len(retired)} deleted asset GUIDs.")
    for problem in problems:
        print(problem)
    print("FAILED" if problems else "PASSED: no dangling references to deleted assets")
    return 1 if problems else 0


def sizes(check=False):
    """Count handwritten files and whole partial classes, not cosmetic file splits."""
    files, partials = {}, defaultdict(int)
    for root in (ASSETS / "RoadDemo", ASSETS / "Scripts"):
        for p in root.rglob("*.cs"):
            text = p.read_text(encoding="utf-8-sig")
            if "GENERATED" in "\n".join(text.splitlines()[:3]):
                continue
            count = len(text.splitlines())
            files[p.relative_to(ROOT).as_posix()] = count
            for name in set(re.findall(r"\bpartial\s+class\s+(\w+)", text)):
                partials[name] += count
    for title, values in (("FILES", files), ("PARTIAL CLASSES (all containing files)", partials)):
        print(title)
        for name, count in sorted(values.items(), key=lambda x: x[1], reverse=True)[:12]:
            print(f"{count:6} {name}")
    if not check:
        return 0
    baseline = json.loads((ROOT / "Tools/source-size-baseline.json").read_text())
    failures = []
    for title, values, default in (("files", files, 1000), ("partials", partials, 1500)):
        for name, count in values.items():
            limit = max(default, baseline[title].get(name, default))
            if count > limit:
                failures.append(f"{name}: {count} lines, budget {limit}; split responsibility")
    for failure in failures:
        print(failure)
    print("FAILED: size budget grew" if failures else "PASSED: source size budgets")
    return 1 if failures else 0


def compile_sources():
    """Reuse Unity's compiler references; write fresh assemblies only to a temp folder.

    Deleted sources must be tracked deletions. New sources are included explicitly;
    new asmdef boundaries require Unity to regenerate its compiler input first.
    This is source verification, never a Player build or Play acceptance.
    """
    version = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text().splitlines()[0].split()[1]
    scripting = (Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Unity/Hub/Editor" / version / "Editor/Data"
                 if os.name == "nt" else
                 Path("/Applications/Unity/Hub/Editor") / version / "Unity.app/Contents/Resources/Scripting")
    dotnet = scripting / "DotNetSdk" / ("dotnet.exe" if os.name == "nt" else "dotnet")
    compilers = sorted((scripting / "DotNetSdk/sdk").glob("*/Roslyn/bincore/csc.dll"))
    if not dotnet.is_file() or not compilers:
        raise RuntimeError("Unity's bundled .NET compiler is unavailable")
    before = fingerprint()
    deleted = git_paths("ls-files", "--deleted")
    current = {p.relative_to(ROOT).as_posix() for p in ASSETS.rglob("*.cs")}
    if list(ASSETS.rglob("*.asmdef")) or list(ASSETS.rglob("*.asmref")):
        raise RuntimeError("Assembly definitions require a full assembly-aware compile; this check supports the two default assemblies only")
    output = Path(tempfile.mkdtemp(prefix="gangsters-compile-"))
    print("Source snapshot:", before, "Output:", output, flush=True)
    for assembly in ("Assembly-CSharp", "Assembly-CSharp-Editor"):
        candidates = list((ROOT / "Library/Bee/artifacts").glob("*/" + assembly + ".rsp"))
        if not candidates:
            raise RuntimeError("No Unity compiler input for " + assembly)
        source_rsp = max(candidates, key=lambda p: p.stat().st_mtime_ns)
        lines = []
        sources = set()
        for line in source_rsp.read_text().splitlines():
            bare = line.strip('"')
            if bare.endswith(".cs") and not line.startswith(("-", "/")):
                if not (ROOT / bare).is_file():
                    if bare not in deleted:
                        raise RuntimeError("Compiler input is missing: " + bare)
                    continue
                sources.add(bare)
            if line.startswith(("-out:", "-refout:")):
                continue
            if assembly.endswith("-Editor") and line.startswith("-r:"):
                line = re.sub(r'"[^"]*/Assembly-CSharp(?:\.ref)?\.dll"',
                              lambda _: '"' + (output / "Assembly-CSharp.dll").as_posix() + '"', line)
            lines.append(line)
        for name in sorted(current - sources):
            path = Path(name)
            if ("Editor" in path.parts) != assembly.endswith("-Editor"):
                continue
            if any(list((ROOT / parent).glob("*.asmdef")) or
                   list((ROOT / parent).glob("*.asmref")) for parent in path.parents):
                raise RuntimeError("New assembly-owned source needs refreshed compiler input: " + name)
            lines.append('"' + name + '"')
            sources.add(name)
        lines.append('-out:"' + str(output / (assembly + ".dll")) + '"')
        rsp = output / (assembly + ".rsp")
        rsp.write_text("\n".join(lines) + "\n")
        print(assembly + ": " + str(len(sources)) + " source files", flush=True)
        result = subprocess.run([str(dotnet), str(compilers[-1]), "@" + str(rsp)], cwd=ROOT,
                                capture_output=True, text=True)
        log = result.stdout + result.stderr
        (output / (assembly + ".log")).write_text(log)
        for line in log.splitlines():
            if "error " in line:
                print(line)
        print(f"{assembly}: exit {result.returncode}, {log.count('warning ')} warnings; log in {output}")
        if result.returncode:
            return result.returncode
    if fingerprint() != before:
        raise RuntimeError("Source changed during compilation; result is not a stable verdict")
    (output / "result.json").write_text(json.dumps({"source": before, "passed": True,
        "scope": "offline runtime and editor C# compilation; no Unity or Play"}, indent=2))
    print("PASSED: offline source compilation only; no Unity/Play verification")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("audit", "compile", "fingerprint", "sizes"))
    parser.add_argument("--check", action="store_true", help="enforce recorded source-size budgets")
    args = parser.parse_args()
    if args.command == "audit":
        return audit()
    if args.command == "compile":
        return compile_sources()
    if args.command == "sizes":
        return sizes(args.check)
    print(fingerprint())
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (RuntimeError, OSError, ValueError, subprocess.SubprocessError) as error:
        print("UNVERIFIED:", error, file=sys.stderr)
        sys.exit(2)
