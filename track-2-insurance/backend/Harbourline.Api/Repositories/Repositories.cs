using Harbourline.Api.Data;
using Harbourline.Api.Domain;
using Harbourline.Api.Infrastructure;
using Harbourline.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Harbourline.Api.Repositories;

public interface IRegionRepository
{
    Task<List<Region>> ListAsync();
    Task<Region?> GetByCodeAsync(string code);
}

public class RegionRepository : IRegionRepository
{
    private readonly HarbourlineDbContext _db;
    public RegionRepository(HarbourlineDbContext db) => _db = db;
    public Task<List<Region>> ListAsync() => _db.Regions.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    public Task<Region?> GetByCodeAsync(string code) => _db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code);
}

public interface IProductRepository
{
    Task<List<Product>> ListAsync(string? line, bool? active);
    Task<Product?> GetByIdAsync(int id);
    Task<Product?> GetByCodeAsync(string code);
    Task<Product> AddAsync(Product product);
    Task UpdateAsync(Product product);
}

public class ProductRepository : IProductRepository
{
    private readonly HarbourlineDbContext _db;
    public ProductRepository(HarbourlineDbContext db) => _db = db;

    public Task<List<Product>> ListAsync(string? line, bool? active)
    {
        var q = _db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(line)) q = q.Where(p => p.Line == line);
        if (active.HasValue) q = q.Where(p => p.Active == active.Value);
        return q.OrderBy(p => p.Code).ToListAsync();
    }

    public Task<Product?> GetByIdAsync(int id) => _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    public Task<Product?> GetByCodeAsync(string code) => _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code);

    public async Task<Product> AddAsync(Product product)
    {
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return product;
    }

    public async Task UpdateAsync(Product product)
    {
        _db.Products.Update(product);
        await _db.SaveChangesAsync();
    }
}

public interface IUserRepository
{
    Task<List<AppUser>> ListAsync();
    Task<AppUser?> GetByIdAsync(int id);
    Task<AppUser?> GetByEmailAsync(string email);
    Task UpdateAsync(AppUser user);
}

public class UserRepository : IUserRepository
{
    private readonly HarbourlineDbContext _db;
    public UserRepository(HarbourlineDbContext db) => _db = db;
    public Task<List<AppUser>> ListAsync() => _db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync();
    public Task<AppUser?> GetByIdAsync(int id) => _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
    public Task<AppUser?> GetByEmailAsync(string email) => _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);

    public async Task UpdateAsync(AppUser user)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync();
    }
}

public interface IHandlerRepository
{
    Task<List<Handler>> ListAsync(int? regionId, bool? active);
    Task<Handler?> GetByIdAsync(int id);
    Task<Handler?> GetByUserIdAsync(int userId);
    Task<List<HandlerGrade>> ListGradesAsync();
    Task<Handler> AddAsync(Handler handler);
    Task UpdateAsync(Handler handler);
}

public class HandlerRepository : IHandlerRepository
{
    private readonly HarbourlineDbContext _db;
    public HandlerRepository(HarbourlineDbContext db) => _db = db;

    public Task<List<Handler>> ListAsync(int? regionId, bool? active)
    {
        var q = _db.Handlers.AsNoTracking().Include(h => h.Grade).Include(h => h.Region).AsQueryable();
        if (regionId.HasValue) q = q.Where(h => h.RegionId == regionId.Value);
        if (active.HasValue) q = q.Where(h => h.Active == active.Value);
        return q.OrderBy(h => h.Name).ToListAsync();
    }

    public Task<Handler?> GetByIdAsync(int id) =>
        _db.Handlers.AsNoTracking().Include(h => h.Grade).Include(h => h.Region).FirstOrDefaultAsync(h => h.Id == id);

    public Task<Handler?> GetByUserIdAsync(int userId) =>
        _db.Handlers.AsNoTracking().Include(h => h.Grade).Include(h => h.Region).FirstOrDefaultAsync(h => h.UserId == userId);

    public Task<List<HandlerGrade>> ListGradesAsync() => _db.HandlerGrades.AsNoTracking().OrderBy(g => g.ApprovalLimit).ToListAsync();

    public async Task<Handler> AddAsync(Handler handler)
    {
        _db.Handlers.Add(handler);
        await _db.SaveChangesAsync();
        return handler;
    }

