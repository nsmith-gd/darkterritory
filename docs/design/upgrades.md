# The fortress's catalogue: train upgrades

*The director, 13 Oct 2026: "We need train upgrades that make sense for the game loop that will balance against the
increase in difficulty that each new run should bring. Some upgrades need to be simply for fun, some purchases need to be
silly, some purchases need to have comedic whimsy, and some purchases need to be legitimate tools that will push runs
further."* Queue #410, ARCHITECTURE §8 note 678 (U1). Extends spec F.3 and GDD §11; the numbers live in
`content/tuning/campaign.json` `upgrades`.

Every call this brief left open is marked **U1's call**, for the director to overrule.

## 1. What the catalogue is for

The loop (GDD §9, §11): the train leaves the fortress empty, loads at its stops, brings home what it can, and the crew
spend one shared wallet on cars and upgrades. More cars put the train on a harder tier (spec F.4), and a harder tier
pays more (note 636) and sends more at it. **Difficulty is produced by success.**

So an upgrade has one of four jobs, and the shop shelves it by which:

| Kind | Its job | Priced | Gameplay |
|---|---|---|---|
| **Tool** | Push a run further: answer a trouble the deeper tiers bring | Spec F.3: a small 15–25% of the next car, a major 100–180% | Takes a share of one trouble, never all of it |
| **Whimsy** | It works, and it's funny | Under a tool: 1–15% of the next car | A real use with a joke, or a cost, built in |
| **Fun** | Delight: the train as the crew's home (GDD §10 "the train should increasingly feel like home") | Pocket money: 2–8% of the next car | None. Looks, sounds, the yard |
| **Silly** | Bought because it's silly | Anything, and the price may be the joke | None, or a little and the wrong way. The yard clerk reads it into every tally |

## 2. How the catalogue keeps pace with the tiers

What grows from one run to the next, and so what the catalogue has to keep pace with:

- **The tier** (the cars set it, spec F.4): more creatures over the same 7 km, and denser line: harder grades and bends,
  junctions, washouts, weak bridges (GDD §11). Some creatures are weighted up tier by tier (`tierWeights`: the Hotbox,
  the Gannet, the Moose).
- **The train's length**: pressure 6% faster a car over eight (§15), a longer blind middle (§13), slower to stop and start
  (spec B.5), more to walk and to hear across.
- **The crew** (§15): more of them, more pressure.

The rules the catalogue keeps (U1's call, from spec F.3's tension and the social test, §20):

1. **Priced off the next car.** Every upgrade costs a share of what the next car costs now (spec F.3), so the tempo of
   buying stays the same while the numbers inflate (spec F.2), and an upgrade always competes with a car.
2. **The town stocks what the line needs.** A tool is held back until the consist works the tier whose trouble it answers
   (`fromTier`). A frontier tool isn't on a local town's shelf, however rich the crew: it reaches them with its trouble,
   not before. The shop shows it with the tier it's stocked from ("SPEED RECORDER: FRONTIER AND ON") and why not.
3. **Answers, never removals.** A tool takes a share of a trouble: a warning sooner, a job done faster, a window held
   open longer. None stops a creature coming, removes a failure, or does a crewmate's job for them. The tightest is half.
4. **The core tension stays.** Every tool is a car not bought. A crew that buys every tool on its shelf stays a tier
   short; one that buys only cars meets each tier with last tier's train.
5. **Fun and silly are the leftovers' shelf.** Spec F.2's leftover after a car grows in absolute terms; the fun shelf is
   where a crew with a little to spare spends it, and the silly shelf is where a crew with a lot to spare loses it.
6. **It all goes down with the train.** A train taken at dawn ends the campaign (note 632). The gold whistle goes with it.

