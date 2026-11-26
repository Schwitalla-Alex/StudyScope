# Glossar Deutsch → Englisch

Verbindliche Begriffe und Regeln für die Übersetzung der Oberfläche. Die deutschen Texte sind das Original (neutrale `.resx`), die englischen stehen in `*.en.resx`. Eine weitere Sprache bekommt eine eigene Spalte bzw. ein eigenes Glossar nach demselben Muster.

## Grundregeln

| Thema | Deutsch | Englisch |
|---|---|---|
| Anrede | durchgehend „Sie“ | „you“, direkt und freundlich; „Bitte wählen Sie …“ wird zu „Select …“ (kein „please“ in jeder Meldung) |
| Groß-/Kleinschreibung | normale deutsche Rechtschreibung | *Sentence case* für Knöpfe, Menüs, Überschriften und Beschriftungen („Open file…“, „Test connection“); großgeschrieben nur Produktnamen und „StudyScope“ |
| Anführungszeichen | „…“ | “…” |
| Auslassung | „…“ (ein Zeichen) | „…“ (ein Zeichen) |
| Emojis und Zeichen | ⚠️ ✓ ✗ 📄 ▶ » usw. | unverändert an derselben Stelle |
| Abkürzungen | z. B., ca., ggf., inkl. | e.g., approx., if needed, incl. |
| Zahlen, Datum, Uhrzeit | über die Kultur (`CurrentCulture`), nie im Text festgeschrieben | ebenso |
| Platzhalter | benannt im ICU-Format: `{count}`, `{name}` | dieselben Namen, Reihenfolge darf sich ändern |
| Einzahl/Mehrzahl | ICU-Plural nach CLDR: `{count, plural, one {# Folie} other {# Folien}}` | `{count, plural, one {# slide} other {# slides}}`; jede Sprache nennt ihre eigenen Kategorien (zero/one/two/few/many/other), `other` ist immer Pflicht |
| Apostroph und Klammern | `{`, `}` und `#` (im Plural) in Text mit Platzhaltern in `'…'` setzen, `''` ergibt ein Apostroph | ebenso; „Didn't“ in einem Text mit Platzhaltern als „Didn''t“ schreiben |
| KI erzeugt etwas | „erstellen“, „erzeugen“ | „generate“ („Generate flashcards“) |
| Benutzer legt etwas an | „anlegen“, „erstellen“ | „create“ („Create profile“) |
| Erneut durch die KI | „Neu erstellen“ | „Regenerate“ |

## Tasten

| Deutsch | Englisch |
|---|---|
| Strg | Ctrl |
| Umschalt | Shift |
| Alt | Alt |
| Leertaste | Space |
| Eingabetaste / Enter | Enter |
| Esc | Esc |
| Entf | Del |
| Pos1 / Ende | Home / End |
| Bild auf / Bild ab | Page Up / Page Down |
| Pfeil links/rechts | Left/Right arrow |
| Doppelklick | double-click |

## Nicht übersetzt

- **Produkt- und Dienstnamen:** StudyScope, Ollama, LM Studio, Google Gemini, OpenAI, Mistral AI, ffmpeg, PayPal, GitHub, Windows, Linux, WebView2, WebKitGTK, Word, Excel, PowerPoint.
- **Dateiformate:** PDF, CSV, TXT, HTML, DOCX, PPTX, XLSX, WAV, MP3, MP4, Markdown, JSON.
- **Befehle und Pfade:** `winget install Gyan.FFmpeg`, `sudo apt install ffmpeg`, Dateinamen, Umgebungsvariablen.
- **Gespeicherte Werte und Prompt-Bestandteile:** Sprecherrollen („Sprecherin“, „Moderatorin“, „Experte“), Formatnamen in Prompts („Podcast-Gespräch“), Spaltenvorschläge der Vorlagen. Diese bleiben in Dateien und Prompts deutsch; übersetzt wird nur die Anzeige (Spalte „Anzeige“ unten).

## Allgemeine Oberfläche

