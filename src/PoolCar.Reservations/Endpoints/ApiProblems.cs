using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Endpoints;

/// <summary>Creates problem details responses with a Dutch <c>detail</c> and a machine-readable <c>code</c>.</summary>
public static class ApiProblems
{
    public const string CodeExtension = "code";
    public const string ConflictingDatesExtension = "conflictingDates";
    public const string UnauthorizedCode = "unauthorized";
    public const string NotFoundCode = "not-found";

    /// <summary>Maps a domain error: <c>invalid</c> to 422, <c>conflict</c> to 409 and <c>not-allowed</c> to
    /// 403.</summary>
    public static ProblemHttpResult FromError(DomainError error, IReadOnlyList<DateOnly>? conflictingDates = null)
    {
        var (status, title) = error.Code switch
        {
            DomainError.InvalidCode => (StatusCodes.Status422UnprocessableEntity, "Ongeldige aanvraag"),
            DomainError.ConflictCode => (StatusCodes.Status409Conflict, "Conflict"),
            DomainError.NotAllowedCode => (StatusCodes.Status403Forbidden, "Niet toegestaan"),
            _ => (StatusCodes.Status400BadRequest, "Ongeldige aanvraag"),
        };

        var problem = Create(status, title, error.Message, error.Code);
        if (conflictingDates is { Count: > 0 })
        {
            problem.Extensions[ConflictingDatesExtension] = conflictingDates;
        }

        return TypedResults.Problem(problem);
    }

    /// <summary>A rule that depends on the current state, such as picking up a returned car, is a conflict.</summary>
    public static ProblemHttpResult StateConflict(DomainError error) =>
        FromError(DomainError.Conflict(error.Message));

    public static ProblemHttpResult Invalid(string detail) => FromError(DomainError.Invalid(detail));

    public static ProblemHttpResult Unauthorized() => TypedResults.Problem(Create(
        StatusCodes.Status401Unauthorized,
        "Niet aangemeld",
        "Meld je aan om verder te gaan.",
        UnauthorizedCode));

    public static ProblemHttpResult Forbidden() =>
        FromError(DomainError.NotAllowed("Je kan alleen je eigen reserveringen wijzigen."));

    public static ProblemHttpResult NotFound(string detail) =>
        TypedResults.Problem(Create(StatusCodes.Status404NotFound, "Niet gevonden", detail, NotFoundCode));

    public static ProblemDetails Create(int status, string title, string detail, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions[CodeExtension] = code;
        return problem;
    }
}
