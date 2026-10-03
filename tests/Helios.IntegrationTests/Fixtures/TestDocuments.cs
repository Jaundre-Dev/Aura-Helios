using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>Synthetic documents built in code. No real customer documents are ever used in tests.</summary>
public static class TestDocuments
{
    /// <summary>A native-text PDF: each inner list is one page of lines, top to bottom.</summary>
    public static byte[] TextPdf(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 800d;
            foreach (var line in lines)
            {
                page.AddText(line, 11, new PdfPoint(50, y), font);
                y -= 18;
            }
        }

        return builder.Build();
    }

    /// <summary>Lines placed at explicit x positions on one page, for column layouts.</summary>
    public static byte[] PositionedPdf(IEnumerable<(string Text, double X, double Y)> items)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);

        foreach (var (text, x, y) in items)
        {
            page.AddText(text, 10, new PdfPoint(x, y), font);
        }

        return builder.Build();
    }

    /// <summary>A PDF whose pages carry no text layer — what a scanner produces.</summary>
    public static byte[] ImageOnlyPdf(int pages = 1)
    {
        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pages; i++)
        {
            builder.AddPage(PageSize.A4).DrawRectangle(new PdfPoint(50, 50), 400, 600);
        }

        return builder.Build();
    }

    public static byte[] ManyPagesPdf(int pages) =>
        TextPdf(Enumerable.Range(1, pages).Select(n => new[] { $"Page {n}" }).ToArray());

    /// <summary>A well-formed PDF that runs JavaScript when opened.</summary>
    public static byte[] JavaScriptPdf() => WellFormedPdf(
        "<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
        "<< /S /JavaScript /JS (app.alert(1)) >>");

    /// <summary>
    /// The same open action with its names hex-escaped (<c>/Java#53cript</c> is <c>/JavaScript</c>),
    /// a known trick for slipping past naive byte scans.
    /// </summary>
    public static byte[] EscapedJavaScriptPdf() => WellFormedPdf(
        "<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
        "<< /S /Java#53cript /J#53 (app.alert(1)) >>");

    /// <summary>
    /// Writes objects 1..n with a correct cross-reference table, so a strict parser reads it and
    /// structural checks — not just byte scans — are exercised. Object 1 must be the catalogue.
    /// </summary>
    public static byte[] WellFormedPdf(params string[] objects)
    {
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>A PNG header declaring the given size; enough for header-based checks.</summary>
    public static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        bytes[24] = 8;
        bytes[25] = 2;
        return bytes;
    }

    /// <summary>The standard, harmless EICAR antivirus test string (not malware).</summary>
    public static readonly string Eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    public static MultipartFormDataContent Form(byte[] content, string fileName, string mediaType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }
}

/// <summary>
/// A tiny stand-in for clamd speaking the real INSTREAM protocol on a loopback port: it reports the
/// EICAR test signature when the stream contains it, and OK otherwise.
/// </summary>
public sealed class FakeClamd : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public int Port { get; }
    public int Scans;

    public FakeClamd()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptAsync);
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            var command = new byte[10];
            await stream.ReadExactlyAsync(command);

            var data = new MemoryStream();
            var length = new byte[4];
            while (true)
            {
                await stream.ReadExactlyAsync(length);
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(length);
                if (size == 0)
                {
                    break;
                }

                var chunk = new byte[size];
                await stream.ReadExactlyAsync(chunk);
                data.Write(chunk);
            }

            Interlocked.Increment(ref Scans);
            var infected = Encoding.ASCII.GetString(data.ToArray()).Contains("EICAR-STANDARD-ANTIVIRUS-TEST-FILE");
            var reply = infected ? "stream: Win.Test.EICAR_HDB-1 FOUND\0" : "stream: OK\0";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { await _loop; } catch { /* stopping */ }
    }
}
