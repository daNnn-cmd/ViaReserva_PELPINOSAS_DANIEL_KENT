namespace ViaReservaERP.Services;

public interface IEmailService
{
    Task SendAsync(string toEmail, string subject, string plainTextContent, string? htmlContent = null, CancellationToken ct = default);
}
