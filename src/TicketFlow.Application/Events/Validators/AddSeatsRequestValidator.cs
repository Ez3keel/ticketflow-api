using FluentValidation;
using TicketFlow.Application.Events.Dtos;

namespace TicketFlow.Application.Events.Validators;

public class AddSeatsRequestValidator : AbstractValidator<AddSeatsRequest>
{
    public AddSeatsRequestValidator()
    {
        RuleFor(x => x.Row).NotEmpty().MaximumLength(5);
        RuleFor(x => x.FromNumber).GreaterThan(0);
        RuleFor(x => x.ToNumber).GreaterThanOrEqualTo(x => x.FromNumber);
        RuleFor(x => x.ToNumber - x.FromNumber)
            .LessThanOrEqualTo(199)
            .WithMessage("Cannot add more than 200 seats in a single request.");
    }
}
