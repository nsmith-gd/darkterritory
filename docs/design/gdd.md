# DARK TERRITORY
## Game Design Document — v1.0, September 2026

**Two to eight players crew an armoured freight train through a corrupted wilderness. Load what you can. Deliver what survives.**

*Dark territory is a real railroad term: track with no functioning signal system, where trains move on verbal authority alone. When communication fails, people die.*

---

# PART ONE — THE GAME

## 1. Overview

| | |
|---|---|
| **Genre** | Co-operative survival horror |
| **Players** | 2–8, ideal at 3–4 |
| **Session** | 25–45 minutes per run |
| **Perspective** | First person |
| **Platform** | PC / Steam, premium |
| **Price** | $9.99–$14.99 |
| **Comms** | Proximity voice — a hard dependency, not a feature |

### Design pillars

**1 — The train is escape route and death trap, same object, all night.**
Jump off at speed and you die. Get left behind and you die. Derail and everyone dies. It is the safest place in the world right up until it isn't, and you cannot simply get off.

**2 — Movement keeps you alive; everything valuable requires stopping.**

**3 — Every rule is a sentence somebody has to scream clearly at a person who is already screaming.**

**4 — You get stronger by getting bigger, and bigger is the problem.**

**5 — Almost nothing ends the night. It just gets worse and funnier.**

---

# PART TWO — THE WORLD

## 2. Premise

Decades ago, a comet broke apart in Earth's atmosphere.

Most of it burned away, but dust, fragments and microscopic material spread across the planet. The sky darkened beneath a permanent atmospheric haze. Daylight became weak and grey. Nights became bitterly cold.

Something inside the comet survived.

The **Corruption** spread through living organisms, twisting them over generations. It does not simply make animals larger or more aggressive. **It exaggerates whatever allows something to survive.**

Predators became better predators. Scavengers became hunters. Prey became territorial and violent. Humans became something stranger.

Civilization collapsed into isolated fortified settlements. Between them lies the **Dark Territory**.

The only infrastructure capable of reliably crossing it is the railway.

## 3. The remaining human world

Humanity no longer controls territory. It controls **points on a map**.

Fortified towns survive behind stone and steel walls, floodlights, watchtowers, artillery, furnaces, rail yards and warehouses. Outside those walls, humanity has almost no permanent presence. A settlement may be separated from the next surviving town by hundreds of kilometres of abandoned countryside.

This makes every town dependent on freight. One settlement produces coal. Another grows food. Another manufactures ammunition, or operates foundries, or produces medicine.

No town is self-sufficient. **The railway is what makes civilization possible.**

## 4. The player's place in it

The players are freight crews. Not elite soldiers. Not monster hunters. Not chosen heroes.

They are the people willing to take heavily armoured locomotives through the Territory and keep the surviving settlements connected. It is dangerous work and extraordinarily valuable. Experienced crews become wealthy because everyone needs them and few survive long enough to get good at it.

> **Keep the train moving. Get the cargo through. Come home richer than you left.**

## 5. The rail network

The surviving railway was built before the Corruption. Much of it remains physically intact, but the systems that supported it are gone.

Crews travel abandoned passenger lines, industrial spurs, mining railways, mountain routes, forest lines, bridges, tunnels, marsh crossings and dead towns. Most stations are abandoned. Signal infrastructure is unreliable or destroyed.

Movement therefore depends on radios, track signs, manual switches, signal lamps, and crew communication. **The railway itself is part of the challenge.**

## 6. Darkness and cold

The sky never truly clears. Day is dim. Night is nearly absolute. Human settlements survive on artificial light, and the railway pushes that light outward into the wilderness.

A moving train appears from a distance as **a small glowing machine crossing an enormous black world.** That is the game's fundamental image.

Nighttime temperatures collapsed after the atmospheric catastrophe. The locomotive provides more than transport — it provides heat. It is a moving pocket of human survivability. Players outside are exposed to monsters, wind, extreme cold, falling and isolation.

This is why being left behind is a death sentence, and why it doesn't need to be enforced arbitrarily.

## 7. Why the train must keep moving

The wilderness notices trains. A locomotive produces vibration, heat, light, smoke, sound, scent and food.

Different creatures react to different parts of that signature. Some avoid it. Some follow it. Some hunt it. Some have adapted specifically to railways.

## 8. Dawn

The crew must reach the destination before dawn — not because monsters disappear, but because **dawn reopens the main railway network.** Passenger trains, industrial traffic and scheduled freight begin moving.

Your crew operates inside a dangerous nighttime freight window. Miss it and the railway itself becomes a threat, because dispatch can no longer guarantee the line ahead is clear.

---

# PART THREE — CORE LOOP

## 9. Run structure

```
FORTRESS → WILDERNESS → FACILITY → WILDERNESS → TERMINUS
```

### Departure
Inside the walls: purchase equipment, repair or upgrade the train, choose freight contracts, add or remove railcars, stock coal, ammunition, lamps, repair supplies and tools.

Then the gates open.

### The threshold
The transition should be a major tonal moment. Inside: workers, lights, machinery, voices, guards, whistles, industrial noise. Then the outer gates open, the train passes the final defensive towers, the lights disappear behind it, and ahead is only track.

**This is where the run actually begins.**

### Arrival
Eventually the crew sees lights. Then walls. Then gun towers. The gates open and the train crosses back into civilization.

**Everything still attached to the locomotive counts.** Cargo is unloaded, surviving players are paid, and lost cars, ammunition, equipment and crew become the cost of the run.

## 10. The consist

| Section | Contains |
|---|---|
| **Engine car** | Conductor, boiler, forward gun. Heat, light, the throttle. |
| **Cargo cars** | The middle. Growing. Unarmed. |
| **Guard car** | Rear gun, tool storage, the back door. |

Early crews run **engine plus one or two cars.** Experienced crews run **engine, armour, guns, utility cars and many freight cars.**

The train should increasingly feel like home. Players learn where guns are mounted, where ammunition is stored, where the repair kits sit, where emergency lamps are kept, where coal is stored, where tools hang.

That familiarity matters because the train gets more complex over time. **Progression literally makes your home harder to defend.**

## 11. Progression

Successful deliveries earn money. Money buys capability. Capability allows more freight. More freight means more railcars — and more weight, slower acceleration, longer braking, more vulnerable cargo, more distance between players, more blind spots, more repair problems, harder facility loading, greater exposure.

**Difficulty is produced by success rather than selected from a menu.**

### Route tiers

| Tier | Description |
|---|---|
| **Local routes** | Short, relatively safe connections between major towns |
| **Frontier routes** | Longer lines through abandoned territory |
| **Dead lines** | Railways to settlements that have stopped responding |
| **Deep territory** | Old industrial regions where Corruption is far more severe |

You aren't picking a difficulty level. You're travelling farther from civilization.

---

# PART FOUR — CREW

## 12. Roles

**Four roles, none of them assigned.** You are whatever the train needs where you happen to be standing.

