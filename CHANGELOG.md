# Änderungen

Hier steht, was sich in den einzelnen Versionen von StudyScope geändert hat. Die Installationsdateien gibt es unter [Releases](https://github.com/Schwitalla-Alex/StudyScope/releases).

<!-- Der Abschnitt einer Version beginnt mit „## [x.y.z]“ und reicht bis zum nächsten „## [“.
     Der Release-Workflow übernimmt genau diesen Abschnitt als Text des GitHub-Releases. -->

## [Unveröffentlicht]

### Neu
- **Menü ☰ oben links statt Menüleiste:** Datei öffnen, neues Profil, Einstellungen und Hilfe stecken jetzt in einem Menü ganz oben links. Unter „Einstellungen“ führt jeder Eintrag direkt in den passenden Bereich (KI-Modell, Sprachausgabe, Export). Die frühere Menüzeile entfällt – Ihre Inhalte bekommen mehr Höhe.
  - Bedienung auch per Tastatur: Pfeiltasten, Pos1/Ende, Enter oder Leertaste, ← bzw. Esc schließt; danach steht der Fokus wieder auf ☰. Ein Klick daneben schließt das Menü.
  - Im Vortrags- und Vollbildmodus ist das Menü ausgeblendet; Strg+B wirkt bei offenem Menü nicht.
- **Reiter immer in einer Zeile:** Bei schmalem Fenster brechen die Reiter nicht mehr auf zwei Zeilen um. Was nicht passt, steht unter „» Mehr“; der gerade geöffnete Reiter bleibt immer sichtbar.
- **StudyScope unterstützen:** Unter ☰ › Hilfe gibt es einen kurzen Spendenaufruf für das Open-Source-Projekt. Der Knopf öffnet die Spendenseite (PayPal) in Ihrem Browser.
- **Über StudyScope:** Lizenz und Quellcode auf GitHub lassen sich direkt im Browser öffnen.
- **Dunkle Darstellung (Teil 1):** Unter ☰ › Einstellungen › 🎨 Darstellung wählen Sie „Hell“, „Dunkel“ oder „Wie System“. Bei „Wie System“ folgt StudyScope der Einstellung von Windows bzw. Linux, auch wenn Sie sie während des Betriebs ändern. Die Auswahl wirkt sofort als Vorschau und gilt nach „Speichern“ für alle Profile; beim nächsten Start erscheint StudyScope gleich in der gewählten Darstellung.
  - Dunkel dargestellt werden bereits Menü, Reiterleiste, Seitenleiste, Statusleiste, der Chat und alle Dialoge. Die übrigen Reiter (Karteikarten, Mindmap, Präsentation, Quiz, Datentabelle, Audio, Video, Berichte) folgen in einem nächsten Schritt und bleiben bis dahin hell.
  - Druck/PDF bleibt immer hell.

## [1.1.1] – 2026-11

Mehr Platz für Ihre Inhalte: Die linke Seitenleiste lässt sich einklappen und in der Breite anpassen.

### Neu
- **Seitenleiste ein- und ausklappen:** Mit ◀ oben rechts in der Seitenleiste oder mit **Strg+B** wird sie ausgeblendet; die Reiter (Chat, Mindmap, Präsentation, Video usw.) nutzen dann die volle Breite. Eingeklappt bleibt eine schmale Leiste mit ▶ und der Anzahl aktiver Dokumente (z. B. „📄 1/2“ – der Tooltip nennt Profil und Dokumente, ein Klick klappt wieder auf).
  - Strg+B wirkt nicht, während Sie in ein Eingabefeld schreiben, und nicht im Vortrags- oder Vollbildmodus.
  - Der Zustand wird gemerkt und beim nächsten Start wiederhergestellt.
  - Bei sehr schmalem Fenster (unter 900 Pixel) klappt die Seitenleiste automatisch ein; Ihre gewählte Einstellung bleibt dabei erhalten.
- **Breite der Seitenleiste mit der Maus ziehen:** Am rechten Rand der Seitenleiste lässt sich die Breite verändern – z. B. um lange Dokumentnamen ganz zu lesen. Ein **Doppelklick** auf den Rand stellt die Standardbreite wieder her; wer sehr weit nach links zieht, klappt die Seitenleiste ein. Auch per Tastatur möglich (Rand mit Tab auswählen, dann ← → Pos1 Ende). Die Breite wird gemerkt.
- **Voller Dokumentname als Tooltip:** Mit der Maus über einem Eintrag der Dokumentliste erscheint immer der vollständige Dateiname.

### Interne Aufräumarbeiten
- Automatische Prüfungen bei jeder Änderung (Build und alle Tests unter Windows und Linux) und automatisch gebaute Installationsdateien mit Prüfsummen.
- Dauerhafte Download-Links, die immer auf die neueste Version zeigen (siehe README, Abschnitt „Download-Links“).
- Kleinere Bereinigungen im Programmcode ohne Auswirkung auf die Bedienung.

### Hinweis zum Update
- Profile, Dokumente, Inhalte und Einstellungen bleiben erhalten. Einfach das neue Setup über die bestehende Installation ausführen.

## [1.1.0] – 2026-11

Mit dieser Version macht StudyScope aus Ihren Unterlagen auch Tabellen, Hörbeiträge und vertonte Videos – und exportiert sie als Excel, Word, HTML, MP3 oder MP4.

### Neu
- **📊 Datentabelle:** Die KI sammelt Angaben aus den Dokumenten in einer Tabelle, z. B. eine Zeitleiste, Begriffe mit Definitionen, Personen, Vergleiche oder eigene Spalten. Jede Zeile nennt ihre Quelle. Die Tabelle lässt sich sortieren, filtern, direkt bearbeiten und um weitere Zeilen ergänzen. Export als Excel (`.xlsx`), CSV, Markdown, Druck/PDF und als Teil eines Berichts.
- **🎧 Audio-Zusammenfassung:** Die KI schreibt ein Sprechskript, wahlweise als Zusammenfassung, Podcast-Gespräch zu zweit oder kurzes Briefing, in drei Längen und mit eigenem Schwerpunkt.
  - **Systemstimme:** Vorlesen mit den Stimmen Ihres Computers, kostenlos und ohne Internet.
  - **Gemini TTS (optional):** Natürlich klingende Stimmen von Google als Audiodatei (WAV, mit ffmpeg auch MP3). Dafür braucht es einen Gemini-API-Schlüssel; die Kosten werden vor dem Erzeugen geschätzt.
  - Das Skript lässt sich bearbeiten, beim Abspielen wird der gesprochene Abschnitt hervorgehoben.
- **🎬 Videoübersicht:** Aus der Präsentation – oder aus neuen Folien nur für das Video – entsteht ein vertontes Video: Die KI schreibt zu jeder Folie einen Sprechtext, die Stichpunkte erscheinen passend nacheinander, zwischen den Folien wird übergeblendet. Abspielen in der App mit Systemstimme oder Gemini-Ton, Vollbild und Tastatursteuerung.
  - **Export als interaktive HTML-Seite** mit Player, Folien und Ton – läuft in jedem aktuellen Browser, auch ohne StudyScope.
  - **Export als MP4-Video** (1920 × 1080) und der Tonspur als **MP3** – dafür wird ffmpeg benötigt (siehe unten).
  - Geschätzte Dateigröße und Exportdauer werden vorher angezeigt, der Export lässt sich abbrechen.
- **📑 Berichte:** Datentabellen, Audio-Skripte und Video-Skripte lassen sich jetzt einbetten. In der App startet „▶ Video abspielen“ das Video direkt aus dem Bericht. Der HTML-Export kann die Videos auf Wunsch samt Player einbetten.

### Verbesserungen und Fehlerbehebungen
- **Video aus der richtigen Präsentation:** Stammt die vorhandene Präsentation aus anderen Dokumenten als den gerade angehakten, warnt StudyScope und bietet an, die Folien neu aus den angehakten Dokumenten zu erzeugen. Videos zeigen, aus welchen Dokumenten sie entstanden sind.
- **Kein halber Export:** Audio-, Video- und Exportdateien werden zuerst unter einem vorläufigen Namen geschrieben und erst am Ende umbenannt. Bei Abbruch oder Fehler bleibt eine vorhandene Datei unverändert.
- **Verständliche Meldungen bei Gemini TTS:** Ungültiger Schlüssel, aufgebrauchtes Kontingent, nicht unterstützte Region oder Störungen bei Google werden auf Deutsch mit Handlungsempfehlung gemeldet.
- **Einstellungen mit Abschnitten:** Neben der KI-Konfiguration gibt es jetzt „🔊 Sprachausgabe“ (Gemini-Schlüssel, Stimmen, Verbindungstest) und „🎞️ Export“ (ffmpeg suchen und testen).

### Seit 1.0.4 außerdem neu
Version 1.0.5 wurde nicht als Download veröffentlicht. Wer von 1.0.4 aktualisiert, bekommt mit 1.1.0 zusätzlich:
- **🧠 Mindmap** der Inhalte als aufklappbarer Themenbaum, mit Fragen an den Chat, Druck/PDF und Markdown-Export.
- **📽️ Präsentation** mit Stichpunkten und Sprechernotizen, Vortragsmodus im Vollbild, Export als PDF und PowerPoint (`.pptx`).
- **❓ Quiz** mit eigenen oder vorgeschlagenen Themen, Lern- und Prüfungsmodus, Verlauf und „Falsche wiederholen“.
- **📑 Berichte** (Lernübersicht, Zusammenfassung, Glossar, FAQ oder eigene Vorgabe), Export als Word, PDF, Markdown, Text oder interaktive HTML-Seite.
- **Echte Verbindungsprüfung** bei Cloud-KI, **verständliche Fehlermeldungen** statt Fehlertext als vermeintliche Antwort, **Karteikarten hinzufügen statt ersetzen**, stabilere Auswertung der KI-Antworten, Chatverlauf je Profil getrennt.

### ffmpeg ist optional
StudyScope funktioniert vollständig ohne ffmpeg. Nur der Export von Videos als **MP4** und von Audio als **MP3** braucht das kostenlose Programm [ffmpeg](https://ffmpeg.org); es wird nicht mitinstalliert.
- Windows: `winget install Gyan.FFmpeg` in der Eingabeaufforderung, danach StudyScope neu starten.
- Linux: `sudo apt install ffmpeg` (Fedora: `sudo dnf install ffmpeg`).

StudyScope findet ffmpeg meist von selbst; sonst lässt sich der Pfad unter *Einstellungen ➔ 🎞️ Export* eintragen. Ohne ffmpeg sind nur die MP4-/MP3-Knöpfe ausgegraut, mit Hinweis.

### Kosten und Datenschutz
- **Gemini TTS ist kostenpflichtig** (abgerechnet von Google über Ihren API-Schlüssel). StudyScope zeigt vor jedem Erzeugen eine Schätzung von Dauer und Kosten; bei Videos werden nur geänderte Folien neu vertont. Die Preise legt Google fest und können sich ändern.
- **Kostenlose Stufe:** Google bietet für Gemini TTS auch eine kostenlose Stufe mit Tageslimits an. Bei der kostenlosen Stufe kann Google die Eingaben – also die Sprechtexte – zur Verbesserung seiner Dienste nutzen. Für vertrauliche Unterlagen eignen sich deshalb die kostenpflichtige Stufe oder die Systemstimme.
- Für Gemini TTS wird der **Sprechtext an Google übertragen**, für Cloud-KI im Chat und in allen KI-Bereichen wie bisher Auszüge aus Ihren Dokumenten an den gewählten Anbieter. Mit einer lokalen KI (Ollama, LM Studio) und der Systemstimme bleibt alles auf Ihrem Computer.
- Manche Systemstimmen sind Online-Stimmen, die den Text an einen Dienst senden; StudyScope weist darauf hin und bevorzugt lokale Stimmen.

### Hinweis zum Update
- Profile, Dokumentlisten, Karteikarten, Mindmaps, Präsentationen, Quizze, Berichte und Einstellungen bleiben erhalten. Einfach das neue Setup über die bestehende Installation ausführen.
- Ältere Berichte bleiben unverändert; die neuen Einbettungen sind dort zunächst ausgeschaltet.

## [1.0.5] – 2026-11-16 (nicht als Download veröffentlicht)
Alle Inhalte sind in 1.1.0 enthalten (siehe „Seit 1.0.4 außerdem neu“): Mindmap, Präsentation, Quiz, Berichte, echte Verbindungsprüfung bei Cloud-KI, verständlichere Fehlermeldungen, Karteikarten hinzufügen statt ersetzen.

## [1.0.4] – 2026-11-15
- Jedes Profil arbeitet mit seiner eigenen KI-Einstellung (vorher galt die zuletzt gespeicherte).
- API-Schlüssel auch in der globalen Konfiguration verschlüsselt; beschädigte Schlüssel werden repariert.
- „Speichern & Verbinden“ prüft die gerade eingegebenen Werte.

## [1.0.3] – 2026-11-15
- Eigenes Installationsprogramm für Windows (setup.exe) und Linux (tar.gz mit `install.sh`), Updates an Ort und Stelle.
- Umstieg auf Photino 4 (WebKitGTK 4.1) für aktuelle Linux-Distributionen; neues Icon.
- Korrekturen bei Profilnamen und beim Dateidialog unter Linux.

## [1.0.2] – 2026-10-29
- API-Schlüssel mit AES-GCM verschlüsselt gespeichert.
- Mistral AI als Anbieter; Dokumente und KI-Modell je Profil; laufende Anfragen abbrechbar.

## [1.0.1] – 2026-10-27
- Verfügbare Modelle lokaler und Cloud-Anbieter werden automatisch abgefragt; überarbeitete Einstellungen und Chat.

## [1.0.0] – 2026-10-26
- Umstellung auf .NET 10; Karteikarten je Profil löschen oder zurücksetzen; getrennte Dokumentsammlungen je Profil.
