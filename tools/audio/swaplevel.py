#!/usr/bin/env python3
"""Level each of install.py's SWAPS to the synth it replaced: main tuned the mix against the synth's level, so the takes
go in at the same loudness. Renders both through the game's own mixer (`dt audio render --sound`, the synth definition
from tools/audio/synth-defs/ under a scratch name) and measures each (dsp.loudness), then prints the SWAP_GAIN_DB that
evens them, counting what's already in it.

  python3 tools/audio/swaplevel.py [sound ...]     # after an install; paste the result into install.py and install again

Loops are heard for 8 s; a param the game drives is held at a middle value (the gun's turn, the boiler half cold).
"""

import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dsp  # noqa: E402
from install import SWAP_GAIN_DB, SWAPS, SYNTH_DEFS, SOUNDS, _read_def  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "out", "audio", "swaplevel")
PARAMS = {"gun-lay": ["--param", "speed=0.6"], "boiler-tick": ["--param", "cool=0.3"]}


def render(sound, wav):
    loop = _read_def(os.path.join(SOUNDS, sound + ".json"))[1].get("loop")
    cmd = ["dotnet", "run", "--project", os.path.join(ROOT, "src", "DarkTerritory.Cli"), "--no-build", "--", "audio", "render",
           "--sound", sound, "--out", wav] + PARAMS.get(sound.removeprefix("_synth-"), []) + (["--seconds", "8"] if loop else [])
    subprocess.run(cmd, cwd=ROOT, check=True, capture_output=True)
    return float(dsp.loudness(dsp.load(wav)))


def main():
    os.makedirs(OUT, exist_ok=True)
    sounds = sys.argv[1:] or sorted({s for s, _ in SWAPS.values()})
    gains = dict(SWAP_GAIN_DB)
    for sound in sounds:
        backup = os.path.join(SYNTH_DEFS, sound + ".json")
        installed = _read_def(os.path.join(SOUNDS, sound + ".json"))[1]
        if not os.path.exists(backup) or not any(l.get("source") == "sample" for l in installed["layers"]):
            print(f"{sound:20s} still its synth: nothing to level")
            continue
        scratch = os.path.join(SOUNDS, f"_synth-{sound}.json")
        shutil.copy(backup, scratch)
        try:
            synth = render(f"_synth-{sound}", os.path.join(OUT, f"synth-{sound}.wav"))
        finally:
            os.unlink(scratch)
        takes = render(sound, os.path.join(OUT, f"takes-{sound}.wav"))
        gains[sound] = round(SWAP_GAIN_DB.get(sound, 0) + synth - takes)
        print(f"{sound:20s} synth {synth:6.1f}  takes {takes:6.1f}  ->  {gains[sound]:+d} dB")
    print("SWAP_GAIN_DB = " + json.dumps(dict(sorted(gains.items()))))


if __name__ == "__main__":
    main()
