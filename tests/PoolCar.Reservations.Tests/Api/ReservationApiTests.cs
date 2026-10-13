using System.Net;
using System.Net.Http.Json;
using PoolCar.Reservations.Domain;
using PoolCar.Reservations.Endpoints;
using PoolCar.Reservations.Tests.Data;
using static PoolCar.Reservations.Tests.Api.TestTimes;

namespace PoolCar.Reservations.Tests.Api;

public class ReservationApiTests(ReservationsApiFactory api) : IClassFixture<ReservationsApiFactory>
{
    private static async Task<HttpResponseMessage> BookAsync(
        HttpClient client,
        string carId,
        DateOnly from,
        DateOnly to,
        DayPart dayPart) =>
        await client.PostAsJsonAsync(
            "/reservations",
            new CreateReservationRequest(carId, from, to, dayPart),
            ReservationsApiFactory.Json,
            TestContext.Current.CancellationToken);

    private static async Task<ReservationResponse> BookCreatedAsync(
        HttpClient client,
        DateOnly from,
        DateOnly to,
        DayPart dayPart = DayPart.Morning)
    {
        var response = await BookAsync(client, ReservationsDatabase.NewCarId(), from, to, dayPart);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.ReadAsync<ReservationResponse>();
    }

    private static async Task<HttpResponseMessage> BookNowAsync(HttpClient client, string carId) =>
        await client.PostAsJsonAsync(
            "/reservations/now",
            new BookNowRequest(carId),
            ReservationsApiFactory.Json,
            TestContext.Current.CancellationToken);

    private async Task<HttpClient> CreateEmployeeClientAsync() => api.CreateClient(await api.AddEmployeeAsync());

    [Fact]
    public async Task Booking_creates_a_period_for_every_date()
    {
        api.StartClockAt(Morning(Today));
        using var client = await CreateEmployeeClientAsync();
        var carId = ReservationsDatabase.NewCarId();

        var response = await BookAsync(client, carId, Today.AddDays(1), Today.AddDays(3), DayPart.Afternoon);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reservation = await response.ReadAsync<ReservationResponse>();
        response.Headers.Location!.ToString().ShouldBe($"/reservations/{reservation.Id}");
        reservation.CarId.ShouldBe(carId);
        reservation.Source.ShouldBe(ReservationSource.User);
        reservation.Periods.Select(p => p.Date).ShouldBe([Today.AddDays(1), Today.AddDays(2), Today.AddDays(3)]);
        reservation.Periods.ShouldAllBe(p => p.Status == PeriodStatus.Scheduled && !p.NotPickedUp && !p.Overdue);
        var (start, end) = DayParts.ToPeriod(Today.AddDays(1), DayPart.Afternoon);
        reservation.Periods[0].Start.ShouldBe(start);
        reservation.Periods[0].End.ShouldBe(end);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.ShouldContain("\"dayPart\":\"Afternoon\"");
        json.ShouldContain("\"status\":\"Scheduled\"");
        json.ShouldContain($"\"from\":\"{Today.AddDays(1):yyyy-MM-dd}\"");
    }

