#!/usr/bin/env bash
# StudyScope – Installation unter Linux
#
#   bash install.sh               Setup-Assistenten starten (Installation oder Update)
#   bash install.sh --uninstall   StudyScope deinstallieren
#   bash install.sh --check       nur die Voraussetzungen prüfen
#
# Das Skript installiert selbst nichts und ruft kein sudo auf: Es prüft die Systembibliotheken,
# nennt bei Bedarf den passenden Installationsbefehl und startet dann den grafischen Setup-Assistenten.
#
# Sprache der Ausgaben: Deutsch, wenn die Spracheinstellung mit „de“ beginnt (LC_ALL, sonst LC_MESSAGES,
# sonst LANG – dieselbe Reihenfolge wie bei anderen Programmen), sonst Englisch. Beide Fassungen stehen
# hier im Skript (Funktion msg), es gibt keine zusätzlichen Dateien. Der Setup-Assistent wählt seine
# Sprache selbst.
set -euo pipefail

cd "$(dirname "$(readlink -f "$0")")"
SETUP="./StudyScope.Setup"

case "${LC_ALL:-${LC_MESSAGES:-${LANG:-}}}" in
  de*) UI_LANG=de ;;
  *)   UI_LANG=en ;;
esac

# msg "<Deutsch>" "<English>" – gibt den Text in der Sprache der Ausgabe aus (ohne Zeilenumbruch)
msg() { if [ "$UI_LANG" = de ]; then printf '%s' "$1"; else printf '%s' "$2"; fi; }

fail() { printf '\n%s\n' "$*" >&2; exit 1; }

[ -f "$SETUP" ] || fail "$(msg \
  "StudyScope.Setup wurde neben install.sh nicht gefunden. Bitte das Archiv vollständig entpacken." \
  "StudyScope.Setup was not found next to install.sh. Extract the complete archive first.")"

ARCH="$(uname -m)"
[ "$ARCH" = "x86_64" ] || fail "$(msg \
  "StudyScope gibt es derzeit nur für 64-Bit-x86-Systeme (gefunden: $ARCH)." \
  "StudyScope is currently only available for 64-bit x86 systems (found: $ARCH).")"

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
    echo "$(msg "Für StudyScope fehlen Systembibliotheken:" "StudyScope needs system libraries that are missing:") ${LIST%, }"
    if [ -n "$CMD" ]; then
      msg "Bitte installieren Sie sie mit:" "Install them with:"; echo
      echo
      echo "    $CMD"
      echo
      echo "$(msg "und starten Sie danach erneut:" "and then run again:")  bash install.sh"
    else
      msg "Bitte installieren Sie über die Paketverwaltung Ihrer Distribution:" \
          "Install the following with your distribution's package manager:"; echo
      msg "  WebKitGTK 4.1 (libwebkit2gtk-4.1.so.0), GTK 3, libnotify und ICU." \
          "  WebKitGTK 4.1 (libwebkit2gtk-4.1.so.0), GTK 3, libnotify and ICU."; echo
    fi
  } >&2
  exit 1
fi

# ------------------------------------------------------------------ Optionale Empfehlung: Farb-Emoji-Schrift
# StudyScope nutzt Emoji als Symbole (z. B. in den Reitern). Ohne Emoji-Schrift erscheinen dort Kästchen.
has_emoji_font() {
  if command -v fc-list >/dev/null 2>&1; then
    fc-list 2>/dev/null | grep -qi 'emoji'
  else
    find /usr/share/fonts /usr/local/share/fonts "$HOME/.local/share/fonts" "$HOME/.fonts" \
      -iname '*emoji*' -print -quit 2>/dev/null | grep -q .
  fi
}

if ! has_emoji_font; then
  EMOJI_CMD=""
  if command -v apt-get >/dev/null 2>&1; then EMOJI_CMD="sudo apt install fonts-noto-color-emoji"
  elif command -v dnf >/dev/null 2>&1; then EMOJI_CMD="sudo dnf install google-noto-color-emoji-fonts"
  elif command -v pacman >/dev/null 2>&1; then EMOJI_CMD="sudo pacman -S noto-fonts-emoji"
  elif command -v zypper >/dev/null 2>&1; then EMOJI_CMD="sudo zypper install noto-coloremoji-fonts"
  fi
  echo
  msg "Empfehlung (optional): Es wurde keine Farb-Emoji-Schrift gefunden. StudyScope funktioniert auch ohne," \
      "Recommendation (optional): No color emoji font was found. StudyScope works without one,"; echo
  msg "zeigt dann aber einige Symbole (z. B. in den Reitern) als Kästchen an." \
      "but then shows some symbols (for example in the tabs) as boxes."; echo
  [ -n "$EMOJI_CMD" ] && echo "$(msg "Zum Nachinstallieren:" "To install it:")  $EMOJI_CMD"
  echo
fi

# ------------------------------------------------------------------ Optionale Empfehlung: ffmpeg
# Für den MP4-Export der Videos und den MP3-Export der Audio-Beiträge; StudyScope läuft auch ohne.
if ! command -v ffmpeg >/dev/null 2>&1; then
  FFMPEG_CMD=""
  if command -v apt-get >/dev/null 2>&1; then FFMPEG_CMD="sudo apt install ffmpeg"
  elif command -v dnf >/dev/null 2>&1; then FFMPEG_CMD="sudo dnf install ffmpeg"
  elif command -v pacman >/dev/null 2>&1; then FFMPEG_CMD="sudo pacman -S ffmpeg"
  elif command -v zypper >/dev/null 2>&1; then FFMPEG_CMD="sudo zypper install ffmpeg"
  fi
  echo
  msg "Hinweis (optional): ffmpeg wurde nicht gefunden. Ohne ffmpeg funktioniert StudyScope vollständig," \
      "Note (optional): ffmpeg was not found. StudyScope works fully without ffmpeg;"; echo
  msg "nur der Export von Videos als MP4 und von Audio als MP3 ist dann gesperrt." \
      "only exporting videos as MP4 and audio as MP3 is then unavailable."; echo
  [ -n "$FFMPEG_CMD" ] && echo "$(msg "Zum Nachinstallieren:" "To install it:")  $FFMPEG_CMD"
  echo
fi

if [ "${1:-}" = "--check" ]; then
  msg "Alle Voraussetzungen für StudyScope sind erfüllt." "All requirements for StudyScope are met."; echo
  exit 0
fi

# ------------------------------------------------------------------ Grafische Sitzung prüfen und Setup starten
[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] || fail "$(msg \
  "Keine grafische Sitzung gefunden. Bitte install.sh in einer Desktop-Sitzung starten." \
  "No graphical session found. Run install.sh in a desktop session.")"

chmod +x "$SETUP"
exec "$SETUP" "$@"
