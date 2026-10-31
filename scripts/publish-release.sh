#!/usr/bin/env bash
# Builds a release, writes the update manifest beside it, and (optionally) publishes
# both to a GitHub release, so "create a release" is the whole update ritual.
#
#   ./scripts/publish-release.sh 1.1.0                       # build + win.json, no upload
#   ./scripts/publish-release.sh 1.1.0 --release             # build, then gh release create/upload
#   ./scripts/publish-release.sh 1.1.0 --release --notes "Adds X and fixes Y"
#   ./scripts/publish-release.sh 1.1.0 --release --prerelease           # alpha/beta channel
#   ./scripts/publish-release.sh 1.1.0 --release --draft               # upload, stay unpublished
#
# The app reads GitHub's releases API by default, prereleases included, so a published release
# updates installed copies with no win.json to host. The manifest is still written (and attached)
# for other hosts and for the stable …/releases/latest/download/ URL.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION=""
NOTES=""
PUBLISH=0
DRAFT=0
PRE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --release) PUBLISH=1; shift ;;
    --draft) DRAFT=1; PUBLISH=1; shift ;;
    --prerelease|--pre) PRE=1; shift ;;
    --notes) NOTES="${2:-}"; shift 2 ;;
    --notes=*) NOTES="${1#--notes=}"; shift ;;
    -h|--help) awk 'NR>1 && /^#/ { sub(/^# ?/, ""); print; next } NR>1 { exit }' "${BASH_SOURCE[0]}"; exit 0 ;;
    -*) echo "Unknown option $1" >&2; exit 1 ;;
    *) VERSION="$1"; shift ;;
  esac
done

if [[ -z "$VERSION" ]]; then
  echo "Give the release to publish, e.g. ./scripts/publish-release.sh a1.1" >&2
  exit 1
fi
# The folder and the tag use the release label (a1.1); the manifest carries the numeric version
# the updater compares, so a1.1 and a1.01 are not mistaken for the same build.
NUMERIC="$(printf '%s' "$VERSION" | sed -E 's/[^0-9.]+//g' | sed -E 's/^\.+//; s/\.+$//')"
IFS='.' read -r -a PARTS <<< "$NUMERIC"
while [[ ${#PARTS[@]} -lt 3 ]]; do PARTS+=("0"); done
NUMERIC="${PARTS[0]}.${PARTS[1]}.${PARTS[2]}"
if [[ ! "$NUMERIC" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Release must look like a1.1 (got '$VERSION')." >&2
  exit 1
fi

OUT="$ROOT/dist/ScorePlus-win-x64-$VERSION"
# Every release carries exactly these two files, under exactly these names: the portable build
# and the installer. The updater looks for ScorePlusSetup by base name, so it keeps working
# whether or not the extension was uploaded.
SETUP="$OUT/ScorePlusSetup.exe"
PORTABLE="$OUT/ScorePlusPortable.exe"
MANIFEST="$OUT/win.json"

# The repository the feed will be served from, read from origin so a fork works unchanged.
ORIGIN="$(git -C "$ROOT" remote get-url origin 2>/dev/null || true)"
REPO="$(printf '%s' "$ORIGIN" | sed -E 's#^(git@github\.com:|https://github\.com/)##; s#\.git$##')"
if [[ ! "$REPO" =~ ^[^/]+/[^/]+$ ]]; then
  echo "Could not read owner/repo from origin ('$ORIGIN')." >&2
  exit 1
fi
BASE="https://github.com/$REPO/releases/latest/download"

"$ROOT/scripts/build-release.sh" "$VERSION"
[[ -f "$SETUP" ]] || { echo "Build produced no $SETUP" >&2; exit 1; }
[[ -f "$PORTABLE" ]] || { echo "Build produced no $PORTABLE" >&2; exit 1; }

HASH="$(sha256sum "$SETUP" | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')"
echo "==> checksum $HASH"

# JSON is written by python so quotes and newlines in the notes cannot break the manifest.
python - "$MANIFEST" "$NUMERIC" "$BASE/ScorePlusSetup.exe" "$HASH" "$NOTES" <<'PY'
import json, sys
path, version, url, sha, notes = sys.argv[1:6]
manifest = {"version": version, "url": url, "sha256": sha}
if notes.strip():
    manifest["notes"] = notes.strip()
with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(manifest, handle, indent=2)
    handle.write("\n")
print(f"    manifest {path}")
PY

TAG="$VERSION"
echo
echo "==> Installed copies read the built-in feed, so this release needs no address typed in:"
echo "    https://api.github.com/repos/$REPO/releases"
echo "    A manifest is also written for other hosts: $BASE/win.json"
echo "    (this release: $BASE/ScorePlusSetup.exe and ScorePlusPortable.exe, tagged $TAG)"

if [[ $PUBLISH -eq 0 ]]; then
  echo
  echo "Not uploaded. Re-run with --release to create the GitHub release, which is all an"
  echo "installed copy needs: it reads the releases API directly, prereleases included."
  exit 0
fi

command -v gh >/dev/null || { echo "GitHub CLI (gh) is not installed." >&2; exit 1; }

if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then
  echo "==> Release $TAG already exists, replacing its assets"
  gh release upload "$TAG" "$SETUP" "$PORTABLE" "$MANIFEST" --repo "$REPO" --clobber
else
  ARGS=(release create "$TAG" "$SETUP" "$PORTABLE" "$MANIFEST" --repo "$REPO" --title "ScorePlus $VERSION")
  if [[ -n "$NOTES" ]]; then ARGS+=(--notes "$NOTES"); else ARGS+=(--generate-notes); fi
  [[ $DRAFT -eq 1 ]] && ARGS+=(--draft)
  [[ $PRE -eq 1 ]] && ARGS+=(--prerelease)
  echo "==> Publishing $TAG to $REPO"
  gh "${ARGS[@]}"
fi

if [[ $DRAFT -eq 1 ]]; then
  echo
  echo "Uploaded as a draft. Installed copies ignore drafts, so nothing is offered until you publish it."
else
  echo
  echo "Published. Installed copies on the built-in feed see $VERSION on their next launch ($( [[ $PRE -eq 1 ]] && echo 'prerelease, ' ))no extra setup."
fi
