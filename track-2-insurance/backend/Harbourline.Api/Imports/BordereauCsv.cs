using System.Text;

namespace Harbourline.Api.Imports;

/// <summary>
/// Reading and splitting the bordereau CSV. No database, no business rules.
/// The header and the data rows are split by two different routines on purpose: that is how the importer has always
/// behaved, and the characterisation tests pin it (see docs/refactor-note.md).
/// </summary>
internal static class BordereauCsv
{
    /// <summary>All lines of the file except blank ones (so row numbers in error messages count non-blank lines only).</summary>
    public static async Task<List<string>> ReadNonBlankLinesAsync(Stream csv)
    {
        var lines = new List<string>();
        using var reader = new StreamReader(csv, Encoding.UTF8, true);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.Trim().Length == 0) continue;
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// Header cells, trimmed and lower-cased. A quote character just switches "inside quotes" on and off, so a doubled
    /// quote is dropped and not kept as a literal (unlike <see cref="SplitRow"/>). A leading byte-order mark is removed.
    /// </summary>
    public static List<string> SplitHeader(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in line.TrimStart('﻿'))
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == ',' && !inQuotes) { cells.Add(cell.ToString().Trim().ToLowerInvariant()); cell.Clear(); }
            else cell.Append(ch);
        }
        cells.Add(cell.ToString().Trim().ToLowerInvariant());
        return cells;
    }

    /// <summary>Data row cells, trimmed. Inside quotes a doubled quote is one literal quote.</summary>
    public static List<string> SplitRow(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        for (var k = 0; k < line.Length; k++)
        {
            var ch = line[k];
            if (ch == '"')
            {
                if (inQuotes && k + 1 < line.Length && line[k + 1] == '"') { cell.Append('"'); k++; }
                else inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes) { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(ch);
        }
        cells.Add(cell.ToString().Trim());
        return cells;
    }
}

/// <summary>Where each column is in the file (-1 when it is missing).</summary>
internal sealed record BordereauColumns(int ExternalRef, int PolicyNumber, int LossDate, int ReportedDate, int ClaimType,
    int Description, int EstimatedAmount, int ContactEmail)
{
    public static BordereauColumns FromHeader(IReadOnlyList<string> header)
    {
        int At(string name) => header.ToList().IndexOf(name);
        return new BordereauColumns(At("externalref"), At("policynumber"), At("lossdate"), At("reporteddate"), At("claimtype"),
            At("description"), At("estimatedamount"), At("contactemail"));
    }

    /// <summary>ExternalRef, PolicyNumber, LossDate and EstimatedAmount must be present. The rest are optional.</summary>
    public bool HasRequiredColumns => ExternalRef >= 0 && PolicyNumber >= 0 && LossDate >= 0 && EstimatedAmount >= 0;

    public const string MissingColumnsMessage = "Header must contain ExternalRef, PolicyNumber, LossDate and EstimatedAmount.";
}
