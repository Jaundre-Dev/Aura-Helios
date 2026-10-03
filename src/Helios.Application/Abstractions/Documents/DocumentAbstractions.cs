namespace Helios.Application.Abstractions.Documents;

/// <summary>
/// Establishes what an uploaded file really is, from its bytes, and refuses anything unsafe or out
/// of bounds before it is stored: wrong or spoofed type, malformed or encrypted PDFs, PDFs with
/// active content (JavaScript, launch actions, embedded files, XFA), too many pages, image
/// dimensions that signal a decompression bomb.
/// </summary>
public interface IDocumentInspector
{
    DocumentInspection Inspect(ReadOnlySpan<byte> content, string? declaredMediaType);
}

/// <param name="Rejection">A machine-readable reason the file is refused, or null when accepted.</param>
public sealed record DocumentInspection(
    string? MediaType,
    int PageCount,
    bool HasTextLayer,
    string? Rejection,
    string? Detail = null)
{
    public static DocumentInspection Reject(string code, string detail) => new(null, 0, false, code, detail);
}

/// <summary>Scans file content for malware. Production requires one to be configured.</summary>
public interface IMalwareScanner
{
    string Name { get; }

    Task<ScanVerdict> ScanAsync(byte[] content, CancellationToken cancellationToken);
}

public sealed record ScanVerdict(bool Clean, string? Signature);

/// <summary>
/// Reads the native text layer of a PDF with positions. No OCR: a page without a text layer
/// returns no words, and callers must say so rather than guess.
/// </summary>
public interface IPdfTextReader
{
    PdfText Read(byte[] content);
}

public sealed record PdfText(IReadOnlyList<PdfTextPage> Pages);

/// <param name="Lines">Words grouped into reading-order lines.</param>
public sealed record PdfTextPage(int Number, double Width, double Height, IReadOnlyList<PdfTextLine> Lines)
{
    public bool HasText => Lines.Count > 0;
}

/// <summary>A line of text and its bounding box, normalised to 0–1 of the page (origin top-left).</summary>
public sealed record PdfTextLine(string Text, TextBox Box, IReadOnlyList<PdfTextWord> Words);

public sealed record PdfTextWord(string Text, TextBox Box);

public sealed record TextBox(double Left, double Top, double Right, double Bottom)
{
    public double[] ToArray() => [Math.Round(Left, 4), Math.Round(Top, 4), Math.Round(Right, 4), Math.Round(Bottom, 4)];
}
