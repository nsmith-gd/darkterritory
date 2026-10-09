# THE HANGER — don't touch the strands

*Creature design, G1 (enemy design), 9 Oct 2026. **Status: the director's pick (9 Oct 2026: "Let's just have it, if you
touch a strand that's hanging, it grabs you. It's a simple positional awareness"), from G1's house-creature proposals
(queue #319, ARCHITECTURE §8 note 586).***

---

## 1. What it is

Something that lives in a house's roof space. Its strands hang from the ceiling to about chest height, thin and glistening,
easy to miss in the dark. Walk into one and it has you: it hauls you up the strand until your feet leave the floor. A
friend holding Use beside you prises you off, or a blow on it makes it let go, and it draws back into the roof a while.

## 2. Roster entry (GDD §21, OUTSIDE)

**THE HANGER** · *in a village house's roof*
Strands hanging from the ceiling, glistening in the lamp.
> **RULE: don't touch the strands.**

**Sense:** movement (a touch on a strand). **Zone:** outside (the villages). **Want:** Kill. **Cost:** none.

## 3. What it looks like (art brief)

A pale, bloated, spider-like thing clinging upside-down to the rafters: a soft sac of a body, many thin jointed limbs braced
against the beams, a mouth underneath paying out the strands. The strands: thin, glistening, slightly swaying threads with a
bead at the end. Clips: `hidden`, `grab`, `haul`, `hold`, `hit`, `retreat`, `death`.

## 4. Behaviour tree (App. A.4 format)

### THE HANGER · movement
```
HIDDEN    up in the roof; 6 strands hang from the ceiling (2.6 m) to 0.9 m off the floor, spread over the house
TELEGRAPH anyone in its house → its strands quiver (the telegraph, the spine's least before it can take anyone)
GRAB      someone walks into a strand (within 0.35 m of it) → it has them (a grab of 10 s)
HAUL      up the strand 0.7 m over 2.5 s (feet off the floor, head against the ceiling), and held there
          └ a friend holding Use beside them, or a blow on it → it lets go
RETREAT   back into the roof for 25 s, its strands slack
```

## 5. First-pass numbers (`enemies.json` `hanger`)

| Field | Value |
|---|---|
| `strands` / `touchWithin` | 6 / 0.35 m |
| `haulSeconds` / `haulTo` | 2.5 s / 0.7 m |
| `grabSeconds` / `retreatSeconds` | 10 s / 25 s |
| `health` | 3 |

## 6. What the harness verifies

`HouseCreatureTests`: keep clear of the strands and nothing happens; walk into one and it has you up it, until a friend
prises you free; left on the strand, you die.

## 7. Decisions (G1's calls, for the director)

1. **Where the strands hang** comes from the house and the Hanger (so every machine agrees, and nothing's sent).
2. **A crew of one** struggles free (the solo rule, as every grab).
