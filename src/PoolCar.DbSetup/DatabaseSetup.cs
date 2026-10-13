using Microsoft.Extensions.Logging;
using Npgsql;

namespace PoolCar.DbSetup;

/// <summary>Opens a connection pool to a database on the server as the administrator.</summary>
public delegate NpgsqlDataSource DataSourceFactory(string database);

/// <summary>Creates a database owned by a principal, idempotently.</summary>
public sealed partial class DatabaseSetup(
    DataSourceFactory dataSources, IPrincipals principals, ILogger<DatabaseSetup> logger)
{
    private const string HoldingRole = "poolcar_dbsetup_holder";

    public async Task RunAsync(DbSetupOptions options, CancellationToken cancellationToken)
    {
        var database = Identifier.Quote(options.Database);
        var principal = Identifier.Quote(options.Principal);

        await using (var server = dataSources("postgres"))
        {
            if (options.Reset)
            {
                await ResetAsync(server, options, database, cancellationToken);
            }

            if (await ExistsAsync(server, "SELECT 1 FROM pg_database WHERE datname = $1", options.Database, cancellationToken))
            {
                LogDatabaseExists(options.Database);
            }
            else
            {
                LogCreatingDatabase(options.Database);
                await ExecuteAsync(server, $"CREATE DATABASE {database}", cancellationToken);
            }

            var objectId = Guid.Parse(options.PrincipalObjectId);
            if (!await ExistsAsync(server, "SELECT 1 FROM pg_roles WHERE rolname = $1", options.Principal, cancellationToken))
            {
                LogCreatingPrincipal(options.Principal);
                await using var connection = await server.OpenConnectionAsync(cancellationToken);
                await principals.CreateAsync(connection, options.Principal, objectId, cancellationToken);
            }
            else
            {
                var boundTo = await GetObjectIdAsync(server, options.Principal, cancellationToken);
                if (boundTo == objectId)
                {
                    LogPrincipalExists(options.Principal);
                }
                else
                {
                    // The role belongs to an earlier identity with the same name, such as a recreated managed identity.
                    LogReplacingPrincipal(options.Principal, boundTo, objectId);
                    await ReplacePrincipalAsync(options.Database, options.Principal, objectId, cancellationToken);
                }
            }

            // Changing the owner requires being able to SET ROLE to the new owner when the administrator isn't a superuser.
            if (!await ExistsAsync(server, "SELECT 1 WHERE pg_has_role(current_user, $1, 'SET')", options.Principal, cancellationToken))
            {
                LogGrantingMembership(options.Principal);
                await ExecuteAsync(server, $"GRANT {principal} TO CURRENT_USER", cancellationToken);
            }

            LogChangingOwner(options.Database, options.Principal);
            await ExecuteAsync(server, $"ALTER DATABASE {database} OWNER TO {principal}", cancellationToken);
        }

        await using (var target = dataSources(options.Database))
        {
            LogCreatingExtension(options.Database);
            await ExecuteAsync(target, "CREATE EXTENSION IF NOT EXISTS btree_gist", cancellationToken);
        }

        LogDone(options.Database);
    }

    private async Task ResetAsync(
        NpgsqlDataSource server, DbSetupOptions options, string database, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(server, "SELECT 1 FROM pg_database WHERE datname = $1", options.Database, cancellationToken))
        {
            LogNothingToReset(options.Database);
            return;
        }

        LogDroppingDatabase(options.Database);
        // FORCE terminates the open connections to the database first.
        await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)", cancellationToken);
    }

    private async Task<Guid?> GetObjectIdAsync(
        NpgsqlDataSource server, string principal, CancellationToken cancellationToken)
    {
        await using var connection = await server.OpenConnectionAsync(cancellationToken);
        return await principals.GetObjectIdAsync(connection, principal, cancellationToken);
    }

    /// <summary>
    /// Drops the role and creates it again for the object ID. Its objects in the database move to a holding role
    /// in between, so the new role owns them. Runs in one transaction, so a failure leaves the old role in place.
    /// </summary>
    private async Task ReplacePrincipalAsync(
        string databaseName, string principalName, Guid objectId, CancellationToken cancellationToken)
    {
        var principal = Identifier.Quote(principalName);
        var holder = Identifier.Quote(HoldingRole);

        await using var database = dataSources(databaseName);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // REASSIGN OWNED and DROP OWNED need the privileges of the roles involved.
        await ExecuteAsync(connection, $"CREATE ROLE {holder} NOLOGIN", cancellationToken);
        await ExecuteAsync(connection, $"GRANT {holder} TO CURRENT_USER", cancellationToken);
        await GrantMembershipIfMissingAsync(connection, principalName, cancellationToken);
        await ExecuteAsync(connection, $"REASSIGN OWNED BY {principal} TO {holder}", cancellationToken);
        await ExecuteAsync(connection, $"DROP OWNED BY {principal}", cancellationToken);
        await ExecuteAsync(connection, $"DROP ROLE {principal}", cancellationToken);

        await principals.CreateAsync(connection, principalName, objectId, cancellationToken);
        await GrantMembershipIfMissingAsync(connection, principalName, cancellationToken);
        await ExecuteAsync(connection, $"REASSIGN OWNED BY {holder} TO {principal}", cancellationToken);
        await ExecuteAsync(connection, $"DROP ROLE {holder}", cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task GrantMembershipIfMissingAsync(
        NpgsqlConnection connection, string principalName, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1 WHERE pg_has_role(current_user, $1, 'USAGE')", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = principalName });
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            await ExecuteAsync(connection, $"GRANT {Identifier.Quote(principalName)} TO CURRENT_USER", cancellationToken);
        }
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlDataSource dataSource, string sql, string value, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter { Value = value });
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task ExecuteAsync(NpgsqlDataSource dataSource, string sql, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database {Database} doesn't exist; nothing to reset.")]
    private partial void LogNothingToReset(string database);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Terminating connections to database {Database} and dropping it.")]
    private partial void LogDroppingDatabase(string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database {Database} exists.")]
    private partial void LogDatabaseExists(string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating database {Database}.")]
    private partial void LogCreatingDatabase(string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Principal {Principal} exists.")]
    private partial void LogPrincipalExists(string principal);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Principal {Principal} is bound to object ID {ExistingObjectId} instead of {ObjectId}; recreating it.")]
    private partial void LogReplacingPrincipal(string principal, Guid? existingObjectId, Guid objectId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating principal {Principal}.")]
    private partial void LogCreatingPrincipal(string principal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Granting membership of {Principal} to the administrator.")]
    private partial void LogGrantingMembership(string principal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Making {Principal} the owner of database {Database}.")]
    private partial void LogChangingOwner(string database, string principal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating extension btree_gist in database {Database}.")]
    private partial void LogCreatingExtension(string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database {Database} is ready.")]
    private partial void LogDone(string database);
}
