namespace CodeFolio.Services;

public interface IClaudeService
{
    Task<string?> AskAsync(string userMessage, CancellationToken cancellationToken = default);

    bool IsConfigured { get; }
}
