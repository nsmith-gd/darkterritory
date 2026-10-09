#!/bin/bash
# A trends line onto trends/main.jsonl on the `trends` branch (ARCHITECTURE §8 note 523), as the Trends workflow does it:
# without checking the branch out (the job's checkout stays main's), by writing the file's new version, a tree and a
# commit straight into the repository and pushing that. The branch is a history of its own (it starts with no parent, and
# nothing but the trends is ever in it); a push that loses a race to another run's line reads the branch again and retries.
#
# Usage: tools/trends/append.sh <line.json> <main.jsonl to write> [remote]
#   writes the whole file, the new line last, to the second path (for `dt trends show` to read after)
set -euo pipefail
line=$1 out=$2 remote=${3:-origin}
index=$(mktemp)
trap 'rm -f "$index"' EXIT
subject=$(git log -1 --format=%s 2> /dev/null | cut -c1-80 || true)
for attempt in 1 2 3 4 5 6; do
  rm -f "$index"
  if git fetch -q --depth=1 "$remote" +refs/heads/trends:refs/remotes/"$remote"/trends 2> /dev/null; then
    tip=refs/remotes/$remote/trends
    parent=(-p "$tip")
    GIT_INDEX_FILE=$index git read-tree "$tip"
    git show "$tip:trends/main.jsonl" > "$out" 2> /dev/null || : > "$out"
  else
    parent=()
    GIT_INDEX_FILE=$index git read-tree --empty
    : > "$out"
  fi
  cat "$line" >> "$out"
  blob=$(git hash-object -w "$out")
  GIT_INDEX_FILE=$index git update-index --add --cacheinfo "100644,$blob,trends/main.jsonl"
  tree=$(GIT_INDEX_FILE=$index git write-tree)
  commit=$(git commit-tree "$tree" "${parent[@]}" -m "trends: $(git rev-parse --short=10 HEAD) ${subject}")
  if git push -q "$remote" "$commit:refs/heads/trends"; then
    echo "trends/main.jsonl: $(wc -l < "$out") lines, the newest $(git rev-parse --short=10 HEAD)'s"
    exit 0
  fi
  echo "the trends branch moved under this run (another line came in): reading it again" >&2
  sleep $((attempt * 5))
done
echo "couldn't push to the trends branch" >&2
exit 1
