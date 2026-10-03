using FluentValidation;
using Helios.Application.Features.ApiKeys;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Catalogue;
using Helios.Application.Features.Identity;
using Helios.Application.Features.Products;
using Helios.Application.Features.Products.Identity;
using Helios.Application.Features.Projects;
using Helios.Application.Features.Requests;
using Helios.Application.Features.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddHeliosApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateWorkspaceRequestValidator>(
            ServiceLifetime.Singleton);

        services.AddScoped<OrganizationAccess>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<ProjectService>();

        services.AddScoped<CatalogueService>();
        services.AddScoped<EntitlementService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<ProductRequestService>();
        services.AddScoped<BillingProfileService>();

        // First-party product executors. Each callable catalogue product needs exactly one here.
        services.AddSingleton<IProductExecutor, SaIdValidateExecutor>();
        services.AddSingleton<ProductExecutorRegistry>();

        return services;
    }
}
