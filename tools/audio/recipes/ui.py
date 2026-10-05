"""The interface: menus, hold-to-interact prompts, the run-end report, the dead phase (T30, T79, D.7, D.10-D.12).

The game's UI is the railway's paperwork and brass, so its sounds are too: a waybill's page, a conductor's ticket punch,
a rubber stamp, a typewriter, a brass switch, a small bell. Real recordings (Kenney's RPG and impact packs, sfx_100's
typewriter-ish mechanisms), cut short and dry so they sit in front of the game without sounding like a game.
"""

import numpy as np

import dsp
import src
from build import recipe
from dsp import samples, hp, lp, bp, mix, fade, Bus
from recipes.kit import scatter

R = lambda n: f"kenney_rpg-audio:{n}"            # noqa: E731
K = lambda n, i: f"kenney_impact-sounds:{n}_{i:03d}"  # noqa: E731
S = lambda n: f"sfx_100_v2:{n}"                  # noqa: E731


def cut(key, start=0.0, length=0.12, a=0.001, b=0.02):
    x = dsp.trim_silence(src.get(key), -45)
    return fade(dsp.trim(x, start, length), a, b)


def tidy(x, lo=150, hi=12000):
    """Dry and close: no room, no rumble."""
    return dsp.lp(hp(x, lo, 2), hi, 2)


# ---- Menus ---------------------------------------------------------------------------------------------------------------

@recipe("ui-menus", "move", "page", "A waybill's page flicked: the corner of a sheet under the thumb",
        """The first 70-90 ms of Kenney's page flips (three different flips, two cuts of one), highpassed so only the
        paper's snap is left, nudged in pitch so four takes alternate without a pattern.""",
        sources=[R("bookFlip1"), R("bookFlip2"), R("bookFlip3")], takes=4, lufs=-26, gap=0.35)
def menu_move(rng, k):
    key = [R("bookFlip1"), R("bookFlip2"), R("bookFlip3"), R("bookFlip1")][k]
    x = cut(key, 0.0 if k < 3 else 0.12, rng.uniform(0.07, 0.09))
    return tidy(dsp.vari(x, rng.uniform(-1, 1.5)), 600)


@recipe("ui-menus", "move", "key", "A typewriter key, struck lightly",
        """Single strikes cut from sfx_100's typewriter-like mechanism recordings (misc_15, misc_13), each trimmed to the key
        and its little ring, kept dry. Reads as 'filing a form', which is what the menus are.""",
        sources=[S("misc_15"), S("misc_13")], takes=4, lufs=-26, gap=0.35)
