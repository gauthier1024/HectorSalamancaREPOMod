#!/usr/bin/env bash
set -euo pipefail

NAME="HectorSalamanca"
VERSION="1.0.0"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -f "$ROOT/.env" ]; then
  echo ".env not found. Copy .env.example to .env and fill it in." >&2
  exit 1
fi

set -a
source "$ROOT/.env"
set +a

: "${GAME_DIR:?GAME_DIR is not set in .env}"
: "${BUNDLE:?BUNDLE is not set in .env}"
IMAGES_DIR="${IMAGES_DIR:-$ROOT/images}"
OUT="$ROOT/dist"
STAGE="$OUT/stage"

if [ ! -d "$GAME_DIR/REPO_Data/Managed" ]; then
  echo "GAME_DIR is invalid, REPO_Data/Managed not found: $GAME_DIR" >&2
  exit 1
fi

if [ ! -f "$BUNDLE" ]; then
  echo "Bundle not found: $BUNDLE" >&2
  exit 1
fi

dotnet build "$ROOT/Plugin/$NAME.csproj" -c Release -p:GameDir="$GAME_DIR"

rm -rf "$OUT"
mkdir -p "$STAGE"

cp "$ROOT/Package/manifest.json" "$ROOT/Package/README.md" "$ROOT/Package/icon.png" "$STAGE/"
cp "$ROOT/Plugin/bin/Release/netstandard2.1/$NAME.dll" "$STAGE/"
cp "$BUNDLE" "$STAGE/hectorassets"

for image in thumbnail.png chibi.png chibitalk.png; do
  if [ -f "$IMAGES_DIR/$image" ]; then
    cp "$IMAGES_DIR/$image" "$STAGE/$image"
  else
    echo "Warning: $IMAGES_DIR/$image not found; the reference character's image will be used in game." >&2
  fi
done

(cd "$STAGE" && python3 -m zipfile -c "$OUT/$NAME-$VERSION.zip" *)
echo "Created $OUT/$NAME-$VERSION.zip"
