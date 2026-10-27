namespace Harbourline.Api.Dtos;

public record RegionDto(int Id, string Code, string Name);

public record ProductDto(int Id, string Code, string Name, string Line, decimal BasePremium, decimal DefaultExcess, bool Active);
public record ProductRequest(string Code, string Name, string Line, decimal BasePremium, decimal DefaultExcess, bool Active);

public record HandlerGradeDto(int Id, string Code, string Name, decimal ApprovalLimit);

public record UserDto(int Id, string Email, string Role, string DisplayName, bool Active, DateTime CreatedAt);
public record LoginRequest(string Email, string Password);
public record LoginResponse(int UserId, string DisplayName, string Role, int? HandlerId);
public record SetActiveRequest(bool Active);

public record HandlerDto(int Id, int UserId, string Name, string Email, int GradeId, string GradeCode, decimal ApprovalLimit,
    int RegionId, string RegionCode, int MaxOpenClaims, bool Active);
public record HandlerRequest(int UserId, string Name, string Email, int GradeId, int RegionId, int MaxOpenClaims, bool Active);
public record CaseloadDto(int HandlerId, string HandlerName, int OpenClaims, int MaxOpenClaims, IReadOnlyList<ClaimListItemDto> Claims);

public record PolicyholderDto(int Id, string FullName, string Email, string? Phone, string AddressLine, string City, string Postcode,
    DateOnly DateOfBirth, int RegionId, DateTime CreatedAt);
public record PolicyholderRequest(string FullName, string Email, string? Phone, string AddressLine, string City, string Postcode,
    DateOnly DateOfBirth, int RegionId);

public record PolicyDto(int Id, string PolicyNumber, int PolicyholderId, string PolicyholderName, int ProductId, string ProductCode,
    string ProductName, DateOnly StartDate, DateOnly EndDate, decimal AnnualPremium, decimal SumInsured, string Status, int RegionId, int NoClaimsYears);
public record PolicyRequest(int PolicyholderId, int ProductId, DateOnly StartDate, DateOnly EndDate, decimal AnnualPremium, decimal SumInsured, int NoClaimsYears);
public record RenewalQuoteDto(int PolicyId, string PolicyNumber, decimal CurrentPremium, decimal NoClaimsDiscountPct, decimal RenewalPremium, DateOnly NewStartDate, DateOnly NewEndDate);

public record ClaimListItemDto(int Id, string ClaimNumber, string PolicyNumber, string PolicyholderName, string ProductName, string? HandlerName,
    string Status, string ClaimType, DateOnly LossDate, decimal EstimatedAmount, int NoteCount, int ReopenedCount);
public record ClaimDto(int Id, string ClaimNumber, int PolicyId, int? HandlerId, string ClaimType, string Status, DateOnly LossDate, DateOnly ReportedDate,
    string Description, decimal EstimatedAmount, decimal? ApprovedAmount, decimal? PaidAmount, decimal Excess, int RegionId, int ReopenedCount,
    DateOnly? ClosedDate, string? ExternalRef, DateTime CreatedAt, DateTime UpdatedAt, bool ReserveReviewDue,
    int? AssessedByUserId, int? ApprovedByUserId, decimal? SettlementAmount);
public record CreateClaimRequest(int PolicyId, string ClaimType, DateOnly LossDate, string Description, decimal EstimatedAmount);
public record UpdateClaimRequest(string ClaimType, string Description, decimal EstimatedAmount, decimal? ApprovedAmount, decimal? PaidAmount);
public record ChangeStatusRequest(string Status, int ChangedByUserId, string? Reason,
    decimal? EstimatedAmount = null, decimal? ApprovedAmount = null, decimal? SettlementAmount = null);
public record AssignClaimRequest(int HandlerId);
public record ReopenClaimRequest(int ChangedByUserId, string Reason);
public record ClaimHistoryDto(int Id, int ClaimId, string? FromStatus, string ToStatus, int ChangedByUserId, DateTime ChangedAt, string? Reason, decimal? Amount);

public record NextActionDto(string Action, string ToStatus, bool Allowed, string? BlockedReason, string[] Requires);
public record NextActionsDto(int ClaimId, string ClaimNumber, string Status, bool IsFinal, int DaysInStage,
    int? TargetDays, DateOnly? DueBy, bool Overdue, decimal? ActingUserApprovalLimit,
    List<NextActionDto> Actions, string NextStep);
public record QueueItemDto(int ClaimId, string ClaimNumber, string Status, string ProductName, string PolicyholderName,
    string? HandlerName, decimal EstimatedAmount, DateOnly ReportedDate, int DaysInStage, int TargetDays,
    bool Overdue, int DaysOverTarget);
public record LimitBreachDto(int ClaimId, string ClaimNumber, string Kind, decimal Amount, decimal? Limit, decimal OverBy,
    int UserId, string UserName, string? GradeCode, string RegionCode, DateTime OccurredAt, string? Reason);

public record ClaimNoteDto(int Id, int ClaimId, int AuthorUserId, string NoteType, string Body, DateTime CreatedAt);
public record CreateNoteRequest(int AuthorUserId, string NoteType, string Body);

public record LossRatioRow(string ProductCode, string ProductName, decimal WrittenPremium, decimal PaidClaims, decimal LossRatioPct, int ClaimCount);
public record AgeingRow(string Status, int Claims, double AverageDaysInStatus, int OldestDaysInStatus);
public record HandlerWorkloadRow(int HandlerId, string HandlerName, string GradeCode, int OpenClaims, int MaxOpenClaims, decimal OpenEstimatedValue);
public record ClaimsSummaryRow(string RegionCode, string Status, int Claims, decimal EstimatedTotal);

public record ImportErrorDto(int Row, string Message);
public record ImportResultDto(int BatchId, int TotalRows, int Created, int Updated, int Rejected, IReadOnlyList<ImportErrorDto> Errors);
public record ImportBatchDto(int Id, string FileName, string Source, int TotalRows, int CreatedRows, int UpdatedRows, int RejectedRows, DateTime CreatedAt, int CreatedByUserId);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
