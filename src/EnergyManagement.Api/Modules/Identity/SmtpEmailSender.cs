using System.Net;
using System.Net.Mail;

namespace EnergyManagement.Api.Modules.Identity;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        var host = configuration["EMS_SMTP_HOST"];
        var sender = configuration["EMS_SMTP_FROM"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sender))
            throw new InvalidOperationException(
                "SMTP delivery requires EMS_SMTP_HOST and EMS_SMTP_FROM.");

        var port = int.TryParse(configuration["EMS_SMTP_PORT"], out var configuredPort)
            ? configuredPort
            : 587;
        if (port is < 1 or > 65535)
            throw new InvalidOperationException("EMS_SMTP_PORT must be between 1 and 65535.");

        var enableSsl = !bool.TryParse(configuration["EMS_SMTP_ENABLE_SSL"], out var configuredSsl)
            || configuredSsl;
        var username = configuration["EMS_SMTP_USERNAME"];
        var password = configuration["EMS_SMTP_PASSWORD"];

        using var message = new MailMessage(sender, recipient, subject, body);
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password ?? string.Empty);

        await client.SendMailAsync(message, cancellationToken);
    }
}
