# THE WITNESS — the one only you can see

*Creature proposal, Sloe (queue #709, ARCHITECTURE §8 note 1004), 23 Oct 2026, for the director's rule "every creature's purpose is to foil some sort of behavior" (`docs/playtest/2026-10-23.md` line 36). **Status: a proposal for the director; nothing is built until the director approves it.** Numbers are a first pass in the roster's units.* It takes the director's other delivery for the Bellman (line 36: "having something that only that player can see"),
for the quiet player.

> **Say what you see.**

## 1. What it is

A figure in a long coat that only one crewmate can see: the one who hasn't said a word in a while. It stands where they'll
see it, a little closer each time they look away. Nobody else sees it, hears it or can hit it, until the one who sees it
says so out loud. Spoken of, it shows.

## 2. Roster entry (GDD §21, SPECTRAL)

**THE WITNESS** · *wherever the quiet one looks*
A figure in a long coat, closer each time.
> **RULE: say what you see.**

**Sense:** silence (a crewmate's voice level). **Zone:** anywhere, aboard or afoot. **Want:** Kill. **Cost:** 1.

## 3. Silhouette and look

A man-shaped dark in a long coat and a hat, its face never lit; always at the edge of the lamp's reach. Drawn only on the
marked crewmate's screen (client-side, the way the Passenger's tell is theirs). Clips: `stand`, `step` (between looks),
`shown` (everyone sees it: a flicker, then solid), `hit`, `gone` (dissolving), `take`.

## 4. The tell (spec A.3/A.4)

- **To the marked player only** (tier 1): their own heartbeat, 60–120 Hz, rising as it closes, and a breath at their ear
  (1–2 kHz) when it's within 5 m.
- **To the crew:** nothing; that's the point. The marked player's *silence* is the tell the crew can learn ("Sam's gone
  quiet").

## 5. The rule (App. A.4 format)

```
MARK      a crewmate quiet (voice.quiet: under 5% of the last 180 s talking, with a living crewmate in earshot or on the
          radio), and not exempt (no microphone; a solo game) → it appears to them, 25 m off
STEP      each time they look away it steps closer (3 m, 2 m late)
          └ TELEGRAPH: the heartbeat; the breath within 5 m
          ├ they speak (voice above TalkingAbove for 1 s) while it's in their view → it's SHOWN to everyone for 6 s:
          │ 2 blows or a gun round and it's gone for the night
          ├ they keep talking (voice.quiet clears) → it fades back 10 m each time they speak
          └ else
TAKE      within 1 m → GRAB: held, voiceless (their mic muted in the game for 10 s), a friend's blow frees, but only
          when shown, so the friend has to have been told
PUNISH    held 10 s: dead
```

## 6. Aboard or afoot; what it counters

**Both.** It foils **quiet on the mic**. Its counter is the behaviour wanted: talk. It never takes a player who has no
microphone (the hello says so: see note 1004's "the mic behaviours"), and never in a solo game.

## 7. Compositions

- **The Witness + the Choir:** the Choir punishes noise; the Witness punishes silence. Say it, quietly, in time.
- **The Witness + the Hark:** the quiet one is pushed to talk, the loud one to stop; a crew that talks in turns is safe
  from both.
- **The Witness + the Passenger:** "make everyone speak" twice over.

## 8. Early versus late

- **Early:** from Frontier; 180 s of quiet; 3 m a look.
- **Late:** 120 s; it can come to two quiet players at once (each sees their own).

## 9. The social test (§20)

| # | Test | Passes? |
|---|---|---|
| 1 | Describable in one phrase | ✔ "The man only the quiet one can see." |
| 2 | Answered better by two | ✔ Only a friend can hit it, once told. |
| 3 | Consequence now, death later | ✔ The heartbeat now; a step at a time. |
| 4 | A verb, not just a "don't" | ✔ Say it, then hit it. |
| 5 | Someone gets blamed | ✔ "You saw it for a minute and didn't SAY?" |
| 6 | Fun to scream | ✔ "THERE'S A MAN BY THE TANK, HIT HIM, HIT HIM!" |

## 10. Build cost

**Medium.** A per-client visible flag (drawn for one player; the sim knows who), the host's rolling talk share
(`PlayerIntent.Voice`, already on every intent), the "no mic" bit in the hello (a protocol bump, rule 10), the in-game mute
(the Choir's voice path). About 2 agent-days of sim, net and bots (bots speak when marked), 1 of art, the heartbeat bed.
