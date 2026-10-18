#!/usr/bin/env python3
"""The Creature Tweaker's values into the game (note 643): the director's `want` into content/tuning/enemies.json.

    python3 tools/tweaker/apply.py <export dir> [--enemies content/tuning/enemies.json] [--check] [--game-out writes.json]

<export dir> is the Tweaker's store as `ArtifactData` saves it with `out_dir`: `creatures/<name>.json` (one per creature,
the document's fields) and, if it was read too, `settings/night.json`. Each creature's `want` is written where the game
keeps it, every comment left as it is:
  - weight       director.weights.<name>      (×1 drops the entry)
  - cost         director.costs.<name>
  - perNight     director.perNight.<name>     (null drops it: no cap)
  - nightChance  director.nightChance.<name>  (1 drops it: every night)
  - tierWeights  <name>.tierWeights           (only where the creature has its own)
  - abandonAtDawn  wakers.staysAtDawn         (the names that don't)
  - settings/night want.nightFleesSeconds  wakers.nightFleesSeconds
  - settings/night want.crewScale, lengthScale  director.pressure.<key>     (the threat's scaling, notes 646, 651)
  - settings/night want.longTrainCars          director.orchestrator.longTrainCars  (two each, note 647; 0 is off)
  - settings/night want.perNightDefault        director.perNightDefault  (most a night for every kind without its own,
                                               note 654; null is no cap)
--check prints what would change and writes nothing (exit 1 if anything would). --game-out writes the ArtifactData batch
that sets each changed creature's `game` to its `want`, and the night's `game` and `rules` (the game's own fixed numbers the
page's crew × cars grid needs) from the tuning as written, for after the change merges.
"""
import argparse, glob, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from calibrate import OWN, load_jsonc, parse_jsonc  # noqa: E402

TIERS = ["local", "frontier", "deadLines", "deepTerritory"]
# settings/night: what the director sets (want/game), and the fixed numbers the page computes its grid with (rules).
NIGHT = {"nightFleesSeconds": ["wakers"], "crewScale": ["director", "pressure"], "lengthScale": ["director", "pressure"],
         "longTrainCars": ["director", "orchestrator"], "perNightDefault": ["director"]}
# Those a null sets (off); the rest a null leaves alone.
NULLABLE = {"perNightDefault"}
RULES = {"scaleCrew": ["director", "pressure"], "scaleCars": ["director", "pressure"], "scaleFloor": ["director", "pressure"],
         "engagedPerActive": ["director", "orchestrator"], "engagedPerCar": ["director", "orchestrator"],
         "perPlayer": ["director", "orchestrator"], "perPlayerLong": ["director", "orchestrator"],
         "maxConcurrentSmallCrew": ["director"], "maxConcurrentLargeCrew": ["director"]}


def at(enemies, path, key):
    o = enemies
    for k in path:
        o = o[k]
    return num(o.get(key))


def night_of(enemies):
    """The night's settings as the game has them, and its fixed rules."""
    return ({k: at(enemies, p, k) for k, p in NIGHT.items()}, {k: at(enemies, p, k) for k, p in RULES.items()})


def skip(s, i):
    """Past whitespace and // comments from i."""
    while i < len(s):
        if s[i].isspace():
            i += 1
        elif s.startswith("//", i):
            while i < len(s) and s[i] != "\n":
                i += 1
        else:
            return i
    return i


def value_end(s, i):
    """The end (exclusive) of the JSON value starting at i: a string, a scalar, or a balanced object or array."""
    if s[i] == '"':
        j = i + 1
        while s[j] != '"':
            j += 2 if s[j] == "\\" else 1
        return j + 1
    if s[i] in "{[":
        depth, j, ins = 0, i, False
        while j < len(s):
            c = s[j]
            if ins:
                if c == "\\":
                    j += 2; continue
                if c == '"':
                    ins = False
            elif c == '"':
                ins = True
            elif s.startswith("//", j):
                while s[j] != "\n":
                    j += 1
                continue
            elif c in "{[":
                depth += 1
            elif c in "}]":
                depth -= 1
                if depth == 0:
                    return j + 1
            j += 1
        raise ValueError("unbalanced value")
    j = i
    while j < len(s) and s[j] not in ",}]\n" and not s.startswith("//", j):
        j += 1
    return i + len(s[i:j].rstrip())


