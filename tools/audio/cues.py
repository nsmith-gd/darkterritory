#!/usr/bin/env python3
"""The game's sound events, one per trigger: what the audio checklist reviews and what the game will hook up.

Director's rule (2 Oct review): a line on the checklist is not one sound. Every sound is split into the separate things a
player or the sim does, each its own file (a one-shot with a few takes to pick between at random, or a seamless loop the
code starts and stops), and a sound that touches something is split again by the surface material it lands on. The code
that triggers and layers them comes later; these are only the single events.

Two kinds of sound, made two ways:
- Gameplay foley (the crew's items and tools, the train, the world, places): a candidate should be the real thing (a Kenney
  wood footstep for "walk on wood"), trimmed and levelled, not rebuilt from unrelated sounds. Where the packs have nothing
  that is the event, the cue says what it needs and waits for a real source (Sonniss).
- Enemies (creature sounds and tells): interesting, organic, made by distorting and kitbashing sources together, as the
  director asked. Each still split into its own cues.
The review lives on the Dark Territory Audio Checklist artifact (https://claude.ai/artifact/F5szjdzd8Svn3nfH3mDWMN), whose
`items` store holds each line's `cues`; the library copies it plays are published under its `library/`.

  python3 tools/audio/cues.py                     # summary
  python3 tools/audio/cues.py --store OUT --from DIR
      # one {"cues": [...]} file per line in OUT, to merge into the store's items. DIR is the store's `items` as saved by a
      # list with out_dir: the director's Keep/Redo verdicts there (on cues, and on the earlier pass's refs) are carried over,
      # so rewriting the cues never drops a review.

Built candidates (tools/audio/build.py's recipes) join their cue automatically once their preview is in the page's asset
store (tools/audio/assets.py); library files named in CUES below are played as they come.
"""

import json
import sys

# Surface materials (sim: PlayerMotor.Surface, CarFrame.SurfaceKind). The director's four, plus the car roofs the sim already
# tells apart (Surface.Roof; "roof tin" in the Climbers' brief).
MATERIALS = {
    "wood": "Wood car floor (cargo and guard car decks, platforms)",
    "grate": "Metal grate and plate (engine running boards, cab footplate, couplers, steps)",
    "roof": "Car roof tin",
    "ground": "Outdoor ground (ballast, dirt, yard)",
    "concrete": "Indoor building concrete (fortress, facilities, holdouts)",
    # Footsteps go finer (the director, 2 Oct: "there are many more footstep surfaces in this game"): the ground the
    # world is textured with, grouped by how it sounds underfoot.
    "plate": "Solid iron plate (the cab's footplate, the tender deck)",
    "coal": "Coal (the tender's pile, the coaling stage)",
    "ballast": "Ballast and loose stone (the track bed, shingle, slag)",
    "dirt": "Dirt and forest floor (yards, paths, clay, needles)",
    "grass": "Grass and heath",
    "mud": "Mud, bog and marsh",
    "cobbles": "Cobbles and stone setts (towns, yards)",
}
DROP = ["wood", "grate", "ground", "concrete"]
FEET = ["wood", "grate", "plate", "roof", "coal", "ballast", "dirt", "grass", "mud", "cobbles", "concrete"]

# The director's note (2 Oct): the bottle-opening/fizz sound was overdone, and used where it made no sense. Fine where a
# fizz belongs; not as a default texture. Most of it came from these two files (air_01 in 10 earlier references, 7 marked
# redo; misc_02 in 10, 8 redo).
OVERUSED = {"sfx_100_v2:sfx100v2_air_01", "sfx_100_v2:sfx100v2_misc_02"}


def lib(key):
    """'pack:name' -> the page's published copy."""
    pack, name = key.split(":")
    return f"library/{pack}__{name}.mp3"


def K(name, n=None):
    """Kenney impact takes: K('impactWood_medium', 5) -> five takes."""
    return [f"kenney_impact-sounds:{name}_{i:03d}" for i in range(n or 5)]


def R(*names):
    return [f"kenney_rpg-audio:{n}" for n in names]


def S(*names):
    return [f"sfx_100_v2:sfx100v2_{n}" for n in names]


def O(id, event, vars=3, mats=None, cand=None, need="", silent=False):
    """A one-shot: `vars` takes the game picks between, per material when `mats` is set."""
    return dict(id=id, event=event, kind="oneshot", vars=vars, mats=mats or [], cand=cand or {}, need=need, silent=silent)


def L(id, event, mats=None, cand=None, need=""):
    """A seamless loop the code starts, holds and stops (its start and stop are their own one-shots where they exist)."""
    return dict(id=id, event=event, kind="loop", vars=1, mats=mats or [], cand=cand or {}, need=need, silent=False)


def old(file):
    """A take from the earlier pass the director kept: it stays as this cue's candidate."""
    return f"old:{file}"


