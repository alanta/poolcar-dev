using System.Net;
using System.Net.Http.Json;
using PoolCar.Reservations.Domain;
using PoolCar.Reservations.Endpoints;
using PoolCar.Reservations.Tests.Data;
using static PoolCar.Reservations.Tests.Api.TestTimes;

namespace PoolCar.Reservations.Tests.Api;

public class PeriodApiTests(ReservationsApiFactory api) : IClassFixture<ReservationsApiFactory>
{
    private static async Task<ReservationResponse> BookAsync(
        HttpClient client,
        DateOnly date,
        DayPart dayPart = DayPart.Morning,
        string? carId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/reservations",
            new CreateReservationRequest(carId ?? ReservationsDatabase.NewCarId(), date, date, dayPart),
            ReservationsApiFactory.Json,
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.ReadAsync<ReservationResponse>();
    }

    private static async Task<HttpResponseMessage> BookNowAsync(HttpClient client, string carId) =>
        await client.PostAsJsonAsync(
            "/reservations/now",
            new BookNowRequest(carId),
            ReservationsApiFactory.Json,
            TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PickUpAsync(HttpClient client, Guid periodId) =>
        client.PostAsync($"/periods/{periodId}/pickup", null, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> ReturnAsync(HttpClient client, Guid periodId) =>
        client.PostAsync($"/periods/{periodId}/return", null, TestContext.Current.CancellationToken);

    private async Task<HttpClient> CreateEmployeeClientAsync() => api.CreateClient(await api.AddEmployeeAsync());

    [Fact]
    public async Task Picking_up_a_running_period_marks_it_picked_up()
    {
        var now = Morning(Today);
        api.StartClockAt(now);
        using var client = await CreateEmployeeClientAsync();
        var reservation = await BookAsync(client, Today);

        var response = await PickUpAsync(client, reservation.Periods[0].Id);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var period = (await response.ReadAsync<ReservationResponse>()).Periods.ShouldHaveSingleItem();
        period.Status.ShouldBe(PeriodStatus.PickedUp);
        period.PickedUpAt.ShouldBe(now);
    }

    [Fact]
    public async Task Picking_up_before_the_period_starts_is_a_conflict()
    {
        api.StartClockAt(Morning(Today));
        using var client = await CreateEmployeeClientAsync();
        var reservation = await BookAsync(client, Today, DayPart.Afternoon);

        var response = await PickUpAsync(client, reservation.Periods[0].Id);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Code().ShouldBe(DomainError.ConflictCode);
        problem.GetProperty("detail").GetString()
            .ShouldBe("Je kan de auto pas ophalen als je reservering begint.");
    }

    [Fact]
    public async Task Picking_up_a_picked_up_period_is_a_conflict()
    {
        api.StartClockAt(Morning(Today));
        using var client = await CreateEmployeeClientAsync();
        var reservation = await BookAsync(client, Today);
        (await PickUpAsync(client, reservation.Periods[0].Id)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PickUpAsync(client, reservation.Periods[0].Id);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Picking_up_another_employees_period_is_forbidden()
    {
        api.StartClockAt(Morning(Today));
        using var owner = await CreateEmployeeClientAsync();
        using var other = await CreateEmployeeClientAsync();
        var reservation = await BookAsync(owner, Today);

        var response = await PickUpAsync(other, reservation.Periods[0].Id);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Code().ShouldBe(DomainError.NotAllowedCode);
    }

    [Fact]
    public async Task An_operator_can_pick_up_and_return_any_period()
    {
        api.StartClockAt(Morning(Today));
        using var owner = await CreateEmployeeClientAsync();
        using var operatorClient = api.CreateClient(await api.AddEmployeeAsync(), isOperator: true);
        var reservation = await BookAsync(owner, Today);

        var pickUp = await PickUpAsync(operatorClient, reservation.Periods[0].Id);
        var returned = await ReturnAsync(operatorClient, reservation.Periods[0].Id);

        pickUp.StatusCode.ShouldBe(HttpStatusCode.OK);
        returned.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Picking_up_an_unknown_period_returns_not_found()
    {
        using var client = await CreateEmployeeClientAsync();

        var response = await PickUpAsync(client, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Picking_up_without_an_employee_header_is_unauthorized()
    {
        var response = await PickUpAsync(api.CreateClient(), Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Returning_after_the_period_ended_keeps_the_period_and_clears_overdue()
    {
        api.StartClockAt(Morning(Today));
        using var client = await CreateEmployeeClientAsync();
        var reservation = await BookAsync(client, Today);
        var periodId = reservation.Periods[0].Id;
        (await PickUpAsync(client, periodId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var later = Afternoon(Today);
        api.Time.SetUtcNow(later);
        var overdue = await api.CreateClient().GetFromJsonAsync<ReservationResponse>(
            $"/reservations/{reservation.Id}", ReservationsApiFactory.Json, TestContext.Current.CancellationToken);
        overdue!.Periods[0].Overdue.ShouldBeTrue();

        var response = await ReturnAsync(client, periodId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var period = (await response.ReadAsync<ReservationResponse>()).Periods.ShouldHaveSingleItem();
        period.Status.ShouldBe(PeriodStatus.Returned);
        period.ReturnedAt.ShouldBe(later);
        period.End.ShouldBe(DayParts.ToPeriod(Today, DayPart.Morning).End);
        period.Overdue.ShouldBeFalse();
    }

    [Fact]
    public async Task Returning_early_frees_the_rest_of_the_period()
    {
        var start = Morning(Today);
        api.StartClockAt(start);
        var carId = ReservationsDatabase.NewCarId();
        using var first = await CreateEmployeeClientAsync();
        using var second = await CreateEmployeeClientAsync();
        var booked = await (await BookNowAsync(first, carId)).ReadAsync<ReservationResponse>();
        (await BookNowAsync(second, carId)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var returnedAt = start.AddMinutes(30);
        api.Time.SetUtcNow(returnedAt);

        var response = await ReturnAsync(first, booked.Periods[0].Id);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var period = (await response.ReadAsync<ReservationResponse>()).Periods.ShouldHaveSingleItem();
        period.End.ShouldBe(returnedAt);
        api.Time.Advance(TimeSpan.FromMinutes(1));
        var rest = await BookNowAsync(second, carId);
        rest.StatusCode.ShouldBe(HttpStatusCode.Created);
        var restPeriod = (await rest.ReadAsync<ReservationResponse>()).Periods.ShouldHaveSingleItem();
        restPeriod.Start.ShouldBe(returnedAt.AddMinutes(1));
        restPeriod.End.ShouldBe(DayParts.ToPeriod(Today, DayPart.Morning).End);
    }

    [Fact]
    public async Task Returning_a_car_that_was_not_picked_up_is_a_conflict()
    {
        api.StartClockAt(Morning(Today));
        using var client = await CreateEmployeeClientAsync();
        var reservation = await BookAsync(client, Today);

        var response = await ReturnAsync(client, reservation.Periods[0].Id);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Code().ShouldBe(DomainError.ConflictCode);
    }

    [Fact]
    public async Task Returning_another_employees_car_is_forbidden()
    {
        api.StartClockAt(Morning(Today));
        using var owner = await CreateEmployeeClientAsync();
        using var other = await CreateEmployeeClientAsync();
        var reservation = await BookAsync(owner, Today);
        (await PickUpAsync(owner, reservation.Periods[0].Id)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await ReturnAsync(other, reservation.Periods[0].Id);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Returning_an_unknown_period_returns_not_found()
    {
        using var client = await CreateEmployeeClientAsync();

        var response = await ReturnAsync(client, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Returning_without_an_employee_header_is_unauthorized()
    {
        var response = await ReturnAsync(api.CreateClient(), Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