def find_key(s, key, start, end):
    """(value start, value end) of "key": at its own depth inside s[start:end] (start just inside an object), or None."""
    i, depth, ins = start, 0, False
    needle = f'"{key}"'
    while i < end:
        c = s[i]
        if ins:
            if c == "\\":
                i += 2; continue
            if c == '"':
                ins = False
            i += 1; continue
        if s.startswith("//", i):
            while i < end and s[i] != "\n":
                i += 1
            continue
        if c == '"':
            if depth == 0 and s.startswith(needle, i):
                j = skip(s, i + len(needle))
                if s[j] == ":":
                    v = skip(s, j + 1)
                    return v, value_end(s, v)
            ins = True
        elif c in "{[":
            depth += 1
        elif c in "}]":
            depth -= 1
        i += 1
    return None


def block(s, path):
    """(inside start, end) of the object at path (a list of keys) from the file's top level."""
    start, end = s.index("{") + 1, len(s)
    for k in path:
        found = find_key(s, k, start, end)
        if found is None:
            raise KeyError("/".join(path))
        start, end = found[0] + 1, found[1] - 1
    return start, end


def compact(v):
    if isinstance(v, dict):
        return "{}" if not v else "{ " + ", ".join(f'"{k}": {compact(x)}' for k, x in v.items()) + " }"
    if isinstance(v, list):
        return "[" + ", ".join(compact(x) for x in v) + "]"
    if isinstance(v, float) and v.is_integer():
        return str(int(v))
    return json.dumps(v)


def wrapped(v, first, cont=6, width=120):
    """An object over lines no wider than width: its first line starting first columns in, the rest at cont (the costs
    table's own shape)."""
    one = compact(v)
    if first + len(one) <= width or not isinstance(v, dict):
        return one
    lines, line, at = [], "{ ", first
    for k, x in v.items():
        piece = f'"{k}": {compact(x)}, '
        if at + len(line) + len(piece.rstrip()) > width and line.strip("{ "):
            lines.append(line.rstrip())
            line, at = " " * cont, 0
        line += piece
    lines.append(line.rstrip().rstrip(",") + " }")
    return "\n".join(lines)


def put(s, path, key, value, wrap_at=None):
    """s with path.key set to value (an existing key replaced in place; a missing one added first in its object)."""
    start, end = block(s, path)
    found = find_key(s, key, start, end)
    if found and isinstance(value, dict) and s[found[0]] == "{":
        # The same keys as now: each changed value replaced where it stands, the object's own layout kept.
        now = parse_jsonc(s[found[0]:found[1]])
        if now and set(now) == set(value):
            for k in reversed([k for k in value if now[k] != value[k]]):
                inner = find_key(s, k, found[0] + 1, found[1] - 1)
                s = s[:inner[0]] + compact(value[k]) + s[inner[1]:]
            return s
    text = wrapped(value, wrap_at) if wrap_at is not None else compact(value)
    if found:
        return s[:found[0]] + text + s[found[1]:]
    raise KeyError(f"{'/'.join(path)}/{key} isn't in the file: add it there first")


def load_export(d):
    creatures = {}
    for f in sorted(glob.glob(os.path.join(d, "creatures", "*.json"))):
        doc = json.load(open(f))
        creatures[os.path.basename(f)[:-5]] = doc.get("data", doc)
    night = None
    nf = os.path.join(d, "settings", "night.json")
    if os.path.exists(nf):
        night = json.load(open(nf))
        night = night.get("data", night)
    return creatures, night


def num(v):
    return int(v) if isinstance(v, float) and v.is_integer() else v