**The budget.** `dt campaign sim`'s standard crew (campaign.json `standardCrew`) spends about 0.3 of each car on smalls
and 1.8 of every second car on a major: over a whole campaign (17 cars bought) about five cars' worth of smalls and fifteen
of majors. The tools on the shelf come to 3.6 cars' worth of smalls and 11 of majors, so a crew that lives to the
Behemoth (F.4) can own every tool by its end, but not by its middle: the dead lines' tools only come to them about run 21,
and the full shelf about run 40. The catalogue doesn't change F.4's progression: the yardstick crew spends shares, not
items (`CampaignTests.TheStandardCrewReachesTwentyCarsInFiftyFiveToSixtyRuns` holds).

## 3. On the shelf now (built in note 678)

### Tools

The seventeen of spec F.3 (notes 184, 196) stay as they were, all from Local. Ten more, each against a trouble the
deeper tiers grow:

| Upgrade | Size, share | From | Does | Answers |
|---|---|---|---|---|
| **Speed recorder** | small 0.15 | Frontier | The cab's bell rings 6 s before a bend, not 4; and a bend must have been rung that long before it can take the train (note 266) | Harder bends (§11), a longer train's longer stop |
| **Composition brake shoes** | small 0.2 | Frontier | Brakes fade half as fast on a descent (spec B.5) | Harder grades |
| **Sprung cradles** | small 0.15 | Local | Knocks and hard braking spoil 40% of what they did of medicine | Fragile cargo (§19) |
| **Pre-cut boards** | small 0.15 | Frontier | A breach boarded up in 4 s, not 8 (spec B.9) | The Car Hugger and the Climbers |
| **Spare radios** | small 0.15 | Local | Two more radios in the guard van | "Half the crew can't hear the other half" (§15) |
| **Big extinguishers** | small 0.2 | Frontier | Half again the spray, full again on the bracket in half the time | Car fires; powder and chemicals (App. C.5) |
| **Axle oilers** | small 0.2 | Dead lines | A Hotbox's axle knocks and glows half again as long before it seizes, and is mended in half the time | The Hotbox, weighted up each tier |
| **Feedwater heater** | small 0.25 | Dead lines | Each step of the cold costs the boiler half the steam it did | The cold |
| **Gun rammers** | small 0.25 | Dead lines | Each step of a reload a fifth quicker | More at the rear and flanks |
| **Trailing-truck booster** | major 1.5 | Dead lines | Every train length pulls away a fifth harder | The longest trains standing at a stop (0.18 m/s² at 20 cars) |

### Whimsy

| Upgrade | Share | Does | The joke |
|---|---|---|---|
| **The honk** | 0.05 | The whistle is a clown's bulb horn: a third as loud to the Choir (it still draws the night's first threat as a whistle does, note 287) | It's a real tool for a quiet crew, and every signal the driver gives is a honk. The clerk: "Horn: comedic." Not with the gold whistle |
| **Lucky horseshoe** | 0.1 | The guns foul 15% less (with gun cooling too: they multiply) | Superstition in a world of customs (GDD §3.1). The clerk: "Horseshoe: still up." |

### Silly

| Upgrade | Share | Does | The joke |
|---|---|---|---|
| **Gold-plated whistle** | 2.5 (more than a major) | The whistle a quarter louder to the Choir | It costs more than a car, it's still a whistle, and louder isn't better. The clerk: "Whistle: gold." Not with the honk |
| **Framed commendation** | 0.5 | Nothing | "For Services to the Railway", from a council nobody's met. The clerk reads it into every tally, for good: "Commendation: framed." |

**The clerk's lines** (GDD §9, §23.1 "shrugged at"): an upgrade's `tally` is read into every delivered night's tally,
after the salvage and before the costs, as flat as the coal (`Radio.Tally`, `Campaign.TallyLines`). The clerk's voice
is a word bank (`tools/audio/clerk.py`): until the audio chat records "whistle", "gold", "horn", "comedic", "horseshoe",
"still up", "commendation" and "framed", the set breaks up over them, as it does over a typed name.

## 4. Designed, not on the shelf yet

