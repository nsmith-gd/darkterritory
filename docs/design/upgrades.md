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
| ~~**Spare radios**~~ | small 0.15 | Local | Retired (15 Oct): S1's note 749 makes the radio every crewmate's, with no loose sets to add to | "Half the crew can't hear the other half" (§15) |
| **Big extinguishers** | small 0.2 | Frontier | Half again the spray, full again on the bracket in half the time | Car fires; powder and chemicals (App. C.5) |
| **Axle oilers** | small 0.2 | Dead lines | A Hotbox's axle knocks and glows half again as long before it seizes, and is mended in half the time | The Hotbox, weighted up each tier |
| ~~**Feedwater heater**~~ | small 0.25 | Dead lines | Retired (18 Oct, note 852): the cold is out of gameplay (the director, 16 Oct), so it had nothing to give back | The cold |
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
| **Speaking tubes** | small, Dead lines | Voice carries between neighbouring cars as in one | Built (note 978): campaign.json `speakingTubes`, a horn in every car with a room joined to the next car's either way, as well as the cab's to the van's (`SpeakingTube`); its sound is note 720's tube voice (AU1 may give the car-to-car horns their own pipe note) |
| **Armoured cab** | major, Deep territory | The cab's windows plated to slits: what comes over the tender is slowed at the glass | The cab (C1), the Climbers' way in (Sim) |
| **Armour II and III** | major, Dead lines / Deep | A second and third car plated (the conversion repeatable) | Ranks on the conversion (Sim). Built: campaign.json `armouredCar2`, `armouredCar3` (note 959) |

## 5. The shelf by tier

| Tier | New on the shelf | What a crew there is fighting |
|---|---|---|
| **Local** (3–5 cars) | F.3's seventeen; sprung cradles, spare radios; all the whimsy and the silly | Learning the train |
| **Frontier** (6–10) | Speed recorder, composition shoes, pre-cut boards, big extinguishers | Bends and grades, breaches, fires |
| **Dead lines** (11–15) | Axle oilers, ~~feedwater heater~~, gun rammers, the booster | Hot boxes, ~~the cold~~, the rear and flanks, a long train's weight |
| **Deep territory** (16–20) | (Designed: the armoured cab, armour III) | Everything |

## 6. For the director

1. **Should the fun shelf rotate?** Lethal Company's decorations rotate; a town's chandler could stock a few of the fun
   and silly things a night, different in each town (each town's custom, §3.1). U1's call: no, until there are enough.
2. **Should the silly shelf be priced off the next car?** It keeps "costs more than a car" true at every size, which is
   the gold whistle's joke. A flat price would make it trivial late. U1's call: off the next car.
3. **Do the old seventeen want tiers?** Gating them would hold the early game back; they stay from Local.
4. **Whimsy first?** U1's order to build next: the human cannonball (it's the clip), Gerald, the pigeon loft, the canary.

## 7. The second round (the director, 13 Oct 2026)

*"Upgrades need to be purchasable in the fortress towns from a vendor who is clearly marked as the vendor blacksmith of the
town who does train upgrades. Also one of the upgrades needs to be an extended front car cow catcher that has a deck, a
couple muskoka chairs that can be sat in, a plastic cooler with beers, and a BBQ mounted on a stand on the deck. Each
character needs to be able to interact with each object so they can have a tiny "party" at the front. That kind of complex
add on is great whimsy, its the BBQ Dads at the end of the world. Brainstorm more Tools, Whimsy's, and Sillies."*

### 7.1 The blacksmith (queue #429, note 697)

