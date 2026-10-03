using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Helios.Api.Security;

/// <summary>Limits for product execution and uploads, bound from <c>Helios:RateLimits:Products</c>.</summary>
public sealed class ProductRateLimitOptions
{
    public const string SectionName = "Helios:RateLimits:Products";

    /// <summary>Requests per window for one API key, or one signed-in user.</summary>
    [Range(1, 1_000_000)]
    public int PermitLimit { get; init; } = 600;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}

/// <summary>
/// Per-credential throttling of billable surfaces (plan section 13: abuse limits). Partitioned by API
/// key, or by user for portal calls, so one integration's burst cannot starve another's. Credit
/// reservations and monthly caps still apply to whatever gets through.
/// </summary>
public static class ProductRateLimiting
{
    public const string PolicyName = "products";

    public static IServiceCollection AddProductRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProductRateLimitOptions>()
            .Bind(configuration.GetSection(ProductRateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(limiter =>
        {
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            limiter.AddPolicy(PolicyName, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<ProductRateLimitOptions>>().Value;
                var user = context.User;
                var partition = user.FindFirstValue(HeliosClaims.ApiKey) is { } key ? $"key:{key}"
                    : user.FindFirstValue(HeliosClaims.Subject) is { } sub ? $"user:{sub}"
                    : $"ip:{context.Connection.RemoteIpAddress}";

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
