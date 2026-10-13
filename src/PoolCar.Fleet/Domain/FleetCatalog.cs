using System.Collections.Frozen;
using System.Text.Json;

namespace PoolCar.Fleet.Domain;

public sealed class FleetCatalog
{
    private const string EmbeddedResourceName = "PoolCar.Fleet.Data.fleet.json";

    public const int MaxSearchResults = 10;

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FrozenDictionary<string, Office> _officesById;
    private readonly FrozenDictionary<string, Category> _categoriesById;
    private readonly FrozenDictionary<string, Car> _carsByPlate;
    private readonly IReadOnlyList<Car> _cars;

    public FleetCatalog(IEnumerable<Office> offices, IEnumerable<Category> categories, IEnumerable<Car> cars)
    {
        Offices = [.. offices];
        Categories = [.. categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id, StringComparer.Ordinal)];

        _officesById = Offices.ToFrozenDictionary(o => o.Id, StringComparer.Ordinal);
        _categoriesById = Categories.ToFrozenDictionary(c => c.Id, StringComparer.Ordinal);

        var sortOrder = Categories.ToDictionary(c => c.Id, c => c.SortOrder, StringComparer.Ordinal);
        var carList = cars.ToList();
        foreach (var car in carList)
        {
            if (!_officesById.ContainsKey(car.Office))
            {
                throw new InvalidOperationException($"Car {car.Plate} refers to unknown office '{car.Office}'.");
            }

            if (!_categoriesById.ContainsKey(car.Category))
            {
                throw new InvalidOperationException($"Car {car.Plate} refers to unknown category '{car.Category}'.");
            }
        }

        var duplicate = carList.GroupBy(c => c.Plate, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Plate {duplicate.Key} occurs more than once.");
        }

        _cars =
        [
            .. carList
                .OrderBy(c => sortOrder[c.Category])
                .ThenByDescending(c => c.RangeKm)
                .ThenBy(c => c.Plate, StringComparer.Ordinal),
        ];
        _carsByPlate = _cars.ToFrozenDictionary(c => c.Plate, StringComparer.Ordinal);
    }

    public IReadOnlyList<Office> Offices { get; }

    public IReadOnlyList<Category> Categories { get; }

    public IReadOnlyList<Car> Cars => _cars;

    public static FleetCatalog Load(Stream json)
    {
        var data = JsonSerializer.Deserialize<FleetData>(json, s_jsonOptions)
            ?? throw new InvalidOperationException("Fleet data is empty.");

        return new FleetCatalog(data.Offices, data.Categories, data.Cars);
    }

    public static FleetCatalog LoadEmbedded()
    {
        using var stream = typeof(FleetCatalog).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' was not found.");

        return Load(stream);
    }

    public bool HasOffice(string id) => _officesById.ContainsKey(id);

    public bool HasCategory(string id) => _categoriesById.ContainsKey(id);

    public Car? FindCar(string plate) => _carsByPlate.GetValueOrDefault(plate);

    public IReadOnlyList<Car> GetCars(string? office = null, string? category = null) =>
    [
        .. _cars.Where(c =>
            (office is null || c.Office == office) &&
            (category is null || c.Category == category)),
    ];

    public IReadOnlyList<Car> GetCarsByPlates(IEnumerable<string> plates) =>
    [
        .. plates
            .Distinct(StringComparer.Ordinal)
            .Select(FindCar)
            .OfType<Car>(),
    ];

    public IReadOnlyList<Car> SearchByPlate(string query) =>
        _cars.Where(c => c.Plate.Contains(query)).Take(MaxSearchResults).ToList();

    private sealed record FleetData(
        IReadOnlyList<Office> Offices,
        IReadOnlyList<Category> Categories,
        IReadOnlyList<Car> Cars);
}
