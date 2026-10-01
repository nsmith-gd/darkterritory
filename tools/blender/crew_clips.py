"""CREW CLIPS: the crew's actions (GDD §31: "heavy, grounded, readable, a bit stiff, practical, hurried under stress"),
authored on crew.py's skeleton apart from its mesh, so a new clip doesn't mean re-baking the crew (tools/models/recipes/
crew.py's high copy and atlas). CreatureArt merges content/art/models/crew_clips.glb into crew.glb on load, bone by
bone by name (ModelLoader.WithClips: `<name>_clips.glb` beside `<name>.glb`; ARCHITECTURE §8 note 145).

What each is for (CrewActs, from the sim's state; GDD/spec where the act is):
  carry, carry_walk   a crate or a heavy crate's end held in front (spec D.2, B.2's 2.8 m/s)
  drag                a body by its collar, walking backwards (GDD App. D.10)
  door                a hand on a door's handle, hauled across (the interiors, T99)
  handbrake           both hands on a brake wheel, hand over hand (spec C.4)
  hatch               a roof hatch heaved up from the feet (T99)
  uncouple            crouched on the plate, tugging the pin (T91)
  vent                both hands on the blow-off's wheel overhead, straining (T97)
  lever               both hands on a pair of levers at the waist (the sandbox, the crane, T48)
  push                leant into the gun, walking it along its rail (T93)
  held                struggling against what's holding you (App. A.1 GRAB)
  gunner              sat on the cannon's seat, the tiller under the left hand, the handwheel under the right (note 137)
  fall                in the air: arms up, legs gathered
  swing               an overhead blow with whatever's in the right hand (App. C.2)
  mend                down at the firebox with the wrench, ratcheting (T109's engineering kit)
  gap                 on the coupling plate between cars, feet wide, arms out for balance (GDD §32)
  extinguish          the extinguisher on the hip, its nozzle aimed at the fire's foot (App. C.5)
  lantern, _walk      the hand lamp held out low, swinging with the step
  haul                down on a knee, both hands on a friend's collar, hauling them free (App. A.1's rescue)
  fp_hold, fp_walk    first person (X3): the tool held up in view, the eye at EYE (CreatureArt.OwnArms puts it at the camera)
  fp_swing            first person: the blow, as long as the melee's recovery (enemies.json melee.swingSeconds, 0.8 s)
In place, 30 fps, like crew.py's; the root never travels (the sim moves the crewmate).

    blender -b --python tools/blender/crew_clips.py -- content/art/models/crew_clips.glb
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, over, swap  # noqa: E402

rig.reset()
sk = rig.human(1.8)
sk.build()
kit = rig.Kit(sk, "crew_clips")
# A stand-in so the file has a skin to name the joints (the exporter writes skins for skinned meshes only); never drawn,
# the clips are taken off it and the file dropped.
standin = kit.part("standin")
standin.box((0, 0, 1.0), (0.02, 0.02, 0.02), Mat("standin", hexc("#808080")), "pelvis")

# crew.py's standing pose and gait keys (kept in step with it: the same bones, the same angles).
STAND = mirror({
    "spine_01": (-2, 0, 0), "spine_02": (-3, 0, 0), "spine_03": (-5, 0, 0), "neck": (8, 0, 0), "head": (-3, 0, 0),
    "clavicle_r": (0, 9, 4), "upperarm_r": (6, 72, 4), "lowerarm_r": (0, 0, 16), "hand_r": (0, 6, 0),
    "fingers_r": (0, 35, 0), "thumb_r": (0, 15, 0),
    "thigh_r": (1, 0, 0), "calf_r": (-2, 0, 0), "foot_r": (1, 0, -6),
})
LEGS = ("pelvis@loc", "pelvis", "thigh_r", "calf_r", "foot_r", "thigh_l", "calf_l", "foot_l")
W_CONTACT = {"pelvis@loc": (0, 0, -0.025), "pelvis": (0, 0, -5), "thigh_r": (24, 0, 0), "calf_r": (-4, 0, 0),
             "foot_r": (10, 0, -6), "thigh_l": (-16, 0, 0), "calf_l": (-18, 0, 0), "foot_l": (-12, 0, 6)}
W_DOWN = dict(W_CONTACT, **{"pelvis@loc": (0, 0, -0.04), "thigh_r": (18, 0, 0), "calf_r": (-14, 0, 0), "foot_r": (2, 0, -6),
                            "thigh_l": (-10, 0, 0), "calf_l": (-40, 0, 0), "foot_l": (-20, 0, 6)})
W_PASS = {"pelvis@loc": (0, 0, 0.01), "pelvis": (0, 0, 0), "thigh_r": (0, 0, 0), "calf_r": (-4, 0, 0), "foot_r": (0, 0, -6),
          "thigh_l": (26, 0, 0), "calf_l": (-58, 0, 0), "foot_l": (8, 0, 6)}
W_UP = dict(W_PASS, **{"pelvis@loc": (0, 0, 0.02), "thigh_r": (-8, 0, 0), "calf_r": (-4, 0, 0), "foot_r": (-8, 0, -6),
                       "thigh_l": (30, 0, 0), "calf_l": (-30, 0, 0), "foot_l": (12, 0, 6)})
GAIT = [(0, W_CONTACT), (4, W_DOWN), (8, W_PASS), (11, W_UP)]


def legs_of(step, short=1.0):
    """A gait key's legs, the stride shortened by `short` (a load held up in front: shorter, flatter steps)."""
    out = {}
    for k, v in step.items():
        if k == "pelvis@loc":
            out[k] = (v[0], v[1], v[2] * short)
        elif k.startswith("thigh"):
            out[k] = (v[0] * short, v[1], v[2])
        else:
            out[k] = v
    return out


