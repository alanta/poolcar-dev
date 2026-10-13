using System.Diagnostics.CodeAnalysis;

namespace PoolCar.Web.Clients;

/// <summary>Machine-readable error codes in the <c>code</c> extension of API problem details.</summary>
public static class ApiProblemCodes
{
    public const string Unauthorized = "unauthorized";
    public const string NotAllowed = "not-allowed";
    public const string NotFound = "not-found";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
}

/// <summary>A failed API call: the HTTP status, the error code, a Dutch message to show and, for booking
/// conflicts, the dates on which the car is already reserved.</summary>
public sealed record ApiProblem(int Status, string? Code, string Detail, IReadOnlyList<DateOnly> ConflictingDates)
{
    public const string FallbackDetail = "Er ging iets mis. Probeer het later opnieuw.";

    public bool IsConflict => Status == 409;

    public bool IsInvalid => Status == 422;

    public bool IsNotFound => Status == 404;
}

/// <summary>The outcome of an API call: a value on success, otherwise the problem.</summary>
public sealed class ApiResult<T>
{
    private ApiResult(T? value, ApiProblem? problem)
    {
        Value = value;
        Problem = problem;
        IsSuccess = problem is null;
    }

    public T? Value { get; }

    public ApiProblem? Problem { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Problem))]
    public bool IsSuccess { get; }

    public static ApiResult<T> Success(T value) => new(value, null);

    public static ApiResult<T> Failure(ApiProblem problem) => new(default, problem);

    public static implicit operator ApiResult<T>(ApiProblem problem) => Failure(problem);

    /// <summary>Converts the value, keeping the problem of a failed call.</summary>
    public ApiResult<TResult> Map<TResult>(Func<T, TResult> map) =>
        IsSuccess ? ApiResult<TResult>.Success(map(Value)) : ApiResult<TResult>.Failure(Problem);
}

/// <summary>Thrown when a read fails for a reason other than a missing resource.</summary>
public sealed class ApiProblemException(ApiProblem problem)
    : Exception($"The API returned {problem.Status} ({problem.Code ?? "no code"}): {problem.Detail}")
{
    public ApiProblem Problem { get; } = problem;
}
