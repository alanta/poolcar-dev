namespace PoolCar.DbSetup;

/// <summary>Settings for the database setup, read from the <c>DbSetup</c> section.</summary>
public sealed class DbSetupOptions
{
    public const string Section = "DbSetup";

    /// <summary>The fully qualified domain name of the PostgreSQL server.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The Entra administrator of the server, which runs the setup.</summary>
    public string AdminUser { get; set; } = string.Empty;

    /// <summary>The database to create.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The Entra principal that Reservations connects as, which may read and write the data.</summary>
    public string Principal { get; set; } = string.Empty;

    /// <summary>The object ID of <see cref="Principal"/>.</summary>
    public string PrincipalObjectId { get; set; } = string.Empty;

    /// <summary>Whether to drop the database before creating it again.</summary>
    public bool Reset { get; set; }
}
