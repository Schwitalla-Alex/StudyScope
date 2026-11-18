# 📚 StudyScope

> **Intelligente Desktop-Lernsoftware mit lokaler LLM-Unterstützung und Dokumenten-RAG.**

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
- 🎧 **Audio-Zusammenfassung:** Aus den aktivierten Dokumenten ein Sprechskript schreiben lassen – als Zusammenfassung (eine Stimme), Podcast-Gespräch (Moderatorin und Experte) oder Kurz-Briefing, Länge kurz (~3 Min.), mittel (~8 Min.) oder lang (~15 Min.), optional mit Schwerpunkt. Das Skript ist in natürlichem gesprochenem Deutsch (ohne Aufzählungen, Abkürzungen ausgeschrieben) und lässt sich bearbeiten. Vorlesen mit der **Systemstimme** (kostenlos, offline, Stimme passend zur Rolle – z. B. Moderatorin weiblich, Experte männlich –, Tempo einstellbar; Abspielen/Pause, Vor/Zurück, der gesprochene Abschnitt wird hervorgehoben, die Wiedergabe läuft beim Reiterwechsel weiter und lässt sich in der Statusleiste anhalten) oder mit **Gemini TTS** als Audiodatei (WAV, Podcast mit zwei Stimmen; vor dem Erzeugen werden Dauer, Dateigröße und geschätzte Kosten angezeigt, Fortschritt mit Abbrechen, Export als WAV). Die Sprachausgabe ist unabhängig von der Chat-KI wählbar – auch mit Ollama oder LM Studio, die selbst keine Sprache erzeugen; den Gemini-Schlüssel tragen Sie dafür unter Einstellungen › Sprachausgabe ein. Mehrere Beiträge je Profil; Export des Skripts als Text oder Markdown; Skripte lassen sich in Berichte einbetten. Unter Linux (WebKitGTK) steht die Systemstimme oft nicht zur Verfügung; der Reiter zeigt das vorab an.
- 🎬 **Videoübersicht:** Aus der Präsentation des Profils (oder aus neu erzeugten Folien nur für das Video) ein vertontes Erklärvideo machen: Die KI schreibt zu jeder Folie einen kurzen Sprechtext; hat die Präsentation ausführliche Sprechernotizen, lassen sich diese auch direkt übernehmen (ohne KI). Abgespielt wird wie ein Video – jede Folie bleibt so lange stehen, wie ihr Sprechtext dauert, die Stichpunkte erscheinen nacheinander, sanfte Übergänge, Vollbild (Esc beendet), Play/Pause, Vor/Zurück je Folie, Fortschrittsleiste zum Springen und Tempo. Vorgelesen wird mit derselben Sprachausgabe wie im Audio-Reiter: **Systemstimme** live oder **Gemini TTS** als Audiodatei – je Folie eine eigene Anfrage, damit die Folienwechsel exakt zum Ton passen; nach Änderungen werden nur die geänderten Folien neu erzeugt (Kosten, Dauer und Größe werden vorher angezeigt). Die Sprechtexte lassen sich je Folie bearbeiten; das Video speichert eine Kopie der Folien und meldet, wenn sich die Präsentation geändert hat („Folien aktualisieren“). Es läuft immer nur eine Wiedergabe (Audio oder Video); die laufende wird in der Statusleiste angezeigt. Mehrere Videos je Profil; Export der Sprechtexte als Text oder Markdown und des ganzen Videos als **interaktive HTML-Datei** (Folien, Stichpunkt-Einblendung, Überblendung, Play/Pause, Vor/Zurück, Fortschrittsleiste, Vollbild – mit eingebettetem Gemini-Ton synchron zu den Folien; ohne Gemini-Audio laufen die Folien stumm im festen Takt). Die ungefähre Dateigröße wird vorher angezeigt.
- 📑 **Berichte:** Aus den aktivierten Dokumenten einen Bericht erstellen lassen – Vorlagen Lernübersicht, Zusammenfassung/Briefing, Glossar, FAQ oder eigene Vorgabe. Der Bericht wird interaktiv angezeigt und kann Mindmap, Karteikarten, Selbsttest (Quizfragen), Präsentation, Datentabellen (per Klick sortierbar), Audio-Skripte und Video-Skripte (je als Text, aufklappbar) einbetten. Aus dem Video-Abschnitt startet „▶ Video abspielen“ das Video im Reiter „Video“; beim HTML-Export können die Videos auf Wunsch als Player mit Folien und Ton eingebettet werden. Mehrere Berichte je Profil; Export als Word (.docx), PDF, Markdown, Text oder interaktive HTML-Datei.
- 📁 **Unterstützte Formate:** Volle Unterstützung für `.pdf`, `.txt` und `.csv`.
- 🗂️ **Profilverwaltung:** Strukturieren Sie Unterlagen nach Semestern, Schulfächern oder Themengebieten.
- 🔒 **Volle Datenkontrolle & Flexibilität:**
  - **Lokale KI:** Nutzen Sie lokale Sprachmodelle via **Ollama** oder **LM Studio** ganz ohne Internetverbindung und API-Kosten.
  - **Cloud-KI:** Anbindung an **Google Gemini**, **OpenAI (GPT-4)** und **Mistral AI**. „Speichern & Verbinden“ prüft den API-Schlüssel über die kostenlose Modellliste.
  - **Benutzerdefiniert:** Beliebige OpenAI-kompatible Endpunkte (Groq, DeepSeek, vLLM, OpenRouter).
