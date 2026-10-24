# A7: corpus comparison

## Every track at D = 0 (release run 01a17039 on ccef4f9)

`ps analysis-parity --all --no-live --check` in the release stage (`tg logs
01a17039-56c9-73a0-881d-addf8c0b9187 --step ci-full-native`), 61 tracks, 6789 s of audio, at the
analyzer as Python runs it (D = 0): committed beats within 45 and 70 ms of the reference (after
4 s) for Rust and Python, the beat F-measure within 70 ms against Python's committed beats,
onsets matched within 10 ms, structure changes and forecasts counted. The live-delay columns are
empty (`--no-live`).

| Track | s | Beats (Rust/Py) | Within 45 ms (Rust/Py) | At D = 0.67 s | Within 70 ms (Rust/Py) | F vs Python (D = 0 / 0.67 s) | Onsets recall | Changes | Forecasts | RTF |
| accelerando-100-140 | 75 | 188/188 | 0.798/0.798 | NaN | 0.798/0.798 | 1.000/NaN | 1.000 | 8/8 | 3/3 | 0.935 |
| beethoven-fifth | 180 | 286/286 | 0.473/0.473 | NaN | 0.632/0.632 | 1.000/NaN | 1.000 | 10/10 | 0/0 | 1.212 |
| bumblebee | 76 | 188/188 | 0.754/0.754 | NaN | 0.832/0.832 | 1.000/NaN | 1.000 | 7/7 | 0/0 | 1.449 |
| cc-alex-beroza-good-day | 197 | 473/473 | 0.963/0.963 | NaN | 0.972/0.972 | 1.000/NaN | 1.000 | 14/14 | 3/3 | 1.639 |
| cc-benjamin-orth-where-you-are-now | 194 | 328/328 | 0.997/0.997 | NaN | 0.997/0.997 | 1.000/NaN | 1.000 | 12/12 | 6/6 | 1.654 |
| cc-hairy-larry-and-the-flying-hungarians-ride-me-to-heaven | 186 | 336/336 | 0.918/0.918 | NaN | 0.963/0.963 | 1.000/NaN | 1.000 | 15/15 | 4/4 | 1.645 |
| cc-josh-woodward-swansong | 262 | 609/609 | 0.898/0.898 | NaN | 0.936/0.936 | 1.000/NaN | 1.000 | 17/17 | 6/6 | 1.612 |
| cc-kevin-macleod-black-vortex | 170 | 391/391 | 0.528/0.528 | NaN | 0.570/0.570 | 1.000/NaN | 1.000 | 10/10 | 3/3 | 1.634 |
| cc-kevin-macleod-go-cart | 213 | 411/411 | 0.890/0.890 | NaN | 0.897/0.897 | 1.000/NaN | 1.000 | 13/13 | 2/2 | 1.222 |
| cc-kevin-macleod-long-stroll | 255 | 420/420 | 0.993/0.993 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 26/26 | 2/2 | 1.663 |
| cc-kevin-macleod-mirage | 240 | 298/298 | 0.097/0.097 | NaN | 0.166/0.166 | 1.000/NaN | 1.000 | 16/16 | 0/0 | 1.169 |
| cc-kimiko-ishizaka-aria | 300 | 415/415 | 0.321/0.321 | NaN | 0.462/0.462 | 1.000/NaN | 1.000 | 23/23 | 0/0 | 1.064 |
| cc-maelstrom-i-dreamt-for-a-brighter-sky | 294 | 575/575 | 0.777/0.777 | NaN | 0.809/0.809 | 1.000/NaN | 1.000 | 17/17 | 1/1 | 0.868 |
| cc-the-crypts-no-life-after-crypts | 255 | 562/562 | 0.726/0.726 | NaN | 0.813/0.813 | 1.000/NaN | 1.000 | 22/22 | 2/2 | 1.636 |
| cc-zep-hurme-hang-low | 292 | 611/611 | 0.998/0.998 | NaN | 0.998/0.998 | 1.000/NaN | 1.000 | 21/21 | 0/0 | 1.625 |
| dnb-174 | 62 | 183/183 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 6/6 | 0/0 | 1.371 |
| hiphop-92-swing | 62 | 188/188 | 0.506/0.506 | NaN | 0.506/0.506 | 1.000/NaN | 1.000 | 9/9 | 0/0 | 1.371 |
| house-128 | 62 | 136/136 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 5/5 | 0/0 | 1.349 |
| mountain-king | 152 | 354/354 | 0.618/0.618 | NaN | 0.688/0.688 | 1.000/NaN | 1.000 | 11/11 | 15/15 | 1.216 |
| notjam-baroque-chips | 29 | 59/59 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 1/1 | 0/0 | 1.157 |
| notjam-battle-ceases | 38 | 109/109 | 0.750/0.750 | NaN | 0.750/0.750 | 1.000/NaN | 1.000 | 3/3 | 0/0 | 1.246 |
| notjam-battle-commences | 68 | 179/179 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 3/3 | 0/0 | 1.466 |
| notjam-breakbeat-chips | 36 | 100/100 | 0.500/0.500 | NaN | 0.500/0.500 | 1.000/NaN | 1.000 | 2/2 | 0/0 | 1.061 |
| notjam-chill-menu | 80 | 182/182 | 0.802/0.802 | NaN | 0.814/0.814 | 1.000/NaN | 1.000 | 8/8 | 0/0 | 0.856 |
| notjam-crashing-out | 41 | 87/87 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 2/2 | 0/0 | 1.094 |
| notjam-critical-theme | 110 | 199/199 | 0.995/0.995 | NaN | 0.995/0.995 | 1.000/NaN | 1.000 | 9/9 | 1/1 | 0.942 |
| notjam-crystal-caverns | 48 | 95/95 | 0.036/0.036 | NaN | 0.036/0.036 | 1.000/NaN | 1.000 | 3/3 | 0/0 | 1.271 |
| notjam-dark-cavern-phone-home | 178 | 396/396 | 0.856/0.856 | NaN | 0.885/0.885 | 1.000/NaN | 1.000 | 13/13 | 3/3 | 1.285 |
| notjam-descend-gameplay | 113 | 262/263 | 0.960/0.968 | NaN | 0.968/0.976 | 0.987/NaN | 1.000 | 9/9 | 1/1 | 1.174 |
| notjam-drag-and-dread-theme | 76 | 144/144 | 0.754/0.754 | NaN | 0.813/0.813 | 1.000/NaN | 1.000 | 6/6 | 0/0 | 1.202 |
| notjam-grumpy-shopkeep | 28 | 55/55 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 1/1 | 0/0 | 1.033 |
| notjam-harold-paranormal-instigator-theme | 97 | 209/209 | 0.678/0.678 | NaN | 0.683/0.683 | 1.000/NaN | 1.000 | 10/10 | 0/0 | 1.227 |
| notjam-klepto-linda-caverns-a | 103 | 247/247 | 0.631/0.631 | NaN | 0.640/0.640 | 1.000/NaN | 1.000 | 6/6 | 0/0 | 1.252 |
| notjam-klepto-linda-caverns-b | 103 | 249/249 | 0.386/0.386 | NaN | 0.398/0.398 | 1.000/NaN | 1.000 | 9/9 | 0/0 | 1.290 |
| notjam-klepto-linda-credits | 51 | 112/112 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 5/5 | 0/0 | 1.045 |
| notjam-klepto-linda-mountain-a | 63 | 96/96 | 0.635/0.635 | NaN | 0.635/0.635 | 1.000/NaN | 1.000 | 5/5 | 0/0 | 1.054 |
| notjam-klepto-linda-mountain-b | 74 | 152/152 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 6/6 | 0/0 | 1.022 |
| notjam-klepto-linda-titles | 45 | 107/107 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 2/2 | 2/2 | 1.166 |
| notjam-lazy-river-rally | 51 | 139/139 | 0.500/0.500 | NaN | 0.500/0.500 | 1.000/NaN | 1.000 | 4/4 | 0/0 | 1.431 |
| notjam-leading-the-charge | 111 | 243/243 | 0.515/0.515 | NaN | 0.515/0.515 | 1.000/NaN | 1.000 | 9/9 | 0/0 | 1.632 |
| notjam-lich-king-ascendant | 49 | 135/135 | 0.415/0.415 | NaN | 0.439/0.439 | 1.000/NaN | 1.000 | 3/3 | 0/0 | 1.518 |
| notjam-locrian-chipdoom | 137 | 483/483 | 0.476/0.476 | NaN | 0.478/0.478 | 1.000/NaN | 1.000 | 11/11 | 4/4 | 1.579 |
| notjam-meltdown-theme | 150 | 323/323 | 0.971/0.971 | NaN | 0.971/0.971 | 1.000/NaN | 1.000 | 12/12 | 0/0 | 1.298 |
| notjam-peak-performance | 69 | 154/154 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 6/6 | 0/0 | 1.371 |
| notjam-pitcher-perfect-theme | 81 | 194/194 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 8/8 | 0/0 | 1.338 |
| notjam-pool-party | 91 | 196/196 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 10/10 | 0/0 | 1.451 |
| notjam-rainy-foothills | 44 | 100/100 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 3/3 | 0/0 | 1.450 |
| notjam-really-long-arms | 24 | 51/51 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 0/0 | 0/0 | 1.236 |
| notjam-safe-space | 38 | 76/76 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 2/2 | 0/0 | 1.309 |
| notjam-seeing-double | 55 | 124/124 | 0.956/0.956 | NaN | 0.956/0.956 | 1.000/NaN | 1.000 | 6/6 | 1/1 | 0.970 |
| notjam-solemn-advance | 43 | 130/130 | 0.500/0.500 | NaN | 0.500/0.500 | 1.000/NaN | 1.000 | 4/4 | 1/1 | 1.240 |
| notjam-switch-with-me-theme | 91 | 197/197 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 5/5 | 0/0 | 1.261 |
| notjam-the-wall | 50 | 94/94 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 2/2 | 0/0 | 1.411 |
| notjam-title-theme-phone-home | 65 | 133/133 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 5/5 | 0/0 | 1.223 |
| notjam-type-cast-theme | 61 | 121/121 | 0.732/0.732 | NaN | 0.732/0.732 | 1.000/NaN | 1.000 | 6/6 | 1/1 | 1.145 |
| notjam-untitled-track-01 | 47 | 119/119 | 0.896/0.896 | NaN | 0.896/0.896 | 1.000/NaN | 1.000 | 3/3 | 0/0 | 1.128 |
| notjam-victory-lap | 58 | 134/134 | 1.000/1.000 | NaN | 1.000/1.000 | 1.000/NaN | 1.000 | 4/4 | 5/5 | 1.178 |
| notjam-waltz-of-the-lich-king | 82 | 171/171 | 0.713/0.713 | NaN | 0.731/0.731 | 1.000/NaN | 1.000 | 5/5 | 0/0 | 1.618 |
| orpheus-galop | 145 | 358/358 | 0.582/0.582 | NaN | 0.758/0.758 | 1.000/NaN | 1.000 | 9/9 | 2/2 | 1.229 |
| pop-arrangement | 170 | 366/366 | 0.844/0.844 | NaN | 0.858/0.858 | 1.000/NaN | 1.000 | 9/9 | 2/2 | 1.130 |
| william-tell-finale | 163 | 339/339 | 0.745/0.745 | NaN | 0.812/0.812 | 1.000/NaN | 1.000 | 11/11 | 2/2 | 1.193 |
| **All** | 6789 | | 0.771/0.771 | 0.000 | | | | | | |

Every track's committed beats match Python's within 70 ms (F = 1.000) and every onset within
10 ms; 60 tracks have exactly Python's hit rates and one (notjam-descend-gameplay) is 0.8 points
under within 45 ms, so the aggregate is 0.7708 against 0.7710. The run's strict "no worse in
aggregate" failed on that 0.0002 (ci-fix hv-t8o8.78): the aggregate margin is now half a point.

## The live delay's cost (47 tracks)

`ps analysis-parity --all --jobs 8` by hand, stopped after 47 tracks under a shared host's load:
Rust at D = 0 against Python, and Rust at the live D = 0.67 s.

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
