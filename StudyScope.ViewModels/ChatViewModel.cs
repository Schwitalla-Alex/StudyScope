using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using StudyScope.ViewModels.Resources;
using StudyScope.Core.Localization;
using System.Globalization;

namespace StudyScope.ViewModels;

public class ChatViewModel : INotifyPropertyChanged
{
    private readonly ILlmService _llmService;
    private readonly IDocumentService _pdfService;
    private readonly DocumentListViewModel _documentListViewModel;
    private string _currentInputPrompt = string.Empty;
    private bool _isBusy = false;
    private string _exportStatusMessage = string.Empty;
    // Token-Quelle für den aktuellen Abbruch
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>Einstellung „Sprache der KI-Ergebnisse“ des Profils (fehlt in Tests → wie Oberfläche).</summary>
    public IOutputLanguageProvider? OutputLanguageProvider { get; set; }

    /// <summary>
    /// Websuche (nur, wenn das Häkchen des Profils an ist: <see cref="DocumentListViewModel.IsWebSearchActive"/>).
    /// Fehlt sie (Tests), verhält sich der Chat wie ohne Websuche.
    /// </summary>
    public IWebSearchService? WebSearch { get; set; }

    // Frage, die die App ins Eingabefeld gelegt hat (z. B. aus der Mindmap, während eine Antwort lief): Wird sie
    // abgeschickt, behält sie ihre Herkunft und sucht nie im Internet
    private string? _pendingPromptText;
    private ChatQuestionOrigin _pendingPromptOrigin = ChatQuestionOrigin.User;

    /// <summary>
    /// Frage samt Sprachzeile: Weicht die Ergebnissprache vom Deutschen ab, steht sie als eigene Zeile am Ende der Frage;
    /// der Dispatcher wählt danach die Systemanweisung (<c>RagSystemPromptFor</c>). Bei Deutsch bleibt die Frage unverändert.
    /// </summary>
    public static string WithLanguageLine(string question, System.Globalization.CultureInfo language) =>
        language.TwoLetterISOLanguageName == UiLanguage.NeutralLanguage
            ? question
            : question.TrimEnd() + "\n\n" + StudyScope.Core.Localization.OutputLanguage.PromptLine(language);

    public ChatViewModel(ILlmService llmService, IDocumentService pdfService, DocumentListViewModel documentListViewModel)
    {
        _llmService = llmService;
        _pdfService = pdfService;
        _documentListViewModel = documentListViewModel;
        Messages = new ObservableCollection<ChatMessage>();

        // Gesprächsverlauf je Profil getrennt halten (Datenschutz: kein Verlauf aus anderen Profilen sichtbar)
        _activeProfile = documentListViewModel?.CurrentProfile ?? string.Empty;
        if (documentListViewModel != null)
            documentListViewModel.PropertyChanged += OnDocumentListPropertyChanged;
    }

    // Verläufe der gerade nicht angezeigten Profile (nur im Speicher, wie bisher kein Speichern auf Platte)
    private readonly Dictionary<string, List<ChatMessage>> _historyByProfile = new(StringComparer.OrdinalIgnoreCase);
    private string _activeProfile;

    /// <summary>Profil, dessen Gesprächsverlauf gerade in <see cref="Messages"/> angezeigt wird.</summary>
    public string ActiveProfile => _activeProfile;