| Role | Where | Does | Blind to |
|---|---|---|---|
| **Conductor** | Engine car | Throttle, brake, whistle, reverse | Everything mechanical, everything behind |
| **Boiler** | Engine car | Fuel, pressure, heat, water | Outside entirely. No windows. |
| **Gunner** | Engine or guard car | Mounted, arc-limited, loud | Whichever direction they aren't facing |
| **Engineer** | Anywhere | Carries the repair kit | Nothing — but has no firing arc |

### Fluidity

**The repair kit is an item, not a station.** The engineer is whoever picked it up. There is no post to be stuck at — there's a toolbox somebody grabbed, and when they die on the roofs it's lying in car four and someone has to go and get it.

Roles emerge from position. You end up on the gun because you're standing at the gun.

## 13. The indefensible middle

Mounted guns protect the locomotive and the rear. Long trains inevitably develop blind areas, and **cargo cars in the middle cannot be protected by any firing arc.**

If something climbs aboard there, someone leaves the enclosed cars and moves across ladders, platforms, roofs and couplings — at speed, in the cold, with no gun.

**The longer the train becomes, the more often players must physically enter the Territory to protect it.**

## 14. Noise

Sound is a primary ecological signal. A train is already loud. Gunfire makes it worse.

Every shot is a decision. Shooting solves the immediate problem while potentially alerting something farther away. **The gunner's job is less about accuracy than restraint.**

Sometimes the correct response to a monster is to do nothing.

## 15. Crew scaling

| Crew | Train | Feel |
|---|---|---|
| **2** | Short | Pure triage. Constant sprinting, constant bad choices about what to leave burning. |
| **3–4** | Standard | The intended fit. Both guns crewed only when nobody's on the ground. |
| **5–8** | Long | Long enough that half the crew can't hear the other half. Eight people on proximity voice is its own difficulty. |

Scaling is handled by **train length**, not enemy count multipliers.

---

# PART FIVE — LOADING

## 16. Freight facilities

The railway still connects to abandoned industry: coal mines, lumber yards, foundries, grain elevators, chemical works, slaughterhouses, machine factories, military depots.

Most are no longer inhabited. Some machinery still functions. Others require crews to restart equipment manually.

## 17. Why loading is dangerous

Facilities were built for industrial efficiency, not monster survival. Most cannot accommodate a full armoured freight train, so **the crew must break the consist apart.**

- **Decouple the engine.** Loaded cars remain behind, parked, with whatever is out there.
- **Take empty cars into the facility.** The conductor drives into a place built for machines, not people.
- **Switches are thrown by hand.** Someone is on the ground at every junction, alone, calling the route.
- **Start the loading machinery.** Chutes, cranes and winches — all slow, all loud.
- **Reverse out and recouple**, ideally with everyone aboard.

**The safe moving fortress becomes several isolated pieces.** This is the game's largest period of vulnerability and its best set piece.

> **The engine can leave without you.**

Not a punishment mechanic — the whole tension of the sequence. The person on the switch is a hundred metres from the only thing that outruns anything, and the conductor genuinely might not know they're not aboard.

## 18. Facility roster

| Facility | Cargo | The coordination problem |
|---|---|---|
| **Coaling tower** | Fuel | Gravity chute. Fast, deafening, fills whether you're ready or not. |
| **Grain elevator** | Bulk, cheap | One spout, one car at a time. Endless repositioning and switch calls. |
| **Foundry** | Heavy, valuable | Overhead crane run from a gantry. The operator can't see the ground crew. |
| **Switchyard** | Mixed | Cars scattered across six sidings. A live track puzzle solved by shouting. |
| **Wreck yard** | Salvage, high value | Pull cargo off derailed trains. Unstable, unlit, already occupied. |
| **Slaughterhouse** | Livestock, food | Animals are loud, and something already lives here. |
| **Chemical works** | Volatile, top payout | Leaks. Do not shoot indoors. |
| **Mine head** | Ore | The spur descends underground. Radio blackout in and out. |
| **Military depot** | Ammunition | Best payout, worst cargo to be carrying when something boards. |

**Every facility is optional. Skipping them is safe and poor.** The payout exists to force bad decisions, not to reward good ones.

## 19. Cargo

Cargo is physically represented and **changes the run** rather than just scoring it.

| Cargo | Complication |
|---|---|
| Coal | Burns |
| Timber | Burns, heavy |
| Ammunition | Explodes |
| Chemicals | Leaks, toxic |
| Livestock | Makes noise constantly |
| Food | Attracts scavengers |
| Medicine | Fragile, high value |
| Machine parts | Heavy, inert, safe |
| Comet-derived material | Attracts everything |

---

# PART SIX — ENEMIES

## 20. Design philosophy

**Fixed, hand-tuned, fully learnable. Mastery is the product and the wiki is welcome.** Retention comes from regular roster updates post-launch, not from obscuring the rules.

Everything that survived the Corruption found a niche. Creatures hunt by sound, movement, heat, scent, light or vibration. Some attack trains, some wait near resources, some live inside industrial ruins, some follow tracks because trains reliably bring food.

**The danger comes from combinations.** A creature attracted to light is far worse when another destroys your lamps. A creature attracted to noise is far worse when another forces you to fire.

### The five-point test

Every enemy must pass all five. This is the filter for post-launch additions too.

1. **The rule fits in six words**
2. **It makes you suppress an instinct**
3. **Breaking it punishes you immediately** — no delayed consequences
4. **One death teaches it**
5. **It's fun to scream**

Criterion 3 is the one that kills most designs. If the cost arrives twenty minutes later, nobody learns anything.

## 21. Roster — 21 enemies

Demo ships with **five**: one per pressure zone.

### FORWARD — the front gun answers

**SLEEPERS** · *vibration*
Lie across the rail, shaped like ties. Invisible until lit.
> **RULE: watch the road.**
Derail risk. Kills everyone.

**THE FERRYMAN** · *sight*
Stands on the track ahead holding a lantern, waving you down.
> **RULE: do not slow down.**
Every instinct says brake. Braking is how you die.

**THE LONG WHISTLE** · *sound*
Sounds a horn on the line ahead. There is no train ahead.
> **RULE: don't trust the horn.**
Punishes the correct safety instinct. Emergency braking on a grade.

**GREASE** · *scent*
Slicks the rail. No traction — can't climb, can't stop.
> **RULE: sand the rail.**
Sends someone onto the running boards at speed.

### REAR — the guard gun answers

**CINDER HOUNDS** · *heat, scent*
Pack. Run the line behind you, gaining on every grade.
> **RULE: keep the rear gun crewed.**
Only the gun stops them — and the gun brings the Choir.

**THE WEIGHT** · *vibration*
Takes hold of the last car and drags. Speed bleeds off.
> **RULE: uncouple, or go back along the roofs.**
Cargo versus survival, on a clock.

**FOLLOWERS** · *scent*
Attach to someone on the ground and ride home with them.
> **RULE: check people before they board.**
Means telling a friend to stay outside in the dark.

### FLANK — nothing answers

*The middle of the train has no firing arc. Handled on foot, on the roofs, with tools.*

