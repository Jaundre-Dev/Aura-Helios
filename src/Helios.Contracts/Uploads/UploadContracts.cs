using System.Text.Json.Serialization;
using Helios.Contracts.Catalogue;

namespace Helios.Contracts.Uploads;

/// <summary>Result of malware scanning an upload.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScanState>))]
public enum ScanState
{
    /// <summary>Scanned and clean.</summary>
    Clean,

    /// <summary>No scanner configured (development and sandbox only). Never accepted for live work.</summary>
    NotScanned
}

public sealed record UploadResponse(
    Guid Id,
    string FileName,
    string MediaType,
    long SizeBytes,
    int PageCount,
    bool HasTextLayer,
    ScanState ScanState,
    ApiEnvironment Environment,
    string Sha256,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
