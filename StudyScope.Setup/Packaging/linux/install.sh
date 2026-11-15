#!/usr/bin/env bash
# StudyScope – Installation unter Linux
#
#   bash install.sh               Setup-Assistenten starten (Installation oder Update)
#   bash install.sh --uninstall   StudyScope deinstallieren
#   bash install.sh --check       nur die Voraussetzungen prüfen
#
# Das Skript installiert selbst nichts und ruft kein sudo auf: Es prüft die Systembibliotheken,
# nennt bei Bedarf den passenden Installationsbefehl und startet dann den grafischen Setup-Assistenten.
set -euo pipefail

cd "$(dirname "$(readlink -f "$0")")"
SETUP="./StudyScope.Setup"

fail() { printf '\n%s\n' "$*" >&2; exit 1; }

[ -f "$SETUP" ] || fail "StudyScope.Setup wurde neben install.sh nicht gefunden. Bitte das Archiv vollständig entpacken."

ARCH="$(uname -m)"
[ "$ARCH" = "x86_64" ] || fail "StudyScope gibt es derzeit nur für 64-Bit-x86-Systeme (gefunden: $ARCH)."

# ------------------------------------------------------------------ Bibliotheken prüfen
LDCONFIG="$(command -v ldconfig || echo /sbin/ldconfig)"
LIBS="$("$LDCONFIG" -p 2>/dev/null || true)"
has_lib() { printf '%s' "$LIBS" | grep -q "$1"; }

MISSING=()
has_lib 'libwebkit2gtk-4\.1\.so\.0' || MISSING+=(webkit)
has_lib 'libgtk-3\.so\.0'           || MISSING+=(gtk)
has_lib 'libnotify\.so\.4'          || MISSING+=(notify)
has_lib 'libicuuc\.so\.[0-9]'       || MISSING+=(icu)

if [ "${#MISSING[@]}" -gt 0 ]; then
  ID="" ; ID_LIKE=""
  # shellcheck disable=SC1091
  [ -r /etc/os-release ] && . /etc/os-release
  DISTRO="$ID $ID_LIKE"

  PKGS=() ; CMD=""
  if command -v apt-get >/dev/null 2>&1 || [[ "$DISTRO" == *debian* || "$DISTRO" == *ubuntu* ]]; then
    for m in "${MISSING[@]}"; do
      case "$m" in
        webkit) PKGS+=(libwebkit2gtk-4.1-0) ;;
        gtk)    if apt-cache show libgtk-3-0t64 >/dev/null 2>&1; then PKGS+=(libgtk-3-0t64); else PKGS+=(libgtk-3-0); fi ;;
        notify) PKGS+=(libnotify4) ;;
        icu)    ICU="$(apt-cache search --names-only '^libicu[0-9]+$' 2>/dev/null | cut -d' ' -f1 | sort -V | tail -1)"
                PKGS+=("${ICU:-libicu-dev}") ;;
      esac
    done
    CMD="sudo apt install ${PKGS[*]}"
  elif command -v dnf >/dev/null 2>&1; then
    for m in "${MISSING[@]}"; do
      case "$m" in webkit) PKGS+=(webkit2gtk4.1) ;; gtk) PKGS+=(gtk3) ;; notify) PKGS+=(libnotify) ;; icu) PKGS+=(libicu) ;; esac
    done
    CMD="sudo dnf install ${PKGS[*]}"
  elif command -v pacman >/dev/null 2>&1; then
    for m in "${MISSING[@]}"; do
      case "$m" in webkit) PKGS+=(webkit2gtk-4.1) ;; gtk) PKGS+=(gtk3) ;; notify) PKGS+=(libnotify) ;; icu) PKGS+=(icu) ;; esac
    done
    CMD="sudo pacman -S ${PKGS[*]}"
  elif command -v zypper >/dev/null 2>&1; then
    for m in "${MISSING[@]}"; do
      case "$m" in webkit) PKGS+=(libwebkit2gtk-4_1-0) ;; gtk) PKGS+=(libgtk-3-0) ;; notify) PKGS+=(libnotify4) ;; icu) PKGS+=(libicu) ;; esac
    done
    CMD="sudo zypper install ${PKGS[*]}"
  fi

  NAMES=()
  for m in "${MISSING[@]}"; do
    case "$m" in webkit) NAMES+=("WebKitGTK 4.1") ;; gtk) NAMES+=("GTK 3") ;; notify) NAMES+=("libnotify") ;; icu) NAMES+=("ICU") ;; esac
  done
  LIST="$(printf '%s, ' "${NAMES[@]}")"

  {
    echo
    echo "Für StudyScope fehlen Systembibliotheken: ${LIST%, }"
    if [ -n "$CMD" ]; then
      echo "Bitte installieren Sie sie mit:"
      echo
      echo "    $CMD"
      echo
      echo "und starten Sie danach erneut:  bash install.sh"
    else
      echo "Bitte installieren Sie über die Paketverwaltung Ihrer Distribution:"
      echo "  WebKitGTK 4.1 (libwebkit2gtk-4.1.so.0), GTK 3, libnotify und ICU."
    fi
  } >&2
  exit 1
fi

if [ "${1:-}" = "--check" ]; then
  echo "Alle Voraussetzungen für StudyScope sind erfüllt."
  exit 0
fi

# ------------------------------------------------------------------ Grafische Sitzung prüfen und Setup starten
[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] || fail "Keine grafische Sitzung gefunden. Bitte install.sh in einer Desktop-Sitzung starten."

chmod +x "$SETUP"
exec "$SETUP" "$@"
