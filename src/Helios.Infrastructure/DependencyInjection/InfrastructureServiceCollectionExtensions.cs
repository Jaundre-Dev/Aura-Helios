using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Infrastructure.Security;
using Helios.Infrastructure.Persistence.MySql;
using Helios.Infrastructure.Persistence.MySql.Identity;
using Helios.Infrastructure.Persistence.MySql.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers persistence. Redis, providers and the rest arrive with the phase that
    /// needs them — see docs/plan/.
    /// </summary>
    public static IServiceCollection AddHeliosPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MySql");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fail at startup rather than on the first request. Secrets are deliberately
            // absent from appsettings.json so a real credential can never be committed.
            throw new InvalidOperationException(
                "No database connection string configured (ConnectionStrings:MySql).\n" +
                "  Local dev:  dotnet user-secrets set \"ConnectionStrings:MySql\" " +
                "\"Server=127.0.0.1;Port=3306;Database=helios;User=helios;Password=...;\" " +
                "--project src/Helios.Api\n" +
                "  Container:  set ConnectionStrings__MySql in the environment.");
        }

        var serverVersion = configuration["Helios:Database:ServerVersion"];

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<HeliosDbContext>((provider, options) =>
        {
            MySqlConfiguration.Configure(options, connectionString, serverVersion);
            options.AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IHeliosDbContext>(sp => sp.GetRequiredService<HeliosDbContext>());
        services.AddScoped<IAuditWriter, AuditWriter>();

        return services;
    }

    /// <summary>
    /// Local ASP.NET Core Identity (ADR-0002 companion decision, plan WP0.4). Claims are
    /// shaped so an OIDC provider can fill them later without touching this registration.
    /// </summary>
    public static IServiceCollection AddHeliosIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<HeliosUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<HeliosRole>()
            .AddEntityFrameworkStores<HeliosDbContext>();

        return services;
    }
}