A purchase that does nothing is a bug (note 196), so nothing here is sold until what makes it work is built. Each says
what it needs and whose that is.

### Fun

| Upgrade | Share | What it is | Needs |
|---|---|---|---|
| **Livery** | 0.05 each | Three paints for the engine and cars: crimson lined gold, forest green and brass, black with a white stripe. Bought once, picked in the yard | The train kit's paint swap (C1); every machine paints from the setup's upgrades |
| **Engine nameplate** | 0.04 | The crew name the engine; a brass plate on the smokebox; the clerk reads "Consist received" as the name | A name field (F1), the plate (C1); the clerk breaks up over a typed name, as over a player's, which is funny |
| **Festoon lamps** | 0.04 | Paper lanterns strung along the roofs, lit in the yard, put out at the gate | The lanterns (C1); lit only in the safe yard (§9), so they never draw the Fire Flies |
| **Three-chime whistle** | 0.03 | The whistle sounds a chord. As loud as before | The audio chat |
| **The hat rack** | 0.06 | Four more looks in OUTFIT: a stovepipe, a fez, a straw boater, a deerstalker (note 298) | The hats (E1), look.json's outfits |
| **More emotes** | 0.05 | The bow, the salute, and the conga (everyone who joins follows the leader) | The emote wheel's slots (F1; a protocol bump), the clips (E1) |
| **The yard photographer** | 0.03 | A crew photo at the gate before each night; the campaign's end card shows every one | A shot from the yard (the screenshot path exists), the end card (F1) |
| **Brass bell** | 0.03 | A bell in the cab to ring | The audio chat; as quiet as the whistle's pull to the Choir |

### Whimsy

| Upgrade | Share, from | What it does | Why it's funny | Needs |
|---|---|---|---|---|
| **Gerald** | 0.1, Frontier | A straw dummy in a crew coat, bolted to the guard van's roof. The grabbers that pick a crewmate off a roof (the Gannet, the Draggers) count him as one; taken, he's gone for the night | The clerk: "Gerald. Body not recovered. Fee 0." Every crew names him; the dead watch the Gannet carry him off | A target in the grabbers' choice (G1), the dummy (E1) |
| **The canary** | 0.08 | A cage in car one. It sings while all's well, stops while an interior creature (the Stoker, Tippy Toesie, the Passenger) is aboard its car or the next, and dies in the chemical works' fumes | A real tell in a cage. "Canary: deceased." | Its song (audio chat), the cage (C1), an event in the Sim |
| **The pigeon loft** | 0.12, Frontier | Three homing pigeons in the guard van. At a stand, a find or a toy in the basket flies home and is paid at the gate whatever becomes of the train. The Gannet takes one now and then | Insurance with feathers. "Pigeon: one short." | A basket interactable and a pigeon's flight (Sim), the birds (E1), the audio chat |
| **Steam calliope** | 0.1 | The whistle becomes a calliope: each pull of the cord the next note of a tune. Louder to the Choir (×1.6), but a Moose stops to listen | The driver is playing a tune while something comes for the train. Not with the honk or the gold whistle | The notes (audio chat), the cord's note in the Sim, the Moose's listen |
| **The gramophone** | 0.08, with the crew car | Plays in the crew car. A Gaunt within earshot hears it as talk and is calmed by it (enemies.json `gaunt.talkingCalms`); the Choir hears it as a music box | Keeping the Gaunt calm with a dance band while the Choir gathers | The records (E.6's music intake, audio chat), the player (C1), a voice in the Sim's hearing |
| **The human cannonball** | 0.2, Frontier | The guard van's cannon takes a crewmate instead of a ball (powder, no shot, Use at the muzzle). They fly 40 m along the gun's aim and land as any jump does: from a train at speed the landing obeys §23's 16.5 m/s, so it's fired standing, or it isn't | Every condition of §23.1 at once: witnessed by all, legible in six words, owned by whoever fired it ("Fired from the rear cannon by Dave"), shrugged at by the clerk | A ballistic player state (Sim), the pose and the shot (E1, audio chat) |
| **The yard cat** | 0.06 | A cat that rides the train and wanders the cars. It hisses at what boards; it sleeps on the warm backhead | Cats in horror. "Cat: not recovered." | A small creature that walks the train (Sim), the cat (E1), the audio chat |
| **The hot tub** | 0.15 | A tub on the tender fed off the boiler. A crewmate in it doesn't feel the cold; it draws steam; it's on the roof at line speed, in the open | Someone in a hot tub on the tender at 70 km/h, and the Gannet's opinion of it | A seat (like a gun's), heat in it, a steam draw (Sim), the tub (C1) |

