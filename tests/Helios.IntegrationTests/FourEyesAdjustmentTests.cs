using System.Net;
using System.Net.Http.Json;
using Helios.Application.Features.Billing;
using Helios.Contracts.Billing;
using Helios.Contracts.Platform;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// Credit adjustments above the threshold (R5 000 by default) wait for a second staff member: the
/// requester can never approve their own, nothing is posted before approval, and a decision is final.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class FourEyesAdjustmentTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions.TryGetValue("code", out var code) == true
            ? code?.ToString()
            : null;

    private async Task<BalanceResponse> BalanceAsync(Guid organizationId)
    {
        using var scope = _factory.CreateSystemScope();
        return await scope.ServiceProvider.GetRequiredService<LedgerService>().GetBalanceAsync(organizationId, CancellationToken.None);
    }

    [Fact]
    public async Task A_large_credit_waits_for_a_second_person_and_is_posted_once()
    {
        var requester = await TestStaff.CreateAsync(_factory, PlatformRole.Finance);
        var approver = await TestStaff.CreateAsync(_factory, PlatformRole.Finance);
        var company = await TestCompany.OnboardAsync(_factory);
        var url = $"/api/v1/platform/organizations/{company.Organization.Id}/adjustments";

        var requested = await requester.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(10_000m, "Pilot prepayment received by EFT", "eft-0001"));
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var pending = (await requested.Content.ReadFromJsonAsync<CreditAdjustmentResponse>())!;
        Assert.False(pending.Posted);
        Assert.NotNull(pending.ApprovalId);
        Assert.Equal(0m, (await BalanceAsync(company.Organization.Id)).Available);

        // Repeating is harmless; reusing the reference for another amount is not.
        var repeat = await (await requester.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(10_000m, "Pilot prepayment received by EFT", "eft-0001")))
            .Content.ReadFromJsonAsync<CreditAdjustmentResponse>();
        Assert.Equal(pending.ApprovalId, repeat!.ApprovalId);
        Assert.Equal("reference_reused", await CodeOf(await requester.Platform.PostAsJsonAsync(url,
            new CreditAdjustmentRequest(20m, "Small one under the same reference", "eft-0001"))));

        var queue = await approver.Platform.GetFromJsonAsync<List<AdjustmentApprovalResponse>>("/api/v1/platform/adjustments/pending");
        Assert.Contains(queue!, a => a.Id == pending.ApprovalId && a.RequestedBy == requester.Account.UserId);

        var decide = $"/api/v1/platform/adjustments/{pending.ApprovalId}/decision";
        var self = await requester.Platform.PostAsJsonAsync(decide, new DecideAdjustmentRequest(ApprovalDecision.Approve, "Approving my own"));
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Equal("four_eyes_required", await CodeOf(self));

        var approved = await approver.Platform.PostAsJsonAsync(decide, new DecideAdjustmentRequest(ApprovalDecision.Approve, "Matched to bank statement line"));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Approved", (await approved.Content.ReadFromJsonAsync<AdjustmentApprovalResponse>())!.State);

        Assert.Equal(HttpStatusCode.Conflict, (await approver.Platform.PostAsJsonAsync(decide,
            new DecideAdjustmentRequest(ApprovalDecision.Approve, "Again"))).StatusCode);

        Assert.Equal(10_000m, (await BalanceAsync(company.Organization.Id)).Available);

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var postings = await db.LedgerTransactions.Where(t => t.OrganizationId == company.Organization.Id && t.Type == LedgerTransactionType.Adjustment).ToListAsync();
        var posting = Assert.Single(postings);
        Assert.Equal(approver.Account.UserId, posting.CreatedBy);
    }

    [Fact]
    public async Task A_rejection_posts_nothing_and_an_overdrawing_approval_changes_nothing()
    {
        var requester = await TestStaff.CreateAsync(_factory, PlatformRole.Finance);
        var approver = await TestStaff.CreateAsync(_factory, PlatformRole.Administrator);
        var company = await TestCompany.OnboardAsync(_factory);
        var url = $"/api/v1/platform/organizations/{company.Organization.Id}/adjustments";

        var credit = (await (await requester.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(6_000m, "Duplicate of an earlier credit", "dup-1")))
            .Content.ReadFromJsonAsync<CreditAdjustmentResponse>())!;
        var rejected = await approver.Platform.PostAsJsonAsync($"/api/v1/platform/adjustments/{credit.ApprovalId}/decision",
            new DecideAdjustmentRequest(ApprovalDecision.Reject, "Already credited under eft-0007"));
        Assert.Equal("Rejected", (await rejected.Content.ReadFromJsonAsync<AdjustmentApprovalResponse>())!.State);

        var debit = (await (await requester.Platform.PostAsJsonAsync(url, new CreditAdjustmentRequest(-7_000m, "Reverse an erroneous credit", "rev-1")))
            .Content.ReadFromJsonAsync<CreditAdjustmentResponse>())!;
        var overdraw = await approver.Platform.PostAsJsonAsync($"/api/v1/platform/adjustments/{debit.ApprovalId}/decision",
            new DecideAdjustmentRequest(ApprovalDecision.Approve, "Approve the reversal"));
        Assert.Equal(HttpStatusCode.PaymentRequired, overdraw.StatusCode);

        var queue = await approver.Platform.GetFromJsonAsync<List<AdjustmentApprovalResponse>>("/api/v1/platform/adjustments/pending");
        Assert.Contains(queue!, a => a.Id == debit.ApprovalId && a.State == "Pending");
        Assert.Equal(new BalanceResponse("ZAR", 0m, 0m, 0m), await BalanceAsync(company.Organization.Id));
    }
}
