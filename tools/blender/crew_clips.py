"""CREW CLIPS: the crew's actions (GDD §31: "heavy, grounded, readable, a bit stiff, practical, hurried under stress"),
authored on crew.py's skeleton apart from its mesh, so a new clip doesn't mean re-baking the crew (tools/models/recipes/
crew.py's high copy and atlas). CreatureArt merges content/art/models/crew_clips.glb into crew.glb on load, bone by
bone by name (ModelLoader.WithClips: `<name>_clips.glb` beside `<name>.glb`; ARCHITECTURE §8 note 145).

What each is for (CrewActs, from the sim's state; GDD/spec where the act is):
  carry, carry_walk   a crate or a heavy crate's end held in front (spec D.2, B.2's 2.8 m/s)
  drag, drag_fwd      a body by its collar, walking backwards (GDD App. D.10); and forwards, hauled along behind
  climb, shovel,      crew.py's, reworked here (Look Review notes; CreatureArt lets these win over the mesh's own):
  crouch_idle         up a ladder's rungs at the ladder's pace, the fireman's swing, and down on the haunches
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
  firedoor            the firehole's door hauled open or shut, off the heat (GDD §12)
  mend                down at the firebox with the repair kit's wrench, ratcheting (T109, note 150)
  gap                 on the coupling plate between cars, feet wide, arms out for balance (GDD §32)
  extinguish, spray   the extinguisher on the hip, its nozzle aimed at the fire's foot; at work, braced, kicking (App. C.5)
  lantern, _walk      the hand lamp held out low, swinging with the step
  haul                down on a knee, both hands on a friend's collar, hauling them free (App. A.1's rescue)
  haul_up             stood at the edge, leant back, hauling a friend up over it hand over hand (the Draggers, App. A.4)
  pull_mouth          a friend half down the Car Hugger's mouth: braced, leant far back, heaved out hand over hand (note 378)
  pry_off             the Tippy Toesie's fingers over a friend's mouth, prised off and wrenched aside (note 378)
  haul_down           a friend being lifted away (the Whistler, the Choir): reached up for the legs, hauled down with the weight (note 378)
  gap_step            across the coupling plate: short wide steps, arms out, eyes on the gap (GDD §32)
  drive, whistle      at the controls, the hands on the regulator and brake; the left up on the whistle cord (GDD §12)
  smash, pry, pick    breaching a Holdout (App. D.7): the lock smashed, the barricade pried, the lock picked with the kit
  getup               freed, up off the Holdout's floor (App. D.8)
  take_down, hang_up  the extinguisher lifted off its bracket into the hands, and hung back on it (App. C.5)
  hurry               running under stress: hunched, arms pumping (GDD §31)
  stagger             a blow taken: rocked back a step, and back (App. C.2)
  shuffle             a freed prisoner's walk (App. D.8, note 407): short dragging steps, hunched, the hands kept together in front
                      as if the irons were still on
  jump                off the roof on a jump: the spring, then the leap tucked, arms reaching for the far roof (note 375)
  stumble             on a car straining on a bend: thrown one way and the other, arms out, a foot stepped wide (note 375)
  throw, chute, spout a ground switch lever heaved over; the coaling chute's lever hauled down; a spout swung round (C.6, D.3)
  cradle, _walk       the child in the arms, clinging (App. C.4; soot_child's clutch)
  shoulder, _walk     a body over the left shoulder, the arm round its legs (App. C.4; the sim's Bodies.Shoulder)
  climb_carry         the solo remainer up a ladder, a body over the shoulder (App. D.9)
  held_*              one per GRAB (App. A.1): hang, mouth, carried, cover, frozen, pinned, seized, dragged
  reload              the cannon's reload from the seat: powder, ram, prime (note 137)
  fp_hold, fp_walk    first person (X3): the tool held up in view, the eye at EYE (CreatureArt.OwnArms puts it at the camera)
  fp_swing            first person: the blow, as long as the melee's recovery (enemies.json melee.swingSeconds, 0.8 s)
  paint               Dave at his easel (note 491): dabs and a stroke at the canvas, the brush to the palette, a step back to look
  wave, point, dance  the yard's emotes (GDD §9, note 298's wheel): a wave over the shoulder, a point straight ahead at the
                      shoulder's height, a workman's jig (queue #44, note 306)
In place, 30 fps, like crew.py's; the root never travels (the sim moves the crewmate).

    blender -b --python tools/blender/crew_clips.py -- content/art/models/crew_clips.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from mathutils import Quaternion  # noqa: E402
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
# The step (Look Review notes: "stiff in the legs"): the front knee soft at the heel strike and taking the weight down
# through it, the back foot rolled up onto its toe before it leaves, the swinging knee lifted well through the pass, and
# the hips dropping on the load and rising over the passing leg.
W_CONTACT = {"pelvis@loc": (0, 0, -0.02), "pelvis": (0, 0, -5), "thigh_r": (26, 0, 0), "calf_r": (-8, 0, 0),
             "foot_r": (14, 0, -6), "thigh_l": (-14, 0, 0), "calf_l": (-26, 0, 0), "foot_l": (-22, 0, 6)}
W_DOWN = dict(W_CONTACT, **{"pelvis@loc": (0, 0, -0.045), "thigh_r": (22, 0, 0), "calf_r": (-24, 0, 0), "foot_r": (2, 0, -6),
                            "thigh_l": (-6, 0, 0), "calf_l": (-58, 0, 0), "foot_l": (-26, 0, 6)})
W_PASS = {"pelvis@loc": (0, 0, 0.005), "pelvis": (0, 0, 0), "thigh_r": (2, 0, 0), "calf_r": (-10, 0, 0), "foot_r": (0, 0, -6),
          "thigh_l": (32, 0, 0), "calf_l": (-76, 0, 0), "foot_l": (12, 0, 6)}
W_UP = dict(W_PASS, **{"pelvis@loc": (0, 0, 0.02), "thigh_r": (-10, 0, 0), "calf_r": (-6, 0, 0), "foot_r": (-12, 0, -6),
                       "thigh_l": (34, 0, 0), "calf_l": (-36, 0, 0), "foot_l": (16, 0, 6)})
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


def seg_dist(p, a, b):
    """How far the point `p` is from the segment `a`-`b`."""
    ab = b - a
    t = 0.0 if ab.length_squared < 1e-9 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def clear_of_body(pose, wrist):
    """An elbow-placing penalty that keeps the arm out of the body as this pose has it (Look Review notes: arms through
    the chest, the hips, the knees): the elbow and the forearm's middle held off the trunk, the coat's skirt, the head
    and the thighs, by the coat's own measure (crew.glb: about 0.2 m round the chest and hips, the skirt 0.22 m)."""
    names = [("pelvis", "head"), ("spine_02", "head"), ("neck", "head"), ("head", "head"),
             ("thigh_r", "head"), ("calf_r", "head"), ("thigh_l", "head"), ("calf_l", "head")]
    pel, sp2, nk, hd, thr, knr, thl, knl = rig.pose_points(sk, pose, names)
    down = (pel - sp2).normalized()
    skirt_end = pel + down * 0.4
    head_c = hd + (hd - nk).normalized() * 0.1
    wrist = Vector(wrist)

    def cost(e):
        c = elbow_out(e) * 0.2
        for q in (e, (e + wrist) * 0.5):
            for a, b, r in ((pel, nk, 0.24), (pel, skirt_end, 0.26), (head_c, head_c, 0.18), (thr, knr, 0.14), (thl, knl, 0.14)):
                d = seg_dist(q, a, b)
                if d < r:
                    c += (r - d) * 1.5
        return c
    return cost


def look_at(pose, target, share=0.45):
    """The head (and the neck, `share` of it) turned so the face is on `target` (armature space). On this rig a
    positive neck or head pitch lifts the face (a Look Review note caught "head down" written as up); a positive yaw
    (z) turns it to the left."""
    target = Vector(target)
    best, best_p = None, pose
    base_n, base_h = pose.get("neck", (0, 0, 0)), pose.get("head", (0, 0, 0))
    for pitch in range(-70, 41, 5):
        for yaw in range(-60, 61, 10):
            p = dict(pose)
            p["neck"] = (pitch * share, base_n[1], yaw * share)
            p["head"] = (pitch * (1 - share), base_h[1], yaw * (1 - share))
            R = rig.world_rotation(sk, p, "head")
            eye = rig.pose_points(sk, p, [("head", "head")])[0] + R @ Vector((0, 0.08, 0.08))
            face = R @ Vector((0, 1, 0))
            want = (target - eye).normalized()
            err = 1 - face.dot(want)
            if best is None or err < best:
                best, best_p = err, p
    return best_p


TOOL_DROOP, TOOL_SIDE = 34, 1  # CreatureArt.ToolDroop (0.6 rad) and the side of hand_r_weapon it tips to


def aim_tool(pose, direction):
    """The right wrist turned so the tool in the fist points along `direction` (Look Review: the bar and the wrench
    pointed at the sky, or back through the head). CreatureArt draws a held tool along hand_r_weapon's +Y tipped toward
    its +X by ToolDroop (ToolGrip), so that's the line aimed."""
    d = Vector(direction).normalized()
    k = math.radians(TOOL_DROOP)
    along = Vector((math.sin(k) * TOOL_SIDE, math.cos(k), 0))
    best, best_p = None, pose
    for rx in range(-180, 181, 10):
        for rz in range(-90, 91, 10):
            p = dict(pose)
            p["hand_r"] = rig.hang(sk, p, "hand_r", rx, 0, rz)
            R = rig.world_rotation(sk, p, "hand_r_weapon")
            err = 1 - (R @ along).normalized().dot(d)
            if best is None or err < best:
                best, best_p = err, p
    return best_p


def arm_to(pose, side, wrist, fist=True, grip=80, elbow=None):
    """The arm put so its wrist is at `wrist` (armature space: x right, y forward, z up from the feet); `elbow`, a point
    its elbow is drawn toward (for the right arm; mirrored for the left). The arm is kept out of the body."""
    k = 1 if side == "r" else -1
    pole = None if elbow is None else (elbow[0] * k, elbow[1], elbow[2])
    p = rig.reach(sk, pose, f"upperarm_{side}", f"lowerarm_{side}", wrist, elbow_axis=2, bend=k, avoid=clear_of_body(pose, wrist),
                  pole=pole)
    if fist:
        p[f"fingers_{side}"] = (0, grip * k, 0)
        p[f"thumb_{side}"] = (0, 30 * k, 0)
    return p


def hands(pose, r, l, **kw):
    return arm_to(arm_to(pose, "r", r, **kw), "l", l, **kw)


def leg_to(pose, side, ankle, knee=None):
    pole = None if knee is None else ((knee[0] if side == "r" else -knee[0]), knee[1], knee[2])
    return rig.reach(sk, pose, f"thigh_{side}", f"calf_{side}", ankle, elbow_axis=0, bend=-1, pole=pole)


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


clips.append(walking("carry_walk", carry_upper, cycle=30, short=0.85))

# --- drag: a body by its collar, hunched, walking backwards -----------------------------------------------------
DRAG_BODY = over(STAND, pelvis=(-10, 0, 0), spine_01=(-14, 0, 0), spine_02=(-12, 0, 0), spine_03=(-8, 0, 0),
                 neck=(22, 0, 0), head=(10, 0, 0))
DRAG_R, DRAG_L = (0.14, 0.42, 0.7), (-0.1, 0.44, 0.72)


def drag_upper(f, side):
    tug = 0.03 if f % 15 < 6 else 0.0
    return hands(DRAG_BODY, at(DRAG_R, dy=-tug), at(DRAG_L, dy=-tug))


clips.append(walking("drag", drag_upper, cycle=36, short=0.6, backwards=True))

# --- drag_fwd: the same body hauled along behind, walking forwards (a Look Review note): leant into it, the right arm back
# and down to the collar at the hip, the left swinging out ahead for the balance, the head up to see where they're going.
DRAG_FWD = over(STAND, pelvis=(-8, 0, 0), spine_01=(-12, 0, 0), spine_02=(-10, 0, -4), spine_03=(-6, 0, -4), neck=(-2, 0, 0),
                head=(-8, 0, 6), clavicle_r=(0, 0, -6))


