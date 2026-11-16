# 📚 StudyScope

> **Intelligente Desktop-Lernsoftware mit lokaler LLM-Unterstützung und Dokumenten-RAG.**

![Lizenz](https://img.shields.io/badge/Lizenz-Apache%202.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Plattform](https://img.shields.io/badge/Plattform-Windows%20%7C%20Linux-lightgrey.svg)

**StudyScope** ist eine schlanke, plattformübergreifende Desktop-Anwendung für Windows und Linux zur gezielten Wissensabfrage und automatischen Karteikartengenerierung auf Basis eigener Lernunterlagen. Dank Retrieval-Augmented Generation (RAG) durchsucht StudyScope Dokumente semantisch und beantwortet Fragen präzise anhand Ihrer Quellen – wahlweise vollständig offline und datenschutzfreundlich oder über gängige Cloud-KI-Dienste.

---

## ✨ Features

- 🔍 **Dokumenten-Chat (RAG):** Stellen Sie gezielte Fragen an Ihre Vorlesungsfolien, Skripte und Unterlagen.
- 🎴 **Automatische Karteikarten:** Generieren Sie auf Knopfdruck interaktive Lernkarten aus den geladenen Texten.
- 🧠 **Mindmap:** Die KI ordnet die Inhalte der aktivierten Dokumente als aufklappbaren Themenbaum an. Ein Klick auf ein Thema zeigt eine Kurzerklärung und stellt auf Wunsch eine passende Frage im Chat; Export als Markdown-Gliederung oder als Grafik über „Drucken / PDF“.
- 📽️ **Präsentation:** Folien mit Stichpunkten und Sprechernotizen – von der KI aus den Dokumenten erstellt oder sofort aus der Mindmap abgeleitet. Vortragsmodus im Vollbild (Pfeiltasten, Esc), Export als PDF (eine Folie pro Seite) und als PowerPoint-Datei (`.pptx`).
- ❓ **Quiz:** Themen selbst anlegen (mit zugeordneten Dokumenten) oder von der KI vorschlagen lassen, mehrere Themen zu einem Quiz mit 5–100 Fragen kombinieren. Fragetypen: Multiple Choice, Wahr/Falsch und mehrere richtige Antworten. Lernmodus (Rückmeldung mit Erklärung nach jeder Frage) oder Prüfungsmodus (Auswertung am Ende), Verlauf mit Statistik je Thema und „Falsche wiederholen“.
- 📁 **Unterstützte Formate:** Volle Unterstützung für `.pdf`, `.txt` und `.csv`.
- 🗂️ **Profilverwaltung:** Strukturieren Sie Unterlagen nach Semestern, Schulfächern oder Themengebieten.
- 🔒 **Volle Datenkontrolle & Flexibilität:**
  - **Lokale KI:** Nutzen Sie lokale Sprachmodelle via **Ollama** oder **LM Studio** ganz ohne Internetverbindung und API-Kosten.
  - **Cloud-KI:** Anbindung an **Google Gemini** und **OpenAI (GPT-4)**.
  - **Benutzerdefiniert:** Beliebige OpenAI-kompatible Endpunkte (Groq, Mistral, DeepSeek, vLLM, OpenRouter).
- 🖨️ **Export & Druck:** Exportieren Sie erstellte Karteikarten als Markdown (`.md`) oder drucken Sie diese als DIN-A4-Bögen aus.

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
- **Cloud-Modelle (OpenAI / Gemini):**
  Wählen Sie den Anbieter aus und tragen Sie Ihren API-Schlüssel ein.
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
