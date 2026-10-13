using Microsoft.AspNetCore.Http.HttpResults;
using PoolCar.Fleet.Domain;

namespace PoolCar.Fleet.Endpoints;

public static class CarEndpoints
{
    public const int MaxPlatesPerRequest = 200;

    public static IEndpointRouteBuilder MapCarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cars").WithTags("Cars");

        group.MapGet("", GetCars).WithName("GetCars");
        group.MapGet("/search", SearchCars).WithName("SearchCars");
        group.MapGet("/{plate}", GetCar).WithName("GetCar");

        return app;
    }

    private static Results<Ok<IReadOnlyList<CarResponse>>, ProblemHttpResult> GetCars(
        FleetCatalog catalog,
        string? office,
        string? category,
        string? plates)
    {
        if (office is not null && !catalog.HasOffice(office))
        {
            return BadRequest("Unknown office", $"Office '{office}' does not exist.");
        }

        if (category is not null && !catalog.HasCategory(category))
        {
            return BadRequest("Unknown category", $"Category '{category}' does not exist.");
        }

        IEnumerable<Car> cars;
        if (plates is null)
        {
            cars = catalog.GetCars(office, category);
        }
        else
        {
            var plateList = plates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (plateList.Length > MaxPlatesPerRequest)
            {
                return BadRequest(
                    "Too many plates",
                    $"At most {MaxPlatesPerRequest} plates can be requested at once.");
            }

            cars = catalog.GetCarsByPlates(plateList)
                .Where(c => (office is null || c.Office == office) && (category is null || c.Category == category));
        }

        return TypedResults.Ok<IReadOnlyList<CarResponse>>([.. cars.Select(CarResponse.From)]);
    }

    private static Ok<IReadOnlyList<CarResponse>> SearchCars(FleetCatalog catalog, string? q) =>
        TypedResults.Ok<IReadOnlyList<CarResponse>>(
            string.IsNullOrEmpty(q) ? [] : [.. catalog.SearchByPlate(q).Select(CarResponse.From)]);

    private static Results<Ok<CarResponse>, ProblemHttpResult> GetCar(FleetCatalog catalog, string plate)
    {
        var car = catalog.FindCar(plate);
        return car is null
            ? TypedResults.Problem(
                title: "Car not found",
                detail: $"No car with plate '{plate}' exists.",
                statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(CarResponse.From(car));
    }

    private static ProblemHttpResult BadRequest(string title, string detail) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
