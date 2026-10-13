using Microsoft.AspNetCore.Http.HttpResults;
using PoolCar.Fleet.Domain;

namespace PoolCar.Fleet.Endpoints;

public static class OfficeEndpoints
{
    public static IEndpointRouteBuilder MapOfficeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/offices").WithTags("Offices");

        group.MapGet("", GetOffices).WithName("GetOffices");

        return app;
    }

    private static Ok<IReadOnlyList<OfficeResponse>> GetOffices(FleetCatalog catalog) =>
        TypedResults.Ok<IReadOnlyList<OfficeResponse>>([.. catalog.Offices.Select(OfficeResponse.From)]);
}