def drag_fwd_upper(f, side):
    a = math.cos(2 * math.pi * f / 36)
    tug = 0.04 * max(0.0, a)
    p = arm_to(DRAG_FWD, "r", (0.24, -0.44 + tug, 0.6 + tug * 0.5), grip=90, elbow=(0.3, -0.1, 0.9))
    return arm_to(p, "l", (-0.24, 0.12 + 0.16 * a, 0.92), grip=40, elbow=(0.3, -0.2, 1.1))


clips.append(walking("drag_fwd", drag_fwd_upper, cycle=36, short=0.7))

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

# --- crouch_idle (crew.py's, reworked: a Look Review pass found its forearms through the thighs): down on the haunches
# behind cover, the forearms resting on top of the knees and the hands hanging between them, breathing hard.
CR = over(STAND, pelvis__loc=(0, -0.12, 0), pelvis=(-10, 0, 0), spine_01=(-14, 0, 0), spine_02=(-12, 0, 0),
          spine_03=(-8, 0, 0), thigh_r=(100, -10, 0), calf_r=(-128, 0, 0), foot_r=(30, 0, -8),
          thigh_l=(96, 10, 0), calf_l=(-124, 0, 0), foot_l=(28, 0, 8))
CR = look_at(CR, (0, 3, 1.0))
crouch = Clip("crouch_idle")
for f, (dz, turn) in ((0, (0.0, 0)), (18, (0.012, 0)), (36, (0.0, 10))):
    crouch.key(f, hands(over(CR, spine_03=(-8 + 3 * dz / 0.012, 0, 0), head=(CR["head"][0], 0, turn)),
                        (0.14, 0.62, 0.6 + dz), (-0.14, 0.62, 0.6 + dz), grip=30, elbow=(0.3, 0.3, 0.95)))
crouch.close(54)
clips.append(crouch)

# --- uncouple: down on the plate, the right hand on the pin, tugging it up -----------------------------------------
# Down on the left knee at the plate, the right knee up and out of the arm's way, the left hand braced on it, the right
# on the pin between, the eyes on it (Look Review notes: the arm went through the knee; and the head looked up).
CROUCH = over(STAND, pelvis__loc=(0, -0.06, -0.42), pelvis=(-16, 0, 0), spine_01=(-16, 0, 0), spine_02=(-12, 0, 0),
              spine_03=(-8, 0, 0), thigh_r=(92, -16, 0), calf_r=(-98, 0, 0), foot_r=(10, 0, -6),
              thigh_l=(-10, 4, 0), calf_l=(-112, 0, 0), foot_l=(-52, 0, 6))
PIN = (-0.04, 0.52, 0.2)
KNEE_R = (-0.22, 0.48, 0.3)  # the left hand on the coupler's knuckle beside the pin, steadying (not across to the knee)
CROUCH = look_at(CROUCH, PIN)
uncouple = Clip("uncouple")
uncouple.key(0, arm_to(arm_to(CROUCH, "r", PIN, elbow=(0.1, 0.3, 0.8)), "l", KNEE_R, grip=20))
uncouple.key(6, arm_to(arm_to(over(CROUCH, spine_02=(-10, 0, 0)), "r", at(PIN, dz=0.07), elbow=(0.1, 0.3, 0.8)), "l", at(KNEE_R, dz=0.01), grip=20), "LINEAR")
uncouple.key(9, arm_to(arm_to(CROUCH, "r", at(PIN, dz=0.01), elbow=(0.1, 0.3, 0.8)), "l", KNEE_R, grip=20))
uncouple.key(15, arm_to(arm_to(over(CROUCH, spine_02=(-9, 0, 2)), "r", at(PIN, dz=0.08), elbow=(0.1, 0.3, 0.8)), "l", at(KNEE_R, dz=0.01), grip=20), "LINEAR")
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

# --- fall: in the air, panicking (a Look Review note: "not as flaily panic as it should be"): the arms thrown about
# for anything to hold, hands open and clawing, the legs running at nothing, the back arched and the head thrown back.
FALL = over(STAND, pelvis=(10, 0, 0), spine_01=(8, 0, 0), spine_02=(8, 0, 0), spine_03=(6, 0, 0), neck=(-10, 0, 0), head=(-16, 0, 0))
fall = Clip("fall")
# Arms up and out reaching for a hold that isn't there, turning slowly over through the drop, the legs bent and pedalling
# a little; the fingers half-closed. (Look Review notes: first not panicked enough, then "far too frantic": a fall lasts
# a second, read as the arms going up, not as thrashing.)
for f, (r, l, tr, cr, tl, cl, twist) in enumerate((
        ((0.48, 0.18, 1.56), (-0.44, 0.26, 1.44), 34, -70, 14, -40, 4),
        ((0.44, 0.26, 1.42), (-0.48, 0.18, 1.6), 18, -44, 30, -64, -4))):
    body = over(FALL, spine_02=(8, 0, twist), head=(-16, 0, -twist), thigh_r=(tr, 0, 0), calf_r=(cr, 0, 0), foot_r=(-20, 0, -6),
                thigh_l=(tl, 0, 0), calf_l=(cl, 0, 0), foot_l=(-20, 0, 6))
    fall.key(f * 15, hands(body, r, l, grip=70, elbow=(0.6, -0.1, 1.2)))
fall.close(30)

clips.append(fall)

# --- swing: an overhead blow, right-handed, the left hand coming off the haft ---------------------------------------
SW_BODY = over(STAND, thigh_r=(-10, 0, 0), calf_r=(-6, 0, 0), thigh_l=(18, 0, 0), calf_l=(-16, 0, 0))
SW_UP = over(SW_BODY, spine_01=(4, 0, -10), spine_02=(6, 0, -14), spine_03=(4, 0, -10), head=(-8, 0, 14))
SW_DOWN = over(SW_BODY, pelvis=(-6, 0, 10), spine_01=(-14, 0, 8), spine_02=(-16, 0, 10), spine_03=(-10, 0, 6), head=(10, 0, -8))
swing = Clip("swing", loop=False)
# The bar aimed as the blow goes: up ready, back over the shoulder, down through the target, low after (a Look Review
# pass: the wrist was never turned, so the bar pointed off at the sky through it all).
swing.key(0, aim_tool(hands(SW_BODY, (0.24, 0.3, 1.1), (0.12, 0.42, 1.06)), (0.1, 0.6, 0.6)))
swing.key(7, aim_tool(hands(SW_UP, (0.24, -0.04, 1.75), (0.1, 0.04, 1.62)), (0.05, -0.5, 0.85)))
swing.key(11, aim_tool(hands(SW_DOWN, (0.1, 0.6, 1.05), (-0.18, 0.3, 1.0)), (-0.1, 0.85, -0.35)), "LINEAR")
swing.key(14, aim_tool(hands(SW_DOWN, (0.06, 0.56, 0.86), (-0.22, 0.28, 0.98)), (-0.1, 0.5, -0.85)))
swing.key(24, aim_tool(hands(SW_BODY, (0.24, 0.3, 1.1), (0.12, 0.42, 1.06)), (0.1, 0.6, 0.6)))
clips.append(swing)

# --- firedoor: the firehole's door swung open or shut (GDD §12, App. A.5 "keep it hot, keep it shut"): stooped at the
# backhead, the right hand on the leaf's handle at the door's height, hauled across and back off the heat, the left arm up
# against the glare. Played once, as the door moves (SceneArt: whoever's at the firehole when it does).
DOOR_BODY = over(STAND, spine_01=(-14, 0, 0), spine_02=(-10, 0, 0), spine_03=(-6, 0, 0), neck=(16, 0, 0), head=(6, 0, -6),
                 thigh_r=(18, 0, 0), calf_r=(-22, 0, 0), thigh_l=(-6, 0, 0), calf_l=(-8, 0, 0))
HANDLE = (0.12, 0.5, 0.86)
# The left hand braced on the left knee to take the stoop (a Look Review note: the arm up "against the glare" read as a
# hand to the face); the head turns away from the heat as the door comes open.
KNEE_L = (-0.16, 0.3, 0.6)
firedoor = Clip("firedoor", loop=False)
firedoor.key(0, hands(DOOR_BODY, at(HANDLE, dx=-0.02), at(KNEE_L, dz=0.04), grip=85))
firedoor.key(6, hands(over(DOOR_BODY, spine_02=(-12, 0, -4)), HANDLE, KNEE_L, grip=90))
firedoor.key(13, hands(over(DOOR_BODY, spine_02=(-6, 0, 8), spine_03=(-4, 0, 6), head=(2, 0, 14)), at(HANDLE, dx=0.3, dy=-0.12),
                       at(KNEE_L, dz=0.02), grip=90), "LINEAR")
firedoor.key(21, hands(over(DOOR_BODY, spine_02=(-4, 0, 6), head=(4, 0, 12)), at(HANDLE, dx=0.34, dy=-0.2, dz=-0.04), at(KNEE_L, dz=0.04), grip=60))
clips.append(firedoor)

# --- mend: down on one knee at the firebox, the wrench worked in short pulls, the left hand braced on the backhead ---
# Down on the right knee, the left up (it was the right up, and the wrench arm went through it: a Look Review note),
# the eyes on the nut.
MEND = over(STAND, pelvis__loc=(0, -0.02, -0.38), pelvis=(-10, 0, 0), spine_01=(-14, 0, 0), spine_02=(-10, 0, 0),
            spine_03=(-6, 0, 0), thigh_l=(90, 6, 0), calf_l=(-96, 0, 0), foot_l=(10, 0, 6),
            thigh_r=(-10, -4, 0), calf_r=(-110, 0, 0), foot_r=(-50, 0, -6))
NUT, BRACE = (0.14, 0.52, 0.8), (-0.22, 0.52, 0.95)
MEND = look_at(MEND, NUT)
mend = Clip("mend")
for f, a in ((0, 0.0), (7, 1.0), (10, 1.0), (18, 0.0)):
    # A pull down and round on the handle (the nut its pivot), then back for a fresh bite.
    r = (NUT[0] + 0.06 * a, NUT[1] - 0.02 * a, NUT[2] - 0.07 * a)
    # The fist on the wrench's handle a hand back from the nut, the wrench's head on it (the wrist turned to it).
    fist = (r[0], r[1] - 0.2, r[2] + 0.04)
    mend.key(f, aim_tool(arm_to(arm_to(over(MEND, spine_02=(-10 - 3 * a, 0, -2 * a)), "r", fist), "l", BRACE, grip=20),
                         tuple(Vector(r) - Vector(fist))), "LINEAR" if f == 7 else "BEZIER")
mend.close(24)
clips.append(mend)

# --- gap: on the coupling plate between two cars, feet wide, arms out for the sway (GDD §32: the gap's the danger) -----
GAP = over(STAND, pelvis__loc=(0, 0, -0.06), pelvis=(-6, 0, 0), spine_01=(-6, 0, 0), spine_02=(-4, 0, 0), neck=(16, 0, 0),
           head=(10, 0, 0), thigh_r=(-18, -12, 0), calf_r=(-20, 0, 0), foot_r=(12, 0, -10),
           thigh_l=(26, 12, 0), calf_l=(-26, 0, 0), foot_l=(-4, 0, 10))
# The eyes down on the gap ahead (this rig's positive pitch lifts the face: neck 16 had them up on the horizon).
GAP = look_at(GAP, (0, 0.9, 0.0))
gap = Clip("gap")
for f, lean in ((0, 0), (12, 5), (24, 0), (36, -5)):
    gap.key(f, hands(over(GAP, spine_02=(-4, 0, lean), spine_03=(-2, 0, lean * 0.6), head=(GAP["head"][0], 0, -lean)),
                     (0.5, 0.22, 0.98 - lean * 0.01), (-0.5, 0.24, 0.98 + lean * 0.01), fist=False))
gap.close(48)
clips.append(gap)

# --- extinguish: the extinguisher braced on the hip, knob struck, the hose's nozzle aimed down at the fire's foot -----
# (Carried, the body rides 1.1 m up, 0.4 out, Bodies.Carry, drawn there unlifted: the left hand on its handle on top, the right
# on the nozzle.)
EXT_BODY = over(STAND, pelvis=(-4, 0, 6), spine_01=(-6, 0, 4), spine_02=(-6, 0, 0), spine_03=(-4, 0, 0), neck=(14, 0, 0),
                head=(8, 0, 0), thigh_r=(16, 0, 0), calf_r=(-12, 0, 0), thigh_l=(-8, 0, 0), calf_l=(-6, 0, 0))
