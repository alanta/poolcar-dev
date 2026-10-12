using PoolCar.Web.Auth;

namespace PoolCar.Web.Tests;

public class EasyAuthPrincipalParserTests
{
    [Fact]
    public void Parse_WithValidHeader_ReturnsPrincipalWithExpectedClaims()
    {
        // Arrange
        var header = EasyAuthPrincipalParser.CreateHeader(
            name: "Jan de Boer",
            objectId: "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            roles: ["FleetOperator"]);

        // Act
        var principal = EasyAuthPrincipalParser.Parse(header);

        // Assert
        Assert.NotNull(principal);
        Assert.NotNull(principal.Identity);
        Assert.True(principal.Identity.IsAuthenticated);
        Assert.Equal("Jan de Boer", principal.GetName());
        Assert.Equal("3fa85f64-5717-4562-b3fc-2c963f66afa6", principal.GetObjectId());
        Assert.Contains("FleetOperator", principal.GetRoles());
        Assert.True(principal.IsOperator());
        Assert.True(principal.IsInRole("FleetOperator"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithMissingHeader_ReturnsNull(string? header)
    {
        // Act
        var principal = EasyAuthPrincipalParser.Parse(header);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public void Parse_WithRolesClaimPresent_ReturnsPrincipalWithRoles()
    {
        // Arrange
        var header = EasyAuthPrincipalParser.CreateHeader(
            name: "Marnix van Valen",
            objectId: "11111111-2222-3333-4444-555555555555",
            roles: ["FleetOperator", "Employee"]);

        // Act
        var principal = EasyAuthPrincipalParser.Parse(header);

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(2, principal.GetRoles().Count);
        Assert.Contains("FleetOperator", principal.GetRoles());
        Assert.Contains("Employee", principal.GetRoles());
        Assert.True(principal.IsOperator());
    }

    [Fact]
    public void Parse_WithRolesClaimAbsent_ReturnsPrincipalWithoutRoles()
    {
        // Arrange
        var header = EasyAuthPrincipalParser.CreateHeader(
            name: "Piet Bakker",
            objectId: "22222222-3333-4444-5555-666666666666",
            roles: null);

        // Act
        var principal = EasyAuthPrincipalParser.Parse(header);

        // Assert
        Assert.NotNull(principal);
        Assert.Equal("Piet Bakker", principal.GetName());
        Assert.Equal("22222222-3333-4444-5555-666666666666", principal.GetObjectId());
        Assert.Empty(principal.GetRoles());
        Assert.False(principal.IsOperator());
        Assert.False(principal.IsInRole("FleetOperator"));
    }

    [Fact]
    public void Parse_WithPreferredUsernameFallback_UsesPreferredUsernameAsName()
    {
        // Arrange
        var header = EasyAuthPrincipalParser.CreateHeader(
            name: null,
            preferredUsername: "jan.deboer@alanta.nl",
            objectId: "3fa85f64-5717-4562-b3fc-2c963f66afa6");

        // Act
        var principal = EasyAuthPrincipalParser.Parse(header);

        // Assert
        Assert.NotNull(principal);
        Assert.Equal("jan.deboer@alanta.nl", principal.GetName());
        Assert.Equal("jan.deboer@alanta.nl", principal.Identity?.Name);
    }

    [Theory]
    [InlineData("not-valid-base64!@#")]
    [InlineData("bm90IGpzb24=")] // "not json" in base64
    public void Parse_WithInvalidHeader_ReturnsNull(string invalidHeader)
    {
        // Act
        var principal = EasyAuthPrincipalParser.Parse(invalidHeader);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public void TryParse_WithValidHeader_ReturnsTrueAndPrincipal()
    {
        // Arrange
        var header = EasyAuthPrincipalParser.CreateHeader(
            name: "Jan de Boer",
            objectId: "3fa85f64-5717-4562-b3fc-2c963f66afa6");

        // Act
        var success = EasyAuthPrincipalParser.TryParse(header, out var principal);

        // Assert
        Assert.True(success);
        Assert.NotNull(principal);
        Assert.Equal("Jan de Boer", principal.GetName());
    }

    [Fact]
    public void TryParse_WithMissingHeader_ReturnsFalseAndNull()
    {
        // Act
        var success = EasyAuthPrincipalParser.TryParse(null, out var principal);

        // Assert
        Assert.False(success);
        Assert.Null(principal);
    }
}
