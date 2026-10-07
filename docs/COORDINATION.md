# Coordination

How the agents working on Dark Territory stay out of each other's way. Read this before starting work. Change it in the
same PR as the work it describes, or in a small PR of its own when you only claim or release an item. **This file is the
source of truth for who owns what**; chat with one session isn't seen by the others.

## The rules

1. **Claim before you start.** Put your name (below) and a branch against an item in the queue, and land that change on
   main first: a docs-only PR, merged as soon as it's green. If someone else's name is already there, pick another item or
   ask on their PR.
2. **One item, one branch, one PR into main.** Squash-merge when CI is green (`dotnet format` clean, build, all tests).
   Merge main into your branch rather than rebasing a branch someone else may have checked out.
3. **Reserve your ARCHITECTURE §8 note number here** when you claim, so two notes never collide. Take the next free
   number from the table.
4. **Done means merged.** Move the item to *Done* with its PR link and note number in the PR that lands it.
5. **Questions between agents go in PR comments** on the PR in question (both sides are watching their own PRs).
   Questions for the director go in *Waiting on the director* below and to the director in your own chat.
6. **Log what you do** in your own file, `docs/log/<agent id>.md`, every entry with its UTC date and time (format in
   docs/log/README.md). Give yourself an agent id when you start (the table below).
7. **Follow CLAUDE.md.** Design numbers in `content/tuning`, a deterministic Sim, everything verifiable headless, visual
   changes looked at.

## Who's who

| Name | Agent ids | What it is | Log |
|---|---|---|---|
| **A** | A1; its agents A1.1, A1.2, … | Claude Code, the director's main cloud session, and the agents it launches | [docs/log/A1.md](log/A1.md) |
| **B** | B1, … (to choose) | Claude Code on the director's second account (to start soon) | docs/log/B1.md |
| **C** | C1; its agents C1.1, … | Claude Code, the director's art session: the art checklist (claude.ai/artifact/7MnAAHwaVRtNosBH7hRLNu), the Look Review (claude.ai/artifact/MgW84RexYLg3JVBC52XoXm) and the art's implementation | [docs/log/C1.md](log/C1.md) |
| **Audio** | — | The director's audio chat: owns all audio work except the derailment opera | — |

Whatever an agent launched by A or B does counts as its owner's: the owner reviews it and opens the PR.

## Direction (the director, 5–7 Oct 2026)

- Work through the queue below, in order. The source for each item is GDD App. F (the director's notes and decisions of
  5–6 Oct), docs/design/gdd.md.
- **VR is on the backburner.** No new VR features or art; keep the existing VR tests green.
- **Audio** belongs to the audio chat, except the derailment opera.
- PRs can be merged by whoever opened them once they're green.

## The queue

Status: **open**, **claimed**, **in review** (a PR is up), **done** (merged).

| # | Item | Owner | Branch / PR | Note | Status |
|---|---|---|---|---|---|
| 1 | **Pacing by pressure:** a quiet spell of 20–90 s picked per night; spawns driven by escalating pressure | A1 | `wp31-pacing`, [#189](https://github.com/nsmith-gd/darkterritory/pull/189) | 266 | done |
| 2 | **Stoker v3, the firebox half:** the territorial door (a heavy burn, the second kills), vent and starve it out, the hose kills it (an extinguisher for now), no chip damage | A1 | `stoker-v3`, [#191](https://github.com/nsmith-gd/darkterritory/pull/191) | 271 | done |
| 3 | **Track Doll escalation:** harmless at first; ignored, she moves to the controls, and in the end lets a standing train off its brake | A1.1 | `track-doll-escalation` | 268 | pushed; PR next |
| 4 | **Damage model, Lethal Company style:** a few big hits, never chip damage; healing items rare loot; an edge flash and a sound; creatures driven off by their rules, mostly not damaged | A1.4 | `damage-model` | 272 | claimed |
| 5 | **Boarding rules per creature:** Cinder Hounds that board stay aboard; Fire Flies only while the train is stopped | A1.2 | `boarding-rules` | 269 | pushed; PR next |
| 6 | **One run length for every tier:** difficulty from monsters and density of challenges, not time; quiet stretches in km (GDD §11, spec B.8; T125) | A1.3 | `run-length` | 270 | claimed |
| 7 | **T128:** tuning for falls after a grab; grabbed players taken somewhere sensible; pressure when a player's left behind; forts safe from creatures | A1.5 | `t128` | 273 | claimed |
| 8 | **T124:** fort collision, and the guns hitting at the forts | A1.6 | `t124` | 274 | claimed |
| 9 | **WP19 tool damage:** the shovel and the breach tool as swings (an old branch, `wp19-melee`, needs merging with main) | A1.7 | `wp19-tools` | 275 | claimed |
| 10 | **Towns (T133):** fortress towns as places, their own cultures, story by inference (notes, text-only townspeople) | — | — | — | open |
| 11 | **The cab redesign:** cab forward, the controls at the front with the whole line in view, the coal bunker in the cab (the director's sketch) | C1 | `claude/busy-carson-0i3g8d`, [#190](https://github.com/nsmith-gd/darkterritory/pull/190) | 276 | done |
| 12 | **Art checklist L1s:** the boiler rupture's burst and seized engine, the noisy toys' models, the headlamp out, lockers that don't block each other, bend limits on the route card, smaller prompts, a lighter HUD out of the cab, the overspeed telegraph's flange sparks, the crew in the wreck mid-task | C1 | `claude/busy-carson-0i3g8d` | 277 | in review |

The GDD artifact (claude.ai/artifact/PrfaZjyZb1rWcWWuiPm1Dx) was republished from main's docs/design/gdd.md on 6 Oct at
23:38 UTC; A1 republishes it whenever gdd.md changes on main.

## ARCHITECTURE §8 note numbers

The next free number is **278**. Reserved: 266 (pacing), 268 (Track Doll), 269 (boarding rules), 270 (run length), 271
(Stoker v3), 272 (damage model), 273 (T128), 274 (T124), 275 (WP19, renumbered from its old 191), 276 (cab forward,
renumbered from 268), 277 (C1's art checklist L1s). Take a number by adding it here and to your queue row.

## Done (since 6 Oct)

| Item | PR | Note |
|---|---|---|
| The engine, cab forward: controls at the front with the rail in full view, coal in the cab | [#190](https://github.com/nsmith-gd/darkterritory/pull/190) | 276 |
| Fire is a grid: cells on the floor, walls and roof; spray the cell you aim at; burnt cells char | [#188](https://github.com/nsmith-gd/darkterritory/pull/188) | 267 |
| Director pacing by pressure; a 20–90 s quiet spell per night | [#189](https://github.com/nsmith-gd/darkterritory/pull/189) | 266 |
| Stoker v3, the firebox half: territorial door, vent and starve, the hose | [#191](https://github.com/nsmith-gd/darkterritory/pull/191) | 271 |
| The derailment opera: CC0 and public-domain recordings, hits at the climax | [#150](https://github.com/nsmith-gd/darkterritory/pull/150) | — |

## Waiting on the director

- GDD D.15, question 3: the answer was cut off.
- Whether the Stoker's hose should be a real cab fitting (a slacking pipe) instead of an extinguisher (note 271).
