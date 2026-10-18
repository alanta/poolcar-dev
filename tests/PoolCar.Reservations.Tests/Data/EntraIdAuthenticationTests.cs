using Azure.Core;
using Npgsql;
using PoolCar.Reservations.Data;

namespace PoolCar.Reservations.Tests.Data;

public class EntraIdAuthenticationTests(PostgresFixture postgres)
{
    [Fact]
    public void A_connection_string_with_a_password_keeps_password_authentication()
    {
        var builder = new NpgsqlDataSourceBuilder("Host=localhost;Username=app;Password=secret");

        var configured = builder.UseEntraIdWhenPasswordless(
            () => throw new InvalidOperationException("No credential expected."));

        configured.ShouldBeFalse();
    }

    [Fact]
    public async Task A_connection_string_without_a_password_signs_in_with_an_access_token()
    {
        var ct = TestContext.Current.CancellationToken;
        var withPassword = new NpgsqlConnectionStringBuilder(await postgres.CreateDatabaseAsync());
        var password = withPassword.Password!;
        var builder = new NpgsqlDataSourceBuilder(withPassword.ConnectionString);
        builder.ConnectionStringBuilder.Password = null;
        var credential = new FixedTokenCredential(password);

        var configured = builder.UseEntraIdWhenPasswordless(() => credential);
        await using var dataSource = builder.Build();
        await using var command = dataSource.CreateCommand("SELECT 1");
        var result = await command.ExecuteScalarAsync(ct);

        configured.ShouldBeTrue();
        result.ShouldBe(1);
        credential.RequestedScopes.ShouldContain(EntraIdAuthentication.Scope);
    }

    private sealed class FixedTokenCredential(string token) : TokenCredential
    {
        public List<string> RequestedScopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            lock (RequestedScopes)
            {
                RequestedScopes.AddRange(requestContext.Scopes);
            }

            return new AccessToken(token, TimeProvider.System.GetUtcNow().AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
