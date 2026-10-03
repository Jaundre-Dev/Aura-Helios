using FluentValidation;
using Helios.Contracts.Identity;

namespace Helios.Application.Features.Accounts;

internal static class CodeRules
{
    public const string TotpOrRecovery = "^([0-9 ]{6,8}|[A-Za-z2-7]{5}-?[A-Za-z2-7]{5})$";
}

public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Token).NotEmpty().MaximumLength(100);
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Token).NotEmpty().MaximumLength(100);
        RuleFor(r => r.NewPassword).NotEmpty().MaximumLength(256);
    }
}

public sealed class MfaLoginRequestValidator : AbstractValidator<MfaLoginRequest>
{
    public MfaLoginRequestValidator()
    {
        RuleFor(r => r.MfaToken).NotEmpty().MaximumLength(4096);
        RuleFor(r => r.Code).NotEmpty().Matches(CodeRules.TotpOrRecovery);
    }
}

public sealed class AccountCodeRequestValidator : AbstractValidator<AccountCodeRequest>
{
    public AccountCodeRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().Matches(CodeRules.TotpOrRecovery);
    }
}
