using PoolCar.Web.Formatting;
using Shouldly;

namespace PoolCar.Web.Tests;

public class LicensePlateTests
{
    [Theory]
    [InlineData("HPN72V", "HPN-72-V")]
    [InlineData("V94FFF", "V-94-FFF")]
    [InlineData("X545NJ", "X-545-NJ")]
    [InlineData("12ABC3", "12-ABC-3")]
    [InlineData("12ABCD", "12-AB-CD")]
    [InlineData("ABCD12", "AB-CD-12")]
    public void Format_InsertsDashes(string plate, string expected)
    {
        LicensePlate.Format(plate).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Format_WithNullOrEmpty_ReturnsInput(string? plate)
    {
        LicensePlate.Format(plate!).ShouldBe(plate);
    }
}
