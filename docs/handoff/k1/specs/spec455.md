=== YOUR ITEM: queue #455, note 721 — Crew memory ===

Why: the audit's Friends goal (exceed the genre's best) has a pillar "a crew remembers": in-jokes need a place to live, and a
campaign the Wakers end should be a story to keep, not a reset. Approved by the director on 14 Oct 2026 (GDD App. F.6).
Shared ground: U1's open PR #967 (branch origin/claude/zealous-albattani-ce3fvj, docs/design/upgrades.md §4 "The yard
photographer": "A crew photo at the gate before each night; the campaign's end card shows every one") is an approved
upgrade that will reuse your photo machinery: build the shot and the album so a departure photo is one call away, but
only take the ARRIVAL photo yourself. The big cab (queue #450) is being built concurrently: its back wall gets a "photo
rail" (empty frames) where photos can later be pinned; don't depend on it.

Build:
1. **The crew photo at the gate.** On a Delivered night (`RunEnd.Delivered`), as the train stands in at the terminus, the
   town photographer's flash: a group shot rendered by the app (presentation only; nothing in the sim depends on it):
   the living crew stood in a line in front of the engine in the town's yard, each in whatever emote they're holding at
   that moment (or a default stand), the dead shown by their caps on a crate at the end of the line, and the night's
   tally chalked on a slate on an easel (net scrip, the route name, the night number). The flash (a white frame) and a
   camera click sound if one exists. Saved as a PNG in the bookmark album (`BookmarkAlbum`, LocalAppData/DarkTerritory/
   bookmarks or a sibling folder) with a text sidecar, and shown on the end card/tally screen as a small framed print.
   Positions for the line-up are computed deterministically from the replicated world (no extra wire) where possible.
   Bots stand in the photo too. Headless: a `dt screenshot` view (e.g. `crewphoto`) that stages a delivered night with a
   crew of 4 (one dead) and renders the photo; look at it and make it read well (the composition matters: faces in frame,
   the engine behind, the slate legible).
2. **The logbook.** A book on a shelf (in the guard van or car one's lockers for now; leave a hook so it can move to the
   cab's back wall later): Use opens it as a full-screen page in the game's paper style (see how notices/papers/the
   route card are drawn), one page per night: the night number, route, date line, the photo (if this machine has it),
   the incident report's cause-of-death lines (IncidentLog), the commendations, the tally's net. Turn pages with the
   movement keys. Stored per campaign in the save (host) AND copied into each player's own profile (`PlayerProfile`)
   on every machine at the night's end from the replicated report, so every player keeps their crew's logbook even
   after the campaign ends or the host's save is gone.
3. **The board of lost crews.** In the fortress town (the departure town's square or by the gate), a board where each
   ended campaign this player rode with is posted as a card: the crew's name, how many nights it ran, how it ended
   ("TAKEN AT DAWN"), the cause line of its last night. From the player's profile. Use reads it close up. Keep the town
   edit small and in B2/P1's existing systems (find how notice boards are placed: content/world/towns.json, Town.cs).
4. GDD: §9 Arrival (the photo), §11 (the logbook and the board, what persists), §3.1 (the board in town); note
   docs/notes/721.md.

Tests: a delivered night's photo staging picks the living in a line and the dead's caps (deterministic positions);
the logbook gains a page per night with the report's lines, survives a campaign's end in the profile, and round-trips
the save; the lost-crews board lists an ended campaign. Game tests for the page layout where the repo tests HUD/pages.
