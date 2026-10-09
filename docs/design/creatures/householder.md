# THE HOUSEHOLDER — pay for what you take

*Creature design, G1 (enemy design), 9 Oct 2026. **Status: the director's pick (9 Oct 2026: "The householder is very
good"), from G1's house-creature proposals (queue #317, ARCHITECTURE §8 note 584).** Calls left open are marked
**(G1's call)**.*

---

## 1. What it is

A gaunt figure sitting at a set table in a house that looks lived-in among the ransacked ones: a laid table, a lit stove.
It doesn't attack guests. It watches you, head turning to follow you round its house. Everything in its house is its own.
Leave something of yours on its table and you can take one of its things. Take one without paying and walk out of the
door with it, and it gets up and comes after whoever's carrying it, slow and relentless, all the way to the train if it has
to. Drop the thing and it picks it up, takes it home and sits down again.

## 2. Roster entry (GDD §21, CORRUPTED HUMANS)

**THE HOUSEHOLDER** · *at its table, in the one house that looks lived in*
A gaunt figure in rotting Sunday clothes, sat at a set table by a lit stove, watching you.
> **RULE: pay for what you take.**

**Sense:** sight. **Zone:** outside (the villages). **Want:** Cargo. **Cost:** none (the house's own).

## 3. What it looks like (art brief)

Tall and thin, in rotting old-fashioned Sunday clothes (a waistcoat, a collarless shirt, sleeves too short); grey, dry skin; a
face too still, the eyes always on you; too-long fingers resting on the table. Its house: a table laid for one (plate,
cutlery, cup, candle), its chair, a lit iron stove, a warm light. Clips: `sit`, `watch`, `rise`, `hunt` (a stiff,
relentless walk), `grab`, `return`, `hit`, `death`.

## 4. Behaviour tree (App. A.4 format)

### THE HOUSEHOLDER · sight
```
SIT       at its table; everything lying in its house is its own
WATCH     a guest in its house → its head follows them; it never touches a guest
PAID      something not its own left on its table (within 1 m) → one of its things is paid for
RISE      one of its things carried out of the door, unpaid → it rises (1.5 s, the telegraph) for whoever carries it
HUNT      after the carrier at 3.4 m/s, to the train if it must
          ├ within 1 m of them → it has them (a grab of 6 s a friend breaks by holding Use beside them)
          ├ the thing put down → it takes it, carries it home, and sits again
          └ the thing taken more than 200 m from its house → it gives up and goes home
HIT       blows hurt it (6 kill it)
```

## 5. First-pass numbers (`enemies.json` `householder`)

| Field | Value |
|---|---|
| `tableReach` | 1.0 m |
| `riseSeconds` | 1.5 s (the spine's least telegraph) |
| `hunt` / `walk` | 3.4 / 1.4 m/s (slower than a walk-run: you can get away, but it keeps coming) |
| `grabReach` / `grabSeconds` | 1.0 m / 6 s |
| `giveUpBeyond` | 200 m |
| `health` | 6 |

## 6. What the harness verifies

`HouseCreatureTests`: guests watched, never touched; one of its things carried out unpaid and it hunts the carrier (and,
left to it, kills); something left on its table pays for one, and it lets it go.

## 7. Decisions (G1's calls, for the director)

1. **Anything counts as payment**: any body that isn't its own, left on its table (a tool, a toy, a crate).
2. **One payment, one thing.**
3. **Only in a house with something to find** (so there's always something to pay for).
