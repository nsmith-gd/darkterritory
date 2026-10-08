"""The facilities' signature structures (GDD §18, §30), modelled, for StructureKit.Facility to set among its buildings,
so each kind reads by its shape from the line:

  * headframe (the mine head): an 18 m riveted steel headframe, its legs and back-stays, the two sheave wheels at
    the top, the cage's guides down the middle;
  * chem_tank (the chemical works): a riveted storage tank, 7 m across and 7 high, a ladder up its side, a
    railed walkway round its crown, a vent and a stained drip down from its valve;
  * watchtower (the military depot): a timber tower, 8 m to its sandbagged platform, a roof, a searchlight;
  * sandbags: a 3 m length of sandbag wall, four courses;
  * cattle_pen: a 3 m panel of timber pen fence, and cattle_ramp, the loading ramp up to a car's door;
  * signal_box (the switchyard): a timber box on a brick base, windows all round, its lever frame inside, stairs;
  * water_tower: a riveted tank on a timber trestle, its canvas spout swung out over the track.
  * grain_elevator (note 381): a concrete terminal elevator, 36 m along the line and 33 m to the bin floor's roof: four
    slip-formed silos in a row, their pour lines banded and rain-streaked from the top, a caged ladder up the end one;
    the bin-floor gallery along their tops, iron-clad, its windows dark but one; the leg house over the far end, the
    tallest thing for miles, with its own lit window; at the foot the receiving shed and its dark doorway; and the
    loading spout swung down from the gallery to 2.5 m off the track, its sock hanging. Its origin is on the ground at
    the silos' middle, 15 m off the line (StructureKit.Facility).
  * the set pieces, the facilities' working modules (note 398), each at the origin the sim's module is placed from,
    +X along the track and +Y across it the way GreyboxScene.SetPieces turns them:
      - spout_bin (the grain elevator's loading bin): a riveted steel hopper on a braced timber trestle astride the
        track, its roof, a ladder, its sight glass and the spout down to just over a car's roof (the ground under the
        spout's mouth at the origin, +Y toward the lever);
      - hose_stand (the chemical works'): a riser on a concrete plinth, its valve wheel and gauge, the gooseneck the
        hose couples to, a drip tray and an ACID plate (its foot at the origin, -Y toward the track);
      - lift_works (the mine head's steam lift): the ore bin on its legs over the track and its chute, the trough down
        from the headframe and its trestles, the skip's guides up the headframe's face (the ground under the chute at
        the origin, +Y toward the headframe); ore_skip, the skip; lever_handle, a set piece's hand lever.
  * slaughterhouse (note 393): the killing hall, 44 m along the line and 14 deep, soot-black brick on a stone plinth,
    piers every 4.4 m, its windows small and high and barred (one lit: something lives here), a clerestory along its
    slate ridge; two cattle doors at the front either side of the high door the dressing rail comes out of, the rail
    on an A-frame out over the yard with its hooks hanging; behind, the boiler house, its chimney and a water tank on
    its tower. Its origin is on the ground at the hall's middle, 22 m off the line, where the kit's hall stood.
  * the mine head's winding gear (note 410): winding_house, a brick engine house on a stone plinth, its gable to the
    headframe with the ropes coming in high on it to the drum (ROPE_Z, ROPE_END), tall arched windows down its sides
    (one lit), a louvred ventilator on its slate ridge, the boiler house behind it and the banded chimney behind that,
    30 m; spoil_heap, the tip, 12 m of dark shale gullied and burnt in seams, its incline up one flank on a timber
    trestle and a tub tipped at the top.
  * the chemical works' works (note 410): chem_works, the process house, a brick base and the steel frame above clad
    in rusting corrugated iron, acid-streaked and sheets fallen off it, a louvred monitor, the window band broken, a
    sliding door, the outside stair, two guyed stacks and the lead-clad acid tower, and the pipe bridges out over the
    tanks onto the rack; pipe_rack, a 12 m bay of the rack on its portal, its pipes flanged bay to bay, a valve, torn
    lagging; pipe_rack_end, the last bay, its pipes turned down into the ground.
  * foundry_shed (note 420): the casting shed, 80 m of soot-black brick under ten north-light teeth, tall arched windows
    lit by the furnace (`_Glow`: a mask baked to the layer's emissive), a great doorway at each end of its front, the
    cupola through its roof, the 40 m stack behind.
  * coaling_tower (note 422): the coaling stage's concrete bunker on its braced timber trestle, the steel hopper and the
    chute out over the track hung from its jib, its mouth where the sim's chute pours (CHUTE_MOUTH), ladders, the stop's
    lamp on its chain, coal spilled round its feet.
  * the wreck yard's dressing (note 427): its older dead, from before the train's time (the heaps the crew work are the
    train's own cars, SceneArt.Wreckage): dead_boxcar, a wooden boxcar off its trucks, boards gapped and charred, a door
    gone, its roof fallen in; dead_gondola, a steel one rusted and holed, a side stove in, scrap heaped in it;
    loose_truck, a freight truck off its car; yard_shed, the engine shed, corrugated iron rusted through, a bay fallen
    in, its open end's doors off and the rails running in.

Axes (Blender): +Z up, the model's front (-Y, the engine's +Z) toward the line.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402


def materials():
    return {
        "paint": make.lib("paint_oxide", 0.4, tint=(0.75, 0.66, 0.6), rough=0.6),
        "steel": make.lib("iron_plate", 0.6, tint=(0.62, 0.62, 0.6), rough=0.45, metal=0.6),
        "rust": make.lib("rust_heavy", 0.5, tint=(0.6, 0.54, 0.5), rough=0.6, metal=0.4),
        "timber": make.lib("wood_sleeper", 0.9, tint=(0.95, 0.84, 0.7), rough=0.8),
        "grey": make.lib("wood_sleeper", 0.9, tint=(0.75, 0.74, 0.72), rough=0.85),
        "brick": make.lib("brick_soot", 0.5, rough=0.85),
        "sack": make.lib("wool", 3.0, tint=(0.75, 0.68, 0.5), rough=0.95),
        "glass": make.flat("window", (0.08, 0.09, 0.09), rough=0.1),
        "lit": make.flat("lit", (0.9, 0.7, 0.4), rough=0.5),
        "roof": make.lib("corrugated_iron", 0.6, tint=(0.7, 0.7, 0.68), rough=0.5),
        "stain": make.flat("stain", (0.08, 0.1, 0.05), rough=0.4),
        "concrete": make.lib("concrete_stain", 0.25, tint=(0.82, 0.82, 0.8), rough=0.9),
        "streak": make.flat("streak", (0.035, 0.034, 0.03), rough=0.85),
        "clad": make.lib("corrugated_iron", 0.5, tint=(0.62, 0.6, 0.58), rough=0.55, metal=0.3),
        "stone": make.lib("stone_block", 0.8, tint=(0.62, 0.62, 0.6), rough=0.85),
        "slate": make.lib("roof_slate", 0.6, tint=(0.8, 0.8, 0.82), rough=0.6),
        "dark": make.flat("dark", (0.012, 0.011, 0.01), rough=0.9),
        "gore": make.flat("gore", (0.09, 0.025, 0.018), rough=0.35),
        "brass": make.lib("brass", 0.5, tint=(0.95, 0.85, 0.65), rough=0.35, metal=0.7),
        "grip": make.flat("grip", (0.55, 0.42, 0.06), rough=0.6),
        "shale": make.lib("slag", 0.35, tint=(0.4, 0.39, 0.39), rough=0.95),
        "fresh": make.lib("slag", 0.3, tint=(0.2, 0.2, 0.21), rough=0.9),
        "burnt": make.lib("ground_red_clay", 0.4, tint=(0.42, 0.3, 0.26), rough=0.95),
        "acid": make.flat("acid", (0.1, 0.095, 0.035), rough=0.35),
        "lead": make.lib("iron_plate", 0.6, tint=(0.5, 0.52, 0.55), rough=0.65, metal=0.3),
        "glow": make.flat("glow", (1.0, 0.42, 0.12), rough=0.5),
        "coal": make.lib("coal", 0.5, tint=(0.5, 0.5, 0.52), rough=0.55),
        "chalk": make.flat("chalk", (0.42, 0.4, 0.36), rough=0.9),
    }


def build(name, fn, what, size=1024, budget=4000, layer=None, reach=0.008, cage=0.003):
    """A recipe's parts baked down onto its game mesh and finished. A recipe may hand back a third thing, its `_Glow`:
    where its furnace light shows, baked as a mask and lit in the glow's colour (the layer's emissive)."""
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    made = fn(materials())
    parts, extra = made[0], made[1]
    glow = made[2] if len(made) > 2 else None
    kw, caught = {}, {}
    if glow is not None:
        kw = {"masks": {"glow": glow}, "paint": lambda base, ao, baked: (caught.update(baked), base)[1]}
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=size, cage=cage, reach=reach, low=list(make.LOW), **kw)[0]
    if glow is not None:
        _emission(low, caught["glow"], glow.colour, size)
    cook.finish(name, [low] + extra, budget=budget + 200, grime=0.6, made=make.provenance("facility_pieces", what), size=layer)


def _emission(low, mask, colour, size):
    """The game mesh's material lit where `mask` is: its Emission Color an image of the glow's colour there, which
    cook.bake_layers reads into the layer's emissive mask (as the Gannet's sacs, tools/models/recipes/gannet.py)."""
    img = bpy.data.images.new(low.name + "_emit", size, size, alpha=True)
    rgba = np.ones((size, size, 4), np.float32)
    rgba[..., :3] = np.clip(mask, 0, 1)[..., None] * np.array(colour, np.float32)
    img.pixels.foreach_set(rgba[::-1].ravel())
    nt = low.data.materials[0].node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Emission Color"])


def _beam(a, b, w, material, name):
    """A square member from a to b, w across (a steel leg, a timber brace), and its game-mesh twin."""
    d = b - a
    bpy_rot = Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4()
    o = make.box((0, 0, 0), (w / 2, w / 2, d.length / 2), material, bevel=0.01, name=name, rot=bpy_rot)
    shift = Matrix.Translation((a + b) / 2)
    o.data.transform(shift)
    make.LOW[-1].data.transform(shift)
    return o


def headframe(m):
    p = []
    H = 18.0
    # Two legs each side, leaning in, braced every 3 m with X-bracing; the back-stays raking back to the winding house.
    feet = [(-2.2, -1.8), (2.2, -1.8), (-2.2, 1.8), (2.2, 1.8)]
    tops = [(-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)]
    for (fx, fy), (tx, ty) in zip(feet, tops):
        p.append(_beam(Vector((fx, fy, 0)), Vector((tx, ty, H)), 0.35, m["paint"], "leg"))
    for z in range(3, int(H), 3):
        t = z / H
        for (a, b) in ((0, 1), (2, 3), (0, 2), (1, 3)):
            pa = Vector(feet[a] + (0,)).lerp(Vector(tops[a] + (H,)), t)
            pb = Vector(feet[b] + (0,)).lerp(Vector(tops[b] + (H,)), t)
            pa.z = pb.z = z
            p.append(_beam(pa, pb, 0.18, m["paint"], "strut"))
    for sx in (-1, 1):
        p.append(_beam(Vector((sx * 2.0, 9.0, 0)), Vector((sx * 1.0, 1.0, H - 1.5)), 0.3, m["paint"], "backstay"))
    # The head: a platform, the two sheave wheels side by side, the cage's guides.
    p.append(make.box((0, 0, H + 0.1), (1.4, 1.4, 0.1), m["steel"], bevel=0.02, name="deck"))
    for sx in (-0.55, 0.55):
        c = Vector((sx, 0.2, H + 1.9))
        p.append(make.torus(c, (1, 0, 0), 1.6, 0.09, m["steel"], n=40, m=8, name="sheave", low=(20, 4)))
        for k in range(8):
            a = k * math.pi / 4
            d = Vector((0, math.cos(a), math.sin(a)))
            p.append(make.cyl(c, c + d * 1.55, 0.05, m["steel"], n=6, bevel=0, name="spoke", low=4))
        p.append(make.cyl(c - Vector((0.15, 0, 0)), c + Vector((0.15, 0, 0)), 0.2, m["steel"], n=12, name="hub"))
        p.append(_beam(Vector((sx, 0.2, H + 0.2)), c, 0.2, m["paint"], "pedestal"))
        # The rope over the sheave, down the shaft and back to the winding house's drum (ROPE_END).
        p.append(make.cyl(c + Vector((0, -1.6, 0)), Vector((sx, -1.4, 2.0)), 0.03, m["rust"], n=6, bevel=0, name="rope", low=4))
        p.append(make.cyl(c + Vector((0, 1.6, 0.3)), Vector((sx * 0.6, *ROPE_END)), 0.03, m["rust"], n=6, bevel=0, name="rope", low=4))
    for sx in (-0.6, 0.6):
        p.append(_beam(Vector((sx, -0.6, 0)), Vector((sx, -0.6, H)), 0.14, m["timber"], "guide"))
    return p, []


def chem_tank(m):
    p = []
    R, H = 3.5, 7.0
    p.append(make.cyl((0, 0, 0), (0, 0, H), R, m["rust"], n=32, bevel=0.03, name="shell", low=16))
    p.append(make.cyl((0, 0, H), (0, 0, H + 0.8), R, m["rust"], n=32, bevel=0.03, name="crown", low=16, r1=0.6))
    for z in (1.2, 2.6, 4.0, 5.4):
        p.append(make.torus((0, 0, z), (0, 0, 1), R + 0.01, 0.03, m["steel"], n=48, m=4, name="seam", low=False))
    p.append(make.cyl((0, 0, -0.3), (0, 0, 0.05), R + 0.4, m["steel"], n=32, bevel=0.02, name="plinth", low=16))
    # The ladder up the side, a railed walkway round the crown, a vent, a valve and its drip.
    for sx in (-0.25, 0.25):
        p.append(make.cyl((sx, -R - 0.25, 0), (sx, -R - 0.25, H + 0.9), 0.03, m["steel"], n=6, bevel=0, name="stile", low=4))
    for k in range(int(H / 0.3)):
        z = 0.3 + k * 0.3
        p.append(make.cyl((-0.25, -R - 0.25, z), (0.25, -R - 0.25, z), 0.018, m["steel"], n=6, bevel=0, name="rung", low=0))
    for k in range(24):
        a = k * math.pi / 12
        p.append(make.cyl((math.cos(a) * 2.2, math.sin(a) * 2.2, H + 0.5), (math.cos(a) * 2.2, math.sin(a) * 2.2, H + 1.5), 0.025, m["steel"], n=6, bevel=0, name="post", low=4))
    p.append(make.torus((0, 0, H + 1.5), (0, 0, 1), 2.2, 0.03, m["steel"], n=40, m=4, name="rail", low=(20, 3)))
    p.append(make.cyl((0, 0, H + 0.8), (0, 0, H + 2.0), 0.18, m["steel"], n=12, name="vent"))
    p.append(make.cyl((1.5, -R + 0.1, 0.8), (1.5, -R - 0.5, 0.8), 0.12, m["steel"], n=12, name="valve"))
    p.append(make.box((1.5, -R - 0.02, 0.4), (0.25, 0.01, 0.4), m["stain"], bevel=0, name="drip", low=False))
    return p, []


def watchtower(m):
    p = []
    H = 8.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(_beam(Vector((sx * 1.6, sy * 1.6, 0)), Vector((sx * 1.2, sy * 1.2, H)), 0.22, m["timber"], "post"))
    for z in (2.5, 5.2):
        for (a, b) in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
            w = 1.6 - (z / H) * 0.4
            p.append(_beam(Vector((a[0] * w, a[1] * w, z)), Vector((b[0] * w, b[1] * w, z + 2.4)), 0.12, m["timber"], "brace"))
    p.append(make.box((0, 0, H), (1.6, 1.6, 0.08), m["timber"], bevel=0.02, name="deck"))
    # Sandbags round the platform's edge, a corrugated roof on four posts, the searchlight facing the line.
    for k in range(4):
        a = k * math.pi / 2
        c = Vector((math.cos(a) * 1.45, math.sin(a) * 1.45, H + 0.35))
        rot = Matrix.Rotation(a, 4, "Z")
        p.append(make.box((0, 0, 0), (0.18, 1.5, 0.3), m["sack"], bevel=0.08, segments=3, name="bags", rot=rot))
        make.LOW[-1].data.transform(Matrix.Translation(c))
        p[-1].data.transform(Matrix.Translation(c))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.cyl((sx * 1.4, sy * 1.4, H), (sx * 1.4, sy * 1.4, H + 2.2), 0.07, m["timber"], n=8, name="roofpost"))
    p.append(make.box((0, 0, H + 2.3), (1.9, 1.9, 0.05), m["roof"], bevel=0.01, name="roof"))
    lamp_at = Vector((0, -1.3, H + 1.0))
    p.append(make.cyl(lamp_at + Vector((0, 0.25, 0)), lamp_at, 0.3, m["steel"], n=16, name="searchlight"))
    p.append(make.cyl(lamp_at + Vector((0, 0.3, -0.35)), lamp_at + Vector((0, 0.3, 0)), 0.05, m["steel"], n=8, name="yoke"))
    glow = cook.eyes_at([lamp_at + Vector((0, -0.01, 0))], 0.26, colour=(0.95, 0.9, 0.75), name="searchlight")
    for o in glow:
        o.data.transform(Matrix.Diagonal((1, 0.1, 1, 1)) @ Matrix.Translation(-lamp_at))
        o.data.transform(Matrix.Translation(lamp_at))
    # A ladder up one face.
    for sx in (-0.25, 0.25):
        p.append(make.cyl((sx, -1.75, 0), (sx, -1.35, H), 0.04, m["timber"], n=6, bevel=0, name="stile", low=4))
    return p, glow


def sandbags(m):
    p = []
    L = 3.0
    for course in range(4):
        z = 0.14 + course * 0.24
        n = 6
        for k in range(n):
            x = -L / 2 + (k + 0.5 + (0.5 if course % 2 else 0)) * L / n
            if x > L / 2 - 0.1:
                continue
            for sy in (-0.18, 0.18):
                o = make.box((x, sy, z), (0.24, 0.15, 0.11), m["sack"], bevel=0.07, segments=3, name="bag")
                p.append(o)
    return p, []


def cattle_pen(m):
    p = []
    for x in (-1.5, 0, 1.5):
        p.append(make.box((x, 0, 0.7), (0.07, 0.07, 0.75), m["grey"], bevel=0.015, name="post"))
    for z in (0.35, 0.75, 1.15):
        p.append(make.box((0, -0.09, z), (1.55, 0.025, 0.08), m["grey"], bevel=0.008, name="rail"))
    return p, []


def cattle_ramp(m):
    p = []
    # A timber ramp 4 m long rising to a car's floor (1.1 m), cleated, with side rails.
    L, Hh = 4.0, 1.1
    ang = math.atan2(Hh, L)
    rot = Matrix.Rotation(-ang, 4, "X")
    deck = make.box((0, 0, 0), (0.8, L / 2 / math.cos(ang), 0.05), m["grey"], bevel=0.01, name="deck", rot=rot)
    shift = Matrix.Translation((0, 0, Hh / 2))
    deck.data.transform(shift)
    make.LOW[-1].data.transform(shift)
    p.append(deck)
    for k in range(10):
        t = (k + 0.5) / 10
        p.append(make.box((0, -L / 2 + t * L, t * Hh + 0.06), (0.75, 0.025, 0.02), m["grey"], bevel=0.004, name="cleat", low=False))
    for sx in (-0.85, 0.85):
        for y in (-L / 2, 0, L / 2):
            t = (y + L / 2) / L
            p.append(make.box((sx, y, t * Hh + 0.5), (0.06, 0.06, 0.55), m["grey"], bevel=0.01, name="post"))
        p.append(_beam(Vector((sx, -L / 2, 1.0)), Vector((sx, L / 2, Hh + 1.0)), 0.08, m["grey"], "rail"))
    return p, []


def signal_box(m):
    p = []
    W, D = 3.2, 2.4
    p.append(make.box((0, 0, 1.25), (W / 2, D / 2, 1.25), m["brick"], bevel=0.02, name="base"))
    p.append(make.box((0, 0, 3.6), (W / 2 + 0.05, D / 2 + 0.05, 1.1), m["grey"], bevel=0.02, name="box"))
    # Windows all round the top half (dark glass, one lit), glazing bars.
    for sy in (-1, 1):
        p.append(make.box((0, sy * (D / 2 + 0.06), 3.9), (W / 2 - 0.2, 0.01, 0.55), m["lit" if sy < 0 else "glass"], bevel=0, name="window", low=False))
        for k in range(5):
            x = -W / 2 + 0.2 + k * (W - 0.4) / 4
            p.append(make.box((x, sy * (D / 2 + 0.07), 3.9), (0.03, 0.02, 0.56), m["grey"], bevel=0, name="bar", low=False))
    for sx in (-1, 1):
        p.append(make.box((sx * (W / 2 + 0.06), 0, 3.9), (0.01, D / 2 - 0.2, 0.55), m["glass"], bevel=0, name="window", low=False))
    # The hipped roof, a stove pipe, the stairs up the side to the door.
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=2.5, radius2=0.2, depth=1.2)
    roof = bpy.context.view_layer.objects.active
    roof.data.transform(Matrix.Translation((0, 0, 5.3)) @ Matrix.Rotation(math.pi / 4, 4, "Z") @ Matrix.Diagonal((1.0, 0.78, 1, 1)))
    roof.data.materials.append(m["roof"])
    r_low = roof.copy()
    r_low.data = roof.data.copy()
    bpy.context.scene.collection.objects.link(r_low)
    make.LOW.append(r_low)
    p.append(roof)
    p.append(make.cyl((1.0, 0.6, 5.2), (1.0, 0.6, 6.2), 0.08, m["rust"], n=10, name="pipe"))
    for k in range(10):
        z = 0.25 + k * 0.25
        p.append(make.box((W / 2 + 0.5, -D / 2 + 0.3 + k * 0.23, z), (0.4, 0.13, 0.03), m["grey"], bevel=0.005, name="step"))
    p.append(_beam(Vector((W / 2 + 0.9, -D / 2 + 0.3, 1.0)), Vector((W / 2 + 0.9, D / 2 - 0.3, 3.5)), 0.05, m["grey"], "handrail"))
    return p, []


def water_tower(m):
    p = []
    H = 6.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(_beam(Vector((sx * 2.0, sy * 2.0, 0)), Vector((sx * 1.6, sy * 1.6, H)), 0.3, m["timber"], "leg"))
    for z in (2.0, 4.0):
        for (a, b) in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
            w = 2.0 - z / H * 0.4
            p.append(_beam(Vector((a[0] * w, a[1] * w, z)), Vector((b[0] * w, b[1] * w, z)), 0.15, m["timber"], "girt"))
    p.append(make.box((0, 0, H + 0.1), (2.0, 2.0, 0.1), m["timber"], bevel=0.02, name="deck"))
    p.append(make.cyl((0, 0, H + 0.2), (0, 0, H + 3.4), 1.9, m["rust"], n=28, bevel=0.03, name="tank", low=14))
    p.append(make.cyl((0, 0, H + 3.4), (0, 0, H + 4.2), 1.95, m["roof"], n=28, bevel=0.02, name="lid", low=14, r1=0.2))
    for z in (H + 1.0, H + 2.2):
        p.append(make.torus((0, 0, z), (0, 0, 1), 1.91, 0.04, m["steel"], n=40, m=4, name="hoop", low=False))
    # The spout: a pipe out from the tank's base over the track, its canvas sock hanging from the end.
    p.append(make.cyl((0, -1.8, H + 0.5), (0, -4.0, H + 0.2), 0.16, m["steel"], n=12, name="spout"))
    p.append(make.cyl((0, -4.0, H + 0.2), (0, -4.1, H - 1.4), 0.15, m["sack"], n=10, name="sock", r1=0.12))
    p.append(make.cyl((0.2, -1.9, H - 0.3), (0.2, -1.9, 1.0), 0.02, m["steel"], n=6, bevel=0, name="chain", low=4))
    return p, []


def grain_elevator(m):
    p = []
    R, H = 4.4, 26.0  # the silos: four in a row along X, touching
    xs = [-13.2, -4.4, 4.4, 13.2]
    for x in xs:
        p.append(make.cyl((x, 0, -0.3), (x, 0, H), R, m["concrete"], n=40, bevel=0.02, name="silo", low=28))
        # The slip-form's pour lines, a lip every 1.5 m (baked into the shell, not kept).
        for z in [k * 1.5 for k in range(1, int(H / 1.5))]:
            p.append(make.torus((x, 0, z), (0, 0, 1), R + 0.005, 0.025, m["concrete"], n=40, m=4, name="pour", low=False))
        # Rain off the gallery's eaves: dark streaks down the line side of each, some long, some short.
        for k, (a, length) in enumerate(((-0.55, 14.0), (-0.2, 22.0), (0.15, 9.0), (0.5, 17.0), (-0.85, 6.0))):
            ang = -math.pi / 2 + a + 0.07 * (xs.index(x) - 1.5)
            cx, cy = math.cos(ang) * (R + 0.02), math.sin(ang) * (R + 0.02)
            p.append(make.box((x + cx, cy, H - length / 2), (0.16 + 0.1 * (k % 2), 0.01, length / 2), m["streak"], bevel=0,
                              name="streak", low=False, rot=Matrix.Rotation(ang + math.pi / 2, 4, "Z")))
    # Between the silos' necks, the interstice walls that close the row at the top and bottom.
    for x0, x1 in zip(xs, xs[1:]):
        xm = (x0 + x1) / 2
        p.append(make.box((xm, 0, H / 2), (0.6, R * 0.55, H / 2 + 0.3), m["concrete"], bevel=0.02, name="web"))
    # The bin floor: a long iron-clad gallery along the silos' tops, its windows dark, one lit.
    G0, G1 = H, H + 5.0
    p.append(make.box((0, 0, H + 0.15), (17.9, 4.6, 0.15), m["concrete"], bevel=0.02, name="slab"))
    p.append(make.box((0, 0, (G0 + G1) / 2 + 0.15), (17.6, 4.0, 2.5), m["clad"], bevel=0.03, name="gallery"))
    p.append(make.box((0, 0, G1 + 0.3), (18.0, 4.4, 0.15), m["roof"], bevel=0.02, name="gallery_roof"))
    for k in range(9):
        x = -14.0 + k * 3.5
        mat = m["lit"] if k == 6 else m["glass"]
        p.append(make.box((x, -4.02, G0 + 2.7), (0.7, 0.03, 0.55), mat, bevel=0, name="window", low=False))
        p.append(make.box((x, -4.03, G0 + 2.1), (0.8, 0.04, 0.05), m["rust"], bevel=0, name="sill", low=False))
    # The leg house over the far end: the elevator's boot-to-head legs run up inside it, so it stands 12 m clear.
    L0, L1 = G1 + 0.3, G1 + 12.0
    p.append(make.box((12.5, 0.5, (L0 + L1) / 2), (4.0, 3.6, (L1 - L0) / 2), m["clad"], bevel=0.03, name="leg_house"))
    p.append(make.box((12.5, 0.5, L1 + 1.2), (4.3, 3.9, 0.1), m["roof"], bevel=0.02, name="leg_eave"))
    p.append(make.cyl((12.5, 0.5, L1), (12.5, 0.5, L1 + 1.2), 3.6, m["roof"], n=4, bevel=0, name="leg_roof", r1=0.4, low=4))
    for z in (L0 + 3.0, L0 + 7.5):
        for x in (10.5, 14.5):
            mat = m["lit"] if (z, x) == (L0 + 7.5, 14.5) else m["glass"]
            p.append(make.box((x, -3.12, z), (0.6, 0.03, 0.8), mat, bevel=0, name="window", low=False))
    # A caged ladder up the near end's silo to the gallery: its stiles, rungs and the hoops of the cage.
    lx, ly = -13.2 - R - 0.25, 0.0
    for dy in (-0.25, 0.25):
        p.append(make.cyl((lx, ly + dy, 0.5), (lx, ly + dy, G0 + 1.0), 0.03, m["rust"], n=6, bevel=0, name="stile", low=False))
    for z in [0.8 + k * 0.3 for k in range(int((G0 - 0.5) / 0.3))]:
        p.append(make.cyl((lx, ly - 0.25, z), (lx, ly + 0.25, z), 0.015, m["rust"], n=5, bevel=0, name="rung", low=False))
    for z in [3.0 + k * 1.2 for k in range(int((G0 - 2.0) / 1.2))]:
        p.append(make.torus((lx - 0.35, ly, z), (0, 0, 1), 0.38, 0.02, m["rust"], n=12, m=4, name="hoop", low=False))
    # At the foot, the receiving shed against the silos on the line side, its doorway dark.
    p.append(make.box((-4.4, -R - 2.0, 2.5), (6.0, 2.2, 2.5), m["clad"], bevel=0.03, name="shed"))
    p.append(make.box((-4.4, -R - 2.0, 5.15), (6.3, 2.5, 0.12), m["roof"], bevel=0.02, name="shed_roof",
                      rot=Matrix.Rotation(-0.12, 4, "X")))
    p.append(make.box((-4.4, -R - 4.22, 1.6), (1.6, 0.03, 1.6), m["glass"], bevel=0, name="doorway", low=False))
    # The loading spout: from the gallery's front, swung down and out to 2.5 m off the track at 6 m up, its sock hanging.
    top, end = Vector((4.4, -4.0, G0 + 1.5)), Vector((4.4, -12.5, 6.0))
    p.append(make.cyl(top, end, 0.35, m["rust"], n=12, bevel=0.01, name="spout", low=8))
    for t in (0.25, 0.5, 0.75):
        p.append(make.torus(top.lerp(end, t), end - top, 0.38, 0.05, m["steel"], n=12, m=4, name="spout_band", low=False))
    p.append(make.cyl(end, end + Vector((0, -0.2, -1.6)), 0.32, m["sack"], n=10, bevel=0, name="sock", r1=0.26, low=6))
    # The cables that hold the spout up, from the leg house's corner.
    p.append(make.cyl((9.0, -3.0, L0 + 1.0), end + Vector((0, 0, 0.3)), 0.025, m["steel"], n=5, bevel=0, name="stay", low=False))
    p.append(make.cyl((9.0, -3.0, L0 + 1.0), top.lerp(end, 0.5) + Vector((0, 0, 0.3)), 0.025, m["steel"], n=5, bevel=0,
                      name="stay", low=False))
    return p, []


def _prism(p, verts, faces, material, name):
    """A piece built from its own faces (a gable's triangle), in the high set and, plain, in the game mesh."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.materials.append(material)
    o = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(o)
    p.append(o)
    low = o.copy()
    low.data = mesh.copy()
    low.name = name + "_low"
    bpy.context.scene.collection.objects.link(low)
    make.LOW.append(low)


def slaughterhouse(m):
    p = []
    L, D, H = 22.0, 7.0, 9.0  # half its length along the line (X), half its depth (Y), the eaves over the ground
    # The hall: on its stone plinth, brick to a corbelled cornice; a pier at each corner and every 4.4 m between.
    p.append(make.box((0, 0, 0.35), (L + 0.12, D + 0.12, 0.65), m["stone"], bevel=0.02, name="plinth"))
    p.append(make.box((0, 0, (1.0 + H) / 2), (L, D, (H - 1.0) / 2), m["brick"], bevel=0.02, name="walls"))
    p.append(make.box((0, 0, H + 0.2), (L + 0.22, D + 0.22, 0.2), m["brick"], bevel=0.02, name="cornice"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.box((sx * L, sy * D, (1.0 + H) / 2), (0.4, 0.4, (H - 1.0) / 2 + 0.05), m["brick"], bevel=0.02, name="corner"))
    xs = [-L + 4.4 * k for k in range(1, 10)]
    for x in xs:
        for sy in (-1, 1):
            p.append(make.box((x, sy * (D + 0.12), (1.0 + H) / 2), (0.3, 0.12, (H - 1.0) / 2), m["brick"], bevel=0.01, name="pier", low=False))
    # The windows: small and high between the piers, barred; the dark of a broken one; one lit.
    for k, x in enumerate([-L + 2.2 + 4.4 * k for k in range(10)]):
        for sy in (-1, 1):
            if sy < 0 and abs(x) < 1.5:
                continue
            state = "lit" if (sy, k) == (-1, 2) else "dark" if (k * 7 + (sy > 0)) % 5 == 0 else "glass"
            y = sy * (D + 0.01)
            p.append(make.box((x, y, 7.3), (0.45, 0.02, 0.7), m[state], bevel=0, name="window", low=False))
            p.append(make.box((x, sy * (D + 0.04), 6.55), (0.6, 0.05, 0.06), m["stone"], bevel=0.005, name="sill", low=False))
            for bx in (-0.3, -0.1, 0.1, 0.3):
                p.append(make.box((x + bx, sy * (D + 0.05), 7.3), (0.012, 0.012, 0.72), m["rust"], bevel=0, name="bar", low=False))
            # Rust and soot run down the brick from the sill.
            p.append(make.box((x + 0.1 * ((k % 3) - 1), sy * (D + 0.015), 5.2), (0.12 + 0.05 * (k % 2), 0.01, 1.3), m["streak"], bevel=0,
                              name="streak", low=False))
    # The front (toward the line, -Y): a cattle door each side, their leaves hung open, and between them the high door
    # the dressing rail comes out of, its iron canopy over it and the dark run down the brick under it.
    for x in (-10.5, 10.5):
        p.append(make.box((x, -D - 0.02, 2.75), (1.6, 0.04, 1.75), m["dark"], bevel=0, name="doorway"))
        p.append(make.box((x, -D - 0.08, 4.62), (1.9, 0.08, 0.12), m["steel"], bevel=0.01, name="lintel"))
        for side in (-1, 1):
            hinge = Vector((x + side * 1.6, -D - 0.05, 2.75))
            leaf = Matrix.Rotation(side * -2.0, 4, "Z")
            centre = hinge + (leaf.to_3x3() @ Vector((-side * 0.8, 0, 0)))
            p.append(make.box(centre, (0.8, 0.05, 1.7), m["timber"], bevel=0.01, name="leaf", rot=leaf))
    p.append(make.box((0, -D - 0.02, 4.3), (1.1, 0.04, 1.4), m["dark"], bevel=0, name="rail_door"))
    p.append(make.box((0, -D - 0.6, 6.15), (1.6, 0.6, 0.05), m["roof"], bevel=0.01, name="canopy", rot=Matrix.Rotation(0.18, 4, "X")))
    p.append(make.box((0, -D - 0.015, 1.9), (0.5, 0.01, 1.0), m["gore"], bevel=0, name="run", low=False))
    p.append(make.box((0.3, -D - 0.016, 2.6), (0.16, 0.01, 0.9), m["gore"], bevel=0, name="run", low=False))
    # The dressing rail: an iron beam out from the door over the yard on an A-frame, its hooks hanging on their chains.
    rail_z, out = 4.9, -11.5
    p.append(make.box((0, (-D + out) / 2, rail_z), (0.06, (out + D) / -2, 0.11), m["steel"], bevel=0.005, name="rail"))
    for side in (-1, 1):
        p.append(make.cyl((side * 1.3, out + 0.5, 0), (0, out + 0.5, rail_z + 0.3), 0.07, m["rust"], n=8, bevel=0, name="aframe", low=4))
    p.append(make.cyl((-0.9, out + 0.5, 1.6), (0.9, out + 0.5, 1.6), 0.05, m["rust"], n=8, bevel=0, name="aframe_tie", low=4))
    for k in range(5):
        y = -D - 0.9 - k * 0.8
        drop = 0.5 + 0.35 * ((k * 3) % 4) / 3
        p.append(make.cyl((0, y, rail_z - 0.1), (0, y, rail_z - drop), 0.012, m["rust"], n=6, bevel=0, name="chain", low=3))
        p.append(make.torus((0, y, rail_z - drop - 0.09), (1, 0, 0), 0.08, 0.014, m["steel"], n=10, m=4, name="hook", low=(8, 3)))
    # The roof: slate both slopes to the ridge, its gables in brick, and a clerestory along the ridge.
    rise = 3.2
    pitch = math.atan2(rise, D + 0.22)
    run = math.hypot(D + 0.6, (D + 0.6) * math.tan(pitch))
    for sy in (-1, 1):
        R = Matrix.Rotation(-sy * pitch, 4, "X")
        centre = Vector((0, sy * (D + 0.6) / 2, H + 0.4 + rise * (0.6 / (D + 0.6)) * -0.5 + rise / 2))
        p.append(make.box(centre, (L + 0.5, run / 2, 0.08), m["slate"], bevel=0.01, name="roof", rot=R))
    for sx in (-1, 1):
        x = sx * (L + 0.05)
        v = [(x, -D - 0.2, H + 0.4), (x, D + 0.2, H + 0.4), (x, 0, H + 0.4 + rise)]
        _prism(p, v, [(0, 1, 2) if sx > 0 else (0, 2, 1)], m["brick"], "gable")
    C0, C1 = H + 0.4 + rise - 1.4, H + 0.4 + rise + 1.3
    p.append(make.box((0, 0, (C0 + C1) / 2), (L - 5, 1.6, (C1 - C0) / 2), m["clad"], bevel=0.02, name="clerestory"))
    p.append(make.box((0, 0, C1 + 0.06), (L - 4.8, 1.95, 0.06), m["slate"], bevel=0.01, name="clerestory_roof"))
    for k in range(int((2 * L - 10) / 1.2)):
        x = -L + 5.6 + k * 1.2
        for sy in (-1, 1):
            p.append(make.box((x, sy * 1.62, C1 - 0.6), (0.45, 0.02, 0.4), m["dark"], bevel=0, name="louvre", low=False))
    # Behind: the boiler house, its flue into the hall, its chimney; and a water tank on its tower.
    p.append(make.box((12.0, D + 3.4, 2.6), (4.0, 3.4, 2.6), m["brick"], bevel=0.02, name="boiler_house"))
    p.append(make.box((12.0, D + 3.4, 5.35), (4.3, 3.7, 0.12), m["roof"], bevel=0.01, name="boiler_roof", rot=Matrix.Rotation(-0.1, 4, "X")))
    p.append(make.cyl((15.0, D + 4.5, 0), (15.0, D + 4.5, 27.0), 1.1, m["brick"], n=12, bevel=0.01, name="chimney", r1=0.7, low=8))
    p.append(make.cyl((15.0, D + 4.5, 26.4), (15.0, D + 4.5, 27.2), 0.85, m["brick"], n=12, bevel=0.01, name="chimney_cap", low=8))
    p.append(make.cyl((9.0, D + 1.0, 4.0), (9.0, D - 0.2, 4.0), 0.3, m["rust"], n=10, bevel=0, name="flue", low=6))
    tx, ty = -15.0, D + 3.5
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.cyl((tx + sx * 1.5, ty + sy * 1.5, 0), (tx + sx * 1.2, ty + sy * 1.2, 13.0), 0.1, m["rust"], n=6, bevel=0, name="tank_leg", low=4))
    p.append(make.cyl((tx, ty, 13.0), (tx, ty, 15.6), 1.7, m["rust"], n=24, bevel=0.02, name="tank", low=10))
    p.append(make.cyl((tx, ty, 15.6), (tx, ty, 16.1), 1.75, m["roof"], n=24, bevel=0.01, name="tank_lid", r1=0.2, low=10))
    p.append(make.cyl((tx + 1.0, ty - 1.0, 13.0), (tx + 1.0, ty - 1.0, 1.0), 0.08, m["rust"], n=8, bevel=0, name="tank_main", low=4))
    return p, []


def _ladder(p, m, x, y, z0, z1, width=0.45, name="ladder"):
    """A ladder up the +Y face at (x, y): two stiles and its rungs every 0.3 m (kept: nothing behind them to bake onto)."""
    for dx in (-width / 2, width / 2):
        p.append(make.cyl((x + dx, y, z0), (x + dx, y, z1), 0.03, m["rust"], n=6, bevel=0, name=name + "_stile", low=4))
    z = z0 + 0.3
    while z < z1 - 0.1:
        p.append(make.cyl((x - width / 2, y, z), (x + width / 2, y, z), 0.016, m["rust"], n=5, bevel=0, name=name + "_rung", low=3))
        z += 0.3


def _ladder_front(p, m, x, y, z0, z1, width=0.45):
    """A ladder up a -Y face (toward the line) at (x, y), as _ladder; its stiles stood off the face on brackets."""
    _ladder(p, m, x, y, z0, z1, width, name="tower_ladder")
    for z in (z0 + 0.5, (z0 + z1) / 2, z1 - 0.5):
        for dx in (-width / 2, width / 2):
            p.append(make.cyl((x + dx, y, z), (x + dx, y + 0.32, z), 0.02, m["rust"], n=5, bevel=0, name="bracket", low=3))


def spout_bin(m):
    p = []
    MOUTH = 5.2
    top = MOUTH + 3.2
    # The trestle: four timber legs astride the track, braced along each side (never across: the train goes under).
    feet = [(sx * 2.4, sy * 2.8) for sx in (-1, 1) for sy in (-1, 1)]
    heads = [(sx * 1.8, sy * 2.2) for sx in (-1, 1) for sy in (-1, 1)]
    for (fx, fy), (hx, hy) in zip(feet, heads):
        p.append(_beam(Vector((fx, fy, -0.3)), Vector((hx, hy, top)), 0.3, m["timber"], "leg"))
    for sy in (-1, 1):
        for z0, z1 in ((0.6, 4.2), (4.2, 7.6)):
            f0 = (z0 + 0.3) / (top + 0.3)
            f1 = (z1 + 0.3) / (top + 0.3)
            xa = 2.4 - 0.6 * f0
            xb = 2.4 - 0.6 * f1
            ya = sy * (2.8 - 0.6 * f0)
            yb = sy * (2.8 - 0.6 * f1)
            p.append(_beam(Vector((-xa, ya, z0)), Vector((xb, yb, z1)), 0.16, m["timber"], "brace"))
            p.append(_beam(Vector((xa, ya, z0)), Vector((-xb, yb, z1)), 0.16, m["timber"], "brace"))
        p.append(_beam(Vector((-2.0, sy * 2.35, 6.6)), Vector((2.0, sy * 2.35, 6.6)), 0.18, m["timber"], "girt"))
    for sx in (-1, 1):
        p.append(_beam(Vector((sx * 1.85, -2.25, top - 0.3)), Vector((sx * 1.85, 2.25, top - 0.3)), 0.22, m["timber"], "cap"))
    # The deck, the hopper on it, its roof.
    p.append(make.box((0, 0, top + 0.07), (2.3, 2.7, 0.07), m["timber"], bevel=0.01, name="deck"))
    H0, H1 = top + 0.14, top + 2.6
    p.append(make.box((0, 0, (H0 + H1) / 2), (2.0, 2.4, (H1 - H0) / 2), m["steel"], bevel=0.02, name="hopper"))
    for z in (H0 + 0.6, H0 + 1.3, H0 + 2.0):
        p.append(make.box((0, 0, z), (2.03, 2.43, 0.04), m["rust"], bevel=0.005, name="band", low=False))
    p.append(make.cyl((0, 0, top), (0, 0, top - 0.8), 1.9, m["steel"], n=4, bevel=0, name="cone", r1=0.35, low=4))
    rise = 0.7
    for sy in (-1, 1):
        R = Matrix.Rotation(-sy * math.atan2(rise, 2.7), 4, "X")
        p.append(make.box((0, sy * 1.35, H1 + rise / 2 + 0.05), (2.35, 1.48, 0.05), m["roof"], bevel=0.01, name="roof", rot=R))
    # The spout: the slide gate under the hopper, the pipe down to the mouth, its canvas sock.
    p.append(make.box((0, 0, top - 0.95), (0.45, 0.45, 0.12), m["rust"], bevel=0.01, name="gate"))
    p.append(make.cyl((0, 0, top - 1.0), (0, 0, MOUTH + 0.35), 0.26, m["steel"], n=12, bevel=0.01, name="spout", low=8))
    p.append(make.cyl((0, 0, MOUTH + 0.4), (0, 0, MOUTH), 0.3, m["sack"], n=12, bevel=0, name="sock", r1=0.24, low=8))
    # The sight glass on the lever's side (the scene draws the grain in it), and the ladder up to the deck beside it.
    p.append(make.box((0.9, 2.42, (H0 + H1) / 2), (0.18, 0.02, (H1 - H0) / 2 - 0.15), m["glass"], bevel=0, name="glass", low=False))
    p.append(make.box((0.9, 2.44, (H0 + H1) / 2), (0.24, 0.03, (H1 - H0) / 2 - 0.08), m["rust"], bevel=0.005, name="glass_frame"))
    _ladder(p, m, -1.0, 2.55, 0.0, top + 0.1)
    # Grain spilt and rotted under the spout, and the rust run down the hopper from its bands.
    p.append(make.box((0, 0, 0.02), (1.6, 1.2, 0.02), m["stain"], bevel=0, name="spill"))
    for k, x in enumerate((-1.4, -0.3, 0.8, 1.6)):
        p.append(make.box((x, -2.41, H0 + 0.9), (0.1, 0.01, 0.8 - 0.15 * (k % 2)), m["streak"], bevel=0, name="streak", low=False))
    return p, []


def hose_stand(m):
    p = []
    p.append(make.box((0, 0, 0.12), (0.45, 0.45, 0.14), m["concrete"], bevel=0.02, name="plinth"))
    p.append(make.box((0, -0.55, 0.03), (0.55, 0.35, 0.03), m["rust"], bevel=0.01, name="tray"))
    p.append(make.box((0, -0.55, 0.065), (0.5, 0.3, 0.004), m["stain"], bevel=0, name="residue", low=False))
    # The riser, its flanges, and the gooseneck at the top the hose couples to (the sim's outlet, 3.4 m up).
    p.append(make.cyl((0, 0, 0.25), (0, 0, 3.2), 0.12, m["steel"], n=12, bevel=0.005, name="riser", low=8))
    for z in (0.3, 1.0, 2.2, 3.15):
        p.append(make.cyl((0, 0, z - 0.03), (0, 0, z + 0.03), 0.18, m["rust"], n=12, bevel=0, name="flange", low=8))
    p += make.pipe([(0, 0, 3.2), (0, 0, 3.45), (0, -0.35, 3.55), (0, -0.55, 3.4)], 0.12, m["steel"], name="neck", n=12, low=8)
    p.append(make.cyl((0, -0.55, 3.4), (0, -0.62, 3.25), 0.16, m["paint"], n=12, bevel=0, name="coupling", low=8))
    # The valve and its wheel on the track side, the gauge's brass housing over it (the scene shows its needle colour).
    p.append(make.cyl((0, -0.1, 1.2), (0, -0.32, 1.2), 0.09, m["steel"], n=10, bevel=0, name="valve", low=6))
    p += make.handwheel((0, -0.35, 1.2), (0, -1, 0), 0.24, m["paint"], spokes=4, name="wheel")
    p.append(make.cyl((0, -0.1, 1.75), (0, -0.2, 1.75), 0.15, m["brass"], n=16, bevel=0.005, name="gauge", low=10))
    p.append(make.box((0, -0.17, 2.3), (0.2, 0.01, 0.13), m["paint"], bevel=0.003, name="plate"))
    p.append(make.stencil("ACID", (0, -0.18, 2.3), (0, -1, 0), (0, 0, 1), 0.12, m["stain"], name="acid"))
    return p, []


def lift_works(m):
    p = []
    MOUTH = 4.6
    top = MOUTH + 1.6
    feet = [(sx * 1.9, sy * 2.6) for sx in (-1, 1) for sy in (-1, 1)]
    heads = [(sx * 1.4, sy * 2.0) for sx in (-1, 1) for sy in (-1, 1)]
    for (fx, fy), (hx, hy) in zip(feet, heads):
        p.append(_beam(Vector((fx, fy, -0.3)), Vector((hx, hy, top)), 0.26, m["timber"], "leg"))
    for sy in (-1, 1):
        f0, f1 = 0.9 / (top + 0.3), 5.2 / (top + 0.3)
        p.append(_beam(Vector((-(1.9 - 0.5 * f0), sy * (2.6 - 0.6 * f0), 0.6)), Vector((1.9 - 0.5 * f1, sy * (2.6 - 0.6 * f1), 4.9)), 0.14, m["timber"], "brace"))
        p.append(_beam(Vector((1.9 - 0.5 * f0, sy * (2.6 - 0.6 * f0), 0.6)), Vector((-(1.9 - 0.5 * f1), sy * (2.6 - 0.6 * f1), 4.9)), 0.14, m["timber"], "brace"))
    p.append(make.box((0, 0, top + 0.06), (1.75, 2.3, 0.06), m["timber"], bevel=0.01, name="deck"))
    B1 = top + 1.8
    p.append(make.box((0, 0, (top + 0.12 + B1) / 2), (1.6, 2.2, (B1 - top - 0.12) / 2), m["steel"], bevel=0.02, name="bin"))
    for z in (top + 0.6, top + 1.2):
        p.append(make.box((0, 0, z), (1.63, 2.23, 0.04), m["rust"], bevel=0.005, name="band", low=False))
    p.append(make.cyl((0, 0, top), (0, 0, top - 0.6), 1.5, m["steel"], n=4, bevel=0, name="cone", r1=0.35, low=4))
    p.append(make.cyl((0, 0, top - 0.6), (0, 0, MOUTH), 0.3, m["rust"], n=12, bevel=0.01, name="chute", low=8))
    p.append(make.box((0, 0, top - 0.75), (0.42, 0.42, 0.1), m["steel"], bevel=0.01, name="gate"))
    # The ore gauge on the track side (the scene draws what's left in it).
    p.append(make.box((0, -2.22, (top + B1) / 2), (0.3, 0.02, (B1 - top) / 2 - 0.1), m["glass"], bevel=0, name="glass", low=False))
    p.append(make.box((0, -2.24, (top + B1) / 2), (0.36, 0.03, (B1 - top) / 2 - 0.05), m["rust"], bevel=0.005, name="glass_frame"))
    # The trough from the headframe's tip down onto the bin, an open iron channel on two trestles.
    a, b = Vector((0, 13.8, 11.0)), Vector((0, 1.6, B1 + 0.1))
    d = b - a
    rot = Vector((0, 1, 0)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    mid = (a + b) / 2
    p.append(make.box(mid, (0.35, d.length / 2, 0.04), m["rust"], bevel=0.005, name="trough_floor", rot=rot))
    for sx in (-1, 1):
        side = mid + (rot.to_3x3() @ Vector((sx * 0.35, 0, 0.18)))
        p.append(make.box(side, (0.03, d.length / 2, 0.2), m["rust"], bevel=0.005, name="trough_side", rot=rot))
    for t in (0.3, 0.65):
        q = a.lerp(b, t)
        for sx in (-1, 1):
            p.append(_beam(Vector((sx * 0.7, q.y, 0)), Vector((sx * 0.35, q.y, q.z - 0.1)), 0.16, m["timber"], "trestle"))
        p.append(_beam(Vector((-0.5, q.y, q.z - 0.15)), Vector((0.5, q.y, q.z - 0.15)), 0.14, m["timber"], "trestle_cap"))
    # The skip's guides up the headframe's face toward the track.
    for sx in (-1, 1):
        p.append(_beam(Vector((sx * 0.8, 13.6, 0)), Vector((sx * 0.8, 13.6, 12.2)), 0.12, m["steel"], "guide"))
    for z in (2.0, 6.0, 10.0):
        p.append(_beam(Vector((-0.9, 13.65, z)), Vector((0.9, 13.65, z)), 0.1, m["steel"], "guide_tie"))
    p.append(make.box((0, 0, 0.02), (1.4, 1.1, 0.02), m["stain"], bevel=0, name="spill"))
    return p, []


def ore_skip(m):
    p = []
    p.append(make.box((0, 0, 0), (0.7, 0.6, 0.8), m["steel"], bevel=0.03, name="skip"))
    for z in (-0.5, 0.0, 0.5):
        p.append(make.box((0, 0, z), (0.72, 0.62, 0.035), m["rust"], bevel=0.005, name="band", low=False))
    p.append(make.box((0, 0, 0.79), (0.62, 0.52, 0.02), m["stain"], bevel=0, name="ore", low=False))
    for sx in (-1, 1):
        p.append(make.cyl((sx * 0.72, 0, 0.3), (sx * 0.72, 0, 1.2), 0.03, m["rust"], n=6, bevel=0, name="bail", low=4))
    p.append(make.cyl((-0.72, 0, 1.2), (0.72, 0, 1.2), 0.03, m["rust"], n=6, bevel=0, name="bail_top", low=4))
    return p, []


def lever_handle(m):
    """A set piece's hand lever along +X from its pivot: a flat iron bar, its end a worn yellow grip."""
    p = []
    p.append(make.box((0.25, 0, 0), (0.25, 0.025, 0.012), m["rust"], bevel=0.004, name="bar"))
    p.append(make.cyl((0.5, 0, 0), (0.62, 0, 0), 0.035, m["grip"], n=10, bevel=0.005, name="grip", low=6))
    p.append(make.cyl((0, -0.05, 0), (0, 0.05, 0), 0.05, m["steel"], n=10, bevel=0, name="pivot", low=6))
    return p, []


def _basis(x, y):
    """A rotation whose local X is `x` and Y is `y` (unit, perpendicular), Z their cross: right-handed, so a box turned by
    it keeps its faces outward."""
    x, y = Vector(x).normalized(), Vector(y).normalized()
    return Matrix((x, y, x.cross(y))).transposed().to_4x4()


def _span(along, out, a, u, o):
    """A box's half-extents in the model's axes, from its half along a wall (`along`), up it and out of it (`out`)."""
    v = Vector(along) * a + Vector((0, 0, u)) + Vector(out) * o
    return (abs(v.x), abs(v.y), abs(v.z))


def _arched(p, m, foot, along, out, w, z0, z1, state="glass", bar=0.016):
    """A tall round-arched window in a brick wall: its glass (or the dark of it broken, or lit), the cast-iron glazing
    bars, a stone sill and the arch's brick voussoirs over it with their keystone (all baked onto the wall). `foot` is
    on the wall's face under its middle; `along` and `out` the wall's run and its outward normal, along the axes; `w`
    its half-width, z0..z1 its straight sides, the arch's crown at z1 + w; `bar` the glazing bars' half-width (thicker on a
    big building, whose texels are coarser)."""
    foot, along, out = Vector(foot), Vector(along), Vector(out)
    up = Vector((0, 0, 1))
    zc = (z0 + z1) / 2
    p.append(make.box(foot + out * 0.01 + up * zc, _span(along, out, w, (z1 - z0) / 2, 0.02), m[state], bevel=0, name="glass", low=False))
    p.append(make.cyl(foot + up * z1 - out * 0.01, foot + up * z1 + out * 0.03, w, m[state], n=16, bevel=0, name="arch_glass", low=0))
    if state != "dark":
        for k in (-1, 1):
            u = k * w / 3
            top = z1 + math.sqrt(w * w - u * u)
            p.append(make.box(foot + along * u + out * 0.045 + up * (z0 + top) / 2, _span(along, out, bar, (top - z0) / 2, 0.012),
                              m["rust"], bevel=0, name="bar", low=False))
        z = z0 + 0.6
        while z < z1 + 0.01:
            p.append(make.box(foot + out * 0.045 + up * z, _span(along, out, w, bar, 0.012), m["rust"], bevel=0, name="bar", low=False))
            z += 0.6
    p.append(make.box(foot + out * 0.09 + up * (z0 - 0.08), _span(along, out, w + 0.16, 0.07, 0.1), m["stone"], bevel=0.01, name="sill", low=False))
    for k in range(9):
        a = math.pi * k / 8
        radial = along * math.cos(a) + up * math.sin(a)
        tangent = up * math.cos(a) - along * math.sin(a)
        key = k == 4
        r = w + (0.2 if key else 0.15)
        p.append(make.box(foot + up * z1 + radial * r + out * 0.04, (0.11 if key else 0.075, 0.22 if key else 0.15, 0.04),
                          m["stone" if key else "brick"], bevel=0.008, name="voussoir", rot=_basis(tangent, radial), low=False))


def _gable(p, m, x0, x1, y, z, apex, outward, material, name="gable"):
    """A gable's triangle across x0..x1 at y, from the eaves at z to the apex, facing `outward` (±1 along Y)."""
    v = [(x0, y, z), (x1, y, z), ((x0 + x1) / 2, y, apex)]
    _prism(p, v, [(0, 1, 2) if outward < 0 else (0, 2, 1)], material, name)


def _pitched(p, m, L, D, ridge, eaves_out, pitch, material, along_x=True, name="roof"):
    """A pitched roof of two flat slopes from a ridge at height `ridge` (along X if `along_x`, else along Y), each running
    `eaves_out` out from the ridge to its eaves, overhanging the ends by 0.5."""
    run = eaves_out / math.cos(pitch)
    for s in (-1, 1):
        drop = eaves_out / 2 * math.tan(pitch)
        if along_x:
            p.append(make.box((0, s * eaves_out / 2, ridge - drop), (L + 0.5, run / 2, 0.08), material, bevel=0.01, name=name,
                              rot=Matrix.Rotation(-s * pitch, 4, "X")))
        else:
            p.append(make.box((s * eaves_out / 2, 0, ridge - drop), (run / 2, D + 0.5, 0.08), material, bevel=0.01, name=name,
                              rot=Matrix.Rotation(s * pitch, 4, "Y")))


# The mine head's winding gear, where StructureKit stands it (MineHead): the headframe 12 m out from the line at 1.5
# times its modelled size, the winding house 38 out, both turned the same way and sunk alike. The drum is 2 m in from
# the house's front wall, its top 5 m up; the ropes off the sheaves land on it (ROPE_END), and come in through the front
# gable at ROPE_Z, where the straight line from the sheaves down to the drum crosses it.
ROPE_Z = 9.75             # over the house's ground
ROPE_END = (16.0, 3.333)  # the drum's top in the headframe's own frame: (36 - 12) / 1.5 out, 5 / 1.5 up


def winding_house(m):
    p = []
    L, D, H, RISE = 7.5, 6.0, 9.5, 4.6  # half along the line (X), half across (Y), the eaves, the gables' rise to the ridge
    APEX = H + RISE
    # The engine house: on its stone plinth, brick, its corners and the bays between its windows in pilasters, a
    # corbelled cornice along the eaves. Its ridge runs away from the line, so its gable faces the headframe.
    p.append(make.box((0, 0, 0.35), (L + 0.12, D + 0.12, 0.65), m["stone"], bevel=0.02, name="plinth"))
    p.append(make.box((0, 0, (1.0 + H) / 2), (L, D, (H - 1.0) / 2), m["brick"], bevel=0.02, name="walls"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.box((sx * L, sy * D, (1.0 + H) / 2), (0.45, 0.45, (H - 1.0) / 2 + 0.05), m["brick"], bevel=0.02, name="corner"))
        for y in (-2.0, 2.0):
            p.append(make.box((sx * (L + 0.1), y, (1.0 + H) / 2), (0.12, 0.32, (H - 1.0) / 2), m["brick"], bevel=0.01, name="pilaster", low=False))
        p.append(make.box((sx * (L + 0.16), 0, H - 0.18), (0.16, D + 0.25, 0.18), m["brick"], bevel=0.01, name="cornice"))
    # The tall arched windows down both its sides, one lit (the engine's lamp: someone still on the brake), two out.
    for sx in (-1, 1):
        for y in (-4.0, 0.0, 4.0):
            state = "lit" if (sx, y) == (-1, 0.0) else "dark" if (sx, y) in ((-1, 4.0), (1, -4.0)) else "glass"
            _arched(p, m, (sx * L, y, 0), (0, 1, 0), (sx, 0, 0), 0.85, 2.6, 7.0, state)
    # The front gable, to the headframe: the engine's double doors, one leaf hanging open; the window over them; the
    # two slots high up the ropes come in by, framed in timber; the date stone in the apex.
    p.append(make.box((0, -D - 0.02, 2.45), (1.5, 0.04, 2.15), m["dark"], bevel=0, name="doorway"))
    p.append(make.box((0, -D - 0.1, 4.75), (1.8, 0.1, 0.16), m["stone"], bevel=0.01, name="lintel"))
    hinge = Vector((-1.5, -D - 0.06, 2.45))
    leaf = Matrix.Rotation(-1.9, 4, "Z")
    p.append(make.box(hinge + leaf.to_3x3() @ Vector((0.75, 0, 0)), (0.75, 0.05, 2.1), m["timber"], bevel=0.01, name="leaf", rot=leaf))
    p.append(make.box((0.75, -D - 0.07, 2.4), (0.75, 0.05, 2.08), m["timber"], bevel=0.01, name="leaf", rot=Matrix.Rotation(0.03, 4, "Y")))
    _arched(p, m, (0, -D, 0), (1, 0, 0), (0, -1, 0), 1.0, 5.6, 7.6, "glass")
    for x in (-0.9, 0.9):
        p.append(make.box((x, -D - 0.02, ROPE_Z), (0.32, 0.04, 0.5), m["dark"], bevel=0, name="rope_slot", low=False))
        for dz in (-0.55, 0.55):
            p.append(make.box((x, -D - 0.06, ROPE_Z + dz), (0.42, 0.05, 0.06), m["timber"], bevel=0.005, name="slot_frame", low=False))
    p.append(make.box((0, -D - 0.05, H + RISE * 0.5), (0.75, 0.05, 0.38), m["stone"], bevel=0.01, name="date_stone", low=False))
    p.append(make.stencil("1889", (0, -D - 0.1, H + RISE * 0.5), (0, -1, 0), (0, 0, 1), 0.34, m["streak"], name="date"))
    # Soot and rust down the brick: from the slots, the window sills, the cornice's ends.
    for x, z, h in ((-0.9, ROPE_Z - 1.6, 1.0), (0.9, ROPE_Z - 1.8, 1.2), (0.3, 4.2, 0.6)):
        p.append(make.box((x, -D - 0.015, z), (0.14, 0.01, h), m["streak"], bevel=0, name="streak", low=False))
    for y in (-4.0, 0.0):
        p.append(make.box((-L - 0.015, y + 0.2, 1.7), (0.01, 0.16, 0.8), m["streak"], bevel=0, name="streak", low=False))
    # The gables and the slate roof, its ridge capped, a louvred ventilator along it for the engine's heat.
    _gable(p, m, -L, L, -D, H, APEX, -1, m["brick"])
    _gable(p, m, -L, L, D, H, APEX, 1, m["brick"])
    pitch = math.atan2(RISE, L)
    _pitched(p, m, L, D, APEX + 0.12, L + 0.4, pitch, m["slate"], along_x=False)
    p.append(make.box((0, 0, APEX + 0.16), (0.22, D + 0.5, 0.08), m["rust"], bevel=0.01, name="ridge"))
    p.append(make.box((0, 0, APEX + 0.75), (0.65, 2.6, 0.55), m["clad"], bevel=0.01, name="ventilator"))
    p.append(make.box((0, 0, APEX + 1.36), (0.95, 2.9, 0.06), m["slate"], bevel=0.01, name="vent_cap"))
    for sx in (-1, 1):
        for k in range(5):
            p.append(make.box((sx * 0.66, -2.0 + k, APEX + 0.75), (0.01, 0.35, 0.36), m["dark"], bevel=0, name="louvre", low=False))
    # The boiler house behind it, against its back gable: lower, brick, its roof a lean-to of corrugated iron, a coal
    # door and a small window each side. Behind that the chimney, the tallest thing on the site after the headframe's
    # sheaves. (Behind, not beside: StructureKit turns the house to face the line from either side of it, and the
    # tip stands off one end of it whichever side that is.)
    bx, by0, by1, bh = 5.5, D, D + 8.0, 5.6
    bcy = (by0 + by1) / 2
    p.append(make.box((0, bcy + 0.05, 0.35), (bx + 0.1, (by1 - by0) / 2 + 0.05, 0.65), m["stone"], bevel=0.02, name="b_plinth"))
    p.append(make.box((0, bcy, (1.0 + bh) / 2), (bx, (by1 - by0) / 2, (bh - 1.0) / 2), m["brick"], bevel=0.02, name="b_walls"))
    lean = math.atan2(1.6, by1 - by0)
    p.append(make.box((0, bcy + 0.2, bh + 0.85), (bx + 0.4, (by1 - by0) / 2 / math.cos(lean) + 0.5, 0.06), m["roof"],
                      bevel=0.01, name="b_roof", rot=Matrix.Rotation(-lean, 4, "X")))
    _prism(p, [(-bx, by0, bh), (-bx, by1, bh), (-bx, by0, bh + 1.6)], [(0, 2, 1)], m["brick"], "b_gable")
    _prism(p, [(bx, by0, bh), (bx, by1, bh), (bx, by0, bh + 1.6)], [(0, 1, 2)], m["brick"], "b_gable")
    for sx in (-1, 1):
        p.append(make.box((sx * (bx + 0.02), by0 + 2.4, 1.55), (0.04, 0.7, 1.1), m["dark"], bevel=0, name="coal_door"))
        p.append(make.box((sx * (bx + 0.08), by0 + 2.4, 2.75), (0.08, 0.9, 0.12), m["rust"], bevel=0.01, name="coal_lintel", low=False))
        _arched(p, m, (sx * bx, by0 + 5.6, 0), (0, 1, 0), (sx, 0, 0), 0.5, 2.2, 3.8, "dark" if sx > 0 else "glass")
    cx, cy = 2.5, by1 + 2.8
    p.append(make.box((cx, cy, 1.6), (1.55, 1.55, 1.9), m["stone"], bevel=0.03, name="stack_base"))
    p.append(make.box((cx, cy, 3.6), (1.7, 1.7, 0.15), m["stone"], bevel=0.02, name="stack_band"))
    p.append(make.cyl((cx, cy, 3.7), (cx, cy, 30.0), 1.3, m["brick"], n=20, bevel=0.01, name="chimney", r1=0.85, low=10))
    p.append(make.cyl((cx, cy, 29.3), (cx, cy, 30.4), 1.06, m["brick"], n=20, bevel=0.01, name="chimney_cap", r1=1.0, low=10))
    for z in (8.0, 14.0, 20.0, 25.5):
        r = 1.3 - (1.3 - 0.85) * (z - 3.7) / (30.0 - 3.7)
        p.append(make.torus((cx, cy, z), (0, 0, 1), r + 0.01, 0.035, m["rust"], n=40, m=4, name="band", low=False))
    p.append(make.cyl((cx, cy, 29.0), (cx, cy, 30.35), 0.87, m["streak"], n=20, bevel=0, name="soot", r1=0.86, low=0))
    p.append(make.cyl((cx, by1 - 0.2, 2.4), (cx, cy - 1.5, 2.4), 0.45, m["rust"], n=12, bevel=0, name="flue", low=6))
    return p, []


def _heap_height(x, y, R, H, fine):
    """The tip's ground: a cone of shale with a slumped top, longer along the line and lobed where the tubs tipped it
    out unevenly; gullies cut down it by the rain and, with `fine`, its shale in lumps."""
    a = math.atan2(y, x)
    reach = R * (1 + 0.1 * math.sin(3 * a + 0.7) + 0.06 * math.sin(5 * a + 2.1) + 0.035 * math.sin(9 * a + 0.3))
    r = math.hypot(x, y / 0.82) / reach
    if r >= 1:
        return -0.5 * min(1.0, (r - 1) * 4)
    h = H * (1 - max(r, 0.14) ** 1.45)
    if fine:
        gully = 0.6 * max(0.0, math.sin(11 * a + 1.3 * math.sin(3 * a))) ** 4 * min(1.0, r * 2.5) * (1.1 - r)
        lumps = 0.12 * math.sin(x * 1.3 + 1.7) * math.sin(y * 1.1 - 0.4) + 0.07 * math.sin(x * 3.1 - y * 2.3) + 0.04 * math.sin(x * 6.7 + y * 5.9)
        h += lumps - gully
    return h


def _heap_shade(x, y, z, R, H):
    """Which of the tip's shales a face of it is: 0 the weathered grey, 1 burnt red in seams down the fall line where
    it fired inside, 2 fresh and black in the fan below the incline's head (its last tubs)."""
    a = math.atan2(y, x)
    r = math.hypot(x, y / 0.82) / R
    if x < 1.5 and r < 0.5 and abs(y) < 5 + 4 * r and z > H * 0.45:
        return 2
    seam = math.sin(a * 17 + 2.0 * math.sin(r * 6)) * math.sin(a * 5 - 1.1) + 0.35 * math.sin(r * 23 + a * 3)
    return 1 if seam > 0.72 and 0.25 < r < 0.95 else 0


def _heap_mesh(p, m, R, H, n, rings, fine, low):
    """The tip as a polar grid out to R * 1.35 (its toe running into the ground): the dense one to bake, or the game's."""
    verts = [(0.0, 0.0, _heap_height(0, 0, R, H, fine))]
    for i in range(1, rings + 1):
        rr = R * 1.35 * (i / rings) ** 0.85
        for k in range(n):
            a = 2 * math.pi * k / n
            x, y = rr * math.cos(a), rr * math.sin(a) * 0.82
            verts.append((x, y, _heap_height(x, y, R, H, fine)))
    faces = []
    for k in range(n):
        faces.append((0, 1 + k, 1 + (k + 1) % n))
    for i in range(1, rings):
        a0, a1 = 1 + (i - 1) * n, 1 + i * n
        for k in range(n):
            k1 = (k + 1) % n
            faces.append((a0 + k, a1 + k, a1 + k1, a0 + k1))
    mesh = bpy.data.meshes.new("heap_low" if low else "heap")
    mesh.from_pydata(verts, [], faces)
    mesh.materials.append(m["shale"])
    if not low:
        mesh.materials.append(m["burnt"])
        mesh.materials.append(m["fresh"])
        for f in mesh.polygons:
            c = f.center
            f.material_index = _heap_shade(c.x, c.y, c.z, R, H)
    for f in mesh.polygons:
        f.use_smooth = True
    o = bpy.data.objects.new(mesh.name, mesh)
    bpy.context.scene.collection.objects.link(o)
    if low:
        make.LOW.append(o)
    else:
        p.append(o)


def spoil_heap(m):
    """The mine head's tip: a cone of shale 12 m high and 32 across at its foot, longer along the line, gullied by the
    rain and red where it burnt; the incline up its +X flank on its timber trestle, a tub tipped at the top, another on
    its side at the foot. Its origin is on the ground at the tip's middle."""
    p = []
    R, H = 16.0, 12.0
    _heap_mesh(p, m, R, H, 180, 64, True, False)
    _heap_mesh(p, m, R, H, 32, 10, False, True)
    # Boulders and slabs of shale at the toe, rolled down.
    for k in range(14):
        a = k * 2.399 + 0.3
        rr = R * (0.92 + 0.12 * math.sin(k * 1.7))
        x, y = rr * math.cos(a), rr * math.sin(a) * 0.82
        if x > 13 and abs(y) < 3:
            continue
        s = 0.18 + 0.12 * ((k * 5) % 3) / 2
        z = _heap_height(x, y, R, H, False) + s * 0.6
        p.append(make.box((x, y, z), (s * 1.3, s, s * 0.7), m["shale"], bevel=0.04, name="boulder", rot=Matrix.Rotation(k * 0.9, 4, "Z") @ Matrix.Rotation(0.3, 4, "X"), low=False))
    # The incline: a timber deck on trestle bents from beyond the toe up to the summit, its rails and sleepers.
    x0, x1, top = 22.0, 2.5, H + 0.4
    slope = math.atan2(top, x0 - x1)

    def deck(x):
        return top * (x0 - x) / (x0 - x1)
    mid = Vector(((x0 + x1) / 2, 0, (deck(x0) + deck(x1)) / 2))
    run = math.hypot(x0 - x1, top)
    tilt = Matrix.Rotation(slope, 4, "Y")
    p.append(make.box(mid, (run / 2, 0.75, 0.08), m["grey"], bevel=0.01, name="deck", rot=tilt))
    for sy in (-0.5, 0.5):
        p.append(make.box(mid + Vector((0, sy, 0.0)) + (tilt.to_3x3() @ Vector((0, 0, 0.13))), (run / 2, 0.035, 0.05), m["rust"], bevel=0.005, name="rail", rot=tilt))
    k = 0
    while k * 0.7 < run:
        c = Vector((x0 - k * 0.7 * math.cos(slope), 0, k * 0.7 * math.sin(slope) + 0.1))
        p.append(make.box(c, (0.09, 0.7, 0.04), m["timber"], bevel=0.005, name="sleeper", rot=tilt, low=False))
        k += 1
    x = x0 - 1.0
    while x > x1 + 3:
        foot = _heap_height(x, 0, R, H, False) - 0.3
        z = deck(x) - 0.1
        if z - foot > 0.4:
            for sy in (-0.65, 0.65):
                p.append(_beam(Vector((x, sy * 1.25, foot)), Vector((x, sy, z)), 0.16, m["grey"], "bent"))
            p.append(_beam(Vector((x, -0.75, z - 0.15)), Vector((x, 0.75, z - 0.15)), 0.14, m["grey"], "cap"))
            if z - foot > 2.2:
                p.append(_beam(Vector((x, -0.9, foot + 0.6)), Vector((x, 0.75, z - 0.4)), 0.1, m["grey"], "brace"))
        x -= 3.0
    # The tub at the top tipped out over the end, a spill of shale under its lip; another fallen at the foot.
    p.append(make.box((x1 - 0.3, 0, top + 0.75), (0.6, 0.45, 0.4), m["rust"], bevel=0.02, name="tub", rot=Matrix.Rotation(0.9, 4, "Y")))
    p.append(make.box((x0 + 1.6, 1.8, 0.4), (0.6, 0.45, 0.4), m["rust"], bevel=0.02, name="tub", rot=Matrix.Rotation(1.45, 4, "X") @ Matrix.Rotation(0.4, 4, "Z")))
    for k, (dx, dy, s) in enumerate(((-0.9, 0.2, 0.25), (-1.3, -0.3, 0.2), (-1.0, 0.5, 0.16))):
        x, y = x1 + dx, dy
        p.append(make.box((x, y, _heap_height(x, y, R, H, False) + s * 0.5), (s * 1.4, s, s * 0.6), m["shale"], bevel=0.03, name="spill",
                          rot=Matrix.Rotation(k * 1.3, 4, "Z"), low=False))
    return p, []


def chem_works(m):
    """The chemical works' process house (GDD §18: "Leaks. Do not fire indoors"): 30 m along the line, 12 deep, a brick
    base and the steel frame above clad in corrugated iron, gone to rust and acid-streaked, sheets fallen off it; a
    louvred monitor along its ridge for the fumes; a band of steel windows, broken; a sliding door, an outside stair
    to the upper floor's door, NO NAKED LIGHTS stencilled by the door. Two tall guyed stacks behind its ridge, the
    lead-clad acid tower at its -X end, and two pipe bridges from its front out over the tanks to the pipe rack (7 m
    off the line: 19 in front of it). Its origin is on the ground at its middle, 26 m off the line."""
    p = []
    L, D, H, BASE = 15.0, 6.0, 11.0, 3.0
    RISE = 2.0
    p.append(make.box((0, 0, BASE / 2 - 0.15), (L, D, BASE / 2 + 0.15), m["brick"], bevel=0.02, name="base"))
    p.append(make.box((0, 0, (BASE + H) / 2), (L - 0.03, D - 0.03, (H - BASE) / 2), m["clad"], bevel=0.01, name="cladding"))
    # The frame: stanchions every 5 m, an eaves beam, a rail at the base's top.
    for sy in (-1, 1):
        for x in range(-15, 16, 5):
            p.append(make.box((x, sy * (D + 0.06), H / 2), (0.16, 0.09, H / 2 + 0.05), m["rust"], bevel=0.005, name="stanchion",
                              low=abs(x) == 15))
        p.append(make.box((0, sy * (D + 0.08), H - 0.15), (L + 0.2, 0.11, 0.17), m["rust"], bevel=0.005, name="eaves_beam"))
        p.append(make.box((0, sy * (D + 0.05), BASE + 0.06), (L, 0.08, 0.08), m["rust"], bevel=0.005, name="base_rail", low=False))
    for sx in (-1, 1):
        for y in (-2.0, 2.0):
            p.append(make.box((sx * (L + 0.06), y, H / 2), (0.09, 0.16, H / 2), m["rust"], bevel=0.005, name="stanchion", low=False))
    # The window band, a steel sash in each bay at 5-8 m: panes in a grid, some out (dark), one bay boarded; none where
    # the upper floor's door is.
    for sy in (-1, 1):
        for b in range(6):
            xc = -12.5 + b * 5
            if (sy, b) == (-1, 0):
                continue
            y = sy * (D + 0.01)
            state = "dark" if (b * 3 + (sy > 0)) % 7 == 2 else "glass"
            p.append(make.box((xc, y, 6.5), (2.1, 0.02, 1.5), m["grey" if (sy, b) == (1, 4) else state], bevel=0, name="sash", low=False))
            if (sy, b) == (1, 4):
                continue
            for u in range(-3, 4):
                p.append(make.box((xc + u * 0.6, sy * (D + 0.035), 6.5), (0.018, 0.012, 1.5), m["rust"], bevel=0, name="glazing", low=False))
            for z in (5.0, 5.75, 6.5, 7.25, 8.0):
                p.append(make.box((xc, sy * (D + 0.035), z), (2.1, 0.012, 0.018), m["rust"], bevel=0, name="glazing", low=False))
            for k in range(3):
                if (b + k * 2 + (sy > 0)) % 4 == 0:
                    u, z = (k - 1) * 1.2 + 0.3, 5.4 + 0.75 * ((b + k) % 4)
                    p.append(make.box((xc + u, sy * (D + 0.04), z), (0.3, 0.01, 0.37), m["dark"], bevel=0, name="pane_out", low=False))
    # Sheets fallen off the cladding (the dark of the frame behind) and one hanging by a corner.
    for x, y, z in ((-9.0, -1, 9.5), (3.5, -1, 9.0), (11.0, 1, 9.6), (-4.0, 1, 9.6)):
        p.append(make.box((x, y * (D + 0.012), z), (0.55, 0.01, 1.2), m["dark"], bevel=0, name="gap", low=False))
    hang = Matrix.Rotation(0.5, 4, "Y")
    p.append(make.box((-9.0 + 0.3, -D - 0.12, 8.7), (0.55, 0.02, 1.2), m["clad"], bevel=0.005, name="sheet", rot=hang))
    # Acid's stains down it: under the windows' sills, the pipe exits, down the brick by the door.
    for k, (x, z, h) in enumerate(((-11.6, 3.9, 1.0), (-6.8, 4.2, 0.8), (-1.5, 3.8, 1.1), (8.0, 8.25, 0.22), (-8.0, 8.25, 0.22), (13.0, 4.0, 0.9))):
        p.append(make.box((x, -D - 0.02, z), (0.18 + 0.06 * (k % 2), 0.01, h), m["acid"], bevel=0, name="acid", low=False))
    p.append(make.box((3.0, -D - 0.015, 1.2), (0.45, 0.01, 0.9), m["acid"], bevel=0, name="acid", low=False))
    # The sliding door in the +X bay, half open on its rail; the stencil by it.
    p.append(make.box((7.5, -D - 0.02, 2.3), (2.0, 0.04, 2.3), m["dark"], bevel=0, name="doorway"))
    p.append(make.box((7.5, -D - 0.16, 4.75), (4.4, 0.06, 0.08), m["steel"], bevel=0.005, name="door_rail"))
    p.append(make.box((10.3, -D - 0.13, 2.35), (1.6, 0.04, 2.3), m["clad"], bevel=0.01, name="door_leaf"))
    p.append(make.box((4.6, -D - 0.03, 1.6), (0.8, 0.01, 0.35), m["paint"], bevel=0, name="sign", low=False))
    p.append(make.stencil("NO NAKED", (4.6, -D - 0.045, 1.72), (0, -1, 0), (0, 0, 1), 0.16, m["dark"], name="stencil"))
    p.append(make.stencil("LIGHTS", (4.6, -D - 0.045, 1.46), (0, -1, 0), (0, 0, 1), 0.16, m["dark"], name="stencil"))
    # The outside stair up the front at the -X end to the upper floor's door: stringers, treads, a rail, the landing.
    s0, s1, sz, sy = -3.0, -9.5, 6.4, -D - 0.95
    for dy in (-0.55, 0.55):
        p.append(_beam(Vector((s0, sy + dy, 0.2)), Vector((s1, sy + dy, sz)), 0.12, m["rust"], "stringer"))
    n = 20
    for k in range(1, n):
        t = k / n
        p.append(make.box((s0 + (s1 - s0) * t, sy, 0.2 + (sz - 0.2) * t), (0.16, 0.52, 0.025), m["steel"], bevel=0.004, name="tread"))
    p.append(make.box((s1 - 1.3, sy, sz), (1.35, 0.85, 0.05), m["steel"], bevel=0.005, name="landing"))
    for x, y in ((s1 - 2.6, sy - 0.8), (s1 - 2.6, sy + 0.8), (s1, sy - 0.8)):
        p.append(make.cyl((x, y, 0), (x, y, sz), 0.06, m["rust"], n=8, bevel=0, name="post", low=4))
    p.append(make.cyl((s0, sy - 0.55, 1.2), (s1, sy - 0.55, sz + 1.0), 0.025, m["rust"], n=6, bevel=0, name="handrail", low=4))
    p.append(make.cyl((s1, sy - 0.8, sz + 1.0), (s1 - 2.6, sy - 0.8, sz + 1.0), 0.025, m["rust"], n=6, bevel=0, name="handrail", low=4))
    p.append(make.box((s1 - 1.4, -D - 0.02, sz + 1.1), (0.55, 0.03, 1.05), m["dark"], bevel=0, name="upper_door"))
    # The roof: shallow, iron, along the line; the gable ends clad; the monitor along its ridge, louvred.
    pitch = math.atan2(RISE, D)
    _pitched(p, m, L, D, H + RISE + 0.1, D + 0.5, pitch, m["roof"])
    for sx in (-1, 1):
        x = sx * (L + 0.0)
        _prism(p, [(x, -D, H), (x, D, H), (x, 0, H + RISE)], [(0, 1, 2) if sx > 0 else (0, 2, 1)], m["clad"], "gable")
    p.append(make.box((0, 0, H + RISE + 0.9), (L - 3.0, 1.4, 0.9), m["clad"], bevel=0.01, name="monitor"))
    p.append(make.box((0, 0, H + RISE + 1.85), (L - 2.8, 1.75, 0.06), m["roof"], bevel=0.01, name="monitor_roof"))
    for sy in (-1, 1):
        for k in range(22):
            p.append(make.box((-L + 3.6 + k * 1.09, sy * 1.41, H + RISE + 0.9), (0.42, 0.01, 0.55), m["dark"], bevel=0, name="louvre", low=False))
    # The stacks: tall, thin, riveted iron, banded, sooted at their lips, each held by three guys.
    for sx in (-1, 1):
        x, y = sx * 8.0, 2.5
        p.append(make.cyl((x, y, H), (x, y, 34.0), 0.78, m["rust"], n=16, bevel=0.005, name="stack", r1=0.6, low=8))
        p.append(make.cyl((x, y, 33.6), (x, y, 34.6), 0.66, m["rust"], n=16, bevel=0.005, name="stack_lip", r1=0.74, low=8))
        p.append(make.cyl((x, y, 33.0), (x, y, 34.62), 0.64, m["streak"], n=16, bevel=0, name="soot", r1=0.75, low=0))
        p.append(make.cyl((x, y, H + RISE * 0.6 - 0.2), (x, y, H + RISE * 0.6 + 0.4), 1.1, m["steel"], n=16, bevel=0.005, name="collar", r1=0.8, low=8))
        for z in range(16, 33, 4):
            r = 0.78 - (0.78 - 0.6) * (z - H) / (34.0 - H)
            p.append(make.torus((x, y, z), (0, 0, 1), r + 0.01, 0.03, m["steel"], n=32, m=4, name="band", low=False))
        for gx, gy, gz in ((x + sx * 7.0, y + 12.0, 0.0), (x - sx * 5.0, y + 13.0, 0.0), (x + sx * 1.0, -D - 0.2, H + 0.1)):
            p.append(make.cyl((x, y, 27.0), (gx, gy, gz), 0.022, m["rust"], n=5, bevel=0, name="guy", low=3))
    # The acid tower at the -X end: a square brick foot, lead-clad above to 21 m, its hood; a pipe down to the works.
    tx = -L - 3.4
    p.append(make.box((tx, 0, 3.0), (2.0, 2.0, 3.0), m["brick"], bevel=0.02, name="tower_foot"))
    p.append(make.box((tx, 0, 13.5), (1.8, 1.8, 7.5), m["lead"], bevel=0.02, name="tower"))
    for z in (8.0, 11.0, 14.0, 17.0, 20.0):
        p.append(make.box((tx, 0, z), (1.84, 1.84, 0.06), m["rust"], bevel=0.005, name="tower_band", low=False))
    p.append(make.box((tx, 0, 21.2), (2.0, 2.0, 0.2), m["lead"], bevel=0.02, name="tower_cap"))
    p.append(make.box((tx, 0, 21.9), (1.1, 1.1, 0.5), m["lead"], bevel=0.02, name="tower_hood"))
    p.append(make.cyl((tx, 0, 22.4), (tx, 0, 24.2), 0.3, m["rust"], n=10, bevel=0, name="vent", low=6))
    p += make.pipe([(tx + 1.8, -1.0, 18.0), (tx + 2.6, -1.0, 18.0), (tx + 2.6, -1.0, 9.5), (-L - 0.05, -1.0, 9.5)], 0.14, m["lead"], name="tower_pipe", n=10, low=6)
    for k in range(-3, 4):
        for sy in (-1, 1):
            p.append(make.box((tx + k * 0.5, sy * 1.81, 13.5), (0.012, 0.012, 7.5), m["steel"], bevel=0, name="seam", low=False))
            p.append(make.box((tx + sy * 1.81, k * 0.5, 13.5), (0.012, 0.012, 7.5), m["steel"], bevel=0, name="seam", low=False))
    for z0, h in ((15.5, 2.6), (10.5, 1.4)):
        p.append(make.box((tx + 0.6, -1.815, z0), (0.4, 0.01, h), m["acid"], bevel=0, name="acid", low=False))
    _ladder_front(p, m, tx - 0.9, -2.15, 6.0, 21.0)
    # The pipe bridges out from the front, over the tanks' gap, onto the rack: each on a mid-span post pair.
    for sx in (-1, 1):
        x = sx * 8.0
        p += make.pipe([(x, -D - 0.05, 8.5), (x, -19.0, 8.5), (x, -19.0, 5.0)], 0.16, m["rust"], name="bridge", n=12, low=6)
        p.append(make.cyl((x, -D - 0.25, 8.5), (x, -D - 0.05, 8.5), 0.26, m["steel"], n=12, bevel=0, name="wall_flange", low=6))
        for dx in (-0.35, 0.35):
            p.append(_beam(Vector((x + dx, -12.5, 0.0)), Vector((x + dx, -12.5, 8.3)), 0.14, m["rust"], "post"))
        p.append(_beam(Vector((x - 0.5, -12.5, 8.27)), Vector((x + 0.5, -12.5, 8.27)), 0.12, m["rust"], "post_cap"))
        p.append(_beam(Vector((x - 0.35, -12.5, 1.0)), Vector((x + 0.35, -12.5, 6.5)), 0.07, m["rust"], "post_brace"))
    return p, []


def pipe_rack(m, end=False):
    """A 12 m bay of the chemical works' pipe rack along the line (origin on the ground under its trestle, +X along
    the track): a steel portal, its knee braces, and the bay's pipes on its beam, flanged where they meet the next bay's;
    a valve with its wheel and the stain of its drip, lagging torn off one pipe. The run's last bay (`end`) turns its
    pipes down into the ground at +X."""
    p = []
    BEAM = 4.55
    for sy in (-0.85, 0.85):
        p.append(_beam(Vector((0, sy, 0.0)), Vector((0, sy, BEAM)), 0.26, m["rust"], "leg"))
        p.append(make.box((0, sy, 0.15), (0.3, 0.3, 0.15), m["concrete"], bevel=0.01, name="pad"))
    p.append(make.box((0, 0, BEAM + 0.08), (0.11, 1.15, 0.1), m["rust"], bevel=0.005, name="beam"))
    for sy in (-1, 1):
        p.append(_beam(Vector((0, sy * 0.85, BEAM - 1.0)), Vector((0, sy * 0.25, BEAM)), 0.08, m["rust"], "knee"))
    p.append(_beam(Vector((0, -0.85, 1.2)), Vector((0, 0.85, 1.2)), 0.1, m["rust"], "tie"))
    runs = ((-0.6, 0.16, "rust"), (-0.18, 0.12, "steel"), (0.2, 0.12, "rust"), (0.55, 0.07, "steel"))
    x_end = 6.0
    for y, r, mat in runs:
        z = BEAM + 0.18 + r
        if end:
            p += make.pipe([(-6.0, y, z), (x_end - 0.6, y, z), (x_end - 0.6, y, 0.1)], r, m[mat], name="pipe", n=12, low=6)
        else:
            p.append(make.cyl((-6.0, y, z), (6.0, y, z), r, m[mat], n=12, bevel=0, name="pipe", low=6))
            p.append(make.cyl((5.86, y, z), (5.98, y, z), r * 1.55, m["steel"], n=12, bevel=0, name="flange", low=6))
        p.append(make.box((0, y, BEAM + 0.18 + 0.02), (0.06, r + 0.03, 0.02), m["steel"], bevel=0, name="saddle", low=False))
    if end:
        p.append(make.box((x_end - 0.6, 0, 0.12), (0.5, 1.2, 0.14), m["concrete"], bevel=0.01, name="pit"))
    # The valve on the big pipe, its wheel, the drip down from it; the lagging torn and hanging off the third.
    vz = BEAM + 0.18 + 0.16
    p.append(make.cyl((-3.0, -0.6, vz - 0.24), (-3.0, -0.6, vz + 0.3), 0.12, m["steel"], n=10, bevel=0, name="valve", low=6))
    p += make.handwheel((-3.0, -0.6, vz + 0.36), (0, 0, 1), 0.2, m["paint"], spokes=4, name="wheel")
    p.append(make.cyl((-3.0, -0.6, vz - 0.27), (-3.0, -0.6, vz - 0.62), 0.035, m["acid"], n=6, bevel=0, name="crust", r1=0.008, low=4))
    p.append(make.box((-3.0, -0.6, 0.31), (0.6, 0.45, 0.012), m["acid"], bevel=0, name="pool", low=True))
    lz = BEAM + 0.18 + 0.12
    p.append(make.cyl((1.0, 0.2, lz), (4.2, 0.2, lz), 0.19, m["sack"], n=12, bevel=0.005, name="lagging", low=6))
    p.append(make.box((4.6, 0.32, lz - 0.6), (0.3, 0.02, 0.55), m["sack"], bevel=0.005, name="lagging_torn", rot=Matrix.Rotation(0.25, 4, "X")))
    return p, []


class _Glow:
    """Where a piece's furnace light shows through it: the glass of the windows it lights, as regions of the high mesh
    (bake_down's masks are per vertex). An arched window is its square part and the half-disc over it, brightest at its
    foot where the furnace is, half that at its crown; a box is a box, fading upward if `fade`."""

    def __init__(self, colour=(1.0, 0.42, 0.12)):
        self.arches, self.boxes, self.colour = [], [], colour

    def arch(self, foot, along, out, w, z0, z1):
        self.arches.append((Vector(foot), Vector(along), Vector(out), w, z0, z1))

    def box(self, centre, half, fade=False):
        self.boxes.append((Vector(centre), Vector(half), fade))

    def __call__(self, co):
        out = np.zeros(len(co), np.float32)
        for foot, along, outn, w, z0, z1 in self.arches:
            d = co - np.array(foot, np.float32)
            u, o, z = d @ np.array(along, np.float32), d @ np.array(outn, np.float32), co[:, 2]
            face = (o > -0.015) & (o < 0.031)
            square = (np.abs(u) <= w + 0.01) & (z >= z0 - 0.01) & (z <= z1)
            dome = (z > z1) & (np.hypot(u, z - z1) <= w + 0.01)
            lit = face & (square | dome)
            out[lit] = 1 - 0.5 * np.clip((z[lit] - z0) / (z1 + w - z0), 0, 1)
        for c, h, fade in self.boxes:
            inside = np.all(np.abs(co - np.array(c, np.float32)) <= np.array(h, np.float32) + 0.01, axis=1)
            up = np.clip((co[inside, 2] - (c.z - h.z)) / (2 * h.z), 0, 1)
            out[inside] = 1 - 0.8 * up if fade else 1
        return out


def foundry_shed(m):
    """The foundry's casting shed (GDD §18: the gantry crane; §30): 80 m along the line and 16 deep, soot-black brick on
    a stone plinth, pilastered every 8 m; its roof ten north-light teeth, slate rising to a glazed face each, panes out
    and the furnace's light in some; tall arched windows down both sides, most of the front's lit orange by a furnace
    nobody tends (the glow mask, `_Glow`); a great doorway at each end of the front, its iron leaf slid half across, for
    the castings out to the crane's yard (one end or the other faces it, as the shed is turned to either side of the
    spur); the cupola furnace up through the roof, its charging door glowing; the 40 m stack behind. Its origin is on the
    ground at its middle, 22 m off the line, where the kit's sheds stood."""
    p = []
    glow = _Glow()
    L, D, H, TOOTH, RISE = 40.0, 8.0, 11.0, 8.0, 4.0
    p.append(make.box((0, 0, 0.3), (L + 0.12, D + 0.12, 0.6), m["stone"], bevel=0.02, name="plinth"))
    p.append(make.box((0, 0, (0.9 + H) / 2), (L, D, (H - 0.9) / 2), m["brick"], bevel=0.02, name="walls"))
    teeth = int(2 * L / TOOTH)
    for sy in (-1, 1):
        for k in range(teeth + 1):
            x = -L + k * TOOTH
            p.append(make.box((x, sy * (D + 0.1), (0.9 + H) / 2), (0.35, 0.14, (H - 0.9) / 2 + 0.02), m["brick"], bevel=0.01,
                              name="pilaster", low=k in (0, teeth)))
        p.append(make.box((0, sy * (D + 0.14), H - 0.2), (L + 0.2, 0.14, 0.2), m["brick"], bevel=0.01, name="cornice"))
    for sx in (-1, 1):
        for y in (-4.0, 0.0, 4.0):
            p.append(make.box((sx * (L + 0.1), y, (0.9 + H) / 2), (0.14, 0.35, (H - 0.9) / 2), m["brick"], bevel=0.01, name="pilaster", low=False))
    # The windows: an arched one in each bay down both sides (none where the great doorways are), most of the front's
    # lit by the furnace, the back's dark, a few out.
    doors = (-21.0, 21.0)
    for sy in (-1, 1):
        for k in range(teeth):
            x = -L + TOOTH * (k + 0.5)
            if sy < 0 and any(abs(x - d) < 4.5 for d in doors):
                continue
            if sy < 0:
                state = "dark" if k in (1, 8) else "glass" if k == 4 else "glow"
            else:
                state = "glow" if k in (3, 6) else "dark" if k % 3 == 1 else "glass"
            foot, along, out = (x, sy * D, 0), (1, 0, 0), (0, sy, 0)
            _arched(p, m, foot, along, out, 1.1, 2.6, 7.4, state, bar=0.05)
            if state == "glow":
                glow.arch(foot, along, out, 1.1, 2.6, 7.4)
    # The great doorways: arched, 5 m across, dark inside but for the furnace's glow low in the dark; the iron leaf slid
    # half across one, the other standing open.
    for i, x in enumerate(doors):
        p.append(make.box((x, -D - 0.02, 3.2), (2.5, 0.04, 3.2), m["dark"], bevel=0, name="doorway"))
        p.append(make.cyl((x, -D - 0.02 + 0.02, 6.4), (x, -D - 0.06, 6.4), 2.5, m["dark"], n=20, bevel=0, name="door_arch", low=0))
        p.append(make.box((x, -D - 0.075, 0.9), (1.8, 0.01, 0.6), m["glow"], bevel=0, name="door_glow", low=False))
        glow.box((x, -D - 0.075, 0.9), (1.8, 0.01, 0.6), fade=True)
        for k in range(11):
            a = math.pi * k / 10
            radial = Vector((math.cos(a), 0, math.sin(a)))
            tangent = Vector((-math.sin(a), 0, math.cos(a)))
            key = k == 5
            p.append(make.box(Vector((x, -D - 0.05, 6.4)) + radial * (2.5 + (0.3 if key else 0.22)), (0.16 if key else 0.11, 0.3 if key else 0.22, 0.05),
                              m["stone" if key else "brick"], bevel=0.008, name="voussoir", rot=_basis(tangent, radial), low=False))
        p.append(make.box((x, -D - 0.2, 6.75), (3.4, 0.06, 0.09), m["steel"], bevel=0.005, name="door_rail"))
        if i == 0:
            p.append(make.box((x + 1.2, -D - 0.16, 3.3), (1.4, 0.05, 3.25), m["rust"], bevel=0.01, name="door_leaf"))
        else:
            p.append(make.box((x + 4.0, -D - 0.16, 3.3), (1.4, 0.05, 3.25), m["rust"], bevel=0.01, name="door_leaf"))
    # The end walls' gables of the saw: the brick up under the first tooth's slope and the last's glazed face.
    for k in range(teeth):
        x0 = -L + k * TOOTH
        for sy in (-1, 1):
            _prism(p, [(x0, sy * D, H), (x0 + TOOTH, sy * D, H), (x0 + TOOTH, sy * D, H + RISE)], [(0, 1, 2) if sy < 0 else (0, 2, 1)], m["brick"], "saw")
    # The roof: each tooth's slate slope rising to the next, its north light dropping back, glazed in iron, panes out;
    # the furnace's glow in some; one tooth's slope fallen in.
    slope = math.atan2(RISE, TOOTH)
    run = math.hypot(TOOTH, RISE)
    for k in range(teeth):
        x0 = -L + k * TOOTH
        centre = Vector((x0 + TOOTH / 2, 0, H + RISE / 2 + 0.09))
        p.append(make.box(centre, (run / 2 + 0.12, D + 0.4, 0.07), m["slate"], bevel=0.01, name="slope", rot=Matrix.Rotation(-slope, 4, "Y")))
        if k == 6:
            p.append(make.box(centre + Vector((0.6, 2.0, 0.08)), (1.8, 2.4, 0.01), m["dark"], bevel=0, name="fallen_in", low=False,
                              rot=Matrix.Rotation(-slope, 4, "Y")))
        fx = x0 + TOOTH
        p.append(make.box((fx, 0, H + RISE / 2), (0.03, D, RISE / 2), m["glass"], bevel=0, name="north_light"))
        for y in [-D + 1.0 * j for j in range(17)]:
            p.append(make.box((fx + 0.075, y, H + RISE / 2), (0.012, 0.03, RISE / 2), m["rust"], bevel=0, name="glazing", low=False))
        for z in (H + 0.05, H + 1.35, H + 2.65, H + RISE - 0.05):
            p.append(make.box((fx + 0.075, 0, z), (0.012, D, 0.03), m["rust"], bevel=0, name="glazing", low=False))
        for j in range(5):
            y = -D + 1.5 + 3.1 * j + (k % 3) * 0.6
            z = H + 0.7 + 1.3 * ((k + j) % 3)
            state = "glow" if (k + j) % 7 == 3 else "dark" if (k * 3 + j) % 4 == 0 else None
            if state:
                p.append(make.box((fx + 0.035, y, z), (0.01, 0.48, 0.6), m[state], bevel=0, name="pane", low=False))
                if state == "glow":
                    glow.box((fx + 0.035, y, z), (0.01, 0.48, 0.6))
    # The cupola furnace up through the roof at the +X end: riveted iron, its spark-arrester hat on struts, a charging
    # stage round it with its rail and ladder, the charging door glowing.
    cx, cy = 28.0, 2.0
    p.append(make.cyl((cx, cy, H), (cx, cy, 22.0), 1.5, m["rust"], n=20, bevel=0.01, name="cupola", low=10))
    for z in (16.0, 18.5, 21.0):
        p.append(make.torus((cx, cy, z), (0, 0, 1), 1.51, 0.03, m["steel"], n=40, m=4, name="cupola_band", low=False))
    p.append(make.cyl((cx, cy, 22.6), (cx, cy, 24.0), 2.0, m["rust"], n=20, bevel=0.01, name="hat", r1=0.35, low=10))
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        p.append(make.cyl((cx + math.cos(a) * 1.3, cy + math.sin(a) * 1.3, 21.9), (cx + math.cos(a) * 1.7, cy + math.sin(a) * 1.7, 22.7),
                          0.05, m["steel"], n=6, bevel=0, name="strut", low=4))
    p.append(make.cyl((cx, cy, 16.9), (cx, cy, 17.1), 3.0, m["steel"], n=20, bevel=0.005, name="stage", low=10))
    for k in range(12):
        a = k * math.pi / 6
        p.append(make.cyl((cx + math.cos(a) * 2.9, cy + math.sin(a) * 2.9, 17.1), (cx + math.cos(a) * 2.9, cy + math.sin(a) * 2.9, 18.1),
                          0.03, m["steel"], n=6, bevel=0, name="stage_post", low=4))
    p.append(make.torus((cx, cy, 18.1), (0, 0, 1), 2.9, 0.03, m["steel"], n=40, m=4, name="stage_rail", low=(16, 3)))
    p.append(make.box((cx, cy - 1.5, 17.8), (0.45, 0.05, 0.45), m["glow"], bevel=0, name="charging_door", low=False))
    glow.box((cx, cy - 1.5, 17.8), (0.45, 0.05, 0.45))
    # The stack behind it: brick on a stone base, banded, corbelled at its lip and sooted.
    sx, sy, top = 14.0, D + 4.5, 40.0
    p.append(make.box((sx, sy, 2.0), (2.1, 2.1, 2.3), m["stone"], bevel=0.03, name="stack_base"))
    p.append(make.cyl((sx, sy, 4.2), (sx, sy, top), 1.9, m["brick"], n=20, bevel=0.01, name="stack", r1=1.2, low=10))
    p.append(make.cyl((sx, sy, top - 0.8), (sx, sy, top + 0.5), 1.4, m["brick"], n=20, bevel=0.01, name="stack_cap", r1=1.35, low=10))
    p.append(make.cyl((sx, sy, top - 0.6), (sx, sy, top + 0.52), 1.22, m["streak"], n=20, bevel=0, name="soot", r1=1.38, low=0))
    for z in (10.0, 17.0, 24.0, 31.0, 36.0):
        r = 1.9 - (1.9 - 1.2) * (z - 4.2) / (top - 4.2)
        p.append(make.torus((sx, sy, z), (0, 0, 1), r + 0.01, 0.04, m["rust"], n=40, m=4, name="band", low=False))
    p.append(make.cyl((sx, D + 0.1, 3.0), (sx, sy - 2.0, 3.0), 0.7, m["brick"], n=12, bevel=0, name="flue", low=6))
    return p, [], glow


# The coaling tower where StructureKit stands it (CoalingTower): 7 m off the line, sunk 0.3 like the others. The sim's
# chute pours from 0.4 m off the track, 8.8 up (GreyboxScene.Chute), so its mouth is 6.6 m in front of the tower's middle
# and 9.1 over its ground.
CHUTE_MOUTH = (0.0, -6.6, 9.1)


def coaling_tower(m):
    """The coaling stage's tower (GDD §18: "gravity chute: fast, deafening, fills whether you're ready or not"): a
    concrete bunker 10 m tall on a braced timber trestle 12 m up, a steel hopper under it and the chute out over the
    track from its gate, hung on chains from a jib, its mouth where the sim's chute pours; an iron roof, a ladder up the
    trestle and on up the bunker, the stop's lamp on its chain from the front girt, coal spilled round the feet. Its
    origin is on the ground at the trestle's middle, 7 m off the line."""
    p = []
    X, Y, TOP = 4.5, 3.5, 12.0           # the posts' feet, half along and half across; the trestle's height
    XT, YT = 4.0, 3.1                    # their tops, battered in

    def post(sx, sy, z):
        t = z / TOP
        return Vector((sx * (X + (XT - X) * t), sy * (Y + (YT - Y) * t), z))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(_beam(post(sx, sy, 0), post(sx, sy, TOP), 0.42, m["grey"], "post"))
            p.append(make.box((sx * X, sy * Y, 0.15), (0.55, 0.55, 0.45), m["concrete"], bevel=0.02, name="footing"))
    levels = (3.2, 6.4, 9.6, TOP - 0.2)
    for z in levels:
        for sx in (-1, 1):
            p.append(_beam(post(sx, -1, z), post(sx, 1, z), 0.24, m["grey"], "girt"))
        for sy in (-1, 1):
            p.append(_beam(post(-1, sy, z), post(1, sy, z), 0.24, m["grey"], "girt"))
    # X-braced between the girts on every face, iron straps and bolts at the joints (baked on).
    for z0, z1 in zip((0.4,) + levels[:-1], levels):
        for sx in (-1, 1):
            p.append(_beam(post(sx, -1, z0), post(sx, 1, z1), 0.16, m["grey"], "brace"))
            p.append(_beam(post(sx, 1, z0), post(sx, -1, z1), 0.16, m["grey"], "brace"))
        for sy in (-1, 1):
            p.append(_beam(post(-1, sy, z0), post(1, sy, z1), 0.16, m["grey"], "brace"))
            p.append(_beam(post(1, sy, z0), post(-1, sy, z1), 0.16, m["grey"], "brace"))
        for sx in (-1, 1):
            for sy in (-1, 1):
                c = post(sx, sy, z1)
                p.append(make.box(c + Vector((0, sy * 0.22, 0)), (0.24, 0.015, 0.14), m["rust"], bevel=0.005, name="strap", low=False))
    # The cap: two great beams across under the bunker.
    for sx in (-1, 1):
        p.append(make.box((sx * 2.6, 0, TOP + 0.1), (0.3, YT + 0.7, 0.3), m["grey"], bevel=0.01, name="cap"))
    # The bunker: board-marked concrete, its pours banded, stained down from its seams and black round its foot; COAL
    # painted across its front long ago; an iron roof.
    B0, B1, BX, BY = TOP + 0.4, 22.0, 5.5, 4.0
    p.append(make.box((0, 0, (B0 + B1) / 2), (BX, BY, (B1 - B0) / 2), m["concrete"], bevel=0.03, name="bunker"))
    for z in (B0 + 1.4 * k for k in range(1, 7)):
        for sy in (-1, 1):
            p.append(make.box((0, sy * (BY + 0.012), z), (BX, 0.01, 0.025), m["streak"], bevel=0, name="pour", low=False))
        for sx in (-1, 1):
            p.append(make.box((sx * (BX + 0.012), 0, z), (0.01, BY, 0.025), m["streak"], bevel=0, name="pour", low=False))
    for k, x in enumerate((-4.6, -2.9, -0.7, 1.8, 3.9)):
        h = 1.6 + 0.9 * (k % 3)
        for sy in (-1, 1):
            p.append(make.box((x + 0.3 * sy, sy * (BY + 0.015), B1 - h / 2 - 0.1), (0.12 + 0.05 * (k % 2), 0.01, h / 2), m["streak"], bevel=0, name="streak", low=False))
    p.append(make.box((0, -BY - 0.015, B0 + 0.6), (BX, 0.01, 0.6), m["dark"], bevel=0, name="coal_black", low=False))
    p.append(make.box((0, -BY - 0.02, B0 + 5.4), (3.0, 0.01, 0.85), m["paint"], bevel=0, name="sign", low=False))
    p.append(make.stencil("COAL", (0, -BY - 0.035, B0 + 5.4), (0, -1, 0), (0, 0, 1), 1.3, m["dark"], name="coal"))
    pitch = math.atan2(0.9, BY + 0.4)
    _pitched(p, m, BX, BY, B1 + 0.95, BY + 0.4, pitch, m["roof"])
    for sx in (-1, 1):
        _prism(p, [(sx * BX, -BY, B1), (sx * BX, BY, B1), (sx * BX, 0, B1 + 0.9)], [(0, 1, 2) if sx > 0 else (0, 2, 1)], m["concrete"], "bunker_gable")
    # The hopper: steel plate from the bunker's floor down to its gate on the line side, and the chute from the gate
    # out over the track, a trough falling to its mouth with the lip turned down.
    gx, gy, gz = 0.0, -1.5, 10.7
    top = [(-3.4, -3.0), (3.4, -3.0), (3.4, 3.0), (-3.4, 3.0)]
    bot = [(-0.7, gy - 0.6), (0.7, gy - 0.6), (0.7, gy + 0.6), (-0.7, gy + 0.6)]
    verts = [(x, y, B0) for x, y in top] + [(x, y, gz) for x, y in bot]
    faces = [(0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0), (4, 7, 6, 5)]
    _prism(p, verts, faces, m["rust"], "hopper")
    p.append(make.box((gx, gy, gz - 0.2), (0.8, 0.7, 0.2), m["steel"], bevel=0.01, name="gate"))
    mx, my, mz = CHUTE_MOUTH
    a = Vector((gx, gy - 0.4, gz - 0.25))
    b = Vector((mx, my - 0.25, mz + 0.2))
    d = b - a
    tilt = Matrix.Rotation(math.atan2(-d.z, -d.y), 4, "X")
    mid = (a + b) / 2
    run = d.length
    p.append(make.box(mid, (0.55, run / 2, 0.03), m["steel"], bevel=0.005, name="chute_floor", rot=tilt))
    for sx in (-1, 1):
        p.append(make.box(mid + Vector((sx * 0.55, 0, 0.2)), (0.03, run / 2, 0.24), m["rust"], bevel=0.005, name="chute_side", rot=tilt))
    p.append(make.box((mx, my - 0.05, mz - 0.05), (0.55, 0.03, 0.35), m["steel"], bevel=0.005, name="lip", rot=Matrix.Rotation(-0.5, 4, "X")))
    p.append(make.box(mid + Vector((0, 0, 0.04)), (0.45, run / 2 - 0.2, 0.01), m["dark"], bevel=0, name="coal_worn", low=False, rot=tilt))
    # Its jib: two beams out from the hopper's front over the track, hung from the bunker's wall by tension rods, a
    # sheave across their ends and the chains down to the chute's mouth.
    jz = TOP + 0.15
    for sx in (-1, 1):
        p.append(_beam(Vector((sx * 0.6, -3.0, jz)), Vector((sx * 0.6, my - 0.3, jz)), 0.22, m["rust"], "jib"))
        p.append(make.cyl((sx * 0.6, -BY - 0.05, 17.5), (sx * 0.6, my - 0.2, jz + 0.12), 0.03, m["steel"], n=6, bevel=0, name="tie", low=4))
        p.append(make.box((sx * 0.6, -BY - 0.06, 17.5), (0.12, 0.04, 0.12), m["steel"], bevel=0.005, name="tie_plate", low=False))
        p.append(make.cyl((sx * 0.45, my, jz - 0.05), (sx * 0.45, my, mz + 0.25), 0.02, m["rust"], n=5, bevel=0, name="chain", low=3))
    p.append(make.cyl((-0.7, my - 0.3, jz + 0.3), (0.7, my - 0.3, jz + 0.3), 0.25, m["rust"], n=14, bevel=0, name="sheave", low=8))
    # Ladders: up the back of the trestle to the cap, on up the bunker to its roof.
    _ladder(p, m, 2.0, post(1, 1, 6).y + 0.32, 0.3, TOP + 0.4)
    _ladder(p, m, 2.0, BY + 0.3, TOP + 0.4, B1 + 0.6)
    # The stop's lamp hangs where GreyboxScene lights it, 4 m off the line 5 m up: on a chain from the front girt.
    p.append(make.cyl((0, post(0, -1, 6.4).y, 6.3), (0, -3.0, 5.35), 0.015, m["rust"], n=5, bevel=0, name="lamp_chain", low=3))
    p.append(make.box((0, -3.0, 5.25), (0.12, 0.12, 0.12), m["rust"], bevel=0.01, name="lamp_cage"))
    # Coal spilled round the feet: a low heap, lumps.
    p.append(make.cyl((2.6, 1.6, 0.0), (2.6, 1.6, 0.9), 2.6, m["coal"], n=16, bevel=0.02, name="spill", r1=0.4, low=8))
    for k in range(9):
        ang = k * 2.4
        x, y = 2.6 + math.cos(ang) * (2.7 + 0.4 * (k % 3)), 1.6 + math.sin(ang) * (2.6 + 0.3 * (k % 2))
        p.append(make.box((x, y, 0.12), (0.16, 0.12, 0.1), m["coal"], bevel=0.03, name="lump", rot=Matrix.Rotation(k * 0.7, 4, "Z"), low=False))
    return p, []


def dead_boxcar(m):
    """An old wooden boxcar dead in the wreck yard, from before the train's time: off its trucks on its sills, its boards
    gapped, charred and gone in places, a door gone and the other hanging, the roof fallen in at one end, faded marks.
    Its origin is on the ground at its middle, +X along it."""
    p = []
    L, W, F, H = 6.2, 1.4, 0.55, 3.55
    p.append(make.box((0, 0, 0.22), (L - 0.4, 0.22, 0.22), m["rust"], bevel=0.01, name="centre_sill"))
    for sy in (-1, 1):
        p.append(make.box((0, sy * (W - 0.1), F - 0.12), (L, 0.1, 0.12), m["rust"], bevel=0.01, name="side_sill"))
    p.append(make.box((0, 0, F - 0.03), (L, W, 0.03), m["timber"], bevel=0.005, name="floor"))
    # The sides and ends: shells, their boards baked on, gapped and wobbling; holes where boards are gone, char.
    for sy in (-1, 1):
        p.append(make.box((0, sy * W, (F + H) / 2), (L, 0.04, (H - F) / 2), m["timber"], bevel=0.005, name="side"))
        y0, y1 = sorted((sy * (W + 0.04), sy * (W + 0.075)))
        p += make.planks((-L, y0, F), (L, y1, H), 0, 30, m["timber"], gap=0.018, wobble=0.004, seed=3 + sy)
        for x0 in (-L + 0.3, -1.6, 1.6, L - 0.3):
            p.append(make.box((x0, sy * (W + 0.09), (F + H) / 2), (0.07, 0.02, (H - F) / 2), m["timber"], bevel=0.005, name="post", low=False))
        for xa, xb in ((-L + 0.3, -1.6), (1.6, L - 0.3)):
            mid = Vector(((xa + xb) / 2, sy * (W + 0.095), (F + H) / 2))
            d = Vector((xb - xa, 0, H - F))
            rot = Matrix.Rotation(-math.atan2(d.z, d.x) * (1 if sy < 0 else 1), 4, "Y")
            p.append(make.box(mid, (d.length / 2, 0.015, 0.06), m["timber"], bevel=0.004, name="brace", rot=rot, low=False))
        for x, z, h in ((-3.9, 2.4, 0.5), (4.3, 1.6, 0.7), (2.6, 2.9, 0.35)):
            p.append(make.box((x * sy, sy * (W + 0.1), z), (0.35, 0.01, h), m["dark"], bevel=0, name="boards_gone", low=False))
        p.append(make.box((4.6, sy * (W + 0.1), 2.4), (1.4, 0.01, 1.1), m["streak"], bevel=0, name="char", low=False))
    for sx in (-1, 1):
        p.append(make.box((sx * L, 0, (F + H) / 2), (0.04, W + 0.04, (H - F) / 2), m["timber"], bevel=0.005, name="end"))
        x0, x1 = sorted((sx * (L + 0.04), sx * (L + 0.075)))
        p += make.planks((x0, -W, F), (x1, W, H), 1, 8, m["timber"], gap=0.018, wobble=0.004, seed=9 + sx)
    # The doors: one side's gone (the dark of the car), the other's hanging off its rail.
    p.append(make.box((0, -W - 0.11, (F + 3.1) / 2), (0.95, 0.01, (3.1 - F) / 2), m["dark"], bevel=0, name="doorway", low=False))
    p.append(make.box((0.3, W + 0.16, 1.9), (0.95, 0.05, 1.25), m["timber"], bevel=0.01, name="door", rot=Matrix.Rotation(0.18, 4, "X")))
    for sy in (-1, 1):
        p.append(make.box((0, sy * (W + 0.12), 3.2), (2.4, 0.03, 0.04), m["rust"], bevel=0.004, name="door_rail", low=False))
    # The roof: whole over the -X end, fallen in at the other, its boards split; the running board along what's left.
    p.append(make.box((-L / 2 + 0.3, 0, H + 0.06), (L / 2 + 0.35, W + 0.12, 0.05), m["timber"], bevel=0.005, name="roof"))
    p.append(make.box((-L / 2 + 0.3, 0, H + 0.14), (L / 2 + 0.3, 0.25, 0.03), m["grey"], bevel=0.005, name="running_board"))
    drop = math.atan2(1.7, L - 1.4)
    p.append(make.box(((0.7 + L - 0.6) / 2, 0, H - 0.85), ((L - 1.3) / 2 / math.cos(drop), W - 0.15, 0.05), m["timber"], bevel=0.005,
                      name="roof_fallen", rot=Matrix.Rotation(drop, 4, "Y")))
    p.append(make.box((L - 0.3, 0, H - 0.3), (0.2, W - 0.1, 0.25), m["streak"], bevel=0, name="char_end", low=False))
    # The end's brake wheel and grab irons; faded marks.
    p.append(make.cyl((-L - 0.12, 0.6, H - 0.3), (-L - 0.12, 0.6, 0.9), 0.03, m["rust"], n=6, bevel=0, name="brake_staff", low=4))
    p += make.handwheel((-L - 0.12, 0.6, H - 0.25), (0, 0, 1), 0.3, m["rust"], spokes=4, name="brake_wheel")
    for k in range(5):
        z = F + 0.5 + k * 0.42
        p.append(make.cyl((-L - 0.1, -W + 0.15, z), (-L - 0.1, -W + 0.55, z), 0.015, m["rust"], n=5, bevel=0, name="grab", low=3))
    p.append(make.stencil("D T R  3127", (-2.6, -W - 0.11, 2.5), (0, -1, 0), (0, 0, 1), 0.24, m["chalk"], name="marks"))
    return p, []


def dead_gondola(m):
    """An old steel gondola dead in the wreck yard: off its trucks, its sides of riveted plate gone to rust and holed,
    one stove in amidships, scrap and ballast heaped in it. Its origin is on the ground at its middle, +X along it."""
    p = []
    L, W, F, H = 6.0, 1.45, 0.6, 1.85
    p.append(make.box((0, 0, 0.24), (L - 0.4, 0.24, 0.24), m["rust"], bevel=0.01, name="centre_sill"))
    p.append(make.box((0, 0, F - 0.03), (L, W, 0.04), m["rust"], bevel=0.005, name="floor"))
    for sx in (-1, 1):
        p.append(make.box((sx * L, 0, (F + H) / 2), (0.03, W, (H - F) / 2), m["rust"], bevel=0.005, name="end"))
    # Its sides: the -Y one whole, the +Y one stove in amidships, its plate bent in; stakes outside, rivets in rows.
    p.append(make.box((0, -W, (F + H) / 2), (L, 0.03, (H - F) / 2), m["rust"], bevel=0.005, name="side"))
    for xa, xb in ((-L, -1.8), (1.8, L)):
        p.append(make.box(((xa + xb) / 2, W, (F + H) / 2), ((xb - xa) / 2, 0.03, (H - F) / 2), m["rust"], bevel=0.005, name="side"))
    p.append(make.box((0, W - 0.3, (F + H) / 2 - 0.05), (1.85, 0.03, (H - F) / 2), m["rust"], bevel=0.005, name="stove_in",
                      rot=Matrix.Rotation(-0.42, 4, "X")))
    for sy in (-1, 1):
        for k in range(13):
            x = -L + 0.5 + k * ((2 * L - 1.0) / 12)
            if sy > 0 and abs(x) < 1.9:
                continue
            p.append(make.box((x, sy * (W + 0.05), (F + H) / 2), (0.05, 0.03, (H - F) / 2 + 0.02), m["rust"], bevel=0.004, name="stake", low=False))
        for z in (F + 0.1, H - 0.08):
            p.append(make.box((0, sy * (W + 0.035), z), (L, 0.006, 0.012), m["steel"], bevel=0, name="rivets", low=False))
        for x, z in ((-3.4, 1.0), (3.9, 1.3), (-0.9 * sy, 1.5)):
            p.append(make.box((x, sy * (W + 0.04), z), (0.22, 0.01, 0.16), m["dark"], bevel=0, name="rust_hole", low=False))
    # What's in it: ballast and scrap heaped, a wheel off something, a sheet of iron leaning.
    p.append(make.cyl((-1.5, 0, F), (-1.5, 0, F + 0.9), 1.3, m["shale"], n=12, bevel=0.02, name="scrap_heap", r1=0.35, low=8))
    p.append(make.cyl((2.4, -0.3, F + 0.45), (2.4, 0.0, F + 0.45), 0.42, m["rust"], n=16, bevel=0.01, name="loose_wheel", low=8))
    p.append(make.box((3.6, 0.4, F + 0.6), (0.9, 0.02, 0.55), m["rust"], bevel=0.005, name="sheet", rot=Matrix.Rotation(0.6, 4, "X")))
    return p, []


def loose_truck(m):
    """A freight truck off its car: two wheelsets in arch-bar side frames, the bolster across, its springs, all rusted
    and seized. Its origin is on the ground under its middle, +X along the track it ran on."""
    p = []
    R, G = 0.42, 0.75                 # the wheels' radius, half the gauge to their treads
    for x in (-0.85, 0.85):
        p.append(make.cyl((x, -G - 0.1, R), (x, G + 0.1, R), 0.07, m["rust"], n=10, bevel=0, name="axle", low=6))
        for sy in (-1, 1):
            y = sy * G
            p.append(make.cyl((x, y - 0.06, R), (x, y + 0.06, R), R, m["rust"], n=24, bevel=0.01, name="wheel", low=12))
            p.append(make.cyl((x, y - sy * 0.07, R), (x, y - sy * 0.09, R), R + 0.04, m["steel"], n=24, bevel=0, name="flange", low=12))
            p.append(make.box((x, sy * (G + 0.2), R), (0.16, 0.1, 0.13), m["rust"], bevel=0.01, name="journal"))
    for sy in (-1, 1):
        y = sy * (G + 0.2)
        p.append(make.box((0, y, R - 0.18), (1.2, 0.05, 0.04), m["rust"], bevel=0.005, name="bottom_bar"))
        p.append(make.box((0, y, R + 0.18), (1.15, 0.05, 0.04), m["rust"], bevel=0.005, name="top_bar"))
        for sx in (-1, 1):
            a, b = Vector((sx * 0.6, y, R + 0.18)), Vector((sx * 0.1, y, R + 0.42))
            p.append(_beam(a, b, 0.09, m["rust"], "arch"))
        p.append(make.box((0, y, R + 0.42), (0.12, 0.05, 0.04), m["rust"], bevel=0.005, name="arch_top"))
        for sx in (-1, 1):
            p.append(make.cyl((sx * 0.12, y, R - 0.05), (sx * 0.12, y, R + 0.22), 0.07, m["steel"], n=8, bevel=0, name="spring", low=6))
    p.append(make.box((0, 0, R + 0.3), (0.16, G + 0.35, 0.1), m["rust"], bevel=0.01, name="bolster"))
    p.append(make.cyl((0, 0, R + 0.4), (0, 0, R + 0.46), 0.22, m["steel"], n=12, bevel=0, name="centre_plate", low=8))
    return p, []


def yard_shed(m):
    """The wreck yard's engine shed: two roads under corrugated iron on a steel frame, 50 m along the line and 14 deep,
    rusted through: sheets gone from its walls (its frame showing) and roof, one bay's roof fallen in, its +X end open
    with the doors off and the rails running in, smoke vents along its ridge. Its origin is on the ground at its middle,
    34 m off the line."""
    p = []
    L, D, H, RISE = 25.0, 7.0, 7.5, 2.4
    p.append(make.box((0, 0, 0.3), (L + 0.1, D + 0.1, 0.6), m["concrete"], bevel=0.02, name="footing"))
    for sy in (-1, 1):
        p.append(make.box((0, sy * D, (0.9 + H) / 2), (L, 0.04, (H - 0.9) / 2), m["clad"], bevel=0.005, name="wall"))
        for k in range(11):
            x = -L + k * 5
            p.append(make.box((x, sy * (D + 0.08), H / 2), (0.12, 0.06, H / 2), m["rust"], bevel=0.005, name="stanchion", low=False))
        # Sheets gone: the dark between the frame's girts; rust run down from the eaves.
        for x, z in ((-17.5, 4.6), (-6.5, 3.2), (3.8, 5.4), (13.0, 3.6), (19.5, 5.0)):
            xx = x * sy
            p.append(make.box((xx, sy * (D + 0.05), z), (0.55, 0.01, 1.25), m["dark"], bevel=0, name="sheet_gone", low=False))
            p.append(make.box((xx, sy * (D + 0.07), z), (0.62, 0.012, 0.05), m["rust"], bevel=0, name="girt", low=False))
        for k in range(14):
            x = -L + 1.7 + k * 3.6
            p.append(make.box((x, sy * (D + 0.05), H - 1.0 - 0.3 * (k % 3)), (0.18 + 0.06 * (k % 2), 0.01, 0.9 + 0.3 * (k % 3)), m["streak"], bevel=0,
                              name="rust_run", low=False))
    # The -X end closed, a small door; the +X end open on its two roads, the doors off, one lying on the ground.
    _prism(p, [(-L, -D, H), (-L, D, H), (-L, 0, H + RISE)], [(0, 2, 1)], m["clad"], "gable")
    p.append(make.box((-L, 0, (0.9 + H) / 2), (0.04, D, (H - 0.9) / 2), m["clad"], bevel=0.005, name="end"))
    p.append(make.box((-L - 0.05, 3.5, 1.6), (0.01, 0.55, 1.05), m["dark"], bevel=0, name="small_door", low=False))
    _prism(p, [(L, -D, H), (L, D, H), (L, 0, H + RISE)], [(0, 1, 2)], m["clad"], "gable")
    for y in (-3.6, 3.6):
        p.append(make.box((L - 0.02, y, 3.0), (0.04, 2.6, 3.0), m["dark"], bevel=0, name="road_door"))
    p.append(make.box((L, 0, 3.2), (0.06, 0.4, 3.2), m["clad"], bevel=0.005, name="mullion"))
    p.append(make.box((L - 0.05, 0, 6.35), (0.06, D, 0.25), m["rust"], bevel=0.005, name="lintel"))
    p.append(make.box((L + 3.4, -4.5, 0.66), (2.6, 1.3, 0.04), m["clad"], bevel=0.005, name="door_down", rot=Matrix.Rotation(0.12, 4, "X")))
    for y in (-3.6, 3.6):
        for g in (-0.72, 0.72):
            p.append(make.box((L + 2.5, y + g, 0.68), (3.0, 0.035, 0.07), m["steel"], bevel=0.005, name="rail"))
    # The roof: corrugated, in bays, one fallen in; vents along the ridge.
    pitch = math.atan2(RISE, D + 0.4)
    run = (D + 0.4) / math.cos(pitch)
    for b in range(10):
        x = -L + 2.5 + b * 5
        for sy in (-1, 1):
            if b == 6 and sy > 0:
                p.append(make.box((x, (D + 0.4) / 2, H + RISE / 2 - 1.1), (2.45, run / 2, 0.04), m["roof"], bevel=0.005, name="roof_fallen",
                                  rot=Matrix.Rotation(-0.25, 4, "X")))
                continue
            p.append(make.box((x, sy * (D + 0.4) / 2, H + RISE / 2 + 0.05), (2.5, run / 2, 0.04), m["roof"], bevel=0.005, name="roof",
                              rot=Matrix.Rotation(-sy * pitch, 4, "X")))
            if (b * 3 + (sy > 0)) % 4 == 1:
                p.append(make.box((x + 0.6, sy * (D + 0.4) / 2, H + RISE / 2 + 0.1), (0.7, 1.0, 0.01), m["dark"], bevel=0, name="roof_hole",
                                  low=False, rot=Matrix.Rotation(-sy * pitch, 4, "X")))
    for x in (-15.0, 0.0, 15.0):
        p.append(make.box((x, 0, H + RISE + 0.4), (1.6, 0.6, 0.4), m["clad"], bevel=0.01, name="vent"))
        p.append(make.box((x, 0, H + RISE + 0.86), (1.9, 0.9, 0.05), m["roof"], bevel=0.005, name="vent_cap"))
        for sy in (-1, 1):
            p.append(make.box((x, sy * 0.61, H + RISE + 0.4), (1.5, 0.01, 0.3), m["dark"], bevel=0, name="louvre", low=False))
    return p, []


PIECES = {
    "headframe": lambda: build("headframe", headframe, "the mine head's headframe", budget=3500),
    "chem_tank": lambda: build("chem_tank", chem_tank, "a chemical works' storage tank", budget=2500),
    "watchtower": lambda: build("watchtower", watchtower, "the military depot's watchtower", budget=2500),
    "sandbags": lambda: build("sandbags", sandbags, "a length of sandbag wall", size=512, budget=1500),
    "cattle_pen": lambda: build("cattle_pen", cattle_pen, "a panel of cattle pen", size=512, budget=400),
    "cattle_ramp": lambda: build("cattle_ramp", cattle_ramp, "the cattle loading ramp", size=512, budget=800),
    "signal_box": lambda: build("signal_box", signal_box, "the switchyard's signal box", budget=1500),
    "water_tower": lambda: build("water_tower", water_tower, "a water tower", budget=2000),
    # (Its layer at 1024, a hero's: some 4500 m² of concrete and iron would get 13 cm a texel at the props' 512.)
    "grain_elevator": lambda: build("grain_elevator", grain_elevator, "the grain elevator", size=2048, budget=3000, layer=1024),
    "spout_bin": lambda: build("spout_bin", spout_bin, "the grain elevator's loading bin and spout", budget=2500),
    "hose_stand": lambda: build("hose_stand", hose_stand, "the chemical works' hose stand", size=512, budget=1200),
    "lift_works": lambda: build("lift_works", lift_works, "the steam lift's ore bin, trough and guides", budget=2500),
    "ore_skip": lambda: build("ore_skip", ore_skip, "the steam lift's skip", size=512, budget=300),
    "lever_handle": lambda: build("lever_handle", lever_handle, "a set piece's hand lever", size=256, budget=120),
    # (Its layer at 1024 too: some 2,000 m² of brick, slate and iron.)
    "slaughterhouse": lambda: build("slaughterhouse", slaughterhouse, "the slaughterhouse's killing hall", size=2048, budget=3000, layer=1024),
    # (Note 410's: the winding house and the process house a hero's layer, as the elevator's and the hall's are.)
    "winding_house": lambda: build("winding_house", winding_house, "the mine head's winding house, its boiler house and chimney", size=2048,
                                   budget=3000, layer=1024),
    # (Its game mesh the tip's coarse shape: the bake's rays start half a metre out over its lumps and reach 1.2 m in for
    # its gullies.)
    "spoil_heap": lambda: build("spoil_heap", spoil_heap, "the mine head's spoil tip and its incline", budget=1500, reach=0.02, cage=0.008),
    "chem_works": lambda: build("chem_works", chem_works, "the chemical works' process house, its stacks and acid tower", size=2048,
                                budget=3500, layer=1024),
    "pipe_rack": lambda: build("pipe_rack", pipe_rack, "a bay of the chemical works' pipe rack", size=512, budget=1200),
    "pipe_rack_end": lambda: build("pipe_rack_end", lambda m: pipe_rack(m, end=True), "the pipe rack's last bay, its pipes turned down",
                                   size=512, budget=1200),
    # (Note 420's: some 5,500 m² of brick, slate and glass at a hero's layer, like the elevator's; its windows lit.)
    "foundry_shed": lambda: build("foundry_shed", foundry_shed, "the foundry's casting shed, its cupola and stack", size=2048, budget=4500,
                                  layer=1024),
    "coaling_tower": lambda: build("coaling_tower", coaling_tower, "the coaling stage's tower, its bunker and chute", budget=2500),
    "dead_boxcar": lambda: build("dead_boxcar", dead_boxcar, "an old wooden boxcar dead in the wreck yard", budget=1500),
    "dead_gondola": lambda: build("dead_gondola", dead_gondola, "an old steel gondola dead in the wreck yard", budget=1200),
    "loose_truck": lambda: build("loose_truck", loose_truck, "a freight truck off its car", size=512, budget=900),
    "yard_shed": lambda: build("yard_shed", yard_shed, "the wreck yard's engine shed, rusted through", size=2048, budget=2500, layer=1024),
}
# tools/models/build.sh builds them all; `blender -b --python facility_pieces.py -- grain_elevator` just the one.
want = set(cook.args()) or set(PIECES)
for name, fn in PIECES.items():
    if name in want:
        fn()
