#!/bin/bash
# Sends the packaged game (tools/package.sh) to a store (roadmap M6): Steam through steamcmd, itch.io through butler.
# Usage: tools/upload.sh steam|itch [--demo] [--dry-run] [--preview] [--branch name] [--version v]
#   --demo      the demo's app (Steam) or channels (itch) instead of the game's
#   --dry-run   check the builds and write what would be sent (out/store/), but call neither tool and need no login
#   --preview   Steam only: steamcmd builds the manifests and uploads nothing (Steam's own dry run; needs a login)
#   --branch    Steam only: set this beta branch live once it's up. Steam never lets a script set the default branch
#               live, so a release to players is always a click in Steamworks.
#   --version   what the store shows; the date and commit by default
# IDs are in tools/store/store.conf, credentials in the environment (tools/store/README.md). The result is also written
# as out/store/<store>.json, so CI can check a dry run without either tool.
set -euo pipefail
cd "$(dirname "$0")/.."
# shellcheck source=store/store.conf
source tools/store/store.conf

store="${1:-}"; shift || true
demo=0 dry=0 preview=0 branch="" version=""
while [ $# -gt 0 ]; do
  case "$1" in
    --demo) demo=1 ;;
    --dry-run) dry=1 ;;
    --preview) preview=1 ;;
    --branch) branch="$2"; shift ;;
    --version) version="$2"; shift ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
  shift
done
case "$store" in steam|itch) ;; *) echo "usage: tools/upload.sh steam|itch [--demo] [--dry-run] [--preview] [--branch name] [--version v]" >&2; exit 2 ;; esac
[ "$preview" = 1 ] && [ "$store" != steam ] && { echo "--preview is Steam's" >&2; exit 2; }
[ -n "$branch" ] && [ "$store" != steam ] && { echo "--branch is Steam's; itch uses channels" >&2; exit 2; }
[ "$branch" = default ] && { echo "Steam won't set the default branch live from a script; set it live in Steamworks" >&2; exit 2; }
[ -z "$version" ] && version="$(date -u +%Y.%m.%d)-$(git rev-parse --short HEAD 2>/dev/null || echo local)"

fail() { echo "$*" >&2; exit 1; }
out="$PWD/out/store/$store"
rm -rf "$out"; mkdir -p "$out"

# The builds: the folders package.sh leaves, each a game that runs on its own (its executable, its content, how to play).
# The demo's are package.sh --demo's, with the demo edition baked in (T79); the game's must not be.
build=DarkTerritory; [ "$demo" = 1 ] && build=DarkTerritory-Demo
rids=(win-x64 linux-x64)
declare -A exe=([win-x64]=DarkTerritory.exe [linux-x64]=DarkTerritory)
declare -A steamlib=([win-x64]=steam_api64.dll [linux-x64]=libsteam_api.so)
for rid in "${rids[@]}"; do
  dir="out/dist/$build-$rid"
  [ -d "$dir" ] || fail "no $dir: run tools/package.sh$([ "$demo" = 1 ] && echo ' --demo') first"
  [ -f "$dir/${exe[$rid]}" ] || fail "$dir has no ${exe[$rid]}"
  [ "$rid" = linux-x64 ] && { [ -x "$dir/${exe[$rid]}" ] || fail "$dir/${exe[$rid]} isn't executable"; }
  [ -f "$dir/content/tuning/train.json" ] || fail "$dir has no content"
  if [ "$demo" = 1 ]; then grep -q '"demo": true' "$dir/content/tuning/edition.json" || fail "$dir isn't the demo edition: run tools/package.sh --demo"
  elif grep -q '"demo": true' "$dir/content/tuning/edition.json" 2>/dev/null; then fail "$dir is the demo edition"; fi
  [ -f "$dir/PLAYING.txt" ] || fail "$dir has no PLAYING.txt"
  # None of the developer tools go to a store (note 514): package.sh checked it, and it's checked again on what's sent.
  dotnet run --project src/DarkTerritory.Cli -c Release -- build check "$dir" > "$out/check-$rid.json" \
    || fail "$dir carries the developer tools (see $out/check-$rid.json): package it without --dev"
  # Steam players need Valve's library beside the game for lobbies and invites (external/steam/README.md). The Windows
  # one is required to ship on Steam; Linux players can still host and join by address without theirs.
  if [ "$store" = steam ] && [ ! -f "$dir/${steamlib[$rid]}" ]; then
    if [ "$rid" = win-x64 ] && [ "$dry" = 0 ]; then fail "$dir has no ${steamlib[$rid]}: see external/steam/README.md"; fi
    echo "warning: $dir has no ${steamlib[$rid]} (Steam lobbies won't work there)" >&2
  fi
done

files() { find "$1" -type f | wc -l | tr -d ' '; }
bytes() { du -sb "$1" | cut -f1; }
uploads=()

