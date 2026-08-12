#!/usr/bin/env bash
#
# Exports every page of docs/duckstore-backend-improved.drawio to a dark-theme SVG in docs/diagrams/.
# The per-bounded-context READMEs embed these directly.
#
# The dark background is baked into each file on purpose. draw.io exports with a transparent
# background, which would leave light theme text on a light page — unreadable wherever the README is
# rendered on a light background. A self-contained canvas renders the same everywhere.
#
# Re-run this whenever the .drawio changes — the committed SVGs are generated artefacts, and a stale
# one is worse than none because it looks authoritative.
#
# Requires the draw.io desktop app (headless CLI). macOS path below; override with DRAWIO_BIN.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$REPO_ROOT/docs/duckstore-backend-improved.drawio"
OUT="$REPO_ROOT/docs/diagrams"
DRAWIO_BIN="${DRAWIO_BIN:-/Applications/draw.io.app/Contents/MacOS/draw.io}"
CANVAS="#121212"   # draw.io's own dark canvas

[ -f "$SRC" ] || { echo "error: $SRC not found" >&2; exit 1; }
[ -x "$DRAWIO_BIN" ] || {
  echo "error: draw.io CLI not found at $DRAWIO_BIN" >&2
  echo "       install the draw.io desktop app, or set DRAWIO_BIN to its binary" >&2
  exit 1
}

mkdir -p "$OUT"

# Page order has changed before, so resolve each page's index from its name rather than hardcoding.
# Emits "<1-based index><TAB><kebab-case name>" per page. draw.io's -p is 1-based.
pages="$(python3 - "$SRC" <<'PY'
import re, sys, xml.etree.ElementTree as ET
tree = ET.parse(sys.argv[1])
for i, d in enumerate(tree.getroot().iter('diagram'), start=1):
    slug = re.sub(r'[^a-z0-9]+', '-', (d.get('name') or f'page{i}').lower()).strip('-')
    print(f"{i}\t{slug}")
PY
)"

count=0
while IFS=$'\t' read -r index slug; do
  [ -n "$index" ] || continue
  target="$OUT/$slug.svg"

  # No -t/--transparent: we want a solid canvas, added below.
  # --embed-svg-fonts false keeps files small (Helvetica is a system font).
  "$DRAWIO_BIN" -x -f svg -p "$index" --theme dark -b 10 --embed-svg-fonts false \
    -o "$target" "$SRC" >/dev/null 2>&1
  [ -s "$target" ] || { echo "error: export produced nothing for page $index ($slug)" >&2; exit 1; }

  python3 - "$target" "$CANVAS" <<'PY'
import re, sys
path, canvas = sys.argv[1], sys.argv[2]
svg = open(path, encoding='utf-8').read()

# 1. the root <svg> advertises a transparent background - make it the dark canvas
svg = re.sub(r'background:\s*transparent;\s*background-color:\s*transparent;',
             f'background: {canvas}; background-color: {canvas};', svg, count=1)

# 2. a CSS background only paints when the SVG is the document; as an <img> it can be ignored,
#    so paint an explicit rect as the first child too.
m = re.search(r'<svg\b[^>]*>', svg)
if not m:
    sys.exit(f'error: no <svg> element in {path}')
if 'data-canvas="1"' not in svg:
    rect = f'<rect data-canvas="1" x="0" y="0" width="100%" height="100%" fill="{canvas}"/>'
    svg = svg[:m.end()] + rect + svg[m.end():]

open(path, 'w', encoding='utf-8').write(svg)
PY

  printf '  %-14s -> %s\n' "$slug" "docs/diagrams/$slug.svg"
  count=$((count + 1))
done <<< "$pages"

echo "exported $count dark-theme pages to docs/diagrams/"