**CLINGERS** · *vibration*
Latch onto cargo hulls and drill through. Audible, locatable.
> **RULE: go out and take them off.**

**CLIMBERS** · *movement*
Come up between cars, onto the roofs, then in.
> **RULE: hold the gaps between cars.**
Scales brutally with train length.

**DRAGGERS** · *movement*
Reach up from beneath the car edges.
> **RULE: stay off the edges.**
Makes roof traversal a route decision.

**THE GAUNT** · *sight*
Rides the roof. Moves only when unobserved.
> **RULE: keep eyes on the roof.**
Costs you a person who can now do nothing else.

**THE DRIFT** · *movement*
Runs alongside, spreading toward movement.
> **RULE: stop moving.**
Every panic instinct is wrong.

### INTERIOR — already aboard

**RATTLE** · *vibration*
Lives in the couplings. You hear it before you cross.
> **RULE: don't cross between cars rattling.**
Isolates the crew instantly. Pure audio tell.

**STOKER** · *heat*
Gets into the firebox. Pressure climbs on its own.
> **RULE: vent, or the boiler goes.**
Rupture is spectacular, loud, occasionally survivable.

**HOLLOW** · *heat*
Comes down the smokestack when the fire burns low.
> **RULE: keep the firebox hot.**
Gives boiler neglect a face.

**DEADMAN** · *absence*
Takes the cab if nobody is in it.
> **RULE: never leave the cab empty.**
Structurally enforces crew distribution. Punishes everyone piling out at a facility.

### STRUCTURAL — attack how you run the train

**THE CHOIR** · *sound*
Drawn to gunfire. The more you shoot, the more arrive.
> **RULE: hold fire.**
The keystone. Turns both gunners into decision-makers and makes every rear threat an argument.

**LAMPLIGHTERS** · *light*
Light-reactive. Work the lineside.
> **RULE: lamps down.**
Contradicts everything that needs forward visibility.

**SOOT CHILDREN** · *sound*
Outside in the dark, calling for help in your crewmates' voices.
> **RULE: never answer a voice from outside.**
Weaponises the proximity voice channel. The failure is opening a door for a friend who is standing right next to you.

### CORRUPTED HUMANS

*The rarest and most disturbing. The Corruption preserves fragments of learned behaviour — a railway worker still operates switches, a miner still returns underground, a guard still patrols. Terrifying because some piece of human cognition remains, and players cannot always tell whether they are dealing with instinct or intelligence.*

**THE SWITCHMAN**
A corrupted railway worker still doing its job. Throws junctions wrong ahead of you.
> **RULE: verify every switch on the ground.**
Costs a stop and a person.

**THE PASSENGER**
Mimics a crew member. Somewhere in the consist.
> **RULE: verify people.**
Social paranoia engine. Proximity voice does the work.

---

# PART SEVEN — WORLD SYSTEMS

## 22. Hazards

**Hazards remove your tools. They never change enemy rules.** This is what keeps a fixed, fully-documented roster dangerous at hour two hundred. The answer is always known — tonight it simply isn't available.

| Hazard | Takes away |
|---|---|
| **Climbing grade** | Stopping and slowing. Lose momentum, lose the summit. |
| **Descending grade** | Speed control. Brakes heat and fade. |
| **Curves** | Forward sightline. The lamp shows you nothing. |
| **Fog** | Identification at range. Everything is a shape. |
| **Wet rail** | Traction, and every speed-based answer with it. |
| **Deep cold** | Boiler efficiency, and survival time outside. |
| **Wind** | Sound discipline. Gunfire carries much further. |
| **Washout** | Route options. Forces the bad junction. |
| **Weak bridges** | Length. Your longer, richer train may not clear it. |
| **Tunnels** | Radio. Compressed proximity voice, no exterior reference. |
| **Brass growth** | Speed. Cut through slowly or ram it and pay. |
| **Dawn** | Time. Every careful option becomes unaffordable. |

The line is generated per run: length, grades, curves, junction topology, facility placement, tunnels and bridges.

### Example pressure stack — all known parts

| Present | Demands | Conflict |
|---|---|---|
| The Choir | Hold fire | Cinder Hounds are on the rear and only the gun stops them |
| Lamplighters | Lamps down | Sleepers on the line need the forward lamp to be seen at all |
| 3% grade | Keep speed up | Clingers are in the middle, and nobody goes on the roofs at speed |

Nothing there is a mystery. It's still a disaster.

## 23. Failure

**Four things kill outright:**

- **Jumping at speed** — the train is a trap by design
- **Getting left behind** — cold and distance do the rest
- **Derailment** — kills the entire crew at once
- **Boiler rupture** — loud, spectacular, occasionally survivable

**Everything else cascades:**

| Failure | Consequence |
|---|---|
| Boiler dies | Coasting on grade and momentum, brakes only |
| Lights fail | Navigate through darkness on shouted landmarks |
| Radio breaks | Shouting down the length of a moving train |
| Gun jams | Someone repairs it by hand, under fire |
| Car catches fire | Save it or abandon it |
| Repair kit lost in car four | Somebody's going out there |
| Crew lost on the ground | Two guns, one gunner, pick a direction |

**Rolling into the terminus with half a crew, three cars and a fire is the good ending.**

## 24. Decoupling

Leaving something behind is a defining choice of the world. A damaged car. Burning cargo. A stranded player. A valuable shipment. A monster-infested section.

> Save the cargo or save the locomotive?
> Go back for a player or keep moving?
> Repair the car or abandon it?

The railway makes these choices physical. **Once the coupler releases, whatever is behind you disappears into the Territory.**

---

# PART EIGHT — ART DIRECTION

## 25. Core style statement

Dark Territory should look like a scary late-PS2 / early-PS3 co-op horror game with a deliberately rough, low-poly, pixelated finish — similar in spirit to the ugly-charming readability of Lethal Company, but pushed one quality tier higher in atmosphere, silhouette design, train detail and environmental storytelling.

**This is not PS1 retro, and not modern clean stylization.** It should feel industrial, grimy, fog-heavy, mechanically readable, slightly degraded, uncanny, and cheap in the right ways — terrifying because visibility and fidelity are imperfect.

> The player should feel trapped inside a badly lit, failing industrial machine moving through a world that already lost.

### Visual north star

**Industrial survival horror through deliberate visual limitation.**

The art sells three truths at once, and the tension between them is the whole look:

1. The train is your home
2. The outside world is poison
3. You still have to go outside

## 26. Art pillars

### 1 — Readability through silhouette, not detail

The game lives or dies on players reading danger fast, at night, in fog, at distance. The art language prioritises big readable shapes, clean silhouettes, recognizable profiles, simple value grouping and clear role distinction.

**If something only reads because of tiny texture detail, it is wrong.**

- Trains need bold profile language
- Cars read by shape first
- Enemies are identifiable by body outline before you understand them
- Props are chunky and functional

### 2 — Deliberately degraded fidelity

Controlled ugliness. Not bad art — intentional roughness. Low-to-mid poly models, low-res textures, visible texture noise and compression feel, broad lighting rather than hyper-detail, harsh speculars on metal, modest material complexity, slightly stiff presentation.

