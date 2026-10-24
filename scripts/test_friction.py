"""Tests for scripts/friction: uv run --no-project python -m unittest scripts/test_friction.py"""

import contextlib
import io
import json
import shutil
import sys
import tempfile
import unittest
from importlib.machinery import SourceFileLoader
from importlib.util import module_from_spec, spec_from_loader
from pathlib import Path

SCRIPT = Path(__file__).resolve().parent / "friction"
_loader = SourceFileLoader("friction", str(SCRIPT))
_spec = spec_from_loader("friction", _loader)
assert _spec is not None
friction = module_from_spec(_spec)
sys.modules["friction"] = friction
_loader.exec_module(friction)

TAGS = {
    "triggerBeads": 3,
    "retrospectiveCadence": 4,
    "tags": {"slow-check": "A slow check.", "flaky-test": "A flaky test."},
    "aliases": {"flake": "flaky-test"},
}
METRICS = """# Budgets

| Budget | Key | Seconds | Baseline | Measured by |
|---|---|---|---|---|
| Fast check | `localValidationS.fast` | 60 | 25 s | records |
| Gate | `gateS` | 60 | 44 s | records |
"""


def record(bead, day, fast=20, gate=None, tags=(), **extra):
    data = {
        "bead": bead,
        "phase": "W",
        "recorded": f"2026-10-{day:02d}T12:00:00Z",
        "implementMin": 30,
        "localValidationS": {"fast": fast},
        "friction": [{"tag": tag, "minutes": 5, "note": "it happened"} for tag in tags],
    }
    if gate is not None:
        data["gateS"] = gate
    data.update(extra)
    return data


