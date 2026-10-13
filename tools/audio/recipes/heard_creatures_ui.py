"""Pain sounds for five creatures (note 290: a ball or a blow landing and not killing), and the interface's new paperwork:
the film's skip, the panels, a name typed and a crew deleted (notes 315, 316, 320).

The pain cues are each creature's own voice, built from the same parts as everything it already makes, so a hit sounds
like the same body: the Gaunt, the Soot Children and the Switchman from recipes/horrors.py, the Grumbler and the
Followers from recipes/beasts.py. The weapon's impact is not in them (a ball has crew-cannon's flesh, a tool its own
blow): each take carries only what the creature's own body does when it's struck (bark knocked, soot shaken out, a lamp
jerked on its chain, skin smacked, a shield knocked) and then its answer.

- The Gaunt: dry bark on stilts, silent until struck. Its grunt is air knocked out of a hollow chest up a neck longer
  than a giant's, or the trunk itself groaning; its ribs knock like dry staves.
- The Soot Children: no child's voice at all, only breath: a puff of soot knocked out of it and a hiss through teeth
  from a mouth that grows as it hisses.
- The Switchman: its oil lamp jerked on its chain (links, tin, the glass chimney in its gallery, the flame ducking) and
  its spindly half's thin reed of a throat forced into a shriek through cracked glass.
- The Grumbler: a man gone wrong, affronted: a bark or a snort through split cheeks, his muttering, before he turns.
- Followers: a hand-sized tick: its shield knocked, the sac sloshing, and a squeal from something with no throat.

The interface is the railway's paperwork (recipes/ui.py, and the run-end report's stamps and typewriter in
recipes/mishaps_world.py): a projector for the film, card and ledger for the panels, the report's own typewriter for a
name, and the report's rubber stamp, heavier, for a crew deleted. Dry and close, cut short, levelled like their
neighbours.
"""

import numpy as np

import dsp
import src
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import beasts as B
from recipes import crew_kit as ck
from recipes import horrors as H
from recipes import kit
from recipes import mishaps_world as MW
from recipes.crew_items import hit_of
from recipes.ui import tidy, cut as ui_cut

SR = dsp.SR


# ---- The Gaunt ----------------------------------------------------------------------------------------------------------
# Its blow and its death (horrors.py) are hollow bark, dry cracks and a thin hiss through needle teeth. Struck and not
# killed, it grunts: low, short, dry, from a body far too big for the breath in it.

NECK = 0.3            # a vocal tract longer than synth's giant (0.36): two metres of neck up to a horse skull
WOOD_HEAVY = H.K("impactWood_heavy")


def bark_struck(rng, weight=1.0):
    """The blow meeting its starved bark body: a hollow trunk knocked, a heavy wood impact pitched down into it, bark
    crumbs knocked loose. Kept light: the weapon brings its own impact."""
    b = Bus(1.0)
    b.at(0, H.knock(rng, rng.uniform(100, 130), 0.4, q=24), -2)
    b.at(0, H.rec(rng, WOOD_HEAVY, rng.uniform(-6, -3), 0.25), -7)
    b.at(0.003, kit.thud(rng, rng.uniform(48, 58), 0.3, weight), -14)
    b.at(0.02, H.bark_debris(rng, 0.6, 50), -20)
    return b.x


def ribs(rng, count, t=0.0):
    """Its ribs knocking together after the blow: dry hollow staves, much smaller than its trunk, in a rattle that
    settles, each knock a little later and softer."""
    b = Bus(1.0)
    for i in range(count):
        b.at(t, H.knock(rng, rng.uniform(260, 520), 0.1, q=28), -4 - 2.2 * i + rng.uniform(-2, 1))
        b.at(t, kit.snap(rng, 0.03, rng.uniform(1600, 2600)), -20 - 2 * i)
        t += rng.uniform(0.016, 0.04) * (1 + 0.3 * i)
    return b.x


def chest_grunt(rng, length, f0, catch=0.0):
    """Air knocked out of its hollow chest up the long neck: a low voice that is more breath than tone (a growl's fry
    gating breath and throat together), through a tract longer than a giant's, rung in the hollow trunk it comes out of.
    `catch` splits the breath in two where the chest catches (0 none, 1 a full stop)."""
    pts = [(0, f0 * 1.2), (0.04, f0), (length, f0 * 0.68)]
    g = synth.glottis(env(pts, length, "exp"), length, rng, jitter=0.04, shimmer=0.35, sub=0.7, rough=0.5, oq=0.45)
    fr = B.fry(rng, env([(0, 34), (length, 24)], length), length, jitter=0.4, decay=0.01)
    x = g * (0.35 + 0.65 * fr) + synth.noise(length, rng) * fr * 0.55
    v = synth.tract(x, [(0, "a"), (length * 0.4, "o"), (length, "u")], NECK, breath=0.6, rng=rng)
    trunk = dsp.resonate(v, [f * rng.uniform(0.55, 0.65) for f in synth.BARK], q=9, gains=[1, 0.8, 0.5, 0.3, 0.2])
    rasp = bp(synth.noise(length, rng), 450, 2400) * fr     # the dry air scraping out, in the same pulses
    y = H.norm(v) * 0.6 + H.norm(trunk) * 0.7 + H.norm(rasp) * 0.3
    a = rng.uniform(0.35, 0.45) * length
    c = 1 - catch
    shape = [(0, 0), (0.012, 1), (a, 0.75), (a + 0.015, 0.75 * c), (a + 0.05, 0.75 * c), (a + 0.08, 0.8), (length, 0)]
    return lp(y * env(shape, length), 5000)


def trunk_groan(rng, length, rate):
    """A grunt with no throat in it: the trunk itself groaning under the blow, a stick-slip at a voice's fry rate rung
    through a hollow log's modes and shaped by the long neck's vowels, the breath going out through the needle teeth."""
    rates = env([(0, rate * 1.15), (0.04, rate), (length, rate * 0.5)], length)
    c = synth.creak(length, rates, rng, body=[f * 0.5 for f in synth.BARK], q=11, jitter=0.2, grit=0.6)
    v = synth.tract(c, [(0, "o"), (length, "u")], NECK, breath=0.25, rng=rng)
    shape = env([(0, 0), (0.01, 1), (length * 0.45, 0.7), (length, 0)], length)
    breath = H.needle_breath(rng, length * 0.9)
    return mix(H.norm(c * 0.5 + v * 0.8) * shape, H.norm(breath) * 0.12)


