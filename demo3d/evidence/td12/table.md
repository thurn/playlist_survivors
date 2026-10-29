## s3

| scene | load | aa | view | found N | on screen (alive + dying) | limit | GPU p99 | main p99 | render p99 | compute est. | fill | fills | sim tick p99 | particles | lights (max) | drop p99 | transp./px (beat peak) | full runs | rests on noisy | at 4.69 ms (fitted) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| S3 | demo | msaa4 | 30 m | 591 | 600 | gpu | 4.17 | 3.81 | 0.79 | 12.52 | 664 | no | 1.70 | 13,309 | 71 | 0 | 2.03 (6.20) | 6 | yes | 741 |
| S3 | demo | msaa4 | 40 m | 730 | 750 | gpu | 4.20 | 4.22 | 0.86 | 12.59 | 1,180 | no | 1.88 | 12,587 | 71 | 0 | 1.45 (4.28) | 2 | yes | 826 |
| S3 | demo | msaa4 | 55 m | 839 | 863 | gpu | 4.15 | 4.69 | 1.03 | 12.45 | 2,232 | no | 1.78 | 12,489 | 90 | 0 | 0.99 (3.13) | 6 | yes | 981 |
| S3 | light | msaa4 | 30 m | 750 | 748 | gpu | 4.08 | 4.50 | 0.83 | 12.23 | 664 | yes | 2.10 | 6,096 | 40 | 0 | 1.29 (4.09) | 6 | yes | 990 |
| S3 | light | msaa4 | 40 m | 862 | 881 | gpu | 4.13 | 4.89 | 0.89 | 12.39 | 1,180 | no | 2.29 | 5,183 | 47 | 0 | 0.80 (2.37) | 4 | yes | 1,005 |
| S3 | light | msaa4 | 55 m | 1,070 | 1,094 | gpu | 4.07 | 5.91 | 0.94 | 12.21 | 2,232 | no | 2.70 | 5,232 | 61 | 0 | 0.51 (1.47) | 2 | yes | 1,232 |
| S3 | heavy | msaa4 | 30 m | 417 | 435 | gpu | 4.04 | 2.70 | 0.79 | 12.13 | 664 | no | 0.97 | 18,635 | 83 | 0 | 5.28 (6.87) | 8 | yes | 615 |
| S3 | heavy | msaa4 | 40 m | 577 | 597 | gpu | 4.07 | 3.15 | 0.74 | 12.21 | 1,180 | no | 1.04 | 19,074 | 90 | 0 | 7.42 (8.15) | 8 | yes | 917 |
| S3 | heavy | msaa4 | 55 m | 1,017 | 1,010 | gpu | 4.04 | 5.03 | 0.96 | 12.13 | 2,232 | no | 1.93 | 8,165 | 63 | 0 | 6.06 (6.37) | 9 | yes | 1,141 |

## heavy55

| scene | load | aa | view | found N | on screen (alive + dying) | limit | GPU p99 | main p99 | render p99 | compute est. | fill | fills | sim tick p99 | particles | lights (max) | drop p99 | transp./px (beat peak) | full runs | rests on noisy | at 4.69 ms (fitted) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| S3 | heavy | msaa4 | 55 m | 735 | 770 | gpu | 4.14 | 3.79 | 0.91 | 12.43 | 2,232 | no | 1.00 | 18,251 | 103 | 0 | 3.71 (5.28) | 9 | yes | 977 |

## taa

| scene | load | aa | view | found N | on screen (alive + dying) | limit | GPU p99 | main p99 | render p99 | compute est. | fill | fills | sim tick p99 | particles | lights (max) | drop p99 | transp./px (beat peak) | full runs | rests on noisy | at 4.69 ms (fitted) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| S3 | demo | taa | 30 m | 1,593 (capacity) | 932 | capacity | 3.67 | 5.69 | 0.77 | 11.02 | 664 | yes | 7.97 | 12,709 | 105 | 0 | 5.86 (10.44) | 3 | yes | 1,593 |

## gpu469

| scene | load | aa | view | found N | on screen (alive + dying) | limit | GPU p99 | main p99 | render p99 | compute est. | fill | fills | sim tick p99 | particles | lights (max) | drop p99 | transp./px (beat peak) | full runs | rests on noisy | at 4.69 ms (fitted) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| S3 | demo | msaa4 | 30 m | 655 | 661 | gpu | 4.58 | 3.35 | 0.76 | 13.75 | 664 | no | 1.91 | 12,908 | 66 | 0 | 2.46 (7.74) | 6 | yes | 721 |

## baseline

| scene | load | aa | view | found N | on screen (alive + dying) | limit | GPU p99 | main p99 | render p99 | compute est. | fill | fills | sim tick p99 | particles | lights (max) | drop p99 | transp./px (beat peak) | full runs | rests on noisy | at 4.69 ms (fitted) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| S3 | demo | msaa4 | 40 m | 362 | 377 | main | 3.77 | 10.06 | 1.35 | 11.31 | 1,180 | no | 0.81 | 13,719 | 43 | 0 | 0.92 (2.13) | 7 | yes | 363 |

## s6

| scene | load | aa | view | found N | on screen (alive + dying) | limit | GPU p99 | main p99 | render p99 | compute est. | fill | fills | sim tick p99 | particles | lights (max) | drop p99 | transp./px (beat peak) | full runs | rests on noisy | at 4.69 ms (fitted) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| S6 | demo | msaa4 | 30 m | 873 | 627 | gpu | 4.18 | 4.25 | 0.73 | 12.55 | 664 | no | 4.62 | 9,485 | 72 | 0 | 5.85 (7.75) | 4 | yes | 1,110 |

**Envelope** (no width fills its screen, so 30 m): 30 m, 500 on screen (frontier 591).

