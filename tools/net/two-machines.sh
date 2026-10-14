#!/bin/bash
# Two machines, virtually (ARCHITECTURE §8 note 450; queue #276): the real app hosting in one Linux network namespace and
# joining from another, over a veth pair, under xvfb with software Vulkan. The only end-to-end run of the host and join
# path over real sockets: the beacon's port, the password refused and then accepted, both players aboard, both quitting clean.
# Needs root for the namespaces (CI's runner has sudo), a Release build of the app, xvfb-run and lavapipe.
# Usage: tools/net/two-machines.sh [seconds]   (how long the host stays up; the joiner runs for a little less)
set -euo pipefail
cd "$(dirname "$0")/../.."
seconds=${1:-40}
app=src/DarkTerritory.App/bin/Release/net10.0/DarkTerritory.App
[ -x "$app" ] || { echo "build the app first: dotnet build src/DarkTerritory.App -c Release" >&2; exit 2; }
out=out/two-machines
mkdir -p "$out"
sudo=""; [ "$(id -u)" = 0 ] || sudo=sudo

cleanup() {
  $sudo ip netns del dt-host 2>/dev/null || true
  $sudo ip netns del dt-join 2>/dev/null || true
}
trap cleanup EXIT
cleanup
$sudo ip netns add dt-host
$sudo ip netns add dt-join
$sudo ip link add veth-h type veth peer name veth-j
$sudo ip link set veth-h netns dt-host
$sudo ip link set veth-j netns dt-join
$sudo ip -n dt-host addr add 10.77.0.1/24 dev veth-h
$sudo ip -n dt-join addr add 10.77.0.2/24 dev veth-j
for ns in dt-host dt-join; do
  $sudo ip -n $ns link set lo up
done
$sudo ip -n dt-host link set veth-h up
$sudo ip -n dt-join link set veth-j up
$sudo ip netns exec dt-join ping -c1 -W2 10.77.0.1 > /dev/null

# Each copy runs as the calling user inside its namespace, drawing to its own xvfb (lavapipe), the route given so the front
# end is skipped (CLAUDE.md's headless app line). The host is private with a password and listed with its mood (note 450).
run() { # ns, log, args...
  local ns=$1 log=$2; shift 2
  $sudo ip netns exec "$ns" sudo -u "$(id -un)" -E env XDG_RUNTIME_DIR=/tmp HOME="$HOME" \
    xvfb-run -a "$app" --route frontier:7 --cars 4 --no-enemies --internal 320x180 "$@" > "$log" 2>&1
}
run dt-host "$out/host.log" --host 27450 --password lantern --mood laughs --quit-after "$seconds" &
host_pid=$!
sleep 12
# Without the password: refused, WRONG PASSWORD, and the app ends on the console's word (from the command line a failed start is the end).
run dt-join "$out/refused.log" --join 10.77.0.1:27450 --quit-after 10 || true
grep -q "WRONG PASSWORD" "$out/refused.log" || { echo "FAIL: the join without the password wasn't refused as WRONG PASSWORD"; cat "$out/refused.log"; exit 1; }
# With it (in any case, note 450): aboard, and both run on until their time's up.
run dt-join "$out/joined.log" --join 10.77.0.1:27450 --password LANTERN --quit-after $((seconds - 16)) --capture "$out/joined.png" || true
wait $host_pid || true
grep -q "joining 10.77.0.1:27450" "$out/joined.log" || { echo "FAIL: the joiner never dialled"; cat "$out/joined.log"; exit 1; }
grep -q "WRONG PASSWORD\|couldn't start" "$out/joined.log" && { echo "FAIL: the join with the password failed"; cat "$out/joined.log"; exit 1; }
grep -q "crew: 2 aboard" "$out/host.log" || { echo "FAIL: the host never saw the joiner aboard"; cat "$out/host.log"; exit 1; }
grep -q "crew: 2 aboard" "$out/joined.log" || { echo "FAIL: the joiner never saw itself aboard"; cat "$out/joined.log"; exit 1; }
echo "OK: refused without the password, aboard with it; logs and the joiner's last frame in $out"