> A half-broken remembered game from 2006 that became horrifying by accident.

More polished than Lethal Company. Less polished than modern stylized horror. **Artful jank.**

### 3 — Atmosphere does the heavy lifting

Darkness, fog, smoke, steam, rain, sparks, furnace glow, lantern light, silhouettes in haze.

Atmosphere is not decoration, it is a core rendering tool. It hides poly limits, unifies scenes, creates fear through partial information, makes practical lights matter, and produces the warm-interior / hostile-exterior contrast the whole game runs on.

### 4 — The train is the protagonist

The locomotive and consist are the main visual identity. Not transport — fortress, workplace, social space, objective, failure machine, moving level.

The train must feel armoured, heavy, temperamental, repairable, hand-operated, and dangerous even when safe. **Every car visually communicates its gameplay role.**

| Car | Visual read |
|---|---|
| Engine car | Dominant, mechanical, boiler-heavy, hot, loud |
| Guard / gun car | Mounted weapon silhouette, firing arc implied |
| Cargo car | Boxy, exposed, vulnerable |
| Crew / utility car | Cramped, lamp-lit, human-scale |
| Armoured variant | Reinforced plating, heavier mass |
| Upgraded cars | Same base language, more defensive complexity |

### 5 — Contamination, not magic

The Corruption should feel like invasive biological wrongness, not fantasy glow goo.

**Avoid:** generic purple corruption, clean sci-fi mutation, magical energy effects.
**Prefer:** swollen flesh, mineralized growths, fungal crust, stretched skin, extra limbs, fused anatomy, joint distortion, pale sacs — coal soot, rot, oil and tissue mixing together.

> The strongest monsters are the ones where you can still tell what they used to be.

### 6 — Human-made survival vs. organic intrusion

| Human world | Corrupted world |
|---|---|
| Straight lines, rivets, steel plates | Asymmetry, swelling, creeping spread |
| Lanterns, switches, rail ties, ladders | Malformed anatomy, contaminated undergrowth |
| Cabs, gauges, crates | Things hiding in mechanical spaces |
| Discipline | Living matter in the wrong place |

Civilization trying to remain procedural and practical while horror refuses to behave.

### 7 — Fear comes from exposure

The art must constantly reinforce protected versus exposed, because the gameplay is about forcing players to leave safety.

| Protected | Exposed |
|---|---|
| Engine interior | Roofs |
| Crew car | Ladders and couplings |
| Near furnace glow | Rail-side switching |
| Lamp-lit work areas | Loading yards |
| Behind mounted gun positions | Ground away from the train, any dark stretch between lit zones |

## 27. Asset creation standards

### Geometry targets

Efficient, readable geometry with strong shape language. Readability comes first — these are guides, not rules.

| Asset class | Triangles |
|---|---|
| Small props | 200–800 |
| Medium props | 800–2,500 |
| Large props / machines | 2,000–8,000 |
| Characters | 4,000–10,000 |
| Large monsters | 8,000–16,000 |
| Locomotive / hero car | 25,000–60,000 depending on modularity |

**Prioritise:** chunky forms, big bevels, obvious planes, faceted cylinders, exaggerated industrial shapes, mechanical silhouettes, visible simplification.

**Avoid:** tiny bevel obsession, micro surface detail, clean CAD perfection, over-smoothed models, realistic clutter everywhere.

### Texture targets

| Asset class | Density |
|---|---|
| Environment | 128–256 px/m |
| Hero assets | 256–512 px/m |
| Major hero pieces | 1024 occasionally |

Visible grain, broad rust fields, chipped paint, soot, grime, oil staining, baked shadows, hand-authored roughness breakup, subtle pixel crawl. **If it looks like a pristine Substance demo, it is off target.**

### Material families

Restrained vocabulary throughout.

| Material | Treatment |
|---|---|
| Iron / steel | Dark, oily, cold |
| Brass / copper | Muted warm metal, tarnished |
| Painted metal | Chipped, faded, practical |
| Wood | Dark, worn, rough grain |
| Glass | Dirty, dark, small reflective highlights |
| Flesh / corruption | Waxy, bruised, damp, wrong |

## 28. Lighting and palette

Lighting is one of the primary style carriers: strong darkness, warm practical lights, cool ambient night, deep shadow pockets, headlamp and lantern cones, furnace glow, limited visual certainty, mist and smoke volume.

**Inside = warm, human, temporary safety. Outside = cold, unreadable, predatory.**

Lighting should constantly ask: *can I actually tell what I'm looking at?*

| Group | Colours |
|---|---|
| **World base** | Charcoal, soot black, deep brown, rust red, dull iron grey, tarnished brass, desaturated blue-grey, muddy olive |
| **Warm accents** | Furnace orange, lamp amber, ember red |
| **Corruption accents** | Bruised violet, dead ivory, blackened red, fungal beige, bile green in moderation |

Don't let corruption become neon soup. Less is more.

## 29. Characters and enemies

### Crew

Practical, rail-working, soot-covered, bundled against cold, slightly anonymous. **Ordinary people in bad circumstances, not heroic fantasy silhouettes.**

Heavy coats, caps and helmets, lanterns, gloves, harnesses, belts, repair tools, boots made for wet metal and ballast. They should read as fragile humans trapped in industrial systems.

### Enemies

Mechanically readable and behaviour-legible from silhouette. Each enemy communicates where it attacks from, what it wants, whether it is territory / cargo / crew pressure, and whether it is a stealth, pressure or panic threat.

Silhouette tied to function. One clear body-language idea. Asymmetric but readable. Corrupted, not abstract. **Native to the rail line** — these things belong near tracks, steam, mud and industrial ruin. Unnerving in stillness, not just in aggression.

## 30. Environment

A **rail corridor civilization** — not fully urban, not pure wilderness. Fortified towns, rail bridges, marshland, black forests, loading facilities, slag heaps, foundries, mining infrastructure, telegraph poles, switching yards, abandoned sidings, dead settlements, ruined refineries, contaminated rural spaces.

Facilities should feel oversized, dangerous, partially abandoned, barely operable, dimly lit, and **too big for the train to handle comfortably** — visually supporting the split consist, the switch throwing, the separated groups, the ground exposure and the delayed rescue.

## 31. Animation and VFX

Clear and slightly rough, never hyper-smooth.

**Humans:** heavy, grounded, readable, a bit stiff, practical, hurried under stress.
**Monsters:** unnaturally still when observed, disturbing changes in pace, abrupt turns, lurches, too-fast corrections, subtle desync between intent and motion.

**VFX supports readability rather than overwhelming it:** steam venting, sparks, smoke trails, muzzle flashes, cinders, lantern sway, rail grit, furnace flare, drifting fog, subtle corruption particulate. Mechanical first, supernatural second.

## 32. Art serves the voice channel

**This is the connective tissue between art and every other system in this document.**

