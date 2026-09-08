using Helios.Api.Middleware;
using Helios.Application.Features.Identity;
using Helios.Contracts.Organizations;

namespace Helios.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations")
            .WithTags("Organizations")
            .RequireAuthorization();

        group.MapGet("/", async (OrganizationService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .WithName("ListOrganizations");

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
            .WithValidation<CreateOrganizationRequest>()
            .Produces<OrganizationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
