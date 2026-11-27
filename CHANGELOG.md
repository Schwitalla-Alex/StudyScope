# Änderungen

Hier steht, was sich in den einzelnen Versionen von StudyScope geändert hat. Die Installationsdateien gibt es unter [Releases](https://github.com/Schwitalla-Alex/StudyScope/releases).

<!-- Der Abschnitt einer Version beginnt mit „## [x.y.z]“ und reicht bis zum nächsten „## [“.
     Der Release-Workflow übernimmt genau diesen Abschnitt als Text des GitHub-Releases. -->

## [Unveröffentlicht]

### Neu
- **Oberfläche vergrößern und verkleinern:** Mit Strg + Mausrad, Strg + Plus / Strg + Minus oder unter ☰ › Einstellungen › 🎨 Darstellung lässt sich die ganze Oberfläche in Stufen von 80 % bis 150 % zoomen; Strg + 0 stellt die Standardgröße wieder her. Die Stufe wirkt sofort und gilt auch nach einem Neustart. Weicht sie von 100 % ab, zeigt die Statusleiste sie an – ein Klick darauf stellt die Standardgröße wieder her. Druck/PDF und exportierte Dateien bleiben unverändert.

## [1.2.0] – 2026-11-27

StudyScope spricht jetzt Englisch, kann dunkel dargestellt werden und schreibt KI-Ergebnisse auf Wunsch in einer anderen Sprache als die Oberfläche. Das neue Menü ☰ schafft mehr Platz für Ihre Inhalte.

### Neu
- **Menü ☰ oben links statt Menüleiste:** Datei öffnen, neues Profil, Einstellungen und Hilfe stecken jetzt in einem Menü ganz oben links; die frühere Menüzeile entfällt. Jeder Eintrag unter „Einstellungen“ führt direkt in den passenden Bereich. Das Menü lässt sich auch komplett per Tastatur bedienen.
- **Reiter immer in einer Zeile:** Bei schmalem Fenster brechen die Reiter nicht mehr um. Was nicht passt, steht unter „» Mehr“; der geöffnete Reiter bleibt immer sichtbar.
- **Dunkle Darstellung:** Unter ☰ › Einstellungen › 🎨 Darstellung wählen Sie „Hell“, „Dunkel“ oder „Wie System“ (folgt Windows bzw. Linux, auch während des Betriebs). Alle Bereiche werden dunkel dargestellt; Folien, Druck/PDF und exportierte Dateien bleiben hell.
- **StudyScope auf Englisch:** Unter 🎨 Darstellung wählen Sie „Wie System“ (Standard), „Deutsch“ oder „English“. Die Sprache wechselt nach „Speichern“ sofort, ohne Neustart; Chat, Dokumente und eine laufende Wiedergabe bleiben erhalten. Bei „Wie System“ erscheint StudyScope auf deutschsprachigen Systemen auf Deutsch, sonst auf Englisch. In der englischen Oberfläche heißt das Profil „Standard“ „Default“.
- **Installationsprogramm auf Englisch:** Setup, Deinstallation, `install.sh` und die Menüeinträge folgen der Sprache Ihres Systems.
- **Sprache der KI-Ergebnisse wählbar:** Unter ☰ › Einstellungen › 🤖 KI-Modell legen Sie je Profil fest, in welcher Sprache die KI schreibt: „Wie Oberfläche“ (Standard), „Wie die Dokumente“ oder fest „Deutsch“ bzw. „English“. So entstehen z. B. englische Karteikarten aus englischen Unterlagen, auch bei deutscher Oberfläche. Das gilt für Chat, Karteikarten, Mindmap, Präsentation, Quiz, Datentabelle, Bericht sowie Audio- und Video-Skripte.
  - Vorhandene Ergebnisse behalten ihre Sprache, auch wenn Sie sie ergänzen. Ergebnisse in einer anderen Sprache als die Oberfläche tragen ein Kennzeichen wie „EN“.
  - Exporte (Word, PowerPoint, Excel, CSV, HTML, Markdown, Text) erscheinen in der Sprache des Ergebnisses, samt Sprache für die Rechtschreibprüfung in Word und PowerPoint.
  - Fachbegriffe aus den Unterlagen werden in die Ergebnissprache übersetzt.
  - **Vorlesen in der Sprache des Beitrags:** Ein englischer Audio-Beitrag oder ein englisches Video wird mit einer englischen Stimme vorgelesen. StudyScope wählt automatisch eine passende Stimme, merkt sich Ihre eigene Wahl je Sprache und zeigt an, wenn für eine Sprache keine Stimme installiert ist – samt Weg, sie unter Windows nachzuinstallieren.
- **StudyScope unterstützen:** Unter ☰ › Hilfe gibt es einen kurzen Spendenaufruf für das Open-Source-Projekt; der Knopf öffnet die Spendenseite (PayPal) im Browser. „Über StudyScope“ öffnet Lizenz und Quellcode auf GitHub direkt im Browser.
- **Esc schließt Dialoge:** Einstellungen, „Neues Profil“, „Über StudyScope“ und andere Dialoge schließen mit Esc; der Fokus kehrt dorthin zurück, wo er vorher war.

### Behobene Fehler und Verbesserungen
- „Speichern & Verbinden“ in den KI-Einstellungen beendet keine laufende Audio- oder Video-Wiedergabe mehr.
- Lehnt ein KI-Dienst eine Anfrage wegen fehlenden Guthabens ab (z. B. Mistral, HTTP 402), nennt StudyScope den Grund und empfiehlt, die Abrechnung beim Anbieter zu prüfen.
- Richtige Einzahl in Zählern („1 Nachricht“, „1 Karte“, „1 Modell gefunden“ statt „1 Nachrichten“, „Karte(n)“, „Modell(e)“).
- Hinweise nennen Knöpfe jetzt richtig („Speichern & Verbinden“, „✨ Weitere Zeilen ergänzen“) und sprechen von Dokumenten statt nur von PDF-Dokumenten; auch der Chat spricht Sie mit „Sie“ an.
- CSV-Dateien der Datentabelle verwenden das Trennzeichen passend zu Ihren Ländereinstellungen (Semikolon bei Dezimalkomma), damit Excel sie direkt in Spalten öffnet.
- Datum, Uhrzeit, Zahlen und Sortierung richten sich nach der gewählten Sprache.

### Hinweis zum Update
- Profile, Dokumente, Inhalte und Einstellungen bleiben erhalten. Einfach das neue Setup über die bestehende Installation ausführen.
- Ältere Ergebnisse ohne gespeicherte Sprache werden wie bisher behandelt und in der Sprache der Oberfläche exportiert bzw. in der Sprache ihres Textes vorgelesen.
- Zurück auf 1.1.1 ist möglich: Alle Profile und Inhalte bleiben lesbar. Sobald 1.1.1 etwas speichert, gehen dort aber die neuen Einstellungen verloren (Darstellung, Sprache der Oberfläche und der KI-Ergebnisse, Systemstimmen je Sprache) – nach einem erneuten Update auf 1.2.0 stellen Sie sie einfach wieder ein.

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