    public async Task UpdateAsync(Handler handler)
    {
        _db.Entry(handler).State = EntityState.Modified;
        await _db.SaveChangesAsync();
    }
}

public interface IPolicyholderRepository
{
    Task<(List<Policyholder> Items, int Total)> ListAsync(string? search, int? regionId, int page, int pageSize);
    Task<Policyholder?> GetByIdAsync(int id);
    Task<Policyholder> AddAsync(Policyholder holder);
    Task UpdateAsync(Policyholder holder);
}

public class PolicyholderRepository : IPolicyholderRepository
{
    private readonly HarbourlineDbContext _db;
    public PolicyholderRepository(HarbourlineDbContext db) => _db = db;

    public async Task<(List<Policyholder> Items, int Total)> ListAsync(string? search, int? regionId, int page, int pageSize)
    {
        var q = _db.Policyholders.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p => p.FullName.Contains(search) || p.Email.Contains(search) || p.Postcode.Contains(search));
        if (regionId.HasValue) q = q.Where(p => p.RegionId == regionId.Value);
        var total = await q.CountAsync();
        var items = await q.OrderBy(p => p.FullName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<Policyholder?> GetByIdAsync(int id) => _db.Policyholders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Policyholder> AddAsync(Policyholder holder)
    {
        _db.Policyholders.Add(holder);
        await _db.SaveChangesAsync();
        return holder;
    }

    public async Task UpdateAsync(Policyholder holder)
    {
        _db.Entry(holder).State = EntityState.Modified;
        await _db.SaveChangesAsync();
    }
}

public interface IPolicyRepository
{
    Task<(List<Policy> Items, int Total)> ListAsync(string? status, int? productId, int? regionId, int page, int pageSize);
    Task<List<Policy>> ListForHolderAsync(int holderId);
    Task<Policy?> GetByIdAsync(int id);
    Task<Policy?> GetByNumberAsync(string policyNumber);
    Task<int> CountAsync();
    Task<Policy> AddAsync(Policy policy);
    Task UpdateAsync(Policy policy);
}

public class PolicyRepository : IPolicyRepository
{
    private readonly HarbourlineDbContext _db;
    public PolicyRepository(HarbourlineDbContext db) => _db = db;

