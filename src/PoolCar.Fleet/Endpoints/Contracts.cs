using PoolCar.Fleet.Domain;

namespace PoolCar.Fleet.Endpoints;

public sealed record OfficeResponse(string Id, string Name, string Address)
{
    public static OfficeResponse From(Office office) => new(office.Id, office.Name, office.Address);
}

public sealed record CategoryResponse(string Id, string Name, string Description, int SortOrder)
{
    public static CategoryResponse From(Category category) =>
        new(category.Id, category.Name, category.Description, category.SortOrder);
}

public sealed record CarResponse(
    string Plate,
    string Make,
    string Model,
    string Variant,
    string Category,
    string Office,
    string Colour,
    int Seats,
    int RangeKm,
    int PowerKw,
    DateOnly FirstRegistration,
    decimal? ListPrice)
{
    public static CarResponse From(Car car) => new(
        car.Plate,
        car.Make,
        car.Model,
        car.Variant,
        car.Category,
        car.Office,
        car.Colour,
        car.Seats,
        car.RangeKm,
        car.PowerKw,
        car.FirstRegistration,
        car.ListPrice);
}
