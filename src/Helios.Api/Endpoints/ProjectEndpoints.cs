using Helios.Api.Middleware;
using Helios.Application.Features.Projects;
using Helios.Contracts.Projects;

namespace Helios.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        group.MapGet("/", async (
                bool? includeInactive,
                ProjectService service,
                CancellationToken ct) =>
                Results.Ok(await service.ListAsync(includeInactive ?? false, ct)))
            .WithName("ListProjects")
            .WithSummary("Projects in the caller's current workspace.");

        group.MapGet("/{id:guid}", async (Guid id, ProjectService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } project
                    ? Results.Ok(project)
                    : Results.NotFound())
            .WithName("GetProject")
            .Produces<ProjectResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
                CreateProjectRequest request,
                ProjectService service,
                CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);

                return Results.Created($"/api/v1/projects/{created.Id}", created);
            })
            .WithName("CreateProject")
            .WithValidation<CreateProjectRequest>()
            .Produces<ProjectResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
                Guid id,
                UpdateProjectRequest request,
                ProjectService service,
                CancellationToken ct) =>
                Results.Ok(await service.UpdateAsync(id, request, ct)))
            .WithName("UpdateProject")
            .WithSummary("Lowering Classification widens where this data may be sent and requires Admin.")
            .WithValidation<UpdateProjectRequest>()
            .Produces<ProjectResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}", async (Guid id, ProjectService service, CancellationToken ct) =>
            {
                await service.DeactivateAsync(id, ct);

                return Results.NoContent();
            })
            .WithName("DeactivateProject")
            .WithSummary("Deactivates the project. Runs and findings reference it, so nothing is hard deleted.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
