namespace Helios.Application.Features.Platform;

/// <summary>
/// Platform controls bound from <c>Helios:Platform</c>. Credit adjustments larger than
/// <see cref="AdjustmentApprovalThreshold"/> (either direction) need a second staff member's approval.
/// </summary>
public sealed record PlatformPolicy(decimal AdjustmentApprovalThreshold = 5_000m);
