using System.Globalization;
using System.Text;

namespace Helios.Application.Common;

/// <summary>
/// RFC 4180 CSV with a UTF-8 byte-order mark (so spreadsheets read accents correctly). Text cells a
/// spreadsheet would run as a formula (<c>= + - @</c>, tab, CR) get a leading apostrophe; plain numbers,
/// including negative amounts, are left as numbers.
/// </summary>
public static class Csv
{
    public static byte[] Write(IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var csv = new StringBuilder();
        csv.AppendJoin(',', header.Select(Cell)).Append("\r\n");

        foreach (var row in rows)
        {
            csv.AppendJoin(',', row.Select(Cell)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' &&
            !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    public static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
