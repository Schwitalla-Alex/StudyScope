using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

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

    // Füllwörter, die in fast jedem Text vorkommen und die Suche verfälschen würden
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "wer", "wie", "was", "wo", "wann", "warum", "welche", "welcher", "welches", "wieso", "weshalb",
        "der", "die", "das", "ein", "eine", "einer", "eines", "einem", "einen", "und", "oder", "ist",
        "sind", "in", "im", "dem", "den", "des", "auf", "zu", "zum", "zur", "mit", "von", "vom", "du", "sie", "es",
        "ich", "mir", "mich", "mein", "meine", "meiner", "meinen", "meinem", "wir", "uns", "unsere", "ihr", "ihre", "ihnen",
        "für", "über", "bei", "aus", "als", "auch", "nicht", "kein", "keine", "wird", "werden", "wurde", "kann", "können",
        "muss", "müssen", "soll", "sollte", "nach", "sich", "dass", "dies", "diese", "dieser", "dieses", "hier", "dort",
        "bitte", "erkläre", "erklären", "erklär", "beschreibe", "beschreiben", "nenne", "nennen", "gib", "gibt",
        "anhand", "zusammenhang", "zusammenhängen", "unterlagen", "dokument", "dokumente", "dokumenten", "text", "textes"
    };

    /// <summary>
    /// Zerlegt eine Frage in Suchwörter: trennt an allen Zeichen außer Buchstaben und Ziffern
    /// (also auch an typografischen Anführungszeichen „ “ und Bindestrichen) und entfernt Füllwörter.
    /// </summary>
    public static List<string> ExtractSearchWords(string query) =>
        Regex.Split(query ?? string.Empty, @"[^\p{L}\p{N}]+")
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
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

        var queryWords = ExtractSearchWords(query);

        // Falls keine aussagekräftigen Suchbegriffe übrig bleiben, Standard-Auswahl nehmen
        if (!queryWords.Any())
            return allChunks.Take(maxChunks).ToList();

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
            .Select(x => x.Chunk)
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

    public async Task SendPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentInputPrompt) || IsBusy)
            return;

        var promptText = CurrentInputPrompt.Trim();
        CurrentInputPrompt = string.Empty;

        // Profil merken: Die Antwort gehört in dessen Verlauf, auch wenn zwischendurch gewechselt wird
        var requestProfile = _activeProfile;

        // 1. Benutzer-Nachricht anfügen
        Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = promptText
        });

        IsBusy = true;
        _cancellationTokenSource = new CancellationTokenSource();

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
                // Prüfen, ob vor/während des Dokument-Einlesens abgebrochen wurde
                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                var docChunks = await _pdfService.ExtractChunksAsync(doc.FilePath);
                if (docChunks != null)
                {
                    contextChunks.AddRange(docChunks);
                }
            }

            // Treffsicherste Passagen ermitteln
            var relevantChunks = GetMostRelevantChunks(promptText, contextChunks, maxChunks: 8);

            // 3. Antwort vom KI-Service abrufen
            var response = await _llmService.AskQuestionAsync(promptText, relevantChunks, _cancellationTokenSource.Token);

            // Der Dienst meldet einen Abbruch teils als Text statt als Ausnahme – wie einen Abbruch behandeln
            _cancellationTokenSource.Token.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(response))
            {
                response = "Keine Antwort vom Sprachmodell erhalten.";
            }

            // 4. KI-Antwort genau einmal anfügen
            AddMessage(requestProfile, new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = response
            });
        }
        catch (OperationCanceledException)
        {
            AddMessage(requestProfile, new ChatMessage
            {
                Role = MessageRole.System,
                Content = "Anfrage wurde durch den Benutzer abgebrochen."
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
                Content = $"Fehler bei der Anfrage: {ex.Message}"
            });
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
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