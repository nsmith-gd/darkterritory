"""G1's creatures of the train itself heard (queue #125, note 388; G1's requests #101, #102 and #104 on the audio
checklist), ahead of their sims: the Brakeman, the Knotter and Hotbox. Their tells (spec A.4) and the Brakeman's
cornered sounds. (The other three of G1's six live off the train: g1_ground.py.)

- The Brakeman, a corrupted railwayman. He walks the roofs car by car winding each car's handbrake on, and the drag
  stalls a climb; chased off, he hides and starts again elsewhere. A man's size and weight in iron-shod boots, lame in
  one leg, a heavy brake chain dragging behind him and a throat that's been crushed. Everything he does is railway iron
  and wood: the brake staff's pawl dropping tooth after tooth (1.5-5 kHz), the shoes binding on the turning wheels of
  the car he's wound (a grinding squeal, steady: never the hot box's dry catch once a wheel turn), his limping steps on
  the roof walk, the chain jerking along behind him in his gait; cornered, a wet wheeze, the chain gathered up into his
  hands and swung.
- The Knotter, a rope-bodied parasite. It takes a coupling at speed and its body becomes the coupling: a hawser of
  slimy living fibre between two cars 5 m apart. A rope under load creaks by stick-slip, strand on strand, and groans
  as the slip quickens; a wet one squeaks and squelches, and this one is alive, so its fibres tack and peel and pop as
  they stretch. The coupler's iron strains with it. Its creak is the 4 s warning, so it's built to be unmistakable:
  swells of a wet, rising creak in the rhythm of the load, a sound nothing else on the train makes.
- Hotbox, an axle parasite in one truck of a freight car. Its tell is a knock once a wheel turn of its axle: a hammer
  on a casting, heavy and dull (150-900 Hz). As it glows the knock goes ragged and wet and the box sizzles, something
  living cooking in the grease; when the axle seizes the wheel drags on the rail in a long grinding scrape. None of it
  may sound like the hot box (state-hotbox, a dry bearing's held squeal at 1.3-2.4 kHz catching once a turn): the knock
  is low and has no ring, the sizzle is wet and has no tone, the grind is noise with a judder in it.

Iron, wood and chain come from the packs' real recordings where they have them (the ratchet's and a lock's clicks,
heavy metal and plate hits, wood knocks, keys jangling for links, the metal scrape, door creaks) and are modelled where
they don't (a casting's modes, stick-slip through a rope's body, a wheel's squeal, a throat). Each cue has a candidate
of each kind, so the two compare as recording against model. Loops are built periodic (events placed round the cycle,
textures crossfaded into their own heads, circular filters and reverb), so build.py's seam gives the cycle back
exactly. Nothing is left under 120 Hz: the weight is in the mids, never a boom.
"""

import numpy as np
from scipy import signal

import dsp
import src
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.beasts import fry
from recipes.crew_items import hit_of
from recipes.gannet_tells import bend
from recipes.horrors import finish
from recipes.moose_tells import held
from recipes.stress import strongest

SR = dsp.SR

# ---- Bands (spec A.4: each tell held where the train's bed, heavy at 60 Hz-1 kHz, can't cover it) ------------------------

RATCHET = (1500, 5000)      # the enemy designer's: the pawl's clicks
SQUEAL = (1500, 7000)       # the shoes binding: the wheel's squeal and the grind round it
STEP = (600, 5000)          # the iron on the boot and the board's knock above its boom
CHAIN = (800, 6000)         # links on links and on the planks
CREEP = (400, 4500)         # the rope's creak and its wet
STRETCH = (300, 4000)       # the groan, the buffers parting and the clanks running down the train
HAWSER = (350, 4000)
KNOCK = (150, 900)          # the enemy designer's: a hammer on a casting, nothing mostly under 120 Hz
SIZZLE = (150, 7000)        # the ragged knock and the sizzle over it
GRIND = (300, 5000)

# ---- Sources ---------------------------------------------------------------------------------------------------------

RATCHET_REC = "sfx_100_v2:misc_20"              # a real ratchet and pawl: two teeth
LOCK = "sfx_100_v2:lock_open_01"                 # a real lock worked: five clicks of its pawl
KEYS = src.S("misc_09", "wood_03", "wood_02")    # keys jangling: small links, pitched down into a brake chain's
METAL_H = src.K("impactMetal_heavy")             # heavy metal struck
PLATE_H = src.K("impactPlate_heavy")             # heavy plate struck: iron's mass
SLAMS = src.S("metal_hit_01", "misc_36")         # a real metal door slammed, a heavy knock-slam
SCRAPE = "sfx_100_v2:misc_10"                    # a real metal scrape, a few strokes
GRIT = "sfx_100_v2:stones_03"                    # stones scraping and crackling

# ---- Bodies (Hz) ------------------------------------------------------------------------------------------------------

CASTING = [1.0, 1.48, 2.11, 2.63, 3.42, 4.27, 5.36]   # a thick casting's modes as ratios: close, inharmonic (jobs.py)
SHOE = [1640, 2390, 3180, 3870, 4730, 5600]          # a freight wheel's tread and web under a shoe: what friction rings
LINK = (1400, 4200)                                  # a 3/4-inch brake chain's links knocking: their rings


# ---- Shared parts ----------------------------------------------------------------------------------------------------

def norm(x):
    return W.norm(x)


def done(x, lo=120):
    """A one-shot's last step: a steep highpass, so the weight stays in the mids and nothing turns to boom or DC."""
    return hp(np.asarray(x, np.float32), lo, 4)


def peaks(x, lufs, ceiling=-4.5):
    """Level a spiky take to `lufs` with its loudest peaks pulled under the ceiling by a look-ahead limiter
    (horrors.finish), clear of build.py's soft knee, so no click is bent into a broadband smear."""
    return finish(x, lufs, ceiling)


def air(x, rng, wet=0.08, tail=0.35):
    """On a car roof at night: a little of the open country's reverb, the tail cut short so a one-shot fired again
    doesn't pile up a wash."""
    x = dsp.trim_silence(x, -60)
    y = dsp.room(x, "night", wet=wet, rng=rng)
    return dsp.fade(y[:len(x) + samples(tail)], 0.0, tail)


def loopify(x, n, xf=0.4):
    """A texture made longer than its cycle joined to itself: what ran past n crossfaded (equal power) over the head,
    so the cycle's last sample runs on into its first."""
    k = samples(xf)
    y = np.asarray(x[:n], np.float32).copy()
    r = np.linspace(0, 1, k, dtype=np.float32)
    y[:k] = y[:k] * np.sqrt(r) + x[n:n + k] * np.sqrt(1 - r)
    return y


def ext(c, n, xf=0.4):
    """A periodic curve of n samples run on past its end (for a texture loopify will join)."""
    return np.concatenate([c, c[:samples(xf)]]).astype(np.float32)


def cband(x, lo, hi, order=4):
    """dsp.band for loops: circular and zero phase, so holding a loop to its band keeps it a loop."""
    return W.cfilter(x, lambda f: 1 / np.sqrt(1 + (lo / f) ** (2 * order)) / np.sqrt(1 + (f / hi) ** (2 * order)))


def chold(x, lo, hi, drive=4):
    """moose_tells.held for a loop: band, its peaks rounded off, band again, all circular."""
    return cband(dsp.saturate(norm(cband(x, lo, hi)), drive), lo, hi)


def close(y, rng, band=None, wet=0.1, drive=4, lufs=-20.0):
    """A loop's last step: the night's reverb round the cycle, held to its band, its loudest peaks limited (round
    three cycles, keeping the middle, so the limiter's gain loops too), handed to build.py seamless."""
    y = W.croom(np.asarray(y, np.float32), "night", wet, rng)
    y = chold(y, *band, drive=drive) if band else W.cfilter(y, lambda f: (f / 120) ** 4 / (1 + (f / 120) ** 4))
    n = len(y)
    return W.seamless(peaks(np.tile(y, 3), lufs)[n:2 * n])


def judder(n, rng, lo, hi):
    """A fast, uneven flutter between lo and hi Hz, 0-1 and periodic over n: a contact catching and slipping too fast
    to hear as beats, heard as roughness (a grind's growl)."""
    j = np.abs(W.cfilter(W.pnoise(n, rng), lambda f: ((f / lo) ** 4 / (1 + (f / lo) ** 4)) / (1 + (f / hi) ** 4)))
    j = W.cfilter(j, lambda f: 1 / (1 + (f / hi) ** 2))
    return np.clip(j / (np.percentile(j, 98) + 1e-9), 0, 1).astype(np.float32)


def tuned(key, target, lo=1000, hi=4000, i=0, length=0.3):
    """A real hit varispeeded so its loudest partial between lo and hi sits at `target` Hz."""
    x = hit_of(key, i, length)
    return dsp.vari(x, 12 * np.log2(target / strongest(x[:samples(0.2)], lo, hi)))


def links(rng, length, rate, f=LINK, q=(8, 18), size=(0.015, 0.045)):
    """Heavy chain moving: link on link, each knock a little ring of iron, Poisson at `rate` a second (a constant or a
    curve), a few loud and many quiet."""
    n = samples(length)
    rc = synth.curve(rate, n)
    out = np.zeros(n + samples(0.06), np.float32)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        a = samples(t)
        if a >= n:
            break
        lf = np.exp(rng.uniform(np.log(f[0]), np.log(f[1])))
        c = synth.click(lf, q=rng.uniform(*q), length=rng.uniform(*size), rng=rng)
        out[a:a + len(c)] += c * min(1.0, 0.06 * rng.pareto(2.5) + 0.15)
    return out[:n]


# ---- The Brakeman: the ratchet (1.5-5 kHz) ----------------------------------------------------------------------------

# Each take is the wheel wound on in pulls, hand over hand: (teeth, seconds a tooth) for each pull, the chain taking up
# as it goes, so later pulls click slower and heavier.
PULLS = [((4, 0.07), (3, 0.085), (3, 0.105)),
         ((6, 0.055), (3, 0.09), (2, 0.13)),
         ((4, 0.075), (3, 0.09), (3, 0.115))]


def winding(rng, k, tooth, chain, taut):
    """Lay a take's pulls out: each tooth (tooth(rng, weight)) at the pull's pace, a little slower at its ends where the
    hand starts and finishes, a short re-grip between pulls; the chain (chain(rng, length)) under each pull; the last
    pull straining taut (taut(rng, length))."""
    b = Bus(2.2)
    t = 0.02
    pulls = PULLS[k % 3]
    for p, (teeth, dt) in enumerate(pulls):
        start = t
        weight = 0.65 + 0.35 * p / (len(pulls) - 1)
        for i in range(teeth):
            edge = 1 + 0.25 * (abs(i - (teeth - 1) / 2) / max(1, (teeth - 1) / 2)) ** 2
            w = weight * rng.uniform(0.85, 1.0) * (1.15 if (p == len(pulls) - 1 and i == teeth - 1) else 1)
            b.at(t, tooth(rng, w), 0)
            t += dt * edge * rng.uniform(0.93, 1.07)
        b.at(start, chain(rng, t - start), -10 - 3 * p)
        if p == len(pulls) - 1:
            b.at(start, taut(rng, t - start + 0.1), -8)
        t += rng.uniform(0.1, 0.13)
    return b.x


def tooth_rec(rng, w):
    """One tooth from real recordings: the ratchet's or the lock's pawl dropping (cut to one click), pitched down four to
    seven semitones for an iron dog the size of a fist, and a real heavy metal hit under it for the cast wheel's mass,
    tuned so its ring sits round 2 kHz and choked at once (the dog stays down on the tooth)."""
    c = hit_of(RATCHET_REC, 0, 0.06) if rng.random() < 0.4 else hit_of(LOCK, int(rng.integers(5)), 0.05)
    c = dsp.vari(c, -rng.uniform(4, 7))
    mass = tuned(METAL_H[int(rng.integers(5))], rng.uniform(1800, 2300))
    mass = ck.choke(mass, 0.008, 0.025)
    return mix(norm(c), norm(mass) * 0.55 * w) * w


