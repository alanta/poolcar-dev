using PoolCar.Fleet.Domain;

namespace PoolCar.Fleet.Tests;

public class FleetDataTests
{
    private static readonly FleetCatalog s_catalog = FleetCatalog.LoadEmbedded();

    [Fact]
    public void Fleet_Has150Cars()
    {
        s_catalog.Cars.Count.ShouldBe(150);
    }

    [Fact]
    public void Fleet_HasThreeOfficesWith50CarsEach()
    {
        s_catalog.Offices.Select(o => o.Id).ShouldBe(["utrecht", "amsterdam", "eindhoven"], ignoreOrder: true);

        foreach (var office in s_catalog.Offices)
        {
            s_catalog.GetCars(office: office.Id).Count.ShouldBe(50, office.Id);
        }
    }

    [Theory]
    [InlineData("utrecht")]
    [InlineData("amsterdam")]
    [InlineData("eindhoven")]
    public void Office_HasExpectedCategoryDistribution(string office)
    {
        var expected = new Dictionary<string, int>
        {
            ["klein"] = 14,
            ["middel"] = 14,
            ["luxe"] = 8,
            ["kleine-bestelbus"] = 8,
            ["grote-bestelbus"] = 6,
        };

        var actual = s_catalog.GetCars(office: office)
            .GroupBy(c => c.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        actual.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void Plates_AreUnique()
    {
        s_catalog.Cars.Select(c => c.Plate).Distinct(StringComparer.Ordinal).Count().ShouldBe(150);
    }

    [Theory]
    [InlineData("HPN72V")]
    [InlineData("KSP73B")]
    [InlineData("GNB11N")]
    [InlineData("V94FFF")]
    [InlineData("V62PLT")]
    [InlineData("V15PGL")]
    public void PinnedCar_IsInUtrecht(string plate)
    {
        s_catalog.FindCar(plate).ShouldNotBeNull().Office.ShouldBe("utrecht");
    }
}
