using Helios.Api.Configuration;
using Helios.Api.Endpoints;
using Helios.Api.Middleware;
using Helios.Api.Security;
using Helios.Application.Abstractions.Security;
using Helios.Application.DependencyInjection;
using Helios.Infrastructure.DependencyInjection;
using Helios.Infrastructure.Persistence.MySql;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Every error response carries the request id, so a customer can quote it to support and it
// lines up with the audit trail's correlation id.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["requestId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<HeliosExceptionHandler>();
builder.Services.AddHeliosRateLimiting(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IWorkspaceContext, HttpWorkspaceContext>();

builder.Services.AddHeliosPersistence(builder.Configuration);
builder.Services.AddHeliosIdentity();
builder.Services.AddHeliosAuthentication(builder.Configuration);
builder.Services.AddHeliosApplication();

builder.Services.AddHeliosExecution(builder.Configuration);

// Trust X-Forwarded-For only from explicitly listed reverse proxies. Without this the rate
// limiter and audit trail see the proxy's address; trusting any sender would let a client
// forge its own address and dodge throttling.
var knownProxies = builder.Configuration.GetSection("Helios:ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in knownProxies.Where(p => !string.IsNullOrWhiteSpace(p)))
    {
        options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    }
});

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

app.UseForwardedHeaders();
app.UseMiddleware<RequestCorrelationMiddleware>();
app.UseExceptionHandler();
app.UseCors("helios-web");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

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

app.MapAuthEndpoints();
app.MapOrganizationEndpoints();
app.MapWorkspaceEndpoints();
app.MapProjectEndpoints();
app.MapPlatformEndpoints();

app.Run();

/// <summary>
/// Exposed so WebApplicationFactory can boot the real host in integration tests.
/// </summary>
public partial class Program;
