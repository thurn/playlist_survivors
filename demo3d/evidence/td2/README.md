# TD2: assets, annotations and style stills

Measured on Cygnus on 2026-10-28 with `demo3d unity AnnotateClips` and `demo3d unity StyleStills` (warm editor: 11 s and 5 s).

## The roster

| Type | Kind | Rig | Height in demo (m) | Bones | Triangles | Radius (m) |
|---|---|---|---|---|---|---|
| skeleton | fodder | generic | 1.756 | 32 | 9,310 | 0.4 |
| goblin | fodder | generic | 1.496 | 66 | 12,656 | 0.4 |
| slime | fodder | generic | 0.721 | 8 | 1,428 | 0.4 |
| bat | fodder | generic | 0.239 | 74 | 3,092 | 0.4 |
| mushroom | fodder | generic | 1.299 | 15 | 1,638 | 0.4 |
| demon | fodder | generic | 1.929 | 59 | 7,590 | 0.4 |
| bigork | elite | humanoid | 2.937 | 49 | 56,296 | 0.7 |
| troll | elite | humanoid | 2.937 | 49 | 56,296 | 0.7 |
| evilgod | rejected-boss | humanoid | 4.185 | 49 | 30,125 | 1.0 |
| dragon | boss | generic | 4.788 | 97 | 11,440 | 1.6 |
| hero | hero | humanoid | 1.842 | 49 | 12,142 | 0.5 |

Nine mesh types and eight rigs, as crowd.md plans: six Magic Pig Games fodder types (each its own Generic rig),
two Fantasy Rivals elites and the dragon as boss; the hero is the POLYGON Knights knight. The Evil God was the other boss
candidate ([boss-candidates.jpg](boss-candidates.jpg)) and stays in the roster as `rejected-boss` so that still can be remade.

## Clip annotations

Proposals by the clip annotator ([clip-annotations.json](../../Assets/Demo3D/Data/clip-annotations.json)); the contact sheets
show each clip in 8 frames with the proposed impact framed in red (`contact-<type>.jpg`). Times in seconds.