    [Fact]
    public async Task Booking_without_an_employee_header_is_unauthorized()
    {
        using var client = api.CreateClient();

        var response = await BookAsync(client, ReservationsDatabase.NewCarId(), Today, Today, DayPart.Morning);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).Code().ShouldBe(ApiProblems.UnauthorizedCode);
    }

    [Fact]
    public async Task Booking_with_an_invalid_employee_header_is_unauthorized()
    {
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(ActingUser.EmployeeHeader, "sanne");

        var response = await BookAsync(client, ReservationsDatabase.NewCarId(), Today, Today, DayPart.Morning);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Booking_for_an_unknown_employee_is_invalid()
    {
        api.StartClockAt(Morning(Today));
        using var client = api.CreateClient(Guid.NewGuid());

        var response = await BookAsync(client, ReservationsDatabase.NewCarId(), Today, Today, DayPart.Morning);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Booking_an_overlapping_period_is_refused_with_the_conflicting_dates()
    {
        api.StartClockAt(Morning(Today));
        var carId = ReservationsDatabase.NewCarId();
        using var first = await CreateEmployeeClientAsync();
        using var second = await CreateEmployeeClientAsync();
        (await BookAsync(first, carId, Today.AddDays(2), Today.AddDays(3), DayPart.Morning))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await BookAsync(second, carId, Today.AddDays(1), Today.AddDays(4), DayPart.FullDay);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Code().ShouldBe(DomainError.ConflictCode);
        problem.ConflictingDates().ShouldBe([Today.AddDays(2), Today.AddDays(3)]);
        problem.GetProperty("detail").GetString()!.ShouldStartWith("De auto is al gereserveerd op");
    }

    [Fact]
    public async Task Booking_an_adjacent_day_part_succeeds()
    {
        api.StartClockAt(Morning(Today));
        var carId = ReservationsDatabase.NewCarId();
        using var first = await CreateEmployeeClientAsync();
        using var second = await CreateEmployeeClientAsync();
        (await BookAsync(first, carId, Today.AddDays(1), Today.AddDays(1), DayPart.Morning))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await BookAsync(second, carId, Today.AddDays(1), Today.AddDays(1), DayPart.Afternoon);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Booking_more_than_fourteen_days_is_invalid()
    {
        api.StartClockAt(Morning(Today));
        using var client = await CreateEmployeeClientAsync();

        var response = await BookAsync(
            client, ReservationsDatabase.NewCarId(), Today.AddDays(1), Today.AddDays(15), DayPart.FullDay);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.Code().ShouldBe(DomainError.InvalidCode);
        problem.GetProperty("detail").GetString().ShouldBe("Je kan maximaal 14 dagen reserveren.");
    }

    [Fact]
    public async Task Booking_now_reserves_the_rest_of_the_day_part_and_picks_up_the_car()
    {
        var now = Morning(Today);
        api.StartClockAt(now);
        using var client = await CreateEmployeeClientAsync();

        var response = await BookNowAsync(client, ReservationsDatabase.NewCarId());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reservation = await response.ReadAsync<ReservationResponse>();
        reservation.DayPart.ShouldBe(DayPart.Morning);
        var period = reservation.Periods.ShouldHaveSingleItem();
        period.Status.ShouldBe(PeriodStatus.PickedUp);
        period.PickedUpAt.ShouldBe(now);
        period.Start.ShouldBe(now);
        period.End.ShouldBe(DayParts.ToPeriod(Today, DayPart.Morning).End);
    }

    [Fact]
    public async Task Booking_now_outside_day_part_hours_is_invalid()
    {
        api.StartClockAt(OutsideHours(Today));
        using var client = await CreateEmployeeClientAsync();

        var response = await BookNowAsync(client, ReservationsDatabase.NewCarId());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code().ShouldBe(DomainError.InvalidCode);
    }

    [Fact]
    public async Task Booking_now_a_reserved_car_is_refused()
    {
        api.StartClockAt(Afternoon(Today));
        var carId = ReservationsDatabase.NewCarId();
        using var first = await CreateEmployeeClientAsync();
        using var second = await CreateEmployeeClientAsync();
        (await BookAsync(first, carId, Today, Today, DayPart.Afternoon)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await BookNowAsync(second, carId);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ConflictingDates().ShouldBe([Today]);
    }

    [Fact]
    public async Task Booking_today_a_car_that_was_not_returned_is_refused()
    {
        api.StartClockAt(Morning(Today));
        var carId = ReservationsDatabase.NewCarId();
        using var first = await CreateEmployeeClientAsync();
        using var second = await CreateEmployeeClientAsync();
        (await BookNowAsync(first, carId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        api.Time.SetUtcNow(Morning(Today.AddDays(1)));

        var today = await BookAsync(second, carId, Today.AddDays(1), Today.AddDays(1), DayPart.Afternoon);
        var later = await BookAsync(second, carId, Today.AddDays(2), Today.AddDays(2), DayPart.Afternoon);

        today.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await today.ReadProblemAsync()).ConflictingDates().ShouldBe([Today.AddDays(1)]);
        later.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task My_reservations_lists_recent_and_future_reservations_of_the_employee()
    {
        var ct = TestContext.Current.CancellationToken;
        api.StartClockAt(Morning(Today));
        using var client = await CreateEmployeeClientAsync();
        using var other = await CreateEmployeeClientAsync();
        await BookCreatedAsync(client, Today.AddDays(-31), Today.AddDays(-31));
        var recent = await BookCreatedAsync(client, Today.AddDays(-30), Today.AddDays(-30));
        var future = await BookCreatedAsync(client, Today.AddDays(5), Today.AddDays(6));
        await BookCreatedAsync(other, Today.AddDays(5), Today.AddDays(5));

        var mine = await client.GetFromJsonAsync<List<ReservationResponse>>(
            "/reservations/mine", ReservationsApiFactory.Json, ct);

        mine.ShouldNotBeNull();
        mine.Select(r => r.Id).ShouldBe([recent.Id, future.Id]);
        mine[0].NotPickedUp.ShouldBeTrue();
        mine[0].Periods.ShouldHaveSingleItem().NotPickedUp.ShouldBeTrue();
        mine[1].Periods.Count.ShouldBe(2);
    }

    [Fact]
    public async Task My_reservations_without_an_employee_header_is_unauthorized()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/reservations/mine", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Getting_a_reservation_returns_it()
    {
        var ct = TestContext.Current.CancellationToken;
        api.StartClockAt(Morning(Today));
        using var client = await CreateEmployeeClientAsync();
        var created = await BookCreatedAsync(client, Today, Today);

        api.Time.SetUtcNow(Afternoon(Today));
        var response = await api.CreateClient().GetAsync($"/reservations/{created.Id}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reservation = await response.ReadAsync<ReservationResponse>();
        reservation.Id.ShouldBe(created.Id);
        reservation.NotPickedUp.ShouldBeTrue();
        reservation.Periods.ShouldHaveSingleItem().NotPickedUp.ShouldBeTrue();
    }

    [Fact]
    public async Task Getting_an_unknown_reservation_returns_not_found()
    {
        var response = await api.CreateClient().GetAsync(
            $"/reservations/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancelling_frees_the_car()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = Morning(Today);
        api.StartClockAt(now);
        var carId = ReservationsDatabase.NewCarId();
        using var client = await CreateEmployeeClientAsync();
        using var other = await CreateEmployeeClientAsync();
        var created = await (await BookAsync(client, carId, Today.AddDays(1), Today.AddDays(2), DayPart.FullDay))
            .ReadAsync<ReservationResponse>();

        var response = await client.PostAsync($"/reservations/{created.Id}/cancel", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelled = await response.ReadAsync<ReservationResponse>();
        cancelled.CancelledAt.ShouldBe(now);
        cancelled.Periods.ShouldAllBe(p => p.Status == PeriodStatus.Cancelled);
        (await BookAsync(other, carId, Today.AddDays(1), Today.AddDays(2), DayPart.FullDay))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cancelling_another_employees_reservation_is_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        api.StartClockAt(Morning(Today));
        using var owner = await CreateEmployeeClientAsync();
        using var other = await CreateEmployeeClientAsync();
        var created = await BookCreatedAsync(owner, Today.AddDays(1), Today.AddDays(1));

        var response = await other.PostAsync($"/reservations/{created.Id}/cancel", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Code().ShouldBe(DomainError.NotAllowedCode);
    }

    [Fact]
    public async Task An_operator_can_cancel_any_reservation()
    {
        var ct = TestContext.Current.CancellationToken;
        api.StartClockAt(Morning(Today));
        using var owner = await CreateEmployeeClientAsync();
        using var operatorClient = api.CreateClient(await api.AddEmployeeAsync(), isOperator: true);
        var created = await BookCreatedAsync(owner, Today.AddDays(1), Today.AddDays(1));

        var response = await operatorClient.PostAsync($"/reservations/{created.Id}/cancel", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cancelling_a_cancelled_reservation_is_a_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        api.StartClockAt(Morning(Today));
        using var client = await CreateEmployeeClientAsync();
        var created = await BookCreatedAsync(client, Today.AddDays(1), Today.AddDays(1));
        (await client.PostAsync($"/reservations/{created.Id}/cancel", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await client.PostAsync($"/reservations/{created.Id}/cancel", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.Code().ShouldBe(DomainError.ConflictCode);
        problem.GetProperty("detail").GetString().ShouldBe("Er valt niets meer te annuleren.");
    }

    [Fact]
    public async Task Cancelling_an_unknown_reservation_returns_not_found()
    {
        using var client = await CreateEmployeeClientAsync();

        var response = await client.PostAsync(
            $"/reservations/{Guid.NewGuid()}/cancel", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancelling_without_an_employee_header_is_unauthorized()
    {
        var response = await api.CreateClient().PostAsync(
            $"/reservations/{Guid.NewGuid()}/cancel", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
