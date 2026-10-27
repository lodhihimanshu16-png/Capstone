using Harbourline.Api.Dtos;
using Harbourline.Api.Imports;
using Harbourline.Api.Repositories;
using Harbourline.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Harbourline.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", service = "harbourline-api" });
}

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserService _users;
    public AuthController(IUserService users) => _users = users;

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request) => Ok(await _users.LoginAsync(request));
}

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;
    public UsersController(IUserService users) => _users = users;

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> List() => Ok(await _users.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> Get(int id) => Ok(await _users.GetAsync(id));

    [HttpPut("{id:int}/active")]
    public async Task<ActionResult<UserDto>> SetActive(int id, SetActiveRequest request) => Ok(await _users.SetActiveAsync(id, request.Active));
}

[ApiController]
[Route("api/regions")]
public class RegionsController : ControllerBase
{
    private readonly IRegionService _regions;
    public RegionsController(IRegionService regions) => _regions = regions;

    [HttpGet]
    public async Task<ActionResult<List<RegionDto>>> List() => Ok(await _regions.ListAsync());
}

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _products;
    public ProductsController(IProductService products) => _products = products;

    [HttpGet]
    public async Task<ActionResult<List<ProductDto>>> List([FromQuery] string? line, [FromQuery] bool? active) => Ok(await _products.ListAsync(line, active));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> Get(int id) => Ok(await _products.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(ProductRequest request)
    {
        var created = await _products.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductDto>> Update(int id, ProductRequest request) => Ok(await _products.UpdateAsync(id, request));
}

[ApiController]
[Route("api/handlers")]
public class HandlersController : ControllerBase
{
    private readonly IHandlerService _handlers;
    public HandlersController(IHandlerService handlers) => _handlers = handlers;

    [HttpGet]
    public async Task<ActionResult<List<HandlerDto>>> List([FromQuery] int? regionId, [FromQuery] bool? active) => Ok(await _handlers.ListAsync(regionId, active));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HandlerDto>> Get(int id) => Ok(await _handlers.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<HandlerDto>> Create(HandlerRequest request)
    {
        var created = await _handlers.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<HandlerDto>> Update(int id, HandlerRequest request) => Ok(await _handlers.UpdateAsync(id, request));

    [HttpGet("{id:int}/caseload")]
    public async Task<ActionResult<CaseloadDto>> Caseload(int id) => Ok(await _handlers.GetCaseloadAsync(id));
}

[ApiController]
[Route("api/handler-grades")]
public class HandlerGradesController : ControllerBase
{
    private readonly IHandlerService _handlers;
    public HandlerGradesController(IHandlerService handlers) => _handlers = handlers;

    [HttpGet]
    public async Task<ActionResult<List<HandlerGradeDto>>> List() => Ok(await _handlers.ListGradesAsync());
}

[ApiController]
[Route("api/policyholders")]
public class PolicyholdersController : ControllerBase
{
    private readonly IPolicyholderService _holders;
    private readonly IPolicyService _policies;
    private readonly IClaimService _claims;

    public PolicyholdersController(IPolicyholderService holders, IPolicyService policies, IClaimService claims)
    {
        _holders = holders;
        _policies = policies;
        _claims = claims;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PolicyholderDto>>> List([FromQuery] string? search, [FromQuery] int? regionId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50) => Ok(await _holders.ListAsync(search, regionId, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyholderDto>> Get(int id) => Ok(await _holders.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<PolicyholderDto>> Create(PolicyholderRequest request)
    {
        var created = await _holders.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PolicyholderDto>> Update(int id, PolicyholderRequest request) => Ok(await _holders.UpdateAsync(id, request));

    [HttpGet("{id:int}/policies")]
    public async Task<ActionResult<List<PolicyDto>>> Policies(int id)
    {
        await _holders.GetAsync(id);
        return Ok(await _policies.ListForHolderAsync(id));
    }

    [HttpGet("{id:int}/claims")]
    public async Task<ActionResult<PagedResult<ClaimListItemDto>>> Claims(int id)
    {
        await _holders.GetAsync(id);
        return Ok(await _claims.ListAsync(new ClaimFilter { PolicyholderId = id, PageSize = 100 }));
    }
}

[ApiController]
[Route("api/policies")]
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policies;
    private readonly IClaimService _claims;

    public PoliciesController(IPolicyService policies, IClaimService claims)
    {
        _policies = policies;
        _claims = claims;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PolicyDto>>> List([FromQuery] string? status, [FromQuery] int? productId, [FromQuery] int? regionId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50) => Ok(await _policies.ListAsync(status, productId, regionId, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PolicyDto>> Get(int id) => Ok(await _policies.GetAsync(id));

    [HttpPost]
    public async Task<ActionResult<PolicyDto>> Create(PolicyRequest request)
    {
        var created = await _policies.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PolicyDto>> Update(int id, PolicyRequest request) => Ok(await _policies.UpdateAsync(id, request));

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<PolicyDto>> Cancel(int id) => Ok(await _policies.CancelAsync(id));

    [HttpGet("{id:int}/renewal-quote")]
    public async Task<ActionResult<RenewalQuoteDto>> RenewalQuote(int id) => Ok(await _policies.GetRenewalQuoteAsync(id));

    [HttpGet("{id:int}/claims")]
    public async Task<ActionResult<PagedResult<ClaimListItemDto>>> Claims(int id)
    {
        await _policies.GetAsync(id);
        return Ok(await _claims.ListAsync(new ClaimFilter { PolicyId = id, PageSize = 100 }));
    }
}

[ApiController]
[Route("api/claims")]
public class ClaimsController : ControllerBase
{
    private readonly IClaimService _claims;
    public ClaimsController(IClaimService claims) => _claims = claims;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ClaimListItemDto>>> List([FromQuery] string? status, [FromQuery] int? handlerId, [FromQuery] int? regionId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(await _claims.ListAsync(new ClaimFilter { Status = status, HandlerId = handlerId, RegionId = regionId, Page = page, PageSize = pageSize }));

    [HttpGet("queue")]
    public async Task<ActionResult<PagedResult<QueueItemDto>>> Queue([FromQuery] int? handlerId, [FromQuery] int? regionId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25) => Ok(await _claims.QueueAsync(handlerId, regionId, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDto>> Get(int id) => Ok(await _claims.GetAsync(id));

    [HttpGet("{id:int}/next-actions")]
    public async Task<ActionResult<NextActionsDto>> NextActions(int id, [FromQuery] int? actingUserId) => Ok(await _claims.GetNextActionsAsync(id, actingUserId));

    [HttpPost]
    public async Task<ActionResult<ClaimDto>> Create(CreateClaimRequest request, [FromHeader(Name = "X-User-Id")] int userId = 1)
    {
        var created = await _claims.CreateAsync(request, userId);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ClaimDto>> Update(int id, UpdateClaimRequest request) => Ok(await _claims.UpdateAsync(id, request));

    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<ClaimDto>> ChangeStatus(int id, ChangeStatusRequest request) => Ok(await _claims.ChangeStatusAsync(id, request));

    [HttpPost("{id:int}/assign")]
    public async Task<ActionResult<ClaimDto>> Assign(int id, AssignClaimRequest request) => Ok(await _claims.AssignAsync(id, request));

    [HttpPost("{id:int}/reopen")]
    public async Task<ActionResult<ClaimDto>> Reopen(int id, ReopenClaimRequest request) => Ok(await _claims.ReopenAsync(id, request));

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<List<ClaimHistoryDto>>> History(int id) => Ok(await _claims.GetHistoryAsync(id));
}

[ApiController]
[Route("api")]
public class ClaimNotesController : ControllerBase
{
    private readonly IClaimNoteService _notes;
    public ClaimNotesController(IClaimNoteService notes) => _notes = notes;

    [HttpGet("claims/{claimId:int}/notes")]
    public async Task<ActionResult<List<ClaimNoteDto>>> List(int claimId) => Ok(await _notes.ListAsync(claimId));

    [HttpPost("claims/{claimId:int}/notes")]
    public async Task<ActionResult<ClaimNoteDto>> Add(int claimId, CreateNoteRequest request)
    {
        var note = await _notes.AddAsync(claimId, request);
        return CreatedAtAction(nameof(List), new { claimId }, note);
    }

    [HttpDelete("notes/{noteId:int}")]
    public async Task<IActionResult> Delete(int noteId)
    {
        await _notes.DeleteAsync(noteId);
        return NoContent();
    }
}

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;
    public ReportsController(IReportService reports) => _reports = reports;

    [HttpGet("loss-ratio")]
    public async Task<ActionResult<List<LossRatioRow>>> LossRatio([FromQuery] DateOnly? from, [FromQuery] DateOnly? to) => Ok(await _reports.LossRatioAsync(from, to));

    [HttpGet("claims-ageing")]
    public async Task<ActionResult<List<AgeingRow>>> Ageing() => Ok(await _reports.AgeingAsync());

    [HttpGet("handler-workload")]
    public async Task<ActionResult<List<HandlerWorkloadRow>>> Workload() => Ok(await _reports.WorkloadAsync());

    [HttpGet("limits-breaches")]
    public async Task<ActionResult<List<LimitBreachDto>>> LimitsBreaches([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? regionId) =>
        Ok(await _reports.LimitsBreachesAsync(from, to, regionId));

    [HttpGet("claims-summary")]
    public async Task<ActionResult<List<ClaimsSummaryRow>>> Summary() => Ok(await _reports.SummaryAsync());
}

[ApiController]
[Route("api/imports")]
public class ImportsController : ControllerBase
{
    private readonly IBordereauImportService _import;
    private readonly IImportBatchService _batches;

    public ImportsController(IBordereauImportService import, IImportBatchService batches)
    {
        _import = import;
        _batches = batches;
    }

    [HttpPost("bordereau")]
    public async Task<ActionResult<ImportResultDto>> Import(IFormFile file, [FromForm] string source, [FromHeader(Name = "X-User-Id")] int userId = 1)
    {
        if (file == null || file.Length == 0) return BadRequest(new ProblemDetails { Title = "A CSV file is required.", Status = 400 });
        await using var stream = file.OpenReadStream();
        return Ok(await _import.ImportAsync(stream, file.FileName, string.IsNullOrWhiteSpace(source) ? "Unknown" : source, userId));
    }

    [HttpGet]
    public async Task<ActionResult<List<ImportBatchDto>>> List() => Ok(await _batches.ListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ImportBatchDto>> Get(int id) => Ok(await _batches.GetAsync(id));
}