def plan(enemies, creatures, night):
    """What the want changes against the game: (path, key, new value, a line saying it)."""
    d, w = enemies["director"], enemies["wakers"]
    changes = []
    tables = {"weights": dict(d.get("weights", {})), "perNight": dict(d.get("perNight", {})),
              "nightChance": dict(d.get("nightChance", {})), "costs": dict(d["costs"])}
    stays = list(w.get("staysAtDawn", []))
    houses = dict(enemies.get("dwellings", {}).get("weights", {}))
    said = []
    for name, doc in sorted(creatures.items(), key=lambda kv: kv[1].get("order", 99)):
        want = doc.get("want") or {}
        own = OWN.get(name)
        if name not in d["costs"] and not own:
            said.append(f"  {name}: not a creature of the game (neither in director.costs nor one that comes its own way): cut, or not yet in; skipped")
            continue
        # Those come their own way (note 644): a house creature's how often is its share of the houses (dwellings.weights);
        # the Pickers have none, and neither has a cost.
        if own == "house" and "weight" in want and num(want["weight"]) != houses.get(name):
            said.append(f"  {name}: how often (dwellings.weights) {houses.get(name)} -> {num(want['weight'])}")
            houses[name] = num(want["weight"])
        for field, table, drop in (("weight", "weights", 1), ("perNight", "perNight", None), ("nightChance", "nightChance", 1)):
            if own and field == "weight":
                continue
            if field not in want:
                continue
            v = num(want[field])
            now = tables[table].get(name, drop)
            if v != now:
                if v == drop:
                    tables[table].pop(name, None)
                else:
                    tables[table][name] = v
                said.append(f"  {name}: {field} {now} -> {v}")
        if not own and "cost" in want and num(want["cost"]) != tables["costs"][name]:
            said.append(f"  {name}: cost {tables['costs'][name]} -> {num(want['cost'])}")
            tables["costs"][name] = num(want["cost"])
        # One that stays whatever (a house: `staysAlways` on its row) has no dawn of its own to set.
        if "abandonAtDawn" in want and not doc.get("staysAlways"):
            leaves = want["abandonAtDawn"] is not False
            if leaves and name in stays:
                stays.remove(name); said.append(f"  {name}: now leaves at dawn")
            elif not leaves and name not in stays:
                stays.append(name); said.append(f"  {name}: now stays at dawn")
        tw = want.get("tierWeights")
        if tw and "tierWeights" in enemies.get(name, {}):
            now = enemies[name]["tierWeights"]
            new = {t: num(tw.get(t, now.get(t))) for t in TIERS}
            if any(new[t] != now.get(t) for t in TIERS):
                changes.append(([name], "tierWeights", new))
                said.append(f"  {name}: tierWeights {compact(now)} -> {compact(new)}")
    for table in ("weights", "perNight", "nightChance"):
        if tables[table] != d.get(table, {}):
            changes.append((["director"], table, tables[table]))
    if tables["costs"] != d["costs"]:
        changes.append((["director"], "costs", tables["costs"]))
    if houses != enemies.get("dwellings", {}).get("weights", {}):
        changes.append((["dwellings"], "weights", houses))
    if stays != w.get("staysAtDawn", []):
        changes.append((["wakers"], "staysAtDawn", stays))
    for k, path in NIGHT.items():
        want = (night or {}).get("want") or {}
        if k not in want or want[k] is None and k not in NULLABLE:
            continue
        v = want[k]
        v, now = num(v), at(enemies, path, k)
        if v != now:
            changes.append((path, k, v))
            said.append(f"  the night: {k} {now} -> {v}")
    return changes, said


