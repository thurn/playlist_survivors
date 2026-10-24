"""Long batch jobs run below Tollgate's gate and release steps (hv-t8o8.55).

On macOS the command re-executes itself under ``taskpolicy -b`` (background QoS:
efficiency cores, throttled I/O), which its child processes inherit; ``nice``
made no measurable difference to a concurrent gate, background QoS a third.
"""

from __future__ import annotations

import os
import shutil
import sys

BATCH_ENV = "PS_BATCH"


def run_as_batch() -> None:
    """Re-exec this command in the background QoS class; a no-op elsewhere or once applied."""
    if sys.platform != "darwin" or os.environ.get(BATCH_ENV):
        return
    taskpolicy = shutil.which("taskpolicy") or "/usr/sbin/taskpolicy"
    if not os.path.exists(taskpolicy):
        return
    os.environ[BATCH_ENV] = "1"
    os.execv(taskpolicy, ["taskpolicy", "-b", sys.executable, *sys.orig_argv[1:]])
