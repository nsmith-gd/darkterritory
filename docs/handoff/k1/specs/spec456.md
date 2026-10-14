=== YOUR ITEM: queue #456, note 722 — Emotes for two, and more of them ===

Why: the audit's Friends goal (exceed the genre's best). Emotes that need a second person are a friend's thing by
definition. Approved by the director on 14 Oct 2026 (GDD App. F.6). Today: note 298 — an emote wheel (hold J) with DANCE,
WAVE, POINT (`EmoteWheel`, `Emote` enum in src/DarkTerritory.Sim/Player/PlayerMotor.cs:94, two bits on the intent's
hotbar byte, `RecordKind.Emote`, player.json `emotes` durations; poses by IK in `CreatureArt.EmoteArms` until clips
come). Shared ground: U1's open PR #967 (branch origin/claude/zealous-albattani-ce3fvj; docs/design/upgrades.md §4 "More
emotes": the bow, the salute and the conga, a purchasable upgrade) needs more wheel slots too — you widen the wheel; do
NOT build the bow, salute or conga (leave them free slots, documented). Queue #454 (another builder, maybe merged by now)
added lamp signals — if it added a lamp-gesture path, keep it compatible.

Build:
1. **Widen the wheel** to 8 slots (a wire change: more bits; bump Messages.Version by 1). Keep DANCE up, WAVE left, POINT
   right where they are.
2. **For two:**
   - **High five:** offered by one (holds the pose up to ~3 s), answered when a crewmate within ~1.5 m facing them picks
     high five too: both hands meet (a synced clap moment, a sound cue — reuse an existing slap/clap if any, else name it).
     Unanswered, it times out, hanging there (funny on its own).
   - **Fist bump:** the same shape, lower.
   - **Dance-off:** two or more dancing within ~3 m facing each other alternate moves (each dance pose varies by who's
     "leading", deterministic from the tick).
   - **Sit on the edge:** at a roof's edge or a car's open side door sill, sit with legs over (a seated state the motor
     holds in the car's frame; Jump or any move stands you). Shoves (queue #451) and the world's rules still apply.
   - **Lean out:** at a car's open side door or the cab door, lean out with a hand on the rail (look direction frees).
3. Bots answer a high five or fist bump offered to them (charming, cheap). Bots never offer.
4. Poses by IK like the existing ones (clips are art's; leave names for E1: `HighFive`, `FistBump`, `DanceOff`, `SitEdge`,
   `LeanOut`), readable from 6 m. Views: e.g. `highfive`, `sitedge`, `leanout`; look at them.
5. GDD §9 (the yard's emotes) and §32 if HUD; note docs/notes/722.md. Update CLAUDE.md's view list.

Tests: wheel's 8 slots round-trip the wire; a high five offered and answered within range syncs (both records show the
meet tick), unanswered times out; a fist bump the same; sit-on-edge holds in a moving car's frame and stands on Jump;
emotes still move nothing else (EmoteAndOutfitTests); NetcodeTests.