- 🖨️ **Export & Druck:** Karteikarten als Markdown (`.md`) oder DIN-A4-Druckbögen; Mindmap, Präsentation, Datentabellen und Berichte mit eigenen Exporten (siehe oben).

---

## 🚀 Download & Installation

Für reguläre Anwender ist keine Installation von .NET oder Programmier-Tools erforderlich. Die Installationsprogramme finden Sie im Bereich **[Releases](https://github.com/Schwitalla-Alex/StudyScope/releases)**.

### Windows
1. Laden Sie `StudyScope-<Version>-Setup.exe` herunter und starten Sie die Datei.
2. Folgen Sie dem Assistenten. Installiert wird nur für Ihr Benutzerkonto (Standard: `%LOCALAPPDATA%\Programs\StudyScope`), Administratorrechte sind nicht nötig.
3. Deinstallation über **Einstellungen ➔ Apps ➔ Installierte Apps ➔ StudyScope**.

Voraussetzung ist die Microsoft Edge WebView2-Runtime (unter Windows 11 vorinstalliert). Fehlt sie, weist das Setup darauf hin.

> **Hinweis zur SmartScreen-Warnung:** Die `setup.exe` ist (noch) nicht digital signiert. Windows zeigt deshalb beim ersten Start „Der Computer wurde durch Windows geschützt“ an. Klicken Sie auf **Weitere Informationen** und dann auf **Trotzdem ausführen**. Wer sichergehen will, vergleicht vorher die SHA256-Prüfsumme aus den Release-Notes, z. B. in PowerShell mit `Get-FileHash .\StudyScope-<Version>-Setup.exe`.

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

Ihre Profile und Einstellungen (Windows: `%APPDATA%\StudyScope`, Linux: `~/.config/StudyScope`) bleiben bei Updates erhalten und werden bei der Deinstallation nur auf ausdrücklichen Wunsch gelöscht.

---

## ⚙️ KI-Einrichtung

Öffnen Sie in StudyScope oben die Menüleiste über **Einstellungen ➔ KI-Konfiguration**:

- **Lokale Modelle (Ollama / LM Studio):**
  Starten Sie Ihre lokale Instanz (z. B. Port `11434` oder `1234`), wählen Sie im Menü den entsprechenden Provider und klicken Sie auf *Modelle suchen*.
- **Cloud-Modelle (OpenAI / Gemini / Mistral):**
  Wählen Sie den Anbieter aus, tragen Sie Ihren API-Schlüssel ein, rufen Sie die Modelle online ab und wählen Sie eines aus. „Speichern & Verbinden“ meldet einen abgelehnten Schlüssel oder eine Störung des Dienstes.
- **Benutzerdefiniert:**
  Geben Sie Ihre individuelle Base-URL (z. B. `https://api.groq.com/openai/v1`), den Modellnamen und optional Ihren Bearer-Token/API-Key ein.

---

## 🛠️ Für Entwickler

### Voraussetzungen
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (oder neuer)
- Visual Studio 2022 / VS Code / JetBrains Rider
- Unter Linux zusätzlich WebKitGTK 4.1, GTK 3 und libnotify (benötigt von Photino 4 für die Oberfläche) sowie ICU (benötigt von .NET), z. B. unter Ubuntu 26.04:
  ```bash
  sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0t64 libnotify4 libicu78
  ```
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