def self_test():
    """A round trip on a copy of the game's tuning: an export with the game's values changes nothing; one with changes
    writes them, every comment kept, and reads back as the export said."""
    import copy, shutil, tempfile
    src = os.path.join(ROOT, "content/tuning/enemies.json")
    enemies = load_jsonc(src)
    d, w = enemies["director"], enemies["wakers"]
    with tempfile.TemporaryDirectory() as tmp:
        dst = os.path.join(tmp, "enemies.json")
        shutil.copy(src, dst)
        os.makedirs(os.path.join(tmp, "export", "creatures"))
        def doc(k):
            want = {"weight": d.get("weights", {}).get(k, 1), "cost": d["costs"][k], "perNight": d.get("perNight", {}).get(k),
                    "nightChance": d.get("nightChance", {}).get(k, 1), "abandonAtDawn": k not in w["staysAtDawn"]}
            if "tierWeights" in enemies.get(k, {}):
                want["tierWeights"] = enemies[k]["tierWeights"]
            return {"name": k, "want": want, "game": copy.deepcopy(want)}
        docs = {k: doc(k) for k in d["costs"]}
        for k, where in OWN.items():
            want = {"weight": enemies["dwellings"]["weights"].get(k, 1) if where == "house" else 1, "perNight": d.get("perNight", {}).get(k),
                    "nightChance": d.get("nightChance", {}).get(k, 1), "abandonAtDawn": k not in w["staysAtDawn"]}
            docs[k] = {"name": k, "want": want, "game": copy.deepcopy(want)}
        changes, _ = plan(enemies, docs, None)
        assert not changes, f"the game's own values change something: {changes}"
        house = {"want": {"abandonAtDawn": False}, "staysAlways": "a house: it stays", "ownWay": "house"}
        assert not plan(enemies, {"hollowHouse": house}, None)[0], "a house's row put it in staysAtDawn"
        docs["knotter"]["want"].update(perNight=1, nightChance=0.5, abandonAtDawn=True, tierWeights={"local": 0.5, "frontier": 1, "deadLines": 1.5, "deepTerritory": 2})
        docs["climbers"]["want"].update(weight=1.5, cost=4, abandonAtDawn=False)
        docs["passenger"]["want"].update(perNight=1)
        docs["lodger"]["want"].update(weight=1.5, perNight=2)
        docs["pickers"]["want"].update(nightChance=0.5)
        for k, v in docs.items():
            json.dump(v, open(os.path.join(tmp, "export", "creatures", k + ".json"), "w"))
        os.makedirs(os.path.join(tmp, "export", "settings"))
        game0, _ = night_of(enemies)
        assert not plan(enemies, {}, {"want": game0})[0], "the game's own night changes something"
        json.dump({"want": {"nightFleesSeconds": 45, "crewScale": 0.2, "longTrainCars": 0, "perNightDefault": 3}, "game": {"nightFleesSeconds": w["nightFleesSeconds"]}},
                  open(os.path.join(tmp, "export", "settings", "night.json"), "w"))
        before = open(dst).read()
        sys.argv = ["apply.py", os.path.join(tmp, "export"), "--enemies", dst, "--game-out", os.path.join(tmp, "writes.json")]
        main()
        after = open(dst).read()
        e2 = load_jsonc(dst)
        assert e2["director"]["perNight"] == {"knotter": 1, "passenger": 1, "lodger": 2}, e2["director"]["perNight"]
        assert e2["director"]["nightChance"] == {**d.get("nightChance", {}), "knotter": 0.5, "pickers": 0.5}, e2["director"]["nightChance"]
        assert e2["dwellings"]["weights"]["lodger"] == 1.5 and "lodger" not in e2["director"]["costs"]
        assert e2["director"]["weights"] == {"climbers": 1.5}, e2["director"]["weights"]
        assert e2["director"]["costs"]["climbers"] == 4 and e2["director"]["costs"]["knotter"] == d["costs"]["knotter"]
        assert set(e2["wakers"]["staysAtDawn"]) == {"hotbox", "climbers"}, e2["wakers"]["staysAtDawn"]
        assert e2["wakers"]["nightFleesSeconds"] == 45
        assert e2["director"]["pressure"]["crewScale"] == 0.2 and e2["director"]["orchestrator"]["longTrainCars"] == 0
        assert e2["director"]["pressure"]["lengthScale"] == d["pressure"]["lengthScale"]
        assert e2["director"]["perNightDefault"] == 3
        assert e2["knotter"]["tierWeights"] == {"local": 0.5, "frontier": 1, "deadLines": 1.5, "deepTerritory": 2}
        assert before.count("//") == after.count("//"), "a comment was lost"
        assert max(len(l) for l in after.splitlines()) <= 120 or max(len(l) for l in before.splitlines()) > 120, "a line past 120"
        writes = json.load(open(os.path.join(tmp, "writes.json")))
        assert {x["doc_id"] for x in writes} == {"knotter", "climbers", "passenger", "lodger", "pickers", "night"}, writes
        nw = next(x for x in writes if x["doc_id"] == "night")["data"]
        assert nw["game"]["crewScale"] == 0.2 and nw["game"]["lengthScale"] == d["pressure"]["lengthScale"], nw
        assert nw["want"]["lengthScale"] == d["pressure"]["lengthScale"] and nw["rules"]["perPlayerLong"] == 2, nw
        sys.argv = ["apply.py", os.path.join(tmp, "export"), "--enemies", dst, "--check"]
        try:
            main()
        except SystemExit as e:
            assert not e.code, "applied, it still differs"
    page_grid(enemies)
    page_estimate(enemies)
    print("apply.py self-test: ok")


