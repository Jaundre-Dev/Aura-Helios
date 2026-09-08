using FluentValidation;
using Helios.Application.Features.Identity;
using Helios.Application.Features.Projects;
using Helios.Application.Features.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddHeliosApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateWorkspaceRequestValidator>(
            ServiceLifetime.Singleton);

        services.AddScoped<OrganizationService>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<ProjectService>();

        return services;
    }
}
