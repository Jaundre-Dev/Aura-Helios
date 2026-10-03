using System.Text;
using Helios.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Helios.Api.Security;

public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// JWT bearer authentication plus the token issuer. Claims are validated as
    /// <c>sub</c>/<c>workspace_id</c>/<c>role</c> verbatim — <see cref="JwtBearerOptions.MapInboundClaims"/>
    /// is off so ASP.NET does not rewrite <c>sub</c> to the long WS-Federation URI that
    /// <see cref="ClaimsPrincipalExtensions"/> would then fail to find.
    /// </summary>
    public static IServiceCollection AddHeliosAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<JwtTokenIssuer>();

        // One entry scheme picks the real handler per request: an hk_… API key goes to the key
        // handler, anything else to JWT bearer. Neither handler sees the other's credentials.
        services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, "JWT or API key", policy =>
                policy.ForwardDefaultSelector = context =>
                    ApiKeyAuthenticationHandler.ReadPresentedKey(context.Request) is not null
                        ? ApiKeyAuthenticationHandler.SchemeName
                        : JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });

        // Configured lazily against the validated JwtOptions rather than re-reading config,
        // so the signing key has one source of truth and one validation.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var options = jwt.Value;

                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = HeliosClaims.Subject,
                    RoleClaimType = HeliosClaims.Role,
                };

                // Signature and lifetime are necessary, not sufficient: confirm the account,
                // session and workspace grant are still live before the request proceeds.
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var validator = context.HttpContext.RequestServices.GetRequiredService<SessionValidator>();
                        var problem = await validator.FindProblemAsync(
                            context.Principal!, context.HttpContext.RequestAborted);

                        if (problem is not null)
                        {
                            context.Fail(problem);
                        }
                    }
                };
            });

        services.AddScoped<SessionValidator>();

        services.AddAuthorization(authorization =>
        {
            // Portal and management endpoints: a signed-in person. An API key is authenticated
            // but has no subject, so it is refused (403) here rather than reaching management.
            authorization.DefaultPolicy = new AuthorizationPolicyBuilder(SelectorScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(HeliosClaims.Subject)
                .Build();

            // Product execution and request reads: a signed-in person or an API key.
            authorization.AddPolicy(ProductCallerPolicy, policy => policy
                .AddAuthenticationSchemes(SelectorScheme)
                .RequireAuthenticatedUser());
        });

        return services;
    }

    private const string SelectorScheme = HeliosAuthPolicies.SelectorScheme;
    private const string ProductCallerPolicy = HeliosAuthPolicies.ProductCaller;
}

/// <summary>Authentication scheme and policy names endpoints refer to.</summary>
public static class HeliosAuthPolicies
{
    /// <summary>Forwards to the API-key or JWT handler depending on the presented credential.</summary>
    public const string SelectorScheme = "Helios";

    /// <summary>A signed-in person or an API key: product execution and request reads.</summary>
    public const string ProductCaller = "product-caller";
}
