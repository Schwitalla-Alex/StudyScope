# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Projekt

StudyScope ist eine Windows-Desktop-Lernsoftware (Photino.Blazor) für Dokumenten-Chat (RAG) und automatische Karteikarten-Generierung aus eigenen Unterlagen (`.pdf`, `.txt`, `.csv`). KI-Anbindung wahlweise lokal (Ollama, LM Studio) oder über Cloud-Dienste (Gemini, OpenAI, Mistral, beliebige OpenAI-kompatible Endpunkte). Code, Kommentare und UI-Texte sind auf Deutsch.

## Befehle

Alle Projekte zielen auf `net10.0` (die README nennt teils .NET 8 – maßgeblich sind die `.csproj`). Solution-Datei ist `StudyScope.slnx`.

```bash
dotnet build StudyScope.slnx
dotnet run --project StudyScope.UI
dotnet test StudyScope.Tests
dotnet test StudyScope.Tests --filter "FullyQualifiedName~LlmServiceDispatcherTests"   # eine Testklasse
dotnet test StudyScope.Tests --filter "Name=MethodenName"                              # ein einzelner Test
dotnet publish StudyScope.UI -c Release -r win-x64                                     # Release-Build (siehe Properties/PublishProfiles)
```

Tests: NUnit 4 + Moq. Die Versionsnummer der App steht in `StudyScope.UI/StudyScope.UI.csproj` (`Version`, `AssemblyVersion`, `FileVersion`).

## Architektur

Schichtenaufbau mit Abhängigkeiten nur nach innen:

- **StudyScope.Core** – Interfaces (`ILlmService`, `IStorageService`, `IDocumentService`, `IEncryptionService`, …) und Modelle (`AiConfig` mit `AiProviderType`-Enum, `UserProfile`, `DocumentItem`, `Flashcard`, `ChatMessage`). Keine Abhängigkeiten.
- **StudyScope.Infrastructure** – Implementierungen. Achtung: Ordner heißt `Service/`, der Namespace aber `StudyScope.Infrastructure.Services`.
- **StudyScope.ViewModels** – ViewModels mit handgeschriebenem `INotifyPropertyChanged` (CommunityToolkit.Mvvm ist referenziert, wird aber nicht genutzt).
- **StudyScope.UI** – Photino.Blazor-Host. `Program.cs` ist der DI-Composition-Root (alles als Singleton). Razor-Komponenten in `Components/` injizieren ViewModels per `@inject`, abonnieren `PropertyChanged` und rufen `StateHasChanged` auf (Abmeldung in `Dispose`). Native Dateidialoge laufen über die statische Klasse `DesktopDialogs` (`MainWindow` wird in `Program.cs` gesetzt).

### KI-Anbindung

`LlmServiceDispatcher` ist die einzige `ILlmService`-Implementierung im Betrieb: Bei jedem Aufruf lädt er die **globale** `AiConfig` (`LoadAiConfigAsync`) und verzweigt per `switch` auf `SelectedProvider` (Ollama nativ, Gemini nativ, Mistral, sonst OpenAI-kompatibler Pfad für LM Studio/OpenAI/Custom). Ein neuer Provider braucht: Enum-Wert in `AiConfig.cs`, Fälle in `AskQuestionAsync` **und** `ValidateConnectionAsync`, sowie UI in `SettingsModal.razor`/`SettingsViewModel`. `FakeAi` ist der Standard-/Testmodus und liefert für Karteikarten-Prompts (enthält „Lernkarteikarten“ oder „JSON“) ein festes JSON-Array.

### RAG und Karteikarten

- `DocumentService` extrahiert Text (PDF via PdfPig) und zerlegt ihn in Chunks (Standard 250 Wörter).
- Retrieval ist **kein** Embedding-Verfahren: `ChatViewModel.GetMostRelevantChunks` bewertet Chunks per Stichwort-Treffer (mit deutscher Stoppwortliste) und gibt die Top 8 an das LLM.
- `FlashcardService` lässt das LLM ein JSON-Array mit `question`/`answer` erzeugen und parst es; außerdem Markdown-Export und Druck.

### Persistenz

`JsonStorageService` speichert unter `%APPDATA%\StudyScope\`: je Profil `Profiles/<name>.json` (Dokumente + profilspezifische `AiSettings`), dazu `config.json` (globale KI-Konfiguration) und `last_profile.txt`. API-Keys werden beim Schreiben per `AesEncryptionService` (AES-GCM, fest abgeleiteter Schlüssel) verschlüsselt und beim Lesen entschlüsselt. `SettingsViewModel` speichert die KI-Konfiguration sowohl profilspezifisch als auch global – der Dispatcher liest nur die globale.

Für Tests nimmt `JsonStorageService` ein Basisverzeichnis entgegen (`new JsonStorageService(tempDir)`); Tests nutzen ein temporäres Verzeichnis, nicht `%APPDATA%`.
