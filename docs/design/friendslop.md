# Friendslop: who the game is for, and closing the gap

*K1, 14 Oct 2026, for the director; the living copy is the doc "Dark Territory — Bartle × RGD Audit (Friendslop)"
(claude.ai/code/artifact/aaa2d03b-5686-44ae-8fbe-87d4a6735cc6), its "Closing the gap" tab. ARCHITECTURE §8 note 733,
queue #467.* The director: "We are chasing the friendslop genre" (Lethal Company, R.E.P.O., Content Warning, PEAK).

## 1. The audit, in short

The build at main 0f47f04 was read against Bartle's eight player types and the Rational Game Design chain (skills,
inputs, mechanics, signs, feedback, progression).

| Subtype | Classic type | Genre target (0–5) | The build | What the build gives it |
|---|---|---|---|---|
| Friends | Socialiser, implicit | 5 | 3.5 | real proximity voice, the voice cut at death, the safe yard, two-person jobs |
| Griefers | Killer, implicit | 4 | 2 | only costly mischief (a car cut loose, wrenches thrown off); crewmates don't even collide |
| Hackers | Explorer, implicit | 4 | 3 | ragdolls and throwing, the fire grid, kegs; most actions a held prompt, not physics |
| Opportunists | Achiever, implicit | 4 | 3 | held searches, the child, one trophy; comet cargo loaded nowhere |
| Planners | Achiever, explicit | 2 | 4 | 20 cars, 17 upgrades, four tiers, the campaign |
| Scientists | Explorer, explicit | 2 | 4.5 | 28 creatures with fixed, learnable rules |
| Politicians | Killer, explicit | 2 | 3 | the incident report names a person; the dead vote |
| Networkers | Socialiser, explicit | 1 | 1.5 | Steam lobbies; no clips a player keeps |

The profile is inverted: the genre runs on the implicit types, and the build serves the explicit ones best. Its best
mechanics already fit the genre and all test Social Coordination: the rescue window, voice as an input (the Choir, the
Gaunt, the Passenger), team kills, the dead kept in the call, the opera derailment. What drags: a night spent at
one-player stations (driving, firing, upkeep), 28 creatures with no curriculum, mischief only in run-ending sizes, failures
with no visible cause, and nothing yet played by a real group.

**The goal (the director, 14 Oct):** "It's extremely important that friends exceeds our target meaningfully." On the
audit's scale 5 is parity with the genre's best, so Friends aims at 6: what single-space games can't do, because this one
runs a home that moves. Griefers, Hackers and Opportunists go to target.

## 2. The director's calls (14 Oct 2026)

- **The cab is the crew car.** "The cab itself becomes the crew car. It becomes the place where players hang out together.
  It becomes the place where coordinated driving happens. It becomes, in essence, the captain's quarters, shared by all."
  The boiler gives up the length: "The boiler is just a long tube ... it can still feel that way without having an
  accurate long boiler." The controls stay as they are: "we are going to have this game playable by a single player". The
  cab map moves behind the controls onto a table, "as long as it's tracking where the train still is and giving our
  route". The gallery's raised benches are "a good spot ... more of a social space". "The back wall should definitely have
  some fun, cute gimmicks back there as well."
- **The shove, in full effect, as a trial:** "if you're standing on top of a car, you can be shoved off of it. And at
  speed, it kills you."
- **Coal thrown** has to "work with the current coal shoveling system".
- **Cut:** the toy slipped into a friend's pocket; a price on every find ("I want players to eventually understand that
  certain objects are more valuable than others ... as they play the game more often, they learn what those objects
  are"); the dead's haunt ("I don't want dead people seeing things that other people don't, and then helping them play
  the game"). In the haunt's place: "sound-only cues in the design of the environment".
- **Singing does not fight the Choir.**
- **Comet cargo** "needs to be at actual stops and needs to be extra valuable loot that players get excited for when they
  see". Team-killable creatures "can sometimes leave trophies". "Very big value things should take two to carry."
