#!/bin/sh
# Builds both variants and copies the release assets to dist/:
#
#   sh build/package.sh "D:/Games/Koikatsu" "D:/Games/Koikatsu Sunshine"
#
#   dist/KK_DBDELookupCache.dll    Koikatsu / Koikatsu Party
#   dist/KKS_DBDELookupCache.dll   Koikatsu Sunshine
#
# README and LICENSE live on the repository page; source code archives are attached to GitHub releases
# automatically from the tag.
set -e
KK_DIR="$1"
KKS_DIR="$2"
if [ -z "$KK_DIR" ] || [ -z "$KKS_DIR" ]; then
    echo "usage: sh build/package.sh <Koikatsu folder> <Koikatsu Sunshine folder>"; exit 1
fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's/.*public const string Version = "\([0-9.]*\)".*/\1/p' "$ROOT/src/DBDELookupCache.cs")"

sh "$ROOT/build/build.sh" KK "$KK_DIR"
sh "$ROOT/build/build.sh" KKS "$KKS_DIR"

rm -rf "$ROOT/dist"
mkdir -p "$ROOT/dist"
cp "$ROOT/bin/KK_DBDELookupCache.dll" "$ROOT/bin/KKS_DBDELookupCache.dll" "$ROOT/dist/"
echo "release assets for v$VERSION:"
ls -l "$ROOT/dist"
