#!/bin/bash
# The commit that made a trends fall (ARCHITECTURE §8 note 523). `git bisect run` over main's own commits (first parent)
# between a good one and a bad one: each is built and measured on the trends' nights with its own `dt harness` (and its own
# `dt perf` for a perf.* metric), so a commit from before `dt trends` existed is measured as well as a new one, and this
# checkout's `dt trends judge` reads the reports and calls it good or bad against the midpoint of the two ends' values.
# It works in a worktree of its own under a temporary folder: this checkout is never switched, and the bisect's state is
# that worktree's. A commit that doesn't build, or whose night fails, is skipped (exit 125).
#
# Usage: tools/trends/bisect.sh --metric m --good <commit> --bad <commit> [--from main.jsonl] [--seeds 1-8] [--seconds 2700]
#            [--route frontier:7] [--bots 4] [--cars 10] [--parallel n] [--keep]
#   --metric   a trends metric, whole (balance.km) or by its end (km)
#   --from     a trends file (the trends branch's trends/main.jsonl): the ends' values, and the nights, from the bad end's
#              line where it has them, instead of measuring them here (seeds/seconds/... given: measured on those instead)
#   --parallel nights at once (default: the cores)
#   --keep     keep the work folder: each commit's reports and the judge's call, in results/<sha>/
# Each step costs a build and the nights: about 4 minutes a round of nights on four cores for full ones (2700 s); for a
# quick look, --seeds 1-4 --seconds 900 (both ends are then measured here on those nights).
set -euo pipefail
repo="$(cd "$(dirname "$0")/../.." && pwd)"
metric="" good="" bad="" from="" keep=0 parallel=$(nproc 2>/dev/null || echo 4) night=()
while [ $# -gt 0 ]; do
  case "$1" in
    --metric) metric=$2; shift 2 ;;
    --good) good=$2; shift 2 ;;
    --bad) bad=$2; shift 2 ;;
    --from) from=$(realpath "$2"); shift 2 ;;
    --parallel) parallel=$2; shift 2 ;;
    --keep) keep=1; shift ;;
    --seeds|--seconds|--route|--bots|--cars) night+=("$1" "$2"); shift 2 ;;
    *) echo "bisect.sh: unknown option $1" >&2; exit 2 ;;
  esac
done
if [ -z "$metric" ] || [ -z "$good" ] || [ -z "$bad" ]; then
  sed -n '9,18p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
fi
command -v jq > /dev/null || { echo "bisect.sh: needs jq" >&2; exit 2; }
good=$(git -C "$repo" rev-parse --verify "$good^{commit}")
bad=$(git -C "$repo" rev-parse --verify "$bad^{commit}")
git -C "$repo" merge-base --is-ancestor "$good" "$bad" || { echo "bisect.sh: $good isn't an ancestor of $bad" >&2; exit 2; }

dt() { (cd "$repo" && dotnet run --project src/DarkTerritory.Cli -c Release --no-build -- "$@"); }
echo "bisect: building this checkout's dt, the judge" >&2
(cd "$repo" && dotnet build src/DarkTerritory.Cli -c Release -nologo -v quiet) >&2

