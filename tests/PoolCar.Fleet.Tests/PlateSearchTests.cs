using PoolCar.Fleet.Domain;

namespace PoolCar.Fleet.Tests;

public class PlateSearchTests
{
    private static readonly FleetCatalog s_catalog = FleetCatalog.LoadEmbedded();

    [Fact]
    public void SearchByPlate_FullPlate_FindsThatCar()
    {
        var results = s_catalog.SearchByPlate("HPN72V");

        var car = results.ShouldHaveSingleItem();
        car.Plate.ShouldBe("HPN72V");
        car.Make.ShouldBe("Kia");
        car.Model.ShouldBe("EV3");
    }

    [Fact]
    public void SearchByPlate_PartialPlate_FindsCarsContainingIt()
    {
        var results = s_catalog.SearchByPlate("72V");

        results.ShouldNotBeEmpty();
        results.ShouldAllBe(c => c.Plate.Contains("72V"));
        results.Select(c => c.Plate).ShouldContain("HPN72V");
    }

    [Fact]
    public void SearchByPlate_UnknownPlate_FindsNothing()
    {
        s_catalog.SearchByPlate("ZZ999Z").ShouldBeEmpty();
    }

    [Fact]
    public void SearchByPlate_ManyMatches_IsCappedAtTen()
    {
        s_catalog.Cars.Count(c => c.Plate.Contains('V')).ShouldBeGreaterThan(FleetCatalog.MaxSearchResults);

        var results = s_catalog.SearchByPlate("V");

        results.Count.ShouldBe(FleetCatalog.MaxSearchResults);
        results.ShouldAllBe(c => c.Plate.Contains('V'));
    }
}
