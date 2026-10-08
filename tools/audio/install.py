#!/usr/bin/env python3
"""Put the chosen sounds into the game: Opus takes under content/audio/samples, a sound definition per cue.

Which candidate a cue gets, from the checklist store (`--from DIR`, the `items` as an ArtifactData list saves them):
  1. what the director kept (Keep) on that cue, all of them as takes;
  2. otherwise the first candidate not marked Redo: built candidates first, then library files, so every cue has its own
     sound, rough or not (L1). A tell plays its picked takes under its own game name (tell_sounds), its tuned synth
     sound only where nothing's picked.

  python3 tools/audio/install.py --from DIR [--dry] [line ...]

For each cue it writes content/audio/samples/<line>/<cue>[/<surface>]/NN.opus (mono 48 kHz Opus; the engine picks a take
at random per instance) and content/audio/sounds/<line>.<cue>[.<surface>].json (one sample layer; tier, range and
level from the line's area). content/audio/samples/index.json records where every folder came from and its licence.
The engine side is Ballast.Audio's sample layer (`"source": "sample"`).
"""

import hashlib
import json
import os
import shutil
import subprocess
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cues as C  # noqa: E402
import dsp  # noqa: E402
import src  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "out", "audio")
SAMPLES = os.path.join(ROOT, "content", "audio", "samples")
SOUNDS = os.path.join(ROOT, "content", "audio", "sounds")

# Area -> (tier, minDistance, maxDistance, rolloff, gainDb) for a cue whose line has no tier of its own. Spec A.3's tiers:
# 1 enemy telegraphs, 2 proximity voice, 3 critical train state, 4 player actions, 5 train bed, 6 ambient world.
AREA = {
    "Enemy tells": (1, 4, 250, 0.8, 8),
    "Creature sounds": (3, 2, 80, 1.0, 6),
    "Train bed": (5, 4, 200, 0.8, 4),
    "Train state & alarms": (3, 4, 300, 0.8, 6),
    "Crew actions & foley": (4, 1, 40, 1.0, 2),
    "Voice & comms": (4, 1, 30, 1.0, 2),
    "Music & UI": (4, 1, 30, 1.0, 0),
    "World & hazards": (6, 10, 600, 0.6, 2),
    "Facilities & places": (6, 8, 300, 0.7, 2),
}
OPUS_KBPS = 64
# Per-cue level on top of the area's, where a sound measured too hot in the game's own benches: the Stoker moving in
# the fire buried the cab's tells with the fire door open (AudioTests' bench: a writhe at -2.4 dB, the Choir at -5.2).
# The train bed and its alarms (GameAudio.Train), levelled to the synthesised bed they replace: as recorded they buried
# every tell in AudioTests' chaos and tells benches (the wheels and the wind 20 dB over the synth, a hound at -13 dB).
# Then too far down (build 1121, the director: "the train doesn't appear to be making any noise while it's on the rail"):
# the rolling, the joints and the exhaust back up 4-6 dB (note 265), the tells still 6 dB over the bed in AudioTests.
CUE_GAIN_DB = {
    "cs-stoker.in-fire": -10,
    "bed-wheel-rail.roll-slow": -17,
    "bed-wheel-rail.roll-fast": -17,
    "bed-wheel-rail.joint": -16,
    "bed-wheel-rail.flange": -2,
    "bed-wind.wind-slow": -10,
    "bed-wind.wind-fast": -16,
    "bed-wind.gust": -10,
    "bed-boiler-roar.roar-low": -12,
    "bed-boiler-roar.roar-high": -12,
    "bed-chuff.chuff": -10,
    "bed-chuff.chuff-heavy": -10,
    "bed-chuff.rod-clank": -18,
    "bed-brake.drag": -12,
    "bed-brake.drag-hot": -9,
    "bed-brake.apply": -10,
    "bed-brake.release": -8,
    "bed-vent.blow": -11,
    "bed-vent.open": -8,
    "bed-vent.close": -4,
    "bed-slack.run-in": -16,
    "bed-slack.run-out": -14,
    "bed-groan.groan": -10,
    "bed-groan.creak": -8,
    "state-valve.blow": -9,
    "state-valve.lift": -6,
    "state-valve.reseat": -4,
    "state-strain.groan": -11,
    "state-strain.tick": -6,
    "state-strain.rivet": -4,
    "state-rupture.burst": -4,
    "state-rupture.debris": -6,
    "state-rupture.steam-out": -8,
    "state-brake-fade.fade": -10,
    "state-engine-damage.leak-small": -12,
    "state-engine-damage.leak-large": -8,
    "state-engine-damage.knock": -6,
}
# Earlier-pass keepers that string several of one event together (a gait, hops coming closer): cut into single takes.
# The rest are one designed event each, pauses and all (a giggle, a swallow, the Whistler's wrong whistle).
# Value: the gap (s) and level (dB under the loudest) that separate one event from the next in that file.
SEQUENCES = {"audio/cs-ribbits--hops.mp3": (0.06, -26), "audio/cs-passenger--boots.mp3": (0.12, -38),
             "audio/cs-climbers--roof.mp3": (0.08, -30)}


