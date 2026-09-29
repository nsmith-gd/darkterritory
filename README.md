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
| E (hold) | With W: grab a ladder. Standing still: shovel at the firebox, vent at the valve, cut at a coupler plate, wind a brake wheel |
| Left mouse | Fire the gun you're standing at (engine cab roof, via the hatch ladder; guard car roof) |
| R / F | Throttle notch up / down |
| B (hold) | Brake |
| X | Reverser (train stopped only) |
| 1–9 · Backspace | Respawn on that car's roof · respawn in the cab |
| Tab | Chase camera |
| Esc | Release mouse, then quit |

Speed, speed band, controls, grade, your state and text cues for enemy telegraphs are shown in the window title. Edit `content/tuning/*.json` or `content/audio/**/*.json` while it runs and the changes apply immediately.

Sound is synthesised from `content/audio/sounds/*.json`. `dt audio render --scenario chaos --listener all` measures every telegraph against the train in the worst mix. Drop `--listener all` for a WAV and a spectrogram of one listener.
