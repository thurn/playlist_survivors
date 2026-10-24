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
            "review without fixed": record("hv-t8o8.1", 1, review={"findings": 3}),
            "review not an integer": record("hv-t8o8.1", 1, review={"findings": 2.5, "fixed": 2}),
            "review fixing more than it found": record("hv-t8o8.1", 1, review={"findings": 2, "fixed": 3}),
        }
        for name, data in cases.items():
            with self.subTest(name):
                self.write(data)
                code, _, err = self.run_cli("validate")
                self.assertEqual(code, 2, err)

    def test_a_review_counts_its_findings(self):
        self.write(record("hv-t8o8.1", 1, review={"findings": 8, "fixed": 6}))
        self.assertEqual(self.run_cli("validate")[0], 0)

    def test_review_churn_needs_the_review_field(self):
        tags = dict(TAGS, tags={**TAGS["tags"], "review-churn": "Review findings."})
        (self.dir / "friction-tags.json").write_text(json.dumps(tags))
        self.write(record("hv-t8o8.1", 1, tags=["review-churn"]))
        code, _, err = self.run_cli("validate")
        self.assertEqual(code, 2)
        self.assertIn("review", err)
        self.write(record("hv-t8o8.1", 1, tags=["review-churn"], review={"findings": 4, "fixed": 4}))
        self.assertEqual(self.run_cli("validate")[0], 0)

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

    def test_a_retrospectives_own_record_is_covered(self):
        for i in range(1, 4):
            self.write(record(f"hv-t8o8.{i}", i))
        self.write(record("hv-t8o8.37", 4, phase="R"))
        self.ledger({"retrospective": "hv-t8o8.37", "date": "2026-10-04", "covers": ["hv-t8o8.1", "hv-t8o8.2", "hv-t8o8.3"]})
        self.assertEqual(self.report()["window"], [])

    def test_only_plan_records_count_toward_the_cadence(self):
        for i in range(1, 4):
            self.write(record(f"hv-t8o8.{i}", i))
        for i in range(4, 9):
            self.write(record(f"hv-t8o8.{i}", i, phase="R"))
        report = self.report()
        self.assertEqual(len(report["window"]), 8)
        self.assertEqual(len(report["planRecords"]), 3)
        self.assertFalse(report["retrospective"]["due"])
        self.write(record("hv-t8o8.9", 9, phase="B"))
        self.assertTrue(self.report()["retrospective"]["due"])
        _, out, _ = self.run_cli()
        self.assertIn("(4 outside phase R)", out)
        self.assertIn("4 >= 4 plan records", out)

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


def status(item_id, subject, elapsed_ms, state="promoted", kind="gate"):
    """`tg status --json` for one candidate, trimmed to what stages reads."""
    attempts = []
    if elapsed_ms is not None:
        attempts = [
            {"step_results": [], "started_at": None},
            {
                "step_results": [{"name": "gate", "elapsed_ms": elapsed_ms}],
                "started_at": [2026, 297, 17, 44, 36, 238548000, 0, 0, 0],
            },
        ]
    return {
        "item": {
            "id": item_id,
            "kind": kind,
            "state": state,
            "metadata": {"subject": subject},
        },
        "attempts": attempts,
    }


class Minutes(unittest.TestCase):
    def test_minutes_count_from_the_branch_creation_rounded_up(self):
        reflog = "1000600 commit: x\n1000000 branch: Created from abc\n"
        self.assertEqual(friction.branch_minutes(reflog, 1000000 + 61), 2)
        self.assertEqual(friction.branch_minutes(reflog, 1000000 + 600), 10)
        self.assertEqual(friction.branch_minutes(reflog, 1000000 + 1500, parked=20), 5)
        self.assertEqual(friction.branch_minutes(reflog, 1000000 + 5), 1)
        with self.assertRaises(friction.Malformed):
            friction.branch_minutes("", 0)