# Line id -> its cues. Lines left out (mix systems, voice processing, music and UI, cut enemies) have no raw sounds of
# their own yet; gameplay first.
CUES = {
    # ---- Crew actions & foley ----------------------------------------------------------------------------------------
    "crew-extinguisher": [
        O("grab-mount", "Unhooked from its wall mount", cand={"_": R("metalLatch")}),
        O("equip", "From the hotbar into the hands"),
        O("spray-start", "Trigger squeezed: the discharge starting", vars=2),
        L("spray", "Spraying, held", need="A real extinguisher or CO2/water discharge recording. Nothing in the packs."),
        O("spray-stop", "Trigger let go: the discharge cutting off", vars=2),
        O("run-dry", "The charge running out mid-spray: the last of it sputtering", vars=2),
        O("dry-trigger", "Trigger squeezed with nothing left", cand={"_": R("metalClick")}),
        O("return-mount", "Hung back on its mount (it recharges there)", cand={"_": R("metalLatch")}),
        O("stow", "Back into the hotbar", need="Not in the game yet: there's no hotbar (an extinguisher is carried or dropped) in the sim, so nothing plays this. Ready for when there is."),
        O("drop", "Dropped, or thrown and landing: a steel cylinder hitting", mats=DROP,
          cand={"wood": K("impactWood_heavy"), "grate": K("impactMetal_heavy")}),
    ],
    "crew-melee": [
        *[c for tool in ("shovel", "wrench", "crowbar") for c in (
            O(f"{tool}-equip", f"{tool.title()} from the hotbar into the hands"),
            O(f"{tool}-stow", f"{tool.title()} back into the hotbar"),
            O(f"{tool}-swing", f"{tool.title()} swung (a miss is just this)", vars=4),
            O(f"{tool}-hit-flesh", f"{tool.title()} landing on a creature or a body", vars=4,
              cand={"_": K("impactPunch_heavy")}),
            O(f"{tool}-hit-metal", f"{tool.title()} striking iron (car side, rail, the engine)", vars=4,
              cand={"_": K("impactMetal_heavy") if tool != "shovel" else K("impactPlate_heavy")}),
            O(f"{tool}-hit-wood", f"{tool.title()} striking wood (car walls, crates, boards)", vars=4,
              cand={"_": K("impactWood_heavy")}),
            O(f"{tool}-drop", f"{tool.title()} dropped: let go as a crewmate holding it goes down", mats=DROP,
              cand={"wood": K("impactWood_medium"), "grate": K("impactMetal_medium")}),
        )],
        # Note 275: the fireman's shovel lives on an iron rack in the cab.
        O("shovel-rack-off", "The shovel lifted off its iron rack in the cab", vars=2),
        O("shovel-rack-on", "The shovel hung back on its rack: iron on iron", vars=2),
    ],
    "crew-carry": [
        O("crate-lift", "A crate picked up"),
        O("crate-set", "A crate set down", mats=DROP, cand={"wood": K("impactWood_heavy"), "grate": K("impactPlate_heavy")}),
        O("crate-land", "A crate thrown and landing", mats=DROP, cand={"wood": K("impactPlank_medium")}),
        L("crate-drag", "A crate sliding along the floor (the train braking hard throws it), scraping till it stops", mats=DROP),
        O("lamp-lift", "A hand lantern picked up (bail and glass rattle)"),
        O("lamp-set", "A lantern set down", mats=DROP),
        O("lamp-break", "A lantern dropped hard enough to break", cand={"_": S("glass_03", "glass_05")}, need="Not in the game yet: there's no breaking a lantern in the sim, so nothing plays this. Ready for when there is."),
        O("toy-lift", "A toy picked up"),
        O("toy-drop", "A toy dropped", mats=DROP, cand={"wood": K("impactWood_light")}),
        O("radio-lift", "The radio picked up"),
        O("radio-drop", "The radio dropped", mats=DROP, cand={"wood": K("impactWood_light"), "grate": K("impactMetal_light")}),
        O("body-lift", "A body or a child hoisted", cand={"_": R("cloth1", "cloth2", "cloth4")}),
        O("body-set", "A body laid down", mats=DROP, cand={"_": K("impactSoft_heavy")}),
        O("body-land", "A body thrown and landing", mats=DROP, cand={"_": K("impactSoft_heavy")}),
        O("loot-take", "Loot picked up"),
    ],
    "crew-footsteps": [
        O("walk", "A step, walking", vars=6, mats=FEET,
          cand={"wood": K("footstep_wood") + S("footstep_wood_01", "footstep_wood_02", "footstep_wood_03", "footstep_wood_04"),
                "concrete": K("footstep_concrete"), "grass": K("footstep_grass")}),
        O("run", "A step, running", vars=6, mats=FEET),
        O("jump", "Push-off into a jump", mats=FEET),
        O("land", "Landing from a jump or a drop", mats=FEET),
        O("scuff", "Turning on the spot or stopping short", mats=FEET),
    ],
    "crew-ladder": [
        O("grab", "Hands taking the ladder"),
        O("rung-up", "One rung climbed: hand and boot on iron", vars=6),
        O("rung-down", "One rung down", vars=6),
        O("let-go", "Letting go at the top or bottom"),
        O("gap-land", "Landing across a coupling gap (on the coupler plate)", vars=3, need="Same family as land on grate."),
    ],
    "crew-doors": [
        O("slide-unlatch", "Sliding car door: the latch lifted", cand={"_": R("metalLatch") + S("lock_open_01")}),
        O("slide-start", "Sliding door: starting to roll", vars=2),
        L("slide-roll", "Sliding door rolling on its track", cand={"_": S("misc_16", "misc_04")}),
        O("slide-open-stop", "Sliding door hitting its open stop", vars=2),
        O("slide-shut", "Sliding door slammed shut (the Choir's rule: must be heard and trusted)",
          cand={"_": S("door_03")}),
        O("slide-latch", "Sliding door latched"),
        O("end-open", "End door (hinged) opened", vars=2, cand={"_": R("doorOpen_1", "doorOpen_2")}),
        O("end-shut", "End door shut", vars=3, cand={"_": R("doorClose_1", "doorClose_3", "doorClose_4")}),
        O("hatch-open", "Roof hatch thrown open"),
        O("hatch-shut", "Roof hatch dropped shut"),
        O("locker-open", "Locker or tool rack opened"),
        O("locker-shut", "Locker or tool rack shut"),
    ],
    "crew-lamps": [
        O("car-lamp-on", "A car's lamp switched on", cand={"_": S("switch_01", "switch_02")}),
        O("car-lamp-off", "A car's lamp switched off", cand={"_": S("switch_01", "switch_02")}),
        O("cab-lamp", "The engine's forward lamp switched"),
        O("lantern-light", "A hand lantern lit", need="Not in the game yet: there's no lighting a hand lantern (it's always lit) in the sim, so nothing plays this. Ready for when there is."),
        O("lantern-out", "A hand lantern put out", need="Not in the game yet: there's no putting a hand lantern out in the sim, so nothing plays this. Ready for when there is."),
    ],
    "crew-firebox-door": [
        O("open", "Firebox door opened", vars=3),
        O("shut", "Firebox door clanged shut", vars=3),
        L("fire-open", "The fire heard through the open door (louder while it's open)"),
    ],
    "crew-shovel": [
        O("scoop", "Blade driven into the coal in the tender", vars=4, cand={"_": S("stones_01", "stones_02", "stones_03")}),
        O("throw", "The coal thrown into the firebox, landing on the fire", vars=4),
        O("knock", "Blade knocked on the firebox door frame", vars=3, cand={"_": K("impactMetal_heavy")}),
    ],
    "crew-cab-controls": [
        O("regulator-notch", "Regulator (throttle) moved one notch", vars=4),
        O("reverser-notch", "Reverser moved one notch", vars=3),
        O("brake-handle", "Brake valve handle moved", vars=3),
        L("brake-apply", "Steam brake applying, held"),
        O("handbrake-ratchet", "A car's handbrake wheel turned one click", vars=4, cand={"_": S("misc_20", "lock_open_01")}),
        O("handbrake-set", "Handbrake wound tight"),
        O("handbrake-release", "Handbrake knocked off"),
        O("sander-lever", "Sander lever pulled"),
        L("sand-flow", "Sand running onto the rail"),
        O("vent-handle", "Vent valve handle thrown (the steam is the Vent blow-off line)"),
        O("whistle-cord", "Whistle cord pulled"),
        O("whistle-start", "Whistle sounding: the onset"),
        L("whistle", "Whistle held"),
        O("whistle-stop", "Whistle released: the tail"),
    ],
    "crew-switch": [
        O("lever-unlatch", "Switch stand lever unlatched"),
        O("lever-throw", "Lever thrown over", vars=2),
        O("points-move", "Point blades sliding across", vars=2),
        O("lever-latch", "Lever latched home"),
    ],
    "crew-coupling": [
        O("pin-lift", "Coupling pin lifted", vars=3),
        O("knuckle-release", "Coupler knuckle opening as the cars part", vars=3),
        O("hose-part", "Brake hoses pulling apart", vars=2, need="A rubber hose and gladhand separating. Not a hiss or a pop."),
        O("knuckle-close", "Knuckles slamming together on a recouple", vars=3),
        O("pin-drop", "Coupling pin dropping home", vars=3),
    ],
    "crew-repair": [
        O("kit-open", "Repair kit opened"),
        L("ratchet", "Wrench ratcheting on the boiler, held", cand={"_": S("misc_20")}),
        O("board-place", "A board set against the hole", cand={"_": K("impactPlank_medium")}),
        O("hammer", "A nail hammered", vars=6, cand={"_": S("wood_hit_03", "misc_19")}),
        O("done", "The last nail: boarded up (a mechanic confirmation)"),
        O("kit-shut", "Repair kit shut"),
    ],
    "crew-cannon-fire": [
        O("ignite", "Linstock to the touch hole (a short flash, no fizz)", vars=2),
        O("shot-close", "The shot, at the gun", vars=3),
        O("shot-far", "The shot, from down the train and beyond", vars=3, cand={"_": [old("audio/crew-cannon-fire--far.mp3")]}),
        O("recoil", "The carriage bucking back on its mount", vars=2),
        L("traverse", "The gun slid along its roof rails"),
        O("traverse-stop", "The gun stopped on its rails"),
    ],
    "crew-cannon-reload": [
        L("powder", "Powder charge pushed down the muzzle, for the 1.5 s hold (stops clean if abandoned)"),
        O("powder-done", "Charge seated"),
    ],
    "crew-cannon-ball": [
        O("ball-in", "The ball dropped into the muzzle", vars=2),
        O("ball-roll", "The ball rolling down the bore, iron on iron", vars=2, cand={"_": [old("audio/crew-cannon-ball--ball.mp3")]}),
    ],
    "crew-cannon-ram": [
        L("ram", "Rammer strokes, for the 1.5 s hold"),
        O("ram-home", "Rammed home: the gun is ready", vars=2, cand={"_": [old("audio/crew-cannon-ram--ram.mp3")]}),
    ],
    # Synthesised on main first; recorded candidates replace them through install.py's SWAPS, level-matched.
    "crew-gun-lay": [
        L("lay", "The seated gunner laying the gun: the steam motor and its worm gear, quicker as it turns faster"),
    ],
    "crew-cannon-impact": [
        O("ground", "A ball coming down on earth: the boom, then earth and splinters pattering after (heard a long way)", vars=3),
        O("water", "A ball into water: the plunge, the column of spray falling back", vars=3),
        O("doll", "A ball through the Track Doll: porcelain bursting, the one sound that says she's gone for good", vars=2),
        # Note 290: a ball meets bodies and walls now, not only the ground.
        O("flesh", "A ball landing in a creature: a heavy wet thud, nothing like earth", vars=3),
        O("structure", "A ball striking a fort's or a building's stone: the crack and the rubble falling", vars=3),
        O("train", "A ball striking the train's own iron: a deep clang, rivets and plate ringing", vars=2),
    ],
    "crew-noisy-toys": [
        L("squeaker", "A rubber squeaker toy squeezed in the hand as it's carried"),
        L("music-box", "A music box's tune, wound and playing as it's carried"),
        L("drummer", "A wind-up tin drummer beating its drum as it's carried"),
    ],
    "crew-hurt": [
        O("hit", "Struck: the blow landing on the body", vars=4, cand={"_": K("impactPunch_medium")}),
        O("grabbed", "Seized: clothing grabbed and pulled", vars=3, cand={"_": R("cloth1", "cloth3", "clothBelt")}),
        O("burned", "Burned: a quick sear", vars=2),
        O("body-fall", "The body falling", mats=DROP, cand={"_": K("impactSoft_heavy")}),
    ],
    "crew-jump-off": [
        L("rush", "Air rushing past at speed (the jump off a moving train)"),
        O("impact", "Hitting the ground at speed", vars=3, mats=["ground", "grate"]),
        L("tumble", "Rolling on the ballast after", mats=["ground"]),
    ],
    "crew-cold": [
        O("breath-in", "A cold breath in", vars=4),
        O("breath-out", "A breath out", vars=4),
        L("shiver", "Shivering, as the cold takes hold"),
    ],
    # The funny ones (the director's call, 3 Oct): where the crew does it to themselves, or the world shrugs. Real sounds
    # with comic timing, never on a tell, a lure, an alarm or a monster's kill.
    "crew-mishaps": [
        O("tunnel-bonk", "Stood on a roof into a tunnel's mouth: the BONK of a head on the portal", vars=3),
        O("tunnel-tumble", "The body tumbling back along the roof and dropping off the end", vars=2),
        O("thrown-flail", "Thrown off a roof on a curve: coat and limbs flapping through the air", vars=2),
        O("pocket-scatter", "After a hard landing off the train: what was in hand skittering away across the ground", vars=3),
        O("crushed", "Under a casting a crewmate let go: the iron bong and the crunch under it", vars=2),
        O("crane-chain", "The crane's chain rattling loose in the quiet after", vars=2),
        O("bare-swing", "Swinging with empty hands: a sleeve whiffing through the air", vars=3),
        O("bare-slap", "An empty hand landing on something: a limp slap", vars=3),
        O("body-boot", "A body set down or thrown: one boot thudding down a beat after the rest", vars=3, mats=["wood", "ground"]),
        O("body-knock", "A carried body's boots knocking the door frame on the way through", vars=3),
        O("foul-fizzle", "A fouled gun: the damp charge giving a feeble pfft out of the vent", vars=2),
        O("extinguisher-dregs", "The extinguisher's last dregs after it runs dry: a few spits and a gurgle", vars=2),
        O("whistle-wheeze", "The whistle blown on low steam: a thin, flat wheeze that dies", vars=2),
        O("startle-cattle", "A cow in the livestock car startled by the slack running in", vars=3),
        O("startle-pigs", "Pigs in the livestock car startled by the slack running in", vars=3),
        O("startle-sheep", "Sheep in the livestock car startled by the slack running in", vars=3),
    ],

    # ---- Train bed -----------------------------------------------------------------------------------------------------
    "bed-boiler-roar": [
        L("roar-low", "Boiler and fire at low pressure", cand={"_": [old("audio/bed-boiler-roar--roar.mp3")]}),
        L("roar-high", "Boiler and fire near the redline (the code crossfades these by pressure)"),
    ],
    "bed-chuff": [
        O("chuff", "One exhaust beat (the code fires these at the wheel's tempo)", vars=6),
        O("chuff-heavy", "One exhaust beat, working hard (regulator wide)", vars=6),
        O("rod-clank", "Side rod knock, once a turn", vars=4),
    ],
    "bed-wheel-rail": [
        L("roll-slow", "Wheels rolling, slow", cand={"_": [old("audio/bed-wheel-rail--library.mp3")] + S("loop_ambient_02")}),
        L("roll-fast", "Wheels rolling, fast", cand={"_": S("loop_ambient_04")}),
        O("joint", "One wheel over a rail joint (the click-clack, fired per axle)", vars=6),
        L("flange", "Flange squeal on a curve"),
    ],
    "bed-slack": [
        O("run-in", "One coupling closing up (fired per car, down the train)", vars=6, cand={"_": [old("audio/bed-slack--run.mp3")]}),
        O("run-out", "One coupling stretching out", vars=6),
    ],
    "bed-brake": [
        O("apply", "Shoes going onto the tyres", vars=3),
        L("drag", "Brakes dragging, cold"),
        L("drag-hot", "Brakes dragging, hot (harsher)"),
        O("release", "Brakes coming off", vars=2),
    ],
    "bed-wind": [
        L("wind-slow", "Wind past you at low speed (outside only)"),
        L("wind-fast", "Wind past you at speed"),
        O("gust", "A gust", vars=4),
    ],
    "bed-groan": [
        L("groan", "The train labouring up a grade", cand={"_": [old("audio/bed-groan--grade.mp3")]}),
        O("creak", "A single frame creak", vars=6, cand={"_": R("creak1", "creak2", "creak3")}),
    ],
    "bed-vent": [
        O("open", "Vent valve opening: the steam starting", vars=2),
        L("blow", "Vent blowing off, held", cand={"_": S("loop_water_03")}),
        O("close", "Vent shut: the steam cutting off", vars=2),
    ],

    # ---- Train state & alarms -------------------------------------------------------------------------------------------
    "state-valve": [
        O("lift", "Safety valve lifting", vars=2),
        L("blow", "Safety valve blowing"),
        O("reseat", "Valve reseating", vars=2),
    ],
    "state-strain": [
        O("tick", "A boiler plate ticking under pressure", vars=6),
        O("rivet", "A rivet pinging", vars=4),
        L("groan", "The boiler groaning in the red"),
    ],
    "state-rupture": [
        O("burst", "The boiler bursting", vars=2),
        O("debris", "Debris coming down after", vars=2),
        L("steam-out", "Steam pouring from the wreck of it"),
    ],
    "state-brake-fade": [
        L("fade", "Hot brakes glazing and losing their bite (replaces drag-hot as they go)"),
    ],
    "state-derail": [
        L("flange-scream", "Flanges screaming on a curve taken too fast (the warning)"),
        O("climb", "A wheel climbing the rail", vars=2),
        O("tip", "A car going over", vars=2),
        O("impact", "A car hitting the ground", vars=3, mats=["ground"]),
        L("grind", "A wreck grinding along the ballast"),
        O("settle", "Wreckage settling", vars=4),
        # The director (2 Oct): a derailment is many sounds the physics triggers, not one; these three complete the set.
        O("collide", "One car slamming into another as the train piles up", vars=4),
        L("rail-scrape", "Steel dragged along a rail head (a car body or a truck sliding on the rail)"),
        O("tear", "Metal and wood ripping apart under force (a car body or frame torn open)", vars=4),
    ],
    # The line's warnings (notes 260, 265): synthesised on main first, replaced through install.py's SWAPS, level-matched.
    "warn-overspeed": [
        O("bell", "The communication bell over the driver, struck twice: a bend ahead this speed would derail the train on", vars=3),
    ],
    "warn-curve": [
        O("chatter", "The roof irons chattering in their sockets as the car starts to lean on a bend taken too fast", vars=3),
    ],
    "warn-low-clearance": [
        O("telltales", "The telltale cords slapping across the roof ahead of you: a tunnel's mouth coming", vars=3),
    ],
    "state-breach": [
        O("breach", "A car's shell giving way: iron wrenched open", vars=3),
        L("open-to-outside", "The outside coming in through the hole (wind and the bed, louder)"),
    ],
    "state-cannon-foul": [
        O("misfire", "The gun failing to fire: a dead click", vars=3),
        L("clear", "Clearing the bore by hand"),
        O("cleared", "Cleared"),
    ],
    "state-engine-damage": [
        L("leak-small", "A small steam leak", cand={"_": [old("audio/state-engine-damage--leaks.mp3")]}),
        L("leak-large", "A big leak"),
        L("knock", "Damaged machinery knocking", cand={"_": S("loop_machine_04")}),
    ],

    # ---- World & hazards ------------------------------------------------------------------------------------------------
    "world-night": [
        L("night", "The wilderness at night, past the train"),
        O("far", "Something far off out there", vars=6),
    ],
    "world-tunnels": [
        O("enter", "The train entering a tunnel mouth", vars=2),
        O("exit", "Coming out of the tunnel", vars=2),
        L("inside", "Inside the tunnel (the bed itself gets the tunnel reverb)"),
        O("drip", "Water dripping in the tunnel", vars=4),
    ],
    "world-bridges": [
        L("iron-drum", "Wheels drumming on an iron bridge"),
        L("timber", "A timber trestle under the train"),
        O("groan", "A weak bridge groaning under the load", vars=4),
    ],
    "world-rain": [
        L("rain-roof", "Rain on the roof tin, heard from inside"),
        L("rain-out", "Rain outside, on the ground and the cars"),
        O("thunder", "Thunder", vars=3, cand={"_": S("thunder_01")}),
        O("slip", "Wheels slipping on wet rail", vars=2),
    ],
    "world-brass": [
        L("cut", "Cutting through brass growth slowly"),
        O("ram", "Ramming through it", vars=2),
    ],
    "world-debris-hit": [
        O("jolt", "Debris taken slowly: a jolt", vars=3),
        O("hit-fast", "Debris taken fast", vars=2),
    ],
    "world-wind": [
        L("gale", "Strong wind (masks voices on the roofs)"),
        O("gust", "A hard gust", vars=4),
    ],
    "world-livestock": [
        L("cattle", "Cattle in a car", need="Animal recordings."),
        L("pigs", "Pigs in a car", need="Animal recordings."),
        L("sheep", "Sheep in a car", need="Animal recordings."),
    ],

    # ---- Facilities & places --------------------------------------------------------------------------------------------
    "place-threshold": [
        L("fortress", "Inside the fortress: workers, machinery, guards", cand={"_": S("loop_construction_site")}),
        O("gate-open", "The gates opening", vars=1),
        O("gate-shut", "The gates shutting behind the train", vars=1),
    ],
    "place-coaling": [
        O("chute-open", "Chute lever pulled"),
        L("coal-pour", "Coal pouring down the chute"),
        O("chute-shut", "Chute shut"),
        O("coal-settle", "The coal settling in the tender", vars=2),
    ],
    "place-grain": [
        O("spout-swing", "The spout swung over a car"),
        L("grain-pour", "Grain pouring"),
        O("spout-stop", "Pour cut off"),
    ],
    "place-crane": [
        L("motor", "The crane's motor running", cand={"_": S("loop_machine_01")}),
        L("chain", "Chain paying out or hauling in"),
        O("load-swing", "A load swinging (creak)", vars=3),
        O("load-set", "A load set down", mats=["wood", "ground"]),
    ],
    "place-wreck": [
        O("creak", "A derailed car creaking", vars=4),
        O("shift", "Wreckage shifting", vars=3),
        L("cargo-pull", "Cargo dragged out of a wreck"),
    ],
    "place-slaughterhouse": [
        L("inside", "Inside the slaughterhouse"),
        O("hook-chain", "Hooks and chains moving", vars=3),
    ],
    "place-villages": [
        L("dead-town", "A dead town at night"),
        O("shutter", "A shutter banging", vars=3),
        O("sign", "A sign creaking", vars=3),
    ],
    "place-mine": [
        L("underground", "Underground at the mine head"),
        O("drip", "Water dripping", vars=4),
        O("timber", "Pit props creaking", vars=3),
    ],
    "place-chemical": [
        L("leak", "A leak hissing in the works"),
        O("drip", "Something dripping", vars=3),
    ],
    "place-breach": [
        O("smash", "A lock smashed (3 s of it: loud as a cannon)", vars=4),
        L("pry", "A barricade pried (6 s: loud as machinery)"),
        O("pry-give", "The barricade giving way", vars=2),
    ],
    "place-depot": [
        O("powder-blast", "A powder keg or powder car going up: a slow whump, the powder's roar, wreckage raining down", vars=2),
    ],

    # ---- Voice & comms (the device, not the voices) --------------------------------------------------------------------
    "voice-radio-sfx": [
        O("key-down", "Radio keyed", vars=3),
        O("key-up", "Radio released", vars=3),
        O("squelch", "Squelch tail after a transmission", vars=3),
        L("static", "Static under a weak signal"),
    ],
    "voice-callout": [
        O("bang", "Banging on the walls of a Holdout from inside", vars=4, cand={"_": [old("audio/voice-callout--banging.mp3")]}),
    ],

    # ---- Creature sounds ------------------------------------------------------------------------------------------------
    "cs-track-doll": [
        O("tamper", "A cab lever moved with nobody there (plays the crew's own lever sounds)", vars=1,
          cand={"_": [old("audio/cs-track-doll--tamper.mp3")]}),
        O("vanish", "Vanishing when approached", vars=2, cand={"_": [old("audio/cs-track-doll--vanish.mp3")]}),
        O("crack", "Porcelain cracking when cornered and clubbed", vars=3, cand={"_": [old("audio/cs-track-doll--cornered.mp3")]}),
        O("take-toy", "A toy taken", vars=2),
    ],
    "cs-car-hugger": [
        O("swallow", "The swallow closing round a player", vars=2, cand={"_": [old("audio/cs-car-hugger--swallow.mp3")]}),
        O("hit", "A hit from the rear platform landing on it", vars=4),
        O("break-away", "The eaten car breaking away with it", vars=1),
        # Note 310: the crew pull a swallowed crewmate back out of its mouth (the grab let go, Held cleared).
        O("spit-out", "A swallowed crewmate pulled free: the mouth forced open, a wet heave, the body sliding out", vars=2),
    ],
    "cs-whistler": [
        O("snatch", "The snatch at the gap", vars=2, cand={"_": [old("audio/cs-whistler--snatch.mp3")]}),
        L("run", "Its run with a victim (so the chase can follow by ear)"),
        L("nest", "The nest", cand={"_": [old("audio/cs-whistler--nest.mp3")]}),
        O("hit", "A hit landing on it", vars=4),
        O("death", "The kill"),
    ],
    "cs-tippy": [
        O("grab", "A hand clamped over a mouth", vars=2, cand={"_": [old("audio/cs-tippy--mouth.mp3")]}),
        L("struggle", "The struggle"),
        O("flee", "Its scurry when seen or hit", vars=3, cand={"_": [old("audio/cs-tippy--flee.mp3")]}),
    ],
    "cs-ribbits": [
        O("hop-land", "A heavy wet landing", vars=6, mats=["wood", "roof", "ground"], cand={"_": [old("audio/cs-ribbits--hops.mp3")]}),
        O("tongue", "The tongue lash", vars=3, cand={"_": [old("audio/cs-ribbits--tongue.mp3")]}),
        L("feed", "Devouring"),
        O("hit", "Clubbed", vars=4),
    ],
    "cs-choir": [
        L("arrive", "The swarm arriving"),
        O("seize", "A ghost seizing someone outside", vars=2),
        O("bang-door", "Banging and rattling on a closed door", vars=6, mats=["wood", "grate"]),
        O("hit", "A hit on one", vars=4),
        O("disperse", "Leaving once quiet holds", vars=2),
    ],
    "cs-hounds": [
        O("paw", "One paw fall (fired at the gallop's tempo)", vars=6, mats=["ground", "grate", "roof"]),
        O("leap", "The leap onto the rear car", vars=2),
        O("snarl", "A snarl", vars=4),
        O("bite", "A bite", vars=4),
        O("yelp", "Driven off by a hit", vars=3),
    ],
    "cs-climbers": [
        O("step", "A foot on the roof overhead, heard from inside", vars=6, mats=["roof"], cand={"_": [old("audio/cs-climbers--roof.mp3")]}),
        O("force", "Forcing a way into an unlit car", vars=2, cand={"_": [old("audio/cs-climbers--force.mp3")]}),
        O("hit", "A hit on one", vars=4),
    ],
    "cs-draggers": [
        O("grab", "The grab over the side", vars=2),
        L("scrabble", "The victim's boots scrabbling on the car side"),
        O("haul-up", "Hauled back up", vars=2),
        O("under", "Dragged under", vars=2),
    ],
    "cs-stoker": [
        L("in-fire", "Something moving in the fire"),
        O("shriek", "Its shriek when clubbed", vars=3, cand={"_": [old("audio/cs-stoker--clubbed.mp3")]}),
        O("burn", "The burn on each swing", vars=3),
    ],
    "cs-gaunt": [
        O("blow", "Its blow landing (nothing before it: silence is the tell)", vars=3),
        O("death", "Its death"),
        # Note 290: pain sounds for a ball are the audio chat's.
        O("hit", "A ball or a blow landing on it and not killing it: a dry, hollow grunt from something too big", vars=3),
    ],
    "cs-followers": [
        O("clubbed-off", "Clubbed off a back", vars=3),
        # The director (2 Oct): smashing a nest is held, so its sound is a loop, with the nest's end its own one-shot.
        L("nest-smash", "A nest being smashed, for as long as someone's at it"),
        O("nest-burst", "The nest finally destroyed", vars=2),
        # Note 290: pain sounds for a ball are the audio chat's.
        O("hit", "A ball or a blow landing on one off a back: a chittering squeal", vars=3),
    ],
    "cs-soot-children": [
        O("turn", "Turning inhuman"),
        O("lunge", "The lunge", vars=2),
        L("drink", "The pin and the drinking"),
        O("death", "Its death"),
        # Note 290: pain sounds for a ball are the audio chat's.
        O("hit", "A ball or a blow landing on it: a puff of soot and a hiss, no child's cry", vars=3),
    ],
    "cs-passenger": [
        O("step", "Its footsteps: the crew's own, so silence stays the only tell (plays crew-footsteps)", vars=1,
          cand={"_": [old("audio/cs-passenger--boots.mp3")]}),
        L("drag", "A victim dragged at walking pace", mats=["wood"]),
        O("uncouple", "The caboose uncoupled (plays crew-coupling)", vars=1, cand={"_": [old("audio/cs-passenger--uncouple.mp3")]}),
    ],
    "cs-switchman": [
        O("throw", "The switch thrown as you pass: its grip and its lamp, over the stand's own lever (crew-switch)", vars=3),
        O("flicker", "The lamp flickering as it grips the lever", vars=2),
        O("death", "Its death"),
        # Note 290: pain sounds for a ball are the audio chat's.
        O("hit", "A ball or a blow landing on it: its lamp rattling, a cracked-glass shriek", vars=3),
    ],
    "cs-grumbler": [
        O("scuttle", "Scuttling over the crane", vars=4),
        O("feral", "Going feral at whoever hit it last", vars=2),
        L("eat", "Eating cargo aboard"),
        # Note 290: pain sounds for a ball are the audio chat's.
        O("hit", "A ball or a blow landing on it before it turns: an indignant bark", vars=3),
    ],

    # ---- Enemy tells (the warning sounds; the director's In review stands) --------------------------------------------
    "tell-track-doll": [O("giggle", "The giggle", vars=4, cand={"_": [old("audio/tell-track-doll--heh.mp3")]})],
    "tell-car-hugger": [L("grind", "The grinding", cand={"_": [old("audio/tell-car-hugger--grind.mp3")]})],
    "tell-whistler": [O("whistle", "The whistle with no hand on the cord", vars=2,
                        cand={"_": [old("audio/tell-whistler--conductor.mp3"), old("audio/tell-whistler--wrong.mp3")]})],
    "tell-tippy": [O("tiptoe", "One faint tiptoe (fired at its pace)", vars=6, mats=["wood", "roof"])],
    "tell-ribbits": [L("swell", "Throats swelling", cand={"_": [old("audio/tell-ribbits--swell.mp3")]})],
    "tell-choir": [L("voices", "Voices multiplying (the code adds layers as they gather)")],
    "tell-car-fire": [L("smoulder", "Crackle through the boards, smouldering"), L("alight", "The car alight")],
    "tell-track-debris": [O("writhe", "One brief wet writhe (the game fires them at uneven intervals)", vars=4)],
    "tell-marsh": [L("reeds", "Reeds rustling")],
    "tell-grumbler": [L("gnaw", "Gnawing on the crates")],
    "tell-hounds": [O("howl-far", "A distant howl", vars=4, cand={"_": [old("audio/tell-hounds--far.mp3")]}),
                    O("howl-near", "The pack howling close behind (40-100 m), as it closes", vars=4)],
    "tell-climbers": [O("scrabble", "Scrabbling at the gap", vars=4)],
    "tell-draggers": [O("rasp", "The scrape at the lip", vars=3, cand={"_": [old("audio/tell-draggers--rasp.mp3")]})],
    "tell-stoker": [L("hiss-wrong", "The fire hissing wrong", cand={"_": [old("audio/tell-stoker--hiss.mp3")]})],
    "tell-fireflies": [L("buzz", "Buzzing round the lamp", cand={"_": [old("audio/tell-fireflies--swarm.mp3")]})],
    "tell-soot-children": [O("call", "A child calling for help", vars=4, need="A recorded child's voice; it must sound the same as a real survivor's.")],
    "tell-gaunt": [O("none", "Silent by design", silent=True)],
    "tell-followers": [O("none", "Silent by design", silent=True)],
    "tell-passenger": [O("none", "Never speaks", silent=True)],
    "tell-switchman": [O("none", "A visual tell", silent=True)],

    # ---- Music, UI, voice sets, the trailer -------------------------------------------------------------------------
    # UI sounds are things on the train's paperwork and brass: a waybill, a ticket punch, a stamp, a switch.
    # Main's features since 7 Oct (queue #61, note 322).
    "crew-heal": [
        # Note 272: a find that heals, used with Use held standing (loot.json healing).
        L("apply", "Using a healing find while Use is held: a bandage torn and wound tight, a medicine bottle uncorked and "
          "swallowed, a morphine syrette's cap off and the plunger pressed", mats=["bandages", "medicine", "morphine"]),
        O("done", "The find used up and the hurt eased: a long breath let out", vars=3),
    ],
    "crew-emotes": [
        # Note 298: the emote wheel (J). Dancing moves nobody, so its feet are its own.
        O("dance", "A jig on the spot: boots stamping in time and a clap", vars=2),
        O("wave", "An arm raised and waved: a coat sleeve's swish", vars=3),
        O("point", "An arm thrown out to point: a sharp coat rustle", vars=3),
        O("outfit", "Trying an outfit on in the yard: a coat shrugged into, buttons done up", vars=2),
    ],
    "state-starved": [
        # Note 319: an engine short of steam holds the train back.
        L("labour", "The engine short of steam, dragging its train: the exhaust thin and gasping, the motion labouring"),
    ],
    "place-derelict": [
        # Note 294: derelict cars on a yard's siding, cleared by shunting them out.
        L("roll", "A seized, rusted derelict car moving: dry axles grinding, flat wheels thumping, the body groaning"),
    ],
    "ui-film": [
        # Note 315: each player skips their own film.
        O("skip", "Skipping the film: a cut, a projector's shutter snapping shut", vars=1),
    ],
    "ui-panels": [
        # Note 316: the roster, the supplies and the route card, opened and closed.
        O("open", "A panel opened: a card or a ledger drawn out", vars=3),
        O("close", "A panel put away", vars=3),
        O("page", "A page turned on the route card", vars=3),
    ],
    "ui-menus": [
        O("move", "Moving between menu items", vars=4),
        O("select", "Choosing an item", vars=3),
        O("back", "Backing out", vars=2),
        L("title", "The title screen's sound, under the title"),
        O("end-card", "The demo's wishlist end card coming up", vars=1),
        # Note 320: a crew renamed and deleted from the fortress.
        O("type", "A letter typed into a name", vars=4),
        O("delete", "A crew deleted for good: a heavy stamp", vars=1),
    ],
    "ui-prompts": [
        L("hold", "A held action ticking on (a reload step, a breach, a repair)"),
        O("complete", "The held action done", vars=2),
        O("cancel", "Let go before it was done", vars=2),
    ],
    "ui-run-end": [
        O("report", "The run-end incident report coming up", vars=1),
        O("tally", "One line of the report filled in", vars=4),
        O("commendation", "A commendation awarded", vars=2),
        O("death-stamp", "A death on the report: the rubber stamp", vars=3),
        O("own-goal", "A death the crew did to themselves (a tunnel, a curve, a jump, a casting): a weary typewriter line and its bell", vars=2),
    ],
    "ui-dead-phase": [
        O("queue", "The respawn queue moving up", vars=2),
        O("vote", "A creature vote locked in", vars=2),
        O("bookmark", "A bookmark taken", vars=2),
    ],
    "ui-stranded-outro": [
        L("boiler-tick", "The dead boiler ticking and pinging as it cools (slower as it goes cold)"),
        O("lamp-out", "A car's lamp guttering out, last car first", vars=3),
    ],
    "ui-music": [
        L("drone", "Music while the crew is at work: low, ambient, almost a drone, under everything"),
    ],
    "voice-prisoner-sets": [
        # Each candidate is one prisoner's whole set (an adult voice, kept for the run); the game needs eight.
        O("call", "A prisoner calling for help ('help', 'in here', 'over here'), one voice's takes", vars=5),
        O("shout", "A wordless shout or cry from the same voice", vars=3),
    ],
    "store-trailer": [
        O("mix", "The trailer's soundtrack, cut from the game's own sounds", vars=1),
    ],
}


