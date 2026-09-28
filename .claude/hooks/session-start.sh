#!/bin/bash
# Installs the .NET 10 SDK and restores packages in Claude Code on the web sessions.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep -q '^10\.'; then
  # builds.dotnet.microsoft.com is blocked by the egress proxy; Ubuntu's archive carries the SDK.
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-10.0 >/dev/null
fi

echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1' >> "${CLAUDE_ENV_FILE:-/dev/null}"
echo 'export DOTNET_NOLOGO=1' >> "${CLAUDE_ENV_FILE:-/dev/null}"

cd "$CLAUDE_PROJECT_DIR"
dotnet restore Ballast.slnx
