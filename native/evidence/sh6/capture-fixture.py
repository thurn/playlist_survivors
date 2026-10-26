"""Convert a kept Rust analyzer session to a temporary still input; no audio/game extraction.
Usage: python3 capture-fixture.py <session>/stream.jsonl <output>/stream.json
This is presentation evidence, not a survival report or a replay oracle.
"""
import json
import sys
from pathlib import Path

rows = [json.loads(line) for line in Path(sys.argv[1]).read_text().splitlines()]
groups = {}
for row in rows:
    entry = row['entry'].copy()
    entry['knownAt'] = max(entry['knownAt'], row['arrived'])
    if row['group'] == 'beat' and entry['knownAt'] > entry['t']:
        continue  # Same startup late-beat policy as session::rebase.
    groups.setdefault(row['group'], []).append(entry)
out = {
    'version': 1,
    'audio': {'file': 'Rust recorded analysis of repeated public house-128',
              'duration': max(e['t'] for e in groups['tick']), 'sampleRate': 44100},
    'params': {'gate': .2, 'tickS': .05, 'toolVersion': 7},
    'beats': groups['beat'], 'ticks': groups['tick'], 'onsets': groups.get('onset', []),
    'hindsight': groups.get('hindsight', []), 'timbre': groups.get('timbre', []),
    'structure': {key+'s': groups.get(key, []) for key in ('change', 'jump', 'end')},
    'accents': {'forecast': groups.get('forecast', [])},
}
path = Path(sys.argv[2])
path.parent.mkdir(parents=True, exist_ok=False)
path.write_text(json.dumps(out))
print(f"{len(out['beats'])} beats, {len(out['ticks'])} ticks, {out['audio']['duration']}s")