def page_estimate(enemies):
    """The page's estimate against calibrate.py's, every kind with a night's chance (the sampled nights, note 657) and with
    none: the same numbers both ways, run with node where there is one."""
    import re, shutil, subprocess
    import calibrate as C
    node = shutil.which("node")
    if not node:
        print("  (no node: the page's estimate not run)")
        return
    page = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "creature-tweaker.html")).read()
    code = page[page.index("  const tw = "):page.index("  // Each creature's settings")]
    st = C.settings(enemies)
    kinds = [k for k in st if not st[k].get("own") and k not in C.FIXED]
    cal = {"relief": 30.0, "base": {k: 0.2 + (i % 7) / 5 for i, k in enumerate(sorted(kinds))}, "fixed": {"stoker": 0.5},
           "own": {k: 1.0 for k in C.OWN}, "ownBase": {k: 0.7 for k in C.OWN}}
    # Every kind on a chance (sampled, as under the director's roster), and every kind about (counted).
    for label, chance in (("the sampled nights", lambda v: v["nightChance"] if v["nightChance"] < 1 else 0.5),
                          ("every kind about", lambda v: 1)):
        s2 = {k: dict(v, nightChance=chance(v)) for k, v in st.items()}
        want = C.estimate(s2, cal, "frontier")
        js = f"const model = {{ own: {json.dumps(C.OWN)} }};\n{code}\nconsole.log(JSON.stringify(estimate({json.dumps(s2)}, 'frontier')));"
        js = js.replace("const cal = model?.tiers?.[t];", f"const cal = {json.dumps(cal)};")
        got = json.loads(subprocess.run([node, "-e", js], capture_output=True, text=True, check=True).stdout)
        bad = {k: (got.get(k), v) for k, v in want.items() if abs(got.get(k, 0) - v) > 1e-6}
        assert not bad, f"{label}: the page's estimate isn't calibrate.py's: {bad}"



