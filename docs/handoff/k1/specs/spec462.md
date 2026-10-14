=== YOUR ITEM: queue #462, note 728 — The sense table, and thrown things do what they are ===

Why: the audit found Hackers (Bartle's implicit explorers: poke the systems, find weird outcomes by feel) underserved:
most actions are prompts, not systems that answer back. The creatures already run on senses (heat, light, sound,
vibration, scent, movement, absence; GDD App. A). Let the crew's items speak that language, consistently, so the roster
becomes a toybox without a tutorial ("never in the Wiki"; the GDD's rules stay fixed and learnable, §20). Approved by the
director on 14 Oct 2026 (GDD App. F.6). The director's playtest line 24 (docs/playtest/2026-10-13-evening.md): "I'm
pretty sure I can take a fire extinguisher and use them on the cinder hounds, can't I? ... they still got me, even though I
was using it on them. That should have worked."

Build:
1. **The sense table** (content: a new tuning file, e.g. content/tuning/senses.json, citing App. A; each item lists what
   it gives off or masks, with a radius and a strength): the hand lamp and car lamps (light), the noisy toys — squeaker,
   music box, drummer (sound; a thrown music box keeps playing where it lands), the extinguisher's spray (cold: masks heat),
   a thrown coal lump or a crate dropped on a rail (vibration knock), a flare if one exists (light), the dinner/food finds
   (scent), a body (scent/absence for the Mourners — exists). Then wire the creatures' existing sense checks to read world
   emitters from the table, so that:
   - a music box thrown into the field lures what hunts by sound (the Moose goes for the loudest; the Choir's meter
     counts it as a toy's noise wherever it lies; the Gaunt hates silence — a playing box near it counts as talk? decide
     per App. A and document);
   - the extinguisher's spray on a Cinder Hound (heat) drives it back (a deterrent, like a cannon round's suppression but
     weaker; tuning) — playtest line 24;
   - a lamp thrown/set into a dark house shows what's waiting there and draws the Fire Flies to it (light);
   - a knock on the rail draws vibration creatures (the Car Hugger, Tower Jaw, Knotter?) toward it, as decoys.
   Keep every creature's rule true to App. A (a sense it doesn't have, it ignores). Deterministic.
2. **Thrown things do what they are** (the bodies' impacts): a thrown lamp breaks where it lands and lights the fire grid
   there if it lands on something that burns (the car floor cells; on the ground, a short burn); a powder keg thrown rolls
   and blows on a hard knock past a threshold (reuse the keg explosion, App. C); a dropped crate on a foot? (no damage
   between crewmates — skip); glass breaks. Each gets a sound cue name for AU1 if new.
3. A `dt` way to see it: e.g. `dt gym`-style headless scenario or tests; views `musicbox` (a box lying in a field with a
   Moose turning to it), `extinguisherhound`; look at them.
4. GDD App. A.1 (the shared skeleton's senses), the affected creatures' entries, App. C (items), §21; note docs/notes/728.md.

Tests: a music box lying 20 m off draws a Moose's attention over a quiet crewmate; a sound creature ignores a lamp; the
extinguisher sprayed on a hound in range drives it back; a thrown lamp landing on a car floor starts a fire cell; a keg
thrown hard blows; NetcodeTests and the creatures' own tests green.
