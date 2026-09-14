using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketFlow.Api.Common;
using TicketFlow.Application.Reservations;
using TicketFlow.Application.Reservations.Dtos;

namespace TicketFlow.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly ReservationService _reservationService;
    private readonly IValidator<ReserveSeatsRequest> _reserveValidator;

    public OrdersController(ReservationService reservationService, IValidator<ReserveSeatsRequest> reserveValidator)
    {
        _reservationService = reservationService;
        _reserveValidator = reserveValidator;
    }

    [HttpPost("reserve")]
    public async Task<ActionResult<OrderDto>> Reserve(ReserveSeatsRequest request, CancellationToken cancellationToken)
    {
        var validation = await _reserveValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToProblemDetails());

        var order = await _reservationService.ReserveSeatsAsync(User.GetUserId(), request, cancellationToken);
        return Ok(order);
    }

    [HttpPost("{orderId:guid}/confirm")]
    public async Task<ActionResult<OrderDto>> Confirm(Guid orderId, CancellationToken cancellationToken)
        => Ok(await _reservationService.ConfirmOrderAsync(User.GetUserId(), orderId, cancellationToken));

    [HttpPost("{orderId:guid}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(Guid orderId, CancellationToken cancellationToken)
        => Ok(await _reservationService.CancelOrderAsync(User.GetUserId(), orderId, cancellationToken));
}
