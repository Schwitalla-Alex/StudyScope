# 📚 StudyScope

> **Intelligente Desktop-Lernsoftware mit lokaler LLM-Unterstützung und Dokumenten-RAG.**

![Lizenz](https://img.shields.io/badge/Lizenz-Apache%202.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0%2B-purple.svg)
![Plattform](https://img.shields.io/badge/Plattform-Windows-lightgrey.svg)

**StudyScope** ist eine schlanke, plattformübergreifend konzipierte Desktop-Anwendung zur gezielten Wissensabfrage und automatischen Karteikartengenerierung auf Basis eigener Lernunterlagen. Dank Retrieval-Augmented Generation (RAG) durchsucht StudyScope Dokumente semantisch und beantwortet Fragen präzise anhand Ihrer Quellen – wahlweise vollständig offline und datenschutzfreundlich oder über gängige Cloud-KI-Dienste.

---

## ✨ Features

- 🔍 **Dokumenten-Chat (RAG):** Stellen Sie gezielte Fragen an Ihre Vorlesungsfolien, Skripte und Unterlagen.
- 🎴 **Automatische Karteikarten:** Generieren Sie auf Knopfdruck interaktive Lernkarten aus den geladenen Texten.
- 📁 **Unterstützte Formate:** Volle Unterstützung für `.pdf`, `.txt` und `.csv`.
- 🗂️ **Profilverwaltung:** Strukturieren Sie Unterlagen nach Semestern, Schulfächern oder Themengebieten.
- 🔒 **Volle Datenkontrolle & Flexibilität:**
  - **Lokale KI:** Nutzen Sie lokale Sprachmodelle via **Ollama** oder **LM Studio** ganz ohne Internetverbindung und API-Kosten.
  - **Cloud-KI:** Anbindung an **Google Gemini** und **OpenAI (GPT-4)**.
  - **Benutzerdefiniert:** Beliebige OpenAI-kompatible Endpunkte (Groq, Mistral, DeepSeek, vLLM, OpenRouter).
- 🖨️ **Export & Druck:** Exportieren Sie erstellte Karteikarten als Markdown (`.md`) oder drucken Sie diese als DIN-A4-Bögen aus.

---

## 🚀 Download & Installation

Für reguläre Anwender ist keine Installation von .NET oder Programmier-Tools erforderlich:

1. Wechseln Sie rechts auf der Repository-Seite in den Bereich **[Releases](https://github.com/Schwitalla-Alex/StudyScope/releases)**.
2. Laden Sie die aktuellste Datei `StudyScope-v1.0-win-x64.zip` herunter.
3. Entpacken Sie das ZIP-Archiv an einem beliebigen Ort auf Ihrem PC.
4. Starten Sie die Anwendung per Doppelklick auf **`StudyScope.exe`**.

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

### Quellcode ausführen
```bash
# Repository klonen
git clone [https://github.com/Schwitalla-Alex/StudyScope.git](https://github.com/Schwitalla-Alex/StudyScope.git)
cd StudyScope

# Abhängigkeiten wiederherstellen
dotnet restore

# Anwendung starten
dotnet run --project StudyScope.UI