    public async Task<(List<Policy> Items, int Total)> ListAsync(string? status, int? productId, int? regionId, int page, int pageSize)
    {
        var q = _db.Policies.AsNoTracking().Include(p => p.Policyholder).Include(p => p.Product).AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(p => p.Status == status);
        if (productId.HasValue) q = q.Where(p => p.ProductId == productId.Value);
        if (regionId.HasValue) q = q.Where(p => p.RegionId == regionId.Value);
        var total = await q.CountAsync();
        var items = await q.OrderBy(p => p.PolicyNumber).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<List<Policy>> ListForHolderAsync(int holderId) =>
        _db.Policies.AsNoTracking().Include(p => p.Policyholder).Include(p => p.Product)
            .Where(p => p.PolicyholderId == holderId).OrderBy(p => p.StartDate).ToListAsync();

    public Task<Policy?> GetByIdAsync(int id) =>
        _db.Policies.AsNoTracking().Include(p => p.Policyholder).Include(p => p.Product).FirstOrDefaultAsync(p => p.Id == id);

    public Task<Policy?> GetByNumberAsync(string policyNumber) =>
        _db.Policies.AsNoTracking().Include(p => p.Policyholder).Include(p => p.Product).FirstOrDefaultAsync(p => p.PolicyNumber == policyNumber);

    public Task<int> CountAsync() => _db.Policies.CountAsync();

    public async Task<Policy> AddAsync(Policy policy)
    {
        _db.Policies.Add(policy);
        await _db.SaveChangesAsync();
        return policy;
    }

    public async Task UpdateAsync(Policy policy)
    {
        _db.Entry(policy).State = EntityState.Modified;
        await _db.SaveChangesAsync();
    }
}

public class ClaimFilter
{
    public string? Status { get; set; }
    public int? HandlerId { get; set; }
    public int? RegionId { get; set; }
    public int? PolicyId { get; set; }
    public int? PolicyholderId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>One row of the claim list, already joined (policy, holder, product, handler, note count).</summary>
public record ClaimListRow(int Id, string ClaimNumber, string PolicyNumber, string PolicyholderName, string ProductName, string? HandlerName,
    string Status, string ClaimType, DateOnly LossDate, decimal EstimatedAmount, int NoteCount, int ReopenedCount);

/// <summary>One row of the handler queue. <c>EnteredAt</c> is when the claim entered its current status.</summary>
public record QueueRow(int ClaimId, string ClaimNumber, string Status, string ProductName, string PolicyholderName, string? HandlerName,
    decimal EstimatedAmount, DateOnly ReportedDate, DateTime EnteredAt);

public interface IClaimRepository
{
    /// <summary>One joined query for a page of the list (no per-row calls).</summary>
    Task<List<ClaimListRow>> ListItemsAsync(ClaimFilter filter);
    Task<int> CountAsync(ClaimFilter filter);
    Task<Claim?> GetByIdAsync(int id);
    Task<int> CountOpenForHandlerAsync(int handlerId);
    /// <summary>Claims in New, Triaged, Assessing, Approved or Reopened owned by the handler, ignoring <paramref name="excludeClaimId"/>.</summary>
    Task<int> CountActiveForHandlerAsync(int handlerId, int? excludeClaimId);
    Task<int> NextSequenceAsync();
    Task<Claim?> GetByExternalRefAsync(string externalRef);
    Task<Claim> AddAsync(Claim claim);
    /// <summary>Saves the claim with the optimistic version check. Throws ConflictException when someone else saved first.</summary>
    Task UpdateAsync(Claim claim);
    Task AddHistoryAsync(ClaimStatusHistory entry);
    /// <summary>Saves the claim change and its history row in ONE SaveChanges (atomic, BR-14), with the version check (BR-15).</summary>
    Task SaveStatusChangeAsync(Claim claim, ClaimStatusHistory history);
    /// <summary>Saves a refused approval in its own save (BR-17).</summary>
    Task AddApprovalAttemptAsync(ClaimApprovalAttempt attempt);
    Task<List<ClaimStatusHistory>> ListHistoryAsync(int claimId);
    /// <summary>When the claim last moved into <paramref name="status"/>, or null when no history row says so.</summary>
    Task<DateTime?> GetStageEnteredAtAsync(int claimId, string status);
    /// <summary>The handler queue (BR-19): one page query plus one count query, whatever the page size.</summary>
    Task<(List<QueueRow> Items, int Total)> QueueAsync(int? handlerId, int? regionId, int page, int pageSize, DateTime todayStart);
}

public class ClaimRepository : IClaimRepository
{
    private readonly HarbourlineDbContext _db;
    public ClaimRepository(HarbourlineDbContext db) => _db = db;

    private IQueryable<Claim> Filtered(ClaimFilter f)
    {
        var q = _db.Claims.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(f.Status)) q = q.Where(c => c.Status == f.Status);
        if (f.HandlerId.HasValue) q = q.Where(c => c.HandlerId == f.HandlerId.Value);
        if (f.RegionId.HasValue) q = q.Where(c => c.RegionId == f.RegionId.Value);
        if (f.PolicyId.HasValue) q = q.Where(c => c.PolicyId == f.PolicyId.Value);
        if (f.PolicyholderId.HasValue)
            q = q.Where(c => _db.Policies.Any(p => p.Id == c.PolicyId && p.PolicyholderId == f.PolicyholderId.Value));
        return q;
    }

    public Task<List<ClaimListRow>> ListItemsAsync(ClaimFilter f) =>
        (from c in Filtered(f)
         join p in _db.Policies.AsNoTracking() on c.PolicyId equals p.Id
         join ph in _db.Policyholders.AsNoTracking() on p.PolicyholderId equals ph.Id
         join pr in _db.Products.AsNoTracking() on p.ProductId equals pr.Id
         join h in _db.Handlers.AsNoTracking() on c.HandlerId equals (int?)h.Id into handlers
         from h in handlers.DefaultIfEmpty()
         orderby c.ReportedDate descending, c.Id descending
         select new ClaimListRow(c.Id, c.ClaimNumber, p.PolicyNumber, ph.FullName, pr.Name, h == null ? null : h.Name, c.Status, c.ClaimType,
             c.LossDate, c.EstimatedAmount, _db.ClaimNotes.Count(n => n.ClaimId == c.Id), c.ReopenedCount))
        .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();

