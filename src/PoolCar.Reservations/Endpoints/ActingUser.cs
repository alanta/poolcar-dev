using Microsoft.AspNetCore.Mvc;

namespace PoolCar.Reservations.Endpoints;

/// <summary>The employee on whose behalf a request is made.</summary>
public sealed record ActingUser(Guid EmployeeId, bool IsOperator)
{
    public const string EmployeeHeader = "X-PoolCar-Employee";
    public const string OperatorHeader = "X-PoolCar-Operator";

    /// <summary>Employees may act on their own reservations; operators on any reservation.</summary>
    public bool CanActOn(Guid ownerId) => IsOperator || ownerId == EmployeeId;
}

/// <summary>The request headers that identify the acting user.</summary>
public sealed class ActingUserHeaders
{
    [FromHeader(Name = ActingUser.EmployeeHeader)]
    public string? Employee { get; init; }

    [FromHeader(Name = ActingUser.OperatorHeader)]
    public string? Operator { get; init; }

    /// <summary>Returns the acting user, or null when the employee header is missing or invalid.</summary>
    public ActingUser? ToActingUser()
    {
        if (!Guid.TryParse(Employee, out var employeeId) || employeeId == Guid.Empty)
        {
            return null;
        }

        var isOperator = string.Equals(Operator?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        return new ActingUser(employeeId, isOperator);
    }
}
