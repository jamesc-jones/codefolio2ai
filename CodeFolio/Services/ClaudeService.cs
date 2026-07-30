using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using CodeFolio.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeFolio.Services;

public class ClaudeService : IClaudeService
{
    private const string SystemPromptCacheKey = "ai_system_prompt";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 2; // 1 retry on a transient upstream error

    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ClaudeService> _logger;

    public bool IsConfigured { get; }

    public ClaudeService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<ClaudeService> logger)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _cache = cache;
        _model = configuration["Anthropic:Model"] ?? "claude-sonnet-4-5";
        _maxTokens = int.TryParse(configuration["Anthropic:MaxTokens"], out var mt) ? mt : 1024;

        var apiKey = configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("YOUR_"))
        {
            IsConfigured = false;
        }
        else
        {
            _client = new AnthropicClient(apiKey);
            IsConfigured = true;
        }

        _logger.LogInformation("ClaudeService configured: {Configured}", IsConfigured);
    }

    public async Task<string?> AskAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || _client is null)
        {
            _logger.LogWarning("AskAsync called but AI service is not configured.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        var systemPrompt = await GetSystemPromptAsync(cancellationToken);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            try
            {
                _logger.LogInformation("AI request received. MessageLength: {Length}, Attempt: {Attempt}", userMessage.Length, attempt);

                var messages = new List<Message>
                {
                    new Message(RoleType.User, userMessage)
                };

                var parameters = new MessageParameters
                {
                    Model = _model,
                    MaxTokens = _maxTokens,
                    System = new List<SystemMessage>
                    {
                        new SystemMessage(systemPrompt)
                    },
                    Messages = messages
                };

                var response = await _client.Messages.GetClaudeMessageAsync(parameters, timeoutCts.Token);

                var text = response.Content
                    .OfType<TextContent>()
                    .FirstOrDefault()
                    ?.Text;

                _logger.LogInformation("AI response returned. InputTokens: {In}, OutputTokens: {Out}",
                    response.Usage?.InputTokens, response.Usage?.OutputTokens);

                return text;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller (HTTP request) was cancelled — not our error to swallow or retry.
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                _logger.LogWarning("Transient Anthropic API error on attempt {Attempt}/{Max}, retrying: {Message}",
                    attempt, MaxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Anthropic API");
                return null;
            }
        }

        return null;
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException;

    private async Task<string> GetSystemPromptAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SystemPromptCacheKey, out string? cached) && cached is not null)
            return cached;

        var prompt = await BuildSystemPromptAsync(cancellationToken);
        _cache.Set(SystemPromptCacheKey, prompt, TimeSpan.FromMinutes(5));
        return prompt;
    }

    private async Task<string> BuildSystemPromptAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var resumeSections = await db.ResumeSections.ToListAsync(cancellationToken);
        var projects = await db.Projects.ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("You are an AI assistant for this portfolio website.");
        sb.AppendLine("Answer questions ONLY using the portfolio information provided below — this is the complete and only source of truth about the site owner.");
        sb.AppendLine("If asked about anything not covered by this information (personal details, contact info, opinions, unrelated topics), respond exactly: \"I don't have that information in this portfolio.\"");
        sb.AppendLine("Never invent skills, experience, or projects that aren't listed below. Be concise and professional — 1 to 4 sentences unless more detail is clearly needed.");
        sb.AppendLine();

        sb.AppendLine("## Resume");
        foreach (var section in resumeSections)
        {
            var content = StripHtml(section.ResumeContent);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var title = section.ResumeTitle?.Trim();
            sb.AppendLine($"### {(string.IsNullOrWhiteSpace(title) ? "Additional Section" : title)}");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        if (projects.Count > 0)
        {
            sb.AppendLine("## Projects");
            foreach (var project in projects)
            {
                sb.AppendLine($"### {project.ProjectTitle} ({project.ProjectCourse}, {project.ProjectDate:yyyy-MM})");
                sb.AppendLine($"Technologies: {project.ProjectTechnologies}");
                sb.AppendLine(project.ProjectDescription);
                sb.AppendLine($"Contribution: {project.ProjectContribution}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = Regex.Replace(html, "<.*?>", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