### Silly

| Upgrade | Share | What it is | The clerk |
|---|---|---|---|
| **Velvet driver's seat** | 0.3 | The driver sits in velvet | "Seat: velvet." |
| **Brass spittoon** | 0.02 | In the cab. Nobody uses it | "Spittoon: brass." |
| **Ornamental second stack** | 0.4 | Twin stacks, for the look. Only one is connected | "Stacks: two." |
| **Stuffed moose head** | 0.25 | On the smokebox. A Moose takes it personally: its temper builds twice as fast as the train passes (`moose.trainPass`) | "Moose head: mounted." |
| **The dining car** | major 1.2 | A cargo car converted: a chandelier and a table laid for eight. It carries nothing and weighs a car | "Dining car: laid." |

Each needs its art (C1, E1); the moose head's temper is one multiplier in the Sim.

### Tools

| Upgrade | Size, from | Does | Needs |
|---|---|---|---|
| **Cowcatcher** | small, Frontier | Debris struck on the line costs half | Read what striking debris costs (Sim); the plow (C1) |
| **Grab irons** | small, Frontier | A ladder caught from the ground at a third more speed (player.json `ladder.grabMaxRelativeSpeed`) | The player's tuning in the night's loadout (Sim) |
| **Rear searchlight** | small, Frontier | A lamp on the guard van facing back, for the rear gunner | Its light and what it reveals (Sim, render) |
| **Flare pistol** | stores, Frontier | A flare lights a village for 60 s, and the Fire Flies follow it away from the train | The flare (Sim, art, audio) |
| **Speaking tubes** | small, Dead lines | Voice carries between neighbouring cars as in one | Voice routing (N1, the audio chat) |
| **Armoured cab** | major, Deep territory | The cab's windows plated to slits: what comes over the tender is slowed at the glass | The cab (C1), the Climbers' way in (Sim) |
| **Armour II and III** | major, Dead lines / Deep | A second and third car plated (the conversion repeatable) | Ranks on the conversion (Sim) |

## 5. The shelf by tier

| Tier | New on the shelf | What a crew there is fighting |
|---|---|---|
| **Local** (3–5 cars) | F.3's seventeen; sprung cradles, spare radios; all the whimsy and the silly | Learning the train |
| **Frontier** (6–10) | Speed recorder, composition shoes, pre-cut boards, big extinguishers | Bends and grades, breaches, fires |
| **Dead lines** (11–15) | Axle oilers, feedwater heater, gun rammers, the booster | Hot boxes, the cold, the rear and flanks, a long train's weight |
| **Deep territory** (16–20) | (Designed: the armoured cab, armour III) | Everything |

## 6. For the director

1. **Should the fun shelf rotate?** Lethal Company's decorations rotate; a town's chandler could stock a few of the fun
   and silly things a night, different in each town (each town's custom, §3.1). U1's call: no, until there are enough.
2. **Should the silly shelf be priced off the next car?** It keeps "costs more than a car" true at every size, which is
   the gold whistle's joke. A flat price would make it trivial late. U1's call: off the next car.
3. **Do the old seventeen want tiers?** Gating them would hold the early game back; they stay from Local.
4. **Whimsy first?** U1's order to build next: the human cannonball (it's the clip), Gerald, the pigeon loft, the canary.
