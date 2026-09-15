using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketFlow.Api.Common;
using TicketFlow.Api.RateLimiting;
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
    [EnableRateLimiting(RateLimitingPolicies.Reserve)]
    public async Task<ActionResult<OrderDto>> Reserve(ReserveSeatsRequest request, CancellationToken cancellationToken)
    {
        var validation = await _reserveValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToProblemDetails());

        var order = await _reservationService.ReserveSeatsAsync(User.GetUserId(), request, cancellationToken);
        return Ok(order);
    }

    [HttpGet("{orderId:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid orderId, CancellationToken cancellationToken)
        => Ok(await _reservationService.GetOrderAsync(User.GetUserId(), orderId, cancellationToken));

    // Returns 202: the order is only marked Processing here. A worker consuming
    // RabbitMQ does the actual confirmation (simulated payment + seats -> Sold +
    // order -> Confirmed) asynchronously -- poll GET /orders/{id} to see it land.
    [HttpPost("{orderId:guid}/confirm")]
    public async Task<ActionResult<OrderDto>> Confirm(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _reservationService.RequestConfirmationAsync(User.GetUserId(), orderId, cancellationToken);
        return Accepted(order);
    }

    [HttpPost("{orderId:guid}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(Guid orderId, CancellationToken cancellationToken)
        => Ok(await _reservationService.CancelOrderAsync(User.GetUserId(), orderId, cancellationToken));
}
