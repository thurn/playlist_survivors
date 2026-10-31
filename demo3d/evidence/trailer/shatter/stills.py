import json, sys, glob, math
# stills.py DUMP_DIR OFFSETS: the trailer still times, OFFSETS frames from each crystal break's impact frame (README.md)
dump = sys.argv[1]
offs = [int(x) for x in sys.argv[2].split(',')]
times = []
for f in sorted(glob.glob(dump + '/shot-*.json')):
    d = json.load(open(f))
    cr = {c['placement'] for c in d.get('crystals', [])}
    for b in d.get('breaks', []):
        if b['placement'] in cr:
            tau = b['time'] - 121.4
            k = math.ceil(tau * 60 - 1e-6)
            for o in offs: times.append((k + o) / 60)
print(','.join(f'{t:.4f}' for t in sorted(set(times))))