@recipe("cs-gaunt", "hit", "chest",
        "Struck: a dry, hollow grunt knocked out of its chest up the long neck, its ribs knocking like dry staves",
        """Its body is hollow bark, so the blow knocks a hollow trunk (a burst rung through a hollow log's modes, a
        heavy wood impact pitched down, bark crumbs), kept light because the weapon brings its own impact. The grunt is
        the air knocked out of it: a synthesised throat far down at 45-60 Hz, more breath than tone (a growl's fry
        gating both), through a tract longer than a giant's for the two-metre neck and rung in the trunk. Its ribs
        knock together after, dry staves settling. Takes: one grunt, one that catches in two, one deeper with a bigger
        rattle.""",
        sources=WOOD_HEAVY, takes=3)
def gaunt_hit_chest(rng, k):
    L = (0.34, 0.44, 0.27)[k] * rng.uniform(0.92, 1.08)
    f0 = (56, 50, 45)[k] * rng.uniform(0.95, 1.05)
    b = Bus(1.2)
    b.at(0, bark_struck(rng, 1.3 if k == 2 else 1.0), 0)
    b.at(0.008, chest_grunt(rng, L, f0, catch=0.85 if k == 1 else 0.0), -1)
    b.at(rng.uniform(0.02, 0.04), ribs(rng, (4, 3, 6)[k]), -3)
    return H.finish(hp(dsp.room(b.x, "car", wet=0.1, rng=rng), 35))


@recipe("cs-gaunt", "hit", "trunk",
        "Struck: the dry bark trunk itself groaning a short grunt, breath out through needle teeth, ribs knocking",
        """No throat at all: what grunts is the wood. The hollow trunk is knocked (a burst through a hollow log's
        modes, a heavy wood impact pitched down, bark crumbs, light: the weapon has its own impact), and the trunk
        groans under it, a stick-slip at a voice's fry rate (55-70 slips a second sagging to half) through the log's
        modes and shaped by the long neck's 'oh' to 'oo', with its only mouth sound under it: a thin dry hiss out
        through the needle teeth. Its ribs knock after like dry staves. Takes: a grunt, a longer one with a joint
        cracking, a short hard one.""",
        sources=WOOD_HEAVY, takes=3)
def gaunt_hit_trunk(rng, k):
    L = (0.36, 0.48, 0.24)[k] * rng.uniform(0.92, 1.08)
    b = Bus(1.2)
    b.at(0, bark_struck(rng, 1.3 if k == 2 else 1.0), 0)
    b.at(0.006, trunk_groan(rng, L, (62, 55, 70)[k] * rng.uniform(0.95, 1.05)), -1)
    b.at(rng.uniform(0.02, 0.04), ribs(rng, (3, 4, 5)[k]), -4)
    if k == 1:     # a knee joint cracking as it takes the blow
        b.at(L * 0.6, kit.snap(rng, 0.08, rng.uniform(800, 1200)), -10)
    return H.finish(hp(dsp.room(b.x, "car", wet=0.1, rng=rng), 35))


# ---- The Soot Children --------------------------------------------------------------------------------------------------
# Wordless, as in horrors.py: a child's breath through a throat that grows wrong, soot and ash. No cry: a struck child
# crying would be the call, and the call is the lure. No crackle riding on the hiss either (that reads as a fizz).

def soot_puff(rng, weight=1.0):
    """The blow on a small soot-black body: a light thud through its rags, and a puff of soot knocked out of it (a soft
    dark breath of air with the dust in it), then a fine sift of soot falling after."""
    b = Bus(1.2)
    b.at(0, kit.thud(rng, rng.uniform(90, 105), 0.18, 0.5 * weight), -8)
    b.at(0, H.rec(rng, H.CLOTH, rng.uniform(-3, 0), 0.25), -12)
    L = 0.45
    air = lp(synth.noise(L, rng, "pink"), 1400) * env([(0, 0), (0.004, 1), (0.07, 0.35), (L, 0)], L)
    b.at(0.003, H.norm(air), 0)
    b.at(0.006, B.ash(rng, 0.3, 500, 5000), -9)
    S = 0.9
    b.at(0.2, H.soot(rng, S, env([(0, 220), (S, 8)], S)), -18)
    return b.x


def teeth_hiss(rng, length, k_to=0.5, inhale=False, tremble=14.0):
    """It hisses back: breath forced between a child's teeth (noise through a child's mouth on 'ee', the teeth's own
    hiss over it), the mouth behind it growing into something bigger as the breath goes on, trembling. Unvoiced."""
    shape = [(0, 0), (length * 0.8, 1), (length, 0)] if inhale else [(0, 0), (0.02, 1), (length * 0.4, 0.8), (length, 0)]
    k = env([(0, 1.0), (length, k_to)], length)
    mouth = H.airflow(rng, length, [(0, "i"), (length * 0.6, "e"), (length, "h")], k, shape)
    teeth = bp(synth.noise(length, rng), 3800, 9000) * env(shape, length)
    y = mix(H.norm(hp(mouth, 600)), H.norm(teeth) * 0.45)
    return dsp.tremolo(H.norm(y), tremble, 0.3, rng, jitter=0.4)


def char_crack(rng):
    """Its blackened body cracking like burnt wood: a charcoal crunch, pitched up a little and darkened."""
    return lp(H.rec(rng, H.CHAR, rng.uniform(2, 4), 0.2), 4500)


@recipe("cs-soot-children", "hit", "puff",
        "Struck: a puff of soot knocked out of it, and a long dry hiss through a child's teeth from a mouth that grows",
        """No child's cry. The blow knocks a small body through its rags (a light thud, cloth) and a puff of soot out
        of it: a soft dark breath of air with ash in it, a fine sift of soot falling after. Then it hisses back, dry:
        breath forced through a child's teeth (noise through a child's mouth on 'ee', the teeth's own hiss on top),
        while the mouth behind it lengthens toward a beast's, so the hiss deepens as it goes, trembling. No crackle on
        the hiss, so it never reads as a fizz. Takes differ in the hiss's length and how far the mouth grows.""",
        sources=H.CLOTH, takes=3)
def soot_hit_puff(rng, k):
    b = Bus(1.8)
    b.at(0, soot_puff(rng), 0)
    L = (0.6, 0.8, 0.45)[k] * rng.uniform(0.9, 1.1)
    b.at(rng.uniform(0.15, 0.2), teeth_hiss(rng, L, (0.55, 0.45, 0.65)[k]), -2)    # a beat, then it answers
    return H.finish(dsp.room(b.x, "car", wet=0.1, rng=rng))