Because the game is voice- and communication-driven, the visuals exist to let players call things out fast. That requires distinct car silhouettes, identifiable roof spaces, readable gap and coupling danger, good landmarking along the train, enemies describable in a phrase, obvious light states, and visually unmistakable switches, ladders and exterior interaction points.

> **If players can't verbally describe the game state quickly, the art is failing the design.**

Silhouette legibility is not an aesthetic preference here. It is a coordination mechanic, and it sits alongside the six-word enemy rule as one of the two things that make the shouting work.

### The screenshot test

If a screenshot reads as *a rough, low-poly industrial horror game where a steam train full of desperate workers is crossing a diseased frontier at night, and the darkness itself feels operationally dangerous* — it is on target.

### Production note

The direction is schedule-compatible, which is not incidental. Deliberate low fidelity, restrained materials, heavy fog and darkness, and modular car construction all reduce asset cost and hide the seams that a 26-week build will inevitably have. **The look and the schedule are pulling the same direction.**

---

# PART NINE — TECHNICAL

## 33. Netcode architecture

Authoritative host-client, validated in prototype.

- **Server is authoritative.** Clients send intent only — which keys are down, and for how long. Never positions.
- **Fixed 30Hz tick.** Server owns truth and broadcasts snapshots.
- **Shared simulation module** loaded identically by server and client. Divergence causes rubber-banding, so this is the foundation.
- **Client prediction.** Input applies locally on press — zero perceived latency on your own movement at any ping.
- **Reconciliation.** Sequence-numbered inputs; server echoes last consumed; client discards acknowledged, snaps to truth, replays what's in flight.
- **Entity interpolation.** Remote players rendered 100ms in the past, lerped between buffered snapshots.
- **Anti-cheat baseline.** Client-supplied `dt` clamped server-side.

**Still needed:** interest management, delta compression, and lag compensation for hitscan weapons (required before gun arcs feel fair).

**Proximity voice is a hard dependency.** No voice, no game.

## 34. Agent verification harness

Embodied in-engine agents play the game headlessly. A fixed roster makes this an exhaustive problem rather than an open-ended one.

| Check | Method |
|---|---|
| **Combination fairness** | Every pair and triple in the roster against every hazard set. Flag unwinnable or trivially solved. |
| **Crew-size sweep** | Survivable at 2, non-trivial at 8 |
| **Train-length sweep** | Does the flank stay defensible as the consist grows? Where is the real progression cap? |
| **Cascade audit** | No failure chain is unrecoverable |
| **Degraded comms** | Agents on lossy, laggy, restricted voice — simulating people shouting over each other |
| **Stability** | Desync and crash detection across 2–8 clients |

**Agents cannot tell us whether it is funny.** That is the director's call, and it is the only judgement the build genuinely requires from a human.

---

# PART TEN — PRODUCTION

## 35. Schedule — 26 weeks

| Phase | Weeks | Work |
|---|---|---|
| 1 | 1–4 | Train sim, consist and coupling, damage model |
| 2 | 5–8 | Netcode and proximity voice |
| 3 | 9–13 | Procedural line, grades, junction topology, hazards |
| 4 | 14–17 | Demo roster of five, gun arcs, two facilities |
| 5 | 18–21 | Agent harness and full balance sweep |
| 6 | 22–26 | Art integration, audio, demo build, Next Fest prep |

## 36. Gates

| Date | Gate |
|---|---|
| Early Oct 2026 | Coming Soon page live |
| Dec 2026 | Public demo. Demos live ahead of the fest earn ~2.5× the wishlists. |
| **15 Dec 2026** | **Checkpoint. Under ~600 wishlists/week and the target isn't happening.** |
| ~4 Jan 2027 | Next Fest registration closes |
| 18 Jan 2027 | Valve pulls trailers for the official event trailer |
| **22 Feb 2027** | **Steam Next Fest. One per game, ever.** |
| Mid-late Mar 2027 | Launch |

## 37. Commercial case

| Metric | Target |
|---|---|
| Wishlists at launch | **50,000** |
| Sustained rate required | **~850/week for 24 weeks** |
| Projected Q1 net | **~$87,500** |

