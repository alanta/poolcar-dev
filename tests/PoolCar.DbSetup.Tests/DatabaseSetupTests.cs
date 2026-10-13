using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PoolCar.DbSetup.Tests;
using PoolCar.Reservations.Data;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace PoolCar.DbSetup.Tests;

/// <summary>A PostgreSQL server with an administrator that, like on Azure, isn't a superuser.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string AdminUser = "dbadmin";
    private const string AdminPassword = "dbadmin-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var superuser = NpgsqlDataSource.Create(_container.GetConnectionString());
        await using var command = superuser.CreateCommand(
            $"CREATE ROLE {AdminUser} LOGIN CREATEDB CREATEROLE PASSWORD '{AdminPassword}'");
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Connects as the superuser, to inspect the server.</summary>
    public NpgsqlDataSource Superuser(string database = "postgres") =>
        NpgsqlDataSource.Create(ConnectionString(database));

    /// <summary>Connects as the administrator that runs the setup.</summary>
    public NpgsqlDataSource Admin(string database) =>
        NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(ConnectionString(database))
        {
            Username = AdminUser,
            Password = AdminPassword,
        }.ConnectionString);

    /// <summary>The connection string of the superuser.</summary>
    public string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;
}

public class DatabaseSetupTests(PostgresFixture postgres)
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly Guid _objectId = Guid.NewGuid();

    private DbSetupOptions Options(bool reset = false, Guid? objectId = null) => new()
    {
        Host = "localhost",
        AdminUser = PostgresFixture.AdminUser,
        Database = $"poolcar_{_suffix}",
        Principal = $"id-poolcar-{_suffix}",
        PrincipalObjectId = (objectId ?? _objectId).ToString(),
        Reset = reset,
    };

    private DatabaseSetup Setup() =>
        new(postgres.Admin, new CommentedRolePrincipals(), NullLogger<DatabaseSetup>.Instance);

    [Fact]
    public async Task Setup_creates_a_database_owned_by_the_administrator_with_btree_gist()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options();

        await Setup().RunAsync(options, ct);

        (await OwnerAsync(options.Database, ct)).ShouldBe(PostgresFixture.AdminUser);
        (await HasBtreeGistAsync(options.Database, ct)).ShouldBeTrue();
        var tableOwners = await TableOwnersAsync(options.Database, ct);
        tableOwners.ShouldNotBeEmpty();
        tableOwners.ShouldAllBe(owner => owner == PostgresFixture.AdminUser);
    }

    [Fact]
    public async Task Setup_applies_the_reservations_migrations()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options();

        await Setup().RunAsync(options, ct);

        await using var db = Context(options.Database);
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe(db.Database.GetMigrations());
    }

    [Fact]
    public async Task The_principal_can_read_and_write_data_but_not_change_the_schema()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options();
        await Setup().RunAsync(options, ct);
        await using var connection = await ConnectAsAsync(options.Principal, options.Database, ct);
        var id = Guid.NewGuid();

        await ExecuteAsync(connection, $"INSERT INTO employees VALUES ('{id}', 'Sanne de Vries', 'utrecht', 'SignedIn')", ct);
        await ExecuteAsync(connection, $"UPDATE employees SET name = 'Sanne Jansen' WHERE id = '{id}'", ct);
        (await ScalarAsync(connection, "SELECT count(*) FROM employees WHERE name = 'Sanne Jansen'", ct)).ShouldBe(1);
        await ExecuteAsync(connection, $"DELETE FROM employees WHERE id = '{id}'", ct);

        var exception = await Should.ThrowAsync<PostgresException>(
            () => ExecuteAsync(connection, "CREATE TABLE intruder (id int)", ct));
        exception.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_principal_can_use_tables_and_sequences_that_the_administrator_creates_later()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options();
        await Setup().RunAsync(options, ct);
        await CreateTableAsync(options.Database, ct, "CREATE TABLE marker (id int GENERATED BY DEFAULT AS IDENTITY)");
        await using var connection = await ConnectAsAsync(options.Principal, options.Database, ct);

        await ExecuteAsync(connection, "INSERT INTO marker DEFAULT VALUES", ct);

        (await ScalarAsync(connection, "SELECT count(*) FROM marker", ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Setup_is_idempotent_and_keeps_the_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options();
        await Setup().RunAsync(options, ct);
        await CreateTableAsync(options.Database, ct);
        var roleOid = await RoleOidAsync(options.Principal, ct);

        await Setup().RunAsync(options, ct);

        (await OwnerAsync(options.Database, ct)).ShouldBe(PostgresFixture.AdminUser);
        (await HasTableAsync(options.Database, ct)).ShouldBeTrue();
        (await RoleOidAsync(options.Principal, ct)).ShouldBe(roleOid);
        await using var connection = await ConnectAsAsync(options.Principal, options.Database, ct);
        (await ScalarAsync(connection, "SELECT count(*) FROM employees", ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_database_owned_by_the_principal_is_taken_over_by_the_administrator()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options();
        await Setup().RunAsync(options, ct);
        await SuperuserExecuteAsync(
            options.Database, $"ALTER DATABASE {Identifier.Quote(options.Database)} OWNER TO {Identifier.Quote(options.Principal)}", ct);
        await CreateTableAsync(options.Database, ct, owner: options.Principal);

        await Setup().RunAsync(options, ct);

        (await OwnerAsync(options.Database, ct)).ShouldBe(PostgresFixture.AdminUser);
        (await TableOwnerAsync(options.Database, ct)).ShouldBe(PostgresFixture.AdminUser);
        await using var connection = await ConnectAsAsync(options.Principal, options.Database, ct);
        (await ScalarAsync(connection, "SELECT count(*) FROM marker", ct)).ShouldBe(0);
        await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "CREATE TABLE intruder (id int)", ct));
    }

    [Fact]
    public async Task The_principal_keeps_data_access_after_a_takeover_when_migrating_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options();
        await Setup().RunAsync(options, ct);
        await SuperuserExecuteAsync(
            options.Database, $"ALTER DATABASE {Identifier.Quote(options.Database)} OWNER TO {Identifier.Quote(options.Principal)}", ct);
        await SuperuserExecuteAsync(
            options.Database, $"REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {Identifier.Quote(options.Principal)}", ct);
        await CreateTableAsync(options.Database, ct, owner: options.Principal);
        // Without its history the migrations run again and fail on the existing tables.
        await SuperuserExecuteAsync(options.Database, "DELETE FROM \"__EFMigrationsHistory\"", ct);

        await Should.ThrowAsync<PostgresException>(() => Setup().RunAsync(options, ct));

        (await OwnerAsync(options.Database, ct)).ShouldBe(PostgresFixture.AdminUser);
        (await TableOwnerAsync(options.Database, ct)).ShouldBe(PostgresFixture.AdminUser);
        await using var connection = await ConnectAsAsync(options.Principal, options.Database, ct);
        (await ScalarAsync(connection, "SELECT count(*) FROM marker", ct)).ShouldBe(0);
        (await ScalarAsync(connection, "SELECT count(*) FROM employees", ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_principal_bound_to_another_object_id_is_recreated_with_data_access()
    {
        var ct = TestContext.Current.CancellationToken;
        await Setup().RunAsync(Options(), ct);
        var options = Options(objectId: Guid.NewGuid());
        var roleOid = await RoleOidAsync(options.Principal, ct);

        await Setup().RunAsync(options, ct);

        (await RoleOidAsync(options.Principal, ct)).ShouldNotBe(roleOid);
        (await ObjectIdAsync(options.Principal, ct)).ShouldBe(Guid.Parse(options.PrincipalObjectId));
        (await OwnerAsync(options.Database, ct)).ShouldBe(PostgresFixture.AdminUser);
        await using var connection = await ConnectAsAsync(options.Principal, options.Database, ct);
        (await ScalarAsync(connection, "SELECT count(*) FROM employees", ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Reset_drops_the_database_and_creates_it_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options();
        await Setup().RunAsync(options, ct);
        await CreateTableAsync(options.Database, ct);
        await using var openConnection = postgres.Admin(options.Database);
        await using (var command = openConnection.CreateCommand("SELECT 1"))
        {
            await command.ExecuteScalarAsync(ct);
        }

        await Setup().RunAsync(Options(reset: true), ct);

        (await OwnerAsync(options.Database, ct)).ShouldBe(PostgresFixture.AdminUser);
        (await HasTableAsync(options.Database, ct)).ShouldBeFalse();
        (await HasBtreeGistAsync(options.Database, ct)).ShouldBeTrue();
        await using var db = Context(options.Database);
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_without_a_database_creates_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = Options(reset: true);

        await Setup().RunAsync(options, ct);

        (await OwnerAsync(options.Database, ct)).ShouldBe(PostgresFixture.AdminUser);
    }

    private ReservationsDbContext Context(string database) =>
        new(new DbContextOptionsBuilder<ReservationsDbContext>()
            .UseNpgsql(postgres.ConnectionString(database))
            .UseSnakeCaseNamingConvention()
            .Options);

    private async Task<string?> OwnerAsync(string database, CancellationToken ct)
    {
        await using var server = postgres.Superuser();
        await using var command = server.CreateCommand(
            "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = database });
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    private async Task<bool> HasBtreeGistAsync(string database, CancellationToken ct) =>
        await ScalarAsync(database, "SELECT count(*) FROM pg_extension WHERE extname = 'btree_gist'", ct) == 1;

    private async Task<bool> HasTableAsync(string database, CancellationToken ct) =>
        await ScalarAsync(database, "SELECT count(*) FROM pg_tables WHERE tablename = 'marker'", ct) == 1;

    private async Task CreateTableAsync(
        string database, CancellationToken ct, string sql = "CREATE TABLE marker (id int)", string? owner = null)
    {
        await using var db = owner is null ? postgres.Admin(database) : postgres.Superuser(database);
        await using var connection = await db.OpenConnectionAsync(ct);
        if (owner is not null)
        {
            await using var setRole = new NpgsqlCommand($"SET ROLE {Identifier.Quote(owner)}", connection);
            await setRole.ExecuteNonQueryAsync(ct);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Connects as the superuser acting as the role, which can't sign in with a password.</summary>
    private async Task<NpgsqlConnection> ConnectAsAsync(string role, string database, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString(database));
        await connection.OpenAsync(ct);
        await ExecuteAsync(connection, $"SET ROLE {Identifier.Quote(role)}", ct);
        return connection;
    }

    private async Task SuperuserExecuteAsync(string database, string sql, CancellationToken ct)
    {
        await using var db = postgres.Superuser(database);
        await using var command = db.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task<List<string>> TableOwnersAsync(string database, CancellationToken ct)
    {
        await using var db = postgres.Superuser(database);
        await using var command = db.CreateCommand("SELECT tableowner::text FROM pg_tables WHERE schemaname = 'public'");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var owners = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            owners.Add(reader.GetString(0));
        }

        return owners;
    }

    private async Task<string?> TableOwnerAsync(string database, CancellationToken ct)
    {
        await using var db = postgres.Superuser(database);
        await using var command = db.CreateCommand("SELECT tableowner::text FROM pg_tables WHERE tablename = 'marker'");
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    private async Task<uint?> RoleOidAsync(string role, CancellationToken ct)
    {
        await using var server = postgres.Superuser();
        await using var command = server.CreateCommand("SELECT oid FROM pg_roles WHERE rolname = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = role });
        return (uint?)await command.ExecuteScalarAsync(ct);
    }

    private async Task<Guid?> ObjectIdAsync(string role, CancellationToken ct)
    {
        await using var server = postgres.Superuser();
        await using var connection = await server.OpenConnectionAsync(ct);
        return await new CommentedRolePrincipals().GetObjectIdAsync(connection, role, ct);
    }

    private async Task<long> ScalarAsync(string database, string sql, CancellationToken ct)
    {
        await using var db = postgres.Superuser(database);
        await using var command = db.CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Creates a plain role with its object ID in the role's comment, since <c>pgaadauth_*</c> exists only on Azure.
    /// </summary>
    private sealed class CommentedRolePrincipals : IPrincipals
    {
        public async Task<Guid?> GetObjectIdAsync(
            NpgsqlConnection connection, string principal, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                "SELECT shobj_description(oid, 'pg_authid') FROM pg_roles WHERE rolname = $1", connection);
            command.Parameters.Add(new NpgsqlParameter { Value = principal });
            return await command.ExecuteScalarAsync(cancellationToken) is string comment
                && Guid.TryParse(comment, out var objectId) ? objectId : null;
        }

        public async Task CreateAsync(
            NpgsqlConnection connection, string principal, Guid objectId, CancellationToken cancellationToken)
        {
            var role = Identifier.Quote(principal);
            await using var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN", connection);
            await create.ExecuteNonQueryAsync(cancellationToken);
            await using var comment = new NpgsqlCommand($"COMMENT ON ROLE {role} IS '{objectId}'", connection);
            await comment.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