def chain_rec(rng, length):
    """The chain winding onto the staff's drum: links knocking, slices of real keys jangling pitched down into heavy
    links, strewn along the pull."""
    b = Bus(length + 0.15)
    for t in np.sort(rng.uniform(0, length, int(3 + 6 * length))):
        x = ck.get(KEYS[int(rng.integers(3))])
        h = ck.hits(x, floor_db=-24, gap=0.02)
        a = h[int(rng.integers(len(h)))][0] if h else 0
        g = dsp.vari(ck.cut(x, a, a + samples(0.06), 0.001, 0.02), -rng.uniform(5, 8))
        b.at(t, norm(g), -rng.uniform(0, 9))
    return b.x


def taut(rng, length):
    """The chain coming taut round the staff on the last pull: iron creaking under strain, the slip quickening."""
    body = [f * rng.uniform(0.95, 1.05) for f in (1150, 1720, 2460, 3350)]
    c = synth.creak(length, env([(0, 18), (length, 55)], length), rng, body=body, q=12, jitter=0.4, grit=0.5)
    return c * env([(0, 0), (length * 0.4, 1), (length, 0.2)], length)


@recipe("tell-brakeman", "ratchet", "pawl",
        "A car's brake wheel wound on, from real recordings: the pawl's heavy click-click-click, the chain taking up",
        """Hand over hand in three pulls: each tooth is a real ratchet's or lock's pawl dropping, pitched down four to
        seven semitones for an iron dog the size of a fist, with a real heavy metal hit choked under it, tuned so the
        cast wheel's ring sits round 2 kHz. The chain winds onto the staff under each pull (real keys jangling, cut into
        links and pitched down into heavy ones); the pulls come slower and heavier as it takes up, and the last one
        strains taut (an iron creak). 1.4-1.5 s. Held to 1.5-5 kHz.""",
        sources=[RATCHET_REC, LOCK] + METAL_H + KEYS, takes=3, band=RATCHET, lufs=-22)
def ratchet_pawl(rng, k):
    y = winding(rng, k, tooth_rec, chain_rec, taut)
    return peaks(held(air(done(y, 300), rng, 0.07, 0.12), *RATCHET, drive=4), -22)


def tooth_cast(rng, w):
    """One tooth, modelled: the dog riding up the tooth's slope (a short scrape, brightening as it climbs), then
    dropping off its edge onto the root, hard iron on iron struck through the ratchet and dog's cast modes (close and
    inharmonic, the high ones dying in hundredths of a second: cast iron clanks), with the wheel's rim ringing faintly."""
    ride = rng.uniform(0.02, 0.035)
    b = Bus(ride + 0.2)
    scr = ck.friction(rng, ride, 700, 1500, 6000, 0.5, env([(0, 0), (ride * 0.8, 1), (ride, 0.2)], ride))
    b.at(0, scr, -18)
    f1 = rng.uniform(1500, 1700)
    fs = [f1 * r * rng.uniform(0.97, 1.03) for r in CASTING]
    am = [rng.uniform(0.5, 1) / (1 + 0.35 * i) for i in range(len(fs))]
    clack = W.modal(fs, [0.045 / (1 + 0.5 * i) for i in range(len(fs))], am, 0.15, rng, contact=0.00025)
    b.at(ride, norm(clack) * w, 0)
    rim = W.body(rng, rng.uniform(2050, 2200), ratios=[1.0, 1.52, 2.27, 2.95], decay=0.12, damp=0.5, length=0.2, contact=0.0003)
    b.at(ride, norm(rim) * w, -14)
    return b.x


def chain_cast(rng, length):
    """The chain winding on, modelled: links knocking as they're drawn onto the drum, quickest mid-pull."""
    return norm(links(rng, length, env([(0, 20), (length * 0.5, 70), (length, 25)], length))) * 0.8


@recipe("tell-brakeman", "ratchet", "dog",
        "A car's brake wheel wound on, modelled: each tooth a scrape up the slope and the iron dog's clack down",
        """Modelled from the brake staff's ratchet: each tooth is the dog riding up the slope (a short brightening scrape)
        and dropping off the edge onto the root, iron on iron struck through a casting's close, inharmonic modes from
        1.6 kHz (dying in hundredths of a second: cast iron clanks), with the hand wheel's rim ringing faintly. Three
        pulls, slower and heavier as the chain takes up (links knocking onto the drum), the last one straining taut. A
        'zrk-tak' each tooth where 'pawl' is a plain click. 1.4-1.5 s. Held to 1.5-5 kHz.""",
        takes=3, band=RATCHET, lufs=-22)
def ratchet_dog(rng, k):
    y = winding(rng, k, tooth_cast, chain_cast, taut)
    return held(air(done(y, 300), rng, 0.07, 0.12), *RATCHET, drive=3)


# ---- The Brakeman: the shoes' squeal (1.5-7 kHz) --------------------------------------------------------------------

SQ_LOOP = 12.0


def tones(n, rng, freqs, wander=0.004, rate=1.5, harm=0.3, level=0.15):
    """A wheel's modes locked into squeal by the friction on it: near-pure tones with their harmonic, each pitch
    shivering a little and its level wandering a little, never letting go. Whole cycles over n, so they loop."""
    out = np.zeros(n, np.float32)
    for i, f in enumerate(freqs):
        fi = f * (1 + wander * W.slow(n, rate, rng) + 0.3 * wander * W.slow(n, 20, rng))
        c = np.sum(fi) / SR
        fi = fi * (max(1, round(c)) / c)
        ph = 2 * np.pi * np.cumsum(fi) / SR
        lvl = np.clip(1 + level * W.slow(n, 0.5, rng), 0.5, 1.5)
        out += ((np.sin(ph) + harm * np.sin(2 * ph + 0.7)) * lvl / (1 + 0.4 * i)).astype(np.float32)
    return out


@recipe("tell-brakeman", "squeal", "shoe",
        "The brake shoes binding on a turning wheel, modelled: a grinding roar with a harsh squeal held in it",
        """Cast-iron shoes wound hard onto the tread: the contact's broadband grind (noise roughened by the asperities
        passing, rung through a freight wheel's modes from 1.6 kHz up), a judder of stick-slip through it too fast to
        hear as beats (the grind's growl), and the wheel's modes locked into squeal at 3.2, 3.4 and 4.6 kHz (several
        shoes on several wheels, the close two beating into a screech). Steady, binding, never letting go: nothing
        catches once a turn, as the hot box does, and the squeal sits more than an octave over its. 12 s exact cycle. Held to
        1.5-7 kHz.""",
        loop=True, takes=1, band=SQUEAL, seconds=SQ_LOOP)
def squeal_shoe(rng, k):
    n = samples(SQ_LOOP)
    grind = norm(W.friction(n, rng, SHOE, rough=2.8, grit=0.9, q=9, loop=True))
    sq = norm(tones(n, rng, [3240, 3420, 4630], wander=0.006, rate=1.2, harm=0.35))
    bite = np.clip(0.8 + 0.2 * W.slow(n, 0.25, rng), 0.55, 1)     # the shoe biting harder and easing, slowly
    j = judder(n, rng, 60, 140)
    y = (grind * 0.6 * (1 - 0.45 * j) + sq * 0.65 * (1 - 0.25 * j)) * bite
    return close(y, rng, SQUEAL, wet=0.1, drive=5)


def scrape_grains(n, rng, rate, semis=(-4, -1), size=(0.06, 0.16), key=SCRAPE, gain=(0.4, 1.0)):
    """A real scrape cut into grains from its strokes and laid end over end round a cycle of n samples (wrapping), so it
    never stops dragging: `rate` grains a second."""
    x = ck.get(key)
    strokes = [s for s, _ in ck.hits(x, floor_db=-18, gap=0.03)] or [0]
    ev = []
    for t in W.poisson(n / SR, rate, rng):
        a = strokes[int(rng.integers(len(strokes)))] + samples(rng.uniform(0.0, 0.05))
        g = dsp.vari(x[a:a + samples(rng.uniform(*size))], rng.uniform(*semis))
        ev.append((t, g * np.hanning(len(g)).astype(np.float32), rng.uniform(*gain)))
    return W.place(n, ev)


@recipe("tell-brakeman", "squeal", "bind",
        "The brake shoes binding, from a real scrape: metal dragged without end, rung into a squeal through the wheel",
        """A real metal scrape cut into grains from its strokes and laid end over end so it never stops dragging, pitched
        down a little for cast iron on steel; the same drag rung through a freight wheel's modes (high-Q resonances at
        3.2, 3.9 and 4.7 kHz) so the scrape itself squeals, harsh and gritty, as a binding shoe does; the stick-slip
        judder through it. Rougher and rawer than 'shoe'; steady, with no catch once a turn (the hot box's). 12 s exact
        cycle. Held to 1.5-7 kHz.""",
        sources=[SCRAPE], loop=True, takes=1, band=SQUEAL, seconds=SQ_LOOP)
def squeal_bind(rng, k):
    n = samples(SQ_LOOP)
    drag = scrape_grains(n, rng, 110, gain=(0.7, 1.0))
    # its strokes' swells flattened: it never lets up
    drag = norm(drag / (W.cfilter(np.abs(drag), lambda f: 1 / (1 + (f / 3) ** 2)) + 0.05 * np.max(np.abs(drag))))
    rung = W.cyclic(lambda x: dsp.resonate(x, [3180, 3870, 4730], q=70, gains=[1, 0.7, 0.5]), drag)
    j = judder(n, rng, 50, 120)
    y = drag * 0.6 * (1 - 0.4 * j) + dsp.saturate(norm(rung), 4) * 0.5
    return close(y, rng, SQUEAL, wet=0.1, drive=5)


# ---- The Brakeman: his steps (600 Hz-5 kHz) ---------------------------------------------------------------------------

DECK = src.S("wood_hit_02", "door_03", "footstep_wood_03", "footstep_wood_04")   # boards knocked: the roof walk
METAL_M = src.K("impactMetal_medium")
ROOF = [150, 205, 280, 385, 520]       # a boxcar's roof, planks over its hollow: broad low modes, kept off the sub
PLANK = [1.0, 2.29, 4.18, 7.09, 11.6]  # a roof-walk plank's modes as ratios (synth.WOOD's)
LAME = (1, 3)                          # the takes that are his bad leg's
LIMP = (0.74, 0.46)                    # his gait: a long stance on the good leg, a quick one on the bad (s)


def hobnails(rng, n=3, spread=0.007, f=(2600, 5200)):
    """A boot's iron hobnails meeting the planks a few milliseconds apart: hard little ticks."""
    b = Bus(spread + 0.03)
    for _ in range(n):
        b.at(rng.uniform(0, spread), synth.click(rng.uniform(*f), q=rng.uniform(6, 12), length=0.012, rng=rng), rng.uniform(-8, 0))
    return b.x


def board_creak(rng, length, rate=(30, 70)):
    """The roof walk's plank giving under his weight: stick-slip through the board where it's nailed."""
    body = [m * rng.uniform(1.3, 1.6) for m in synth.WOOD]
    c = synth.creak(length, env([(0, rate[0]), (length, rate[1])], length), rng, body=body, q=14, jitter=0.45)
    return c * env([(0, 0), (length * 0.25, 1), (length, 0)], length)


