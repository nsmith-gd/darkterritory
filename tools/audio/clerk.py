"""The yard on the radio (GDD §9; ARCHITECTURE §8 note 178): the dispatcher's manifest and the clerk's tally, read in one
man's voice the crew learn, flat and bored.

The words are made by the game from the night's own numbers (Sim.Run.Radio), so there's no script to record: this builds
the clerk's vocabulary, every phrase and word those lines can hold (numbers as words, the cargoes, the bots' names), each a
take of its own, and the game strings a line together from them as it goes on air (GameAudio.Clerk). A word the bank
doesn't have (a name a player typed) is the set breaking up over it; the card on screen still says it.

Stand-in until a voice is recorded: Piper's LibriTTS model (CC BY 4.0, credit "LibriTTS, Zen et al. 2019"), one speaker,
read with the expression taken out (low noise scales). Dry: the game's radio-clerk-voice puts it through the set.

    python3 tools/audio/clerk.py [--speaker 385] [--previews DIR]

writes content/audio/samples/voice-clerk/bank/<word>.opus and bank.json (each entry's file and length, so the game can time
a line before it plays), and its provenance into content/audio/samples/index.json. Needs the model in out/audio/tts
(tools/audio/README.md); without it this fails, it never fakes a voice. --previews writes a sample manifest and tally in
each candidate speaker, for the checklist.
"""

import argparse
import hashlib
import json
import os
import re
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import dsp  # noqa: E402
from install import encode  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODEL = os.path.join(ROOT, "out", "audio", "tts", "en-us-libritts-high.onnx")
BANK = os.path.join(ROOT, "content", "audio", "samples", "voice-clerk", "bank")
INDEX = os.path.join(ROOT, "content", "audio", "samples", "index.json")

# Flattest men's voices of LibriTTS's 904 (every 7th surveyed: median pitch 85-130 Hz, least pitch spread over a clerk's
# line), none of them a prisoner's (recipes/voices.py SETS). The first is the bank's until the director keeps another.
CANDIDATES = [(385, "a low, even man's voice"), (518, "a deep man's voice, slower"), (329, "a man's voice, flattest")]

# Phrases read whole, so they keep a phrase's run (the game matches the longest first); then single words. Every word
# Sim.Run.Radio's lines can hold: ClerkTests fails on one that isn't here.
PHRASES = ["yard to consist", "manifest follows", "gates open", "yard out", "yard clerk", "consist received",
           "tally follows", "powder and shot", "cars delivered", "cars lost", "child survivor", "child survivors",
           "body recovered", "body not recovered", "gunpowder and shot", "machine parts", "comet-derived material"]
WORDS = ["crew", "coal", "cars", "freight", "cargo", "paid", "fee", "refund", "mail", "salvage", "repairs", "net", "next",
         # The cargoes (Train.Cargoes.Name).
         "goods", "grain", "salvage", "livestock", "food", "chemicals", "ore", "medicine", "timber",
         # Numbers, as the game says them (ClerkVoice.Say): "two thousand four hundred fifty", "minus".
         "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
         "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty", "thirty", "forty",
         "fifty", "sixty", "seventy", "eighty", "ninety", "hundred", "thousand", "million", "minus"]


# The derail cause card (GDD v1.4 App. E.5; IncidentLog.CauseCard and the causes World.Derail and Overspeed are given:
# Lineside's bends, the Sleepers, the Switchman, TrackRules' washouts and bridges, the Stoker's runaway, C.9's blame lines)
# and the Stranded line (E.9; Radio.Stranded). ClerkVoice says "68 km/h" as "68 kilometres an hour", "at km 12" as
# "at kilometre 12", "40 s" as "40 seconds" and "12 m" as "12 metres".
CAUSE_PHRASES = ["consist derailed", "took the", "too fast", "ran onto the washout", "gave way under car",
                 "the switchman threw the points under it", "they throw a train off", "ran onto the sleepers", "they'll take",
                 "the stoker ran away with it", "nobody on the throttle", "forward cannon crewed by", "forward cannon not crewed",
                 "no forward cannon on the consist", "firebox last tended", "nobody had tended the firebox",
                 "recovery not scheduled", "cause not established", "consist reported stranded", "recovery at first light",
                 "recovery is chargeable", "kilometres an hour", "from the train"]
