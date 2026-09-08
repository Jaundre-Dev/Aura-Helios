using Helios.Api.Middleware;
using Helios.Application.Features.Workspaces;
using Helios.Contracts.Workspaces;

namespace Helios.Api.Endpoints;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces")
            .WithTags("Workspaces");

        group.MapGet("/", async (WorkspaceService service, CancellationToken ct) =>
                Results.Ok(await service.ListMineAsync(ct)))
            .WithName("ListWorkspaces")
            .WithSummary("Every workspace the caller belongs to.");

        group.MapGet("/{id:guid}", async (Guid id, WorkspaceService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } workspace
                    ? Results.Ok(workspace)
                    : Results.NotFound())
            .WithName("GetWorkspace")
            .Produces<WorkspaceResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
                CreateWorkspaceRequest request,
                WorkspaceService service,
                CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);

                return Results.Created($"/api/v1/workspaces/{created.Id}", created);
            })
            .WithName("CreateWorkspace")
            .WithSummary("Creates a workspace and makes the caller its Owner.")
            .WithValidation<CreateWorkspaceRequest>()
            .Produces<WorkspaceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
                Guid id,
                UpdateWorkspaceRequest request,
                WorkspaceService service,
                CancellationToken ct) =>
                Results.Ok(await service.UpdateAsync(id, request, ct)))
            .WithName("UpdateWorkspace")
            .WithValidation<UpdateWorkspaceRequest>()
            .Produces<WorkspaceResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}/members", async (
                Guid id,
                WorkspaceService service,
                CancellationToken ct) =>
                Results.Ok(await service.ListMembersAsync(id, ct)))
            .WithName("ListWorkspaceMembers");

        group.MapPost("/{id:guid}/members", async (
                Guid id,
                AddWorkspaceMemberRequest request,
                WorkspaceService service,
                CancellationToken ct) =>
            {
                var member = await service.AddMemberAsync(id, request, ct);

                return Results.Created($"/api/v1/workspaces/{id}/members/{member.Id}", member);
            })
            .WithName("AddWorkspaceMember")
            .WithSummary("Grants a user access. Cannot grant a role above the caller's own.")
            .WithValidation<AddWorkspaceMemberRequest>()
            .Produces<WorkspaceMemberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
