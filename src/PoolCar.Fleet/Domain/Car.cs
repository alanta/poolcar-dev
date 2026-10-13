namespace PoolCar.Fleet.Domain;

public sealed record Car(
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
