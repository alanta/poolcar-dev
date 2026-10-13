using Azure.Core;
using Npgsql;

namespace PoolCar.Reservations.Data;

/// <summary>Signs in to Azure Database for PostgreSQL with a Microsoft Entra access token.</summary>
public static class EntraIdAuthentication
{
    public const string Scope = "https://ossrdbms-aad.database.windows.net/.default";

    private static readonly TimeSpan s_refreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan s_retryInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Uses an access token as password when the connection string has no password.
    /// Returns whether the token provider was configured.
    /// </summary>
    public static bool UseEntraIdWhenPasswordless(
        this NpgsqlDataSourceBuilder dataSource, Func<TokenCredential> createCredential)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(createCredential);

        if (!string.IsNullOrEmpty(dataSource.ConnectionStringBuilder.Password))
        {
            return false;
        }

        var credential = createCredential();
        var context = new TokenRequestContext([Scope]);
        dataSource.UsePeriodicPasswordProvider(
            async (_, ct) => (await credential.GetTokenAsync(context, ct)).Token,
            s_refreshInterval,
            s_retryInterval);
        return true;
    }
}
