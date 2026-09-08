using FluentValidation;
using Helios.Contracts.Workspaces;

namespace Helios.Application.Features.Workspaces;

public sealed class CreateWorkspaceRequestValidator : AbstractValidator<CreateWorkspaceRequest>
{
    public CreateWorkspaceRequestValidator()
    {
        RuleFor(r => r.OrganizationId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Slug).MaximumLength(100).Matches("^[a-z0-9-]+$")
            .When(r => !string.IsNullOrWhiteSpace(r.Slug))
            .WithMessage("Slug may contain only lowercase letters, digits and hyphens.");
        RuleFor(r => r.Description).MaximumLength(1000);
    }
}

public sealed class UpdateWorkspaceRequestValidator : AbstractValidator<UpdateWorkspaceRequest>
{
    public UpdateWorkspaceRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200).When(r => r.Name is not null);
        RuleFor(r => r.Description).MaximumLength(1000);
    }
}

public sealed class AddWorkspaceMemberRequestValidator : AbstractValidator<AddWorkspaceMemberRequest>
{
    public AddWorkspaceMemberRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Role).IsInEnum();
    }
}
