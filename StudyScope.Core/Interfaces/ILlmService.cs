using System;
using System.Collections.Generic;
using System.Text;

using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Abstraktion für die Kommunikation mit der ausgewählten KI:
/// </summary>
public interface ILlmService
{
    Task<string> AskQuestionAsync(string prompt,IEnumerable<string> contextChunks,CancellationToken cancellationToken = default);

    /// <summary>
    /// Prüft die Verbindung. Ohne Argument wird die Konfiguration des aktiven Profils geprüft,
    /// mit Argument die übergebene (z. B. die noch nicht gespeicherten Werte aus den Einstellungen).
    /// Cloud-Dienste werden über ihre (kostenlose) Modellliste geprüft; ein abgelehnter Schlüssel, eine Störung
    /// oder ein Netzwerkfehler wird als <see cref="StudyScope.Core.Common.LlmServiceException"/> mit verständlicher Meldung geworfen.
    /// </summary>
    Task<bool> ValidateConnectionAsync(AiConfig? config = null);
}