| Type | Role | Clip | Length | Impact | End | Stride or root motion (m) | Synty's own hit frame |
|---|---|---|---|---|---|---|---|
| skeleton | walk | Walk | 1.3 |  |  | 1.224 |  |
| skeleton | attack | Attack 1 | 1.5 | 0.75 | 1.5 |  |  |
| skeleton | attack2 | Attack 2 | 2.0 | 0.817 | 2.017 |  |  |
| skeleton | hit | Got Hit | 1.933 | 0.133 | 1.6 |  |  |
| skeleton | death | Death | 2.1 | 0.617 | 1.183 |  |  |
| goblin | walk | Walk | 1.067 |  |  | 1.015 |  |
| goblin | attack | Attack1 | 2.267 | 1.017 | 1.7 |  |  |
| goblin | attack2 | Attack2 | 3.167 | 1.167 | 1.417 |  |  |
| goblin | hit | GotHit | 2.167 | 0.117 | 1.617 |  |  |
| goblin | death | Death | 2.0 | 0.95 | 1.267 |  |  |
| slime | walk | Walk Cycle | 4.042 |  |  | 0.832 |  |
| slime | attack | Stab Attack | 2.792 | 0.867 | 2.333 |  |  |
| slime | attack2 | Cannonball Attack | 4.25 | 1.2 | 2.85 |  |  |
| slime | hit | Been Hit | 1.208 | 0.267 | 1.017 |  |  |
| slime | death | Die | 5.125 | 1.5 | 2.517 |  |  |
| bat | walk | Fly Forward | 2.0 |  |  | 4.424 |  |
| bat | attack | Attack 1 | 2.367 | 1.667 | 2.183 |  |  |
| bat | attack2 | Attack 2 | 2.6 | 1.967 | 2.383 |  |  |
| bat | hit | Got Hit | 2.267 | 0.2 | 2.067 |  |  |
| bat | death | Death 1 | 0.833 | 0.2 | 0.85 |  |  |
| mushroom | walk | MushWalk | 1.0 |  |  | 1.262 |  |
| mushroom | attack | MushAttack01 | 2.0 | 0.683 | 1.367 |  |  |
| mushroom | attack2 | MushAttack02 | 3.0 | 0.917 | 2.783 |  |  |
| mushroom | hit | MushHit | 1.167 | 0.05 | 0.983 |  |  |
| mushroom | death | MushDeath | 1.0 | 0.883 | 0.967 |  |  |
| demon | walk | walk | 0.75 |  |  | 1.477 |  |
| demon | attack | attack1 | 1.25 | 0.35 | 1.233 |  |  |
| demon | attack2 | attack2 | 1.25 | 0.367 | 1.183 |  |  |
| demon | hit | hit reaction | 1.25 | 0.3 | 1.25 |  |  |
| demon | death | death | 2.042 | 0.6 | 2.05 |  |  |
| bigork | walk | A_Walk_F_Masc | 1.033 |  |  | 2.15 |  |
| bigork | attack | A_Attack_HeavyCombo01A_Sword | 2.033 | 1.133 | 1.4 |  | 0.967 |
| bigork | attack2 | A_Attack_HeavyStab01_Sword | 1.367 | 0.883 | 0.983 |  | 0.867 |
| bigork | hit | A_Hit_F_React_Sword | 0.833 | 0.05 | 0.617 |  |  |
| bigork | death | A_Death_F_01_Sword | 1.433 | 0.967 | 1.383 |  |  |
| troll | walk | A_Walk_F_Masc | 1.033 |  |  | 2.15 |  |
| troll | attack | A_Attack_HeavyCombo01B_Sword | 1.667 | 0.85 | 1.05 |  | 0.8 |
| troll | attack2 | A_Attack_HeavyCombo01C_Sword | 1.6 | 0.95 | 1.267 |  | 0.933 |
| troll | hit | A_Hit_L_React_Sword | 0.833 | 0.017 | 0.1 |  | 0.0 |
| troll | death | A_Death_B_01_Sword | 1.7 | 1.133 | 1.517 |  |  |
| evilgod | walk | A_Walk_F_Masc | 1.033 |  |  | 2.907 |  |
| evilgod | attack | A_Attack_HeavyFlourish_Sword | 2.033 | 1.433 | 1.65 |  | 1.367 |
| evilgod | attack2 | A_Attack_HeavyCombo01A_Sword | 2.033 | 1.133 | 1.417 |  | 0.967 |
| evilgod | hit | A_Hit_F_React_Sword | 0.833 | 0.05 | 0.617 |  |  |
| evilgod | death | A_Death_B_01_Sword | 1.7 | 1.133 | 1.55 |  |  |
| dragon | walk | Walk | 1.333 |  |  | 2.625 |  |
| dragon | attack | Attack 1 | 1.5 | 0.45 | 1.467 |  |  |
| dragon | attack2 | Tail Whip L | 2.167 | 1.167 | 1.883 |  |  |
| dragon | hit | Got Hit 1 | 0.833 | 0.1 | 0.817 |  |  |
| dragon | death | Death | 2.667 | 0.1 | 1.967 |  |  |
| hero | walk | A_Walk_F_Masc | 1.033 |  |  | 1.446 |  |
| hero | run | A_Run_F_Masc | 0.7 |  |  | 1.745 |  |
| hero | slashL | A_Attack_LightCombo01A_Sword | 0.8 | 0.35 | 0.717 | 0.0 | 0.333 |
| hero | slashR | A_Attack_LightCombo01B_Sword | 0.667 | 0.267 | 0.45 | 1.213 | 0.167 |
| hero | slash3 | A_Attack_LightCombo01C_Sword | 0.733 | 0.417 | 0.467 | 1.426 | 0.367 |
| hero | thrust | A_Attack_HeavyStab01_Sword | 1.367 | 0.883 | 0.983 | 0.485 | 0.867 |
| hero | spin | A_Attack_HeavyFlourish_Sword | 2.033 | 1.433 | 1.65 | 1.205 | 1.367 |
| hero | slam | A_Attack_LightLeaping01_Sword | 1.567 | 1.1 | 1.167 | 7.089 | 1.0 |
| hero | parry | A_Parry_F_Sword | 0.467 | 0.233 | 0.4 |  |  |

The manifest lists 259 vendor assets; their dependency closure is 673 files (304 MB) in `Assets/Vendor/`.