def toe_drag(rng, length):
    """The bad foot's iron toe dragged along the planks before it's planted: a scrape that judders, hobnails catching."""
    scr = ck.friction(rng, length, 260, 700, 5000, 0.9, env([(0, 0), (length * 0.3, 1), (length * 0.85, 0.7), (length, 0)], length))
    nails = synth.skitter(length, 30, rng, f=(2500, 5000), q=(6, 12), legs=4, shape=env([(0, 0.3), (length, 1)], length))
    return mix(scr, norm(nails) * 0.4)


def step(rng, k, plant, heel):
    """One footfall. The good leg: the iron heel plate strikes and the board takes his weight (plant(rng, weight)),
    the toe comes down after it, the plank creaks. The bad leg (LAME): the toe dragged along the planks first, then
    planted flat-footed and heavy, heel and toe at once, with a lurch."""
    b = Bus(1.0)
    if k in LAME:
        d = rng.uniform(0.17, 0.24)
        b.at(0, toe_drag(rng, d), -9)
        t = d - 0.01
        b.at(t, hobnails(rng, 4, 0.01), -6)
        b.at(t, heel(rng, 0.8), -3)
        b.at(t + 0.004, plant(rng, 1.15), 0)
        b.at(t + 0.06, board_creak(rng, 0.22, (25, 80)), -12)
    else:
        b.at(0, hobnails(rng, 3), -5)
        b.at(0.001, heel(rng, 1.0), 0)
        b.at(0.002, plant(rng, 1.0), 0)
        toe = rng.uniform(0.07, 0.1)
        b.at(toe, hobnails(rng, 2, 0.004), -10)
        b.at(toe, plant(rng, 0.45), -9)
        b.at(toe + 0.03, board_creak(rng, 0.16), -14)
    return b.x


def limp(takes, rng):
    """The preview: his gait across a roof, good leg and bad by turns, a long stance on the good one and a quick one
    on the bad."""
    seq = [0, 1, 2, 3, 0, 3, 2, 1, 0, 1, 2, 3]
    b = Bus(len(seq) * 0.62 + 1.2)
    t = 0.2
    for i, k in enumerate(seq):
        lead = samples(0.2) if k in LAME else 0          # the bad leg's take starts with its drag: land it on the beat
        b.at(t - lead / SR, takes[k], 0 if k not in LAME else -1)
        t += LIMP[i % 2] * rng.uniform(0.95, 1.05)
    return b.x


def deck_rec(rng, w):
    """The roof walk taking his weight, from real recordings: a board knocked (real wood hits and a door's thunk),
    pitched down a little and choked (his weight stays on it), with the car's hollow under it."""
    x = dsp.vari(ck.align(ck.get(DECK[int(rng.integers(len(DECK)))])), rng.uniform(-2, 0.5))
    x = ck.choke(norm(hp(x, 250)), 0.03, 0.04)
    return mix(norm(x), ck.hollow(rng, ROOF, 0.18, q=5) * 0.3) * w


def heel_rec(rng, w):
    """The iron heel plate: a real medium metal hit tuned so it rings round 2.2 kHz, choked at once by the board."""
    return norm(ck.choke(tuned(METAL_M[int(rng.integers(5))], rng.uniform(1900, 2500)), 0.004, 0.012)) * w


@recipe("tell-brakeman", "step", "hob",
        "One iron-shod footfall on the roof walk, from real recordings: the good leg's, or the bad one dragged and planted",
        """His iron heel plate (a real metal hit tuned to ring round 2.2 kHz, choked by the board at once) and hobnails
        ticking, on a real board's knock pitched down a little with the car's hollow under it, the toe coming down after
        and the plank creaking. Takes 1 and 3 are his bad leg: the iron toe dragged juddering along the planks first,
        then planted flat and heavy. Fired in his gait (the preview: long on the good leg, quick on the bad), it limps.
        Held to 600 Hz-5 kHz.""",
        sources=DECK + METAL_M, takes=4, band=STEP, lufs=-22, preview=limp)
def step_hob(rng, k):
    return peaks(dsp.band(air(done(step(rng, k, deck_rec, heel_rec), 250), rng, 0.08, 0.2), *STEP, order=2), -22)


def deck_model(rng, w):
    """The roof walk taking his weight, modelled: a plank's modes from about 230 Hz (dull: a boot is soft against a
    board, ~1.5 ms of contact) and the car's hollow under it."""
    y = W.body(rng, rng.uniform(215, 250), ratios=PLANK, decay=0.07, damp=1.4, length=0.25, contact=0.0015, tilt=0.4)
    return mix(norm(y), ck.hollow(rng, ROOF, 0.18, q=5) * 0.3) * w


def heel_model(rng, w):
    """The iron heel plate, modelled: a small plate's modes struck hard (0.2 ms), damped by the wood in a few
    hundredths of a second."""
    f = rng.uniform(1850, 2300)
    y = W.modal([f, f * 1.58, f * 2.21, f * 2.9], [0.018, 0.012, 0.008, 0.006], [1, 0.7, 0.5, 0.3], 0.06, rng, contact=0.0002)
    return norm(y) * w


@recipe("tell-brakeman", "step", "heel",
        "One iron-shod footfall on the roof walk, modelled: an iron heel plate and hobnails on a plank over a hollow",
        """Modelled: a small iron plate struck hard (his heel plate, ringing round 2 kHz and damped by the wood at once)
        and hobnail ticks, on a roof-walk plank's modes from about 230 Hz with the car's hollow booming under it (kept
        off the sub), the toe after and the plank creaking where it's nailed. Takes 1 and 3 are his bad leg, the iron
        toe dragged juddering along the planks and then planted flat and heavy. Fired in his gait it limps (the preview).
        Held to 600 Hz-5 kHz.""",
        takes=4, band=STEP, lufs=-22, preview=limp)
def step_heel(rng, k):
    return peaks(dsp.band(air(done(step(rng, k, deck_model, heel_model), 300), rng, 0.08, 0.2), *STEP, order=2), -22)


# ---- The Brakeman: the chain dragged behind him (800 Hz-6 kHz) --------------------------------------------------------

CH_LOOP = 12.0
STRIDE = sum(LIMP)                     # 1.2 s: ten strides a cycle


def gait_pull(n, rng):
    """How hard the chain's being pulled, 0-1 over the cycle: a jerk as each foot lands and his body lurches on (the
    good leg's stronger), easing as it slides, never quite still."""
    out = np.zeros(n, np.float32)
    for i in range(int(round(CH_LOOP / STRIDE))):
        for j, (at, a) in enumerate(((0.0, 1.0), (LIMP[0], 0.6))):
            r, f = 0.05, rng.uniform(0.3, 0.42)
            e = env([(0, 0), (r, a * rng.uniform(0.85, 1.0)), (r + f, 0)], r + f) ** 1.5
            out = np.maximum(out, W.place(n, [(i * STRIDE + at + rng.uniform(0.03, 0.06), e, 1.0)]))
    return np.clip(0.12 + 0.88 * out, 0, 1)


def plank_tok(rng, w=1.0):
    """A link landing on a plank: a small hard wooden knock, iron on wood."""
    y = W.body(rng, rng.uniform(880, 1150), ratios=PLANK[:4], decay=0.03, damp=1.0, length=0.08, contact=0.0006)
    return norm(y) * w


def events_at(rng, pull, rate, make, length):
    """Events round the cycle at up to `rate` a second, thinned by the pull (more as it jerks), each make(rng, pull)."""
    n = len(pull)
    ev = []
    for t in W.poisson(length, rate, rng):
        p = pull[min(samples(t), n - 1)]
        if rng.random() < p:
            ev.append((t, make(rng, p), 1.0))
    return W.place(n, ev)


@recipe("tell-brakeman", "chain", "links",
        "A heavy chain dragged along the roofs behind him, modelled: links knocking and sliding, jerked in his limp",
        """A brake chain's links knocking on each other (each a little ring of iron, 1.4-4.2 kHz) and landing on the
        roof walk's planks (small hard wooden knocks), sliding over the boards between (a friction drag through the
        planks), jerked forward as each of his feet lands: a strong jerk off the good leg, a weaker one off the bad, so
        the chain limps with him (1.2 s a stride). 12 s exact cycle. Held to 800 Hz-6 kHz.""",
        loop=True, takes=1, band=CHAIN, seconds=CH_LOOP)
def chain_links(rng, k):
    n = samples(CH_LOOP)
    pull = gait_pull(n, rng)
    ln = loopify(links(rng, CH_LOOP + 0.4, ext(15 + 190 * pull ** 1.3, n)), n)
    tok = events_at(rng, pull, 22, lambda r, p: plank_tok(r, 0.4 + 0.6 * p), CH_LOOP)
    slide = norm(W.friction(n, rng, [760, 1150, 1700, 2450, 3350], rough=1.6, grit=0.6, q=7, loop=True)) * (0.25 + 0.75 * pull)
    y = norm(ln) + norm(tok) * 0.2 + slide * 0.2
    return close(y, rng, CHAIN, wet=0.1, drive=4)


def key_grain(rng, semis=(-9, -5), length=0.07):
    """A slice of real keys jangling from one of its hits, pitched down into a heavy chain's links (the handling's
    thump under it, pitched down with it, taken off)."""
    x = ck.get(KEYS[int(rng.integers(len(KEYS)))])
    h = ck.hits(x, floor_db=-26, gap=0.015)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    return norm(hp(dsp.vari(ck.cut(x, a, a + samples(length), 0.001, 0.02), rng.uniform(*semis)), 500, 2))


WOOD_L = src.K("impactWood_light")     # a light wooden knock: a link landing on the planks


@recipe("tell-brakeman", "chain", "drag",
        "A heavy chain dragged along the roofs behind him, from real recordings: keys and a scrape, jerked in his limp",
        """Real keys jangling cut into slices at their hits and pitched down five to nine semitones into heavy links,
        strewn thick as the chain's jerked and thin as it slides; a real metal scrape in grains for the drag along the
        boards; real light wooden knocks pitched up for links landing on the planks. Jerked as each of his feet lands, a
        strong jerk off the good leg and a weaker one off the bad, so it limps with him (1.2 s a stride). 12 s exact
        cycle. Held to 800 Hz-6 kHz.""",
        sources=KEYS + [SCRAPE] + WOOD_L, loop=True, takes=1, band=CHAIN, seconds=CH_LOOP)
def chain_drag(rng, k):
    n = samples(CH_LOOP)
    pull = gait_pull(n, rng)
    jangle = events_at(rng, pull, 160, lambda r, p: key_grain(r) * (0.3 + 0.7 * p * r.uniform(0.4, 1)), CH_LOOP)
    tok = events_at(rng, pull, 20, lambda r, p: norm(ck.choke(dsp.vari(ck.align(ck.get(WOOD_L[int(r.integers(5))])),
                                                                         r.uniform(3, 6)), 0.01, 0.02)) * (0.4 + 0.6 * p), CH_LOOP)
    drag = norm(scrape_grains(n, rng, 40, semis=(-7, -4), gain=(0.5, 1.0))) * (0.2 + 0.8 * pull)
    y = norm(jangle) + norm(tok) * 0.4 + drag * 0.3
    return close(y, rng, CHAIN, wet=0.1, drive=4)


# ---- The Brakeman cornered: the wheeze --------------------------------------------------------------------------------

WZ_LOOP = 10.0
BREATHS = [1.45, 1.8, 1.35, 1.7, 1.6, 2.1]          # cornered, quick and uneven: six breaths a cycle (s each)
GASPS = src.S("misc_05", "misc_25")                   # a real gasp and sigh; a real groan and wail
ROAR = "sfx_100_v2:misc_24"


