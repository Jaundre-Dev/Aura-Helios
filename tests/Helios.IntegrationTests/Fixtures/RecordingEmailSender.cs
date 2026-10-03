using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Helios.Application.Abstractions.Messaging;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>An in-memory mailbox standing in for the email provider.</summary>
public sealed partial class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The user id and token from the latest link of the given kind sent to an address.</summary>
    public (Guid UserId, string Token)? LatestLink(string to, string path)
    {
        var message = Sent.LastOrDefault(m => m.To == to && m.TextBody.Contains($"/{path}?", StringComparison.Ordinal));
        if (message is null)
        {
            return null;
        }

        var match = Link().Match(message.TextBody);
        return (Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
    }

    [GeneratedRegex(@"\?user=(?<user>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex Link();
}
