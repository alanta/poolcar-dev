using System.Security.Claims;
using PoolCar.Web.Auth;
using PoolCar.Web.Clients;

namespace PoolCar.Web.Employees;

/// <summary>The signed-in user and their employee record, loaded once per request. Call
/// <see cref="LoadAsync"/> before reading <see cref="Employee"/>, <see cref="IsKnown"/> or
/// <see cref="HomeOffice"/>; the onboarding filter does this for every page.</summary>
public sealed class CurrentEmployee(IHttpContextAccessor httpContextAccessor, ReservationsApiClient reservationsClient)
{
    private Task<Employee?>? _loading;
    private Employee? _employee;
    private bool _loaded;

    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    /// <summary>The object id of the signed-in user, or <see cref="Guid.Empty"/> when it is missing.</summary>
    public Guid Id => IsAuthenticated && Guid.TryParse(User.GetObjectId(), out var id) ? id : Guid.Empty;

    public bool IsOperator => IsAuthenticated && User.IsOperator();

    /// <summary>The employee's name, or the name of the sign-in when there is no employee record yet.</summary>
    public string Name => _employee?.Name ?? User.GetName() ?? string.Empty;

    public string FirstName => GetFirstName(Name);

    /// <summary>The Fleet office id of the employee's home office, or null for an unknown employee.</summary>
    public string? HomeOffice => Employee?.HomeOffice;

    public bool IsKnown => Employee is not null;

    public Employee? Employee => _loaded
        ? _employee
        : throw new InvalidOperationException($"Call {nameof(LoadAsync)} before reading the employee.");

    /// <summary>Fetches the employee record once per request.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        _loading ??= Id == Guid.Empty
            ? Task.FromResult<Employee?>(null)
            : reservationsClient.GetEmployeeAsync(Id, cancellationToken);
        Set(await _loading);
    }

    /// <summary>Replaces the cached employee after it was saved.</summary>
    public void Set(Employee? employee)
    {
        _employee = employee;
        _loaded = true;
    }

    /// <summary>The first word of a name; for an e-mail address the first part before the <c>@</c>.</summary>
    public static string GetFirstName(string name)
    {
        var value = name.Trim();
        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at > 0)
        {
            value = value[..at];
        }

        var first = value.Split([' ', '.', '_'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return first.Length == 0 ? first : char.ToUpperInvariant(first[0]) + first[1..];
    }
}