| Deutsch | Englisch | Hinweis |
|---|---|---|
| Einstellungen | Settings | |
| Profil | profile | |
| Profil / Thema | Profile / topic | Beschriftung der Profilauswahl |
| Themengebiet | subject area | |
| Neues Profil… | New profile… | |
| Datei öffnen… | Open file… | |
| Hilfe | Help | |
| Über StudyScope | About StudyScope | |
| StudyScope unterstützen | Support StudyScope | |
| Anwendung schließen | Exit | |
| Reiter | tab | |
| » Mehr | » More | Überlaufmenü der Reiter |
| Seitenleiste | sidebar | |
| Seitenleiste ein-/ausklappen | Collapse/expand sidebar | |
| Statusleiste | status bar | |
| Menü | menu | |
| Fenster | window | |
| Vollbild | full screen | |
| Vortragsmodus | presentation mode | |
| Ansicht | view | |
| Abschnitt | section | |
| Darstellung | Appearance | |
| Farbschema | Color scheme | |
| Hell / Dunkel / System | Light / Dark / System | |
| Zurzeit: | Currently: | |
| Sprache | Language | |
| Oberfläche | interface | |

## Knöpfe und Aktionen

| Deutsch | Englisch |
|---|---|
| Speichern | Save |
| Abbrechen | Cancel |
| Schließen | Close |
| Übernehmen | Apply |
| Löschen | Delete |
| Umbenennen | Rename |
| Bearbeiten | Edit |
| Hinzufügen | Add |
| Exportieren / Export | Export |
| Drucken / PDF | Print / PDF |
| Kopieren | Copy |
| Senden | Send |
| Erneut versuchen | Try again |
| Weiter / Zurück | Next / Back |
| Fertig | Done |
| Alle an / Alle aus | Select all / Clear all |
| Aufklappen / Zuklappen | Expand / Collapse |
| Im Browser öffnen | Open in browser |
| Meldung schließen | Dismiss message |
| Technische Details | Technical details |

## Meldungen

| Deutsch | Englisch |
|---|---|
| Hinweis | Note |
| Tipp | Tip |
| Warnung | Warning |
| Fehler | Error |
| Fehler beim … | Could not … („Could not save the file: {0}“) |
| … wurde abgebrochen. | … was canceled. (amerikanische Schreibweise) |
| … erfolgreich gespeichert | … saved |
| Bitte wählen Sie links mindestens ein Dokument aus. | Select at least one document on the left. |
| nicht verfügbar | not available |

## KI-Anbindung

| Deutsch | Englisch | Hinweis |
|---|---|---|
| KI | AI | |
| KI-Modell | AI model | |
| Chat-KI | chat AI | |
| Anbieter | provider | |
| Fake-KI (Testmodus) | Fake AI (test mode) | |
| Lokal | local | „Ollama (local)“ |
| Benutzerdefiniert (OpenAI-kompatibel) | Custom (OpenAI-compatible) | |
| API-Schlüssel | API key | |
| Endpunkt / Server-Adresse | endpoint / server URL | |
| Modelle suchen | Find models | lokale Dienste |
| Modelle online abrufen | Fetch models online | Cloud-Dienste |
| Speichern & Verbinden | Save & connect | |
| Verbindung testen | Test connection | |
| Zeitüberschreitung | timeout | |
| Kontingent | quota | |
| StudyScope denkt nach … | StudyScope is thinking… | |
| Antwortet … | Answering… | |

## Dokumente und Chat

| Deutsch | Englisch | Hinweis |
|---|---|---|
| Dokument(e) | document(s) | |
| aktivierte / angehakte Dokumente | selected documents | |
| fehlt (Datei nicht gefunden) | missing | |
| Unterlagen | materials | „anhand meiner Unterlagen“ → „based on my materials“ |
| Textstelle(n) | passage(s) | |
| Auszüge | excerpts | |
| Quelle / Quelldokumente | source / source documents | |
| Chat & Fragen | Chat & questions | Reitername |
| Gesprächsverlauf | conversation history | |
| Chat-Protokoll | chat log | |
| Sie / Assistent (KI) | You / Assistant (AI) | Rollenanzeige |
| Hilfreich / Nicht hilfreich | Helpful / Not helpful | Bewertung |

## Lernen

