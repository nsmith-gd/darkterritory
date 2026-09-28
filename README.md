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
dotnet run --project src/DarkTerritory.App
```
| Input | Action |
|---|---|
| Mouse · WASD · Shift · Space | Look · move · run · jump |
| E (hold) | Grab / let go of a ladder · shovel at the firebox · vent at the valve (in the cab) |
| R / F | Throttle notch up / down |
| B (hold) | Brake |
| X | Reverser (train stopped only) |
| 1–9 · Backspace | Respawn on that car's roof · respawn in the cab |
| Tab | Chase camera |
| Esc | Release mouse, then quit |

Speed, speed band, controls, grade and your state are shown in the window title. Edit `content/tuning/*.json` while it runs and the changes apply immediately.