@recipe("cs-soot-children", "hit", "rattle",
        "Struck: its charred body cracking in a puff of soot, a sharp hiss drawn in through its teeth, out with a rattle",
        """No child's cry. The blow cracks its blackened body like burnt wood (a stone crunch pitched up and
        darkened, as its death crumbles) in a puff of soot. It draws a sharp hiss in through its teeth (a child's mouth
        on 'ee', quick and rising), then lets it out through a throat far too long for it, with a dry rattle low in it
        (a 50-60 Hz fry under the breath, as when it turns). Takes: in and out, a longer rattle, a quick one.""",
        sources=H.CLOTH + H.CHAR, takes=3)
def soot_hit_rattle(rng, k):
    b = Bus(1.8)
    b.at(0, soot_puff(rng, 0.8), -1)
    b.at(0.002, char_crack(rng), -5)
    Li = (0.22, 0.26, 0.16)[k] * rng.uniform(0.9, 1.1)
    t = rng.uniform(0.07, 0.11)
    b.at(t, teeth_hiss(rng, Li, 0.9, inhale=True, tremble=9.0), -4)
    t += Li + rng.uniform(0.02, 0.05)
    Lo = (0.45, 0.65, 0.3)[k] * rng.uniform(0.9, 1.1)
    out = teeth_hiss(rng, Lo, 0.4, tremble=11.0)
    f0 = env([(0, rng.uniform(55, 62)), (Lo, rng.uniform(44, 50))], Lo)
    rattle = H.throat(rng, f0, "h", 0.45, breath=0.7, rough=0.8, sub=0.6, jitter=0.03, shimmer=0.3)
    rattle = rattle * env([(0, 0), (0.04, 0.8), (Lo * 0.7, 0.6), (Lo, 0)], Lo)
    b.at(t, mix(out, H.norm(rattle) * 0.5), -3)
    return H.finish(dsp.room(b.x, "car", wet=0.1, rng=rng))


# ---- The Switchman ------------------------------------------------------------------------------------------------------
# Its lamp is an oil lamp (horrors.py): tin, a glass chimney in its gallery, a flame. Its voice is two throats, a man's
# and a thin reedy one; struck, the reed goes up into a shriek.

CHAIN = ["sfx_100_v2:misc_09", "sfx_100_v2:wood_03"]       # keys jangling: the lamp's chain, pitched into heavier links
RING = ["sfx_100_v2:glass_05", "sfx_100_v2:glass_02"]       # struck glass that rings on


def lamp_jerked(rng, length=0.55, hard=1.0):
    """Its lamp jerked on its chain by the blow: the links jangling, the tin lamp knocking against them, the glass
    chimney chattering in its gallery, the flame ducking and catching again with a soft whump."""
    b = Bus(length + 0.3)
    ch = dsp.vari(dsp.trim_silence(src.get(CHAIN[int(rng.integers(2))]), -40, 0.002), rng.uniform(-5, -2))
    b.at(0, H.cut(ch, length), -2 + 2 * (hard - 1))
    b.at(rng.uniform(0.0, 0.02), H.rec(rng, H.TIN, rng.uniform(-2, 1), 0.12), -7)
    b.at(rng.uniform(0.18, 0.3), H.rec(rng, H.TIN, rng.uniform(1, 4), 0.1), -13)
    H.glass_chatter(rng, b, rng.uniform(0.01, 0.03), int(rng.integers(3, 6)))
    F = length + 0.1
    d = rng.uniform(0.06, 0.1)
    lv = env([(0, 0.8), (d, 0.12 / hard), (d + 0.12, 0.08), (d + 0.22, 0.9), (F, 0.7)], F)
    b.at(0, H.flame(rng, F, lv) * env([(0, 0), (0.05, 1), (F - 0.15, 1), (F, 0)], F), -15)
    b.at(d + 0.2, H.whump(rng, 0.25), -14)
    return b.x


def struck_frame(rng):
    """The blow landing on the man half: a coat struck, the body under it; long fingers flinching on the lever."""
    b = Bus(0.6)
    b.at(0, H.rec(rng, H.CLOTH, rng.uniform(-3, -1), 0.3), -6)
    b.at(0, kit.thud(rng, rng.uniform(70, 85), 0.25, 0.8), -10)
    H.knuckles(rng, b, 0.01, int(rng.integers(4, 7)), 0.12)
    return b.x


def cracked(y, rng, rate=(95, 140)):
    """Through cracked glass: the crack's edges chatter against each other (the sound gated in uneven pulses a hundred
    or so times a second) and the edge folds it into grit."""
    n = len(y)
    c = B.fry(rng, rng.uniform(*rate), n / SR, jitter=0.5, decay=0.003)
    z = y * (0.35 + 0.65 * c)
    return H.norm(mix(dsp.saturate(H.norm(z), 6), dsp.fold(H.norm(z) * 0.5, 2.5) * 0.15))


def reed(rng, f0, breath=0.5):
    """Its spindly half's thin reedy throat: a pressed glottis through the narrow, breathy 'ee' it strains with on the
    lever (horrors.py), with the glottis's own bright harmonics kept over it so the pitch carries when it's forced
    high (up there the 'ee's narrow formants would only catch one harmonic and flatten it into a whistle)."""
    g = synth.glottis(f0, len(f0) / SR, rng, jitter=0.015, shimmer=0.15, oq=0.45)
    ee = H.morph_tract(g, "i", 0.6, breath=breath, rng=rng)
    return H.norm(H.norm(bp(g, 700, 8000)) * 0.7 + ee * 0.4)


def reed_shriek(rng, length, top):
    """The reed forced up into a shriek, sung through the lamp's hot glass (the chimney's partials, as when it sings)
    and cracked."""
    f0 = env([(0, top * 0.68), (0.05, top), (length * 0.6, top * 1.07), (length, top * 0.78)], length, "exp")
    v = reed(rng, f0)
    fg = rng.uniform(2300, 2800)
    glass = dsp.resonate(v, [fg, fg * 2.32, fg * 4.25], q=40, gains=[1, 0.5, 0.25])
    y = cracked(v * 0.8 + H.norm(glass) * 0.35, rng)
    return bp(y, 700, 9000) * env([(0, 0), (0.02, 1), (length * 0.7, 0.8), (length, 0)], length)


