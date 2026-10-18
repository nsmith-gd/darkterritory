"""The band's recorded instruments (queue #443, note 711): CC0 instrument samples into content/audio/samples/band.

The band (content/tuning/band.json) plays Celtic / East Coast folk tunes on eight instruments, each note by resampling the
nearest recorded one. This takes single notes and strokes from CC0 sample libraries on GitHub (each source pinned to a
commit, its licence file checked), and writes:

  content/audio/samples/band/<instrument>/<note or stroke>.opus   mono 48 kHz Ogg Opus, as install.py encodes takes
  content/audio/samples/band/manifest.json                        what the game reads: notes (midi) or strokes, gainDb
  content/audio/samples/band/provenance.json                      every file's source, original path, pitch, processing
  content/audio/samples/index.json  "band/<instrument>"           the sound credits' provenance (Credits.cs reads it)

    python3 -I tools/audio/band_intake.py [--src DIR] [--dry]

--src: where the sources are fetched (blobless, only the files in the table below; default a temp dir, reused).
--dry: fetch and measure, print the table, write nothing.

Every note's pitch is measured (YIN on its steady part), labelled with the nearest semitone (a source name that's more
than 40 cents off is corrected and said so) and resampled onto that semitone exactly, so the game's resampling starts in
tune. Needs ffmpeg (with libopus) and numpy. Run it with `python3 -I`: the sources are downloaded data.
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SAMPLES = os.path.join(ROOT, "content", "audio", "samples")
BAND = os.path.join(SAMPLES, "band")
INDEX = os.path.join(SAMPLES, "index.json")
SR = 48000
OPUS_KBPS = 64           # as install.py
PEAK_DB = -3.0           # every file's (melodic) or every instrument's loudest take's (percussion) peak
MELODIC_MAX_S = 2.5
PERC_MAX_S = 0.6
GAIN_RANGE_DB = (-12.0, 6.0)  # a trim, not a rescue: no take pushed more than 3 dB over full scale
MUSETTE_CENTS = 12.0     # the accordion's second reed, tuned wet (see ACCORDION below)

# ---- The sources: CC0 libraries on GitHub, each pinned to the commit its licence file was read at ----------------------
SOURCES = {
    "vsco2ce": {"name": "VS Chamber Orchestra: Community Edition, by Versilian Studios (Sam Gossner, Simon Dalzell)",
                "url": "https://github.com/sgossner/VSCO-2-CE", "commit": "440300901dfe9275fd84e0b7763af1f8443ae62e",
                "licence": "CC0 1.0", "licenceFile": "LICENSE"},
    "vcsl": {"name": "Versilian Community Sample Library, by Versilian Studios",
             "url": "https://github.com/sgossner/VCSL", "commit": "c1ea7bcc3c7309650ab0da9d15c9cd1fbc4a4c7e",
             "licence": "CC0 1.0", "licenceFile": "LICENSE"},
    "shinyguitar": {"name": "Shinyguitar (archtop guitar, microphone), by D. Smolken, Karoryfer Samples",
                    "url": "https://github.com/sfzinstruments/karoryfer.shinyguitar",
                    "commit": "57243cca85277dbcc120ce17c6178032f93c80f3", "licence": "CC0 1.0", "licenceFile": "LICENSE"},
    "ganjo": {"name": "ganjo (SX 6-string banjo), recorded by itsclipping",
              "url": "https://github.com/sfzinstruments/ganjo", "commit": "ccff5cd5cd3b513873a48994c07724d9d3c39e1c",
              "licence": "CC0 1.0", "licenceFile": "LICENSE.md"},
    "bodypercussion": {"name": "Body percussion, sfzinstruments",
                       "url": "https://github.com/sfzinstruments/body_percussion",
                       "commit": "4ac9d8966679c648b62fa10a188179e186b97f24", "licence": "CC0 1.0", "licenceFile": "LICENSE"},
}

# ---- What each instrument is made from ----------------------------------------------------------------------------------
# Melodic: (source, path, the note the source's file name gives). Percussion: stroke -> [(source, path[, slice s, s])].
VLN = "Strings/Solo Violin/Arco Vib/LLVln_ArcoVib_{}_f.wav"
REC = "Aerophones/Edge-blown Aerophones/Baroque Soprano Recorder/Sustain/SopRecorder_Sus_{}_rr1_Main.wav"
HARP = "Aerophones/Free Aerophones/Harmonica-Hohner-Super64/Sustains/Normal/Hohner-Super64_Normal _{}.wav"
BANJO = "Common/Banjo_Common - {}.wav"
GTR = "Samples/acoustic/{}_vl3_rr1_1.wav"
FRAME = "Membranophones/Struck Membranophones/Frame Drum/{}.wav"
MISC2 = "Miscellania Raw/Misc 2/{}.wav"
STOMP = "Samples/body/{}.wav"

MELODIC = {
    # The fiddle: a solo violin, arco with vibrato, forte (G3-E6).
    "fiddle": {"label": "A solo violin, bowed (arco, vibrato, forte)",
               "notes": [("vsco2ce", VLN.format(n), n) for n in ("G3", "C4", "E4", "A4", "C5", "E5", "G5", "C6", "E6")]},
    # The tin whistle: a baroque soprano recorder (the nearest fipple flute; no CC0 tin whistle was reachable). VCSL names
    # it an octave under where it sounds, so its labels are corrected (+12) by the measured pitch.
    "whistle": {"label": "A baroque soprano recorder, sustained (stands in for the D whistle)",
                "notes": [("vcsl", REC.format(n), n) for n in ("D4", "E4", "F#4", "A#4", "C5", "D5", "E5", "F#5", "A#5")]},
    # The button accordion: a chromatic harmonica (free reeds, as an accordion's), with a second copy tuned
    # MUSETTE_CENTS sharp mixed in for the accordion's two-reed beat, and its slow breath-in swell cut (ACCORDION).
    "accordion": {"label": "A chromatic harmonica (Hohner Super 64), doubled a few cents sharp for an accordion's musette",
                  "notes": [("vcsl", HARP.format(n), n) for n in ("C3", "E3", "G3", "C4", "E4", "G4", "C5")]},
    # The tenor banjo: a 6-string (guitar) banjo, picked. Its names run an octave high and its tuning loose.
    "banjo": {"label": "A 6-string banjo, picked",
              "notes": [("ganjo", BANJO.format(n), n) for n in ("A#3", "E4", "A4", "C5", "D#5", "G5", "B5")]},
    # The guitar: an archtop played acoustic (its microphone), plucked, third of four velocity layers (E2-E5).
    "guitar": {"label": "An archtop guitar, acoustic, plucked",
               "notes": [("shinyguitar", GTR.format(n), n) for n in ("e2", "a2", "c3", "gb3", "a3", "c4", "eb4", "a4", "eb5")]},
}
ACCORDION = {"accordion"}

PERCUSSION = {
    # The bodhrán: a large frame drum struck open (and once damped) for the down stroke; a small one, damped, for the tip.
    "bodhran": {"label": "Frame drums: a large one struck for the down stroke, a small damped one for the tipper",
                "strokes": {"down": [("vcsl", FRAME.format("HDrumL_Hit_v3_rr1_Sum")),
                                     ("vcsl", FRAME.format("HDrumL_Hit_v3_rr2_Sum")),
                                     ("vcsl", FRAME.format("HDrumL_HitMuted_v3_rr1_Sum"))],
                            "tip": [("vcsl", FRAME.format("HDrumS_HitMuted_v3_rr1_Sum")),
                                    ("vcsl", FRAME.format("HDrumS_Hit_v3_rr2_Sum")),
                                    ("vcsl", FRAME.format("HDrumS_Hit_v3_rr1_Sum"))]}},
    # The spoons: real spoons, single clacks cut from one played take (the loud ones and the light ones).
    "spoons": {"label": "Spoons, played: single clacks cut from one take",
               "strokes": {"clack": [("vsco2ce", MISC2.format("spoons_fun"), (1.505, 1.665)),
                                     ("vsco2ce", MISC2.format("spoons_fun"), (3.790, 4.100)),
                                     ("vsco2ce", MISC2.format("spoons_fun"), (6.005, 6.300))],
                           "light": [("vsco2ce", MISC2.format("spoons_fun"), (4.365, 4.640)),
                                     ("vsco2ce", MISC2.format("spoons_fun"), (6.545, 6.840)),
                                     ("vsco2ce", MISC2.format("spoons_fun"), (7.360, 7.640))]}},
    # The ugly stick: a boot stomped on a floor (the stick's rubber boot), and a tambourine's jingles (its bottle caps).
    "uglyStick": {"label": "A boot stomped on a wooden floor, and a tambourine's jingles for the bottle caps",
                  "strokes": {"boot": [("bodypercussion", STOMP.format("stomp_r_vl2_rr3")),
                                       ("bodypercussion", STOMP.format("stomp_r_vl2_rr5")),
                                       ("bodypercussion", STOMP.format("stomp_l_vl2_rr3"))],
                              "caps": [("vsco2ce", MISC2.format("tamb3_1")),
                                       ("vsco2ce", MISC2.format("tamb3_3")),
                                       ("vsco2ce", MISC2.format("tamb3_7"))]}},
}
ORDER = ["fiddle", "whistle", "accordion", "banjo", "guitar", "bodhran", "spoons", "uglyStick"]

NOTE_NAMES = ["C", "Cs", "D", "Ds", "E", "F", "Fs", "G", "Gs", "A", "As", "B"]


def note_midi(name):
    """'A#4', 'eb3', 'gb2' -> MIDI (C4 = 60)."""
    m = re.fullmatch(r"([A-Ga-g])([#b]?)(-?\d)", name)
    base = {"c": 0, "d": 2, "e": 4, "f": 5, "g": 7, "a": 9, "b": 11}[m.group(1).lower()]
    return 12 * (int(m.group(3)) + 1) + base + {"#": 1, "b": -1, "": 0}[m.group(2)]


def midi_name(m):
    return f"{NOTE_NAMES[m % 12]}{m // 12 - 1}"


# ---- Fetching ------------------------------------------------------------------------------------------------------------
def git(cwd, *args):
    subprocess.run(["git", "-c", "advice.detachedHead=false", *args], cwd=cwd, check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)


def fetch(src_root):
    """Each source at its commit, blobless, with only the files the table names (and its licence file) checked out."""
    wanted = {k: {s["licenceFile"]} for k, s in SOURCES.items()}
    for spec in MELODIC.values():
        for key, path, _ in spec["notes"]:
            wanted[key].add(path)
    for spec in PERCUSSION.values():
        for takes in spec["strokes"].values():
            for key, path, *_ in takes:
                wanted[key].add(path)
    for key, s in SOURCES.items():
        d = os.path.join(src_root, key)
        if not os.path.isdir(os.path.join(d, ".git")):
            os.makedirs(d, exist_ok=True)
            git(d, "init", "-q")
            git(d, "remote", "add", "origin", s["url"])
        missing = [p for p in sorted(wanted[key]) if not os.path.exists(os.path.join(d, p))]
        if missing:
            git(d, "fetch", "-q", "--depth", "1", "--filter=blob:none", "origin", s["commit"])
            git(d, "--literal-pathspecs", "checkout", "-q", s["commit"], "--", *missing)
        head = subprocess.run(["git", "rev-parse", "FETCH_HEAD"], cwd=d, capture_output=True, text=True).stdout.strip()
        assert head in ("", s["commit"]), f"{key}: fetched {head}, pinned {s['commit']}"
        with open(os.path.join(d, s["licenceFile"]), encoding="utf-8", errors="replace") as f:
            text = f.read()
        assert "CC0 1.0 Universal" in text, f"{key}: {s['licenceFile']} isn't CC0 1.0"
        s["licenceSha256"] = hashlib.sha256(text.encode()).hexdigest()
    return {k: os.path.join(src_root, k) for k in SOURCES}


# ---- Audio ---------------------------------------------------------------------------------------------------------------
def load(path, rate=SR):
    out = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(rate), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(out, np.float32).astype(np.float64)


def shift_cents(x, cents):
    """Resampled (pitch and length together) by `cents`, through ffmpeg's resampler: read as if at another rate."""
    if abs(cents) < 0.05:
        return x
    rate = int(round(SR * 2 ** (cents / 1200)))
    out = subprocess.run(["ffmpeg", "-v", "error", "-f", "f32le", "-ar", str(rate), "-ac", "1", "-i", "-",
                          "-af", "aresample=resampler=soxr:precision=28" if HAVE_SOXR else "aresample",
                          "-ar", str(SR), "-f", "f32le", "-"],
                         input=x.astype(np.float32).tobytes(), capture_output=True, check=True).stdout
    return np.frombuffer(out, np.float32).astype(np.float64)


