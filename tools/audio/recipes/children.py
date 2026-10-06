"""The Soot Children's call (GDD v1.1 App. A.6): a child calling for help out in the dark, near facilities and dead
towns. Half the time it's a real survivor and half a Soot Child, and they must sound the same: the call is the lure, the
eyes are the tell. So it's a plain, frightened child's voice with nothing uncanny in it.

Stand-ins until a child's voice is recorded: a neural text-to-speech model (Piper's LibriTTS voice, CC BY 4.0: credit
"LibriTTS, Zen et al. 2019"), an adult speaker made a child's size. A child's vocal tract is shorter, so its formants sit
higher: the voice is first pitched up with its formants moved with it (a smaller throat), then pitched up again with
the formants kept (a higher voice in that throat). Read slow and raised, as a child calls across a yard; a little open
air round it. The game puts it where the child is.
"""

import dsp
from build import recipe
from dsp import hp
from recipes.voices import tts

CALLS = ["Help!", "Help me!", "Is anybody there?", "Please! Help!"]

# Speakers whose voices are highest in LibriTTS (speakers.json: median pitch 345 and 276 Hz), not among the prisoners'
# voice sets (voices.py), so a child never sounds like a grown prisoner.
GIRL, BOY = 140, 693


def child(x, rng, size, lift):
    """An adult voice made a child's: `size` semitones of smaller throat (formants and pitch together), then `lift` more
    of pitch alone; a raised voice's edge; a little open air."""
    y = dsp.shift(x, size + rng.uniform(-0.3, 0.3), formant=False)
    y = dsp.shift(y, lift + rng.uniform(-0.5, 0.5), formant=True)
    y = dsp.peak(y, 3000, 1.0, 3)
    y = dsp.compress(y, -22, 3, 0.005, 0.12)
    y = hp(y, 200, 2)
    y = dsp.lp(y, 7000, 2)    # the shift turns 's' and 'h' into a sizzle no child makes
    return dsp.room(y, "night", wet=0.1, rng=rng)


def _call_recipe(key, speaker, size, lift, desc):
    @recipe("tell-soot-children", "call", key, f"A {desc} calling for help (synthesised stand-in)",
            f"""LibriTTS speaker {speaker} through Piper (CC BY 4.0) saying 'help', 'help me', 'is anybody there' and
            'please, help', read slow and raised, made a child's size: pitched up {size} semitones with the formants
            moved (a smaller throat), then {lift} more with them kept (a higher voice), a raised voice's presence, a
            little open air. Plain and frightened, nothing uncanny: a real survivor's call and a Soot Child's are the
            same sound. A stand-in until a child's voice is recorded.""",
            takes=len(CALLS), band=(300, 3000), lufs=-20, gap=0.8)
    def call(rng, k, speaker=speaker):
        return child(tts(CALLS[k], speaker, rng, rate=1.05), rng, size, lift)


_call_recipe("girl", GIRL, 2.0, 2.0, "young girl")
_call_recipe("boy", BOY, 3.0, 3.0, "young boy")
