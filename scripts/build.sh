#!/usr/bin/env bash
# examples:
#   ./scripts/build.sh webgl
#   ./scripts/build.sh webgl release
#   ./scripts/build.sh win64 dev
#   ./scripts/build.sh linux64 release
set -euo pipefail

usage() { sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; }

if [[ $# -eq 0 || "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  [[ $# -eq 0 ]] && exit 2 || exit 0
fi

platform="$1"
mode="${2:-dev}"

case "$platform" in
  webgl|win64|linux64|macos) ;;
  *) echo "error: unknown platform '$platform' (expected: webgl, win64, linux64, macos)" >&2; exit 2 ;;
esac

case "$mode" in
  dev|release) ;;
  *) echo "error: unknown mode '$mode' (expected: dev or release)" >&2; exit 2 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
proj="$(cd "$script_dir/.." && pwd)"

escape_json() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

write_webgl_env() {
  local build_dir="$proj/Build"
  local index_file="$build_dir/index.html"
  local arcviewer_base_url scoresaber_base_url scoresaber_api_url

  arcviewer_base_url="$(escape_json "${ARCVIEWER_BASE_URL:-https://scoresaber.com/}")"
  scoresaber_base_url="$(escape_json "${ARCVIEWER_SCORESABER_BASE_URL:-https://scoresaber.com/}")"
  scoresaber_api_url="$(escape_json "${ARCVIEWER_SCORESABER_API_URL:-https://scoresaber.com/api/v2/}")"

  cat > "$build_dir/arcviewer-env.js" <<EOF
window.arcviewerEnv = {
  arcViewerBaseUrl: "${arcviewer_base_url}",
  scoreSaberBaseUrl: "${scoresaber_base_url}",
  scoreSaberApiUrl: "${scoresaber_api_url}"
};
EOF

  if [[ -f "$index_file" ]] && ! grep -q 'arcviewer-env.js' "$index_file"; then
    local tmp_file
    tmp_file="$(mktemp)"
    awk '
      /<\/head>/ && !inserted {
        print "  <script src=\"arcviewer-env.js\"></script>"
        inserted = 1
      }
      { print }
    ' "$index_file" > "$tmp_file"
    mv "$tmp_file" "$index_file"
  fi
}

version_file="$proj/ProjectSettings/ProjectVersion.txt"
if [[ ! -f "$version_file" ]]; then
  echo "error: ProjectVersion.txt not found at $version_file" >&2
  exit 1
fi
unity_version="$(awk '/^m_EditorVersion:/ {print $2}' "$version_file")"
if [[ -z "$unity_version" ]]; then
  echo "error: could not parse Unity version from $version_file" >&2
  exit 1
fi

if [[ -n "${UNITY_PATH:-}" ]]; then
  unity="$UNITY_PATH"
else
  case "$(uname -s)" in
    Darwin)
      unity="/Applications/Unity/Hub/Editor/$unity_version/Unity.app/Contents/MacOS/Unity"
      ;;
    Linux)
      unity="$HOME/Unity/Hub/Editor/$unity_version/Editor/Unity"
      [[ -x "$unity" ]] || unity="/opt/unity/Editor/Unity"
      ;;
    *)
      echo "error: unsupported OS '$(uname -s)', set UNITY_PATH" >&2
      exit 1
      ;;
  esac
fi

if [[ ! -e "$unity" ]]; then
  echo "error: Unity binary not found at: $unity" >&2
  echo "install Unity $unity_version, or override with UNITY_PATH=/path/to/Unity" >&2
  exit 1
fi

if pgrep -f "Unity.*-projectPath.*$proj" >/dev/null 2>&1; then
  echo "error: Unity is already open on this project; close it first" >&2
  exit 1
fi

log="${LOG_FILE:-/tmp/arcviewer-$platform-$mode.log}"
echo "building $platform/$mode -> $proj/Build"
echo "log: $log"

ARCVIEWER_PLATFORM="$platform" ARCVIEWER_MODE="$mode" \
  "$unity" \
  -batchmode -nographics -quit \
  -disable-assembly-updater \
  -projectPath "$proj" \
  -executeMethod PlayerBuilder.Build \
  -logFile "$log"

mkdir -p "$proj/Build"
printf '%s %s\n' "$platform" "$mode" > "$proj/Build/last-built.txt"
if [[ "$platform" == "webgl" ]]; then
  write_webgl_env
fi