def glass_shriek(rng, length, rise):
    """A shriek made of the glass itself: a struck glass's ring held out into a tone (paulstretched), bent up as a
    voice would go and dropping as it breaks off, made to speak by the reed throat it's multiplied with (the glass
    rings in the voice's own pulses, so every partial splits: a cracked, glassy voice)."""
    g = dsp.trim_silence(src.get(RING[int(rng.integers(2))]), -40, 0.002)
    seg = dsp.trim(g, 0.004, 0.3)
    held = dsp.smear(seg, (length + 0.15) / 0.3, 0.1, rng)
    held = H.bend(held, env([(0, -rise * 0.3), (0.06, 0), (length * 0.6, rise), (length + 0.15, rise - 4)], length + 0.15))
    held = dsp.fit(H.norm(held), samples(length))
    f0 = env([(0, 330), (0.05, 440), (length * 0.6, 470), (length, 360)], length, "exp") * rng.uniform(0.92, 1.08)
    v = reed(rng, f0, 0.6)
    y = held * (0.3 + 0.7 * v) + bp(v, 1200, 6000) * 0.2
    y = cracked(y, rng, (110, 160))
    return hp(y, 500) * env([(0, 0), (0.015, 1), (length * 0.7, 0.75), (length, 0)], length)


@recipe("cs-switchman", "hit", "chimney",
        "Struck: its lamp jerking and rattling on its chain, and its thin reed of a throat shrieking through cracked glass",
        """The blow lands on the man half (a coat struck, long fingers flinching on the lever in a ripple of dry
        knuckles) and jerks the oil lamp on its chain: the links jangle (keys jangling, pitched down a few semitones
        into heavier links), the tin lamp knocks, the glass chimney chatters in its gallery and the flame ducks and
        catches. Over it, its spindly half's thin reedy throat (the breathy 'ee' it strains with on the lever) is forced
        up into a shriek, sung through the chimney's glass partials and cracked: the sound gated in uneven pulses where
        a crack's edges chatter, folded into grit. A few metres off, in the open. Takes: one shriek, one that breaks
        off and comes again, a harder jerk with a short shriek.""",
        sources=CHAIN + H.TIN + H.GLASS_TICK + H.CLOTH, takes=3)
def switchman_hit_chimney(rng, k):
    b = Bus(2.0)
    b.at(0, struck_frame(rng), 0)
    b.at(0.005, lamp_jerked(rng, 0.55, (1.0, 1.0, 1.4)[k]), -1)
    t = rng.uniform(0.04, 0.07)
    top = rng.uniform(900, 1250)
    if k == 1:     # the shriek breaks off and comes again, higher
        L1 = rng.uniform(0.22, 0.28)
        b.at(t, reed_shriek(rng, L1, top), -2)
        b.at(t + L1 + rng.uniform(0.06, 0.1), reed_shriek(rng, rng.uniform(0.3, 0.38), top * 1.12), -2)
    else:
        b.at(t, reed_shriek(rng, (0.48, 0, 0.32)[k] * rng.uniform(0.9, 1.1), top), -2)
    return H.finish(H.junction(dsp.trim_silence(b.x, -70), rng, 0.45))


@recipe("cs-switchman", "hit", "glass",
        "Struck: the lamp rattling on its chain, and a shriek made of glass, a struck glass's ring bent up into a voice",
        """The same blow and lamp as 'chimney' (a coat struck, knuckles flinching, the chain jangling, the tin lamp
        knocking, the chimney chattering, the flame ducking). The shriek is glass made to speak: a real struck glass's
        ring held out into a tone (paulstretched), bent up a few semitones as a voice would go and dropping as it
        breaks off, multiplied by its thin reedy throat so the glass rings in the voice's own pulses (every partial
        splits: a cracked, glassy voice), then gated where the crack chatters. The man half grunts under it. A few
        metres off, in the open.""",
        sources=CHAIN + RING + H.TIN + H.GLASS_TICK + H.CLOTH, takes=3)
def switchman_hit_glass(rng, k):
    b = Bus(2.0)
    b.at(0, struck_frame(rng), 0)
    b.at(0.005, lamp_jerked(rng, 0.55, (1.0, 1.3, 1.0)[k]), -1)
    t = rng.uniform(0.04, 0.07)
    L = (0.5, 0.36, 0.62)[k] * rng.uniform(0.9, 1.1)
    b.at(t, glass_shriek(rng, L, (5, 4, 6)[k] * rng.uniform(0.85, 1.15)), -2)
    Lg = rng.uniform(0.22, 0.3)
    man = H.throat(rng, env([(0, 125), (0.05, 140), (Lg, 100)], Lg, "exp"), [(0, "a"), (Lg, "u")], 0.72, breath=0.45,
                   rough=0.5, jitter=0.02)
    b.at(0.01, man * env([(0, 0), (0.02, 1), (Lg * 0.6, 0.6), (Lg, 0)], Lg), -11)
    return H.finish(H.junction(dsp.trim_silence(b.x, -70), rng, 0.45))


# ---- The Grumbler -------------------------------------------------------------------------------------------------------
# A dock labourer gone wrong (beasts.py): skin, a man's muttering voice, cheeks split back to the ears. Struck, he is
# affronted first; going feral is its own cue.

def skin_struck(rng, weight=1.0):
    """The blow on his bare back: skin smacked (a real slap made broad, as his palms on the girders) and the weight of
    a man under it."""
    b = Bus(0.5)
    s = B.clip(B.SLAPS[int(rng.integers(2))], length=0.12, semis=-rng.uniform(4, 7))
    b.at(0, B.unit(lp(s, 5000)), -2)
    b.at(0, kit.thud(rng, rng.uniform(75, 90), 0.2, weight), -9)
    return b.x


def affronted(rng, length, top, rasp=0.45):
    """A man's bark of outrage ('HAH!'): his throat jumps up and falls, through a man's tract, with a second throat an
    octave under it as the split cheeks make two mouths of one."""
    pts = [(0, top * 0.7), (0.035, top), (length * 0.5, top * 0.94), (length, top * 0.62)]
    vow = [(0, "a"), (length * 0.6, "a"), (length, "o")]
    sh = [(0, 0), (0.015, 1), (length * 0.5, 0.85), (length, 0)]
    a = B.throat(rng, pts, length, vow, sh, rasp=rasp, fry_rate=(40, 60), sub=0.35, hiss=0.15, fire=0.0, tract=0.66,
                 jitter=0.02)
    lo = B.throat(rng, [(t, p / 2) for t, p in pts], length, vow, sh, rasp=0.7, fry_rate=(28, 40), sub=0.6, hiss=0.0,
                  fire=0.0, tract=0.58)
    return B.unit(mix(a, lo * 0.45))


def snort(rng, length):
    """Breath blasted out through his nose and the split cheeks: the cheeks flapping and spraying spit, the nostrils'
    rush through a shut mouth."""
    sh = env([(0, 0), (0.015, 1), (length * 0.5, 0.6), (length, 0)], length)
    nose = synth.breath(length, "m", 0.7, rng, shape=sh)
    return B.unit(mix(B.unit(B.blubber(rng, length)) * sh * 0.8, B.unit(hp(nose, 250))))


