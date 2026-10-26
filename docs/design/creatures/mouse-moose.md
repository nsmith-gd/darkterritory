# The Moose Mouse

*Renamed by the director on the Look Review, 23 Oct 2026: "rename them Moose Mouse" (note 1015). The quote below keeps its
first name; the code keeps `MouseMoose`, `mouseMoose` and `mouse_moose` (the wire, the recordings and the Tweaker are keyed by
them).*

*The director, 15 Oct 2026 (A1's chat); built by A1, queue #515, ARCHITECTURE §8 note 790. GDD §21.*

> "Okay, I'm having a creature idea about a creature that wanders yards stealing loot. It does so very slowly, and once it
> gets out of position so that the crane can't reach it, then you can't get it as a player. So it's sort of like when you
> get to a yard, sometimes they'll show up and they'll just start dragging loot away. And it's sort of a management task of
> trying to keep awareness of all of the loot around you and then trying to drive them off. If you get within, let's say 6
> meters of the creature, then it runs away and it will run away and hide for a bit before it comes back to try and steal
> the same piece of loot again. So when you know what piece of loot it's targeting, you can start to guard that piece of
> loot. Once you scare it away 5 times, it will target a new piece of loot. This creature is called the Mouse Moose. It's a
> mutant rat with little tiny moose antlers."

## 1. What it is

A mutant rat the size of a dog, mangy and hunched, with a pink rope of a tail and a little pair of palmate moose antlers on
its head. It squeaks. It never hurts anyone, and nothing kills it.

**RULE: keep an eye on the castings, and stand by the one it wants.**

## 2. What it does

- **Where:** at a yard where a crane still has castings to load (the facility's own gantry, or a yard crane's). The director
  sends it (`director.costs` 2, `nightChance` 0.5, by tier), one at a stop, once the train stands there (after the facility
  lull, App. B.1).
- **The loot it takes:** a crane's castings, the yard's loot that only the crane can load. The director's "once it gets out
  of position so that the crane can't reach it, then you can't get it as a player" is exactly the crane's reach. A rigger
  hooks a casting only at the hook, and the hook can't be put further out than the gantry's span. So a casting dragged out
  past that is lost to the crew.
- **Coming:** out of its hole, 22 m out past the castings from the track (`holeOut`). It creeps in at 1.2 m/s.
- **Taking:** it gets its teeth into the casting on its hole's side for 1.5 s (`biteSeconds`, the reaction window). Then it
  drags it back towards the hole at 0.15 m/s ("very slowly").
  - About 17 s of that takes a stacked casting (7.5 m out) past the hook's reach. The casting is then **taken** (a casting
    state of its own): nobody can rig it, the bots don't go for it, and the crane has one fewer to load.
  - It drags on at 0.5 m/s to its hole, where the casting is gone. It hides there, then comes for another.
- **Driving it off:** a crewmate within 6 m of it (`scareWithin`, as the crow flies), or a blow, and it lets go and bolts
  for its hole at 5 m/s. The crane's operator, up in the cab over the stack, is too high to count.
  - It hides 25 s (`hideSeconds`), then comes back **for the same casting**.
  - The 5th scare off one casting (`scaresToGiveUp`) and it goes for another, the nearest it can take.
- **Leaving:** when the train is more than 300 m from its crane (`leaveBeyond`: the train's left the yard), or there's
  nothing left to take, it goes down its hole for good.

## 3. Counterplay

Someone stands by the casting it wants; the rest load the others first. A crew that loads in a hurry and keeps a hand on the
ground beats it. A crew all up in the cars, or all at the far end of the yard, loses castings without noticing. That's the
director's "management task of trying to keep awareness of all of the loot around you".

## 4. Open

*All three are in since (note 1050): the model (`content/art/models/mouse_moose.glb`, `tools/blender/mouse_moose.py`, note 796), its sounds (`tell-mouse-moose.*`, `cs-mouse-moose.*`, note 809), and the bots guarding a casting (`Heed.MouseMoose`, enemies.json `crewBots.mouseMooseWithin`, note 790).*

- **Its model:** it's drawn from boxes for now (`CreatureArt.MouseMoose.cs`), and a modelled and rigged one is wanted
  (C1/E1).
- **Its sounds:** none yet (AU1). The squeak as it comes, the scrape of the casting dragged, the squeal as it bolts.
- **The bots:** they don't guard a casting against it yet. A ground bot at the crane scares it off only by being near.
