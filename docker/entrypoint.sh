#!/bin/sh
set -eu

escape_json() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

arcviewer_base_url="$(escape_json "${ARCVIEWER_BASE_URL:-https://scoresaber.com/}")"
scoresaber_base_url="$(escape_json "${ARCVIEWER_SCORESABER_BASE_URL:-https://scoresaber.com/}")"
scoresaber_api_url="$(escape_json "${ARCVIEWER_SCORESABER_API_URL:-https://scoresaber.com/api/v2/}")"

cat > /usr/share/nginx/html/arcviewer-env.js <<EOF
window.arcviewerEnv = {
  arcViewerBaseUrl: "${arcviewer_base_url}",
  scoreSaberBaseUrl: "${scoresaber_base_url}",
  scoreSaberApiUrl: "${scoresaber_api_url}"
};
EOF

index_file=/usr/share/nginx/html/index.html
if [ -f "$index_file" ] && ! grep -q 'arcviewer-env.js' "$index_file"; then
  sed -i 's#</head>#  <script src="arcviewer-env.js"></script>\n</head>#' "$index_file"
fi
