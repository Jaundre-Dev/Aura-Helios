using Helios.Application.Abstractions.Execution;
using Helios.Application.Abstractions.Security;
using Helios.Application.DependencyInjection;
using Helios.Application.Features.Execution;
using Helios.Application.Features.Retention;
using Helios.Application.Features.Webhooks;
using Helios.Infrastructure.DependencyInjection;

namespace Helios.Worker;

public static class WorkerServiceCollectionExtensions
{
    /// <summary>Everything the worker process runs. Shared with tests so they exercise the real wiring.</summary>
    public static IServiceCollection AddHeliosWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHeliosPersistence(configuration);
        services.AddHeliosApplication();
        services.AddHeliosExecution(configuration);

        // Each job runs in a scope confined to its own workspace; only claiming uses system scope.
        services.AddScoped<WorkerWorkspaceContext>();
        services.AddScoped<IWorkspaceContext>(sp => sp.GetRequiredService<WorkerWorkspaceContext>());
        services.AddSingleton<ITenantScopeFactory, WorkerTenantScopes>();
        services.AddSingleton<JobWorker>();
        services.AddSingleton<WebhookDispatcher>();
        services.AddSingleton<RetentionSweeper>();

        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));
        services.AddHostedService<JobProcessingService>();

        return services;
    }
}
