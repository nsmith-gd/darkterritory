# Dark Territory

*Two to eight players crew an armoured freight train through a corrupted wilderness. Load what you can. Deliver what survives.*

Built on **Ballast**, a small custom C# engine designed to be built and operated largely by AI agents.

- Design: [`docs/design/gdd.md`](docs/design/gdd.md), [`docs/design/systems-spec.md`](docs/design/systems-spec.md), [art direction](docs/design/art-direction.webp)
- Engine: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) · Plan: [`docs/ROADMAP.md`](docs/ROADMAP.md) · Agent guide: [`CLAUDE.md`](CLAUDE.md)

## Quick start
Requires the .NET 10 SDK.
```bash
dotnet test --solution Ballast.slnx
dotnet run --project src/DarkTerritory.Cli -- train table
```

## Feel prototype
```bash
dotnet run --project src/DarkTerritory.App                          # greybox test loop
dotnet run --project src/DarkTerritory.App -- --route frontier:7     # a generated night: fortress to terminus before dawn, enemies and all
```
Add `--no-enemies` for a quiet line, `--mute` for no sound.
Every key rebinds in **Settings > Controls**. The defaults:

<!-- controls map: written by `dt controls --write` from Controls.Defaults (note 898); don't edit by hand -->
| Key | Action |
|---|---|
| W | Walk forward |
| S | Walk back |
| A | Step left |
| D | Step right |
| Left Shift | Run (held) |
| Space | Jump |
| E | Use: press to pick up or put down; hold to work things (the shovel, a valve, a door, a coupler, a switch stand, a brake wheel), to carry a crewmate, to struggle free |
| Left Mouse | Fire the gun you're at |
| T | Throw what you're holding (the coal off the shovel too) |
| R | In the cab: release the brake and go |
| Y | In the cab: close the regulator (a train with no boiler only) |
| B | In the cab: brake (held) |
| X | In the cab: the reverser (stopped only) |
| L | The engine's headlamp, on or off |
| V | Push to talk (with PUSH TO TALK on in Settings; otherwise the mic is open) |
| Right Mouse | The radio, held, with one on your belt: the whole train hears |
| Q | The crew roster (held); with a number, mute that crewmate (Shift and a number: put them off, or vote to) |
| C | The route card, a page a press, then away |
| Tab | The chase view; dead, the next of the dead's views |
| G | Swing the tool you carry |
| H | The whistle cord, in the cab |
| K | The lamp in the car you're in |
| Z | Cut a coupling (held, looking down at the coupler) |
| F | Grab the nearest ladder |
| P | Dead: bookmark the view you're following |
| Left Alt | Vent the boiler's steam (held, in the cab) |
| I | What's aboard (toggle) |
| J | The emote wheel (held; the mouse picks) |
| Left Ctrl | Crouch (held, or a toggle with HOLD KEYS on TOGGLE) |
| Middle Mouse | Push the gun along its rail (held, walking) |
| 5 | Shove the crewmate in front of you |
| U | With a hand lamp: the lamp signals' wheel (held; the mouse picks) |
| 1-4 · Mouse wheel | The hotbar's slots: pick one · step through them (not rebindable) |
| 1-9 · Enter | Dead, with a ballot: pick a creature · cast it (the same number again casts it too) (not rebindable) |
| Space | Dead: your live mic to the living, on or off; at the run's end, give the commendation the arrows picked (not rebindable) |
| F1 | The HUD, off or on; dead and watching, every word and the UI off for the picture alone (not rebindable) |
| F2 | Invite Steam friends (not rebindable) |
| F4 | Hide the first nights' card (not rebindable) |
| F5 | Reconnect, when the link to the host is gone (not rebindable) |
| Esc | The night's menu (settings, leave the night); in a menu, back (not rebindable) |
| Alt+Enter | Windowed or full screen (not rebindable) |
<!-- end of the controls map -->

Speed, speed band, controls, grade, your state and text cues for enemy telegraphs are shown in the window title. Edit `content/tuning/*.json` or `content/audio/**/*.json` while it runs and the changes apply immediately.

### Designer tools
```bash
dotnet run --project src/DarkTerritory.Cli -- edit     # prints a local URL; open it in a browser
```
- **Tuning:** every number in `content/tuning` and `content/audio`, with its note from the file beside it. A change saves straight into the file (comments kept) and a running game picks it up at once.
- **Routes:** generate a night by tier and seed, look at the map and elevation profile, move, add or delete features, then *Save as* and play it with `-- --route-file <name>`.

### Multiplayer (UDP: LAN or direct IP)
```bash
dotnet run --project src/DarkTerritory.App -- --host --route frontier:7 --cars 6   # host on UDP 27450 (or --host <port>)
dotnet run --project src/DarkTerritory.App -- --join 192.168.1.20                 # join by address (or address:port)
```
Voice is open-mic with voice activity: `--push-to-talk` makes it hold V, and `--no-mic` means listen only. Hold **right mouse** to talk on the walkie-talkie, with one on your belt. Proximity voice is heard out to 26 m and muffled through the cab walls. The radio reaches the whole train, except into a tunnel. The dead hear only each other.

The host picks the route and train. Joiners build the same world from what the host sends. Everyone, host included, plays through the same client path. The host's player boards first and takes the cab. Networked, you drive from the cab only (GDD §12): stand in it for R/Y/B/X. The host needs the UDP port open or forwarded. Steam and EOS transports (relay, lobbies, invites) come later.

A night: leave the fortress yard through the gates, and the dawn clock starts. Stop at the coaling tower to refill the tender: someone gets down, holds E at the lever, and the chute pours, into the tender if it's under the spout, onto the ballast if not. Pull up at the terminus before dawn and you're paid for every loaded car still coupled to the engine.

Sound is synthesised from `content/audio/sounds/*.json`. `dt audio render --scenario chaos --listener all` measures every telegraph against the train in the worst mix. Drop `--listener all` for a WAV and a spectrogram of one listener.