- K1 is authorised to build all of it and to merge as it goes green.
- **The mail, cut for now** (15 Oct 2026): "Im soft on the mail. I dont like it so far. Lets cut for now." No mail cranes
  on the line (#461's catch and F1's note 673 with it); kept in the code behind sight.json `mailCranes`, so it can come
  back with one switch (queue #530, ARCHITECTURE §8 note 803).

## 3. The work (queue #450–#467)

| # | Item | Note |
|---|---|---|
| 450 | **The big cab, the crew car.** The cab 11 m of the engine's 20 m (was 5), the boiler about 8 m (was about 14). Front to back: the footplate exactly as it is (one person still drives it all; the bots don't move); the chart table, the cab map on a lit table tracking the train and its route, one lamp per crewmate aboard; the gallery, a raised floor with benches facing forward so everyone sees the line over the driver, long side windows; the back wall, warm off the backhead, with its gimmicks (a kettle that sings as the boiler nears its redline, a cuckoo clock for the last minutes before dawn, the crew's photos, the trophy shelf, a hook for each crewmate's cap) and a shelf left for U1's gramophone | 716 |
| 451 | **Horseplay.** Crewmates collide; a shove staggers, and on a roof can put you off (at speed, §23's 16.5 m/s kills, and the report names the shover); the shovelful from the bunker thrown instead of fired (a friend sooted and staggered, the fire a handful short); a tool's bonk staggers a crewmate with no damage | 717 |
| 452 | **The night's prizes.** Comet fragments at real stops, glowing so a crew sees them from the line, worth far more than anything; value learned (each kind of find always worth the same, the tally naming the night's best finds); the finder's line in the tally; team kills sometimes leave a trophy | 718 |
| 453 | **Big finds.** Two-person jackpots (a safe, a church bell, a ship's figurehead); fragile valuables that lose value knocked or dropped; one jackpot house a stop; loose loot that slides and topples in the cars | 719 |
| 454 | **Voices.** The speaking tube from the cab to the guard van; the whistle codes on a card in the cab; shanties (two or more singing in time calm the livestock and talk the Gaunt down faster, and never feed the Choir); lamp signals the driver reads | 720 |
| 455 | **Crew memory.** The crew photo at the gate on a delivered night; the logbook, a page a night, kept after the campaign; the fortress's board of lost crews | 721 |
| 456 | **Emotes for two, and more** (a high five, a fist bump, a dance-off, sitting on a roof edge, leaning out of a door); the wheel widened (U1's bow, salute and conga use the new slots) | 722 |
| 457 | **Bringing them home.** The hand-up onto a moving car; carrying a live friend; helping a knocked-down friend up; a bandage put on someone else heals half again as much | 723 |
| 458 | **Sessions.** The crew's save travels with any member who hosts; a friend joining takes a bot's seat; kick and mute in public lobbies | 724 |
| 459 | **Cuts you can get back** (a car cut at a stand stays put; one cut at speed can be caught by backing up within the 400 m) and the chart table's lamp per crewmate aboard | 725 |
| 460 | **Hoist a friend** on the crane's hook (the drop a gag, at most 10 health by its height and never a death: the director, 15 Oct 2026, queue #529, note 802) | 726, 802 |
| 461 | **The mail catch:** the bag snagged from the car door as the train passes a mail crane. *Cut for now (the director, 15 Oct 2026; queue #530): no mail cranes on the line, kept behind sight.json `mailCranes`* | 727, 803 |
| 462 | **The sense table** (every item gives off or masks a sense the creatures answer: a thrown music box lures what hunts by sound, the extinguisher's cold drives the hounds back, a lamp shows what waits in a dark house) **and thrown things do what they are** (a lamp breaks into fire, a keg rolls and blows) | 728 |
| 463 | **The handcar:** a two-person pump trolley on a flatcar, for a branch, a friend left behind, or a race | 729 |
| 464 | **Odd outcomes to find,** about ten a tier, never in the Wiki | 730 |
| 465 | **Painted masks:** a player's colours and stencil, and a crew sigil on the tender | 731 |
| 466 | **Sound cues in the world** for the living: boards that creak over what waits, a coupling that ticks before it goes, a far bell from a stop that's been hit | 732 |
| 467 | This page, and App. F's entry | 733 |

**Rules for all of it.** No damage between crewmates from tools or guns: staggers, soot and knocks, and deaths only
through the world's own rules (falls, fire, creatures), so the report can name a cause. Mischief is on by default; a
public lobby gets kick and mute (#458). Everything that moves a player or the train goes through intent to the host's
deterministic sim, its numbers in `content/tuning`, with a test or a `dt` command.

**Shared with U1's catalogue (#967):** the gramophone and the yard cat live on the big cab's back wall; the bow, salute and
conga use #456's slots; the yard photographer uses #455's shot; the speaking-tubes upgrade extends #454's Tube voice path.
