using System.Text.RegularExpressions;

namespace PoolCar.DbSetup;

/// <summary>Validates and quotes PostgreSQL identifiers such as database and role names.</summary>
public static partial class Identifier
{
    public const string Pattern = "^[a-z0-9_-]+$";

    public static bool IsValid(string? name) => name is not null && NamePattern().IsMatch(name);

    /// <summary>Returns the name as a quoted identifier, after checking it against <see cref="Pattern"/>.</summary>
    public static string Quote(string name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException($"'{name}' does not match {Pattern}.", nameof(name));
        }

        return $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    // \z instead of $, which also matches before a final newline.
    [GeneratedRegex(@"^[a-z0-9_-]+\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamePattern();
}
