using Microsoft.AspNetCore.Identity.UI.Services;
using SendGrid;
using SendGrid.Helpers.Mail;
using Microsoft.Extensions.Logging;

namespace CodeFolio.Services;

public class EmailSender : IEmailSender
{
    private readonly string? _sendGridApiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;

    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
    {
        _logger = logger;
        _sendGridApiKey = configuration["SendGrid:ApiKey"];
        _fromEmail = configuration["SendGrid:FromEmail"] ?? "no-reply@example.com";
        _fromName = configuration["SendGrid:FromName"] ?? "CodeFolio";

        if (string.IsNullOrEmpty(_sendGridApiKey))
        {
            _logger.LogWarning("SendGrid API key is missing. Email sending is disabled.");
        }
    }

    public async Task SendEmailAsync(string email, string subject, string message)
    {
        if (string.IsNullOrEmpty(_sendGridApiKey))
        {
            _logger.LogWarning("Skipped sending email to {Email}: SendGrid API key is not configured.", email);
            return;
        }

        try
        {
            _logger.LogInformation("Sending email to: {Email} with subject: {Subject} at {Time}",
                email, subject, DateTime.Now);

            var client = new SendGridClient(_sendGridApiKey);
            var from = new EmailAddress(_fromEmail, _fromName);
            var to = new EmailAddress(email);
            var msg = MailHelper
                .CreateSingleEmail(from, to, subject, "Welcome to CodeFolio!", message);

            var response = await client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent successfully to {Email}", email);
            }
            else
            {
                var errorMessage = await response.Body.ReadAsStringAsync();
                _logger.LogWarning("An error occured while sending an email to {Email}. Response: {Error}",
                    email, errorMessage);
            }

        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "An error occurred while sending the email to {Email}", email);
            return;
        }


    }

}