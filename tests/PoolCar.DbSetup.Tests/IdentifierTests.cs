namespace PoolCar.DbSetup.Tests;

public class IdentifierTests
{
    [Theory]
    [InlineData("poolcar_live", "\"poolcar_live\"")]
    [InlineData("id-poolcar-live", "\"id-poolcar-live\"")]
    [InlineData("0", "\"0\"")]
    public void A_valid_name_is_quoted(string name, string expected) =>
        Identifier.Quote(name).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("Poolcar")]
    [InlineData("pool car")]
    [InlineData("pool\"car")]
    [InlineData("poolcar;drop")]
    [InlineData("poolcar\n")]
    public void An_invalid_name_is_rejected(string name)
    {
        Identifier.IsValid(name).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => Identifier.Quote(name));
    }
}
