namespace PoolCar.Web.Clients;

public sealed record FleetOffice(string Id, string Name, string Address);

public sealed record FleetCategory(string Id, string Name, string Description, int SortOrder);

public sealed record FleetCar(
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
    decimal? ListPrice);
