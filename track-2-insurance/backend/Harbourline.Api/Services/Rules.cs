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
    // A run of digits that may be split by up to two separator characters (space, hyphen, dot, slash, underscore, tab, line break).
    private static readonly Regex DigitRun = new(@"\d(?:[ \-./_\t\r\n]{0,2}\d)*", RegexOptions.Compiled);

    /// <summary>Throws the same refusal everywhere free text is accepted, naming the field so the user can fix it.</summary>
    public static void RejectIfCardNumber(string? text, string fieldName)
    {
        if (ContainsCardNumber(text))
            throw new Infrastructure.BusinessRuleException($"The {fieldName} looks like it contains a card number (13 to 19 digits that pass the card check). Card numbers are not stored in this system: remove the number and try again.");
    }

    public static bool ContainsCardNumber(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (Match m in DigitRun.Matches(text))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            // Every 13 to 19 digit window, so a number written right after other digits ("amount 250 4111 ...") is still found.
            for (var length = 13; length <= Math.Min(19, digits.Length); length++)
                for (var start = 0; start + length <= digits.Length; start++)
                    if (PassesLuhn(digits.Substring(start, length))) return true;
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
