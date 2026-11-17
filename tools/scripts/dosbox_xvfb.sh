#!/usr/bin/env bash
# DOSBox-X on a private virtual X screen with all sound off (never the user's desktop or speakers).
#
#   dosbox_xvfb.sh start :N GAME_DIR [COMMAND]   Xvfb :N + DOSBox-X with GAME_DIR mounted as C:, runs COMMAND
#                                                (default START). Use a scratch copy of the game, never a master.
#   dosbox_xvfb.sh key   :N KEY…                 send xdotool keys (Return Escape Up a …; "sleep:0.5" waits)
#   dosbox_xvfb.sh type  :N TEXT                 type a string
#   dosbox_xvfb.sh shot  :N OUT.png              screenshot of the DOSBox-X window
#   dosbox_xvfb.sh stop  :N                      SIGKILL DOSBox-X and the Xvfb on :N (only that display)
#
# Env: OUT (default /tmp/goldbox-dosbox-N: conf, logs), CYCLES (default 3000), MACHINE (default svga_s3; e.g. ega,
# vgaonly, cga, tandy), CONF_EXTRA (lines appended to the conf; sections allowed).
#
# Lessons carried over from OpenPhantasie (reverse-engineering/dos/working/xvfb, RE-REFERENCE.md):
# - `[mixer] nosound=true` alone still opened a FluidSynth MIDI stream on the real sink: MIDI, PC speaker, Tandy,
#   Sound Blaster and GUS are all switched off too, and DOSBox-X runs with PULSE_SINK on a null sink of its own.
# - DOSBox-X ignores SIGTERM and a window close pops a quit dialog: stop = SIGKILL (`quit warning=false` as well).
# - With no Xvfb up, SDL would open the window on the Wayland desktop: WAYLAND_DISPLAY is unset, SDL is forced to x11,
#   and start refuses to run unless the Xvfb answers.
# - A second start beside a running instance gives two emulators: start refuses if one is already on :N.
# - saveremark=false, or F12+S (save state) starts kdialog and the emulator waits.
set -euo pipefail

cmd=${1:?usage: dosbox_xvfb.sh start|key|type|shot|stop :N …}
disp=${2:?display, e.g. :91}
shift 2
[[ $disp =~ ^:[0-9]+$ ]] || { echo "display must look like :91" >&2; exit 1; }
num=${disp#:}
out=${OUT:-/tmp/goldbox-dosbox-$num}
sink=goldbox_dosbox_$num
export DISPLAY=$disp

xvfb_pid() { pgrep -x Xvfb -a | awk -v d="$disp" '$3 == d { print $1 }'; }
dosbox_pids() {
  local p
  for p in $(pgrep -x dosbox-x || true); do
    tr '\0' '\n' 2>/dev/null < "/proc/$p/environ" | grep -qx "DISPLAY=$disp" && echo "$p"
  done
  # Fallback when /proc/<pid>/environ is unreadable: the pid file, only while it still names a running dosbox-x.
  if [[ -f $out/dosbox.pid ]]; then
    p=$(cat "$out/dosbox.pid")
    [[ $(cat "/proc/$p/comm" 2>/dev/null) == dosbox-x ]] && echo "$p"
  fi
  return 0
}
window() {
  local w i
  for i in $(seq 1 100); do
    w=$(xdotool search --onlyvisible --name 'DOSBox' 2>/dev/null | head -1 || true)
    [[ -n $w ]] && { echo "$w"; return; }
    sleep 0.2
  done
  echo "no DOSBox-X window on $disp" >&2; exit 1
}

case $cmd in
  start)
    game=$(cd "${1:?GAME_DIR}" && pwd); run=${2:-START}
    running=$(dosbox_pids | head -1)
    [[ -z $running ]] || { echo "DOSBox-X already running on $disp (pid $running)" >&2; exit 1; }
    mkdir -p "$out"
    if [[ -z $(xvfb_pid) ]]; then
      Xvfb "$disp" -screen 0 1600x1200x24 -nolisten tcp >"$out/xvfb.log" 2>&1 &
      for _ in $(seq 1 50); do xdpyinfo >/dev/null 2>&1 && break; sleep 0.1; done
    fi
    xdpyinfo >/dev/null 2>&1 && [[ -n $(xvfb_pid) ]] || { echo "Xvfb $disp is not up; not starting DOSBox-X" >&2; exit 1; }
    if command -v pactl >/dev/null && ! pactl list short sinks | awk '{print $2}' | grep -qx "$sink"; then
      pactl load-module module-null-sink sink_name="$sink" > "$out/sink.module"
    fi
    cat > "$out/dosbox.conf" <<CONF
[sdl]
windowresolution=1280x960
output=surface
[dosbox]
machine=${MACHINE:-svga_s3}
quit warning=false
saveremark=false
[cpu]
cycles=${CYCLES:-3000}
[mixer]
nosound=true
[midi]
mpu401=none
mididevice=none
[speaker]
pcspeaker=false
tandy=off
[sblaster]
sbtype=none
[gus]
gus=false
${CONF_EXTRA:-}
[autoexec]
@echo off
mount c "$game"
c:
$run
CONF
    env -u WAYLAND_DISPLAY SDL_VIDEODRIVER=x11 PULSE_SINK=$sink setsid dosbox-x -conf "$out/dosbox.conf" > "$out/dosbox.log" 2>&1 &
    echo $! > "$out/dosbox.pid"
    w=$(window)
    echo "DOSBox-X window $w on $disp; conf $out/dosbox.conf"
    ;;
  key)
    w=$(window); xdotool windowfocus --sync "$w" 2>/dev/null || true
    for k in "$@"; do
      if [[ $k == sleep:* ]]; then sleep "${k#sleep:}"; continue; fi
      xdotool key --delay 80 "$k"; sleep 0.3
    done
    ;;
  type)
    w=$(window); xdotool windowfocus --sync "$w" 2>/dev/null || true
    xdotool type --delay 120 "${1:?text}"
    ;;
  shot)
    w=$(window); o=${1:?out file}
    xwd -silent -id "$w" | ffmpeg -loglevel error -y -f xwd_pipe -i - -frames:v 1 "$o" && echo "$o"
    ;;
  stop)
    for p in $(dosbox_pids); do kill -9 "$p" 2>/dev/null || true; done
    p=$(xvfb_pid)
    if [[ -n $p ]]; then
      kill "$p"; for _ in $(seq 1 50); do kill -0 "$p" 2>/dev/null || break; sleep 0.1; done
    fi
    for m in $(pactl list short modules 2>/dev/null | awk -v s="sink_name=$sink" '$0 ~ (s "([ \t]|$)") { print $1 }'); do
      pactl unload-module "$m" || true
    done
    ;;
  *) echo "unknown command $cmd" >&2; exit 1 ;;
esac
