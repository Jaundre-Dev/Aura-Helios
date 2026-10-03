using System.Collections.Concurrent;
using System.Net;

namespace Helios.IntegrationTests.Fixtures;

public sealed record ReceivedWebhook(Uri Url, string Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>An in-memory webhook receiver standing in for customer servers.</summary>
public sealed class RecordingWebhookReceiver : HttpMessageHandler
{
    public ConcurrentQueue<ReceivedWebhook> Received { get; } = new();
    public ConcurrentDictionary<string, HttpStatusCode> StatusByHost { get; } = new();

    public IReadOnlyList<ReceivedWebhook> For(string host) =>
        Received.Where(r => r.Url.Host == host).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);

        Received.Enqueue(new ReceivedWebhook(request.RequestUri!, body, headers));

        return new HttpResponseMessage(StatusByHost.GetValueOrDefault(request.RequestUri!.Host, HttpStatusCode.OK));
    }
}