def rattle(rng, length, rate=(18, 30), lo=350, hi=1600):
    """Fluid in a ruined airway: wet crackles and small low bubbles bursting, gated in the breath's flutter."""
    fl = fry(rng, rng.uniform(*rate), length, jitter=0.4, decay=0.01)
    b = synth.bubbles(length, 70, lo, hi, rng, rise=(0.01, 0.08))
    c = synth.crackle(length, 240, rng, size=(0.0004, 0.003), hi=lo)
    return norm(norm(b) * 0.7 + norm(lp(c, hi * 2)) * 0.5) * (0.25 + 0.75 * fl[:samples(length)])


def stridor(rng, length, f, shape):
    """The wheeze itself: air whistling through a crushed windpipe, noise through a narrow resonance that wavers (the
    throat's walls fluttering) and climbs as the breath is dragged in harder, a fainter one over it."""
    n = samples(length)
    wob = 1 + 0.04 * lp(rng.standard_normal(n).astype(np.float32), 8) * 6
    fc = f * dsp.fit(env([(0, 0.9), (length * 0.6, 1.08), (length, 1.0)], length), n) * wob
    a = dsp.sweep_filter(synth.noise(length, rng), "bp", fc, q=rng.uniform(16, 24), block=64)
    b = dsp.sweep_filter(synth.noise(length, rng), "bp", fc * rng.uniform(1.45, 1.6), q=14, block=64)
    return norm(norm(a) + norm(b) * 0.35) * dsp.fit(env(shape, length), n)


def breath_in(rng, L):
    """Dragged in through the ruin: rough air through a man's throat, the whistle in it, the wet rattling."""
    shape = [(0, 0), (L * 0.2, 0.8), (L * 0.75, 1), (L, 0)]
    sh = dsp.fit(env(shape, L), samples(L))
    air_ = synth.breath(L, [(0, "h"), (L * 0.6, "u")], "man", rng, sh)
    whistle = stridor(rng, L, rng.uniform(640, 820), shape)
    return norm(bp(air_, 300, 4500)) * 0.6 + whistle * 0.8 + rattle(rng, L) * 0.5 * sh


def breath_out(rng, L):
    """Let out: a low voice under it that can't hold (a broken glottis in fry, period-doubled and rough), breathy,
    gurgling with what's in the throat."""
    f0 = rng.uniform(78, 92)
    pts = [(0, f0 * 1.1), (L * 0.3, f0), (L, f0 * 0.82)]
    shape = [(0, 0), (0.06, 1), (L * 0.5, 0.75), (L, 0)]
    g = synth.glottis(env(pts, L, "exp"), L, rng, jitter=0.04, shimmer=0.35, sub=0.6, rough=0.6, oq=0.7)
    v = synth.tract(g, [(0, "o"), (L * 0.6, "a"), (L, "o")], 0.62, breath=0.9, rng=rng)
    v = norm(hp(v, 150)) * dsp.fit(env(shape, L), samples(L))
    return v * 0.75 + rattle(rng, L, (14, 22), 250, 1100) * 0.6 * dsp.fit(env(shape, L), samples(L))


@recipe("cs-brakeman", "wheeze", "throat",
        "Cornered: a low, wet wheeze through a crushed throat, quick and uneven, modelled",
        """Six breaths in a 10 s cycle, quick and uneven as he's cornered. Each dragged in through a crushed windpipe:
        rough air through a man's throat with a whistle in it (noise through a narrow wavering resonance at 650-800 Hz,
        climbing as he hauls the breath in) and fluid rattling (wet crackles and small bubbles bursting in the breath's
        flutter); then let out over a low broken voice in fry (about 85 Hz, period-doubled and rough), breathy and
        gurgling. Kept off the sub. 10 s exact cycle.""",
        loop=True, takes=1, lufs=-21, seconds=WZ_LOOP)
def wheeze_throat(rng, k):
    n = samples(WZ_LOOP)
    ev, t = [], 0.0
    for p in BREATHS:
        p *= WZ_LOOP / sum(BREATHS)
        li = p * rng.uniform(0.38, 0.45)
        lo = p * rng.uniform(0.42, 0.5)
        ev.append((t, breath_in(rng, li), 1.0))
        ev.append((t + li + rng.uniform(0.03, 0.08), breath_out(rng, lo), rng.uniform(0.75, 0.95)))
        t += p
    return close(W.place(n, ev), rng, wet=0.08)


def gasp_rec(rng, L, key, semis, start=0.0):
    """A slice of a real breath held out to L s (rubberband, smooth) and pitched down into a big man's throat."""
    x = dsp.trim_silence(src.get(key), -40)
    x = dsp.trim(x, start, min(len(x) / SR - start, 0.45))
    y = dsp.stretch(dsp.shift(x, semis), L / (len(x) / SR), smooth=True)
    return norm(dsp.fit(y, samples(L)))


@recipe("cs-brakeman", "wheeze", "gasp",
        "Cornered: a low, wet wheeze from real breaths, pitched down and held, whistling and gurgling",
        """Each breath in is a real gasp (sfx_100's gasp-and-sigh) pitched down five to seven semitones and held out to
        the breath's length, rung through a narrow wavering resonance so it whistles through a crushed windpipe; each
        breath out is a real sigh-and-groan pitched down an octave and more, a real roar under it smeared low (the voice
        that can't hold), both gurgling with fluid (wet crackles and small bubbles in the breath's flutter). Six quick,
        uneven breaths in a 10 s cycle. Kept off the sub.""",
        sources=GASPS + [ROAR], loop=True, takes=1, lufs=-21, seconds=WZ_LOOP)
def wheeze_gasp(rng, k):
    n = samples(WZ_LOOP)
    ev, t = [], 0.0
    for p in BREATHS:
        p *= WZ_LOOP / sum(BREATHS)
        li, lo = p * rng.uniform(0.38, 0.45), p * rng.uniform(0.42, 0.5)
        shape_i = [(0, 0), (li * 0.2, 0.8), (li * 0.75, 1), (li, 0)]
        si = dsp.fit(env(shape_i, li), samples(li))
        g = gasp_rec(rng, li, GASPS[0], -rng.uniform(5, 7))
        fw = rng.uniform(650, 800) * (1 + 0.04 * W.norm(lp(rng.standard_normal(len(g)).astype(np.float32), 8)))
        whistle = W.norm(dsp.sweep_filter(g, "bp", fw, q=10, block=64))
        inh = (norm(bp(g, 300, 5000)) * 0.5 + whistle * 0.8) * si + rattle(rng, li) * 0.4 * si
        shape_o = [(0, 0), (0.06, 1), (lo * 0.5, 0.75), (lo, 0)]
        so = dsp.fit(env(shape_o, lo), samples(lo))
        s_ = gasp_rec(rng, lo, GASPS[1], -rng.uniform(12, 14), 0.05)
        v = gasp_rec(rng, lo, ROAR, -rng.uniform(9, 11), 0.08)
        exh = (norm(hp(s_, 150)) * 0.6 + norm(hp(v, 150)) * 0.45) * so
        gurgle = fry(rng, rng.uniform(16, 24), lo, jitter=0.4)[:samples(lo)]
        exh = exh * (0.6 + 0.4 * gurgle) + rattle(rng, lo, (14, 22), 250, 1100) * 0.5 * so
        ev.append((t, inh, 1.0))
        ev.append((t + li + rng.uniform(0.03, 0.08), exh, rng.uniform(0.75, 0.95)))
        t += p
    return close(W.place(n, ev), rng, wet=0.08)


# ---- The Brakeman cornered: the chain gathered up, and its lash --------------------------------------------------------

def grip(rng):
    """The chain caught taut between his fists: a fistful of links knocking at once, a gloved hand slapping shut on
    them, the length between snapping straight (a short iron chunk)."""
    b = Bus(0.4)
    b.at(0, norm(links(rng, 0.03, 1500, f=(1300, 4200))), 0)
    b.at(0.004, W.norm(bp(synth.noise(0.06, rng, "pink"), 400, 3000) * env([(0, 1), (0.01, 0.4), (0.06, 0)], 0.06)), -6)
    f1 = rng.uniform(850, 1000)
    chunk = W.modal([f1 * r for r in CASTING[:5]], [0.05, 0.035, 0.025, 0.02, 0.015], [1, 0.8, 0.6, 0.4, 0.3], 0.15, rng,
                    contact=0.0005)
    b.at(0.012, norm(chunk), -5)
    return b.x


@recipe("cs-brakeman", "chain-up", "haul",
        "The chain rattling up off the roof into his hands, modelled: links quickening, caught taut",
        """Modelled: the chain dragged up off the roof walk (a scrape and a couple of links knocking on the planks), its
        links rattling faster and brighter as it comes up into the air hand over hand (link on link, quickening from 25
        to 350 knocks a second), the swing of its slack end through the air, and caught: a fistful of links knocking at
        once, a gloved hand slapping shut, the length between snapping taut with a short iron chunk. About a second.""",
        takes=2, lufs=-20)
def chainup_haul(rng, k):
    L = rng.uniform(0.6, 0.72)
    b = Bus(L + 0.6)
    b.at(0, ck.friction(rng, 0.18, 300, 600, 5000, 0.8, env([(0, 0), (0.03, 1), (0.18, 0)], 0.18)), -10)
    for t in (rng.uniform(0, 0.04), rng.uniform(0.06, 0.12)):
        b.at(t, plank_tok(rng), -15)
    rate = env([(0, 40), (L * 0.7, 350), (L, 220)], L, "exp")
    low = links(rng, L, rate, f=(1100, 3300)) * dsp.fit(env([(0, 1), (L, 0.3)], L), samples(L))
    high = links(rng, L, rate, f=(1800, 4800)) * dsp.fit(env([(0, 0.2), (L, 1)], L), samples(L))
    b.at(0.03, norm(low + high) * dsp.fit(env([(0, 0.35), (L, 1)], L), samples(L)), 0)
    b.at(L * 0.35, ck.whoosh(rng, L * 0.6, 250, 1800, 0.6), -12)
    b.at(L, grip(rng), 0)
    b.at(L + 0.03, norm(links(rng, 0.3, env([(0, 80), (0.3, 0)], 0.3))), -12)
    return peaks(air(done(b.x, 150), rng, 0.08, 0.3), -20)


def leather(rng):
    """A gloved hand closing: a real leather handling, its loudest moment."""
    return norm(hit_of("kenney_rpg-audio:handleSmallLeather", 0, 0.12))


@recipe("cs-brakeman", "chain-up", "gather",
        "The chain rattling up into his hands, from real recordings: keys jangling quickened and pitched down, caught",
        """Real keys jangling cut into slices and pitched down six to nine semitones into heavy links, coming quicker and
        quicker as the chain's hauled up off the roof (a real scrape for it leaving the boards), the slack swinging
        through the air, and caught: a real leather handling for the glove closing, a real heavy metal hit choked short
        for the links snapping taut, a few links settling. About a second.""",
        sources=KEYS + [SCRAPE, "kenney_rpg-audio:handleSmallLeather"] + METAL_H, takes=2, lufs=-20)
def chainup_gather(rng, k):
    L = rng.uniform(0.6, 0.72)
    b = Bus(L + 0.6)
    scr = dsp.vari(hit_of(SCRAPE, int(rng.integers(3)), 0.2), -rng.uniform(4, 6))
    b.at(0, norm(scr) * dsp.fit(env([(0, 1), (0.2, 0)], 0.2), len(scr)), -10)
    t, gap = 0.02, 0.06
    while t < L:
        b.at(t, key_grain(rng, (-9, -6), rng.uniform(0.04, 0.08)), -10 * (1 - t / L) - rng.uniform(0, 4))
        t += gap * rng.uniform(0.6, 1.3)
        gap = max(0.008, gap * 0.9)
    b.at(L * 0.35, ck.whoosh(rng, L * 0.6, 250, 1800, 0.6), -12)
    b.at(L, leather(rng), -3)
    taut_ = ck.choke(tuned(METAL_H[(k * 2 + 1) % 5], rng.uniform(1100, 1400), 600, 3000), 0.015, 0.03)
    b.at(L + 0.006, norm(taut_), 2)
    for i in range(4):
        b.at(L + 0.03 + i * rng.uniform(0.04, 0.08), key_grain(rng, (-8, -6), 0.05), -10 - 3 * i)
    return peaks(air(done(b.x, 150), rng, 0.08, 0.3), -20)


