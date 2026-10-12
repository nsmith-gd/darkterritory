#!/usr/bin/env python3
"""The Creature Tweaker's estimate, calibrated from measured nights (note 635).

    python3 tools/tweaker/calibrate.py --nights 'out/k/n-*.json' [--uncapped 'out/k/m-frontier:7-*.json'] > calibration.json

Each night is a `dt harness --as-app ... --enemies` report whose file name holds its route (`local:7`, `frontier:7`, ...).
For each tier it writes what the Tweaker's page (tools/tweaker/creature-tweaker.html) needs, as its store's
`model/calibration` document:
  - base[k]:  how readily the director picks k when it can, per night: its measured count over its weights then
              (`director.weights`, its tier weight); half a sighting is added so a kind never seen still answers a raise.
  - fixed[k]: kinds that come on their own condition, not the director's pick (the Stoker: a firebox held hot), a night.
  - relief:   the night's pressure the creatures used up, in cost (each sent takes 3 × its cost off; spec App. B.1, note 266).
A kind held under a cap or a night's chance when measured (the Knotter) can't show its own rate; --uncapped nights (a
build without the cap) give its share against the rest, carried to each tier by its tier weights.
`--check` prints the model's estimate for the measured settings beside what was measured.
"""
import argparse, glob, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TIERS = ["local", "frontier", "deadLines", "deepTerritory"]
FIXED = {"stoker"}
# Who can't be about at once (Director.Room, App. B.1's caps): one engaged creature per zone and sense, and the corrupted
# humans one at a time (director.maxCorrupted). From each creature's Zone and Sense in src/DarkTerritory.Sim/Enemies.
SLOTS = [
    {"kinds": ["knotter", "hotbox"], "why": "both in the train's running gear, felt"},
    {"kinds": ["draggers", "climbers"], "why": "both on the train's flanks, moving"},
    {"kinds": ["ribbits", "freightBeetle"], "why": "both on the ground at a stop, by sight"},
    {"kinds": ["gaunt", "sootChildren"], "why": "both on the ground at a stop, by sound"},
    {"kinds": ["brakeman", "passenger", "switchman", "grumbler"], "why": "the corrupted humans, one at a time"},
]
PLURAL = {"CinderHound": "cinderHounds", "Dragger": "draggers", "Climber": "climbers", "Follower": "followers", "Ribbit": "ribbits",
          "Sleepers": "sleepers"}


def key(kind):
    return PLURAL.get(kind, kind[0].lower() + kind[1:])


def load_jsonc(path):
    return parse_jsonc(open(path).read())


def parse_jsonc(s):
    """JSON with // comments and trailing commas, as content/ writes it."""
    out, i, ins = [], 0, False
    while i < len(s):
        c = s[i]
        if ins:
            out.append(c)
            if c == "\\":
                out.append(s[i + 1]); i += 2; continue
            if c == '"':
                ins = False
        elif c == '"':
            ins = True; out.append(c)
        elif s.startswith("//", i):
            while i < len(s) and s[i] != "\n":
                i += 1
            continue
        else:
            out.append(c)
        i += 1
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", "".join(out)))


def settings(enemies):
    d = enemies["director"]
    out = {}
    for k, cost in d["costs"].items():
        s = {"weight": d.get("weights", {}).get(k, 1), "cost": cost, "perNight": d.get("perNight", {}).get(k),
             "nightChance": d.get("nightChance", {}).get(k, 1)}
        if "tierWeights" in enemies.get(k, {}):
            s["tierWeights"] = enemies[k]["tierWeights"]
        out[k] = s
    return out


def tw(s, t):
    return s.get("tierWeights", {}).get(t, 1) if "tierWeights" in s else 1


def night1(st, cal, ids, fixed, absent, t):
    res, spent = {}, 0.0
    for k in fixed:
        res[k] = 0 if k in absent else min(cal["fixed"][k], st[k]["perNight"] if st[k]["perNight"] is not None else 1e9)
        spent += res[k] * st[k]["cost"]
    raw = {k: 0 if k in absent else max(0, cal["base"][k] * st[k]["weight"] * tw(st[k], t)) for k in ids}

    def alloc(n):
        a = {k: 0.0 for k in ids}
        left, open_ = n, [k for k in ids if raw[k] > 0]
        for _ in range(40):
            if left <= 1e-9 or not open_:
                break
            tot = sum(raw[k] for k in open_)
            nxt, used = [], 0.0
            for k in open_:
                cap = st[k]["perNight"] if st[k]["perNight"] is not None else 1e9
                if a[k] + left * raw[k] / tot >= cap:
                    used += cap - a[k]; a[k] = cap
                else:
                    nxt.append(k)
            if len(nxt) == len(open_):
                for k in open_:
                    a[k] += left * raw[k] / tot
                break
            left -= used; open_ = nxt
        return a

    cost = lambda a: sum(a[k] * st[k]["cost"] for k in ids)
    target = max(0, cal["relief"] - spent)
    lo, hi = 0.0, 400.0
    if cost(alloc(hi)) < target:
        lo = hi
    for _ in range(50):
        if hi - lo < 1e-4:
            break
        mid = (lo + hi) / 2
        if cost(alloc(mid)) < target:
            lo = mid
        else:
            hi = mid
    res.update(alloc(400 if lo == 400 else (lo + hi) / 2))
    return res