def built(line):
    """Built candidates for a line, by cue: {cue: [manifest entry with its page url]}, uploaded ones only."""
    import assets
    import build
    global _MANIFEST
    if _MANIFEST is None:
        _MANIFEST = build.manifest()
    out = {}
    for e in _MANIFEST.values():
        if e["line"] != line:
            continue
        url = assets.url_for(e["preview"])
        if url is None:
            print(f"  not uploaded yet: {e['id']} ({e['preview']})", file=sys.stderr)
            continue
        out.setdefault(e["cue"], []).append(dict(e, url=url))
    return out


_MANIFEST = None


def store_cue(c, made=()):
    """The cue as the checklist store holds it: library files and earlier keepers from CUES, then built candidates."""
    cands = []
    for mat, keys in c["cand"].items():
        for k in keys:
            if k.startswith("old:"):
                cands.append({"mat": None if mat == "_" else mat, "src": k[4:], "label": "Kept from the earlier pass", "old": True})
            else:
                src = lib(k)
                cands.append({"mat": None if mat == "_" else mat, "src": src, "label": k.split(":")[1], "old": False})
    for e in made:
        k = {"mat": e["mat"], "src": e["url"], "label": e["label"], "old": False, "key": e["key"], "how": e["how"],
             "sources": e["sources"], "takes": e["takes"], "restricted": e["restricted"], "seconds": e["seconds"]}
        if "inBand" in e:
            k["inBand"], k["band"] = e["inBand"], e["band"]
        cands.append(k)
    status = "silent" if c["silent"] else ("candidate" if cands else "needs")
    out = {k: c[k] for k in ("id", "event", "kind", "vars", "mats", "need")}
    out.update(status=status, cands=cands)
    return out


