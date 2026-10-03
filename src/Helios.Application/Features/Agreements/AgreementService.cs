using FluentValidation;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Identity;
using Helios.Contracts.Organizations;
using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Agreements;

/// <summary>
/// The legal documents a company must accept before live use, and their current versions, bound
/// from <c>Helios:Legal:Documents</c> (e.g. <c>terms</c>, <c>dpa</c>). The texts and versions are the
/// owner's legal decision; until they are published here, live use stays closed.
/// </summary>
public sealed record AgreementPolicy(IReadOnlyDictionary<string, string> Current);

/// <summary>
/// Records which document versions a company accepted (plan section 4: "accept required terms …
/// production approval when applicable"), and enforces them before live use. Acceptance is the
/// Owner's act and is kept as evidence: who, when and from which address.
/// </summary>
public sealed class AgreementService(
    IHeliosDbContext db,
    OrganizationAccess access,
    AgreementPolicy policy,
    IWorkspaceContext context,
    IAuditWriter audit,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<AgreementStatusResponse>> ListAsync(Guid organizationId, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ViewOrganization, ct);
        return await StatusAsync(organizationId, ct);
    }

    public async Task<IReadOnlyList<AgreementStatusResponse>> AcceptAsync(Guid organizationId, AcceptAgreementRequest request, CancellationToken ct)
    {
        var member = await access.RequireAsync(organizationId, OrganizationPermission.AcceptAgreements, ct);

        if (!policy.Current.TryGetValue(request.Document, out var current))
        {
            throw new NotFoundException("Agreement", request.Document);
        }

        if (!string.Equals(current, request.Version, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"The current version of '{request.Document}' is {current}; review and accept that version.", "agreement_version_mismatch");
        }

        var exists = await db.AgreementAcceptances.AnyAsync(a =>
            a.OrganizationId == organizationId && a.Document == request.Document && a.Version == current, ct);

        if (!exists)
        {
            db.AgreementAcceptances.Add(new AgreementAcceptance
            {
                OrganizationId = organizationId,
                Document = request.Document,
                Version = current,
                AcceptedBy = member.UserId,
                AcceptedAt = clock.GetUtcNow(),
                IpAddress = context.IpAddress
            });

            audit.Record("agreement.accept", nameof(AgreementAcceptance), $"{request.Document}:{current}", organizationId: organizationId);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Accepted concurrently by another request: the unique index kept one row.
            }
        }

        return await StatusAsync(organizationId, ct);
    }

    /// <summary>Throws unless every required document's current version has been accepted.</summary>
    public async Task EnsureAcceptedAsync(Guid organizationId, CancellationToken ct)
    {
        if (policy.Current.Count == 0)
        {
            throw new ConflictException(
                "Live use opens once customer terms and the processing agreement are published.", "agreements_not_published");
        }

        var missing = (await StatusAsync(organizationId, ct)).Where(s => !s.Accepted).Select(s => $"{s.Document} {s.CurrentVersion}").ToList();
        if (missing.Count > 0)
        {
            throw new ConflictException(
                $"Live use needs the company Owner to accept: {string.Join(", ", missing)}.", "agreements_required");
        }
    }

    private async Task<IReadOnlyList<AgreementStatusResponse>> StatusAsync(Guid organizationId, CancellationToken ct)
    {
        var accepted = await db.AgreementAcceptances.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId)
            .ToListAsync(ct);

        return policy.Current
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .Select(d =>
            {
                var row = accepted.FirstOrDefault(a => a.Document == d.Key && a.Version == d.Value);
                return new AgreementStatusResponse(d.Key, d.Value, row is not null, row?.AcceptedAt, row?.AcceptedBy);
            })
            .ToList();
    }
}

public sealed class AcceptAgreementRequestValidator : AbstractValidator<AcceptAgreementRequest>
{
    public AcceptAgreementRequestValidator()
    {
        RuleFor(r => r.Document).NotEmpty().MaximumLength(50);
        RuleFor(r => r.Version).NotEmpty().MaximumLength(50);
    }
}
