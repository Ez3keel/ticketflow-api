using FluentValidation;
using TicketFlow.Application.Reservations.Dtos;

namespace TicketFlow.Application.Reservations.Validators;

public class ReserveSeatsRequestValidator : AbstractValidator<ReserveSeatsRequest>
{
    public ReserveSeatsRequestValidator()
    {
        RuleFor(x => x.SeatIds).NotEmpty().WithMessage("At least one seat must be selected.");
        RuleFor(x => x.SeatIds.Count).LessThanOrEqualTo(10).WithMessage("Cannot reserve more than 10 seats per order.");
    }
}
