# Social collision audit: does the train make situations between people?

*An audit and design proposal, 14 Oct 2026, against main at `ed7b929` and the open PRs
[#999](https://github.com/nsmith-gd/darkterritory/pull/999) (K1: horseplay, voices, prizes, cuts) and
[#1001](https://github.com/nsmith-gd/darkterritory/pull/1001) (A1: the lone struggle). Nothing in the game was changed
for it. The GDD stays the source of truth: §11 lists the amendments this proposes. None of them has been applied. No
queue or note number has been claimed yet; that waits on the director's go-ahead (COORDINATION rule 1).*

**The question.** Do the mechanics we have make overlapping responsibilities, tempting bad decisions, disasters you can
come back from, rescues, and stories? Or are they mostly independent systems running side by side?

**Evidence used, and how strong it is.** Four levels, kept apart throughout:

| Level | What it is | How much we have |
|---|---|---|
| **Code** | Merged on main, read for this audit | Everything below |
| **Tested** | Pinned by automated sim tests | Most mechanics (named in place) |
| **Branch** | Implemented on an open PR, not on main | K1's #999, A1's #1001 |
| **Observed** | Seen in a human playtest | Solo or bot playtests only. **No recorded playtest has had two humans in it.** The 13 Oct morning and evening and 14 Oct files are the director alone. The second 14 Oct file is the director and two bots. `friendslop.md` §1 says the same: "nothing yet played by a real group" |

Every social score below is therefore **mechanical potential**. Observed social success is **UNVERIFIED** throughout.

**Bot baseline** (`dt balance --tiers frontier --crews 2,4 --seeds 4 --cars 10 --seconds 1500`, and two `dt harness --bots 4
--as-app --enemies` nights on frontier:2 and frontier:3, 10 cars):
- Half the nights were delivered. Deaths ran 0–1 a night. Quiet share was 0.24–0.72. The longest quiet was 40–175 s.
- At crew 4 one crewmate's slack went 170 s past the press.
- In the two harness nights, **Cinder Hounds were 13 of 22 and 15 of 29 spawns**.
- There were **zero grabs and zero rescues**.
- The gunner bot's longest slack was **371 s and 202 s**.
- Seed 2's only conflict pair was `whistler+facilityStop`, drawn three times.

Bots show the systems run. They cannot show that friends have fun. Nothing below is presented as if they could.

---

## 0. The director's calls (14 Oct 2026)

Given on the audit in S1's chat; #999 and #1001 merged the same evening. The work is queue #479–#485 (notes 749–755).

- **R1, incidents travel:** "looks good", and more: **"make the radios part of the default kit ... Hold down right click to
  use the radio since communication is so important"** (#479). **"A grab should allow players to still use the radio but
  their voice should be muffle filtered as if the grab is covering their mouth partially."** The news line: #480.
- **R2, breathing room:** "good. Don't necessarily want a lot of it though" (#481: short).
- **R3, cliffs into cascades:** explained; **the struggle in a crew approved** ("Approved for Grabbed", #486). The bend
  throw: the director asked what happens to a crewmate the driver leaves behind (the left-behind rules, note 273); not yet
  decided.
- **R4, one can, two do it faster:** "absolutely the right direction" (#482).
- **R5, the census reads commitment:** "good" (#483).
- **R6, the comet after #999:** "good" (#485).
- **R8, the shover's mark:** **"Shovers mark should reset to normal targeting after they are killed"** (#484).
- **The estimates:** "You can build all this in a night for sure."
- **Why interdependence shrinks, on purpose:** "we want solo play a bit and solo testing. Its a good intro into 'I like this
  game but I need my friends to play for it to go farther'. This is what PEAK did really well ... you can make it a few
  runs and things are slow and the loot isnt necessarily large because you're on your own and have limited time.
  Eventually the threats become so numerous you succumb to the games systems because friends arent able to help you push
  further with Scrip needs or monster rescues." So §4's second deficiency reads as a progression curve, not a fault:
  solo is the way in, and the wall is where friends come in. R4 is that curve's mechanism.
- **The bots:** "We need our Bots to start simulating more player behaviour. Maybe bots should talk or call out, make
  jokes, fill dead air." S1's answer (bots hear only what a person would; good crew, middling friends, so a bot crew never
  erases the wall; voiced callouts and chatter from game state through the same voice paths; seeded personalities) is
  offered to the director and A1, whose area the bots are.

## 1. Executive verdict

**Dark Territory has a strong co-op skeleton, and two things keep it from working as a social game.**

**The skeleton.** These already exist and are tested:
- Slow kills with rescue windows (`enemies.json` `grab`; most windows 8–14 s).
- Creatures that use voice as an input: the Gaunt is talked to, the Choir hears loud crews, the Soot Children mimic voices.
- A conflict table for the director (26 pairs, `enemies.json:1158`).
- Two-person jobs at the stops: the winch, the steam lift, heavy crates, the livestock ramp.
- The dead come back through Holdouts.
- Cut cars can be coupled back on.
- Fires can be fought while the train runs, and a ruptured boiler can be mended.
- Every incident is attributed to a person (`Attribution`, `IncidentLog`).

That is more social design than most of the genre ships with.

**The first problem: incidents don't travel down the train.**
- Proximity voice cuts off at 26 m (`VoiceRouting.cs:50`). The starting 3-car train is about 66 m long; a 10-car train is 175 m.
- The living get no notice during the night that someone was grabbed, died, or is on fire. Only the victim sees "SOMETHING HAS YOU" (`Hud.cs:1043`).
- The radio is the GDD's way of carrying a grab down the train. There are 2 sets for the whole crew (`train.json:308`), and being grabbed **smashes the victim's set 25% of the time** (`radioBreakOnGrab` 0.25, `train.json:322`).
- An 8–12 s window covers about 2 car-lengths at roof pace.
- So a grab is either seen by a neighbour or becomes a silent death read out in the end-of-night report. A collision nobody perceives is not a social event.

**The second problem: interdependence has been shrinking, one sensible fix at a time.** Each change below was right for the solo and bot playtests that prompted it:
- The guns no longer need boiler pressure, and their shot is endless (note 689). That deleted the only built two-person job on the run, a walker carrying powder to the gunner (note 374/377, `combat.json:38-42`).
- The cab is one person by decision (§12).
- Coupling is automatic below 1.5 m/s.
- The crane can be worked alone.
- The lone struggle is going from 15% to 50% (#1001).

Nothing replaced the lost "you need me" with "you're better with me".

**The director and orchestrator are close, but aimed at the wrong player.**
- The census knows each crewmate's post and slack. It does not know what anyone is committed to (Census.cs:60-79 reads only `Seated`).
- It steers the next threat to **the idlest** crewmate (`WhoseNext`, Director.cs:527-549).
- It is off at stops, where the two-person jobs are (Director.cs:156-158).
- The settings meant to give breathing room (`facilityLullSeconds`, `incidentWeight`, `incidentFullCrew`, `enemies.json:1113-1116`) are **read by no code at all**.

**How close are we?** The pieces for "one friend's bad call drags three others into a ridiculous rescue" are mostly built. What's missing is that the three others **find out in time**, that the rescue **is worth running for**, and that afterwards there's **a breath to laugh in**. The shortest path is not a new director or new content. It is:
1. Make incidents travel.
2. Make a second pair of hands faster wherever one pair is enough.
3. Point the census at commitment.
4. Turn the few instant-death cliffs into recoverable cascades.
5. Wire up the breathing room that's already in the tuning.

Then put four humans on it. That last step is the one we have never done.

---

## 2. Scorecard

Scored 0–5 for **mechanical potential** on main (with #999 in brackets where it changes the score). Observed social success is **UNVERIFIED** in every row: no human-group playtest exists. Confidence is about the potential score, from code and tests.

| Dimension | Score | Evidence | Weakest element | Strongest existing opportunity | Confidence |
|---|---|---|---|---|---|
| **Meaningful tasks** | 3 | Stops are rich: crane, winch, steam lift, conveyor, spout, tipple, ramp (`facilities.json`). On the run there are ≈2 hot boxes and ≈2 lamps a night, mail, and the guns (`upkeep.json`; spec B.8). About 80 s between stops at cruise | Riding between stops for everyone but the driver. The gunner bot idled 371 s in one night | The stops' modules and upkeep's escalation (a hot box left 120 s becomes a car fire, `HotBoxes.cs:77`) | High |
| **Natural role interdependence** | 2 | Hard two-person: the winch (both handles), the steam lift (a lever at the facility while the driver vents the cab), heavy crates, the ramp, breaking a grab. One person suffices for: the cab (§12), the guns (note 689), coupling (automatic, `train.json:141`), the crane (Crane.cs), switches, every upkeep job and every repair | Since note 689 nothing on the run needs two people | The **steam lift**, a true cross-role dependency (the driver's vent feeds the lift). Also the GRAB rescue | High |
| **Conflicting priorities** | 2.5 | The conflict table is built (Director.cs:472-482): 26 pairs, but 0–2 a night (`pairsPerRun`). A pair is "a kind present", and `facilityLoading` means just being at a facility (Director.cs:231-239). `WhoseNext` sends threats to the idle. The answers table lacks 7 kinds (`enemies.json:1024-1030`) | Collisions are seeded by creature kind, never by what a player is doing | The conflict table's per-pair "bind" text (GDD App. B.1) is the right design. It needs to read players | High |
| **Player temptation** | 1.5 (2.5) | Main: comet cargo is loaded nowhere. Village finds have no value per kind, so nothing can be learned. Finds are hidden until searched. The child is very rare (`soot` `realChance` 0.5, `minCrew` 2). The tally pays nothing undelivered and doesn't say why (playtest 13 Oct ev. line 27). #999 adds glowing comet fragments (6–22% of stops), value by kind and finder lines | Nothing visible from the line asks you to stop | #999's comet fragment. It's only 1.2 car-loads and rare, which is too small and too seldom to tempt reliably | Medium |
| **Physical comedy** | 1.5 (3) | Main: crewmates pass through each other (`PlayerMotor.cs:873`). Ragdolls, carried bodies, a crane casting falling. #999: collisions, the shove, the bonk, thrown coal | Main has no contact between friends. On #999 the horseplay has no sounds yet (it asks AU1) | #999 plus crane hoisting and loose loot (#453, #460, unbuilt) | Medium |
| **Recoverable disasters** | 3 | Recoverable: Holdouts (D.6), re-coupling (`CouplingTests`), fires fought underway, boiler rupture (25 s), failing cars, left-behind (the train backs; `Bots.cs:2370`). Instant: pulled or thrown off a roof above 4 m/s (`jumpOffLethal`, `PlayerMotor.cs:1332`); derailment kills everyone, those off the train too (`World.cs:2663`); Knotter, Lodger and Dave hold for 1.5 s or less; any solo death ends the night. A lone struggle only works at crew alive ≤1 (`World.cs:1609`). No downed state | Most recoveries are **one person** with one tool | Left-behind plus reversing, and parted cars plus re-coupling. Both are full rescue stories already built | High |
| **Communication necessity** | 3.5 | Voice feeds the Choir, the Gaunt, the Moose and the first-threat draw (`combat.json:101`, `enemies.json:756, 339, 885`). Roll call is verbal by design (GDD open Q2). Pillar 3 | Bots are mute to humans (`CrewVoice.cs`). With 2 radios, most of a crew can't reach the cab | #999's tube and lamp signals. The **spotter** (playtest 14 Oct line 9: the driver can't see the car under the chute) | High (mechanics), none (observed) |
| **Social visibility** | 1.5 | 26 m voice against a 66–330 m train. No grab or death notice to the living. The grab cry carries 40 m (`GameAudio.Crew.cs:428`). Radios: 2, smashed by grabs. The cab map has no crew on it. Bookmarks and the incident report show only after the night | The night's events reach most of the crew only at the end | `Attribution` already knows who, what and where, live (Attribution.cs:15-60). It just isn't shown in the night. `News` exists for cars (`News.cs`) | High |
| **Breathing room** | 2.5 | 25–45 s cooldown after a spawn (Director.cs:511). The busy damping fades to nothing by night's end (:608). `facilityLullSeconds` is dead. Nothing slows after a death or a lost car. 17-min night, ~80 s between stops. Note 646: at crew 4 the cap held for ≈400–630 of 900 s. The big cab (#450) is unbuilt | The director never waits after a crisis | The yard and forts are safe (`yardIsSafe`). The relief valve. The terminus hush | High |
| **Replay variety** | 2.5 | Varied lines from linegen. A `nightChance` roster. Variety window 4 (Director.cs:484). Note 652: about 6 kinds a night, the kinds that come pile up. The director's playtest: "very one note". The hounds were half the spawns in the bot nights | No memory of combinations; hounds dominate | The conflict table, plus A1's #476 already on the hounds | Medium |

**Mechanical potential averages about 2.3/5 on main (≈2.7 with #999).** Observed social success has no data.

---

## 3. What already works: use these, don't replace them

1. **The GRAB skeleton** (App. A.1; `Enemy.cs:290-414`). Slow kills, rescue windows, the victim can talk while held, the voice cut hard at death. It's the genre's best comedy engine. It only needs a witness.
2. **The conflict table** (App. B.1; `enemies.json:1158-1198`). The right design idea, already in the pick. It needs to know about players, not just kinds.
3. **The census** (`Census.cs`). Posts, slack, who a threat is "on", per-player caps. It's the place to add commitment, so there is no reason for a second director.
4. **`Attribution` and `IncidentLog`.** A live, host-side record of every grab, death, rescue, cut, fire and draw, with names. The end-of-night report already turns it into sentences. In-night alerts and every metric in §8 can hang off it.
5. **Left-behind and parted cars.** Hunts, cold, the train backing up, re-coupling, bots going back (notes 273, 653; `LeftBehindTests`, `PartedCarsTests`). These are whole rescue stories already built.
6. **The two-person stop modules**: the winch, the steam lift, heavy crates, the ramp (`facilities.json`). The steam lift is the model cross-role dependency.
7. **Holdouts** (App. D). The dead return if the crew stops for them. A real "go back for him" decision at every stop.
8. **Upkeep that escalates** (`upkeep.json`). A hot box becomes a fire, a loose pin becomes a parted train. Small jobs with consequences.
9. **Replays and test hooks.** `.dtrec` replays (note 515) re-simulate a human night tick for tick, so every code metric in §8 can be computed **on real human playtests**, not only bots. Voice can be recorded with consent (`NightRecorder.Voice`). `dt balance`, `dt harness`, `--insist`, and `dt mcp` (`night_insist`) give deterministic scenarios.
10. **K1's #999.** Collisions, the shove, the tube, lamp signals, comet fragments, value by kind. All aimed at the right genre gap. The only part to reconsider is the shover's mark (§10).

---

## 4. The five biggest deficiencies

1. **Incidents don't travel.**
   - Voice reaches 26 m. Only the victim gets a HUD alert. There are 2 radios, and a grab smashes one 25% of the time.
   - The cab map shows no crew. Bots never call out.
   - Most rescue windows are 8–12 s, enough to cross about two car-lengths.
   - **The game's best social mechanic, the rescue, is only reachable by whoever happens to be nearby.** That wastes the conflict table, the orchestrator and pillar 5 at once.
   - It also cuts against the user's own pillar: "never make something more challenging by removing essential feedback".
2. **Interdependence keeps shrinking, and nothing has replaced it.**
   - Every solo fix removed a "you need me" (note 689's guns, automatic coupling, the solo crane, #1001's struggle).
   - The design hasn't adopted the pattern that keeps both: **one can, two do it faster**.
   - The run between stops has no two-person job at all.
3. **The orchestrator aims at idleness, not commitment.**
   - The census can't tell a crane operator mid-lift from a rider. It reads only `Seated`; `Heavy`, `Operating`, `Shovelful` and `Held` go unused.
   - It's off at stops.
   - Seven newer kinds have no `answers` entry, so their threats are "on" nobody.
   - Pairs are by kind present. `facilityLoading` is just being at a facility.
   - So the director gives the idle something to do. It doesn't make the idle choose between their own post and a friend's emergency, which is the collision we want.
4. **No temptation on main.**
   - Nothing is visible from the line. Nothing is worth knowing the value of. The jackpot (the child) almost never comes.
   - The tally doesn't explain itself. Gross is zero when undelivered, and the clerk never says so (`Radio.cs:50-69`).
   - #999 starts to fix this, but its fragment is rare (0.06–0.22 of stops) and small (1.2 car-loads).
5. **Cliffs where cascades would make better stories, and no breath afterwards.** These instant deaths are exactly where a recoverable version would produce the best stories:
   - Being thrown off a roof on a bend kills at 4 m/s (`PlayerMotor.cs:1332`), though landing on your own feet kills only at 16.5.
   - The lone struggle is impossible if any crewmate is alive anywhere on the train (`World.cs:1609`).
   - Knotter, Lodger and Dave hold for 0.25–1.5 s.

   Afterwards there's no breathing room:
   - There's no lull after a death, a lost car or a rescue.
   - The facility lull is dead tuning.
   - The busy damping is gone by the end of the night.

**Across all five:** every tuning decision of the last week came from solo or bot nights. Bots don't talk, don't cover for a dead driver (#475), and don't come aboard on the horn (#474). The social game has been tuned through an instrument that can't hear it.

---

## 5. Ranked changes

Ranked by expected social benefit over implementation and regression cost. Every change is a tuning flag (off restores today's behaviour), deterministic, numbers in `content/tuning`, with a test or a `dt` command. None adds a creature, a facility, a minigame or a progression system.

### R1. Incidents travel the train (small, 1.5–2 days)

- **Current behaviour.**
  - A grab alerts only the victim (`Hud.cs:1041-1047`).
  - `News` carries only cars lost and the engine failing (`News.cs:5-50`).
  - `radioBreakOnGrab` is 0.25.
  - The cab map has no crew.
- **Social deficiency.** Rescue windows only work for whoever's within 2 cars. A friend dying three cars away is learned about at the report. The incident gives no shared moment at all.
- **Proposed adjustment.**
  1. A held crewmate becomes a **news line for the living beyond voice reach**, naming who and roughly where, but not what: "PRIYA — CAR 6 ROOF". It lasts the hold, from `Attribution`'s Grab event. Within 26 m, nothing is shown: you hear it.
  2. `radioBreakOnGrab` 0.25 → 0. The GDD's "radio broadcast of a GRAB" (C.8) should not be undone by the grab.
  3. A death is the same kind of line, one beat: "PRIYA IS GONE — CAR 6".
  4. **The what stays verbal.** The line says who and where. The victim, or whoever's near, has to say what has them and what to bring. That keeps pillar 3.
- **Example encounter.** Sam is coaling at the tender when "TOM — CAR 4 GAP" comes up. Tom is screaming "it's the knotty one, bring the wrench, NOT the shovel". Sam sprints two cars with the shovel anyway. Tom dies in the gap. The report already says "Nearest crew: Sam, 3 m".
- **Comedy/social mechanism.** It brings the far crew into the victim's window and lets them try. Trying and failing in front of everyone is the joke; dying alone isn't.
- **Implementation.**
  - `News.cs` gets kinds Held and Dead from `Attribution`; `Hud.cs`'s news strip.
  - `train.json` `radioBreakOnGrab`.
  - New `run.json` or `sight.json` `crewNews { on, beyondMetres: 26, deathSeconds: 4 }`.
  - Tests in `NewsTests` and `AttributionTests`.
  - GDD C.8, §23.1 and open Q2 (see §11).
- **Failure risks.**
  - It weakens "roll call is verbal". It doesn't remove it: there's still no aboard roster. Note that K1's #459 lamps and A1's #474 "all aboard" already move this way.
  - Alert fatigue if it shows for every Tippy Toesie grab. Mitigation: only beyond reach, and one line at a time.
  - It must never show the dead anything (the director's 14 Oct call on haunting).
- **Verification.**
  - Harness: a "rescue attempt" (§8) is anyone closing distance on a held crewmate from beyond 26 m.
  - Compare attempts and rescues per grab on seeds 1–8 at crew 4, flag on vs off, with `--insist draggers,knotter` so grabs happen.
  - Human: count grabs where someone from beyond voice reach arrives.

### R2. Breathing room after a crisis: wire up what's already in the tuning (small, 1 day)

- **Current behaviour.**
  - `facilityLullSeconds` 20, `incidentWeight` and `incidentFullCrew` are declared (`EnemyTuning.cs:700, 761, 763`) and read by no code.
  - After a death, a lost car, a put-out fire or a rescue, the director carries on. The post-spawn cooldown is 25–45 s, and under heavy pressure it gives way.
  - The busy damping `1/(1+0.5×engaged×(1−progress))` is 1 at night's end.
- **Social deficiency.** Stories need a gap to be retold in, and the director has no idea a crisis just ended. Note 646: the cap was held for about half the night at crew 4.
- **Proposed adjustment.**
  1. Implement App. B.1's **facility lull** as written: 20 s of no facility threats on arrival.
  2. Add an **aftermath** hold. When `Attribution` logs a Death, CarLost, Rescue, a Holdout freed, or a fire out on a car, the director adds no *quiet* pressure for `aftermathSeconds` (≈30 s), and its cooldown can't be pressed through.
  3. Floor the busy term's progress at `busyFloor` (≈0.5), so late in the night two engaged threats still slow new spawns.
- **Example encounter.** The crew drag Priya back from a Draggers' hold. Three people are on the roof of car 5, out of breath, and someone starts blaming the driver. For 30 s nothing new comes. They get to finish the argument.
- **Comedy/social mechanism.** It gives the laugh time to happen. The cost is the dawn clock, which keeps running, so the lull is never free.
- **Implementation.**
  - `Director.cs` `Decide` (the gates at :342-406) and `Build` (:557-617).
  - `enemies.json` `director.aftermath`.
  - `PacingTests` gets "after a rescue nothing spawns for N s unless pressed past `pressAt`+".
- **Failure risks.**
  - Exploits: crews can't cheaply make deaths or rescues, so it's hard to farm.
  - Long lulls can feel flat. Mitigation: keep it ≤30 s, and let the pressure still build from cargo and escalation.
- **Verification.**
  - Harness metric "seconds from a major event to the next telegraph" (§8). Target ≥20 s after 80% of major events.
  - The balance check "never quiet over 90 s" must stay as it is.

### R3. Cliffs into cascades (small, 1–1.5 days)

- **Current behaviour.**
  - Being thrown off a roof on a bend is judged by `jumpOffLethal` 4.0 m/s (`PlayerMotor.cs:1332`; `LinesideTests:112`), so it's effectively always death. Landing on your own feet from a jump is judged at 16.5 m/s.
  - The lone struggle only works when ≤1 crewmate is alive (`World.cs:1609`). In a crew of 4 a crewmate grabbed 200 m from anyone has nothing they can do.
- **Social deficiency.** These are the moments that should become rescue stories ("he flew off on the bend!"). Instead the story ends.
- **Proposed adjustment.**
  1. **Thrown off on a bend** uses the landing rules (`landing.lethalAbove` 16.5). Under it, you land hurt and become a **left-behind**: the hunt, the cold, the train backing up. All of that exists.
  2. **The struggle is gated by distance, not by crew count.** It's allowed when no living crewmate is within `soloStruggleBeyond` (26 m), at `crewStruggleChance` (proposed 0.2). Crew of one keeps #1001's number.
  3. **Flag to G1, no change proposed here:** the Knotter (1.5 s), Lodger (0.25 s) and Dave (0.4 s) holds break social test 3 ("the kill takes long enough for a friend to act, 8–20 s"). Either the GDD records them as deliberate exceptions, or they get windows. The tuning comment "the Householder's 6 s is the shortest" is wrong.
  4. **Draggers' pull-off stays lethal.** It's their punish, after an 8–12 s window.
- **Example encounter.** Jo is on the roof as Max takes the bend 4 km/h over. Jo flies off into a field at 13 m/s. Max has to back the whole train up 400 m in the dark while Jo runs from Ribbits towards the lamp, and someone hangs off the rear ladder to grab Jo's hand.
- **Comedy/social mechanism.** It's a player-authored mistake (the driver's speed). The recovery is recognisable and costs the whole crew time. Max will hear about it for weeks.
- **Implementation.**
  - `PlayerMotor.cs:1107-1140` and `:1332` (`PullOff` by cause).
  - `Lineside.cs` (the bend throw).
  - `World.cs:1609`.
  - `enemies.json` `grab`.
  - Tests: `LinesideTests` (update the pin), `LeftBehindTests`, `DemoRosterTests`.
  - GDD §23 (the outright-kill list says "three" and lists four, `gdd.md:831`).
- **Failure risks.** Fewer deaths on bends could make speeding cheap. It doesn't: the crew still lose the time, the hunt is real, and above 16.5 m/s it's still death. The crew struggle could cut rescues. Its odds are low, and a friend is still better.
- **Verification.**
  - Deterministic scenario: a bot rider on a roof, the bot driver forced over a posted bend (`dt mcp` `night_insist` / harness `--express`).
  - Then count left-behind retrievals and deaths, flag on vs off.
  - Hold deaths a night at crew 4 within ±20% of baseline.

### R4. One can, two do it faster (small–medium, 2 days)

- **Current behaviour.** Since note 689 the guns are one person's job. Most jobs ignore a second person; the only built "two at it" bonus is rerailing at the tipple.
- **Social deficiency.** Nothing on the run makes a friend useful beyond doing a second job alongside you. This is the cheapest way back to mutual dependence without breaking solo play or the director's 13 Oct calls.
- **Proposed adjustment.** A second person at the same job roughly halves its time. The rate is a tuning number per job; solo times are unchanged:
  1. **The cannon's loader.** A crewmate holding Use at a seated gunner's gun does the reload steps (`reloadSteps` 3 × 1.5 s) while the gunner keeps aiming. With a loader the gun fires about twice as often.
  2. **Two on a fire.** Two extinguishers on one car stack. This may already be the case per cell; check first.
  3. **Board-up and wrench work with two** (breach 8 s, dent, boiler 25 s) at ×1.8.
  4. **The spotter:** this is how to answer playtest 14 Oct line 9. Use #999's lamp signals (GO/STOP/BACK UP) as the fast way to put a car under the chute. The solo answer (a brake you can reach from the side, or a cab mirror) stays slower.
  5. **Not proposed:** the powder carry back. The director cut it on purpose.
- **Example encounter.** Hounds behind. Ana is in the gun seat screaming "LOAD, LOAD". Ben is working the rammer, then turns round to deal with a hot box. Ana gets one shot off in the next 6 seconds and misses. The pack is aboard.
- **Comedy/social mechanism.** It's a visible pair job, with a clear person to blame when one of them leaves.
- **Implementation.**
  - `Guns.cs` (the reload where `ReloadNeeded` drains: a second actor at `reach`).
  - `Incidents.cs` (fire).
  - `Repair` and breach code.
  - `combat.json` `guns.loaderRate`; `train.json` `repair.pairRate`.
  - Bots: `GunnerBot` gets a loader in a crew of ≥4 (one walker), the way note 377 split powder.
  - Tests: `GunTests` and `RepairTests` (two at it is faster, one is unchanged).
  - GDD §12 Weapons and Fluidity.
- **Failure risks.** The gun becomes stronger, which raises the noise: the Choir hears every shot, which is a good counter-pressure. It also adds a prediction path, so any change to a second player's reload intent goes through `NetcodeTests`.
- **Verification.**
  - Harness metric "two-hand seconds" (§8).
  - Wave outcomes with a loader vs without.
  - Human: does a loader get called for unprompted?

### R5. The census reads commitment: the minimal collision extension (medium, 3 days)

- **Current behaviour.** Covered in §7 below:
  - posts and slack only;
  - aims at the idle;
  - off at stops;
  - 7 kinds missing from `answers`;
  - `facilityLoading` means just being at a facility.
- **Social deficiency.** The director can't make "the crane operator is mid-lift and something is coming for the car the rigger stands on" happen on purpose. It happens only by chance.
- **Proposed adjustment.**
  1. A `Committed` bit per census entry from the state that already exists:
     - `Operating` (crane);
     - `Heavy` (carrying freight, a find, a body);
     - `Seated` with a reload or a target;
     - holding Use on a timed job (winch, grease, coupling, board-up, breach, Holdout);
     - the driver while at a stop with a facility module active.
  2. **Census at stops** too.
  3. **`facilityLoading`** is true only while a module is actually running.
  4. Fill the `answers` table for gannet, brakeman, hotbox, knotter, moose, towerJaw and freightBeetle.
  5. One more weight in `WhoseNext`, ×`collisionWeight` (proposed 1.5–2). It applies to a kind that would land **on or near a committed crewmate** *and* **that a free crewmate's post answers**. The free player then has to choose between their own post and the committed friend.
  6. A `pairRepeat` penalty: a pair already made tonight weighs ÷2. On seed 2, `whistler+facilityStop` came 3×.
  7. **Hard constraints.** These keep the existing rules and are never scored:
     - caps;
     - zone and sense caps;
     - telegraphs and `minReactionSeconds`;
     - boarding-first;
     - crew of 1 as it was (`minCrew` 2);
     - at crew 2, never both on one;
     - never weigh up a threat on a committed player who has **no** free crewmate within reach (then it's not a collision, it's a pile-on).
- **Example encounter.** Ana works the crane, Ben rigs the casting, Cat holds the cab, Dan rides the roof. A Freight Beetle comes for Ben's car, and Dan's post (walker) answers it. Dan has to leave the roof gun's arc. The pick favours it because Ana and Ben are committed and Dan is free. Dan arrives, the beetle shoves the casting off and onto Ben's foot, and Ana drops the next one from too high.
- **Comedy/social mechanism.** A reassignment the players make, under a clear threat. No outcome is forced; only *what* comes is chosen.
- **Implementation.**
  - `Census.cs` (`CensusEntry` + `Committed`; `PostOf`).
  - `Director.cs` (`WhoseNext` :527-549; `Completes` :231-239; the census gate :156-158).
  - `enemies.json` `director.orchestrator { collisionWeight, pairRepeat, committedNear }` and `answers`.
  - `CensusTests` + 3 facts.
  - GDD App. B.1 "The orchestrator" paragraph; `orchestrator.md` §3.
  - Coordinate with A1's #476 (variety), which works in the same pick.
- **Failure risks.**
  - A pile-on on whoever's working hardest. The "a free crewmate within reach" constraint and the caps prevent that.
  - Pressure on the busiest players can feel targeted. The weight changes only *which* kind comes, never when or how many, so pacing and caps stay the director's.
- **Verification.** Experiment E1 (§6).

### R6. Temptation you can see from the cab (small for this audit's part, 1 day after #999 merges)

- **Current behaviour.** Main: nothing tempts from the line. #999: comet fragments at 6–22% of stops, 1.2 car-loads, glow 10 m.
- **Social deficiency.** Rare and small means a crew sees one every other night and it's worth less than a car-load of freight. That isn't an argument-starter.
- **Proposed adjustment.**
  1. Once #999 is merged (K1's), tune it rather than adding:
     - **at most one fragment a night**, at a chance high enough to come most nights (≈0.6 a night on Frontier);
     - placed **40–90 m off the line, in sight of the cab's lamp**, *past* the stop's safe ground (in the village, or by the wreck yard);
     - worth **2–3 car-loads**;
     - its own hum (AU1).
  2. **The tally explains itself** (playtest 13 Oct evening line 27): "Not delivered: nothing paid. Coal 190 …".
  3. **Leave as it is:** the director's pressure ×2 for comet aboard (spec B.10). It's what makes the prize a risk after you've got it.
- **Example encounter.** "Is that green?" "Leave it." "It's worth more than the train." Two go, one stays to cover. The Gaunt is in that village. They come back with it and one fewer of them, and the dead one spends the rest of the night saying "worth it" on the dead channel.
- **Comedy/social mechanism.** A voluntary, visible, player-authored risk whose payoff the whole crew shares and whose cost one person pays.
- **Implementation.**
  - K1's `loot.json` prizes (#452).
  - `Radio.Tally` (`Radio.cs:50-69`).
  - Test: `PrizesTests` (once per night, distance band).
  - GDD §19 and App. F.6.
- **Failure risks.** One person always solo-runs it. That's fine, and a story. It could make delivery an afterthought: the fragment pays only if delivered (`Run.cs:935`), so the train still matters.
- **Verification.** Experiment E3 (§6).

### R7. Recovery for more than one: carry the injured and the hand-up (medium; K1 holds it, #457)

- **Current behaviour.** No downed state. You can carry the dead, not the living. Healing is self-use. #457 (the hand-up onto a moving car, carrying a live friend, helping up, bandaging someone else) is claimed by K1 and unbuilt.
- **Social deficiency.** Most recoveries are one person with one tool. #457 is the cheapest route to "we got him out".
- **Proposed adjustment.** No new design: **raise #457 above #463 (handcar), #464 (odd outcomes) and #465 (masks) in K1's order**, and build the hand-up first. It's what makes R3's left-behind recovery a two-person act: someone on the rear ladder hauls the runner aboard at up to the 3.5 m/s ladder limit plus their reach.
- **Example encounter.** See R3. "GIVE ME YOUR HAND" at 9 m/s with Ribbits behind.
- **Implementation, risks, verification.** K1's #457 and note 723. This audit asks only for the order to change. Ask on #999 / K1's log.

### R8. Make the shover's mark learnable (small, 0.5 day; on #999, the director's call)

- **Current behaviour on #999.** A shove that kills marks the shover for the night. Every creature weighs them ×6 and 20 m nearer, the director's next threat goes to their post, and there's no HUD marker.
- **Social deficiency.** It conflicts with "monsters remain learnable" and "no adaptive cheating / hidden rule changes".
  - It hands the victim's retaliation to the monsters instead of the victim.
  - It punishes accidents just as hard on crowded roofs at 17+ m/s.
  - ×6 for 15 minutes is effectively a sentence of death, and that ends the joke, not starts one.
- **Proposed adjustment.** Keep the shove and the attribution. Make the mark:
  1. **visible to the marked player and the crew** (a smear of soot, an icon, a line in the Wiki: "the Territory smells a murderer");
  2. **time-limited** (≈120 s, or until the victim is freed from a Holdout, since #999 says rescue doesn't clear it today);
  3. **×2, not ×6.**

  The victim's revenge should be theirs: the shove back, the bonk, the "Shoved off by Dave" in the report.
- **This is the director's explicit call** (in #999's body). This audit flags the conflict and asks; it doesn't override. Ask on #999 and in the Director's Inbox.

**Ranking.**

| Rank | Change | Benefit | Cost |
|---|---|---|---|
| 1 | R1 Incidents travel | Very high | Small |
| 2 | R2 Breathing room | High | Small |
| 3 | R3 Cliffs into cascades | High | Small |
| 4 | R4 One can, two faster | High | Small–medium |
| 5 | R5 Commitment census | High | Medium |
| 6 | R6 Temptation tuning | Medium–high | Small, after #999 |
| 7 | R7 Hand-up first | Medium | Medium (K1's) |
| 8 | R8 The mark | Protects a pillar | Small (K1's, a decision) |

---

## 6. Three experiments

Every experiment is a flag in tuning, compared on equivalent seeds with it on and off. Nothing here is tuned for incident count.

### E1. Responsibility collision: the commitment census (R5, minimal slice)

- **Expected improvement.** More moments where a free crewmate leaves their post to cover a committed one, without more deaths.
- **Baseline.**
  - `WhoseNext` aims at the slackest post; the census is off at stops.
  - In the two bot nights: 0 grabs, the gunner idle for up to 371 s, the only conflict pair repeated 3×.
- **Smallest change.** Behind `director.orchestrator.collision.on`:
  - `Committed` from `Operating | Heavy | timed Use`;
  - the census at stops;
  - `facilityLoading` = a module running;
  - ×`collisionWeight` 1.75 for kinds near a committed crewmate that a free crewmate answers;
  - the 7 missing `answers` entries.
- **Deterministic reproduction.**
  - `dt balance --tiers frontier --crews 2,4 --seeds 8 --cars 10` with the flag off, then on.
  - Plus the crane stop: `dt harness --bots 4 --as-app --enemies --route frontier:7 --seed 1..8 --cars 10`, where #470's crane stop is.
- **Short automated tests** (`CensusTests`):
  - (a) a crewmate at the crane's stand is `Committed`;
  - (b) with a committed operator and a free walker 30 m off, a kind the walker's post answers that lands near the operator weighs ×`collisionWeight`;
  - (c) with nobody free within reach it doesn't;
  - (d) a crew of one is unchanged.
- **Four-player human test.**
  - Frontier, 10 cars, 3 nights flag on and 3 off, the same seeds in swapped order.
  - Roles not assigned. Recorded with voice (consent).
- **Success criteria.**
  - Collisions (§8) per night at crew 4 up ≥1.5× vs off.
  - Reassignments (§8) up.
  - Deaths per night within +20%. Delivered rate within −10 pts.
  - In the debrief, players name at least one moment where they "had to choose".
- **Rollback criteria.**
  - Deaths more than +25%.
  - "It always goes for whoever's busy" said unprompted by two or more players.
  - The fairness check (`dt balance` "fair") fails.
  - The busiest crewmate dies more than 2× as often as the others.

### E2. Recoverable failure: thrown, not killed, and the crew told (R3.1 + R1's left-behind line)

- **Expected improvement.** A bend taken hot becomes a left-behind rescue instead of a death.
- **Baseline.** The throw kills above 4 m/s (`LinesideTests:112`).
- **Smallest change.**
  - `Lineside` throws judge by `landing.lethalAbove` behind `lineside.throwLanding.on`.
  - With R1's news: "JO OFF THE TRAIN — 200 m BEHIND" to the cab.
- **Deterministic reproduction.**
  - A harness night with a bot rider held on the roof and the bot driver taking frontier:7's first posted bend +4 m/s (`dt mcp night_insist` or a `--trace` setup).
  - Seeds 1–8, flag on and off.
- **Short automated test.** `LinesideTests`: thrown at 13 m/s lands alive and becomes LeftBehind past 150 m; at 17 m/s still dies; the throw is attributed to the driver's speed.
- **Four-player human test.** Ask nobody to take bends hot. Note every throw that happens naturally. Add one scripted night where the driver is asked to run the posted speed +5.
- **Success criteria.**
  - ≥50% of throws end with the thrown crewmate back aboard or freed from a Holdout.
  - At least one crewmate other than the driver acts in the recovery (ladder, lamp signal, cover).
  - The night's delivered rate within −10 pts.
- **Rollback criteria.** Crews jump off on purpose at 10–16 m/s as a shortcut (watch the replays), or left-behind hunts kill ≥80% of throws anyway. If they do, the recovery isn't real: fix the hunt's distance or the hand-up first.

### E3. Temptation: one comet fragment, in sight, off the line (R6; after #999 merges)

- **Expected improvement.** Detours the crew chooses, argued about, sometimes regretted.
- **Baseline.** Main has none. #999 has 6–22% of stops, 1.2 car-loads, a 10 m glow.
- **Smallest change.** Tuning on #999's prizes:
  - `perNight` 1;
  - chance 0.6 a night on Frontier;
  - `offLine` [40, 90] m in the lamp's sight;
  - worth 2.5 car-loads.
- **Deterministic reproduction.** frontier:7 seeds 1–8 with the fragment forced (test flag), crew 4 bots. Bots don't fetch prizes, so this checks only placement and that it's visible from the cab (`dt screenshot --view cab` at the stop).
- **Short automated test.** `PrizesTests`:
  - at most one a night;
  - in the band;
  - in line of sight of the headlamp at the stop's board;
  - pays only if delivered.
- **Four-player human test.**
  - 6 nights, 3 with the fragment, 3 without.
  - Note who calls it, the argument, who goes, who covers, what it cost.
- **Success criteria.**
  - The fragment is spotted unprompted on ≥2/3 of nights.
  - A detour is argued for (out loud) on ≥1/2.
  - At least one night where it's left behind on purpose.
  - Delivered rate within −15 pts.
- **Rollback criteria.** It's always fetched with no argument (too safe or too valuable), or always ignored (too far, too little). Retune the distance and worth before scrapping it.

---

## 7. The director and orchestrator: is a change needed, and how small?

**No new director, no Collision Evaluator.** Everything the proposed Collision Score asks for can go into the pick that exists, as one weight and one penalty, with the hard rules left as gates:

| Score term | Where it goes | Built? |
|---|---|---|
| **C** conflicting responsibility | `collisionWeight` in `WhoseNext` (R5), plus the conflict table's `pairWeight` as now | New weight |
| **I** information divided | **Not a runtime score.** It's a property of the creature (the Gaunt, the Passenger, the Soot Children) and of R1's alert ("who and where, not what"). Keep it in the creature reviews (§20's social test) | Design-time |
| **T** temptation | **Not the director's.** The director must never place bait; temptation comes from the level and loot (R6). The director already answers what the crew took (cargo pressure, comet ×2) | Already in pressure |
| **R** recovery and cooperation | Hard rule, not a bonus: "a free crewmate within reach" (R5's constraint) | New gate |
| **V** visibility | R1 for events. As a pick weight, "a free crewmate within 26 m of where it lands" is cheap but gameable by how players stand. Not proposed for now | No |
| **P** repetition | `pairRepeat` ÷2, beside the existing variety halving (Director.cs:484) | New penalty |

The weights 3C+2I+2T+2R+V−3P make a good **offline rubric** for reviewing the conflict table and coding playtest moments. They make a poor runtime scorer: I, T and V can't be measured reliably in the sim, and a summed score would hide the fairness rules inside a number. Keep fairness, reachability, telegraphs and caps as gates before any weighting, as `Decide` already does (Director.cs:337-406).

**The minimal extension, in order:**
1. Census: `Committed`, and census at stops (`Census.cs`).
2. The `answers` table completed (`enemies.json:1024`).
3. `facilityLoading` means loading (`Director.cs:231`).
4. `collisionWeight` and `pairRepeat` in `WhoseNext` (`Director.cs:527`).
5. Aftermath and the facility lull wired up (R2), so the director knows about crises.

Things to fix while there:
- The comment at `enemies.json:992-994` and orchestrator.md P8 still say solo is "never more than 1". Since note 652 (`longTrainCars` 8), a solo player on an 8-car train can have two threats.
- `LiveCrewTests` only pins 4-car nights.

---

## 8. Measuring entertainment

**Rule:** the code counts *situations*. Only people can tell us whether those situations were fun. Voice activity is not communication, and incident count is not comedy.

### From code (harness, `dt balance`, and `dt replay` of recorded human nights)

All of these hang off `Attribution`, the census and `World.Beats`, computed host-side, deterministic, and replayable from a `.dtrec`. So they apply to **real human nights** recorded by a dev build, not only to bots. Each is a field in `HarnessReport` and a `trends` metric.

| Metric | Definition | Hook |
|---|---|---|
| **Dependency events / two-hand seconds** | Seconds where two crewmates work the same job (winch, steam lift, heavy crate, R4's loader and repairs, a grab pull) | Facility modules, `Guns`, repair code |
| **Reassignments** | A crewmate leaves their post (census `Post` change) within 10 s of a threat engaging *another* crewmate, and moves toward them | Census + `Attribution` |
| **Rescue diversions** | A crewmate >26 m from a held friend at grab start closes ≥10 m on them during the hold | `Attribution.Grab` + positions |
| **Recovery attempts / successes** | Grab → Rescue (exists), Holdout freed (exists), parted cars re-coupled (bots count it today, note 653), left-behind back aboard, a car's fire out before it's lost | `Attribution`, `HoldoutReport`, `PartedCars` |
| **Cargo under pressure** | Car-loads cut loose, or finds dropped, while ≥1 threat is engaged; vs saved (fire out, car mended while engaged) | `Attribution.CarLost`, `RunLoot` |
| **Collisions by type** | A threat engaged within 20 m of a committed crewmate while ≥1 crewmate is free, keyed by (kind, the committed job) | Census (R5) |
| **Concurrent unresolved incidents** | Count over time of open {grabs, fires, hot boxes, loose pins, failing cars, left-behinds}. Report its distribution and the seconds at ≥3 | `World` state |
| **Aftermath time** | Seconds from a major event (death, car lost, rescue, Holdout freed) to the next telegraph | `Attribution` + `Beats` |
| **Inactivity** | The census slack, extended to stops; seconds a crewmate is `IdleSeconds` > 20 | `Census`, `World.IdleSeconds` |
| **Detours** | A crewmate >20 m off the train for a find/prize; what it cost (time, health, death); delivered or not | `RunLoot`, positions |

**First targets, hypotheses not quotas.** At crew 4, Frontier, 10 cars:

| Metric | Target |
|---|---|
| Collisions | 2–4 a night |
| Rescue diversions | ≥1 a night |
| Aftermath time | ≥20 s after 80% of major events |
| Concurrent unresolved incidents | ≥3 for under 10% of the night |
| Slack | Never >150 s for more than one crewmate at once (orchestrator P1) |
| Two-hand seconds | ≥90 s a night |

Revisit them after the first human sessions.

### From people only

Record each session with consent: the game's own `.dtrec` with voice (`NightRecorder.Voice`), plus screen and Discord audio. Code these:

- **Unprompted useful communication:** a call that changes another player's action within 10 s ("hounds left!" → someone turns). Hand-coded from the recording, timestamped against the replay. Voice-activity seconds are recorded but **not** used as a score.
- **Shared laughter:** two or more laughing within 3 s, noted with what came just before (the replay tick).
- **Arguments over decisions:** a disagreement about what to do (leave him, go for the green thing, cut the car).
- **Retelling:** in the debrief, unprompted ("did you see when …"). Count them, and note which of the night's incidents were retold.
- **One more run:** asked at the end with no pressure. Did they start another, or ask to?
- **Bad moments:** "that was unfair", "I didn't know what killed me", boredom ("what do I do now?").

Comparison happens only between flag on and flag off on equivalent seeds and crews. Never between a bot number and a human feeling.

---

## 9. Pillars check

| Pillar | R1 | R2 | R3 | R4 | R5 | R6 | R8 |
|---|---|---|---|---|---|---|---|
| The train is the game | ✓ alerts locate by car | ✓ | ✓ the train backs up | ✓ train jobs | ✓ | ✓ the prize pays only if the train delivers | — |
| Roles are emergent | ✓ | ✓ | ✓ | ✓ whoever's there loads | ✓ reads what you're doing, assigns nothing | ✓ | — |
| Solo remains viable | ✓ nothing to alert | ✓ | ✓ solo struggle untouched | ✓ solo times unchanged | ✓ `minCrew` 2 | ✓ | ✓ |
| Monsters remain learnable | ✓ | ✓ | ✓ creature rules unchanged | ✓ | ✓ only *which* kind, no rule changes | ✓ | **restores it** |
| Voice matters | ✓ who/where shown, *what* must be said | ✓ | ✓ | ✓ "LOAD!" | ✓ | ✓ the argument | — |
| The middle is vulnerable | ✓ | ✓ | ✓ | ✓ | ✓ caps unchanged | ✓ | — |
| Mistakes make stories | ✓ | ✓ | **✓ core** | ✓ | ✓ | ✓ | ✓ |
| Funny without forcing jokes | ✓ | ✓ room to laugh | ✓ | ✓ | ✓ | ✓ | ✓ |
| Failure is legible | **✓ core** | ✓ | ✓ the report still names the cause | ✓ | ✓ | ✓ the tally explains | ✓ |
| The director keeps pacing and fairness | ✓ | ✓ it's the director's | ✓ | ✓ | ✓ weights only; gates and caps untouched | ✓ | ✓ |

---

## 10. One-week plan

These agents work alongside others. Claim queue and note numbers per COORDINATION, and don't touch other agents' items:
- **K1:** #999; #450, #457, #459.
- **A1:** #1001; #473–#477, especially #476 (variety, the same pick as R5) and #475 (the relief driver).
- **G1:** creature windows.
- **AU1:** sounds.

| Day | Work | Long runs |
|---|---|---|
| **1** | Baseline. The §8 counters in `HarnessReport`: reassignments, rescue diversions, concurrent incidents, aftermath time, two-hand seconds, collisions (needs `Committed`; start with `Operating`/`Heavy`). Read-only to gameplay, a test per counter. The baseline: `dt balance` frontier crews 2 and 4, seeds 1–8, 10 cars. **Ask the director for one 4-human session on the current Test build, recorded with voice: the first real baseline.** | One `dt balance` grid (≈15 min on 4 cores) |
| **2** | R1 (news lines from `Attribution`; `radioBreakOnGrab` 0; GDD C.8, Q2 amendment) and R2 (facility lull, aftermath, busy floor). Both small and flagged. Focused tests only (`NewsTests`, `PacingTests`) | None |
| **3** | R3 (bend throw by landing; the struggle by distance) and R4's loader (the gun only; repairs if time allows). `LinesideTests`, `DemoRosterTests`, `GunTests`, `NetcodeTests` for the loader's intent | `NetcodeTests` once |
| **4** | E1's slice of R5: `Committed`, census at stops, `facilityLoading` live, `collisionWeight`, `pairRepeat`, the `answers` gaps. `CensusTests`. Then the on/off comparison of each flag on seeds 1–8 at crews 2/4 (the day's one expensive run). Tune `collisionWeight` and `aftermathSeconds` against §8's targets, not against incident count | One balance grid per flag set, run once |
| **5** | A Test build with every flag (`tonight` picks up green PRs). The four-human protocol: 6 nights, flags on/off on swapped seed order, recorded; the debrief questions from §8; the code metrics from their `.dtrec`s. Review: keep, retune or roll back each flag; the next decision (R6 once #999 merges, R7's order with K1) | The human session; `dt replay` metrics on its recordings |

**Realism.** R5 may take two days, not one. If so, E1 moves to the following week and day 4 runs only the R1–R4 comparison. Nothing here needs the big cab or new content. **No result from bots is reported as a social result.** Bots validate that the flags work, are fair and don't break pacing; only day 5 says whether they're fun.

---

## 11. Proposed GDD amendments (none applied)

1. **App. C.8, "Radio broadcast of a GRAB":** add "A grab never breaks the victim's set (`radioBreakOnGrab` 0): the broadcast is the point."
2. **Open question 2 / D.6 ("roll call is verbal")**, add: "Roll call stays verbal: nothing shows who is aboard. **An emergency is shown, not a roster.** A crewmate held, or killed, out of voice reach is a line for the living: who and where, never what (R1)." This sits alongside App. F.6's aboard lamps (#459) and A1's #474.
3. **§23, the outright kills:** "Three things" → list them as they are. "Thrown from a roof on a bend is a landing (§23 jump rule): above 16.5 m/s it kills; below, you're left behind."
4. **App. C.1, the lone struggle:** "A held player with no living crewmate within 26 m can struggle (one try; `soloStruggleChance` alone, `crewStruggleChance` in a crew)."
5. **§12 Weapons and Fluidity:** "One can do any job; two at the same job do it faster: a loader at a seated gunner's gun does the reload; two at a fire, a breach or a dent work at `pairRate`."
6. **App. B.1 Pacing rules:** add **Aftermath**: "After a death, a car lost, a rescue or a fire put out, the director adds no quiet pressure for `aftermathSeconds`, and the cooldown isn't pressed through." Note that the facility lull is now implemented as written.
7. **App. B.1 The orchestrator:** "It reads what each crewmate is committed to (the crane, a carry, a timed job) as well as where they are, at stops as well as on the run. Among what may come, it favours a threat on a committed crewmate that a free one answers, never one with nobody free in reach (`collisionWeight`); a pair it has already made weighs less."
8. **App. F.6, the shove's mark** (#999), the director's decision to confirm: visible, time-limited, ×2.
9. **§20 social test 3:** record the Knotter, the Lodger and Dave either as deliberate exceptions, with a reason, or with windows (G1).

---

## 12. What not to build

- **A second director, or a runtime Collision Score.** One weight, one penalty and the hard gates do it (§7).
- **More creatures.** There are 28–30. Variety is about the roster and the pick (A1's #476, R5's `pairRepeat`), not the count.
- **More single-player minigames on the run**: the handcar (#463), odd outcomes (#464), masks (#465), the mail. Each is fine, and each is later than R1–R5 and #457.
- **The side brake alone** (playtest 14 Oct line 9). Build it only beside the spotter path (#999's lamp signals), or it removes a natural two-person job before it exists.
- **More "two required" gates.** They break solo. Use "two faster".
- **Tuning the social game on bot nights.** Bots are mute (`CrewVoice.cs`) and don't cover (#474, #475). Use them for fairness and pacing; use people for fun.
- **Raising incident counts to hit P3.** More engaged time isn't more entertainment. The aftermath exists to make *less* happen right after something big.
- **Information for the dead.** Already cut by the director, and R1 never shows the dead anything.

---

## 13. The one change to make first

**R1: make a grab travel the length of the train.**

- Every social system we have runs through the GRAB: the rescue window, the victim talking, the voice cut, the report's blame, the dead watching.
- Today it reaches only the people who happen to be standing within two cars, and sometimes it breaks the one radio meant to carry it.
- The fix is a news line from data `Attribution` already has, plus one tuning value. It's about two days of work, it can't break solo, and it keeps voice at the centre (the line says who and where; a person has to say what).
- It's also the change that makes every other one count. A collision, a recoverable failure or a temptation only becomes a story if the rest of the crew are in it.

**Then put four humans on the build, recorded. That's the step this project has never taken.**
