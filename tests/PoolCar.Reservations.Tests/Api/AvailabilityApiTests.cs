using System.Net;
using System.Net.Http.Json;
using PoolCar.Reservations.Domain;
using PoolCar.Reservations.Endpoints;
using PoolCar.Reservations.Tests.Data;
using static PoolCar.Reservations.Tests.Api.TestTimes;

namespace PoolCar.Reservations.Tests.Api;

public class AvailabilityApiTests(ReservationsApiFactory api) : IClassFixture<ReservationsApiFactory>
{
    private readonly HttpClient _client = api.CreateClient();

    private async Task<ReservationResponse> BookAsync(string carId, DateOnly from, DateOnly to, DayPart dayPart)
    {
        using var client = api.CreateClient(await api.AddEmployeeAsync());
        var response = await client.PostAsJsonAsync(
            "/reservations",
            new CreateReservationRequest(carId, from, to, dayPart),
            ReservationsApiFactory.Json,
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.ReadAsync<ReservationResponse>();
    }

    private async Task BookNowAsync(string carId)
    {
        using var client = api.CreateClient(await api.AddEmployeeAsync());
        var response = await client.PostAsJsonAsync(
            "/reservations/now",
            new BookNowRequest(carId),
            ReservationsApiFactory.Json,
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string url, T request) =>
        await _client.PostAsJsonAsync(
            url, request, ReservationsApiFactory.Json, TestContext.Current.CancellationToken);

    private async Task<IReadOnlyList<string>> GetBusyAsync(
        IReadOnlyList<string> carIds,
        DateOnly from,
        DateOnly to,
        DayPart dayPart)
    {
        var response = await PostAsync("/availability/busy", new BusyCarsRequest(carIds, from, to, dayPart));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.ReadAsync<CarIdsResponse>()).CarIds;
    }

    private async Task<IReadOnlyList<string>> GetInUseAsync(IReadOnlyList<string> carIds)
    {
        var response = await PostAsync("/availability/now", new CarsNowRequest(carIds));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.ReadAsync<CarIdsResponse>()).CarIds;
    }

    private static IReadOnlyList<string> TooManyCars() =>
        [
            .. Enumerable.Range(0, AvailabilityEndpoints.MaxCarsPerRequest + 1)
                .Select(_ => ReservationsDatabase.NewCarId()),
        ];

    [Fact]
    public async Task Busy_lists_cars_that_are_reserved_in_any_requested_period()
    {
        api.StartClockAt(Morning(Today));
        var morning = ReservationsDatabase.NewCarId();
        var afternoon = ReservationsDatabase.NewCarId();
        var free = ReservationsDatabase.NewCarId();
        await BookAsync(morning, Today.AddDays(2), Today.AddDays(2), DayPart.Morning);
        await BookAsync(afternoon, Today.AddDays(1), Today.AddDays(1), DayPart.Afternoon);

        IReadOnlyList<string> cars = [morning, afternoon, free];

        var busyInMornings = await GetBusyAsync(cars, Today.AddDays(1), Today.AddDays(2), DayPart.Morning);
        var busyAllDay = await GetBusyAsync(cars, Today.AddDays(1), Today.AddDays(2), DayPart.FullDay);

        busyInMornings.ShouldBe([morning]);
        busyAllDay.ShouldBe([morning, afternoon]);
    }

    [Fact]
    public async Task Busy_ignores_cancelled_reservations()
    {
        api.StartClockAt(Morning(Today));
        var carId = ReservationsDatabase.NewCarId();
        var reservation = await BookAsync(carId, Today.AddDays(1), Today.AddDays(1), DayPart.Morning);
        using var operatorClient = api.CreateClient(await api.AddEmployeeAsync(), isOperator: true);
        var cancelled = await operatorClient.PostAsync(
            $"/reservations/{reservation.Id}/cancel", null, TestContext.Current.CancellationToken);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);

        var busy = await GetBusyAsync([carId], Today.AddDays(1), Today.AddDays(1), DayPart.Morning);

        busy.ShouldBeEmpty();
    }

    [Fact]
    public async Task Busy_includes_a_car_that_was_not_returned_when_the_request_includes_today()
    {
        api.StartClockAt(Morning(Today));
        var carId = ReservationsDatabase.NewCarId();
        await BookNowAsync(carId);
        api.Time.SetUtcNow(Morning(Today.AddDays(1)));

        var today = await GetBusyAsync([carId], Today.AddDays(1), Today.AddDays(1), DayPart.Afternoon);
        var later = await GetBusyAsync([carId], Today.AddDays(2), Today.AddDays(3), DayPart.Afternoon);

        today.ShouldBe([carId]);
        later.ShouldBeEmpty();
    }

    [Fact]
    public async Task Busy_with_too_many_cars_is_invalid()
    {
        var response = await PostAsync(
            "/availability/busy", new BusyCarsRequest(TooManyCars(), Today, Today, DayPart.Morning));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code().ShouldBe(DomainError.InvalidCode);
    }

    [Fact]
    public async Task Busy_for_more_than_fourteen_days_is_invalid()
    {
        var response = await PostAsync(
            "/availability/busy",
            new BusyCarsRequest([ReservationsDatabase.NewCarId()], Today, Today.AddDays(14), DayPart.Morning));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task In_use_lists_cars_with_a_running_or_overdue_period()
    {
        api.StartClockAt(Morning(Today.AddDays(-1)));
        var overdue = ReservationsDatabase.NewCarId();
        await BookNowAsync(overdue);
        api.StartClockAt(Morning(Today));
        var running = ReservationsDatabase.NewCarId();
        var later = ReservationsDatabase.NewCarId();
        var free = ReservationsDatabase.NewCarId();
        await BookAsync(running, Today, Today, DayPart.Morning);
        await BookAsync(later, Today, Today, DayPart.Afternoon);

        var inUse = await GetInUseAsync([running, later, overdue, free]);

        inUse.ShouldBe([running, overdue]);
    }

    [Fact]
    public async Task In_use_with_too_many_cars_is_invalid()
    {
        var response = await PostAsync("/availability/now", new CarsNowRequest(TooManyCars()));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
