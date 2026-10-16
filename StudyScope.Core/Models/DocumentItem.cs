using System;
using System.Collections.Generic;
using System.Text;

namespace StudyScope.Core.Models
{
    /// <summary>
    /// Repräsentiert ein geladenes PDF-Dokument inklusive Status (ob die Datei noch auf der Festplatte existiert oder durchgestrichen werden soll)
    /// </summary>
    public class DocumentItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;
        public bool IsMissing { get; set; } = false; // Steuert die durchgestrichene Anzeige
        public int ExtractedChunkCount { get; set; } = 0; // Für die Statusleiste
    }
}
