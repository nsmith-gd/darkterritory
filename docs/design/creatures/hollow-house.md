# THE HOLLOW HOUSE — know the house you're in

*Creature design, G1 (enemy design), 9 Oct 2026. **Status: the director's pick (9 Oct 2026), revised by the director, from
G1's house-creature proposals (queue #318, ARCHITECTURE §8 note 585).***

> **The director, 9 Oct 2026:** "Don't give players 40 seconds because the houses are actually quite small. Just start
> maybe rumbling the ground underneath as a tell, and then start quickly closing all the doors. And any players who are
> trapped inside, like a Venus flytrap, that grumbling should be the house starting to sink. And then it should collapse
> down on the player. It would make for a good funny moment too, because the collapse would be a good mic cut moment for
> any players who are on voice chat. The way to counter it really should just be awareness of the house and awareness of
> the surroundings."

---

## 1. What it is

The house itself is the creature: a Venus flytrap. Step inside and, a moment later, the ground starts rumbling under it.
That's the tell. If anyone's still inside when the rumble ends, the doors slam shut one after another, fast, and the house
sinks into the ground and comes down on whoever's in it. Out in time, and it goes still again, waiting.

## 2. Roster entry (GDD §21, OUTSIDE)

**THE HOLLOW HOUSE** · *one of the village's houses*
A house like the others, that rumbles when you're in it.
> **RULE: know the house you're in.**

**Sense:** vibration (feet on its floor). **Zone:** outside (the villages). **Want:** Kill. **Cost:** none.

## 3. Behaviour tree (App. A.4 format)

### THE HOLLOW HOUSE · vibration
```
STILL     waiting
RUMBLE    someone inside 1.5 s → the ground rumbles under it for 3 s (the telegraph)
          └ nobody inside when it ends → still again
SHUT      its doors slam shut one every 0.35 s, and are held shut
SINK      it sinks 2.4 m over 3 s
COLLAPSE  it comes down on whoever's still inside (a crush: a structure coming down kills, as a fall does)
SETTLED   it stays as it fell, sunk and shut
```

## 4. How it sounds and looks

- **The rumble** (the tell): a low groan through the floor, grit falling from the ceiling, the house shaking.
- **Shut:** doors slamming one after another. **Sink:** timber screaming, earth heaving round its base.
- **Collapse:** the roof and walls coming down, and every voice inside cut off.

## 5. First-pass numbers (`enemies.json` `hollowHouse`)

| Field | Value |
|---|---|
| `waitSeconds` / `rumbleSeconds` | 1.5 s / 3 s |
| `shutEvery` | 0.35 s |
| `sinkSeconds` / `sinkDepth` | 3 s / 2.4 m |

## 6. What the harness verifies

`HouseCreatureTests`: someone still inside when the rumble ends and the doors shut, it sinks and comes down on them, and
it stays as it fell; out before the rumble ends and it's still again, its door open.

## 7. Decisions (G1's calls, for the director)

1. **The kill is a crush, not a grab**: the house holds everyone inside at once, so it kills as a falling structure does.
2. **Its doors are the houses' own** (note 401); its windows aren't shut yet (the open houses' windows are openings in the
   art, not the sim). Asked of the art.
