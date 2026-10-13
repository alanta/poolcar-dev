namespace PoolCar.Reservations.Domain;

/// <summary>A broken business rule, with a code for the API and a message for the user.</summary>
public sealed record DomainError(string Code, string Message)
{
    public const string ConflictCode = "conflict";
    public const string NotAllowedCode = "not-allowed";
    public const string InvalidCode = "invalid";

    public static DomainError Conflict(string message) => new(ConflictCode, message);

    public static DomainError NotAllowed(string message) => new(NotAllowedCode, message);

    public static DomainError Invalid(string message) => new(InvalidCode, message);
}
