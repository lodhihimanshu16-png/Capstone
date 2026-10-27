using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Harbourline.Api.Domain;
using Harbourline.Api.Dtos;
using Harbourline.Api.Infrastructure;
using Harbourline.Api.Mappers;
using Harbourline.Api.Repositories;

namespace Harbourline.Api.Services;

public interface IRegionService { Task<List<RegionDto>> ListAsync(); }

public class RegionService : IRegionService
{
    private readonly IRegionRepository _regions;
    public RegionService(IRegionRepository regions) => _regions = regions;
    public async Task<List<RegionDto>> ListAsync() => (await _regions.ListAsync()).Select(RegionMapper.ToDto).ToList();
}

public interface IProductService
{
    Task<List<ProductDto>> ListAsync(string? line, bool? active);
    Task<ProductDto> GetAsync(int id);
    Task<ProductDto> CreateAsync(ProductRequest request);
    Task<ProductDto> UpdateAsync(int id, ProductRequest request);
}

public class ProductService : IProductService
{
    private readonly IProductRepository _products;
    private readonly IValidator<ProductRequest> _validator;

    public ProductService(IProductRepository products, IValidator<ProductRequest> validator)
    {
        _products = products;
        _validator = validator;
    }

    public async Task<List<ProductDto>> ListAsync(string? line, bool? active) =>
        (await _products.ListAsync(line, active)).Select(ProductMapper.ToDto).ToList();

    public async Task<ProductDto> GetAsync(int id) =>
        ProductMapper.ToDto(await _products.GetByIdAsync(id) ?? throw new NotFoundException($"Product {id} was not found."));

    public async Task<ProductDto> CreateAsync(ProductRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        if (await _products.GetByCodeAsync(request.Code.Trim().ToUpperInvariant()) != null)
            throw new ConflictException($"Product code {request.Code} already exists.");
        var product = new Product();
        ProductMapper.Apply(product, request);
        return ProductMapper.ToDto(await _products.AddAsync(product));
    }

    public async Task<ProductDto> UpdateAsync(int id, ProductRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var product = await _products.GetByIdAsync(id) ?? throw new NotFoundException($"Product {id} was not found.");
        ProductMapper.Apply(product, request);
        await _products.UpdateAsync(product);
        return ProductMapper.ToDto(product);
    }
}

public interface IUserService
{
    Task<List<UserDto>> ListAsync();
    Task<UserDto> GetAsync(int id);
    Task<UserDto> SetActiveAsync(int id, bool active);
    Task<LoginResponse> LoginAsync(LoginRequest request);
}

public class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IHandlerRepository _handlers;

    public UserService(IUserRepository users, IHandlerRepository handlers)
    {
        _users = users;
        _handlers = handlers;
    }

    public async Task<List<UserDto>> ListAsync() => (await _users.ListAsync()).Select(UserMapper.ToDto).ToList();

    public async Task<UserDto> GetAsync(int id) =>
        UserMapper.ToDto(await _users.GetByIdAsync(id) ?? throw new NotFoundException($"User {id} was not found."));

    public async Task<UserDto> SetActiveAsync(int id, bool active)
    {
        var user = await _users.GetByIdAsync(id) ?? throw new NotFoundException($"User {id} was not found.");
        user.Active = active;
        await _users.UpdateAsync(user);
        return UserMapper.ToDto(user);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user == null || !user.Active || user.PasswordHash != Hash(request.Password))
            throw new BusinessRuleException("Invalid email or password.");
        var handler = await _handlers.GetByUserIdAsync(user.Id);
        return new LoginResponse(user.Id, user.DisplayName, user.Role, handler?.Id);
    }

    public static string Hash(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hl$" + password))).ToLowerInvariant();
}

public interface IHandlerService
{
    Task<List<HandlerDto>> ListAsync(int? regionId, bool? active);
    Task<HandlerDto> GetAsync(int id);
    Task<HandlerDto> CreateAsync(HandlerRequest request);
    Task<HandlerDto> UpdateAsync(int id, HandlerRequest request);
    Task<List<HandlerGradeDto>> ListGradesAsync();
    Task<CaseloadDto> GetCaseloadAsync(int id);
}

public class HandlerService : IHandlerService
{
    private readonly IHandlerRepository _handlers;
    private readonly IClaimService _claims;
    private readonly IValidator<HandlerRequest> _validator;

    public HandlerService(IHandlerRepository handlers, IClaimService claims, IValidator<HandlerRequest> validator)
    {
        _handlers = handlers;
        _claims = claims;
        _validator = validator;
    }

    public async Task<List<HandlerDto>> ListAsync(int? regionId, bool? active) =>
        (await _handlers.ListAsync(regionId, active)).Select(HandlerMapper.ToDto).ToList();

    public async Task<HandlerDto> GetAsync(int id) =>
        HandlerMapper.ToDto(await _handlers.GetByIdAsync(id) ?? throw new NotFoundException($"Handler {id} was not found."));

    public async Task<HandlerDto> CreateAsync(HandlerRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var handler = new Handler();
        HandlerMapper.Apply(handler, request);
        await _handlers.AddAsync(handler);
        return await GetAsync(handler.Id);
    }

    public async Task<HandlerDto> UpdateAsync(int id, HandlerRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var handler = await _handlers.GetByIdAsync(id) ?? throw new NotFoundException($"Handler {id} was not found.");
        handler.Grade = null;
        handler.Region = null;
        HandlerMapper.Apply(handler, request);
        await _handlers.UpdateAsync(handler);
        return await GetAsync(id);
    }