**Position.** Extraction horror is the fastest-growing indie horror genre on Steam, so "extraction with a twist" is the baseline rather than a position. Quota-plus-proximity-chat is held by Lethal Company (97% positive, 276,000+ reviews, est. 10–15M copies). Physics hauling by R.E.P.O. ($147M, ~300K peak concurrent, 2025's best seller). Verticality by PEAK ($87M). Filming by Content Warning. Unstable vehicles by RV There Yet?. Adaptive creature AI by FEEDERS.

**Every one of those runs a single space.** A facility, a mountain, a house. None of them run a vehicle the crew must keep alive while repeatedly leaving it.

**Demand.** Psychological and atmospheric horror is the strongest demand cluster on Steam — 572% wishlist growth over 90 days against a platform average near 1%, and 65.5% sales growth against 4.6%. Co-op holds a 3,200 median wishlist count versus 900 for roguelites. The intersection is the position.

**Arithmetic.** At ~$10 average gross per unit, Steam's 30% leaves ~$7 net. 50,000 launch wishlists at the 0.10x first-week benchmark for games above $10 gives ~5,000 week-one units. A first week at ~40% of the quarter puts Q1 near $87,500 net — enough headroom that $60,000 is a base case rather than a coin flip.

*The underlying conversion benchmark swings 10–20x between games. This is planning arithmetic, not a forecast.*

---

# PART ELEVEN — OPEN QUESTIONS

1. **Do guns use ammunition, or is heat the limit?** Ammo makes scarcity a cargo decision — and cargo space is already contested. Heat keeps it about rhythm. Ammo is probably better because it ties the gunner to the economy.
2. **Can the conductor see whether everyone is aboard, or is roll call purely verbal?** Verbal is funnier and considerably crueller.
3. **How many cars before the flank is genuinely indefensible** — and is that ceiling the real progression cap?
4. **Do corrupted humans ever use the radio?** A voice on your own channel that isn't crew is the single most disturbing thing available in this design. Possibly too much.
5. **Does cold need a meter,** or is it enough that being outside too long simply kills you?
6. **How do enemies stay silhouette-legible in heavy fog at distance?** The art direction wants fog doing the atmospheric work, and the design wants creatures identifiable before you understand them. Those pull against each other. Likely answers: rim lighting from practical sources, or enemies that are legible by *motion* signature before shape.
7. **Do cargo cars visually show what's inside?** Ammunition versus livestock versus chemicals changes how the crew should behave around a car. If the exterior reads it, callouts get faster. If not, the interior becomes worth learning.
8. **Is corruption ever visible on the train itself** — growth in the couplings, in the tender, under the cars? It would give the flank a visual tell and make the home slowly stop feeling like home.

---

---

# APPENDIX A — BEHAVIOUR TREES

## A.1 The shared skeleton

Every enemy runs the same five-state spine. Consistency is deliberate: it makes creatures learnable across the roster, and it makes them cheap to implement, which matters on a 26-week build.

```
DORMANT ──[sense trigger]──► ALERT ──[tell fires]──► TELEGRAPH
                                                          │
                                              [reaction window: 1.5–4s]
                                                          │
                                                       COMMIT
                                                          │
                                            ┌─────────────┴─────────────┐
                                     [rule obeyed]              [rule broken]
                                            │                          │
                                       BREAK OFF                    PUNISH
                                            │                          │
                                            └────────► DORMANT ◄───────┘
```

**The fairness contract: TELEGRAPH always precedes COMMIT.** No enemy in this game may punish a player who was given no window. This is the single rule the agent harness checks hardest.

**Sense triggers** are one of: sound, light, heat, movement, scent, vibration, sight, absence. Each enemy uses exactly one primary. Creatures sharing a trigger must not share a pressure zone, or their tells become indistinguishable.

---

## A.2 Forward

### SLEEPERS · vibration
```
DORMANT   motionless across the rail, silhouette reads as ties
          └ TELEGRAPH: forward lamp at >120m → subtle writhe reveals shape
BRACE     train within 60m → they set themselves; never flee
IMPACT    speed >40  → DERAIL (run ends)
          speed 20–40 → heavy damage, car integrity loss
          speed <20  → crushed, minor jolt
```
Never pursues. Purely positional. **Requires the forward lamp to be lit**, which is what puts it in direct conflict with Lamplighters.

### THE FERRYMAN · sight
```
WAITING   static on the track, lantern raised
          └ TELEGRAPH: the lantern itself, visible from very far out
LURE      train detected → waves lantern, steps forward onto the line
          ├ train DECELERATES → ADVANCE
          └ train HOLDS or ACCELERATES → steps aside at the last moment, BREAK OFF
ADVANCE   closes on the slowed train, boards the engine
PUNISH    cab invasion; attacks the conductor directly
```
Cannot re-engage after breaking off. **Entirely defeated by doing nothing**, which is why it works.

### THE LONG WHISTLE · sound
```
HIDDEN    never visible; operates from ahead on the line
BROADCAST periodic → emits an authentic locomotive horn ahead
          └ TELEGRAPH: the horn does not doppler correctly and has a pitch flaw
IF crew brakes hard → the stop itself is the punishment
IF ignored → escalates twice, then abandons
```
**Design note: has no punish of its own.** It manufactures a stop, and whatever else is in the region does the killing. Never spawn it alone — the harness must enforce a co-spawn requirement.

### GREASE · scent
```
DORMANT   coats a rail section
          └ TELEGRAPH: lamp reflection off the slicked rail; a sharp chemical smell in the cab
CONTACT   traction multiplier drops to ~0.2 for the length of the section
EFFECTS   cannot climb grade · cannot brake · cannot accelerate
COUNTER   sanding from the running boards restores traction over ~8s
```
No agency, never pursues. Compounds savagely with grade and with anything demanding a speed change.

---

## A.3 Rear

### CINDER HOUNDS · heat, scent
```
ROAM      offscreen, tracking the train's heat signature
ACQUIRE   heat above threshold OR food/livestock cargo aboard
          └ TELEGRAPH: distant howling behind, audibly closing
PURSUE    matches speed; gains on every grade (the train slows, they do not)
LEAP      within 30m → jump for the rear car
BOARD     on success → becomes an interior threat
BREAK OFF sustained rear gun fire, or train exceeds their sustainable speed
```
**Every burst of gunfire increments the Choir's global aggro counter.** This pair is the roster's central tension and should be tuned together.

### THE WEIGHT · vibration
```
BURIED    waits beside the track, sensing vibration
GRAB      rear car passes → attaches to the coupling or underframe
          └ TELEGRAPH: abrupt speed loss plus heavy scraping audio from the rear
DRAG      constant negative force; speed decays continuously
          └ speed reaches 0 → pulls the car off the rails → derail risk
COUNTER   uncouple the rear car (it takes the car and leaves)
          OR engineer melee from the rear platform, ~5 hits to release
```
**Deliberately below the rear gun's arc.** Cannot be shot. Forces either a sacrifice or a trip outside.

### FOLLOWERS · scent
```
STALK     near facilities; tracks a disembarked player by scent
ATTACH    moves into the carrier's blind spot and matches their pace
          └ TELEGRAPH: visible ONLY to other players, never to the carrier
BOARD     carrier enters the train → it enters with them
NEST      moves to an unlit cargo car → becomes an interior threat
COUNTER   an observer calls it out → carrier halts → it detaches and flees light
          OR refuse boarding until the carrier is visually checked
```
The asymmetry is the entire mechanic. **The person in danger cannot see the danger.**

---

## A.4 Flank

### CLINGERS · vibration
```
DRIFT     attaches to a passing cargo car hull
DRILL     static, boring inward
          └ TELEGRAPH: rhythmic directional scraping, clearly audible inside that car
BREACH    ~90s → hull penetrated; cold enters, cargo integrity begins failing
COUNTER   engineer reaches it via the roof and prises it off (~6s fully exposed)
```
Not shootable. No firing arc covers the flank.

### CLIMBERS · movement
```
PACE      runs alongside at track level, matching speed
MOUNT     only at a COUPLING GAP → climbs between cars
          └ TELEGRAPH: brief scrabbling at the gap, visible from adjacent roofs
TRAVERSE  moves along the roofs toward the engine
ENTER     first unlit or unoccupied car → interior threat
COUNTER   a player physically occupying a gap blocks that mount point
```
**Scales directly with train length.** More cars means more gaps means more mount points, defended by the same crew. This is the progression curve made literal.

### DRAGGERS · movement
```
CLING     underside of the car edges, out of sight
GRAB      player within 1m of a car edge → grab attempt
          └ TELEGRAPH: a limb visible at the edge lip ~1s prior
OUTCOME   grabbed alone        → pulled off the train, death at speed
          grabbed with ally near → ally has a 2s window to free them
COUNTER   walk the centreline of the roof
```
Never leaves its car. Makes roof traversal a route decision rather than a straight line.

### THE GAUNT · sight
```
FROZEN    perfectly motionless while inside ANY player's view cone
ADVANCE   moves only when unobserved — fast, silent
          └ TELEGRAPH: it is closer than it was. No audio cue at all.
REACH     adjacent to a player → attacks on the next unobserved frame
RETREAT   continuously observed for 60s → withdraws
COUNTER   sustained observation, by anyone
```
**The cost is a person.** Whoever watches it can do nothing else, and the train still needs running.

### THE DRIFT · movement
```
SPREAD    a mass moving alongside and over the train, expanding toward motion
DETECT    any player movement within radius → surges toward it
          └ TELEGRAPH: visible creeping advance, audible rustle
CONSUME   contact → damage over time, spreads onto the player
COUNTER   complete stillness ~4s → loses the target, resumes drifting
```
Compounds brutally with anything that demands movement — which is most of the roster.

---

## A.5 Interior

### RATTLE · vibration
```
NEST      occupies a coupling gap
IDLE      dormant and silent
ARM       a player approaches the gap → begins rattling
          └ TELEGRAPH: the rattle. Loud, directional, unmistakable.
STRIKE    player enters the gap while it rattles → instant grab, pulled under
DORMANT   no approach for ~10s → silent again
COUNTER   wait it out, or route over the roof
```
**Blocks train traversal**, which isolates crew sections from each other. Does more social damage than physical.

### STOKER · heat
```
INFILTRATE enters the firebox during a stop, or down the stack
FEED       boiler pressure climbs independent of shovelling
           └ TELEGRAPH: gauge rising with no fuel added; wrong-coloured firebox glow
CRITICAL   pressure at maximum for 20s → BOILER RUPTURE
COUNTER    vent valve — drops pressure, kills speed temporarily
           OR open the firebox and drive it out, exposing the boiler player
```
**The counter has a clock cost.** Repeated venting is how you miss dawn.

### HOLLOW · heat
```
WAIT      perched on the smokestack, unseen
DESCEND   firebox temperature drops below threshold → enters via the stack
          └ TELEGRAPH: the fire gutters, then soot falls into the cab
EMERGE    enters the engine car interior
HUNT      attacks whoever is in the engine car
RETREAT   temperature restored → leaves immediately
COUNTER   keep the fire hot
```
Fully preventable. Gives boiler neglect a face.

### DEADMAN · absence
```
DORMANT   outside, tracking the cab
WATCH     cab unoccupied → begins approach
          └ TELEGRAPH: cab lamp dims; controls click on their own
TAKE      cab unoccupied 30s → occupies it
          ├ throttle locks
          ├ brake unresponsive
          └ train accelerates toward the next hazard
EVICT     a player entering contests it — ~4s and damage taken
COUNTER   never leave the cab empty
```
Fully preventable, and it does structural design work: it punishes the whole crew piling out at a facility.

---

## A.6 Structural

### THE CHOIR · sound
```
COUNTER   global aggro value; increments on every gunshot, decays with silence
DISTANT   low → audible singing far off; no threat
APPROACH  above threshold → converges on the train from every side
          └ TELEGRAPH: the singing closes and multiplies
SWARM     high → simultaneous assault on all cars
DECAY     no gunfire for ~45s → value drops, they disperse
```
**Cannot be killed. Shooting them raises the counter faster than it thins them.** The keystone of the roster and the reason the gunner role has decisions instead of a trigger.

### LAMPLIGHTERS · light
```
LINESIDE  paces alongside, just beyond lamp range
ACQUIRE   any light source visible → moves toward it
          └ TELEGRAPH: eyeshine at the edge of the lamp cone
STRIKE    reaches the source → destroys the lamp, attacks the nearest player
BREAK OFF all lights extinguished → loses track, returns to the lineside
COUNTER   lamps down
```
The counter blinds you to Sleepers and the Ferryman. **That contradiction is the point.**

### SOOT CHILDREN · sound
```
LISTEN    outside; samples crew proximity voice
MIMIC     reproduces a crew member's voice, calling for help from outside
          └ TELEGRAPH: the voice has NO proximity falloff — constant volume
            regardless of distance. That flaw is the tell.
LURE      a player opens a door or exits toward the voice
STRIKE    takes whoever came
DECAY     ignored ~30s → moves on, tries a different voice
COUNTER   never answer a voice from outside; verify by looking at the crew
```
The falloff flaw makes it fair, learnable, and implementable in the existing voice layer.

---

## A.7 Corrupted humans

### THE SWITCHMAN
```
PATROL    works the junction network ahead of the train
SET       reaches an upcoming junction → throws it to a dead line
          └ TELEGRAPH: a lamp signal reading wrong; a distant figure at the switch
CONSEQUENCE wrong route → dead end, wreck yard, or a long backtrack
FLEE      approached on the ground → retreats; will not fight
COUNTER   verify switches on the ground — costs a stop and a person
```
**Never directly lethal. It costs you the clock**, and the clock is what kills you.

### THE PASSENGER
```
BOARD     enters during a facility stop, unnoticed
BLEND     takes crew appearance; appears on the roster
IDLE      behaves plausibly — moves between cars, performs half-finished tasks
          └ TELEGRAPH: never speaks on proximity voice
                       repeats a task loop
                       crew count reads one too many
ISOLATE   waits for a player alone in a car
STRIKE    kills and replaces them
COUNTER   head count, and make everyone speak
```
**The tell is silence on a voice channel**, in a game entirely about talking. Costs nothing to implement and does an enormous amount of work.

---

## A.8 Implementation notes

**Trigger collisions to watch.** Vibration is used by four enemies (Sleepers, The Weight, Clingers, Rattle) and sound by three (Long Whistle, Choir, Soot Children). They are separated by pressure zone so their tells remain distinguishable, but any new enemy must respect that separation.

**Dependent spawns.** The Long Whistle has no punish of its own and must never spawn alone. Cinder Hounds and The Choir should be tuned as a pair.

**Fully preventable enemies.** Hollow, Deadman and The Ferryman can each be reduced to zero threat by correct play. That is intentional — a roster where everything is unavoidable stops rewarding mastery.

**Cost-only enemies.** The Switchman and Tallyman-class threats cost time rather than lives. Keep this category small; two is probably the ceiling before the run stops feeling dangerous.

**What the harness verifies per tree.** Telegraph precedes commit in every path · reaction window is achievable at all crew sizes · at least one counter is reachable given the crew's actual equipment · no two active enemies present indistinguishable tells.

---

---

# APPENDIX B — SPAWN RULES

## B.1 The director

Enemies are not rolled independently. A **pressure director** spends a budget across the run, which is what allows deliberate contradiction stacking instead of random pile-ups.

### Budget

```
RUN BUDGET = base(route tier) × length multiplier × crew multiplier
```

| Route tier | Base budget |
|---|---|
| Local | 40 |
| Frontier | 70 |
| Dead lines | 110 |
| Deep territory | 160 |

**Length multiplier:** `1.0 + (0.15 × cars beyond the third)`
**Crew multiplier:** `0.7 + (0.12 × crew)` — capped at 1.6

Budget is spent across the run against a rising curve, not evenly. Roughly 15% before the first facility, 45% across the middle, 40% in the final approach.

### Pressure cost per enemy

| Cost | Enemies |
|---|---|
| **1** | Grease, Sleepers, Rattle |
| **2** | Clingers, Draggers, Lamplighters, Switchman |
| **3** | Climbers, Cinder Hounds, The Weight, Followers, Stoker, Hollow, Long Whistle |
| **4** | The Drift, Soot Children, Deadman, The Ferryman |
| **5** | The Gaunt, The Passenger |

### Hard caps

Enforced regardless of budget:

| Constraint | Limit |
|---|---|
| Concurrent flank threats | **2** — the middle is uncoverable; three is unfair |
| Concurrent interior threats | 2 |
| Concurrent forward threats | 2 |
| Total concurrent active | 4 at crew ≤4 · 6 at crew ≥6 |
| Same sense trigger, overlapping audible range | **1** — tells must stay distinguishable |

### Pacing rules

**Grace period.** No threats for the first 90 seconds past the gate. The tonal transition needs room, and the crew needs to settle into stations.

**Facility lull.** ~20 seconds of calm on arrival before facility threats activate. Lets the crew commit to a plan before it falls apart.

**Post-event cooldown.** After any punish resolves, a 30–60s trough. Sustained pressure reads as noise; pressure with troughs reads as rhythm.

**Terminus approach.** One deliberate spike, then a hard stop 500m out. Nothing may spawn inside the final approach — the last stretch is for surviving what's already aboard.

### Contradiction seeding

The director draws pairs from a **conflict table** rather than spawning independently. This is the design spine expressed as a spawn rule.

| Pair | The bind |
|---|---|
| Choir + Cinder Hounds | Hold fire vs. only the gun stops them |
| Lamplighters + Sleepers | Lamps down vs. lamps needed to see the track |
| Lamplighters + Ferryman | Lamps down vs. the lantern is the tell |
| The Drift + climbing grade | Stop moving vs. lose speed, lose the summit |
| Clingers + grade | Handle it on the roofs vs. nobody goes up at speed |
| Rattle + interior threat aft | Don't cross vs. something is in car four |
| Soot Children + Followers | Don't answer voices vs. someone genuinely is outside |
| Deadman + facility loading | Never leave the cab vs. the facility needs everyone |

At least one pair per run on Frontier and above. Two on Deep Territory.

---

## B.2 Forward

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **Sleepers** | Placed at line generation, not dynamic. Straight sections and blind curve exits. | Never within first 2km | Density ×1 Local, ×2 Frontier, ×3.5 Dead lines |
| **The Ferryman** | Long straight with clear sightline. Mid-to-late run only. | Frontier+ · once per run · requires functioning forward lamp | Weight up if crew has braked for a false positive earlier |
| **The Long Whistle** | 400–900m ahead, immediately before a grade or curve so braking is worst | Frontier+ · **requires ≥1 other active lineside threat in region** | Never spawns alone. Weight up in fog. |
| **Grease** | Placed at generation. Preferentially on grades and curve approaches. | Any tier | Density ×2 in wet or cold weather |

**Note:** Sleepers and Grease are level content, not spawns. They're authored into the generated line, which means the director cannot use them to respond to crew behaviour — they set the baseline instead.

---

## B.3 Rear

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **Cinder Hounds** | Behind the train, out of visual range, after sustained speed | Any tier · requires guard car or rear-most car | Weight ×2.5 with livestock or food cargo · ×1.5 when boiler runs hot |
| **The Weight** | Marsh, water crossings, low ground only | Train length ≥2 · terrain-gated | Weight up at low speed · cannot spawn on grades |
| **Followers** | Facility grounds only. Attaches to a disembarked player. | Requires an excursion · any tier | Weight up per additional player on the ground simultaneously |

---

## B.4 Flank

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **Clingers** | Attaches to a cargo car in transit | **Train length ≥3** (needs a middle) | Count scales with cargo cars, max 2 concurrent |
| **Climbers** | Alongside at track level, mounts at a coupling gap | **≥2 coupling gaps** · minimum speed threshold | Weight scales directly with gap count — the length curve made literal |
| **Draggers** | Pre-attached beneath car edges at generation or facility departure | Train length ≥2 | Dormant until a player is on the roofs |
| **The Gaunt** | Roof, during a stop or on tunnel exit | Frontier+ · **once per run** · crew ≥3 | Weight up if the crew has been fully interior for >3 min |
| **The Drift** | Terrain region, not an entity. Marsh and contaminated ground. | Terrain-gated | Weight ×2 with chemical cargo aboard |

---

## B.5 Interior

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **Rattle** | Occupies a coupling gap during a facility stop | Train length ≥2 | Weight up on longer consists · prefers mid-train gaps |
| **Stoker** | Firebox, during a stop with the box open or unattended | Any tier | Weight ×3 if the firebox was left open at a facility |
| **Hollow** | Down the smokestack. **Condition-triggered, not placed.** | Firebox temp below threshold for 45s | Pure neglect response. No location gate. |
| **Deadman** | Enters the cab. **Condition-triggered.** | **Not on Local routes** · cab empty 30s (20s on Deep territory) | Fires whenever the condition holds. Free to prevent. |

**Design note:** Hollow and Deadman cost budget only when they actually fire. They are a standing threat the crew controls entirely, which is why they're safe to leave uncapped.

---

## B.6 Structural

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **The Choir** | **Not spawned.** Present from run start as an ambient global system. | Always active, dormant at zero aggro | Aggro floor raised by livestock cargo · decays 45s after last shot |
| **Lamplighters** | Lineside, in dark forest and open sections | Any tier · requires ≥1 lit lamp | Weight ×2 at night depth · ×0 if all lights are already out |
| **Soot Children** | Near facilities and dead settlements | **Crew ≥2** (needs a voice to mimic) · requires recent proximity voice activity | Weight up per crew member currently outside |

**Soot Children cannot spawn for a solo player** — there is no voice to steal. This is one of several places where crew size gates content rather than scaling numbers.

---

## B.7 Corrupted humans

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **The Switchman** | Junction network ahead of the train | Frontier+ · **route must contain ≥3 junctions** | Weight up on routes with dead-line branches available |
| **The Passenger** | Boards during a facility stop | **Dead lines+ · crew ≥3** (needs a crowd to hide in) · once per run | Weight up if crew were separated across multiple facility tasks |

Corrupted humans are the rarest category by design. **Maximum one active at a time**, regardless of budget.

---

## B.8 Cargo modifiers

Cargo changes the run rather than just scoring it. These multipliers apply to spawn weight.

| Cargo | Effect |
|---|---|
| **Livestock** | Choir aggro floor +25% · Cinder Hounds ×2.5 |
| **Food** | Cinder Hounds ×2 · Followers ×1.5 |
| **Chemicals** | The Drift ×2 · gunfire indoors becomes lethal to the crew |
| **Ammunition** | No spawn change — but every consequence is worse |
| **Coal / timber** | No spawn change — fire cascades escalate faster |
| **Comet-derived material** | **All weights ×1.4** · Gaunt and Passenger gates relaxed by one tier |

Comet material is the high-risk contract: it is the best payout and it makes the entire world more interested in you.

---

## B.9 What the harness verifies

| Check | Method |
|---|---|
| **Budget sanity** | No generated run exceeds survivable pressure at any crew size |
| **Cap enforcement** | Flank never exceeds 2 · no duplicate sense triggers in range |
| **Dependency integrity** | Long Whistle never spawns without a partner threat |
| **Gate correctness** | No length- or crew-gated enemy appears below its threshold |
| **Contradiction quality** | Every seeded pair is solvable — hard, not impossible |
| **Pacing shape** | Grace period, troughs and terminus silence all present |
| **Tier progression** | Deep territory is meaningfully harder than Local at matched crew and length |

Spawn tuning is the single largest use of the agent harness. Twenty-one enemies against four tiers, seven crew sizes and a variable consist length is a space no human tester can cover — but it can be swept exhaustively overnight.

---

*Dark Territory · GDD v1.0 · September 2026*