The catalogue is bought in the town, from its blacksmith, not from a menu (GDD §3.1 already says "buying, upgrading and
turning loot into scrip in the town itself, not the menu"). Note 687 has how it's built.

- **Who.** Every fortress town has one: the town's blacksmith, who does the trains. A forge by the line a little in from
  the gate, where the train stands in the yard: an anvil, a quench barrel, the forge's glow, and a painted board over it,
  **BLACKSMITH · TRAIN UPGRADES**, lit so it reads from the cab. Their name over the door like everyone's ("Agnes Anvil").
- **How.** Look at them: "ARCHIE MACNEIL, BLACKSMITH   TALK : [E]   UPGRADES : HOLD [E]". Talk and they say what they'd fit next;
  hold and the catalogue opens, shelved as the menu was (TOOLS, WHIMSY, FUN, SILLY), the crew's scrip at the top. Anyone
  in the crew can buy: one wallet (§11). What's bought is fitted before the gate, and the clerk reads it at the next tally.
- **When.** In the yard, where the town's a safe space (§9). Past the gate the forge is shut.

### 7.2 The BBQ deck (queue #430, note 698): the flagship whimsy

**"The BBQ Dads at the end of the world."** An extended cowcatcher on the front of the engine with a plank deck on it,
railed, ahead of the smokebox. On the deck:

| Thing | Use | What it does | What it costs |
|---|---|---|---|
| **Two Muskoka chairs** | Use: sit; Use again or move: get up | The best seat on the train: the line ahead, first. Sat, you're still, and Tippy Toesie comes for a crewmate standing still (App. A.5) | Out front in the cold and the wind, at the first thing the train hits |
| **A plastic cooler of beers** | Use: take a beer; Use with it in hand: drink it | A beer warms you a little (the cold's onset held off a while) and the crew cheers. A two-four a night (24) | The cans. A dropped empty rattles about like a noisy toy |
| **A BBQ on a stand** | Use: light it, or turn it off; hold Use at a lit one: grill | A sausage off the grill heals (the damage model's rare healing, note 272), one at a time, a while each | Lit, it glows like a lamp (the Fire Flies) and smells (the Cinder Hounds and the Followers come to a hot, smelly train: `houndsHotBoilerWeight`'s kin) |

Priced as a whimsy that's also a tool: 0.6 of the next car, from Local (U1's call). **Built** (note 698): the deck is
walked to along the running boards, which run on past the cab; a beer warms (30 s off the cold) rather than heals; the
grill's lit for a bag of charcoal (5 minutes) and a sausage takes 6 s and heals 20. The cowcatcher's own job (debris) is
still §4's tool.

### 7.3 Brainstorm: tools

Each answers a trouble that grows with the tiers (§2); none removes one.

| Upgrade | Trouble | Does |
|---|---|---|
| **The handcar** (major) | Crew left behind; a village far off the line | A pump handcar on a flatcar, set on the rails at a stand. Two pump it, back for whoever's left behind or out to a far stop and home |
| **Communication cord** | "Half the crew can't hear the other half" (§15) | A cord the length of the train: pulled anywhere, the cab's bell rings with the car's number |
| **End-of-train telltale** | The Passenger, the Knotter, a coupling let go | A lamp in the cab that goes out when the train parts behind it |
| **Fusees** (stores) | Getting lost in a village at a stop | Red flares dropped on the line: the way back to the train glows for a few minutes |
| **Stretcher** | Bodies home pay 75% (App. D) | Carry a body at walking pace, and up a ladder |
| **Roof gangplanks** | The gaps (§13) | Plates over the first two couplings back from the cab: walked, not jumped |
| **Rope ladders** | Boarding from the ground at speed | A ladder caught at a third more speed (`ladder.grabMaxRelativeSpeed`) |
| **Coal pusher** | The driver alone at the fire (§12) | The tender's coal slides to the fire door: a shovel a fifth quicker |
| **Safety valve, larger** | Rupture (§23) | Lifts harder: pressure pins at 100 a third as often (`safetyValveCapacity`) |
| **Pyrometer** | Overfiring | A gauge on the backhead: the redline's warning sooner |
| **Roof steam lines** | Climbers and Draggers on the roofs | A scald valve at each car end, fed off the boiler: knocks a creature off a roof, costs pressure |
| **Sanders** | Rain (weather) on the grades | Wet rails pull and brake as dry |
| **Lookout cupola** (major) | The blind middle (§13) | A cupola on the guard van or crew car: the whole roofline from one seat |
| **Hose car** (major, Dead lines) | Fires on powder and chemicals | A water car with a pump and hoses reaching two cars either way |
| **Swivel headlamp** | Bends at night | The driver aims the lamp into a bend |
| **Anti-climb skirts** | Climbers | Sheet iron along the car sides: Climbers take longer to get over |

### 7.4 Brainstorm: whimsy

Each works, and each is funny; most do one useful thing and one stupid thing.

| Upgrade | Does | And |
|---|---|---|
| **The BBQ deck** | §7.2 | The BBQ Dads |
| **Tennis-ball launcher** | On the guard van: fire a ball off the back and the Cinder Hounds chase it (they're hounds) | Fetch. They bring it back |
| **Bubble machine** | On the tender: a stream of bubbles behind the train, and the hounds snap at those instead | It's very loud to the Choir, in a way |
| **The Tannoy** | Horns on every car: the driver's voice through the whole train, for a long train's callouts | It's the loudest thing in the Territory and the Choir loves it |
| **The piano** | In the crew car, playable, badly. A Gaunt in earshot hears it as talk and is calmed | The Choir hears it as a piano |
| **Garden gnomes** | Bolted along the roofs; the Gannet dives on a gnome now and then | "Gnome: not recovered." |
| **The dog** | A yard dog that rides the train, barks at what boards (a tell) and fetches toys | Sometimes it fetches the Track Doll's toy back from her |
| **Fishing rod** | At a stand by water (the line's lakes), fish; a fish grilled on the BBQ heals more | Requires patience at a stop, which is when things come |
| **Wind chimes** | In every car: they ring when a door opens (an interior tell) | Constantly, a little, to the Choir |
| **Hammock** | Strung across a car end: lie in it, swinging, warm-ish | Lying down is standing still (Tippy Toesie) |
| **The accordion** | Carried: playing it counts as talking (calms the Gaunt), and the crew dances | Loud |
| **Disco ball** | In the crew car, turning, the stove's light thrown round the walls | The Fire Flies come to dance |
| **Paddling pool of ice** | Beside the cooler: beers stay cold, and a crewmate's ankles in it warm up somehow | Nobody can explain this either |

### 7.5 Brainstorm: silly

Nothing, or a little the wrong way. The clerk reads every one.

| Upgrade | What | The clerk |
|---|---|---|
| **Fuzzy dice** | In the cab, swinging on the bends | "Dice: fuzzy." |
| **Bumper stickers** | On the guard van's end: HOW'S MY DRIVING? CALL THE YARD · MY OTHER TRAIN IS ALSO A TRAIN · HONK IF YOU'RE CORRUPTED | "Stickers: three." |
| **Flames** | Painted down the smokebox. It goes faster (it doesn't) | "Flames: painted." |
| **Spinner hubcaps** | On the driving wheels, still spinning at a stand | "Hubcaps: spinning." |
| **The tiny caboose** | A toy caboose towed off the back of the real one. It derails at every bend and is dragged | "Caboose: small." |
| **La Cucaracha horn** | The whistle plays it. Every pull, all of it | "Horn: musical." |
| **The mayor's bobblehead** | On the backhead, nodding at every gauge | "Bobblehead: nodding." |
| **A doorbell** | On the cab door | "Doorbell: fitted." |
| **Pine-tree air freshener** | In the cab. The Territory already smells of pine | "Air freshener: pine." |
| **Trophy plaques** | A brass plaque in the crew car for each kind of creature the crew has killed | "Plaques: eleven." |
| **A participation ribbon** | From the town, for the guard van's door. It costs a lot | "Ribbon: participation." |
| **Monogrammed towels** | In the crew car. Nobody washes | "Towels: monogrammed." |
| **Neon OPEN sign** | In the guard van's window, lit. Open for what | "Sign: open." |
| **Extra-long whistle cord** | Reaches through every car, so anyone can blow the whistle from anywhere | "Cord: long." Everyone will |

## 8. The director's answers (13 Oct 2026)

*"I really think that all of these are going to need hard signs and feedback for the player to understand what the
consequence is and why they want to purchase this thing."* And, item by item, which of §4 and §7 go ahead. Queue rows for
the approved ones are taken as each is built.

### 8.1 Every upgrade says what it did (the rule)

An upgrade is only worth buying if the crew can see it working, and only fair if they can see what it costs them. So every
upgrade, on the shelf or to come, has three tells, and an upgrade isn't done until it has all three (U1's call on their form):

1. **On the train, where it is.** It's a thing you can see and point at: the speed recorder's bell on the backhead, the
   oil cups on the axles, the bigger bottles in their brackets, the gold whistle gleaming in the cab. An upgrade with no
   object (the feedwater heater, the booster) gets one: a pipe run, a plate, a stencilled name.
2. **When it works, at the moment.** A sound and a word where it happened, the HUD's way (`ACTION : [KEY]`'s short state,
   never what it will do): the recorder's bell ringing early is its own sound, not the overspeed's; a breach boarded with
   pre-cut boards says "PRE-CUT: BOARDED"; a hot box held off by the oilers smokes thinner and its knock is slower. The
   honk honks.
3. **After, in the tally.** What it saved or what it cost, read by the clerk as flat as the coal: "Speed recorder: three
   bends rung early." "Grill: hounds drawn, two." "Gold whistle: heard by the Choir, eleven times." The incident report's
   causes name an upgrade when it was one (C.9): "Drawn by the barbecue: Dave."

And the shop's card says the consequence, both halves, before you pay: what it does, and what it brings.

| On the shelf | On the train | When it works | In the tally |
|---|---|---|---|
| Speed recorder | A brass bell and a dial on the backhead | Its own bell, early, and "BEND" on the dial | Bends rung early |
| Composition shoes | Grey shoes on the wheels, a stencil on the frame | Less shriek on a long descent; the brake gauge's needle holds | Fade saved on the grades |
| Sprung cradles | Spring racks in a medicine car | The medicine crates rock instead of jolt | Medicine kept that a stop would have spoiled |
| Pre-cut boards | A stack of cut planks by every car's door | "PRE-CUT: BOARDED" and a quicker hammer | Breaches boarded |
| Spare radios | Two more sets on the guard van's shelf | (They're radios) | — |
| Big extinguishers | Fat red bottles in the brackets | A longer spray | Fires out |
| Axle oilers | Oil cups on the axle boxes, a drip | A slower knock, thinner smoke | Hot boxes caught before they seized |
| ~~Feedwater heater~~ | *Retired with the cold (note 852)* | | |
| Gun rammers | A sprung rammer and a brass measure at each gun | A quicker clack on each step | — |
| Trailing-truck booster | A second set of cylinders under the tender | A second exhaust beat pulling away | — |
| The honk | A bulb horn where the whistle was | It honks | Honks |
| Lucky horseshoe | Over the cab door, points up | A glint when a foul didn't happen | Fouls dodged (the dice's, honestly) |
| Gold-plated whistle | It gleams | A brighter note | Heard by the Choir |
| Framed commendation | On the cab wall | — | Read every night |
| The BBQ deck | It's the deck | The grill's smoke and glow; a hound's howl that's its answer | Sausages, beers, hounds drawn |