def swing(rng, L):
    """The chain swung: the air it tears (a band of noise climbing and falling with its speed) whirring with its links
    (gated by a flutter that quickens as it comes round), the links rattling along it."""
    n = samples(L)
    sp = dsp.fit(env([(0, 0.1), (L * 0.75, 1), (L, 0.6)], L), n) ** 1.5
    airb = dsp.sweep_filter(synth.noise(L, rng, "pink"), "bp", 220 * (2400 / 220) ** sp, q=1.1)
    whirr = fry(rng, 40 + 110 * sp, L, jitter=0.3, decay=0.005)[:n]
    y = norm(airb) * sp * (0.55 + 0.45 * whirr)
    y = y + norm(links(rng, L, 30 + 260 * sp)) * sp * 0.4
    return y


def strike(rng, wrap_=True):
    """The chain landing on someone: a dense slap of links all at once, the dull blow in the body under the coat (a
    short thud with its weight at 150-600 Hz, never a boom), the coat slapped, and its end wrapping round a moment
    after."""
    b = Bus(0.6)
    b.at(0, norm(links(rng, 0.035, 1600, f=(1200, 4500), size=(0.02, 0.06))), 0)
    thud = bp(synth.noise(0.12, rng, "pink"), 150, 700) * env([(0, 1), (0.025, 0.35), (0.12, 0)], 0.12)
    b.at(0.002, norm(thud), -3)
    b.at(0.002, synth.thump(rng.uniform(170, 200), 0.06, drop=0.3), -10)
    b.at(0.001, norm(bp(synth.noise(0.07, rng), 500, 4000) * env([(0, 1), (0.008, 0.4), (0.07, 0)], 0.07, "exp")), -6)
    if wrap_:
        b.at(rng.uniform(0.05, 0.08), norm(links(rng, 0.05, 700, f=(1300, 4000))), -7)
    b.at(0.06, norm(links(rng, 0.35, env([(0, 90), (0.35, 0)], 0.35))), -13)
    return b.x


@recipe("cs-brakeman", "lash", "whirl",
        "The chain's lash, modelled: whirring through the air and landing, a slap of links and a dull blow",
        """Modelled: the chain swung round (the air it tears, a band of noise climbing with its speed, whirring with
        its links and rattling), then landing: a dense slap of links all at once, a dull blow into a body under a coat
        (a short thud at 150-600 Hz, never a boom), the coat slapped, the end wrapping round a moment after, the links
        settling. 0.6-0.9 s.""",
        takes=3, lufs=-20)
def lash_whirl(rng, k):
    L = (0.34, 0.42, 0.3)[k] * rng.uniform(0.95, 1.05)
    b = Bus(L + 0.7)
    b.at(0, swing(rng, L), -6)
    b.at(L, strike(rng, k != 2), 0)
    return peaks(air(done(b.x, 150), rng, 0.08, 0.3), -20)


CLOTH = src.R("cloth1", "cloth2")       # heavy cloth whacked: a coat taking the blow
WHIP = src.S("switch_01", "switch_02")  # a switch cracking through the air


@recipe("cs-brakeman", "lash", "switch",
        "The chain's lash, from real recordings: a switch's whip pitched down, keys shaken, an iron slam into a coat",
        """Kitbashed: a real switch whipping through the air, pitched down five to seven semitones for a chain's weight,
        with real keys jangling shaken along it (pitched down into heavy links); the landing a real metal slam (sfx_100's
        door slam or knock-slam, choked short) into a real heavy cloth whack (a coat taking it), a fistful of links
        jangling as the end wraps round, settling. 0.6-0.9 s.""",
        sources=WHIP + KEYS + SLAMS + CLOTH, takes=3, lufs=-20)
def lash_switch(rng, k):
    L = (0.34, 0.42, 0.3)[k] * rng.uniform(0.95, 1.05)
    b = Bus(L + 0.7)
    w = dsp.vari(src.get(WHIP[k % 2]), -rng.uniform(5, 7))
    pk = int(np.argmax(np.abs(w)))
    b.at(L - pk / SR, norm(w), -3)
    t = 0.0
    while t < L:
        b.at(t, key_grain(rng, (-8, -5), 0.05), -22 + 12 * (t / L) ** 2)
        t += rng.uniform(0.015, 0.04)
    slam = ck.choke(dsp.vari(hit_of(SLAMS[k % 2], 0, 0.3), -rng.uniform(1, 3)), 0.03, 0.04)
    b.at(L, norm(hp(slam, 150)), 0)
    b.at(L, norm(hit_of(CLOTH[k % 2], 0, 0.2)), -3)
    for i in range(6):
        b.at(L + 0.02 + i * rng.uniform(0.03, 0.07), key_grain(rng, (-8, -5), 0.06), -9 - 2.5 * i)
    return peaks(air(done(b.x, 150), rng, 0.08, 0.3), -20)


# ---- The Knotter: the rope under load ---------------------------------------------------------------------------------

ROPE = [430, 650, 940, 1360, 1950, 2750]   # a hawser under load with the coupler it's knotted round for a sounding board
KNUCKLE = [f * 1.2 for f in synth.IRON[2:]]   # the coupler's knuckle and pin: iron, 560 Hz-4.4 kHz
CREAKS = src.R("creak1", "creak2", "creak3", "doorOpen_2")   # real creaks: a door's hinge, a floor's boards
SQUISH = ["kenney_rpg-audio:handleCoins"] + src.S("footstep_wet_01", "footstep_wet_03")   # a chewing squish, wet steps


def slips(rng, length, rate, jitter=0.12):
    """Stick-slip: an impulse each time the strands let go and catch again, `rate` a second (a constant or a curve),
    uneven by `jitter`, each slip a little different in size."""
    n = samples(length)
    rc = synth.curve(rate, n)
    x = np.zeros(n, np.float32)
    t = rng.uniform(0, 0.01)
    while True:
        a = samples(t)
        if a >= n:
            break
        x[a] = rng.uniform(0.4, 1.0)
        t += (1 / max(rc[a], 1.0)) * max(0.3, 1 + jitter * rng.standard_normal())
    return x


def rung(rng, x, modes, decay, spread=0.04, length=0.25):
    """Slips rung through a body: each one strikes its modes (decaying sines, the high ones dying first)."""
    fs = [f * (1 + spread * rng.standard_normal()) for f in modes]
    ir = W.modal(fs, [decay / (1 + 0.35 * i) for i in range(len(fs))], [1 / (1 + 0.25 * i) for i in range(len(fs))],
                 length, rng, contact=0.0004)
    return signal.fftconvolve(x, ir)[:len(x)].astype(np.float32)


def rope(rng, length, load, top=110, bottom=14, scales=(0.85, 1.0, 1.2), jitter=0.12, decay=0.03):
    """The Knotter's body creaking under load: strand on strand slipping (stick-slip at a rate that quickens with the
    load, `load` 0-1 per sample: a slow slip is a creak, a quick one a groan), each strand its own thickness and a
    little behind the last, each slip ringing the rope's broad, soft modes."""
    n = samples(length)
    load = synth.curve(load, n)
    out = np.zeros(n, np.float32)
    for sc in scales:
        ld = np.roll(load, samples(rng.uniform(0.0, 0.06)))
        rate = bottom + (top - bottom) * ld ** 1.4 * rng.uniform(0.85, 1.15)
        x = slips(rng, length, rate, jitter) * (0.08 + 0.92 * ld)
        out += rung(rng, x, [f * sc for f in ROPE], decay) * rng.uniform(0.6, 1.0)
    return norm(out)


def tack(rng, length, rate, f=(700, 2400)):
    """Slime between the fibres letting go as they stretch: wet little peels, each a short burst of noise through a
    resonance that jumps up as it parts, Poisson at `rate` a second (a constant or a curve), a few loud, many quiet."""
    n = samples(length)
    rc = synth.curve(rate, n)
    out = np.zeros(n + samples(0.05), np.float32)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        a = samples(t)
        if a >= n:
            break
        L = rng.uniform(0.006, 0.03)
        f0 = np.exp(rng.uniform(np.log(f[0]), np.log(f[1])))
        nz = synth.noise(L, rng)
        y = dsp.sweep_filter(nz, "bp", env([(0, f0 * 0.6), (L, f0 * 1.7)], L, "exp"), q=rng.uniform(5, 10), block=32)
        y = y * env([(0, 0), (0.001, 1), (L, 0)], L)
        out[a:a + len(y)] += y * min(1.0, 0.1 * rng.pareto(2.0) + 0.2)
    return out[:n]


def squish(rng, length=0.3, lo=380, hi=1700):
    """Slime squeezed out of the fibres as the load comes off: wet noise through a resonance that opens and closes,
    with fibres popping in it. No bubbles (they'd bloop)."""
    n = synth.noise(length, rng)
    fc = env([(0, lo), (length * rng.uniform(0.3, 0.5), hi), (length, lo * 1.2)], length, "exp")
    y = dsp.sweep_filter(n, "bp", fc, q=rng.uniform(3, 6), block=64)
    y = y * env([(0, 0), (0.02, 1), (length * 0.6, 0.5), (length, 0)], length)
    pops = synth.crackle(length, 300, rng, size=(0.0003, 0.002), hi=900) * env([(0, 1), (length, 0)], length)
    return norm(mix(norm(y), norm(lp(pops, 5000)) * 0.4))


def knuckle(rng, length, load):
    """The coupler's iron straining with it: a slow, heavy iron creak where the knuckle's faces bear, quickening with
    the load."""
    load = synth.curve(load, samples(length))
    x = slips(rng, length, 5 + 18 * load, 0.4) * (0.05 + 0.95 * load ** 1.5)
    return rung(rng, x, KNUCKLE, 0.05)


def surges(n, times, rng, rise=(0.45, 0.7), fall=(0.35, 0.55), floor=0.06):
    """The load on the coupling over a cycle, 0-1: a surge at each of `times` as the slack runs in and out, never
    quite slack. Returns the curve and each surge's peak (s)."""
    out = np.zeros(n, np.float32)
    tops = []
    for t in times:
        r, f = rng.uniform(*rise), rng.uniform(*fall)
        e = env([(0, 0), (r, rng.uniform(0.8, 1.0)), (r + f, 0)], r + f) ** 1.3
        out = np.maximum(out, W.place(n, [(t, e, 1.0)]))
        tops.append(t + r)
    return np.clip(floor + (1 - floor) * out, 0, 1), tops


CREEP_LOOP = 8.0
SURGES = [0.15, 1.45, 2.55, 3.95, 5.2, 6.55]    # the slack running in and out at speed: a surge every 1.1-1.4 s


@recipe("tell-knotter", "creep", "fibre",
        "Wet creaking at a coupling, modelled: a living rope groaning in surges, its slime peeling, the iron straining",
        """The Knotter's body as a hawser under load: three strands slipping on each other (stick-slip that quickens
        from a creak into a groan as each surge of the slack comes on, rung through a thick rope's broad, soft modes from
        about 370 Hz), the slime between its fibres letting go in wet little peels as it stretches (hundreds a second at the
        top of a surge), squeezed out as the load comes off, and the coupler's knuckle creaking in its iron under it. A
        surge every 1.1-1.4 s, so the 4 s warning holds three of them. 8 s exact cycle. Held to 400 Hz-4.5 kHz.""",
        loop=True, takes=1, band=CREEP, seconds=CREEP_LOOP)