    public async Task<List<HandlerGradeDto>> ListGradesAsync() =>
        (await _handlers.ListGradesAsync()).Select(HandlerMapper.ToDto).ToList();

    public async Task<CaseloadDto> GetCaseloadAsync(int id)
    {
        var handler = await _handlers.GetByIdAsync(id) ?? throw new NotFoundException($"Handler {id} was not found.");
        var all = new List<ClaimListItemDto>();
        foreach (var status in ClaimStatuses.Open)
        {
            var page = await _claims.ListAsync(new ClaimFilter { HandlerId = id, Status = status, PageSize = 200 });
            all.AddRange(page.Items);
        }
        return new CaseloadDto(handler.Id, handler.Name, all.Count, handler.MaxOpenClaims, all);
    }
}

public interface IPolicyholderService
{
    Task<PagedResult<PolicyholderDto>> ListAsync(string? search, int? regionId, int page, int pageSize);
    Task<PolicyholderDto> GetAsync(int id);
    Task<PolicyholderDto> CreateAsync(PolicyholderRequest request);
    Task<PolicyholderDto> UpdateAsync(int id, PolicyholderRequest request);
}

public class PolicyholderService : IPolicyholderService
{
    private readonly IPolicyholderRepository _holders;
    private readonly IValidator<PolicyholderRequest> _validator;

    public PolicyholderService(IPolicyholderRepository holders, IValidator<PolicyholderRequest> validator)
    {
        _holders = holders;
        _validator = validator;
    }

    public async Task<PagedResult<PolicyholderDto>> ListAsync(string? search, int? regionId, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var (items, total) = await _holders.ListAsync(search, regionId, page, pageSize);
        return new PagedResult<PolicyholderDto>(items.Select(PolicyholderMapper.ToDto).ToList(), total, page, pageSize);
    }

    public async Task<PolicyholderDto> GetAsync(int id) =>
        PolicyholderMapper.ToDto(await _holders.GetByIdAsync(id) ?? throw new NotFoundException($"Policyholder {id} was not found."));

    public async Task<PolicyholderDto> CreateAsync(PolicyholderRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var holder = new Policyholder { CreatedAt = DateTime.UtcNow };
        PolicyholderMapper.Apply(holder, request);
        return PolicyholderMapper.ToDto(await _holders.AddAsync(holder));
    }

    public async Task<PolicyholderDto> UpdateAsync(int id, PolicyholderRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var holder = await _holders.GetByIdAsync(id) ?? throw new NotFoundException($"Policyholder {id} was not found.");
        PolicyholderMapper.Apply(holder, request);
        await _holders.UpdateAsync(holder);
        return PolicyholderMapper.ToDto(holder);
    }
}

public interface IPolicyService
{
    Task<PagedResult<PolicyDto>> ListAsync(string? status, int? productId, int? regionId, int page, int pageSize);
    Task<List<PolicyDto>> ListForHolderAsync(int holderId);
    Task<PolicyDto> GetAsync(int id);
    Task<PolicyDto> CreateAsync(PolicyRequest request);
    Task<PolicyDto> UpdateAsync(int id, PolicyRequest request);
    Task<PolicyDto> CancelAsync(int id);
    Task<RenewalQuoteDto> GetRenewalQuoteAsync(int id);
}

public class PolicyService : IPolicyService
{
    private readonly IPolicyRepository _policies;
    private readonly IPolicyholderRepository _holders;
    private readonly IProductRepository _products;
    private readonly IValidator<PolicyRequest> _validator;

    public PolicyService(IPolicyRepository policies, IPolicyholderRepository holders, IProductRepository products, IValidator<PolicyRequest> validator)
    {
        _policies = policies;
        _holders = holders;
        _products = products;
        _validator = validator;
    }

    public async Task<PagedResult<PolicyDto>> ListAsync(string? status, int? productId, int? regionId, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var (items, total) = await _policies.ListAsync(status, productId, regionId, page, pageSize);
        return new PagedResult<PolicyDto>(items.Select(PolicyMapper.ToDto).ToList(), total, page, pageSize);
    }

    public async Task<List<PolicyDto>> ListForHolderAsync(int holderId) =>
        (await _policies.ListForHolderAsync(holderId)).Select(PolicyMapper.ToDto).ToList();

    public async Task<PolicyDto> GetAsync(int id) =>
        PolicyMapper.ToDto(await _policies.GetByIdAsync(id) ?? throw new NotFoundException($"Policy {id} was not found."));

    public async Task<PolicyDto> CreateAsync(PolicyRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var holder = await _holders.GetByIdAsync(request.PolicyholderId) ?? throw new NotFoundException($"Policyholder {request.PolicyholderId} was not found.");
        var product = await _products.GetByIdAsync(request.ProductId) ?? throw new NotFoundException($"Product {request.ProductId} was not found.");
        if (!product.Active) throw new BusinessRuleException("Product is no longer sold.");

        var sequence = await _policies.CountAsync() + 100001;
        var policy = new Policy
        {
            PolicyNumber = $"HL{(product.Line == "Home" ? "H" : "M")}-{sequence}",
            PolicyholderId = holder.Id,
            ProductId = product.Id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            AnnualPremium = request.AnnualPremium,
            SumInsured = request.SumInsured,
            NoClaimsYears = request.NoClaimsYears,
            Status = "Active",
            RegionId = holder.RegionId,
            CreatedAt = DateTime.UtcNow
        };
        await _policies.AddAsync(policy);
        return await GetAsync(policy.Id);
    }

