"""A bot's answer to a ping spoken (queue #687, note 987; Finn's note 936: "The answer is shown, not spoken: a bot's 'on it'
voice is the audio chat's (AU1)"; the director, 20 Oct: "oh, the spur? Yeah, of course, you want me to throw the spur. Oh,
an abandoned house? Sure, I'll go search it"), as tools/audio/cues.py lists it and GameAudio.Pings plays it: the crewmate
bot who takes a ping says so over the radio every uniform has (note 749), as the ping itself was heard (note 777).

What it is decides the build: a crewmate's quick, easy "on it", not the clerk's flat reading. Each cue is one answer
(Pings.Answer's: the fire, the controls, the loot, the crane, the house, the spur, the points, the cargo), and its takes
are the bots' voices in order, so a bot keeps one voice all night (GameAudio picks the take by the bot's id): four of
Piper's LibriTTS speakers (CC BY 4.0, "LibriTTS, Zen et al. 2019"), two low and two higher, none of them the clerk's.
Read with more life than the clerk (Piper's noise scales up), quick.

Two candidates: through the uniform's radio (the voice-radio chain's band, 300 Hz-3 kHz lifted at 1.8 kHz, crushed as the
cheap set is, the key's click and the squelch's tail), and plain (the voice as said, close and dry), for the director's ear.
"""

import os

import numpy as np

import dsp
import synth
from dsp import samples, hp, lp, bp, env
from recipes import crew_kit as ck
from recipes.crew_kit import recipe

L_ = "voice-bot"
# The bots' voices, in take order (a bot's is its id's place, mod four): Piper LibriTTS speaker ids, by their median pitch
# reading "Sure, I'll go search it." (106, 140, 199 and 223 Hz). Never 385, 518 or 329, the clerk's and his candidates.
VOICES = [561, 602, 77, 408]
# What each answer is, said (Pings.Answer: "ON THE SPUR", "SEARCHING THE HOUSE"), a crewmate's way.
SAID = {
    "fire": "On the fire!",
    "controls": "I've got the controls.",
    "loot": "I'll fetch it.",
    "crane": "On the crane.",
    "town": "Sure, I'll go search it.",
    "spur": "The spur? On it.",
    "points": "Throwing the points.",
    "cargo": "Loading her up.",
}
MODEL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "out", "audio", "tts", "en-us-libritts-high.onnx")
_voice = None


def said(text, speaker):
    """One answer read with a crewmate's ease: Piper's noise scales up from the clerk's flat 0.35/0.4, a touch quick."""
    global _voice
    from piper import PiperVoice, SynthesisConfig
    if not os.path.exists(MODEL):
        raise SystemExit(f"no voice model at {MODEL} (tools/audio/README.md)")
    if _voice is None:
        _voice = PiperVoice.load(MODEL, config_path=MODEL + ".json")
    cfg = SynthesisConfig(speaker_id=speaker, length_scale=0.9, noise_scale=0.6, noise_w_scale=0.7)
    a = np.concatenate([c.audio_float_array for c in _voice.synthesize(text, syn_config=cfg)])
    x = dsp.resample(a.astype(np.float32), dsp.SR / 22050)
    x = dsp.trim_silence(hp(x, 80), db=-42, pad=0.015)
    return dsp.fade(x, 0.004, 0.03)


def crush(x, bits=8, every=4):
    """The cheap set's crush (spec A.6, voice-radio's 8 bits at 11 kHz): held samples and coarse steps."""
    y = np.repeat(x[::every], every)[:len(x)]
    q = 2.0 ** (bits - 1)
    return (np.round(y * q) / q).astype(np.float32)


def radio(x, rng):
    """Through the uniform's radio, as the voice-radio chain has a crewmate: the key's click, the band, the crush, a static
    floor under it while keyed, the squelch's tail when it's let go."""
    v = hp(hp(x, 300, 2), 300, 2)
    v = lp(lp(v, 3000, 2), 3000, 2)
    v = dsp.peak(v, 1800, 1.0, 4)
    v = crush(ck.norm(v) * 0.9)
    click = synth.click(f=rng.uniform(2200, 2600), q=6, length=0.015, rng=rng)
    L = len(v) / dsp.SR
    floor = bp(synth.noise(L + 0.1, rng), 1200, 3000, 2) * 0.02
    tail = 0.12
    squelch = bp(synth.noise(tail, rng), 1500, 3500, 2) * env([(0, 0.3), (tail, 0)], tail, "exp")
    pre = 0.05
    y = np.zeros(samples(pre + L + 0.03 + tail), np.float32)
    y[:len(click)] += ck.norm(click) * 0.25
    y[samples(pre - 0.02):samples(pre - 0.02) + len(floor)][:len(y) - samples(pre - 0.02)] += floor[:len(y) - samples(pre - 0.02)]
    y[samples(pre):samples(pre) + len(v)] += v
    a = samples(pre + L + 0.03)
    y[a:a + len(squelch)] += squelch[:len(y) - a]
    return dsp.fade(y, 0.002, 0.02)


def answer(cue, real_radio):
    def make(rng, k):
        x = said(SAID[cue], VOICES[k % len(VOICES)])
        return radio(x, rng) if real_radio else x
    return make


for _cue in SAID:
    recipe(L_, _cue, "radio", f"A bot's answer over the radio: \"{SAID[_cue]}\"",
           f"""Said by Piper's LibriTTS voices (CC BY 4.0), one a take, in the bots' order (speakers {", ".join(map(str, VOICES))}),
           read easy and quick: "{SAID[_cue]}". Through the uniform's radio as the voice-radio chain has a crewmate: the key's
           click, 300 Hz-3 kHz lifted at 1.8 kHz, crushed to the cheap set's 8 bits, a static floor while keyed, the squelch's
           tail.""", takes=len(VOICES), lufs=-20)(answer(_cue, True))
    recipe(L_, _cue, "plain", f"A bot's answer, plain: \"{SAID[_cue]}\"",
           f"""Said by Piper's LibriTTS voices (CC BY 4.0), one a take, in the bots' order: "{SAID[_cue]}". As said, close and
           dry, nothing of the radio on it.""", takes=len(VOICES), lufs=-20)(answer(_cue, False))
