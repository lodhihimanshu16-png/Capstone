using Harbourline.Api.Domain;
using Harbourline.Api.Dtos;
using Harbourline.Api.Repositories;

namespace Harbourline.Api.Imports;

public interface IBordereauImportService
{
    Task<ImportResultDto> ImportAsync(Stream csv, string fileName, string source, int userId);
}

/// <summary>
/// Imports a monthly bordereau CSV from a delegated-authority partner. The service decides what happens to each row
/// and saves it; reading the CSV is in <see cref="BordereauCsv"/> and the checks that need no database are in
/// <see cref="BordereauRowValidator"/>. Behaviour is pinned by ImporterCharacterisationTests: do not change it here
/// without changing those tests on purpose.
/// </summary>
public class BordereauImportService : IBordereauImportService
{
    private readonly IPolicyRepository _policies;
    private readonly IPolicyholderRepository _holders;
    private readonly IClaimRepository _claims;
    private readonly IHandlerRepository _handlers;
    private readonly IImportBatchRepository _batches;
    private readonly TimeProvider _clock;

    public BordereauImportService(IPolicyRepository policies, IPolicyholderRepository holders, IClaimRepository claims,
        IHandlerRepository handlers, IImportBatchRepository batches, TimeProvider clock)
    {
        _policies = policies;
        _holders = holders;
        _claims = claims;
        _handlers = handlers;
        _batches = batches;
        _clock = clock;
    }

    /// <summary>Counts and errors for one import, filled in as rows are processed.</summary>
    private sealed class Tally
    {
        public int Total, Created, Updated, Rejected;
        public readonly List<ImportErrorDto> Errors = new();

        public void Reject(int row, string message)
        {
            Rejected++;
            Errors.Add(new ImportErrorDto(row, message));
        }
    }

    public async Task<ImportResultDto> ImportAsync(Stream csv, string fileName, string source, int userId)
    {
        var lines = await BordereauCsv.ReadNonBlankLinesAsync(csv);

        if (lines.Count == 0)
        {
            var empty = new Tally();
            empty.Errors.Add(new ImportErrorDto(0, "File is empty."));
            return await SaveBatchAsync(fileName, source, userId, empty);
        }

        var columns = BordereauColumns.FromHeader(BordereauCsv.SplitHeader(lines[0]));
        if (!columns.HasRequiredColumns)
        {
            // Every data row counts as rejected and the single error is reported against row 1 (the header).
            var bad = new Tally { Total = lines.Count - 1, Rejected = lines.Count - 1 };
            bad.Errors.Add(new ImportErrorDto(1, BordereauColumns.MissingColumnsMessage));
            return await SaveBatchAsync(fileName, source, userId, bad);
        }

        var tally = new Tally();
        var seenRefs = new HashSet<string>();
        for (var i = 1; i < lines.Count; i++)
        {
            tally.Total++;
            var rowNo = i + 1;
            try
            {
                await ProcessRowAsync(lines[i], rowNo, columns, fileName, source, userId, seenRefs, tally);
            }
            catch (Exception ex)
            {
                tally.Reject(rowNo, "Row could not be processed: " + ex.Message);
            }
        }
        return await SaveBatchAsync(fileName, source, userId, tally);
    }

    private async Task ProcessRowAsync(string line, int rowNo, BordereauColumns columns, string fileName, string source, int userId,
        ISet<string> seenRefs, Tally tally)
    {
        var (row, error) = BordereauRowValidator.Validate(BordereauCsv.SplitRow(line), columns, fileName, seenRefs);
        if (row == null)
        {
            tally.Reject(rowNo, error!);
            return;
        }

        var policy = await _policies.GetByNumberAsync(row.PolicyNumber);
        if (policy == null) { tally.Reject(rowNo, $"Policy {row.PolicyNumber} was not found."); return; }
        if (policy.Status == "Cancelled") { tally.Reject(rowNo, $"Policy {row.PolicyNumber} is cancelled."); return; }
        if (row.LossDate < policy.StartDate || row.LossDate > policy.EndDate)
        {
            tally.Reject(rowNo, $"LossDate {row.LossDate:yyyy-MM-dd} is outside the period of policy {row.PolicyNumber}.");
            return;
        }

        // ODD, kept on purpose: this happens before the claim checks below, so the contact email is changed even when
        // the row is then rejected (for example because the claim is Closed).
        await UpdateContactEmailAsync(policy, row.ContactEmail);

        var existing = await _claims.GetByExternalRefAsync(row.ExternalRef);
        if (existing != null)
        {
            var rejection = await UpdateExistingClaimAsync(existing, policy, row, fileName);
            if (rejection != null) tally.Reject(rowNo, rejection);
            else tally.Updated++; // ODD: counted as updated even when nothing changed
            return;
        }

        await CreateClaimAsync(policy, row, source, userId);
        tally.Created++;
    }

