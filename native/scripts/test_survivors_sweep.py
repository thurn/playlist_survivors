"""Survival-only verdicts and ownership of bounded worker failures."""

from __future__ import annotations

import io
import json
import subprocess
import tempfile
import threading
import time
import unittest
from argparse import Namespace
from contextlib import redirect_stdout
from copy import deepcopy
from pathlib import Path
from unittest.mock import patch

from native.scripts import survivors_sweep as sweep


def data(bot="positioning"):
    return {
        "bot": bot,
        "noHeroDamage": False,
        "requestedMinutes": 30,
        "runWallSeconds": 0.1,
        "report": {
            "fallen": False,
            "done": True,
            "survivalS": 1800,
            "lowestHpShare": 0.5,
            "levelsByMinute": [],
            "level": 1,
            "weaponsAtChests": [],
            "evolutions": [],
            "peakEnemies": 300,
            "settledHash": "0000000000000001",
        },
    }


class SurvivalReports(unittest.TestCase):
    def test_matrix_covers_every_seed_order_and_both_analysis_paths(self):
        cases = sweep.cases()
        self.assertEqual(len(cases), 41)
        self.assertEqual(len({c.name for c in cases}), 41)
        for mode, orders in [
            ("cached", ("original", "reverse", "rotate")),
            ("continuous", ("original",)),
        ]:
            for order in orders:
                for bot in ("positioning", "afk"):
                    self.assertEqual(
                        {
                            c.seed
                            for c in cases
                            if (c.mode, c.order, c.bot) == (mode, order, bot)
                        },
                        set(range(1, 6)),
                    )

    def test_only_survival_and_hp_share_gate_positioning(self):
        d = data()
        self.assertTrue(sweep.verdict(d))
        for share, passing in [(0.25, False), (0.25001, True)]:
            d["report"]["lowestHpShare"] = share
            self.assertEqual(sweep.verdict(d), passing)
        d["report"]["lowestHpShare"] = 1
        d["report"]["survivalS"] = 1799.999
        self.assertFalse(sweep.verdict(d))
        d["report"]["survivalS"] = 1800
        d["report"]["level"] = 0
        d["report"]["levelsByMinute"] = [
            {"minute": 5, "level": 0},
            {"minute": 30, "level": 0},
        ]
        self.assertTrue(sweep.verdict(d), "pacing never changes the gate")

    def test_afk_minimum_difficulty_is_diagnostic_at_any_survival_time(self):
        d = data("afk")
        d["report"].update(fallen=True, survivalS=599.999)
        self.assertIsNone(sweep.verdict(d))
        for seconds in [600, 606.15, 1800]:
            d["report"]["survivalS"] = seconds
            self.assertIsNone(sweep.verdict(d))
        d["report"].update(fallen=False, survivalS=1800)
        self.assertIsNone(sweep.verdict(d))

    def test_random_pick_soak_and_short_runs_have_no_balance_verdict(self):
        self.assertIsNone(sweep.verdict(data("random-pick")))
        d = data()
        d["noHeroDamage"] = True
        self.assertIsNone(sweep.verdict(d))
        d["noHeroDamage"] = False
        d["requestedMinutes"] = 3
        self.assertIsNone(sweep.verdict(d))

    def test_summary_reports_pacing_without_inventing_unreached_minutes(self):
        d = data()
        d["report"]["levelsByMinute"] = [
            {"minute": 0, "level": 1},
            {"minute": 5, "level": 7},
        ]
        row = sweep.summary(sweep.Case("cached", "original", "positioning", 1), d)
        self.assertEqual(row["levelAt5"], 7)
        self.assertIsNone(row["levelAt30"])
        self.assertTrue(row["survivalGate"])


class Workers(unittest.TestCase):
    def exercise(self, jobs: int, fail: bool, continuous: bool = False) -> None:
        lock = threading.Lock()
        active = 0
        peak = 0

        def fake_case(binary, case, out, minutes, deadline, *args, **kwargs):
            nonlocal active, peak
            with lock:
                active += 1
                peak = max(peak, active)
            try:
                time.sleep(0.01)
                if fail and case.seed == 1 and case.bot == "afk":
                    raise RuntimeError("worker failed")
                result = deepcopy(data(case.bot))
                result["requestedMinutes"] = minutes
                return result
            finally:
                with lock:
                    active -= 1

        selected = (
            [
                sweep.Case("cached", "original", "positioning", 1),
                sweep.Case("continuous", "original", "positioning", 1),
                sweep.Case("continuous", "original", "afk", 1),
            ]
            if continuous
            else sweep.cases()[:2]
        )
        with tempfile.TemporaryDirectory() as folder:
            out = Path(folder) / "reports"
            args = Namespace(
                jobs=jobs,
                only=len(selected),
                minutes=3,
                assert_survival=False,
                release=False,
                force=True,
                out=out,
            )
            with (
                patch.object(sweep, "enter_qos"),
                patch.object(sweep, "cases", return_value=selected),
                patch.object(sweep, "run_case", fake_case),
                patch.object(
                    sweep,
                    "checked",
                    return_value=subprocess.CompletedProcess([], 0, "", ""),
                ),
                redirect_stdout(io.StringIO()),
            ):
                if fail:
                    with self.assertRaisesRegex(RuntimeError, "worker failed"):
                        sweep.run(args)
                else:
                    sweep.run(args)
                    self.assertEqual(
                        len(json.loads((out / "summary.json").read_text())["cases"]),
                        len(selected),
                    )
            self.assertEqual(
                active, 0, "all workers joined, including the failure path"
            )
            self.assertLessEqual(peak, jobs)

    def test_one_worker_stays_bounded_and_joins(self):
        self.exercise(jobs=1, fail=False)

    def test_continuous_preparation_with_one_worker_does_not_overlap_warmup(self):
        self.exercise(jobs=1, fail=False, continuous=True)

    def test_worker_failure_is_reraised_after_other_workers_join(self):
        self.exercise(jobs=2, fail=True)


