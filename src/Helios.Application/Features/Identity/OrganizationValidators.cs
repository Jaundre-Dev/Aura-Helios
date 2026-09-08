using FluentValidation;
using Helios.Contracts.Organizations;

namespace Helios.Application.Features.Identity;

public sealed class CreateOrganizationRequestValidator : AbstractValidator<CreateOrganizationRequest>
{
    public CreateOrganizationRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Slug).MaximumLength(100).Matches("^[a-z0-9-]+$")
            .When(r => !string.IsNullOrWhiteSpace(r.Slug))
            .WithMessage("Slug may contain only lowercase letters, digits and hyphens.");
    }
}
