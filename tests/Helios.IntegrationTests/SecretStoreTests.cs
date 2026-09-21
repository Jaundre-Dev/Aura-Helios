using System.Security.Cryptography;
using System.Text;
using Helios.Application.Abstractions.Security;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// The tenant-scoped secret store (H0): values sealed with AES-256-GCM before they reach the
/// database, reachable only from the workspace that wrote them, audited without ever recording the
/// plaintext, and refused entirely to a system caller with no workspace to resolve against.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class SecretStoreTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    /// <summary>Points the shared context at a workspace and hands back a scope resolved under it.</summary>
    private IServiceScope ScopeFor(Guid workspaceId, bool isSystem = false)
    {
        _factory.Context.UserId = isSystem ? null : Guid.CreateVersion7();
        _factory.Context.WorkspaceId = isSystem ? null : workspaceId;
        _factory.Context.IsSystem = isSystem;
        return _factory.Services.CreateScope();
    }

    [Fact]
    public async Task Set_then_get_returns_the_plaintext()
    {
        var workspace = Guid.CreateVersion7();
        using var scope = ScopeFor(workspace);
        var store = scope.ServiceProvider.GetRequiredService<ISecretStore>();

        await store.SetAsync("provider:openai:apikey", "sk-super-secret-value", CancellationToken.None);
        var read = await store.GetAsync("provider:openai:apikey", CancellationToken.None);

        Assert.Equal("sk-super-secret-value", read);
    }

    [Fact]
    public async Task Set_twice_overwrites_the_previous_value()
    {
        var workspace = Guid.CreateVersion7();
        using var scope = ScopeFor(workspace);
        var store = scope.ServiceProvider.GetRequiredService<ISecretStore>();

        await store.SetAsync("webhook:signing", "first", CancellationToken.None);
        await store.SetAsync("webhook:signing", "second", CancellationToken.None);

        Assert.Equal("second", await store.GetAsync("webhook:signing", CancellationToken.None));

        // Overwrite is an update in place, not a second row.
        using var read = ScopeFor(workspace);
        var db = read.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var count = await db.Secrets.IgnoreQueryFilters()
            .CountAsync(s => s.WorkspaceId == workspace && s.Reference == "webhook:signing");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Get_of_an_unknown_reference_returns_null()
    {
        using var scope = ScopeFor(Guid.CreateVersion7());
        var store = scope.ServiceProvider.GetRequiredService<ISecretStore>();

        Assert.Null(await store.GetAsync("does:not:exist", CancellationToken.None));
    }

    [Fact]
    public async Task The_value_is_encrypted_at_rest()
    {
        var workspace = Guid.CreateVersion7();
        const string plaintext = "sk-plaintext-must-not-be-stored";

        using var scope = ScopeFor(workspace);
        var store = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        await store.SetAsync("provider:anthropic:apikey", plaintext, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var row = await db.Secrets.IgnoreQueryFilters()
            .SingleAsync(s => s.WorkspaceId == workspace && s.Reference == "provider:anthropic:apikey");

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        Assert.DoesNotContain(plaintext, Encoding.UTF8.GetString(row.Envelope));
        // envelope = 12-byte nonce + ciphertext (= plaintext length) + 16-byte tag
        Assert.Equal(12 + plainBytes.Length + 16, row.Envelope.Length);
        Assert.False(string.IsNullOrEmpty(row.KeyId));
    }

    [Fact]
    public async Task A_workspace_cannot_read_another_workspaces_secret()
    {
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        using (var aliceScope = ScopeFor(alice))
        {
            var store = aliceScope.ServiceProvider.GetRequiredService<ISecretStore>();
            await store.SetAsync("provider:openai:apikey", "alice-only", CancellationToken.None);
        }

        using var bobScope = ScopeFor(bob);
        var bobStore = bobScope.ServiceProvider.GetRequiredService<ISecretStore>();

        // Same reference, different tenant: the query filter makes it simply not exist.
        Assert.Null(await bobStore.GetAsync("provider:openai:apikey", CancellationToken.None));
    }

    [Fact]
    public async Task Two_workspaces_may_hold_the_same_reference_independently()
    {
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        using (var aliceScope = ScopeFor(alice))
            await aliceScope.ServiceProvider.GetRequiredService<ISecretStore>()
                .SetAsync("shared:ref", "alice-value", CancellationToken.None);

        using (var bobScope = ScopeFor(bob))
            await bobScope.ServiceProvider.GetRequiredService<ISecretStore>()
                .SetAsync("shared:ref", "bob-value", CancellationToken.None);

        using var readAlice = ScopeFor(alice);
        Assert.Equal("alice-value", await readAlice.ServiceProvider.GetRequiredService<ISecretStore>()
            .GetAsync("shared:ref", CancellationToken.None));

        using var readBob = ScopeFor(bob);
        Assert.Equal("bob-value", await readBob.ServiceProvider.GetRequiredService<ISecretStore>()
            .GetAsync("shared:ref", CancellationToken.None));
    }

    [Fact]
    public async Task Setting_a_secret_leaves_an_audit_row_without_the_value()
    {
        var workspace = Guid.CreateVersion7();
        const string plaintext = "sk-never-appears-in-audit";

        using var scope = ScopeFor(workspace);
        await scope.ServiceProvider.GetRequiredService<ISecretStore>()
            .SetAsync("provider:gemini:apikey", plaintext, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var entry = await db.AuditLogs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.Action == "secret.set" && a.ResourceId == "provider:gemini:apikey");

        Assert.NotNull(entry);
        Assert.Equal(workspace, entry.WorkspaceId);
        Assert.Contains("provider:gemini:apikey", entry.Metadata);
        Assert.DoesNotContain(plaintext, entry.Metadata ?? "");
    }

    [Fact]
    public async Task A_system_caller_with_no_workspace_is_refused()
    {
        using var scope = ScopeFor(Guid.Empty, isSystem: true);
        var store = scope.ServiceProvider.GetRequiredService<ISecretStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SetAsync("anything", "value", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetAsync("anything", CancellationToken.None));
    }

    [Fact]
    public async Task A_tampered_ciphertext_fails_to_decrypt()
    {
        var workspace = Guid.CreateVersion7();
        using var scope = ScopeFor(workspace);
        var store = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        await store.SetAsync("provider:openai:apikey", "authentic", CancellationToken.None);

        // Flip a byte of the stored ciphertext directly, then read back through the store.
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var row = await db.Secrets.IgnoreQueryFilters()
            .SingleAsync(s => s.WorkspaceId == workspace && s.Reference == "provider:openai:apikey");
        // Reassign a new array — EF tracks byte[] by reference, so an in-place mutation would not save.
        // Flip a ciphertext byte (index 13 is past the 12-byte nonce); any altered byte breaks the tag.
        var tampered = (byte[])row.Envelope.Clone();
        tampered[13] ^= 0xFF;
        row.Envelope = tampered;
        await db.SaveChangesAsync();

        using var reread = ScopeFor(workspace);
        var reader = reread.ServiceProvider.GetRequiredService<ISecretStore>();

        // GCM authentication rejects the altered row rather than returning corrupted plaintext.
        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(
            () => reader.GetAsync("provider:openai:apikey", CancellationToken.None));
    }
}