def yin(x, fmin=60.0, fmax=2600.0, frame=2048, hop=1024, threshold=0.15):
    """Per-frame f0 by YIN (de Cheveigné & Kawahara 2002) with parabolic interpolation; quiet frames skipped."""
    tmin, tmax = int(SR / fmax), int(SR / fmin)
    level = np.sqrt(np.mean(x ** 2)) if len(x) else 0
    out = []
    for s in range(0, len(x) - frame - tmax, hop):
        f = x[s:s + frame + tmax]
        if np.sqrt(np.mean(f[:frame] ** 2)) < 0.1 * level:
            continue
        a = f[:frame]
        e0 = np.sum(a ** 2)
        # Difference function by the energy terms and a cross-correlation (FFT).
        n = 1 << int(np.ceil(np.log2(2 * (frame + tmax))))
        cc = np.fft.irfft(np.fft.rfft(f, n) * np.conj(np.fft.rfft(a, n)), n)[:tmax + 1]
        sq = np.concatenate([[0], np.cumsum(f ** 2)])
        et = sq[np.arange(tmax + 1) + frame] - sq[np.arange(tmax + 1)]
        d = e0 + et - 2 * cc
        d[0] = 0
        cmnd = np.ones_like(d)
        cmnd[1:] = d[1:] * np.arange(1, tmax + 1) / np.maximum(np.cumsum(d[1:]), 1e-12)
        t = next((t for t in range(tmin, tmax) if cmnd[t] < threshold and cmnd[t] <= cmnd[t + 1]), None)
        if t is None:
            continue
        p, q, r = cmnd[t - 1], cmnd[t], cmnd[t + 1]
        den = p - 2 * q + r
        out.append(SR / (t + (0.5 * (p - r) / den if den else 0.0)))
    return np.array(out)


