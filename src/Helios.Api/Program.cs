using Helios.Api.Configuration;
using Helios.Api.Endpoints;
using Helios.Api.Middleware;
using Helios.Api.Hubs;
using Helios.Application.Abstractions.Security;
using Helios.Application.DependencyInjection;
using Helios.Contracts.Realtime;
using Helios.Infrastructure.DependencyInjection;
using Helios.Infrastructure.Persistence.MySql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<HeliosExceptionHandler>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IWorkspaceContext, HttpWorkspaceContext>();

builder.Services.AddHeliosPersistence(builder.Configuration);
builder.Services.AddHeliosIdentity();
builder.Services.AddHeliosApplication();

builder.Services.AddCors(options =>
{
    options.AddPolicy("helios-web", policy => policy
        .WithOrigins(builder.Configuration["Helios:WebUrl"] ?? "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// TODO WP0.4: JWT bearer, authorization policies, and the workspace claim.

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseCors("helios-web");

// Liveness: the process is up. Deliberately touches nothing else, so a database outage
// never causes the orchestrator to kill an otherwise healthy container.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "helios-api" }))
   .WithName("Health");

// Readiness: the dependencies this instance needs to serve traffic. Gate 0 check 5.
app.MapGet("/health/ready", async (HeliosDbContext db, CancellationToken ct) =>
{
    var mysql = await db.Database.CanConnectAsync(ct);

    return mysql
        ? Results.Ok(new { status = "ready", mysql = "up" })
        : Results.Json(new { status = "not-ready", mysql = "down" }, statusCode: 503);
})
.WithName("Readiness");

app.MapHub<WorkspaceHub>(HubRoutes.Workspace);
app.MapHub<AgentHub>(HubRoutes.Agent);
app.MapHub<ProjectHub>(HubRoutes.Project);
app.MapHub<NotificationHub>(HubRoutes.Notification);
app.MapHub<WorkflowHub>(HubRoutes.Workflow);
app.MapHub<MonitoringHub>(HubRoutes.Monitoring);

app.MapOrganizationEndpoints();
app.MapWorkspaceEndpoints();
app.MapProjectEndpoints();

app.Run();

/// <summary>
/// Exposed so WebApplicationFactory can boot the real host in integration tests.
/// </summary>
public partial class Program;
