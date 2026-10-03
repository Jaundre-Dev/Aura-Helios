using System.Net;
using System.Net.Sockets;
using System.Text;
using Helios.Application.Abstractions.Webhooks;

namespace Helios.Infrastructure.Webhooks;

/// <summary>
/// Sends webhook deliveries through <see cref="ClientName"/>, whose handler checks the address it
/// is actually about to connect to — after DNS resolution, on every connection — so a hostname that
/// later resolves to an internal address (DNS rebinding) is refused. Redirects are never followed.
/// </summary>
public sealed class GuardedWebhookSender(IHttpClientFactory clients) : IWebhookSender
{
    public const string ClientName = "helios-webhooks";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<WebhookSendResult> SendAsync(
        Uri destination,
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, destination)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            using var response = await clients.CreateClient(ClientName).SendAsync(request, timeout.Token);
            return new WebhookSendResult((int)response.StatusCode, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            return new WebhookSendResult(null, ex.Message);
        }
    }

    /// <summary>The primary handler: no redirects, no proxies, and a connect-time address guard.</summary>
    public static SocketsHttpHandler CreateGuardedHandler(bool allowPrivateNetworks) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);

            var allowed = addresses.Where(a => allowPrivateNetworks || !OutboundUrlPolicy.IsDisallowed(a)).ToArray();
            if (allowed.Length == 0)
            {
                throw new HttpRequestException(
                    $"Webhook destination '{context.DnsEndPoint.Host}' resolves only to addresses HELIOS does not deliver to.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(allowed[0], context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}
