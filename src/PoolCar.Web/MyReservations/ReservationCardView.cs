using PoolCar.Web.Htmx;

namespace PoolCar.Web.MyReservations;

/// <summary>The model of the reservation card partial: the card, whether it asks to confirm cancelling, and a
/// toast to show next to it after an action.</summary>
public sealed record ReservationCardView(ReservationCard Card, bool ConfirmCancel = false, Toast? Toast = null)
{
    public const string Partial = "_ReservationCard";
}
