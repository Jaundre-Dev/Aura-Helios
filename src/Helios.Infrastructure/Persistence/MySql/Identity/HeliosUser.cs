using Microsoft.AspNetCore.Identity;

namespace Helios.Infrastructure.Persistence.MySql.Identity;

/// <summary>
/// The authentication subject. Deliberately an infrastructure type: ASP.NET Core Identity
/// is an implementation of sign-in, and Helios.Domain references users only by Guid so it
/// never depends on how they are authenticated.
/// </summary>
public class HeliosUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSignInAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Workspace selected on last sign-in, used to seed the workspace claim.</summary>
    public Guid? DefaultWorkspaceId { get; set; }
}

public class HeliosRole : IdentityRole<Guid>;
