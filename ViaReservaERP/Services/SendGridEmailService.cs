using SendGrid;
using SendGrid.Helpers.Mail;

namespace ViaReservaERP.Services;

public class SendGridEmailService : IEmailService
{
    private readonly IConfiguration _config;

    public SendGridEmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendAsync(string toEmail, string subject, string plainTextContent, string? htmlContent = null, CancellationToken ct = default)
    {
        var apiKey = _config["SendGrid:ApiKey"];
        var fromEmail = _config["SendGrid:FromEmail"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(fromEmail))
        {
            // Email is not configured. Return gracefully to avoid crashing the business flow.
            return;
        }

        var fromName = _config["SendGrid:FromName"];
        if (string.IsNullOrWhiteSpace(fromName))
            fromName = "ViaReserva";

        var client = new SendGridClient(apiKey);

        var msg = new SendGridMessage
        {
            From = new EmailAddress(fromEmail, fromName),
            Subject = subject,
            PlainTextContent = plainTextContent,
            HtmlContent = string.IsNullOrWhiteSpace(htmlContent) ? null : htmlContent
        };
        msg.AddTo(new EmailAddress(toEmail));

        var response = await client.SendEmailAsync(msg, ct);
        if ((int)response.StatusCode >= 400)
        {
            var body = await response.Body.ReadAsStringAsync(ct);
            // Log to console for the developer to see the exact reason
            Console.WriteLine($"!!! SENDGRID ERROR: {(int)response.StatusCode} - {body}");
            throw new InvalidOperationException($"SendGrid send failed: {(int)response.StatusCode} {response.StatusCode}. Reason: {body}");
        }
    }
}