EXT_HANDLE, EXT_NOZZLE = (-0.1, 0.44, 1.32), (0.26, 0.62, 0.9)
extinguish = Clip("extinguish")
for f, (dx, dz) in ((0, (0.0, 0.0)), (8, (0.06, -0.03)), (16, (0.0, -0.05)), (24, (-0.06, -0.02))):
    extinguish.key(f, hands(EXT_BODY, at(EXT_NOZZLE, dx=dx, dz=dz), EXT_HANDLE, grip=70))
extinguish.close(32)
clips.append(extinguish)

# --- spray: the same, at work on a fire (GreyboxScene sees it going down under them): braced lower, the weight forward
# into the jet, the nozzle kicking back and up with each pulse of it and dragged back down onto the fire's foot; the head
# turned a little away from the heat.
SPRAY_BODY = over(EXT_BODY, pelvis__loc=(0, 0.02, -0.04), spine_01=(-10, 0, 4), spine_02=(-8, 0, 0), neck=(16, 0, 0),
                  head=(6, -10, 0), thigh_r=(26, 0, 0), calf_r=(-24, 0, 0), thigh_l=(-14, 0, 0), calf_l=(-16, 0, 0))
spray = Clip("spray")
for f, kick in ((0, 0.0), (2, 1.0), (6, 0.35), (9, 0.0), (11, 0.9), (15, 0.3), (18, 0.0)):
    body = over(SPRAY_BODY, spine_02=(-8 + 4 * kick, 0, 0), spine_03=(-4 + 3 * kick, 0, 0))
    spray.key(f, hands(body, at(EXT_NOZZLE, dy=-0.06 * kick, dz=0.05 * kick), at(EXT_HANDLE, dy=-0.03 * kick, dz=0.02 * kick), grip=85),
              "LINEAR")
spray.close(18)
clips.append(spray)

# --- lantern: the hand lamp held out low in the right hand, swinging with the step, the left arm free --------------
LAMP_AT = (0.2, 0.34, 0.98)


def lantern_upper(f, side):
    # The lamp swings a little with the step; the free arm swings against the legs, the elbow soft (not a post).
    a = math.cos(2 * math.pi * f / 30)  # +1 at the right heel's strike: the left arm forward
    body = over(STAND, spine_02=(-3, 0, 2 * a))
    p = arm_to(body, "r", at(LAMP_AT, dy=-0.05 * a, dz=-0.02 * abs(a)), grip=90)
    return arm_to(p, "l", (-0.21, 0.03 + 0.17 * a, 0.88 + 0.02 * abs(a)), grip=40, elbow=(0.3, -0.25, 1.1))


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

# --- haul_up: stood at an edge, leant back, hauling a friend up over it hand over hand (App. A.1, A.4's Draggers) -----
HAUL_UP = over(STAND, pelvis__loc=(0, -0.12, -0.3), pelvis=(-30, 0, 0), spine_01=(-16, 0, 0), spine_02=(-8, 0, 0),
               spine_03=(-4, 0, 0), neck=(4, 0, 0), head=(-6, 0, 0),
               thigh_r=(84, -8, 0), calf_r=(-100, 0, 0), foot_r=(18, 0, -6), thigh_l=(70, 8, 0), calf_l=(-84, 0, 0), foot_l=(14, 0, 6))
HAUL_BACK = over(HAUL_UP, pelvis__loc=(0, -0.2, -0.22), pelvis=(-10, 0, 0), spine_01=(4, 0, 0), spine_02=(6, 0, 0),
                 spine_03=(2, 0, 0), neck=(-6, 0, 0), head=(-12, 0, 0), thigh_r=(64, -8, 0), calf_r=(-70, 0, 0),
                 thigh_l=(46, 8, 0), calf_l=(-54, 0, 0))
haul_up = Clip("haul_up")
# Down over the edge for a grip on the collar (both hands, low and out ahead), then the whole body heaves back and up,
# the hands coming in to the chest; again.
haul_up.key(0, hands(HAUL_UP, (0.14, 0.62, 0.42), (-0.14, 0.62, 0.42), grip=90))
haul_up.key(6, hands(HAUL_UP, (0.14, 0.6, 0.38), (-0.14, 0.6, 0.38), grip=90))
haul_up.key(18, hands(HAUL_BACK, (0.14, 0.34, 0.86), (-0.14, 0.34, 0.86), grip=90), "LINEAR")
haul_up.key(24, hands(HAUL_BACK, (0.14, 0.36, 0.84), (-0.14, 0.36, 0.84), grip=90))
haul_up.close(36)
clips.append(haul_up)

# --- the rescue matched to the grab (App. A.1; note 378): CrewActs picks by what has the friend ---------------------------
# pull_mouth: a friend half down the Car Hugger's mouth (D1's swallowed rescue, note 310). Feet braced, the front leg
# out straight against the pull and the back one bent under, the whole body leant far back; both fists on their belt and
# legs at the waist's height, heaved in, then the right hand thrown forward for a fresh grip, hand over hand.
PULL_REACH = over(STAND, pelvis__loc=(0, 0.04, -0.06), pelvis=(-8, 0, 0), spine_01=(-10, 0, 0), spine_02=(-6, 0, 0), neck=(14, 0, 0),
                  head=(4, 0, 0), thigh_r=(30, 0, 0), calf_r=(-24, 0, 0), foot_r=(8, 0, -6), thigh_l=(-16, 0, 0), calf_l=(-22, 0, 0),
                  foot_l=(-10, 0, 6))
PULL_HEAVE = over(STAND, pelvis__loc=(0, -0.2, -0.14), pelvis=(16, 0, 0), spine_01=(14, 0, 0), spine_02=(10, 0, 0), spine_03=(6, 0, 0),
                  neck=(4, 0, 0), head=(-10, 0, 0), thigh_r=(44, 0, 0), calf_r=(-10, 0, 0), foot_r=(-6, 0, -6),
                  thigh_l=(-8, 0, 0), calf_l=(-62, 0, 0), foot_l=(-24, 0, 6))
pull_mouth = Clip("pull_mouth")
pull_mouth.key(0, hands(PULL_REACH, (0.12, 0.64, 0.92), (-0.12, 0.62, 0.9), grip=95))
pull_mouth.key(12, hands(PULL_HEAVE, (0.12, 0.34, 1.0), (-0.12, 0.32, 0.98), grip=95), "LINEAR")
pull_mouth.key(18, hands(over(PULL_HEAVE, spine_02=(12, 0, -4)), (0.12, 0.3, 1.02), (-0.12, 0.3, 1.0), grip=95))
pull_mouth.key(26, hands(over(PULL_REACH, pelvis__loc=(0, -0.08, -0.1), pelvis=(4, 0, 0), spine_01=(0, 0, 0), spine_02=(0, 0, 4)),
                         (0.14, 0.66, 0.94), (-0.12, 0.36, 0.98), grip=60))
pull_mouth.close(36)
clips.append(pull_mouth)

# pry_off: the Tippy Toesie stood behind a friend, its 0.3 m fingers over their mouth (App. A.5's smother). Squared up
# to them, feet wide, both hands up at the face: the fingers gripped and wrenched off to the one side, the body leant into
# it, then back for them and wrenched to the other.
PRY_STAND = over(STAND, pelvis__loc=(0, 0, -0.06), spine_01=(-6, 0, 0), spine_02=(-4, 0, 0), neck=(6, 0, 0), head=(0, 0, 0),
                 thigh_r=(8, -12, 0), calf_r=(-16, 0, 0), foot_r=(4, 0, -10), thigh_l=(8, 12, 0), calf_l=(-16, 0, 0), foot_l=(4, 0, 10))
FACE = (0.0, 0.52, 1.54)
pry_off = Clip("pry_off")
pry_off.key(0, hands(PRY_STAND, at(FACE, dx=0.1), at(FACE, dx=-0.1, dz=-0.02), grip=90))
pry_off.key(8, hands(over(PRY_STAND, spine_02=(-4, 0, 10), spine_03=(-2, 0, 6), head=(0, 0, -6)),
                     at(FACE, dx=0.38, dy=-0.1, dz=-0.18), at(FACE, dx=-0.14, dy=0.04, dz=-0.04), grip=95), "LINEAR")
pry_off.key(14, hands(PRY_STAND, at(FACE, dx=0.1), at(FACE, dx=-0.1), grip=90))
pry_off.key(22, hands(over(PRY_STAND, spine_02=(-4, 0, -10), spine_03=(-2, 0, -6), head=(0, 0, 6)),
                      at(FACE, dx=0.14, dy=0.04, dz=-0.04), at(FACE, dx=-0.38, dy=-0.1, dz=-0.18), grip=95), "LINEAR")
pry_off.close(30)
clips.append(pry_off)

# haul_down: a friend being lifted off the roof (the Whistler's carry, the Choir's seizing): reached up for their legs
# overhead, then the whole weight dropped onto them, knees bending, the fists hauled down to the chest; and up again.
HD_UP = look_at(over(STAND, pelvis__loc=(0, 0, 0.04), spine_01=(4, 0, 0), spine_02=(6, 0, 0), thigh_r=(-4, 0, 0), thigh_l=(10, 0, 0),
                     calf_l=(-8, 0, 0), foot_r=(-14, 0, -6), foot_l=(-10, 0, 6)), (0, 0.8, 2.6))
HD_DOWN = look_at(over(STAND, pelvis__loc=(0, -0.06, -0.26), pelvis=(-6, 0, 0), spine_01=(-8, 0, 0), spine_02=(-4, 0, 0),
                       thigh_r=(56, -6, 0), calf_r=(-84, 0, 0), foot_r=(26, 0, -6), thigh_l=(40, 6, 0), calf_l=(-70, 0, 0), foot_l=(28, 0, 6)),
                  (0, 0.8, 2.4))
haul_down = Clip("haul_down")
haul_down.key(0, hands(HD_UP, (0.16, 0.36, 2.04), (-0.16, 0.32, 1.98), grip=95))
haul_down.key(12, hands(HD_DOWN, (0.2, 0.38, 1.38), (-0.2, 0.36, 1.34), grip=95), "LINEAR")
haul_down.key(18, hands(HD_DOWN, (0.2, 0.38, 1.34), (-0.2, 0.36, 1.32), grip=95))
haul_down.close(32)
clips.append(haul_down)

# --- gap_step: across the coupling plate, a short wide careful step, arms out, eyes down at the gap (GDD §32) ----------
def gap_upper(f, side):
    lean = 4 if side == "r" else -4
    return hands(over(GAP, spine_02=(-6, 0, lean), spine_03=(-4, 0, lean * 0.6), head=(GAP["head"][0], 0, -lean)),
                 (0.48, 0.26, 1.0 - lean * 0.01), (-0.48, 0.28, 1.0 + lean * 0.01), fist=False)


def gap_legs(f, pose):
    # Feet kept wide (a plate's a metre across and it sways): the thighs splayed out on top of the gait's swing.
    pose["thigh_r"] = (pose.get("thigh_r", (0, 0, 0))[0], -10, 0)
    pose["thigh_l"] = (pose.get("thigh_l", (0, 0, 0))[0], 10, 0)
    return pose


clips.append(walking("gap_step", gap_upper, cycle=40, short=0.55, tweak=gap_legs))

# --- drive: at the controls, the regulator under one hand and the brake valve under the other, looking out ahead ----
# (CreatureArt reaches the hands onto the levers where they are, SceneArt.Driving; this is the body and its weight.)
DRIVE = over(STAND, spine_01=(-3, 0, 0), spine_02=(-4, 0, -4), spine_03=(-3, 0, -4), neck=(6, 0, 6), head=(-2, 0, 8),
             thigh_r=(-4, 0, 0), thigh_l=(6, 0, 0), calf_l=(-8, 0, 0))