class FrictionTest(unittest.TestCase):
    def setUp(self):
        self.dir = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.dir)
        (self.dir / "friction").mkdir()
        (self.dir / "friction-tags.json").write_text(json.dumps(TAGS))
        (self.dir / "metrics.md").write_text(METRICS)
        (self.dir / "introspection.jsonl").write_text("")

    def write(self, data, name=None):
        (self.dir / "friction" / f"{name or data['bead']}.json").write_text(json.dumps(data))

    def ledger(self, *entries):
        with (self.dir / "introspection.jsonl").open("a") as out:
            for entry in entries:
                out.write(json.dumps(entry) + "\n")

    def run_cli(self, *args):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = friction.main([*args, "--evidence", str(self.dir)])
        return code, out.getvalue(), err.getvalue()

    def report(self):
        return friction.report(friction.load(self.dir))

    def test_valid_records_pass_validation(self):
        self.write(record("hv-t8o8.1", 1, tags=["flake"]))
        self.write(record("hv-t8o8.2", 2, gate=40, hostLoad=3.5))
        code, out, _ = self.run_cli("validate")
        self.assertEqual(code, 0)
        self.assertIn("2 records valid", out)

    def test_malformed_records_exit_2(self):
        cases = {
            "unknown field": record("hv-t8o8.1", 1, gateSeconds=3),
            "unknown tag": record("hv-t8o8.1", 1, tags=["mystery"]),
            "negative time": record("hv-t8o8.1", 1, fast=-1),
            "bad phase": {**record("hv-t8o8.1", 1), "phase": "Z"},
            "no offset": {**record("hv-t8o8.1", 1), "recorded": "2026-10-01T12:00:00"},
            "missing friction": {k: v for k, v in record("hv-t8o8.1", 1).items() if k != "friction"},
        }
        for name, data in cases.items():
            with self.subTest(name):
                self.write(data)
                code, _, err = self.run_cli("validate")
                self.assertEqual(code, 2, err)

    def test_record_must_match_its_file_name(self):
        self.write(record("hv-t8o8.1", 1), name="hv-t8o8.9")
        code, _, err = self.run_cli("validate")
        self.assertEqual(code, 2)
        self.assertIn("does not match the file name", err)

    def test_invalid_json_exits_2(self):
        (self.dir / "friction" / "hv-t8o8.1.json").write_text("{")
        self.assertEqual(self.run_cli("validate")[0], 2)

    def test_malformed_ledger_line_exits_2(self):
        self.ledger({"trigger": "slow-check"})
        self.assertEqual(self.run_cli("validate")[0], 2)

    def test_a_tag_in_three_beads_fires_once_aliases_resolved(self):
        self.write(record("hv-t8o8.1", 1, tags=["flake", "flaky-test"]))
        self.write(record("hv-t8o8.2", 2, tags=["flaky-test"]))
        self.assertEqual(self.report()["triggers"], [])
        self.write(record("hv-t8o8.3", 3, tags=["flake"]))
        (trigger,) = self.report()["triggers"]
        self.assertEqual(trigger["trigger"], "flaky-test")
        self.assertEqual(trigger["beads"], ["hv-t8o8.1", "hv-t8o8.2", "hv-t8o8.3"])
        code, out, _ = self.run_cli()
        self.assertEqual(code, 1)
        self.assertIn("NEEDS A DISPOSITION", out)

    def test_a_disposition_settles_a_trigger(self):
        for i in range(1, 4):
            self.write(record(f"hv-t8o8.{i}", i, tags=["slow-check"]))
        self.ledger({"trigger": "slow-check", "bead": "hv-t8o8.50"})
        code, out, _ = self.run_cli()
        self.assertEqual(code, 0)
        self.assertIn("-> hv-t8o8.50", out)

    def test_a_retrospective_closes_the_window(self):
        for i in range(1, 4):
            self.write(record(f"hv-t8o8.{i}", i, tags=["slow-check"]))
        self.ledger(
            {"trigger": "slow-check", "reason": "harness problem outside the repository"},
            {"retrospective": "hv-t8o8.37", "date": "2026-10-04", "covers": ["hv-t8o8.1", "hv-t8o8.2", "hv-t8o8.3"]},
        )
        result = self.report()
        self.assertEqual(result["triggers"], [])
        self.assertEqual(result["window"], [])
        # A new recurrence after the retrospective needs a new disposition.
        for i in range(4, 7):
            self.write(record(f"hv-t8o8.{i}", i, tags=["slow-check"]))
        (trigger,) = self.report()["triggers"]
        self.assertIsNone(trigger["disposition"])

    def test_budget_trigger_needs_three_consecutive_overruns(self):
        # Over 1.5 x 60 s = 90 s: 91 counts, 90 does not.
        self.write(record("hv-t8o8.1", 1, fast=91))
        self.write(record("hv-t8o8.2", 2, fast=90))
        self.write(record("hv-t8o8.3", 3, fast=95))
        self.write(record("hv-t8o8.4", 4, fast=100))
        self.assertEqual(self.report()["triggers"], [])
        self.write(record("hv-t8o8.5", 5, fast=120, hostLoad=9.0))
        (trigger,) = self.report()["triggers"]
        self.assertEqual(trigger["trigger"], "budget:localValidationS.fast")
        self.assertEqual(trigger["beads"], ["hv-t8o8.3", "hv-t8o8.4", "hv-t8o8.5"])
        self.assertEqual(trigger["hostLoads"], [None, None, 9.0])

    def test_budget_trigger_orders_by_recorded_and_skips_records_without_the_field(self):
        self.write(record("hv-t8o8.9", 1, gate=100))
        self.write(record("hv-t8o8.2", 2))
        self.write(record("hv-t8o8.10", 3, gate=100))
        self.write(record("hv-t8o8.3", 4, gate=100))
        (trigger,) = self.report()["triggers"]
        self.assertEqual(trigger["beads"], ["hv-t8o8.9", "hv-t8o8.10", "hv-t8o8.3"])

    def test_cadence_retrospective_is_due_and_keyed_by_the_last_retrospective(self):
        for i in range(1, 4):
            self.write(record(f"hv-t8o8.{i}", i))
        self.assertFalse(self.report()["retrospective"]["due"])
        self.write(record("hv-t8o8.4", 4))
        retro = self.report()["retrospective"]
        self.assertTrue(retro["due"])
        self.assertEqual(retro["key"], "retrospective=hv-t8o8/since-start")
        self.ledger({"retrospective": "hv-t8o8.37", "date": "2026-10-05", "covers": ["hv-t8o8.1", "hv-t8o8.2"]})
        retro = self.report()["retrospective"]
        self.assertFalse(retro["due"])
        self.assertEqual(retro["key"], "retrospective=hv-t8o8/since-hv-t8o8.37")
        _, out, _ = self.run_cli()
        self.assertNotIn("retrospective due (", out)

    def test_an_earlier_run_of_overruns_stays_fired_after_a_record_under_budget(self):
        for i in range(1, 4):
            self.write(record(f"hv-t8o8.{i}", i, fast=100))
        self.write(record("hv-t8o8.4", 4, fast=10))
        (trigger,) = self.report()["triggers"]
        self.assertEqual(trigger["beads"], ["hv-t8o8.1", "hv-t8o8.2", "hv-t8o8.3"])

    def test_a_budget_row_without_a_number_is_malformed(self):
        (self.dir / "metrics.md").write_text(METRICS + "| Release | `releaseS` | about 1800 | - | runs |\n")
        code, _, err = self.run_cli("validate")
        self.assertEqual(code, 2)
        self.assertIn("releaseS", err)

    def test_metrics_without_budgets_is_malformed(self):
        (self.dir / "metrics.md").write_text("# Budgets\n\nNone yet.\n")
        self.assertEqual(self.run_cli("validate")[0], 2)

    def test_the_committed_evidence_is_valid(self):
        evidence = friction.load(friction.DEFAULT_EVIDENCE)
        self.assertIn("localValidationS.fast", evidence.budgets)
        self.assertIn("gateS", evidence.budgets)


if __name__ == "__main__":
    unittest.main()