### 8.2 What goes ahead

**Yes** (the director's words where there are some):

| | Approved |
|---|---|
| Fun | Livery · engine nameplate · the hat rack · more emotes · the yard photographer · the disco ball ("classic") |
| Whimsy | Gerald · the canary · the calliope · the gramophone · the human cannonball (§8.3: "needs strong feedback and consent") · the yard cat ("great") · the hot tub ("fantastic") · the tennis-ball launcher · the garden gnomes · the dog ("fantastic. Great design there") · the fishing rod ("brilliant") · the hammock · the accordion, and a band of instruments (§8.4) |
| Silly | The stuffed moose head · the dining car, with a bar (§8.5) · fuzzy dice · bumper stickers, all puns (§8.6) · flames · spinner hubcaps ("hilarious") · the tiny toy caboose ("super funny") · the La Cucaracha horn · the pine-tree air freshener · participation ribbons · monogrammed towels, their monograms jokes and tiny insults (§8.7) |
| Tools | The cowcatcher · the rear searchlight · the flare pistol ("fantastic") · speaking tubes ("funny") · the armoured cab, with full sight of the line (§8.8) · armour II and III · all of §7.3's |

**The cold.** *"I don't find that crews end up really feeling the cold that much and I'm okay with that now."* So the
cold's not a reason to buy anything: the feedwater heater, car insulation, a beer's warmth and the hot tub's heat are
small comforts, not answers. The hot tub's case is that it's a hot tub. (U1's call: nothing's re-priced; the shop's cards
don't sell the cold.) *18 Oct (note 852): the cold is out of gameplay altogether (the director, 16 Oct), so the feedwater
heater and car insulation are retired from the catalogue (campaign.json keeps a comment where each was); the beer and the
hot tub stay, for their own sake.*

**Not mentioned, so parked** (not built until asked): the pigeon loft, the three-chime whistle, festoon lamps, the brass
bell, the velvet seat, the spittoon, the second stack, the bubble machine, the Tannoy, the piano, wind chimes, the
paddling pool, the mayor's bobblehead, the doorbell, trophy plaques, the neon sign, the extra-long whistle cord.

**Built (note 701):** the silly ones (fuzzy dice, bumper stickers, flames, spinner hubcaps, the tiny caboose, the La
Cucaracha horn, the air freshener, the participation ribbon, monogrammed towels, the moose head) and the disco ball: each on
the train where its card says, read in the tally; the horn heard every pull; the moose head and the disco ball priced in the
night.

**Built (note 714):** the fun ones: the three liveries (bought once, painted in again at the blacksmith for nothing), the
engine nameplate (named at the fortress), the hat rack (HAT in the yard), more emotes (the bow, the salute, the conga) and
the yard photographer (his picture as she comes out of the gate, on the HUD and in the night's folder; the end card's
gallery is still to come).

**Built (note 715):** the first whimsies: Gerald and the garden gnomes (the Gannet takes one for a walker now and then, and the
clerk reads it out), the canary (quiet while something's aboard, dead in fumes), the steam calliope (a note a pull; a Moose
stops for it) and the gramophone (wound with Use; the Gaunt hears it as talk, a Moose stops for it). **Built (note 744):**
the hammock (warm-ish), the hot tub on the engine's hood (warm while there's steam, and it eats steam) and the fishing rod (a
fish at the water's edge; grilled on the deck, a better meal than a sausage). **Built (note 745):** the yard cat (sleeps on
the warm backhead, hisses at what's aboard near it), the dog (barks at what's aboard or coming up behind, fetches a toy to
whoever's near) and the tennis-ball launcher (a ball off the back, and a Cinder Hound goes for it, worries it, and brings it
back). Next: the human cannonball.

### 8.3 The human cannonball: consent and feedback

Nobody is fired who didn't climb in, and everyone knows what's about to happen.

- **Consent.** Only a living crewmate gets in, and only themselves: Use at the guard van's cannon's muzzle, with it loaded
  with powder and no shot, "CLIMB IN : HOLD [E]". Nobody can put anyone else in: not a body, not someone held, not a bot
  that wasn't asked (a bot climbs in only when the gunner says so on the radio, and the bots' answer is "absolutely not",
  the first time). In the barrel, Jump climbs out, any time before the shot.
