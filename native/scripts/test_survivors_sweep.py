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
    def test_matrix_covers_every_order_and_both_analysis_paths(self):
        cases = sweep.cases()
        self.assertEqual(
            {(c.mode, c.order) for c in cases},
            {
                ("cached", "original"),
                ("cached", "reverse"),
                ("cached", "rotate"),
                ("continuous", "original"),
            },
        )
        self.assertEqual(len({c.name for c in cases}), len(cases))
        self.assertTrue(all(c.bot == "positioning" for c in cases))

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
                if fail and case.order == "reverse":
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

    def exercise(self, mismatch: bool):
        with tempfile.TemporaryDirectory() as folder:
            session = Path(folder) / "normal-session"
            session.mkdir()
            d = data()
            d["report"]["settledRecords"] = 123
            (session / "meta.json").write_text(
                json.dumps(
                    {
                        "runs": [
                            {
                                "noHeroDamage": mismatch,
                                "settledRecords": 123,
                                "settledHash": d["report"]["settledHash"],
                            }
                        ]
                    }
                )
            )
            commands = []

            def checked(command, *args, **kwargs):
                commands.append(command)
                return subprocess.CompletedProcess(command, 0, "the same", "")

            with (
                patch.object(sweep, "checked", side_effect=checked),
                redirect_stdout(io.StringIO()),
            ):
                if mismatch:
                    with self.assertRaisesRegex(ValueError, "damage modes differ"):
                        sweep.replay_session(Path("ps"), session, d, 0)
                    self.assertEqual(commands, [], "bad metadata cannot reach replay")
                else:
                    result = sweep.replay_session(Path("ps"), session, d, 0)
                    self.assertEqual(commands, [["ps", "replay", str(session)]])
                    self.assertEqual(result["settledRecords"], 123)
                    self.assertTrue((Path(folder) / "replay.log").is_file())

    def test_a_full_matrix_replays_its_base_order_run(self):
        replays = []

        def fake_case(binary, case, out, minutes, deadline, *args, **kwargs):
            result = deepcopy(data(case.bot))
            result["requestedMinutes"] = minutes
            return result

        def fake_replay(binary, session, result, deadline):
            replays.append(session.name)
            return {"session": str(session)}

        with tempfile.TemporaryDirectory() as folder:
            out = Path(folder) / "reports"
            args = Namespace(
                jobs=3,
                only=None,
                minutes=30,
                assert_survival=True,
                release=False,
                force=True,
                out=out,
            )
            with (
                patch.object(sweep, "enter_qos"),
                patch.object(sweep, "input_key", return_value=None),
                patch.object(sweep, "run_case", fake_case),
                patch.object(sweep, "replay_session", fake_replay),
                patch.object(
                    sweep,
                    "checked",
                    return_value=subprocess.CompletedProcess([], 0, "", ""),
                ),
                redirect_stdout(io.StringIO()),
            ):
                sweep.run(args)
            report = json.loads((out / "summary.json").read_text())
        self.assertEqual(replays, ["normal-session"])
        self.assertEqual(report["replay"], {"session": str(out / "normal-session")})

    def test_a_recorded_run_is_replayed(self):
        self.exercise(mismatch=False)

    def test_recording_and_report_damage_modes_must_agree(self):
        self.exercise(mismatch=True)


if __name__ == "__main__":
    unittest.main()
