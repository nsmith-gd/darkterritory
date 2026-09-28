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
