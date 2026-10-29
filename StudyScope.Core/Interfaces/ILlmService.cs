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
    Task<bool> ValidateConnectionAsync();
    
}
