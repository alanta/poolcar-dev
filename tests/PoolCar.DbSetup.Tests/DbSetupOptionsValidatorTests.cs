namespace PoolCar.DbSetup.Tests;

public class DbSetupOptionsValidatorTests
{
    private static DbSetupOptions Valid() => new()
    {
        Host = "psql-poolcar.postgres.database.azure.com",
        AdminUser = "id-poolcar-dbadmin",
        Database = "poolcar_live",
        Principal = "id-poolcar-live",
        PrincipalObjectId = "0b5f3a1e-8d2c-4c1b-9f3e-2a6d7c8e9f01",
        Reset = true,
    };

    [Fact]
    public void Valid_options_pass() =>
        new DbSetupOptionsValidator().Validate(null, Valid()).Succeeded.ShouldBeTrue();

    public static TheoryData<string, Action<DbSetupOptions>> InvalidOptions => new()
    {
        { "Host", o => o.Host = "" },
        { "Host", o => o.Host = "psql poolcar" },
        { "AdminUser", o => o.AdminUser = "" },
        { "Database", o => o.Database = "poolcar\"; DROP DATABASE postgres; --" },
        { "Database", o => o.Database = "PoolCar" },
        { "Principal", o => o.Principal = "id poolcar" },
        { "PrincipalObjectId", o => o.PrincipalObjectId = "" },
        { "PrincipalObjectId", o => o.PrincipalObjectId = "not-a-guid" },
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Invalid_options_fail_with_the_setting_name(string setting, Action<DbSetupOptions> change)
    {
        var options = Valid();
        change(options);

        var result = new DbSetupOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldStartWith($"DbSetup:{setting} ");
    }
}
