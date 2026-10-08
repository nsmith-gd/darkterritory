# DARK TERRITORY
## Game Design Document — v1.5, October 2026

**Two to eight players crew an armoured freight train through a corrupted wilderness. Load what you can. Deliver what survives.**

*Dark territory is a real railroad term: track with no functioning signal system, where trains move on verbal authority alone. When communication fails, people die.*

Interactive version, with the roster explorer and director tools: [claude.ai/artifact/PrfaZjyZb1rWcWWuiPm1Dx](https://claude.ai/artifact/PrfaZjyZb1rWcWWuiPm1Dx). It opens only for people it has been shared with.

**v1.5 changes — the director's play-tests of 5–7 October.**
- **Boarding-first.** Nothing acts inside the train unless it boarded: each creature by its own rule at a known point, with a telegraph and a counter, at its own speeds. A hauling train is safe from most creatures, not all; stops, facilities and tight bends are where things get on (§21, App. A, F.1 Decided).
- **A derailment is the driver's mistake.** The Sleepers are retired. Every bend that can derail the train is boarded and on the cab map with its speed, and the cab hears and sees the stress before it goes. Every night carries its tier's hard bends. The Switchman sends you down a dead line, and only running off its end derails you (§21, §22, A.8).
- **A derailment commits.** However slow it comes off, the first car is thrown and every car after goes over, one after another (App. E, F.3).
- **One night length for every tier:** 24 km and dawn at 51 minutes. A deeper tier is denser, not longer (§11).
- **The director runs on pressure.** Quiet, loudness, cargo and the night's depth build it; the grace is 20–90 s, picked per night; the night's first threat answers something a crewmate did, and the report names it (B.1).
- **The orchestrator** (outlined): threats planned against the players active now. Built so far: the budget from the crew alive, and a cap on threats at once by it (B.1, orchestrator.md).
- **Something to do between stops.** The hound run, a stream of Cinder Hounds a fast train draws for the guns to answer one at a time; and the first upkeep job, the hot box, an axle box running dry that's greased from the gap or the ground before it slows the train and catches (B.3, F.3).
- **Off the train, a presence of threat.** The crew on foot are watched from the lamp's edge, and outdoor creatures come sooner while anyone's out (B.1, F.3).
- **Driven off, or killed together.** A creature is driven off by its rule; a coordinated team can kill the Grumbler, the Gaunt, the Passenger, Climbers and the Choir's ghosts (F.1).
- **Damage, Lethal Company style:** a few big hits, never chip damage, rare healing finds, an edge flash (F.1; systems spec B.2).
- **The wrench repairs, Sea of Thieves style.** Everyone carries one; in hand, Use at a break mends it: the burst boiler, a breach, a battered car. Stranding is to follow the wrench, not the kit: every wrench lost, then a rupture (the director, 8 Oct; §12, §23, §23.2).
- **Fire is a grid** of cells on a car's floor, walls and roof; the extinguisher puts out the cell you aim at, and burnt cells char (C.5).
- **Stoker v3:** drawn by a hot firebox, it boards at the coal bunker and can be caught there; in the firebox it's territorial, and venting starves it out (§21, A.5). **The Track Doll** escalates only when ignored (A.2).
- **The fortress is safe** until the gate, and the forts all night (§9). **Fortress towns:** each a Maritime town with its own custom, people who talk, and houses to walk into (§3.1).
- **Village houses to search:** they stand open, and a held search of each cupboard, cabinet, cellar or loose board turns up the finds (F.3).
- **The HUD is your hands and the dark:** only the crosshair and the hotbar always; prompts say the action and its key and never foretell (§32).
- **The film:** each player is alive until the hit that kills them, and skips their own film whenever they want (E.2, E.5, E.12).
- **Solo is tested** (F.1); open question 12 (crew 2) is swept (Part Eleven).
- Touches §3.1, §9, §11, §12, §21, §22, §23, §32, Part Eleven, A.2, A.5, A.8, B.1, B.3, C.5, E.2, E.5, E.12 and App. F, where the play-tests are logged.

**v1.4 changes — the boiler, stranding and the derailment cinematic.**
- **Boiler rupture no longer kills.** It catches up with the build (T109): the engine seizes, the train slows hard and coasts, and the engineering kit held at the firebox for 25s mends it. The train restarts from cold (§23).
- **New run end: Stranded, unable to repair.** A ruptured boiler with the engineering kit lost ends the night (§23.2).
- **New Appendix E — End-of-night sequences.** A slow-motion derailment cinematic shows every crew member's death, ragdolled by the wreck, to a rotating pool of CC0 opera recordings. Includes camera, occlusion, collision, music and licensing rules. Stranded gets a short, quiet outro.
- **"Repair kit" is now the engineering kit** throughout: a carried kit, kept in the fitter's locker in car one. It's what mends the boiler; the wrench is a tool to swing (§12).
- **Crew lockers.** Car one has a row of crew lockers, each lettered with a railway grade; anything hand-sized can be stowed in one and stays put. The engineering kit lives in the fitter's (§12).
- **Spare kits, bought and found.** Answers E.12 question 4: the fortress sells spare engineering kits, and kits turn up as loot at stops. Stranded takes losing every one (§23.2).
- **Jumping off** is lethal above 16.5 m/s, matching the systems spec in the repo (§23).
- **Run budgets match the build** (B.1): 90, 150, 230 and 330 by tier, about twice v1.1's, to pay for the paced director the build runs.
- Touches §12, §23, §23.1, B.1, C.9, D.2, D.4, D.7, D.12, E.9 and E.12.

**Director's decisions of 2026-10-06 (build 1121 play-test; ARCHITECTURE §8 note 264).**
- **One supplies view, toggled on.** "The UI is a little too heavy overall in the game, but we do need a way to track all of the supplies that we have on board." The HUD gets lighter (the cab's gauges are the boiler's read-out), and a single panel, off until it's wanted, lists the supplies aboard: coal, the engineering kit and where it is, extinguishers, cargo and crates, stores (lamps, radios, toys, finds) and powder and shot (§10).
- **No spoken manifest at the gates.** The dispatcher's reading leaving the yard is dropped (too long, hard to localise); the clerk's tally home stays (§9).
- **Toys are found, not issued.** The guard van no longer starts with toys; they turn up in villages' cupboards, cellars, haylofts and under floors (§19, C.4).
- **The lockers start stocked** with emergency lamps and extinguishers by grade, and say what's in them on the door (§12).
- **The whistle cord hangs in the driver's corner**, worked with Use when looked at, and the HUD names whose hand is on it; a whistle with no name on it is the Whistler's (§12, A.4).

**v1.3 changes — failure is funny.**
- New **§23.1 — Failure has to be funny**: the four conditions a failure must meet, and the rule *horror in the telegraph, comedy in the grab*.
- **Death hard-cuts the victim's voice** mid-word, and a grabbed player holding a radio broadcasts the whole GRAB to the crew (D.2, C.8).
- **GRAB victims can talk** by default. Voice effects are the deliberate exceptions (C.1).
- **The Choir takes the loudest voice** first (§21, A.7, C.7).
- New **Appendix C.9 — Failure attribution** feeds a cause-of-death line and itemised fee for every death in the incident report (D.12).
- **Bodies are ragdolls** and keep what they carried, so a dead engineer is where the engineering kit is (§12, D.9).
- **Auto-bookmarks** at every GRAB, PUNISH and whole-train commit (D.12).
- **Noisy loot**: some toys feed the loudness meter while carried (§19, C.4, C.7).
- **The settlement clerk** prices the night at the gates (§9).
- **The dead get a cue when their vote lands** (D.11).
- Answers open question 2: **roll call is verbal.** Touches open questions 11 and 13, and D.15 question 5.

**v1.2 changes — death and return.**
- New **Appendix D — Death, Holdouts and Return**: Holdouts at halts and yards are the only way back mid-run. It replaces the Vigil and POI drop-in.
- Bodies are loot: every death charges a crew-loss fee, and a body brought home refunds most of it.
- The crew shares one wallet (§11).
- Dead players spectate, call out from Holdouts and get one creature vote. Answers open question 10.
- Touches §9 Arrival, §11, §23, Part Eleven, B.1 and B.10.

**v1.1 changes — the social roster.**
- Enemy roster rebuilt for social play: 17 enemies, down from 21, with seven new designs (§21).
- The five-point test is replaced by the social test (§20). Kills now go through a rescue window (A.1).
- Cut: Ferryman, Clingers, Rattle. Replaced: Long Whistle, The Weight, Deadman, Lamplighters, Hollow. Sleepers, Grease and The Drift became hazards.
- Guns are now crude cannons with a manual reload. Melee with the train's tools is a core verb.
- Noise is now the crew's combined loudness, voices included (§14).
- New Appendix C lists the eight supporting systems the roster depends on.
- Spawn rules rebuilt around want balance and a new conflict table (Appendix B).

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

This makes every town dependent on freight. One settlement produces coal. Another grows food. Another manufactures gunpowder, or operates foundries, or produces medicine.

No town is self-sufficient. **The railway is what makes civilization possible.**

### 3.1 Fortress towns *(the director's direction of 2026-10-06 and 2026-10-07, App. F.1 T133; built for review, ARCHITECTURE §8 note 281)*

The towns are where the world is built. They tell the story of what happened and how people survived, by inference: a line from somebody at their table, a little posted on a board, never a speech.

**Maritime towns, walled.** They are towns of Maritime Canada (Nova Scotia, New Brunswick, the Island) with walls round them: twenty to three hundred and fifty people, fewer than lived there before. Every town has lost people and every culture is marked by it. The plaque at the way in says how many live there now and how many did.

**A town is its custom.** The Corruption exaggerates whatever lets a thing survive (§2), and the towns did the same. Each one got through one bad winter by doing one thing, kept doing it, and did it harder until it was the town's custom. Every custom is the human answer to one creature's rule, said sideways: the crew hear the custom, and the rule is theirs to work out. In a hush town nobody sings, not even at a burying, and the bell's clapper is bound in felt (the Choir). In a pairs town nobody sleeps or waits alone (Tippy Toesie). In an offerings town the children have no toys, because the toys go on a shelf by the gate (the Track Doll). The towns name the creatures in their own words, never the game's. A custom only ever answers a creature the edition fields. Fourteen customs are written (content/world/towns.json), one for each of the demo's five and the Choir among them.

**Each town is different from the last.** It is made from the night's seed: the line's fortress name, its custom (never the one the crew left last night), one or two smaller habits (they wear their dead's coats; every clock stopped at the same minute; the foghorn sounded for the ones still out), and what it makes (a pit town, a growing town, a foundry town).

**The square** is where the walls step back, beside the engine as the night starts. It holds:
- the custom's own building in a Maritime form: a white clapboard church with a steeple over its door, a one-room school with its belfry, a car shed, a hall;
- the clerk's office and the stores, lit;
- the custom's centrepiece in the middle: the felted bell, the waiting post, the tally board with one mark too many;
- a notice board, a plaque at the way in, market stalls shut for the night, benches, fire barrels and lamps.

**The houses.** Down the yard's street from the square, both sides, fronts to the line:
- the households' houses, nearest the square. Painted clapboard (barn red, ochre, slate blue, sage, white) with white trim on a fieldstone foundation, in the Maritime forms: a storey and a half with a gable over the door, a saltbox, a gambrel, a two-storey house with a Lunenburg bump. A lamp burns by every lived-in door, and lamplight shows in some windows (none where the custom keeps them dark). A knock is answered through the door, or not.
- a few houses standing open with their household at home, explorable: a kitchen with its range, table and dresser, a parlour with the boxed stair and its shut door, the household's own thing (the laid place, the letters, the ankle bell, the cradle). Two to four of them are at home, at the range, at the table, in the chair, at the window.
- then the houses of the people the town has lost: boarded, burnt to the sills, or left open on the dark. Each has something to say when you look at it.

*The director is finding references for the houses; this is the first pass at them.*

**People.** Text only, no voices, as many as the town is big:
- the gatekeeper says the town's law first;
- the keeper of the custom's building and the folk round the centrepiece talk about the custom;
- the clerk, the fitter, the lampman and the rest talk about their work;
- a few are out in the street with a lamp;
- the households at home tell their own story: the empty chair kept for somebody, the child who walks in their sleep, the letters to a town that stopped answering.

Use talks to someone, and Use again hears their next line. They turn to face you. Nobody can be talked to through a wall.

**Lore is through what people say** (the director, 7 Oct). They say creepy things. Sometimes it's a hint of how another town deals with a creature they've heard about ("in Pell's Cove they won't sing"), never the full rule. Everyone is a little damaged by what happened.

**Papers.** The board carries three or four notices: the custom's orders first, then the town's trade and whatever else is posted. One or two notes lie about the square. A town's papers name its own people, so a line from one person and a notice about another can be put together.

**Mystery is seeded, not explained.** A few threads run through every town, a scrap or two each: the fall, the lost trains (the night mail, "still due"), the towns that stopped answering, the sea, the penal cars, Dispatch, and the clerk's fees. None of them is ever explained.

**Everything is solid and safe.** The square, the houses (an open one through its doors), their furniture and the people stand where they're drawn, and the fortress's walls step back round the square. The yard is a safe space until the gate (§9).

**Not yet** (for the next passes):
- the houses to the director's references;
- buying, upgrading and turning loot into scrip in the town itself, not the menu;
- the terminus as a town of its own, and the town you arrive at becoming the one you leave from;
- townspeople who move about, and faces of their own (they wear the crew's model);
- upstairs in the open houses;
- things everyone hears when one person does them (ringing the bell);
- notes found out along the line.

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
Inside the walls: purchase equipment, repair or upgrade the train, choose freight contracts, add or remove railcars, stock coal, powder and shot, lamps, repair supplies and tools.

Then the gates open. ~~The yard dispatcher reads the crew out over the radio by name, as a manifest, with the same tone used for the coal.~~ *Director's decision of 2026-10-06: no reading at the gates; the night starts as the train moves off.*

### The threshold
The transition should be a major tonal moment. Inside: workers, lights, machinery, voices, guards, whistles, industrial noise. Then the outer gates open, the train passes the final defensive towers, the lights disappear behind it, and ahead is only track.

**This is where the run actually begins.**

### The fortress is a safe space *(the director's decision, 2026-10-06)*
Until the train passes the outer gate, the fortress yard and the lobby are a safe space, like the ship in *Lethal Company*. The crew wait for friends, hang out, dance, try on outfits, walk away for a cigarette, and come back when they're ready. Nothing of consequence happens there:

- nothing spawns, and the Choir's meter doesn't gather;
- the boiler and its fire hold: no overheating, no rupture, no running out of steam or coal;
- the cold doesn't bite;
- the train doesn't move off unless someone in the cab lets the brake off and drives it out through the gate.

The run, and every consequence in it, begins as the train goes through the gate (run.json `yardIsSafe`). Emotes (a dance, a wave, a point) and outfits to try on are ARCHITECTURE §8 note 298.

### Arrival
Eventually the crew sees lights. Then walls. Then cannon towers. The gates open and the train crosses back into civilization.

**Everything still attached to the locomotive counts.** Cargo is unloaded and paid into the crew's shared wallet. Bodies brought home earn back most of their crew-loss fee. Lost cars, powder, equipment and unrecovered crew become the cost of the run (Appendix D.9).

**The settlement does not mourn.** A yard clerk tallies the run over the radio as the cars come through: cargo by the car, bodies by the body, each fee read out flat, in the same voice as the coal. The town values your friend at 75%, says so, and moves on to the next line. This is the world's indifference, and it is the punchline to every failure that came before it (§23.1).

## 10. The consist

| Section | Contains |
|---|---|
| **Engine car** | Conductor, boiler, forward cannon. Heat, light, the throttle. |
| **Cargo cars** | The middle. Growing. Unarmed. |
| **Guard car** | Rear cannon, tool storage, the back door. |

Early crews run **engine plus one or two cars.** Experienced crews run **engine, armour, cannons, utility cars and many freight cars.**

The train should increasingly feel like home. Players learn where cannons are mounted, where powder and shot are stored, where the engineering kit is kept, where emergency lamps are kept, where coal is stored, where tools and fire extinguishers hang.

That familiarity matters because the train gets more complex over time. **Progression literally makes your home harder to defend.**

**Supplies aboard** (director's decision of 2026-10-06). One compact view, toggled on and never always there, says what the train carries: coal, the engineering kit and where it is, extinguishers, cargo and crates, stores (lamps, radios, toys, finds) and powder and shot. The rest of the HUD stays light; in the cab the gauges on the backhead are the boiler's read-out.

## 11. Progression

Successful deliveries earn money. Money buys capability. Capability allows more freight. More freight means more railcars — and more weight, slower acceleration, longer braking, more vulnerable cargo, more distance between players, more blind spots, more repair problems, harder facility loading, greater exposure.

**Difficulty is produced by success rather than selected from a menu.**

**One wallet.** The whole crew shares a single wallet for every purchase and upgrade. Nobody holds their own money. More crew means more hands and more loot, faster, which is why getting people back into a run matters (Appendix D).

### Route tiers

| Tier | Description |
|---|---|
| **Local routes** | Relatively safe connections between major towns: few creatures, few junctions and stops |
| **Frontier routes** | Lines through abandoned territory: more creatures, more to do per kilometre |
| **Dead lines** | Railways to settlements that have stopped responding |
| **Deep territory** | Old industrial regions where Corruption is far more severe: the most creatures and the densest line |

You aren't picking a difficulty level. You're travelling farther from civilization.

**Every night is the same length** *(the director's decision, 2026-10-06)*: "The time a run takes should always be the same. Difficulty scales not by time but by monsters and density of challenges." Every route is one length to the same dawn (systems spec B.8: 24 km, dawn in 51 minutes). A deeper tier sends more creatures over it and packs more into each kilometre: harder grades and bends, junctions, washouts, weak bridges and brass. Every tier has the same three stops, since a stop is time.

---

# PART FOUR — CREW

## 12. Roles

**Four roles, none of them assigned.** You are whatever the train needs where you happen to be standing.

| Role | Where | Does | Blind to |
|---|---|---|---|
| **Conductor** | Engine car | Throttle, brake, whistle, reverse | Everything mechanical, everything behind |
| **Boiler** | Engine car | Fuel, pressure, heat, water | Outside entirely. No windows. |
| **Gunner** | Engine or guard car | Crude cannon: arc-limited, slow to reload, loud | Whichever direction they aren't facing |
| **Engineer** | Anywhere | Carries the engineering kit | Nothing — but has no firing arc |

### Fluidity

**The engineering kit is an item, not a station.** It's a carried kit, kept in the fitter's locker in car one, a walk back from the footplate. The engineer is whoever picked it up. There is no post to be stuck at — there's a kit somebody grabbed, and when they die on the roofs it's lying in car four and someone has to go and get it. It mends a ruptured boiler (§23), and nothing else can; the wrench in the cab is just a tool to swing. The fitter's empty shelf shows whether the kit is home.

**The crew lockers.** Along car one's left wall, ahead of its side door, stands a row of twelve tall iron lockers, each with a crew grade on an enamel plate: DRIVER, FIREMAN, GUARD, SHUNTER, SIGNALMAN, BRAKESMAN, LAMPMAN, FITTER, GANGER, WHEELTAPPER, PORTER, YARDMASTER. Hold Use at one to open or shut its door; tap Use to put what's in your hands on a shelf, or take the top thing off one. Each has two shelves and takes anything hand-sized: a lamp, a radio, a toy, a find, an extinguisher, the kit. What's in a locker stays put through any stop or curve, and a shut locker keeps it from the Gaunt. A locker in a car the Territory takes is lost with the car. The kit starts in the **fitter's**: the fitter is the shed's mechanic, who mends engines.

**Spare kits.** The fortress sells spare engineering kits, and kits also turn up rarely as loot at stops (E.12, question 4). A spare starts the night on the fitter's other shelf, then in the lockers after his. A spare lost in the night is gone; a kit found at a stop and brought home is kept.

**The kit stays on the body** (Appendix D.2). When the engineer dies, nobody goes looking for the engineering kit. They go looking for the engineer, and the dead player watches them do it.

Roles emerge from position. You end up on the cannon because you're standing at the cannon.

### Weapons

**Guns and bullets are rare in this world.** Crude cannons and crude gunpowder are not. Each mounted cannon has a full manual reload — powder, ball, ram, fire — so every shot is a timed decision. Everyone else fights with the tools they carry: shovel, wrench, crowbar.

## 13. The indefensible middle

Mounted cannons protect the locomotive and the rear. Long trains inevitably develop blind areas, and **cargo cars in the middle cannot be protected by any firing arc.**

If something climbs aboard there, someone leaves the enclosed cars and moves across ladders, platforms, roofs and couplings — at speed, in the cold, with nothing but a wrench.

**The longer the train becomes, the more often players must physically enter the Territory to protect it.**

## 14. Noise

Sound is a primary ecological signal. A train is already loud. Cannon fire makes it worse, and so does a crew shouting over each other.

The game tracks the crew's **combined loudness**: cannons, machinery, the whistle, and every voice on the channel. Loudness held above a threshold draws the Choir. A single scream doesn't; a crew arguing at full volume does.

Every shot is a decision. A cannon solves the immediate problem while alerting everything nearby, and the reload leaves you exposed. **The gunner's job is less about accuracy than restraint** — and everyone's job includes shushing each other.

Sometimes the correct response to a monster is to go quiet.

## 15. Crew scaling

| Crew | Train | Feel |
|---|---|---|
| **2** | Short | Pure triage. Constant sprinting, constant bad choices about what to leave burning. |
| **3–4** | Standard | The intended fit. Both cannons crewed only when nobody's on the ground. |
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
| **Chemical works** | Volatile, top payout | Leaks. Do not fire indoors. |
| **Mine head** | Ore | The spur descends underground. Radio blackout in and out. |
| **Military depot** | Gunpowder and shot | Best payout, worst cargo to be carrying when something boards. |

**Every facility is optional. Skipping them is safe and poor.** The payout exists to force bad decisions, not to reward good ones.

## 19. Cargo

Cargo is physically represented and **changes the run** rather than just scoring it.

| Cargo | Complication |
|---|---|
| Coal | Burns |
| Timber | Burns, heavy |
| Gunpowder and shot | Explodes |
| Chemicals | Leaks, toxic |
| Livestock | Makes noise constantly |
| Food | Attracts scavengers |
| Medicine | Fragile, high value |
| Machine parts | Heavy, inert, safe |
| Comet-derived material | Attracts everything |
| Child survivor | The most valuable cargo there is. Carried by hand to a cargo car |
| Hand-carried loot | Toys, crates, salvage. Stolen, eaten, and traded to monsters. Some toys are noisy: a squeaker, a music box, a wind-up drummer. They feed the crew loudness meter while carried (Appendix C.7) |

---

# PART SIX — ENEMIES

## 20. Design philosophy

**Fixed, hand-tuned, fully learnable. Mastery is the product and the wiki is welcome.** Retention comes from regular roster updates post-launch, not from obscuring the rules.

**This is a social game, and the roster is built for it.** Enemies are characters the crew deals with together: things you fight, feed, chase, outnumber or talk to. Most threaten your life, many split the crew up, some break trust, and a few go after the cargo. Dead players only observe, so almost every kill is slow enough for a friend to step in.

**The danger comes from combinations.** A creature that only shows in the light is far worse when another punishes lamps. A creature that needs talking to is far worse when another punishes noise.

### The social test

Every enemy must pass at least four of the six, including 1 and 3. This is the filter for post-launch additions too.

1. **Describable in one phrase** — a character, not a sense trigger. "The doll that wants your cargo."
2. **Answered better by two than by one** — two players make it safer, faster or possible at all.
3. **The consequence starts now, death comes later** — feedback is immediate so players learn; the kill takes long enough for a friend to act (8–20s).
4. **It has a verb, not just a "don't"** — hit it, feed it, chase it, outnumber it, talk to it.
5. **Someone gets blamed** — the failure traces back to a person's choice.
6. **It's fun to scream**

The learnability rules from v1.0 — the rule fits in six words, one death teaches it, the telegraph always comes first — now live in the fairness contract (Appendix A.1).

## 21. Roster — 17 enemies

Demo ships with **five**: Track Doll, Car Hugger, Whistler, Tippy Toesie and Ribbits, with the Choir running underneath as the ambient system. If the Foundry is one of the two demo facilities, the Grumbler replaces the Ribbits.

### FORWARD — the brakes answer

**TRACK DOLL** · *on the track ahead, then aboard*
A large porcelain doll standing on the rails. Its white face shines in the lamp out to 200m.
> **RULE: stop before you hit the doll.**
Stop in time and it's gone. Hit it and it haunts the train: it giggles in the cars, admires your cargo, and plays with the cab controls whenever the cab is empty. Two players can corner and kill it, or it leaves if you let it steal a toy.

### REAR — the rear cannon answers, then the crew

**CINDER HOUNDS** · *behind the train*
Pack. Run the line behind you, gaining on every grade.
> **RULE: keep the rear cannon crewed.**
The cannon drives them off, and it's loud. Any that board become a pack fight the crew bludgeons together.

**CAR HUGGER** · *the last car*
Clamps onto the rear car and eats it, shell and loot. Caps your top speed while attached.
> **RULE: cut the caboose or kill it.**
Anyone in front of its mouth can be swallowed. A group kills it fast; one player can, slowly.

### FLANK — no cannon answers

*The middle of the train has no firing arc. Handled on foot, on the roofs, with tools.*

**CLIMBERS** · *alongside, then the gaps*
Come up between cars, onto the roofs, then in.
> **RULE: outnumber them at the gaps.**
They mount any gap held by fewer players than there are Climbers. Scales brutally with train length.

**DRAGGERS** · *under the car edges*
Reach up from beneath the car edges.
> **RULE: stay off the edges.**
A grabbed player hangs over the side for a few seconds. Someone has to pull them up.

**WHISTLER** · *between the cars*
Blows your own whistle, then hides in a coupling gap.
> **RULE: check the gaps after the whistle.**
Only strikes when the train is stopped. It carries its victim off to a nest, and the crew chases or leaves them.

### INTERIOR — already aboard

**STOKER** · *the firebox*
Looks for heat *(the director's decision, 2026-10-06)*. A firebox run hot draws it: it boards at the coal bunker in the cab, scraping and glowing on the coal, and crosses the footplate to the fire door. A fireman at the fire can catch it there. Once in, it eats the fire and drives the pressure up in lurches, and the speed with it. Beaten, it stays away a couple of minutes.
> **RULE: run cool, or watch the coal.**
Running hot is fast and draws it; running cool is safe and slow. Catch it on the way in. Once it's in, vent to buy time, open the firebox and club it, and get burned doing it. Ignored, the boiler goes or the train runs away and derails.

**TIPPY TOESIE** · *behind anyone standing still*
Tiptoes up behind idle players. Runs if you see it coming.
> **RULE: don't stand still alone.**
Covers your mouth and suffocates you over 20 seconds, and your voice goes muffled. A friend breaking it off saves you.

**FIRE FLIES** · *lit cars*
Swarm to the lamps inside cars. Linger long enough and the car catches fire.
> **RULE: lamps off when they swarm.**
Or drive away. The fire spreads, and a big one takes several extinguishers at once.

### OUTSIDE — facilities, villages and yards

*Where the crew is on foot and furthest from the train.*

**RIBBITS** · *yards and villages*
Giant toad-rabbits in packs of two to four.
> **RULE: never be outnumbered.**
They ignore any group at least their size. Catch someone alone and their tongues freeze them in place while the pack hops over to eat. Outrunnable, if you run.

**THE GAUNT** · *asleep in villages and yards*
A lonely, spindly thing. Wake it and it follows you home.
> **RULE: keep talking to it.**
Every silence makes it angrier, and it hits hard. Kill it together, or let it take the best thing in one of your cars.

**FOLLOWERS** · *facility grounds*
A hand-sized parasite that rides on your back. You can't see it; your friends can, if they look.
> **RULE: check each other's backs.**
It drops off aboard and nests in your best loot car. Club it off your friend, or find the nest.

**SOOT CHILDREN** · *outside facilities and dead towns*
A child calling for help. Half the time it's a real survivor, the most valuable cargo in the game.
> **RULE: check the eyes from five metres.**
Black eyes and blackened hands mean a Soot Child. Get within five metres and it pins you and drinks. Your cries for help get quieter.

### STRUCTURAL — attack how you run the train

**THE CHOIR** · *drawn by noise*
Small flying ghosts that come for a loud crew. Long warning, then the swarm.
> **RULE: hush, and shut every door.**
Takes anyone outside, on the roofs, or behind no door, **loudest first**: whoever put the most into the meter during the build. The player shouting at everyone to shut up is usually the one it takes. Killable, barely. Takes one crew member per run at most, then it's gone.

### CORRUPTED HUMANS

*The rarest and most disturbing. The Corruption preserves fragments of learned behaviour — a railway worker still throws switches, a crewman still rides the rear car, a labourer still works the crane. Terrifying because some piece of human cognition remains, and players cannot always tell whether they are dealing with instinct or intelligence.*

**THE PASSENGER** · *boards at a facility*
A crew member from a train lost long ago. In the dark it passes for one of yours.
> **RULE: make everyone speak.**
It never talks. It drags a lone player to the caboose, uncouples it, and rolls away into the dark to eat. Only the crew can stop it.

**THE SWITCHMAN** · *junctions ahead*
Half railway worker, half something spindly and wrong. Throws switches as you pass.
> **RULE: kill the Switchman before the switch.**
Wrong routes cost the clock: it sends you down a line that leads nowhere, and you stop, back out and set the points back by hand. Its switch never derails you; running off the end of that line does, and that's on the driver. One cannon shot, or stop and club it. *(The director's decision, 7 Oct 2026.)*

**GRUMBLER** · *facility cranes*
Scuttles like a spider over the crane, gnawing food crates.
> **RULE: gang up or leave it alone.**
Interrupt it and it hunts whoever hit it last. No one player can kill it. Crane it aboard by mistake and it eats your cargo.

### Retired from v1.0

- **Cut:** The Ferryman, Clingers, Rattle.
- **Replaced:** The Long Whistle → Whistler · The Weight → Car Hugger · Deadman → Track Doll (the empty-cab job) · Lamplighters → Fire Flies · Hollow → merged into the Stoker.
- **Now hazards (§22):** Sleepers → track debris · Grease → wet rail · The Drift → marsh.

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
| **Curves** *(a bend taken too fast)* | Speed. Every bend that can derail the train is boarded and marked in red on the cab map with its figure; over its derailing speed the train comes off, after the stress has warned the cab (flanges, creaking couplings, the lurch, the cab's bell) for a full reaction window. Run right up to it and brake hard, or slow early and deal with what's on the roof. |
| ~~Track debris~~ | *Retired: the director's decision, 6 Oct 2026. "A derailment needs to be clearly a mistake by the driver, someone not paying attention to the map." Debris forced a derailment on the game's behalf. Off by a tuning flag (enemies.json sleepers.enabled), kept in the code for mods.* |
| **Marsh** | Movement. Something in the reeds surges toward motion; stand still for ~4s and it loses you. |
| **Dawn** | Time. Every careful option becomes unaffordable. |

The line is generated per run: length, grades, curves, junction topology, facility placement, tunnels and bridges.

### Example pressure stack — all known parts

| Present | Demands | Conflict |
|---|---|---|
| The Choir | Hush | Cinder Hounds are on the rear and only the cannon stops them |
| Fire Flies | Lamps off | The Track Doll only shows in the light |
| 3% grade | Keep speed up | The Car Hugger is capping your top speed |

Nothing there is a mystery. It's still a disaster.

## 23. Failure

**Three things kill outright:**

- **Jumping at speed** — above 16.5 m/s. Below it, landing is a knock. The train is a trap by design
- **Getting left behind** — cold and distance do the rest
- **Derailment** — kills the entire crew at once, in slow motion, to opera (Appendix E)

**Everything else cascades:**

| Failure | Consequence |
|---|---|
| Boiler ruptures | The engine seizes and the train slows hard, then coasts. Someone fetches the engineering kit and holds it at the firebox for 25s, then the fire is built back up from cold. Lose the kit and you're stranded (§23.2) |
| Fire dies | Coasting on grade and momentum, brakes only |
| Lights fail | Navigate through darkness on shouted landmarks |
| Radio breaks | Shouting down the length of a moving train |
| Cannon fouls | Someone clears it by hand, under fire |
| Car catches fire | Grab the extinguishers or abandon it |
| Engineering kit left in car four | Somebody's going out there, and the kit is on a body (§12) |
| Crew lost on the ground | Two cannons, one gunner, pick a direction |

**Rolling into the terminus with half a crew, three cars and a fire is the good ending.**

**How a night ends:**

| End | When |
|---|---|
| **Delivered** | The engine stops at the terminus on the main line |
| **Derailed** | The train leaves the rails. Everyone dies, and Appendix E plays |
| **Crew lost** | No living crew remain |
| **Dawn missed** | Still out when the line goes live (§8) |
| **Stranded** | The boiler is ruptured and the engineering kit is lost (§23.2) |

### Boiler rupture

Overfire past the safety valve and pressure pins at 100. Hold it there for 20s and the boiler ruptures. The countdown is the telegraph: a shriek from the valve and a shaking cab (systems spec B.6).

| Stage | What happens |
|---|---|
| **Rupture** | A burst loud enough to carry. Pressure and fire drop to zero, and the cylinders seize |
| **Slowdown** | The seized engine drags the train down at 1.5 m/s² until it's below 4 m/s, then it coasts. Grades still apply, so it can roll on downhill and stall short of a summit |
| **Repair** | In the cab, the engineering kit held at the firebox for **25s**. Interrupted, it starts over |
| **Restart** | The boiler is whole but cold and empty. Coal, fire, then pressure back to the working band: 40s at three cars, over three minutes at twenty |

Nobody dies. The cost is the clock, a stopped train with everything that means (the Whistler, a haunting Track Doll at the cab controls, Cinder Hounds closing), and whatever you have to do to get the kit back to the firebox.

### 23.2 Stranded, unable to repair

*With the wrench as the repair tool (ARCHITECTURE §8 note 301), the director, 8 Oct 2026: "If every crew member drops their wrench off the train then leaves them behind and then the train breaks down they could be stranded."* **A ruptured boiler with no wrench left to mend it ends the night:** every crewmate's wrench and the cab's rack wrench gone, dropped off the train and left behind, in a car cut loose or taken, or on a body carried off. Queue #39's second slice (D1.3) moves this section's rules from the kit to the wrench; until it lands, the build strands nobody, and what follows is the kit's rule as it was.

**A ruptured boiler with the engineering kit lost ends the night.** Nothing else mends a boiler. With spares (§12), it takes losing **every** kit the crew has. A kit found at a stop counts once someone has picked it up.

**The kit is lost** only when the Territory has taken it. A kit lying on the line, on a body, or in a reachable car (on its floor or in a locker) is never lost, however far back it is. Somebody walks.

| The kit is lost when it is… | Because |
|---|---|
| In a car the Car Hugger finished | The car dropped away with it (A.3) |
| In the caboose the Passenger rolled away | It rolled into the dark (A.8) |
| On a body the Gaunt carried off | The Gaunt left for the run (A.6) |
| In an uncoupled car, on the main line, more than **400 m** from every living crew member | Gone into the Territory (§24). Same distance as Holdout release (D.13). Cars parked on a facility pad are exempt |

A kit that comes to rest outside the walkable corridor is relocated like a body (Line Plan §12.6), so falling off a bridge doesn't lose it.

**When it ends.** The check runs every tick. If the boiler is ruptured and the kit is lost, the night ends as **Stranded** once the train comes to rest, or at once if it is already stopped. The crew gets the whole coast to work out what just happened.

**Losing the kit without a rupture doesn't end anything.** The crew runs on with no margin, and the fitter's empty shelf says so. Any rupture from then on ends the night when the train stops.

**Settlement.** A stranded night pays like any failed night: no cargo, no body refunds, and crew-loss fees for anyone who died. The settlement sends a dawn freight to tow the train in, and bills for it: a **recovery fee** of 0.5 × the tier's per-car value. The locomotive and every car still coupled come home. The living crew come home too, and aren't charged a crew-loss fee. Appendix E.9 covers the outro.

**Death takes you out of the night, not out of the session.** The dead watch their crew and talk among themselves, wait in a queue, and come back through a **Holdout** at the next halt or yard, if the crew stops for them. Their bodies stay out there, and the settlement pays the crew to bring them home. See Appendix D.

### 23.1 Failure has to be funny

Pillar 5 says the night gets worse and funnier. A failure is only funny when four things are true:

| Condition | Means | Where it's built |
|---|---|---|
| **Witnessed** | Somebody saw it or heard it happen | GRAB rescue windows (A.1) · the radio broadcast of a GRAB (C.8) · the dead watching (D.10) · auto-bookmarks (D.12) |
| **Legible** | Everyone understands what happened, instantly | The six-word rule (A.1) · the voice hard-cut on death (D.2) · silhouettes built for callouts (§32) |
| **Owned** | It traces back to a person's choice | Social test criterion 5 (§20) · the Choir taking the loudest voice (A.7) · failure attribution (C.9) · cause-of-death lines in the incident report (D.12) |
| **Shrugged at** | The world responds with indifference | The settlement clerk (§9) · crew-loss fees and body refunds (D.9) · the Gaunt choosing a corpse over the medicine (A.6) |

**Horror in the telegraph, comedy in the grab.** The shared skeleton (A.1) splits the two cleanly. DORMANT through TELEGRAPH is where the game is frightening: the porcelain face in the lamp, the tiptoeing, the rising Choir. GRAB through PUNISH is where it gets funny: a friend narrating their own death, a crew sprinting after a Whistler carrying someone off at a run, a voice cut off mid-word.

**Whole-train deaths still need a witness.** Derailment kills everyone at once, so nobody is left to watch. The game becomes the witness: a slow-motion cinematic shows every crew member's death to opera (Appendix E), then the incident report names the cause: the ignored Stoker, the un-shot Switchman, the bend taken at 70 (D.12, C.9). The run ends, but it ends with a show, a photo and a culprit.

**Kept straight, on purpose.** The Soot Children and the Passenger are built to disturb, and so is the idea of a corrupted human on the radio (open question 4). None of the comedy levers above are applied to them beyond the shared systems. A few failures that aren't funny are what make the rest land.

**Demo scope.** The cheapest levers, all on systems already in the schedule, ship in the December demo: the voice hard-cut, radio broadcast of GRABs, GRAB victims talking, the Choir taking the loudest voice, cause-of-death lines in the incident report, and auto-bookmarks. Ragdoll bodies, noisy loot and the settlement clerk follow if Phase 6 has room. The derailment cinematic (Appendix E) ships in the demo with at least four tracks: it's the clip the demo will be shared for.

## 24. Decoupling

Leaving something behind is a defining choice of the world. A damaged car. Burning cargo. A stranded player. A valuable shipment. A monster-infested section.

> Save the cargo or save the locomotive?
> Go back for a player or keep moving?
> Repair the car or abandon it?

The railway makes these choices physical. **Once the coupler releases, whatever is behind you disappears into the Territory.**

Sometimes the Territory decides for you. The Passenger uncouples the caboose to eat in peace, and a Car Hugger can leave you no choice but to cut the rear car.

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
| Guard / cannon car | Mounted cannon silhouette, firing arc implied |
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
| Behind mounted cannon positions | Ground away from the train, any dark stretch between lit zones |

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

**VFX supports readability rather than overwhelming it:** steam venting, sparks, smoke trails, cannon flash and powder smoke, cinders, lantern sway, rail grit, furnace flare, drifting fog, subtle corruption particulate. Mechanical first, supernatural second.

## 32. Art serves the voice channel

**This is the connective tissue between art and every other system in this document.**

Because the game is voice- and communication-driven, the visuals exist to let players call things out fast. That requires distinct car silhouettes, identifiable roof spaces, readable gap and coupling danger, good landmarking along the train, enemies describable in a phrase, obvious light states, and visually unmistakable switches, ladders and exterior interaction points.

> **If players can't verbally describe the game state quickly, the art is failing the design.**

Silhouette legibility is not an aesthetic preference here. It is a coordination mechanic, and it sits alongside the six-word enemy rule as one of the two things that make the shouting work.

### The HUD: your hands and the dark

*The director's decisions of 2026-10-07: "There's too much UI on screen. I like the way Repo and Lethal Company do their UI/UX designs." Then, on the overhaul: keep the speed figures, "PULL CORD : [E]", a setting to hide the corner's controls, and the max speed on the map for every stretch that can't take top speed; discard "long prompts that foretell the consequence of actions. Consequences need to be learned."* The screen is the night, and the voice channel carries the rest. The HUD keeps to the same rules as those two games:

- **Only your hands are always there.** A dot to aim with, and the hotbar: small slots with a picture of each tool. A tool's name shows for a moment after a change of hands.
- **What you look at is said at the crosshair.** The action and its key, in fine print: "PULL CORD : [E]", "SHOVEL COAL : HOLD [E]". A short state only when it stops you ("POINTS HELD", "NO SHOT"), and a hold's progress.
- **Prompts never foretell.** Nothing says what an action will do: whether it's loud, what it mends, who hears it, that it needs two, or what happens if you don't. Consequences are learned, as in Lethal Company. The stop's status says what's there and what's under way, not how to work it.
- **What you hold is said in the corner.** The bottom right, in fine print: the keys for what's in your hands, the cab's controls or the gun, a line each. In the cab, the speed sits over them as a figure, read against the boards. A setting (CONTROL HINTS) hides the keys; the speed and what you're holding stay.
- **The rest comes when it matters, then goes.** A place's name as the train nears it. The cold, as you go into a deeper step. The dawn clock in the night's last stretch. The ping in the lobby, and on the line only when it's bad. The noise meter only once the crew's loud.
- **The world says it first.** The gauges and the run map in the cab, the route card (where the train is is pencilled on it), the supplies view and the roster are things you look at or open. They're not on screen. Every stretch of the line that would derail the train at its top speed has its max speed on the map and the card.
- **No frames in play.** Text sits on the picture with a shadow. Only the panels you open, and the run's end, are framed.
- **Alarms are rare and short.** The headline is big only when it's urgent. The hazard telegraphs (a tunnel mouth, a bend you're too fast for, the boiler about to go) keep their warnings and their speed figures; what to do sits under them in fine print, without the outcome.

The timings are in `content/tuning/hud.json`; the engineering is in ARCHITECTURE §8 note 285.

### Accessibility

*The director, 8 Oct 2026, on the list of what's next for the UI: a text size, a colourblind-safe palette, captions, a light first-night onboarding and a polish pass in the real window: "these are all quite important".* Each is a setting, and none of them tells a player what an action will do: the rules above still hold.

- **TEXT SIZE** (100%, 125%, 150%): the HUD's print and the menus', bigger. The whole overlay is drawn on a smaller canvas and stretched over the window, so everything on it grows together. A long line wraps or is cut short where it would leave the frame (ARCHITECTURE §8 note 347).

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

**Still needed:** interest management, delta compression, and lag compensation for cannon shots and melee (required before either feels fair).

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
| 4 | 14–17 | Demo roster of five, cannons and melee, two facilities |
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

1. **Does a cannon reload need two players, or is it just slower solo?** A two-person reload makes the gunner a pair: more social, and more expensive.
2. ~~**Can the conductor see whether everyone is aboard, or is roll call purely verbal?**~~ **Answered in v1.3: verbal.** There is no aboard indicator for the conductor or anyone else. Roll call is a question somebody has to shout, and the engine leaving without someone is this game's version of Lethal Company's ship leaving at midnight (§17, §23.1). D.6 already keeps the queue hidden from the living.
3. **How many cars before the flank is genuinely indefensible** — and is that ceiling the real progression cap?
4. **Do corrupted humans ever use the radio?** A voice on your own channel that isn't crew is the single most disturbing thing available in this design. Possibly too much.
5. **Does cold need a meter,** or is it enough that being outside too long simply kills you?
6. **How do enemies stay silhouette-legible in heavy fog at distance?** The art direction wants fog doing the atmospheric work, and the design wants creatures identifiable before you understand them. Those pull against each other. Likely answers: rim lighting from practical sources, or enemies that are legible by *motion* signature before shape.
7. **Do cargo cars visually show what's inside?** Gunpowder versus livestock versus chemicals changes how the crew should behave around a car. If the exterior reads it, callouts get faster. If not, the interior becomes worth learning.
8. **Is corruption ever visible on the train itself** — growth in the couplings, in the tender, under the cars? It would give the flank a visual tell and make the home slowly stop feeling like home.
9. **The Gaunt: does talking lower its aggro, or only stop it rising? And whose voice counts** — only the waker's, or anyone within 8m?
10. ~~**What can dead players do while observing?**~~ **Answered in Appendix D.** They spectate, talk on a dead channel the living never hear, call out from Holdouts with canned lines, and get one creature vote. Only the player about to be freed can speak to the living, over Live Mic at the Holdout door (D.15 tracks the Passenger risk).
11. **Is hand-carried loot a second economy next to freight, or part of it?** A rescued child is both. v1.3 gives some loot a risk of its own, noise while carried (§19, C.4), which pushes it toward a second economy with its own decisions.
12. ~~**At crew 2, which group-based enemies are still fair?**~~ **Swept (note 305): all of them, played right.** Every grab can be broken by the one other crewmate at crew 2 (`dt audit grabs --crews 2`), Ribbits and Tippy Toesie included. Two were losing crew-2 nights, both to how the bots played them: the Choir took a gunner from a car with its door open (get behind a shut door), and the Car Hugger ate a gunner who went down to club it alone (with nobody but the driver, keep clear and let it take the car). Played so, none loses a crew-2 night. The bots never drew a Ribbit pack or Tippy Toesie at any crew size; people who split up will.
13. **What counts toward the Choir's loudness threshold, and over what time window?** Too sensitive and the core shouting loop summons it constantly. Whatever the answer, shushing counts: a crew hissing "shut up" at each other is part of the noise, and the Choir takes the loudest contributor (A.7, C.7).

---

---

# APPENDIX A — BEHAVIOUR TREES

## A.1 The shared skeleton

Every enemy runs the same six-state spine. Consistency is deliberate: it makes creatures learnable across the roster, and it makes them cheap to implement, which matters on a 26-week build.

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
                                       BREAK OFF                     GRAB ──[friend interrupts]──► BREAK OFF
                                            │                          │
                                            │                [rescue window: 8–20s]
                                            │                          │
                                            │                       PUNISH
                                            └────────► DORMANT ◄───────┘
```

**The fairness contract.**

- **TELEGRAPH always precedes COMMIT.** No enemy may punish a player who was given no window. This is the rule the agent harness checks hardest.
- **Kills go through GRAB.** Any enemy that kills a player holds them first, long enough for a friend to act. The only exceptions are whole-train events (derailment), which carry their own telegraph.
- **The rule fits in six words, and one death teaches it.**

**Sense triggers** are one of: sound, light, heat, movement, scent, vibration, sight, absence. Enemies sharing a trigger in the same zone must have tells that stay distinguishable by ear and by eye.

*All timings below are first pass, built for feel-testing.*

---

## A.2 Forward

### TRACK DOLL · sight
```
STANDING  motionless on the rail ahead
          └ TELEGRAPH: white porcelain catches the forward lamp at ~200m
IF train stops short → VANISH (no threat for the rest of the run)
IF train strikes it  → HAUNT
HAUNT     aboard; giggles in random cars; seen admiring cargo
          └ vanishes when approached from one side
TAMPER    cab unoccupied → plays with throttle and brake
          └ only once she's been left alone a while, and worse the longer (F.1)
CORNERED  approached from both doors at once → cannot vanish → can be bludgeoned
APPEASED  given a toy loot item → steals it, leaves for the run
```
**No lethal punish of its own.** It costs speed control and cargo, and it replaces Deadman as the reason someone stays in the cab. At full speed only trains of 6 cars or fewer can stop within 200m; at cruise, all can.

---

## A.3 Rear

### CINDER HOUNDS · heat, scent
```
ROAM      offscreen, tracking the train's heat signature
ACQUIRE   heat above threshold OR food/livestock cargo aboard
          └ TELEGRAPH: distant howling behind, audibly closing
PURSUE    matches speed; gains on every grade
LEAP      within 30m → jump for the rear car
BOARD     on success → PACK FIGHT
PACK FIGHT several hounds; each takes several bludgeons; bites hard
BREAK OFF rear cannon hit, or train exceeds their sustainable speed
```
**Every cannon shot feeds the loudness meter.** This pair with the Choir is the roster's central tension.

### CAR HUGGER · vibration
```
LURK      beside the track on low ground
LATCH     rear car passes → clamps on
          └ TELEGRAPH: heavy grinding from the rear; top speed capped
FEED      eats the car's shell and loot steadily
SWALLOW   player in front of its mouth → GRAB (~10s); victim can still talk
          └ interrupt: friends pull them free or hit it
PUNISH    window expires → eaten
FINISH    car fully eaten → drops away with it
COUNTER   uncouple the car (it leaves with it)
          OR bludgeon it from the rear platform: fast as a group, slow alone
```
The speed cap stacks with grades. A capped long train may not crest a hill.

---

## A.4 Flank

### CLIMBERS · movement
```
PACE      runs alongside at track level
COUNT     compares its pack to players within 8m of each coupling gap
MOUNT     gap held by fewer players than Climbers present
          └ TELEGRAPH: scrabbling at the gap, visible from adjacent roofs
TRAVERSE  along the roofs toward the engine
ENTER     first unlit or unoccupied car → interior threat
COUNTER   outnumber them at the gap; bludgeon any that mount
```
**Scales directly with train length.** More cars means more gaps to hold with the same crew.

### DRAGGERS · movement
```
CLING     underside of the car edges, out of sight
REACH     player within 1m of an edge
          └ TELEGRAPH: a limb visible at the edge lip ~1s prior
GRAB      pulls the player over the side; they hang (~8s), still talking
          └ interrupt: a friend hauls them back up
PUNISH    window expires → dragged under
COUNTER   walk the centreline
```
Grab range +50% at max speed. Makes roof traversal a route decision.

### WHISTLER · absence (the train stopping)
```
HIDE      in a coupling gap; stays hidden while the train moves
WHISTLE   blows the train's own whistle (feeds the loudness meter)
          └ TELEGRAPH: the whistle sounds with no hand on the cord
WAIT      train stopped → watches its gap
GRAB      a player passes the gap → snatched and carried off at a run
          └ interrupt: the crew chases on foot and bludgeons it
NEST      reaches its nest → paralyses the victim
PUNISH    ~20s at the nest → eaten
COUNTER   check the gaps after a whistle; move in pairs at stops
```
**Never grabs from a moving train.** Every rescue is a chase on foot, which makes every stop more dangerous.

---

## A.5 Interior

### STOKER · heat *(the director's decision, 2026-10-06; replaces the low-fire and open-door entry)*
```
DRAWN     the firebox run hot (above a tuned heat) for a while; a low fire never draws it
          └ PERCH: on the smokestack, watching the heat
BOARD     at the coal bunker: onto the coal, then across the footplate to the fire door
          └ TELEGRAPH: scraping on the coal, a sick glow there, the hiss (the fireman hears it)
CATCH     a blow on the way in drives it off, and doesn't burn
FEED      in the firebox: it eats the fire, and the pressure lurches up toward rupture, the
          safety valve held shut; the brake comes off and the speed climbs with it
          └ TELEGRAPH: the gauge swinging, wrong-coloured glow, train accelerating
RUNAWAY   speed exceeds the limit for the next curve or grade → DERAIL (or the boiler ruptures first)
COUNTER   vent: pressure and speed drop, time is lost (buys time only)
KILL      open the firebox (a shovelful) and bludgeon it; each swing burns the attacker;
          a swing at the shut door only rings on the iron
BREAK     beaten, no Stoker returns for two to three minutes
```
Fully preventable. **The trade-off is the fire**: running hot is fast and draws it; running cool is safe and slow. Once it's in, it's urgent: someone must drop what they're doing. The counter has a clock cost, and killing it has a health cost. Numbers in enemies.json `stoker`.

### TIPPY TOESIE · absence (a player standing still)
```
STALK     picks a player idle and alone; approaches slowly from behind
          └ TELEGRAPH: faint tiptoeing; visible to anyone facing it
SEEN      target looks at it → flees → picks another target, or waits 20s
GRAB      reaches the target → covers their mouth; their voice goes muffled
          suffocates over 20s
          └ interrupt: any friend hits or pulls it → it flees
PUNISH    window expires → death
COUNTER   don't stand still alone; watch each other's backs; bludgeon to kill
```
It naturally targets the conductor and boiler player. At crew 2, shorten its reach or slow its approach.

### FIRE FLIES · light
```
DRIFT     lineside in dark sections
SWARM     a lit lamp inside a car → they gather on it
          └ TELEGRAPH: glow and buzzing around the lamp
IGNITE    linger ~20s → the car catches fire
SPREAD    fire grows and jumps couplings over time
BREAK OFF lamp turned off, or the train pulls away at speed
COUNTER   lamps off; extinguishers (see Appendix C)
```
The lamps-off answer blinds you to the Track Doll. **That contradiction is the point.**

---

## A.6 Outside

### RIBBITS · sight
```
IDLE      pack of 2–4 (never more than crew size) in yards and villages
HOP       toward the nearest group, in bursts averaging ~4 m/s
COUNT     a group is players within 8m of each other
IGNORE    group size ≥ pack size → no attack
TONGUE    pack outnumbers the target and is within ~4m
          └ TELEGRAPH: throats swell, the pack halts and lines up
GRAB      target frozen, can still talk; pack hops in slowly (~8s)
          └ interrupt: friends arrive and the count evens, or bludgeon them
PUNISH    the pack devours
COUNTER   stay grouped; run to the group or the train (player run 5.5 m/s)
```

### THE GAUNT · sound (silence)
```
ASLEEP    curled up in a village or yard
          └ TELEGRAPH: a folded, spindly shape breathing slowly; stirs as you near
WAKE      a player comes too close
FOLLOW    trails its waker at arm's length; boards the train with them
LISTEN    every 5s of silence near it → aggro +1
          └ TELEGRAPH: it leans in closer and tilts its head as aggro climbs
ATTACK    aggro at threshold → heavy attacks on the nearest player
COUNTER   kill it (a group job; it hits hard)
          OR lead it to a car → it takes the most valuable item → leaves for the run
```
Talking only holds it off. **It punishes silence while the Choir punishes noise**, so players must chat to it quietly. Whose voice counts, and whether talking lowers aggro, are open questions (Part Eleven).

**A body is an item.** It ranks at its refund value (D.9), so the Gaunt will sometimes pass over the medicine and leave with a dead crew member. It carries the body out at walking pace, in full view, and the crew can still chase it down before it clears the train.

### FOLLOWERS · scent
```
WAIT      facility grounds
LATCH     onto a player's back, between the shoulder blades
          └ TELEGRAPH: a small lump that twitches; its host can't see it
RIDE      boards with its host; harmless to them
DROP      once aboard → crawls to the car holding the most loot
NEST      builds a nest over ~60s → then eats that car's loot steadily
COUNTER   bludgeon it off a friend's back (kills it)
          OR kill it while it crawls, OR bludgeon the nest
```
**The loot is the risk, not the host.** The fix is a friend clubbing you in the back.

### SOOT CHILDREN · sound
```
CALL      a child's voice calling for help from the dark
ROLL      50/50 real survivor or Soot Child
          (the first one a host player ever meets is always real)
          └ TELEGRAPH: a Soot Child has black eyes, blackened hands and feet
REAL      carried to a cargo car → the most valuable cargo; cannot be harmed
LUNGE     Soot Child, player within 5m → turns visibly inhuman, pins them
GRAB      drains blood; every second adds hits needed to kill it
          the victim's voice grows weaker and quieter
          └ interrupt: friends kill it
PUNISH    drained → death
```

---

## A.7 Structural

### THE CHOIR · sound
```
ABSENT    not present by default
LISTEN    crew loudness (voices, cannons, whistle, machinery) held above threshold
BUILD     long, rising telegraph
          └ crew goes quiet before the commit point → skipped
COMMIT    the swarm arrives: several small flying ghosts
SEIZE     anyone outside, on the roofs, or not behind a closed door → GRAB
          loudest first: highest loudness contribution during BUILD (C.7)
          └ interrupt: kill the one holding them (many hits; they hit back hard)
BESIEGE   rattle and bang on closed doors until loudness drops
DISPERSE  quiet held → they leave
LIMIT     one crew member taken → the Choir is gone for the rest of the run
```
**Fully preventable by hushing in time.** Fighting it is possible and almost always a mistake.

**Loudest first is the blame rule.** Among exposed players, the Choir takes whoever contributed most to the meter during BUILD: their voice, a cannon they fired, the whistle they pulled, a squeaker in their hands. It stays fair because loudness is entirely player-controlled, and a player behind a closed door is safe however loud they were. The one shouting at everyone to shut up is usually the one it takes.

---

## A.8 Corrupted humans

### THE PASSENGER · absence (a player alone)
```
BOARD     during a facility stop, unnoticed
BLEND     old coat and cap; passes for crew in the dark; hangs near the rear
          └ TELEGRAPH: it never speaks on proximity voice
STALK     waits for a player alone
GRAB      drags them toward the caboose at walking pace
          └ interrupt: other crew kill it (the victim can't break free alone)
UNCOUPLE  reaches the caboose → uncouples it → rolls away into the dark
PUNISH    victim and caboose lost
```
**The only tell is silence**, in a game entirely about talking.

### THE SWITCHMAN · vibration
```
WAIT      at a junction ahead
          └ TELEGRAPH: a tall figure at the lever in the headlamp;
                       the junction lamp shows the wrong signal
THROW     as the train passes → wrong route: a dead line that leads nowhere, to buffers in the dark
          (the clock's cost: stop, back out, set the points back by hand on the ground)
DEAD END  the switch never derails the train (the director, 7 Oct 2026: "the switch itself shouldn't
          cause derail, it should be lines that lead nowhere"); running off the end of the dead line does
          └ TELEGRAPH: the cab's warning, the bell and "BUFFERS AHEAD", a full lead before it's too late
            to stop; a hit short of that lead is the buffer stop's damage alone
COUNTER   a forward cannon hit (one shot, long reload): killed before the points, its lever falls back
          OR stop and bludgeon it to death
          OR take the dead line and stop short of its end
```

### GRUMBLER · sound
```
GNAW      on the crane and food crates at a facility
          └ TELEGRAPH: audible gnawing; visible on the crates
FERAL     interrupted or hit → hunts whoever damaged it last
REGEN     heals if only one player has hit it in the last ~5s
LOADED    craned aboard with the crates → eats cargo on the train
COUNTER   gang up and kill it, or leave it alone;
          a spotter checks the crates before every lift
```

---

## A.9 Implementation notes

**The shared GRAB state** serves Car Hugger, Draggers, Whistler, Tippy Toesie, Ribbits, Soot Children, the Choir and the Passenger. Build it once, with a timer, an interrupt and a hook for voice effects. **The victim talks at full clarity by default** (C.1). Tippy Toesie's muffle and the Soot Child fade are the only voice overrides.

**GRAB and PUNISH are where the comedy lives** (§23.1). Every GRAB start and every PUNISH writes a failure-attribution record (C.9) and an auto-bookmark (D.12), so the run-end screen has both the cause and the picture.

**Voice-system enemies.** The Gaunt reads silence, the Choir reads loudness, Tippy Toesie muffles its victim, Soot Child victims fade, and the Passenger never speaks. All five sit on the voice layer, so it is a gameplay system, not just comms.

**Fully preventable enemies.** Track Doll (stop in time), the Choir (hush before commit) and the Stoker (run cool, or catch it on the way in) can each be reduced to zero threat by correct play. A roster where everything is unavoidable stops rewarding mastery.

**Cost-only enemies.** The Track Doll and the Switchman's routing throws cost time and control rather than lives. Keep this category small.

**What the harness verifies per tree.** Telegraph precedes commit in every path · every GRAB is interruptible by the crew actually present · reaction and rescue windows are achievable at all crew sizes · at least one counter is reachable with the crew's actual equipment · no two active enemies present indistinguishable tells.

---

---

# APPENDIX B — SPAWN RULES

## B.1 The director

Enemies are not rolled independently. A **pressure director** spends a budget across the run, which is what allows deliberate contradiction stacking instead of random pile-ups.

**The orchestrator** (*proposed, 7 Oct 2026*; App. F.3): a layer over the director that plans threats against the players currently active, where each is and what each is doing, and the threats and upkeep that fill the run between stops. The outline is [orchestrator.md](orchestrator.md) (ARCHITECTURE §8 note 328).

### Budget

```
RUN BUDGET = base(route tier) × length multiplier × crew multiplier
```

| Route tier | Base budget |
|---|---|
| Local | 90 |
| Frontier | 150 |
| Dead lines | 230 |
| Deep territory | 330 |

These match the build (`director.baseBudget` in `content/tuning/enemies.json`). They are about twice the v1.1 figures, to pay for the build's pressed spawns: after the 100-night playtest the director would not let the line go quiet for long, and under the pressure model (below) a long quiet still presses it to send something.

**Length multiplier:** `1.0 + (0.15 × cars beyond the third)`
**Crew multiplier:** `0.7 + (0.12 × crew)` — capped at 1.6. *The crew counted is the crew alive now, each second, not the crew the night started with (the orchestrator, 7 Oct 2026, App. F.3; ARCHITECTURE §8 note 336): a crew that has lost players spends like the smaller crew it is.*

Budget is spent across the run against a rising curve, not evenly. Roughly 15% before the first facility, 45% across the middle, 40% in the final approach.

### Pressure: when the director spends

*Design decision, 2026-10.* The budget says how much; **pressure** says when. Monster spawns are algorithmic, driven by escalating pressures, never by fixed timers. Once a second after the grace period the director's pressure builds:

```
pressure per second = tier × conditions × relief × busy × escalation × (base + quiet + loudness + cargo)
```

| Input | What raises it |
|---|---|
| **Escalation** | How far into the night: along the line or toward dawn, whichever is further. ×1 at the gate, ×4 at the end |
| **Quiet** | Seconds since a threat last came at the crew. Quiet builds pressure, faster the longer it lasts |
| **Loudness** | The Choir meter. A crew loud enough to draw the Choir draws everything else too |
| **Cargo** | Each car-load aboard, by what it's worth. Cargo lost stops drawing them |
| **Tier** | Harder tiers build faster |
| **Conditions** | Dark (the lamp out), cold, rain and wind add a little |

The **relief valve** eases it when the crew is losing: by the share of the crew still alive (squared), and less again for each crewmate badly hurt, so a night going wrong doesn't snowball. While threats are already engaged it builds more slowly (the crew's busy), less so as the night goes on: toward the end the director stops waiting for the crew to finish.

**The first threat is drawn** (*design decision, 2026-10*; ARCHITECTURE §8 note 287). The night's first threat answers something a crewmate did: the whistle, a cannon shot, raised voices, a noisy toy, the firebox run hot, a lamp lit, cargo taken aboard. Each is credited to whoever did it, and fades over half a minute. Past the grace, the first draw big enough is answered from the dark, a distant call and eyes at the lamp's edge, and the threat follows a few seconds later, weighted toward what that draw calls (the whistle the Whistler, a lamp the Fire Flies, the cargo the Car Hugger). A crew that draws nothing is listened for longer, until the pressure presses. The incident report names the draw and who made it (C.9, §23.1 "Owned").

Past the **threshold** the director spends: on what its weights, wants, pairs, gates and caps pick, as before. **Spending relieves the pressure** by the spawn's cost. Well past the threshold (a long quiet, or a night the budget's curve can't keep up with) the post-event cooldown gives way and the curve may be overdrawn a little. The dead's votes move weight between creatures and never touch the pressure. All the coefficients are in `director.pressure` in `content/tuning/enemies.json`; the systems spec (B.9) has the numbers.

### Pressure cost per enemy

*First pass.*

| Cost | Enemies |
|---|---|
| **2** | Followers, Draggers, Fire Flies, The Switchman |
| **3** | Track Doll, Cinder Hounds, Climbers, Whistler, Stoker, Ribbits |
| **4** | Car Hugger, Tippy Toesie, The Gaunt, Soot Children, Grumbler |
| **5** | The Passenger |
| **—** | The Choir (not spawned; triggered by the loudness meter) |

### Want balance

The director tags each enemy with the player want it attacks, and aims for a target share of the run budget. Players want, in order: to not die, to stay near friends, to get a story, to look good in front of the crew, then to get the train and the money home.

| Tag | Target share | Enemies |
|---|---|---|
| **Kill** | 40% | Cinder Hounds, Draggers, Whistler, Stoker, Tippy Toesie, Ribbits, The Choir |
| **Split** | 25% | Track Doll, Climbers, The Gaunt |
| **Trust** | 20% | Followers, Soot Children, The Passenger |
| **Cargo** | 15% | Car Hugger, Fire Flies, The Switchman, Grumbler |

Split is the thinnest category and the best target for a post-launch addition.

**Dead vote.** Each dead player gets one vote per run that raises one creature's spawn weight, ×1.2 per vote and capped at ×1.5, applied within that creature's want tag so the target shares hold (Appendix D.11). It never changes budget, gates, caps or pacing.

### Hard caps

Enforced regardless of budget:

| Constraint | Limit |
|---|---|
| Concurrent flank threats | **2** — the middle is uncoverable; three is unfair |
| Concurrent interior threats | 2 |
| Concurrent outside threats | 2 |
| Corrupted humans | **1** active at a time |
| Total concurrent active | 4 at crew ≤4 · 6 at crew ≥6 · and never more than ceil(0.75 × the crew alive), one a player: 1 at crew 1, 2 at crew 2, 3 at crews 3–4 (*the orchestrator, 7 Oct 2026*; note 336) |
| Same tell type, overlapping range | **1** — tells must stay distinguishable |

### Pacing rules

**Grace period.** No threats for the first 20–90 seconds past the gate (design decision, 2026-10): the length is picked per night from the night's seed, shorter on the harder tiers, so a crew can't count the seconds. The tonal transition needs room, and the crew needs to settle into stations. Pressure starts building when the grace ends.

**Facility lull.** ~20 seconds of calm on arrival before facility threats activate. Lets the crew commit to a plan before it falls apart.

**Post-event cooldown.** After a spawn, a trough of at least 25–45 s (design decision, 2026-10; was 30–60 s). Sustained pressure reads as noise; pressure with troughs reads as rhythm. The spawn's relief of the pressure makes the trough; the cooldown gives way only when the pressure is well past its threshold.

**Terminus approach.** One deliberate spike, then a hard stop 500m out. Nothing may spawn inside the final approach — the last stretch is for surviving what's already aboard.

### Contradiction seeding

The director draws pairs from a **conflict table** rather than spawning independently. This is the design spine expressed as a spawn rule.

| Pair | The bind |
|---|---|
| Choir + Cinder Hounds | Hush vs. only the cannon stops them |
| Choir + The Gaunt | Stay quiet vs. keep talking to it |
| Fire Flies + Track Doll | Lamps off vs. the doll only shows in the light |
| Track Doll + facility loading | Keep someone in the cab vs. the facility needs everyone |
| Whistler + any facility stop | Stopping is the job vs. stopping is when it grabs |
| Tippy Toesie + Stoker | The boiler player is idle and alone vs. the firebox needs watching |
| Ribbits + The Switchman | Don't go alone vs. someone must reset the switch |
| Car Hugger + climbing grade | Kill it or cut the car vs. the speed cap loses the summit |

At least one pair per run on Frontier and above. Two on Deep Territory.

---

## B.2 Forward

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **Track Doll** | Straight track with a clear 200m sightline | Any tier (first pass) · once per run | Weight up if the cab has been left empty earlier in the run |

**Note:** track debris (formerly Sleepers) was level content placed at line generation, not a spawn. *Retired by the director's decision of 6 Oct 2026 (§22): a derailment comes only from the driver taking a bend, a weak bridge or a switch over its limit. Off by default (enemies.json sleepers.enabled); a mod can bring it back.*

---

## B.3 Rear

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **Cinder Hounds** | Behind the train, out of visual range, after sustained speed | Any tier · requires a rear cannon or rearmost car | ×2.5 with livestock or food cargo · ×1.5 when the boiler runs hot |
| **Car Hugger** | Marsh, water crossings, low ground | Train length ≥2 · cannot spawn on grades | Weight up at low speed |

**The hound run** (*7 Oct 2026*, App. F.3; ARCHITECTURE §8 note 328): apart from the director's packs, a train run faster than the packs can follow (2.4 km at 19 m/s or more without slowing, sooner with the boiler hot) draws a stream of Cinder Hounds faster than it is, sized to the crew alive (2 to 6), outside the budget and caps. The guns answer them one hound at a time ([orchestrator.md](orchestrator.md) §5.3, §6.1).

---

## B.4 Flank

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **Climbers** | Alongside at track level, mounts at a coupling gap | **≥2 coupling gaps** · minimum speed threshold | Weight scales directly with gap count — the length curve made literal |
| **Draggers** | Pre-attached beneath car edges at generation or facility departure | Train length ≥2 | Dormant until a player is on the roofs |
| **Whistler** | Hides in a coupling gap; boards at any stop | Train length ≥2 | Weight up on routes with more planned stops |

---

## B.5 Interior

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **Stoker** | **Condition-triggered, not placed.** Boards at the coal bunker, crosses to the firebox *(2026-10-06)* | The firebox run above a tuned heat for a while; never within a few minutes of the last one's defeat, nor in the fortress | None |
| **Tippy Toesie** | Any car, or the ground near the train | Crew ≥2 · any tier (first pass) | Weight up per player idle and alone |
| **Fire Flies** | Lineside in dark forest and open sections | ≥1 lamp lit inside a car | ×2 at night depth · ×0 if every car lamp is out |

**Design note:** the Stoker costs budget only when it actually fires. It is a standing threat the crew controls entirely, which is why it is safe to leave uncapped.

---

## B.6 Outside

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **Ribbits** | Yards and villages | Any tier · pack capped at crew size | Weight up per player on the ground |
| **The Gaunt** | Asleep in villages and yards | Frontier+ (first pass) · once per run | Weight up on long facility stops |
| **Followers** | Facility grounds; latches onto a disembarked player | Requires an excursion · any tier | Weight up per additional player on the ground |
| **Soot Children** | Near facilities and dead settlements | Crew ≥2 | True 50/50 with a real child survivor; a host player's first-ever call is always a real child |

---

## B.7 Structural

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **The Choir** | **Not spawned.** Triggered when crew loudness is held above threshold | Available all run until it has taken one crew member | Livestock raises baseline loudness · disabled for the rest of the run after its first victim |

---

## B.8 Corrupted humans

| Enemy | Spawn context | Gates | Weighting |
|---|---|---|---|
| **The Passenger** | Boards during a facility stop | **Dead lines+ · crew ≥3** (needs a crowd to hide in) · once per run | Weight up if the crew were split across several facility tasks |
| **The Switchman** | Junction network ahead of the train | Frontier+ · **route must contain ≥3 junctions** | Weight up on routes with dead-line branches available |
| **Grumbler** | Facilities with a crane and crates (Foundry first) | Any tier (first pass) | ×2 with food crates at the facility |

Corrupted humans are the rarest category by design. **Maximum one active at a time**, regardless of budget.

---

## B.9 Cargo modifiers

Cargo changes the run rather than just scoring it. These multipliers apply to spawn weight.

| Cargo | Effect |
|---|---|
| **Livestock** | Raises the crew's baseline loudness · Cinder Hounds ×2.5 |
| **Food** | Cinder Hounds ×2 · Grumbler targets food crates |
| **Chemicals** | Fires spread faster · firing a cannon near chemical cars is lethal to the crew |
| **Gunpowder and shot** | No spawn change — but every fire is worse |
| **Coal / timber** | No spawn change — fire cascades escalate faster |
| **Child survivor** | Highest payout · carried by hand · no spawn change |
| **Comet-derived material** | **All weights ×1.4** · Passenger gate relaxed by one tier |

Comet material is the high-risk contract: it is the best freight payout and it makes the entire world more interested in you.

---

## B.10 What the harness verifies

| Check | Method |
|---|---|
| **Budget sanity** | No generated run exceeds survivable pressure at any crew size |
| **Cap enforcement** | Flank never exceeds 2 · one corrupted human · no duplicate tell types in range |
| **Gate correctness** | No length- or crew-gated enemy appears below its threshold |
| **Contradiction quality** | Every seeded pair is solvable — hard, not impossible |
| **Rescue windows** | Every GRAB is interruptible by the crew present, at every crew size |
| **Want balance** | Generated runs land near the target shares |
| **Pacing shape** | Grace period, troughs and terminus silence all present; the director's own spawns rise toward the end of the night (the harness's pressure trace) |
| **Tier progression** | Deep territory is meaningfully harder than Local at matched crew and length |
| **Vote bounds** | Dead-vote weighting never exceeds ×1.5, keeps want-tag shares, and never bypasses a gate, cap or once-per-run limit |

Spawn tuning is the single largest use of the agent harness. Seventeen enemies against four tiers, seven crew sizes and a variable consist length is a space no human tester can cover — but it can be swept exhaustively overnight.

---

---

# APPENDIX C — SUPPORTING SYSTEMS

The roster depends on nine systems. Each is shared by several enemies, so each is built once. The ninth, failure attribution, was added in v1.3.

1. **The GRAB rescue state.** Part of the shared skeleton (A.1): a held player, a timer (8–20s), an interrupt that ends in BREAK OFF, and a hook for voice effects. **A held player can talk at full clarity** unless the enemy applies a voice effect (C.8). Their commentary is the point of the window: it's the call for help, and it's the joke.
2. **Melee.** The core verb. The tools already on the train are the weapons: shovel, wrench, crowbar. The boiler player's shovel doubling as the crew's best club is intended tension. Server-authoritative hits with lag compensation.
3. **Cannons.** Bullets and guns are rare in this world; crude cannons and crude gunpowder are not. Each mounted cannon has a full manual reload (powder, ball, ram, fire), so every shot is a timed decision. Powder and shot are stocked at departure.
4. **Hand-carried loot.** Small items players carry alongside car-level freight: toys (for the Track Doll), crates (the Grumbler), salvage, and a rescued child. Carrying runs at 2.8 m/s with no climbing, per the systems spec. **Some toys are noisy** (a squeaker, a music box, a wind-up drummer). They emit sound while carried or jostled and feed the loudness meter in the carrier's name (C.7). They are worth more than quiet toys, and the Track Doll likes them just as much.
5. **Fire and firefighting.** Fire grows and jumps couplings. Every car has a wall-mounted extinguisher that players grab, use and put back. Each holds limited charge and recharges slowly on its mount. Bigger fires need more extinguishers at once.
6. **Group counting.** Players within 8m of each other count as a group — the full-clarity voice radius. Used by Ribbits and Climbers.
7. **The crew loudness meter.** Combined loudness from voices, cannons, the whistle and machinery, measured over a few seconds so single shouts don't count. It drives the Choir and is fed by livestock and noisy loot. **The meter keeps a per-player share**: each player's voice, the cannon they fired, the whistle they pulled and anything noisy they carry. Machinery, livestock and a whistle blown by the Whistler belong to nobody. The Choir uses the shares from its BUILD phase to pick who it takes first (A.7).
8. **Voice effects on the server.** Tippy Toesie muffles its victim; Soot Child victims fade as they're drained; the Gaunt listens for silence; the Passenger never speaks. All live in the existing voice layer. Two more, added in v1.3:
   - **Death hard-cut.** At PUNISH, or any instant death, the victim's proximity voice and radio stop on the tick, mid-word, with no fade. The same tick routes their mic to the dead channel (D.10).
   - **Radio broadcast of a GRAB.** A grabbed player holding a radio keys it open for the whole GRAB, so everyone else on radio hears it with no way to tell which car it's coming from. It closes at BREAK OFF or with the hard-cut. Radio dies in tunnels and mine spurs as usual, so a GRAB there is heard only by whoever is close.
9. **Failure attribution.** A server-side log written at every GRAB start, PUNISH, instant death, car loss, boiler rupture, stranding and derailment. Each record holds what happened, who it happened to, and the **contributing action**: the most recent crew input the rules of that failure name, and who made it. It feeds the incident report (D.12) and never touches money, progression or the director. It records facts, not fault, and it has no demerits.

   | Failure | Contributing action recorded |
   |---|---|
   | Track Doll struck | Who was on the throttle, and the speed at impact |
   | Track debris derailment *(retired 6 Oct 2026, §22)* | Who was on the throttle, and the speed at impact |
   | Switchman derailment *(since 7 Oct 2026: off the end of the dead line it threw the train down)* | Whether the forward cannon was crewed, and by whom |
   | Stoker runaway or derailment | Who last fuelled or tended the firebox, and how long it had been unattended |
   | Boiler rupture | Who last fired or vented the boiler, and how long it sat at 100 |
   | Stranded | How the engineering kit was lost, and who last held it (and, for a decoupled car, who pulled the coupler) |
   | Choir seizure | The victim's share of loudness during BUILD, and the top contributor if different |
   | Fire Flies fire | Who last lit that car's lamp |
   | Followers nest | Who carried it aboard |
   | Grumbler aboard | Who ran the crane for that lift |
   | Car lost, decoupled | Who pulled the coupler, and what was inside (including bodies) |
   | Left behind | Who was on the throttle when the train pulled away, and how far back the player was |
   | Jumped at speed | The victim, and the speed |
   | Any other GRAB death | The victim, the nearest living crew member, and their distance |

---

# APPENDIX D — DEATH, HOLDOUTS AND RETURN

*Added in v1.2. This appendix is the single source of truth for death, respawning and body recovery.*

> **Supersedes:**
> - Systems Spec **Part C** in full (C.1 Death, C.2 The Vigil). **The Vigil is cut.**
> - Systems Spec **Part E**, the *Drop-in* and *Drop-out* rows and the paragraph beneath them
> - Systems Spec **Part G**, open question 3 (Vigil cap)
> - Line Plan **§11.2** (pickup points are replaced by Holdouts)
> - Line Plan **§11.3** (halts and dead towns now carry a Holdout and become optional stops)
> - Line Plan **§17.3** late-join note (late joiners receive the Line Plan on joining, not at facility drop-in)
>
> Line Plan **§12.6** (walkable body recovery) still applies unchanged.
>
> **Resolves** Part Eleven open question 10 (what dead players can do).

Once a run has left the gate, **a Holdout is the only way back into it.** A dead player sits out until the next halt or yard. The crew has to stop and get them. Their old body stays where it fell, and the settlement pays the crew to bring it home.

## D.1 Principles

1. **Holdouts are the only way back mid-run.** No Vigil, no revive at the gate, no open pickup shelter.
2. **Nobody spawns in the open or mid-transit.** Every mid-run spawn happens inside a sealed Holdout. "Getting left behind is fatal" stays intact.
3. **Being dead means watching your crew and talking to the other dead.** A dead player affects the run in exactly two ways: calling out from a Holdout, and one creature vote.
4. **Nothing earned while dead is worth dying for.**
5. **Dying always costs the crew money.** Recovering the body gets most of it back, never all of it.

## D.2 Death

- **The voice hard-cuts on the tick of death**, mid-word, on proximity and radio (C.8). The next thing the dead player says is on the dead channel. The living hear the cut; the dead hear what comes after it.
- A dead player becomes a **spectator** (D.10) and joins the **respawn queue** (D.6).
- **The body persists at the death location** and never despawns. It keeps everything the player was carrying, **including the engineering kit** (§12). If it comes to rest outside the walkable corridor, Line Plan §12.6 relocates it.
- **The body is a ragdoll** (D.9). It lands where physics puts it, not in a tidy pose.
- **If no living crew remain, the run ends** as a failure.
- **Drop-out.** A player who disconnects mid-run leaves an inert body. Its kit can be recovered, but it carries **no crew-loss fee and no refund** (D.9). A player who rejoins enters the queue as a lobbied player.

## D.3 Spawning

| When | How |
|---|---|
| **Run start** | Every player in the session, including everyone in the queue, spawns at the fortress. The queue is empty when the gates open. |
| **Mid-run join** | The joiner goes to the back of the queue as a **lobbied** player. They receive the Line Plan and current state immediately so they can spectate. |
| **Mid-run return** | Through a Holdout only (D.5). |

## D.4 Holdout sites

### Where they are

| Site | Holdouts | Placement |
|---|---|---|
| **Facility pad** | 2: the second at every facility, active only with a big crew (D.15 question 2) | 60–200 m from the consist's stopping position on the pad. Must not share a walking route with the nearest loading module, so rescue competes with loading for people. |
| **Halt** (Line Plan §11.3) | 1 | On or beside the platform, ≤40 m from the main line |
| **Dead town / village** (Line Plan §11.3) | 1 | Within the station footprint, ≤80 m from the main line |

- **The second facility Holdout only activates when the session crew is 5 or more** at assignment time. Below that it stays dormant dressing.
- **Every Holdout lamp needs line of sight** to the site's approach: from the 1 km board at facilities, and from the whistle board at halts and dead towns.
- **Halts and dead towns become optional stops.** They are still landmarks and spawn contexts. Stopping on the open main line to rescue someone gets no facility lull, and it costs dawn slack like any other stop.

### Types

| Type | Sites | To free | Breach cost |
|---|---|---|---|
| **Prison car** | Facilities with a spare siding. Preferred at switchyards, wreck yards and military depots. | Smash the lock, or open it with the engineering kit | Smash: 3s, loud · Engineering kit: 6s, silent |
| **Barricaded shelter** | Any facility (always at mine heads: the portal lamp room) · dead towns | Pry the barricade | 6s, loud |
| **Halt lockup** | Halts | Smash the lock | 3s, loud |

Type is chosen deterministically from the site's sub-seed. **The repair-kit option is deliberate:** the quiet way in needs whoever is carrying the kit.

### Fiction and art

- **Prison car:** a derelict penal transport. Settlements sent convicts to work remote yards, and the guards didn't come back.
- **Barricaded shelter:** a wildlander has held out in a signal box, lamp room or water tower.
- **Halt lockup:** a caged waiting room or parcel cage.

Occupants are drawn from a **survivor appearance pool**: prisoners in penal greys with shackle scars, and wildlanders in patched furs and soot.

### The interior is a safe volume

No enemy spawns, paths or deals damage inside a sealed Holdout. The Holdout lamp is a world light: it does **not** count as a car lamp for Fire Flies and is not affected by "lamps off".

## D.5 Holdout states

```
DORMANT ──assign──▶ OCCUPIED ──breach starts──▶ BREACHING ──complete──▶ FREED
   ▲                   │                            │
   └────release────────┘◀───────interrupted─────────┘ (back to OCCUPIED)
```

| Transition | Condition |
|---|---|
| **Assign** | The consist enters the site's approach zone (the 2 km board at facilities, the whistle board at halts and dead towns) **and** the queue has an eligible entry. The Holdout takes the first eligible queue entry; a second Holdout at the same site takes the next. The lamp lights. |
| **Eligibility** | A player is **not eligible at a site if their most recent death happened inside that site's zone.** They are skipped and keep their queue position. |
| **Reassign while Occupied** | If the assigned player defers (D.6) or disconnects before breaching starts, the Holdout reassigns to the next eligible entry. The lamp stays lit. |
| **Breach starts** | A living crew member begins the breach interaction. Assignment is now locked. |
| **Interrupted** | The breacher takes damage, moves out of range, or releases the input. Progress resets to zero. |
| **Freed** | Breach completes. The assigned player spawns (D.8). The Holdout is spent for the rest of the run. |
| **Release** | The consist has left the site zone moving away **and** no living crew member is within 400 m of the Holdout. The assigned player returns to their queue position, and the Holdout goes back to Dormant. **If the crew comes back for them, it can assign again.** |

A released player watches the train's lights leave from whoever they are following. That's the beat: whatever is behind you disappears into the Territory.

## D.6 Respawn queue

| Rule | Detail |
|---|---|
| **Order** | Dead players by time of death. Lobbied players by time of joining. One shared queue. |
| **Defer** | A player can move **down** the queue, to any position behind them. |
| **No jumping** | Nobody can move themselves up. Positions only improve as the people ahead are freed. |
| **Assignment** | Each Holdout takes the first *eligible* entry (D.5). Skipped entries keep their place. |
| **Missed rescue** | A released player goes back to the position they held, normally the front. |
| **Disconnect** | The entry is removed. Rejoining creates a new lobbied entry at the back. |
| **Visibility** | Dead and lobbied players see the full queue and their position. The living see nothing; roll call stays verbal. |

With halts included, the typical wait is **one site, about 5–8 minutes.** A crew that skips sites makes the wait longer.

## D.7 Finding and freeing

### Signals

| Signal | Who controls it | Range | Notes |
|---|---|---|---|
| **Holdout lamp** | Automatic: lit while Occupied or Breaching | Visible from the approach board | Tells the crew *someone is waiting here* |
| **Call Out** | **Any dead or lobbied player** | 60 m, normal falloff and occlusion | Tells the crew *which building* |
| **Live Mic Holdout** | **Only the player assigned to that Holdout** | Proximity voice (8 m clear, 26 m cutoff) | Tells the rescuer at the door *anything*. Mostly comedy. |

### Call Out

- **Available** while the Holdout is Occupied or Breaching **and** any living crew member is within 200 m of it.
- **Plays** a prisoner shout or a bout of banging from that occupant's voice set, positioned at the Holdout.
- **Cooldown is shared per Holdout**: 7s by default, tunable 5–10s. Six dead players firing on separate timers would be a shout a second, which is noise, not a tell.
- **Call Out never counts toward the crew loudness meter** (Appendix C.7) and triggers nothing that listens for sound. It is the dead player's job, and it must not change the run.

### Live Mic Holdout

- A toggle offered **only to the player assigned to that Holdout**. It defaults off, and switches off when they are freed or released.
- While it's on, the player's mic plays from the Holdout on the normal proximity voice layer, and they stay audible on the dead channel. Anything they say to the dead, the rescuer at the door hears.
- **It never counts toward the crew loudness meter**, and the Gaunt doesn't hear it as talking.
- The 26 m cutoff means only rescuers already at the Holdout can hear it, which limits what spectator knowledge can leak.

### Breach

Breaching is a hold-to-interact action by a living crew member, using the tools already on the train (Appendix C.2).

| Method | Tool | Duration | Noise |
|---|---|---|---|
| Smash lock | Any melee tool: shovel, wrench, crowbar | 3s | **Counts toward crew loudness** at cannon level for its duration |
| Pry barricade | Any melee tool | 6s | **Counts toward crew loudness** at machinery level for its duration |
| Open lock | Engineering kit in hand | 6s | None |

Breach noise counts because it is the living's action. A crew that smashes a lock while already loud can bring the Choir.

### Enemy interactions

- **Holdout occupants are always adults.** Every occupant voice set is an adult prisoner or wildlander, never a child. A child calling for help is always the Soot Children roll (real survivor or Soot Child), never a Holdout, so the two can't be confused.
- **A child survivor is cargo; a Holdout survivor is a player.** They are separate systems.
- **The Passenger never occupies a Holdout.** Holdouts only ever contain players, so the rescue itself is never a trick. Its silence tell is unchanged, because freed players are always real people on voice. **A successful rescue changes the headcount, so it is the moment the crew should recount.**
- **Fire Flies** swarm to lamps inside cars. The Holdout lamp is a world light and never attracts them.
- **The Choir.** Breach noise counts toward crew loudness. Call Out and Live Mic never do (see above).
- **Stopping for a rescue is a stop.** The Whistler strikes when the train is stopped. A haunting Track Doll plays with the cab controls whenever the cab is empty. Ribbits and the Gaunt live in yards and villages, which is where the Holdouts are.

## D.8 The freed player

| | Value |
|---|---|
| Health | **80 / 100** |
| Kit | **Standard kit**: the same loadout every player departs the fortress with |
| Role access | Full. No restrictions (§12 Fluidity). |
| Control | The camera cuts from the followed player into the survivor's body inside the Holdout |
| Identity | **The survivor becomes that player's character from then on**, carried into future runs until they die and are freed again. It's stored in the host's campaign save against the player's ID. |
| Rules | Normal from the moment they're out. They can be left behind, and dying again adds another body and another fee. |

**The crew meets the new you.** A friend who went down as a railwayman walks out of a prison car in penal greys and shackle scars, and stays that way. The freed player's first words double as the recount D.7 asks for: the crew will want to hear them talk before anyone trusts them.

## D.9 Bodies as loot

**Every body brought home earns the crew cash back.** Every death costs the crew more than the body returns, so death can never be farmed.

| Rule | Value |
|---|---|
| **Crew-loss fee** | Charged to the shared wallet at settlement for every in-run death: **50% of the tier's per-car value** |
| **Body refund** | For every body delivered: **75% of that death's fee**, plus everything the body was carrying returned to stores |
| **Net cost of a recovered death** | 25% of the fee. Unrecovered, 100%. |
| **Delivered means** | Stowed in a car attached to the locomotive at arrival, or carried by a crew member aboard. **Decouple the car and the body goes with it.** |
| **Carry** | Hand-carried loot (Appendix C.4): 2.8 m/s and no climbing, with the exception below |
| **Physics** | A body is a ragdoll. The server owns where it comes to rest; clients may simulate the fall locally. Dropped, it falls. It can slide off a roof, drop through a coupling gap, or be thrown into any car, the livestock car included. A body that falls from a moving train comes to rest where it lands, and Line Plan §12.6 applies. |
| **Loot-seeking enemies** | A body is valued at its refund when enemies rank loot. The Gaunt can take it, Followers can nest in its car, and a Car Hugger eating the rear car or the Passenger uncoupling the caboose takes any bodies inside. |
| **Solo remainer** | When exactly one living crew member remains, they can climb ladders while carrying a body at **0.4 m/s**, a quarter of normal climb speed |
| **One body per death** | A player who dies twice leaves two bodies and incurs two fees |
| **Drop-out bodies** | Kit recoverable. No fee, no refund. |

| Tier | Per-car value | Fee | Refund | Recovered net |
|---|---|---|---|---|
| Local | 450 | 225 | 169 | −56 |
| Frontier | 700 | 350 | 263 | −87 |
| Dead lines | 1,100 | 550 | 413 | −137 |
| Deep territory | 1,700 | 850 | 638 | −212 |

**Settlement:** `wallet += delivered cargo − running costs − Σ crew-loss fees + Σ body refunds`

**Silent-settlement termini** (Dead lines) pay cargo and body refunds as normal, credited by the fortress that issued the contract.

## D.10 The dead phase

Being dead means watching your crew and talking with the other dead. Lobbied players get the same experience, **minus the vote.**

| Feature | Spec |
|---|---|
| **Camera** | Locked to a living crew member's view. Cycle between living crew with a button. **No free camera, no seeing through walls.** If the target dies, it switches to the next living player. It stays on the chosen player until your Holdout is freed. |
| **Hearing** | Exactly what the followed player hears: their proximity voice mix with occlusion, their radio if they're holding one, and the train bed. Implement by placing the spectator's audio listener at the target's listener and mirroring the target's channel subscriptions. |
| **Dead channel** | Mic chat between dead and lobbied players. **The living never receive it.** |
| **UI** | Queue list and position · Defer · Call Out (when available) · Live Mic toggle (assigned player only) · Creature vote (dead players who haven't voted) · Bookmark |

## D.11 Creature vote

| Rule | Value |
|---|---|
| **Who** | **Dead players only.** Lobbied players can't vote until they've spawned into the crew. A joiner who spawns, plays and then dies gets a vote like anyone else. |
| **How often** | **Once per run per player**, locked on submit. An unused vote carries over to a later death in the same run. |
| **Options** | Only creatures the director selects by **weighted roll at runtime** that are currently eligible for this route, tier, crew and consist |
| **Excluded** | Condition-triggered (the Stoker) · loudness-triggered (the Choir) · anything currently gated out |
| **Effect** | **×1.2 spawn weight per vote**, capped at **×1.5** per creature, for the rest of the run. The multiplier applies **within the creature's want tag** (B.1), so the Kill / Split / Trust / Cargo target shares still hold. |
| **Never touches** | Budget, want-tag shares, gates, hard caps, pacing rules, cooldowns, once-per-run limits, corrupted-human exclusivity, or the Soot Children's 50/50 real-child roll |
| **Visibility** | Hidden from the living until the run-end screen |
| **Payoff for the dead** | When a creature spawns that a dead player voted for, every dead player gets a dead-channel cue naming it and the voters. The living hear nothing. The run-end reveal then lands twice: once for the dead in the moment, once for the living at the end. |

Provisional voteable set, subject to the runtime rule: Track Doll · Cinder Hounds · Car Hugger · Climbers · Draggers · Whistler · Tippy Toesie · Fire Flies · Ribbits · The Gaunt · Followers · Soot Children · The Passenger · The Switchman · Grumbler.

## D.12 Run end: incident report and commendations

### Incident report

Shown on the run-end screen. It lists:

- deaths, with who, where, and **a cause-of-death line** from the failure-attribution log (C.9)
- rescues, with who freed whom and at which site
- bodies delivered and bodies lost
- cars lost
- **what the dead voted for**
- **bookmarks**, manual and automatic

**Cause-of-death lines** are written in the settlement clerk's voice (§9): flat, procedural, and specific. Each one names the failure, the contributing action and who made it, and sits next to the itemised fee and refund for that death.

> *Struck by own consist at Mile 4 Halt. Throttle: Dave, 38 km/h. Fee 350. Body recovered. Refund 263.*
> *Taken by the Choir on the roof of car three. Loudest on the line: Priya. Fee 350. Body not recovered.*

The lines record facts, not fault (C.9). There are no demerits; the report just makes sure everyone knows what happened.

A bookmark is made with a button while dead. It stores a timestamp, the followed player's name, and a **still capture** of the followed view. There are no replays.

**Auto-bookmarks.** The server also captures a still at every GRAB start and every PUNISH, taken from the nearest living crew member with line of sight to the victim, or from the victim's own view if nobody can see it. A derailment captures one still per crew member from the cinematic, at the peak of their shot (E.5). A stranding captures the outro's last frame (E.9). Auto-bookmarks show beside the cause-of-death line they belong to, and are capped per run (D.13) so the screen stays readable.

### Commendations

| Rule | Value |
|---|---|
| When | Run-end screen only |
| How many | **One award per player per run**, to anyone except yourself |
| Who can award | Everyone in the session at run end: living, dead, or still in the queue |
| Where it's kept | The **player profile**, not the character. Characters change on death; the tally survives. |
| Value | Social only. No scrip, no progression. |
| Demerits | None |

**Starter set:** *Came Back For Me* · *Held the Switch* · *Kept the Fire* · *Brought Them Home* · *Last One Standing*

## D.13 Tunables

Every number in this appendix lives in data, not code.

| Tunable | Default | Range |
|---|---|---|
| Facility Holdout distance from consist | 60–200 m | — |
| Halt Holdout distance from main line | ≤40 m | — |
| Dead-town Holdout distance from main line | ≤80 m | — |
| Second facility Holdout: pad scale | Every pad (0 m) | — |
| Second facility Holdout: crew | ≥5 | 4–6 |
| Release distance | 400 m | 250–600 m |
| Call Out active radius | 200 m | 150–300 m |
| Call Out audible range | 60 m | 40–80 m |
| Call Out cooldown (shared per Holdout) | 7s | 5–10s |
| Smash duration / loudness level | 3s / cannon | — |
| Pry duration / loudness level | 6s / machinery | — |
| Repair-kit breach duration | 6s | — |
| Freed health | 80 | — |
| Crew-loss fee | 0.5 × tier car value | — |
| Body refund | 0.75 × fee | Must stay < 1.0 |
| Solo-remainer climb with body | 0.4 m/s | 0.3–0.6 m/s |
| Vote multiplier per vote / cap | ×1.2 / ×1.5 | — |
| Commendations per player per run | 1 | — |
| Auto-bookmarks per run, cap | 12 | 8–20 |
| Auto-bookmark priority when capped | Derailment cinematic > PUNISH > GRAB start | — |

## D.14 What the harness verifies

| Check | Method |
|---|---|
| **No open-world spawns** | Every mid-run spawn position lies inside a Holdout volume |
| **Recoverability** | Every generated Holdout is reachable on foot from the stopped consist, and every body from the corridor |
| **Queue integrity** | No entry ever moves up except by people ahead being freed. Deferral only moves down. Ineligible entries are skipped without losing position. |
| **Release and reassign** | A crew that leaves and returns can still free the occupant |
| **No farming** | Across randomised death and recovery sequences, the wallet never ends higher than with the same run and no deaths |
| **Dead silence** | Call Out and Live Mic never feed the crew loudness meter, the Gaunt's silence check, or director state |
| **Vote bounds** | No creature's weight exceeds ×1.5 from votes. Want-tag shares hold. No vote bypasses a gate, cap or once-per-run limit, or shifts the Soot Children roll. |
| **Channel isolation** | Living clients never receive dead-channel audio |
| **Hard-cut** | On every death, the victim's proximity and radio output stop on the death tick, and dead-channel routing starts on the same tick |
| **Attribution** | Every death, car loss and whole-train event has exactly one failure-attribution record, and every record names an actor or states that none applied |
| **Vote cue isolation** | The dead-channel vote cue never reaches a living client |

## D.15 Open questions

1. ~~**Drop-out fee.**~~ **Answered: no fee.** Drop-outs carry no fee, so a crew isn't punished for someone's connection. Quitting instead of dying has no purpose: nothing (no XP, no levelling) is tied to wins, and a run goes better with more people, so dropping out only punishes your friends. Still watched for in playtests.
2. ~~**The second Holdout threshold.**~~ **Answered: big crews get one at every facility.** The goal of Holdouts is to get everyone back and cut downtime in a balanced way, so every facility has a second Holdout, active when the session crew is 5 or more (D.4).
3. **Halt stop cost.** Is a main-line stop at a halt dangerous enough, with no facility lull, or does it need a dedicated director response?
4. **Live Mic and the Passenger.** A spectator who noticed a silent crew member could name it over Live Mic to a rescuer at the door. The living have the same tell, and the 26 m range limits it to one listener mid-rescue, so it's accepted for now. Watch for it in playtests.
5. **Commendation reel.** Bookmarks are stills, now including automatic captures at every GRAB and PUNISH. If they turn out to be the best part of the run-end screen, short clips may be worth the tech later.
6. ~~**Attribution tone.** Cause-of-death lines name players. Watch playtests for whether they read as a joke the crew shares or as a scoreboard someone resents. The fallback is to keep names on self-inflicted deaths (jumping, a Choir victim who was also the loudest) and replace another player's name with their role ("Throttle: conductor") where the cause was someone else's action.~~ **Answered (the director, 7 Oct 2026): name players, forget roles.** Cause-of-death lines and the cause card always name the player, whoever caused it; there is no role fallback.

---

# APPENDIX E — END-OF-NIGHT SEQUENCES

*Added in v1.4. Derailment is the only failure that kills the whole crew at once, so it is the only one nobody survives to watch (§23.1). This appendix makes the game the witness. Stranding (§23.2) gets a short, quiet outro of its own (E.9).*

## E.1 Principles

1. **Opera means the night is over.** The music in E.6 plays at a derailment and nowhere else in the game, so the first bar is the punchline.
2. **Nothing in the cinematic is gameplay.** The run ended on the derail tick, and the settlement is already fixed. Physics may exaggerate for comedy, because it can't change an outcome.
3. **Everyone gets a shot.** Every crew member alive at the derail gets their own on-screen death, however boring their position.
4. **Slapstick, not gore.** Limbs stay attached, joints bend the right way, and there is no blood. The bodies tumble, flop and bounce.
5. **Everyone sees the same film.** The host simulates it once and every client plays back the same recording.

## E.2 Pipeline

| Step | Where | What |
|---|---|---|
| **1. Derail tick** | Host | The run ends as Derailed, and the settlement is fixed (E.7). Nobody dies yet: from this tick the living are the wreck's, and nothing they press moves them. Each player dies in an impact that follows, as the wreck's physics plays it (E.3): alive, they take their first hits (two, where the wreck gives them that many), and the next hard one kills them (the ground, a car, the train running into them), as does being crushed under a car or a hit of 16 m/s at any time; the fourth hit kills whatever it is. The host reads it from the pre-sim (step 3) and kills each of them as it lands in their own first person (E.5); their mic hard-cuts mid-word then and moves to the dead channel (C.8). A bystander (E.3) takes no hit: they die as their first person ends, with the settle. *(The director's decision, 5 Oct 2026.)* |
| **2. Snapshot** | Host | The world at that tick is copied: every car's position and velocity, every player's world position and velocity, every loose body aboard. |
| **3. Pre-simulate** | Host | The copy runs the wreck to rest under E.3, up to **6s of sim time**, recording every body at 30Hz. It's a few hundred PBD steps and costs less than a frame. |
| **4. Plan** | Host | The camera director (E.4) uses the full recording to choose shots, find each player's peak moment, and pick and align the music (E.6). It can see the future, which is what makes the occlusion rules achievable. |
| **5. Stream** | Host → clients | Quantised keyframes, the camera track, the shot list and the music cue (E.8). |
| **6. Play** | Every client | Playback starts once 1s of real time is buffered. Clients don't simulate anything. |

## E.3 Physics and collision rules

**The bodies**

| Rule | Detail |
|---|---|
| **Cars become rigid boxes** | On the derail tick, the engine and every car become PBD rigid boxes (8 corner particles with edge and diagonal constraints), keeping their mass and velocity. |
| **Couplings tear** | Each coupling becomes a distance constraint that breaks above a strain threshold. The consist concertinas, then comes apart. |
| **Players ragdoll** | Every living player becomes their existing ragdoll (the 11-particle body from D.9), launched with their world velocity at the derail tick. |
| **Alive till the fatal hit** | Until their fatal impact (E.2 step 1) each player's ragdoll is alive: an active ragdoll with muscle tone, holding a brace (elbows and hands up and out, knees bent) and reaching its hands toward where it's going. It collides with the cars, the ground, the others and the debris, and takes its first hits and survives them, visibly knocked about. At the fatal hit the muscles let go and it goes limp. *(The director's decision, 5 Oct 2026, take 3.)* |
| **Comic fling** | Each player ragdoll gets its velocity × **1.6**, an upward kick of **3–5 m/s** and a spin of **3.5–7 rad/s**, all drawn from the run seed. Ejection speed is capped at **30 m/s** so bodies stay in frame and in the sim. Someone in the gun's seat is thrown up out of it at **6 m/s** with the seat's own velocity, from clear of the gun, head over heels at the full spin. *(The director's numbers, 5 Oct 2026; they were ×1.3, 2–4 m/s and 4 rad/s.)* |
| **Slow derails still throw people** | Below 6 m/s the kick is applied in full regardless, so a slow tip-over still launches somebody. |
| **Bystanders** | A player more than 25 m from every car at the derail tick gets no impulse. They play a 0.8s beat turning to watch the wreck, then go limp where they stand. Nothing hits them, so they die as their own first person ends, with the settle (E.2 step 1), and get a shot like everyone else. |
| **Extras** | Bodies of the already-dead stowed in cars, crates, loot and extinguishers aboard all join the wreck. They never get their own shot. |
| **Joint limits** | Elbows and knees bend one way only, and the head-chest-pelvis spine bends at most 60°. No limb separates. |

**Colliders**

| Rule | Detail |
|---|---|
| **What collides** | Terrain heightfield, track bed, bridges, tunnel bores, car boxes, static props within 50 m, and every ragdoll particle against every other ragdoll's (0.12 m spheres), so bodies pile up. The cars are moving solids: a car that ploughs into a body throws it, and one that comes down on a body pins it, which crushes it. Nothing is pushed out to somewhere safe; a body can end up under a car. A mounted gun is solid too. |
| **Cars are hollow** | Floor, walls and roof collide from both sides. Doorways and windows are gaps, so a player inside tumbles around inside the car and can be thrown out through an opening, but never through a wall. |
| **No starting overlap** | Before the first step, any particle inside a solid is pushed out along the shortest axis. The pre-sim never starts interpenetrating. |
| **No tunnelling** | 4 substeps per tick. Each particle's travel per substep is clamped to its radius, and each car's to its half-thickness. |
| **Floor safety** | Any particle that ends up more than 0.5 m below the terrain is projected back onto the surface. |
| **Sim radius** | Anything that leaves a 300 m radius around the engine's derail position freezes and drops out of the shot plan. |
| **Water** | Bodies get buoyancy and drag and float face down. Cars sink. |
| **Settle** | The pre-sim ends when every body is asleep, or at 6s of sim time. |
| **Body budget** | Up to 8 player ragdolls, 8 extra ragdolls, 20 cars and 40 loose items. Past that, the loose items furthest from any player freeze in place. |

## E.4 Camera and occlusion rules

The camera director runs on the host during planning. Because it has the whole recording, every rule below is checked against the future, not guessed.

| Rule | Detail |
|---|---|
| **O1 Subject visibility** | Three rays from the camera to the subject's head, chest and pelvis. The subject is visible if at least two are clear of **non-fadeable** geometry. |
| **O2 Fadeable occluders** | Car shells, props, vegetation and tunnel liners between the camera and the subject dither-fade out over 0.15s and back in after the shot. It's a cutaway, PS2 style, and it's how players inside a tumbling car stay visible. Terrain and rock never fade. |
| **O3 Camera body** | A 0.3 m sphere swept along the whole camera path must never touch a collider. The camera stays at least 1 m above terrain and water, and never inside a car volume or tunnel rock. |
| **O4 Nothing hits the lens** | No car, body or debris may pass within 1.5 m of the camera during a shot. The planner checks the recorded trajectories. |
| **O5 Distance** | At least 2.5 m from the subject. |
| **O6 Framing** | The subject's bounds sit in the central 70% of the frame for at least 80% of the shot, at 25–60% of frame height. |
| **O7 Other bodies** | Other ragdolls and debris may cross the frame, but may not cover the subject's head or chest for more than 0.2s. |
| **O8 Cut rule** | If the subject fails O1 or O7 for more than 0.2s of real time, cut to the next-best candidate. |
| **O9 Candidates** | 24 positions on a shell around the subject's peak position: 4, 7 and 11 m out, at 10°, 25° and 45° elevation, 8 azimuths. Score only the ones that pass O1–O5. Prefer the side away from the consist, a subject moving across the frame rather than toward or away from it, and no forward-lamp glare. |
| **O10 Fallback** | If nothing passes, shoot from 70° overhead at 12 m with every fadeable occluder faded. |
| **O11 Tunnels and bridges** | In a tunnel, candidates are limited to the bore, and the liner fades (O2). On a bridge, candidates may sit below the deck, above O3's water and ground clearance. |
| **O12 Light** | The lamps die in the wreck, so the cinematic adds a rig: a key and a warm rim light on the subject, in the forward lamp's colour, and fog pushed out to at least twice the shot distance. It's the rim-light answer to open question 6. |
| **O13 Movement** | Shots are locked off or slow dollies, at most 2 m/s in real time. Impacts get a small, tunable shake. |
| **O14 Wide shots** | Frame the bounds of every player ragdoll plus the engine. Past 150 m wide, frame the engine and the nearest half of the crew. |

## E.5 Shot plan

Shots may revisit the same sim time from new angles. The cinematic is edited, not continuous.

| Beat | Real time | Speed | What |
|---|---|---|---|
| **Freeze** | To your death, then 3.8s (4.5–9s) | 0.5× | Each client sees through its own player's eyes as their body is thrown, through the hits they survive and the impact that kills them (E.2 step 1), then 3.8s more as the view rolls and settles with the limp body. It no longer holds the derail tick's frame, and it isn't a fixed length. *(The director's decision, 5 Oct 2026; take 3 added 3s after the death.)* |
| **Establishing** | 3s | 0.25× | A wide of the consist leaving the rails. The music starts on the cut. |
| **One per player** | 4s each, block capped at 32s | 0.4×, easing to 0.15× at the hit and back | Each player's ragdoll thrown into the hit that kills them (E.2 step 1), with the hits they took on the way, and then the limp aftermath. *(4s, the director's decision of 5 Oct 2026, take 3; it was 2.5s, capped at 16s.)* Their peak (the highest apex, longest airtime or hardest landing, whichever scores highest) orders the shots. *(The director's decision, 5 Oct 2026: it was the peak itself, at 0.25× to 0.1×.)* A lower-third name card in the clerk's typewriter face: **DAVE — on the throttle**, the role taken from where they stood (§12). |
| **Settle** | 3s | 0.25× ramping to 1× | A wide as everything comes to rest. |
| **Cause card** | 2s | — | The clerk on radio static, reading the derail's attribution line (C.9). |

- **Order.** Players go in ascending order of peak score, so the biggest flight comes last. The music's hit (E.6) lands on that final death.
- **Length.** From the first person to the cause card, with the replay between: about 36s at crew 4 and 52s at crew 8 (the first person 4.5–9s, the replay 9s, 4s a player, the settle 3s, the cause card 2s). *(5 Oct 2026, take 3; it was about 18s and 24s.)*
- **Bookmarks.** Each player's death frame is captured as their auto-bookmark (D.12).
- **The cause card**, for example: *Consist derailed at km 14, 68 km/h. Took the 45 km/h bend at 68 km/h, 23 km/h too fast. Throttle: Dave. Recovery not scheduled.*
- **Skipping.** Each player skips their own film, whenever they want: holding the key half a second cuts their screen to the cause card, from the first frame on. Nobody else's film is skipped. The cause card is never skipped. *(The director's answer to E.12 question 2, 7 Oct 2026: "players should be able to skip whenever they want. It's up to each player if they want to skip their film". It was a majority vote after the first player's shot, or the host.)*

## E.6 Music

**One rule for licensing:** a **CC0 1.0 recording** of a **public-domain composition**. Nothing else is accepted.

- A public-domain composition does not make a recording public domain. The performance has its own rights, so the recording itself must carry an explicit CC0 1.0 dedication.
- A "public domain" mark on an old recording is not enough. It depends on the country, and the game sells worldwide. Explicit CC0 only.
- **Where to look:** Musopen, Wikimedia Commons, Freesound (CC0 filter) and the Internet Archive. Check every file's own licence page, because licences vary within the same collection.
- **Fallback:** commission a singer and a small ensemble or piano, and get the recordings dedicated CC0 in writing. A single baritone over a piano reduction is cheap and arguably funnier.

**Rotation**

| Rule | Detail |
|---|---|
| **Pool size** | At least **4 tracks for the December demo**, at least **10 at launch**. |
| **Shuffle bag** | The host's campaign save keeps a bag of every eligible track. Each derail draws one without replacement, and the bag refills once empty. |
| **No back-to-back** | The first draw from a refilled bag can't be the last track played. |
| **Mood weighting** | Each track has a mood tag. The draw weights by derail speed, within the bag: below 8 m/s favours **Lament** (the tragedy of a slow tip-over), above 16 m/s favours **Gallop** and **Doom**. **Swagger** fits any derail. Weighting never overrides the no-repeat rules. |
| **Alignment** | Each track declares a **hit**: the big note or crash. The planner chooses the in-point so the hit lands within ±0.1s of the final player's apex (E.5). If the hit is further in than the cinematic is long, play starts mid-track from a later in-point. |
| **Out** | A fade of at most 1.5s under the cause card. |
| **Mix** | Normalised to −16 LUFS. Ducks 6 dB under any voice on the dead channel (50 ms attack, 400 ms release), so the laughing stays audible. |
| **Game audio** | Everything else plays at half speed through a 1.2 kHz low-pass. The train bed stops on the derail tick. |
| **Voices** | Everyone is dead, so everyone is on the dead channel together, lobbied players included. It's the one moment the whole session shares a channel. |

**Candidate works.** All of these compositions are public domain. Each still needs a CC0 recording.

| Work | Composer, year | Mood |
|---|---|---|
| "Vesti la giubba", *Pagliacci* | Leoncavallo, 1892 | Lament |
| "O mio babbino caro", *Gianni Schicchi* | Puccini, 1918 | Lament |
| "Flower Duet", *Lakmé* | Delibes, 1883 | Lament |
| "Nessun dorma", *Turandot* | Puccini, 1926 | Lament |
| Overture finale, *William Tell* | Rossini, 1829 | Gallop |
| "Infernal Galop", *Orpheus in the Underworld* | Offenbach, 1858 | Gallop |
| "Ride of the Valkyries", *Die Walküre* | Wagner, 1870 | Gallop |
| "Largo al factotum", *The Barber of Seville* | Rossini, 1816 | Gallop |
| "Dies irae", *Requiem* | Verdi, 1874 | Doom |
| "Der Hölle Rache", *The Magic Flute* | Mozart, 1791 | Doom |
| "Anvil Chorus", *Il trovatore* | Verdi, 1853 | Swagger |
| "La donna è mobile", *Rigoletto* | Verdi, 1851 | Swagger |
| "Toreador Song" and "Habanera", *Carmen* | Bizet, 1875 | Swagger |

**Excluded:** "O Fortuna" from *Carmina Burana* (Orff, 1937). It's the obvious choice and it is still under copyright.

**Manifest.** Every track lives in a music manifest in `content/audio`, with: id, file, work, composer and year, performers, source URL, licence (must read `CC0-1.0`), evidence (an archived copy of the licence page and the SHA-256 of the file as downloaded), mood, in-point, hit, out-point and measured loudness. **A music file without a CC0-1.0 manifest entry fails the build.** CC0 doesn't require credit, but the credits screen lists every performer anyway.

## E.7 Stowed bodies and the report

- Bodies of the already-dead in cars come along for the ride as extras (E.3). It's the second time their owner has died tonight.
- The derail's attribution record (C.9) feeds the cause card and the incident report line.
- Settlement is fixed on the derail tick (§23, D.9). Nothing in the cinematic changes it.

## E.8 Network and data

| | Value |
|---|---|
| Keyframe rate | 30Hz of sim time, interpolated on playback |
| Ragdolls | 11 particles each, positions quantised to 16 bits per axis inside the wreck's bounds |
| Cars | Position and orientation, quantised |
| Camera | Position, orientation and FOV per keyframe |
| Budget | ≤250 KB for a 6s wreck at 8 players and 20 cars, streamed while it plays |
| Start | Once 1s of real time is buffered on every client, or after 2s, whichever comes first |
| Late or dropped clients | A client that falls behind skips ahead to the current shot. A client that drops sees the incident report on rejoin. |

## E.9 Stranded outro

Nobody died, so there's no opera. The joke is how little anyone cares.

| Beat | Real time | What |
|---|---|---|
| **The empty place** | 1.5s | In car one, on the fitter's locker, standing open on the empty shelf where the engineering kit is kept. |
| **The pull-back** | 6s | Up and back over the stopped consist to a high wide, while the lamps go out one by one, from the last car forward, the engine last. It's §6's small glowing machine in an enormous black world, going dark. |
| **The clerk** | Over the pull-back | On the radio: *Consist reported stranded at mile 14. Recovery at first light. Recovery is chargeable.* |

- **Sound:** wind and the boiler ticking as it cools. No music.
- **Voices:** the living keep proximity voice throughout. They're still alive, and they'll have things to say.
- **Camera:** the rules in E.4 apply (O2, O3, O13).
- **Skippable** after 3s, by each player for themselves, like the film (E.5).
- **Bookmark:** the final frame is captured as an auto-bookmark (D.12).

## E.10 Tunables

| Tunable | Default | Range |
|---|---|---|
| Pre-sim length | 6s | 4–8s |
| Substeps | 4 | 2–8 |
| Coupling break strain | — | Set in feel-testing |
| Fling multiplier | ×1.6 (5 Oct; was ×1.3) | ×1.0–2.0 |
| Upward kick | 3–5 m/s (5 Oct; was 2–4) | 0–6 m/s |
| Spin | 3.5–7 rad/s (5 Oct; was ≤4) | 0–8 |
| Ejection cap | 30 m/s | 20–40 |
| Minimum-kick speed | 6 m/s | 4–10 |
| Bystander distance | 25 m | 15–40 |
| Base slow motion | 0.4× (5 Oct; was 0.25×) | 0.1–0.5× |
| Peak slow motion | 0.15× (5 Oct; was 0.1×) | 0.05–0.25× |
| Player shot | 4s (5 Oct, take 3; was 2.5s) | 1.5–5s |
| Player-shot block cap | 32s (5 Oct, take 3; was 16s) | 10–40s |
| First person | to the death + 3.8s, 4.5–9s (5 Oct, take 3) | — |
| Hits survived before the fatal one | 2, at most 3 (5 Oct, take 3) | 0–4 |
| Camera sphere / min distance | 0.3 m / 2.5 m | — |
| Lens clearance | 1.5 m | 1–3 m |
| Occluder fade | 0.15s | 0.1–0.3s |
| Cut threshold | 0.2s | 0.1–0.5s |
| Music duck | −6 dB | −3 to −12 dB |
| Music loudness | −16 LUFS | — |
| Lament / Gallop speed thresholds | 8 / 16 m/s | — |
| Stranded recovery fee | 0.5 × tier car value | 0.25–1.0 |
| Stranded skip delay | 3s | — |

## E.11 What the harness verifies

Agents can't tell whether it's funny, but they can tell whether everyone was on screen.

| Check | Method |
|---|---|
| **Everyone gets a shot** | Every derail in the balance sweep is planned. Every crew member has a shot that passes O1 for the whole shot. |
| **Clean camera** | No camera sweep touches a collider, and nothing enters lens clearance. |
| **No interpenetration** | No particle rests inside a solid by more than 2 cm at the end of any substep. |
| **In the sim** | No subject leaves the sim radius during its own shot. |
| **Length** | Every plan fits the beat and block caps at crews 1–8. |
| **Rotation** | Across 1,000 simulated derails on one save, no track repeats inside a bag and no track plays twice running. |
| **Licensing** | Every file in the music folder has a manifest entry reading `CC0-1.0` with evidence attached. |
| **Determinism** | Every client's playback matches the host's recording frame for frame. |
| **Stranding** | Every way of losing the kit in §23.2 ends a ruptured night as Stranded, and no reachable kit ever does. |

## E.12 Open questions

1. **Solo nights.** A single player gets one shot. Is a six-second film of one person funny enough, or should solo derails shorten to the establishing wide and their shot?
2. ~~**First-time skip.** Should a player's first derail be unskippable for them?~~ **Answered (the director, 7 Oct 2026): no.** "Players should be able to skip whenever they want. It's up to each player if they want to skip their film." Each player skips their own, from the first frame, and nobody else's (E.5; ARCHITECTURE §8 note 315).
3. **Trailer capture.** A debug flag that renders the cinematic at 60fps from any saved derail would make Next Fest trailer footage cheap. Worth building in Phase 6?
4. ~~**A spare kit.** Should the fortress sell a second engineering kit? It removes Stranded as a failure for crews who pay for it, which may be exactly the right kind of upgrade, or may defang the rupture entirely.~~ **Answered: yes.** The fortress sells spares, and kits are also found as loot at stops. Spares ride in the crew lockers. Stranded takes losing every kit, and a lost spare is gone for good (§12, §23.2).
5. ~~**Kit loss warning.** The fitter's empty shelf is the only tell that the kit is gone. Is that enough, or does the clerk need a radio line when it's lost?~~ **Answered (the director, 7 Oct 2026): a radio line.** When the train's last engineering kit is lost, the yard says so once, flat: "Engineering kit reported lost", with the car's number when it went with a car. The empty shelf stays (ARCHITECTURE §8 note 308).

---

# APPENDIX F — BUILD REVIEWS

*Added October 2026. A running log of the director's play-tests and reviews of a build: what was said (the point, not verbatim), tracked against the design. Newest entry first. Each entry gives the date, the build number and what was played, then the notes grouped by area. Each note carries a status: **open**, **in progress** (with its task), **fixed in PR …**, or **design change → §/note** for a note that changed the design. Praise and observations that need no work are marked **keep** or **noted**. When a review decides something about the design, a **Decided** line names the section it changed, and that section carries the date of the decision; engineering detail goes in the numbered notes of `docs/ARCHITECTURE.md`.*

## F.3 2026-10-07 — main at 8442eee, and the integration test builds

*Numbered F.3 so that "App. F.1" stays the build 1121 review it has always meant; newest first all the same.*

**Played:** the director, the day's Windows test builds (main with the open PRs merged on top) and CI's build of main at 8442eee (everything merged up to 4 pm EDT).

**Boiler**
- Heat and pressure both fell and the train held its top speed. *Done (note 319, #243): an engine short of steam holds the train back below the working band (`boiler.json` `starvedDecel`).*

**Towns and world building**
- Still waiting on the towns from the world-building chat: the fortresses feel static and lame, with not enough world building in them. *In progress (B2, queue #10 and #74, #194; walled towns of up to 3000 next).*
- The world's set dressing repeats: "the same three things over and over again". More art assets to dress the world. *Open (queue #64, note 325, E1).*

**Level design**
- The villages have no explorable interiors. They should be searchable for finds. This is where crews decide whether to split up for more loot, or work the yard together, or the village together, which takes more time. *In (queue #65, note 326, B4): the village houses stand open, and what they keep in a cupboard, cabinet, cellar or under the boards comes out only to a crewmate who holds Use there a few seconds. A searched cupboard stands open, a cellar's hatch up, and the cupboards and cabinets are solid. Every house stands open, the L, cross and paired ones too. A bot crew splits up too: once the yard's crates are in, half its crate hands go to search the houses and bring the finds aboard.*
  - *The director, 8 Oct, on the first interiors:* "These interiors are looking good but a few things: they need some lighting inside, dim to keep it scary, I like that they look worn down; sometimes a monster should be nesting in there; they need procedurally generated furniture scattered about, like the place has been ransacked many times before; loot needs to be discoverable in there, that's the whole point of this. They need a bit of a brighter more unique look to them so that they stand out from the background as interactable objects. Do this with good texture work, not VFX." *(B4, under queue #65. In: a dim candle or lamp in each house, the night kept out; each kind of find its own model in bright printed stock. Every house ransacked, heavy furniture against its walls and things underfoot; the Gaunt's house dark, its nest on the floor.)*

**Encounters**
- Off the train it isn't dangerous enough. Exploring a village, nothing much happened. Raise the threat when people leave the train, or at least the perceived threat; more encounters is probably a good idea overall too. The goal: when you leave, there's a presence of threat at all times. *Done (note 327): off the train the crew are watched: now and then eyes at the lamp's edge toward where something that lives at the stop is, and its sound; and the night's pressure builds faster while anyone's afoot, so encounters come sooner (`enemies.json` `director.afoot`).*
- A couple of playtests ago, running hot the whole time and never stopping, two Car Huggers came. More threats that can board the train at speed, to give players things to do. *First piece done (note 328): the hound run. A train run faster than the hounds' own 19 m/s for 2.4 km (sooner with the boiler hot) draws a stream of Cinder Hounds faster than it is, so a hot train can't outrun them: 2 runners solo, up to 6 at crew 8, in pairs on alternating flanks. Missed, they board the rear car at any speed. More at-speed boarders are proposed in [orchestrator.md](orchestrator.md) §5.2.*

**Pacing on the train**
- Point A to point B is still relatively boring. The director is open to ideas: activities on the train, things players do to maintain the train while it runs; threats attacking the train tower-defense style, to give the gunners something to do. *Tower defence, first piece done (note 328): the guns answer the hound run one hound at a time. A ball landing near a runner scatters it, and one good shot between a pair takes both. Upkeep while the train runs ([orchestrator.md](orchestrator.md) §5.1), first job in (queue #71, note 331): the hot box, an axle box running dry as the train runs, a squeal then smoke at a car's rear bogie, greased from the gap behind it or the ground beside; left alone it drags the train off its top speed, then the car catches fire.*
- A threat orchestrator that takes into account how many players are in the game and orchestrates threats to the number currently active. A design outline first; it can go to a different chat. *Outlined (queue #67, note 328): [the threat orchestrator, and the run between stops](orchestrator.md): the orchestrator over the director (who's active, at which post, and how long since each had something to answer), upkeep while the train runs, threats that board at speed, waves at the guns, pacing targets, what changes in the director, and five questions for the director.*

**Repairs**
- The wrench doesn't repair things Sea of Thieves style yet. Wanted as soon as possible. *Done (note 301): everyone carries a wrench; in hand, Use at a break mends it, a few presses or a hold (the burst boiler, a breach, a battered car's dent), and every break is called out with an amber glow and sparks. The repair kit's other uses go in a second slice.*

**Audio**
- A weird high, repeated sound when the cannon turns. Get rid of it, or make it a lot lower and slower. *Done (note 329): the gun's lay is now a low motor hum with a slow, deep gear clunk (2–5 a second under 420 Hz, where it ticked 6–28 a second at 1.8–3.1 kHz).*

**Derailment**
- Derailments are underwhelming. A curve into a yard taken at 30 km/h, and the train just glided off the rails. Commit to it: more impulse, more drama. "Oh my God, that was a dramatic derailment." *Done (queue #69, note 330): however slow it comes off, the first car is thrown and popped up and every car after is thrown, hops and tips, so a 30 km/h derail rolls the whole train over, one car after another.*

## F.1 2026-10-06 — build 1121

**Played:** the director, solo host, frontier seed 7, four cars, no bots; then a quick night.

**Front end**
- The updated menu looks good, and so do the menu sound effects. *Keep.*
- The mouse should work in the menus. *Done (note 264).*
- The lobby name field starts typing as soon as WASD reaches it. It should need Enter or a click. *Done (note 264): it needs Enter or a click.*

**Audio**
- Turning on the spot shouldn't make a sound; only walking should. *Open.*
- Footsteps on the ground sound wrong; on wood and grates they're good. *Open.*
- The boiler over-pressure sound is good (§23, Boiler rupture). *Keep.*
- The gun's traverse sound is bad. *Open.*
- The train is near-silent on the rail: no rolling sound to reinforce speed. *In progress (T127).*
- There's no audible stress before a derailment (A.1: whole-train events carry their own telegraph). *In progress (T127).*
- The Choir was heard behind the train (A.7). *Noted.*

**Cab**
- Far too much UI for what's on screen. *Done (§32 "The HUD: your hands and the dark", the director's decision of 7 Oct; notes 264, 277 and 285): only the crosshair and the hotbar are always on screen, the prompts are in fine print under the crosshair, and the rest comes and goes.*
- The hold-to-vent control feels off. *Done (note 264): one key held anywhere in the cab. How it feels is the director's to judge.*
- There's no whistle cord, and people will want one. *Done (notes 264, 276): a cord in the driver's corner, worked by look and Use.*
- The coal shovel is fun. *Keep.*
- The firebox door shutting by itself is OK. *Keep.*

**Lockers (§12)**
- The lockers are cute, but neighbouring lockers block each other. *Done: an open door stands out into the aisle (ARCHITECTURE.md note 277).*
- Stowing a held item in a locker doesn't work well. *Done (note 264): one tap opens the locker and stows what's in your hands.*
- The lockers are mostly empty; only the fitter's had the engineering kit. *Done (note 264): stocked by grade.*
- A locker with something in it should say so. *Done (note 264).*
- It isn't clear how to hold the kit in the inventory. *Done (note 264): "IN HANDS" on the hotbar.*

**Train and world**
- It's good that every car has an extinguisher (C.5). *Keep.*
- The toys in the guard van at spawn should be found in the world instead (C.4). *Done (note 264): found at the stops.*
- There's a walkie-talkie aboard. *Noted.*

**The fort**
- Fort buildings have no collision, and gun shots hit nothing. *Done (T124, note 274).*
- Forts must be safe spaces that monsters never enter (§9). *Done (T128, note 273): both forts, all night (`run.json` `forts`): the departure fortress up to its outer gate and the terminus from its gate on, 80 m either side of the line. Nothing is sent while the train is in one, and a creature that comes into one is driven off. Caveat: the fort buildings still have no collision (T124), and the Choir's meter isn't stilled in the terminus.* *The fort buildings: done (T124, note 274). The Choir: done (note 296): with the train in a fort the meter doesn't gather, and a swarm that followed it in is gone.*

**Bugs**
- The train left on its own, with nobody in the cab, after the director got out of the gun seat. It didn't slow down, and the boiler then ruptured. *Done (note 263).*
- The rupture message said the train had no engineering kit, though it had been dropped in the front car (§23.2: a kit in a reachable car is never lost). *Done (note 263).*
- The Stoker was killed by crowbar through a shut firebox door (A.5). *Done (notes 263, 271).*

**Abandoned player**
- A player left behind by the train should feel the world close in: tension, monsters coming, the difficulty spiking for that player. They needn't die at once (§7, §23). *Done (T128, note 273): past 150 m from the train, on the ground and outside the forts, a player builds a pressure of their own (`enemies.json` `director.abandoned`). About 45 s in, a Ribbit pack comes for them alone. Each pack after is bigger (2 to 5) and starts closer (35 m to 18 m). Ribbits can be outrun, and running back into a fort saves them. Caveat: only the Ribbits hunt so far. Hounds or the Gaunt could join once their own rules allow a lone player on the ground.* *The Gaunt: done (note 296): from the third hunt a Gaunt comes with the pack, woken on them, following at their back (talking holds it off), and home onto the train with them. The Cinder Hounds still hunt only the train.*

**Line and derailment**
- The speed boards weren't on curves. The tightest curves should carry the limits, as on a real railway. *Done (notes 265, 277): boards only on bends that can derail, inked red on the map and on the route card.*
- The derailment from track debris (the Sleepers) at 43 km/h felt cheap: no visible threat, and punished for not being in the right place. Good for role theory, bad for game feel (§22, A.1). *The Sleepers are gone (note 265); a bend taken too fast is heard (note 265) and now seen: flange sparks off the outer rail, and a judder (note 277).*
- The train's lights and lanterns were all off. *Done (note 265): a smashed lamp can be relit.*
- With the Sleepers gone, a Frontier night may have had nowhere the train could derail: frontier:7's sharpest bend was 707 m, over what the engine can reach (note 265). *Done (note 278): every night carries its tier's count of hard bends that derail the train under its top speed (Frontier 3–4, at 58–68 km/h), spread through the night, each boarded, on the cab map and going round a hill. The rules are in docs/design/level-design.md Part B.*

**Second session (same build, continued)**
- *Run start:* the spoken "the yard's open, here's the consist" intro is too long, cheesy, and a pain to localize. *Done (note 264): dropped.*
- *Run start:* nothing makes the player feel they've done something that draws a monster they need to worry about. *Done (note 287): the night's first threat answers something a crewmate did (the whistle, a cannon shot, raised voices, a noisy toy, the firebox run hot, a lamp lit, cargo taken aboard). Past the grace, a draw big enough is answered from the dark: a distant call and a pair of eyes at the lamp's edge, and the threat follows five seconds later. A quiet crew is listened for longer. The incident report names it: "Cinder Hounds came first at km 2. Drawn by the whistle: Dave."*
- *Cab:* the whistle sounded by itself. The hanging cord sits inside the coal shovel's use volume. *Done (notes 264, 276).*
- *Cab:* a redesign of the front of the train, for a lone driver's view of track hazards. Every function stays. *Done (ARCHITECTURE §8 note 276): to the director's sketch, cab forward. The cab leads with the driver at its front windows (the rail in sight from 8 m past the plough), the cannon on its roof, the firebox in its back wall and the coal in a bunker beside it; the boiler and stack behind.*
- *Sleepers:* the train derailed before the game said it had hit the Sleepers; "a bad design for a creature" (A.2). *Done (note 265): the Sleepers are retired.*
- *Grab:* a creature carried the director up a mountainside, a destination that makes no sense. *Done (T128, note 273): it was the Whistler, running 60 m straight out at rail height. Wherever the land rose, the player was stood up on the slope. Its nest is now 30 m out at most, inside the walkable corridor, reached over runnable land (no steep stretch, within 6 m of the rail's height, no water), and on the far side of the line if theirs is a mountainside.*
- *Fire (C.5):* putting out one car doused the whole train; it should douse only that car. Extinguishing feels too slow. *Done (notes 263, 267): each car's fire, and each cell of it, is its own.*
- *Fire:* barely touching it killed outright. Fire should burn over time, and only standing in it kills. *Done (note 263): it burns by the second; standing in it kills.*
- *Stoker (A.5):* it came back straight after being beaten off: "I should have earned a break." It should get in only when the firebox is untended or too hot. *Done (notes 263, 271): a break of 150 s.*
- *Falling:* far too easy to fall off the train. *Done (T128, note 273): a roof's edge now holds you while you're only walking (`player.json` `edge`). In 20 minutes of careless roof walking (looking aside with the key held, a normal night's wind), falls went from 42 to 0. You still go over if you walk straight at the side, jump short, are pulled off by a creature, or take a bend you were warned of too fast. Caveat: the wind no longer takes you over the edge (`windOverLip` puts it back).*
- *Fire Flies:* "Nobody lit that lamp" set car 2 alight. What were the bubbles? *Done (ARCHITECTURE.md note 286): the line says what drew them ("Drawn by the car's lamp, lit since the yard", or who relit it, and the way in: "Door left open."); the bubbles were the moths' smouldering wing rims, lit all round against the lamp, which drew each as a hollow orange ring. The rims are charred paper now, and each moth on the wing sheds ember sparks off its tail.*
- *Overall:* the director hasn't finished a run yet.

**Direction** (the director, 6 Oct 2026; proposal in T131, not yet decided):
- *Boarding-first enemies:* "things shouldn't be able to get on the train unless they board it." Enemy design centres on boarding. A train that's just hauling is safer, but runs the risk of derailment, and that becomes the core fear.
- *Solo like Lethal Company:* a solo player can explore and get a few runs in to learn the game, and won't get far. Friends let you go further.

**Decided** (the director, 6 Oct 2026, on the boarding-first proposal, T131):
- **Nothing acts inside the train unless it boarded.** Players must be able to understand or learn the rule for how each creature gets on; they needn't see it happen. A player's action opens a pathway by invoking or negating a rule.
- **Each creature has its own boarding rule** at a specific, known point, with a telegraph and a counter.
- **Boarding speeds are per creature.** Some board faster than a human can; the Cinder Hounds are the example. Outrunning them means going fast, which risks derailing on curves the conductor can clearly identify on the map. That makes a choice: run up to the curve and brake hard, or deal with them now. A savvy, experienced player should be able to outsmart the situation.
- **A hauling train is safe from most monsters, not all.** A fast, flying class may come later to answer the top-speed strategy.
- **Slowing opens the doors.** Stops, facilities and tight curves are where things board or the yards attack. These are the heightened scares; the train between them is a relative break. Loot and progress pull players into them.
- **Benchmark every creature** against how it feels in Lethal Company and R.E.P.O.
- **The Sleepers go.** "The game is forcing a tactical point of derailment on its own behalf, not against the player's control." A derailment must clearly be the driver's mistake: someone not paying attention to the map. *Done (note 265).*
- **Cinder Hounds that board stay aboard.** They keep setting the car alight while they eat the supplies, which forces the crew to confront them. *Done (note 269).*
- **The world is solid.** The carry that clipped straight through the mountain made everything feel like 2D billboards. Creatures, carries and players must respect the terrain and geometry; everything should be interactable.
- **UI.** It's still too heavy overall, but players need a way to track all the supplies on board. *Done (notes 264, 277): a supplies panel on I.*
- **The fortress and the lobby are safe spaces.** Until the run starts, nothing of consequence happens: no boiler overheating, no threats. Players wait for friends, mess about, or walk away for a cigarette, and resume when they're ready (as in Lethal Company's ship). *Done (note 263).*
- **Bug:** the guns do nothing. Rounds don't collide where they land and have no visible effect on the monsters. *Done (note 290): every creature in the open has a body a ball stops at, fitted to its model; a ball lands as a heavy blow, answered by the creature's own rule (hurt, a held crewmate freed, a Grumbler turned on the gunner); a hit throws the creature's insides, and a wall keeps the scorch. The Fire Flies and the Choir's ghosts have no body. Whether each can be killed stays with queue #25.*

Further decisions (the director, 6 Oct 2026):
- **Shut doors stop some boarders, not all.** Some creatures can open, force or get around doors, each by its own rule. *In progress (note 286: every creature's boarding point, telegraph, counter and door rule in one table; the Climbers and Draggers board a slow train, at a stop or a tight bend; Tippy Toesie gets on at a stop; a car shut up tight keeps the Fire Flies off its lamp).*
- **The Track Doll may haunt the train.** It's a supernatural apparition, a deliberate exception to the boarding rule. *Done (notes 268, 269).*
- **The Stoker** is drawn to the train only when the firebox runs above a set heat; it seeks heat. Once beaten, it stays gone for at least a couple of minutes. If it gets in, the consequences must be larger and more urgent. Running hot has to be a real trade-off. *Done (notes 263, 271).*
- **Car lamps start lit.** Their pull on Fire Flies is rare, and only while the car is stopped. *Done (note 269).*
- **Run length** is to be set by simulation sweeps (T125). *Set: 24 km, every tier, a 51 min dawn; `dt linegen sweep` passes at it (note 270).*
- **Solo:** a solo player can finish one to three runs before it gets seriously hard and they realise they need friends. You can teach yourself the game solo, but you can't really advance solo. The solo finish target will be tested later. *Tested (note 300): with two bot bugs fixed (a lone driver stranded on a car's landing, and a train stood on the points it needed thrown), a bot alone delivers every night swept: Local and Frontier at 3 and 6 cars, Frontier and Dead Lines at 10. What solo pays is income, about a third of a crew's. The bot never forgets the fire; people alone will find it harder. `balance.json` `solo` judges the target; what should make solo seriously hard is the director's call (four options in note 300).*

- **Quiet stretches are counted in kilometres, not seconds.** A stretch of line holds the same danger whatever the train's speed. A time backstop keeps a stopped train from waiting it out. *Done (T125): the director's quiet pressure ramps over 1.26 km of line run since a threat was engaged, or 120 s if that's sooner (note 270).*

**Direction** (the director, 6 Oct 2026; proposals in T133; *first pass landed: §3.1, note 281, #194*):
- *Fortress towns are where the world is built.* They tell the story of what happened and how people survived. They're where the train is upgraded and loot becomes scrip, so they must feel special.
- *Procedural, with people in them.* Each town has its own odd culture, different from the last.
- *Learn by inference, as in Lethal Company.* Little notes, and text-only lines from the townspeople (no voice needed). Some interactions give a scrap of story; most is left to inference. Mystery is seeded, not explained, without going overboard.
- *Much more in the towns is interactable.*

**Direction** (the director, 7 Oct 2026, notes for the towns; *second pass built for review: §3.1, note 281*):
- *These are Maritime Canada towns.* The buildings must look like Maritime buildings. (The director is finding references for the houses.)
- *Fully modelled inside and explorable,* some of them, *with residents.*
- *Lore is through NPC dialogue.* They should say creepy things, sometimes hints of how to deal with monsters they've heard about, but never the full rule.
- *Every culture is somewhat traumatised* by the events of the world.
- *Towns of 20 to 350 people.*
- Later the same day, with photographs of Maritime houses (a Cape Breton cedar-shingled studio with a gable window and blue barn shutters; Peggy's Cove, painted houses on the granite above the fish sheds and the wharf; a Lunenburg house in periwinkle clapboard with coral trim and a bump; Shelburne's waterfront row in red, blue, ochre and weathered shingle under a church cupola; Blue Rocks' shingled fish shacks on cribwork): *"Village houses should feel like these, they should have lots of variations so it doesn't feel like the same 10 assets recycled across towns over and over again."*
- *"Towns can go up to 3000, and fortresses aren't just some straight line around the railroad: they should surround towns. Towns should be explorable. This is our big worldbuilding work that makes it feel interesting and exciting to go around."* (Queue #74, walled towns, note 335.)

**Decided** (the director, 6 Oct 2026, later the same day):
- **The Track Doll escalates if ignored.** She's no problem at first: she haunts, plays with and admires things in the car. Left alone, she moves on to the controls, and in the end she can let a standing train off its brake. It's a consequence of the crew's inattention and of not getting her off the train, never sudden. *Done (note 268).*
- **Stoker v3.** It's drawn by heat and boards at the coal bunker with a telegraph, and can be driven off on the way in. In the firebox it's territorial: opening the door while it's in gets you a heavy burn, and a second kills. That's the mistake you learn from. The counter is to vent and starve the fire below a set heat; it then leaves the way it came, and a break of two to three minutes follows, so the crew can fire up again. Cooling costs time and speed. A water hose through the open door kills it, at the cost of much of the fire. No chip damage: a crew that knows the rule never gets hurt. *Done (note 271): the boarding half in note 263, the firebox half in 271; the hose is an extinguisher for now.*
- **Damage model: Lethal Company style.** Health exists, but damage comes in a few big hits, never chip damage. Healing items are rare loot. Damage feedback is minimal: an edge flash and a sound. Creatures mostly don't take damage; they're driven off by their rules. Genre fans arrive already knowing this. *Done (note 272; systems spec B.2): big hits only, rare healing finds, the edge flash. Caveat: creature health is not yet reworked; note 272 lists which creatures still die to blows, for the director.*
  - **Driven off, or killed together** *(the director, 7 Oct 2026)*: "They should be driven off by rules but they should also be able to be killed like in Lethal Company if the team coordinates effectively." Following a creature's rule drives it off (the normal answer, a break: it can come back); a coordinated team can kill it, which takes several of the crew striking at once with the setup its rule asks for, and a lone player swinging never gets there. Killing is riskier and costlier than driving off, and rewarded: it doesn't come back that night, and the incident report names the gang. *Done for the Grumbler, the Gaunt, the Passenger, Climbers and the Choir's ghosts (note 288; enemies.json `coordinatedKill` and each one's `drivenOff`).*
- **Fire is a grid.** Each car's surfaces (floor, walls, roof; never mid-air) are cut into large cells of 1–2 m. Fire spreads cell to cell, the extinguisher puts out the cell you aim at, and burnt cells char the textures. *Done (note 267; systems spec B.11).*

**Decided** (the director, 6 Oct 2026): "The time a run takes should always be the same. Difficulty scales not by time but by monsters and density of challenges." One night length for every tier; §11 Route tiers and systems spec B.8 (run length) to change. *Done (T125): one 24 km line and a 51 min dawn on every tier, the tiers' counts and creature budgets now their density (§11, spec B.8; ARCHITECTURE.md note 270).*

**Decided** (the director, 7 Oct 2026): "The switch itself shouldn't cause derail, it should be lines that lead nowhere." The Switchman never derails the train by throwing points under it; it throws a junction ahead and the train goes down a dead line. The crew notice (the wrong lamp, the figure at the lever, the route card), stop, back out and set the switch back by hand. A derailment comes only from the driver running off the end of the dead line, warned in the cab in time to stop. §21 and App. A.8 changed. *Done (note 286).*

## F.2 2026-10-05/06 — derailment film, takes 3–5

**Played:** the director watching `dt film`, the derailment cinematic (Appendix E). Film page: [claude.ai/artifact/JyXaqcHCPbZAo8JK3AmSLw](https://claude.ai/artifact/JyXaqcHCPbZAo8JK3AmSLw).

**The film**
- Take 3 was much better, but people should be alive and colliding before they go limp. Each third-person death shot gets 2s more, and the first person 3s more. *Design change → E.2 step 1, E.5, E.10; ARCHITECTURE.md note 258.*
- Take 4: the timing and the first person are better, but the third-person poses are arms-up and awkward, not people mid-task. *Design change → E.3; ARCHITECTURE.md note 257.*
- Hits should bounce more, on derailments only. *Design change → E.3, E.10; ARCHITECTURE.md note 257.*

**Decided** (the director, 5 Oct 2026; marked in E.2, E.3, E.5 and E.10):
- Each player dies on their own first fatal hit, not on the derail tick (E.2 step 1).
- Until then they are an alive, active ragdoll (E.3).
- Death shots run 4s (E.5, E.10).
- The first person runs 3.8s past the death (E.5, E.10).
- The extra bounce applies to derailments only (E.3, E.10).

---

*Dark Territory · GDD v1.5 · October 2026*