# The nights: the bad end's line's (when --from has it and no nights were asked for), else the options' (measure's defaults).
if [ -n "$from" ] && [ ${#night[@]} -eq 0 ] && line=$(dt trends value "$from" --commit "$bad" --metric "$metric" 2> /dev/null); then
  harness=$(jq -r .harness <<< "$line")
  seeds=$(jq -r '.seeds | map(tostring) | join(" ")' <<< "$line")
else
  nights=$(dt trends nights "${night[@]}")
  harness=$(jq -r .harness <<< "$nights")
  seeds=$(jq -r '.seeds | map(tostring) | join(" ")' <<< "$nights")
fi
perf=0
[[ "$metric" == perf.* ]] && perf=1

work=$(mktemp -d "${TMPDIR:-/tmp}/dt-bisect.XXXXXX")
tree="$work/tree"
cleanup() {
  git -C "$tree" bisect reset --quiet > /dev/null 2>&1 || true
  git -C "$repo" worktree remove --force "$tree" > /dev/null 2>&1 || true
  if [ "$keep" = 1 ]; then echo "bisect: the reports are in $work/results" >&2; else rm -rf "$work"; fi
}
trap cleanup EXIT
git -C "$repo" worktree add --quiet --detach "$tree" "$bad"

# One commit's measure and call, run by `git bisect run` in the bisect's worktree (and for the ends, by hand). It lives in
# the work folder, not the tree, so checking out an older commit can't take it away.
cat > "$work/step.sh" << 'STEP'
#!/bin/bash
set -uo pipefail
sha=$(git rev-parse HEAD)
out="$WORK/results/$sha"
mkdir -p "$out"
if [ ! -f "$out/done" ]; then
  echo "bisect: $sha $(git log -1 --format=%s | cut -c1-90)" >&2
  dotnet build src/DarkTerritory.Cli -c Release -nologo -v quiet > "$out/build.log" 2>&1 || { echo "bisect: $sha doesn't build: skipped" >&2; exit 125; }
  pids=() fail=0
  for seed in $SEEDS; do
    # shellcheck disable=SC2086 # the harness line is words
    dotnet run --project src/DarkTerritory.Cli -c Release --no-build -- harness $HARNESS --seed "$seed" > "$out/seed-$seed.json" 2> "$out/seed-$seed.log" &
    pids+=($!)
    if [ ${#pids[@]} -ge "$PARALLEL" ]; then wait "${pids[0]}" || fail=1; pids=("${pids[@]:1}"); fi
  done
  for p in "${pids[@]}"; do wait "$p" || fail=1; done
  [ "$fail" = 0 ] || { echo "bisect: $sha: a night failed: skipped" >&2; exit 125; }
  if [ "$PERF" = 1 ]; then
    dotnet run --project src/DarkTerritory.Cli -c Release --no-build -- perf --frames 6 > "$out/perf.json" 2> "$out/perf.log" \
      || { echo "bisect: $sha: dt perf failed: skipped" >&2; exit 125; }
  fi
  touch "$out/done"
fi
judge=(trends judge --metric "$METRIC")
[ -n "${GOODV:-}" ] && judge+=(--good "$GOODV" --bad "$BADV")
reports=("$out"/seed-*.json)
[ -f "$out/perf.json" ] && reports+=("$out/perf.json")
(cd "$REPO" && dotnet run --project src/DarkTerritory.Cli -c Release --no-build -- "${judge[@]}" "${reports[@]}") > "$out/judge.json"
code=$?
jq -c '{metric, value, verdict}' "$out/judge.json" >&2 || true
case $code in
  0|1|125) exit $code ;;
  *) echo "bisect: the judge failed ($code)" >&2; exit 255 ;;
esac
STEP
chmod +x "$work/step.sh"
export WORK="$work" REPO="$repo" HARNESS="$harness" SEEDS="$seeds" METRIC="$metric" PARALLEL="$parallel" PERF="$perf"
echo "bisect: $metric on \`dt harness $harness\`, seeds $seeds; work in $work" >&2

# The two ends' values: from the trends file when it measured them on these nights, else measured here.
end_value() {
  local sha=$1 line
  if [ -n "$from" ] && line=$(dt trends value "$from" --commit "$sha" --metric "$metric" 2> /dev/null) \
    && [ "$(jq -r .harness <<< "$line")" = "$harness" ] && [ "$(jq -r '.seeds | map(tostring) | join(" ")' <<< "$line")" = "$seeds" ]; then
    jq -r .value <<< "$line"
    return
  fi
  git -C "$tree" checkout --quiet --detach "$sha"
  (cd "$tree" && "$work/step.sh") > /dev/null || { echo "bisect: couldn't measure $sha" >&2; exit 2; }
  jq -r .value "$work/results/$sha/judge.json"
}
goodv=$(end_value "$good")
badv=$(end_value "$bad")
echo "bisect: $metric is $goodv at the good end (${good:0:8}), $badv at the bad (${bad:0:8})" >&2
if [ "$goodv" = "$badv" ]; then echo "bisect: the two ends measure the same: nothing to find" >&2; exit 1; fi
export GOODV="$goodv" BADV="$badv"

git -C "$tree" checkout --quiet --detach "$bad"
if ! (cd "$tree" && git bisect start --first-parent "$bad" "$good" > /dev/null && git bisect run "$work/step.sh") 2>&1 | tee "$work/bisect.log" >&2; then
  echo "bisect: git bisect run stopped" >&2
  exit 1
fi
first=$(git -C "$tree" rev-parse refs/bisect/bad)
tried=$(for d in "$work"/results/*/; do
  [ -f "$d/judge.json" ] && jq -c --arg sha "$(basename "$d")" '{commit: $sha, value, verdict}' "$d/judge.json"
done | jq -s .)
jq -n --arg metric "$metric" --arg harness "$harness" --arg seeds "$seeds" --arg good "$good" --arg bad "$bad" \
  --argjson goodv "$goodv" --argjson badv "$badv" --arg first "$first" --arg subject "$(git -C "$repo" log -1 --format=%s "$first")" \
  --argjson tried "$tried" \
  '{metric: $metric, nights: "dt harness \($harness)", seeds: $seeds, good: {commit: $good, value: $goodv}, bad: {commit: $bad, value: $badv},
    midpoint: (($goodv + $badv) / 2), first: {commit: $first, subject: $subject, value: ([$tried[] | select(.commit == $first) | .value][0])}, tried: $tried}'
