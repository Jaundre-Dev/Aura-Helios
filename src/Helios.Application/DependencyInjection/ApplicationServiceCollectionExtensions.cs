using FluentValidation;
using Helios.Application.Features.Accounts;
using Helios.Application.Features.Agreements;
using Helios.Application.Features.ApiKeys;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Catalogue;
using Helios.Application.Features.Execution;
using Helios.Application.Features.Identity;
using Helios.Application.Features.Platform;
using Helios.Application.Features.Products;
using Helios.Application.Features.Products.Documents;
using Helios.Application.Features.Products.Identity;
using Helios.Application.Features.Projects;
using Helios.Application.Features.Requests;
using Helios.Application.Features.Uploads;
using Helios.Application.Features.Webhooks;
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
        services.AddScoped<AgreementService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<CallerResolver>();
        services.AddScoped<ProductRequestService>();
        services.AddScoped<ReviewService>();
        services.AddScoped<UploadService>();
        services.AddScoped<IUploadAccess, UploadAccess>();
        services.AddScoped<BillingProfileService>();

        services.AddScoped<LedgerService>();
        services.AddScoped<SpendingLimits>();
        services.AddScoped<PriceService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<BillingQueryService>();
        services.AddScoped<JobRunner>();

        services.AddScoped<WebhookOutbox>();
        services.AddScoped<WebhookService>();

        services.AddScoped<AccountSecurityService>();
        services.AddScoped<PlatformAccess>();
        services.AddScoped<PlatformStaffService>();
        services.AddScoped<PlatformAdministrationService>();
        services.AddScoped<PlatformOperationsService>();

        // First-party product executors. Each callable catalogue product needs exactly one here.
        services.AddSingleton<IProductExecutor, SaIdValidateExecutor>();
        services.AddSingleton<IProductExecutor, OcrGeneralExecutor>();
        services.AddSingleton<IProductExecutor, InvoiceExtractionExecutor>();
        services.AddSingleton<IProductExecutor, BankStatementExecutor>();
        services.AddSingleton<IProductExecutor, PayslipExecutor>();
        services.AddSingleton<IProductExecutor, ProofOfAddressExecutor>();
        services.AddSingleton<IProductExecutor, DocumentClassifyExecutor>();
        services.AddSingleton<ProductExecutorRegistry>();

        return services;
    }
}