def pitch(x, plucked):
    """The note's pitch in MIDI (fractional) from its steady part, and the frames' spread in cents."""
    seg = x[int((0.03 if plucked else 0.12) * SR):int((0.45 if plucked else 0.9) * SR)]
    f = yin(seg)
    if len(f) < 3:
        raise ValueError("no steady pitch")
    m = 69 + 12 * np.log2(np.median(f) / 440)
    spread = float(np.std(1200 * np.log2(f / np.median(f))))
    return float(m), spread


def onset(x, rel=0.02):
    pk = np.max(np.abs(x))
    return int(np.argmax(np.abs(x) >= pk * rel))


def trim(x, swell=False):
    """From just before the first sound (the onset within 1 ms of the start), a 1 ms fade-in. `swell`: from where the
    note has reached a third of its level instead (a harmonica's breath-in swell cut to an accordion's quick start)."""
    if swell:
        hop = 240
        env = np.array([np.sqrt(np.mean(x[i:i + hop] ** 2)) for i in range(0, len(x) - hop, hop)])
        start = int(np.argmax(env >= env[:int(1.5 * SR / hop)].max() / 3)) * hop
        y = x[start:].copy()
        n = int(0.006 * SR)
    else:
        start = max(0, onset(x) - int(0.001 * SR))
        y = x[start:].copy()
        n = int(0.001 * SR)
    y[:n] *= np.sin(np.linspace(0, np.pi / 2, n)) ** 2
    return y


