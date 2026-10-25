import json, sys, glob, bisect
ref = json.load(open(sys.argv[1]))["reference"]["beats"]
for mode in ("direct", "delayed"):
    d = glob.glob(f"{sys.argv[2]}/{mode}/*/stream.jsonl")[0]
    beats = []
    for l in open(d):
        a = json.loads(l)
        if a["group"] == "beat":
            e = a["entry"]
            beats.append((e["t"], e["knownAt"], a["arrived"]))
    ts = [b[0] for b in beats if b[0] >= 4.0 and ref[0] <= b[0] <= ref[-1]]
    def near(t):
        i = bisect.bisect_left(ref, t)
        return min(abs(t - ref[j]) for j in (i - 1, i) if 0 <= j < len(ref))
    w45 = sum(near(t) <= 0.045 for t in ts)
    lead = [b[0] - b[1] for b in beats]
    print(f"{mode}: {len(ts)} committed beats after 4 s, {100*w45/max(1,len(ts)):.1f}% within 45 ms; lead (t - knownAt) min {min(lead):.2f} s, median {sorted(lead)[len(lead)//2]:.2f} s")
