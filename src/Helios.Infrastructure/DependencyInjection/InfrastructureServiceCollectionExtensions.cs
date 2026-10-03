using Helios.Application.Abstractions.Messaging;
using Helios.Infrastructure.Messaging;
using Helios.Application.Abstractions.Execution;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Features.Execution;
using Helios.Application.Features.Requests;
using Helios.Application.Abstractions.Payments;
using Helios.Infrastructure.Execution;
using Helios.Application.Abstractions.Webhooks;
using Helios.Application.Features.Webhooks;
using Helios.Application.Abstractions.Documents;
using Helios.Application.Features.Uploads;
using Helios.Infrastructure.Documents;
using Helios.Infrastructure.Payments;
using Helios.Infrastructure.Webhooks;
using Helios.Application.Abstractions.Security;
using Helios.Application.Abstractions.Storage;
using Helios.Infrastructure.Security;
using Helios.Infrastructure.Storage;
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
        services.AddScoped<IRowLocks, MySqlRowLocks>();
        services.AddScoped<IHeliosDbContext>(sp => sp.GetRequiredService<HeliosDbContext>());
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        // Built from the fully-merged configuration on first resolution, so a missing or malformed
        // key fails before any secret is written — never with a corrupted value — rather than being
        // read from a half-built configuration at registration time.
        services.AddSingleton(sp => SecretKeyring.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
        services.AddScoped<ISecretStore, MySqlSecretStore>();
        services.AddSingleton<IRequestFingerprinter, HmacRequestFingerprinter>();
        services.AddSingleton<IPayloadProtector, KeyringPayloadProtector>();

        // Durable job queue (P2): MySQL rows, SKIP LOCKED claims, leases and fencing tokens.
        services.AddScoped<IJobQueue, MySqlJobQueue>();
        services.AddSingleton<IJobLeases, MySqlJobLeases>();

        // Tenant-scoped object storage for run outputs and uploads (WP0.7).
        services.AddScoped<IObjectStore, MySqlObjectStore>();

        return services;
    }

    /// <summary>
    /// Execution timing and result retention, shared by the API (inline execution) and the worker
    /// so both apply the same rules. Bound from <c>Helios:Execution</c> and <c>Helios:Requests</c>.
    /// </summary>
    public static IServiceCollection AddHeliosExecution(this IServiceCollection services, IConfiguration configuration)
    {
        var execution = configuration.GetSection("Helios:Execution");
        var defaults = new ExecutionPolicy();

        services.AddSingleton(new ExecutionPolicy
        {
            Lease = TimeSpan.FromSeconds(execution.GetValue("LeaseSeconds", defaults.Lease.TotalSeconds)),
            LeaseRenewal = execution.GetValue<double?>("LeaseRenewalSeconds") is { } renew ? TimeSpan.FromSeconds(renew) : null,
            RetryBaseDelay = TimeSpan.FromSeconds(execution.GetValue("RetryBaseDelaySeconds", defaults.RetryBaseDelay.TotalSeconds)),
            ReconcileDelay = TimeSpan.FromSeconds(execution.GetValue("ReconcileDelaySeconds", defaults.ReconcileDelay.TotalSeconds)),
            MaxReconcileAttempts = execution.GetValue("MaxReconcileAttempts", defaults.MaxReconcileAttempts)
        });

        // Result payloads are personal data for most products; keep them only as long as configured.
        services.AddSingleton(new RequestRetentionPolicy(
            TimeSpan.FromDays(configuration.GetValue("Helios:Requests:ResultRetentionDays", 30))));

        // Required legal documents and their current versions; empty keeps live use closed.
        services.AddSingleton(new Helios.Application.Features.Agreements.AgreementPolicy(
            (configuration.GetSection("Helios:Legal:Documents").Get<Dictionary<string, string>>() ?? [])
                .Where(d => !string.IsNullOrWhiteSpace(d.Value)).ToDictionary(d => d.Key, d => d.Value)));

        services.AddSingleton(new Helios.Application.Features.Platform.PlatformPolicy(
            configuration.GetValue("Helios:Platform:AdjustmentApprovalThreshold", 5_000m)));

        services.AddHostedService<ProductionSafetyCheck>();

        AddPaymentGateway(services, configuration);
        AddWebhooks(services, configuration);
        AddDocuments(services, configuration);
        AddEmail(services, configuration);

        return services;
    }

    /// <summary>
    /// Transactional email (<c>Helios:Email:Sender</c>): <c>smtp</c> through a contracted relay, or
    /// <c>file</c> (Development only — writes messages to <c>Helios:Email:File:Directory</c>). With
    /// none configured nothing is registered, and flows that need email answer 503.
    /// </summary>
    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Helios:Email");
        var sender = section["Sender"];
        var from = section["From"] ?? "no-reply@helios.invalid";

        switch (sender?.ToLowerInvariant())
        {
            case null or "":
                return;

            case "smtp":
                var smtp = section.GetSection("Smtp").Get<SmtpEmailOptions>() ?? new SmtpEmailOptions();
                if (string.IsNullOrWhiteSpace(smtp.Host))
                {
                    throw new InvalidOperationException("Helios:Email:Smtp:Host is required for the smtp sender.");
                }

                services.AddSingleton<IEmailSender>(new SmtpEmailSender(smtp, from));
                return;

            case "file":
                var directory = section["File:Directory"] ?? Path.Combine(Path.GetTempPath(), "helios-mail");
                services.AddSingleton<IEmailSender>(sp => new FileDropEmailSender(directory, sp.GetRequiredService<TimeProvider>()));
                return;

            default:
                throw new InvalidOperationException($"Unknown Helios:Email:Sender '{sender}'. Use 'smtp' or 'file'.");
        }
    }

    /// <summary>
    /// Upload inspection, PDF text reading and malware scanning (<c>Helios:Uploads</c>). With no
    /// scanner configured, uploads are accepted as unscanned for sandbox use only; Production
    /// refuses to start without one.
    /// </summary>
    private static void AddDocuments(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Helios:Uploads");
        var defaults = new DocumentLimits();

        services.AddSingleton(new DocumentLimits
        {
            MaxBytes = section.GetValue("MaxBytes", defaults.MaxBytes),
            MaxPages = section.GetValue("MaxPages", defaults.MaxPages),
            MaxImagePixels = section.GetValue("MaxImagePixels", defaults.MaxImagePixels),
            MaxImageSide = section.GetValue("MaxImageSide", defaults.MaxImageSide),
        });
        services.AddSingleton(new UploadPolicy(TimeSpan.FromDays(section.GetValue("RetentionDays", 7))));
        services.AddSingleton<IDocumentInspector, DocumentInspector>();
        services.AddSingleton<IPdfTextReader, PdfPigTextReader>();

        var scanner = section["Scanner"];
        if (string.Equals(scanner, "clamav", StringComparison.OrdinalIgnoreCase))
        {
            var host = section["ClamAv:Host"] ?? "localhost";
            var port = section.GetValue("ClamAv:Port", 3310);
            services.AddSingleton<IMalwareScanner>(new ClamAvScanner(host, port));
        }
        else if (!string.IsNullOrWhiteSpace(scanner))
        {
            throw new InvalidOperationException($"Unknown malware scanner '{scanner}'. Supported: clamav.");
        }
    }

    /// <summary>
    /// Outbound webhooks: SSRF policy, connect-time address guard and delivery queue.
    /// <c>Helios:Webhooks:AllowPrivateNetworks</c> is for local development and tests; Production refuses it.
    /// </summary>
    private static void AddWebhooks(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Helios:Webhooks");
        var allowPrivate = section.GetValue("AllowPrivateNetworks", false);
        var defaults = new WebhookPolicy();

        services.AddSingleton<IOutboundUrlPolicy>(new OutboundUrlPolicy(allowPrivate));
        services.AddSingleton(new WebhookPolicy
        {
            MaxAttempts = section.GetValue("MaxAttempts", defaults.MaxAttempts),
            BaseDelay = TimeSpan.FromSeconds(section.GetValue("BaseDelaySeconds", defaults.BaseDelay.TotalSeconds)),
            DeliveryRetention = TimeSpan.FromDays(section.GetValue("DeliveryRetentionDays", defaults.DeliveryRetention.TotalDays)),
        });

        services.AddHttpClient(GuardedWebhookSender.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => GuardedWebhookSender.CreateGuardedHandler(allowPrivate));
        services.AddSingleton<IWebhookSender, GuardedWebhookSender>();
        services.AddScoped<IWebhookQueue, MySqlWebhookQueue>();
    }

    /// <summary>
    /// Registers the configured payment gateway (<c>Helios:Payments:Gateway</c>). With none
    /// configured, top-ups answer 503 rather than pretending. Only the fake test gateway exists until
    /// a gateway contract is signed; Production refuses it at startup.
    /// </summary>
    private static void AddPaymentGateway(IServiceCollection services, IConfiguration configuration)
    {
        var gateway = configuration["Helios:Payments:Gateway"];

        if (string.Equals(gateway, FakeTestGatewayOptions.Name, StringComparison.OrdinalIgnoreCase))
        {
            var section = configuration.GetSection("Helios:Payments:FakeTest");
            var secret = section["WebhookSecret"];

            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            {
                throw new InvalidOperationException(
                    "Helios:Payments:FakeTest:WebhookSecret must be set (32+ characters) when the fake-test gateway is configured.");
            }

            services.AddSingleton(new FakeTestGatewayOptions
            {
                WebhookSecret = secret,
                MerchantId = section["MerchantId"] ?? "fake-merchant"
            });
            services.AddSingleton<IPaymentGateway, FakeTestPaymentGateway>();
        }
        else if (!string.IsNullOrWhiteSpace(gateway))
        {
            throw new InvalidOperationException($"Unknown payment gateway '{gateway}'. No real gateway adapter is implemented yet.");
        }
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

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<HeliosRole>()
            .AddEntityFrameworkStores<HeliosDbContext>();

        return services;
    }
}