    public Task<int> CountAsync(ClaimFilter f) => Filtered(f).CountAsync();

    public Task<Claim?> GetByIdAsync(int id) => _db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

    public Task<int> CountOpenForHandlerAsync(int handlerId) =>
        _db.Claims.CountAsync(c => c.HandlerId == handlerId && ClaimStatuses.Open.Contains(c.Status));

    public Task<int> CountActiveForHandlerAsync(int handlerId, int? excludeClaimId) =>
        _db.Claims.CountAsync(c => c.HandlerId == handlerId && ClaimStatuses.Active.Contains(c.Status) && (excludeClaimId == null || c.Id != excludeClaimId));

    public async Task<int> NextSequenceAsync() => (await _db.Claims.MaxAsync(c => (int?)c.Id) ?? 0) + 1;

    public Task<Claim?> GetByExternalRefAsync(string externalRef) =>
        _db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.ExternalRef == externalRef);

    public async Task<Claim> AddAsync(Claim claim)
    {
        _db.Claims.Add(claim);
        await _db.SaveChangesAsync();
        return claim;
    }

    public Task UpdateAsync(Claim claim) => SaveClaimAsync(claim, null);

    public async Task AddHistoryAsync(ClaimStatusHistory entry)
    {
        _db.ClaimStatusHistory.Add(entry);
        await _db.SaveChangesAsync();
    }

    public Task SaveStatusChangeAsync(Claim claim, ClaimStatusHistory history)
    {
        history.ClaimId = claim.Id;
        return SaveClaimAsync(claim, history);
    }

    public async Task AddApprovalAttemptAsync(ClaimApprovalAttempt attempt)
    {
        _db.ClaimApprovalAttempts.Add(attempt);
        await _db.SaveChangesAsync();
    }

    // The claim was loaded without tracking, so it carries the version that was read. The UPDATE is
    // "WHERE id = @id AND row_version = <that version>" and sets row_version + 1; no row matched means
    // somebody else saved first (BR-15). The history row, if any, goes in the same SaveChanges.
    private async Task SaveClaimAsync(Claim claim, ClaimStatusHistory? history)
    {
        var entry = _db.Entry(claim);
        entry.State = EntityState.Modified;
        var readVersion = claim.RowVersion;
        entry.Property(c => c.RowVersion).OriginalValue = readVersion;
        claim.RowVersion = readVersion + 1;
        if (history != null) _db.ClaimStatusHistory.Add(history);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            claim.RowVersion = readVersion;
            _db.ChangeTracker.Clear();
            throw new ConflictException(ApiExceptionHandler.ConcurrencyMessage);
        }
        catch (Exception ex) when (IsLockConflict(ex))
        {
            // Two people saving the same claim at the same instant can make MySQL abort one transaction
            // ("Deadlock found"). It rolls back completely, so nothing was written: report it like any other
            // lost race (409) and let the caller reload.
            claim.RowVersion = readVersion;
            _db.ChangeTracker.Clear();
            throw new ConflictException(ApiExceptionHandler.ConcurrencyMessage);
        }
        catch
        {
            claim.RowVersion = readVersion;
            _db.ChangeTracker.Clear();
            throw;
        }
        // Reads are no-tracking; do not leave this instance attached to block a later save of the same claim.
        entry.State = EntityState.Detached;
        if (history != null) _db.Entry(history).State = EntityState.Detached;
    }

