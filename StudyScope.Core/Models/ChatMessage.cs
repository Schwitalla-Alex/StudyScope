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
    }
}
