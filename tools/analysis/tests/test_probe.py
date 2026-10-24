"""The probed library arithmetic still matches the native crate's pinned fixture.

A numpy, scipy or madmom upgrade that changes any entry fails here: re-check the native
analyzer against the new behaviour before regenerating the fixture with
`python -m ps_analysis.probe <fixture>`.
"""

import json

import numpy as np

from ps_analysis import probe


def test_the_libraries_still_compute_what_the_native_crate_pins():
    with np.errstate(divide="ignore"):
        now = probe.probe()
    pinned = json.loads(probe.FIXTURE.read_text())
    now.pop("versions")
    pinned.pop("versions")
    assert now == pinned