def menu_key(rng, k):
    x = dsp.trim_silence(src.get([S("misc_15"), S("misc_13")][k % 2]), -40)
    on = _onsets(x)
    a = on[min(k // 2 * 2 + 1, len(on) - 1)] if len(on) > 1 else 0
    return tidy(fade(x[a:a + samples(0.08)], 0.001, 0.03), 400)


def _onsets(x, db=-20):
    """Start samples of the strikes in a recording (rises past db under its peak after a quiet spell)."""
    hop = samples(0.004)
    e = np.array([np.max(np.abs(x[i:i + hop])) for i in range(0, len(x) - hop, hop)])
    thr = np.max(e) * dsp.db2a(db)
    out, last = [], -100
    for i, v in enumerate(e):
        if v > thr and i - last > 12:
            out.append(i * hop)
        if v > thr:
            last = i
    return out or [0]


@recipe("ui-menus", "select", "punch", "A conductor's ticket punch: the click and the paper giving",
        """Kenney's metal click (the punch's jaw) layered over the snap of a page (the hole going through), both trimmed
        tight; the click pitched a little per take.""",
        sources=[R("metalClick"), R("bookFlip2")], takes=3, lufs=-24, gap=0.45)
def menu_select(rng, k):
    click = cut(R("metalClick"), 0, 0.12)
    paper = cut(R("bookFlip2"), 0.0, 0.05)
    x = mix(dsp.vari(click, rng.uniform(-1.5, 1.0)), dsp.gain(hp(paper, 1500), -6))
    return tidy(x, 300)


@recipe("ui-menus", "back", "slide", "A sheet slid back into the folder",
        """A page flip played backwards and softened (lowpassed, slower), so going back is the same paper, quieter and
        duller than going forward.""",
        sources=[R("bookFlip3"), R("bookClose")], takes=2, lufs=-28, gap=0.45)
def menu_back(rng, k):
    x = dsp.reverse(cut(R("bookFlip3"), 0.0, 0.14, 0.002, 0.005))
    x = mix(dsp.vari(x, -2 - k), dsp.gain(lp(cut(R("bookClose"), 0, 0.1), 2000), -10))
    return tidy(fade(x, 0.01, 0.03), 200, 7000)


@recipe("ui-menus", "title", "far-train", "Under the title: wind over open country and a train far off, its whistle once",
        """A night wind (filtered noise breathing in slow gusts), sfx_100's rail loop pulled 400 m away (dark, wet,
        quiet), and the conductor's whistle kept from the earlier pass, far off and once a cycle. Under it all, the
        work drone from ui-music. A 24 s loop.""",
        sources=[S("loop_ambient_04"), "earlier:tell-whistler--conductor"], takes=1, loop=True, lufs=-26)
def title(rng, k):
    L = 24.0
    n = samples(L)
    wind = dsp.sweep_filter(synth_noise(rng, L), "bp", dsp.env([(0, 300), (6, 700), (12, 380), (18, 900), (24, 300)], L, "exp"), q=0.8)
    wind = wind * dsp.env([(0, 0.6), (5, 1), (11, 0.5), (17, 1), (24, 0.6)], L)
    train = src.get(S("loop_ambient_04"))
    train = np.tile(train, int(np.ceil(n / len(train))))[:n]
    train = dsp.distance(train, 400, rng)[:n]
    wh = dsp.load(_earlier("tell-whistler--conductor"))
    wh = dsp.distance(wh, 600, rng)
    b = Bus(L + 4)
    b.at(0, dsp.gain(wind / (np.max(np.abs(wind)) + 1e-9), -8))
    b.at(0, dsp.gain(train / (np.max(np.abs(train)) + 1e-9), -10))
    b.at(9.0, dsp.gain(wh / (np.max(np.abs(wh)) + 1e-9), -12))
    import recipes.music as music
    b.at(0, dsp.gain(music.drone_ground(rng, L), -10))
    return dsp.wrap(b.x, n)


def synth_noise(rng, L):
    import synth
    return synth.noise(L, rng, "pink")


def _earlier(name):
    import os
    return os.path.join(os.path.dirname(__file__), "..", "..", "..", "out", "audio", "earlier", "audio", name + ".mp3")


@recipe("ui-menus", "end-card", "stamp", "The wishlist end card: a rubber stamp comes down, and a far whistle answers",
        """A heavy rubber stamp on a desk (Kenney's soft heavy impact under a book being set down, with the paper's
        slap), then the conductor's whistle from the earlier pass a long way off, fading into the wind.""",
        sources=[K("impactSoft_heavy", 2), R("bookPlace2"), "earlier:tell-whistler--conductor"], takes=1, lufs=-22)
def end_card(rng, k):
    stamp = mix(cut(K("impactSoft_heavy", 2), 0, 0.3), dsp.gain(cut(R("bookPlace2"), 0, 0.25), -4))
    wh = dsp.distance(dsp.load(_earlier("tell-whistler--conductor")), 500, rng)
    b = Bus(5.5)
    b.at(0.05, tidy(stamp, 60))
    b.at(0.9, dsp.gain(wh / (np.max(np.abs(wh)) + 1e-9), -10))
    return b.x


# ---- Hold-to-interact ----------------------------------------------------------------------------------------------------

@recipe("ui-prompts", "hold", "ratchet", "A held action ticking: a ratchet's pawl clicking over, steady",
        """One click of the ratchet in sfx_100's misc_20, cut clean and repeated every 0.15 s with a little drift in time
        and level, so it ticks like a mechanism, not a clock. The game raises its rate as the action nears done.""",
        sources=[S("misc_20")], takes=1, loop=True, lufs=-26)
def hold_ratchet(rng, k):
    x = dsp.trim_silence(src.get(S("misc_20")), -35)
    on = _onsets(x, -14)
    clicks = [fade(x[a:a + samples(0.05)], 0.0005, 0.02) for a in on[:4]]
    L, step = 1.8, 0.15
    b = Bus(L + 0.2)
    t, i = 0.0, 0
    while t < L - 0.01:
        b.at(t + rng.normal(0, 0.004), clicks[i % len(clicks)], rng.uniform(-2, 0))
        t += step
        i += 1
    return tidy(dsp.wrap(b.x, samples(L)), 400)


@recipe("ui-prompts", "complete", "latch", "Done: a latch dropping home",
        """Kenney's metal latch over a light metal impact for the weight behind it, cut short and dry: the same family
        as the ratchet, ending.""",
        sources=[R("metalLatch"), K("impactMetal_light", 1), K("impactMetal_light", 3)], takes=2, lufs=-22, gap=0.5)
def hold_done(rng, k):
    x = mix(cut(R("metalLatch"), 0, 0.2), dsp.gain(cut(K("impactMetal_light", 1 + 2 * k), 0, 0.3), -5))
    return tidy(dsp.vari(x, rng.uniform(-1, 0)), 120)


@recipe("ui-prompts", "cancel", "slip", "Let go too soon: the pawl slipping back",
        """The ratchet's clicks run backwards and falling in pitch over a quarter second, then nothing: the mechanism
        letting go.""",
        sources=[S("misc_20")], takes=2, lufs=-28, gap=0.5)
def hold_cancel(rng, k):
    x = dsp.trim_silence(src.get(S("misc_20")), -35)
    on = _onsets(x, -14)
    b = Bus(0.4)
    for i in range(4 + k):
        a = on[i % len(on)]
        c = dsp.vari(fade(x[a:a + samples(0.04)], 0.0005, 0.015), -1.5 * i)
        b.at(i * (0.045 + 0.012 * i), c, -2 * i)
    return tidy(b.x, 300)


# ---- Run end and the dead phase ------------------------------------------------------------------------------------------

@recipe("ui-run-end", "report", "carriage", "The incident report comes up: a form wound into the typewriter",
        """A sheet slid in (a page flip slowed) and the platen's ratchet wound three clicks (misc_20), then the carriage
        thrown with a short metal slide and a bell-like ding (a light glass impact, pitched down).""",
        sources=[R("bookFlip1"), S("misc_20"), K("impactGlass_light", 2)], takes=1, lufs=-22)
def report(rng, k):
    b = Bus(1.4)
    b.at(0, dsp.vari(cut(R("bookFlip1"), 0, 0.3), -3))
    x = dsp.trim_silence(src.get(S("misc_20")), -35)
    on = _onsets(x, -14)
    for i in range(3):
        b.at(0.35 + i * 0.11, fade(x[on[i % len(on)]:on[i % len(on)] + samples(0.05)], 0.0005, 0.02), -3)
    b.at(0.85, dsp.vari(cut(K("impactGlass_light", 2), 0, 0.4), -5), -6)
    return tidy(b.x, 150)


@recipe("ui-run-end", "tally", "keys", "A line of the report typed in: a quick run of keys",
        """Four to seven strikes cut from the typewriter-like recordings, fired at a typist's uneven pace.""",
        sources=[S("misc_15"), S("misc_13")], takes=4, lufs=-24, gap=0.4)
def tally(rng, k):
    srcs = [dsp.trim_silence(src.get(s), -40) for s in (S("misc_15"), S("misc_13"))]
    b = Bus(1.0)
    t = 0.0
    for i in range(4 + k):
        x = srcs[rng.integers(2)]
        on = _onsets(x)
        a = on[rng.integers(len(on))]
        b.at(t, fade(x[a:a + samples(0.06)], 0.0005, 0.02), rng.uniform(-3, 0))
        t += rng.uniform(0.07, 0.13)
    return tidy(b.x, 400)


@recipe("ui-run-end", "commendation", "stamp-bell", "A commendation: the stamp, and the station bell",
        """A rubber stamp coming down hard (a heavy soft impact with a book's slap on top), and a brass hand bell
        (Kenney's heavy bell, a short cut so it rings rather than tolls).""",
        sources=[K("impactSoft_heavy", 0), R("bookPlace1"), K("impactBell_heavy", 1), K("impactBell_heavy", 3)],
        takes=2, lufs=-20, gap=0.6)
def commendation(rng, k):
    stamp = mix(cut(K("impactSoft_heavy", 0), 0, 0.3), dsp.gain(cut(R("bookPlace1"), 0, 0.25), -4))
    bell = cut(K("impactBell_heavy", 1 + 2 * k), 0, 1.4, 0.001, 0.4)
    b = Bus(2.0)
    b.at(0, tidy(stamp, 60))
    b.at(0.28, dsp.gain(bell, -6))
    return b.x


@recipe("ui-dead-phase", "queue", "punch", "The queue moves up: one ticket punched",
        """The menu's ticket punch, softer and a little lower, so the dead phase speaks the same language.""",
        sources=[R("metalClick"), R("bookFlip2")], takes=2, lufs=-26, gap=0.45)
def queue(rng, k):
    return dsp.vari(menu_select(rng, k), -2)


@recipe("ui-dead-phase", "vote", "stamp", "A creature vote locked in: a stamp on the ballot",
        """The commendation's rubber stamp without the bell: a heavy soft impact and the paper's slap, dry.""",
        sources=[K("impactSoft_heavy", 1), R("bookPlace3")], takes=2, lufs=-22, gap=0.5)
def vote(rng, k):
    x = mix(cut(K("impactSoft_heavy", 1 + k), 0, 0.3), dsp.gain(cut(R("bookPlace3"), 0, 0.25), -4))
    return tidy(x, 60)


@recipe("ui-dead-phase", "bookmark", "fold", "A bookmark taken: a page corner folded down",
        """A short page flip and a soft book close, cut together: a corner creased and pressed flat.""",
        sources=[R("bookFlip3"), R("bookClose")], takes=2, lufs=-26, gap=0.45)
def bookmark(rng, k):
    b = Bus(0.4)
    b.at(0, cut(R("bookFlip3"), 0.0, 0.1))
    b.at(0.09 + 0.02 * k, dsp.gain(cut(R("bookClose"), 0, 0.15), -6))
    return tidy(b.x, 300)
