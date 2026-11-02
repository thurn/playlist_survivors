"""Every test runs against scratch caches, never this machine's (a test sets its own to read one)."""

from __future__ import annotations

from pathlib import Path

import pytest


@pytest.fixture(autouse=True)
def scratch_caches(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("CHAMPIONS_CACHE", str(tmp_path / "champions-cache"))
    monkeypatch.setenv("DEMO3D_CACHE", str(tmp_path / "frozen-cache"))
