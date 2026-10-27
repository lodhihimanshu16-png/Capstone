using System.Globalization;

namespace Harbourline.Api.Imports;

/// <summary>One row that passed the checks that need no database.</summary>
internal sealed record BordereauRow(string ExternalRef, string PolicyNumber, DateOnly LossDate, DateOnly ReportedDate,
    decimal Amount, string ClaimType, string Description, string ContactEmail);

/// <summary>
/// The checks on a data row that need no database. Pure: no I/O. The ORDER of the checks is part of the behaviour,
/// because the first failing check decides the message, so do not reorder them.
/// </summary>
internal static class BordereauRowValidator
{
    private static readonly string[] DateFormats = { "yyyy-MM-dd", "dd/MM/yyyy", "d MMM yyyy", "dd-MM-yyyy" };

    // ODD, kept on purpose: "the future" is a fixed date, not today. A change here is a behaviour change.
    private static readonly DateOnly LatestLossDate = new(2026, 10, 1);
    private const decimal AmountCeiling = 500000m;

    /// <summary>Returns the row, or the error message that rejects it. <paramref name="seenRefs"/> is updated.</summary>
    public static (BordereauRow? Row, string? Error) Validate(IReadOnlyList<string> cells, BordereauColumns cols, string fileName,
        ISet<string> seenRefs)
    {
        string Cell(int idx) => idx >= 0 && idx < cells.Count ? cells[idx] : "";

        var extRef = Cell(cols.ExternalRef);
        var policyNumber = Cell(cols.PolicyNumber).ToUpperInvariant();
        if (extRef == "") return (null, "ExternalRef is missing.");
        if (policyNumber == "") return (null, "PolicyNumber is missing.");
        // ODD: the reference is recorded as "seen" here, before the row is fully validated, so a later row with the
        // same reference is a "duplicate" even if this row is then rejected.
        if (!seenRefs.Add(extRef)) return (null, "Duplicate ExternalRef in file.");

        var lossText = Cell(cols.LossDate);
        if (!DateOnly.TryParseExact(lossText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lossDate))
            return (null, $"LossDate '{lossText}' is not a recognised date.");

        DateOnly reportedDate;
        var reportedText = Cell(cols.ReportedDate);
        if (reportedText == "") reportedDate = lossDate;
        else if (!DateOnly.TryParseExact(reportedText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out reportedDate))
            return (null, $"ReportedDate '{reportedText}' is not a recognised date.");
        if (reportedDate < lossDate) return (null, "ReportedDate is before LossDate.");
        if (lossDate > LatestLossDate) return (null, "LossDate is in the future.");

        var amountText = Cell(cols.EstimatedAmount).Replace("£", "").Replace(",", "").Replace(" ", "");
        if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return (null, $"EstimatedAmount '{Cell(cols.EstimatedAmount)}' is not a positive amount.");
        if (amount > AmountCeiling) return (null, "EstimatedAmount exceeds the bordereau ceiling.");

        var claimType = Cell(cols.ClaimType);
        if (claimType == "") claimType = "Unspecified";
        if (claimType.Length > 40) claimType = claimType.Substring(0, 40);
        var description = Cell(cols.Description);
        if (description == "") description = DefaultDescription(fileName);
        if (description.Length > 1000) description = description.Substring(0, 1000);

        return (new BordereauRow(extRef, policyNumber, lossDate, reportedDate, amount, claimType, description, Cell(cols.ContactEmail)), null);
    }

    /// <summary>The text used when a row has no description. An existing claim's description is not overwritten with it.</summary>
    public static string DefaultDescription(string fileName) => "Imported from bordereau " + fileName;
}
