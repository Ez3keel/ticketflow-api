using FluentValidation;
using TicketFlow.Application.Events.Dtos;

namespace TicketFlow.Application.Events.Validators;

public class AddSessionRequestValidator : AbstractValidator<AddSessionRequest>
{
    public AddSessionRequestValidator()
    {
        RuleFor(x => x.VenueName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StartsAtUtc).GreaterThan(DateTime.UtcNow).WithMessage("Session must start in the future.");
        RuleFor(x => x.TicketPrice).GreaterThanOrEqualTo(0);
    }
}
