"""The village houses' doors (queue #145, note 409; B4's note 401): a crewmate on foot holds Use at an open house's doorway
and its door swings shut, or open again. The sim flips the door at the end of the hold (World.DoorAct), host and client
alike, and the art pops it from hanging open to shut in the doorway, so each sound starts the moment it's worked.

The door: a ledged plank door of weathered deal on strap hinges with a thumb latch, swollen and sagging in its frame since
the village emptied (run.json's 0.6 s hold, "a swollen, sagging door"). Shut, it's the Choir's rule ("behind a closed
door", GDD §21), so the shut has to be heard and trusted from either side of it: a hard wooden bang with the sneck
dropping into its keeper last. The packs' real doors first (Kenney RPG's doorOpen and doorClose, sfx_100's door slam and
wood hits, the latches), pitched to a cottage's light plank door, which is lighter than a boxcar's and dryer than a car's
end door (no car's hollow under it: the house's small room is the game's own space, spaces.json `room`). The packs'
creaks are real wooden creaks; a swollen door scraping its sill is stick-slip friction on wood (synth.creak through deal).
"""

import numpy as np

import dsp
import synth
from dsp import samples, hp, lp, env, mix
from recipes import crew_kit as ck
from recipes.crew_kit import recipe, R, S, K
from recipes.crew_items import hit_of

L = "crew-house-door"
OPENS = R("doorOpen_1", "doorOpen_2")
CLOSES = R("doorClose_1", "doorClose_3", "doorClose_4")
SLAM = S("door_03")[0]
CREAKS = R("creak1", "creak2", "creak3")
LATCHES = S("door_02", "door_04", "door_05") + R("metalLatch")
KNOCKS = ck.WOOD_KNOCKS
PLANK = K("impactPlank_medium")
WOOD_H = K("impactWood_heavy")
DEAL = [310, 520, 870, 1340]                      # a thin plank door's modes: light, boxy


def dry(y):
    """A whisker of the doorway's own reflections (the jambs, the floor): the room's reverb is the game's."""
    return ck.space(y, 0.12, 0.08, 6000, ((0.003, 0.4), (0.007, 0.25)))


def sneck(rng, take, semis=0.0):
    """The thumb latch's bar dropping over its keeper: the packs' latch click, a little lower for a cottage's iron."""
    k = LATCHES[take % len(LATCHES)]
    return dsp.vari(hit_of(k, 0, 0.15, -16), semis - 2 + rng.uniform(-0.6, 0.6))


def creak(rng, take, length, semis=-3.0):
    """A strap hinge turning: one of the packs' real wooden creaks, dropped for an old hinge and cut to `length`."""
    c = ck.align(ck.get(CREAKS[take % len(CREAKS)]))
    c = dsp.vari(c, semis + rng.uniform(-0.8, 0.8))
    c = c[:samples(length)] if len(c) > samples(length) else c
    return ck.norm(hp(c * env([(0, 1), (len(c) / dsp.SR * 0.75, 1), (len(c) / dsp.SR, 0)], len(c) / dsp.SR), 180))


def sill(rng, length, start=40.0, end=120.0):
    """A swollen door's foot dragging over the sill: stick-slip on wood, slow and catching, through deal."""
    s = synth.creak(length, env([(0, start), (length, end)], length, "exp"), rng, body=[f * rng.uniform(0.9, 1.1) for f in DEAL],
                    q=8, jitter=0.45, grit=0.5)
    return ck.norm(hp(s, 150)) * env([(0, 0), (0.03, 1), (length * 0.8, 0.8), (length, 0)], length)


def bang(rng, take, semis=-1.0):
    """The leaf into its frame: the packs' door slam, its first hit, brought up to a light plank door."""
    x = dsp.vari(hit_of(SLAM, take % 2, 0.5), semis + rng.uniform(-0.5, 0.5))
    return ck.norm(ck.tilt(x, lo_db=-2, hi_db=2, lo_f=200))


@recipe(L, "shut", "sag", "A swollen house door pushed shut: the sag dragged over the sill, the bang, the sneck dropping",
        """Hands on a ledged plank door that's swollen in its frame: the hinge creaks (the packs' real wooden creak, dropped
        for an old strap hinge), its foot drags over the sill (stick-slip on wood through thin deal), then it's shoved home
        with a hard wooden bang (the packs' door slam, first hit, a light door's) and the thumb latch's bar drops into its
        keeper last: the confirmation, heard and trusted from either side. The loose planks rattle once.""",
        sources=CREAKS + [SLAM] + LATCHES + KNOCKS, takes=3, lufs=-17)
