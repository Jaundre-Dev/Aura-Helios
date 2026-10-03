using Helios.Application.Features.Products;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Helios.Infrastructure.DependencyInjection;

/// <summary>
/// Refuses to start a Production host carrying test or fake components (plan section 14, P2:
/// "production startup rejects mock configuration"). Runs before the host serves anything.
/// </summary>
public sealed class ProductionSafetyCheck(
    IHostEnvironment environment,
    IConfiguration configuration,
    IEnumerable<IProductExecutor> executors) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsProduction())
        {
            return Task.CompletedTask;
        }

        var problems = FindProblems(configuration, executors).ToList();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Refusing to start in Production:\n  " + string.Join("\n  ", problems));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static IEnumerable<string> FindProblems(IConfiguration configuration, IEnumerable<IProductExecutor> executors)
    {
        foreach (var executor in executors.Where(e => e.ProductSlug.StartsWith("test.", StringComparison.Ordinal)))
        {
            yield return $"Test product executor '{executor.ProductSlug}' is registered.";
        }

        var gateway = configuration["Helios:Payments:Gateway"];
        if (string.Equals(gateway, "fake-test", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Helios:Payments:Gateway is the fake test gateway.";
        }

        if (string.IsNullOrWhiteSpace(configuration["Helios:Uploads:Scanner"]))
        {
            yield return "No malware scanner is configured (Helios:Uploads:Scanner); uploads would go unscanned.";
        }

        if (string.Equals(configuration["Helios:Email:Sender"], "file", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Helios:Email:Sender is the development file drop; emails would never reach customers.";
        }

        if (configuration.GetValue("Helios:Webhooks:AllowPrivateNetworks", false))
        {
            yield return "Helios:Webhooks:AllowPrivateNetworks is enabled (SSRF protection off).";
        }
    }
}
