# A7: corpus comparison (47 of 61 tracks)

`taskpolicy -c utility native/target/release/ps analysis-parity --all --jobs 8` on 2026-10-24, stopped
after 47 tracks: the host was shared with another executor's builds (load 20-41), so the last 14
were still running after 90 minutes. Each line is the tool's per-track summary: committed beats
within 45 ms of the reference (after 4 s, as `stream.py`'s `accuracy`), Rust at D = 0 against
Python's committed stream, and Rust at the live D = 0.67 s; the real-time factor is the wall time
of both runs over the audio's length under that load (the analysis thread waits for hindsight
decodes when it runs faster than real time). The release stage runs every track at D = 0.

| Track | Within 45 ms: Rust (D = 0) | Python | Rust (D = 0.67 s) | Wall RTF |
|---|---|---|---|---|
| accelerando-100-140 | 0.798 | 0.798 | 0.798 | 0.688 |
| beethoven-fifth | 0.473 | 0.473 | 0.473 | 1.393 |
| bumblebee | 0.754 | 0.754 | 0.739 | 0.724 |
| dnb-174 | 1.000 | 1.000 | 1.000 | 0.639 |
| hiphop-92-swing | 0.506 | 0.506 | 0.506 | 0.635 |
| house-128 | 1.000 | 1.000 | 1.000 | 0.623 |
| mountain-king | 0.618 | 0.618 | 0.618 | 1.111 |
| notjam-baroque-chips | 1.000 | 1.000 | 1.000 | 3.534 |
| notjam-battle-ceases | 0.750 | 0.750 | 0.750 | 1.501 |
| notjam-battle-commences | 1.000 | 1.000 | 1.000 | 2.837 |
| notjam-breakbeat-chips | 0.500 | 0.500 | 0.500 | 1.013 |
| notjam-chill-menu | 0.802 | 0.802 | 0.784 | 1.042 |
| notjam-crashing-out | 1.000 | 1.000 | 1.000 | 2.009 |
| notjam-critical-theme | 0.995 | 0.995 | 0.995 | 1.716 |
| notjam-crystal-caverns | 0.036 | 0.036 | 0.036 | 3.109 |
| notjam-dark-cavern-phone-home | 0.856 | 0.856 | 0.856 | 2.321 |
| notjam-descend-gameplay | 0.960 | 0.968 | 0.956 | 1.625 |
| notjam-drag-and-dread-theme | 0.754 | 0.754 | 0.724 | 1.808 |
| notjam-grumpy-shopkeep | 1.000 | 1.000 | 1.000 | 4.522 |
| notjam-harold-paranormal-instigator-theme | 0.678 | 0.678 | 0.673 | 1.731 |
| notjam-klepto-linda-caverns-a | 0.631 | 0.631 | 0.617 | 2.671 |
| notjam-klepto-linda-caverns-b | 0.386 | 0.386 | 0.361 | 2.745 |
| notjam-klepto-linda-credits | 1.000 | 1.000 | 1.000 | 0.710 |
| notjam-klepto-linda-mountain-a | 0.635 | 0.635 | 0.635 | 1.119 |
| notjam-klepto-linda-mountain-b | 1.000 | 1.000 | 1.000 | 1.633 |
| notjam-klepto-linda-titles | 1.000 | 1.000 | 1.000 | 1.422 |
| notjam-lazy-river-rally | 0.500 | 0.500 | 0.500 | 2.593 |
| notjam-leading-the-charge | 0.515 | 0.515 | 0.515 | 3.445 |
| notjam-lich-king-ascendant | 0.415 | 0.415 | 0.407 | 2.796 |
| notjam-meltdown-theme | 0.971 | 0.971 | 0.974 | 1.601 |
| notjam-peak-performance | 1.000 | 1.000 | 1.000 | 2.477 |
| notjam-pitcher-perfect-theme | 1.000 | 1.000 | 1.000 | 2.544 |
| notjam-rainy-foothills | 1.000 | 1.000 | 1.000 | 2.305 |
| notjam-really-long-arms | 1.000 | 1.000 | 1.000 | 1.379 |
| notjam-safe-space | 1.000 | 1.000 | 1.000 | 2.654 |
| notjam-seeing-double | 0.956 | 0.956 | 0.956 | 1.510 |
| notjam-solemn-advance | 0.500 | 0.500 | 0.500 | 2.316 |
| notjam-switch-with-me-theme | 1.000 | 1.000 | 1.000 | 1.970 |
| notjam-the-wall | 1.000 | 1.000 | 1.000 | 4.557 |
| notjam-title-theme-phone-home | 1.000 | 1.000 | 1.000 | 1.706 |
| notjam-type-cast-theme | 0.732 | 0.732 | 0.739 | 1.997 |
| notjam-untitled-track-01 | 0.896 | 0.896 | 0.896 | 2.055 |
| notjam-victory-lap | 1.000 | 1.000 | 1.000 | 1.702 |
| notjam-waltz-of-the-lich-king | 0.713 | 0.713 | 0.713 | 3.870 |
| orpheus-galop | 0.582 | 0.582 | 0.576 | 1.009 |
| pop-arrangement | 0.844 | 0.844 | 0.844 | 1.932 |
| william-tell-finale | 0.745 | 0.745 | 0.736 | 1.200 |

At D = 0, 46 of 47 tracks have exactly Python's hit rate (to the 3 places printed). At the live
delay 10 tracks lose accuracy: bumblebee 0.015, notjam-chill-menu 0.018, notjam-descend-gameplay 0.012, notjam-drag-and-dread-theme 0.030, notjam-harold-paranormal-instigator-theme 0.005, notjam-klepto-linda-caverns-a 0.014, notjam-klepto-linda-caverns-b 0.025, notjam-lich-king-ascendant 0.008, orpheus-galop 0.006, william-tell-finale 0.009; the rest are unchanged.