DRIVE_REG, DRIVE_BRAKE = (-0.18, 0.4, 1.5), (0.34, 0.16, 1.12)
drive = Clip("drive")
drive.key(0, hands(DRIVE, DRIVE_BRAKE, DRIVE_REG, grip=70))
drive.key(40, hands(over(DRIVE, head=(-4, 0, 14), neck=(6, 0, 10)), at(DRIVE_BRAKE, dz=0.01), at(DRIVE_REG, dz=-0.01), grip=70))
drive.key(70, hands(over(DRIVE, head=(0, 0, 2)), DRIVE_BRAKE, DRIVE_REG, grip=70))
drive.close(100)
clips.append(drive)

# --- whistle: the left hand up on the cord's handle and hauled down, the right on the brake, a blast ----------------------
# Out to the left and forward of the head (a Look Review note: the arm went up through the head).
CORD_UP, CORD_DOWN = (-0.3, 0.34, 1.88), (-0.3, 0.36, 1.7)
whistle = Clip("whistle")
whistle.key(0, hands(over(DRIVE, head=(-8, 0, 4)), DRIVE_BRAKE, CORD_UP, grip=90))
whistle.key(5, hands(over(DRIVE, head=(-6, 0, 4), spine_03=(-1, 0, -4)), DRIVE_BRAKE, CORD_DOWN, grip=90), "LINEAR")
whistle.hold(24)
whistle.key(30, hands(over(DRIVE, head=(-8, 0, 4)), DRIVE_BRAKE, CORD_UP, grip=90))
whistle.close(36)
clips.append(whistle)

# --- breaching a Holdout (App. D.7): hold-to-interact loops, interrupted the moment Use is let go ----------------------
# The door's lock (a prison car's hasp, a lockup's padlock) at the waist ahead; the barricade's boards at the chest.
LOCK = (0.06, 0.5, 1.0)
SMASH_BODY = over(STAND, thigh_r=(-12, 0, 0), calf_r=(-8, 0, 0), thigh_l=(20, 0, 0), calf_l=(-18, 0, 0), foot_l=(-6, 0, 6))
smash = Clip("smash")
# Wound up over the right shoulder, brought down two-handed on the hasp, the jar back up the arms, again: 3 a second
# is too fast for a heavy tool, 1.25 reads as weight.
WIND = over(SMASH_BODY, spine_01=(6, 0, -8), spine_02=(8, 0, -14), spine_03=(6, 0, -10), neck=(-4, 0, 0), head=(-10, 0, 12))
STRIKE = over(SMASH_BODY, pelvis__loc=(0, 0.02, -0.06), pelvis=(-10, 0, 8), spine_01=(-18, 0, 6), spine_02=(-20, 0, 8),
              spine_03=(-10, 0, 4), neck=(16, 0, 0), head=(12, 0, -6), thigh_l=(30, 0, 0), calf_l=(-30, 0, 0))
# The bar over the shoulder, then driven down onto the hasp: its head at the lock, the hands a bar's length back from it.
SMASH_WIND = aim_tool(hands(WIND, (0.22, 0.0, 1.92), (0.12, 0.1, 1.84)), (0.1, -0.5, 0.85))
smash.key(0, SMASH_WIND)
smash.key(9, aim_tool(hands(STRIKE, (0.14, 0.5, 1.12), (0.02, 0.44, 1.08)), (-0.05, 0.4, -0.45)), "LINEAR")
smash.key(12, aim_tool(hands(over(STRIKE, spine_02=(-17, 0, 7)), (0.14, 0.46, 1.2), (0.02, 0.4, 1.16)), (-0.04, 0.4, -0.2)))
smash.key(24, SMASH_WIND)
clips.append(smash)

# Pry: the bar's end bitten in behind a board at the chest, both hands on it, the whole weight hung back off it, heaving.
PRY_BODY = over(STAND, pelvis__loc=(0, -0.04, -0.04), pelvis=(-6, 0, 0), spine_01=(-6, 0, 0), spine_02=(-6, 0, 0),
                spine_03=(-4, 0, 0), neck=(10, 0, 0), head=(4, 0, 0), thigh_r=(28, 0, 0), calf_r=(-36, 0, 0), foot_r=(8, 0, -6),
                thigh_l=(-10, 0, 0), calf_l=(-14, 0, 0))
PRY_BACK = over(PRY_BODY, pelvis__loc=(0, -0.2, -0.1), pelvis=(6, 0, 0), spine_01=(14, 0, 0), spine_02=(10, 0, -4),
                spine_03=(6, 0, -4), neck=(-4, 0, 0), head=(-12, 0, 0), thigh_r=(44, 0, 0), calf_r=(-60, 0, 0), foot_r=(16, 0, -6),
                thigh_l=(-24, 0, 0), calf_l=(-8, 0, 0))
pry = Clip("pry")
# The bar run forward from the fists to its claw behind the board (at the chest, 0.75 m ahead), hauled back on.
BOARD = Vector((0.06, 0.78, 1.36))


def pry_key(body, r, l):
    return aim_tool(hands(body, r, l, grip=90), tuple(BOARD - Vector(r)))


pry.key(0, pry_key(PRY_BODY, (0.14, 0.4, 1.3), (0.04, 0.5, 1.33)))
pry.key(14, pry_key(PRY_BACK, (0.14, 0.24, 1.18), (0.04, 0.32, 1.22)), "LINEAR")
pry.key(22, pry_key(over(PRY_BACK, spine_02=(12, 0, 4), spine_03=(8, 0, 4)), (0.16, 0.22, 1.14), (0.06, 0.3, 1.18)))
pry.key(32, pry_key(PRY_BODY, (0.14, 0.38, 1.28), (0.04, 0.48, 1.31)))
pry.close(40)
clips.append(pry)

# Pick: down on a knee at the lock with the repair kit's picks, the left hand steadying the padlock, the right working the
# tension wrench and pick in small twists: quiet, close work, the head down at it.
PICK_BODY = over(STAND, pelvis__loc=(0, -0.02, -0.34), pelvis=(-4, 0, 0), spine_01=(-8, 0, 0), spine_02=(-8, 0, 0),
                 spine_03=(-6, 0, 0), neck=(34, 0, 0), head=(24, 0, 0),
                 thigh_r=(-10, -4, 0), calf_r=(-108, 0, 0), foot_r=(-50, 0, -6), thigh_l=(86, 4, 0), calf_l=(-92, 0, 0), foot_l=(8, 0, 6))
PADLOCK = (0.04, 0.5, 0.96)
# The eyes on the lock (a Look Review note: "looking more up, not down" — this rig's positive pitch lifts the face).
PICK_BODY = look_at(PICK_BODY, at(PADLOCK, dz=-0.02))
pick = Clip("pick")
for f, (twist, dz) in ((0, (0.0, 0.0)), (6, (0.014, 0.006)), (11, (-0.008, 0.0)), (17, (0.016, -0.006)), (24, (0.0, 0.003))):
    pick.key(f, hands(over(PICK_BODY, head=(PICK_BODY["head"][0], 0, PICK_BODY["head"][2] + twist * 300)), at(PADLOCK, dx=0.07 + twist, dy=-0.04, dz=dz),
                      at(PADLOCK, dx=-0.06, dy=0.0, dz=-0.03), grip=60))
pick.close(30)
clips.append(pick)

# --- getup: freed, inside the Holdout, up off the floor (App. D.8: "comes back inside it") -------------------------------
LYING = over(STAND, pelvis__loc=(0, -0.3, -0.62), pelvis=(-50, 0, 0), spine_01=(-16, 0, 0), spine_02=(-10, 0, 0),
             spine_03=(-4, 0, 0), neck=(30, 0, 0), head=(16, 0, 0),
             thigh_r=(120, -6, 0), calf_r=(-140, 0, 0), foot_r=(40, 0, -6), thigh_l=(110, 6, 0), calf_l=(-136, 0, 0), foot_l=(36, 0, 6))
KNEEL = over(STAND, pelvis__loc=(0, -0.06, -0.42), pelvis=(-20, 0, 0), spine_01=(-14, 0, 0), spine_02=(-10, 0, 0),
             spine_03=(-6, 0, 0), neck=(20, 0, 0), head=(8, 0, 0),
             thigh_r=(92, -4, 0), calf_r=(-98, 0, 0), foot_r=(10, 0, -6), thigh_l=(-10, 4, 0), calf_l=(-112, 0, 0), foot_l=(-52, 0, 6))
getup = Clip("getup", loop=False)
getup.key(0, hands(LYING, (0.22, 0.34, 0.12), (-0.24, 0.3, 0.12), fist=False))
getup.key(16, hands(over(LYING, pelvis__loc=(0, -0.2, -0.52), pelvis=(-36, 0, 0)), (0.26, 0.4, 0.06), (-0.24, 0.36, 0.08), fist=False))
getup.key(30, hands(KNEEL, (0.24, 0.42, 0.52), (-0.2, 0.36, 0.5), fist=False))
getup.key(42, hands(over(KNEEL, pelvis__loc=(0, -0.04, -0.2), thigh_r=(60, -4, 0), calf_r=(-60, 0, 0), thigh_l=(10, 4, 0), calf_l=(-40, 0, 0), foot_l=(-10, 0, 6)),
                    (0.22, 0.44, 0.62), (-0.2, 0.3, 0.92), fist=False))
getup.key(56, STAND)
clips.append(getup)

# --- take_down / hang_up: the extinguisher lifted off its wall bracket into both hands, and back (App. C.5) ------------
# (The mount stands on the wall at the left, World.ExtinguisherMount: the crewmate faces the wall, the bracket ahead.)
MOUNT_AT = (0.0, 0.42, 0.62)
take = Clip("take_down")
take.key(0, hands(over(STAND, spine_01=(-12, 0, 0), spine_02=(-10, 0, 0), neck=(16, 0, 0)), at(MOUNT_AT, dx=0.1, dz=0.18), at(MOUNT_AT, dx=-0.1, dz=0.0), grip=60))
take.key(10, hands(over(STAND, spine_01=(-16, 0, 0), spine_02=(-12, 0, 0), neck=(18, 0, 0)), at(MOUNT_AT, dx=0.1, dz=0.3), at(MOUNT_AT, dx=-0.1, dz=0.12), grip=80))
take.key(22, hands(EXT_BODY, EXT_NOZZLE, EXT_HANDLE, grip=70))
take.close(40)
clips.append(take)
# Hung back (come to the bracket already carrying it): lifted up off the hip to the bracket, pushed home onto it, the hands
# let go and come away, and stood back up off it (1.3 s, once; it plays on through the sim's drop, CreatureArt.Crewmate).
hang = Clip("hang_up")
hang.key(0, hands(EXT_BODY, EXT_NOZZLE, EXT_HANDLE, grip=70))
hang.key(12, hands(over(STAND, spine_01=(-14, 0, 0), spine_02=(-10, 0, 0), neck=(18, 0, 0)), at(MOUNT_AT, dx=0.1, dz=0.34), at(MOUNT_AT, dx=-0.1, dz=0.16), grip=80))
hang.key(20, hands(over(STAND, spine_01=(-12, 0, 0), spine_02=(-10, 0, 0), neck=(16, 0, 0)), at(MOUNT_AT, dx=0.1, dz=0.18), at(MOUNT_AT, dx=-0.1, dz=0.0), grip=60))
hang.key(27, hands(over(STAND, spine_01=(-8, 0, 0), spine_02=(-6, 0, 0), neck=(12, 0, 0)), at(MOUNT_AT, dx=0.16, dy=-0.1, dz=0.12), at(MOUNT_AT, dx=-0.16, dy=-0.1, dz=0.0), fist=False))
hang.key(40, STAND)
clips.append(hang)

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

# --- hurry: running under stress (GDD §31: "hurried under stress"), hunched, head up to see ahead, arms pumping ----------
def hurry_upper(f, side):
    # The arm opposite the leading leg forward, the elbows bent near square and driven back past the hips, the fists
    # close in by the body (a Look Review note: the arms read wrong, flung out); pitched into it, rocking with the step.
    a = math.cos(2 * math.pi * f / 18)  # +1 at the right heel's strike: the right arm back
    def hand(k):
        # Driven back, the fist goes out past the coat's skirt (0.22 m), not into it (a Look Review note).
        return (0.26 + 0.04 * max(0.0, k), 0.06 - 0.22 * k, 1.12 + 0.1 * max(0.0, -k))
    body = over(STAND, spine_01=(-12, 0, 0), spine_02=(-10, 0, 3 * a), spine_03=(-8, 0, 0), neck=(18, 0, 0), head=(4, 0, 0))
    r = hand(a)
    l = hand(-a)
    p = arm_to(body, "r", r, grip=80, elbow=(0.3, -0.35, 0.95))
    return arm_to(p, "l", (-l[0], l[1], l[2]), grip=80, elbow=(0.3, -0.35, 0.95))


