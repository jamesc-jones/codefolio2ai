using Microsoft.AspNetCore.Identity.UI.Services;

namespace CodeFolio.Tests.Fakes;

/// <summary>No-op IEmailSender for tests — avoids any real network/SendGrid call.</summary>
public class FakeEmailSender : IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string message) => Task.CompletedTask;
}
