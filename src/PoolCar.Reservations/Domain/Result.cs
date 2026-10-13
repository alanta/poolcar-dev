using System.Diagnostics.CodeAnalysis;

namespace PoolCar.Reservations.Domain;

/// <summary>The outcome of a domain operation without a value.</summary>
public sealed class Result
{
    private static readonly Result s_success = new(null);

    private Result(DomainError? error) => Error = error;

    public DomainError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success() => s_success;

    public static Result Failure(DomainError error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result(DomainError error) => Failure(error);
}

/// <summary>The outcome of a domain operation that produces a value.</summary>
public sealed class Result<T>
{
    private Result(T? value, DomainError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public DomainError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value) => new(value ?? throw new ArgumentNullException(nameof(value)), null);

    public static Result<T> Failure(DomainError error) =>
        new(default, error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(DomainError error) => Failure(error);
}
