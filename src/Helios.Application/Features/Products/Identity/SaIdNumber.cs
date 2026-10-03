namespace Helios.Application.Features.Products.Identity;

/// <summary>Outcome of one structural check on an ID number.</summary>
public enum CheckOutcome
{
    Pass,
    Fail,

    /// <summary>Not evaluated because an earlier check (the format) failed.</summary>
    NotEvaluated
}

public sealed record SaIdNumberChecks(
    CheckOutcome Format,
    CheckOutcome DateOfBirth,
    CheckOutcome Citizenship,
    CheckOutcome Checksum);

public sealed record SaIdNumberResult(
    bool Valid,
    SaIdNumberChecks Checks,
    DateOnly? DateOfBirth,
    string? Citizenship);

/// <summary>
/// Structural validation of a 13-digit South African ID number: YYMMDD SSSS C A Z.
/// <list type="bullet">
///   <item>YYMMDD — a real calendar date not in the future. The century is inferred: the 2000s
///   unless that would put the date in the future, then the 1900s.</item>
///   <item>SSSS — sequence number; it encodes sex, which is deliberately not returned.</item>
///   <item>C — status: 0 citizen, 1 permanent resident, 2 refugee.</item>
///   <item>A — historically a classification digit; carries no validation rule today.</item>
///   <item>Z — Luhn check digit over the preceding twelve digits.</item>
/// </list>
/// A pass means the number is well formed. It does not mean the number was issued, belongs to
/// the person presenting it, or is current: that requires an authorised Home Affairs source.
/// </summary>
public static class SaIdNumber
{
    public const int Length = 13;

    public static SaIdNumberResult Validate(string? input, DateOnly today)
    {
        var digits = Normalise(input);

        if (digits is null)
        {
            return new SaIdNumberResult(
                false,
                new SaIdNumberChecks(CheckOutcome.Fail, CheckOutcome.NotEvaluated, CheckOutcome.NotEvaluated, CheckOutcome.NotEvaluated),
                null,
                null);
        }

        var dateOfBirth = ParseDateOfBirth(digits, today);
        var citizenship = digits[10] switch
        {
            '0' => "citizen",
            '1' => "permanent_resident",
            '2' => "refugee",
            _ => null
        };
        var checksum = PassesLuhn(digits);

        var checks = new SaIdNumberChecks(
            CheckOutcome.Pass,
            dateOfBirth is null ? CheckOutcome.Fail : CheckOutcome.Pass,
            citizenship is null ? CheckOutcome.Fail : CheckOutcome.Pass,
            checksum ? CheckOutcome.Pass : CheckOutcome.Fail);

        var valid = dateOfBirth is not null && citizenship is not null && checksum;

        return new SaIdNumberResult(valid, checks, dateOfBirth, citizenship);
    }

    /// <summary>
    /// Thirteen ASCII digits, allowing spaces as separators (as people often type them). Anything
    /// else — letters, punctuation, wrong length, non-ASCII digits — is a format failure.
    /// </summary>
    internal static string? Normalise(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var compact = input.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (compact.Length != Length || !compact.All(char.IsAsciiDigit))
        {
            return null;
        }

        return compact;
    }

    private static DateOnly? ParseDateOfBirth(string digits, DateOnly today)
    {
        var yy = int.Parse(digits.AsSpan(0, 2));
        var month = int.Parse(digits.AsSpan(2, 2));
        var day = int.Parse(digits.AsSpan(4, 2));

        if (month is < 1 or > 12)
        {
            return null;
        }

        foreach (var century in new[] { 2000, 1900 })
        {
            var year = century + yy;

            if (day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                continue;
            }

            var candidate = new DateOnly(year, month, day);
            if (candidate <= today)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Luhn mod-10 over all thirteen digits; the last digit is the check digit.</summary>
    public static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubleIt = false;

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var value = digits[i] - '0';

            if (doubleIt)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }
}
