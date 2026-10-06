"""Where the source recordings are, and how a recipe names one.

Sources live outside the repo (they're fetched, never committed): under $DT_AUDIO_SRC, default out/audio/src, one folder
per pack, as tools/audio/fetch.py unpacks them. A recipe names a file as "pack:name":

  sfx_100_v2:misc_24              -> sfx_100_v2/sfx100v2_misc_24.ogg
  kenney_impact-sounds:impactWood_heavy_002 -> kenney_impact-sounds/Audio/impactWood_heavy_002.ogg
  kenney_rpg-audio:creak1         -> kenney_rpg-audio/Audio/creak1.ogg
  sonniss:<path inside the bundle folder>   -> sonniss/<path> (the picks fetch.py pulled out of the GDC bundles)

The four small packs are CC0. The Sonniss GDC bundles are royalty-free for use in a game but not CC0: they may be built
into the game's sounds, and the checklist page only ever gets trimmed, processed previews of them (build.py), never the
originals.
"""

import functools
import glob
import os

import dsp

ROOT = os.environ.get("DT_AUDIO_SRC") or os.path.join(os.path.dirname(__file__), "..", "..", "out", "audio", "src")

PACKS = {
    "sfx_100_v2": ("sfx_100_v2", "sfx100v2_{}.ogg", "CC0"),
    "kenney_impact-sounds": ("kenney_impact-sounds/Audio", "{}.ogg", "CC0"),
    "kenney_rpg-audio": ("kenney_rpg-audio/Audio", "{}.ogg", "CC0"),
    "kenney_ui-audio": ("kenney_ui-audio/Audio", "{}.ogg", "CC0"),
    "sonniss": ("sonniss", "{}", "Sonniss GDC (royalty-free, not CC0: previews only on the page)"),
}


def path(key):
    pack, name = key.split(":", 1)
    folder, pat, _ = PACKS[pack]
    p = os.path.join(ROOT, folder, pat.format(name))
    if not os.path.exists(p):
        hits = glob.glob(os.path.join(ROOT, folder, "**", pat.format(name)), recursive=True)
        if not hits:
            raise FileNotFoundError(f"{key}: not under {os.path.join(ROOT, folder)} (run tools/audio/fetch.py)")
        p = hits[0]
    return p


@functools.lru_cache(maxsize=512)
def _load(key):
    return dsp.load(path(key))


def get(key):
    """The source as mono float32 at 48 kHz (a copy: recipes are free to change it)."""
    return _load(key).copy()


def licence(key):
    return PACKS[key.split(":", 1)[0]][2]


def is_restricted(key):
    """True for sources that are not CC0 (their raw audio never goes on the page)."""
    return key.startswith("sonniss:")


def S(*names):
    return [f"sfx_100_v2:{n}" for n in names]


def K(name, takes=range(5)):
    return [f"kenney_impact-sounds:{name}_{i:03d}" for i in takes]


def R(*names):
    return [f"kenney_rpg-audio:{n}" for n in names]
