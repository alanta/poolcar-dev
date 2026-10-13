using System.Net;
using System.Text.Json;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public class ReservationsApiClientTests
{
    private const string EmployeeJson = """
        {"id":"f0671165-c417-421a-9eac-649e8534de6e","name":"Bram Testbeek","homeOffice":"utrecht","kind":"Seeded"}
        """;

    private const string ReservationJson = """
        {"id":"11111111-1111-1111-1111-111111111111","employeeId":"f0671165-c417-421a-9eac-649e8534de6e",
         "carId":"HPN72V","from":"2026-10-13","to":"2026-10-14","dayPart":"Morning",
         "createdAt":"2026-10-12T08:00:00+00:00","cancelledAt":null,"notPickedUp":false,"overdue":false,
         "periods":[{"id":"22222222-2222-2222-2222-222222222222","date":"2026-10-13",
           "start":"2026-10-13T06:00:00+00:00","end":"2026-10-13T10:00:00+00:00","status":"Scheduled",
           "pickedUpAt":null,"returnedAt":null,"notPickedUp":false,"overdue":false}]}
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetEmployeeAsync_ReturnsEmployee()
    {
        var handler = new StubHttpHandler().OnJson(HttpMethod.Get, $"/employees/{TestPrincipals.KnownId}", EmployeeJson);

        var employee = await CreateClient(handler).GetEmployeeAsync(TestPrincipals.KnownId, Token);

        employee.ShouldBe(new Employee(TestPrincipals.KnownId, "Bram Testbeek", "utrecht", EmployeeKind.Seeded));
    }

    [Fact]
    public async Task GetEmployeeAsync_WhenNotFound_ReturnsNull()
    {
        var employee = await CreateClient(new StubHttpHandler()).GetEmployeeAsync(TestPrincipals.UnknownId, Token);

        employee.ShouldBeNull();
    }

    [Fact]
    public async Task GetEmployeeAsync_WhenServerFails_Throws()
    {
        var handler = new StubHttpHandler().On(
            HttpMethod.Get,
            $"/employees/{TestPrincipals.KnownId}",
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var exception = await Should.ThrowAsync<ApiProblemException>(
            () => CreateClient(handler).GetEmployeeAsync(TestPrincipals.KnownId, Token));

        exception.Problem.Status.ShouldBe(500);
        exception.Problem.Detail.ShouldBe(ApiProblem.FallbackDetail);
    }

    [Fact]
    public async Task SaveEmployeeAsync_PutsNameAndHomeOffice()
    {
        var handler = new StubHttpHandler().OnJson(HttpMethod.Put, $"/employees/{TestPrincipals.KnownId}", EmployeeJson);

        var result = await CreateClient(handler).SaveEmployeeAsync(TestPrincipals.KnownId, "Bram Testbeek", "utrecht", Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Bram Testbeek");
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Put);
        request.Body.ShouldBe("""{"name":"Bram Testbeek","homeOffice":"utrecht"}""");
    }

    [Fact]
    public async Task ChangeHomeOfficeAsync_KeepsName()
    {
        var handler = new StubHttpHandler().OnJson(HttpMethod.Put, $"/employees/{TestPrincipals.KnownId}", EmployeeJson);
        var employee = new Employee(TestPrincipals.KnownId, "Bram Testbeek", "utrecht", EmployeeKind.Seeded);

        await CreateClient(handler).ChangeHomeOfficeAsync(employee, "amsterdam", Token);

        handler.Requests.ShouldHaveSingleItem().Body.ShouldBe("""{"name":"Bram Testbeek","homeOffice":"amsterdam"}""");
    }

    [Fact]
    public async Task CreateReservationAsync_SendsDayPartAsText_AndReadsReservation()
    {
        var handler = new StubHttpHandler().OnJson(HttpMethod.Post, "/reservations", ReservationJson, HttpStatusCode.Created);

        var result = await CreateClient(handler).CreateReservationAsync(
            new CreateReservationRequest("HPN72V", new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14), DayPart.Morning),
            Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DayPart.ShouldBe(DayPart.Morning);
        var period = result.Value.Periods.ShouldHaveSingleItem();
        period.Date.ShouldBe(new DateOnly(2026, 10, 13));
        period.Status.ShouldBe(PeriodStatus.Scheduled);
        using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
        body.RootElement.GetProperty("dayPart").GetString().ShouldBe("Morning");
        body.RootElement.GetProperty("from").GetString().ShouldBe("2026-10-13");
    }

    [Fact]
    public async Task CreateReservationAsync_WhenConflict_ReturnsProblemWithConflictingDates()
    {
        var handler = new StubHttpHandler().On(
            HttpMethod.Post,
            "/reservations",
            _ => StubHttpHandler.Problem(
                HttpStatusCode.Conflict,
                "conflict",
                "Deze auto is al gereserveerd.",
                ""","conflictingDates":["2026-10-13"]"""));

        var result = await CreateClient(handler).CreateReservationAsync(
            new CreateReservationRequest("HPN72V", new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14), DayPart.Morning),
            Token);

        result.IsSuccess.ShouldBeFalse();
        result.Problem.ShouldBe(result.Problem with { Status = 409, Code = ApiProblemCodes.Conflict, Detail = "Deze auto is al gereserveerd." });
        result.Problem.IsConflict.ShouldBeTrue();
        result.Problem.ConflictingDates.ShouldBe([new DateOnly(2026, 10, 13)]);
    }

    [Theory]
    [InlineData("/reservations/now")]
    [InlineData("/reservations/11111111-1111-1111-1111-111111111111/cancel")]
    [InlineData("/periods/22222222-2222-2222-2222-222222222222/pickup")]
    [InlineData("/periods/22222222-2222-2222-2222-222222222222/return")]
    public async Task Writes_PostToTheirEndpoint(string path)
    {
        var handler = new StubHttpHandler().OnJson(HttpMethod.Post, path, ReservationJson);
        var client = CreateClient(handler);
        var reservationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var periodId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var result = path switch
        {
            "/reservations/now" => await client.BookNowAsync("HPN72V", Token),
            _ when path.EndsWith("/cancel", StringComparison.Ordinal) => await client.CancelReservationAsync(reservationId, Token),
            _ when path.EndsWith("/pickup", StringComparison.Ordinal) => await client.PickUpAsync(periodId, Token),
            _ => await client.ReturnAsync(periodId, Token),
        };

        result.IsSuccess.ShouldBeTrue();
        handler.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldBe(path);
    }

    [Fact]
    public async Task PickUpAsync_WhenNotAllowed_ReturnsDutchDetail()
    {
        var handler = new StubHttpHandler().On(
            HttpMethod.Post,
            $"/periods/{Guid.Empty}/pickup",
            _ => StubHttpHandler.Problem(HttpStatusCode.Forbidden, "not-allowed", "Je kan alleen je eigen reserveringen wijzigen."));

        var result = await CreateClient(handler).PickUpAsync(Guid.Empty, Token);

        result.Problem.ShouldNotBeNull().Code.ShouldBe(ApiProblemCodes.NotAllowed);
        result.Problem.Detail.ShouldBe("Je kan alleen je eigen reserveringen wijzigen.");
    }

    [Fact]
    public async Task GetMyReservationsAsync_ReadsList()
    {
        var handler = new StubHttpHandler().OnJson(HttpMethod.Get, "/reservations/mine", $"[{ReservationJson}]");

        var reservations = await CreateClient(handler).GetMyReservationsAsync(Token);

        reservations.ShouldHaveSingleItem().CarId.ShouldBe("HPN72V");
    }

    [Fact]
    public async Task GetReservationAsync_WhenNotFound_ReturnsNull()
    {
        var reservation = await CreateClient(new StubHttpHandler()).GetReservationAsync(Guid.NewGuid(), Token);

        reservation.ShouldBeNull();
    }

    [Fact]
    public async Task GetBusyCarIdsAsync_SplitsLargeRequests()
    {
        var handler = new StubHttpHandler().On(HttpMethod.Post, "/availability/busy", request =>
        {
            using var body = JsonDocument.Parse(request.Body!);
            var first = body.RootElement.GetProperty("carIds")[0].GetString();
            return StubHttpHandler.Json($$"""{"carIds":["{{first}}"]}""");
        });
        var carIds = Enumerable.Range(0, 250).Select(i => $"CAR{i}").ToList();

        var result = await CreateClient(handler).GetBusyCarIdsAsync(
            carIds, new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 13), DayPart.Afternoon, Token);

        result.Value.ShouldBe(["CAR0", "CAR200"]);
        handler.Requests.Count.ShouldBe(2);
        using var firstBody = JsonDocument.Parse(handler.Requests[0].Body!);
        firstBody.RootElement.GetProperty("carIds").GetArrayLength().ShouldBe(200);
        firstBody.RootElement.GetProperty("dayPart").GetString().ShouldBe("Afternoon");
    }

    [Fact]
    public async Task GetBusyCarIdsAsync_WhenInvalid_ReturnsProblem()
    {
        var handler = new StubHttpHandler().On(
            HttpMethod.Post,
            "/availability/busy",
            _ => StubHttpHandler.Problem(HttpStatusCode.UnprocessableEntity, "invalid", "Je kan maximaal 14 dagen reserveren."));

        var result = await CreateClient(handler).GetBusyCarIdsAsync(
            ["HPN72V"], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), DayPart.Morning, Token);

        result.Problem.ShouldNotBeNull().IsInvalid.ShouldBeTrue();
        result.Problem.Detail.ShouldBe("Je kan maximaal 14 dagen reserveren.");
    }

    [Fact]
    public async Task GetCarIdsInUseAsync_ReturnsIds()
    {
        var handler = new StubHttpHandler().OnJson(HttpMethod.Post, "/availability/now", """{"carIds":["HPN72V"]}""");

        var ids = await CreateClient(handler).GetCarIdsInUseAsync(["HPN72V", "V123AB"], Token);

        ids.ShouldBe(["HPN72V"]);
        handler.Requests.Single().Body.ShouldBe("""{"carIds":["HPN72V","V123AB"]}""");
    }

    [Fact]
    public async Task GetScheduleAsync_ReadsEntries()
    {
        var handler = new StubHttpHandler().OnJson(HttpMethod.Post, "/schedule", """
            [{"reservationId":"11111111-1111-1111-1111-111111111111","periodId":"22222222-2222-2222-2222-222222222222",
              "carId":"HPN72V","date":"2026-10-13","dayPart":"FullDay","start":"2026-10-13T06:00:00+00:00",
              "end":"2026-10-13T16:00:00+00:00","status":"PickedUp","employeeFirstName":"Bram",
              "notPickedUp":false,"overdue":true,"atRisk":false}]
            """);

        var entries = await CreateClient(handler).GetScheduleAsync(
            ["HPN72V"], new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 18), Token);

        var entry = entries.ShouldHaveSingleItem();
        entry.DayPart.ShouldBe(DayPart.FullDay);
        entry.Status.ShouldBe(PeriodStatus.PickedUp);
        entry.EmployeeFirstName.ShouldBe("Bram");
        entry.Overdue.ShouldBeTrue();
    }

    private static ReservationsApiClient CreateClient(StubHttpHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://reservations") });
}
