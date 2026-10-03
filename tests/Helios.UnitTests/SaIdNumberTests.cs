using Helios.Application.Features.Products.Identity;

namespace Helios.UnitTests;

/// <summary>
/// Structural validation of SA ID numbers. 8001015009087 is a widely published sample whose
/// Luhn digit was checked by hand: from the right, 7 + (8·2→7) + 0 + (9·2→9) + 0 + 0 + 5 + (1·2)
/// + 0 + (1·2) + 0 + 0 + 8 = 40, divisible by 10.
/// </summary>
public class SaIdNumberTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    [Fact]
    public void A_well_formed_number_passes_every_check()
    {
        var result = SaIdNumber.Validate("8001015009087", Today);

        Assert.True(result.Valid);
        Assert.Equal(new SaIdNumberChecks(CheckOutcome.Pass, CheckOutcome.Pass, CheckOutcome.Pass, CheckOutcome.Pass), result.Checks);
        Assert.Equal(new DateOnly(1980, 1, 1), result.DateOfBirth);
        Assert.Equal("citizen", result.Citizenship);
    }

    [Fact]
    public void Spaces_used_as_separators_are_ignored()
    {
        Assert.True(SaIdNumber.Validate("800101 5009 087", Today).Valid);
    }

    [Fact]
    public void A_wrong_check_digit_fails_only_the_checksum()
    {
        var result = SaIdNumber.Validate("8001015009088", Today);

        Assert.False(result.Valid);
        Assert.Equal(CheckOutcome.Pass, result.Checks.DateOfBirth);
        Assert.Equal(CheckOutcome.Fail, result.Checks.Checksum);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("800101500908")]     // 12 digits
    [InlineData("80010150090877")]   // 14 digits
    [InlineData("80010150090A7")]
    [InlineData("8001-015009087")]
    [InlineData("８００１０１５００９０８７")] // full-width digits are not ASCII digits
    public void Malformed_input_fails_the_format_check_and_nothing_else_is_evaluated(string? input)
    {
        var result = SaIdNumber.Validate(input, Today);

        Assert.False(result.Valid);
        Assert.Equal(CheckOutcome.Fail, result.Checks.Format);
        Assert.Equal(CheckOutcome.NotEvaluated, result.Checks.Checksum);
        Assert.Null(result.DateOfBirth);
    }

    [Theory]
    [InlineData("8013015009087")] // month 13
    [InlineData("8002305009087")] // 30 February
    [InlineData("8000015009087")] // month 0
    public void An_impossible_date_fails_the_date_check(string input)
    {
        var result = SaIdNumber.Validate(input, Today);

        Assert.False(result.Valid);
        Assert.Equal(CheckOutcome.Fail, result.Checks.DateOfBirth);
        Assert.Null(result.DateOfBirth);
    }

    [Fact]
    public void Leap_day_is_accepted_only_in_a_leap_year()
    {
        // 2000 is a leap year; 2001 and 1901 are not.
        Assert.Equal(new DateOnly(2000, 2, 29), SaIdNumber.Validate("0002290000000", Today).DateOfBirth);
        Assert.Null(SaIdNumber.Validate("0102290000000", Today).DateOfBirth);
    }

    [Fact]
    public void The_century_is_the_2000s_unless_that_date_is_in_the_future()
    {
        Assert.Equal(2026, SaIdNumber.Validate("2610030000000", Today).DateOfBirth!.Value.Year);
        Assert.Equal(1926, SaIdNumber.Validate("2610040000000", Today).DateOfBirth!.Value.Year);
    }

    [Theory]
    [InlineData('0', "citizen")]
    [InlineData('1', "permanent_resident")]
    [InlineData('2', "refugee")]
    public void Known_status_digits_are_named(char digit, string expected)
    {
        var number = $"8001015009{digit}87";
        Assert.Equal(expected, SaIdNumber.Validate(number, Today).Citizenship);
    }

    [Fact]
    public void An_unknown_status_digit_fails_the_citizenship_check()
    {
        var result = SaIdNumber.Validate("8001015009387", Today);

        Assert.Equal(CheckOutcome.Fail, result.Checks.Citizenship);
        Assert.False(result.Valid);
    }

    [Theory]
    [InlineData("79927398713", true)]   // canonical Luhn example
    [InlineData("79927398710", false)]
    public void Luhn_matches_the_reference_examples(string digits, bool expected)
    {
        Assert.Equal(expected, SaIdNumber.PassesLuhn(digits));
    }
}
