#!/usr/bin/env bash
# Builds a release: the self-contained app, then a setup exe with that app embedded.
#
#   ./scripts/build-release.sh [release]
#
# Releases are named the way they ship: a1.0, a1.01, a1.1. The label becomes the folder name —
# dist/ScorePlus-win-x64-a1.1 — and is written into the app so the launch box shows it, while the
# numbers inside it stay numeric because the updater compares versions. Every release gets its own
# folder, so a new build never replaces the one you are testing.
set -euo pipefail

VERSION="${1:-a1.0}"
NUMERIC="$(printf '%s' "$VERSION" | sed -E 's/[^0-9.]+//g' | sed -E 's/^\.+//; s/\.+$//')"
if [[ -z "$NUMERIC" ]]; then
  echo "Release must carry a number (a1.1, 1.0.0, got '$VERSION')." >&2
  exit 1
fi
IFS='.' read -r -a PARTS <<< "$NUMERIC"
while [[ ${#PARTS[@]} -lt 3 ]]; do PARTS+=("0"); done
NUMERIC="${PARTS[0]}.${PARTS[1]}.${PARTS[2]}"
if [[ ! "$NUMERIC" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Release must look like a1.1 or 1.0.0 (got '$VERSION')." >&2
  exit 1
fi
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$ROOT/dist"
OUT="$DIST/ScorePlus-win-x64-$VERSION"
SETUP_BUILD="$DIST/.setup-build"
APP_BUILD="$DIST/.app-build"
PAYLOAD="$ROOT/src/ScorePlus.Setup/payload.zip"

echo "==> ScorePlus $VERSION (assembly $NUMERIC) → $OUT"
mkdir -p "$DIST"
rm -rf "$OUT" "$SETUP_BUILD" "$APP_BUILD"
mkdir -p "$OUT"

# The app is published off to the side, not into the release folder, because a release holds only
# the two files it ships as: ScorePlusPortable and ScorePlusSetup.
dotnet publish "$ROOT/src/Scoreboard.Desktop/Scoreboard.Desktop.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:Version="$NUMERIC" -p:AssemblyVersion="$NUMERIC.0" -p:FileVersion="$NUMERIC.0" \
  -p:InformationalVersion="$VERSION" \
  -o "$APP_BUILD" -v minimal

# The installer carries the finished app as a compressed payload, so one exe is all it needs.
python - "$APP_BUILD/ScorePlus.exe" "$PAYLOAD" <<'PY'
import sys, zipfile, os
app, payload = sys.argv[1], sys.argv[2]
if not os.path.exists(app):
    raise SystemExit(f"missing published app: {app}")
with zipfile.ZipFile(payload, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    archive.write(app, "ScorePlus.exe")
print(f"    payload {os.path.getsize(payload) / 1048576:.1f} MB")
PY

dotnet publish "$ROOT/src/ScorePlus.Setup/ScorePlus.Setup.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:Version="$NUMERIC" -p:AssemblyVersion="$NUMERIC.0" -p:FileVersion="$NUMERIC.0" \
  -o "$SETUP_BUILD" -v minimal

# Releases always carry the same two assets: ScorePlusPortable (run it from anywhere) and
# ScorePlusSetup (install it). Both leave the build folder under the names they are uploaded as.
cp "$SETUP_BUILD/ScorePlusSetup.exe" "$OUT/ScorePlusSetup.exe"
cp "$APP_BUILD/ScorePlus.exe" "$OUT/ScorePlusPortable.exe"

rm -rf "$SETUP_BUILD" "$APP_BUILD"
rm -f "$PAYLOAD"
cp "$ROOT/README.md" "$OUT/README.md" 2>/dev/null || true
cp "$ROOT/docs/updates.md" "$OUT/UPDATES.md" 2>/dev/null || true

echo
echo "==> Done"
ls -lh "$OUT" | sed 's/^/    /'
