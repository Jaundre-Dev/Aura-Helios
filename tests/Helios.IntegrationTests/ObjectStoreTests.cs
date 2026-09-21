using System.Text;
using Helios.Application.Abstractions.Storage;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// The tenant-scoped object store (H0): run outputs and uploads reachable only from the workspace
/// that wrote them, opaque references that resolve to nothing in another tenant, deletion, bounded
/// size, and a refusal to serve a system caller with no workspace.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class ObjectStoreTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private IServiceScope ScopeFor(Guid workspaceId, bool isSystem = false)
    {
        _factory.Context.UserId = isSystem ? null : Guid.CreateVersion7();
        _factory.Context.WorkspaceId = isSystem ? null : workspaceId;
        _factory.Context.IsSystem = isSystem;
        return _factory.Services.CreateScope();
    }

    private static ObjectToStore Report(string body) =>
        new("review-report.md", "text/markdown", Encoding.UTF8.GetBytes(body));

    [Fact]
    public async Task Put_then_get_returns_the_content_and_metadata()
    {
        using var scope = ScopeFor(Guid.CreateVersion7());
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();

        var reference = await store.PutAsync(Report("# Findings\nAll clear."), CancellationToken.None);
        var read = await store.GetAsync(reference, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(reference, read.Ref);
        Assert.Equal("review-report.md", read.Name);
        Assert.Equal("text/markdown", read.ContentType);
        Assert.Equal("# Findings\nAll clear.", Encoding.UTF8.GetString(read.Content));
        Assert.Equal(read.Content.Length, read.SizeBytes);
    }

    [Fact]
    public async Task Get_of_an_unknown_or_malformed_reference_returns_null()
    {
        using var scope = ScopeFor(Guid.CreateVersion7());
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();

        Assert.Null(await store.GetAsync(Guid.CreateVersion7().ToString(), CancellationToken.None));
        Assert.Null(await store.GetAsync("not-a-guid", CancellationToken.None));
    }

    [Fact]
    public async Task A_workspace_cannot_read_another_workspaces_object()
    {
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        string reference;
        using (var aliceScope = ScopeFor(alice))
        {
            reference = await aliceScope.ServiceProvider.GetRequiredService<IObjectStore>()
                .PutAsync(Report("alice's report"), CancellationToken.None);
        }

        using var bobScope = ScopeFor(bob);
        var bobStore = bobScope.ServiceProvider.GetRequiredService<IObjectStore>();

        // Even holding the exact reference, another tenant resolves it to nothing.
        Assert.Null(await bobStore.GetAsync(reference, CancellationToken.None));
        Assert.False(await bobStore.DeleteAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_the_object()
    {
        var workspace = Guid.CreateVersion7();
        using var scope = ScopeFor(workspace);
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();

        var reference = await store.PutAsync(Report("to be deleted"), CancellationToken.None);

        Assert.True(await store.DeleteAsync(reference, CancellationToken.None));
        Assert.Null(await store.GetAsync(reference, CancellationToken.None));

        // A second delete is a no-op, not an error.
        Assert.False(await store.DeleteAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_object_is_rejected()
    {
        using var scope = ScopeFor(Guid.CreateVersion7());
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.PutAsync(new ObjectToStore("empty.bin", "application/octet-stream", []), CancellationToken.None));
    }

    [Fact]
    public async Task An_object_over_the_size_limit_is_rejected()
    {
        using var scope = ScopeFor(Guid.CreateVersion7());
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();

        var tooBig = new byte[MySqlObjectStoreMax + 1];
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.PutAsync(new ObjectToStore("huge.bin", "application/octet-stream", tooBig), CancellationToken.None));
    }

    // Kept in one place so the test and the store cannot drift apart on the limit.
    private const long MySqlObjectStoreMax = 16 * 1024 * 1024;

    [Fact]
    public async Task A_system_caller_with_no_workspace_is_refused()
    {
        using var scope = ScopeFor(Guid.Empty, isSystem: true);
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PutAsync(Report("nope"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetAsync(Guid.CreateVersion7().ToString(), CancellationToken.None));
    }

    [Fact]
    public async Task Storing_and_deleting_leave_audit_rows()
    {
        var workspace = Guid.CreateVersion7();
        using var scope = ScopeFor(workspace);
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();

        var reference = await store.PutAsync(Report("audited"), CancellationToken.None);
        await store.DeleteAsync(reference, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        var actions = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.ResourceId == reference && a.WorkspaceId == workspace)
            .Select(a => a.Action)
            .ToListAsync();

        Assert.Contains("object.put", actions);
        Assert.Contains("object.delete", actions);
    }
}