def creep_fibre(rng, k):
    n = samples(CREEP_LOOP)
    load, tops = surges(n, SURGES, rng)
    ld = ext(load, n)
    L = CREEP_LOOP + 0.4
    body = loopify(rope(rng, L, ld), n)
    wet = loopify(tack(rng, L, 20 + 320 * ld ** 1.5), n)
    iron = loopify(knuckle(rng, L, ld), n)
    sq = W.place(n, [(t + rng.uniform(0.0, 0.1), squish(rng, rng.uniform(0.25, 0.4)), rng.uniform(0.6, 1.0)) for t in tops])
    y = body + norm(wet) * 0.4 + norm(iron) * 0.3 + norm(sq) * 0.35
    return close(y, rng, CREEP, wet=0.08, drive=4)


def creak_rec(rng, length, semis, key=None):
    """One real creak (a door's hinge, a floor's boards: cut at its loudest event) pitched down into a hawser's size
    and held out to `length` s (rubberband, crisp, so the slips stay slips)."""
    key = key or CREAKS[int(rng.integers(len(CREAKS)))]
    x = ck.get(key)
    h = ck.hits(x, floor_db=-14, gap=0.08)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    c = ck.cut(x, a - samples(0.01), a + samples(0.3), 0.005, 0.05)
    c = dsp.vari(c, semis)
    return norm(dsp.fit(dsp.stretch(c, max(1.0, length / (len(c) / SR))), samples(length)))


