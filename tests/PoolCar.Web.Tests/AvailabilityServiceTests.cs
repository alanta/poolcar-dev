using System.Net;
using System.Text.Json;
using PoolCar.Web.Availability;
using PoolCar.Web.Clients;
using PoolCar.Web.Tests.Support;
using Shouldly;

namespace PoolCar.Web.Tests;

public class AvailabilityServiceTests
{
    private static readonly DateOnly Tuesday = new(2026, 10, 13);
    private readonly StubHttpHandler _fleet = FleetData.Fleet();
    private readonly StubHttpHandler _reservations = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetAvailableAsync_RemovesBusyCars_AndCountsEveryCategory()
    {
        _reservations.OnJson(HttpMethod.Post, "/availability/busy", """{"carIds":["AA11BB","HPN72V"]}""");

        var result = await CreateService().GetAvailableAsync(
            new AvailabilityRequest("utrecht", Tuesday, Tuesday.AddDays(1), DayPart.Morning),
            Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Cars.Select(c => c.Plate).ShouldBe(["CC22DD"]);
        result.Value.Categories.Select(c => (c.Category.Id, c.Available, c.Total)).ShouldBe(
        [
            ("klein", 1, 2),
            ("middel", 0, 1),
            ("luxe", 0, 0),
        ]);
        result.Value.Categories.Single(c => c.Category.Id == "middel").HasAvailable.ShouldBeFalse();

        _fleet.Requests.ShouldContain(r => r.PathAndQuery == "/cars?office=utrecht");
        using var body = JsonDocument.Parse(_reservations.Requests.Single().Body!);
        body.RootElement.GetProperty("carIds").EnumerateArray().Select(e => e.GetString()).ShouldBe(["AA11BB", "CC22DD", "HPN72V"]);
        body.RootElement.GetProperty("from").GetString().ShouldBe("2026-10-13");
        body.RootElement.GetProperty("to").GetString().ShouldBe("2026-10-14");
        body.RootElement.GetProperty("dayPart").GetString().ShouldBe("Morning");
    }

    [Fact]
    public async Task GetAvailableAsync_WithCategory_KeepsCountsForAllCategories()
    {
        _reservations.OnJson(HttpMethod.Post, "/availability/busy", """{"carIds":[]}""");

        var result = await CreateService().GetAvailableAsync(
            new AvailabilityRequest("utrecht", Tuesday, Tuesday, DayPart.FullDay, "klein"),
            Token);

        result.Value!.Cars.Select(c => c.Plate).ShouldBe(["AA11BB", "CC22DD"]);
        result.Value.Categories.Sum(c => c.Available).ShouldBe(3);
    }

    [Fact]
    public async Task GetAvailableAsync_WhenRequestBreaksRule_ReturnsProblem()
    {
        _reservations.On(HttpMethod.Post, "/availability/busy", _ => StubHttpHandler.Problem(
            HttpStatusCode.UnprocessableEntity, "invalid", "Je kan maximaal 14 dagen reserveren."));

        var result = await CreateService().GetAvailableAsync(
            new AvailabilityRequest("utrecht", Tuesday, Tuesday.AddDays(20), DayPart.Morning),
            Token);

        result.Problem.ShouldNotBeNull().Detail.ShouldBe("Je kan maximaal 14 dagen reserveren.");
    }

    [Fact]
    public async Task GetAvailableNowAsync_RemovesCarsInUse()
    {
        _reservations.OnJson(HttpMethod.Post, "/availability/now", """{"carIds":["CC22DD"]}""");

        var result = await CreateService().GetAvailableNowAsync("utrecht", cancellationToken: Token);

        result.Cars.Select(c => c.Plate).ShouldBe(["AA11BB", "HPN72V"]);
        result.Categories.Select(c => c.Available).ShouldBe([1, 1, 0]);
        _reservations.Requests.Single().Uri.AbsolutePath.ShouldBe("/availability/now");
    }

    private AvailabilityService CreateService() => new(
        new FleetApiClient(new HttpClient(_fleet) { BaseAddress = new Uri("http://fleet") }),
        new ReservationsApiClient(new HttpClient(_reservations) { BaseAddress = new Uri("http://reservations") }));
}
