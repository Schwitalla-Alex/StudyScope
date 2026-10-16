using System;
using System.Collections.Generic;
using System.Text;


namespace StudyScope.Core.Interfaces;

/// <summary>
/// ADer PDF-Parser-Dienst, der für das Einlesen von PDF-Dateien, das Extrahieren von Text und das Bilden von Chunks (Textstellen) zuständig ist. 
/// Damit speisen wir auch die Zählung für die Statusleiste (Anzahl der Textstellen).  Für eine leichtgewichtige,
/// plattformunabhängige (Windows & Linux) C#-Lösung eignet sich die Open-Source-Bibliothek PdfPig hervorragend, da sie ohne native C++-Abhängigkeiten auskommt.
/// </summary>
public interface IDocumentService
{
    Task<int> GetChunkCountAsync(string filePath);
    Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250);
}