def wet_grains(rng, length, rate, semis=(-6, -2)):
    """Real wet sounds (a chewing squish, wet steps) cut into grains and strewn at `rate` a second (a curve)."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.1)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        if t >= length:
            break
        x = ck.get(SQUISH[int(rng.integers(len(SQUISH)))])
        gl = rng.uniform(0.015, 0.05)
        a = rng.uniform(0, len(x) / SR - gl)
        g = dsp.vari(dsp.trim(x, a, gl), rng.uniform(*semis))
        b.at(t, g * np.hanning(len(g)).astype(np.float32), rng.uniform(-12, 0))
    return b.x[:n]


@recipe("tell-knotter", "creep", "hemp",
        "Wet creaking at a coupling, from real recordings: creaks pitched down into a hawser's groan, slime squishing",
        """Each surge of the load a real creak (a door's hinge or a floor's boards, cut at its loudest) pitched down five
        to eight semitones and held out over the surge so it groans like a hawser, two at once a little apart for its
        strands; real wet sounds (a chewing squish, wet steps) cut into grains and strewn thick as it stretches; a real
        metal scrape pitched well down under it for the coupler's iron bearing the load. A surge every 1.1-1.4 s, so the
        4 s warning holds three. 8 s exact cycle. Held to 400 Hz-4.5 kHz.""",
        sources=CREAKS + SQUISH + [SCRAPE], loop=True, takes=1, band=CREEP, seconds=CREEP_LOOP)
def creep_hemp(rng, k):
    n = samples(CREEP_LOOP)
    load, tops = surges(n, SURGES, rng)
    ev = []
    for t, top in zip(SURGES, tops):
        span = top - t + rng.uniform(0.4, 0.55)
        shape = dsp.fit(env([(0, 0), (top - t, 1), (span, 0)], span), samples(span)) ** 1.2
        for j in range(2):
            ev.append((t + j * rng.uniform(0.03, 0.08), creak_rec(rng, span, -rng.uniform(5, 8)) * shape, rng.uniform(0.55, 1.0)))
    body = W.place(n, ev)
    ld = ext(load, n)
    L = CREEP_LOOP + 0.4
    wet = loopify(wet_grains(rng, L, 10 + 160 * ld ** 1.5), n)
    iron = norm(scrape_grains(n, rng, 25, semis=(-11, -8), gain=(0.5, 1.0))) * load ** 1.5
    y = norm(body) + norm(wet) * 0.45 + iron * 0.2
    return close(y, rng, CREEP, wet=0.08, drive=4)


def tighten(rng, length, load, lo=35, hi=150, modes=(520, 880, 1330, 2050, 2900), q=16):
    """A living rope pulled tight: its slip a steady train (little jitter, so it has a pitch) gliding up from `lo` to
    `hi` a second with the load, rung through resonances that rise with the tension too (a string tightened goes up),
    so each surge is a creak that tightens as it goes."""
    n = samples(length)
    load = synth.curve(load, n)
    rate = lo * (hi / lo) ** load
    x = np.zeros(n, np.float32)
    ph = np.cumsum(rate * (1 + 0.04 * lp(rng.standard_normal(n).astype(np.float32), 20) * 8)) / SR
    hits = np.nonzero(np.diff(np.floor(ph)) > 0)[0]
    x[hits] = rng.uniform(0.6, 1.0, len(hits))
    out = np.zeros(n, np.float32)
    tense = 1 + 0.18 * load
    for i, f in enumerate(modes):
        out += dsp.sweep_filter(x, "bp", f * rng.uniform(0.96, 1.04) * tense, q=q, block=64) / (1 + 0.3 * i)
    return norm(out * (0.05 + 0.95 * load ** 1.2))


@recipe("tell-knotter", "creep", "sinew",
        "Wet creaking at a coupling, modelled as sinew: each surge a creak that tightens, rising in pitch, slime peeling",
        """Modelled as living fibre pulled tight: the slip comes as a steady train with a pitch, gliding up from 35 to
        150 a second as each surge of the load comes on, rung through resonances that climb with the tension (a string
        tightened goes up), so every surge is a creak that tightens as it goes and slackens back; the slime peeling off
        its fibres in wet little ticks, thickest at the top, squeezed out as it eases; the coupler's iron creaking under
        it. More pitched and more alive than 'fibre'. A surge every 1.1-1.4 s. 8 s exact cycle. Held to 400 Hz-4.5 kHz.""",
        loop=True, takes=1, band=CREEP, seconds=CREEP_LOOP)
def creep_sinew(rng, k):
    n = samples(CREEP_LOOP)
    load, tops = surges(n, SURGES, rng)
    ld = ext(load, n)
    L = CREEP_LOOP + 0.4
    body = loopify(tighten(rng, L, ld), n) + 0.5 * loopify(tighten(rng, L, np.roll(ld, samples(0.05)), 28, 120), n)
    wet = loopify(tack(rng, L, 20 + 280 * ld ** 1.5), n)
    iron = loopify(knuckle(rng, L, ld), n)
    sq = W.place(n, [(t + rng.uniform(0.0, 0.1), squish(rng, rng.uniform(0.25, 0.4)), rng.uniform(0.6, 1.0)) for t in tops])
    y = norm(body) + norm(wet) * 0.4 + norm(iron) * 0.25 + norm(sq) * 0.3
    return close(y, rng, CREEP, wet=0.08, drive=4)


def clank(rng, f1=None, weight=1.0):
    """Two cars' couplers slamming together or snatching apart as the slack runs: a heavy cast-steel knuckle struck,
    its close modes from 300-400 Hz dying in a tenth of a second (it clanks, it doesn't ring), and the drawgear's
    springs taking it (a short dull knock)."""
    f1 = f1 or rng.uniform(300, 400)
    fs = [f1 * r * rng.uniform(0.97, 1.03) for r in CASTING]
    am = [rng.uniform(0.5, 1.0) / (1 + 0.25 * i) for i in range(len(fs))]
    y = W.modal(fs, [0.11 / (1 + 0.45 * i) for i in range(len(fs))], am, 0.4, rng, contact=0.0006)
    knock = bp(synth.noise(0.06, rng, "pink"), 200, 900) * env([(0, 1), (0.06, 0)], 0.06, "exp")
    clack = bp(synth.noise(0.015, rng), 900, 4000) * env([(0, 1), (0.015, 0)], 0.015, "exp")   # steel faces meeting
    return mix(norm(y), norm(knock) * 0.5, norm(clack) * 0.45) * weight


def run_down(rng, clanks, gap=(0.12, 0.16), cars=5):
    """The jolt running down the train: a clank at each coupling in turn (clanks(rng, i) the i-th), each a car further
    off, so quieter, darker and more of it the night's reverb."""
    b = Bus(cars * gap[1] + 0.8)
    t = 0.0
    for i in range(cars):
        x = lp(clanks(rng, i), 7000 / (1 + 0.6 * i))
        x = dsp.room(x, "night", wet=0.08 + 0.09 * i, rng=rng)[:len(x) + samples(0.4)]
        b.at(t, x, -3.2 * i)
        t += rng.uniform(*gap)
    return b.x


def twang(rng, f0=None, length=0.5):
    """The rope taking the whole load at once: its length thrumming (a wet string's harmonics, dying fast, the low ones
    longest), a heavy wet note under the creak."""
    f0 = f0 or rng.uniform(150, 185)
    hs = range(2, 11)
    y = W.modal([f0 * h * rng.uniform(0.99, 1.01) for h in hs], [0.22 / h ** 0.6 for h in hs], [1 / h ** 0.7 for h in hs],
                length, rng, contact=0.002)
    return norm(y)


STRETCH_LEN = 2.9


@recipe("tell-knotter", "stretch", "rope",
        "A long groaning stretch, the buffers parting and a jolt down the train, modelled",
        """The rope's groan climbing for a second and a half as the Knotter hauls the cars apart (its strands' stick-slip
        quickening from a creak into a groan, slime peeling off it faster and faster), then the buffers part: the
        knuckle's iron grinds and lets go with a clank, the rope takes the whole load at once with a wet thrum and
        squeezes slime, and the jolt runs down the train, a coupler's clank at each of five cars in turn, each further
        off. 2.4-2.8 s. Held to 300 Hz-4 kHz.""",
        takes=2, band=STRETCH)
def stretch_rope(rng, k):
    T = (1.35, 1.55)[k] * rng.uniform(0.97, 1.03)
    span = T + 0.5
    load = env([(0, 0.05), (T * 0.6, 0.5), (T, 1.0), (T + 0.05, 0.25), (span, 0.06)], span) ** 1.2
    b = Bus(STRETCH_LEN + 1)
    b.at(0, rope(rng, span, load, top=150, bottom=10), -3)
    b.at(0, norm(tack(rng, span, 30 + 380 * load ** 2)) * dsp.fit(load, samples(span)), -12)
    b.at(T - 0.12, norm(ck.friction(rng, 0.14, 220, 500, 4000, 0.9, env([(0, 0), (0.05, 1), (0.14, 0)], 0.14))), -8)
    b.at(T, clank(rng, rng.uniform(380, 440)), 4)
    b.at(T + 0.01, twang(rng), -4)
    b.at(T + 0.03, squish(rng, 0.35), -9)
    b.at(T + 0.14, run_down(rng, lambda r, i: clank(r)), 3)
    y = held(done(b.x, 200), *STRETCH, drive=3)
    return peaks(dsp.fade(y[:samples(STRETCH_LEN)], 0, 0.3), -20)


def creak_run(rng, length, semis, keys=None):
    """A groan made of real creaks laid end to end, each cut at its loudest event and varispeeded (pitched down into a
    hawser's size, `semis` a pair: the first's and the last's, so the run climbs as the load comes on), overlapped a
    little so it never stops."""
    keys = keys or CREAKS
    b = Bus(length + 1.0)
    t, i = 0.0, 0
    while t < length:
        st = semis[0] + (semis[1] - semis[0]) * min(1.0, t / length)
        x = ck.get(keys[(i + int(rng.integers(len(keys)))) % len(keys)])
        h = ck.hits(x, floor_db=-14, gap=0.08)
        a = h[int(rng.integers(len(h)))][0] if h else 0
        c = dsp.vari(ck.cut(x, a - samples(0.01), a + samples(rng.uniform(0.18, 0.28)), 0.01, 0.04), st + rng.uniform(-0.4, 0.4))
        b.at(t, norm(c), rng.uniform(-3, 0))
        t += len(c) / SR - rng.uniform(0.04, 0.08)
        i += 1
    return norm(b.x[:samples(length)])


@recipe("tell-knotter", "stretch", "iron",
        "A long groaning stretch, the buffers parting and a jolt down the train, from real recordings",
        """The groan is real creaks (a door's hinge, a floor's boards) cut at their loudest and laid end to end, pitched
        down nine semitones and climbing to five as the load comes on, two strands of it, with real wet squish grains
        thickening through it; then the buffers part: a real metal scrape, a real metal door slam pitched down a few
        semitones for the knuckle letting go, a wet thrum and squelch as the rope takes it, and the jolt running down the
        train in real slams and knock-slams choked short, a car further off each. 2.4-2.8 s. Held to 300 Hz-4 kHz.""",
        sources=CREAKS + SQUISH + [SCRAPE] + SLAMS, takes=2, band=STRETCH)
def stretch_iron(rng, k):
    T = (1.35, 1.55)[k] * rng.uniform(0.97, 1.03)
    span = T + 0.5
    load = dsp.fit(env([(0, 0.05), (T * 0.6, 0.5), (T, 1.0), (T + 0.08, 0.35), (span, 0.0)], span), samples(span))
    b = Bus(STRETCH_LEN + 1)
    for j in range(2):
        b.at(j * 0.05, creak_run(rng, span, (-9 - j, -5 - j)) * load ** 1.1, -2 * j)
    b.at(0, norm(wet_grains(rng, span, 15 + 220 * load ** 2)) * load, -8)
    scr = dsp.vari(hit_of(SCRAPE, int(rng.integers(3)), 0.2), -rng.uniform(5, 7))
    b.at(T - 0.12, norm(scr) * dsp.fit(env([(0, 0), (0.05, 1), (0.2, 0)], 0.2), len(scr)), -8)

    def slam(r, i, d=0.035):
        x = ck.choke(dsp.vari(hit_of(SLAMS[i % 2], 0, 0.4), -r.uniform(2, 4)), d, 0.05)
        return norm(hp(x, 250, 4))
    b.at(T, slam(rng, k), 2)
    b.at(T + 0.01, twang(rng), -7)
    b.at(T + 0.03, norm(wet_grains(rng, 0.25, 120)), -9)
    b.at(T + 0.14, run_down(rng, slam), 0)
    y = held(done(b.x, 200), *STRETCH, drive=3)
    return peaks(dsp.fade(y[:samples(STRETCH_LEN)], 0, 0.3), -20)


# The hawser's creak: the jerks of the load in each take as the train's speed changes (s from the start, s rising,
# how hard).
JERKS = [((0.0, 0.07, 1.0), ), ((0.0, 0.3, 0.9), ), ((0.0, 0.07, 0.85), (0.38, 0.06, 1.0))]


def jerk_load(k, rng):
    """A take's load: one sharp jerk (0), a slower, longer haul (1), or a jerk and a second, harder one (2)."""
    L = 1.35 if k == 1 else (1.0 if k == 0 else 1.4)
    n = samples(L)
    out = np.zeros(n, np.float32)
    for at, rise, top in JERKS[k % 3]:
        ease = L - at - rise
        e = env([(0, 0), (rise, top * rng.uniform(0.92, 1.0)), (rise + 0.1, top * 0.7), (rise + ease, 0)], rise + ease) ** 1.3
        out = np.maximum(out, dsp.fit(np.concatenate([np.zeros(samples(at), np.float32), e]), n))
    return L, out


@recipe("tell-knotter", "hawser", "groan",
        "Taut: a big wet rope groaning under a jerk as the train's speed changes, modelled",
        """The Knotter's body as a hawser jerked taut: its strands' slip leaping from a creak to a groan as the jerk comes
        on and easing back as it settles, a thrum of the whole rope at the snatch, slime squeezed out of it with a wet
        squelch and peeling off its fibres, the coupler's knuckle knocking once in its iron. One sharp jerk, one slower
        and longer haul, one jerk and a second harder one. 1.0-1.5 s. Held to 350 Hz-4 kHz.""",
        takes=3, band=HAWSER)
def hawser_groan(rng, k):
    L, load = jerk_load(k, rng)
    b = Bus(L + 0.6)
    b.at(0, rope(rng, L, load, top=200, bottom=12), 0)
    b.at(0, norm(tack(rng, L, 20 + 400 * load ** 2)) * load, -12)
    for at, rise, top in JERKS[k % 3]:
        b.at(at + rise * 0.8, twang(rng, length=0.35), -6)
        b.at(at + rise, squish(rng, rng.uniform(0.25, 0.35)), -10)
        b.at(at + rise * 0.5, clank(rng, rng.uniform(420, 500), 0.6), -8)
    return held(air(done(b.x, 200), rng, 0.08, 0.3), *HAWSER, drive=3)


@recipe("tell-knotter", "hawser", "strand",
        "Taut: a big wet rope groaning under a jerk, from real creaks pitched down and wet squish",
        """Real creaks (a door's hinge, a floor's boards) cut at their loudest, laid end to end and pitched down eight or
        nine semitones, two strands of them snatched up in pitch by the jerk and sagging back as it settles; real wet
        squish grains (a chewing squish, wet steps) squeezed out as it pulls taut; a real heavy plate hit choked for the
        coupler's knuckle knocking. One sharp jerk, one slower and longer haul, one jerk and a second harder one.
        1.0-1.5 s. Held to 350 Hz-4 kHz.""",
        sources=CREAKS + SQUISH + PLATE_H, takes=3, band=HAWSER)
def hawser_strand(rng, k):
    L, load = jerk_load(k, rng)
    b = Bus(L + 0.6)
    for j in range(2):
        c = dsp.fit(bend(creak_run(rng, L + 0.3, (-8 - j, -8 - j)), 3.5 * dsp.fit(load, samples(L + 0.3)) + j), samples(L))
        b.at(j * 0.03, c * load, -2 * j)
    b.at(0, norm(wet_grains(rng, L, 10 + 260 * load ** 2)) * load, -7)
    for at, rise, top in JERKS[k % 3]:
        kn = norm(hp(ck.choke(dsp.vari(hit_of(PLATE_H[int(rng.integers(5))], 0, 0.3), rng.uniform(1, 4)), 0.03, 0.04), 300))
        b.at(at + rise * 0.5, kn, -10)
    return held(air(done(b.x, 200), rng, 0.08, 0.3), *HAWSER, drive=3)


# ---- Hotbox: the knock (150-900 Hz) ------------------------------------------------------------------------------------

BOX = (205, 232, 190, 248)          # each take's axle box: the casting's lowest mode (Hz), a different box or blow each
KNOCK_LEN = 0.24
MINING = src.K("impactMining")      # a pick into rock: a heavy dull strike


def cast_knock(rng, f1, contact=0.002, decay=0.05, length=KNOCK_LEN):
    """A blow on the axle box: its casting's close, inharmonic modes from f1 (struck in a different place each time, so
    a different few come out), a heavy dull blow (about 2 ms of contact reaches only the low ones), the high ones gone
    at once: cast iron clanks, it doesn't sing. The grease and the weight on it damp the rest in a tenth of a second."""
    fs = [f1 * r * rng.uniform(0.97, 1.03) for r in CASTING]
    am = [rng.uniform(0.35, 1.0) / (1 + 0.45 * i) for i in range(len(fs))]
    return norm(W.modal(fs, [decay / (1 + 0.6 * i) for i in range(len(fs))], am, length, rng, contact=contact))


def box_thud(rng, length=0.07):
    """The box's own weight answering: a short dull bump of low-mid noise (never a sub)."""
    return norm(bp(synth.noise(length, rng, "pink"), 160, 650) * env([(0, 1), (0.015, 0.4), (length, 0)], length))


def turning(takes, rng):
    """The preview: the knock as the game fires it, once a wheel turn, a take at random (never the same twice running),
    3.5 a second (10 m/s) gathering to 7 (20 m/s)."""
    b = Bus(8.0)
    t, last = 0.2, -1
    while t < 7.4:
        k = int(rng.integers(len(takes)))
        k = (k + 1) % len(takes) if k == last else k
        b.at(t, takes[k], 0)
        t += 1 / (3.5 + 3.5 * float(np.clip((t - 2.5) / 3.0, 0, 1)))
        last = k
    return b.x


def knock_done(y):
    """A knock's last step: off the sub, the top taken away (it's dull), cut to its moment."""
    y = lp(done(y, 140), 1300, 2)
    return dsp.fade(dsp.fit(y, samples(KNOCK_LEN)), 0.0005, 0.05)


@recipe("tell-hotbox", "knock", "cast",
        "A heavy, dull knock in the axle box, modelled: a hammer on a casting, no ring, once a wheel turn",
        """Modelled from the axle box: a heavy blow on cast iron, its close, inharmonic modes from about 190-250 Hz
        (each take a different box or a different place struck, so different modes come out), the contact long enough
        (2 ms) that only the low ones sound, all of them damped by the grease and the car's weight within a tenth of a
        second, with the box's dull bump under it. No ring and nothing above about 1.2 kHz: nothing like the hot box's
        squeal. One knock a take, about 0.23 s; the game fires one a wheel turn (the preview: 3.5 a second gathering to
        7, 10 to 20 m/s). Held to 150-900 Hz.""",
        takes=4, band=KNOCK, lufs=-22, preview=turning)
def knock_cast(rng, k):
    f1 = BOX[k] * rng.uniform(0.97, 1.03)
    y = mix(cast_knock(rng, f1), box_thud(rng) * 0.5)
    return peaks(knock_done(y), -22)


@recipe("tell-hotbox", "knock", "struck",
        "A heavy, dull knock in the axle box, from real recordings: big iron plate and a pick's blow, pitched down, dulled",
        """Real heavy iron struck (Kenney's heavy plate, a pick into rock, sfx_100's knock-slam), each take a different
        one, pitched down two to four semitones for a bigger casting, dulled (the top rolled off above 1.2 kHz) and
        choked at 40 ms like a loaded box, with a little of the box's own casting resonance under it. Rougher and more
        real than 'cast'. One knock a take, about 0.22 s. Held to 150-900 Hz.""",
        sources=PLATE_H + MINING + SLAMS[1:], takes=4, band=KNOCK, lufs=-22, preview=turning)
def knock_struck(rng, k):
    key = (PLATE_H[0], MINING[1], PLATE_H[2], SLAMS[1])[k]
    x = dsp.vari(hit_of(key, 0, 0.35), -rng.uniform(2, 4))
    x = ck.choke(norm(hp(x, 170, 4)), 0.04, 0.05)
    body = dsp.resonate(x, [BOX[k] * r for r in CASTING[:4]], q=8, gains=[1, 0.7, 0.5, 0.35])
    return peaks(knock_done(mix(norm(x), norm(body) * 0.35)), -22)


@recipe("tell-hotbox", "knock", "box",
        "A heavy, dull knock in the axle box: a real iron strike's first instant rung through the box's casting",
        """A hybrid: the first 25 ms of a real heavy strike (Kenney's heavy metal and plate hits, the packs' metal door
        slam), its grain and its bite, rung through the axle box's casting (close, inharmonic modes from about 190-250 Hz,
        damped by grease within a tenth of a second), with the box's dull bump under it. Between 'cast' and 'struck': the
        casting's own sound, struck by something real. One knock a take, about 0.15 s. Held to 150-900 Hz.""",
        sources=METAL_H + PLATE_H + SLAMS[:1], takes=4, band=KNOCK, lufs=-22, preview=turning)
def knock_box(rng, k):
    key = (METAL_H[0], PLATE_H[1], SLAMS[0], METAL_H[3])[k]
    s_ = dsp.fade(hp(hit_of(key, 0, 0.025), 150), 0.0003, 0.01)
    f1 = BOX[(k + 2) % 4] * rng.uniform(0.97, 1.03)
    fs = [f1 * r * rng.uniform(0.97, 1.03) for r in CASTING]
    q = rng.uniform(13, 16)
    gains = [rng.uniform(0.4, 1.0) / (1 + 0.4 * i) for i in range(len(fs))]     # struck in a different place each time
    cast = dsp.resonate(dsp.fit(s_, samples(KNOCK_LEN)), fs, q=q, gains=gains)
    cast = ck.choke(cast, 0.06, 0.07)
    y = mix(norm(cast), norm(lp(s_, 1500)) * 0.25, box_thud(rng) * 0.35)
    return peaks(knock_done(y), -22)


# ---- Hotbox: the glow (the knock ragged and wet, a sizzle) --------------------------------------------------------------

SZ_LOOP = 8.0


def ragged(rng, length, knock, period=0.2):
    """The knock gone ragged: about five a second but never even (a turn of the wheel catching), now and then doubled
    as it slops back, each a different strength: [(t, sound, gain), ...]."""
    ev, t = [], 0.0
    while t < length - 0.05:
        w = rng.uniform(0.5, 1.0)
        ev.append((t, knock(rng, w), w))
        if rng.random() < 0.18:
            ev.append((t + rng.uniform(0.04, 0.07), knock(rng, w), w * rng.uniform(0.35, 0.6)))
        t += period * rng.uniform(0.7, 1.35)
    return ev


def wet_knock(rng, w):
    """A knock with something soft and wet in the box now: the casting's blow, duller, and a wet slap and spit with
    it."""
    f1 = rng.uniform(185, 240)
    y = mix(cast_knock(rng, f1, contact=0.0025, decay=0.06, length=0.16), box_thud(rng) * 0.5)
    slap = bp(synth.noise(0.06, rng), 350, 1800) * env([(0, 1), (0.006, 0.5), (0.06, 0)], 0.06, "exp")
    spit = synth.crackle(0.08, 900, rng, size=(0.0003, 0.002), hi=1500) * env([(0, 1), (0.08, 0)], 0.08)
    return mix(lp(y, 1300), norm(slap) * 0.35, norm(spit) * 0.25 * w)


def sizzle(n, rng, ev_times, rate=420):
    """Something living cooking in the grease: a dense crackle of fat spitting (tiny sharp ticks, a few loud, many
    quiet) over a fluttering hiss, both flaring after each knock squeezes more out, periodic over n."""
    L = n / SR + 0.4
    flare = np.zeros(n, np.float32)
    for t in ev_times:
        e = env([(0, 0), (0.03, 1), (0.25, 0)], 0.25) ** 1.5
        flare = np.maximum(flare, W.place(n, [(t, e, 1.0)]))
    lvl = 0.45 + 0.55 * flare
    fat = loopify(synth.crackle(L, ext(rate * lvl, n), rng, size=(0.0002, 0.0014), hi=2200), n)
    hiss = W.pnoise(n, rng, lambda f: ((f / 2500) ** 2 / (1 + (f / 2500) ** 2)) / (1 + (f / 8000) ** 4))
    hiss = hiss * (0.55 + 0.45 * judder(n, rng, 8, 30)) * lvl
    fat, hiss = fat / (np.std(fat) + 1e-9), hiss / (np.std(hiss) + 1e-9)
    return fat * 0.8 + hiss * 0.45


@recipe("tell-hotbox", "sizzle", "glow",
        "The glow: the knock gone ragged and wet, about five a second, over a sizzle of something cooking, modelled",
        """The casting's knock (as 'cast', duller) gone ragged: about five a second but never even, now and then doubled
        as it slops back, each with a wet slap and a spit of fat in it; over it a sizzle, something living cooking in
        the grease (a dense crackle of fat spitting over a fluttering hiss, flaring after each knock squeezes more out).
        Wet and toneless: nothing like the hot box's dry squeal. 8 s exact cycle. Held to 150 Hz-7 kHz.""",
        loop=True, takes=1, band=SIZZLE, seconds=SZ_LOOP)
def sizzle_glow(rng, k):
    n = samples(SZ_LOOP)
    ev = ragged(rng, SZ_LOOP, wet_knock)
    knocks = W.place(n, ev)
    knocks = knocks / (np.std(knocks) + 1e-9)
    y = knocks + sizzle(n, rng, [t for t, _, _ in ev]) * 0.4
    return close(y, rng, SIZZLE, wet=0.06, drive=3)


HISS = "sfx_100_v2:loop_water_03"    # a real steam hiss off a locomotive


def wet_struck(rng, w):
    """The knock from real iron, wet: a heavy plate's blow pitched down and dulled, a real wet step's slap in it."""
    x = dsp.vari(hit_of(PLATE_H[int(rng.integers(5))], 0, 0.25), -rng.uniform(2, 4))
    x = lp(ck.choke(norm(hp(x, 150, 4)), 0.03, 0.04), 1300)
    wet = hit_of(SQUISH[1 + int(rng.integers(2))], 0, 0.08)
    return mix(norm(x), norm(bp(dsp.vari(wet, -rng.uniform(3, 6)), 300, 3000)) * 0.4)


@recipe("tell-hotbox", "sizzle", "fry",
        "The glow from real recordings: heavy iron knocks gone ragged and wet, a steam hiss cut into a frying sizzle",
        """Real heavy plate hits pitched down and dulled for the knock, about five a second but ragged and sometimes
        doubled, a real wet step's slap in each; the sizzle a real locomotive's steam hiss, its top cut into short grains
        so it fries and flutters rather than blows, with fat crackling and spitting in it, flaring after each knock.
        8 s exact cycle. Held to 150 Hz-7 kHz.""",
        sources=PLATE_H + SQUISH[1:] + [HISS], loop=True, takes=1, band=SIZZLE, seconds=SZ_LOOP)
def sizzle_fry(rng, k):
    n = samples(SZ_LOOP)
    ev = ragged(rng, SZ_LOOP, wet_struck)
    knocks = W.place(n, ev)
    h = hp(src.get(HISS), 2200, 4)
    gr = []
    for t in W.poisson(SZ_LOOP, 90, rng):
        a = rng.uniform(0, len(h) / SR - 0.08)
        g = dsp.trim(h, a, rng.uniform(0.02, 0.06))
        gr.append((t, g * np.hanning(len(g)).astype(np.float32), rng.uniform(0.3, 1.0)))
    hiss = W.place(n, gr)
    flare = np.zeros(n, np.float32)
    for t, _, w in ev:
        flare = np.maximum(flare, W.place(n, [(t, env([(0, 0), (0.03, 1), (0.25, 0)], 0.25) ** 1.5 * w, 1.0)]))
    fat = loopify(synth.crackle(SZ_LOOP + 0.4, ext(380 * (0.45 + 0.55 * flare), n), rng, size=(0.0002, 0.0014), hi=2200), n)
    y = norm(knocks) + (norm(hiss) * 0.3 * (0.5 + 0.5 * flare) + norm(fat) * 0.35)
    return close(y, rng, SIZZLE, wet=0.06, drive=3)


# ---- Hotbox: seized (the grind) ----------------------------------------------------------------------------------------

GR_LOOP = 10.0
RAIL = [520, 830, 1240, 1780, 2600, 3500]     # a wheel dragged on a rail: tread, web and rail head, what the scrape rings


def skid_judder(n, rng, rate=11.0, depth=0.55):
    """The locked wheel sticking and slipping along the rail, about `rate` times a second and wandering (whole cycles
    over n, so it loops): the grind's judder."""
    f = rate * (1 + 0.15 * W.slow(n, 0.3, rng))
    ph = np.cumsum(f) / SR
    ph *= max(1, round(ph[-1])) / ph[-1]
    return (1 - depth * (0.5 + 0.5 * np.cos(2 * np.pi * ph)) ** 2).astype(np.float32)


@recipe("tell-hotbox", "grind", "skid",
        "Seized: the axle locked and the wheel dragged along the rail, a long juddering iron scrape, modelled",
        """Modelled: iron dragged on iron, the locked wheel's tread scraping along the rail head (broadband contact noise
        roughened by the asperities, rung through the wheel's and rail's modes from 520 Hz), juddering as it sticks and
        slips about eleven times a second, surging slowly as the car's weight comes and goes, the axle groaning in its
        seized box (slow, heavy iron slips) and sparks crackling off it. Noise with a judder, no tone: nothing like the
        hot box's squeal. 10 s exact cycle. Held to 300 Hz-5 kHz.""",
        loop=True, takes=1, band=GRIND, seconds=GR_LOOP)
def grind_skid(rng, k):
    n = samples(GR_LOOP)
    scrape = norm(W.friction(n, rng, RAIL, rough=3.0, grit=0.7, q=7, loop=True))
    surge = np.clip(0.75 + 0.25 * W.slow(n, 0.25, rng), 0.4, 1)
    L = GR_LOOP + 0.4
    groan = loopify(rung(rng, slips(rng, L, ext(28 + 14 * W.slow(n, 0.5, rng), n), 0.35), KNUCKLE, 0.06), n)
    sparks = loopify(synth.crackle(L, 45, rng, size=(0.0002, 0.0012), hi=2500), n)
    y = scrape * skid_judder(n, rng) * surge + norm(groan) * 0.3 * surge + norm(sparks) * 0.15
    return close(y, rng, GRIND, wet=0.08, drive=4)


def buck(rng):
    """The truck bucking on the seized axle: a real heavy plate hit, pitched down and dulled."""
    return lp(norm(hp(dsp.vari(hit_of(PLATE_H[int(rng.integers(5))], 0, 0.25), -3), 150, 4)), 1500)


@recipe("tell-hotbox", "grind", "rasp",
        "Seized: a long grinding scrape from real recordings, a metal scrape and stones pitched down and dragged on",
        """A real metal scrape cut into grains and laid end over end, pitched down six to nine semitones for a wheel
        under a loaded car, juddering as the locked wheel sticks and slips about eleven times a second; real stones
        scraping cut into grains for the grit; now and then a real heavy plate hit, dulled, as the truck bucks. Noise
        with a judder, no tone. 10 s exact cycle. Held to 300 Hz-5 kHz.""",
        sources=[SCRAPE, GRIT] + PLATE_H, loop=True, takes=1, band=GRIND, seconds=GR_LOOP)
def grind_rasp(rng, k):
    n = samples(GR_LOOP)
    drag = scrape_grains(n, rng, 90, semis=(-9, -6), gain=(0.6, 1.0))
    drag = norm(drag / (W.cfilter(np.abs(drag), lambda f: 1 / (1 + (f / 3) ** 2)) + 0.05 * np.max(np.abs(drag))))
    grit = norm(scrape_grains(n, rng, 40, semis=(-5, -2), size=(0.03, 0.08), key=GRIT, gain=(0.3, 1.0)))
    surge = np.clip(0.75 + 0.25 * W.slow(n, 0.25, rng), 0.4, 1)
    bucks = W.place(n, [(t, buck(rng), rng.uniform(0.3, 0.6)) for t in W.poisson(GR_LOOP, 0.9, rng)])
    y = drag * skid_judder(n, rng) * surge + grit * 0.25 + norm(bucks) * 0.3
    return close(y, rng, GRIND, wet=0.08, drive=4)