class FullReplay(unittest.TestCase):
    def test_completion_is_structural_not_a_pacing_or_balance_threshold(self):
        d = data()
        self.assertTrue(sweep.complete_run(d))
        d["report"]["lowestHpShare"] = 0.01
        d["report"]["level"] = 1
        self.assertTrue(sweep.complete_run(d))
        d["report"]["fallen"] = True
        self.assertFalse(sweep.complete_run(d))
        d["report"].update(fallen=False, survivalS=1799.999)
        self.assertFalse(sweep.complete_run(d))
        d["report"].update(survivalS=1800, done=False)
        self.assertFalse(sweep.complete_run(d))

    def test_exact_matrix_can_select_a_completed_normal_case_after_a_failed_case(self):
        with tempfile.TemporaryDirectory() as folder:
            home = Path(folder)
            reports = home / "reports"
            reports.mkdir()
            stamps = home / "ci-stamps"
            stamps.mkdir()
            (stamps / "survivors-key-False").write_text(str(reports))
            first = sweep.Case("cached", "original", "positioning", 1)
            second = sweep.Case("cached", "reverse", "positioning", 1)
            failed = data()
            failed["report"]["fallen"] = True
            (reports / f"{first.name}.json").write_text(json.dumps(failed))
            (reports / f"{second.name}.json").write_text(json.dumps(data()))
            with (
                patch.object(
                    sweep, "input_key", side_effect=lambda flag: f"key-{flag}"
                ),
                patch.object(sweep, "cases", return_value=[first, second]),
            ):
                case, session, result = sweep.matrix_run(home)
                self.assertEqual(case, second)
                self.assertIsNone(session)
                self.assertFalse(result["noHeroDamage"])
            with patch.object(sweep, "input_key", return_value=None):
                self.assertIsNone(
                    sweep.matrix_run(home),
                    "dirty/different inputs cannot reuse a matrix",
                )

    def exercise(self, mismatch: bool):
        with tempfile.TemporaryDirectory() as folder:
            args = Namespace(
                release=False,
                replay_case=None,
                force=True,
                jobs=1,
                out=Path(folder) / "out",
            )
            commands = []

            def checked(command, *args, **kwargs):
                commands.append(command)
                return subprocess.CompletedProcess(command, 0, "the same", "")

            def case(binary, selected, out, minutes, deadline, **kwargs):
                self.assertTrue(kwargs["soak"])
                d = data()
                d["noHeroDamage"] = True
                d["report"]["settledRecords"] = 123
                session = out / kwargs["record"]
                session.mkdir()
                (session / "meta.json").write_text(
                    json.dumps(
                        {
                            "runs": [
                                {
                                    "noHeroDamage": not mismatch,
                                    "settledRecords": 123,
                                    "settledHash": d["report"]["settledHash"],
                                }
                            ]
                        }
                    )
                )
                return d

            with (
                patch.object(sweep, "enter_qos"),
                patch.object(sweep, "input_key", return_value=None),
                patch.object(sweep, "matrix_run", return_value=None),
                patch.object(sweep, "checked", side_effect=checked),
                patch.object(sweep, "run_case", side_effect=case),
                redirect_stdout(io.StringIO()),
            ):
                if mismatch:
                    with self.assertRaisesRegex(ValueError, "damage modes differ"):
                        sweep.run_replay(args)
                    self.assertEqual(
                        len(commands), 1, "bad metadata cannot reach replay"
                    )
                else:
                    sweep.run_replay(args)
                    report = json.loads((args.out / "summary.json").read_text())
                    self.assertTrue(report["runnerDiagnostic"])
                    self.assertIsNone(report["survivalVerdict"])
                    self.assertEqual(commands[-1][1], "replay")
                    self.assertTrue((args.out / "replay.log").is_file())

    def test_diagnostic_is_labeled_and_replayed_after_a_full_run(self):
        self.exercise(mismatch=False)

    def test_recording_and_report_damage_modes_must_agree(self):
        self.exercise(mismatch=True)


if __name__ == "__main__":
    unittest.main()