def page_grid(enemies):
    """The page's crew × cars grid against Director.cs's own reading (PressureTuning.Scale, EngagedCap, MaxConcurrent),
    and its fallback rules against the game's: run with node where there is one."""
    import math, re, shutil, subprocess
    page = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "creature-tweaker.html")).read()
    game, rules = night_of(enemies)
    fallback = json.loads(re.sub(r"(\w+):", r'"\1":', re.search(r"const RULES = (\{[^}]*\});", page).group(1)))
    assert fallback == rules, f"the page's fallback rules aren't the game's: {fallback} vs {rules}"
    node = shutil.which("node")
    if not node:
        print("  (no node: the page's grid not run)")
        return
    fn = page[page.index("  function scaleAt("):]
    fn = fn[:fn.index("\n  }\n") + 4]
    cases = [(c, n) for c in (1, 2, 3, 4, 6, 8) for n in (4, 6, 8, 10, 12, 13, 15, 18)]
    js = f"const RULES = {{}}, night = {{ rules: {json.dumps(rules)} }};\n{fn}\nconsole.log(JSON.stringify({json.dumps(cases)}.map(([c, n]) => scaleAt({json.dumps(game)}, c, n))));"
    got = json.loads(subprocess.run([node, "-e", js], capture_output=True, text=True, check=True).stdout)
    r, g = rules, game
    for (crew, cars), x in zip(cases, got):
        mult = max(r["scaleFloor"], 1 + g["crewScale"] * (crew - r["scaleCrew"])) * max(r["scaleFloor"], 1 + g["lengthScale"] * (cars - r["scaleCars"]))
        long = g["longTrainCars"] > 0 and cars >= g["longTrainCars"]
        each = r["perPlayerLong"] if long else r["perPlayer"]
        share = r["perPlayerLong"] if long else min(r["perPlayer"], r["engagedPerActive"] + r["engagedPerCar"] * max(0, cars - r["scaleCars"]))
        hard = round((r["maxConcurrentLargeCrew"] if crew >= 6 else r["maxConcurrentSmallCrew"]) * each / r["perPlayer"])
        once = min(hard, max(1, min(math.ceil(crew * share - 1e-9), math.floor(crew * each + 1e-9))))
        assert abs(x["mult"] - mult) < 1e-9 and x["atOnce"] == once, f"crew {crew}, {cars} cars: page {x}, game {mult}, {once}"
    # ThreatScaleTests' own: a crew of four on a long train, eight at once (two each); 1 at four crew on eight cars.
    at = {(c, n): x for (c, n), x in zip(cases, got)}
    if 0 < g["longTrainCars"] <= 18:
        assert (at[(4, 18)]["atOnce"], at[(4, 18)]["each"]) == (8, 2), at
    assert abs(at[(4, 8)]["mult"] - 1) < 1e-9


def main():
    if "--self-test" in sys.argv:
        return self_test()
    ap = argparse.ArgumentParser()
    ap.add_argument("export")
    ap.add_argument("--enemies", default=os.path.join(ROOT, "content/tuning/enemies.json"))
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--game-out")
    a = ap.parse_args()
    creatures, night = load_export(a.export)
    enemies = load_jsonc(a.enemies)
    changes, said = plan(enemies, creatures, night)
    print("\n".join(said) if said else "  nothing to change: the game has the Tweaker's values")
    if a.check:
        sys.exit(1 if changes else 0)
    if changes:
        s = open(a.enemies).read()
        for path, key, value in changes:
            # The director's tables wrap at 120 like the costs table (a value starts after '    "<key>": ').
            s = put(s, path, key, value, wrap_at=len(f'    "{key}": ') if path == ["director"] and isinstance(value, dict) else None)
        open(a.enemies, "w").write(s)
        # Read back what was written: it must still parse and hold every value.
        back, _ = plan(load_jsonc(a.enemies), creatures, night)
        if back:
            sys.exit(f"written, but reading back still differs: {back}")
    if a.game_out:
        writes = []
        for name, doc in creatures.items():
            if doc.get("want") and doc.get("want") != doc.get("game"):
                writes.append({"op": "update", "collection": "creatures", "doc_id": name, "data": {"game": doc["want"]}})
        if night is not None:
            game, rules = night_of(load_jsonc(a.enemies))
            # A setting the page hasn't had yet starts at the game's; the director's own wants are kept.
            want = {**game, **(night.get("want") or {})}
            data = {k: v for k, v in (("game", game), ("rules", rules), ("want", want)) if night.get(k) != v}
            if data:
                writes.append({"op": "update", "collection": "settings", "doc_id": "night", "data": data})
        json.dump(writes, open(a.game_out, "w"), indent=1)
        print(f"  {len(writes)} store writes for after it merges -> {a.game_out}")


if __name__ == "__main__":
    main()
