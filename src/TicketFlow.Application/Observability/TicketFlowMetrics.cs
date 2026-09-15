using System.Diagnostics.Metrics;

namespace TicketFlow.Application.Observability;

public class TicketFlowMetrics
{
    public const string MeterName = "TicketFlow";

    private readonly Counter<long> _seatsReserved;
    private readonly Counter<long> _seatLockRejections;
    private readonly Counter<long> _ordersConfirmed;
    private readonly Histogram<double> _reservationDurationMs;

    public TicketFlowMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _seatsReserved = meter.CreateCounter<long>(
            "ticketflow.seats.reserved", unit: "seats",
            description: "Seats successfully reserved.");

        _seatLockRejections = meter.CreateCounter<long>(
            "ticketflow.seats.lock_rejections", unit: "requests",
            description: "Reservation attempts rejected because another request already held the seat's lock.");

        _ordersConfirmed = meter.CreateCounter<long>(
            "ticketflow.orders.confirmed", unit: "orders",
            description: "Orders confirmed by the worker after simulated payment processing.");

        _reservationDurationMs = meter.CreateHistogram<double>(
            "ticketflow.reservation.duration", unit: "ms",
            description: "Time to acquire locks, validate availability and persist a reservation.");
    }

    public void RecordSeatsReserved(int count) => _seatsReserved.Add(count);

    public void RecordSeatLockRejection() => _seatLockRejections.Add(1);

    public void RecordOrderConfirmed() => _ordersConfirmed.Add(1);

    public void RecordReservationDuration(double milliseconds) => _reservationDurationMs.Record(milliseconds);
}
