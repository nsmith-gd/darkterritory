#!/bin/bash
# Starts a simulated OpenXR headset (Monado's "Simulated HMD") on a virtual display, so `dt vr check` and VrTests run
# without a headset (ARCHITECTURE §8 note 25). Needs, from apt: monado-service libopenxr1-monado libopenxr-loader1
# xvfb mesa-vulkan-drivers. Afterwards run things with XDG_RUNTIME_DIR=/tmp/xr.
set -euo pipefail
dir=/tmp/xr # short on purpose: Monado's IPC socket path has to fit a sockaddr_un (108 bytes)
display=:97
mkdir -p "$dir" && chmod 700 "$dir"
if [ -S "$dir/monado_comp_ipc" ] && pgrep -x monado-service >/dev/null; then
  echo "simulated headset already up: XDG_RUNTIME_DIR=$dir"
  exit 0
fi
rm -f "$dir/monado_comp_ipc" "$dir/monado.pid"
pgrep -f "Xvfb $display" >/dev/null || (Xvfb "$display" -screen 0 1280x720x24 >/dev/null 2>&1 &)
sleep 1
# monado-service polls stdin, so it needs a pipe that stays open rather than /dev/null.
(sleep infinity | DISPLAY="$display" XRT_COMPOSITOR_FORCE_XCB=1 XDG_RUNTIME_DIR="$dir" monado-service > /tmp/monado.log 2>&1 &)
for _ in $(seq 1 20); do
  [ -S "$dir/monado_comp_ipc" ] && break
  sleep 0.5
done
if [ ! -S "$dir/monado_comp_ipc" ]; then
  echo "monado-service didn't start; see /tmp/monado.log" >&2
  exit 1
fi
echo "simulated headset up: XDG_RUNTIME_DIR=$dir"