def hrumph(rng, length, f0=95):
    """A closed-mouth growl of indignation rising through his nose: 'hrrm'."""
    pts = [(0, f0), (length * 0.6, f0 * 1.35), (length, f0 * 1.15)]
    sh = [(0, 0), (0.03, 1), (length * 0.7, 0.8), (length, 0)]
    return B.throat(rng, pts, length, [(0, "m"), (length, "o")], sh, rasp=0.75, fry_rate=(26, 36), sub=0.6, hiss=0.05,
                    fire=0.0, tract=0.62)


@recipe("cs-grumbler", "hit", "bark",
        "Struck: his skin smacked and an affronted man's bark through split cheeks, then indignant muttering",
        """Skin, not chitin: the blow on his bare back (a real slap dropped into a broad smack, a man's weight) and he
        barks at it, outraged: a short 'HAH!' that jumps up and falls through a man's throat with fry in it, a second
        throat an octave under it (the split cheeks make two mouths of one, as in his bellow). Then his muttering, quick
        and climbing, the way he grumbles on the crane. Not yet feral (that's its own cue). Takes: a bark and a grumble,
        two barks the second higher, a bark that drags into a growl with his teeth clacking.""",
        sources=B.SLAPS, takes=3)
def grumbler_hit_bark(rng, k):
    b = Bus(1.8)
    b.at(0, skin_struck(rng, 1.2 if k == 2 else 1.0), 0)
    t = rng.uniform(0.03, 0.05)
    top = rng.uniform(170, 200)
    if k == 0:
        L = rng.uniform(0.2, 0.26)
        b.at(t, affronted(rng, L, top), -1)
        b.at(t + L + 0.05, B.mutter(rng, rng.uniform(0.45, 0.6), rng.uniform(105, 120), rise=1.25, rate=8, open_=0.4), -6)
    elif k == 1:
        L1, L2 = rng.uniform(0.14, 0.18), rng.uniform(0.2, 0.26)
        b.at(t, affronted(rng, L1, top * 0.9), -3)
        b.at(t + L1 + rng.uniform(0.07, 0.11), affronted(rng, L2, top * 1.2), -1)
    else:
        L = rng.uniform(0.5, 0.6)
        b.at(t, affronted(rng, L, top * 0.85, rasp=0.85), -1)
        b.at(t + L * 0.6, B.gnash(rng, 0.25, 22), -8)
    return H.finish(B.outdoors(b.x, rng, 0.1, 0.4))


@recipe("cs-grumbler", "hit", "snort",
        "Struck: his skin smacked and an indignant snort out through flapping split cheeks, a closed-mouth growl rising",
        """Skin, not chitin: the blow on his bare back (a real slap dropped into a broad smack, a man's weight). He
        snorts at it like a bull: breath blasted out through his nose and through cheeks split back to the ears, so
        they flap and spray spit (the same flapping breath as his feral 'gnash', short), then a closed-mouth growl of
        indignation rising through his nose, 'hrrm'. Not yet feral. Takes: a snort and a growl, two huffs and teeth
        clacking, a growl first and a long snort.""",
        sources=B.SLAPS, takes=3)
def grumbler_hit_snort(rng, k):
    b = Bus(1.8)
    b.at(0, skin_struck(rng), 0)
    t = rng.uniform(0.02, 0.04)
    if k == 0:
        L = rng.uniform(0.2, 0.26)
        b.at(t, snort(rng, L), -1)
        b.at(t + L + 0.03, hrumph(rng, rng.uniform(0.4, 0.5), rng.uniform(90, 105)), -3)
    elif k == 1:
        for i in range(2):
            L = rng.uniform(0.12, 0.16)
            b.at(t, snort(rng, L), -1 - 2 * i)
            t += L + rng.uniform(0.05, 0.08)
        b.at(t, B.gnash(rng, 0.2, 18), -6)
    else:
        L = rng.uniform(0.3, 0.38)
        b.at(t, hrumph(rng, L, rng.uniform(85, 95)), -2)
        b.at(t + L - 0.05, snort(rng, rng.uniform(0.32, 0.4)), -1)
    return H.finish(B.outdoors(b.x, rng, 0.1, 0.4))


# ---- Followers ----------------------------------------------------------------------------------------------------------
# A hand-sized engorged tick (beasts.py): a leather sac of blood under a hard shield, a barbed beak, ten hooked legs.
# It has no throat, so its squeal is made by what it has: the beak, or air squeezed out of the sac.

def pulses(rng, rate, length, wobble=0.06):
    """A train of impulses `rate` a second (a constant or a curve), their timing wandering and their strength uneven:
    a beak's clicks before its shell rings them."""
    n = samples(length)
    w = lp(rng.standard_normal(n).astype(np.float32), 30)
    w = w / (np.std(w) + 1e-9)
    ph = np.cumsum(synth.curve(rate, n) * (1 + wobble * w)) / SR
    idx = np.nonzero(np.diff(np.floor(ph)) > 0)[0] + 1
    x = np.zeros(n, np.float32)
    x[idx] = rng.uniform(0.45, 1.0, len(idx))
    return x


def shield_knocked(rng, weight=1.0):
    """The blow off a back meeting it: its hard little shield knocked (a tick of chitin rung through a small hollow
    shell), the blood in the sac sloshing under it, the small weight of it."""
    b = Bus(0.4)
    f = rng.uniform(1800, 2600)
    b.at(0, ck.hollow(rng, [f, f * 1.7, f * 2.6], 0.05, q=8), -2)
    b.at(0.002, B.squish(rng, rng.uniform(0.08, 0.12), 500, 2600), -7)
    b.at(0, kit.thud(rng, 180, 0.06, 0.4 * weight), -14)
    return b.x


def beak_squeal(rng, length, top):
    """The barbed beak chattering: a few clicks quickening, then they break into a squeal all at once (a train of clicks
    past a hundred or so a second is heard as a pitch), hold there wavering roughly, and break back into chatter. The
    jumps are quick, as a throat's would be, so it never slides like a whistle. Each click rings the beak's small hard
    shell."""
    rate = env([(0, 30), (length * 0.12, 70), (length * 0.15, top * 0.85), (length * 0.18, top), (length * 0.62, top * 1.06),
                (length * 0.68, top * 0.8), (length * 0.71, 110), (length, 32)], length, "exp")
    x = pulses(rng, rate, length, wobble=0.1)
    fb = rng.uniform(3600, 4600)
    y = dsp.resonate(x, [fb, fb * 1.47, fb * 2.2], q=7, gains=[1, 0.6, 0.35])
    air = bp(synth.noise(length, rng), 3500, 9000) * B.follow(y, 8) * 0.25
    return H.norm(mix(H.norm(y), air)) * env([(0, 0.5), (length * 0.3, 1), (length * 0.7, 0.85), (length, 0.2)], length)


