namespace Helios.Contracts.Common;

/// <summary>
/// Drives routing. RESTRICTED never leaves local inference unless explicitly permitted.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DataClassification>))]
public enum DataClassification
{
    Public = 0,
    Internal = 1,
    Confidential = 2,
    Restricted = 3
}