CAUSE_WORDS = ["at", "the", "over", "bend", "throttle", "unattended", "kilometre", "seconds", "metres", "car",
               # The places a derailment is put at (IncidentLog.At): the line generator's own words for them.
               "mile", "fort", "junction", "tunnel", "wreck"]


def jsonc(*path):
    with open(os.path.join(ROOT, *path)) as f:
        return json.loads(re.sub(r"(?m)^\s*//.*$", "", f.read()))


def places():
    """Every word a station's, a bridge's or a facility's name can hold (content/linegen names.json, facilities.json)."""
    n = jsonc("content", "linegen", "names.json")
    words = []
    for key in ("surnames", "features", "owners", "haltSuffixes", "bridgeKinds", "tunnelWords"):
        words += n.get(key, [])
    for f in jsonc("content", "linegen", "facilities.json")["types"].values():
        words += f["type"].split()
    return [w.lower() for w in words]


# How a word's said where the phonemizer guesses wrong ("Achebe" came out "Aitch-b").
SAY = {"achebe": "Ah-chay-bay", "okafor": "Oh-kah-for", "reyes": "Ray-ez", "moreau": "Mor-oh", "combe": "Coom",
       "drury": "Droo-ree"}


def bots():
    """The bots' names (player.json botNames): a bot crew's manifest is read in full."""
    with open(os.path.join(ROOT, "content", "tuning", "player.json")) as f:
        text = re.sub(r"(?m)^\s*//.*$", "", f.read())
    return [n.lower() for n in json.loads(text)["botNames"]]


def vocabulary():
    seen, out = set(), []
    for w in PHRASES + CAUSE_PHRASES + WORDS + CAUSE_WORDS + bots() + places():
        if w not in seen:
            seen.add(w)
            out.append(w)
    return out


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


_voice = None


def say(text, speaker, rate=0.92):
    """One phrase, read flat: low noise scales take the expression out (the same tone used for the coal)."""
    global _voice
    from piper import PiperVoice, SynthesisConfig
    if not os.path.exists(MODEL):
        raise SystemExit(f"no voice model at {MODEL} (tools/audio/README.md)")
    if _voice is None:
        _voice = PiperVoice.load(MODEL, config_path=MODEL + ".json")
    cfg = SynthesisConfig(speaker_id=speaker, length_scale=rate, noise_scale=0.35, noise_w_scale=0.4)
    # Said as a sentence ("Coal."), so a word ends as a word does, not hanging on a comma.
    a = np.concatenate([c.audio_float_array for c in _voice.synthesize(text[0].upper() + text[1:] + ".", syn_config=cfg)])
    x = dsp.resample(a.astype(np.float32), dsp.SR / 22050)
    x = dsp.trim_silence(dsp.hp(x, 70), db=-42, pad=0.012)
    return dsp.fade(x, 0.004, 0.025)


