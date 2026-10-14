=== YOUR ITEM: queue #457, note 723 — Bringing them home ===

Why: the audit's Friends goal (exceed the genre's best) has a pillar "nobody gets left": the game's tension is "the engine
can leave without you" (GDD §17); give friends the physical verbs to answer it. Approved by the director on 14 Oct 2026
(GDD App. F.6). Builds on queue #451 (horseplay, merged before you: crewmate collision, the shove, staggers — read its
note docs/notes/717.md and reuse its stagger/collision code rather than duplicating).

Build:
1. **The hand-up.** A crewmate on the ground running beside a moving car (on its step's side) and a crewmate standing at
   that car's open side door, end platform or step: the one aboard holds Use looking at the runner (within ~1.6 m, the
   grab reach scale; tuning) and hauls them aboard, if the relative speed is under a limit (tuning, e.g. 7 m/s: a runner's
   sprint plus a little). Takes ~0.6 s (tuning) of both staying in reach. The runner lands inside/at the door in the car's
   frame (moving-frame rule: never convert frames before integrating a tick; see MovingFrameRegressionTests). The HUD:
   "HAUL UP : HOLD [E]" when aimed at a runner in range (house prompt style). Bots on the train haul up a left-behind
   human who reaches a door (if cheap: the bots' walkers already react to left-behind crew — check). The incident report
   / commendations: "Came Back For Me" exists — a hand-up should count toward it if it fits its rule.
2. **Carry a friend.** A living crewmate can be picked up (Use held looking at them, ~1 s) and carried over the shoulder
   (reuse the body-carry: `Heavy` pace, no climbing — or the solo-carry rules; tuning). The carried one can look and talk;
   Jump wriggles free (lands at the carrier's feet). Can be put down (Use) or thrown a short way (Throw, like bodies;
   falls by height apply — that's a griefer joy, intended). A carried player is safe from nothing: what grabs the carrier
   may drop them. Bots can be carried; bots never carry humans.
3. **Help up.** A fall from over 5.5 m knocks you (note 573): find what a knock does today (a stun/lying state?). A
   crewmate's Use on a knocked friend gets them up at once (or faster, tuning). If there's no knocked state, add a short
   one only if it already exists in design; otherwise skip this sub-feature and say so.
4. **Bandage a friend.** A healing find (bandages 30, medicine 50, morphine 80; loot.json `healing`; used by holding Use,
   `Bodies.Dose`, note 272) used on a crewmate (holding it, Use held looking at them within reach) heals them half again as
   much (tuning ×1.5, capped at full). HUD: "BANDAGE : HOLD [E]" / the find's own verb.
5. Views: `handup` (a crewmate in a door reaching down to a runner beside a moving car), `carryfriend`; look at them.
6. GDD §17 (the engine can leave without you, and the hand-up), §12/§23 (carry, help up), App. D or the damage model's
   healing line; note docs/notes/723.md.

Tests: a runner beside a moving car within reach and under the speed limit is hauled aboard into the car's frame;
over the speed limit, not; a carried friend moves with the carrier, wriggles free on Jump, falls by height if thrown;
a bandage on a friend heals ×1.5; NetcodeTests and MovingFrameRegressionTests green.
