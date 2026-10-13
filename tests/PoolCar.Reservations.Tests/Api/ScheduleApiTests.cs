using System.Net;
using System.Net.Http.Json;
using PoolCar.Reservations.Domain;
using PoolCar.Reservations.Endpoints;
using PoolCar.Reservations.Tests.Data;
using static PoolCar.Reservations.Tests.Api.TestTimes;

namespace PoolCar.Reservations.Tests.Api;

public class ScheduleApiTests(ReservationsApiFactory api) : IClassFixture<ReservationsApiFactory>
{
    private static async Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string url, T request) =>
        await client.PostAsJsonAsync(
            url, request, ReservationsApiFactory.Json, TestContext.Current.CancellationToken);

    private async Task<HttpClient> CreateEmployeeClientAsync(string name = "Sanne de Vries") =>
        api.CreateClient(await api.AddEmployeeAsync(name));

    private static async Task<ReservationResponse> BookAsync(
        HttpClient client,
        string carId,
        DateOnly from,
        DateOnly to,
        DayPart dayPart)
    {
        var response = await PostAsync(
            client, "/reservations", new CreateReservationRequest(carId, from, to, dayPart));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.ReadAsync<ReservationResponse>();
    }

    private async Task<IReadOnlyList<ScheduleEntryResponse>> GetScheduleAsync(
        IReadOnlyList<string> carIds,
        DateOnly from,
        DateOnly to)
    {
        var response = await PostAsync(api.CreateClient(), "/schedule", new ScheduleRequest(carIds, from, to));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.ReadAsync<List<ScheduleEntryResponse>>();
    }

    [Fact]
    public async Task Schedule_lists_the_periods_of_the_cars_on_the_dates()
    {
        api.StartClockAt(Morning(Today));
        var carId = ReservationsDatabase.NewCarId();
        var otherCar = ReservationsDatabase.NewCarId();
        using var sanne = await CreateEmployeeClientAsync("Sanne de Vries");
        using var daan = await CreateEmployeeClientAsync("Daan Bakker");
        var booked = await BookAsync(sanne, carId, Today.AddDays(1), Today.AddDays(3), DayPart.Afternoon);
        var cancelled = await BookAsync(daan, carId, Today.AddDays(2), Today.AddDays(2), DayPart.Morning);
        (await daan.PostAsync($"/reservations/{cancelled.Id}/cancel", null, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await BookAsync(daan, otherCar, Today.AddDays(1), Today.AddDays(1), DayPart.Morning);

        var schedule = await GetScheduleAsync([carId], Today.AddDays(2), Today.AddDays(5));

        schedule.Count.ShouldBe(2);
        var first = schedule[0];
        var (start, end) = DayParts.ToPeriod(Today.AddDays(2), DayPart.Afternoon);
        first.ShouldBe(new ScheduleEntryResponse(
            booked.Id,
            booked.Periods[1].Id,
            carId,
            Today.AddDays(2),
            DayPart.Afternoon,
            start,
            end,
            PeriodStatus.Scheduled,
            "Sanne",
            NotPickedUp: false,
            Overdue: false,
            AtRisk: false));
        schedule[1].Date.ShouldBe(Today.AddDays(3));
    }

    [Fact]
    public async Task Schedule_marks_a_future_booking_of_an_overdue_car_at_risk()
    {
        api.StartClockAt(Morning(Today));
        var carId = ReservationsDatabase.NewCarId();
        var otherCar = ReservationsDatabase.NewCarId();
        using var first = await CreateEmployeeClientAsync();
        using var second = await CreateEmployeeClientAsync("Daan Bakker");
        (await PostAsync(first, "/reservations/now", new BookNowRequest(carId))).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        await BookAsync(second, otherCar, Today, Today, DayPart.Morning);
        api.Time.SetUtcNow(Afternoon(Today));
        await BookAsync(second, carId, Today.AddDays(1), Today.AddDays(1), DayPart.Morning);

        var schedule = await GetScheduleAsync([carId, otherCar], Today, Today.AddDays(1));

        var overdue = schedule.Single(e => e.CarId == carId && e.Date == Today);
        overdue.Status.ShouldBe(PeriodStatus.PickedUp);
        overdue.Overdue.ShouldBeTrue();
        overdue.AtRisk.ShouldBeFalse();
        var atRisk = schedule.Single(e => e.CarId == carId && e.Date == Today.AddDays(1));
        atRisk.AtRisk.ShouldBeTrue();
        atRisk.EmployeeFirstName.ShouldBe("Daan");
        var notPickedUp = schedule.Single(e => e.CarId == otherCar);
        notPickedUp.NotPickedUp.ShouldBeTrue();
        notPickedUp.AtRisk.ShouldBeFalse();
    }

    [Fact]
    public async Task Schedule_for_too_many_days_is_invalid()
    {
        var response = await PostAsync(
            api.CreateClient(),
            "/schedule",
            new ScheduleRequest([ReservationsDatabase.NewCarId()], Today, Today.AddDays(ScheduleEndpoints.MaxDays)));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code().ShouldBe(DomainError.InvalidCode);
    }

    [Fact]
    public async Task Schedule_for_too_many_cars_is_invalid()
    {
        var carIds = Enumerable.Range(0, AvailabilityEndpoints.MaxCarsPerRequest + 1)
            .Select(_ => ReservationsDatabase.NewCarId())
            .ToList();

        var response = await PostAsync(api.CreateClient(), "/schedule", new ScheduleRequest(carIds, Today, Today));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