    private void OnDocumentListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentListViewModel.CurrentProfile))
            SwitchToProfile(_documentListViewModel.CurrentProfile);
    }

    /// <summary>
    /// Zeigt den Verlauf eines anderen Profils an. Der bisherige Verlauf bleibt für die Rückkehr erhalten.
    /// </summary>
    public void SwitchToProfile(string? profileName)
    {
        var target = profileName ?? string.Empty;
        if (string.Equals(target, _activeProfile, StringComparison.OrdinalIgnoreCase))
            return;

        _historyByProfile[_activeProfile] = Messages.ToList();
        Messages.Clear();
        if (_historyByProfile.Remove(target, out var stored))
        {
            foreach (var message in stored)
                Messages.Add(message);
        }

        _activeProfile = target;
        ExportStatusMessage = string.Empty;
        OnPropertyChanged(nameof(Messages));
        OnPropertyChanged(nameof(ActiveProfile));
    }

    /// <summary>
    /// Fügt eine Nachricht dem Verlauf des angegebenen Profils hinzu – auch wenn inzwischen ein anderes Profil angezeigt wird
    /// (z. B. eine Antwort, die erst nach einem Profilwechsel eintrifft).
    /// </summary>
    private void AddMessage(string profileName, ChatMessage message)
    {
        if (string.Equals(profileName, _activeProfile, StringComparison.OrdinalIgnoreCase))
        {
            Messages.Add(message);
            return;
        }

        if (!_historyByProfile.TryGetValue(profileName, out var list))
            _historyByProfile[profileName] = list = new List<ChatMessage>();
        list.Add(message);
    }

    public ObservableCollection<ChatMessage> Messages { get; }

    public string ExportStatusMessage
    {
        get => _exportStatusMessage;
        set
        {
            if (_exportStatusMessage != value)
            {
                _exportStatusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentInputPrompt
    {
        get => _currentInputPrompt;
        set
        {
            if (_currentInputPrompt != value)
            {
                _currentInputPrompt = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Bewertet eine Assistenten-Nachricht positiv oder negativ.
    /// Erneutes Klicken hebt die Bewertung auf.
    /// </summary>
    public void RateMessage(ChatMessage message, bool isLiked)
    {
        if (message.Role != MessageRole.Assistant)
            return;

        if (message.IsLiked == isLiked)
            message.IsLiked = null; // Toggle: Bewertung zurücksetzen
        else
            message.IsLiked = isLiked;

        OnPropertyChanged(nameof(Messages));
    }

    /// <summary>
    /// Generiert die letzte Assistenten-Antwort neu.
    /// </summary>
    public async Task RegenerateLastResponseAsync()
    {
        if (IsBusy || Messages.Count == 0)
            return;

        // Letzte Benutzer-Nachricht suchen
        var lastUserMsg = Messages.LastOrDefault(m => m.Role == MessageRole.User);
        if (lastUserMsg == null)
            return;

        // Antwort und Hinweise nach der letzten Frage entfernen (z. B. „Websuche nicht verfügbar“ – er käme sonst doppelt)
        var index = Messages.IndexOf(lastUserMsg);
        while (Messages.Count > index + 1)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }

        // Erneut mit dem Text der letzten User-Nachricht abschicken
        CurrentInputPrompt = lastUserMsg.Content;

        // Die doppelte User-Nachricht vermeiden wir, indem wir die alte entfernen vor dem Neuaufruf
        Messages.Remove(lastUserMsg);

        // Herkunft bleibt: eine Frage aus der Mindmap sucht auch beim Neu-Generieren nicht im Internet
        await SendPromptAsync(lastUserMsg.Origin);
    }

    /// <summary>
    /// Zerlegt eine Frage in Suchwörter: trennt an allen Zeichen außer Buchstaben und Ziffern
    /// (also auch an typografischen Anführungszeichen „ “ und Bindestrichen) und entfernt Füllwörter
    /// aller Sprachen (<see cref="StudyScope.Core.Common.StopWords"/>, Dateien unter Core/Languages).
    /// </summary>
    public static List<string> ExtractSearchWords(string query) =>
        Regex.Split(query ?? string.Empty, @"[^\p{L}\p{N}]+")
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length > 2 && !StudyScope.Core.Common.StopWords.Contains(w))
            .Distinct()
            .ToList();

    /// <summary>
    /// Wählt die passendsten Textabschnitte per Stichwort-Treffer. Gibt es weniger Treffer als <paramref name="maxChunks"/>,
    /// wird mit den übrigen Abschnitten (in Dokumentreihenfolge) aufgefüllt, damit die KI genug Zusammenhang erhält.
    /// </summary>
    public static List<string> GetMostRelevantChunks(string query, List<string> allChunks, int maxChunks = 8)
    {
        if (allChunks == null || !allChunks.Any())
            return new List<string>();

        return RankChunkIndices(query, allChunks, maxChunks).Select(i => allChunks[i]).ToList();
    }

    /// <summary>
    /// Wie <see cref="GetMostRelevantChunks(string, List{string}, int)"/>, aber mit Dokumentnamen (für die Quellmarken
    /// „[D1] Datei“ im Chat mit Websuche). Auswahl und Reihenfolge sind identisch.
    /// </summary>
    public static List<SourceChunk> GetMostRelevantChunks(string query, IReadOnlyList<SourceChunk> allChunks, int maxChunks = 8)
    {
        if (allChunks == null || allChunks.Count == 0)
            return new List<SourceChunk>();

        var texts = allChunks.Select(c => c.Text).ToList();
        return RankChunkIndices(query, texts, maxChunks).Select(i => allChunks[i]).ToList();
    }

    private static List<int> RankChunkIndices(string query, List<string> allChunks, int maxChunks)
    {
        var queryWords = ExtractSearchWords(query);

        // Falls keine aussagekräftigen Suchbegriffe übrig bleiben, Standard-Auswahl nehmen
        if (!queryWords.Any())
            return Enumerable.Range(0, Math.Min(maxChunks, allChunks.Count)).ToList();

        // Chunks anhand von Treffern bewerten
        var scoredChunks = allChunks
            .Select((chunk, index) =>
            {
                var lowerChunk = chunk.ToLowerInvariant();
                var score = 0;

                foreach (var word in queryWords)
                {
                    if (lowerChunk.Contains(word))
                    {
                        // Treffer werten
                        score += 3;

                        // Häufigkeit berücksichtigen
                        var occurrences = (lowerChunk.Length - lowerChunk.Replace(word, "").Length) / word.Length;
                        score += Math.Min(occurrences - 1, 3);
                    }
                }

                return new { Chunk = chunk, Index = index, Score = score };
            })
            .ToList();

        // Chunks mit Relevanz bevorzugen, danach in Dokumentreihenfolge auffüllen
        return scoredChunks
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Concat(scoredChunks.Where(x => x.Score == 0).OrderBy(x => x.Index))
            .Take(maxChunks)
            .Select(x => x.Index)
            .ToList();
    }
    /// <summary>
    /// Bricht die aktuell laufende KI-Anfrage ab.
    /// </summary>
    public void CancelPrompt()
    {
        if (IsBusy && _cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
        }
    }

    /// <summary>
    /// Legt eine von der App gebildete Frage ins Eingabefeld (z. B. aus der Mindmap, während noch eine Antwort läuft).
    /// Wird sie abgeschickt – auch ergänzt –, behält sie ihre Herkunft und löst keine Websuche aus.
    /// </summary>
    public void SetPendingPrompt(string text, ChatQuestionOrigin origin)
    {
        CurrentInputPrompt = text;
        _pendingPromptText = origin == ChatQuestionOrigin.User ? null : text.Trim();
        _pendingPromptOrigin = origin;
    }

    /// <summary>Schickt die Frage im Eingabefeld ab (selbst eingegeben, sofern sie nicht aus <see cref="SetPendingPrompt"/> stammt).</summary>
    public Task SendPromptAsync() => SendPromptAsync(ChatQuestionOrigin.User);

    /// <summary>
    /// Schickt die Frage im Eingabefeld ab. <paramref name="origin"/> = Herkunft: Nur selbst eingegebene Fragen dürfen
    /// bei eingeschaltetem Häkchen im Internet suchen; Fragen aus der Mindmap nie (Hinweis im Verlauf).
    /// </summary>
    public async Task SendPromptAsync(ChatQuestionOrigin origin)
    {
        if (string.IsNullOrWhiteSpace(CurrentInputPrompt) || IsBusy)
            return;

        var promptText = CurrentInputPrompt.Trim();
        CurrentInputPrompt = string.Empty;

        // Liegt noch eine von der App gebildete Frage im Feld (auch ergänzt), gilt deren Herkunft
        if (_pendingPromptText != null && promptText.Contains(_pendingPromptText, StringComparison.Ordinal))
            origin = _pendingPromptOrigin;
        _pendingPromptText = null;
        _pendingPromptOrigin = ChatQuestionOrigin.User;

        // Profil merken: Die Antwort gehört in dessen Verlauf, auch wenn zwischendurch gewechselt wird
        var requestProfile = _activeProfile;

        // 1. Benutzer-Nachricht anfügen
        Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = promptText,
            Origin = origin
        });

        IsBusy = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            if (_pdfService == null)
                throw new InvalidOperationException("IPdfService ist nicht initialisiert (null). Prüfen Sie die Registrierung in Program.cs.");

            if (_llmService == null)
                throw new InvalidOperationException("ILlmService ist nicht initialisiert (null). Prüfen Sie die Registrierung in Program.cs.");

            var token = _cancellationTokenSource.Token;

            // Websuche nur bei eingeschaltetem Häkchen des fragenden Profils (Zustand frisch gelesen)
            var webSearchOn = WebSearch != null && _documentListViewModel != null
                && string.Equals(_documentListViewModel.CurrentProfile, requestProfile, StringComparison.OrdinalIgnoreCase)
                && await _documentListViewModel.RefreshWebSearchStateAsync();

            // Frage aus der Mindmap: nie im Internet suchen (Knotentitel stammen aus den Unterlagen), sichtbar vermerken
            if (webSearchOn && origin != ChatQuestionOrigin.User)
            {
                AddMessage(requestProfile, new ChatMessage { Role = MessageRole.System, Content = VmStrings.Chat_Web_SkippedMindmap });
                webSearchOn = false;
            }

            // 2. Nur aktivierte Dokumente berücksichtigen
            var activeDocs = _documentListViewModel?.Documents?
                .Where(d => d.IsSelected && !d.IsMissing && !string.IsNullOrWhiteSpace(d.FilePath) && File.Exists(d.FilePath))
                .ToList() ?? new List<DocumentItem>();

            var contextChunks = new List<SourceChunk>();

            foreach (var doc in activeDocs)
            {
                // Prüfen, ob vor/während des Dokument-Einlesens abgebrochen wurde
                token.ThrowIfCancellationRequested();

                var docChunks = await _pdfService.ExtractChunksAsync(doc.FilePath);
                if (docChunks != null)
                {
                    contextChunks.AddRange(docChunks.Select(text => new SourceChunk(doc.FileName, text)));
                }
            }

            // Treffsicherste Passagen ermitteln
            var relevant = GetMostRelevantChunks(promptText, contextChunks, maxChunks: 8);
            var relevantChunks = relevant.Select(c => c.Text).ToList();

            // 3. Antwort vom KI-Service abrufen
            // Antwortsprache: Einstellung des fragenden Profils, bei „Wie die Dokumente“ erkannt an den Auszügen
            var setting = OutputLanguageProvider == null ? null : await OutputLanguageProvider.GetSettingAsync(requestProfile);
            var language = StudyScope.Core.Localization.OutputLanguage.Resolve(setting, relevantChunks);

            // Websuche: Ins Netz geht nur die selbst eingegebene Frage (ohne Sprachzeile, Auszüge, Dateinamen)
            IReadOnlyList<WebSearchResult> webResults = Array.Empty<WebSearchResult>();
            if (webSearchOn)
            {
                try
                {
                    webResults = await WebSearch!.SearchAsync(WebSearchQuery.FromUserQuestion(promptText), language, token);
                    if (webResults.Count == 0)
                        AddMessage(requestProfile, new ChatMessage { Role = MessageRole.System, Content = VmStrings.Chat_Web_NoResults });
                }
                catch (WebSearchException ex)
                {
                    // Nie still auf die Unterlagen zurückfallen: Grund sichtbar im Verlauf
                    AddMessage(requestProfile, new ChatMessage
                    {
                        Role = MessageRole.System,
                        Content = Loc.Format(VmStrings.Chat_Web_Unavailable, ("error", ex.Message))
                    });
                }
            }

            // Eigene Kennungen, die der Benutzer selbst tippt („[StudyScope-Auftrag: …]“, „[StudyScope-Websuche]“), lösen
            // weder einen Auftrag noch den Web-Modus aus; Fragen ohne diese Zeichenfolge bleiben unverändert
            var questionForAi = TaskPrompt.Defuse(promptText);

            string prompt;
            List<string> chunksForAi;
            IReadOnlyList<ChatSource> sources = Array.Empty<ChatSource>();
            if (webResults.Count > 0)
            {
                // Unterlagen „[D1] Datei“ (gleiche Datei = gleiche Marke) und Treffer „[W1] Titel – Domain“, Frage mit Markierung
                var labelled = LabelDocumentChunks(relevant, out var documentSources);
                chunksForAi = labelled.Concat(WebContextPrompt.WebChunks(webResults)).ToList();
                prompt = WithLanguageLine(WebContextPrompt.WithMarker(questionForAi), language);
                sources = documentSources
                    .Concat(webResults.Select((r, i) => new ChatSource(ChatSourceKind.Web, WebContextPrompt.WebLabel(i + 1), r.Title, r.Url, r.Domain)))
                    .ToList();
            }
            else
            {
                // Ohne Websuche bzw. ohne Treffer: genau wie bisher
                chunksForAi = relevantChunks;
                prompt = WithLanguageLine(questionForAi, language);
            }

            var response = await _llmService.AskQuestionAsync(prompt, chunksForAi, token);

            // Der Dienst meldet einen Abbruch teils als Text statt als Ausnahme – wie einen Abbruch behandeln
            _cancellationTokenSource.Token.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(response))
            {
                response = VmStrings.Chat_NoResponse;
            }

            // 4. KI-Antwort genau einmal anfügen (mit Quellen, wenn im Internet gesucht wurde)
            AddMessage(requestProfile, new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = response,
                Sources = sources
            });
        }
        catch (OperationCanceledException)
        {
            AddMessage(requestProfile, new ChatMessage
            {
                Role = MessageRole.System,
                Content = VmStrings.Chat_Cancelled
            });
        }
        catch (LlmServiceException ex)
        {
            AddMessage(requestProfile, new ChatMessage
            {
                Role = MessageRole.System,
                Content = ex.UserMessage
            });
        }
        catch (Exception ex)
        {
            AddMessage(requestProfile, new ChatMessage
            {
                Role = MessageRole.System,
                Content = Loc.Format(VmStrings.Chat_RequestFailed, ("error", ex.Message))
            });
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Beschriftet die Auszüge für den Chat mit Websuche: „[D1] Unterlage: datei.pdf“ – Marken in der Reihenfolge des
    /// ersten Auftretens, gleiche Datei = gleiche Marke. Liefert die Quellen für die Anzeige unter der Antwort.
    /// </summary>
    public static List<string> LabelDocumentChunks(IReadOnlyList<SourceChunk> chunks, out List<ChatSource> sources)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        sources = new List<ChatSource>();
        var result = new List<string>(chunks.Count);
        foreach (var chunk in chunks)
        {
            if (!labels.TryGetValue(chunk.SourceDocument, out var label))
            {
                label = WebContextPrompt.DocumentLabel(labels.Count + 1);
                labels[chunk.SourceDocument] = label;
                sources.Add(new ChatSource(ChatSourceKind.Document, label, chunk.SourceDocument));
            }
            result.Add(WebContextPrompt.DocumentChunk(label, chunk.SourceDocument, chunk.Text));
        }
        return result;
    }

    public async Task<bool> ExportConversationToFileAsync()
    {
        if (!Messages.Any())
        {
            ExportStatusMessage = VmStrings.Chat_NothingToExport;
            return false;
        }

        try
        {
            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "StudyScope",
                "Exporte");

            Directory.CreateDirectory(exportDir);

            var fileName = $"{VmStrings.Chat_LogFilePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var fullPath = Path.Combine(exportDir, fileName);

            var text = GetConversationExportText();
            await File.WriteAllTextAsync(fullPath, text, Encoding.UTF8);

            ExportStatusMessage = Loc.Format(VmStrings.Chat_LogSaved, ("path", fullPath));
            return true;
        }
        catch (Exception ex)
        {
            ExportStatusMessage = Loc.Format(VmStrings.Chat_SaveFailed, ("error", ex.Message));
            return false;
        }
    }

    public async Task<bool> ExportConversationToCustomFileAsync(string destinationFilePath)
    {
        if (!Messages.Any())
        {
            ExportStatusMessage = VmStrings.Chat_NothingToExport;
            return false;
        }

        if (string.IsNullOrWhiteSpace(destinationFilePath))
            return false;

        try
        {
            var text = GetConversationExportText();
            await File.WriteAllTextAsync(destinationFilePath, text, Encoding.UTF8);
            ExportStatusMessage = Loc.Format(VmStrings.Chat_LogSaved, ("path", destinationFilePath));
            return true;
        }
        catch (Exception ex)
        {
            ExportStatusMessage = Loc.Format(VmStrings.Chat_SaveFailed, ("error", ex.Message));
            return false;
        }
    }

    public string GetConversationExportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine("              " + VmStrings.Chat_Log_Title);
        sb.AppendLine(" " + Loc.Format(VmStrings.Chat_Log_Created, ("date", DateTime.Now.ToString("G", CultureInfo.CurrentCulture))));
        sb.AppendLine(" " + Loc.Format(VmStrings.Chat_Log_Messages, ("count", Messages.Count)));
        sb.AppendLine("==================================================");
        sb.AppendLine();

        foreach (var msg in Messages)
        {
            var sender = msg.Role switch
            {
                MessageRole.User => VmStrings.Chat_Log_User,
                MessageRole.Assistant => VmStrings.Chat_Log_Assistant,
                MessageRole.System => VmStrings.Chat_Log_System,
                _ => msg.Role.ToString().ToUpperInvariant()
            };

            sb.AppendLine($"[{msg.Timestamp.ToString("T", CultureInfo.CurrentCulture)}] {sender}:");
            sb.AppendLine(msg.Content);

            // Quellen nur mit Marke, Name bzw. Titel und Adresse – nie Ausschnitte der Webseiten
            if (msg.Sources.Count > 0)
            {
                sb.AppendLine(VmStrings.Chat_Log_Sources);
                foreach (var source in msg.Sources)
                {
                    sb.AppendLine(source.Kind == ChatSourceKind.Web
                        ? $"  [{source.Label}] {source.Name} – {source.Url}"
                        : $"  [{source.Label}] {source.Name}");
                }
            }

            // Bewertung nur bei Assistenten-Nachrichten ausgeben
            if (msg.Role == MessageRole.Assistant)
            {
                var ratingText = msg.IsLiked switch
                {
                    true => VmStrings.Chat_Log_Liked,
                    false => VmStrings.Chat_Log_Disliked,
                    null => VmStrings.Chat_Log_Unrated
                };
                sb.AppendLine(Loc.Format(VmStrings.Chat_Log_Rating, ("rating", ratingText)));
            }

            sb.AppendLine(new string('-', 40));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public void ClearConversation()
    {
        Messages.Clear();
        ExportStatusMessage = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}