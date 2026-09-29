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
| Input | Action |
|---|---|
| Mouse · WASD · Shift · Space | Look · move · run · jump |
| E (hold) | With W: grab a ladder. Standing still: shovel at the firebox, vent at the valve, open or shut a door you're facing, cut at a coupler plate, wind a brake wheel |
| Left mouse | Fire the gun you're standing at (engine cab roof, via the hatch ladder; guard car roof) |
| R / F | Throttle notch up / down |
| B (hold) | Brake |
| X | Reverser (train stopped only) |
| 1–9 · Backspace | Respawn on that car's roof · respawn in the cab |
| Tab | Chase camera |
| Esc | Release mouse, then quit |

Speed, speed band, controls, grade, your state and text cues for enemy telegraphs are shown in the window title. Edit `content/tuning/*.json` or `content/audio/**/*.json` while it runs and the changes apply immediately.

### Multiplayer (UDP: LAN or direct IP)
```bash
dotnet run --project src/DarkTerritory.App -- --host --route frontier:7 --cars 6   # host on UDP 27450 (or --host <port>)
dotnet run --project src/DarkTerritory.App -- --join 192.168.1.20                 # join by address (or address:port)
```
Voice is open-mic with voice activity: `--push-to-talk` makes it hold V, and `--no-mic` means listen only. Hold **T** to talk on the walkie-talkie. Proximity voice is heard out to 26 m and muffled through the cab walls. The radio reaches the whole train, except into a tunnel. The dead hear only each other.

The host picks the route and train. Joiners build the same world from what the host sends. Everyone, host included, plays through the same client path. The host's player boards first and takes the cab. Networked, you drive from the cab only (GDD §12): stand in it for R/F/B/X. The host needs the UDP port open or forwarded. Steam and EOS transports (relay, lobbies, invites) come later.

A night: leave the fortress yard through the gates, and the dawn clock starts. Stop at the coaling tower to refill the tender: someone gets down, holds E at the lever, and the chute pours, into the tender if it's under the spout, onto the ballast if not. Pull up at the terminus before dawn and you're paid for every loaded car still coupled to the engine.

Sound is synthesised from `content/audio/sounds/*.json`. `dt audio render --scenario chaos --listener all` measures every telegraph against the train in the worst mix. Drop `--listener all` for a WAV and a spectrogram of one listener.
