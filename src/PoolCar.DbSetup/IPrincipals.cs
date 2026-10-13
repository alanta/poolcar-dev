using Npgsql;

namespace PoolCar.DbSetup;

/// <summary>Reads and creates the database roles that are bound to a principal's object ID.</summary>
public interface IPrincipals
{
    /// <summary>Returns the object ID the role is bound to, or <see langword="null"/> when it isn't bound to one.</summary>
    Task<Guid?> GetObjectIdAsync(NpgsqlConnection connection, string principal, CancellationToken cancellationToken);

    /// <summary>Creates the role for a principal that doesn't exist yet.</summary>
    Task CreateAsync(NpgsqlConnection connection, string principal, Guid objectId, CancellationToken cancellationToken);
}

/// <summary>Roles for Microsoft Entra service principals, such as managed identities.</summary>
public sealed class EntraPrincipals : IPrincipals
{
    public async Task<Guid?> GetObjectIdAsync(
        NpgsqlConnection connection, string principal, CancellationToken cancellationToken)
    {
        // Documented to return TABLE(rolename name, principalType text, objectId text, ...); read the columns by
        // name, which Npgsql matches case-insensitively, rather than depending on how their case is stored.
        await using var command = new NpgsqlCommand(
            "SELECT * FROM pg_catalog.pgaadauth_list_principals(false)", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var roleName = reader.GetOrdinal("rolename");
        var objectId = reader.GetOrdinal("objectId");
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetString(roleName) == principal)
            {
                return Guid.TryParse(reader.GetString(objectId), out var id) ? id : null;
            }
        }

        return null;
    }

    public async Task CreateAsync(
        NpgsqlConnection connection, string principal, Guid objectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT * FROM pg_catalog.pgaadauth_create_principal_with_oid($1, $2, 'service', false, false)", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = principal });
        command.Parameters.Add(new NpgsqlParameter { Value = objectId.ToString() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
