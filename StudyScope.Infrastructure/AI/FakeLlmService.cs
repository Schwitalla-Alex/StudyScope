using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.AI;

public class FakeLlmService : ILlmService
{
    public Task<bool> ValidateConnectionAsync(AiConfig? config = null)
    {
        return Task.FromResult(true);
    }

    public Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks, CancellationToken cancellationToken = default)
    {
        var chunks = contextChunks?.ToList() ?? new List<string>();

        // Gleiche Weiche wie der Dispatcher: Aufträge nur an der Kennzeile, sonst Chat bzw. Web-Chat
        return Task.FromResult(FakeAiRouter.Answer(prompt, chunks));
    }
}
