using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PoolCar.Reservations.Data;

namespace PoolCar.DbSetup;

/// <summary>Opens a connection pool to a database on the server as the administrator.</summary>
public delegate NpgsqlDataSource DataSourceFactory(string database);

/// <summary>
/// Creates and migrates a database owned by the administrator, idempotently, and gives a principal access to its data.
/// </summary>
public sealed partial class DatabaseSetup(
    DataSourceFactory dataSources, IPrincipals principals, ILogger<DatabaseSetup> logger)
{
    public async Task RunAsync(DbSetupOptions options, CancellationToken cancellationToken)
    {
        var database = Identifier.Quote(options.Database);

        await using (var server = dataSources("postgres"))
        {
            if (options.Reset)
            {
                await ResetAsync(server, options, database, cancellationToken);
            }

            if (await ExistsAsync(server, "SELECT 1 FROM pg_database WHERE datname = $1", [options.Database], cancellationToken))
            {
                LogDatabaseExists(options.Database);
            }
            else
            {
                LogCreatingDatabase(options.Database);
                await ExecuteAsync(server, $"CREATE DATABASE {database}", cancellationToken);
            }

            var objectId = Guid.Parse(options.PrincipalObjectId);
            if (!await ExistsAsync(server, "SELECT 1 FROM pg_roles WHERE rolname = $1", [options.Principal], cancellationToken))
            {
                LogCreatingPrincipal(options.Principal);
                await using var connection = await server.OpenConnectionAsync(cancellationToken);
                await principals.CreateAsync(connection, options.Principal, objectId, cancellationToken);
            }
            else
            {
                await TakeOwnershipAsync(server, options, cancellationToken);

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
        }

        await using (var target = dataSources(options.Database))
        {
            // First, so the principal keeps access to the data when a later step fails. The default privileges
            // cover the tables and sequences that the migrations create.
            LogGrantingDataAccess(options.Principal, options.Database);
            await using (var connection = await target.OpenConnectionAsync(cancellationToken))
            {
                foreach (var statement in GrantDataAccess(database, Identifier.Quote(options.Principal)))
                {
                    await ExecuteAsync(connection, statement, cancellationToken);
                }
            }

            LogCreatingExtension(options.Database);
            await ExecuteAsync(target, "CREATE EXTENSION IF NOT EXISTS btree_gist", cancellationToken);

            await using var db = CreateContext(target);
            await MigrateAsync(db, options.Database, cancellationToken);
        }

        LogDone(options.Database);
    }

    /// <summary>
    /// Lets the principal read and write the data in schema public, also in the tables and sequences that the
    /// administrator creates later, but not change the schema.
    /// </summary>
    private static string[] GrantDataAccess(string database, string principal) =>
    [
        $"GRANT CONNECT ON DATABASE {database} TO {principal}",
        $"GRANT USAGE ON SCHEMA public TO {principal}",
        $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {principal}",
        $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {principal}",
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {principal}",
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO {principal}",
    ];

    /// <summary>Revokes what <see cref="GrantDataAccess"/> grants.</summary>
    private static string[] RevokeDataAccess(string database, string principal) =>
    [
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM {principal}",
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM {principal}",
        $"REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM {principal}",
        $"REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {principal}",
        $"REVOKE ALL ON SCHEMA public FROM {principal}",
        $"REVOKE ALL ON DATABASE {database} FROM {principal}",
    ];

    private static ReservationsDbContext CreateContext(NpgsqlDataSource dataSource) =>
        new(new DbContextOptionsBuilder<ReservationsDbContext>()
            .UseNpgsql(dataSource)
            .UseSnakeCaseNamingConvention()
            .Options);

    private async Task MigrateAsync(ReservationsDbContext db, string database, CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            LogNoPendingMigrations(database);
            return;
        }

        LogApplyingMigrations(database, string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
    }

    private async Task ResetAsync(
        NpgsqlDataSource server, DbSetupOptions options, string database, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(server, "SELECT 1 FROM pg_database WHERE datname = $1", [options.Database], cancellationToken))
        {
            LogNothingToReset(options.Database);
            return;
        }

        LogDroppingDatabase(options.Database);
        // FORCE terminates the open connections to the database first.
        await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)", cancellationToken);
    }

    /// <summary>
    /// Makes the administrator the owner of a database that the principal owns, and of the principal's objects in it.
    /// </summary>
    private async Task TakeOwnershipAsync(
        NpgsqlDataSource server, DbSetupOptions options, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(
            server,
            "SELECT 1 FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba WHERE d.datname = $1 AND r.rolname = $2",
            [options.Database, options.Principal],
            cancellationToken))
        {
            return;
        }

        LogTakingOwnership(options.Database, options.Principal);
        await using var database = dataSources(options.Database);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // REASSIGN OWNED and changing the owner of the database need the privileges of the principal.
        await using (var command = new NpgsqlCommand("SELECT 1 WHERE pg_has_role(current_user, $1, 'USAGE')", connection))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = options.Principal });
            if (await command.ExecuteScalarAsync(cancellationToken) is null)
            {
                await ExecuteAsync(connection, $"GRANT {Identifier.Quote(options.Principal)} TO CURRENT_USER", cancellationToken);
            }
        }

        await ExecuteAsync(connection, $"REASSIGN OWNED BY {Identifier.Quote(options.Principal)} TO CURRENT_USER", cancellationToken);
        await ExecuteAsync(connection, $"ALTER DATABASE {Identifier.Quote(options.Database)} OWNER TO CURRENT_USER", cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Guid?> GetObjectIdAsync(
        NpgsqlDataSource server, string principal, CancellationToken cancellationToken)
    {
        await using var connection = await server.OpenConnectionAsync(cancellationToken);
        return await principals.GetObjectIdAsync(connection, principal, cancellationToken);
    }

    /// <summary>
    /// Drops the role and creates it again for the object ID, after revoking its privileges, which would otherwise
    /// block dropping it. Runs in one transaction, so a failure leaves the old role in place.
    /// </summary>
    private async Task ReplacePrincipalAsync(
        string databaseName, string principalName, Guid objectId, CancellationToken cancellationToken)
    {
        var principal = Identifier.Quote(principalName);

        await using var database = dataSources(databaseName);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var statement in RevokeDataAccess(Identifier.Quote(databaseName), principal))
        {
            await ExecuteAsync(connection, statement, cancellationToken);
        }

        await ExecuteAsync(connection, $"DROP ROLE {principal}", cancellationToken);
        await principals.CreateAsync(connection, principalName, objectId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlDataSource dataSource, string sql, string[] values, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Database {Database} is owned by {Principal}; making the administrator its owner.")]
    private partial void LogTakingOwnership(string database, string principal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Granting {Principal} access to the data in database {Database}.")]
    private partial void LogGrantingDataAccess(string principal, string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating extension btree_gist in database {Database}.")]
    private partial void LogCreatingExtension(string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database {Database} has no pending migrations.")]
    private partial void LogNoPendingMigrations(string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying migrations {Migrations} to database {Database}.")]
    private partial void LogApplyingMigrations(string database, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database {Database} is ready.")]
    private partial void LogDone(string database);
}
