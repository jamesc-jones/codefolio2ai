using CodeFolio.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace CodeFolio.Controllers;

[ApiController]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly IClaudeService _claudeService;
    private readonly ILogger<AiController> _logger;

    public AiController(IClaudeService claudeService, ILogger<AiController> logger)
    {
        _claudeService = claudeService;
        _logger = logger;
    }

    [HttpPost("chat")]
    [EnableRateLimiting("ai-chat")]
    public async Task<IActionResult> Chat(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!_claudeService.IsConfigured)
        {
            _logger.LogWarning("AI chat request received but service is not configured.");
            return StatusCode(503, new ChatResponse
            {
                Success = false,
                Error = "AI assistant is not available right now. Please use the contact form."
            });
        }

        var reply = await _claudeService.AskAsync(request.Message, cancellationToken);

        if (reply is null)
        {
            return StatusCode(503, new ChatResponse
            {
                Success = false,
                Error = "The AI assistant encountered an error. Please try again."
            });
        }

        return Ok(new ChatResponse
        {
            Success = true,
            Reply = reply
        });
    }
}

public class ChatRequest
{
    [Required]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Message must be between 1 and 500 characters.")]
    public string Message { get; set; } = string.Empty;
}

public class ChatResponse
{
    public bool Success { get; set; }
    public string? Reply { get; set; }
    public string? Error { get; set; }
}
