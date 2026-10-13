using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using PoolCar.Fleet.Endpoints;

namespace PoolCar.Fleet.Tests;

public class FleetApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetOffices_ReturnsAllOffices()
    {
        var offices = await _client.GetFromJsonAsync<OfficeResponse[]>("/offices", CancellationToken);

        offices.ShouldNotBeNull().Select(o => o.Id).ShouldBe(["utrecht", "amsterdam", "eindhoven"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetCategories_ReturnsCategoriesInSortOrder()
    {
        var categories = await _client.GetFromJsonAsync<CategoryResponse[]>("/categories", CancellationToken);

        categories.ShouldNotBeNull().Select(c => c.Id)
            .ShouldBe(["klein", "middel", "luxe", "kleine-bestelbus", "grote-bestelbus"]);
    }

    [Fact]
    public async Task GetCars_WithoutFilters_ReturnsWholeFleetSorted()
    {
        var cars = await _client.GetFromJsonAsync<CarResponse[]>("/cars", CancellationToken);

        cars.ShouldNotBeNull().Length.ShouldBe(150);
        cars[0].Category.ShouldBe("klein");
        cars[^1].Category.ShouldBe("grote-bestelbus");
    }

    [Fact]
    public async Task GetCars_FilteredByOfficeAndCategory_ReturnsMatchingCarsByRangeDescending()
    {
        var cars = await _client.GetFromJsonAsync<CarResponse[]>(
            "/cars?office=utrecht&category=middel", CancellationToken);

        cars.ShouldNotBeNull().Length.ShouldBe(14);
        cars.ShouldAllBe(c => c.Office == "utrecht" && c.Category == "middel");
        cars.Select(c => c.RangeKm).ShouldBeInOrder(SortDirection.Descending);
    }

    [Theory]
    [InlineData("/cars?office=rotterdam")]
    [InlineData("/cars?category=cabrio")]
    public async Task GetCars_UnknownFilter_ReturnsBadRequestProblem(string url)
    {
        using var response = await _client.GetAsync(url, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task GetCars_ByPlates_ReturnsRequestedCars()
    {
        var cars = await _client.GetFromJsonAsync<CarResponse[]>(
            "/cars?plates=HPN72V,KSP73B,ZZ999Z", CancellationToken);

        cars.ShouldNotBeNull().Select(c => c.Plate).ShouldBe(["HPN72V", "KSP73B"]);
    }

    [Fact]
    public async Task GetCars_TooManyPlates_ReturnsBadRequestProblem()
    {
        var plates = string.Join(',', Enumerable.Range(0, CarEndpoints.MaxPlatesPerRequest + 1).Select(i => $"AA{i:000}A"));

        using var response = await _client.GetAsync($"/cars?plates={plates}", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        problem.ShouldNotBeNull().Status.ShouldBe(400);
    }

    [Fact]
    public async Task GetCar_KnownPlate_ReturnsCar()
    {
        var car = await _client.GetFromJsonAsync<CarResponse>("/cars/HPN72V", CancellationToken);

        car.ShouldNotBeNull();
        car.Make.ShouldBe("Kia");
        car.Model.ShouldBe("EV3");
        car.Office.ShouldBe("utrecht");
    }

    [Fact]
    public async Task GetCar_CarWithoutListPrice_ReturnsNullListPrice()
    {
        var car = await _client.GetFromJsonAsync<CarResponse>("/cars/87BZV2", CancellationToken);

        car.ShouldNotBeNull().ListPrice.ShouldBeNull();
    }

    [Fact]
    public async Task GetCar_UnknownPlate_ReturnsNotFoundProblem()
    {
        using var response = await _client.GetAsync("/cars/ZZ999Z", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        problem.ShouldNotBeNull().Status.ShouldBe(404);
    }

    [Fact]
    public async Task SearchCars_FullPlate_ReturnsThatCar()
    {
        var cars = await _client.GetFromJsonAsync<CarResponse[]>("/cars/search?q=HPN72V", CancellationToken);

        cars.ShouldNotBeNull().ShouldHaveSingleItem().Model.ShouldBe("EV3");
    }

    [Fact]
    public async Task SearchCars_ManyMatches_ReturnsAtMostTen()
    {
        var cars = await _client.GetFromJsonAsync<CarResponse[]>("/cars/search?q=V", CancellationToken);

        cars.ShouldNotBeNull().Length.ShouldBe(10);
    }

    [Fact]
    public async Task SearchCars_WithoutQuery_ReturnsEmptyList()
    {
        var cars = await _client.GetFromJsonAsync<CarResponse[]>("/cars/search", CancellationToken);

        cars.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var response = await _client.GetAsync("/health", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
