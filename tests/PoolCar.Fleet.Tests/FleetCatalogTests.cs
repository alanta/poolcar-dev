using PoolCar.Fleet.Domain;

namespace PoolCar.Fleet.Tests;

public class FleetCatalogTests
{
    private static readonly Office[] s_offices =
    [
        new("utrecht", "Utrecht", "Laan 1, Utrecht"),
        new("amsterdam", "Amsterdam", "Weg 2, Amsterdam"),
    ];

    private static readonly Category[] s_categories =
    [
        new("middel", "Middel", "Gezinsauto", 2),
        new("klein", "Klein", "Stadsauto", 1),
    ];

    private static FleetCatalog CreateCatalog() => new(
        s_offices,
        s_categories,
        [
            CreateCar("AA111A", "middel", "utrecht", 400),
            CreateCar("BB222B", "klein", "utrecht", 300),
            CreateCar("CC333C", "klein", "amsterdam", 350),
            CreateCar("DD444D", "middel", "amsterdam", 500),
            CreateCar("EE555E", "klein", "utrecht", 300),
        ]);

    private static Car CreateCar(string plate, string category, string office, int rangeKm) =>
        new(plate, "Make", "Model", "Variant", category, office, "Wit", 5, rangeKm, 100,
            new DateOnly(2024, 1, 1), 30000m);

    [Fact]
    public void Categories_AreOrderedBySortOrder()
    {
        var catalog = CreateCatalog();

        catalog.Categories.Select(c => c.Id).ShouldBe(["klein", "middel"]);
    }

    [Fact]
    public void GetCars_WithoutFilters_SortsByCategoryThenRangeDescendingThenPlate()
    {
        var catalog = CreateCatalog();

        var plates = catalog.GetCars().Select(c => c.Plate);

        plates.ShouldBe(["CC333C", "BB222B", "EE555E", "DD444D", "AA111A"]);
    }

    [Fact]
    public void GetCars_ByOffice_ReturnsOnlyCarsAtThatOffice()
    {
        var catalog = CreateCatalog();

        var plates = catalog.GetCars(office: "utrecht").Select(c => c.Plate);

        plates.ShouldBe(["BB222B", "EE555E", "AA111A"]);
    }

    [Fact]
    public void GetCars_ByCategory_ReturnsOnlyCarsInThatCategory()
    {
        var catalog = CreateCatalog();

        var plates = catalog.GetCars(category: "middel").Select(c => c.Plate);

        plates.ShouldBe(["DD444D", "AA111A"]);
    }

    [Fact]
    public void GetCars_ByOfficeAndCategory_CombinesFilters()
    {
        var catalog = CreateCatalog();

        var plates = catalog.GetCars(office: "amsterdam", category: "klein").Select(c => c.Plate);

        plates.ShouldBe(["CC333C"]);
    }

    [Fact]
    public void FindCar_KnownPlate_ReturnsCar()
    {
        var catalog = CreateCatalog();

        catalog.FindCar("CC333C").ShouldNotBeNull().Office.ShouldBe("amsterdam");
    }

    [Fact]
    public void FindCar_UnknownPlate_ReturnsNull()
    {
        var catalog = CreateCatalog();

        catalog.FindCar("ZZ999Z").ShouldBeNull();
    }

    [Fact]
    public void GetCarsByPlates_ReturnsKnownCarsInRequestedOrder()
    {
        var catalog = CreateCatalog();

        var plates = catalog.GetCarsByPlates(["DD444D", "ZZ999Z", "AA111A", "DD444D"]).Select(c => c.Plate);

        plates.ShouldBe(["DD444D", "AA111A"]);
    }

    [Fact]
    public void HasOfficeAndHasCategory_RecogniseKnownIds()
    {
        var catalog = CreateCatalog();

        catalog.HasOffice("utrecht").ShouldBeTrue();
        catalog.HasOffice("rotterdam").ShouldBeFalse();
        catalog.HasCategory("klein").ShouldBeTrue();
        catalog.HasCategory("luxe").ShouldBeFalse();
    }

    [Fact]
    public void Constructor_DuplicatePlate_Throws()
    {
        Should.Throw<InvalidOperationException>(() => new FleetCatalog(
            s_offices,
            s_categories,
            [CreateCar("AA111A", "klein", "utrecht", 300), CreateCar("AA111A", "middel", "utrecht", 400)]));
    }

    [Fact]
    public void Constructor_UnknownOffice_Throws()
    {
        Should.Throw<InvalidOperationException>(() => new FleetCatalog(
            s_offices,
            s_categories,
            [CreateCar("AA111A", "klein", "rotterdam", 300)]));
    }
}
