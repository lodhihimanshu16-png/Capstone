namespace Harbourline.Api.Domain;

public class Region
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public class Product
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Line { get; set; } = "";
    public decimal BasePremium { get; set; }
    public decimal DefaultExcess { get; set; }
    public bool Active { get; set; } = true;
}

public class HandlerGrade
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal ApprovalLimit { get; set; }
}

public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class Handler
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int GradeId { get; set; }
    public int RegionId { get; set; }
    public int MaxOpenClaims { get; set; }
    public bool Active { get; set; } = true;
    public HandlerGrade? Grade { get; set; }
    public Region? Region { get; set; }
}

public class Policyholder
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string AddressLine { get; set; } = "";
    public string City { get; set; } = "";
    public string Postcode { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public int RegionId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Policy
{
    public int Id { get; set; }
    public string PolicyNumber { get; set; } = "";
    public int PolicyholderId { get; set; }
    public int ProductId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal AnnualPremium { get; set; }
    public decimal SumInsured { get; set; }
    public string Status { get; set; } = "Active";
    public int RegionId { get; set; }
    public int NoClaimsYears { get; set; }
    public DateTime CreatedAt { get; set; }
    public Policyholder? Policyholder { get; set; }
    public Product? Product { get; set; }
}

public class Claim
{
    public int Id { get; set; }
    public string ClaimNumber { get; set; } = "";
    public int PolicyId { get; set; }
    public int? HandlerId { get; set; }
    public string ClaimType { get; set; } = "";
    public string Status { get; set; } = "New";
    public DateOnly LossDate { get; set; }
    public DateOnly ReportedDate { get; set; }
    public string Description { get; set; } = "";
    public decimal EstimatedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal Excess { get; set; }
    public int RegionId { get; set; }
    public int ReopenedCount { get; set; }
    public DateOnly? ClosedDate { get; set; }
    public string? ExternalRef { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? AssessedByUserId { get; set; }
    public int? ApprovedByUserId { get; set; }
    public decimal? SettlementAmount { get; set; }
    /// <summary>Optimistic concurrency token (BR-15). Incremented on every save of the claim.</summary>
    public int RowVersion { get; set; }
    public Policy? Policy { get; set; }
    public Handler? Handler { get; set; }
}

/// <summary>A refused approval (BR-06 or BR-07), kept for the limits report (BR-17).</summary>
public class ClaimApprovalAttempt
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public int UserId { get; set; }
    public string? GradeCode { get; set; }
    public decimal? ApprovalLimit { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    public DateTime AttemptedAt { get; set; }
}

public class ClaimNote
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public int AuthorUserId { get; set; }
    public string NoteType { get; set; } = "Update";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class ClaimStatusHistory
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public int ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Reason { get; set; }
    public decimal? Amount { get; set; }
}

public class ImportBatch
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string Source { get; set; } = "";
    public int TotalRows { get; set; }
    public int CreatedRows { get; set; }
    public int UpdatedRows { get; set; }
    public int RejectedRows { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
}

public static class ClaimStatuses
{
    public const string New = "New";
    public const string Triaged = "Triaged";
    public const string Assessing = "Assessing";
    public const string Approved = "Approved";
    public const string Declined = "Declined";
    public const string Settled = "Settled";
    public const string Closed = "Closed";
    public const string Reopened = "Reopened";

    public static readonly string[] All = { New, Triaged, Assessing, Approved, Declined, Settled, Closed, Reopened };
    /// <summary>Claims a handler is working on: used by caseload, ageing, workload and the max_open_claims check.
    /// Triaged is included, otherwise triaged claims would vanish from those views.</summary>
    public static readonly string[] Open = { New, Triaged, Assessing, Approved, Reopened };
    public static readonly string[] Active = Open;
    /// <summary>Everything that is not Declined or Closed: the handler queue and the capacity check.</summary>
    public static readonly string[] NonFinal = { New, Triaged, Assessing, Approved, Settled, Reopened };
}
