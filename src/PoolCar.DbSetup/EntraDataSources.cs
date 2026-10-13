using Azure.Core;
using Npgsql;
using PoolCar.Reservations.Data;

namespace PoolCar.DbSetup;

/// <summary>Connects to the server as its Microsoft Entra administrator.</summary>
public static class EntraDataSources
{
    public static DataSourceFactory Create(DbSetupOptions options, TokenCredential credential) =>
        database =>
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
            builder.UseEntraIdWhenPasswordless(() => credential);
            return builder.Build();
        };
}