class Stages(unittest.TestCase):
    def test_since_takes_dates_datetimes_and_the_retrospective(self):
        ev = friction.Evidence(3, 8, set(), {}, {}, [])
        self.assertIsNone(friction.parse_since(None, ev))
        self.assertEqual(
            friction.parse_since("2026-10-24", ev).isoformat(), "2026-10-24T00:00:00+00:00"
        )
        self.assertEqual(
            friction.parse_since("2026-10-24T14:12:45-07:00", ev).isoformat(),
            "2026-10-24T21:12:45+00:00",
        )
        with self.assertRaises(friction.Malformed):
            friction.parse_since("retrospective", ev)
        with self.assertRaises(friction.Malformed):
            friction.parse_since("yesterday", ev)

    def test_release_rows_since_a_time_drop_older_runs(self):
        old, new = "01a16f03-ae4b-7163-9176-622fa552e0cd", "01a16fd2-6dd2-7371-b1b5-ac88c067f768"
        release = {
            "runs": [
                {"item_id": new, "target_oid": {"bytes": "b" * 40}, "state": "succeeded",
                 "elapsed_ms": 1000, "steps": []},
                {"item_id": old, "target_oid": {"bytes": "a" * 40}, "state": "failed",
                 "elapsed_ms": 2000, "steps": []},
            ]
        }
        cut = friction.uuid7_time(new)
        rows = friction.release_rows(release, cut)
        self.assertEqual([r["target"] for r in rows], ["bbbbbbbbbb"])
        self.assertEqual(len(friction.release_rows(release)), 2)

    def test_tollgate_and_uuid7_times(self):
        self.assertEqual(
            friction.tollgate_time(
                [2026, 297, 17, 44, 36, 238548000, 0, 0, 0]
            ).isoformat(),
            "2026-10-24T17:44:36.238548+00:00",
        )
        self.assertIsNone(friction.tollgate_time(None))
        # 0x01a16f03ae4b ms since the epoch.
        self.assertEqual(
            friction.uuid7_time("01a16f03-ae4b-7163-9176-622fa552e0cd").strftime(
                "%Y-%m-%d"
            ),
            "2026-10-24",
        )

    def test_a_gate_row_reads_the_latest_attempt_with_results(self):
        row = friction.gate_row(status("01a16f03-ae4b-7163", "build: x", 76955))
        self.assertEqual(row["gateS"], 77.0)
        self.assertEqual(row["subject"], "build: x")
        self.assertTrue(row["at"].startswith("2026-10-24T17:44:36"))
        self.assertIsNone(
            friction.gate_row(status("01a1", "y", None, "queued"))["gateS"]
        )

    def test_release_rows_are_oldest_first_with_step_times(self):
        release = {
            "runs": [
                {
                    "target_oid": {"bytes": "b" * 40},
                    "state": "running",
                    "elapsed_ms": None,
                    "steps": [],
                },
                {
                    "target_oid": {"bytes": "a" * 40},
                    "state": "check-passed",
                    "elapsed_ms": 534676,
                    "steps": [
                        {"name": "gate", "elapsed_ms": 73308},
                        {"name": "ci-full-native", "elapsed_ms": 4560},
                    ],
                },
            ]
        }
        rows = friction.release_rows(release)
        self.assertEqual([r["target"] for r in rows], ["a" * 10, "b" * 10])
        self.assertEqual(rows[0]["releaseS"], 534.7)
        self.assertEqual(rows[0]["steps"], {"gate": 73.3, "ci-full-native": 4.6})

    def test_stages_flags_three_consecutive_overruns(self):
        gates = [
            friction.gate_row(status(f"id{i}", "x", ms))
            for i, ms in enumerate([2000, 95000, 91000, 99000, 1000])
        ]
        result = friction.stages(gates, [], {"gateS": 60, "releaseS": 1800}, 3)
        self.assertEqual(result["gate"]["overBudget"], 3)
        self.assertTrue(result["gate"]["overrun"])
        self.assertEqual(result["release"]["runs"], 0)
        calm = friction.stages(gates[:2] + gates[3:], [], {"gateS": 60}, 3)
        self.assertFalse(calm["gate"]["overrun"])

    def test_the_window_starts_at_the_retrospectives_record(self):
        evidence = friction.load(friction.DEFAULT_EVIDENCE)
        if not evidence.retrospectives:
            self.skipTest("no retrospective yet")
        last = evidence.retrospectives[-1]["retrospective"]
        mine = [r for r in evidence.records if r.bead == last]
        if mine:
            self.assertEqual(
                friction.retrospective_time(evidence),
                mine[0].recorded.astimezone(friction.UTC),
            )


if __name__ == "__main__":
    unittest.main()