- **Feedback, before.** The one in the barrel sees out of the muzzle and "IN THE CANNON   CLIMB OUT : [SPACE]". The gunner
  sees "LOADED: SAM" and the arc: a dotted line to where they'll land, red where the landing kills (over 16.5 m/s across
  the ground, §23: fired from a train at speed, it does). Firing at a red arc asks again: "FIRE SAM ANYWAY : HOLD [MOUSE]".
- **Feedback, after.** The radio says it to everyone ("Sam's been fired from the rear cannon"), the camera follows Sam a
  moment for the crew who look, and the incident report owns it: "Fired from the rear cannon by Dave."

### 8.4 The band: instruments that play together

*"You shouldn't automatically make the crew dance. That should be ... a function. People can participate by dancing. We
should also add more instruments ... all play together should create some sort of public domain song ... synced up by the
first person who starts playing. It will be the song that is selected from a repertoire. And then as people play
different instruments or the same instrument, they should sync up to whoever played first. And then that should all stop
once the last person stops playing."*

*And (the director, 13 Oct, starting it): "When it comes to the band, which I think is important, make sure that they are
like Celtic folk style songs, because we want to keep in with the East Coast folk aesthetic here."* Built (note 700).

- **Instruments**, each bought from the blacksmith, each a carried thing played with Use held (a tap still puts it down):
  a fiddle, a tin whistle, a button accordion, a tenor banjo, a guitar, a bodhrán, the spoons and an ugly stick (the
  Newfoundland one: a mop handle, a rubber boot, bottle caps and a tin can). A kitchen party's band, not a jug band.
  Each plays its part of every tune: the tune (fiddle, whistle an octave up, accordion, banjo), the chords and the bass
  ("boom-chuck", a jig's "boom-chuck-a": the guitar), the beat (the bodhrán's tipper, the spoons' clack, the ugly stick's
  boot and caps). Two of the same play in unison.
