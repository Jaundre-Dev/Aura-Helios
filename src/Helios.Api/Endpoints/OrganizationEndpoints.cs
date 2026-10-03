using Helios.Application.Features.Agreements;
using Helios.Api.Middleware;
using Helios.Application.Features.Identity;
using Helios.Contracts.Organizations;

namespace Helios.Api.Endpoints;

/// <summary>
/// Customer companies and their teams. Every route is membership-checked in
/// <see cref="OrganizationService"/>; a caller who is not an active member gets 404.
/// </summary>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations")
            .WithTags("Organizations")
            .RequireAuthorization();

        group.MapGet("/", async (OrganizationService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .WithName("ListOrganizations")
            .WithSummary("Companies the caller is an active member of.");

        group.MapGet("/{id:guid}", async (Guid id, OrganizationService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } organization
                    ? Results.Ok(organization)
                    : Results.NotFound())
            .WithName("GetOrganization")
            .Produces<OrganizationResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
                CreateOrganizationRequest request,
                OrganizationService service,
                CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);

                return Results.Created($"/api/v1/organizations/{created.Id}", created);
            })
            .WithName("CreateOrganization")
            .WithSummary("Creates a company with the caller as Owner and a default workspace.")
            .WithValidation<CreateOrganizationRequest>()
            .Produces<OrganizationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);


        group.MapGet("/{id:guid}/agreements", async (Guid id, AgreementService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(id, ct)))
            .WithName("ListAgreements")
            .WithSummary("The documents live use requires, their current versions and this company's acceptances.")
            .Produces<IReadOnlyList<AgreementStatusResponse>>();

        group.MapPost("/{id:guid}/agreements", async (Guid id, AcceptAgreementRequest request, AgreementService service, CancellationToken ct) =>
                Results.Ok(await service.AcceptAsync(id, request, ct)))
            .WithName("AcceptAgreement")
            .WithSummary("Records the Owner's acceptance of the current version of a document.")
            .WithValidation<AcceptAgreementRequest>()
            .Produces<IReadOnlyList<AgreementStatusResponse>>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}/members", async (Guid id, OrganizationService service, CancellationToken ct) =>
                Results.Ok(await service.ListMembersAsync(id, ct)))
            .WithName("ListOrganizationMembers")
            .Produces<IReadOnlyList<OrganizationMemberResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/members", async (
                Guid id,
                AddOrganizationMemberRequest request,
                OrganizationService service,
                CancellationToken ct) =>
            {
                var member = await service.AddMemberAsync(id, request, ct);

                return Results.Created($"/api/v1/organizations/{id}/members/{member.Id}", member);
            })
            .WithName("AddOrganizationMember")
            .WithSummary("Adds an existing account to the company. Only an Owner can grant Owner.")
            .WithValidation<AddOrganizationMemberRequest>()
            .Produces<OrganizationMemberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}/members/{memberId:guid}", async (
                Guid id,
                Guid memberId,
                UpdateOrganizationMemberRequest request,
                OrganizationService service,
                CancellationToken ct) =>
                Results.Ok(await service.UpdateMemberAsync(id, memberId, request, ct)))
            .WithName("UpdateOrganizationMember")
            .WithValidation<UpdateOrganizationMemberRequest>()
            .Produces<OrganizationMemberResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/members/{memberId:guid}", async (
                Guid id,
                Guid memberId,
                OrganizationService service,
                CancellationToken ct) =>
            {
                await service.RemoveMemberAsync(id, memberId, ct);

                return Results.NoContent();
            })
            .WithName("RemoveOrganizationMember")
            .WithSummary("Removes the member and their workspace access; their existing tokens stop working.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