def estimate(st, cal, t):
    ids = [k for k in cal["base"] if k in st]
    fixed = [k for k in cal.get("fixed", {}) if k in st]
    chancey = [k for k in ids + fixed if st[k]["nightChance"] < 1][:10]
    out = {k: 0.0 for k in ids + fixed}
    for mask in range(1 << len(chancey)):
        p, absent = 1.0, set()
        for i, k in enumerate(chancey):
            c = st[k]["nightChance"]
            if mask & (1 << i):
                p *= c
            else:
                p *= 1 - c; absent.add(k)
        if p <= 0:
            continue
        for k, v in night1(st, cal, ids, fixed, absent, t).items():
            out[k] += p * v
    return out


def counts(files):
    nights, tot = 0, {}
    for f in files:
        try:
            spawned = json.load(open(f))["threats"]["spawned"]
        except Exception:
            continue
        nights += 1
        for kind, n in spawned.items():
            tot[key(kind)] = tot.get(key(kind), 0) + n
    return nights, tot


def tier_of(path):
    m = re.search(r"(local|frontier|deadLines|deepTerritory):\d+", os.path.basename(path))
    return m.group(1) if m else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--nights", required=True, help="glob of harness reports, the game's settings now")
    ap.add_argument("--uncapped", help="glob of harness reports from a build without the caps (frontier)")
    ap.add_argument("--enemies", default=os.path.join(ROOT, "content/tuning/enemies.json"))
    ap.add_argument("--uncapped-enemies", help="that build's enemies.json (its settings), for --check")
    ap.add_argument("--how", default="dt harness --as-app, 4 bots, enemies, 7 km nights")
    ap.add_argument("--at", default="")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--writes", help="also write the ArtifactData batch for the Tweaker's store: each creature's measured "
                    "a night per tier, and model/calibration")
    a = ap.parse_args()
    st = settings(load_jsonc(a.enemies))
    capped = {k for k, s in st.items() if s["perNight"] is not None or s["nightChance"] < 1}
    ratio = {}
    if a.uncapped:
        n, tot = counts(sorted(glob.glob(a.uncapped)))
        others = sum(v / st[k]["weight"] / tw(st[k], "frontier") for k, v in tot.items() if k in st and k not in capped | FIXED)
        for k in capped:
            ratio[k] = (tot.get(k, 0) / st[k]["weight"] / tw(st[k], "frontier")) / others if others else 0
    by = {}
    for f in sorted(glob.glob(a.nights)):
        if tier_of(f):
            by.setdefault(tier_of(f), []).append(f)
    model = {"how": a.how, "at": a.at, "slots": SLOTS, "tiers": {}}
    for t in TIERS:
        if t not in by:
            continue
        n, tot = counts(by[t])
        base, fixed = {}, {}
        for k, s in st.items():
            c = (tot.get(k, 0) + 0.5) / n
            if k in FIXED:
                fixed[k] = round(tot.get(k, 0) / n, 3)
            else:
                base[k] = c / s["weight"] / tw(s, t)
        others = sum(v for k, v in base.items() if k not in capped)
        for k in capped & set(base):
            if k in ratio:
                base[k] = ratio[k] * others
        base = {k: round(v, 4) for k, v in base.items()}
        relief = sum(tot.get(k, 0) / n * st[k]["cost"] for k in st)
        model["tiers"][t] = {"nights": n, "relief": round(relief, 2), "base": base, "fixed": fixed}
        if a.check:
            e = estimate(st, model["tiers"][t], t)
            print(f"== {t}: {n} nights, relief {relief:.1f}", file=sys.stderr)
            for k in sorted(st, key=lambda k: -tot.get(k, 0)):
                print(f"   {k:14s} measured {tot.get(k, 0) / n:5.2f}  estimate {e.get(k, 0):5.2f}", file=sys.stderr)
    if a.check and a.uncapped and a.uncapped_enemies and "frontier" in model["tiers"]:
        st0 = settings(load_jsonc(a.uncapped_enemies))
        n, tot = counts(sorted(glob.glob(a.uncapped)))
        e = estimate(st0, model["tiers"]["frontier"], "frontier")
        print(f"== frontier, the uncapped build's settings: {n} nights measured vs the estimate", file=sys.stderr)
        for k in sorted(st0, key=lambda k: -tot.get(k, 0)):
            print(f"   {k:14s} measured {tot.get(k, 0) / n:5.2f}  estimate {e.get(k, 0):5.2f}", file=sys.stderr)
        print(f"   {'all':14s} measured {sum(tot.values()) / n:5.2f}  estimate {sum(e.values()):5.2f}", file=sys.stderr)
    if a.writes:
        writes = []
        for k in sorted(st):
            m = {}
            for t, files in by.items():
                n, tot = counts(files)
                m[t] = {"perNight": round(tot.get(k, 0) / n, 3), "nights": n, "at": a.at}
            writes.append({"op": "update", "collection": "creatures", "doc_id": k, "data": {"measured": m}})
        writes.append({"op": "set", "collection": "model", "doc_id": "calibration", "data": model})
        json.dump(writes, open(a.writes, "w"), indent=1)
    json.dump(model, sys.stdout, indent=1)
    print()


if __name__ == "__main__":
    main()
