#!/usr/bin/env bash
# Baut das StudyScope-Installationsprogramm unter Linux (oder macOS) mit dem .NET SDK.
#
#   ./build/build-setup.sh              # linux-x64 (tar.gz mit install.sh)
#   ./build/build-setup.sh win-x64      # setup.exe für Windows
#   ./build/build-setup.sh all
#
# Schritte: StudyScope.UI veröffentlichen → payload.zip packen → Setup als Single-File mit
# eingebettetem Paket veröffentlichen → Auslieferung nach artifacts/. Version aus Directory.Build.props.
set -euo pipefail

RUNTIME="${1:-linux-x64}"
CONFIGURATION="${CONFIGURATION:-Release}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACTS="$ROOT/artifacts"

case "$RUNTIME" in
  linux-x64|win-x64) RUNTIMES=("$RUNTIME") ;;
  all) RUNTIMES=(win-x64 linux-x64) ;;
  *) echo "Unbekannte Runtime: $RUNTIME (erlaubt: linux-x64, win-x64, all)" >&2; exit 2 ;;
esac

VERSION="$(dotnet msbuild "$ROOT/StudyScope.UI/StudyScope.UI.csproj" -getProperty:Version | tr -d '[:space:]')"
[ -n "$VERSION" ] || { echo "Version konnte nicht gelesen werden." >&2; exit 1; }

for RID in "${RUNTIMES[@]}"; do
  echo
  echo "=== StudyScope $VERSION für $RID ==="
  APP_DIR="$ARTIFACTS/app/$RID"
  PAYLOAD="$ARTIFACTS/payload/StudyScope-$RID.zip"
  SETUP_DIR="$ARTIFACTS/setup/$RID"
  rm -rf "$APP_DIR" "$SETUP_DIR"
  mkdir -p "$(dirname "$PAYLOAD")"

  echo "[1/4] StudyScope.UI veröffentlichen"
  dotnet publish "$ROOT/StudyScope.UI/StudyScope.UI.csproj" -c "$CONFIGURATION" -r "$RID" --self-contained \
    -p:DebugType=none -o "$APP_DIR" --nologo -v quiet

  echo "[2/4] Installationspaket packen"
  dotnet msbuild "$ROOT/build/Zip.proj" "-p:SourceDirectory=$APP_DIR" "-p:DestinationFile=$PAYLOAD" -nologo -v:quiet

  echo "[3/4] Setup als Single-File veröffentlichen"
  dotnet publish "$ROOT/StudyScope.Setup/StudyScope.Setup.csproj" -c "$CONFIGURATION" -r "$RID" --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none "-p:PayloadZip=$PAYLOAD" -o "$SETUP_DIR" --nologo -v quiet

  echo "[4/4] Auslieferung erstellen"
  if [[ "$RID" == win-* ]]; then
    OUTPUT="$ARTIFACTS/StudyScope-$VERSION-Setup.exe"
    cp "$SETUP_DIR/StudyScope.Setup.exe" "$OUTPUT"
  else
    NAME="StudyScope-$VERSION-$RID"
    STAGE="$ARTIFACTS/stage/$NAME"
    rm -rf "$ARTIFACTS/stage"
    mkdir -p "$STAGE"
    install -m 755 "$SETUP_DIR/StudyScope.Setup" "$STAGE/StudyScope.Setup"
    install -m 755 "$ROOT/StudyScope.Setup/Packaging/linux/install.sh" "$STAGE/install.sh"
    OUTPUT="$ARTIFACTS/$NAME.tar.gz"
    tar -czf "$OUTPUT" -C "$ARTIFACTS/stage" "$NAME"
    rm -rf "$ARTIFACTS/stage"
  fi

  echo "Fertig: $OUTPUT ($(du -h "$OUTPUT" | cut -f1))"
  echo "SHA256: $(sha256sum "$OUTPUT" | cut -d' ' -f1)"
done
