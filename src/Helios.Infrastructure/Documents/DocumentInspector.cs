using System.Buffers.Binary;
using System.Text;
using Helios.Application.Abstractions.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Helios.Infrastructure.Documents;

/// <summary>Bounds for accepted documents, bound from <c>Helios:Uploads</c>.</summary>
public sealed record DocumentLimits
{
    public long MaxBytes { get; init; } = 10 * 1024 * 1024;
    public int MaxPages { get; init; } = 50;
    public long MaxImagePixels { get; init; } = 40_000_000;
    public int MaxImageSide { get; init; } = 12_000;
}

/// <summary>
/// Byte-level type detection and structural safety checks. The declared type is only ever compared
/// against the detected one; it never decides how a file is handled.
/// <para>
/// PDF active content is refused two ways: a token-aware scan of the raw bytes for action and
/// embedding names, and a structural check of the document catalogue (name trees, open actions,
/// additional actions, XFA forms). Names inside compressed object streams can evade the raw scan;
/// the catalogue check and, in Production, the malware scanner are the further layers.
/// </para>
/// </summary>
public sealed class DocumentInspector(DocumentLimits limits) : IDocumentInspector
{
    public const string Pdf = "application/pdf";
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";
    public const string Tiff = "image/tiff";

    private static readonly string[] ForbiddenPdfNames =
        ["JavaScript", "JS", "Launch", "EmbeddedFile", "EmbeddedFiles", "RichMedia", "XFA", "SubmitForm", "ImportData", "GoToE"];

    public DocumentInspection Inspect(ReadOnlySpan<byte> content, string? declaredMediaType)
    {
        if (content.Length == 0)
        {
            return DocumentInspection.Reject("empty_file", "The file is empty.");
        }

        if (content.Length > limits.MaxBytes)
        {
            return DocumentInspection.Reject("file_too_large", $"The file exceeds {limits.MaxBytes} bytes.");
        }

        var detected = Detect(content);
        if (detected is null)
        {
            return DocumentInspection.Reject("unsupported_type", "Only PDF, PNG, JPEG and TIFF files are accepted.");
        }

        var declared = declaredMediaType?.Split(';')[0].Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(declared) && declared != "application/octet-stream" &&
            declared != detected && !(declared == "image/jpg" && detected == Jpeg))
        {
            return DocumentInspection.Reject("type_mismatch", $"The file is {detected}, not {declared}.");
        }