clips.append(walking("hurry", hurry_upper, cycle=18, short=1.0))

# --- stagger: a blow taken (App. C.2 "hit and stagger"): rocked back a step, arms flung up, and back on the feet ----------
STAG = over(STAND, pelvis__loc=(0, -0.08, -0.04), pelvis=(10, 0, -6), spine_01=(12, 0, -4), spine_02=(10, 0, -6),
            spine_03=(8, 0, -4), neck=(-10, 0, 0), head=(-14, 0, 8), thigh_r=(-22, 0, 0), calf_r=(-10, 0, 0),
            thigh_l=(20, 0, 0), calf_l=(-34, 0, 0))
stagger = Clip("stagger", loop=False)
stagger.key(0, STAND)
stagger.key(4, hands(STAG, (0.34, 0.2, 1.5), (-0.4, 0.1, 1.42), fist=False), "LINEAR")
stagger.key(10, hands(over(STAG, spine_02=(6, 0, -3), head=(-6, 0, 4)), (0.3, 0.24, 1.2), (-0.34, 0.16, 1.15), fist=False))
stagger.key(20, STAND)
clips.append(stagger)

# --- shuffle: a freed prisoner's walk (App. D.8, the checklist's survivor-prisoner; note 407). Years in irons: short,
# flat, dragging steps that barely clear the ground, hunched over them, the head down, the hands held together low in
# front where the cuffs kept them, the shoulders rounded in; it nods a little with each step.
def shuffle_upper(f, side):
    nod = 3 if side == "r" else -3
    body = over(STAND, pelvis__loc=(0, 0, -0.04), spine_01=(-16, 0, 0), spine_02=(-16, 0, nod * 0.5), spine_03=(-12, 0, 0), neck=(30, 0, 0),
                head=(12 + abs(nod), 0, -nod), clavicle_r=(0, 2, 14), clavicle_l=(0, 2, -14))
    return hands(body, (0.07, 0.34, 0.9 + 0.01 * nod), (-0.07, 0.35, 0.9 - 0.01 * nod), grip=60)


def shuffle_legs(f, pose):
    # The feet barely lifted: the passing knee's bend and the toe-off halved, the knees kept soft.
    for k in ("calf_r", "calf_l"):
        x = pose.get(k, (0, 0, 0))
        pose[k] = (max(x[0] * 0.5, -40) - 6, x[1], x[2])
    for k in ("foot_r", "foot_l"):
        x = pose.get(k, (0, 0, 0))
        pose[k] = (x[0] * 0.4, x[1], x[2])
    return pose


clips.append(walking("shuffle", shuffle_upper, cycle=40, short=0.42, tweak=shuffle_legs))

# --- jump: off the roof on a jump, between cars (the checklist's crew-gap: "a jump between roofs"; note 375). Played once
# from the spring (CrewActs: rising), held over the top (SceneArt) until they're falling or down. The push off the back
# foot with the arms swung up and forward, then gathered: both knees drawn up under the chest, leant into it, the arms
# reaching ahead for the far roof's edge, the eyes on it.
JUMP_SPRING = over(STAND, pelvis__loc=(0, 0.04, 0.02), pelvis=(-8, 0, 0), spine_01=(-10, 0, 0), spine_02=(-8, 0, 0), spine_03=(-4, 0, 0),
                   neck=(10, 0, 0), head=(4, 0, 0), thigh_r=(48, 0, 0), calf_r=(-72, 0, 0), foot_r=(16, 0, -6),
                   thigh_l=(-24, 0, 0), calf_l=(-18, 0, 0), foot_l=(-34, 0, 6))
JUMP_TUCK = over(STAND, pelvis__loc=(0, 0.02, 0.1), pelvis=(-14, 0, 0), spine_01=(-14, 0, 0), spine_02=(-10, 0, 0), spine_03=(-6, 0, 0),
                 neck=(14, 0, 0), head=(6, 0, 0), thigh_r=(74, 6, 0), calf_r=(-104, 0, 0), foot_r=(20, 0, -6),
                 thigh_l=(58, -6, 0), calf_l=(-96, 0, 0), foot_l=(14, 0, 6))
JUMP_TUCK = look_at(JUMP_TUCK, (0, 3.0, 0.9))
jump = Clip("jump", loop=False)
jump.key(0, hands(over(STAND, pelvis__loc=(0, 0, -0.08), thigh_r=(20, 0, 0), calf_r=(-36, 0, 0), thigh_l=(14, 0, 0), calf_l=(-30, 0, 0)),
                  (0.32, -0.24, 0.98), (-0.32, -0.24, 0.98), fist=False))
jump.key(5, hands(JUMP_SPRING, (0.3, 0.42, 1.62), (-0.3, 0.38, 1.56), fist=False), "LINEAR")
jump.key(12, hands(JUMP_TUCK, (0.34, 0.6, 1.36), (-0.34, 0.56, 1.32), fist=False))
jump.key(22, hands(over(JUMP_TUCK, thigh_r=(66, 6, 0), calf_r=(-88, 0, 0), thigh_l=(62, -6, 0), calf_l=(-90, 0, 0)),
                   (0.36, 0.62, 1.3), (-0.36, 0.6, 1.28), fist=False))
clips.append(jump)

# --- stumble: on a car straining on a bend taken too fast (App. F.1's overspeed telegraph: "the cars straining and
# leaning"; note 375), past halfway to off: thrown towards the outside of the bend and caught on a foot stepped wide, the
# arms flung out for balance, then back over the other way as the car snatches; low, knees soft, the eyes on the roof.
STUM = over(STAND, pelvis__loc=(0, 0, -0.1), spine_01=(-8, 0, 0), spine_02=(-6, 0, 0), neck=(18, 0, 0), head=(8, 0, 0),
            thigh_r=(14, -14, 0), calf_r=(-30, 0, 0), foot_r=(8, 0, -10), thigh_l=(14, 14, 0), calf_l=(-30, 0, 0), foot_l=(8, 0, 10))


def stumble_key(lean, step):
    # Thrown towards `lean` (+1 the right): the body tipped over that way and the head kept level against it, the foot
    # on that side stepped out wide and taking the weight (knee buckled), the other leg light and long; the arm on the
    # thrown side low and out to catch, the other high and wide.
    side, other = ("r", "l") if lean > 0 else ("l", "r")
    body = over(STUM, pelvis__loc=(0.1 * lean * step, 0, -0.13), pelvis=(0, 0, 12 * lean), spine_01=(-8, 0, 14 * lean),
                spine_02=(-6, 0, 18 * lean), spine_03=(-4, 0, 10 * lean), head=(8, 0, -24 * lean),
                **{f"thigh_{side}": (18, -36 * step, 0) if side == "r" else (18, 36 * step, 0), f"calf_{side}": (-50, 0, 0),
                   f"thigh_{other}": (6, -4, 0) if other == "r" else (6, 4, 0), f"calf_{other}": (-12, 0, 0)})
    low = (0.62 * lean, 0.12, 0.86)
    high = (-0.56 * lean, 0.06, 1.62)
    r, l = (low, high) if lean > 0 else (high, low)
    return hands(body, r, l, fist=False)


stumble = Clip("stumble")
stumble.key(0, stumble_key(1, 1.0))
stumble.key(9, hands(STUM, (0.5, 0.2, 1.1), (-0.5, 0.2, 1.1), fist=False))
stumble.key(16, stumble_key(-1, 0.8))
stumble.key(25, hands(over(STUM, pelvis__loc=(0, 0, -0.14)), (0.48, 0.24, 1.0), (-0.52, 0.18, 1.14), fist=False))
stumble.close(34)
clips.append(stumble)

# --- throw: a ground switch lever by the line (spec C.6's junctions), gripped low and heaved up and over ----------------
THROW_LOW = over(STAND, pelvis__loc=(0, -0.06, -0.12), pelvis=(-14, 0, 0), spine_01=(-18, 0, 0), spine_02=(-14, 0, 0),
                 spine_03=(-8, 0, 0), neck=(20, 0, 0), head=(10, 0, 0), thigh_r=(36, 0, 0), calf_r=(-50, 0, 0), foot_r=(14, 0, -6),
                 thigh_l=(-6, 0, 0), calf_l=(-20, 0, 0))
THROW_UP = over(STAND, pelvis__loc=(0, -0.1, -0.03), pelvis=(6, 0, 0), spine_01=(8, 0, 0), spine_02=(6, 0, -4),
                spine_03=(4, 0, -4), neck=(-2, 0, 0), head=(-6, 0, 0), thigh_r=(24, 0, 0), calf_r=(-26, 0, 0),
                thigh_l=(-14, 0, 0), calf_l=(-8, 0, 0))
throw = Clip("throw")
throw.key(0, hands(THROW_LOW, (0.22, 0.5, 0.56), (0.06, 0.5, 0.6), grip=90))
throw.key(14, hands(THROW_UP, (0.2, 0.3, 1.06), (0.04, 0.3, 1.1), grip=90), "LINEAR")
throw.hold(26)
throw.close(40)
clips.append(throw)

# --- chute: the coaling chute's lever (spec D.3), hauled down overhead on its chain, the weight hung off it ------------
CHUTE = over(STAND, pelvis__loc=(0, -0.04, 0), spine_01=(4, 0, 0), spine_02=(4, 0, 0), neck=(-14, 0, 0), head=(-14, 0, 0))
CHUTE_DOWN = over(CHUTE, pelvis__loc=(0, -0.1, -0.1), spine_01=(8, 0, 0), spine_02=(6, 0, 0), neck=(-4, 0, 0), head=(-4, 0, 0),
                  thigh_r=(26, 0, 0), calf_r=(-38, 0, 0), thigh_l=(16, 0, 0), calf_l=(-30, 0, 0))
chute = Clip("chute")
chute.key(0, hands(CHUTE, (0.08, 0.32, 2.0), (-0.04, 0.32, 1.9), grip=90))
chute.key(12, hands(CHUTE_DOWN, (0.08, 0.34, 1.5), (-0.04, 0.34, 1.42), grip=90), "LINEAR")
chute.hold(28)
chute.close(40)
clips.append(chute)

# --- spout: a grain or water spout's handle overhead (the depot's spout, spec D.3), swung round over the car -------------
SPOUT = over(STAND, spine_01=(4, 0, 0), spine_02=(4, 0, 0), neck=(-16, 0, 0), head=(-12, 0, 0),
             thigh_r=(-8, 0, 0), thigh_l=(10, 0, 0), calf_l=(-10, 0, 0))
spout = Clip("spout")
for f, turn in ((0, 0), (20, 1), (40, 0), (60, -1)):
    spout.key(f, hands(over(SPOUT, pelvis=(0, 0, 8 * turn), spine_02=(4, 0, 10 * turn), spine_03=(2, 0, 8 * turn)),
                       (0.16 + 0.14 * turn, 0.44, 1.78), (-0.16 + 0.14 * turn, 0.44, 1.76), grip=85))
spout.close(80)
clips.append(spout)

# --- shoulder, shoulder_walk: a body over the left shoulder (App. C.4 "a body over the shoulder"), a fireman's carry --------
# The sim rides the body's hips on the left shoulder and its knees at the chest (Bodies.Shoulder); the left arm wrapped
# round the backs of the thighs holds them there, the right free. Bent forward under the dead weight, the left shoulder
# dropped under it, the steps short and heavy.
SHOULDER_BODY = over(STAND, spine_01=(-8, 0, 3), spine_02=(-8, 0, 5), spine_03=(-6, 0, 4), neck=(10, 0, -4), head=(0, 0, -6),
                     clavicle_l=(0, 0, -8))
