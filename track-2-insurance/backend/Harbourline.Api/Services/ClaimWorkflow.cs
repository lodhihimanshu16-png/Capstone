using System.Globalization;
using Harbourline.Api.Domain;

namespace Harbourline.Api.Services;

/// <summary>One legal arrow of the workflow (BR-01).</summary>
public record WorkflowMove(string Action, string From, string To, string[] Requires);

/// <summary>
/// The claims workflow as pure rules: the transition table (BR-01), the stage targets (BR-18)
/// and the message helpers. No I/O and no clock, so it is unit-testable on its own.
/// </summary>
public static class ClaimWorkflow
{
    public const string Triage = "Triage";
    public const string Assess = "Assess";
    public const string Approve = "Approve";
    public const string Decline = "Decline";
    public const string Settle = "Settle";
    public const string Close = "Close";
    public const string Reopen = "Reopen";

    public const string EstimatedAmount = "estimatedAmount";
    public const string ApprovedAmount = "approvedAmount";
    public const string SettlementAmount = "settlementAmount";
    public const string Reason = "reason";

    // Order matters: it is the order used in "Allowed next statuses" and in next-actions.
    private static readonly WorkflowMove[] Moves =
    {
        new(Triage, ClaimStatuses.New, ClaimStatuses.Triaged, new[] { EstimatedAmount }),
        new(Decline, ClaimStatuses.New, ClaimStatuses.Declined, new[] { Reason }),
        new(Assess, ClaimStatuses.Triaged, ClaimStatuses.Assessing, Array.Empty<string>()),
        new(Decline, ClaimStatuses.Triaged, ClaimStatuses.Declined, new[] { Reason }),
        new(Approve, ClaimStatuses.Assessing, ClaimStatuses.Approved, new[] { ApprovedAmount }),
        new(Decline, ClaimStatuses.Assessing, ClaimStatuses.Declined, new[] { Reason }),
        new(Settle, ClaimStatuses.Approved, ClaimStatuses.Settled, new[] { SettlementAmount }),
        new(Close, ClaimStatuses.Settled, ClaimStatuses.Closed, Array.Empty<string>()),
        new(Reopen, ClaimStatuses.Settled, ClaimStatuses.Reopened, new[] { Reason }),
        new(Reopen, ClaimStatuses.Closed, ClaimStatuses.Reopened, new[] { Reason }),
        new(Assess, ClaimStatuses.Reopened, ClaimStatuses.Assessing, Array.Empty<string>()),
    };

    /// <summary>BR-18 targets in whole days. Declined and Closed have none.</summary>
    public static int? TargetDays(string status) => status switch
    {
        ClaimStatuses.New => 0,
        ClaimStatuses.Triaged => 2,
        ClaimStatuses.Assessing => 10,
        ClaimStatuses.Approved => 3,
        ClaimStatuses.Settled => 5,
        ClaimStatuses.Reopened => 2,
        _ => null
    };

    public static IReadOnlyList<WorkflowMove> MovesFrom(string status) => Moves.Where(m => m.From == status).ToList();

    public static string[] AllowedNextStatuses(string status) => Moves.Where(m => m.From == status).Select(m => m.To).ToArray();

    public static WorkflowMove? Find(string from, string to) => Moves.FirstOrDefault(m => m.From == from && m.To == to);

    public static bool IsLegal(string from, string to) => Find(from, to) != null;

    /// <summary>A claim with no legal move at all (Declined) is final.</summary>
    public static bool IsFinal(string status) => !Moves.Any(m => m.From == status);

    public static string IllegalMoveMessage(string claimNumber, string from, string to)
    {
        var allowed = AllowedNextStatuses(from);
        return allowed.Length == 0
            ? $"Cannot move claim {claimNumber} from {from} to {to}. {from} claims are final: no further status changes are allowed."
            : $"Cannot move claim {claimNumber} from {from} to {to}. Allowed next statuses: {string.Join(", ", allowed)}.";
    }

    /// <summary>Whole calendar days between the entry date and today (never negative).</summary>
    public static int DaysInStage(DateTime enteredAt, DateOnly today) =>
        Math.Max(today.DayNumber - DateOnly.FromDateTime(enteredAt).DayNumber, 0);

    public static bool IsOverdue(string status, int daysInStage) => TargetDays(status) is { } target && daysInStage > target;

    public static int DaysOverTarget(string status, int daysInStage) =>
        TargetDays(status) is { } target ? Math.Max(daysInStage - target, 0) : 0;

    public static DateOnly? DueBy(string status, DateTime enteredAt) =>
        TargetDays(status) is { } target ? DateOnly.FromDateTime(enteredAt).AddDays(target) : null;

    /// <summary>Money as text with two decimals and thousands separators, culture independent.</summary>
    public static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>The lowest grade whose limit covers the amount, or null when only the Head of Claims can.</summary>
    public static HandlerGrade? LowestGradeFor(decimal amount, IEnumerable<HandlerGrade> grades) =>
        grades.OrderBy(g => g.ApprovalLimit).FirstOrDefault(g => g.ApprovalLimit >= amount);

    /// <summary>BR-07. Returns null when the amount is within the limit, otherwise the refusal message.</summary>
    public static string? ApprovalLimitMessage(decimal amount, string gradeName, decimal limit, IEnumerable<HandlerGrade> grades)
    {
        if (amount <= limit) return null;
        var head = $"Amount {Money(amount)} is above your approval limit of {Money(limit)} ({gradeName}).";
        var needed = LowestGradeFor(amount, grades);
        return needed == null
            ? $"{head} Only the Head of Claims can approve this amount."
            : $"{head} It needs a {needed.Name} (limit {Money(needed.ApprovalLimit)}) or above.";
    }

    /// <summary>Whether the claim's policy covers the date of loss (BR-03a). A cancelled policy never does.</summary>
    public static bool PolicyInForce(string policyStatus, DateOnly start, DateOnly end, DateOnly lossDate) =>
        policyStatus != "Cancelled" && lossDate >= start && lossDate <= end;
}
