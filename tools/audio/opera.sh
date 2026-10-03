#!/usr/bin/env bash
# GDD v1.4 App. E.6: rebuilds the derailment's music (our own CC0 recordings of public-domain works) into content/audio/music,
# with its manifest (SHA-256, loudness, hit) and CREDITS.md. The arrangements and the synth are C#, in
# src/DarkTerritory.Cli/OperaCommands.cs; this runs them. --check renders without writing and says whether the files match.
# Spectrograms of each track land in out/audio/opera/ for looking at.
set -euo pipefail
cd "$(dirname "$0")/../.."
exec dotnet run --project src/DarkTerritory.Cli -- audio opera "$@"
