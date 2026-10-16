using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels;

public class ChatViewModel : INotifyPropertyChanged
{
    private readonly ILlmService _llmService;
    private readonly IDocumentService _pdfService;
    private readonly DocumentListViewModel _documentListViewModel;
    private string _currentInputPrompt = string.Empty;
    private bool _isBusy = false;
    private string _exportStatusMessage = string.Empty;

    public ChatViewModel(ILlmService llmService, IDocumentService pdfService, DocumentListViewModel documentListViewModel)
    {
        _llmService = llmService;
        _pdfService = pdfService;
        _documentListViewModel = documentListViewModel;
        Messages = new ObservableCollection<ChatMessage>();
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

        // Falls die letzte Nachricht eine Assistenten-Nachricht war, diese entfernen
        if (Messages.Last().Role == MessageRole.Assistant)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }

        // Erneut mit dem Text der letzten User-Nachricht abschicken
        CurrentInputPrompt = lastUserMsg.Content;

        // Die doppelte User-Nachricht vermeiden wir, indem wir die alte entfernen vor dem Neuaufruf
        Messages.Remove(lastUserMsg);

        await SendPromptAsync();
    }

    private List<string> GetMostRelevantChunks(string query, List<string> allChunks, int maxChunks = 8)
    {
        if (allChunks == null || !allChunks.Any())
            return new List<string>();

        // Zerlege die Frage in einzelne Suchwörter (Stoppwörter herausfiltern)
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "wer", "wie", "was", "wo", "wann", "warum", "welche", "welcher", "welches",
            "der", "die", "das", "ein", "eine", "einer", "eines", "und", "oder", "ist",
            "sind", "in", "im", "dem", "den", "auf", "zu", "mit", "von", "du", "sie", "es"
        };

        var queryWords = query
            .Split(new[] { ' ', '?', '!', '.', ',', ';', ':', '-', '\"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();

        // Falls keine aussagekräftigen Suchbegriffe übrig bleiben, Standard-Auswahl nehmen
        if (!queryWords.Any())
            return allChunks.Take(maxChunks).ToList();

        // Chunks anhand von Treffern bewerten
        var scoredChunks = allChunks
            .Select(chunk =>
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

                return new { Chunk = chunk, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        // Chunks mit Relevanz bevorzugen
        var relevantHits = scoredChunks
            .Where(x => x.Score > 0)
            .Select(x => x.Chunk)
            .Take(maxChunks)
            .ToList();

        if (!relevantHits.Any())
        {
            return allChunks.Take(maxChunks).ToList();
        }

        return relevantHits;
    }

    public async Task SendPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentInputPrompt) || IsBusy)
            return;

        var promptText = CurrentInputPrompt.Trim();
        CurrentInputPrompt = string.Empty;

        // 1. Benutzer-Nachricht anfügen
        Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = promptText
        });

        IsBusy = true;

        try
        {
            if (_pdfService == null)
                throw new InvalidOperationException("IPdfService ist nicht initialisiert (null). Prüfen Sie die Registrierung in Program.cs.");

            if (_llmService == null)
                throw new InvalidOperationException("ILlmService ist nicht initialisiert (null). Prüfen Sie die Registrierung in Program.cs.");

            // 2. Nur aktivierte Dokumente berücksichtigen
            var activeDocs = _documentListViewModel?.Documents?
                .Where(d => d.IsSelected && !d.IsMissing && !string.IsNullOrWhiteSpace(d.FilePath) && File.Exists(d.FilePath))
                .ToList() ?? new List<DocumentItem>();

            var contextChunks = new List<string>();

            foreach (var doc in activeDocs)
            {
                var docChunks = await _pdfService.ExtractChunksAsync(doc.FilePath);
                if (docChunks != null)
                {
                    contextChunks.AddRange(docChunks);
                }
            }

            // Treffsicherste Passagen ermitteln
            var relevantChunks = GetMostRelevantChunks(promptText, contextChunks, maxChunks: 8);

            // 3. Antwort vom Sprachmodell anfordern
            var response = await _llmService.AskQuestionAsync(promptText, relevantChunks);

            if (string.IsNullOrWhiteSpace(response))
            {
                response = "Keine Antwort vom Sprachmodell erhalten.";
            }

            // 4. KI-Antwort anfügen
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = response
            });
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = $"Fehler bei der Anfrage: {ex.Message}\nStacktrace: {ex.StackTrace}"
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ExportConversationToFileAsync()
    {
        if (!Messages.Any())
        {
            ExportStatusMessage = "Hinweis: Es ist noch kein Gesprächsverlauf vorhanden, der exportiert werden kann.";
            return false;
        }

        try
        {
            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "StudyScope",
                "Exporte");

            Directory.CreateDirectory(exportDir);

            var fileName = $"ChatProtokoll_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var fullPath = Path.Combine(exportDir, fileName);

            var text = GetConversationExportText();
            await File.WriteAllTextAsync(fullPath, text, Encoding.UTF8);

            ExportStatusMessage = $"Protokoll erfolgreich gespeichert unter:\n{fullPath}";
            return true;
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Fehler beim Speichern: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> ExportConversationToCustomFileAsync(string destinationFilePath)
    {
        if (!Messages.Any())
        {
            ExportStatusMessage = "Hinweis: Es ist noch kein Gesprächsverlauf vorhanden, der exportiert werden kann.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(destinationFilePath))
            return false;

        try
        {
            var text = GetConversationExportText();
            await File.WriteAllTextAsync(destinationFilePath, text, Encoding.UTF8);
            ExportStatusMessage = $"Protokoll erfolgreich gespeichert unter:\n{destinationFilePath}";
            return true;
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Fehler beim Speichern: {ex.Message}";
            return false;
        }
    }

    public string GetConversationExportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine("              STUDYSCOPE CHAT-PROTOKOLL           ");
        sb.AppendLine($" Erstellt am: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($" Nachrichten: {Messages.Count}");
        sb.AppendLine("==================================================");
        sb.AppendLine();

        foreach (var msg in Messages)
        {
            var sender = msg.Role switch
            {
                MessageRole.User => "BENUTZER",
                MessageRole.Assistant => "ASSISTENT (KI)",
                MessageRole.System => "SYSTEM",
                _ => msg.Role.ToString().ToUpperInvariant()
            };

            sb.AppendLine($"[{msg.Timestamp:HH:mm:ss}] {sender}:");
            sb.AppendLine(msg.Content);

            // Bewertung nur bei Assistenten-Nachrichten ausgeben
            if (msg.Role == MessageRole.Assistant)
            {
                var ratingText = msg.IsLiked switch
                {
                    true => "👍 Hilfreich / Gut",
                    false => "👎 Nicht hilfreich / Verbesserungsbedürftig",
                    null => "— Unbewertet"
                };
                sb.AppendLine($"[Bewertung: {ratingText}]");
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