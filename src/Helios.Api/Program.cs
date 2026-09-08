using Helios.Api.Configuration;
using Helios.Api.Endpoints;
using Helios.Api.Middleware;
using Helios.Api.Hubs;
using Helios.Api.Security;
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
builder.Services.AddHeliosAuthentication(builder.Configuration);
builder.Services.AddHeliosApplication();

builder.Services.AddCors(options =>
{
    options.AddPolicy("helios-web", policy => policy
        .WithOrigins(builder.Configuration["Helios:WebUrl"] ?? "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseCors("helios-web");

app.UseAuthentication();
app.UseAuthorization();

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

// Hubs require a valid token. A browser sends it in the query string on the websocket
// handshake, which JwtBearerEvents.OnMessageReceived accepts for /hubs paths only.
app.MapHub<WorkspaceHub>(HubRoutes.Workspace).RequireAuthorization();
app.MapHub<AgentHub>(HubRoutes.Agent).RequireAuthorization();
app.MapHub<ProjectHub>(HubRoutes.Project).RequireAuthorization();
app.MapHub<NotificationHub>(HubRoutes.Notification).RequireAuthorization();
app.MapHub<WorkflowHub>(HubRoutes.Workflow).RequireAuthorization();
app.MapHub<MonitoringHub>(HubRoutes.Monitoring).RequireAuthorization();

app.MapAuthEndpoints();
app.MapOrganizationEndpoints();
app.MapWorkspaceEndpoints();
app.MapProjectEndpoints();

app.Run();

/// <summary>
/// Exposed so WebApplicationFactory can boot the real host in integration tests.
/// </summary>
public partial class Program;