def shut_sag(rng, k):
    drag = 0.16 + rng.uniform(0, 0.05)
    rattle = ck.grains(rng, 5, 0.1, 400, 2400, q=(6, 12), length=(0.015, 0.04))
    return dry(ck.place([(0, creak(rng, k, 0.22), -10), (0.02, sill(rng, drag), -12),
                         (drag + 0.02, bang(rng, k), 0), (drag + 0.02, ck.floor(rng, "wood", k, 1.0, 0.7), -9),
                         (drag + 0.05, rattle, -18), (drag + 0.09 + rng.uniform(0, 0.03), sneck(rng, k), -5)]))


@recipe(L, "shut", "pull", "A house door pulled to: a short swing, a dull wooden thump into the frame, the latch",
        """The packs' real door closing (Kenney RPG doorClose takes) given a plank door's dull weight (its low knock, a heavy
        wood hit under it, the swollen leaf stopping dead in its frame), and the thumb latch dropping as it seats.""",
        sources=CLOSES + WOOD_H + LATCHES, takes=3, lufs=-17)
def shut_pull(rng, k):
    c = ck.align(ck.get(CLOSES[k % len(CLOSES)]))
    c = ck.tilt(dsp.vari(c, -1 + rng.uniform(-0.5, 0.5)), lo_db=4, lo_f=300)
    thud = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(WOOD_H[(k + 1) % 5]), 1 + rng.uniform(-0.5, 0.5)))), 0.04, 0.05)
    first = ck.hits(c, floor_db=-20, gap=0.03)
    at = first[0][0] / dsp.SR if first else 0.0
    return dry(ck.place([(0, c, 0), (at, thud, -6), (at + 0.07 + rng.uniform(0, 0.03), sneck(rng, k + 1), -6)]))


@recipe(L, "open", "creak", "A house door opened: the sneck lifted, the leaf jerked free of its swollen frame, a long creak in",
        """The thumb latch pressed and its bar lifted (the packs' latch), the leaf jerking free of the swollen frame with a
        wooden knock, then swinging in on its old strap hinges with a long creak (the packs' real wooden creak, dropped, with the
        leaf's weight groaning under it: stick-slip through deal) and bumping the room's wall as it comes round past square.""",
        sources=LATCHES + WOOD_H + CREAKS + PLANK, takes=3, lufs=-20)
def open_creak(rng, k):
    swing = 0.55 + rng.uniform(0, 0.25)
    free = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(WOOD_H[(k + 2) % 5]), 3 + rng.uniform(-0.5, 0.5)))), 0.03, 0.04)
    bump = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLANK[k % 5]), 3 + rng.uniform(-0.5, 0.5)))), 0.02, 0.05)
    c = creak(rng, k + 1, swing)
    swing = len(c) / dsp.SR
    # Under the hinge's squeal, the leaf's own weight on it: a slow stick-slip groan through deal, easing as it swings.
    groan = synth.creak(swing, env([(0, 18), (swing, 40)], swing), rng, body=DEAL, q=12, jitter=0.35)
    groan = ck.norm(hp(groan, 120)) * env([(0, 0), (0.04, 1), (swing, 0.3)], swing)
    return dry(ck.place([(0, sneck(rng, k + 2, 1), -6), (0.06, free, -6), (0.09, c, -3), (0.09, groan, -12),
                         (0.09 + swing + 0.02, bump, -14)]))


@recipe(L, "open", "door", "A house door opened: the latch, a short hinge creak, the door swinging in",
        """The packs' real door opening (Kenney RPG doorOpen: latch and hinge creak) pitched down a little for a heavier
        plank door, with the leaf's foot catching the sill once as it starts (stick-slip on wood).""",
        sources=OPENS + LATCHES, takes=2, lufs=-20)
def open_door(rng, k):
    o = ck.align(ck.get(OPENS[k % 2]))
    o = ck.choke(dsp.vari(o, -2 + rng.uniform(-0.5, 0.5)), 0.7, 0.15)
    return dry(ck.place([(0, o, 0), (0.12, sill(rng, 0.09, 60, 90), -16)]))
