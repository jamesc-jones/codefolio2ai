using Microsoft.AspNetCore.Identity.UI.Services;
using SendGrid;
using SendGrid.Helpers.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;

namespace CodeFolio.Services;

public class EmailSender : IEmailSender
{
    private readonly string _sendGridApiKey;
    
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
    {
        _sendGridApiKey = configuration["SendGrid:ApiKey"] 
                          ?? throw new ArgumentNullException("SendGrid Key is missing");
        _logger = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string message)
    {
        try
        {
            _logger.LogInformation("Sending email to: {Email} with subject: {Subject} at {Time}",
                email, subject, DateTime.Now);

            var client = new SendGridClient(_sendGridApiKey);
            var from = new EmailAddress("james.jones@georgebrown.ca", "CodeFolio Default Sender");
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
            throw;
        }
        
        
    }
    
}