    private static bool IsLockConflict(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException!)
        {
            if (e is MySqlConnector.MySqlException m
                && (m.ErrorCode == MySqlConnector.MySqlErrorCode.LockDeadlock || m.ErrorCode == MySqlConnector.MySqlErrorCode.LockWaitTimeout))
                return true;
            if (e.InnerException == null) break;
        }
        return false;
    }

    public Task<List<ClaimStatusHistory>> ListHistoryAsync(int claimId) =>
        _db.ClaimStatusHistory.AsNoTracking().Where(h => h.ClaimId == claimId).OrderBy(h => h.ChangedAt).ThenBy(h => h.Id).ToListAsync();

    public Task<DateTime?> GetStageEnteredAtAsync(int claimId, string status) =>
        _db.ClaimStatusHistory.Where(h => h.ClaimId == claimId && h.ToStatus == status).MaxAsync(h => (DateTime?)h.ChangedAt);

    public async Task<(List<QueueRow> Items, int Total)> QueueAsync(int? handlerId, int? regionId, int page, int pageSize, DateTime todayStart)
    {
        var nonFinal = ClaimStatuses.NonFinal;
        var tNew = ClaimWorkflow.TargetDays(ClaimStatuses.New)!.Value;
        var tTriaged = ClaimWorkflow.TargetDays(ClaimStatuses.Triaged)!.Value;
        var tAssessing = ClaimWorkflow.TargetDays(ClaimStatuses.Assessing)!.Value;
        var tApproved = ClaimWorkflow.TargetDays(ClaimStatuses.Approved)!.Value;
        var tSettled = ClaimWorkflow.TargetDays(ClaimStatuses.Settled)!.Value;
        var tReopened = ClaimWorkflow.TargetDays(ClaimStatuses.Reopened)!.Value;

        var baseQuery = _db.Claims.AsNoTracking().Where(c => nonFinal.Contains(c.Status));
        if (handlerId.HasValue) baseQuery = baseQuery.Where(c => c.HandlerId == handlerId.Value);
        if (regionId.HasValue) baseQuery = baseQuery.Where(c => c.RegionId == regionId.Value);

        var total = await baseQuery.CountAsync();

        // "due" is the date the stage target runs out. A claim is overdue when due < today, and among overdue
        // claims the earliest due date is the one most days over target. Non-overdue claims sort by entry time.
        var query =
            from c in baseQuery
            join p in _db.Policies.AsNoTracking() on c.PolicyId equals p.Id
            join ph in _db.Policyholders.AsNoTracking() on p.PolicyholderId equals ph.Id
            join pr in _db.Products.AsNoTracking() on p.ProductId equals pr.Id
            join h in _db.Handlers.AsNoTracking() on c.HandlerId equals (int?)h.Id into handlers
            from h in handlers.DefaultIfEmpty()
            let entered = _db.ClaimStatusHistory.Where(x => x.ClaimId == c.Id && x.ToStatus == c.Status).Max(x => (DateTime?)x.ChangedAt) ?? c.CreatedAt
            let due = c.Status == ClaimStatuses.New ? entered.Date.AddDays(tNew)
                : c.Status == ClaimStatuses.Triaged ? entered.Date.AddDays(tTriaged)
                : c.Status == ClaimStatuses.Assessing ? entered.Date.AddDays(tAssessing)
                : c.Status == ClaimStatuses.Approved ? entered.Date.AddDays(tApproved)
                : c.Status == ClaimStatuses.Settled ? entered.Date.AddDays(tSettled)
                : entered.Date.AddDays(tReopened)
            orderby (due < todayStart ? 0 : 1), (due < todayStart ? due : entered), entered, c.Id
            select new QueueRow(c.Id, c.ClaimNumber, c.Status, pr.Name, ph.FullName, h == null ? null : h.Name, c.EstimatedAmount, c.ReportedDate, entered);

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }
}

public interface IClaimNoteRepository
{
    Task<List<ClaimNote>> ListForClaimAsync(int claimId);
    Task<int> CountForClaimAsync(int claimId);
    Task<DateTime?> LastNoteAtAsync(int claimId);
    Task<ClaimNote?> GetByIdAsync(int id);
    Task<ClaimNote> AddAsync(ClaimNote note);
    Task DeleteAsync(ClaimNote note);
}

public class ClaimNoteRepository : IClaimNoteRepository
{
    private readonly HarbourlineDbContext _db;
    public ClaimNoteRepository(HarbourlineDbContext db) => _db = db;

    public Task<List<ClaimNote>> ListForClaimAsync(int claimId) =>
        _db.ClaimNotes.AsNoTracking().Where(n => n.ClaimId == claimId).OrderByDescending(n => n.CreatedAt).ToListAsync();

    public Task<int> CountForClaimAsync(int claimId) => _db.ClaimNotes.CountAsync(n => n.ClaimId == claimId);

    public async Task<DateTime?> LastNoteAtAsync(int claimId) =>
        await _db.ClaimNotes.Where(n => n.ClaimId == claimId).MaxAsync(n => (DateTime?)n.CreatedAt);