if [ "$store" = steam ]; then
  if [ "$demo" = 1 ]; then app=$STEAM_DEMO_APP; declare -A depot=([win-x64]=$STEAM_DEMO_DEPOT_WIN [linux-x64]=$STEAM_DEMO_DEPOT_LINUX)
  else app=$STEAM_APP; declare -A depot=([win-x64]=$STEAM_DEPOT_WIN [linux-x64]=$STEAM_DEPOT_LINUX); fi
  if [ "$dry" = 0 ]; then
    [ "$app" != 0 ] && [ "${depot[win-x64]}" != 0 ] && [ "${depot[linux-x64]}" != 0 ] || fail "the Steam app and depot IDs in tools/store/store.conf are still 0"
    [ -n "${STEAM_USERNAME:-}" ] || fail "STEAM_USERNAME isn't set: the Steamworks build account (tools/store/README.md)"
    command -v "${STEAMCMD:-steamcmd}" >/dev/null || fail "no steamcmd (set STEAMCMD, or see tools/store/README.md)"
  fi
  # One app build with a depot per platform, each the whole folder. The VDF paths are absolute, so it runs from anywhere.
  vdf="$out/app_build_$app.vdf"
  {
    printf '"AppBuild"\n{\n'
    printf '\t"AppID" "%s"\n' "$app"
    printf '\t"Desc" "Dark Territory %s%s"\n' "$version" "$([ "$demo" = 1 ] && echo ' (demo)')"
    printf '\t"Preview" "%s"\n' "$preview"
    printf '\t"SetLive" "%s"\n' "$branch"
    printf '\t"BuildOutput" "%s/output/"\n' "$out"
    printf '\t"Depots"\n\t{\n'
    for rid in "${rids[@]}"; do
      printf '\t\t"%s"\n\t\t{\n' "${depot[$rid]}"
      printf '\t\t\t"ContentRoot" "%s/"\n' "$PWD/out/dist/$build-$rid"
      printf '\t\t\t"FileMapping"\n\t\t\t{\n\t\t\t\t"LocalPath" "*"\n\t\t\t\t"DepotPath" "."\n\t\t\t\t"recursive" "1"\n\t\t\t}\n'
      # Steam gives the game its app id when it launches it; a stray steam_appid.txt would pin the dev one.
      printf '\t\t\t"FileExclusion" "steam_appid.txt"\n'
      printf '\t\t\t"FileExclusion" "*.pdb"\n'
      printf '\t\t}\n'
      uploads+=("$(jq -n --arg rid "$rid" --arg depot "${depot[$rid]}" --arg dir "out/dist/$build-$rid" \
        --argjson files "$(files "out/dist/$build-$rid")" --argjson bytes "$(bytes "out/dist/$build-$rid")" \
        '{rid: $rid, depot: ($depot | tonumber), dir: $dir, files: $files, bytes: $bytes}')")
    done
    printf '\t}\n}\n'
  } > "$vdf"
  cmd=("${STEAMCMD:-steamcmd}" +login "${STEAM_USERNAME:-<build account>}" +run_app_build "$vdf" +quit)
  echo "${cmd[*]}"
  [ "$dry" = 1 ] || "${cmd[@]}"
  jq -n --arg store steam --arg version "$version" --argjson demo "$demo" --argjson dry "$dry" --argjson preview "$preview" \
    --arg branch "$branch" --arg app "$app" --arg vdf "$vdf" --argjson uploads "$(printf '%s\n' "${uploads[@]}" | jq -s .)" \
    '{store: $store, version: $version, demo: ($demo == 1), dryRun: ($dry == 1), preview: ($preview == 1), branch: $branch,
      app: ($app | tonumber), vdf: $vdf, uploads: $uploads}' > "$out.json"
else
  target="${ITCH_TARGET:-}"
  if [ "$dry" = 0 ]; then
    [ -n "$target" ] || fail "ITCH_TARGET in tools/store/store.conf isn't set (user/game)"
    [ -n "${BUTLER_API_KEY:-}" ] || [ -f "$HOME/.config/itch/butler_creds" ] || fail "butler isn't logged in: set BUTLER_API_KEY or run 'butler login'"
    command -v "${BUTLER:-butler}" >/dev/null || fail "no butler (set BUTLER, or see tools/store/README.md)"
  fi
  suffix=""; [ "$demo" = 1 ] && suffix=-demo
  declare -A channel=([win-x64]=$ITCH_CHANNEL_WIN$suffix [linux-x64]=$ITCH_CHANNEL_LINUX$suffix)
  for rid in "${rids[@]}"; do
    dir="out/dist/$build-$rid"
    # --if-changed: pushing the same build twice is a no-op, so a re-run release job is harmless.
    cmd=("${BUTLER:-butler}" push "$dir" "${target:-<user/game>}:${channel[$rid]}" --userversion "$version" --if-changed)
    echo "${cmd[*]}"
    [ "$dry" = 1 ] || "${cmd[@]}"
    uploads+=("$(jq -n --arg rid "$rid" --arg channel "${channel[$rid]}" --arg dir "$dir" \
      --argjson files "$(files "$dir")" --argjson bytes "$(bytes "$dir")" \
      '{rid: $rid, channel: $channel, dir: $dir, files: $files, bytes: $bytes}')")
  done
  jq -n --arg store itch --arg version "$version" --argjson demo "$demo" --argjson dry "$dry" --arg target "$target" \
    --argjson uploads "$(printf '%s\n' "${uploads[@]}" | jq -s .)" \
    '{store: $store, version: $version, demo: ($demo == 1), dryRun: ($dry == 1), target: $target, uploads: $uploads}' > "$out.json"
fi
echo "$out.json"