        return detected == Pdf ? InspectPdf(content.ToArray()) : InspectImage(content, detected);
    }

    private static string? Detect(ReadOnlySpan<byte> b)
    {
        // A PDF header may follow a little leading junk; the specification tolerates up to 1 KB.
        if (b[..Math.Min(b.Length, 1024)].IndexOf("%PDF-"u8) >= 0) return Pdf;
        if (b.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png;
        if (b.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return Jpeg;
        if (b.StartsWith("II*\0"u8) || b.StartsWith("MM\0*"u8)) return Tiff;
        return null;
    }

    private DocumentInspection InspectPdf(byte[] bytes)
    {
        if (FindForbiddenName(bytes) is { } name)
        {
            return DocumentInspection.Reject("active_content", $"PDFs containing /{name} are not accepted.");
        }

        try
        {
            using var document = PdfDocument.Open(bytes);

            if (document.IsEncrypted)
            {
                return DocumentInspection.Reject("encrypted", "Encrypted or password-protected PDFs are not accepted.");
            }

            if (CatalogProblem(document) is { } problem)
            {
                return DocumentInspection.Reject("active_content", problem);
            }

            var pages = document.NumberOfPages;
            if (pages < 1)
            {
                return DocumentInspection.Reject("malformed", "The PDF has no pages.");
            }

            if (pages > limits.MaxPages)
            {
                return DocumentInspection.Reject("too_many_pages", $"The PDF has {pages} pages; the limit is {limits.MaxPages}.");
            }

            var allText = Enumerable.Range(1, pages).All(n => document.GetPage(n).Letters.Count > 0);
            return new DocumentInspection(Pdf, pages, allText, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DocumentInspection.Reject("malformed", "The PDF could not be parsed.");
        }
    }

    /// <summary>
    /// Token-aware: "/JS" matches only as a whole name, not as the start of "/JSON". Stream bodies
    /// (between <c>stream</c> and <c>endstream</c>) are skipped: compressed data is effectively random
    /// and would otherwise contain short names like "/JS" by chance in a few percent of large files.
    /// </summary>
    internal static string? FindForbiddenName(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];

            if (b == (byte)'s' && bytes[i..].StartsWith("stream"u8) && (i == 0 || IsDelimiter(bytes[i - 1])) &&
                i + 6 < bytes.Length && bytes[i + 6] is (byte)'\r' or (byte)'\n')
            {
                var end = bytes[(i + 6)..].IndexOf("endstream"u8);
                if (end < 0)
                {
                    return null;
                }

                i += 6 + end + "endstream".Length;
                continue;
            }

            if (b == (byte)'/')
            {
                var start = i + 1;
                var end = start;
                while (end < bytes.Length && !IsDelimiter(bytes[end]))
                {
                    end++;
                }

                var name = DecodeName(Encoding.ASCII.GetString(bytes[start..end]));
                if (ForbiddenPdfNames.Contains(name, StringComparer.Ordinal))
                {
                    return name;
                }

                i = end;
                continue;
            }

            i++;
        }

        return null;
    }

    /// <summary>
    /// Resolves <c>#xx</c> hex escapes (PDF 1.2+), so <c>/Java#53cript</c> is seen as <c>/JavaScript</c>.
    /// </summary>
    internal static string DecodeName(string raw)
    {
        if (!raw.Contains('#'))
        {
            return raw;
        }

        var decoded = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '#' && i + 2 < raw.Length && Uri.IsHexDigit(raw[i + 1]) && Uri.IsHexDigit(raw[i + 2]))
            {
                decoded.Append((char)Convert.ToInt32(raw.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                decoded.Append(raw[i]);
            }
        }

        return decoded.ToString();
    }

    private static bool IsDelimiter(byte b) =>
        b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f' or 0
            or (byte)'/' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'(' or (byte)')' or (byte)'{' or (byte)'}' or (byte)'%';

    private static string? CatalogProblem(PdfDocument document)
    {
        var catalog = document.Structure.Catalog.CatalogDictionary;

        if (catalog.ContainsKey(NameToken.Create("AA")))
        {
            return "PDFs with document-level actions are not accepted.";
        }

        if (Resolve(document, catalog, "Names") is DictionaryToken names &&
            (names.ContainsKey(NameToken.Create("JavaScript")) || names.ContainsKey(NameToken.Create("EmbeddedFiles"))))
        {
            return "PDFs with embedded scripts or files are not accepted.";
        }

        if (Resolve(document, catalog, "OpenAction") is DictionaryToken openAction &&
            openAction.TryGet(NameToken.S, out var actionType) &&
            actionType is NameToken { Data: "JavaScript" or "Launch" or "ImportData" or "SubmitForm" })
        {
            return "PDFs that run an action on opening are not accepted.";
        }

        if (Resolve(document, catalog, "AcroForm") is DictionaryToken form && form.ContainsKey(NameToken.Create("XFA")))
        {
            return "PDFs with XFA forms are not accepted.";
        }

        return null;
    }

    private static IToken? Resolve(PdfDocument document, DictionaryToken dictionary, string key)
    {
        if (!dictionary.TryGet(NameToken.Create(key), out var token))
        {
            return null;
        }

        return token is IndirectReferenceToken reference ? document.Structure.GetObject(reference.Data).Data : token;
    }

    private DocumentInspection InspectImage(ReadOnlySpan<byte> b, string mediaType)
    {
        var size = mediaType switch
        {
            Png => PngSize(b),
            Jpeg => JpegSize(b),
            _ => TiffSize(b)
        };

        if (size is not var (width, height) || width <= 0 || height <= 0)
        {
            return DocumentInspection.Reject("malformed", "The image header could not be read.");
        }

        if (width > limits.MaxImageSide || height > limits.MaxImageSide || (long)width * height > limits.MaxImagePixels)
        {
            return DocumentInspection.Reject("image_too_large", $"The image is {width}×{height}; images are limited to {limits.MaxImagePixels} pixels.");
        }

        return new DocumentInspection(mediaType, 1, false, null);
    }

    private static (int, int)? PngSize(ReadOnlySpan<byte> b) =>
        b.Length >= 24 && b[12..16].SequenceEqual("IHDR"u8)
            ? (BinaryPrimitives.ReadInt32BigEndian(b[16..20]), BinaryPrimitives.ReadInt32BigEndian(b[20..24]))
            : null;

    private static (int, int)? JpegSize(ReadOnlySpan<byte> b)
    {
        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF)
            {
                return null;
            }

            var marker = b[i + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..(i + 4)]);

            // SOF0–SOF15 carry the frame size, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..(i + 9)]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..(i + 7)]));
            }

            i += 2 + length;
        }

        return null;
    }

    private static (int, int)? TiffSize(ReadOnlySpan<byte> b)
    {
        if (b.Length < 8)
        {
            return null;
        }

        var little = b[0] == (byte)'I';

        var ifd = (int)U32(b, 4, little);
        if (ifd <= 0 || ifd + 2 > b.Length)
        {
            return null;
        }

        int width = 0, height = 0;
        var entries = U16(b, ifd, little);
        for (var e = 0; e < entries; e++)
        {
            var at = ifd + 2 + (e * 12);
            if (at + 12 > b.Length)
            {
                return null;
            }

            var tag = U16(b, at, little);
            var type = U16(b, at + 2, little);
            var value = type == 3 ? U16(b, at + 8, little) : (int)U32(b, at + 8, little);

            if (tag == 256) width = value;
            if (tag == 257) height = value;
        }

        return (width, height);
    }

    private static uint U32(ReadOnlySpan<byte> b, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt32BigEndian(b[at..]);

    private static ushort U16(ReadOnlySpan<byte> b, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt16BigEndian(b[at..]);
}
