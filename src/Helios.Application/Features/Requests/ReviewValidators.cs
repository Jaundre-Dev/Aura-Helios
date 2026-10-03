using FluentValidation;
using Helios.Contracts.Requests;

namespace Helios.Application.Features.Requests;

public sealed class SubmitReviewRequestValidator : AbstractValidator<SubmitReviewRequest>
{
    public SubmitReviewRequestValidator()
    {
        RuleFor(r => r.Decision).IsInEnum();
        RuleFor(r => r.Reason).MaximumLength(500);
    }
}
