"""Offline regression tests: malformed/missing evidence must never pass."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parent


def module(name):
    spec = importlib.util.spec_from_file_location(name, HERE / (name + ".py"))
    loaded = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


class EvidenceTests(unittest.TestCase):
    def console(self, result, *args):
        return subprocess.run([sys.executable, str(HERE / "consoleread.py"), *args],
            input=json.dumps({"success": True, "data": {"result": result}}),
            capture_output=True, text=True)

    def test_console_rejects_lost_entries(self):
        self.assertNotEqual(self.console({"entries": [], "dropped": 1}, "0").returncode, 0)

    def test_console_rejects_full_tail(self):
        self.assertNotEqual(self.console({"entries": [], "returned": 400}, "0").returncode, 0)

    def test_console_rejects_missing_result_and_cursor(self):
        for value in (None, [], {}, "bad"):
            with self.subTest(value=value):
                self.assertNotEqual(self.console(value, "--mark").returncode, 0)

    def test_console_clean_and_scoped_errors(self):
        result = self.console({"entries": [{"seq": 4, "message": "error CS0001: old"},
            {"seq": 6, "message": "error CS0002: new"}]}, "5")
        self.assertEqual(result.returncode, 0)
        self.assertNotIn("old", result.stdout)
        self.assertIn("new", result.stdout)
        self.assertEqual(self.console({"entries": []}, "0").returncode, 0)

    def test_mission_requires_completed_active_scenario(self):
        analyze = module("analyze")
        with tempfile.TemporaryDirectory() as folder, contextlib.redirect_stdout(io.StringIO()):
            root = Path(folder)
            (root / "trace.jsonl").write_text(json.dumps({"k": "mission", "t": 0, "state": "Waiting"}) + "\n")
            self.assertNotEqual(analyze.verdict(folder), 0)
            for summary in ({"why": "aborted"}, {"why": "done", "errors": 1},
                            {"why": "done", "exceptions": 1}, {"why": "done", "errors": 0, "exceptions": 0}):
                (root / "summary.json").write_text(json.dumps(summary))
                self.assertNotEqual(analyze.verdict(folder), 0)

    def test_gate_rejects_stale_unfinished_and_unbound_runs(self):
        gate = module("gate")
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            stamp = root / "compile.json"
            stamp.write_text(json.dumps({"passed": True, "source": "current"}))
            valid = {"source": "current", "sourceUnchanged": True, "why": "done", "errors": 0, "exceptions": 0}
            (root / "summary.json").write_text(json.dumps(valid))
            gate.verify(stamp, root, "current")
            for delta in ({"source": "old"}, {"sourceUnchanged": False}, {"why": "aborted"},
                          {"errors": 2}, {"exceptions": None}):
                (root / "summary.json").write_text(json.dumps({**valid, **delta}))
                with self.subTest(delta=delta), self.assertRaises(ValueError):
                    gate.verify(stamp, root, "current")
            with self.assertRaises(ValueError):
                gate.verify(stamp, root, "other-source")

    def test_active_mission_and_truncated_trace(self):
        analyze = module("analyze")
        with tempfile.TemporaryDirectory() as folder, contextlib.redirect_stdout(io.StringIO()):
            root = Path(folder)
            (root / "summary.json").write_text(json.dumps({"why": "done", "errors": 0, "exceptions": 0}))
            trace = root / "trace.jsonl"
            trace.write_text(json.dumps({"k": "mission", "t": 1, "who": "Waiting", "what": "still waiting"}) + "\n")
            self.assertNotEqual(analyze.verdict(folder), 0)
            active = json.dumps({"k": "mission", "t": 1, "state": "Hunting"}) + "\n"
            trace.write_text(active)
            self.assertEqual(analyze.verdict(folder), 0)
            trace.write_text(active + '{"k":')
            self.assertNotEqual(analyze.verdict(folder), 0)

    def test_compile_status_rejects_unsuccessful_envelope(self):
        for doc in ([], {"success": False, "data": {"result": {"status": "completed", "failed": False}}}):
            result = subprocess.run([sys.executable, str(HERE / "statusread.py")],
                input=json.dumps(doc), capture_output=True, text=True)
            self.assertEqual(result.stdout.strip(), "unknown")

    def test_recompile_refuses_without_permission_flag(self):
        result = subprocess.run(["bash", str(HERE / "recompile.sh")], capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn("explicit user permission", result.stderr)


if __name__ == "__main__":
    unittest.main()
