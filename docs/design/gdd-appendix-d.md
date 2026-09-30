# DARK TERRITORY
## GDD v1.2, Appendix D, September 2026

*Appendix D of GDD v1.2, as it stands in that version: the rest of this repo's GDD (`gdd.md`) is still v1.0. It supersedes the Systems Spec's Part C (death, the Vigil) and Part E's drop-in and drop-out rows, and the Line Plan's pickup points (§11.2, §11.3, §17.3's late-join note). Its numbers live in `content/tuning/holdouts.json`; how it's built, and the readings where it's silent, are in `docs/ARCHITECTURE.md` §8 note 92. Where it names the v1.1 roster (the Gaunt's silence check, the Car Hugger), this build has what it has.*

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

- A dead player becomes a **spectator** (D.10) and joins the **respawn queue** (D.6).
- **The body persists at the death location** and never despawns. It keeps everything the player was carrying. If it comes to rest outside the walkable corridor, Line Plan §12.6 relocates it.
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
| **Facility pad** | 1, plus a second on pads of scale ≥200 m and on every switchyard | 60–200 m from the consist's stopping position on the pad. Must not share a walking route with the nearest loading module, so rescue competes with loading for people. |
| **Halt** (Line Plan §11.3) | 1 | On or beside the platform, ≤40 m from the main line |
| **Dead town / village** (Line Plan §11.3) | 1 | Within the station footprint, ≤80 m from the main line |

- **The second facility Holdout only activates when the session crew is 5 or more** at assignment time. Below that it stays dormant dressing.
- **Every Holdout lamp needs line of sight** to the site's approach: from the 1 km board at facilities, and from the whistle board at halts and dead towns.
- **Halts and dead towns become optional stops.** They are still landmarks and spawn contexts. Stopping on the open main line to rescue someone gets no facility lull, and it costs dawn slack like any other stop.

### Types

| Type | Sites | To free | Breach cost |
|---|---|---|---|
| **Prison car** | Facilities with a spare siding. Preferred at switchyards, wreck yards and military depots. | Smash the lock, or open it with the repair kit | Smash: 3s, loud · Repair kit: 6s, silent |
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
| Open lock | Repair kit in hand | 6s | None |

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

## D.9 Bodies as loot

**Every body brought home earns the crew cash back.** Every death costs the crew more than the body returns, so death can never be farmed.

| Rule | Value |
|---|---|
| **Crew-loss fee** | Charged to the shared wallet at settlement for every in-run death: **50% of the tier's per-car value** |
| **Body refund** | For every body delivered: **75% of that death's fee**, plus everything the body was carrying returned to stores |
| **Net cost of a recovered death** | 25% of the fee. Unrecovered, 100%. |
| **Delivered means** | Stowed in a car attached to the locomotive at arrival, or carried by a crew member aboard. **Decouple the car and the body goes with it.** |
| **Carry** | Hand-carried loot (Appendix C.4): 2.8 m/s and no climbing, with the exception below |
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

Provisional voteable set, subject to the runtime rule: Track Doll · Cinder Hounds · Car Hugger · Climbers · Draggers · Whistler · Tippy Toesie · Fire Flies · Ribbits · The Gaunt · Followers · Soot Children · The Passenger · The Switchman · Grumbler.

## D.12 Run end: incident report and commendations

### Incident report

Shown on the run-end screen. It lists:

- deaths, with who and where
- rescues, with who freed whom and at which site
- bodies delivered and bodies lost
- cars lost
- **what the dead voted for**
- **bookmarks**

A bookmark is made with a button while dead. It stores a timestamp, the followed player's name, and a **still capture** of the followed view. There are no replays.

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
| Second facility Holdout: pad scale | ≥200 m | — |
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

## D.15 Open questions

1. **Drop-out fee.** Drop-outs currently carry no fee, so a crew isn't punished for someone's connection. This could be abused by quitting instead of dying. Watch for it.
2. **The second Holdout threshold.** Is crew ≥5 right, or should big crews get one at every facility?
3. **Halt stop cost.** Is a main-line stop at a halt dangerous enough, with no facility lull, or does it need a dedicated director response?
4. **Live Mic and the Passenger.** A spectator who noticed a silent crew member could name it over Live Mic to a rescuer at the door. The living have the same tell, and the 26 m range limits it to one listener mid-rescue, so it's accepted for now. Watch for it in playtests.
5. **Commendation reel.** Bookmarks are stills. If they turn out to be the best part of the run-end screen, short clips may be worth the tech later.
