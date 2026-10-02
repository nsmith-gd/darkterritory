"""Prisoner voice sets (D.7, decided 1 Oct): each prisoner in a Holdout is one person to the ear, calling for help in a
voice of their own for the whole run; adults only, so a call is never mistaken for the Soot Children; at least eight
sets, so a full crew never shares one.

Stand-ins until the voices are recorded: a neural text-to-speech model (Piper's LibriTTS voice, 904 speakers from public
domain audiobooks, CC BY 4.0: credit "LibriTTS, Zen et al. 2019") speaking each line, pushed toward a shout (faster,
higher, strained and bright) and put in a barricaded room. The game muffles it further through the Holdout's walls.
Needs the model in out/audio/tts (tools/audio/README.md); a recipe without it fails the build, it never fakes a voice.
"""

import os

import numpy as np

import dsp
from build import recipe
from dsp import samples, hp, lp, Bus

TTS = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "out", "audio", "tts"))
MODEL = os.path.join(TTS, "en-us-libritts-high.onnx")
CALLS = ["Help!", "In here!", "Over here!", "Somebody, help us!", "We're in here! Please!"]
SHOUTS = ["Hey!", "Hello?!", "Out here! Hey!"]

# Eight speakers chosen for distinct, adult voices: four low, four high, spread in timbre (speakers.json: median pitch
# and brightness of each LibriTTS speaker saying a test line). (speaker id, short description)
SETS = []

_voice = None


def tts(text, speaker, rng, rate=0.82):
    global _voice
    from piper import PiperVoice, SynthesisConfig
    if _voice is None:
        _voice = PiperVoice.load(MODEL, config_path=MODEL + ".json")
    cfg = SynthesisConfig(speaker_id=speaker, length_scale=rate * rng.uniform(0.95, 1.05), noise_scale=0.75,
                          noise_w_scale=0.9)
    a = np.concatenate([c.audio_float_array for c in _voice.synthesize(text, syn_config=cfg)])
    return dsp.resample(a.astype(np.float32), dsp.SR / 22050)


def shout(x, rng, lift=2.5):
    """A calm reading pushed toward a shout: pitch up (formants kept, it's the same person), strain, presence, the hard
    edge of a raised voice; then a barricaded room."""
    y = dsp.shift(x, lift + rng.uniform(-0.5, 0.5), formant=True)
    y = dsp.peak(y, 2800, 1.2, 5)
    y = dsp.saturate(dsp.compress(y, -20, 4, 0.005, 0.12), 6)
    y = hp(y, 140, 2)
    return dsp.room(y, "stone", wet=0.18)


def _set_recipe(n, speaker, desc):
    @recipe("voice-prisoner-sets", "call", f"set{n}", f"Prisoner {n}: {desc}, calling for help (synthesised stand-in)",
            f"""LibriTTS speaker {speaker} through Piper (CC BY 4.0), saying 'help', 'in here', 'over here' and two longer
            calls, read fast and pushed toward a shout: pitched up with the formants kept, compressed and driven for strain,
            a presence lift, in a small stone room. The same person in every take. A stand-in until the voices are
            recorded.""", takes=len(CALLS), lufs=-20, gap=0.5)
    def call(rng, k, speaker=speaker):
        return shout(tts(CALLS[k], speaker, rng), rng)

    @recipe("voice-prisoner-sets", "shout", f"set{n}", f"Prisoner {n}: {desc}, a raw shout (synthesised stand-in)",
            f"""The same LibriTTS speaker {speaker}: 'hey', 'hello' and 'out here, hey', faster and harder than the calls,
            pushed further into a shout.""", takes=len(SHOUTS), lufs=-20, gap=0.5)
    def yell(rng, k, speaker=speaker):
        return shout(tts(SHOUTS[k], speaker, rng, rate=0.75), rng, lift=4.0)


for _n, (_sp, _desc) in enumerate(SETS, 1):
    _set_recipe(_n, _sp, _desc)
