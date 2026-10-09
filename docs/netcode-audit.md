# Netcode audit: Dark Territory against the P2P model of Lethal Company, REPO and PEAK

N2, 9 October 2026 (queue #267, ARCHITECTURE §8 note 530). The director's question: "Audit the networking for the game
and how much it resembles a good P2P set up like Lethal Company, REPO, and PEAK. We don't want to be paying for servers,
we want exceptionally strong P2P that is easy."

## The short answer

**The model is already the right one, and it is the cheaper of the two those games use.** One player's machine hosts
(a listen server, the host is a player too), friends find it through a Steam lobby, and the game's own packets ride
Steam Datagram Relay (SDR), which Valve runs for every Steamworks game at no charge. Nothing is rented and nothing is
metered. That is exactly Lethal Company's arrangement. REPO and PEAK are a different arrangement that *looks* the same
to the player: a Steam lobby in front of a **Photon** room, and Photon's cloud is a hosted service billed by concurrent
players. If the goal is "no servers to pay for", Lethal Company is the one to copy, and this codebase copies it.

**What's built is more than those games have** on the things that make co-op forgiving: a dropped player comes back to
their own body on a token (none of the three do this), friends drop in at any stop (Lethal Company only in orbit, PEAK
not at all), a content and mod check before a joiner builds a world (Lethal Company crashes on a mod mismatch), listed
private runs behind a password with the host's friends let in, every row's ping, bots and the agent harness on the same
intent path as players, and a nightly eight-client soak over a lossy link that fails on a millimetre of divergence.

**The one thing that matters most has never happened: a game over real Steam.** Every Steam test runs against
`FakeOnline`, an in-process stand-in. The Steam half (`SteamBackend`, 295 lines) is a thin mapping that has never
carried a packet between two accounts, because the cloud sessions have no Steam client and Steam's hosts are blocked
there. Until two machines on two accounts have hosted, invited, joined from the list, dropped and rejoined, the P2P
story is a design, not a fact. That is gap 1 below, and it needs the director (two accounts, two machines, a Steamworks
app id). Everything else here is smaller.

## 1. What the three games actually do

| | Lethal Company | REPO | PEAK | Dark Territory |
|---|---|---|---|---|
| Who hosts | one player's machine (listen server) | one player's machine; Photon calls it the "master client" | one player's machine | one player's machine (`NetPlaySession.HostGame`) |
| Who carries the packets | Steam's relay (SDR), P2P by SteamID | **Photon Cloud** (Photon PUN): every client, the host included, connects to Photon's regional servers, which relay the room | **Photon** behind Steam lobbies (a community fix exists for joining a Steam lobby before Photon's connected) | Steam's relay (SDR) through `ISteamNetworkingMessages`; UDP on a LAN or by address |
| What it costs the studio | nothing: SDR comes with Steamworks | a Photon plan priced by concurrent users above a free tier | the same | nothing (Steam); nothing (UDP) |
| Finding a game | Steam lobby list (public), invites, "Join Game", lobby name | Steam lobbies and invites, then a Photon room | Steam lobbies and invites, then a Photon room | the JOIN list: Steam's public lobbies and the LAN beacon, in one list with ping and mood; overlay invites; "Join Game"; `+connect_lobby`; an address |
| Private games | unlisted, invite only | invite only | invite only | listed with a lock, behind a password; the host's Steam friends get in without it (note 450) |
| Late join | only while the ship is in orbit | at the lobby/truck | no | at the yard, at any stop, at the terminus; between stops the joiner waits and boards at the next (note 23) |
| A dropped player | gone; rejoin is a new player, and only in orbit | gone | gone | comes back to their own body on a slot token, within 3 minutes, by any route (note 253) |
| Host leaves | everyone to the menu | everyone to the menu | everyone to the menu | everyone to the menu (spec E: no host migration) |
| Host migration | no | no | no | no |
| Voice | built in (Dissonance over the game's transport), free | Photon Voice (billed with Photon) | Photon Voice | built in (Opus through Concentus, host-routed by proximity, radio, the dead channel), free |
| Version/content check | none (mod mismatches crash or desync) | Photon room properties | | protocol version on the lobby, a hash of every tuning file and the mod list in the Welcome, refused with the file named |
| Cheating/safety | host trusts clients | host trusts clients | | host validates and clamps intent; clients send intent only; SDR hides IPs and authenticates SteamIDs |

Sources on the three (the studios don't publish their netcode; these are the best public statements): Photon's own
case study of REPO ([blog.photonengine.com](https://blog.photonengine.com/r-e-p-o-multiplayer-success-powered-by-photon/)),
PEAK's "Disconnected from Photon" reports and the `PeakLobbyJoinFix` mod, and Lethal Company's Steam community thread
on its P2P and SDR behaviour. Lethal Company's stack (Unity Netcode for GameObjects over a Facepunch Steamworks transport)
is the community's reading of its files, not a developer statement.

## 2. What's built here

### 2.1 The transport (`src/Ballast.Net`)

- **One protocol over any carrier.** `DatagramTransport<TAddress>` is the game's own connection protocol: a handshake
  (Connect with magic, version and nonce; Accept with a peer id and a random session token that rides on every datagram),
  an unreliable channel (one datagram per message) and a reliable-ordered channel (sequence numbers, a cumulative ack on
  every datagram, resend after 1.5 round trips with an 80 ms floor, a reorder buffer), pings every second, 8 s of silence
  is a timeout, 1,200-byte maximum with no fragmentation. Malformed datagrams are swallowed, never thrown.
- **Carriers.** `UdpTransport` (one non-blocking IPv4 socket, polled once a tick, no threads; the Windows
  `SIO_UDP_CONNRESET` trap handled); `OnlineTransport` (the same protocol over `IOnlineBackend.Carrier`, addressed by
  user id); `LoopbackNetwork` (latency, jitter and loss, seeded, for tests and the harness).
- **`HostGroup`:** one host on several transports at once (Steam for friends, UDP for LAN and for its own player on
  localhost); the session above sees one crew.
- **`LanBeacon`:** the host's advert on UDP 27451 every second (game, protocol, port, name, crew, tier, lobby id, locked,
  mood), and a ping socket so the browser measures a LAN round trip.

### 2.2 Steam (`src/Ballast.Online`)

- **`IOnlineBackend`:** identity, friends, lobbies (create, join, leave, data, joinable, member limit), the lobby
  search with filters, the ping location, the overlay invite dialog, invites, the store page, and the relayed datagram
  carrier. `FakeOnline` is the in-process platform the tests run against; `SteamBackend` is Steamworks.NET 2024.8
  (SDK 1.60) against app id 480 (Valve's test app) until the game has its own.
- **`SteamBackend`'s carrier** is `ISteamNetworkingMessages` on channel 0: unreliable, no Nagle, broken sessions
  reopened quietly. Sessions are accepted only from lobby-mates (a request from a stranger is held and accepted the
  moment they appear in a lobby we're in). `InitRelayNetworkAccess` is called at start so the first connection doesn't
  wait on the relay config.
- **`Lobby`:** host or join; the lobby carries game, protocol, host name, public flag, ping location, and the game's own
  keys (name, tier, run, aboard, max, full, locked, mood). A joiner refuses the wrong game or protocol before connecting,
  then connects to the owner. Everyone stays in the lobby while playing: that's what makes "Join Game" and the session
  rule work.
- **Join list** (`LobbyBrowser`): Steam's public lobbies for this game and protocol (worldwide, 50 results, searched
  every 10 s while the screen's up, pinged by Steam's estimate from the host's published ping location) merged with the
  LAN beacon's games (pinged for real), sorted by mood then nearest, FULL and other-protocol rows greyed with the reason.

### 2.3 The sessions (`src/DarkTerritory.Sim/Net`)

- **`HostSession`:** 30 Hz, host-authoritative, the host's own player a loopback client like anyone (no host-only
  path, no latency advantage beyond the loopback). Inputs sequence-numbered and sent four times over; the host clamps
  everything a client can send. Snapshots are fixed-point records delta-encoded against the newest snapshot the client
  has acked, with per-client baselines, interest by distance along the train (520 m, sized to the farthest tell) and by
  what the dead are watching, a Follower never sent to its victim, and a budget that spreads a snapshot bigger than a
  datagram over a few ticks (note 101). Lag compensation for the guns. The greeting line (Hello first, then Welcome, the
  crew cap, the password, the trusted friend, the held place's token), the Wait for a stop, the reserve for a dropped
  player, the Leave, the refusal that lingers long enough to arrive. Voice forwarded star-wise to exactly who hears it.
- **`ClientSession`:** prediction and reconciliation of the player *and the train* (rewind to the host's truth, replay
  what it hasn't applied), remote players interpolated on their car, placements adopted without counting as
  corrections, `Reconnect(transport)` keeping the world and the input sequence.
- **Protocol 43**, bumped on every layout change, refused before connecting.

### 2.4 Verification (what runs without Steam)

- `NetcodeTests` (24), `RejoinTests` (9), `CrewCapTests` (9), `PrivateRunTests` (5), `NetPlayTests` (11, real UDP
  sockets and the fake lobby), `LobbyTests` (9), `LobbyListTests` (3), `LobbyBrowserTests` (6), `UdpTransportTests` (8,
  including heavy loss and a fuzzer at the host), `LoopbackNetworkTests` (5).
- `dt harness` over the loopback link, real UDP sockets (`--udp`), the fake Steam lobby (`--online`), with a drop and
  rejoin (`--drop-rejoin`); the nightly soak runs eight clients for 30 minutes on the rough link, a night with enemies, and
  degraded voice. The `crossplay` CI job diffs Windows' and Linux's generated lines from one seed.
- `dt online check` says whether this machine reaches Steam and who's signed in.

### 2.5 Measured today (main at 887e851, frontier:7, 10 cars, 8 bots, enemies on)

| Link | Snapshot | Down per client | Up per client | Worst correction | Snapshots got, of sent | Deaths |
|---|---|---|---|---|---|---|
| Rough (90 ms ±20, 3 % loss), 300 s | 291 B | 64.3 kbit/s | 24.5 kbit/s | 0.37 m (once; 28 small ones across 8 clients) | 8,569–8,625 of 9,000 | 0 |
| Real UDP sockets, 120 s | 252 B | 57.0 | 24.5 | 0.1 mm | 3,599 of 3,600 | 0 |
| Fake Steam lobby, 120 s | 252 B | 57.0 | 24.5 | 0.1 mm | 3,599 of 3,600 | 0 |
| Rough with a drop at 120 s and a redial at 135 s | 296 B | 63.9 | 24.3 | 0.08 m | the rejoiner 8,174 | 0; back in 0.23 s as the same player, seen once by everyone, 1.6 mm of corrections after |
| Bad (200 ms ±50, 10 % loss), 120 s | 291 B | 56.1 | 24.4 | 0.08 m | 2,457–2,530 of 3,600 | 0 |

Two readings of that table. The game stays exact and playable on a link worse than most home connections: on the bad
link a third of the snapshots never land or land stale (the jitter reorders them past the next tick), and the worst
correction anyone makes is 8 cm. And **the down rate sits at the budget**: ARCHITECTURE §6.2 says ≤ 64 kbit/s typical
and 128 peak; note 23 measured 52 (and 39 with interest) when it was written, and the content since (fire grids, upkeep,
the sites, hits and impacts, emotes) has brought it to 64. The host's upload with seven remote crewmates is about
450 kbit/s before voice; voice adds 24 kbit/s per speaker per listener in range. A host on a home connection is fine; a
host on hotel wifi or a phone isn't, and nothing yet adapts (gap 5).

## 3. The gaps, in order

Each is a queue item in docs/COORDINATION.md for whoever takes it; the numbers are there.

### Gap 1. Real Steam has never carried a game (the director's, with an agent)

The whole Steam path (lobby create and join, the session request rule, the relayed datagrams, "Join Game", the overlay
invite, `+connect_lobby`, the public list with Steam's ping estimate, the friend let in without the password, a drop and
rejoin through the lobby) has only ever run against `FakeOnline`. The roadmap has carried "a first run on real Steam
between two accounts (needs the SDK library and two machines)" since M2.

What it needs, and none of it is code an agent can run here:

1. **A Steamworks app id** for the game and the demo (the release workflow already takes both). App id 480 shares its
   lobby space with every test project on Steam; the `game` key keeps strangers out of the list, but a real id is needed
   before Next Fest's build anyway.
2. **Valve's redistributable in the builds.** `release.yml` fetches it from the `STEAM_REDIST_URL` secret; `package.sh`
   builds locally copy whatever is in `external/steam/`. Set the secret, or the Steam build is a LAN build.
3. **Two machines, two accounts, one evening**, with a script to follow and the console logs kept. The script is §4 of
   this document. An agent writes it up afterwards as a note, and fixes what it finds.

Until then, treat every Steam line in the roadmap as untested. The risk is concrete: `SteamBackend.Poll` accepts a
P2P session only once the asker shows in a lobby's member list, and the two callbacks (the session request, the member
change) have an order Steam decides. `FakeOnline` can't tell us whether a slow lobby update leaves the joiner's first
Connects unanswered for longer than its 10 s give-up. It will probably work (the held request is re-checked every poll
and the client re-sends Connect every 0.25 s), but "probably" is the state of the whole path.

### Gap 2. The host's network runs inside its frame loop (an agent; medium)

Every transport is polled once per sim tick, and the sim ticks inside the window's frame loop (`FixedStepClock`,
at most 8 ticks a frame). So:

- a host that stalls for 8 s drops every client, bots included (loading a stop's village, the derail film's first
  compile, a driver update, alt-tab on some GPUs: note 101's "watch" already names the software renderer's first frame);
- a host under 4 fps can never catch up: 8 ticks a frame at 30 Hz needs 3.75 fps, and below it the host's clock falls
  behind real time, every client's inputs arrive "early", and the whole crew rubber-bands on the host's frame rate;
- the ping the HUD shows is a frame-time measurement (note 450 saw 3,167 ms on a software-rendered pair).

The first of those is already in hand: the director saw it on main's build ("I tried to play with bots and they were
all dead when we spawned in"), and D1 holds it as queue #268 (a transport that wasn't polled through a stall doesn't
count the stall as its peers' silence). The rest is this gap.

*Done as note 532 (N2, queue #270): the pump thread, player.json `link`, the ping from the pump, the client's pace to a host
that's behind, and the host told.*

Lethal Company has the same coupling (Unity's main thread), with Steam's own 10 s timeout, and its players know "the
host's PC is the server". Dark Territory can do better cheaply: pump keepalives, acks and resends from a timer thread
(the sim keeps reading received datagrams on its tick; the transport's queues become thread-safe), raise the play
timeout from 8 s to 20 s (tests keep their short one; `DatagramOptions` already exists for it), and take the round trip
from the pump. Separately, cap the host's catch-up differently from a client's: a host that can't keep 30 Hz should
*slow the night* (ticks are the clock) rather than skip time, and the lobby panel should say "YOUR MACHINE IS HOLDING
THE CREW BACK" when it happens.

### Gap 3. The link's quality is invisible (an agent; small)

Spec E: "Ping visibility is load-bearing. Without host migration, a bad host connection loses everyone's run." The
JOIN list shows a ping, and the HUD shows the round trip to the host. Nothing shows loss, jitter, whether Steam is
relaying or direct, or that the host's upload is saturated. `ISteamNetworkingMessages.GetSessionConnectionInfo` returns
Steam's own ping, quality and in/out rates for a session; the transport already counts datagrams sent and received and
can count stale snapshots. Put a link line in the HUD's panel (ping, loss over the last 10 s, "VIA STEAM RELAY") and
colour it, and put the same on the host's lobby panel for each crewmate so a host can see who's struggling before
driving out of the yard.

### Gap 4. Joining mid-game by invite relaunches the game (an agent; small)

*Done as note 551 (N2, queue #272): the friend's lobby is the next launch, in-process.*

Note 24: an invite accepted while playing ends the game and restarts the process with `+connect_lobby`. The outcome
matches Lethal Company (you leave the game you're in), but a relaunch costs the renderer's startup and loses the menu.
Tear the session down and go through `JoinLobby` in-process instead; the fresh start stays for an invite accepted from
outside.

### Gap 5. Nothing adapts to a weak host or a weak client (an agent; medium)

*Done as note 553 (N2, queue #273): the client reports its loss, a thin link is sent every other snapshot, the crew are sent
in a rotating order, and the host is told when its upload can't carry them; the interest radius was left alone (the tells).*

At the budget (§2.5), a host on a thin uplink hurts everyone and a client on a thin downlink hurts themselves, and
neither is told. Three levers, in order of return: send a client whose acks lag at 15 Hz instead of 30 (prediction
tolerates it; the host already keeps two seconds of baselines); a lower interest radius for a struggling client, keeping
the tells' 520 m for everyone else; and a lobby-panel warning on the host when its upload per crewmate times the crew
passes what the connection has shown it can carry. Measure with the harness on a link profile with a capped rate
(`LoopbackNetwork` has latency, jitter and loss; add a bandwidth cap).

### Gap 6. Players off Steam have no relay (the director's decision)

The itch.io build has no platform: joiners need the host's address and an open port, which is where most of Lethal
Company's "can't connect" threads come from on the Steam side too, except that there SDR saves them. The design allows
a third carrier (EOS P2P, which Epic runs free of charge and which works without an Epic account for the host's
friends to be invited by link), and the roadmap moved it to M6. The decision: ship Steam-only online at launch, as
Lethal Company did, and say so on the itch page (LAN and direct IP work), or take EOS before the itch build. Either is
fine; the second is a queue item of a week.

### Gap 7. Note 450's "not yet" (N1 or an agent; small)

The host can't change the password or the mood once the lobby's open; a password guesser isn't slowed beyond a
connection per guess; nothing lists a crew's record distance beside a competitive run. All small, all in N1's area.

### Gap 8. The two-machine test is by hand (an agent; small)

*Done as note 552 (N2, queue #276): `tools/net/two-machines.sh`, first in the nightly soak.*

Note 450's two Linux network namespaces joined by a veth pair (two real app windows, one hosting, one joining over the
wire, the beacon heard, the password refused and then accepted) was run by hand. Put it in the nightly soak as a Linux
job: it is the only test that runs the real app's host and join screens over real sockets end to end.

## 4. The first real Steam evening (the script for gap 1)

Two machines (one Windows, one Linux if possible, for cross-play), two Steam accounts that are friends, the Steam
client running on both, `external/steam/` filled on the build machine, the same build on both. Keep every console log.

1. `dt online check` on both: Steam reachable, the right account named.
2. **Host A** from the menu: HOST, PUBLIC, a name, OPEN THE LOBBY. Expect: the lobby panel says the game's listed, and
   within a few seconds the lobby carries a ping location (the browser on B shows a ping, not "-- MS").
3. **B** opens JOIN: A's run is on the list with its name, tier, crew and ping. Pick it. Expect: B aboard within 2 s,
   A's panel "2 aboard", both HUDs a ping under 100 ms for machines in one country. Walk, climb, throw a crate, fire a gun:
   no corrections worth seeing.
4. **Invite paths.** B leaves. A presses F2 (the overlay's invite picker): B accepts in the overlay → aboard. B leaves.
   B uses "Join Game" on A in the friends list → aboard. B quits the game entirely; A invites; B accepts from the
   desktop → the game starts with `+connect_lobby` and joins.
5. **Private.** A hosts PRIVATE with a password. B sees it locked on the list; is B let in without the password, as A's
   friend? (Expected yes: `IsFriend`.) Unfriend for a minute: B is asked, WRONG PASSWORD on a wrong one, in on the right one.
6. **Drop and rejoin.** B pulls the network cable for 10 s. Expect: B's HUD "RECONNECTING: TRY 1 OF 5", back on its
   own body; A's view: B's body went limp and got up; "2 aboard" throughout (the held place counts).
7. **Host leaves.** A quits. Expect: B told the host left, back to the menu, nothing hung.
8. **Full crew.** A hosts with 7 bots. B is refused CREW FULL on the list (greyed) and on an invite.
9. **LAN and Steam at once.** A hosts with both on one machine; B joins by Steam, a third machine joins by address.
10. **A real night**: drive out, 20 minutes, the derailment film at the end, both machines' logs kept. Then
    `--drop-rejoin` by hand again mid-night.

Write the results up as a note; each failure is a queue item.

## 5. What looks like a gap and isn't

- **No host migration.** None of the three has it, and the spec rules it out. With the rejoin token and the inert body,
  what's lost when a host drops is the night, not the campaign (the host's autosave) or the crew's time to find each
  other again (the lobby's still theirs to re-host from).
- **Its own reliability on top of Steam's.** SDR has a reliable channel; the game doesn't use it (note 24: one protocol
  on every carrier, tested once; the harness, the netcode tests and the prediction numbers all carry over to Steam). The
  cost is a few header bytes a datagram. Keep it.
- **The 1,200-byte limit with no fragmentation.** That is SDR's MTU too; a bigger unreliable message would be
  fragmented by Steam and lost whole on any fragment's loss. The budget (note 101) is the right answer.
- **The token in the clear over UDP.** On Steam the relay encrypts and authenticates; on a LAN or by address the password
  gates the door and the token only lets a dropped player back to their own slot. Enough for co-op.
- **Relay always, never direct.** SDR relays by default and hides every IP; a direct path (Steam's ICE) saves a few
  milliseconds and shows addresses. Leave the default.
- **Steam only when launched by Steam.** `--steam` is needed from a bare command line; a game launched from the Steam
  client has `SteamAppId` set and starts Steam on its own, and an accepted invite does too. That is the right default for
  a packaged build (an itch build with no library says "LAN and direct IP still work" and carries on).

## 6. How this was measured

```bash
dotnet run --project src/DarkTerritory.Cli -c Release --no-build -- harness --bots 8 --seconds 300 --route frontier:7 --enemies --cars 10
dotnet run --project src/DarkTerritory.Cli -c Release --no-build -- harness --bots 8 --seconds 120 --route frontier:7 --enemies --udp
dotnet run --project src/DarkTerritory.Cli -c Release --no-build -- harness --bots 8 --seconds 120 --route frontier:7 --enemies --online
dotnet run --project src/DarkTerritory.Cli -c Release --no-build -- harness --bots 8 --seconds 300 --route frontier:7 --enemies --drop-rejoin roof-walker:120:15
dotnet run --project src/DarkTerritory.Cli -c Release --no-build -- harness --bots 8 --seconds 120 --route frontier:7 --enemies --latency 0.2 --jitter 0.05 --loss 0.1
```

Read with the rest: ARCHITECTURE §6.2 and §8 notes 18, 23, 24, 101, 159, 160, 169, 199, 253, 254 and 450; GDD §33; spec
Part E; the roadmap's M2 and "Steam (M2)".
