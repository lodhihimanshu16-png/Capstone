using Harbourline.Api.Domain;
using Harbourline.Api.Dtos;

namespace Harbourline.Api.Mappers;

public static class RegionMapper
{
    public static RegionDto ToDto(Region r) => new(r.Id, r.Code, r.Name);
}

public static class ProductMapper
{
    public static ProductDto ToDto(Product p) => new(p.Id, p.Code, p.Name, p.Line, p.BasePremium, p.DefaultExcess, p.Active);

    public static void Apply(Product p, ProductRequest r)
    {
        p.Code = r.Code.Trim().ToUpperInvariant();
        p.Name = r.Name.Trim();
        p.Line = r.Line;
        p.BasePremium = r.BasePremium;
        p.DefaultExcess = r.DefaultExcess;
        p.Active = r.Active;
    }
}

public static class UserMapper
{
    public static UserDto ToDto(AppUser u) => new(u.Id, u.Email, u.Role, u.DisplayName, u.Active, u.CreatedAt);
}

public static class HandlerMapper
{
    public static HandlerGradeDto ToDto(HandlerGrade g) => new(g.Id, g.Code, g.Name, g.ApprovalLimit);

    public static HandlerDto ToDto(Handler h) => new(h.Id, h.UserId, h.Name, h.Email, h.GradeId, h.Grade?.Code ?? "", h.Grade?.ApprovalLimit ?? 0,
        h.RegionId, h.Region?.Code ?? "", h.MaxOpenClaims, h.Active);

    public static void Apply(Handler h, HandlerRequest r)
    {
        h.UserId = r.UserId;
        h.Name = r.Name.Trim();
        h.Email = r.Email.Trim();
        h.GradeId = r.GradeId;
        h.RegionId = r.RegionId;
        h.MaxOpenClaims = r.MaxOpenClaims;
        h.Active = r.Active;
    }
}

public static class PolicyholderMapper
{
    public static PolicyholderDto ToDto(Policyholder p) => new(p.Id, p.FullName, p.Email, p.Phone, p.AddressLine, p.City, p.Postcode, p.DateOfBirth, p.RegionId, p.CreatedAt);

    public static void Apply(Policyholder p, PolicyholderRequest r)
    {
        p.FullName = r.FullName.Trim();
        p.Email = r.Email.Trim();
        p.Phone = r.Phone;
        p.AddressLine = r.AddressLine.Trim();
        p.City = r.City.Trim();
        p.Postcode = r.Postcode.Trim().ToUpperInvariant();
        p.DateOfBirth = r.DateOfBirth;
        p.RegionId = r.RegionId;
    }
}

public static class PolicyMapper
{
    public static PolicyDto ToDto(Policy p) => new(p.Id, p.PolicyNumber, p.PolicyholderId, p.Policyholder?.FullName ?? "", p.ProductId,
        p.Product?.Code ?? "", p.Product?.Name ?? "", p.StartDate, p.EndDate, p.AnnualPremium, p.SumInsured, p.Status, p.RegionId, p.NoClaimsYears);
}

public static class ClaimMapper
{
    public static ClaimDto ToDto(Claim c, bool reserveReviewDue) => new(c.Id, c.ClaimNumber, c.PolicyId, c.HandlerId, c.ClaimType, c.Status, c.LossDate,
        c.ReportedDate, c.Description, c.EstimatedAmount, c.ApprovedAmount, c.PaidAmount, c.Excess, c.RegionId, c.ReopenedCount, c.ClosedDate,
        c.ExternalRef, c.CreatedAt, c.UpdatedAt, reserveReviewDue, c.AssessedByUserId, c.ApprovedByUserId, c.SettlementAmount);

    public static ClaimListItemDto ToListItem(Claim c, Policy policy, Policyholder holder, Product product, Handler? handler, int noteCount) =>
        new(c.Id, c.ClaimNumber, policy.PolicyNumber, holder.FullName, product.Name, handler?.Name, c.Status, c.ClaimType, c.LossDate,
            c.EstimatedAmount, noteCount, c.ReopenedCount);

    public static ClaimNoteDto ToDto(ClaimNote n) => new(n.Id, n.ClaimId, n.AuthorUserId, n.NoteType, n.Body, n.CreatedAt);

    public static ClaimHistoryDto ToDto(ClaimStatusHistory h) => new(h.Id, h.ClaimId, h.FromStatus, h.ToStatus, h.ChangedByUserId, h.ChangedAt, h.Reason, h.Amount);
}

public static class ImportBatchMapper
{
    public static ImportBatchDto ToDto(ImportBatch b) => new(b.Id, b.FileName, b.Source, b.TotalRows, b.CreatedRows, b.UpdatedRows, b.RejectedRows, b.CreatedAt, b.CreatedByUserId);
}