def encode(x, path):
    """A take as Ogg Opus. Byte-identical for identical audio, so rebuilds don't churn the repo."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    raw = np.clip(x, -1, 1).astype(np.float32).tobytes()
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(dsp.SR), "-ac", "1", "-i", "-",
                    "-c:a", "libopus", "-b:a", f"{OPUS_KBPS}k", "-application", "audio", "-map_metadata", "-1",
                    "-fflags", "+bitexact", "-flags:a", "+bitexact", path], input=raw, check=True)


def split_events(x, n, gap=0.12, db=-38):
    """An earlier-pass file that strings several events together (hops coming closer) cut back into single takes."""
    hop = dsp.samples(0.01)
    rms = np.array([np.sqrt(np.mean(x[i:i + hop] ** 2)) for i in range(0, len(x) - hop, hop)])
    on = rms > np.max(rms) * dsp.db2a(db)
    events, start, quiet = [], None, 0
    for i, v in enumerate(on):
        if v:
            if start is None:
                start = i
            quiet = 0
        elif start is not None:
            quiet += 1
            if quiet * 0.01 >= gap:
                events.append((start, i - quiet + 1))
                start, quiet = None, 0
    if start is not None:
        events.append((start, len(on)))
    takes = [dsp.fade(x[a * hop:min(len(x), (b + 3) * hop)], 0.002, 0.02) for a, b in events if (b - a) * 0.01 > 0.04]
    return takes if len(takes) >= 2 else [x]


def takes_of(cand, cue):
    """The audio of a candidate as a list of takes."""
    if cand.get("built"):
        d = os.path.join(OUT, "takes", cand["line"], cand["cue"])
        stem = cand["key"] + (f"_{cand['mat']}" if cand.get("mat") else "")
        files = sorted(f for f in os.listdir(d) if f.startswith(stem + "_") and f[len(stem) + 1:-4].isdigit())
        return [dsp.load(os.path.join(d, f)) for f in files]
    if cand.get("old"):
        x = dsp.load(os.path.join(OUT, "earlier", cand["src"]))
        if cue["kind"] == "loop":
            return [dsp.loop_seam(x, 0.3)]
        if cand["src"] in SEQUENCES:
            return [dsp.level(t, -20) for t in split_events(x, cue["vars"], *SEQUENCES[cand["src"]])]
        return [dsp.level(x, -20)]
    x = src.get(cand["libkey"])
    return [dsp.level(dsp.fade(dsp.trim_silence(x, -60), 0.002, 0.02), -20)]


def libkey(srcpath):
    """'library/pack__name.mp3' (the page's copy) -> 'pack:name' (the source file)."""
    base = os.path.basename(srcpath)[:-4]
    pack, name = base.split("__", 1)
    return f"{pack}:{name[len('sfx100v2_'):] if pack == 'sfx_100_v2' else name}"


def candidates(line, cue, stored):
    """The stored cue's candidates in pick order, each tagged with where its audio is."""
    import build
    man = build.manifest()
    by_url = {}
    import assets
    for e in man.values():
        u = assets.url_for(e["preview"])
        if u:
            by_url[u] = e
    out = []
    for k in stored.get("cands") or []:
        k = dict(k)
        if k["src"] in by_url:
            e = by_url[k["src"]]
            k.update(built=True, line=e["line"], cue=e["cue"], key=e["key"], mat=e["mat"])
        elif k.get("old") or k["src"].startswith("audio/"):
            k["old"] = True
        elif k["src"].startswith("library/"):
            k["libkey"] = libkey(k["src"])
        else:
            continue
        out.append(k)
    return out


# Which candidate plays while nothing's kept, where the director's brief already says which (rather than the first): the
# tunnel's hit is to be a bonk, not a thock (3 Oct), and the clean bonk is the one that says so. The bend's stress is to be
# heard building (build 1121, note 265): the squeal that holds and the shriek that climbs were made for that, where the
# first squeal dropped out most of its loop (`dt audio render --scenario bend`: -42 dB in the cab at the bend's board).
# Gameplay foley is the real thing where there's a choice: the wind-up drummer from real tin over the modelled one; the
# lamp guttering from its flame over cloth whooshes.
FIRST_CHOICE = {"place-town.fire": "drum", "place-town.murmur": "masks",  # note 415: the square's barrels; the folk about
                "crew-mishaps.tunnel-bonk": "coconut", "bed-wheel-rail.flange": "sing", "state-derail.flange-scream": "shriek",
                "crew-noisy-toys.drummer": "tin", "ui-stranded-outro.lamp-out": "gutter",
                # Note 322: the chuff already beats, so the starved engine's struggle under it is the beatless one.
                "state-starved.labour": "drag",
                # Note 384: the croak's voice is a tenth of a second (the roar it's cut from has no more); the squawk's is a cry.
                "cs-gannet-strike.hit": "squawk",
                # Note 385: a real plate's knock first (the casting's is the synth's tone again); the engine house's beat over
                # the headframe's rope, whose tones can read as a whine.
                "state-coupling-loose.knock": "clank", "place-mine-lift.winding": "engine"}


def pick(cands, mat, line_level, cue_name=None):
    here = [k for k in cands if k.get("mat") in (mat, None)]
    kept = [k for k in here if k.get("verdict") == "keep"]
    if kept:
        return kept, "kept"
    # Nothing kept yet: the first candidate not marked Redo, on every line, so every cue the game's hooks name has a sound
    # of its own (a tell's takes go under its game name: tell_sounds).
    ok = [k for k in here if k.get("verdict") != "redo"]
    # (A cue with no first choice leaves the order alone: a library candidate has no key, and None isn't a choice.)
    want = FIRST_CHOICE.get(cue_name)
    ok.sort(key=lambda k: (want is not None and k.get("key") != want, not k.get("built"), not k.get("old"), k.get("mat") is None))
    return ok[:1], "first"


# Lines whose sounds aren't the game's (the trailer's cut goes to the store tools, not content/).
NOT_IN_GAME = {"store-trailer"}
# A tell the game already plays under a name of its own (GameAudio.Enemies, Choir): that sound becomes its picked takes
# (kept, or the first candidate while it's under review), keeping its tuned tier, range and level (spec A.4's audit
# numbers live there). The synth definition it replaces is saved in tools/audio/synth-defs/ and comes back if nothing's
# picked any more.
# Line -> (sound, [(cue, layer extras)]): extras such as a gain curve on the param the game drives.
TELL_SOUNDS = {
    "tell-choir": ("choir-voice", [("voices", {"rate": {"param": "pitch", "points": [[0.75, 0.985], [1.25, 1.015]]}})]),
    "tell-car-fire": ("car-fire", [("smoulder", {"gain": {"param": "progress", "points": [[0, 1], [0.5, 0.7], [1, 0]]}}),
                                   ("alight", {"gain": {"param": "progress", "points": [[0, 0], [0.4, 0.25], [1, 1]]}})]),
    # The upkeep's faults (queue #95, note 358), for D1's synths (notes 331, 346), their params kept: the hot box's squeal
    # rising in pitch and level as it heats and its smoke from halfway; the lamp's sputter and flame worse as it gutters.
    "state-hotbox": ("hotbox", [("squeal", {"rate": {"param": "heat", "points": [[0, 0.94], [1, 1.08]]},
                                            "gain": {"param": "heat", "points": [[0, 0.45], [0.5, 0.75], [1, 1]]}}),
                                ("smoke", {"gain": {"param": "heat", "points": [[0, 0], [0.45, 0], [1, 1]]}})]),
    "state-gutter": ("lamp-gutter", [("sputter", {"gain": {"param": "gutter", "points": [[0, 0.35], [1, 1]]}}),
                                     ("flutter", {"gain": {"param": "gutter", "points": [[0, 0.5], [1, 1]]}})]),
    # T118: the game plays the giggle now and then, each at its own "pitch" (0.92-1.10), so the take follows it.
    # A presence lift on the porcelain's ring: the kept giggle has less in its 3-6 kHz tell band than T118's synth, and the
    # cab's din buries it there (AudioTests); lifting the band, not the whole giggle, keeps it from being loud and crazy (T115).
    "tell-track-doll": ("doll-giggle", [("giggle", {"rate": {"param": "pitch", "points": [[0, 0], [2, 2]]},
                                                    "filters": [{"type": "peak", "frequency": 4200, "q": 0.9, "gainDb": 11}]})]),
    "tell-car-hugger": ("hugger-grind", [("grind", {})]),
    "tell-ribbits": ("ribbit-swell", [("swell", {})]),
    "tell-hounds": ("hound-howl", [("howl-far", {})]),
    "tell-draggers": ("dragger-scrape", [("rasp", {})]),
    "tell-stoker": ("stoker-hiss", [("hiss-wrong", {})]),
    "tell-fireflies": ("fireflies-buzz", [("buzz", {})]),
    "tell-grumbler": ("grumbler-gnaw", [("gnaw", {})]),
    "tell-marsh": ("drift-rustle", [("reeds", {})]),
    "tell-track-debris": ("sleepers-writhe", [("writhe", {})]),
    "tell-climbers": ("climber-scrabble", [("scrabble", {})]),
    "tell-tippy": ("tippy-tiptoe", [("tiptoe", {})]),
    "tell-soot-children": ("child-call", [("call", {})]),
    # Spec A.4: the Whistler's tell is the train's own whistle with nobody on the cord, so its kept whistle is the train's.
    "tell-whistler": ("train-whistle", [("whistle", {})]),
}
# Played once rather than looped: the Dragger's rasp is one scrape before the grab (spec A.4), held till the phase ends; a
# whistle take is a whole blast, start to release, which rings out to its end (GameAudio.Whistle).
ONCE = {"dragger-scrape", "train-whistle"}
# Tells the game holds like a loop whose kept takes are single bursts or steps: GameAudio.Repeat fires them again at an
# uneven pace while the tell lasts.
PACED = {"climber-scrabble", "tippy-tiptoe", "child-call"}
# Level on top of the synth definition's, where the kept takes sit lower than the synth did: AudioTests holds every
# tell 6 dB over the bed for whoever has to hear it (the Choir's kept voices measured -2.5 at a car roof at the old level;
# the kept whistle 4.6 at the middle car).
TELL_GAIN_DB = {"choir-voice": 11, "train-whistle": 3}
SYNTH_DEFS = os.path.join(HERE, "synth-defs")
# Sounds main put in as synth definitions outside the tells (the line's warnings, notes 260 and 265; the gun's laying, the
# cannon's landings, the toys, the depot's blast, the stranded ending): each cue's picked takes replace its sound the same
# way, keeping its tier and range. line.cue -> (sound, layer extras): the extras put the param the game drives on the
# takes (the turn's speed on the gun's motor, the boiler's cooling on its ticks).
SWAPS = {
    "warn-overspeed.bell": ("warn-overspeed", {}),
    "warn-curve.chatter": ("warn-curve", {}),
    "warn-low-clearance.telltales": ("warn-low-clearance", {}),
    # The gun's laying: the director heard "a weird high repeated sound" when the cannon turns (GDD App. F.3, note 329), and
    # the first candidates were that too, so it's HELD (below) on main's low, slow synth until a candidate is kept.
    "crew-gun-lay.lay": ("gun-lay", {"rate": {"param": "speed", "points": [[0, 0.8], [1, 1.2]]},
                                     "gain": {"param": "speed", "points": [[0, 0.45], [1, 1]]}}),
    "crew-cannon-impact.ground": ("cannon-impact", {}),
    "crew-cannon-impact.water": ("cannon-splash", {}),
    "crew-cannon-impact.doll": ("doll-shatter", {}),
    "crew-noisy-toys.squeaker": ("toy-squeaker", {}),
    "crew-noisy-toys.music-box": ("toy-musicbox", {}),
    "crew-noisy-toys.drummer": ("toy-drummer", {}),
    "place-depot.powder-blast": ("powder-blast", {}),
    "ui-stranded-outro.boiler-tick": ("boiler-tick", {"rate": {"param": "cool", "points": [[0, 1], [1, 0.8]]},
                                                      "gain": {"param": "cool", "points": [[0, 1], [1, 0.6]]}}),
    "ui-stranded-outro.lamp-out": ("lamp-out", {}),
}
# Level on top of each swapped synth definition's, so the takes sit where the synth did (main tuned the mix against
# it): the synth's loudness less the takes', both through `dt audio render sound:<name>`. The toys instead by note 174's
# test (AudioTests' toys bench: carried on a roof, each 6 dB over the wind and the three within 4 dB): a squeaker that
# squeaks now and then and a music box's decaying plucks measure quieter than their loudness says.
# Cues held out of the game until the director keeps one of their candidates (no first-candidate install): the game's
# sound stays what main has meanwhile. The gun's laying (note 333): its first candidates were the high repeated sound the
# director heard (note 329), so the low, slow ones wait for a Keep, on main's low, slow synth.
HELD = {"crew-gun-lay.lay"}

SWAP_GAIN_DB = {"boiler-tick": -4, "cannon-impact": -1, "cannon-splash": -4, "doll-shatter": 0, "hotbox": 4, "lamp-gutter": -16, "lamp-out": -14,
                "powder-blast": -3, "toy-drummer": -10, "toy-musicbox": 11, "toy-squeaker": 5, "warn-curve": -6,
                "warn-low-clearance": 2, "warn-overspeed": 5}

# Lines whose candidates are alternatives the game uses all of, one per instance (a prisoner's whole voice).
SETS_LINES = {"voice-prisoner-sets"}
# Folders a full install leaves alone: the clerk's word bank (clerk.py), which the checklist doesn't pick.
KEEP_SAMPLES = {"voice-clerk"}
# Voices read by Piper's LibriTTS model (recipes/voices.py, children.py; clerk.py): not CC0, they carry its credit.
TTS_CUES = {("voice-prisoner-sets", "call"), ("voice-prisoner-sets", "shout"), ("tell-soot-children", "call")}
TTS_LICENCE = "CC BY 4.0: LibriTTS, Zen et al. 2019"
# A prisoner calling from a Holdout (D.7): heard to 60 m with normal falloff and occlusion, on the voice tier.
VOICE_LINES = {"voice-prisoner-sets", "voice-callout"}


# Cue -> layer extras: a curve on a param the game drives (GameAudio sets it on the playing instance).
# ui-prompts.hold: the hold-to-interact loop hurries as the held action gets there (GameAudio.Ui.cs sets "progress", 0-1).
LAYER_EXTRAS = {
    "ui-prompts.hold": {"rate": {"param": "progress", "points": [[0, 1], [1, 1.4]]}},
    # A derelict's flat wheels thump once a turn, built at 1.2 s a turn (a 0.9 m wheel at 2.4 m/s): faster or slower with
    # the car (GameAudio.BedWheels sets "speed"; note 322).
    "place-derelict.roll": {"rate": {"param": "speed", "points": [[0.6, 0.6], [2.4, 1.0], [4.0, 1.35]]}},
    # The bed's air and wheels kept under the tells' bands, as the synths they replace were (wind.json lowpassed at 650 Hz,
    # wheel-rail.json under 420 Hz): recorded, their hiss over 2 kHz buried the Climbers' scrabble (AudioTests' chaos bench).
    "bed-wind.wind-slow": {"filters": [{"type": "lowPass", "frequency": 1500}]},
    "bed-wind.wind-fast": {"filters": [{"type": "lowPass", "frequency": 1500}]},
    "bed-wheel-rail.roll-slow": {"filters": [{"type": "lowPass", "frequency": 2500}]},
    "bed-wheel-rail.roll-fast": {"filters": [{"type": "lowPass", "frequency": 2500}]},
    # The safety valve's blow kept over the writhe's band, as the synth's was (safety-valve.json: highpassed at 2.8 kHz): its
    # roar under 2 kHz buried the Sleepers for the cab.
    "state-valve.blow": {"filters": [{"type": "highPass", "frequency": 2000}]},
    # The scream climbs as the bend pulls harder (note 265: the stress telegraph; GameAudio.BedWheels sets "stress", the
    # scream's share of the stress's last half): two semitones from where it starts to where the train comes off.
    "state-derail.flange-scream": {"rate": {"param": "stress", "points": [[0, 0.94], [1, 1.06]]}},
}


# Per-cue fields over the area's where one cue isn't like its line: the whistle's wheeze is still the train's whistle (tier 1,
# heard as far, as the Whistler's tell is: spec A.4); the livestock are the world's, out along the train; the casting's bong
# carries across a yard.
CUE_DEF = {
    "crew-mishaps.whistle-wheeze": {"tier": 1, "minDistance": 20, "maxDistance": 1500, "rolloff": 0.45, "gainDb": 2},
    "crew-mishaps.startle-cattle": {"tier": 6, "minDistance": 6, "maxDistance": 200, "rolloff": 0.8},
    "crew-mishaps.startle-pigs": {"tier": 6, "minDistance": 6, "maxDistance": 200, "rolloff": 0.8},
    "crew-mishaps.startle-sheep": {"tier": 6, "minDistance": 6, "maxDistance": 200, "rolloff": 0.8},
    "crew-mishaps.crushed": {"maxDistance": 120, "rolloff": 0.8},
    # A loose coupling's knock is heard where D1's synth was (note 356): from the gap, its ladders and the ground beside.
    "state-coupling-loose.knock": {"minDistance": 3, "maxDistance": 60},
    # The walled town (note 415): a fire barrel warms a few metres of the square, a range and a clock a room; the townsfolk
    # are heard across a street or two.
    "place-town.fire": {"minDistance": 1.5, "maxDistance": 30, "rolloff": 1.0},
    "place-town.range": {"minDistance": 1, "maxDistance": 14, "rolloff": 1.0},
    "place-town.clock": {"minDistance": 1, "maxDistance": 10, "rolloff": 1.0},
    "place-town.radio": {"minDistance": 1, "maxDistance": 14, "rolloff": 1.0},
    "place-town.murmur": {"minDistance": 3, "maxDistance": 35, "rolloff": 1.0},
    "place-town.cough": {"minDistance": 1.5, "maxDistance": 35, "rolloff": 1.0},
}


# Spec A.4 rule 4, a tell "non-repeating at short intervals" (note 250): every looping tell wanders a little in pitch and
# level (Ballast.Audio DriftDef), so no two passes round its loop are the same.
TELL_DRIFT = {"semitones": 0.4, "db": 1.5, "seconds": 2.5}


def sound_def(item, cue, folder, line):
    tier, lo, hi, roll, g = AREA.get(item.get("area"), (4, 1, 40, 1.0, 2))
    if isinstance(item.get("tier"), int):
        tier = item["tier"]
    flat = line.startswith("ui-") or line == "voice-radio-sfx"   # the radio's clicks are the set in your own hand
    if line == "ui-music":
        tier = 7            # music's own bottom tier, under the ambient world (decided 1 Oct)
    elif flat:
        tier = 4
    if line in VOICE_LINES:
        tier, lo, hi, roll = 2, 2, 60, 1.0
    d = {"tier": tier, "loop": cue["kind"] == "loop", "maxInstances": 8 if cue["kind"] == "loop" else 12,
         "minDistance": lo, "maxDistance": hi, "rolloff": roll, "gainDb": g + CUE_GAIN_DB.get(f"{line}.{cue['id']}", 0), "flat": flat,
         "layers": [{"source": "sample", "sample": folder, "gain": 1,
                     "pitchJitter": 0 if cue["kind"] == "loop" or flat else 0.4,
                     "gainJitter": 0 if cue["kind"] == "loop" or flat else 1.0}]}
    d["layers"][0].update(LAYER_EXTRAS.get(f"{line}.{cue['id']}", {}))
    if tier == 1 and d["loop"]:
        d["drift"] = dict(TELL_DRIFT)
    d.update(CUE_DEF.get(f"{line}.{cue['id']}", {}))
    return d


def _read_def(path):
    """A content JSON file (comments allowed) -> (leading comment lines, dict)."""
    lines = open(path).read().splitlines()
    head = [l for l in lines if l.strip().startswith("//")]
    body = "\n".join(l for l in lines if not l.strip().startswith("//"))
    return head, json.loads(body)


def _write_def(path, head, d):
    """Write a sound definition, unless the file already says the same (comments and layout aside): what's been tidied
    by hand on main keeps its notes until the definition itself changes."""
    if os.path.exists(path):
        try:
            if _read_def(path)[1] == json.loads(json.dumps(d)):
                return False
        except ValueError:
            pass
    with open(path, "w") as f:
        f.write(head)
        json.dump(d, f, indent=1)
        f.write("\n")
    return True


def swapped():
    """TELL_SOUNDS and SWAPS as one list: (line, the game's sound, [(cue, layer extras)])."""
    out = [(line, sound, cues) for line, (sound, cues) in TELL_SOUNDS.items()]
    for name, (sound, extra) in SWAPS.items():
        line, cue = name.split(".", 1)
        out.append((line, sound, [(cue, extra)]))
    return out


def tell_sounds(chosen):
    """Swap each tell's game sound (and each of SWAPS') to the takes the checklist picked for it: kept ones, or while it's still under review
    its first candidate (the director's call, 3 Oct: what's made goes in the game without waiting for a verdict). Back
    to its synth definition only where nothing's picked (no candidate yet, or every one marked Redo).

    A cue split by surface (tell-tippy's tiptoe) gives the sound a variant per picked surface, <sound>.<surface>, which
    GameAudio picks by what the creature's on (the nearest one otherwise); the sound itself plays the first of them.
    """
    os.makedirs(SYNTH_DEFS, exist_ok=True)
    for line, sound, cues in swapped():
        path = os.path.join(SOUNDS, sound + ".json")
        backup = os.path.join(SYNTH_DEFS, sound + ".json")
        if not os.path.exists(backup):
            head, d = _read_def(path)
            if any(l.get("source") == "sample" for l in d["layers"]):
                continue
            shutil.copy(path, backup)
        for f in os.listdir(SOUNDS):
            if f.startswith(sound + ".") and f != sound + ".json":
                os.unlink(os.path.join(SOUNDS, f))      # last install's surface variants
        # Each cue's kept folders: the cue's own, or one per kept surface.
        kept = [(sorted(k for k in chosen if k == f"{line}/{cue}" or k.startswith(f"{line}/{cue}/")), extra)
                for cue, extra in cues]
        if any(not fs for fs, _ in kept):
            shutil.copy(backup, path)      # nothing picked for a cue: the tuned synth sound stays
            continue
        surfaces = {f.rsplit("/", 1)[1]: f for fs, _ in kept for f in fs if f.count("/") == 2}
        if surfaces:
            # One cue split by surface: a definition per kept surface, and the sound itself the first of them.
            extra = kept[0][1]
            variants = [(sound, [(next(iter(surfaces.values())), extra)])]
            variants += [(f"{sound}.{mat}", [(folder, extra)]) for mat, folder in surfaces.items()]
        else:
            variants = [(sound, [(f, extra) for fs, extra in kept for f in fs])]
        _, base = _read_def(backup)
        for name, folders in variants:
            d = dict(base)
            layers = []
            for folder, extra in folders:
                layer = {"source": "sample", "sample": folder, "gain": 1}
                layer.update(extra)
                layers.append(layer)
            d["layers"] = layers
            d["gainDb"] = d.get("gainDb", 0) + TELL_GAIN_DB.get(sound, 0) + SWAP_GAIN_DB.get(sound, 0)
            if sound in ONCE or sound in PACED:
                d["loop"] = False
            if not d.get("loop"):
                d.pop("duration", None)    # a one-shot of takes ends with its take
            d.pop("cycleSeconds", None)    # the synth's envelopes' period: the takes have none
            if _write_def(os.path.join(SOUNDS, name + ".json"),
                          f"// The {'tell' if line in TELL_SOUNDS else 'cue'}'s takes from the audio checklist, kept or the first candidate under review\n"
                          f"// ({', '.join(fo for fo, _ in folders)}), in place of its synth definition\n"
                          f"// (tools/audio/synth-defs/{sound}.json), keeping its tier, range and level. Written by\n"
                          f"// tools/audio/install.py; edit the cue or that file, not this one.\n", d):
                print(f"{name}: now the takes of {', '.join(fo for fo, _ in folders)}")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    store = sys.argv[sys.argv.index("--from") + 1]
    args = [a for a in args if a != store]
    dry = "--dry" in sys.argv
    if not args and not dry:
        # A full install starts clean: what the checklist no longer picks leaves the game. The clerk's word bank isn't a
        # checklist pick (clerk.py builds it), so it stays.
        for entry in os.listdir(SAMPLES) if os.path.isdir(SAMPLES) else []:
            if entry not in KEEP_SAMPLES and os.path.isdir(os.path.join(SAMPLES, entry)):
                shutil.rmtree(os.path.join(SAMPLES, entry))
        for f in os.listdir(SOUNDS):
            with open(os.path.join(SOUNDS, f)) as fh:
                if "tools/audio/install.py" in fh.readline():
                    os.unlink(os.path.join(SOUNDS, f))
    index_path = os.path.join(SAMPLES, "index.json")
    index = json.load(open(index_path)) if os.path.exists(index_path) else {}
    if not args and not dry:
        index = {rel: e for rel, e in index.items() if rel.split("/")[0] in KEEP_SAMPLES}
    n_cues = n_files = 0
    for line, cues in C.CUES.items():
        if args and line not in args:
            continue
        p = os.path.join(store, line + ".json")
        if not os.path.exists(p):
            continue
        item = json.load(open(p))
        item = item.get("data", item)
        if item.get("status") == "cut" or line in NOT_IN_GAME:
            continue
        stored = {c["id"]: c for c in item.get("cues") or []}
        for cue in cues:
            if cue["silent"] or cue["id"] not in stored:
                continue
            if f"{line}.{cue['id']}" in HELD and not any(k.get("verdict") == "keep" for k in stored[cue["id"]].get("cands") or []):
                continue
            cands = candidates(line, cue, stored[cue["id"]])
            # Prisoner voice sets: every set not marked Redo goes in, each its own folder and sound (setN), since the game
            # gives each prisoner a different voice for the run (D.7).
            groups = [(k.get("key"), [k]) for k in cands if k.get("verdict") != "redo"] if line in SETS_LINES else None
            for mat in (cue["mats"] or [None]) if groups is None else [g for g, _ in groups]:
                if groups is not None:
                    chosen, why = dict(groups)[mat], "set"
                else:
                    chosen, why = pick(cands, mat, item.get("level"), f"{line}.{cue['id']}")
                rel = f"{line}/{cue['id']}" + (f"/{mat}" if mat else "")
                if not chosen:
                    # Every candidate marked Redo: what's installed plays on till its replacement comes, but isn't kept
                    # any more (a tell goes back to its synth sound).
                    if rel in index and index[rel]["picked"] != "redo":
                        index[rel]["picked"] = "redo"
                        print(f"{rel:50s} redo  (installed takes stay till a new candidate)")
                    continue
                takes = [t for k in chosen for t in takes_of(k, cue)]
                n_cues += 1
                n_files += len(takes)
                print(f"{rel:50s} {why:5s} {len(takes)} take(s) from {', '.join(k.get('key') or k.get('libkey') or k['src'] for k in chosen)}")
                if dry:
                    continue
                folder = os.path.join(SAMPLES, rel)
                shutil.rmtree(folder, ignore_errors=True)
                for i, x in enumerate(takes):
                    encode(x, os.path.join(folder, f"{i:02d}.opus"))
                name = f"{line}.{cue['id']}" + (f".{mat}" if mat else "")
                _write_def(os.path.join(SOUNDS, name + ".json"),
                           f"// {item['name']}: {cue['event']}" + (f" ({C.MATERIALS.get(mat, mat)})" if mat else "") +
                           f". Written by tools/audio/install.py from the audio checklist ({why}); edit the cue, not this.\n",
                           sound_def(item, cue, rel, line))
                index[rel] = {"cue": f"{line}.{cue['id']}", "surface": mat, "picked": why,
                              "candidates": [{"label": k.get("label"), "key": k.get("key") or k.get("libkey") or k["src"],
                                              "sources": k.get("sources") or ([k["libkey"]] if k.get("libkey") else []),
                                              "licence": "Sonniss GDC" if k.get("restricted")
                                              else TTS_LICENCE if (line, cue["id"]) in TTS_CUES else "CC0"} for k in chosen],
                              "takes": len(takes),
                              "sha": hashlib.sha256(b"".join(open(os.path.join(folder, f), "rb").read()
                                                             for f in sorted(os.listdir(folder)))).hexdigest()[:16]}
    if not dry:
        os.makedirs(SAMPLES, exist_ok=True)
        json.dump(dict(sorted(index.items())), open(index_path, "w"), indent=1)
        tell_sounds({rel for rel, e in index.items() if e["picked"] in ("kept", "first")})
    print(f"{n_cues} cue folders, {n_files} takes")


if __name__ == "__main__":
    main()
