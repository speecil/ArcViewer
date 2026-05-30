#!/usr/bin/env bash
# examples:
#   ./scripts/start.sh                       run the last build (or build webgl release)
#   ./scripts/start.sh --rebuild             rebuild the last config first
#   ./scripts/start.sh --port 8080           webgl server on a different port
#   ./scripts/start.sh -p 1234 --rebuild
set -euo pipefail

usage() { sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; }

rebuild=0
port=1183

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    -r|--rebuild) rebuild=1; shift ;;
    -p|--port) port="$2"; shift 2 ;;
    --port=*) port="${1#--port=}"; shift ;;
    *) echo "error: unknown arg '$1'" >&2; usage >&2; exit 2 ;;
  esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
proj="$(cd "$script_dir/.." && pwd)"
config_file="$proj/Build/last-built.txt"

artifact_for() {
  case "$1" in
    webgl)   echo "$proj/Build/index.html" ;;
    win64)   echo "$proj/Build/win64/ArcViewer.exe" ;;
    linux64) echo "$proj/Build/linux64/ArcViewer" ;;
    macos)   echo "$proj/Build/macos/ArcViewer.app" ;;
    *) echo "error: unknown platform '$1'" >&2; exit 2 ;;
  esac
}

escape_json() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

write_webgl_env() {
  local serve_dir="$1"
  local index_file="$serve_dir/index.html"
  local arcviewer_base_url scoresaber_base_url scoresaber_api_url

  arcviewer_base_url="$(escape_json "${ARCVIEWER_BASE_URL:-https://scoresaber.com/}")"
  scoresaber_base_url="$(escape_json "${ARCVIEWER_SCORESABER_BASE_URL:-https://scoresaber.com/}")"
  scoresaber_api_url="$(escape_json "${ARCVIEWER_SCORESABER_API_URL:-https://scoresaber.com/api/v2/}")"

  cat > "$serve_dir/arcviewer-env.js" <<EOF
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

if [[ -f "$config_file" ]]; then
  read -r platform mode < "$config_file"
else
  platform=webgl
  mode=release
fi

artifact="$(artifact_for "$platform")"
if [[ "$rebuild" == "1" || ! -e "$artifact" ]]; then
  "$script_dir/build.sh" "$platform" "$mode"
fi

case "$platform" in
  webgl)
    serve_dir="$proj/Build"
    write_webgl_env "$serve_dir"
    echo "serving $serve_dir at http://localhost:$port"
    PORT="$port" SERVE_DIR="$serve_dir" exec python3 - <<'PY'
import http.server, os, socketserver, sys
port = int(os.environ['PORT'])
os.chdir(os.environ['SERVE_DIR'])
class H(http.server.SimpleHTTPRequestHandler):
    extensions_map = {
        **http.server.SimpleHTTPRequestHandler.extensions_map,
        '.wasm': 'application/wasm',
        '.data': 'application/octet-stream',
    }
    def end_headers(self):
        path = self.path.split('?', 1)[0]
        if path.endswith('.br'):
            self.send_header('Content-Encoding', 'br')
        elif path.endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        super().end_headers()
    def guess_type(self, path):
        if path.endswith('.br') or path.endswith('.gz'):
            path = path[:-3]
        return super().guess_type(path)
socketserver.TCPServer.allow_reuse_address = True
with socketserver.TCPServer(('', port), H) as srv:
    try: srv.serve_forever()
    except KeyboardInterrupt: pass
PY
    ;;
  macos)
    open "$artifact"
    ;;
  linux64)
    chmod +x "$artifact" 2>/dev/null || true
    exec "$artifact"
    ;;
  win64)
    exec "$artifact"
    ;;
esac
