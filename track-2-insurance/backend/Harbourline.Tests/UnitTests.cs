using System.Net.Http.Json;
using FluentValidation;
using Harbourline.Api.Domain;
using Harbourline.Api.Dtos;
using Harbourline.Api.Infrastructure;
using Harbourline.Api.Repositories;
using Harbourline.Api.Services;
using Harbourline.Api.Validators;
using Moq;

namespace Harbourline.Tests;

public class ProductServiceTests
{
    private static ProductRequest Request(string code = "HOME-NEW") => new(code, "New Home", "Home", 400m, 200m, true);

    [Fact]
    public async Task Create_rejects_duplicate_code()
    {
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.GetByCodeAsync("HOME-NEW")).ReturnsAsync(new Product { Id = 9, Code = "HOME-NEW" });
        var service = new ProductService(repo.Object, new ProductRequestValidator());

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Request()));
    }

    [Fact]
    public async Task Create_normalises_code_to_upper_case()
    {
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<Product>())).ReturnsAsync((Product p) => p);
        var service = new ProductService(repo.Object, new ProductRequestValidator());

        var created = await service.CreateAsync(Request("home-new"));

        Assert.Equal("HOME-NEW", created.Code);
    }
}

public class PolicyPricingTests
{
    [Fact]
    // The cap in PolicyPricing.NoClaimsDiscount is 30 percent (6 years x 5 percent). The old test was named "20 percent"
    // and asserted "<= 0.30", which would have passed for any discount at all.
    public void Renewal_discount_is_capped_at_30_percent()
    {
        Assert.Equal(0.30m, PolicyPricing.NoClaimsDiscount(9));   // above the cap: exactly the cap
        Assert.Equal(0.25m, PolicyPricing.NoClaimsDiscount(5));   // below the cap: 5 years x 5 percent
    }

    [Fact]
    public void Renewal_premium_applies_the_discount()
    {
        Assert.Equal(900m, PolicyPricing.RenewalPremium(1000m, 2));
    }
}

public class ClaimRulesTests
{
    // ASSUMPTION to be reconciled with the backend: the clock-based signature is
    // ClaimRules.IsReserveReviewDue(DateTime lastNoteAtUtc, DateTime nowUtc). The old form read DateTime.Now, so
    // these tests passed or failed depending on the day they ran. All dates here are fixed.
    private static readonly DateTime Now = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Reserve_review_is_due_after_one_month()
    {
        var lastNote = new DateTime(2026, 9, 20, 15, 0, 0, DateTimeKind.Utc);   // one calendar month before Now

        Assert.True(ClaimRules.IsReserveReviewDue(lastNote, Now));
    }

    [Fact]
    public void Reserve_review_is_not_due_one_day_before_the_month_is_up()
    {
        var lastNote = new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);

        Assert.False(ClaimRules.IsReserveReviewDue(lastNote, Now));
    }

    [Fact]
    public void Reserve_review_is_not_due_for_a_note_made_today()
    {
        Assert.False(ClaimRules.IsReserveReviewDue(Now, Now));
    }

    [Fact]
    public void Claim_number_is_zero_padded()
    {
        Assert.Equal("HLC-26-00042", ClaimRules.NextClaimNumber(42));
    }
}

public class LossRatioCalculatorTests
{
    [Fact]
    public void Zero_premium_gives_zero_ratio()
    {
        Assert.Equal(0m, LossRatioCalculator.Percent(5000m, 0m));
    }

    [Fact]
    public void Ratio_is_paid_over_premium_as_a_percentage()
    {
        Assert.Equal(62.5m, LossRatioCalculator.Percent(625m, 1000m));
    }
}

public class ValidatorTests
{
    [Fact]
    public void CreateClaim_rejects_empty_description()
    {
        var result = new CreateClaimRequestValidator().Validate(new CreateClaimRequest(1, "Theft", new DateOnly(2026, 5, 1), "", 500m));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateClaim_rejects_zero_amount()
    {
        var result = new CreateClaimRequestValidator().Validate(new CreateClaimRequest(1, "Theft", new DateOnly(2026, 5, 1), "Bike stolen", 0m));

        Assert.False(result.IsValid);
    }
}

public class ClaimServiceTests
{
    // These three tests used to build ClaimService with Moq repositories, which tied them to its constructor. The
    // constructor changes with the claims workflow, so the same intent is now checked through HTTP (ApiFactory).
    // Original behaviour kept: BusinessRuleException maps to 422.

    [Fact]
    public async Task Assign_rejects_inactive_handler()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => s.Status = "New");

        var response = await c.PostAsJsonAsync($"/api/claims/{id}/assign", new { handlerId = Ids.HalInactiveHandler });

        await response.AssertProblem(System.Net.HttpStatusCode.UnprocessableEntity);
        Assert.True((await c.Claim(id)).IsNull("handlerId"), "the claim must not be given to an inactive handler");
    }

    [Fact]
    public async Task Reopen_is_refused_for_a_claim_that_is_still_open()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Assessing"; s.HandlerId = Ids.Ben; });

        var response = await c.Reopen(id, Ids.Carl, "New evidence");

        await response.AssertProblem(System.Net.HttpStatusCode.UnprocessableEntity);
        await c.AssertStatus(id, "Assessing");
    }

    [Fact]
    public async Task Reopen_increments_the_reopened_count()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Settled"; s.HandlerId = Ids.Ben; s.ReopenedCount = 1; s.Approved = 5000m; s.Paid = 4750m; s.Settlement = 4750m; });

        var claim = await (await c.Reopen(id, Ids.Carl, "Additional damage found")).AssertOk();

        Assert.Equal(2, claim.Int("reopenedCount"));
        Assert.Equal("Reopened", claim.Str("status"));
        Assert.Equal(2, (await c.Claim(id)).Int("reopenedCount"));
    }
}