    public async Task<PolicyDto> UpdateAsync(int id, PolicyRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        var policy = await _policies.GetByIdAsync(id) ?? throw new NotFoundException($"Policy {id} was not found.");
        if (policy.Status == "Cancelled") throw new BusinessRuleException("A cancelled policy cannot be changed.");
        policy.Policyholder = null;
        policy.Product = null;
        policy.StartDate = request.StartDate;
        policy.EndDate = request.EndDate;
        policy.AnnualPremium = request.AnnualPremium;
        policy.SumInsured = request.SumInsured;
        policy.NoClaimsYears = request.NoClaimsYears;
        await _policies.UpdateAsync(policy);
        return await GetAsync(id);
    }

    public async Task<PolicyDto> CancelAsync(int id)
    {
        var policy = await _policies.GetByIdAsync(id) ?? throw new NotFoundException($"Policy {id} was not found.");
        if (policy.Status == "Cancelled") throw new ConflictException("Policy is already cancelled.");
        policy.Policyholder = null;
        policy.Product = null;
        policy.Status = "Cancelled";
        await _policies.UpdateAsync(policy);
        return await GetAsync(id);
    }

    public async Task<RenewalQuoteDto> GetRenewalQuoteAsync(int id)
    {
        var policy = await _policies.GetByIdAsync(id) ?? throw new NotFoundException($"Policy {id} was not found.");
        var discount = PolicyPricing.NoClaimsDiscount(policy.NoClaimsYears);
        return new RenewalQuoteDto(policy.Id, policy.PolicyNumber, policy.AnnualPremium, discount * 100,
            PolicyPricing.RenewalPremium(policy.AnnualPremium, policy.NoClaimsYears), policy.EndDate.AddDays(1), policy.EndDate.AddYears(1));
    }
}

public interface IClaimService
{
    Task<PagedResult<ClaimListItemDto>> ListAsync(ClaimFilter filter);
    Task<ClaimDto> GetAsync(int id);
    Task<ClaimDto> CreateAsync(CreateClaimRequest request, int userId);
    Task<ClaimDto> UpdateAsync(int id, UpdateClaimRequest request);
    Task<ClaimDto> ChangeStatusAsync(int id, ChangeStatusRequest request);
    Task<ClaimDto> AssignAsync(int id, AssignClaimRequest request);
    Task<ClaimDto> ReopenAsync(int id, ReopenClaimRequest request);
    Task<List<ClaimHistoryDto>> GetHistoryAsync(int id);
    Task<NextActionsDto> GetNextActionsAsync(int id, int? actingUserId);
    Task<PagedResult<QueueItemDto>> QueueAsync(int? handlerId, int? regionId, int page, int pageSize);
}

public class ClaimService : IClaimService
{
    private readonly IClaimRepository _claims;
    private readonly IPolicyRepository _policies;
    private readonly IHandlerRepository _handlers;
    private readonly IClaimNoteRepository _notes;
    private readonly IValidator<CreateClaimRequest> _createValidator;
    private readonly IValidator<UpdateClaimRequest> _updateValidator;
    private readonly IValidator<ChangeStatusRequest> _statusValidator;
    private readonly IValidator<ReopenClaimRequest> _reopenValidator;
    private readonly IUserRepository _users;
    private readonly TimeProvider _clock;

