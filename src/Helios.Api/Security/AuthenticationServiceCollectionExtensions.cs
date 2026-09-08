using System.Text;
using Helios.Api.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

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

                bearer.Events = new JwtBearerEvents
                {
                    // A browser cannot set an Authorization header on a websocket, so the
                    // SignalR client sends the token in the query string. Accept it there,
                    // but only for hub paths — never for the REST surface.
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];

                        if (!string.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