def walking(name, upper, cycle=30, short=1.0, backwards=False, tweak=None):
    """A walk cycle under an upper body: `upper(frame, side)` gives the arms and torso for a key (side: which leg leads)."""
    c = Clip(name)
    half = cycle // 2
    keys = [(f * cycle // 30, legs_of(p, short)) for f, p in GAIT]
    seq = [(f, p, "r") for f, p in keys] + [(f + half, swap(p), "l") for f, p in keys]
    if backwards:
        # The same steps the other way: each key's legs played in reverse order round the cycle.
        seq = [(f, seq[(len(seq) - i) % len(seq)][1], seq[(len(seq) - i) % len(seq)][2]) for i, (f, _, _) in enumerate(seq)]
    for f, legs, side in seq:
        pose = dict(upper(f, side))
        pose.update(legs)
        if tweak:
            pose = tweak(f, pose)
        c.key(f, pose)
    c.close(cycle)
    return c


def elbow_out(e):
    """Keeps an elbow out beside the body rather than through it."""
    return 0.3 if abs(e.x) < 0.14 and abs(e.y) < 0.14 else 0.0


def arm_to(pose, side, wrist, fist=True, grip=80):
    """The arm put so its wrist is at `wrist` (armature space: x right, y forward, z up from the feet)."""
    k = 1 if side == "r" else -1
    p = rig.reach(sk, pose, f"upperarm_{side}", f"lowerarm_{side}", wrist, elbow_axis=2, bend=k, avoid=elbow_out)
    if fist:
        p[f"fingers_{side}"] = (0, grip * k, 0)
        p[f"thumb_{side}"] = (0, 30 * k, 0)
    return p


def hands(pose, r, l, **kw):
    return arm_to(arm_to(pose, "r", r, **kw), "l", l, **kw)


def leg_to(pose, side, ankle):
    return rig.reach(sk, pose, f"thigh_{side}", f"calf_{side}", ankle, elbow_axis=0, bend=-1)


def at(v, dx=0.0, dy=0.0, dz=0.0):
    return (v[0] + dx, v[1] + dy, v[2] + dz)


clips = []

# --- carry: a crate held in front at the chest, leant back against it -----------------------------------------
# (The sim holds it 1.1 m up and 0.4 m out from the feet, Bodies.Carry; the hands either side of it, a little under.)
CARRY_BODY = over(STAND, spine_01=(4, 0, 0), spine_02=(3, 0, 0), spine_03=(0, 0, 0), neck=(4, 0, 0), head=(-6, 0, 0),
                  clavicle_r=(0, 4, 8), clavicle_l=(0, -4, -8))
CARRY_R, CARRY_L = (0.2, 0.44, 1.08), (-0.2, 0.44, 1.08)
CARRY = hands(CARRY_BODY, CARRY_R, CARRY_L, grip=60)
carry = Clip("carry")
carry.key(0, CARRY)
carry.key(24, hands(over(CARRY_BODY, spine_03=(1.5, 0, 0), head=(-4, 0, 2)), at(CARRY_R, dz=-0.02), at(CARRY_L, dz=-0.02), grip=60))
carry.key(40, hands(over(CARRY_BODY, pelvis=(0, 2, 0), head=(-7, 0, -3)), at(CARRY_R, dz=-0.01), at(CARRY_L, dz=-0.01), grip=60))
carry.close(60)
clips.append(carry)


def carry_upper(f, side):
    # The load bobs with the step (down on the contact); the shoulders work against it.
    bob = -0.02 if f % 15 < 5 else 0.0
    sway = 3 if side == "r" else -3
    return hands(over(CARRY_BODY, spine_02=(3, 0, sway)), at(CARRY_R, dz=bob), at(CARRY_L, dz=bob), grip=60)


clips.append(walking("carry_walk", carry_upper, cycle=30, short=0.75))

# --- drag: a body by its collar, hunched, walking backwards -----------------------------------------------------
DRAG_BODY = over(STAND, pelvis=(-10, 0, 0), spine_01=(-14, 0, 0), spine_02=(-12, 0, 0), spine_03=(-8, 0, 0),
                 neck=(22, 0, 0), head=(10, 0, 0))
DRAG_R, DRAG_L = (0.14, 0.42, 0.7), (-0.1, 0.44, 0.72)


def drag_upper(f, side):
    tug = 0.03 if f % 15 < 6 else 0.0
    return hands(DRAG_BODY, at(DRAG_R, dy=-tug), at(DRAG_L, dy=-tug))


clips.append(walking("drag", drag_upper, cycle=36, short=0.6, backwards=True))

# --- door: the right hand to the handle, hauled across, let go, again -------------------------------------------
DOOR_BODY = over(STAND, spine_02=(-4, 0, -6), spine_03=(-5, 0, -6), head=(-2, 0, -10))
door = Clip("door")
door.key(0, arm_to(DOOR_BODY, "r", (0.36, 0.44, 1.08), grip=20))
door.key(5, arm_to(DOOR_BODY, "r", (0.38, 0.46, 1.06)))
door.key(14, arm_to(over(DOOR_BODY, spine_02=(-3, 0, 4), spine_03=(-4, 0, 4), pelvis=(0, 0, 4)), "r", (0.12, 0.36, 1.06)), "LINEAR")
door.hold(18)
door.close(24)
clips.append(door)

# --- handbrake: a wheel at the waist, hand over hand (a quarter turn each, 36 frames a turn) --------------------------
import math  # noqa: E402

WHEEL, WR = Vector((0.0, 0.44, 1.02)), 0.2
HB_BODY = over(STAND, pelvis=(-4, 0, 0), spine_01=(-8, 0, 0), spine_02=(-8, 0, 0), spine_03=(-6, 0, 0), neck=(14, 0, 0),
               head=(6, 0, 0), thigh_r=(8, 0, 0), calf_r=(-14, 0, 0), thigh_l=(-4, 0, 0), calf_l=(-6, 0, 0))


def rim(a):
    """A point on the wheel's rim (it lies flat): `a` round from the right, clockwise from above."""
    return tuple(WHEEL + Vector((math.cos(a) * WR, -math.sin(a) * WR * 0.6, 0.0)))


handbrake = Clip("handbrake")
for i, f in enumerate((0, 9, 18, 27)):
    # The right hand winds from the near right round to the far side while the left comes back for a fresh grip.
    a = i * math.pi / 2
    r = rim(-0.6 + 0.5 * math.sin(a))
    l = rim(math.pi + 0.6 - 0.5 * math.sin(a + math.pi / 2))
    lean = 3 * math.sin(a)
    handbrake.key(f, hands(over(HB_BODY, spine_02=(-8, 0, lean), spine_03=(-6, 0, lean)), r, l))
handbrake.close(36)
clips.append(handbrake)

# --- hatch: stooped over it, both hands under its edge, heaved up -----------------------------------------------
HATCH_LOW = over(STAND, pelvis__loc=(0, -0.08, 0), pelvis=(-24, 0, 0), spine_01=(-22, 0, 0), spine_02=(-16, 0, 0),
                 spine_03=(-8, 0, 0), neck=(24, 0, 0), head=(8, 0, 0),
                 thigh_r=(60, 0, 0), calf_r=(-80, 0, 0), foot_r=(20, 0, -6), thigh_l=(50, 0, 0), calf_l=(-70, 0, 0), foot_l=(18, 0, 6))
HATCH_UP = over(HATCH_LOW, pelvis__loc=(0, -0.03, 0), pelvis=(-10, 0, 0), spine_01=(-8, 0, 0), spine_02=(-6, 0, 0),
                thigh_r=(24, 0, 0), calf_r=(-30, 0, 0), foot_r=(6, 0, -6), thigh_l=(18, 0, 0), calf_l=(-24, 0, 0), foot_l=(6, 0, 6))
hatch = Clip("hatch")
hatch.key(0, hands(HATCH_LOW, (0.22, 0.5, 0.36), (-0.22, 0.5, 0.36)))
hatch.key(8, hands(HATCH_LOW, (0.22, 0.52, 0.4), (-0.22, 0.52, 0.4)))
hatch.key(20, hands(HATCH_UP, (0.22, 0.48, 0.95), (-0.22, 0.48, 0.95)))
hatch.hold(26)
hatch.close(36)
clips.append(hatch)

# --- uncouple: down on the plate, the right hand on the pin, tugging it up -----------------------------------------
CROUCH = over(STAND, pelvis__loc=(0, -0.08, 0), pelvis=(-14, 0, 0), spine_01=(-18, 0, 0), spine_02=(-12, 0, 0),
              spine_03=(-8, 0, 0), neck=(26, 0, 0), head=(14, 0, 0),
              thigh_r=(100, -6, 0), calf_r=(-128, 0, 0), foot_r=(30, 0, -8),
              thigh_l=(84, 8, 0), calf_l=(-110, 0, 0), foot_l=(24, 0, 8))
PIN = (0.06, 0.5, 0.2)
uncouple = Clip("uncouple")
uncouple.key(0, arm_to(arm_to(CROUCH, "r", PIN), "l", (-0.2, 0.32, 0.55), grip=20))
uncouple.key(6, arm_to(arm_to(over(CROUCH, spine_02=(-10, 0, 0)), "r", at(PIN, dz=0.07)), "l", (-0.2, 0.32, 0.57), grip=20), "LINEAR")
uncouple.key(9, arm_to(arm_to(CROUCH, "r", at(PIN, dz=0.01)), "l", (-0.2, 0.32, 0.55), grip=20))
uncouple.key(15, arm_to(arm_to(over(CROUCH, spine_02=(-9, 0, 2)), "r", at(PIN, dz=0.08)), "l", (-0.2, 0.32, 0.57), grip=20), "LINEAR")
uncouple.close(24)
clips.append(uncouple)

# --- vent: the blow-off's wheel up at the head, both hands on it, wrenching -----------------------------------------
# (The valve stands 1.1 m off the running board, its wheel over that: SceneArt.CabControls' vent stand.)
VWHEEL = Vector((0.0, 0.34, 1.32))
VENT_BODY = over(STAND, spine_01=(2, 0, 0), spine_02=(2, 0, 0), spine_03=(0, 0, 0), neck=(-4, 0, 0), head=(-10, 0, 0),
                 thigh_r=(14, 0, 0), calf_r=(-10, 0, 0), thigh_l=(-8, 0, 0), calf_l=(-4, 0, 0))
vent = Clip("vent")
for f, a, lean in ((0, 0.0, 0), (10, 0.35, -4), (18, 0.4, -4), (28, 0.05, 2)):
    r = tuple(VWHEEL + Vector((math.cos(-0.3 + a) * 0.13, -0.04, math.sin(-0.3 + a) * 0.13)))
    l = tuple(VWHEEL + Vector((-math.cos(-0.3 - a) * 0.13, -0.04, math.sin(-0.3 - a) * 0.13)))
    vent.key(f, hands(over(VENT_BODY, spine_02=(2, 0, lean), spine_03=(0, 0, lean)), r, l))
vent.close(36)
clips.append(vent)

# --- lever: a pair of levers at the waist, worked one against the other ---------------------------------------------
LEVER_BODY = over(STAND, spine_01=(-4, 0, 0), spine_02=(-6, 0, 0), spine_03=(-4, 0, 0), neck=(12, 0, 0), head=(4, 0, 0))
lever = Clip("lever")
for f, d in ((0, 0.0), (10, 0.08), (20, 0.0), (30, -0.06)):
    lever.key(f, hands(over(LEVER_BODY, spine_02=(-6, 0, d * 40)), (0.16, 0.42 + d, 0.98), (-0.16, 0.42 - d, 0.98)))
lever.close(40)
clips.append(lever)

# --- push: leant into the gun, walking it along -----------------------------------------------------------------
PUSH_BODY = over(STAND, pelvis=(-14, 0, 0), spine_01=(-16, 0, 0), spine_02=(-12, 0, 0), spine_03=(-6, 0, 0),
                 neck=(24, 0, 0), head=(8, 0, 0))


def push_upper(f, side):
    shove = 0.03 if f % 15 < 5 else 0.0
    return hands(PUSH_BODY, (0.22, 0.52 + shove, 1.15), (-0.22, 0.52 + shove, 1.15), grip=50)


clips.append(walking("push", push_upper, cycle=36, short=0.7))

# --- held: struggling, jerking against it ----------------------------------------------------------------------
HELD = over(STAND, pelvis__loc=(0, 0, -0.04), pelvis=(-6, 0, 0), spine_01=(8, 0, 0), spine_02=(6, 0, 0), spine_03=(4, 0, 0),
            neck=(-6, 0, 0), head=(-14, 0, 0), thigh_r=(20, 0, 0), calf_r=(-30, 0, 0), thigh_l=(-6, 0, 0), calf_l=(-20, 0, 0))
held = Clip("held")
held.key(0, hands(HELD, (0.3, 0.3, 1.2), (-0.25, 0.25, 1.3), fist=True))
held.key(5, hands(over(HELD, spine_02=(8, 0, 14), spine_03=(6, 0, 10), head=(-10, 0, 24)), (0.38, 0.1, 1.0), (-0.2, 0.35, 1.45)), "LINEAR")
held.key(9, hands(over(HELD, spine_02=(5, 0, 8)), (0.34, 0.16, 1.05), (-0.22, 0.32, 1.4)))
held.key(15, hands(over(HELD, spine_02=(8, 0, -14), spine_03=(6, 0, -10), head=(-12, 0, -24)), (0.2, 0.36, 1.42), (-0.38, 0.1, 1.0)), "LINEAR")
held.key(19, hands(over(HELD, spine_02=(5, 0, -8)), (0.24, 0.32, 1.35), (-0.34, 0.16, 1.05)))
held.close(24)
clips.append(held)

# --- gunner: sat on the cannon's seat ---------------------------------------------------------------------------
# The seat pan's top is 0.48 m over the roof (Guns' pivot 0.9 up, the seat 0.42 under it); the tiller's grip and the
# handwheel's knob from tools/models/recipes/cannon.py, measured from the seat (it's 0.75 m behind the pivot): the model
# is drawn on the roof under the seat, facing the gun's way.
PAN = 0.48
TILLER = (-0.3, 0.16, PAN + 0.21)
WHEEL_KNOB = (0.31, 0.3, PAN + 0.29)
SEATED = over(STAND, pelvis__loc=(0, -0.02, PAN + 0.06 - 0.955), pelvis=(-6, 0, 0), spine_01=(-6, 0, 0),
              spine_02=(-6, 0, 0), spine_03=(-4, 0, 0), neck=(10, 0, 0), head=(-2, 0, 0))
SEATED = leg_to(leg_to(SEATED, "r", (0.16, 0.44, 0.1)), "l", (-0.16, 0.48, 0.1))
SEATED["foot_r"], SEATED["foot_l"] = (-6, 0, -6), (-6, 0, 6)
gunner = Clip("gunner")
gunner.key(0, hands(SEATED, WHEEL_KNOB, TILLER))
gunner.key(30, hands(over(SEATED, spine_03=(-2.5, 0, 0), head=(0, 0, 4)), at(WHEEL_KNOB, dz=0.005), at(TILLER, dy=0.01)))
gunner.key(55, hands(over(SEATED, head=(-3, 0, -5)), WHEEL_KNOB, at(TILLER, dy=-0.01)))
gunner.close(80)
clips.append(gunner)

# --- fall: in the air -----------------------------------------------------------------------------------------
FALL = over(STAND, **{k.replace("@", "__"): v for k, v in mirror({
    "spine_01": (4, 0, 0), "spine_02": (4, 0, 0), "neck": (-4, 0, 0), "head": (-10, 0, 0),
    "clavicle_r": (0, -10, 0), "upperarm_r": (0, -20, -10), "lowerarm_r": (0, 0, 30), "hand_r": (0, -10, 0), "fingers_r": (0, 10, 0),
}).items()}, thigh_r=(40, 0, 0), calf_r=(-70, 0, 0), foot_r=(-20, 0, -6), thigh_l=(14, 0, 0), calf_l=(-40, 0, 0), foot_l=(-24, 0, 6))
fall = Clip("fall")
fall.key(0, FALL)
fall.key(6, over(FALL, upperarm_r=(0, -34, -20), upperarm_l=(0, 4, 4), thigh_r=(30, 0, 0), thigh_l=(26, 0, 0)))
fall.key(12, over(FALL, upperarm_r=(0, 4, -4), upperarm_l=(0, 34, 20), thigh_r=(44, 0, 0), thigh_l=(10, 0, 0)))
fall.close(18)
clips.append(fall)

# --- swing: an overhead blow, right-handed, the left hand coming off the haft ---------------------------------------
SW_BODY = over(STAND, thigh_r=(-10, 0, 0), calf_r=(-6, 0, 0), thigh_l=(18, 0, 0), calf_l=(-16, 0, 0))
SW_UP = over(SW_BODY, spine_01=(4, 0, -10), spine_02=(6, 0, -14), spine_03=(4, 0, -10), head=(-8, 0, 14))
SW_DOWN = over(SW_BODY, pelvis=(-6, 0, 10), spine_01=(-14, 0, 8), spine_02=(-16, 0, 10), spine_03=(-10, 0, 6), head=(10, 0, -8))
swing = Clip("swing", loop=False)
swing.key(0, hands(SW_BODY, (0.24, 0.3, 1.1), (0.08, 0.3, 1.05)))
swing.key(7, hands(SW_UP, (0.24, -0.04, 1.75), (0.1, 0.04, 1.62)))
swing.key(11, hands(SW_DOWN, (0.1, 0.6, 1.05), (-0.18, 0.3, 1.0)), "LINEAR")
swing.key(14, hands(SW_DOWN, (0.06, 0.56, 0.86), (-0.22, 0.28, 0.98)))
swing.key(24, hands(SW_BODY, (0.24, 0.3, 1.1), (0.08, 0.3, 1.05)))
clips.append(swing)

# --- mend: down on one knee at the firebox, the wrench worked in short pulls, the left hand braced on the backhead ---
MEND = over(STAND, pelvis__loc=(0, -0.02, 0), pelvis=(-10, 0, 0), spine_01=(-14, 0, 0), spine_02=(-10, 0, 0),
            spine_03=(-6, 0, 0), neck=(18, 0, 0), head=(10, 0, 0),
            thigh_r=(90, -4, 0), calf_r=(-96, 0, 0), foot_r=(10, 0, -6),
            thigh_l=(-10, 4, 0), calf_l=(-110, 0, 0), foot_l=(-50, 0, 6))
NUT, BRACE = (0.14, 0.5, 0.78), (-0.2, 0.52, 0.95)
mend = Clip("mend")
for f, a in ((0, 0.0), (7, 1.0), (10, 1.0), (18, 0.0)):
    # A pull down and round on the handle (the nut its pivot), then back for a fresh bite.
    r = (NUT[0] + 0.06 * a, NUT[1] - 0.02 * a, NUT[2] - 0.07 * a)
    mend.key(f, arm_to(arm_to(over(MEND, spine_02=(-10 - 3 * a, 0, -2 * a)), "r", r), "l", BRACE, grip=20), "LINEAR" if f == 7 else "BEZIER")
mend.close(24)
clips.append(mend)

# --- gap: on the coupling plate between two cars, feet wide, arms out for the sway (GDD §32: the gap's the danger) -----
GAP = over(STAND, pelvis__loc=(0, 0, -0.06), pelvis=(-6, 0, 0), spine_01=(-6, 0, 0), spine_02=(-4, 0, 0), neck=(16, 0, 0),
           head=(10, 0, 0), thigh_r=(-18, -12, 0), calf_r=(-20, 0, 0), foot_r=(12, 0, -10),
           thigh_l=(26, 12, 0), calf_l=(-26, 0, 0), foot_l=(-4, 0, 10))
gap = Clip("gap")
for f, lean in ((0, 0), (12, 5), (24, 0), (36, -5)):
    gap.key(f, hands(over(GAP, spine_02=(-4, 0, lean), spine_03=(-2, 0, lean * 0.6), head=(10, 0, -lean)),
                     (0.5, 0.22, 0.98 - lean * 0.01), (-0.5, 0.24, 0.98 + lean * 0.01), fist=False))
gap.close(48)
clips.append(gap)

# --- extinguish: the extinguisher braced on the hip, knob struck, the hose's nozzle aimed down at the fire's foot -----
# (Carried, the body rides 1.1 m up, 0.4 out, Bodies.Carry, drawn there unlifted: the left hand on its handle on top, the right
# on the nozzle.)
EXT_BODY = over(STAND, pelvis=(-4, 0, 6), spine_01=(-6, 0, 4), spine_02=(-6, 0, 0), spine_03=(-4, 0, 0), neck=(14, 0, 0),
                head=(8, 0, 0), thigh_r=(16, 0, 0), calf_r=(-12, 0, 0), thigh_l=(-8, 0, 0), calf_l=(-6, 0, 0))
EXT_HANDLE, EXT_NOZZLE = (-0.04, 0.42, 1.34), (0.26, 0.62, 0.9)
extinguish = Clip("extinguish")
for f, (dx, dz) in ((0, (0.0, 0.0)), (8, (0.06, -0.03)), (16, (0.0, -0.05)), (24, (-0.06, -0.02))):
    extinguish.key(f, hands(EXT_BODY, at(EXT_NOZZLE, dx=dx, dz=dz), EXT_HANDLE, grip=70))
extinguish.close(32)
clips.append(extinguish)

# --- lantern: the hand lamp held out low in the right hand, swinging with the step, the left arm free --------------
LAMP_AT = (0.2, 0.34, 0.98)


def lantern_upper(f, side):
    swing = 0.05 if side == "r" else -0.05
    p = arm_to(STAND, "r", at(LAMP_AT, dy=swing, dz=-abs(swing) * 0.4), grip=90)
    p["upperarm_l"] = (-6 if side == "r" else 18, -72, -4)
    return p


clips.append(walking("lantern_walk", lantern_upper, cycle=30))
lantern = Clip("lantern")
lantern.key(0, arm_to(STAND, "r", LAMP_AT, grip=90))
lantern.key(30, arm_to(over(STAND, head=(-3, 0, 6)), "r", at(LAMP_AT, dz=0.02, dx=0.02), grip=90))
lantern.close(60)
clips.append(lantern)

# --- haul: down on a knee, both hands on a friend's coat collar, hauling them back (the rescue, App. A.1) -------------
HAUL = over(STAND, pelvis__loc=(0, -0.12, 0), pelvis=(-18, 0, 0), spine_01=(-14, 0, 0), spine_02=(-8, 0, 0),
            spine_03=(-4, 0, 0), neck=(18, 0, 0), head=(6, 0, 0),
            thigh_r=(80, -4, 0), calf_r=(-90, 0, 0), foot_r=(10, 0, -6), thigh_l=(-14, 6, 0), calf_l=(-100, 0, 0), foot_l=(-40, 0, 6))
haul = Clip("haul")
for f, pull in ((0, 0.0), (10, 0.16), (14, 0.16), (24, 0.0)):
    lean = -12 * pull / 0.16
    haul.key(f, hands(over(HAUL, spine_01=(-14 - lean, 0, 0), spine_02=(-8 - lean * 0.6, 0, 0)),
                      (0.14, 0.62 - pull, 0.62 + pull * 0.6), (-0.14, 0.62 - pull, 0.62 + pull * 0.6), grip=90),
             "LINEAR" if f == 10 else "BEZIER")
haul.close(32)
clips.append(haul)

# --- first person: only the forearms and hands are drawn (CreatureArt.OwnArms), from the eye --------------------
# The eye: over the head bone's root, a little forward (the mask's eyepieces). Hands placed from it: the right low and
# out to the right with the tool up in view, the left lower, at the edge of it.
EYE = Vector((0.0, 0.1, 1.68))
FP_R, FP_L = tuple(EYE + Vector((0.19, 0.44, -0.2))), tuple(EYE + Vector((-0.22, 0.42, -0.27)))


def fp(r, l, wrist=(65, 0, 90)):
    """Both hands placed, the right wrist turned so what it holds stands up out of the fist and leans in across the view
    (the socket's haft, hand_r_weapon's +Y, is forward at rest; `wrist` is its world turn from there: rig.hang)."""
    p = hands(STAND, r, l)
    p["hand_r"] = rig.hang(sk, p, "hand_r", *wrist)
    return p


fp_hold = Clip("fp_hold")
fp_hold.key(0, fp(FP_R, FP_L))
fp_hold.key(30, fp(at(FP_R, dz=-0.008, dx=0.003), at(FP_L, dz=-0.01)))
fp_hold.key(50, fp(at(FP_R, dz=0.004), at(FP_L, dz=0.002, dx=-0.004)))
fp_hold.close(72)
clips.append(fp_hold)
fp_walk = Clip("fp_walk")
for f, (dx, dz) in ((0, (0.0, -0.02)), (8, (0.012, 0.0)), (15, (0.0, -0.02)), (23, (-0.012, 0.0))):
    fp_walk.key(f, fp(at(FP_R, dx=dx, dz=dz), at(FP_L, dx=dx * 0.6, dz=dz * 1.2)))
fp_walk.close(30)
clips.append(fp_walk)
# The blow: drawn back up by the right ear, the bar laid back over the shoulder; brought down hard across and through, to
# low left; back up. (The wrist cocks back as it rises and snaps over as it comes down.)
fp_swing = Clip("fp_swing", loop=False)
fp_swing.key(0, fp(FP_R, FP_L))
fp_swing.key(7, fp(tuple(EYE + Vector((0.3, 0.12, 0.02))), at(FP_L, dz=0.04, dx=0.06), wrist=(150, 0, 90)))
fp_swing.key(11, fp(tuple(EYE + Vector((0.0, 0.5, -0.32))), at(FP_L, dz=-0.04, dx=-0.04), wrist=(20, 0, 70)), "LINEAR")
fp_swing.key(14, fp(tuple(EYE + Vector((-0.1, 0.46, -0.46))), at(FP_L, dz=-0.05, dx=-0.05), wrist=(0, 0, 60)))
fp_swing.key(24, fp(FP_R, FP_L))
clips.append(fp_swing)

kit.build()
rig.bake(sk, clips, plant=rig.feet_planter(sk, clips={"carry", "carry_walk", "drag", "door", "handbrake", "hatch",
                                                        "uncouple", "vent", "lever", "push", "swing", "mend",
                                                        "gap", "extinguish", "lantern", "lantern_walk", "haul"}))
rig.export(rig.args()[0] if rig.args() else "crew_clips.glb", kit)
print(f"[dt] crew clips {[c.name + ':' + str(c.length) for c in clips]}")