- **The repertoire** (band.json; traditional, public domain, our own transcriptions in abc, played by the game's own
  synthesized instruments): *Drunken Sailor*, *The Swallowtail Jig*, *Soldier's Joy*, *The Irish Washerwoman*,
  *Morrison's Jig*, *Auld Lang Syne*. Reels, jigs, a shanty and the one everyone knows. For the audio chat to transcribe
  and add (the East Coast's own, public domain): *Farewell to Nova Scotia*, *I's the B'y*, *Lukey's Boat*, *Jack Was
  Every Inch a Sailor*, *Mussels in the Corner*. Not the minstrel songs.
- **The sync.** The first to start playing starts a tune: the next in the repertoire, its first bar on the host's tick.
  Anyone who starts after comes in on the next bar of it, on their instrument's part, heard where they stand. Played out,
  the next tune follows after a breath, as a session's set does. When the last player stops, it stops; the next to start
  begins the tune after. It's the host's clock, so every machine hears the same bar.
- **Dancing is a choice.** Nobody's made to dance. The emote wheel's DANCE (note 298) is anyone's; dancing to the band's
  beat is still to do.
- **The night hears it.** A band is loud: each player playing is as loud to the Choir as a noisy toy, and draws the
  night's first threat as one (band.json `loudness`). A Gaunt in earshot hears it as talk and is calmed; the Moose stops
  to listen (note 711).
- **Recorded, and the bots play** (note 711): CC0 recordings of each instrument (or the nearest to be had), and a bot with
  nothing to do joins in when the band's playing.

### 8.5 The dining car's bar

*"If we have a dining car, we should be able to order drinks at the bar. If you drink one of those drinks, though, your
vision should get blurry ... [it should] make you sort of tip over a bit. Of course, that wouldn't happen in VR."*

- **The bar** is the dining car's far end: a counter, bottles behind it in a rack, a bell on the counter. Use at the
  counter: "ORDER A DRINK : [E]", and a drink's poured into your hands (a few a night: what the fortress's cellar sold
  the crew, U1's call: eight). Held Use drinks it, as a beer.
- **A drink** blurs the drinker's view a while (a minute, a little more with each), and leans it: the horizon tilts a few
  degrees one way and the other, slowly, as if the floor's moving more than it is. It's only the drinker's camera and only
  this machine's (presentation, never the sim: the walk is straight, so the netcode doesn't care), and never in a headset
  (VR comfort; VR's on the back burner).
- **Feedback**: the drinker sees it; the crew see the drinker's head loll (the art's); the tally reads "Bar: eight
  drinks. Repairs: one window."
