"""The facilities' working modules (spec D.2), modelled, as parts the scene moves where the sim has them:

  * the capstan winch (T43, facilities.json winch: handles 1.2 m either side of the capstan, 0.9 m up, cranks of
    0.3 m): winch_frame (its trestles, bearings, ratchet and pawl), winch_drum (turned by the crank; rope on it),
    winch_crank (an arm and grip, one per handle) and winch_sled (a skid sled with freight lashed on it);
  * the gantry crane (T48, facilities.json crane: 20 m long, legs at -3.5 and 9 m across, rails 8.6 m up):
    crane_leg, crane_girder (a 5 m length of the rail girder, laid end to end), crane_bridge (12.5 m, with its end
    trucks), crane_trolley, crane_hook, crane_cab and crane_controls;
  * casting: the foundry's freight the crane lifts (1.4 x 0.9 x 1.0 m, a lifting eye on top);
  * chute_lever: the coaling tower's lever stand, and chute_handle, its handle.

Axes (Blender): +X along a part's axis (the winch's, a girder's length), +Z up, -Y outward (the engine's +Z).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402


def materials():
    return {
        "iron": make.lib("rust_heavy", 1.5, tint=(0.55, 0.5, 0.47), rough=0.5, metal=0.6),
        "paint": make.lib("paint_oxide", 0.8, tint=(0.8, 0.7, 0.65), rough=0.6),
        "steel": make.lib("iron_plate", 2.0, tint=(0.7, 0.7, 0.68), rough=0.4, metal=0.7),
        "timber": make.lib("wood_sleeper", 1.6, tint=(0.95, 0.82, 0.66), rough=0.8),
        "rope": make.lib("wool", 7.0, tint=(0.75, 0.62, 0.45), rough=0.95),
        "brass": make.lib("brass", 3.0, rough=0.3, metal=0.9),
        "cast": make.lib("rust_heavy", 2.5, tint=(0.35, 0.32, 0.3), rough=0.6, metal=0.5),
        "glass": make.flat("cab_glass", (0.25, 0.26, 0.24), rough=0.15),
    }


def build(name, fn, what, size=1024, budget=4000):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = fn(materials())
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=size, cage=0.004, reach=0.012, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.55, made=make.provenance("depot_modules", what))


def rivets(a, b, n, normal, material, r=0.012):
    return [make.nail(tuple(a[i] + (b[i] - a[i]) * k / max(1, n - 1) for i in range(3)), normal, material, r=r) for k in range(n)]


# ----------------------------------------------------------------------------------------------------------------
# The winch

def winch_frame(m):
    p = []
    for sx in (-1, 1):
        x = sx * 1.08
        # An A-frame trestle of timber on a skid, an iron bearing block on top.
        for sy in (-1, 1):
            p.append(make.box((x, sy * 0.28, 0.45), (0.06, 0.06, 0.48), m["timber"], bevel=0.01, name="leg",
                              rot=None))
        p.append(make.box((x, 0, 0.06), (0.09, 0.6, 0.06), m["timber"], bevel=0.01, name="skid"))
        p.append(make.box((x, 0, 0.55), (0.05, 0.3, 0.05), m["timber"], bevel=0.01, name="brace"))
        p.append(make.box((x, 0, 0.9), (0.07, 0.12, 0.09), m["iron"], bevel=0.012, name="bearing"))
        p += rivets((x - 0.071, -0.08, 0.95), (x - 0.071, 0.08, 0.95), 3, (-1, 0, 0), m["iron"])
    # A long skid under it all, and the ratchet's pawl on the right trestle.
    for sy in (-1, 1):
        p.append(make.box((0, sy * 0.55, 0.04), (1.2, 0.05, 0.04), m["timber"], bevel=0.008, name="rail"))
    p.append(make.box((0.98, -0.2, 0.75), (0.02, 0.02, 0.12), m["iron"], bevel=0.004, name="pawl", rot=None))
    return p


def winch_drum(m):
    # Along X, its axle at the origin; the rope wound on it, flanges at the ends, a ratchet wheel on the right.
    p = [make.cyl((-0.95, 0, 0), (0.95, 0, 0), 0.2, m["timber"], n=20, name="barrel", low=12)]
    for x in (-0.95, 0.95):
        p.append(make.cyl((x - 0.03, 0, 0), (x + 0.03, 0, 0), 0.3, m["iron"], n=24, name="flange", low=12))
    p.append(make.cyl((-1.25, 0, 0), (1.25, 0, 0), 0.04, m["steel"], n=12, name="axle", low=8))
    for k in range(34):
        x = -0.85 + k * 0.05
        p.append(make.torus((x, 0, 0), (1, 0, 0), 0.215, 0.022, m["rope"], n=24, m=6, name="turn", low=False))
    p.append(make.cyl((1.0, 0, 0), (1.06, 0, 0), 0.26, m["iron"], n=28, name="ratchet", low=12))
    for k in range(16):
        a = k * math.pi / 8
        p.append(make.box((1.03, math.cos(a) * 0.27, math.sin(a) * 0.27), (0.025, 0.02, 0.02), m["iron"], bevel=0, name="tooth", low=False))
    return p


def winch_crank(m):
    # From the axle's end (the origin) the arm out along -Y (the engine's outward) 0.3 m, the grip along +X.
    p = [make.box((0, -0.15, 0), (0.025, 0.17, 0.035), m["iron"], bevel=0.008, name="arm")]
    p.append(make.cyl((-0.03, 0, 0), (0.05, 0, 0), 0.05, m["iron"], n=14, name="boss"))
    p.append(make.cyl((0.0, -0.3, 0), (0.26, -0.3, 0), 0.028, m["timber"], n=14, name="grip"))
    p.append(make.cyl((0.24, -0.3, 0), (0.27, -0.3, 0), 0.035, m["brass"], n=14, name="ferrule"))
    return p


def winch_sled(m):
    # A skid sled of heavy timber, 2.4 x 1.6 m, freight on it lashed with chain: two cases and a drum.
    p = []
    for sy in (-1, 1):
        p.append(make.box((0, sy * 0.65, 0.08), (1.2, 0.09, 0.08), m["timber"], bevel=0.02, name="runner"))
        p.append(make.box((1.24, sy * 0.65, 0.12), (0.06, 0.08, 0.06), m["timber"], bevel=0.02, name="nose"))
    p += make.planks((-1.15, -0.8, 0.16), (1.15, 0.8, 0.2), 0, 8, m["timber"], gap=0.02, name="deck", low=True)
    p.append(make.box((-0.45, 0, 0.62), (0.55, 0.5, 0.42), m["timber"], bevel=0.015, name="case"))
    p.append(make.box((0.55, -0.3, 0.52), (0.4, 0.35, 0.32), m["timber"], bevel=0.015, name="case2"))
    p.append(make.cyl((0.6, 0.45, 0.2), (0.6, 0.45, 0.95), 0.28, m["iron"], n=20, name="drum", low=10))
    for x in (-0.6, -0.2):
        p.append(make.box((x, 0, 1.05), (0.015, 0.52, 0.012), m["iron"], bevel=0.003, name="chain", low=False))
    # The towing eye, where the rope takes it.
    p.append(make.torus((1.32, 0, 0.14), (0, 0, 1), 0.07, 0.018, m["iron"], name="eye"))
    return p


# ----------------------------------------------------------------------------------------------------------------
# The gantry crane

def crane_leg(m):
    # 8.6 m to the rail girder's underside: a riveted box column of plate, tapering, on a base plate, braced.
    H = 8.6
    p = [make.box((0, 0, 0.05), (0.45, 0.45, 0.05), m["steel"], bevel=0.01, name="base")]
    p.append(make.cyl((0, 0, 0.1), (0, 0, H), 0.24, m["paint"], n=4, bevel=0.01, name="column", low=4, r1=0.17))
    for z in (1.0, 3.0, 5.0, 7.0):
        p.append(make.box((0, 0, z), (0.26 - z * 0.009, 0.26 - z * 0.009, 0.03), m["paint"], bevel=0.006, name="band"))
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        p.append(make.box((math.cos(a) * 0.35, math.sin(a) * 0.35, 0.3), (0.02, 0.12, 0.2), m["steel"], bevel=0.005,
                          name="gusset"))
    p.append(make.box((0, 0, H - 0.1), (0.3, 0.3, 0.1), m["steel"], bevel=0.01, name="cap"))
    return p


def crane_girder(m):
    # 5 m of the rail girder (end to end along the crane): a riveted box girder, the crane rail on top.
    L = 5.0
    p = [make.box((0, 0, 0), (L / 2, 0.2, 0.3), m["paint"], bevel=0.01, name="web")]
    for z in (-0.3, 0.3):
        p.append(make.box((0, 0, z), (L / 2, 0.26, 0.02), m["paint"], bevel=0.006, name="flange"))
    p.append(make.box((0, 0, 0.36), (L / 2, 0.035, 0.04), m["steel"], bevel=0.006, name="rail"))
    for sy in (-1, 1):
        p += rivets((-L / 2 + 0.1, sy * 0.201, 0.25), (L / 2 - 0.1, sy * 0.201, 0.25), 25, (0, sy, 0), m["steel"], r=0.014)
        p += rivets((-L / 2 + 0.1, sy * 0.201, -0.25), (L / 2 - 0.1, sy * 0.201, -0.25), 25, (0, sy, 0), m["steel"], r=0.014)
        for x in (-2.0, -1.0, 0.0, 1.0, 2.0):
            p.append(make.box((x, sy * 0.205, 0), (0.03, 0.006, 0.26), m["paint"], bevel=0.003, name="stiffener", low=False))
    return p


def crane_bridge(m):
    # 12.5 m across (the legs at -3.5 and 9), along X here: a pair of girders on end trucks with their wheels.
    L = 12.5
    p = []
    for sy in (-1, 1):
        p.append(make.box((0, sy * 0.45, 0), (L / 2 - 0.4, 0.14, 0.45), m["paint"], bevel=0.01, name="girder"))
        p += rivets((-L / 2 + 0.6, sy * 0.59, 0.38), (L / 2 - 0.6, sy * 0.59, 0.38), 40, (0, sy, 0), m["steel"], r=0.014)
    p.append(make.box((0, 0, 0.47), (L / 2 - 0.4, 0.62, 0.02), m["steel"], bevel=0.005, name="walkway"))
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.2)
        p.append(make.box((x, 0, -0.1), (0.35, 0.9, 0.3), m["paint"], bevel=0.012, name="truck"))
        for y in (-0.6, 0.6):
            p.append(make.cyl((x, y - 0.08, -0.35), (x, y + 0.08, -0.35), 0.22, m["steel"], n=16, name="wheel", low=10))
    for x in (-3.0, 0.0, 3.0):
        p.append(make.box((x, 0, -0.35), (0.04, 0.5, 0.04), m["steel"], bevel=0.005, name="tie"))
    return p


def crane_trolley(m):
    # The crab on the bridge's rails: its frame, wheels, the hoist drum and motor housing.
    p = [make.box((0, 0, 0.35), (0.7, 0.75, 0.12), m["paint"], bevel=0.012, name="frame")]
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.cyl((sx * 0.55, sy * 0.45 - 0.05, 0.2), (sx * 0.55, sy * 0.45 + 0.05, 0.2), 0.14, m["steel"], n=14, name="wheel", low=8))
    p.append(make.cyl((-0.45, 0, 0.72), (0.45, 0, 0.72), 0.28, m["iron"], n=20, name="hoist", low=10))
    p.append(make.box((0.0, 0.5, 0.75), (0.35, 0.2, 0.3), m["steel"], bevel=0.02, name="motor"))
    return p


def crane_hook(m):
    # The block (two sheaves in iron cheeks) and the hook under it, its point open to the side.
    p = []
    for sy in (-1, 1):
        p.append(make.box((0, sy * 0.1, 0.3), (0.2, 0.02, 0.24), m["steel"], bevel=0.01, name="cheek"))
    p.append(make.cyl((0, -0.08, 0.35), (0, 0.08, 0.35), 0.16, m["iron"], n=18, name="sheave", low=10))
    p.append(make.cyl((0, 0, 0.06), (0, 0, -0.08), 0.05, m["steel"], n=12, name="shank", low=8))
    pts = [(0, 0, -0.08)]
    for k in range(9):
        a = math.radians(180 - k * 26)
        pts.append((0.12 * math.cos(a) + 0.12, 0, -0.2 - 0.12 * math.sin(a) + 0.0))
    p += make.pipe(pts, 0.035, m["steel"], name="hook", n=10, low=6)
    return p


def crane_cab(m):
    # The operator's cab on the near leg: a riveted steel box, windows all round, a door, a railing on its platform.
    p = [make.box((0, 0, 0), (0.9, 0.9, 0.8), m["paint"], bevel=0.015, name="box")]
    p.append(make.box((0, 0, 0.84), (1.0, 1.0, 0.05), m["steel"], bevel=0.01, name="roof"))
    for sy in (-1, 1):
        p.append(make.box((0, sy * 0.905, 0.25), (0.7, 0.004, 0.22), m["glass"], bevel=0, name="window", low=False))
    for sx in (-1, 1):
        p.append(make.box((sx * 0.905, 0, 0.25), (0.004, 0.6, 0.22), m["glass"], bevel=0, name="window", low=False))
    p.append(make.box((0, 0, -0.85), (1.05, 1.05, 0.04), m["steel"], bevel=0.008, name="platform"))
    for sx in (-1, 1):
        p.append(make.cyl((sx * 1.02, -1.02, -0.8), (sx * 1.02, -1.02, -0.3), 0.02, m["steel"], n=8, name="post", low=6))
    p.append(make.cyl((-1.02, -1.02, -0.3), (1.02, -1.02, -0.3), 0.02, m["steel"], n=8, name="rail", low=6))
    return p


def crane_controls(m):
    # The ground control stand at the leg's foot: a cast pedestal, a box of contacts, three levers and a brass plate.
    p = [make.box((0, 0, 0.05), (0.3, 0.25, 0.05), m["steel"], bevel=0.008, name="foot")]
    p.append(make.box((0, 0, 0.55), (0.22, 0.18, 0.45), m["paint"], bevel=0.02, name="pedestal"))
    p.append(make.box((0, 0, 1.03), (0.26, 0.21, 0.04), m["steel"], bevel=0.01, name="top"))
    for k, x in enumerate((-0.14, 0.0, 0.14)):
        tilt = (0.2, -0.1, 0.05)[k]
        p.append(make.cyl((x, 0, 1.06), (x, -0.4 * math.sin(tilt), 1.06 + 0.35), 0.012, m["steel"], n=8, name="lever", low=6))
        p.append(make.cyl((x, -0.4 * math.sin(tilt), 1.4), (x, -0.4 * math.sin(tilt), 1.46), 0.025, m["timber"], n=10, name="knob", low=6))
    p.append(make.box((0, -0.182, 0.8), (0.12, 0.003, 0.05), m["brass"], bevel=0.002, name="plate", low=False))
    return p


def casting(m):
    # A foundry casting (1.4 x 0.9 x 1.0 m, spec D.2's heavy freight): a pump body's housing, ribbed, flanged ports,
    # still black from the sand, its lifting eye on top.
    p = [make.box((0, 0, 0), (0.62, 0.42, 0.38), m["cast"], bevel=0.05, segments=3, name="body")]
    for x in (-0.45, -0.15, 0.15, 0.45):
        p.append(make.box((x, 0, 0), (0.025, 0.46, 0.42), m["cast"], bevel=0.015, name="rib"))
    for sx in (-1, 1):
        p.append(make.cyl((sx * 0.62, 0, -0.05), (sx * 0.7, 0, -0.05), 0.26, m["cast"], n=20, name="port", low=12))
        for k in range(8):
            a = k * math.pi / 4
            p.append(make.nail((sx * 0.702, math.cos(a) * 0.2, -0.05 + math.sin(a) * 0.2), (sx, 0, 0), m["iron"], r=0.02))
    p.append(make.box((0, 0, -0.44), (0.66, 0.46, 0.06), m["cast"], bevel=0.02, name="foot"))
    p.append(make.torus((0, 0, 0.5), (0, 1, 0), 0.09, 0.03, m["iron"], name="eye"))
    p.append(make.box((0, 0, 0.4), (0.05, 0.05, 0.04), m["iron"], bevel=0.01, name="eye_boss"))
    return p


def chute_lever(m):
    # The coaling lever's stand: an iron post 0.9 m, a quadrant, the pivot at its top (the handle turns on it).
    p = [make.box((0, 0, 0.45), (0.05, 0.05, 0.45), m["iron"], bevel=0.01, name="post")]
    p.append(make.box((0, 0, 0.03), (0.2, 0.2, 0.03), m["steel"], bevel=0.008, name="foot"))
    p.append(make.box((0, 0.07, 0.85), (0.012, 0.01, 0.2), m["steel"], bevel=0.003, name="quadrant"))
    p.append(make.cyl((-0.08, 0, 0.9), (0.08, 0, 0.9), 0.03, m["iron"], n=12, name="pivot"))
    return p


def chute_handle(m):
    # From the pivot (the origin) the lever out 0.5 m along +X, its worn wooden grip.
    p = [make.box((0.22, 0, 0), (0.25, 0.018, 0.025), m["iron"], bevel=0.006, name="arm")]
    p.append(make.cyl((0.44, 0, 0), (0.6, 0, 0), 0.03, m["timber"], n=12, name="grip"))
    p.append(make.cyl((0.42, 0, 0), (0.45, 0, 0), 0.035, m["brass"], n=12, name="ferrule"))
    return p


build("winch_frame", winch_frame, "the capstan winch's frame")
build("winch_drum", winch_drum, "the capstan winch's drum", budget=3000)
build("winch_crank", winch_crank, "the capstan winch's crank", size=512, budget=400)
build("winch_sled", winch_sled, "the winch's freight sled")
build("crane_leg", crane_leg, "the gantry crane's leg", budget=600)
build("crane_girder", crane_girder, "the gantry crane's rail girder", budget=800)
build("crane_bridge", crane_bridge, "the gantry crane's bridge", budget=2000)
build("crane_trolley", crane_trolley, "the gantry crane's trolley", budget=1200)
build("crane_hook", crane_hook, "the gantry crane's hook", size=512, budget=800)
build("crane_cab", crane_cab, "the gantry crane's cab", budget=800)
build("crane_controls", crane_controls, "the gantry crane's control stand", size=512, budget=600)
build("casting", casting, "a foundry casting", budget=1500)
build("chute_lever", chute_lever, "the coaling tower's lever stand", size=512, budget=300)
build("chute_handle", chute_handle, "the coaling tower's lever", size=512, budget=200)