HUG = (-0.08, 0.34, 1.28)
shoulder = Clip("shoulder")
shoulder.key(0, hands(SHOULDER_BODY, (0.26, 0.06, 0.86), HUG, grip=85))
shoulder.key(30, hands(over(SHOULDER_BODY, spine_02=(-9, 0, 6), head=(2, 0, -4)), (0.26, 0.06, 0.85), at(HUG, dz=-0.01), grip=85))
shoulder.close(60)
clips.append(shoulder)


def shoulder_upper(f, side):
    # Only the right arm swings; the body rocks onto each step under the load.
    swing = 0.12 if side == "r" else -0.12
    body = over(SHOULDER_BODY, spine_02=(-8, 0, 5 + (2 if side == "r" else -2)))
    return hands(body, (0.26, 0.06 + swing, 0.88), HUG, grip=85)


clips.append(walking("shoulder_walk", shoulder_upper, cycle=36, short=0.75))

# --- cradle, cradle_walk: the child in the arms (App. C.4), clinging to them (soot_child's clutch): the right forearm under
# its seat, the left hand across its back, leant back a little against it, the head bent to it.
CRADLE_BODY = over(STAND, spine_01=(4, 0, 0), spine_02=(4, 0, 0), spine_03=(2, 0, 0), neck=(14, 0, 4), head=(10, 0, 10))
SEAT, BACK = (0.06, 0.3, 0.98), (-0.04, 0.42, 1.3)
cradle = Clip("cradle")
cradle.key(0, hands(CRADLE_BODY, SEAT, BACK, grip=40))
cradle.key(36, hands(over(CRADLE_BODY, spine_02=(5, 0, 2), head=(12, 0, 8)), at(SEAT, dz=-0.01), at(BACK, dx=0.02), grip=40))
cradle.close(72)
clips.append(cradle)


def cradle_upper(f, side):
    bob = -0.015 if f % 15 < 5 else 0.0
    return hands(over(CRADLE_BODY, spine_02=(4, 0, 2 if side == "r" else -2)), at(SEAT, dz=bob), at(BACK, dz=bob), grip=40)


clips.append(walking("cradle_walk", cradle_upper, cycle=32, short=0.85))

# --- climb, climb_carry: up a ladder's rungs (TrainKit.RungPitch, 0.3 m), the hands and feet on them (Look Review notes:
# "make sure this follows ladder pacing"; climb_carry's free hand went up through the head). One cycle is two rungs
# climbed, 0.6 m: a hand or foot on a rung holds it, and so sinks down the body as the body rises past it, then lets go
# and goes up past the other for the next rung but one. SceneArt plays these by how far up the ladder they are, not by
# the clock, so the hands stay on the rungs at any speed and stop when the climber does. Right hand with left foot, then
# left hand with right foot. The root doesn't rise (the sim moves the climber); the ladder stands 0.3 m in front.
CLIMB_CYCLE, CLIMB_RISE = 40, 0.6
C_BODY = over(STAND, pelvis__loc=(0, 0.04, 0), pelvis=(-4, 0, 0), spine_01=(-4, 0, 0), spine_02=(-2, 0, 0), spine_03=(2, 0, 0),
              neck=(-6, 0, 0), head=(-10, 0, 0))


def rung_phase(f, offset, hold=0.5):
    """Where a limb is in its own cycle at frame `f`: (holding, t) — holding a rung (sinking, t 0..1) or moving up to the
    next (t 0..1). It holds for `hold` of the cycle."""
    u = ((f + offset) % CLIMB_CYCLE) / CLIMB_CYCLE
    return (True, u / hold) if u < hold else (False, (u - hold) / (1 - hold))


def limb_height(top, f, offset, hold=0.5):
    """The limb's height in the body's frame: down from `top` while it holds (the body rising past the rung), back up to
    it while it moves."""
    holding, t = rung_phase(f, offset, hold)
    sink = CLIMB_RISE * hold
    if holding:
        return top - sink * t, 0.0
    e = t * t * (3 - 2 * t)
    return top - sink * (1 - e), math.sin(math.pi * t)  # (height, how far through its move: 0 on a rung, 1 mid-air)


