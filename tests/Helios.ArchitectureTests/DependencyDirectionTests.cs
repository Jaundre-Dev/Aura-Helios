using System.Reflection;
using Helios.Application.Abstractions.Ai;
using Helios.Contracts.Common;
using Helios.Domain.Common;

namespace Helios.ArchitectureTests;

/// <summary>
/// The layering in docs/architecture/overview.md is enforced here rather than
/// remembered. A modular monolith only stays modular if something fails the build.
/// </summary>
public class DependencyDirectionTests
{
    private static readonly Assembly Contracts = typeof(DataClassification).Assembly;
    private static readonly Assembly Domain = typeof(Entity).Assembly;
    private static readonly Assembly Application = typeof(IModelGateway).Assembly;

    private static IEnumerable<string> HeliosReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Helios.", StringComparison.Ordinal));

    [Fact]
    public void Contracts_references_no_other_helios_project()
    {
        Assert.Empty(HeliosReferencesOf(Contracts));
    }

    [Fact]
    public void Domain_references_only_contracts()
    {
        Assert.All(
            HeliosReferencesOf(Domain),
            reference => Assert.Equal("Helios.Contracts", reference));
    }

    [Fact]
    public void Application_does_not_reference_infrastructure()
    {
        Assert.DoesNotContain("Helios.Infrastructure", HeliosReferencesOf(Application));
    }

    [Fact]
    public void Application_and_domain_reference_no_provider_sdk()
    {
        string[] forbidden = ["OpenAI", "Anthropic", "Azure.AI", "Google.Cloud.AIPlatform", "Ollama"];

        foreach (var assembly in new[] { Domain, Application })
        {
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

            Assert.DoesNotContain(
                referenced,
                name => forbidden.Any(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
