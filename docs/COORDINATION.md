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
| **B** | B1, B2, B3, B4; their agents B1.1, B2.1, B3.1, B4.1, … | Claude Code on the director's second account, several sessions: B1 **Level Design** (the line generator's set pieces, sites, and the world's solidity); B2 **Towns** (the fortress towns and the world's story; registered on [#194](https://github.com/nsmith-gd/darkterritory/pull/194)); B3 **UI/UX** (the HUD overhaul; registered on [#206](https://github.com/nsmith-gd/darkterritory/pull/206)); B4 **Level Design 3**, the third level-design session, beside B1 and B2 | [docs/log/B1.md](log/B1.md), docs/log/B2.md (on #194), docs/log/B3.md (on #206), [docs/log/B4.md](log/B4.md) |
| **C** | C1; its agents C1.1, … | Claude Code, the director's art session: the art checklist (claude.ai/artifact/7MnAAHwaVRtNosBH7hRLNu), the Look Review (claude.ai/artifact/MgW84RexYLg3JVBC52XoXm) and the art's implementation | [docs/log/C1.md](log/C1.md) |
| **Audio** | — | The director's audio chat: owns all audio work except the derailment opera | — |
| **D** | D1; its agents D1.1, D1.2, … | Claude Code on another of the director's accounts: **Gameplay 3**, the third gameplay session (alongside A's gameplay queue and one more on another account). Takes gameplay items from the queue | [docs/log/D1.md](log/D1.md) |

Whatever an agent launched by A or B does counts as its owner's: the owner reviews it and opens the PR.

## Direction (the director, 5–7 Oct 2026)

- Work through the queue below, in order. The source for each item is GDD App. F (the director's notes and decisions of
  5–6 Oct), docs/design/gdd.md.
