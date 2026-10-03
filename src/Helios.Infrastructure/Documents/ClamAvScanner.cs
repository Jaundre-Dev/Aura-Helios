using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Helios.Application.Abstractions.Documents;

namespace Helios.Infrastructure.Documents;

/// <summary>
/// Scans content with a ClamAV daemon over its <c>INSTREAM</c> TCP protocol: the file is streamed in
/// length-prefixed chunks and clamd answers <c>stream: OK</c> or <c>stream: {signature} FOUND</c>.
/// Any other answer, or no answer, is an error — the upload is refused rather than accepted unscanned.
/// </summary>
public sealed class ClamAvScanner(string host, int port) : IMalwareScanner
{
    private const int ChunkSize = 64 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public string Name => "clamav";

    public async Task<ScanVerdict> ScanAsync(byte[] content, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var ct = timeout.Token;

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        await using var stream = client.GetStream();

        await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), ct);

        var length = new byte[4];
        for (var offset = 0; offset < content.Length; offset += ChunkSize)
        {
            var size = Math.Min(ChunkSize, content.Length - offset);
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)size);
            await stream.WriteAsync(length, ct);
            await stream.WriteAsync(content.AsMemory(offset, size), ct);
        }

        BinaryPrimitives.WriteUInt32BigEndian(length, 0);
        await stream.WriteAsync(length, ct);

        var reply = await ReadReplyAsync(stream, ct);
        return Interpret(reply);
    }

    internal static ScanVerdict Interpret(string reply)
    {
        var text = reply.TrimEnd('\0', '\n', ' ');

        if (text.EndsWith(": OK", StringComparison.Ordinal))
        {
            return new ScanVerdict(true, null);
        }

        if (text.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            var signature = text[(text.IndexOf(": ", StringComparison.Ordinal) + 2)..^" FOUND".Length];
            return new ScanVerdict(false, signature);
        }

        throw new InvalidOperationException($"Unexpected ClamAV reply: {text}");
    }

    private static async Task<string> ReadReplyAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[1024];
        var reply = new StringBuilder();

        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            reply.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (reply.ToString().Contains('\0') || reply.Length > 4096)
            {
                break;
            }
        }

        return reply.ToString();
    }
}
