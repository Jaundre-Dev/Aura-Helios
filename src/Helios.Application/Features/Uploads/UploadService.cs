using System.Security.Cryptography;
using System.Text;
using Helios.Application.Abstractions.Documents;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Abstractions.Storage;
using Helios.Application.Common;
using Helios.Application.Features.Products;
using Helios.Application.Features.Requests;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Contracts.Uploads;
using Helios.Domain.Uploads;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Uploads;

/// <summary>How long raw documents are kept. Short by default (plan section 13).</summary>
public sealed record UploadPolicy(TimeSpan Retention);

/// <summary>
/// Document uploads (plan section 11: validation and malware scan before anything else). A file is
/// inspected from its own bytes and scanned before it is stored; a rejected file is never stored.
/// Uploads belong to one workspace and environment, like keys and requests.
/// </summary>
public sealed class UploadService(
    IHeliosDbContext db,
    CallerResolver callers,
    IDocumentInspector inspector,
    IEnumerable<IMalwareScanner> scanners,
    IObjectStore store,
    IAuditWriter audit,
    UploadPolicy policy,
    TimeProvider clock)
{
    private static readonly OrganizationPermission[] MetadataPermissions =
        [OrganizationPermission.ExecuteProducts, OrganizationPermission.ViewResults, OrganizationPermission.ViewRequestDiagnostics];

    public async Task<UploadResponse> CreateAsync(
        string? fileName,
        string? declaredMediaType,
        byte[] content,
        ApiEnvironment? environment,
        CancellationToken ct)
    {
        var caller = await callers.ResolveAsync(environment, [OrganizationPermission.ExecuteProducts], ct);

        var inspection = inspector.Inspect(content, declaredMediaType);
        if (inspection.Rejection is { } rejection)
        {
            audit.Record("upload.reject", nameof(Upload), allowed: false, denyReason: rejection,
                organizationId: caller.OrganizationId);
            await db.SaveChangesAsync(ct);
            throw new BadRequestException(inspection.Detail ?? "The file was rejected.", rejection);
        }

        var (scanState, scannerName) = await ScanAsync(content, caller, ct);
        var safeName = SafeFileName(fileName);
        var now = clock.GetUtcNow();

        var storageRef = await store.PutAsync(new ObjectToStore(safeName, inspection.MediaType!, content), ct);

        var upload = new Upload
        {
            OrganizationId = caller.OrganizationId,
            WorkspaceId = caller.WorkspaceId,
            Environment = caller.Environment,
            StorageRef = storageRef,
            FileName = safeName,
            MediaType = inspection.MediaType!,
            SizeBytes = content.Length,
            PageCount = inspection.PageCount,
            HasTextLayer = inspection.HasTextLayer,
            ScanState = scanState,
            Scanner = scannerName,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
            CreatedByUserId = caller.UserId,
            CreatedByApiKeyId = caller.Key?.Id,
            CreatedAt = now,
            ExpiresAt = now + policy.Retention
        };

        db.Uploads.Add(upload);
        audit.Record("upload.create", nameof(Upload), upload.Id.ToString(), organizationId: caller.OrganizationId,
            metadataJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                mediaType = upload.MediaType, sizeBytes = upload.SizeBytes, pages = upload.PageCount,
                scan = scanState.ToString(), environment = caller.Environment.ToString()
            }));
        await db.SaveChangesAsync(ct);

        return ToResponse(upload);
    }

    public async Task<UploadResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var upload = await FindAsync(id, MetadataPermissions, ct);
        return upload is null ? null : ToResponse(upload);
    }

    /// <summary>
    /// The original document. Users need <c>ViewResults</c> — documents are as sensitive as the
    /// results drawn from them; Finance and Developer roles cannot download them.
    /// </summary>
    public async Task<(string FileName, string MediaType, byte[] Content)?> OpenAsync(Guid id, CancellationToken ct)
    {
        var upload = await FindAsync(id, [OrganizationPermission.ViewResults], ct);
        if (upload is null)
        {
            return null;
        }

        if (!upload.IsAvailableAt(clock.GetUtcNow()))
        {
            throw new GoneException("This upload has been deleted or is past its retention period.");
        }

        var stored = await store.GetAsync(upload.StorageRef!, ct)
            ?? throw new GoneException("This upload's content is no longer available.");

        return (upload.FileName, upload.MediaType, stored.Content);
    }

    /// <summary>Deletes the content now; the metadata row remains for audit and billing.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var upload = await FindAsync(id, [OrganizationPermission.ExecuteProducts], ct, tracked: true);
        if (upload is null)
        {
            return false;
        }

        if (upload.StorageRef is { } reference)
        {
            await store.DeleteAsync(reference, ct);
        }

        upload.StorageRef = null;
        upload.DeletedAt ??= clock.GetUtcNow();
        audit.Record("upload.delete", nameof(Upload), id.ToString(), organizationId: upload.OrganizationId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<(ScanState State, string? Scanner)> ScanAsync(byte[] content, RequestCaller caller, CancellationToken ct)
    {
        var scanner = scanners.FirstOrDefault();
        if (scanner is null)
        {
            // Without a scanner, documents may be tried in sandbox only; live work refuses them.
            if (caller.Environment == ApiEnvironment.Live)
            {
                throw new ServiceUnavailableException(
                    "Live uploads require malware scanning, and no scanner is configured.", "scanner_unavailable");
            }

            return (ScanState.NotScanned, null);
        }

        ScanVerdict verdict;
        try
        {
            verdict = await scanner.ScanAsync(content, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ServiceUnavailableException("The malware scanner is unavailable; try again later.", "scanner_unavailable");
        }

        if (!verdict.Clean)
        {
            audit.Record("upload.reject", nameof(Upload), allowed: false, denyReason: "malware_detected",
                organizationId: caller.OrganizationId,
                metadataJson: System.Text.Json.JsonSerializer.Serialize(new { signature = verdict.Signature, scanner = scanner.Name }));
            await db.SaveChangesAsync(ct);
            throw new BadRequestException("The file was rejected by the malware scanner.", "malware_detected");
        }

        return (ScanState.Clean, scanner.Name);
    }

    private async Task<Upload?> FindAsync(Guid id, OrganizationPermission[] permissions, CancellationToken ct, bool tracked = false)
    {
        var caller = await callers.ResolveAsync(null, permissions, ct);

        var query = tracked ? db.Uploads : db.Uploads.AsNoTracking();
        var upload = await query.SingleOrDefaultAsync(u => u.Id == id, ct);

        // Workspace query filter plus explicit checks: a key sees only its own environment.
        if (upload is null || upload.WorkspaceId != caller.WorkspaceId ||
            (caller.Key is not null && upload.Environment != caller.Environment))
        {
            return null;
        }

        return upload;
    }

    /// <summary>A display name only — never a path. Keeps letters, digits and a few separators.</summary>
    internal static string SafeFileName(string? name)
    {
        var leaf = Path.GetFileName(name ?? string.Empty);
        var builder = new StringBuilder(leaf.Length);

        foreach (var ch in leaf)
        {
            builder.Append(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or ' ' ? ch : '_');
        }

        var cleaned = builder.ToString().Trim(' ', '.');
        return cleaned.Length == 0 ? "document" : cleaned[..Math.Min(cleaned.Length, 200)];
    }

    private static UploadResponse ToResponse(Upload u) =>
        new(u.Id, u.FileName, u.MediaType, u.SizeBytes, u.PageCount, u.HasTextLayer, u.ScanState, u.Environment,
            u.Sha256, u.CreatedAt, u.ExpiresAt);
}

/// <summary>Executor access to upload content, confined by the workspace query filter.</summary>
public sealed class UploadAccess(IHeliosDbContext db, IObjectStore store, TimeProvider clock) : IUploadAccess
{
    public async Task<byte[]?> OpenAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        var upload = await db.Uploads.AsNoTracking().SingleOrDefaultAsync(u => u.Id == uploadId, cancellationToken);
        if (upload is null || !upload.IsAvailableAt(clock.GetUtcNow()))
        {
            return null;
        }

        return (await store.GetAsync(upload.StorageRef!, cancellationToken))?.Content;
    }
}
