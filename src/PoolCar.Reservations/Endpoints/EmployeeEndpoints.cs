using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Endpoints;

public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/employees").WithTags("Employees");

        group.MapGet("/{id:guid}", GetEmployee)
            .WithName("GetEmployee")
            .WithSummary("Get an employee")
            .WithDescription("Returns the employee, for example to look up the signed-in user.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", PutEmployee)
            .WithName("PutEmployee")
            .WithSummary("Create or update an employee")
            .WithDescription("Saves the name and home office of an employee. New employees are signed-in users.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<Results<Ok<EmployeeResponse>, ProblemHttpResult>> GetEmployee(
        Guid id,
        ReservationsDbContext db,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return employee is null
            ? ApiProblems.NotFound("Deze medewerker bestaat niet.")
            : TypedResults.Ok(EmployeeResponse.From(employee));
    }

    private static async Task<Results<Ok<EmployeeResponse>, ProblemHttpResult>> PutEmployee(
        Guid id,
        EmployeeRequest request,
        ReservationsDbContext db,
        CancellationToken cancellationToken)
    {
        Result<Employee> result;
        try
        {
            result = await SaveAsync(id, request, db, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        })
        {
            // The employee was created by a concurrent request; update it instead.
            db.ChangeTracker.Clear();
            result = await SaveAsync(id, request, db, cancellationToken);
        }

        return result.IsSuccess
            ? TypedResults.Ok(EmployeeResponse.From(result.Value))
            : ApiProblems.FromError(result.Error);
    }

    private static async Task<Result<Employee>> SaveAsync(
        Guid id,
        EmployeeRequest request,
        ReservationsDbContext db,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            var created = Employee.Create(
                id,
                request.Name ?? string.Empty,
                request.HomeOffice ?? string.Empty,
                EmployeeKind.SignedIn);
            if (!created.IsSuccess)
            {
                return created.Error;
            }

            employee = created.Value;
            db.Employees.Add(employee);
        }
        else
        {
            var updated = employee.Update(request.Name ?? string.Empty, request.HomeOffice ?? string.Empty);
            if (!updated.IsSuccess)
            {
                return updated.Error;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return employee;
    }
}