def spiracle_squeal(rng, length, f):
    """Air squeezed out of the sac through its breathing pores: a tiny rough squeal (a throat-less whistle on a pitch
    contour, period-doubled into a rasp) chopped into a chitter by the beak's clicks, wet bubbling in the sac behind."""
    f0 = env([(0, f * 0.8), (0.04, f), (length * 0.5, f * 1.1), (length, f * 0.75)], length, "exp")
    g = synth.glottis(f0, length, rng, jitter=0.02, shimmer=0.3, sub=0.5, oq=0.4)
    v = bp(g, 1500, 9000)
    whistle = dsp.sweep_filter(synth.noise(length, rng), "bp", f0 * 2, q=30)
    chop = B.fry(rng, env([(0, rng.uniform(26, 32)), (length, rng.uniform(36, 44))], length), length, jitter=0.35,
                 decay=0.006)
    y = (H.norm(v) * 0.7 + H.norm(whistle) * 0.4) * (0.15 + 0.85 * chop)
    wet = synth.bubbles(length, 120, 1500, 4500, rng, rise=(0.02, 0.1))
    return mix(H.norm(y), H.norm(wet) * 0.2) * env([(0, 0), (0.01, 1), (length * 0.6, 0.8), (length, 0)], length)


@recipe("cs-followers", "hit", "beak",
        "Struck: its hard shield knocked, the sac sloshing, and its beak's chatter running together into a squeal",
        """The blow meets its hard little shield (a chitin tick rung through a small hollow shell) and sloshes the
        blood in the sac under it. It has no throat, so the squeal is its barbed beak: clicks that come faster and
        faster until they run together into a squeal (a click train past a hundred or so a second is heard as a pitch,
        here up to 1.3-2.2 kHz), hold wavering, and fall apart into clicks again, each click ringing the beak's small
        hard shell, with a breath of air through it. Inside a car. Takes: one squeal, two (the second higher), a long
        one that chatters off as its legs scrabble.""",
        takes=3)
def followers_hit_beak(rng, k):
    b = Bus(1.6)
    b.at(0, shield_knocked(rng), 0)
    t = rng.uniform(0.02, 0.04)
    top = rng.uniform(1300, 1800)
    if k == 1:
        L1 = rng.uniform(0.22, 0.28)
        b.at(t, beak_squeal(rng, L1, top), -1)
        b.at(t + L1 + 0.03, beak_squeal(rng, rng.uniform(0.25, 0.3), top * 1.25), -1)
    else:
        L = (0.4, 0, 0.6)[k] * rng.uniform(0.9, 1.1)
        b.at(t, beak_squeal(rng, L, top), -1)
        if k == 2:
            b.at(t + L * 0.5, B.legs(rng, 0.5, env([(0, 60), (0.5, 15)], 0.5)), -12)
    return H.finish(dsp.room(b.x, "car", wet=0.12, rng=rng))


@recipe("cs-followers", "hit", "spiracle",
        "Struck: its shield knocked, and air squeezed out of the sac in a tiny rough squeal, chopped into chittering",
        """The blow meets its hard little shield (a chitin tick rung through a small hollow shell) and squeezes the
        sac. It has no throat, so the squeal is air forced out through its breathing pores: a tiny rough whistle on a
        pitch contour (1.6-2.6 kHz, period-doubled into a rasp, with a narrow whistle an octave over it), chopped into
        a chitter by the beak's clicks at 26-44 a second, wet bubbling in the sac behind it. Inside a car. Takes: one
        chittering squeal, a short squeal and a longer one, a long one with its legs scrabbling.""",
        takes=3)
def followers_hit_spiracle(rng, k):
    b = Bus(1.6)
    b.at(0, shield_knocked(rng), 0)
    t = rng.uniform(0.02, 0.04)
    f = rng.uniform(1600, 2100)
    if k == 1:
        L1 = rng.uniform(0.14, 0.18)
        b.at(t, spiracle_squeal(rng, L1, f * 1.15), -2)
        b.at(t + L1 + 0.05, spiracle_squeal(rng, rng.uniform(0.32, 0.4), f), -1)
    else:
        L = (0.42, 0, 0.62)[k] * rng.uniform(0.9, 1.1)
        b.at(t, spiracle_squeal(rng, L, f), -1)
        if k == 2:
            b.at(t + L * 0.4, B.legs(rng, 0.5, env([(0, 60), (0.5, 15)], 0.5)), -12)
    return H.finish(dsp.room(b.x, "car", wet=0.12, rng=rng))


# ---- The film: skipping it ----------------------------------------------------------------------------------------------
# The end-of-night film is a projector's, so skipping it is the projector stopped: its gate clattering, the shutter.

SHUTTER = "sfx_100_v2:misc_17"          # a camera's shutter mechanism: the projector's shutter blade snapping to
TICK = "kenney_ui-audio:switch13"       # a tiny dry tick: the claw pulling the film down a frame
FLAP = "kenney_rpg-audio:bookFlip3"     # a page's snap: the film's loose tail slapping the reel


def gate_clatter(rng, length, fps=24.0):
    """The projector running: the claw pulling the film down a frame at a time (a small dry tick each frame) and the
    film chattering through the gate in the same rhythm (a soft paper flutter)."""
    b = Bus(length + 0.05)
    tick = ui_cut(TICK, 0, 0.03, 0.0005, 0.01)
    for t in np.arange(0, length, 1 / fps):
        b.at(t + rng.normal(0, 0.0015), dsp.vari(tick, rng.uniform(-6, -4)), rng.uniform(-3, 0))
    n = samples(length)
    flutter = bp(synth.noise(length, rng), 900, 6000) * (0.3 + 0.7 * (np.sin(2 * np.pi * fps * np.arange(n) / SR) > 0.6))
    b.at(0, H.norm(flutter), -16)
    return b.x


def shutter(rng):
    """The shutter blade snapping to across the gate: the first strike of a camera's shutter mechanism, a hair lower."""
    x = ck.get(SHUTTER)
    h = ck.hits(x, floor_db=-12, gap=0.02)
    a = h[0][0] if h else 0
    return ck.norm(dsp.vari(ck.cut(x, a - samples(0.001), a + samples(0.09), 0.0005, 0.03), -2))