| Deutsch | Englisch | Hinweis |
|---|---|---|
| Lernkarteikarten (Reiter) | Flashcards | |
| Karteikarte(n) | flashcard(s) | |
| Stapel | deck | |
| Lernstand | learning progress | |
| Vorderseite / Rückseite | front / back | |
| Umdrehen | Flip | |
| Gewusst / Nicht gewusst | Knew it / Didn't know | |
| Mindmap | mind map | zwei Wörter, Reiter „Mind map“ |
| Ast / Hauptast | branch / main branch | |
| Knoten | node | |
| Thema / Unterthema | topic / subtopic | |
| Im Chat erklären lassen | Explain in chat | |
| Präsentation | presentation | |
| Folie(n) | slide(s) | |
| Titelfolie | title slide | |
| Stichpunkt(e) | bullet point(s) | |
| Sprechernotizen | speaker notes | |
| Quiz | quiz | |
| Frage / Antwort | question / answer | |
| Fragenpool | question pool | |
| Einfachauswahl / Mehrfachauswahl | single choice / multiple choice | |
| Wahr / Falsch | True / False | Fragetyp |
| Richtig / Falsch | Correct / Incorrect | Auswertung |
| Lernmodus / Prüfungsmodus | practice mode / exam mode | |
| Falsche wiederholen | Retry incorrect | |
| Ergebnis / Verlauf | result / history | |

## Datentabelle und Bericht

| Deutsch | Englisch | Hinweis |
|---|---|---|
| Datentabelle | data table | Reiter „Data table“ |
| Zeile / Spalte | row / column | |
| Zelle | cell | |
| Kopfzeile | header row | |
| Sortieren / Filter | sort / filter | |
| Weitere Zeilen ergänzen | Add more rows | |
| Vorlage | template | |
| Zeitleiste | timeline | Vorlage |
| Bericht(e) | report(s) | |
| Studio-Inhalte einbetten | Embed studio content | |
| Zusammenfassung | summary | |
| Begriffe / Glossar | terms / glossary | |
| Merksatz | key takeaway | |
| Selbsttest | self-test | |
| Eigene Vorgabe | Custom instructions | Vorlage „Custom“ |
| Ihre Vorgabe | Your instructions | Eingabefeld |
| Quelle (Spalte) | source | gespeicherter Spaltenname bleibt „Quelle“ |
| Grundlage: … | Based on: … | Kopfzeile der Reiter |
| Lernübersicht | Study guide | Berichtsvorlage |
| Zusammenfassung / Briefing | Summary / briefing | Berichtsvorlage |
| Eingebettet | Embedded | |
| Wiederholung (doppelte Zeile) | duplicate | |

## Audio und Video

| Deutsch | Englisch | Anzeige gespeicherter Werte |
|---|---|---|
| Audio-Zusammenfassung | audio summary | |
| Sprachausgabe | Text-to-speech | Einstellungsabschnitt und Karte |
| Systemstimme | system voice | |
| Stimme / Stimmen | voice / voices | |
| Sprecher / Sprecherin | speaker / narrator | „Sprecherin“ wird als „Narrator“ angezeigt |
| Moderatorin / Experte | host / expert | Anzeige „Host“ / „Expert“ |
| Zusammenfassung / Podcast-Gespräch / Kurz-Briefing | Summary / Podcast conversation / Short briefing | nur Anzeige, Prompt bleibt deutsch |
| kurz / mittel / lang | short / medium / long | |
| Schwerpunkt | focus | |
| Skript | script | |
| Beitrag | episode | |
| Abschnitt (Skript) | segment | |
| Audiodatei | audio file | |
| Tonspur | audio track | |
| Wiedergabe / Abspielen | playback / play | |
| Tempo | speed | |
| Erklärvideo | explainer video | |
| Videoübersicht | video overview | |
| Szene(n) | scene(s) | |
| Sprechtext(e) | narration | |
| vertont | narrated | |
| zusammensetzen (Audio) | combine | |
| veraltet | outdated | |
| Schätzung / Dauer / Kosten | estimate / duration / cost | |
| Einführungspreis | introductory price | |

## Schlüssel in den `.resx`-Dateien

- Aufbau `Bereich_Element[_Variante]`, z. B. `Menu_OpenFile`, `Settings_Ai_Title`, `Chat_Send`, `Quiz_Result_Score`. Unterstriche statt Punkte, weil daraus Eigenschaften der Ressourcenklasse werden.
- Gemeinsame Wörter unter `Common_` (`Common_Save`, `Common_Cancel`, `Common_Close`), damit sie überall gleich lauten.
- Der Schlüssel beschreibt die Bedeutung, nicht den Wortlaut; ändert sich der deutsche Text, bleibt der Schlüssel.
- Kommentar in der `.resx` (Feld `comment`), wenn der Zusammenhang nicht aus dem Schlüssel hervorgeht, z. B. was `{count}` zählt.
