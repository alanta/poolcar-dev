using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoolCar.Web.Clients;
using PoolCar.Web.Formatting;
using PoolCar.Web.Htmx;
using PoolCar.Web.MyReservations;

namespace PoolCar.Web.Pages;

public class MijnReserveringenModel(
    ReservationsApiClient reservationsApiClient,
    FleetApiClient fleetApiClient,
    TimeProvider timeProvider) : PageModel
{
    public const string Path = "/mijn-reserveringen";

    public ReservationOverview Overview { get; private set; } = ReservationOverview.Empty;

    /// <summary>The reservation that asks to confirm cancelling, when the page is opened without JavaScript.</summary>
    public Guid? ConfirmCancelId { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    /// <summary>Asks to confirm cancelling: the card for htmx, otherwise the page with the confirmation in the card.</summary>
    public async Task<IActionResult> OnGetCancelAsync(Guid id)
    {
        if (!Request.IsHtmxPartial())
        {
            ConfirmCancelId = id;
            await LoadAsync();
            return Page();
        }

        var card = await GetCardAsync(id);
        return card is null ? NotFound() : Partial(ReservationCardView.Partial, new ReservationCardView(card, card.CanCancel));
    }

    /// <summary>The card without confirmation, after choosing Nee.</summary>
    public async Task<IActionResult> OnGetCardAsync(Guid id)
    {
        if (!Request.IsHtmxPartial())
        {
            return Redirect(CardUrl(id));
        }

        var card = await GetCardAsync(id);
        return card is null ? NotFound() : Partial(ReservationCardView.Partial, new ReservationCardView(card));
    }

    public Task<IActionResult> OnPostPickUpAsync(Guid id, Guid periodId) => ActAsync(
        id,
        cancellationToken => reservationsApiClient.PickUpAsync(periodId, cancellationToken),
        card => $"Goede reis! Je hebt de {card.Car} opgehaald.");

    public Task<IActionResult> OnPostReturnAsync(Guid id, Guid periodId) => ActAsync(
        id,
        cancellationToken => reservationsApiClient.ReturnAsync(periodId, cancellationToken),
        card => $"Bedankt! Je hebt de {card.Car} ingeleverd.");

    public Task<IActionResult> OnPostCancelAsync(Guid id) => ActAsync(
        id,
        cancellationToken => reservationsApiClient.CancelReservationAsync(id, cancellationToken),
        card => $"Je reservering van de {card.Car} is geannuleerd.");

    public static string CardUrl(Guid id) => $"{Path}#reservering-{id}";

    private async Task<IActionResult> ActAsync(
        Guid id,
        Func<CancellationToken, Task<ApiResult<Reservation>>> action,
        Func<ReservationCard, string> successMessage)
    {
        var cancellationToken = HttpContext.RequestAborted;
        var result = await action(cancellationToken);
        if (result.IsSuccess)
        {
            var card = await CreateCardAsync(result.Value, cancellationToken);
            var success = Toast.Success(successMessage(card));
            if (Request.IsHtmxPartial())
            {
                return Partial(ReservationCardView.Partial, new ReservationCardView(card, Toast: success));
            }

            success.Store(TempData);
            return Redirect(CardUrl(id));
        }

        var error = Toast.Error(result.Problem.Detail);
        if (!Request.IsHtmxPartial())
        {
            error.Store(TempData);
            return Redirect(CardUrl(id));
        }

        var status = result.Problem.IsConflict || result.Problem.IsInvalid
            ? result.Problem.Status
            : StatusCodes.Status200OK;
        var current = await GetCardAsync(id);
        if (current is null)
        {
            // Keep the card as it is and only show the message
            Response.Headers["HX-Reswap"] = "none";
            return this.ToastResult(error, status);
        }

        return this.Partial(ReservationCardView.Partial, new ReservationCardView(current, Toast: error), status);
    }

    private async Task LoadAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var reservations = await reservationsApiClient.GetMyReservationsAsync(cancellationToken);
        var plates = reservations.Select(r => r.CarId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var cars = plates.Count == 0 ? [] : await fleetApiClient.GetCarsByPlatesAsync(plates, cancellationToken);
        Overview = ReservationOverview.Create(
            reservations,
            cars,
            timeProvider.GetUtcNow(),
            AmsterdamTime.Today(timeProvider));
    }

    private async Task<ReservationCard?> GetCardAsync(Guid id)
    {
        var cancellationToken = HttpContext.RequestAborted;
        Reservation? reservation;
        try
        {
            reservation = await reservationsApiClient.GetReservationAsync(id, cancellationToken);
        }
        catch (ApiProblemException)
        {
            return null;
        }

        return reservation is null ? null : await CreateCardAsync(reservation, cancellationToken);
    }

    private async Task<ReservationCard> CreateCardAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        var cars = await fleetApiClient.GetCarsByPlatesAsync([reservation.CarId], cancellationToken);
        return ReservationOverview.CreateCard(
            reservation,
            cars.FirstOrDefault(),
            timeProvider.GetUtcNow(),
            AmsterdamTime.Today(timeProvider));
    }
}
