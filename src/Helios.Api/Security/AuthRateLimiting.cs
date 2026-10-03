using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Helios.Api.Security;

/// <summary>Limits for the anonymous sign-in surface, bound from <c>Helios:RateLimits:Auth</c>.</summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "Helios:RateLimits:Auth";

    /// <summary>Requests to register/login allowed per client address per window.</summary>
    [Range(1, 100_000)]
    public int PermitLimit { get; init; } = 20;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}

/// <summary>
/// Per-address throttling for registration and sign-in. Account lockout stops guessing against
/// one account; this stops one client cycling through many accounts or flooding registration.
/// Behind a reverse proxy, configure forwarded headers so the client address is the real one.
/// </summary>
public static class AuthRateLimiting
{
    public const string PolicyName = "auth";

    public static IServiceCollection AddHeliosRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthRateLimitOptions>()
            .Bind(configuration.GetSection(AuthRateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(PolicyName, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                var partition = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
        });

        return services;
    }
}
