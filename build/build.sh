#!/bin/sh
# Builds DBDE Lookup Cache with the .NET SDK's Roslyn compiler against a game install's own assemblies (no NuGet).
#
#   sh build/build.sh KK  "D:/Games/Koikatsu"            -> bin/KK_DBDELookupCache.dll
#   sh build/build.sh KKS "D:/Games/Koikatsu Sunshine"   -> bin/KKS_DBDELookupCache.dll
#
# Needs DynamicBoneDistributionEditor and the modding API (KKAPI / KKSAPI) installed in that game folder.
# CSC can be overridden, e.g. CSC="C:/Program Files/dotnet/sdk/8.0.425/Roslyn/bincore/csc.dll".
set -e
GAMEKIND="$1"
G="$2"
if [ -z "$GAMEKIND" ] || [ -z "$G" ]; then
    echo "usage: sh build/build.sh KK|KKS <game folder>"; exit 1
fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ -z "$CSC" ]; then
    CSC="$(ls -d "C:/Program Files/dotnet/sdk/"*/Roslyn/bincore/csc.dll 2>/dev/null | tail -1)"
fi
[ -f "$CSC" ] || { echo "csc.dll not found - install the .NET SDK or set CSC"; exit 1; }
find1() { find "$G/BepInEx/plugins" -iname "$1" | head -1; }

if [ "$GAMEKIND" = "KK" ]; then
    for d in Koikatu_Data CharaStudio_Data; do [ -d "$G/$d/Managed" ] && M="$G/$d/Managed" && break; done
    DBDE="$(find1 KK_DynamicBoneDistributionEditor.dll)"; API="$(find1 KKAPI.dll)"
    set -- "-r:$M/mscorlib.dll" "-r:$M/System.dll" "-r:$M/System.Core.dll" "-r:$M/UnityEngine.dll" "-r:$M/Assembly-CSharp.dll"
else
    for d in KoikatsuSunshine_Data CharaStudio_Data; do [ -d "$G/$d/Managed" ] && M="$G/$d/Managed" && break; done
    DBDE="$(find1 KKS_DynamicBoneDistributionEditor.dll)"; API="$(find1 KKSAPI.dll)"
    set -- "-r:$M/mscorlib.dll" "-r:$M/System.dll" "-r:$M/System.Core.dll" "-r:$M/netstandard.dll" \
           "-r:$M/UnityEngine.dll" "-r:$M/UnityEngine.CoreModule.dll" "-r:$M/Assembly-CSharp.dll"
fi
[ -n "$M" ] || { echo "Managed folder not found under $G"; exit 1; }
[ -n "$DBDE" ] || { echo "DynamicBoneDistributionEditor dll not found in $G/BepInEx/plugins"; exit 1; }
[ -n "$API" ] || { echo "KKAPI/KKSAPI dll not found in $G/BepInEx/plugins"; exit 1; }

mkdir -p "$ROOT/bin"
OUT="$ROOT/bin/${GAMEKIND}_DBDELookupCache.dll"
dotnet "$CSC" -nologo -noconfig -nostdlib+ -target:library -langversion:7.3 -optimize+ -deterministic -out:"$OUT" "$@" \
    "-r:$G/BepInEx/core/BepInEx.dll" "-r:$G/BepInEx/core/0Harmony.dll" "-r:$DBDE" "-r:$API" \
    "$ROOT/src/DBDELookupCache.cs"
echo "built $OUT"
