#!/bin/sh
# Gate time (scripts/ci-gate, forced to every section) idle and under a concurrent 10-job oracle export.
cd /Users/dthurn/playlist_survivors/.worktrees/batch-priority
RUNS=cc-maelstrom-i-dreamt-for-a-brighter-sky/reactive-moderate,cc-maelstrom-i-dreamt-for-a-brighter-sky/mash4,cc-josh-woodward-swansong/reactive-moderate,cc-josh-woodward-swansong/mash4,cc-kevin-macleod-go-cart/reactive-moderate,cc-kevin-macleod-go-cart/mash4,cc-kevin-macleod-mirage/reactive-moderate,cc-kevin-macleod-mirage/mash4,cc-benjamin-orth-where-you-are-now/reactive-moderate,cc-benjamin-orth-where-you-are-now/mash4,cc-kimiko-ishizaka-aria/reactive-human,cc-kimiko-ishizaka-aria/mash8,pop-arrangement/reactive-human,pop-arrangement/mash8,orpheus-galop/mash2,orpheus-galop/mash8,orpheus-galop/reactive-half,orpheus-galop/holder,william-tell-finale/mash4,mountain-king/mash4
gate() { s=$(python3 -c 'import time;print(time.time())'); CHANGES_BASE=866d95c^ scripts/ci-gate >/dev/null 2>&1; rc=$?; echo "$1 rc=$rc $(python3 -c "import time;print(round(time.time()-$s,1))") s load=$(sysctl -n vm.loadavg)"; }
gate warm; gate idle; gate idle
for prio in 0 10 0 10; do
  nice -n $prio npx tsx scripts/oracle-export.ts runs --only $RUNS --jobs 10 >/dev/null 2>&1 &
  sleep 25
  gate "export-nice$prio"
  pkill -f 'oracle-export.ts runs' ; wait 2>/dev/null; sleep 5
done
git checkout -q -- native/oracle
