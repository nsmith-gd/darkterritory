#!/bin/bash
# A pull request's review packet (ARCHITECTURE §8 note 517): its gallery (out/shots, from tools/review/gallery.sh) against
# main's at the pull request's base, as strips and a summary in out/review/ (review.md goes on the run's summary page; the
# folder is the run's review-packet artifact, for the PR's agent to post where the director looks).
# Main's CI keeps its gallery as each push run's `screenshots` artifact. This takes the one at the base commit, or the
# nearest before it whose run has finished, and says how far back that was.
# Usage: tools/review/packet.sh <base sha> [dt arguments]   (needs GH_TOKEN with actions: read, and GITHUB_REPOSITORY)
set -euo pipefail
cd "$(dirname "$0")/../.."
base="$1"; shift
DT="dotnet run --project src/DarkTerritory.Cli ${*:--c Release --no-build} --"
rm -rf out/review out/review-base; mkdir -p out/review out/review-base
summary() { cat out/review/review.md >> "${GITHUB_STEP_SUMMARY:-/dev/null}"; cat out/review/review.md; }
found="" run="" back=0
for sha in $(gh api "repos/$GITHUB_REPOSITORY/commits?sha=$base&per_page=15" --jq '.[].sha'); do
  run=$(gh run list --repo "$GITHUB_REPOSITORY" --workflow ci.yml --branch main --event push --commit "$sha" --status success \
    --json databaseId --jq '.[0].databaseId // empty' 2>/dev/null || true)
  if [ -n "$run" ]; then found=$sha; break; fi
  back=$((back + 1))
done
if [ -z "$found" ] || ! gh run download "$run" --repo "$GITHUB_REPOSITORY" -n screenshots -D out/review-base; then
  printf '## Review packet\n\nNo gallery from main to compare with: no finished main run at %s or the 14 commits before it.\n' "${base:0:7}" > out/review/review.md
  summary
  exit 0
fi
title="against main at ${found:0:7}"
[ "$back" -gt 0 ] && title="$title, $back commit(s) before this PR's base (their changes show here too)"
$DT review diff out/review-base out/shots --out out/review --title "$title" > out/review/diff.json
summary
