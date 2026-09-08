using FluentValidation;
using Helios.Contracts.Identity;

namespace Helios.Application.Features.Identity;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);

        // Only presence is checked here. Complexity (length, character classes) is owned by
        // ASP.NET Core Identity, and the register endpoint surfaces its messages as a 400 —
        // duplicating the policy here would let the two drift apart.
        RuleFor(r => r.Password).NotEmpty();

        RuleFor(r => r.DisplayName).MaximumLength(200)
            .When(r => !string.IsNullOrWhiteSpace(r.DisplayName));
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
        RuleFor(r => r.Password).NotEmpty();
    }
}

public sealed class SelectWorkspaceRequestValidator : AbstractValidator<SelectWorkspaceRequest>
{
    public SelectWorkspaceRequestValidator()
    {
        RuleFor(r => r.WorkspaceId).NotEmpty();
    }
}
