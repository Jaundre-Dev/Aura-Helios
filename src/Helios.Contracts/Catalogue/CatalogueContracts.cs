namespace Helios.Contracts.Catalogue;

/// <summary>
/// Where a product is in its life. Only <see cref="Sandbox"/>, <see cref="Beta"/> and
/// <see cref="Live"/> are callable, and only <see cref="Beta"/>/<see cref="Live"/> in the live
/// environment. A planned product is listed so customers can see the roadmap, never executed.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ProductReleaseState>))]
public enum ProductReleaseState
{
    Planned,
    Sandbox,
    Beta,
    Live,
    Suspended,
    Deprecated
}

/// <summary>How a product is fulfilled (API-CATALOGUE.md "Delivery").</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ProductDelivery>))]
public enum ProductDelivery
{
    Build,
    AI,
    Hybrid,
    Partner
}

/// <summary>How sensitive the data a product processes is; drives approval and routing rules.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ProductSensitivity>))]
public enum ProductSensitivity
{
    Standard,
    Personal,
    SpecialPersonal
}

/// <summary>Sandbox and live are separate credentials, data and billing — never interchangeable.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ApiEnvironment>))]
public enum ApiEnvironment
{
    Sandbox,
    Live
}

public sealed record ProductSummaryResponse(
    string Slug,
    string Name,
    string Category,
    string Summary,
    ProductDelivery Delivery,
    ProductReleaseState ReleaseState,
    ProductSensitivity Sensitivity,
    string BillingUnit,
    string? CurrentVersion,
    bool CallableInSandbox,
    bool CallableInLive,
    string? Limitations);

public sealed record ProductDetailResponse(
    ProductSummaryResponse Product,
    IReadOnlyList<ProductVersionResponse> Versions);

public sealed record ProductVersionResponse(
    string Version,
    ProductReleaseState ReleaseState,
    int MaxInputBytes,
    string? RequestSchemaJson,
    string? ResponseSchemaJson,
    string? RequestExample,
    DateTimeOffset PublishedAt);

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<EntitlementState>))]
public enum EntitlementState
{
    Enabled,
    PendingApproval,
    Disabled
}

public sealed record EnableEntitlementRequest(string ProductSlug, ApiEnvironment Environment, string? Purpose = null);

public sealed record EntitlementResponse(
    Guid Id,
    Guid OrganizationId,
    string ProductSlug,
    ApiEnvironment Environment,
    EntitlementState State,
    string? Purpose,
    DateTimeOffset CreatedAt);
