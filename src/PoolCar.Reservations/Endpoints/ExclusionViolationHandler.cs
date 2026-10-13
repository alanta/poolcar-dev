using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Endpoints;

/// <summary>Turns an unhandled double booking, refused by the database, into a 409 response.</summary>
public sealed class ExclusionViolationHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var postgres = exception as PostgresException
            ?? (exception as DbUpdateException)?.InnerException as PostgresException;
        if (postgres?.SqlState != PostgresErrorCodes.ExclusionViolation)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = ApiProblems.Create(
                StatusCodes.Status409Conflict,
                "Conflict",
                "De auto is al gereserveerd.",
                DomainError.ConflictCode),
        });
    }
}