def cap(x, seconds, fade):
    """At most `seconds` long, ending in a `fade`-second cosine fade (only if it's cut, or it would end abruptly)."""
    x = x[:int(seconds * SR)].copy()
    n = min(int(fade * SR), len(x))
    x[len(x) - n:] *= np.cos(np.linspace(0, np.pi / 2, n)) ** 2
    return x


def db(v):
    return 20 * np.log10(max(v, 1e-12))


def encode(x, path):
    """Ogg Opus as install.py writes a take (64 kbit/s, bit-exact, no metadata), so rebuilds don't churn the repo."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(SR), "-ac", "1", "-i", "-",
                    "-c:a", "libopus", "-b:a", f"{OPUS_KBPS}k", "-application", "audio", "-map_metadata", "-1",
                    "-fflags", "+bitexact", "-flags:a", "+bitexact", path],
                   input=np.clip(x, -1, 1).astype(np.float32).tobytes(), check=True)
    y = load(path)
    assert np.max(np.abs(y)) < 0.995, f"{path} would clip decoded ({np.max(np.abs(y)):.3f})"
    on_ms = onset(y) / SR * 1000
    assert on_ms <= 5.0, f"{path}: onset at {on_ms:.1f} ms"
    return y, on_ms


def have_soxr():
    r = subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=mono", "-t", "0.01",
                        "-af", "aresample=resampler=soxr", "-f", "null", "-"], capture_output=True)
    return r.returncode == 0


HAVE_SOXR = False


# ---- The intake ----------------------------------------------------------------------------------------------------------
def melodic(name, spec, dirs, out, dry):
    notes, prov = [], []
    plucked = name in ("banjo", "guitar")
    for key, path, label in spec["notes"]:
        x = load(os.path.join(dirs[key], path))
        if name in ACCORDION:
            # The second reed: the same note a few cents sharp, the pair beating as an accordion's musette does.
            twin = shift_cents(x, MUSETTE_CENTS)
            n = min(len(x), len(twin))
            x = 0.5 * (x[:n] + twin[:n])
        x = trim(x, swell=name in ACCORDION)
        raw, spread = pitch(x, plucked)
        labelled = note_midi(label)
        midi = int(round(raw))
        corrected = abs(raw - labelled) * 100 > 40
        retune = (midi - raw) * 100
        x = shift_cents(x, retune) if abs(retune) >= 1.0 else x
        after, _ = pitch(x, plucked)
        x = cap(x, MELODIC_MAX_S, 0.25 if len(x) > MELODIC_MAX_S * SR else 0.02)
        x *= 10 ** (PEAK_DB / 20) / np.max(np.abs(x))
        rel = f"{name}/{midi_name(midi)}.opus"
        on_ms = 0.0
        if not dry:
            x, on_ms = encode(x, os.path.join(out, rel))
        notes.append({"file": rel, "midi": midi})
        prov.append({"file": rel, "source": key, "path": path, "sourceLabel": label, "sourceLabelMidi": labelled,
                     "measuredMidi": round(raw, 3), "measuredCents": round((raw - midi) * 100, 1),
                     "labelCorrected": corrected, "retunedCents": round(retune, 1) if abs(retune) >= 1.0 else 0.0,
                     "afterCents": round((after - midi) * 100, 1), "spreadCents": round(spread, 1),
                     "seconds": round(len(x) / SR, 3), "onsetMs": round(on_ms, 2), "x": x})
    notes.sort(key=lambda n: n["midi"])
    return notes, prov


def percussion(name, spec, dirs, out, dry):
    takes = []
    for stroke, srcs in spec["strokes"].items():
        for i, (key, path, *cut) in enumerate(srcs, 1):
            x = load(os.path.join(dirs[key], path))
            if cut:
                a, b = cut[0]
                x = x[int(a * SR):int(b * SR)]
            x = trim(x)
            x = cap(x, PERC_MAX_S, 0.08 if len(x) > PERC_MAX_S * SR else 0.01)
            takes.append((stroke, i, key, path, cut[0] if cut else None, x))
    # One gain for the instrument: its loudest take's peak at PEAK_DB, so a tip stays lighter than a down stroke.
    g = 10 ** (PEAK_DB / 20) / max(np.max(np.abs(t[-1])) for t in takes)
    strokes, prov = {s: [] for s in spec["strokes"]}, []
    for stroke, i, key, path, cut, x in takes:
        x = x * g
        rel = f"{name}/{stroke}{i}.opus"
        on_ms = 0.0
        if not dry:
            x, on_ms = encode(x, os.path.join(out, rel))
        strokes[stroke].append(rel)
        prov.append({"file": rel, "source": key, "path": path, **({"slice": list(cut)} if cut else {}),
                     "peakDb": round(db(np.max(np.abs(x))), 1), "seconds": round(len(x) / SR, 3),
                     "onsetMs": round(on_ms, 2), "x": x})
    return strokes, prov


def loudness(prov, window=None):
    """An instrument's level: the RMS of each file's first 300 ms (or, with `window`, of its loudest `window` seconds
    within them), averaged in power over its files."""
    p = []
    for e in prov:
        x = e["x"][:int(0.3 * SR)]
        if window:
            n, hop = int(window * SR), int(0.01 * SR)
            p.append(max(np.mean(x[i:i + n] ** 2) for i in range(0, max(1, len(x) - n + 1), hop)))
        else:
            p.append(np.mean(x ** 2))
    return db(np.sqrt(np.mean(p)))


def main():
    global HAVE_SOXR
    dry = "--dry" in sys.argv
    src_root = sys.argv[sys.argv.index("--src") + 1] if "--src" in sys.argv else os.path.join(tempfile.gettempdir(),
                                                                                              "dt-band-intake")
    HAVE_SOXR = have_soxr()
    dirs = fetch(src_root)
    out = BAND
    if not dry:
        for d in ORDER:
            p = os.path.join(out, d)
            if os.path.isdir(p):
                for f in os.listdir(p):
                    if f.endswith(".opus"):
                        os.remove(os.path.join(p, f))
    instruments, provenance, levels = {}, {}, {}
    for name in ORDER:
        if name in MELODIC:
            notes, prov = melodic(name, MELODIC[name], dirs, out, dry)
            instruments[name] = {"notes": notes}
        else:
            strokes, prov = percussion(name, PERCUSSION[name], dirs, out, dry)
            instruments[name] = {"strokes": strokes}
        provenance[name] = prov
        levels[name] = loudness(prov)
    # The trims bring each instrument to the band's median level.
    ref = float(np.median(list(levels.values())))
    for name in ORDER:
        instruments[name]["gainDb"] = float(np.clip(round((ref - levels[name]) * 2) / 2, *GAIN_RANGE_DB))
        instruments[name]["source"] = sorted({e["source"] for e in provenance[name]})

    # The report.
    for name in ORDER:
        spec = MELODIC.get(name) or PERCUSSION[name]
        print(f"{name}  ({spec['label']}; level {levels[name]:.1f} dB, gainDb {instruments[name]['gainDb']:+.1f}"
              f"{'' if abs(ref - levels[name] - instruments[name]['gainDb']) < 0.3 else f', estimate {ref - levels[name]:+.1f}'})")
        for e in provenance[name]:
            if "measuredMidi" in e:
                print(f"  {e['file']:22s} midi {int(round(e['measuredMidi'])):3d}  measured {e['measuredCents']:+6.1f} c"
                      f"  (source says {e['sourceLabel']}{', corrected' if e['labelCorrected'] else ''})"
                      f"  retuned {e['retunedCents']:+6.1f} -> {e['afterCents']:+5.1f} c  spread {e['spreadCents']:4.1f} c"
                      f"  {e['seconds']:.2f} s  onset {e['onsetMs']:.2f} ms")
            else:
                print(f"  {e['file']:22s} peak {e['peakDb']:+5.1f} dBFS  {e['seconds']:.2f} s  onset {e['onsetMs']:.2f} ms"
                      f"  <- {e['source']}:{os.path.basename(e['path'])}{' ' + str(e.get('slice')) if 'slice' in e else ''}")
    if dry:
        return

    used = sorted({e["source"] for p in provenance.values() for e in p})
    # "source" stays one object (name, url, licence, commit) for a reader that wants one; with several libraries its
    # commit lists each as key@sha, each instrument's "source" names the ones it's made from, and "sources" has them whole.
    manifest = {
        "instruments": {n: instruments[n] for n in ORDER},
        "source": {"name": f"{len(used)} CC0 sample libraries on GitHub (see \"sources\"; each instrument's \"source\" "
                           "names its own)" if len(used) > 1 else SOURCES[used[0]]["name"],
                   "url": SOURCES["vcsl"]["url"] if "vcsl" in used else SOURCES[used[0]]["url"],
                   "licence": "CC0 1.0",
                   "commit": ", ".join(f"{k}@{SOURCES[k]['commit']}" for k in used) if len(used) > 1
                   else SOURCES[used[0]]["commit"]},
        "sources": {k: {f: SOURCES[k][f] for f in ("name", "url", "licence", "commit")} for k in used},
    }
    with open(os.path.join(out, "manifest.json"), "w") as f:
        f.write("// The band's recorded instruments (tools/audio/band_intake.py; queue #443, note 711). Melodic instruments:\n"
                "// each note's file and its pitch (measured, and resampled onto that semitone); the game plays a note by\n"
                "// resampling the nearest. Percussion: each stroke's takes. gainDb: a per-instrument trim so the eight sit\n"
                "// at a similar level (the RMS of each file's first 300 ms, brought to the eight's median; held to -12..+6 dB).\n"
                "// \"source\": the libraries each is made from (\"sources\"). All CC0 1.0; provenance.json has every file's origin.\n")
        json.dump(manifest, f, indent=1)
        f.write("\n")
    with open(os.path.join(out, "provenance.json"), "w") as f:
        f.write("// Where each of the band's samples came from and what was done to it (tools/audio/band_intake.py).\n")
        json.dump({
            "sources": {k: SOURCES[k] for k in used},
            "processing": [
                "Decoded to mono (channels averaged) at 48 kHz.",
                "accordion only: mixed with itself resampled +%g cents (a second reed, musette), and started where the "
                "note reaches a third of its level, with a 6 ms fade-in (the harmonica's breath-in swell cut)." % MUSETTE_CENTS,
                "Leading silence trimmed to 1 ms before the onset (2%% of the peak), with a 1 ms fade-in; spoons are "
                "cut from one take at the given slice (seconds) first.",
                "Melodic: pitch measured by YIN over the steady part (median of frames), labelled with the nearest "
                "semitone, and resampled (ffmpeg aresample%s) onto it; capped at %g s with a 0.25 s fade-out; peak "
                "normalised to %g dBFS per file." % (" soxr" if HAVE_SOXR else "", MELODIC_MAX_S, PEAK_DB),
                "Percussion: capped at %g s with an 80 ms fade-out; one gain per instrument putting its loudest take's "
                "peak at %g dBFS (the strokes keep their relative levels)." % (PERC_MAX_S, PEAK_DB),
                "Encoded as Ogg Opus, %d kbit/s, mono 48 kHz, bit-exact (as tools/audio/install.py)." % OPUS_KBPS,
            ],
            "files": {e["file"]: {k: v for k, v in e.items() if k not in ("file", "x")}
                      for n in ORDER for e in provenance[n]},
        }, f, indent=1)
        f.write("\n")

    # The sound credits' provenance: a cue per instrument in samples/index.json, as clerk.py writes the clerk's bank.
    with open(INDEX) as f:
        index = json.load(f)
    for n in ORDER:
        folder = os.path.join(out, n)
        files = sorted(x for x in os.listdir(folder) if x.endswith(".opus"))
        digest = hashlib.sha256(b"".join(open(os.path.join(folder, x), "rb").read() for x in files)).hexdigest()[:16]
        spec = MELODIC.get(n) or PERCUSSION[n]
        index[f"band/{n}"] = {
            "cue": f"band.{n}", "surface": None, "picked": "intake",
            "candidates": [{"label": f"The band's {n}: {spec['label']} (tools/audio/band_intake.py)", "key": "intake",
                            "sources": sorted({f"{e['source']}:{e['path']}" for e in provenance[n]}),
                            "licence": "CC0"}],
            "takes": len(files), "sha": digest}
    with open(INDEX, "w") as f:
        json.dump(dict(sorted(index.items())), f, indent=1)  # as install.py writes it
    total = sum(os.path.getsize(os.path.join(dp, x)) for dp, _, fs in os.walk(out) for x in fs if x.endswith(".opus"))
    print(f"{sum(len(p) for p in provenance.values())} files, {total / 1e6:.2f} MB -> {os.path.relpath(out, ROOT)}")


if __name__ == "__main__":
    main()
