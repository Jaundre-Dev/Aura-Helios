using Helios.Application.Abstractions.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Helios.Infrastructure.Documents;

/// <summary>
/// Reads a PDF's native text layer with PdfPig and groups words into lines by vertical overlap,
/// in top-to-bottom, left-to-right order. Coordinates are normalised to the page with the origin
/// at the top left, so evidence boxes can be drawn over a rendered page directly.
/// </summary>
public sealed class PdfPigTextReader : IPdfTextReader
{
    public PdfText Read(byte[] content)
    {
        using var document = PdfDocument.Open(content);

        var pages = document.GetPages()
            .Select(page => new PdfTextPage(page.Number, page.Width, page.Height, Lines(page)))
            .ToList();

        return new PdfText(pages);
    }

    private static IReadOnlyList<PdfTextLine> Lines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .Select(w => new PdfTextWord(w.Text, Normalise(w.BoundingBox.Left, w.BoundingBox.Top, w.BoundingBox.Right, w.BoundingBox.Bottom, page)))
            .OrderBy(w => w.Box.Top)
            .ToList();

        var lines = new List<List<PdfTextWord>>();
        foreach (var word in words)
        {
            var centre = (word.Box.Top + word.Box.Bottom) / 2;
            var line = lines.FirstOrDefault(l =>
            {
                var top = l.Min(w => w.Box.Top);
                var bottom = l.Max(w => w.Box.Bottom);
                return centre >= top && centre <= bottom;
            });

            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        return lines
            .Select(l => l.OrderBy(w => w.Box.Left).ToList())
            .OrderBy(l => l.Min(w => w.Box.Top))
            .Select(l => new PdfTextLine(
                string.Join(' ', l.Select(w => w.Text)),
                new TextBox(l.Min(w => w.Box.Left), l.Min(w => w.Box.Top), l.Max(w => w.Box.Right), l.Max(w => w.Box.Bottom)),
                l))
            .ToList();
    }

    private static TextBox Normalise(double left, double top, double right, double bottom, Page page) =>
        new(left / page.Width, 1 - (top / page.Height), right / page.Width, 1 - (bottom / page.Height));
}
