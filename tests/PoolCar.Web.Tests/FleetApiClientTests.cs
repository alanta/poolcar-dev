using System.Net;
using System.Text;
using PoolCar.Web.Clients;

namespace PoolCar.Web.Tests;

public class FleetApiClientTests
{
    private const string OfficesJson = """
        [{"id":"utrecht","name":"Utrecht","address":"Laan van de Pool 12, 3521 AA Utrecht"}]
        """;

    private const string CategoriesJson = """
        [{"id":"klein","name":"Klein","description":"Compacte auto","sortOrder":1}]
        """;

    private const string CarsJson = """
        [{
          "plate":"HPN72V","make":"Kia","model":"Niro","variant":"EV","category":"middel",
          "office":"utrecht","colour":"Grijs","seats":5,"rangeKm":460,"powerKw":150,
          "firstRegistration":"2024-03-01","listPrice":42950.00
        }]
        """;

    [Fact]
    public async Task GetOfficesAsync_DeserialisesOffices()
    {
        var handler = new FakeHandler(OfficesJson);
        var client = CreateClient(handler);

        var offices = await client.GetOfficesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/offices", handler.LastRequestUri?.PathAndQuery);
        var office = Assert.Single(offices);
        Assert.Equal(new FleetOffice("utrecht", "Utrecht", "Laan van de Pool 12, 3521 AA Utrecht"), office);
    }

    [Fact]
    public async Task GetCategoriesAsync_DeserialisesCategories()
    {
        var handler = new FakeHandler(CategoriesJson);
        var client = CreateClient(handler);

        var categories = await client.GetCategoriesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/categories", handler.LastRequestUri?.PathAndQuery);
        Assert.Equal(new FleetCategory("klein", "Klein", "Compacte auto", 1), Assert.Single(categories));
    }

    [Fact]
    public async Task GetCarsAsync_WithoutFilters_RequestsAllCars()
    {
        var handler = new FakeHandler(CarsJson);
        var client = CreateClient(handler);

        var cars = await client.GetCarsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("/cars", handler.LastRequestUri?.PathAndQuery);
        var car = Assert.Single(cars);
        Assert.Equal("HPN72V", car.Plate);
        Assert.Equal("Kia", car.Make);
        Assert.Equal("Niro", car.Model);
        Assert.Equal("Grijs", car.Colour);
        Assert.Equal(5, car.Seats);
        Assert.Equal(460, car.RangeKm);
        Assert.Equal(new DateOnly(2024, 3, 1), car.FirstRegistration);
        Assert.Equal(42950.00m, car.ListPrice);
    }

    [Fact]
    public async Task GetCarsAsync_WithOfficeAndCategory_AddsQueryString()
    {
        var handler = new FakeHandler(CarsJson);
        var client = CreateClient(handler);

        await client.GetCarsAsync("utrecht", "kleine-bestelbus", TestContext.Current.CancellationToken);

        Assert.Equal("/cars?office=utrecht&category=kleine-bestelbus", handler.LastRequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task GetCarsAsync_WithOfficeOnly_OmitsCategory()
    {
        var handler = new FakeHandler(CarsJson);
        var client = CreateClient(handler);

        await client.GetCarsAsync(office: "amsterdam", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("/cars?office=amsterdam", handler.LastRequestUri?.PathAndQuery);
    }

    private static FleetApiClient CreateClient(FakeHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://fleet") });

    private sealed class FakeHandler(string json) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