- **VR is on the backburner.** No new VR features or art; keep the existing VR tests green.
- **Audio** belongs to the audio chat, except the derailment opera.
- PRs can be merged by whoever opened them once they're green.
- **Keep the shared artifacts current** (the director, 7 Oct): every time you land something or get an instruction, update the
  artifacts that track it: the GDD (claude.ai/artifact/PrfaZjyZb1rWcWWuiPm1Dx; A1 republishes it from gdd.md), the Wiki
  (claude.ai/artifact/WPsKWpHkYctBbpGGFutbjA: what a crew sees, page by page), the Art Checklist
  (claude.ai/artifact/7MnAAHwaVRtNosBH7hRLNu), the Audio Checklist (claude.ai/artifact/F5szjdzd8Svn3nfH3mDWMN) and the Look
  Review (claude.ai/artifact/MgW84RexYLg3JVBC52XoXm). Read the live version first and change only your own rows or sections.

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
| 13 | **Bends worth braking for:** every line carries bends that derail the train under its top speed, boarded and on the map; note 265's open call | B1 | `claude/level-design-derailment-terrain-93awvc` | 278 | claimed |
| 14 | **The world is solid:** creatures, carries and players respect terrain and geometry (past T128's grab destinations and creatures on the ground); the world's structures interactable | B1 | `claude/level-design-derailment-terrain-93awvc` (after #13) | 279 | claimed |
| 15 | **The cab, everything facing forward:** the director on #190: "the controls and firebox need to be in front not facing the back" | C1 | `claude/busy-carson-0i3g8d` (after #195) | 280 | claimed |
| 28 | **The guns' effect on creatures:** rounds land on what they hit and a hit creature shows it: the strike seen and heard where it lands, the creature reacting by its own rule (GDD App. F.1: "the guns do nothing. Rounds don't collide where they land and have no visible effect on the monsters"). **Overlaps:** A1's #25 (creatures killable by a coordinated team, on #198) owns whether a creature dies; #28 makes the hit land, read and drive off by the creature's rule, and leaves the kill rules to #25 | D1 | `claude/relaxed-franklin-xkfjgb`, [#212](https://github.com/nsmith-gd/darkterritory/pull/212) | 290 | done |
| 32 | **Blocked sidings:** derelict cars standing on a yard's sidings, by tier (level-design D.2: 0 / 0–1 / 1–2 / 1–3, never all), scored as clearances (D.1: +2 throws, ×5) in the stop's difficulty, laid in the world as standing rakes the crew must pull clear (note 187's `Stand`), and worked by the bots' stop crew. Level-design I.4's "derelict cars on the sidings" | B4 | `claude/friendly-davinci-y7imvb` (after #209) | 294 | claimed |
| 33 | **Tunnel name plates:** a named tunnel's plate on both portals (linegen-plan §13.3; the signage skips tunnels today) | B4 | `claude/friendly-davinci-y7imvb` (after #32) | 295 | claimed |
| 34 | **T128's caveats, the forts and the left-behind:** the Choir's meter stilled while the train's in a fort (note 273: "the Choir's meter isn't stilled in the terminus"); more than Ribbits hunt a crewmate left behind (note 273: "only the Ribbits hunt so far. Hounds or the Gaunt could join once their own rules allow a lone player on the ground"; GDD App. F.1 "Abandoned player") | D1 | `claude/relaxed-franklin-xkfjgb`, [#213](https://github.com/nsmith-gd/darkterritory/pull/213) | 296 | done |
| 37 | **The bot gunner takes its gun:** in an 8-bot night with enemies (`dt harness --route frontier:7 --bots 8 --enemies --seconds 600`) the gunner never fires a round (main and #212 alike: 0 rounds, a hound pack aboard, 3 mauled). From the yard it goes into car 1 to catch the mail cranes' bags (`RoofWalkerBot.Look`'s drops) and there's always another ahead, so it never reaches the guard gun (the harness's `posts`: gunner -1). Bots crew solo nights in the app too | D1 | `claude/relaxed-franklin-xkfjgb`, [#215](https://github.com/nsmith-gd/darkterritory/pull/215) | 299 | done |
| 38 | **Solo, a few runs then friends:** the director (GDD App. F.1, 6 Oct): "a solo player can finish one to three runs before it gets seriously hard and they realise they need friends ... The solo finish target will be tested later." Sweep solo nights (`dt balance --crews 1`) across the early tiers and train lengths, judge them in `balance.json`, and tune if it's off | D1 | `claude/relaxed-franklin-xkfjgb` (after #37) | 300 | claimed |

The GDD artifact (claude.ai/artifact/PrfaZjyZb1rWcWWuiPm1Dx) was republished from main's docs/design/gdd.md on 7 Oct at
14:45 UTC (D1, main at 8caabe7, version 8); whoever lands a gdd.md change republishes it (A1 when it's about).

## ARCHITECTURE §8 note numbers

The next free number is **301**, after the claims on open PRs (B2 281 on #194; C1 280–281 on #203; A1 286–289 on #198; AU1 284 on #205; B3 285 on #206; D1 290, landed in #208; E1 291 on #210; F1 292–294 on #211, colliding with B4's 294). Reserved: 266 (pacing), 268 (Track Doll), 269 (boarding rules), 270 (run length), 271
(Stoker v3), 272 (damage model), 273 (T128), 274 (T124), 275 (WP19, renumbered from its old 191), 276 (cab forward,
renumbered from 268), 277 (C1's art checklist L1s), 278 (B1's bends), 279 (B1's solid world), 280 (C1's cab facing forward), 290 (D1's guns on creatures), 294 (B4's blocked sidings), 295 (B4's tunnel name plates), 296 (D1's T128 caveats), 299 (D1's bot gunner; F1's #211 holds 292–298), 300 (D1's solo target). Take a number by adding it here and to your queue row.

## Done (since 6 Oct)

| Item | PR | Note |
|---|---|---|
| The bot gunner takes its gun: the mail cranes' bags are the walkers' | [#215](https://github.com/nsmith-gd/darkterritory/pull/215) | 299 |
| T128's caveats: the Choir stilled in the forts, a Gaunt among the left-behind's hunters | [#213](https://github.com/nsmith-gd/darkterritory/pull/213) | 296 |
| The guns' effect on creatures: every body in the open stops a ball, a ball lands as a blow, the hit seen | [#212](https://github.com/nsmith-gd/darkterritory/pull/212) | 290 |
| The engine, cab forward: controls at the front with the rail in full view, coal in the cab | [#190](https://github.com/nsmith-gd/darkterritory/pull/190) | 276 |
| Fire is a grid: cells on the floor, walls and roof; spray the cell you aim at; burnt cells char | [#188](https://github.com/nsmith-gd/darkterritory/pull/188) | 267 |
| Director pacing by pressure; a 20–90 s quiet spell per night | [#189](https://github.com/nsmith-gd/darkterritory/pull/189) | 266 |
| Stoker v3, the firebox half: territorial door, vent and starve, the hose | [#191](https://github.com/nsmith-gd/darkterritory/pull/191) | 271 |
| The derailment opera: CC0 and public-domain recordings, hits at the climax | [#150](https://github.com/nsmith-gd/darkterritory/pull/150) | — |

## Waiting on the director

- GDD D.15, question 3: the answer was cut off.
- Whether the Stoker's hose should be a real cab fitting (a slacking pipe) instead of an extinguisher (note 271).