def carry_verdicts(line, cues, existing):
    """Keep every verdict the store already holds: on a cue's candidate (same cue, same file) or on an earlier-pass take."""
    seen = {}
    for c in existing.get("cues") or []:
        for k in c.get("cands") or []:
            if k.get("verdict"):
                seen[(c["id"], k["src"])] = k
    refs = {r["file"]: r for r in existing.get("refs") or [] if r.get("verdict")}
    for c in cues:
        for k in c["cands"]:
            old_k = seen.get((c["id"], k["src"])) or refs.get(k["src"])
            if old_k:
                for f in ("verdict", "verdictBy", "verdictAt"):
                    if old_k.get(f):
                        k[f] = old_k[f]


def main():
    total = sum(len(v) for v in CUES.values())
    files = sum(max(1, len(c["mats"])) * c["vars"] for v in CUES.values() for c in v if not c["silent"])
    loops = sum(1 for v in CUES.values() for c in v if c["kind"] == "loop")
    with_cand = sum(1 for v in CUES.values() for c in v if c["cand"])
    print(f"{len(CUES)} lines, {total} cues ({loops} loops), {files} files to make in all, {with_cand} cues with a library candidate")
    if "--store" in sys.argv:
        import os
        out = sys.argv[sys.argv.index("--store") + 1]
        src = sys.argv[sys.argv.index("--from") + 1] if "--from" in sys.argv else None
        if src is None:
            sys.exit("--store needs --from DIR (the store's items) so the director's verdicts are carried over")
        os.makedirs(out, exist_ok=True)
        for line, cues in CUES.items():
            path = os.path.join(src, line + ".json")
            if not os.path.exists(path):
                sys.exit(f"{line} is not in the store ({path})")
            existing = json.load(open(path))
            existing = existing.get("data", existing)
            made = built(line)
            stored = [store_cue(c, made.get(c["id"], ())) for c in cues]
            for cue in set(made) - {c["id"] for c in cues}:
                print(f"  {line}: built candidates for a cue CUES doesn't list: {cue}", file=sys.stderr)
            carry_verdicts(line, stored, existing)
            json.dump({"cues": stored}, open(os.path.join(out, line + ".json"), "w"))
        print("wrote", len(CUES), "files to", out)


if __name__ == "__main__":
    main()