    public Task<ClaimNote?> GetByIdAsync(int id) => _db.ClaimNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id);

    public async Task<ClaimNote> AddAsync(ClaimNote note)
    {
        _db.ClaimNotes.Add(note);
        await _db.SaveChangesAsync();
        return note;
    }

    public async Task DeleteAsync(ClaimNote note)
    {
        _db.ClaimNotes.Remove(note);
        await _db.SaveChangesAsync();
    }
}

public interface IImportBatchRepository
{
    Task<List<ImportBatch>> ListAsync();
    Task<ImportBatch?> GetByIdAsync(int id);
    Task<ImportBatch> AddAsync(ImportBatch batch);
}

public class ImportBatchRepository : IImportBatchRepository
{
    private readonly HarbourlineDbContext _db;
    public ImportBatchRepository(HarbourlineDbContext db) => _db = db;
    public Task<List<ImportBatch>> ListAsync() => _db.ImportBatches.AsNoTracking().OrderByDescending(b => b.CreatedAt).ToListAsync();
    public Task<ImportBatch?> GetByIdAsync(int id) => _db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);

    public async Task<ImportBatch> AddAsync(ImportBatch batch)
    {
        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync();
        return batch;
    }
}

public interface IReportRepository
{
    Task<List<LossRatioProductRow>> LossRatioAsync(DateTime periodStart, DateTime toExclusive);
    Task<List<AgeingProjection>> AgeingAsync();
    Task<List<WorkloadProjection>> WorkloadAsync();
    Task<List<SummaryProjection>> SummaryAsync();
    /// <summary>Blocked attempts plus legacy approvals above the approver's grade limit: two queries, each capped at 500 rows, newest first.</summary>
    Task<List<LimitBreachRow>> LimitBreachesAsync(DateTime? fromUtc, DateTime? toExclusiveUtc, int? regionId);
}

public record LimitBreachRow(int ClaimId, string ClaimNumber, string Kind, decimal Amount, decimal? Limit, int UserId, string UserName,
    string? GradeCode, string RegionCode, DateTime OccurredAt, string? Reason);

public record LossRatioProductRow(string ProductCode, string ProductName, decimal WrittenPremium, decimal PaidClaims, int ClaimCount);
public record AgeingProjection(string Status, int ClaimId, DateTime? EnteredStatusAt);
public record WorkloadProjection(int HandlerId, string HandlerName, string GradeCode, int MaxOpenClaims, int OpenClaims, decimal OpenEstimatedValue);
public record SummaryProjection(string RegionCode, string Status, int Claims, decimal EstimatedTotal);

public class ReportRepository : IReportRepository
{
    private readonly HarbourlineDbContext _db;
    public ReportRepository(HarbourlineDbContext db) => _db = db;

    public async Task<List<LossRatioProductRow>> LossRatioAsync(DateTime periodStart, DateTime toExclusive)
    {
        var fromDate = DateOnly.FromDateTime(periodStart);
        var toDate = DateOnly.FromDateTime(toExclusive);

        var premiums = await _db.Policies.AsNoTracking()
            .Where(p => p.StartDate >= fromDate && p.StartDate < toDate && p.Status != "Cancelled")
            .GroupBy(p => p.ProductId)
            .Select(g => new { ProductId = g.Key, Premium = g.Sum(p => p.AnnualPremium) })
            .ToListAsync();

        var settlements = await (
            from h in _db.ClaimStatusHistory.AsNoTracking()
            join c in _db.Claims.AsNoTracking() on h.ClaimId equals c.Id
            join p in _db.Policies.AsNoTracking() on c.PolicyId equals p.Id
            where h.ToStatus == ClaimStatuses.Settled && h.ChangedAt >= periodStart && h.ChangedAt < toExclusive
            select new { p.ProductId, Paid = c.PaidAmount ?? 0m }
        ).ToListAsync();

        var paidByProduct = settlements.GroupBy(s => s.ProductId)
            .ToDictionary(g => g.Key, g => (Paid: g.Sum(x => x.Paid), Count: g.Count()));

        var products = await _db.Products.AsNoTracking().OrderBy(p => p.Code).ToListAsync();
        return products.Select(p =>
        {
            var premium = premiums.FirstOrDefault(x => x.ProductId == p.Id)?.Premium ?? 0m;
            paidByProduct.TryGetValue(p.Id, out var paid);
            return new LossRatioProductRow(p.Code, p.Name, premium, paid.Paid, paid.Count);
        }).ToList();
    }

