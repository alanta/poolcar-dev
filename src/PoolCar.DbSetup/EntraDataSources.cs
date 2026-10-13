using Azure.Core;
using Npgsql;

namespace PoolCar.DbSetup;

/// <summary>Connects to the server as its Microsoft Entra administrator.</summary>
public static class EntraDataSources
{
    public const string Scope = "https://ossrdbms-aad.database.windows.net/.default";

    public static DataSourceFactory Create(DbSetupOptions options, TokenCredential credential)
    {
        var context = new TokenRequestContext([Scope]);

        return database =>
        {
            var builder = new NpgsqlDataSourceBuilder
            {
                ConnectionStringBuilder =
                {
                    Host = options.Host,
                    Database = database,
                    Username = options.AdminUser,
                    SslMode = SslMode.Require,
                },
            };
            builder.UsePasswordProvider(
                _ => credential.GetToken(context, default).Token,
                async (_, ct) => (await credential.GetTokenAsync(context, ct)).Token);
            return builder.Build();
        };
    }
}
