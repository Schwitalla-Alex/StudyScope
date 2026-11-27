using System;
using System.Collections.Generic;
using System.Text;

namespace StudyScope.Core.Models
{

    public enum MessageRole
    {
        User,
        Assistant,
        System
    }

    /// <summary>
    /// Herkunft einer Chatfrage. Nur selbst eingegebene Fragen dürfen eine Websuche auslösen; Fragen, die die App aus
    /// anderen Inhalten bildet (z. B. „Im Chat erklären lassen“ in der Mindmap aus Knotentiteln), nie.
    /// </summary>
    public enum ChatQuestionOrigin
    {
        /// <summary>Vom Benutzer selbst eingegeben.</summary>
        User,
        /// <summary>Aus der Mindmap übergeben (Knotentitel stammen aus den Unterlagen).</summary>
        Mindmap
    }

    /// <summary>Knopf an einem Systemhinweis im Chat (nur im Speicher, nicht im Chat-Protokoll).</summary>
    public enum ChatMessageAction
    {
        None,
        /// <summary>„⚙ Einstellungen öffnen“ → Einstellungen › Websuche (nur, wenn die Einstellungen helfen können).</summary>
        OpenWebSearchSettings
    }

    /// <summary>Art einer Quelle unter einer Chat-Antwort.</summary>
    public enum ChatSourceKind
    {
        Document,
        Web
    }

    /// <summary>
    /// Quelle einer Chat-Antwort mit Websuche: Marke („D1“, „W1“), Name (Dateiname bzw. Titel der Seite) und bei
    /// Web-Quellen Adresse und Domain. Nur im Speicher wie der ganze Verlauf; Ausschnitte der Webseiten werden nie
    /// gespeichert.
    /// </summary>
    public sealed record ChatSource(ChatSourceKind Kind, string Label, string Name, string? Url = null, string? Domain = null);

    /// <summary>
    /// Für den Gesprächsverlauf zwischen Benutzer und KI:
    /// </summary>
    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public MessageRole Role { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        // IsLiked für Feedback: null = unbewertet, true = Daumen hoch, false = Daumen runter
        public bool? IsLiked { get; set; }

        /// <summary>Herkunft der Frage (nur bei Benutzer-Nachrichten; „Neu generieren“ übernimmt sie).</summary>
        public ChatQuestionOrigin Origin { get; set; } = ChatQuestionOrigin.User;

        /// <summary>Quellen einer Antwort mit Websuche (Unterlagen und Internet getrennt); sonst leer.</summary>
        public IReadOnlyList<ChatSource> Sources { get; set; } = Array.Empty<ChatSource>();

        /// <summary>Knopf am Systemhinweis (z. B. Einstellungen der Websuche öffnen); sonst <see cref="ChatMessageAction.None"/>.</summary>
        public ChatMessageAction Action { get; set; } = ChatMessageAction.None;
    }
}