    public ClaimService(IClaimRepository claims, IPolicyRepository policies, IHandlerRepository handlers, IClaimNoteRepository notes,
        IValidator<CreateClaimRequest> createValidator, IValidator<UpdateClaimRequest> updateValidator,
        IValidator<ChangeStatusRequest> statusValidator, IValidator<ReopenClaimRequest> reopenValidator,
        IUserRepository users, TimeProvider clock)
    {
        _claims = claims;
        _policies = policies;
        _handlers = handlers;
        _notes = notes;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _statusValidator = statusValidator;
        _reopenValidator = reopenValidator;
        _users = users;
        _clock = clock;
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<ClaimListItemDto>> ListAsync(ClaimFilter filter)
    {
        filter.Page = Math.Max(filter.Page, 1);
        filter.PageSize = Math.Clamp(filter.PageSize, 1, 200);
        // Two queries whatever the page size: one joined page query and one count.
        var rows = await _claims.ListItemsAsync(filter);
        var total = await _claims.CountAsync(filter);
        var items = rows.Select(r => new ClaimListItemDto(r.Id, r.ClaimNumber, r.PolicyNumber, r.PolicyholderName, r.ProductName, r.HandlerName,
            r.Status, r.ClaimType, r.LossDate, r.EstimatedAmount, r.NoteCount, r.ReopenedCount)).ToList();
        return new PagedResult<ClaimListItemDto>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<ClaimDto> GetAsync(int id)
    {
        var claim = await _claims.GetByIdAsync(id) ?? throw new NotFoundException($"Claim {id} was not found.");
        var lastNote = await _notes.LastNoteAtAsync(id);
        return ClaimMapper.ToDto(claim, lastNote.HasValue && ClaimRules.IsReserveReviewDue(lastNote.Value, Now()));
    }

    public async Task<ClaimDto> CreateAsync(CreateClaimRequest request, int userId)
    {
        await _createValidator.ValidateAndThrowAsync(request);
        CardNumberDetector.RejectIfCardNumber(request.Description, "description");
        var policy = await _policies.GetByIdAsync(request.PolicyId) ?? throw new NotFoundException($"Policy {request.PolicyId} was not found.");
        if (policy.Status == "Cancelled") throw new BusinessRuleException("Claims cannot be registered against a cancelled policy.");

        var now = Now();
        var sequence = await _claims.NextSequenceAsync();
        var claim = new Claim
        {
            ClaimNumber = ClaimRules.NextClaimNumber(sequence),
            PolicyId = policy.Id,
            ClaimType = request.ClaimType,
            Status = ClaimStatuses.New,
            LossDate = request.LossDate,
            ReportedDate = DateOnly.FromDateTime(now),
            Description = request.Description,
            EstimatedAmount = request.EstimatedAmount,
            Excess = policy.Product?.DefaultExcess ?? 0,
            RegionId = policy.RegionId,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _claims.AddAsync(claim);
        await _claims.AddHistoryAsync(new ClaimStatusHistory
        {
            ClaimId = claim.Id, ToStatus = ClaimStatuses.New, ChangedByUserId = userId, ChangedAt = now, Reason = "Claim registered"
        });
        return await GetAsync(claim.Id);
    }

    public async Task<ClaimDto> UpdateAsync(int id, UpdateClaimRequest request)
    {
        await _updateValidator.ValidateAndThrowAsync(request);
        CardNumberDetector.RejectIfCardNumber(request.Description, "description");
        var claim = await _claims.GetByIdAsync(id) ?? throw new NotFoundException($"Claim {id} was not found.");

        // BR-12: a Closed or Declined claim cannot be edited.
        if (claim.Status == ClaimStatuses.Closed)
            throw new BusinessRuleException($"Claim {claim.ClaimNumber} is Closed and cannot be edited. The only change allowed is to reopen it with a reason.");
        if (claim.Status == ClaimStatuses.Declined)
            throw new BusinessRuleException($"Claim {claim.ClaimNumber} is Declined. Declined claims are final and cannot be edited.");

        // BR-13: approved and paid amounts change only through status moves. Sending the current value is fine.
        if (request.ApprovedAmount.HasValue && request.ApprovedAmount != claim.ApprovedAmount)
            throw new BusinessRuleException("The approved amount cannot be changed by editing the claim. It is set when the claim is moved to Approved (status change with approvedAmount).");
        if (request.PaidAmount.HasValue && request.PaidAmount != claim.PaidAmount)
            throw new BusinessRuleException("The paid amount cannot be changed by editing the claim. It is set when the claim is moved to Settled (status change with settlementAmount).");

        claim.ClaimType = request.ClaimType;
        claim.Description = request.Description;
        claim.EstimatedAmount = request.EstimatedAmount;
        claim.UpdatedAt = Now();
        await _claims.UpdateAsync(claim);
        return await GetAsync(id);
    }

    // ---- who is acting (BR-16) ----

    private sealed record Actor(AppUser User, Handler? Handler)
    {
        public bool IsManager => User.Role == "Manager";
        public decimal? Limit => IsManager ? null : Handler?.Grade?.ApprovalLimit;
    }

    private async Task<Actor> LoadActorAsync(int userId)
    {
        var user = await _users.GetByIdAsync(userId) ?? throw new NotFoundException($"User {userId} was not found.");
        var handler = user.Role == "Handler" ? await _handlers.GetByUserIdAsync(user.Id) : null;
        return new Actor(user, handler);
    }

    /// <summary>BR-16. Returns why this user may not act on claims, or null when they may.</summary>
    private static string? ActorProblem(Actor actor)
    {
        if (!actor.User.Active)
            return $"{actor.User.DisplayName} is not an active user and cannot act on claims. Only active handlers and the Head of Claims can.";
        return actor.User.Role switch
        {
            "Manager" => null,
            "Handler" when actor.Handler is { Active: true } => null,
            "Handler" => $"{actor.User.DisplayName} has no active handler record and cannot act on claims. Only active handlers and the Head of Claims can.",
            _ => $"Users with the role {actor.User.Role} are read-only and cannot act on claims. Only active handlers and the Head of Claims can."
        };
    }

    private static string TriageNeedsHandler(Actor actor) =>
        $"{actor.User.DisplayName} cannot triage a claim: the handler who triages becomes its owner, so an active handler is needed. Use the assign call to give the claim to a handler.";

    private static string PolicyNotInForce(Claim claim, Policy policy) =>
        policy.Status == "Cancelled"
            ? $"Claim {claim.ClaimNumber} cannot be triaged: policy {policy.PolicyNumber} is cancelled, so it was not in force on the date of loss ({Iso(claim.LossDate)})."
            : $"Claim {claim.ClaimNumber} cannot be triaged: policy {policy.PolicyNumber} was not in force on the date of loss ({Iso(claim.LossDate)}). Cover runs from {Iso(policy.StartDate)} to {Iso(policy.EndDate)}.";

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // ---- status change ----

    public async Task<ClaimDto> ChangeStatusAsync(int id, ChangeStatusRequest request)
    {
        await _statusValidator.ValidateAndThrowAsync(request);
        CardNumberDetector.RejectIfCardNumber(request.Reason, "reason");
        var claim = await _claims.GetByIdAsync(id) ?? throw new NotFoundException($"Claim {id} was not found.");
        var from = claim.Status;
        var to = request.Status;

        // BR-01: only the arrows in the transition table (409).
        if (!ClaimWorkflow.IsLegal(from, to))
            throw new ConflictException(ClaimWorkflow.IllegalMoveMessage(claim.ClaimNumber, from, to));

        var actor = await LoadActorAsync(request.ChangedByUserId);
        var problem = ActorProblem(actor);
        if (problem != null) throw new BusinessRuleException(problem);

        if (to == ClaimStatuses.Reopened)
            return await ReopenCoreAsync(claim, actor, request.Reason);

        var now = Now();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var history = new ClaimStatusHistory
        {
            ClaimId = id, FromStatus = from, ToStatus = to, ChangedByUserId = actor.User.Id, ChangedAt = now, Reason = reason
        };

        switch (to)
        {
            case ClaimStatuses.Triaged:
                await ApplyTriageAsync(claim, actor, request.EstimatedAmount!.Value, history);
                break;
            case ClaimStatuses.Assessing:
                claim.AssessedByUserId = actor.User.Id;
                claim.ApprovedByUserId = null;
                break;
            case ClaimStatuses.Approved:
                await ApplyApprovalAsync(claim, actor, request.ApprovedAmount!.Value, history, now);
                break;
            case ClaimStatuses.Declined:
                if (reason == null)
                    throw new BusinessRuleException("A reason is required to decline a claim (up to 300 characters).");
                break;
            case ClaimStatuses.Settled:
                ApplySettlement(claim, request.SettlementAmount, history);
                break;
            case ClaimStatuses.Closed:
                if (claim.SettlementAmount is null)
                    throw new BusinessRuleException($"Claim {claim.ClaimNumber} cannot be closed because it has no settlement amount. Reopen it and settle it again with a settlement amount first.");
                claim.ClosedDate = DateOnly.FromDateTime(now);
                history.Amount = claim.SettlementAmount;
                break;
        }

        claim.Status = to;
        claim.UpdatedAt = now;
        // One SaveChanges: the claim, its new version and the history row are saved together or not at all.
        await _claims.SaveStatusChangeAsync(claim, history);
        return await GetAsync(id);
    }

    private async Task ApplyTriageAsync(Claim claim, Actor actor, decimal estimate, ClaimStatusHistory history)
    {
        if (actor.Handler == null) throw new BusinessRuleException(TriageNeedsHandler(actor));
        var policy = await _policies.GetByIdAsync(claim.PolicyId) ?? throw new NotFoundException($"Policy {claim.PolicyId} was not found.");
        if (!ClaimWorkflow.PolicyInForce(policy.Status, policy.StartDate, policy.EndDate, claim.LossDate))
            throw new BusinessRuleException(PolicyNotInForce(claim, policy));

        var handler = actor.Handler;
        var held = await _claims.CountActiveForHandlerAsync(handler.Id, claim.Id);
        if (held >= handler.MaxOpenClaims)
            throw new ConflictException($"{handler.Name} already has {held} open claims (limit {handler.MaxOpenClaims}). Finish or reassign a claim before triaging another.");

        claim.HandlerId = handler.Id;
        claim.EstimatedAmount = estimate;
        history.Amount = estimate;
    }

    private async Task ApplyApprovalAsync(Claim claim, Actor actor, decimal amount, ClaimStatusHistory history, DateTime now)
    {
        // BR-06 first: the assessor never approves, whatever the amount and whoever they are.
        if (claim.AssessedByUserId == actor.User.Id)
        {
            var message = $"The assessor cannot approve the claim. {actor.User.DisplayName} assessed claim {claim.ClaimNumber}, so another handler or the Head of Claims (who did not assess it) must approve it.";
            await RecordBlockedAttemptAsync(claim, actor, amount, message, now);
            throw new BusinessRuleException(message);
        }

        // BR-07: the limit of the handler's grade. The Head of Claims has none.
        if (!actor.IsManager)
        {
            var grade = actor.Handler?.Grade;
            var limit = grade?.ApprovalLimit ?? 0m;
            var grades = await _handlers.ListGradesAsync();
            var message = ClaimWorkflow.ApprovalLimitMessage(amount, grade?.Name ?? "no grade", limit, grades);
            if (message != null)
            {
                await RecordBlockedAttemptAsync(claim, actor, amount, message, now);
                throw new BusinessRuleException(message);
            }
        }

        claim.ApprovedAmount = amount;
        claim.ApprovedByUserId = actor.User.Id;
        history.Amount = amount;
    }

    // BR-17: its own save, before the 422 is thrown, so the attempt survives the failed request.
    private Task RecordBlockedAttemptAsync(Claim claim, Actor actor, decimal amount, string message, DateTime now) =>
        _claims.AddApprovalAttemptAsync(new ClaimApprovalAttempt
        {
            ClaimId = claim.Id,
            UserId = actor.User.Id,
            GradeCode = actor.Handler?.Grade?.Code,
            ApprovalLimit = actor.Limit,
            Amount = amount,
            Reason = message.Length <= 300 ? message : message[..300],
            AttemptedAt = now
        });

    private static void ApplySettlement(Claim claim, decimal? settlement, ClaimStatusHistory history)
    {
        if (settlement is null or <= 0)
            throw new BusinessRuleException("A settlement amount above 0.00 is required to settle a claim.");
        if (claim.ApprovedAmount is null)
            throw new BusinessRuleException($"Claim {claim.ClaimNumber} has no approved amount, so it cannot be settled.");
        if (settlement > claim.ApprovedAmount)
            throw new BusinessRuleException($"Settlement amount {ClaimWorkflow.Money(settlement.Value)} is above the approved amount of {ClaimWorkflow.Money(claim.ApprovedAmount.Value)}. The settlement can be up to {ClaimWorkflow.Money(claim.ApprovedAmount.Value)}.");

        claim.SettlementAmount = settlement;
        claim.PaidAmount = settlement;
        history.Amount = settlement;
    }

    public async Task<NextActionsDto> GetNextActionsAsync(int id, int? actingUserId)
    {
        var claim = await _claims.GetByIdAsync(id) ?? throw new NotFoundException($"Claim {id} was not found.");
        var actor = actingUserId.HasValue ? await LoadActorAsync(actingUserId.Value) : null;
        var actorProblem = actor == null ? null : ActorProblem(actor);

        var entered = await _claims.GetStageEnteredAtAsync(id, claim.Status) ?? claim.CreatedAt;
        var today = DateOnly.FromDateTime(Now());
        var days = ClaimWorkflow.DaysInStage(entered, today);

        // Claim-level facts that block an action whoever is acting.
        Policy? policy = claim.Status == ClaimStatuses.New ? await _policies.GetByIdAsync(claim.PolicyId) : null;
        var held = actor?.Handler != null && claim.Status == ClaimStatuses.New
            ? await _claims.CountActiveForHandlerAsync(actor.Handler.Id, claim.Id) : 0;

        var actions = new List<NextActionDto>();
        foreach (var move in ClaimWorkflow.MovesFrom(claim.Status))
        {
            string? blocked = null;
            if (move.Action == ClaimWorkflow.Triage && policy != null
                && !ClaimWorkflow.PolicyInForce(policy.Status, policy.StartDate, policy.EndDate, claim.LossDate))
                blocked = PolicyNotInForce(claim, policy);
            else if (move.Action == ClaimWorkflow.Settle && claim.ApprovedAmount is null)
                blocked = "This claim has no approved amount, so it cannot be settled.";
            else if (move.Action == ClaimWorkflow.Close && claim.SettlementAmount is null)
                blocked = "This claim has no settlement amount. Reopen it and settle it again with a settlement amount before closing.";

            if (blocked == null && actor != null)
            {
                if (actorProblem != null) blocked = actorProblem;
                else if (move.Action == ClaimWorkflow.Triage && actor.Handler == null) blocked = TriageNeedsHandler(actor);
                else if (move.Action == ClaimWorkflow.Triage && held >= actor.Handler!.MaxOpenClaims)
                    blocked = $"You already have {held} open claims (limit {actor.Handler.MaxOpenClaims}).";
                else if (move.Action == ClaimWorkflow.Approve && claim.AssessedByUserId == actor.User.Id)
                    blocked = "You assessed this claim, so you cannot approve it.";
            }
            actions.Add(new NextActionDto(move.Action, move.To, blocked == null, blocked, move.Requires));
        }

        var grades = claim.Status == ClaimStatuses.Assessing ? await _handlers.ListGradesAsync() : new List<HandlerGrade>();
        return new NextActionsDto(claim.Id, claim.ClaimNumber, claim.Status, ClaimWorkflow.IsFinal(claim.Status), days,
            ClaimWorkflow.TargetDays(claim.Status), ClaimWorkflow.DueBy(claim.Status, entered), ClaimWorkflow.IsOverdue(claim.Status, days),
            actor?.Limit, actions, NextStep(claim, grades));
    }

    private static string NextStep(Claim claim, IReadOnlyList<HandlerGrade> grades)
    {
        switch (claim.Status)
        {
            case ClaimStatuses.New:
                return "The claim is new. A handler needs to triage it: check the policy was in force on the date of loss, set the estimate and take ownership. It can also be declined with a reason.";
            case ClaimStatuses.Triaged:
                return "The claim has been triaged. A handler can start the assessment, or decline it with a reason.";
            case ClaimStatuses.Assessing:
                var needed = ClaimWorkflow.LowestGradeFor(claim.EstimatedAmount, grades);
                var who = needed == null ? "the Head of Claims" : $"a {needed.Name} or above";
                var who2 = claim.AssessedByUserId.HasValue ? "Another handler" : "A handler";
                return $"Assessment is in progress. {who2} needs to approve it (estimate {ClaimWorkflow.Money(claim.EstimatedAmount)} needs {who}), or it can be declined with a reason.";
            case ClaimStatuses.Approved:
                var approved = claim.ApprovedAmount.HasValue ? ClaimWorkflow.Money(claim.ApprovedAmount.Value) : "the approved amount";
                return $"The claim is approved for {approved}. Settle it by recording the settlement amount, which can be up to {approved}.";
            case ClaimStatuses.Settled:
                var paid = claim.SettlementAmount.HasValue ? ClaimWorkflow.Money(claim.SettlementAmount.Value) : "the settlement";
                return $"The claim is settled for {paid}. Close it once the final payment has gone out, or reopen it with a reason.";
            case ClaimStatuses.Closed:
                return "The claim is closed. The only possible step is to reopen it, with a reason.";
            case ClaimStatuses.Declined:
                return "The claim was declined. Declined is final: nothing more can be done to it.";
            case ClaimStatuses.Reopened:
                return "The claim was reopened. It must be assessed again before it can be approved.";
            default:
                return "";
        }
    }

    public async Task<PagedResult<QueueItemDto>> QueueAsync(int? handlerId, int? regionId, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var now = Now();
        var today = DateOnly.FromDateTime(now);
        var (rows, total) = await _claims.QueueAsync(handlerId, regionId, page, pageSize, now.Date);
        var items = rows.Select(r =>
        {
            var days = ClaimWorkflow.DaysInStage(r.EnteredAt, today);
            return new QueueItemDto(r.ClaimId, r.ClaimNumber, r.Status, r.ProductName, r.PolicyholderName, r.HandlerName, r.EstimatedAmount,
                r.ReportedDate, days, ClaimWorkflow.TargetDays(r.Status) ?? 0, ClaimWorkflow.IsOverdue(r.Status, days),
                ClaimWorkflow.DaysOverTarget(r.Status, days));
        }).ToList();
        return new PagedResult<QueueItemDto>(items, total, page, pageSize);
    }

    public async Task<ClaimDto> AssignAsync(int id, AssignClaimRequest request)
    {
        var claim = await _claims.GetByIdAsync(id) ?? throw new NotFoundException($"Claim {id} was not found.");
        // BR-12: a Closed or Declined claim cannot be changed, and that includes who owns it.
        if (claim.Status is ClaimStatuses.Closed or ClaimStatuses.Declined)
            throw new BusinessRuleException($"Claim {claim.ClaimNumber} is {claim.Status}, so its handler cannot be changed.");
        var handler = await _handlers.GetByIdAsync(request.HandlerId) ?? throw new NotFoundException($"Handler {request.HandlerId} was not found.");
        if (!handler.Active) throw new BusinessRuleException("Handler is not active.");

        var openClaims = await _claims.CountOpenForHandlerAsync(handler.Id);
        if (openClaims >= handler.MaxOpenClaims)
            throw new ConflictException($"{handler.Name} already has {openClaims} open claims (limit {handler.MaxOpenClaims}).");

        claim.HandlerId = handler.Id;
        claim.UpdatedAt = Now();
        await _claims.UpdateAsync(claim);
        return await GetAsync(id);
    }

    public async Task<ClaimDto> ReopenAsync(int id, ReopenClaimRequest request)
    {
        await _reopenValidator.ValidateAndThrowAsync(request);
        CardNumberDetector.RejectIfCardNumber(request.Reason, "reason");
        var claim = await _claims.GetByIdAsync(id) ?? throw new NotFoundException($"Claim {id} was not found.");
        if (claim.Status != ClaimStatuses.Settled && claim.Status != ClaimStatuses.Closed)
            throw new BusinessRuleException(claim.Status == ClaimStatuses.Declined
                ? $"Claim {claim.ClaimNumber} is Declined, which is final. Only Settled or Closed claims can be reopened."
                : $"Only Settled or Closed claims can be reopened. Claim {claim.ClaimNumber} is {claim.Status}.");

        var actor = await LoadActorAsync(request.ChangedByUserId);
        var problem = ActorProblem(actor);
        if (problem != null) throw new BusinessRuleException(problem);
        return await ReopenCoreAsync(claim, actor, request.Reason);
    }

    // BR-11. The caller has already checked the claim is Settled or Closed and that the actor may act.
    private async Task<ClaimDto> ReopenCoreAsync(Claim claim, Actor actor, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException("A reason is required to reopen a claim (not blank, up to 300 characters).");
        if (reason.Trim().Length > 300)
            throw new BusinessRuleException("The reason must be at most 300 characters.");

        var from = claim.Status;
        var now = Now();
        claim.Status = ClaimStatuses.Reopened;
        claim.ReopenedCount += 1;
        claim.ClosedDate = null;
        claim.AssessedByUserId = null;
        claim.ApprovedByUserId = null;
        claim.UpdatedAt = now;
        await _claims.SaveStatusChangeAsync(claim, new ClaimStatusHistory
        {
            ClaimId = claim.Id, FromStatus = from, ToStatus = ClaimStatuses.Reopened, ChangedByUserId = actor.User.Id, ChangedAt = now, Reason = reason.Trim()
        });
        return await GetAsync(claim.Id);
    }

    public async Task<List<ClaimHistoryDto>> GetHistoryAsync(int id)
    {
        if (await _claims.GetByIdAsync(id) == null) throw new NotFoundException($"Claim {id} was not found.");
        return (await _claims.ListHistoryAsync(id)).Select(ClaimMapper.ToDto).ToList();
    }
}

public interface IClaimNoteService
{
    Task<List<ClaimNoteDto>> ListAsync(int claimId);
    Task<ClaimNoteDto> AddAsync(int claimId, CreateNoteRequest request);
    Task DeleteAsync(int noteId);
}

public class ClaimNoteService : IClaimNoteService
{
    private readonly IClaimNoteRepository _notes;
    private readonly IClaimRepository _claims;
    private readonly IValidator<CreateNoteRequest> _validator;
    private readonly TimeProvider _clock;

    public ClaimNoteService(IClaimNoteRepository notes, IClaimRepository claims, IValidator<CreateNoteRequest> validator, TimeProvider clock)
    {
        _notes = notes;
        _claims = claims;
        _validator = validator;
        _clock = clock;
    }

    public async Task<List<ClaimNoteDto>> ListAsync(int claimId)
    {
        if (await _claims.GetByIdAsync(claimId) == null) throw new NotFoundException($"Claim {claimId} was not found.");
        return (await _notes.ListForClaimAsync(claimId)).Select(ClaimMapper.ToDto).ToList();
    }

    public async Task<ClaimNoteDto> AddAsync(int claimId, CreateNoteRequest request)
    {
        await _validator.ValidateAndThrowAsync(request);
        if (await _claims.GetByIdAsync(claimId) == null) throw new NotFoundException($"Claim {claimId} was not found.");
        // BR-22: the system stores no card numbers.
        CardNumberDetector.RejectIfCardNumber(request.Body, "note");
        var note = new ClaimNote
        {
            ClaimId = claimId, AuthorUserId = request.AuthorUserId, NoteType = request.NoteType, Body = request.Body, CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        return ClaimMapper.ToDto(await _notes.AddAsync(note));
    }

    public async Task DeleteAsync(int noteId)
    {
        var note = await _notes.GetByIdAsync(noteId) ?? throw new NotFoundException($"Note {noteId} was not found.");
        await _notes.DeleteAsync(note);
    }
}

public interface IReportService
{
    Task<List<LossRatioRow>> LossRatioAsync(DateOnly? from, DateOnly? to);
    Task<List<AgeingRow>> AgeingAsync();
    Task<List<HandlerWorkloadRow>> WorkloadAsync();
    Task<List<ClaimsSummaryRow>> SummaryAsync();
    Task<List<LimitBreachDto>> LimitsBreachesAsync(DateOnly? from, DateOnly? to, int? regionId);
}

public class ReportService : IReportService
{
    private readonly IReportRepository _reports;
    private readonly IConfiguration _config;

    public ReportService(IReportRepository reports, IConfiguration config)
    {
        _reports = reports;
        _config = config;
    }

    public async Task<List<LossRatioRow>> LossRatioAsync(DateOnly? from, DateOnly? to)
    {
        var start = from ?? new DateOnly(2025, 10, 1);
        var end = to ?? new DateOnly(2026, 9, 30);
        if (end < start) throw new BusinessRuleException("'to' must not be before 'from'.");
        var rows = await _reports.LossRatioAsync(start.ToDateTime(TimeOnly.MinValue), end.AddDays(1).ToDateTime(TimeOnly.MinValue));
        return rows.Select(r => new LossRatioRow(r.ProductCode, r.ProductName, r.WrittenPremium, r.PaidClaims,
            LossRatioCalculator.Percent(r.PaidClaims, r.WrittenPremium), r.ClaimCount)).ToList();
    }

    public async Task<List<AgeingRow>> AgeingAsync()
    {
        var asOf = DateTime.Parse(_config["Reporting:AsOfDate"] ?? "2026-10-01").Date.AddDays(1);
        var rows = await _reports.AgeingAsync();
        return rows.GroupBy(r => r.Status).Select(g =>
        {
            var days = g.Select(r => (int)(asOf - (r.EnteredStatusAt ?? asOf)).TotalDays).ToList();
            return new AgeingRow(g.Key, g.Count(), Math.Round(days.Average(), 1), days.Max());
        }).OrderBy(r => Array.IndexOf(ClaimStatuses.All, r.Status)).ToList();
    }

    public async Task<List<HandlerWorkloadRow>> WorkloadAsync() =>
        (await _reports.WorkloadAsync()).Select(w => new HandlerWorkloadRow(w.HandlerId, w.HandlerName, w.GradeCode, w.OpenClaims, w.MaxOpenClaims, w.OpenEstimatedValue)).ToList();

    public async Task<List<LimitBreachDto>> LimitsBreachesAsync(DateOnly? from, DateOnly? to, int? regionId)
    {
        if (from.HasValue && to.HasValue && to < from)
            throw new BadRequestException($"'to' ({to:yyyy-MM-dd}) must not be before 'from' ({from:yyyy-MM-dd}). Use a 'to' date on or after the 'from' date.");
        var rows = await _reports.LimitBreachesAsync(from?.ToDateTime(TimeOnly.MinValue), to?.AddDays(1).ToDateTime(TimeOnly.MinValue), regionId);
        return rows.OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.ClaimId).Take(500)
            .Select(r => new LimitBreachDto(r.ClaimId, r.ClaimNumber, r.Kind, r.Amount, r.Limit,
                r.Limit.HasValue ? Math.Max(r.Amount - r.Limit.Value, 0m) : 0m,
                r.UserId, r.UserName, r.GradeCode, r.RegionCode, r.OccurredAt, r.Reason)).ToList();
    }

    public async Task<List<ClaimsSummaryRow>> SummaryAsync() =>
        (await _reports.SummaryAsync()).Select(s => new ClaimsSummaryRow(s.RegionCode, s.Status, s.Claims, s.EstimatedTotal)).ToList();
}

public interface IImportBatchService
{
    Task<List<ImportBatchDto>> ListAsync();
    Task<ImportBatchDto> GetAsync(int id);
}

public class ImportBatchService : IImportBatchService
{
    private readonly IImportBatchRepository _batches;
    public ImportBatchService(IImportBatchRepository batches) => _batches = batches;
    public async Task<List<ImportBatchDto>> ListAsync() => (await _batches.ListAsync()).Select(ImportBatchMapper.ToDto).ToList();

    public async Task<ImportBatchDto> GetAsync(int id) =>
        ImportBatchMapper.ToDto(await _batches.GetByIdAsync(id) ?? throw new NotFoundException($"Import batch {id} was not found."));
}
