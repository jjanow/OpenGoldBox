#!/usr/bin/env bash
# Start the Gold Box Client. Builds it first when it is missing or a source file is newer than the last build; every
# other argument goes to the Client.
#
#   ./play.sh --game ~/games/POOLRAD      first start: the player's Pool of Radiance (DOS) folder (remembered;
#                                         imported into ~/.cache/goldbox/modules/ first, again when it changes)
#   ./play.sh                             load save A of that folder
#   ./play.sh --module DIR                play a module made by goldbox-import (no game folder needed)
#   ./play.sh --save B                    load SAVGAMB.DAT
#   ./play.sh --area 20 --at 15,4,W       enter area script 20 at 15,4 facing west (no save)
#   ./play.sh --scale 4                   bigger window (4 × 320×200)
#   ./play.sh --rebuild ...               force a build first
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$root/src/GoldBox.Client"
out="$project/bin/Debug/net10.0"
dll="$out/GoldBox.Client.dll"
stamp="$out/.play-built"

rebuild=0
args=()
for a in "$@"; do
  case "$a" in
    --rebuild) rebuild=1 ;;
    -h|--help) sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) args+=("$a") ;;
  esac
done

if [[ $rebuild == 1 || ! -f $stamp ]] || [[ -n $(find "$root/src" \( -name '*.cs' -o -name '*.csproj' \) -newer "$stamp" -print -quit) ]]; then
  nice -n 19 ionice -c 3 dotnet build "$project" -v q -nologo >&2
  touch "$stamp"
fi
exec dotnet "$dll" "${args[@]}"
