# SH2 cold warden review

Fresh read-only same-host warden reviewed source tree `d97c484171dbd66220db48522bce763e17225b2e` against `fd8ee17c06c53a3e53048c1dfbd1ee9bb5397d54`. Required for the runner-adjacent speculative pickup projection. Self-review checklist recorded before dispatch. No confirmation review.

Both P2 findings confirmed and fixed:

1. The gameplay effects ceiling also clamped calibration clicks to zero while no music played. Synthesized reference/calibration clicks now bypass the gameplay ceiling, keeping the common output limiter. A PCM mixer regression verifies audible clicks in silent Direct System mode; sample-based gameplay effects remain silent there.
2. Direct System’s absolute RMS accessor averaged ten overlapping levels in dB, retaining stale loudness after a drop. It now returns the current 400 ms linear-energy window. A PCM step regression moves from RMS 0.1 to 0.01, verifies the new value at 400 ms and then exact silence.

No additional actionable chain, budget, cancellation, restart or hash-projection findings. The long recording check uses samples and a fixed synthetic RMS; neither confirmed fix changes its scheduler/cap path. Its binary preceded these two mix fixes and the load-basis validation/restart-tail guard; those later paths are covered by final focused/aggregate tests and the final short smoke.