- **Built** (note 959): `diningCar` in campaign.json, the car behind the crew cars, its bar's eight drinks
  (train.json `composition.dining`), the blur and the lean (look.json `tipsy`). The clerk's "Bar: drinks, 8." (note 972: the
  tally lines read the night's counts; note 982: in the catalogue's words, the clerk's bank built with them). Note 974: the
  room modelled (the table laid for eight, its chairs, the chandelier and candelabra, the counter, its bell and the back
  bar), and the crew seeing a drinker's head loll (look.json `tipsy` `loll*`).

### 8.6 Bumper stickers: all puns

On the guard van's end, three at a time, a new set from the blacksmith's drawer each night (U1's call). Never about the
Soot Children or the Passenger: those stay straight (GDD §23.1).

- TENDER IS THE NIGHT
- GAUGE YOUR EXPECTATIONS
- COAL HARD CASH
- STEAM HAPPENS
- TRAIN OF THOUGHT: DERAILED
- SOME LIKE IT HOTBOX
- RIBBIT OR LEAVE IT
- GAUNT TELL ME WHAT TO DO
- MOOSE-T WE DO THIS EVERY NIGHT
- WHISTLE WHILE YOU LURK
- STOKER? I HARDLY KNOW HER
- COUPLE GOALS (UNCOUPLING IS ALSO AN OPTION)
- I BRAKE FOR NOBODY
- LOCO-MOTIVATED
- RAIL AGAINST THE MACHINE
- KEEP CALM AND CARRY COAL
- ALL ABOARD THE NOPE TRAIN
- SWITCH HAPPENS
- BOILER ALERT
- HOW'S MY DRIVING? SHOUT IT, THE CHOIR'S LISTENING
- IF YOU CAN READ THIS, YOU'VE BEEN LEFT BEHIND
- I'M NOT LATE, DAWN IS EARLY
- MY OTHER CAR IS A CABOOSE
- THIS IS FINE (THE CAR IS ON FIRE)

### 8.7 Monogrammed towels

In the crew car, a stack of good towels, each embroidered. Some with a joke, some with a small insult, some with a crewmate's
name and an opinion of them (filled in from the crew's names each night, as the clerk reads them):

- MOSTLY DRY · STILL DAMP · NOT YOURS · SPARE · THE OTHER ONE · IOU · PROBABLY FINE · BACKUP · RETURN TO SENDER
- WASH ME · FOR EMERGENCIES ONLY · DO NOT USE THIS ONE · YES, THAT ONE · FLOOR
- {NAME}: SHOWERS RARELY · {NAME}: TRIED · {NAME}: FAVOURITE (OF NOBODY) · {NAME}: SECOND BEST · {NAME}: ASK THE OTHERS

### 8.8 The armoured cab: full sight of the line

*"Armoured cab has the right idea. But I don't like the slits for the windows. You still want full visibility of the
track."* The cab's windows stay whole: the armour is thick glass in heavy riveted frames, and a grille of a few thin bars
outside it (the bars the art already draws over the front windows, note 338). What comes over the front is slowed at the
glass (it cracks before it gives; the Climbers' smash takes twice as long), and the driver sees the whole line through it,
cracks and all, until it's mended with the wrench.
