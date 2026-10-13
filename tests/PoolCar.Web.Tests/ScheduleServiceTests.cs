using System.Text.Json;
using PoolCar.Web.Clients;
using PoolCar.Web.Schedule;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public class ScheduleServiceTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Tuesday = new(2026, 10, 13);
    private static readonly DateOnly Sunday = new(2026, 10, 18);

    // Start and end are deliberately unrelated to the day part, so placement can only come from date and day part.
    private static readonly string ScheduleJson = $"""
        [{Entry("HPN72V", "2026-10-13", "Morning", "Scheduled", "2026-10-12T23:30:00+00:00")},
         {Entry("HPN72V", "2026-10-14", "FullDay", "PickedUp", "2026-10-14T06:00:00+00:00", overdue: true)},
         {Entry("AA11BB", "2026-10-13", "Afternoon", "Returned", "2026-10-13T10:00:00+00:00")},
         {Entry("AA11BB", "2026-10-13", "Afternoon", "Scheduled", "2026-10-13T13:00:00+00:00")}]
        """;

    private readonly StubHttpHandler _fleet = FleetData.Fleet();
    private readonly StubHttpHandler _reservations = new StubHttpHandler()
        .OnJson(HttpMethod.Post, "/schedule", ScheduleJson);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetOfficeScheduleAsync_JoinsCarsWithTheirPeriods()
    {
        var view = await CreateService().GetOfficeScheduleAsync("utrecht", "klein", Monday, Sunday, Token);

        view.Dates.Count.ShouldBe(7);
        view.Dates[0].ShouldBe(Monday);
        view.Dates[^1].ShouldBe(Sunday);
        view.Rows.Select(r => r.Car.Plate).ShouldBe(["AA11BB", "CC22DD", "HPN72V"]);
        view.Rows[0].Periods.Count.ShouldBe(2);
        view.Rows[1].Periods.ShouldBeEmpty();
        view.Rows[2].Periods.Count.ShouldBe(2);

        _fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?office=utrecht&category=klein");
        using var body = JsonDocument.Parse(_reservations.Requests.Single().Body!);
        body.RootElement.GetProperty("from").GetString().ShouldBe("2026-10-12");
        body.RootElement.GetProperty("to").GetString().ShouldBe("2026-10-18");
    }

    [Theory]
    [InlineData("2026-10-13", DayPart.Morning, true)]
    [InlineData("2026-10-13", DayPart.Afternoon, false)]
    [InlineData("2026-10-12", DayPart.Afternoon, false)]
    [InlineData("2026-10-14", DayPart.Morning, true)]
    [InlineData("2026-10-14", DayPart.Afternoon, true)]
    [InlineData("2026-10-15", DayPart.Morning, false)]
    public async Task Periods_ArePlacedByDateAndDayPart(string date, DayPart half, bool occupied)
    {
        var view = await CreateService().GetOfficeScheduleAsync("utrecht", null, Monday, Sunday, Token);
        var kia = view.Rows.Single(r => r.Car.Plate == "HPN72V");

        (kia.PrimaryAt(DateOnly.Parse(date), half) is not null).ShouldBe(occupied);
    }

    [Fact]
    public async Task PrimaryAt_PrefersActivePeriodOverReturnedOne()
    {
        var view = await CreateService().GetOfficeScheduleAsync("utrecht", null, Monday, Sunday, Token);
        var renault = view.Rows.Single(r => r.Car.Plate == "AA11BB");

        renault.At(Tuesday, DayPart.Afternoon).Count.ShouldBe(2);
        renault.PrimaryAt(Tuesday, DayPart.Afternoon)!.Status.ShouldBe(PeriodStatus.Scheduled);
        renault.At(Tuesday, DayPart.FullDay).Count.ShouldBe(2);
        renault.PrimaryAt(Tuesday, DayPart.Morning).ShouldBeNull();
    }

    [Fact]
    public async Task GetCarScheduleAsync_ReturnsSingleCar()
    {
        _fleet.OnJson(HttpMethod.Get, "/cars/HPN72V", """
            {"plate":"HPN72V","make":"Kia","model":"Niro","variant":"EV","category":"middel","office":"utrecht",
             "colour":"Grijs","seats":5,"rangeKm":460,"powerKw":150,"firstRegistration":"2024-03-01","listPrice":null}
            """);
        _reservations.OnJson(HttpMethod.Post, "/schedule", $"[{Entry("HPN72V", "2026-10-13", "Morning", "Scheduled", "2026-10-13T06:00:00+00:00")}]");

        var row = await CreateService().GetCarScheduleAsync("HPN72V", Monday, Sunday, Token);

        row.ShouldNotBeNull().Car.Model.ShouldBe("Niro");
        row.Periods.ShouldHaveSingleItem().EmployeeFirstName.ShouldBe("Bram");
        _reservations.Requests.Single().Body!.ShouldContain("\"carIds\":[\"HPN72V\"]");
    }

    [Fact]
    public async Task GetCarScheduleAsync_WhenPlateIsUnknown_ReturnsNull()
    {
        var row = await CreateService().GetCarScheduleAsync("XX99XX", Monday, Sunday, Token);

        row.ShouldBeNull();
        _reservations.Requests.ShouldBeEmpty();
    }

    private static string Entry(string carId, string date, string dayPart, string status, string start, bool overdue = false) => $$"""
        {"reservationId":"{{Guid.NewGuid()}}","periodId":"{{Guid.NewGuid()}}","carId":"{{carId}}","date":"{{date}}",
         "dayPart":"{{dayPart}}","start":"{{start}}","end":"{{start}}","status":"{{status}}",
         "employeeFirstName":"Bram","notPickedUp":false,"overdue":{{(overdue ? "true" : "false")}},"atRisk":false}
        """;

    private ScheduleService CreateService() => new(
        new FleetApiClient(new HttpClient(_fleet) { BaseAddress = new Uri("http://fleet") }),
        new ReservationsApiClient(new HttpClient(_reservations) { BaseAddress = new Uri("http://reservations") }));
}