def build(speaker, fresh=False):
    words = vocabulary()
    os.makedirs(BANK, exist_ok=True)
    # The reading isn't the same twice (the model samples its noise), so a take already in the bank, said the same way
    # by the same speaker, is kept: a rebuild only reads what's new or respelled, and doesn't churn the repo.
    old = {}
    if not fresh and os.path.exists(os.path.join(BANK, "bank.json")):
        with open(os.path.join(BANK, "bank.json")) as f:
            was = json.loads(re.sub(r"(?m)^//.*$", "", f.read()))
        if was.get("speaker") == speaker:
            old = was["entries"]
    entries, digest = {}, hashlib.sha256()
    for w in words:
        said = SAY.get(w, w)
        path = os.path.join(BANK, slug(w) + ".opus")
        if w in old and old[w].get("said", said) == said and os.path.exists(path):
            entries[w] = old[w] | {"said": said}
        else:
            x = dsp.level(say(said, speaker), lufs=-20, ceiling=-1)
            encode(x, path)
            entries[w] = {"file": slug(w), "seconds": round(len(x) / dsp.SR, 3), "said": said}
            print(f"  read {w!r}" + (f" as {said!r}" if said != w else ""))
        with open(path, "rb") as f:
            digest.update(f.read())
    for stale in os.listdir(BANK):
        if stale.endswith(".opus") and stale[:-5] not in {e["file"] for e in entries.values()}:
            os.remove(os.path.join(BANK, stale))
    label = dict(CANDIDATES).get(speaker, f"LibriTTS speaker {speaker}")
    with open(os.path.join(BANK, "bank.json"), "w") as f:
        f.write("// The clerk's vocabulary (tools/audio/clerk.py; note 238): each phrase or word, its take, and how long it runs.\n")
        json.dump({"speaker": speaker, "voice": label, "entries": entries}, f, indent=1, sort_keys=True)
        f.write("\n")
    with open(INDEX) as f:
        index = json.load(f)
    index["voice-clerk/bank"] = {
        "cue": "voice-clerk.bank",
        "picked": f"speaker{speaker}",
        "candidates": [{"label": f"The yard clerk: {label}, every word of the manifest and the tally (synthesised stand-in)",
                        "key": f"speaker{speaker}", "sources": ["Piper, LibriTTS en-us high (rhasspy/piper v0.0.2)"],
                        "licence": "CC BY 4.0: LibriTTS, Zen et al. 2019"}],
        "takes": len(entries),
        "sha": digest.hexdigest()[:16],
    }
    with open(INDEX, "w") as f:
        json.dump(dict(sorted(index.items())), f, indent=1)  # as install.py writes it
    print(f"{len(entries)} entries, speaker {speaker}, {sum(e['seconds'] for e in entries.values()):.1f} s -> {BANK}")


def previews(out):
    """A manifest and a tally in each candidate's voice, as the game would string them (radio band, crushed), for the page."""
    os.makedirs(out, exist_ok=True)
    lines = ["Yard to consist. Manifest follows.", "Crew: Okafor.", "Crew: Halloran.", "Coal: four hundred twelve.",
             "Powder and shot: forty.", "Cars: six.", "Gates open. Yard out.",
             "Yard clerk. Consist received. Tally follows.", "Cars delivered: five. Cargo: two thousand four hundred fifty.",
             "Reyes. Body recovered. Fee three hundred fifty. Refund two hundred sixty three.", "Net: one thousand one hundred fifty eight. Next."]
    for speaker, label in CANDIDATES:
        parts = []
        for line in lines:
            parts += [say(line.lower().rstrip("."), speaker), np.zeros(dsp.samples(0.45), np.float32)]
        x = np.concatenate(parts)
        x = dsp.band(x, 300, 3000)
        x = dsp.peak(x, 1800, 1, 4)
        x = dsp.crush(dsp.saturate(x, 4), bits=8, down=4)
        x = dsp.level(x, lufs=-18, ceiling=-1)
        dsp.write_preview(os.path.join(out, f"clerk-speaker{speaker}.mp4"), x)
        dsp.write_wav(os.path.join(out, f"clerk-speaker{speaker}.wav"), x)
        print(f"speaker {speaker} ({label}): {len(x) / dsp.SR:.1f} s")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--speaker", type=int, default=CANDIDATES[0][0])
    ap.add_argument("--previews", metavar="DIR")
    ap.add_argument("--fresh", action="store_true", help="read every take again, not only what's new")
    a = ap.parse_args()
    if a.previews:
        previews(a.previews)
    else:
        build(a.speaker, a.fresh)