def climb_pose(f, carry=False):
    p = dict(CC if carry else C_BODY)
    hands_ = []
    for side, k, off in (("r", 1, 0), ("l", -1, CLIMB_CYCLE // 2)):
        if carry and side == "l":
            continue
        hold = 0.65 if carry else 0.5
        z, lift = limb_height(1.94, f, off, hold)
        # One-handed, the hand goes up out at the side and well forward, clear of the head and the load.
        x, y = (0.2, 0.3 - 0.06 * lift) if not carry else (0.22, 0.36 - 0.04 * lift)
        hands_.append((side, (k * x, y, z + 0.03 * lift)))
    for side, wrist in hands_:
        p = arm_to(p, side, wrist, grip=90 if wrist[1] > 0.27 or carry else 30, elbow=(0.6, 0.05, 1.2) if carry else (0.5, 0.05, 1.15))
    if carry:
        p = arm_to(p, "l", HUG, grip=85)
    for side, k, off in (("l", -1, 0), ("r", 1, CLIMB_CYCLE // 2)):
        # The feet on rungs a whole number of pitches under the hands' (1.5 m: five rungs).
        z, lift = limb_height(0.44, f, off)
        p = leg_to(p, side, (k * 0.16, 0.2 - 0.08 * lift, z + 0.04 * lift), knee=(0.32, 0.7, 0.7))
        p[f"foot_{side}"] = (6 - 10 * lift, 0, 0)
    if carry:
        p = dict(p, neck=CC_LOOK["neck"], head=CC_LOOK["head"])
    # The weight goes onto the side whose hand has just taken a rung.
    sway = math.sin(2 * math.pi * f / CLIMB_CYCLE)
    p["spine_02"] = (p.get("spine_02", (0, 0, 0))[0], 0, 3 * sway)
    return p


climb = Clip("climb")
for f in range(0, CLIMB_CYCLE, 4):
    climb.key(f, climb_pose(f), "LINEAR")
climb.close(CLIMB_CYCLE)
clips.append(climb)

# climb_carry: the solo remainer (App. D.9, note 181), a body over the left shoulder: the left arm hooked over its legs, the
# right hand alone on the rungs, out to the side clear of the head and the load, holding longer (one hand to pull on).
CC = over(STAND, spine_01=(-6, 0, 0), spine_02=(-8, 0, 4), spine_03=(-6, 0, 6), neck=(-4, 0, 0), head=(-6, 0, 0),
          clavicle_l=(0, 0, -8))
CC_LOOK = look_at(CC, (0.1, 0.45, 2.2))
climb_carry = Clip("climb_carry")
for f in range(0, CLIMB_CYCLE, 4):
    climb_carry.key(f, climb_pose(f, carry=True), "LINEAR")
climb_carry.close(CLIMB_CYCLE)
clips.append(climb_carry)

# --- shovel (overrides crew.glb's: a Look Review note, "looks a bit off"): the fireman's swing. Feet wide and the knees
# well bent, the blade driven flat into the coal on the floor ahead and to the right, the back hand low at the hip; the
# load lifted on the legs, not the back; turned left and the blade thrown forward through the firehole at the waist, the
# back hand driving it; back round for the next. The right fist holds the handle by the blade (crew.py's shovel part,
# bound to the hand: its handle runs through the fist along the bind pose's +Y, the D-grip 0.62 m back), the left hand
# put on the D-grip wherever that ends up. 48 frames, as crew.py's.
SHOVEL_G = Vector((0.80, 0.0, 1.425))
SHOVEL_BACK = Vector((0, -0.62, 0))
WRIST_R = sk["hand_r"].head.copy()


def with_shovel(pose, wrist, direction):
    p = arm_to(pose, "r", wrist, elbow=(0.4, 0.0, 0.7))
    d = Vector(direction).normalized()
    R = Vector((0, 1, 0)).rotation_difference(d)
    p["hand_r"] = rig.world_rotation(sk, p, "lowerarm_r").inverted() @ R
    w = rig.pose_points(sk, p, [("hand_r", "head")])[0]
    grip_end = w + R @ (SHOVEL_G - WRIST_R) + R @ SHOVEL_BACK
    return arm_to(p, "l", grip_end - (grip_end - w).normalized() * 0.05, elbow=(0.3, -0.1, 0.9))


# Down into the scoop: the knees deep, the back flat over them, so the lower hand reaches the shaft near the blade.
SH_LOW = over(STAND, pelvis__loc=(0, -0.12, -0.24), pelvis=(-22, 0, -14), spine_01=(-18, 0, -6), spine_02=(-14, 0, -4),
              spine_03=(-8, 0, -2), thigh_r=(70, -10, 0), calf_r=(-100, 0, 0), foot_r=(30, 0, -10),
              thigh_l=(50, 12, 0), calf_l=(-80, 0, 0), foot_l=(30, 0, 10))
SH_LOW = look_at(SH_LOW, (0.3, 1.0, 0.0))
SH_LIFT = over(SH_LOW, pelvis__loc=(0, -0.06, -0.1), pelvis=(-10, 0, -8), spine_01=(-10, 0, -4), spine_02=(-8, 0, -2), spine_03=(-4, 0, 0),
               thigh_r=(34, -10, 0), calf_r=(-46, 0, 0), foot_r=(12, 0, -10),
               thigh_l=(20, 12, 0), calf_l=(-34, 0, 0), foot_l=(14, 0, 10))
SH_LIFT = look_at(SH_LIFT, (0.1, 1.5, 0.9))
SH_THROW = over(SH_LIFT, pelvis=(-8, 0, 14), spine_01=(-8, 0, 10), spine_02=(-10, 0, 10), spine_03=(-8, 0, 8),
                thigh_l=(36, 12, 0), calf_l=(-42, 0, 0), thigh_r=(10, -10, 0), calf_r=(-26, 0, 0))
SH_THROW = look_at(SH_THROW, (0.0, 1.5, 0.9))
shovel_clip = Clip("shovel")
# The shaft runs across the body as a fireman holds it: the D-grip by the left hip, the right hand down it toward the
# blade, out ahead and to the right (Look Review notes: with the shaft straight ahead the D-grip was behind the right hip
# and the left arm went across the body and through the legs to it). Each key puts the right fist where the arm reaches
# and the D-grip by the left hip; the shaft runs between them (0.62 m apart, crew.py's shovel) and on to the blade.
def shovel_key(body, fist, d_grip):
    return with_shovel(body, fist, tuple(Vector(fist) - Vector(d_grip)))


shovel_clip.key(0, shovel_key(SH_LOW, (0.22, 0.54, 0.58), (-0.28, 0.24, 0.82)))
shovel_clip.key(7, shovel_key(over(SH_LOW, pelvis__loc=(0, -0.08, -0.25)), (0.24, 0.6, 0.54), (-0.24, 0.3, 0.78)), "LINEAR")
shovel_clip.key(18, shovel_key(SH_LIFT, (0.2, 0.52, 0.88), (-0.3, 0.22, 0.98)))
shovel_clip.key(26, shovel_key(SH_THROW, (0.04, 0.7, 0.98), (-0.3, 0.24, 0.92)), "LINEAR")
shovel_clip.hold(31)
shovel_clip.close(48)
clips.append(shovel_clip)

# --- held: one per GRAB (App. A.1), so who's got them reads from across the car ---------------------------------------
# Hanging over the edge (the Draggers): both hands clawing at the roof's edge over them, the legs kicking below it.
held_hang = Clip("held_hang")
HANG = over(STAND, pelvis__loc=(0, 0, -0.05), spine_01=(4, 0, 0), neck=(-20, 0, 0), head=(-18, 0, 0))
for f, k in ((0, 1), (9, -1), (18, 1), (27, -1)):
    held_hang.key(f, hands(over(HANG, thigh_r=(20 * k, 0, 0), calf_r=(-30, 0, 0), thigh_l=(-14 * k, 0, 0), calf_l=(-40, 0, 0)),
                           (0.18, 0.28, 2.02 + 0.04 * k), (-0.18, 0.3, 2.0 - 0.04 * k), grip=95), "LINEAR")
held_hang.close(36)
clips.append(held_hang)
# In the mouth (the Car Hugger): bent double, the head and shoulders gone into it, the arms shoving back off it, the legs out.
held_mouth = Clip("held_mouth")
MOUTH = over(STAND, pelvis__loc=(0, 0.1, -0.1), pelvis=(-30, 0, 0), spine_01=(-24, 0, 0), spine_02=(-20, 0, 0),
             spine_03=(-16, 0, 0), neck=(10, 0, 0), thigh_r=(-12, 0, 0), calf_r=(-6, 0, 0), thigh_l=(18, 0, 0), calf_l=(-24, 0, 0))
for f, k in ((0, 1), (6, -1), (12, 1), (18, -1)):
    held_mouth.key(f, hands(over(MOUTH, spine_02=(-20, 0, 6 * k)), (0.32, 0.62, 1.2 + 0.05 * k), (-0.32, 0.6, 1.2 - 0.05 * k), fist=False), "LINEAR")
held_mouth.close(24)
clips.append(held_mouth)
# Carried off (the Whistler): hoisted off their feet by the hooks under their arms (whistler.py carry: the armpits at its
# CARRY_UNDERARM, 1.42 m, so the body's lifted 0.1 m), the shoulders forced up, the hands clawing back at the forelegs
# either side of the head, the legs swept back off the ground by the run and kicking; the head jerked about.
held_carried = Clip("held_carried")
LIFT = over(STAND, pelvis__loc=(0, 0, 0.1), pelvis=(6, 0, 0), spine_01=(6, 0, 0), spine_02=(4, 0, 0), neck=(-8, 0, 0),
            head=(-14, 0, 0), clavicle_r=(0, 0, 24), clavicle_l=(0, 0, -24))
for f, k in ((0, 1), (6, -1), (12, 1), (18, -1)):
    held_carried.key(f, hands(over(LIFT, head=(-14, 0, 14 * k), thigh_r=(-14 + 10 * k, 0, 0), calf_r=(-30 - 14 * k, 0, 0),
                                   foot_r=(46, 0, -6), thigh_l=(-14 - 10 * k, 0, 0), calf_l=(-30 + 14 * k, 0, 0), foot_l=(46, 0, 6)),
                              (0.27, -0.06 + 0.04 * k, 1.56), (-0.27, -0.06 - 0.04 * k, 1.58), grip=95))
held_carried.close(24)
clips.append(held_carried)
# Mouth covered (Tippy Toesie): both hands up at the mask, clawing at what's over it, the knees going.
held_cover = Clip("held_cover")
COVER = over(STAND, pelvis__loc=(0, -0.04, -0.08), spine_01=(8, 0, 0), spine_02=(8, 0, 0), neck=(-8, 0, 0), head=(-16, 0, 0),
             thigh_r=(20, 0, 0), calf_r=(-34, 0, 0), thigh_l=(18, 0, 0), calf_l=(-30, 0, 0))
for f, k in ((0, 1), (7, -1), (14, 1), (21, -1)):
    held_cover.key(f, hands(over(COVER, head=(-16, 0, 8 * k)), (0.08, 0.2, 1.62 + 0.02 * k), (-0.08, 0.2, 1.62 - 0.02 * k), fist=False))
held_cover.close(28)
clips.append(held_cover)
# Tongue-frozen (the Ribbits): caught mid-stride and stopped dead, stiff as a post, one foot still off the boards, the
# arms half up where they were, the head back; a tremor through it and nothing else (they can still talk).
held_frozen = Clip("held_frozen")
FROZEN = over(STAND, pelvis__loc=(0, 0, 0.02), spine_01=(8, 0, -4), spine_02=(8, 0, -4), spine_03=(4, 0, 0), neck=(-14, 0, 0),
              head=(-22, 0, 10), thigh_r=(-14, 0, 0), calf_r=(-4, 0, 0), foot_r=(-6, 0, -6),
              thigh_l=(34, 0, 0), calf_l=(-58, 0, 0), foot_l=(-24, 0, 6))
for f, k in ((0, 1), (2, -1), (4, 1), (6, -1)):
    held_frozen.key(f, hands(over(FROZEN, head=(-22, 0, 10 + 1.5 * k)), (0.42, 0.3, 1.36 + 0.006 * k), (-0.36, 0.12, 1.12 - 0.006 * k),
                             fist=False), "LINEAR")
held_frozen.close(8)
clips.append(held_frozen)
# Pinned and drained (a Soot Child): down on the back, the arms pushing up weakly at what's on the chest, slowing.
held_pinned = Clip("held_pinned")
# On the back (a Look Review note: it lay face down with the face through the floor and the legs under it): the head behind
# the feet, the chin lifted off the boards, the knees up a little.
PINNED = over(STAND, pelvis__loc=(0, 0.15, -0.84), pelvis=(84, 0, 0), spine_01=(4, 0, 0), spine_02=(2, 0, 0), spine_03=(0, 0, 0),
              neck=(30, 0, 0), head=(14, 0, 0), thigh_r=(24, -6, 0), calf_r=(-46, 0, 0), foot_r=(-20, 0, -6),
              thigh_l=(8, 6, 0), calf_l=(-16, 0, 0), foot_l=(-20, 0, 6))
for f, k in ((0, 1), (20, -1)):
    held_pinned.key(f, hands(PINNED, (0.16, -0.2 + 0.06 * k, 0.42), (-0.16, -0.2 - 0.06 * k, 0.4), fist=False))
held_pinned.close(40)
clips.append(held_pinned)
# Seized (the Choir): hauled up onto the toes by what's got them, the arms clamped to the sides, the back arched and the
# head thrown back, jerked about in it, too fast (the monsters' pops, CONSTANT).
held_seized = Clip("held_seized")
SEIZED = over(STAND, pelvis__loc=(0, 0, 0.09), pelvis=(8, 0, 0), spine_01=(14, 0, 0), spine_02=(14, 0, 0), spine_03=(10, 0, 0),
              neck=(-18, 0, 0), head=(-34, 0, 0), thigh_r=(-4, 0, 0), calf_r=(-2, 0, 0), foot_r=(-38, 0, -6),
              thigh_l=(-4, 0, 0), calf_l=(-2, 0, 0), foot_l=(-38, 0, 6))
for f, k in ((0, 1), (5, -1), (13, 1), (17, -1)):
    held_seized.key(f, hands(over(SEIZED, pelvis=(8, 0, 6 * k), spine_02=(14, 0, -7 * k), head=(-34, 0, 12 * k)),
                             (0.22, 0.06, 0.98), (-0.22, 0.06, 0.98)), "CONSTANT" if f in (5, 17) else "BEZIER")
held_seized.close(24)
clips.append(held_seized)

# Dragged (the Passenger): on the back, feet first, the hands clawing back over the head at the boards for a hold.
held_dragged = Clip("held_dragged")
for f, k in ((0, 1), (10, -1)):
    held_dragged.key(f, hands(over(PINNED, neck=(34, 0, 0), head=(16, 0, 8 * k), thigh_r=(4, -4, 0), calf_r=(-6, 0, 0),
                                   thigh_l=(2, 4, 0), calf_l=(-4, 0, 0)),
                              (0.22, -0.82 + 0.1 * k, 0.1), (-0.22, -0.82 - 0.1 * k, 0.1), grip=90))
held_dragged.close(20)
clips.append(held_dragged)

# --- reload: the cannon's three reload steps from the seat (enemies.json gun reloadSteps, 1.5 s each), four beats in all:
# the powder bag shoved in the breech, the shot rammed home, the vent primed; then the gunner's own pose is the fourth, ready.
# Not looped: SceneArt plays it at (steps done + this step's progress) x 1.5 s.
reload_ = Clip("reload", loop=False)
LEAN = over(SEATED, spine_01=(-14, 0, 0), spine_02=(-12, 0, 0), spine_03=(-8, 0, 0), neck=(16, 0, 0))
reload_.key(0, hands(SEATED, WHEEL_KNOB, TILLER))
reload_.key(10, hands(LEAN, (0.18, 0.5, PAN + 0.36), (-0.02, 0.5, PAN + 0.38), grip=60))
reload_.key(30, hands(over(LEAN, spine_02=(-16, 0, 0)), (0.18, 0.76, PAN + 0.4), (-0.02, 0.76, PAN + 0.42), grip=60), "LINEAR")
reload_.key(45, hands(SEATED, (0.24, 0.3, PAN + 0.5), (-0.06, 0.3, PAN + 0.52), grip=90))
# (The ram at the chest, the staff run out under the chin: at the face, the rammer went back through the head. A Look
# Review note.)
reload_.key(58, hands(LEAN, (0.2, 0.56, PAN + 0.42), (0.04, 0.5, PAN + 0.4), grip=90))
reload_.key(70, hands(over(LEAN, spine_01=(-20, 0, 0)), (0.2, 0.86, PAN + 0.42), (0.04, 0.8, PAN + 0.4), grip=90), "LINEAR")
reload_.key(80, hands(LEAN, (0.2, 0.56, PAN + 0.42), (0.04, 0.5, PAN + 0.4), grip=90))
reload_.key(90, hands(SEATED, WHEEL_KNOB, TILLER))
reload_.key(102, hands(over(SEATED, head=(10, 0, 0), neck=(20, 0, 0)), (0.06, 0.42, PAN + 0.62), TILLER, grip=40))
reload_.key(112, hands(over(SEATED, head=(10, 0, 0), neck=(20, 0, 0)), (0.06, 0.4, PAN + 0.58), TILLER, grip=40), "LINEAR")
reload_.key(122, hands(over(SEATED, head=(10, 0, 0), neck=(20, 0, 0)), (0.06, 0.42, PAN + 0.62), TILLER, grip=40))
reload_.key(135, hands(SEATED, WHEEL_KNOB, TILLER))
clips.append(reload_)

# --- emotes (GDD §9: in the yard "the crew wait for friends, hang out, dance"; note 298's wheel, queue #44, note 306) ----------
# The game loops each from a beat of its own per crewmate (CreatureArt.Crewmate: time + the variant's offset), so the wave
# and the point are holds that loop anywhere, not a start and an end.
SHOULDER_R, SHOULDER_L = rig.pose_points(sk, STAND, [("upperarm_r", "head"), ("upperarm_l", "head")])


def bounce(pose, k):
    """The weight dropped into the knees by `k` (0..1), both knees bent the same."""
    return over(pose, **{"pelvis@loc": (0, 0, -0.06 * k), "thigh_r": (14 * k, 0, 0), "calf_r": (-28 * k, 0, 0), "foot_r": (14 * k, 0, -6),
                         "thigh_l": (14 * k, 0, 0), "calf_l": (-28 * k, 0, 0), "foot_l": (14 * k, 0, 6)})


# Wave (1 s, loop): the right hand up over the shoulder, open, waved side to side from the elbow; the weight on one hip,
# the head tipped to whoever it's for.
WAVE_BODY = over(STAND, spine_02=(-2, 0, 4), spine_03=(-4, 0, 6), neck=(8, 4, 6), head=(-2, 6, 4), pelvis=(0, 0, -4))
wave = Clip("wave")
for f, x in ((0, 0.0), (7, 1.0), (15, 0.0), (22, -1.0)):
    wrist = (SHOULDER_R.x + 0.16 + 0.11 * x, SHOULDER_R.y + 0.12, SHOULDER_R.z + 0.36 - 0.03 * abs(x))
    p = arm_to(WAVE_BODY, "r", wrist, fist=False, elbow=(0.45, 0.0, 1.3))
    p["hand_r"] = (0, 6, -14 * x)
    p["fingers_r"], p["thumb_r"] = (0, 6, 0), (0, 4, 0)
    wave.key(f, p)
wave.close(30)
clips.append(wave)

# Point (2 s, loop): the right arm straight out ahead at the shoulder's height, the hand flat along it, the eyes down it;
# the left hand on the hip; a jab of emphasis, once a second, the body leant into it.
POINT_AT = (SHOULDER_R.x + 0.04, SHOULDER_R.y + 0.66, SHOULDER_R.z - 0.02)
POINT_BODY = look_at(over(STAND, spine_02=(-4, 0, 0), spine_03=(-4, 0, 0)), (0.3, 6.0, 1.4))
HIP_L = (-0.24, 0.02, 1.0)
point = Clip("point")
for f, jab in ((0, 0.0), (5, 1.0), (12, 0.2), (30, 0.0), (35, 1.0), (42, 0.2)):
    body = over(POINT_BODY, spine_02=(-4 - 3 * jab, 0, 0), pelvis=(0, 0, -2))
    p = arm_to(body, "r", (POINT_AT[0], POINT_AT[1] + 0.06 * jab, POINT_AT[2]), fist=False, elbow=(0.3, 0.0, 1.45))
    p["hand_r"] = (0, 0, 0)
    p["fingers_r"], p["thumb_r"] = (0, 4, 0), (0, 20, 0)
    p = arm_to(p, "l", HIP_L, grip=40, elbow=(0.45, -0.2, 1.15))
    point.key(f, p, "LINEAR" if jab == 1.0 else "BEZIER")
point.close(60)
clips.append(point)

# Dance (4 s, loop, played twice over the emote's 8 s): a workman's jig in heavy boots, goofy and in earnest (§31's
# weight: it's all in the knees). Two bars of stomping knee-lifts with the fists pumped up over the head on the beat
# ("raise the roof"), then two of a side-to-side shuffle, the hips swinging, the elbows out flapping like a hen's, the
# head bobbing a beat behind.
DANCE = over(STAND, spine_02=(-4, 0, 0), spine_03=(-6, 0, 0))
dance = Clip("dance")
for f in range(0, 120, 3):
    beat = (f % 15) / 15                                    # 2 beats a second
    down = 0.5 + 0.5 * math.cos(2 * math.pi * beat)        # 1 on the beat: weight down
    if f < 60:
        side = "r" if (f // 15) % 2 == 0 else "l"
        lift = math.sin(math.pi * beat) ** 1.5             # the knee up between the beats, stomped down on them
        p = bounce(DANCE, 0.4 * down)
        p = over(p, **{f"thigh_{side}": (50 * lift, 0, 0), f"calf_{side}": (-70 * lift, 0, 0), f"foot_{side}": (10 * lift, 0, 0),
                       "spine_03": (-6 + 4 * down, 0, 0), "neck": (8 - 6 * down, 0, 0)})
        up = 0.5 + 0.5 * math.cos(2 * math.pi * beat)
        h = SHOULDER_R.z + 0.18 + 0.32 * up
        p = hands(p, (0.2, 0.1, h), (-0.2, 0.1, h), grip=90, elbow=(0.45, 0.0, SHOULDER_R.z))
    else:
        sway = math.sin(2 * math.pi * (f - 60) / 30)        # a bar each way
        p = bounce(DANCE, 0.5 * down)
        p = over(p, **{"pelvis@loc": (0.06 * sway, 0, -0.03 * down), "pelvis": (0, 0, 10 * sway), "spine_02": (-4, 0, -6 * sway),
                       "spine_03": (-6, 6 * sway, -4 * sway), "neck": (8, -6 * sway, 4 * math.sin(2 * math.pi * (beat - 0.25))),
                       "thigh_r": (14 * down + 10 * max(0.0, sway), 0, 4), "thigh_l": (14 * down + 10 * max(0.0, -sway), 0, -4)})
        flap = math.sin(2 * math.pi * beat)
        # Fists up before the chest, clear of the coat (dt art clearance), the elbows out and flapping.
        p = hands(p, (0.25, 0.32, SHOULDER_R.z - 0.16), (-0.25, 0.32, SHOULDER_R.z - 0.16), grip=90,
                  elbow=(0.55, -0.1, SHOULDER_R.z - 0.1 + 0.12 * flap))
    dance.key(f, p, "LINEAR")
dance.close(120)
clips.append(dance)

# --- paint: Dave at his easel (GDD §3.2, ARCHITECTURE §8 note 491) ---------------------------------------------------
# Four seconds, looped from wherever (DaveArt plays it under him at his canvas). The canvas stands where DaveKit's easel
# puts it: its painted face 0.737 m ahead of his feet, 1.0-1.62 m up, 0.84 m across. The brush (dave_kit's dave_brush,
# along hand_r_weapon's +Y, its tip BRUSH_TIP on from the grip) dabbed at a spot three times, then a long stroke across;
# back to the palette (dave_palette, under the left palm, the hand held palm up at the waist) to work the paint round;
# and a step back to look past the canvas at what he's painting, then in again. The weight on the back foot, the left
# forward; the eyes on the brush.
CANVAS_Y = 0.737
BRUSH_TIP = 0.235
GRIP = sk["hand_r_weapon"].head


def posed_at(pose, bone, p):
    """Where a point of the bind pose, carried by `bone`, is in `pose` (armature space)."""
    head = rig.pose_points(sk, pose, [(bone, "head")])[0]
    return head + rig.world_rotation(sk, pose, bone).to_matrix() @ (Vector(p) - sk[bone].head)


def planted(pose):
    """The pose dropped (or lifted) so its lowest foot's on the floor, as the bake's planter will: the brush's aim is
    taken after it."""
    low = min(q.z for q in rig.pose_points(sk, pose, [(b, w) for b in ("foot_l", "foot_r", "ball_l", "ball_r") for w in ("head", "tail")]))
    loc = pose.get("pelvis@loc", (0, 0, 0))
    return over(pose, pelvis__loc=(loc[0], loc[1], loc[2] + 0.03 - low))


def brush_on(pose, tip):
    """The right arm and wrist set so the brush's tip is at `tip`, the brush pointing in at it from the shoulder's
    side (a painter's long brush, held well back): the wrist reached for, then the hand turned until the tip's there
    (the wrist moved by what's left, and again)."""
    tip = Vector(tip)
    shoulder = rig.pose_points(sk, pose, [("upperarm_r", "head")])[0]
    d = (tip - (shoulder + Vector((0.05, 0.1, -0.2)))).normalized()
    reach_of = GRIP + Vector((0, BRUSH_TIP, 0)) - sk["hand_r"].head
    wrist = tip - d * (BRUSH_TIP + 0.08)
    best_p = pose
    for _ in range(4):
        p = arm_to(pose, "r", tuple(wrist), grip=70, elbow=(0.42, 0.05, 1.12))
        head = rig.pose_points(sk, p, [("hand_r", "head")])[0]
        Wp = rig.world_rotation(sk, p, "lowerarm_r")

        def cost(r):
            R = Wp @ rig.rot(*r)
            now = head + R @ reach_of
            return (now - tip).length + 0.04 * (1 - (R @ Vector((0, 1, 0))).dot(d))

        best = min(((rx, ry, rz) for rx in range(-120, 121, 12) for ry in range(-60, 61, 12) for rz in range(-96, 97, 12)), key=cost)
        for step in (4, 1, 0.25):
            best = min(((best[0] + i * step, best[1] + j * step, best[2] + k * step) for i in (-3, -2, -1, 0, 1, 2, 3)
                        for j in (-3, -2, -1, 0, 1, 2, 3) for k in (-3, -2, -1, 0, 1, 2, 3)), key=cost)
        p["hand_r"] = best
        best_p = p
        now = head + (Wp @ rig.rot(*best)) @ reach_of
        if (now - tip).length < 0.004:
            break
        wrist = wrist + (tip - now)
    best_p["fingers_r"], best_p["thumb_r"] = (0, 62, 0), (0, 28, 0)
    return best_p


# The palette: the left forearm across the waist, the hand palm up under it, fingers out to the left.
PALETTE_WRIST = (-0.2, 0.3, 1.06)


def palette_hold(pose):
    p = arm_to(pose, "l", PALETTE_WRIST, grip=20, elbow=(0.36, -0.05, 1.08))
    # Palm up and the fingers forward and out: the bind's palm-down hand rolled over about its length, then turned.
    q = Quaternion((0, 0, 1), math.radians(-55)) @ Quaternion((1, 0, 0), math.radians(180))
    p["hand_l"] = rig.world_rotation(sk, p, "lowerarm_l").inverted() @ q
    p["fingers_l"], p["thumb_l"] = (0, -18, 0), (0, -10, 0)
    return p


PAINT_BODY = over(STAND, pelvis=(0, 0, 6), spine_01=(-4, 0, -2), spine_02=(-6, 0, -3), spine_03=(-5, 0, -2),
                  thigh_l=(14, 0, -4), calf_l=(-6, 0, 0), foot_l=(-6, 0, 2), thigh_r=(-8, 0, 8), calf_r=(-4, 0, 0), foot_r=(10, 0, -12))
LOOK = (-1.2, 9.0, 1.7)            # past the canvas's left edge: the view he's painting
SPOT = Vector((0.06, CANVAS_Y, 1.4))


def paint_key(tip, lean=0.0, look=None, back=0.0):
    """A key: the body leant in by `lean` (0..1) or back by `back`, the palette held, the brush's tip at `tip` (or
    tip(pose), a point on the palette as it's held), the eyes on the tip (or on `look`)."""
    body = over(PAINT_BODY, spine_01=(-4 - 4 * lean + 5 * back, 0, -2), spine_02=(-6 - 5 * lean + 4 * back, 0, -3),
                pelvis__loc=(0, 0.03 * lean - 0.05 * back, 0))
    body = palette_hold(planted(body))
    tip = tip(body) if callable(tip) else Vector(tip)
    body = look_at(body, look if look is not None else tuple(tip), share=0.4)
    return brush_on(body, tip)


PALETTE_FACE = 1.414    # dave_palette's painted face in the bind pose: under the palm-down hand, its far side


def palette_tip(pose, u=0.0, v=0.0):
    """A point in the palette's mixing (dave_palette, held against the left palm), as the hold has it."""
    return posed_at(pose, "hand_l", (-0.85 - u, 0.0 + v, PALETTE_FACE))


paint = Clip("paint")
for f, tip, kw in ((0, SPOT + Vector((0, -0.06, 0.01)), dict(lean=0.6)),
                   (6, SPOT, dict(lean=1.0)), (10, SPOT + Vector((0.004, -0.03, 0.004)), dict(lean=0.9)),
                   (14, SPOT + Vector((0.022, 0, -0.016)), dict(lean=1.0)), (18, SPOT + Vector((0.024, -0.03, -0.01)), dict(lean=0.9)),
                   (22, SPOT + Vector((0.038, 0, 0.008)), dict(lean=1.0)), (27, SPOT + Vector((-0.14, -0.05, -0.1)), dict(lean=0.8)),
                   (31, SPOT + Vector((-0.17, 0, -0.12)), dict(lean=1.0)), (45, SPOT + Vector((0.12, 0, -0.15)), dict(lean=1.0)),
                   (51, SPOT + Vector((0.12, -0.12, -0.12)), dict(lean=0.5))):
    paint.key(f, paint_key(tip, **kw), "LINEAR" if f in (31, 45) else "BEZIER")
# To the palette, working the paint in two small circles, back up.
for f, (u, v) in ((63, (0.0, 0.0)), (68, (0.02, 0.015)), (73, (0.0, 0.03)), (78, (-0.02, 0.015)), (83, (0.0, 0.0))):
    paint.key(f, paint_key(lambda pose, u=u, v=v: palette_tip(pose, u, v), lean=0.3))
# A step back to look past the canvas at the world, the brush lowered; then in again.
paint.key(94, paint_key(SPOT + Vector((0.1, -0.32, -0.22)), back=1.0, look=LOOK))
paint.key(106, paint_key(SPOT + Vector((0.1, -0.3, -0.2)), back=1.0, look=at(LOOK, dx=0.6, dz=0.3)))
paint.key(114, paint_key(SPOT + Vector((0.02, -0.12, 0.0)), lean=0.4))
paint.close(120)
clips.append(paint)

kit.build()
rig.bake(sk, clips, plant=rig.feet_planter(sk, clips={"wave", "point", "dance", "paint", "carry", "carry_walk", "drag", "drag_fwd", "shovel", "door", "handbrake", "hatch",
                                                        "uncouple", "vent", "lever", "push", "swing", "mend",
                                                        "gap", "extinguish", "spray", "lantern", "lantern_walk", "haul",
                                                        "haul_up", "drive", "whistle", "smash", "pry", "pick", "take_down", "hang_up",
                                                        "stagger", "throw", "chute", "spout", "shoulder", "cradle", "firedoor", "held_cover", "held_frozen",
                                                        "held_seized", "held_mouth", "stumble", "pull_mouth", "pry_off", "haul_down"}))
rig.export(rig.args()[0] if rig.args() else "crew_clips.glb", kit)
print(f"[dt] crew clips {[c.name + ':' + str(c.length) for c in clips]}")
