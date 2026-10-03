using Helios.Contracts.Catalogue;
using Helios.Domain.Common;

namespace Helios.Domain.Catalogue;

/// <summary>
/// A sellable API service. The slug is stable and public; the release state decides where, if
/// anywhere, it may be executed. Listing a planned product is roadmap transparency, not an offer.
/// </summary>
public class ApiProduct : AuditableEntity, IAggregateRoot
{
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string Summary { get; set; }
    public ProductDelivery Delivery { get; set; }
    public ProductReleaseState ReleaseState { get; set; } = ProductReleaseState.Planned;
    public ProductSensitivity Sensitivity { get; set; } = ProductSensitivity.Standard;
    public required string BillingUnit { get; set; }

    /// <summary>The version new requests run against; null while no version is published.</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>What the product does not do — shown wherever the product is offered.</summary>
    public string? Limitations { get; set; }

    public bool IsCallableIn(ApiEnvironment environment) => environment switch
    {
        ApiEnvironment.Sandbox => ReleaseState is ProductReleaseState.Sandbox or ProductReleaseState.Beta or ProductReleaseState.Live,
        ApiEnvironment.Live => ReleaseState is ProductReleaseState.Beta or ProductReleaseState.Live,
        _ => false
    };
}

/// <summary>A published contract for a product: schemas and bounds that a request is executed under.</summary>
public class ApiProductVersion : AuditableEntity
{
    public Guid ProductId { get; set; }
    public required string Version { get; set; }
    public ProductReleaseState ReleaseState { get; set; }

    /// <summary>Largest request body accepted for this version.</summary>
    public int MaxInputBytes { get; set; }

    public string? RequestSchemaJson { get; set; }
    public string? ResponseSchemaJson { get; set; }
    public string? RequestExample { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}
