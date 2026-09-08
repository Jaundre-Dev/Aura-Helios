using FluentValidation;
using Helios.Contracts.Projects;

namespace Helios.Application.Features.Projects;

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Slug).MaximumLength(100).Matches("^[a-z0-9-]+$")
            .When(r => !string.IsNullOrWhiteSpace(r.Slug))
            .WithMessage("Slug may contain only lowercase letters, digits and hyphens.");
        RuleFor(r => r.Description).MaximumLength(2000);
        RuleFor(r => r.Classification).IsInEnum();
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200).When(r => r.Name is not null);
        RuleFor(r => r.Description).MaximumLength(2000);
        RuleFor(r => r.Classification).IsInEnum().When(r => r.Classification is not null);
    }
}
