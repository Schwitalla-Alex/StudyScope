# 📚 StudyScope

> **Intelligente Desktop-Lernsoftware mit lokaler LLM-Unterstützung und Dokumenten-RAG.**

[![CI](https://github.com/Schwitalla-Alex/StudyScope/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/Schwitalla-Alex/StudyScope/actions/workflows/ci.yml)
![Lizenz](https://img.shields.io/badge/Lizenz-Apache%202.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Plattform](https://img.shields.io/badge/Plattform-Windows%20%7C%20Linux-lightgrey.svg)

**StudyScope** ist eine schlanke, plattformübergreifende Desktop-Anwendung für Windows und Linux zur gezielten Wissensabfrage auf Basis eigener Lernunterlagen. Aus Ihren Dokumenten erstellt StudyScope Karteikarten, Mindmaps, Präsentationen, Quizze, Datentabellen, Berichte, Audio-Zusammenfassungen und vertonte Erklärvideos. Dank Retrieval-Augmented Generation (RAG) durchsucht StudyScope Dokumente semantisch und beantwortet Fragen präzise anhand Ihrer Quellen – wahlweise vollständig offline und datenschutzfreundlich oder über gängige Cloud-KI-Dienste.

---

## ✨ Features

- 🔍 **Dokumenten-Chat (RAG):** Stellen Sie gezielte Fragen an Ihre Vorlesungsfolien, Skripte und Unterlagen.
- 🎴 **Automatische Karteikarten:** Generieren Sie auf Knopfdruck interaktive Lernkarten aus den geladenen Texten.
- 🧠 **Mindmap:** Die KI ordnet die Inhalte der aktivierten Dokumente als aufklappbaren Themenbaum an. Ein Klick auf ein Thema zeigt eine Kurzerklärung und stellt auf Wunsch eine passende Frage im Chat; Export als Markdown-Gliederung oder als Grafik über „Drucken / PDF“.
- 📽️ **Präsentation:** Folien mit Stichpunkten und Sprechernotizen – von der KI aus den Dokumenten erstellt oder sofort aus der Mindmap abgeleitet. Vortragsmodus im Vollbild (Pfeiltasten, Esc), Export als PDF (eine Folie pro Seite) und als PowerPoint-Datei (`.pptx`).
- ❓ **Quiz:** Themen selbst anlegen (mit zugeordneten Dokumenten) oder von der KI vorschlagen lassen, mehrere Themen zu einem Quiz mit 5–100 Fragen kombinieren. Fragetypen: Multiple Choice, Wahr/Falsch und mehrere richtige Antworten. Lernmodus (Rückmeldung mit Erklärung nach jeder Frage) oder Prüfungsmodus (Auswertung am Ende), Verlauf mit Statistik je Thema und „Falsche wiederholen“.
- 📊 **Datentabelle:** Strukturierte Angaben aus den aktivierten Dokumenten als Tabelle herausziehen lassen – Vorlagen Begriffe & Definitionen, Vergleich, Zeitleiste, Personen & Beiträge, Formeln & Kennzahlen oder eigene Vorgabe (optional mit eigenen Spalten), wahlweise auf ein Thema eingegrenzt. Jede Zeile nennt ihr Quelldokument; Fehlendes bleibt leer statt erfunden. Spalten per Klick sortieren (Zahlen und Datumsangaben richtig), filtern, Zellen per Doppelklick bearbeiten, Zeilen und Spalten hinzufügen, umbenennen oder löschen, „Weitere Zeilen ergänzen“ per KI (ohne Wiederholungen, bis 500 Zeilen). Mehrere Tabellen je Profil; Export als Excel (.xlsx), CSV, Markdown oder PDF.
- 🎧 **Audio-Zusammenfassung:** Aus den aktivierten Dokumenten ein Sprechskript schreiben lassen – als Zusammenfassung (eine Stimme), Podcast-Gespräch (Moderatorin und Experte) oder Kurz-Briefing, Länge kurz (~3 Min.), mittel (~8 Min.) oder lang (~15 Min.), optional mit Schwerpunkt. Das Skript ist in natürlicher gesprochener Sprache (ohne Aufzählungen, Abkürzungen ausgeschrieben) – in der Sprache der KI-Ergebnisse – und lässt sich bearbeiten. Vorlesen mit der **Systemstimme** (kostenlos, offline, Stimme passend zur Rolle – z. B. Moderatorin weiblich, Experte männlich –, Tempo einstellbar; Abspielen/Pause, Vor/Zurück, der gesprochene Abschnitt wird hervorgehoben, die Wiedergabe läuft beim Reiterwechsel weiter und lässt sich in der Statusleiste anhalten) oder mit **Gemini TTS** als Audiodatei (WAV, Podcast mit zwei Stimmen; vor dem Erzeugen werden Dauer, Dateigröße und geschätzte Kosten angezeigt, Fortschritt mit Abbrechen, Export als WAV oder – mit ffmpeg – als MP3). Die Sprachausgabe ist unabhängig von der Chat-KI wählbar – auch mit Ollama oder LM Studio, die selbst keine Sprache erzeugen; den Gemini-Schlüssel tragen Sie dafür unter Einstellungen › Sprachausgabe ein. Mehrere Beiträge je Profil; Export des Skripts als Text oder Markdown; Skripte lassen sich in Berichte einbetten. Unter Linux (WebKitGTK) steht die Systemstimme oft nicht zur Verfügung; der Reiter zeigt das vorab an.
- 🎬 **Videoübersicht:** Aus der Präsentation des Profils (oder aus neu erzeugten Folien nur für das Video) ein vertontes Erklärvideo machen: Die KI schreibt zu jeder Folie einen kurzen Sprechtext; hat die Präsentation ausführliche Sprechernotizen, lassen sich diese auch direkt übernehmen (ohne KI). Abgespielt wird wie ein Video – jede Folie bleibt so lange stehen, wie ihr Sprechtext dauert, die Stichpunkte erscheinen nacheinander, sanfte Übergänge, Vollbild (Esc beendet), Play/Pause, Vor/Zurück je Folie, Fortschrittsleiste zum Springen und Tempo. Vorgelesen wird mit derselben Sprachausgabe wie im Audio-Reiter: **Systemstimme** live oder **Gemini TTS** als Audiodatei – je Folie eine eigene Anfrage, damit die Folienwechsel exakt zum Ton passen; nach Änderungen werden nur die geänderten Folien neu erzeugt (Kosten, Dauer und Größe werden vorher angezeigt). Die Sprechtexte lassen sich je Folie bearbeiten; das Video speichert eine Kopie der Folien und meldet, wenn sich die Präsentation geändert hat („Folien aktualisieren“). Es läuft immer nur eine Wiedergabe (Audio oder Video); die laufende wird in der Statusleiste angezeigt. Mehrere Videos je Profil; Export der Sprechtexte als Text oder Markdown und des ganzen Videos als **interaktive HTML-Datei** (Folien, Stichpunkt-Einblendung, Überblendung, Play/Pause, Vor/Zurück, Fortschrittsleiste, Vollbild – mit eingebettetem Gemini-Ton synchron zu den Folien; ohne Gemini-Audio laufen die Folien stumm im festen Takt) oder – mit ffmpeg – als **MP4-Video** (1920 × 1080, H.264/AAC, Folien wie in der App, Stichpunkte nacheinander, Überblendung) sowie die Tonspur als MP3. Ungefähre Dateigröße und Exportdauer werden vorher angezeigt; der Export lässt sich abbrechen.
- 📑 **Berichte:** Aus den aktivierten Dokumenten einen Bericht erstellen lassen – Vorlagen Lernübersicht, Zusammenfassung/Briefing, Glossar, FAQ oder eigene Vorgabe. Der Bericht wird interaktiv angezeigt und kann Mindmap, Karteikarten, Selbsttest (Quizfragen), Präsentation, Datentabellen (per Klick sortierbar), Audio-Skripte und Video-Skripte (je als Text, aufklappbar) einbetten. Aus dem Video-Abschnitt startet „▶ Video abspielen“ das Video im Reiter „Video“; beim HTML-Export können die Videos auf Wunsch als Player mit Folien und Ton eingebettet werden. Mehrere Berichte je Profil; Export als Word (.docx), PDF, Markdown, Text oder interaktive HTML-Datei.
- 📁 **Unterstützte Formate:** Volle Unterstützung für `.pdf`, `.txt` und `.csv`.
- 🗂️ **Profilverwaltung:** Strukturieren Sie Unterlagen nach Semestern, Schulfächern oder Themengebieten.
- ◀ **Mehr Platz:** Die linke Seitenleiste lässt sich mit ◀ bzw. **Strg+B** einklappen; eingeklappt bleibt eine schmale Leiste mit ▶ und der Anzahl aktiver Dokumente (z. B. „📄 1/2“). Der Zustand wird gemerkt, bei sehr schmalem Fenster (unter 900 px) klappt sie automatisch ein. Die Breite lässt sich an der Trennlinie mit der Maus ziehen (oder per Tastatur mit ← → Pos1 Ende), Doppelklick stellt die Standardbreite wieder her; lange Dokumentnamen zeigen den vollen Namen als Tooltip.
- ☰ **Aufgeräumte Oberfläche:** Datei öffnen, neues Profil, Einstellungen (direkt im passenden Bereich) und Hilfe liegen im Menü ☰ oben links – mit Maus oder Tastatur bedienbar (Pfeiltasten, Enter, Esc). Die Reiter bleiben immer einzeilig; was bei schmalem Fenster nicht mehr passt, steht unter „» Mehr“.
- 🔒 **Volle Datenkontrolle & Flexibilität:**
  - **Lokale KI:** Nutzen Sie lokale Sprachmodelle via **Ollama** oder **LM Studio** ganz ohne Internetverbindung und API-Kosten.
  - **Cloud-KI:** Anbindung an **Google Gemini**, **OpenAI (GPT-4)** und **Mistral AI**. „Speichern & Verbinden“ prüft den API-Schlüssel über die kostenlose Modellliste.
  - **Benutzerdefiniert:** Beliebige OpenAI-kompatible Endpunkte (Groq, DeepSeek, vLLM, OpenRouter).
- 🖨️ **Export & Druck:** Karteikarten als Markdown (`.md`) oder DIN-A4-Druckbögen; Mindmap, Präsentation, Datentabellen und Berichte mit eigenen Exporten (siehe oben).

---

## 🚀 Download & Installation

Für reguläre Anwender ist keine Installation von .NET oder Programmier-Tools erforderlich. Die Installationsprogramme finden Sie im Bereich **[Releases](https://github.com/Schwitalla-Alex/StudyScope/releases)**, was sich in den einzelnen Versionen geändert hat, im **[CHANGELOG](CHANGELOG.md)**.

### Windows
1. Laden Sie `StudyScope-<Version>-Setup.exe` herunter und starten Sie die Datei.
2. Folgen Sie dem Assistenten. Installiert wird nur für Ihr Benutzerkonto (Standard: `%LOCALAPPDATA%\Programs\StudyScope`), Administratorrechte sind nicht nötig.
3. Deinstallation über **Einstellungen ➔ Apps ➔ Installierte Apps ➔ StudyScope**.

Voraussetzung ist die Microsoft Edge WebView2-Runtime (unter Windows 11 vorinstalliert). Fehlt sie, weist das Setup darauf hin.

Setup und `install.sh` erscheinen in der Sprache Ihres Systems: Deutsch auf deutschsprachigen Systemen, sonst Englisch.

> **Hinweis zur SmartScreen-Warnung:** Die `setup.exe` ist (noch) nicht digital signiert. Windows zeigt deshalb beim ersten Start „Der Computer wurde durch Windows geschützt“ an. Klicken Sie auf **Weitere Informationen** und dann auf **Trotzdem ausführen**. Wer sichergehen will, prüft vorher die SHA-256-Prüfsumme (siehe [Download prüfen](#download-prüfen-sha-256)).

### Linux
1. `StudyScope-<Version>-linux-x64.tar.gz` herunterladen.
2. Im Terminal entpacken und das Installationsskript starten:
   ```bash
   tar -xzf StudyScope-<Version>-linux-x64.tar.gz
   cd StudyScope-<Version>-linux-x64
   bash install.sh
   ```
3. Fehlen Systembibliotheken, nennt das Skript den passenden Befehl (z. B. `sudo apt install …`). Diesen ausführen und danach erneut `bash install.sh` starten.

`install.sh` prüft die benötigten Systembibliotheken (WebKitGTK 4.1, GTK 3, libnotify, ICU), nennt bei Bedarf den passenden Installationsbefehl Ihrer Distribution und startet dann den Setup-Assistenten. Installiert wird nach `~/.local/share/StudyScope`, mit Eintrag im Anwendungsmenü. Deinstallation über den Menüeintrag (Rechtsklick ➔ „StudyScope deinstallieren“) oder mit `bash install.sh --uninstall`.

### Download-Links
Diese Links zeigen immer auf die neueste veröffentlichte Version und eignen sich z. B. für eine Homepage:

- Windows: <https://github.com/Schwitalla-Alex/StudyScope/releases/latest/download/StudyScope-Setup.exe>
- Linux (x86_64): <https://github.com/Schwitalla-Alex/StudyScope/releases/latest/download/StudyScope-linux-x64.tar.gz>

Die Dateien sind inhaltlich identisch mit den Dateien mit Versionsnummer im selben Release (gleiche Prüfsumme). Das Linux-Archiv entpackt sich weiterhin in einen Ordner mit Versionsnummer:
```bash
tar -xzf StudyScope-linux-x64.tar.gz
cd StudyScope-*-linux-x64
bash install.sh
```

### Download prüfen (SHA-256)
Zu jedem Release stehen in der Beschreibung die SHA-256-Prüfsummen der Dateien, zusätzlich liegt die Datei `SHA256SUMS.txt` bei. Stimmt die Prüfsumme Ihrer heruntergeladenen Datei mit der angegebenen überein, ist die Datei vollständig und unverändert.

- **Windows (PowerShell)** im Download-Ordner:
  ```powershell
  Get-FileHash .\StudyScope-<Version>-Setup.exe -Algorithm SHA256
  ```
  Den angezeigten Wert (`Hash`) mit der Tabelle im Release vergleichen (Groß-/Kleinschreibung spielt keine Rolle).
- **Linux** im Download-Ordner:
  ```bash
  sha256sum StudyScope-<Version>-linux-x64.tar.gz
  # oder automatisch gegen die mitgelieferte Liste:
  sha256sum -c --ignore-missing SHA256SUMS.txt
  ```

Ihre Profile und Einstellungen (Windows: `%APPDATA%\StudyScope`, Linux: `~/.config/StudyScope`) bleiben bei Updates erhalten und werden bei der Deinstallation nur auf ausdrücklichen Wunsch gelöscht.

### Optional: ffmpeg für MP4- und MP3-Export
StudyScope funktioniert vollständig ohne ffmpeg. Nur für den Export von Videos als **MP4** und von Audio als **MP3** (auch als kleinere Tonspur in HTML-Exporten) wird das kostenlose Programm [ffmpeg](https://ffmpeg.org) genutzt. Es wird nicht mitgeliefert:

- Windows: `winget install Gyan.FFmpeg` in der Eingabeaufforderung, danach StudyScope neu starten.
- Linux: `sudo apt install ffmpeg` (Fedora: `sudo dnf install ffmpeg`).

StudyScope sucht ffmpeg automatisch (PATH, unter Windows auch im WinGet-Ordner). Wird es nicht gefunden, lässt sich der Pfad unter **☰ ➔ Einstellungen ➔ Export** eintragen und mit „ffmpeg suchen/testen“ prüfen. Ohne ffmpeg sind die Knöpfe ausgegraut und nennen den Grund.

---

## 🌐 Sprache

StudyScope gibt es auf Deutsch und Englisch. Unter **☰ ➔ Einstellungen ➔ 🎨 Darstellung ➔ Sprache** wählen Sie „Wie System“ (Standard), „Deutsch“ oder „English“; die Oberfläche wechselt beim Speichern sofort. Bei „Wie System“ erscheint StudyScope auf deutschsprachigen Systemen auf Deutsch, sonst auf Englisch. Das Installationsprogramm richtet sich immer nach der Systemsprache.

**Sprache der KI-Ergebnisse:** Unabhängig von der Oberfläche legen Sie unter **☰ ➔ Einstellungen ➔ 🤖 KI-Modell ➔ Sprache der KI-Ergebnisse** je Profil fest, in welcher Sprache die KI schreibt: „Wie Oberfläche“ (Standard), „Wie die Dokumente“ (erkennt zurzeit Deutsch und Englisch, sonst gilt die Oberfläche) oder fest „Deutsch“ bzw. „English“. Neue Ergebnisse entstehen in dieser Sprache, vorhandene behalten ihre – auch wenn Sie sie ergänzen. Exporte erscheinen in der Sprache des jeweiligen Ergebnisses; weicht sie von der Oberfläche ab, zeigen Listen und Kopfzeilen ein Kennzeichen wie „EN“. Audio-Beiträge und Videos werden in ihrer Sprache vorgelesen; die Systemstimme braucht dafür eine installierte Stimme der Sprache (StudyScope zeigt an, wenn sie fehlt), eine selbst gewählte Stimme gilt je Sprache.

## ⚙️ KI-Einrichtung

Öffnen Sie in StudyScope oben links das Menü **☰ ➔ Einstellungen ➔ KI-Modell**:

- **Lokale Modelle (Ollama / LM Studio):**
  Starten Sie Ihre lokale Instanz (z. B. Port `11434` oder `1234`), wählen Sie im Menü den entsprechenden Provider und klicken Sie auf *Modelle suchen*.
- **Cloud-Modelle (OpenAI / Gemini / Mistral):**
  Wählen Sie den Anbieter aus, tragen Sie Ihren API-Schlüssel ein, rufen Sie die Modelle online ab und wählen Sie eines aus. „Speichern & Verbinden“ meldet einen abgelehnten Schlüssel oder eine Störung des Dienstes.
- **Benutzerdefiniert:**
  Geben Sie Ihre individuelle Base-URL (z. B. `https://api.groq.com/openai/v1`), den Modellnamen und optional Ihren Bearer-Token/API-Key ein.

---

## ❤️ StudyScope unterstützen

StudyScope ist freie Open-Source-Software unter der [Apache License 2.0](LICENSE) und bleibt kostenlos – ohne Werbung, ohne Abo und ohne Funktionen, die erst nach einer Spende freigeschaltet werden. Wenn Ihnen StudyScope beim Lernen hilft, freue ich mich über eine freiwillige Unterstützung:

**[❤ Spenden über PayPal](https://www.paypal.com/ncp/payment/JS8K7QTNVQGN8)**

In der App finden Sie denselben Link unter **☰ ➔ Hilfe ➔ StudyScope unterstützen**. Genauso willkommen sind Fehlerberichte und Ideen über die [Issues](https://github.com/Schwitalla-Alex/StudyScope/issues).

---

## 🛠️ Für Entwickler

### Voraussetzungen
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (oder neuer)
- Visual Studio 2022 / VS Code / JetBrains Rider
- Unter Linux zusätzlich WebKitGTK 4.1, GTK 3 und libnotify (benötigt von Photino 4 für die Oberfläche) sowie ICU (benötigt von .NET), z. B. unter Ubuntu 26.04:
  ```bash
  sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0t64 libnotify4 libicu78
  ```
  Für den MP4-Export (Folienbilder mit SkiaSharp) zusätzlich fontconfig (`libfontconfig1`, mit GTK meist schon vorhanden); optional `ffmpeg` für MP4/MP3 und `fonts-noto-color-emoji` für Emojis auf den Folien.
  Das ICU-Paket trägt die Versionsnummer im Namen und unterscheidet sich je nach Ubuntu-Version (Ubuntu 24.04: `libicu74`, Ubuntu 26.04: `libicu78`). Den passenden Namen zeigt `apt-cache search --names-only '^libicu[0-9]+$'`.

### Quellcode ausführen
```bash
# Repository klonen
git clone https://github.com/Schwitalla-Alex/StudyScope.git
cd StudyScope

# Abhängigkeiten wiederherstellen
dotnet restore

# Anwendung starten
dotnet run --project StudyScope.UI
```

### Installationsprogramm bauen
```bash
# Windows (PowerShell) – setup.exe und/oder Linux-Paket nach artifacts/
.\build\build-setup.ps1 -Runtime all

# Linux
./build/build-setup.sh all
```
Mit `-OutputDirectory <ordner>` (PowerShell) bzw. der Umgebungsvariable `STUDYSCOPE_ARTIFACTS_DIR=<ordner>` landen die Pakete in einem anderen Ordner; vorhandene Dateien in `artifacts/` bleiben dann unberührt.

### Automatische Prüfungen (CI)
Jeder Pull Request nach `master` und jeder Push auf `master` wird mit GitHub Actions unter Windows und Linux gebaut und getestet (inklusive der Tests mit echtem ffmpeg und SkiaSharp). Nach dem Merge auf `master` entstehen zusätzlich die Installationspakete als Download (Reiter **Actions** › Lauf › *Artifacts*, 7 Tage aufbewahrt).

### Release erstellen
Version in `Directory.Build.props` anheben und den Abschnitt `## [<Version>]` in `CHANGELOG.md` ergänzen (per Pull Request). Nach dem Merge auf `master` einen Tag `v<Version>` setzen und pushen – der Workflow `release.yml` prüft Tag, Version und CHANGELOG, baut und testet, erzeugt setup.exe, linux-x64.tar.gz und Prüfsummen und legt ein GitHub-Release als **Entwurf** an, das von Hand veröffentlicht wird.