@recipe("ui-film", "skip", "shutter",
        "The film skipped: the projector's gate clattering, cut dead by the shutter snapping shut",
        """A projector's mechanism, dry and close like the rest of the paperwork: a fifth of a second of the gate
        running (a tiny dry tick each frame at 24 a second, pitched down, and the film fluttering through in the same
        rhythm), cut dead by the shutter snapping to (the first strike of sfx_100's camera shutter, a hair lower), and
        nothing after it.""",
        sources=[TICK, SHUTTER], takes=1, lufs=-24)
def film_skip_shutter(rng, k):
    b = Bus(0.6)
    run = 0.2
    b.at(0, gate_clatter(rng, run), -11)
    b.at(run, shutter(rng), 0)
    return tidy(b.x, 200)


@recipe("ui-film", "skip", "run-out",
        "The film skipped: the shutter snaps shut and the film's loose tail slaps round the spinning reel to a stop",
        """The sound of a film ending in a projection booth: the shutter snapping to (the first strike of sfx_100's
        camera shutter), and the film's loose tail slapping the reel as it spins down, five slaps further apart and
        softer each time (a page's snap, pitched down into stiffer film). Dry and close.""",
        sources=[SHUTTER, FLAP], takes=1, lufs=-24)
def film_skip_runout(rng, k):
    b = Bus(1.0)
    b.at(0, shutter(rng), -3)
    flap = hp(ui_cut(FLAP, 0, 0.04, 0.0005, 0.02), 500)
    t, gap = 0.07, 0.075
    for i in range(5):
        b.at(t, dsp.vari(flap, -3 - 0.8 * i), -2 - 2.5 * i)
        t += gap
        gap *= 1.3
    return tidy(b.x, 200)


# ---- The panels: the roster, the supplies, the route card ---------------------------------------------------------------
# Paperwork drawn out, put away and leafed through: the menus' own page and folder sounds (recipes/ui.py), close and dry.

BOOK = {n: f"kenney_rpg-audio:{n}" for n in ("bookOpen", "bookClose", "bookFlip1", "bookFlip2", "bookFlip3", "bookPlace1",
                                             "bookPlace3")}
LEATHER_SLIDE = "kenney_rpg-audio:handleSmallLeather2"     # a leather strap drawn through a hand: a bound ledger sliding


def slide(rng, length, rising=True, lo=1800, hi=9000):
    """A card slid along card or paper: fine stick-slip friction, light and bright, speeding up as it comes out (or
    slowing as it goes home)."""
    sh = [(0, 0.2), (length * 0.8, 1), (length, 0)] if rising else [(0, 0), (0.02, 1), (length, 0.15)]
    return ck.friction(rng, length, rate=rng.uniform(500, 800), lo=lo, hi=hi, rough=0.5, shape=env(sh, length))


def ledger_slide(rng, length):
    """A bound ledger drawn across the desk: its leather cover's slide, quiet and dark."""
    x = dsp.trim_silence(ck.get(LEATHER_SLIDE), -40)
    return ck.norm(lp(dsp.fade(dsp.fit(dsp.vari(x, -3), samples(length)), 0.01, length * 0.5), 5000))


TAPS = [BOOK["bookPlace1"], BOOK["bookPlace3"]]     # bookPlace2 lands twice


def paper_tap(rng, which=0):
    """A card squared on the desk: a book set down, its first hit only, darkened and choked to one soft tap."""
    return ck.norm(lp(ck.choke(hit_of(TAPS[which % 2], 0, 0.1), 0.015, 0.012), 3000))


@recipe("ui-panels", "open", "ledger",
        "A panel opened: a ledger drawn across the desk and its cover opened",
        """A bound ledger slid out (Kenney's leather strap drawn through a hand, pitched down and darkened) and its
        cover opened onto the page (the low bump of Kenney's book opening, the paper's snap from a page flip). The
        menus' paper, a little fuller for a whole book.""",
        sources=[LEATHER_SLIDE, BOOK["bookOpen"], BOOK["bookFlip2"]], takes=3, lufs=-25, gap=0.45)
def panel_open_ledger(rng, k):
    b = Bus(0.6)
    Ls = (0.12, 0.16, 0.1)[k]
    b.at(0, ledger_slide(rng, Ls), -10)
    o = ck.norm(hit_of(BOOK["bookOpen"], 0, 0.12, -20))
    b.at(Ls - 0.02, dsp.vari(o, rng.uniform(-1.5, 0.5)), 0)
    flip = hp(ui_cut(BOOK["bookFlip2"], 0.0, 0.05), 1200)
    b.at(Ls + rng.uniform(0.04, 0.07), dsp.vari(flip, rng.uniform(-2, 0)), -10)
    return tidy(b.x, 120)


@recipe("ui-panels", "open", "card",
        "A panel opened: a card drawn out of its sleeve, ending in a flick",
        """A stiff card slid out of a pigeonhole: light, bright stick-slip friction that speeds up as it comes (paper
        on paper), ending in the card's flick as it clears (the snap of a page flip, highpassed). Short and dry, the
        menus' paper.""",
        sources=[BOOK["bookFlip3"]], takes=3, lufs=-25, gap=0.45)
def panel_open_card(rng, k):
    b = Bus(0.5)
    Ls = (0.14, 0.18, 0.11)[k] * rng.uniform(0.9, 1.1)
    b.at(0, slide(rng, Ls, True), -9)
    flick = hp(ui_cut(BOOK["bookFlip3"], 0.0, 0.05), 900)
    b.at(Ls - 0.01, dsp.vari(flick, rng.uniform(-1, 1.5)), 0)
    return tidy(b.x, 300)


@recipe("ui-panels", "close", "ledger",
        "A panel put away: the ledger's cover shut and the book pushed back",
        """The ledger shut (Kenney's book closing: the cover's soft slap and the pages' air) and pushed back across
        the desk (the leather slide, short and quieter). Duller and quieter than opening, as the menus' 'back' is to
        their 'move'.""",
        sources=[BOOK["bookClose"], LEATHER_SLIDE], takes=3, lufs=-27, gap=0.45)
def panel_close_ledger(rng, k):
    b = Bus(0.6)
    c = ck.norm(hit_of(BOOK["bookClose"], 0, 0.15))
    b.at(0, dsp.vari(c, rng.uniform(-2, 0.5)), 0)
    b.at(rng.uniform(0.07, 0.11), ledger_slide(rng, (0.12, 0.1, 0.15)[k]), -12)
    return tidy(lp(b.x, 7000), 100)


@recipe("ui-panels", "close", "card",
        "A panel put away: a card slid back into its sleeve and squared with a tap",
        """The card slid home (the same light friction as drawing it out, slowing as it goes in) and squared against
        the desk with a soft tap (a book set down, its first hit only, darkened and choked short).""",
        sources=TAPS, takes=3, lufs=-27, gap=0.45)
