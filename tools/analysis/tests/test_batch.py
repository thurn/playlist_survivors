"""run_as_batch re-executes under taskpolicy -b on macOS, once."""

import os

import pytest

from ps_analysis import batch


def test_a_no_op_once_applied(monkeypatch):
    monkeypatch.setenv(batch.BATCH_ENV, "1")
    monkeypatch.setattr(os, "execv", lambda *a: pytest.fail("re-executed"))
    batch.run_as_batch()


def test_a_no_op_off_macos(monkeypatch):
    monkeypatch.delenv(batch.BATCH_ENV, raising=False)
    monkeypatch.setattr(batch.sys, "platform", "linux")
    monkeypatch.setattr(os, "execv", lambda *a: pytest.fail("re-executed"))
    batch.run_as_batch()


def test_re_executes_under_taskpolicy_on_macos(monkeypatch):
    monkeypatch.delenv(batch.BATCH_ENV, raising=False)
    monkeypatch.setattr(batch.sys, "platform", "darwin")
    monkeypatch.setattr(batch.shutil, "which", lambda _: "/usr/sbin/taskpolicy")
    monkeypatch.setattr(batch.os.path, "exists", lambda _: True)
    calls = []
    monkeypatch.setattr(os, "execv", lambda path, args: calls.append((path, args)))
    batch.run_as_batch()
    assert calls and calls[0][0] == "/usr/sbin/taskpolicy"
    assert calls[0][1][:3] == ["taskpolicy", "-b", batch.sys.executable]
    assert os.environ[batch.BATCH_ENV] == "1"
