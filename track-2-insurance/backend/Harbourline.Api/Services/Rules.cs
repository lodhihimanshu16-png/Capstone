using System.Text.RegularExpressions;

namespace Harbourline.Api.Services;

public static class PolicyPricing
{
    public static decimal NoClaimsDiscount(int noClaimsYears) => Math.Min(noClaimsYears * 0.05m, 0.30m);

    public static decimal RenewalPremium(decimal currentPremium, int noClaimsYears) =>
        Math.Round(currentPremium * (1 - NoClaimsDiscount(noClaimsYears)), 2);
}

public static class ClaimRules
{
    /// <summary>True when a month or more has passed since the last note. <paramref name="now"/> comes from the injected clock.</summary>
    public static bool IsReserveReviewDue(DateTime lastNoteAt, DateTime now) => now.Date >= lastNoteAt.Date.AddMonths(1);

    public static string NextClaimNumber(int sequence) => $"HLC-26-{sequence:D5}";
}

public static class LossRatioCalculator
{
    public static decimal Percent(decimal paid, decimal premium) => premium == 0 ? 0 : Math.Round(paid / premium * 100m, 1);
}

/// <summary>BR-22: finds card numbers (13 to 19 digits that pass the Luhn check) in free text.</summary>
public static class CardNumberDetector
{
    // A run of digits that may be split by single spaces or hyphens.
    private static readonly Regex DigitRun = new(@"\d(?:[ -]?\d)*", RegexOptions.Compiled);

    public static bool ContainsCardNumber(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (Match m in DigitRun.Matches(text))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 13 and <= 19 && PassesLuhn(digits)) return true;
        }
        return false;
    }

    public static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (alternate)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }
}