def panel_close_card(rng, k):
    b = Bus(0.5)
    Ls = (0.13, 0.16, 0.1)[k] * rng.uniform(0.9, 1.1)
    b.at(0, lp(slide(rng, Ls, False, 1500, 7000), 6000), -5)
    b.at(Ls - 0.005, dsp.vari(paper_tap(rng, k), rng.uniform(-2, 0)), -1)
    return tidy(b.x, 150, 8000)


@recipe("ui-panels", "page", "leaf",
        "A page turned: a ledger's leaf lifted, swept over and laid down",
        """A whole page turn, fuller than the menus' flick: a leaf lifted, swept over and landing, cut from Kenney's
        page flips (three different turns, nudged in pitch), highpassed so only the paper is left.""",
        sources=[BOOK["bookFlip1"], BOOK["bookFlip2"], BOOK["bookFlip3"]], takes=3, lufs=-26, gap=0.4)
def panel_page_leaf(rng, k):
    if k == 0:     # bookFlip1: the swish (0.27-0.56 s) into the page landing (0.57 s)
        x = dsp.trim(src.get(BOOK["bookFlip1"]), 0.36, 0.3)
    elif k == 1:   # bookFlip2: the lift's snap and the sweep after it
        x = dsp.trim(dsp.trim_silence(src.get(BOOK["bookFlip2"]), -45), 0.0, 0.26)
    else:          # bookFlip3, slowed: a stiffer leaf
        x = dsp.vari(dsp.trim(dsp.trim_silence(src.get(BOOK["bookFlip3"]), -45), 0.0, 0.2), -2)
    return tidy(dsp.fade(dsp.vari(x, rng.uniform(-1, 1)), 0.01, 0.04), 500)


@recipe("ui-panels", "page", "stiff",
        "A page turned on the route card: a stiff card leaf swung over and laid flat with a soft tap",
        """The route card is card, not paper: its leaf swung over in one stiff sweep (a page's swish pitched down,
        with a short band of air rising and falling) and laid flat with a soft tap (a book set down, choked short).""",
        sources=[BOOK["bookFlip1"]] + TAPS, takes=3, lufs=-26, gap=0.4)
def panel_page_stiff(rng, k):
    b = Bus(0.5)
    L = (0.16, 0.2, 0.13)[k] * rng.uniform(0.9, 1.1)
    sw = dsp.fit(dsp.vari(dsp.trim(src.get(BOOK["bookFlip1"]), 0.28, 0.28), -3), samples(L))
    b.at(0, dsp.fade(hp(sw, 400), 0.02, L * 0.4), 0)
    b.at(0, ck.whoosh(rng, L, 500, 3000, 0.6), -14)
    b.at(L - 0.01, dsp.vari(paper_tap(rng, k + 1), rng.uniform(-1, 1)), -4)
    return tidy(b.x, 300)


# ---- The fortress: a crew named and deleted -----------------------------------------------------------------------------
# The same typewriter and rubber stamp as the run-end report (recipes/mishaps_world.py), so a crew's paperwork is all one
# office.

TYPED = (2, 3, 7, 9)     # the pool's cleanest strikes (an early peak, little after it), one from each recording


@recipe("ui-menus", "type", "strike",
        "A letter typed into a name: one key of the report's typewriter, struck lightly",
        """One strike from the run-end report's own typewriter (the strikes cut from sfx_100's typewriter-like
        mechanisms, as in its tally and its weary own-goal line): the four cleanest, one from each recording, matched
        to each other's balance so they're keys of one machine, nudged in pitch and highpassed so only the key and its
        little ring are left. Quieter than moving through the menu.""",
        sources=MW.KEYS, takes=4, lufs=-28, gap=0.25)
def type_strike(rng, k):
    keys = ck.matched([dsp.trim(MW.strikes()[i], 0, 0.07) for i in TYPED], 0.6)
    return tidy(dsp.fade(dsp.vari(keys[k], rng.uniform(-0.6, 0.6)), 0.0005, 0.02), 400)


@recipe("ui-menus", "type", "machine",
        "A letter typed into a name: the report's modelled typewriter, one light key",
        """The run-end report's modelled typewriter (its own-goal 'machine'), one key at a time and struck lightly:
        the key lever bottoming, the typebar's slug snapping onto the paper on the rubber platen, the frame's brief
        ring and the escapement's little tick as the carriage steps one letter on.""",
        takes=4, lufs=-28, gap=0.25)
def type_machine(rng, k):
    return tidy(MW.keystroke(rng, (0.75, 0.85, 0.7, 0.8)[k]), 300)


@recipe("ui-menus", "delete", "rubber",
        "A crew deleted for good: the report's rubber stamp lifted off its pad, a long beat, and brought down hard",
        """The run-end report's death stamp, heavier: the rubber unsticking from the ink pad (a small low tacky click
        and the tin pad knocking the desk), a longer clerk's beat, then the stamp brought down with the whole arm (a
        book slapped on a desk pitched further down, a soft heavy hit choked short under it for the desk's weight,
        harder than for a death), and pressed once more to be sure it took. Dry and close.""",
        sources=[MW.LIFT] + MW.PLACES + MW.SOFT, takes=1, lufs=-22)
def delete_rubber(rng, k):
    slap = dsp.vari(hit_of(MW.PLACES[1], 0, 0.24, -5), rng.uniform(-4, -3))
    thud = ck.choke(ck.norm(ck.get(MW.SOFT[3])), 0.04, 0.05)
    down = ck.place([(0, slap, 0), (0.002, thud, -3)])
    parts = [(0, MW.ink_lift(rng, 1), -6), (0.5, down, 0), (0.62, lp(down, 1500), -14)]
    return tidy(ck.place(parts), 50)


@recipe("ui-menus", "delete", "desk",
        "A crew deleted for good: the modelled rubber stamp peeled off its pad and thumped down hard on a wooden desk",
        """The run-end report's modelled stamp (its death stamp's 'desk'), heavier: the rubber peeling off the inked
        felt and the tin pad's tick, a longer beat, then the stamp thumped down with more weight than for a death (the
        desk top's low boards, the paper's slap, the wooden handle knocking in its mount, the hand behind it).""",
        takes=1, lufs=-22)
def delete_desk(rng, k):
    tin = MW.W.body(rng, rng.uniform(1300, 1600), MW.W.PLATE, decay=0.005, contact=0.0008, length=0.04, count=6)
    lift = ck.place([(0, MW.peel(rng, 0.03), 0), (0.024, ck.norm(tin), -18)])
    return tidy(ck.place([(0, lift, -6), (0.5, MW.desk_thump(rng, 1.45), 0)]), 50)
