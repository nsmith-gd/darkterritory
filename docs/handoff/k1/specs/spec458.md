=== YOUR ITEM: queue #458, note 724 — Sessions ===

Why: friend groups break on friction: uneven attendance, slow joins, and strangers in public games. The audit's Friends
goal and its guardrail (mischief is fine among friends; a public lobby needs kick and mute so horseplay can ship there).
Approved by the director on 14 Oct 2026 (GDD App. F.6). Read the networking notes first (N1/N2's: docs/log/N1.md,
N2.md, docs/netcode-audit.md summary; notes 24, 169, 253, 450, 481, 555, 606, 611) and `HostSession`, `ClientSession`,
`SaveSlots`, `CampaignState`, `PlayerProfile`, `LobbyBrowser`, the roster (held Q).

Build:
1. **The crew's save travels.** Today a campaign save lives on the host's machine (3 slots). Make the save the crew's:
   at each night's end (when the host settles the night into the save), the host sends the save's bytes to every client
   aboard (a new message; chunk it if large; bump Messages.Version by 1); each client keeps it under the crew's id in a
   "crews you've ridden with" store (not overwriting the client's own slots). The front end's campaign/continue screen
   lists those crews ("CREW <NAME> · NIGHT 7 · LAST HOSTED BY DAVE"), and any member can host from it. When two copies
   differ, the one with more nights settled wins (deterministic; a tie: the newest settlement tick). Keep all file IO in
   the Game/App layer (the Sim has no files outside content/). Dawn's campaign end (note 632) closes every copy: a closed
   save stays listed as ended (it feeds #455's board of lost crews if that's merged; don't depend on it).
2. **A friend takes a bot's seat.** Joining mid-night today puts you in the Holdout queue or has you wait at a stop
   (`HostSession` ~l.451). When a bot crews the night and a human joins, the joiner takes a bot's place where it stands:
   the bot's body, slots, health and post (pick the bot the director's census rates least busy, or the newest);
   the bot leaves. If no bot is aboard, the old way stands. Tuning flag `crew.joinTakesBotSeat` (default true).
3. **Kick and mute.**
   - **Mute:** per player, on this machine only, from the roster (held Q): MUTE / UNMUTE beside a name; their voice isn't
     played here (VoiceChat), on any path. Persists for that player id this session.
   - **Kick:** the host can kick a player from the roster; in a public lobby, any player can call a vote-kick (majority
     of the living humans, host's vote counts, 30 s window, tuning) — the kicked player is sent back to the menu with a
     line ("THE CREW PUT YOU OFF AT THE NEXT STOP"), and can't rejoin that run. Friends-only/private lobbies: host kick only.
   - Keep the UI in F1's established style (find the in-night menu / roster code, note 292/104).
4. GDD §33/§33.1 (sessions), §11 (one wallet, the crew's save); note docs/notes/724.md.

Tests: the save round-trips host→client and a client can host from it; the more-nights copy wins; a joiner mid-night
takes a bot's seat with its slots and post, and the bot count drops; a muted player's frames aren't played (routing or
playback level); a vote-kick passes by majority and the kicked id can't rejoin; NetcodeTests and the session/join tests
(RejoinTests etc.) green. Use the headless harness (`dt harness`) if it helps prove the join path.
