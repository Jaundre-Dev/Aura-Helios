using System.Globalization;
using System.Text;

namespace Helios.Application.Common;

/// <summary>
/// Slugs appear in URLs and carry a uniqueness constraint per parent, so generation has
/// to be deterministic and stable rather than merely pretty.
/// </summary>
public static class Slug
{
    public static string From(string value, int maxLength = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalised = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalised.Length);
        var lastWasHyphen = false;

        foreach (var ch in normalised)
        {
            // Strip accents rather than transliterate: "Café" becomes "cafe", not "cafa".
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > maxLength)
        {
            slug = slug[..maxLength].TrimEnd('-');
        }

        return slug.Length == 0
            ? throw new ArgumentException($"'{value}' contains no characters usable in a slug.", nameof(value))
            : slug;
    }
}