    public async Task<List<AgeingProjection>> AgeingAsync()
    {
        var open = ClaimStatuses.Open;
        return await _db.Claims.AsNoTracking()
            .Where(c => open.Contains(c.Status))
            .Select(c => new AgeingProjection(c.Status, c.Id,
                _db.ClaimStatusHistory.Where(h => h.ClaimId == c.Id && h.ToStatus == c.Status).Max(h => (DateTime?)h.ChangedAt)))
            .ToListAsync();
    }

    public async Task<List<WorkloadProjection>> WorkloadAsync()
    {
        var open = ClaimStatuses.Open;
        return await _db.Handlers.AsNoTracking().Where(h => h.Active).OrderBy(h => h.Name)
            .Select(h => new WorkloadProjection(h.Id, h.Name, h.Grade!.Code, h.MaxOpenClaims,
                _db.Claims.Count(c => c.HandlerId == h.Id && open.Contains(c.Status)),
                _db.Claims.Where(c => c.HandlerId == h.Id && open.Contains(c.Status)).Sum(c => (decimal?)c.EstimatedAmount) ?? 0m))
            .ToListAsync();
    }

    public async Task<List<LimitBreachRow>> LimitBreachesAsync(DateTime? fromUtc, DateTime? toExclusiveUtc, int? regionId)
    {
        var blocked = await (
            from a in _db.ClaimApprovalAttempts.AsNoTracking()
            join c in _db.Claims.AsNoTracking() on a.ClaimId equals c.Id
            join u in _db.Users.AsNoTracking() on a.UserId equals u.Id
            join r in _db.Regions.AsNoTracking() on c.RegionId equals r.Id
            where (fromUtc == null || a.AttemptedAt >= fromUtc) && (toExclusiveUtc == null || a.AttemptedAt < toExclusiveUtc)
                && (regionId == null || c.RegionId == regionId)
            orderby a.AttemptedAt descending, a.Id descending
            select new LimitBreachRow(c.Id, c.ClaimNumber, "BlockedAttempt", a.Amount, a.ApprovalLimit, u.Id, u.DisplayName, a.GradeCode, r.Code,
                a.AttemptedAt, a.Reason)
        ).Take(500).ToListAsync();

        // Old data: a history row that moved a claim to Approved, by a handler whose grade limit is below the amount.
        // Old rows have no amount, so the claim's current approved amount stands in for it (A-17).
        var legacy = await (
            from h in _db.ClaimStatusHistory.AsNoTracking()
            join c in _db.Claims.AsNoTracking() on h.ClaimId equals c.Id
            join u in _db.Users.AsNoTracking() on h.ChangedByUserId equals u.Id
            join hd in _db.Handlers.AsNoTracking() on u.Id equals hd.UserId
            join g in _db.HandlerGrades.AsNoTracking() on hd.GradeId equals g.Id
            join r in _db.Regions.AsNoTracking() on c.RegionId equals r.Id
            where h.ToStatus == ClaimStatuses.Approved
                && (h.Amount ?? c.ApprovedAmount ?? 0m) > g.ApprovalLimit
                && (fromUtc == null || h.ChangedAt >= fromUtc) && (toExclusiveUtc == null || h.ChangedAt < toExclusiveUtc)
                && (regionId == null || c.RegionId == regionId)
            orderby h.ChangedAt descending, h.Id descending
            select new LimitBreachRow(c.Id, c.ClaimNumber, "ApprovedAboveLimit", h.Amount ?? c.ApprovedAmount ?? 0m, g.ApprovalLimit, u.Id,
                u.DisplayName, g.Code, r.Code, h.ChangedAt, h.Reason)
        ).Take(500).ToListAsync();

        return blocked.Concat(legacy).ToList();
    }

    public async Task<List<SummaryProjection>> SummaryAsync()
    {
        var rows = await _db.Claims.AsNoTracking()
            .GroupBy(c => new { c.RegionId, c.Status })
            .Select(g => new { g.Key.RegionId, g.Key.Status, Claims = g.Count(), Total = g.Sum(c => c.EstimatedAmount) })
            .ToListAsync();
        var regions = await _db.Regions.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Code);
        return rows.OrderBy(r => r.RegionId).ThenBy(r => r.Status)
            .Select(r => new SummaryProjection(regions[r.RegionId], r.Status, r.Claims, r.Total)).ToList();
    }
}
