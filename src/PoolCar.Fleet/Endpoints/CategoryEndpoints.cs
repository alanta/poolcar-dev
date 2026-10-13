using Microsoft.AspNetCore.Http.HttpResults;
using PoolCar.Fleet.Domain;

namespace PoolCar.Fleet.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").WithTags("Categories");

        group.MapGet("", GetCategories).WithName("GetCategories");

        return app;
    }

    private static Ok<IReadOnlyList<CategoryResponse>> GetCategories(FleetCatalog catalog) =>
        TypedResults.Ok<IReadOnlyList<CategoryResponse>>([.. catalog.Categories.Select(CategoryResponse.From)]);
}