    /// <summary>The policyholder's email is replaced by the row's contact email when it looks like an email and differs.</summary>
    private async Task UpdateContactEmailAsync(Policy policy, string email)
    {
        if (email != "" && policy.Policyholder != null
            && !string.Equals(policy.Policyholder.Email, email, StringComparison.OrdinalIgnoreCase)
            && email.Contains('@') && email.Length <= 120)
        {
            var holder = await _holders.GetByIdAsync(policy.PolicyholderId);
            if (holder != null)
            {
                holder.Email = email;
                await _holders.UpdateAsync(holder);
            }
        }
    }

    /// <summary>Returns the reason the row is rejected, or null when the existing claim was (or did not need to be) updated.</summary>
    private async Task<string?> UpdateExistingClaimAsync(Claim existing, Policy policy, BordereauRow row, string fileName)
    {
        // ODD: only Closed and Declined are protected. An Approved or Settled claim can still have its estimate changed.
        if (existing.Status == ClaimStatuses.Closed || existing.Status == ClaimStatuses.Declined)
            return $"Claim {existing.ClaimNumber} is {existing.Status} and cannot be updated by import.";
        if (existing.PolicyId != policy.Id)
            return $"ExternalRef {row.ExternalRef} already belongs to a different policy.";

        var changed = false;
        if (existing.EstimatedAmount != row.Amount)
        {
            existing.EstimatedAmount = row.Amount;
            changed = true;
        }
        // An empty description in the file becomes the default text, which must not overwrite a real description.
        if (existing.Description != row.Description && row.Description != BordereauRowValidator.DefaultDescription(fileName))
        {
            existing.Description = row.Description;
            changed = true;
        }
        if (changed)
        {
            existing.UpdatedAt = Now();
            await _claims.UpdateAsync(existing);
        }
        return null;
    }

    private async Task CreateClaimAsync(Policy policy, BordereauRow row, string source, int userId)
    {
        var handler = await ChooseHandlerAsync(policy.RegionId, row.Amount);
        var sequence = await _claims.NextSequenceAsync();
        var now = Now();
        var claim = new Claim
        {
            ClaimNumber = Services.ClaimRules.NextClaimNumber(sequence),
            PolicyId = policy.Id,
            HandlerId = handler?.Id,
            ClaimType = row.ClaimType,
            Status = ClaimStatuses.New,
            LossDate = row.LossDate,
            ReportedDate = row.ReportedDate,
            Description = row.Description,
            EstimatedAmount = row.Amount,
            Excess = policy.Product?.DefaultExcess ?? 0,
            RegionId = policy.RegionId,
            ExternalRef = row.ExternalRef,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _claims.AddAsync(claim);
        await _claims.AddHistoryAsync(new ClaimStatusHistory
        {
            ClaimId = claim.Id, ToStatus = ClaimStatuses.New, ChangedByUserId = userId, ChangedAt = now,
            Reason = "Imported from " + source
        });
    }

    /// <summary>
    /// The active handler in the claim's region with the fewest open claims, who still has room. Handlers whose approval
    /// limit is below the amount are skipped, but only if someone in the region can cover it. Ties go to the first by
    /// name. Null when everyone is full.
    /// </summary>
    private async Task<Handler?> ChooseHandlerAsync(int regionId, decimal amount)
    {
        Handler? chosen = null;
        var candidates = await _handlers.ListAsync(regionId, true);
        var bestLoad = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var load = await _claims.CountOpenForHandlerAsync(candidate.Id);
            if (load >= candidate.MaxOpenClaims) continue;
            if (candidate.Grade != null && candidate.Grade.ApprovalLimit < amount
                && candidates.Any(c => c.Grade != null && c.Grade.ApprovalLimit >= amount))
                continue;
            if (load < bestLoad)
            {
                bestLoad = load;
                chosen = candidate;
            }
        }
        return chosen;
    }

    private async Task<ImportResultDto> SaveBatchAsync(string fileName, string source, int userId, Tally tally)
    {
        var batch = await _batches.AddAsync(new ImportBatch
        {
            FileName = fileName, Source = source, TotalRows = tally.Total, CreatedRows = tally.Created, UpdatedRows = tally.Updated,
            RejectedRows = tally.Rejected, CreatedAt = Now(), CreatedByUserId = userId
        });
        return new ImportResultDto(batch.Id, tally.Total, tally.Created, tally.Updated, tally.Rejected, tally.Errors);
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
}
