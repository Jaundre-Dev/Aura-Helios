using System.Net;
using System.Net.Mail;
using Helios.Application.Abstractions.Messaging;

namespace Helios.Infrastructure.Messaging;

/// <summary>SMTP settings, bound from <c>Helios:Email:Smtp</c>. Credentials come from secret configuration only.</summary>
public sealed class SmtpEmailOptions
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
}

/// <summary>
/// Sends through any SMTP relay the owner contracts (most transactional email providers offer one).
/// TLS is on by default. Message bodies are never logged.
/// </summary>
public sealed class SmtpEmailSender(SmtpEmailOptions options, string from) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.EnableSsl };
        if (!string.IsNullOrEmpty(options.Username))
        {
            client.Credentials = new NetworkCredential(options.Username, options.Password);
        }

        using var mail = new MailMessage(from, message.To, message.Subject, message.TextBody);
        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>
/// Development only: writes each message as a file in a local folder instead of sending it, so the
/// verification and reset flows can be exercised without an email provider. Production refuses it.
/// </summary>
public sealed class FileDropEmailSender(string directory, TimeProvider clock) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var name = $"{clock.GetUtcNow():yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.eml";
        var content = $"To: {message.To}\r\nSubject: {message.Subject}\r\n\r\n{message.TextBody}\r\n";
        await File.WriteAllTextAsync(Path.Combine(directory, name), content, cancellationToken);
    }
}
