namespace Helios.Application.Abstractions.Messaging;

/// <summary>A transactional email. Bodies may carry single-use links; they are never logged.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody);

/// <summary>
/// Sends transactional email (verification, password reset). Configured by <c>Helios:Email:Sender</c>;
/// with none configured, flows that need email answer 503 instead of pretending to have sent it.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
