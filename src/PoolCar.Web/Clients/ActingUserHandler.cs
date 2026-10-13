using PoolCar.Web.Auth;

namespace PoolCar.Web.Clients;

/// <summary>Adds the signed-in user's employee id and operator flag to Reservations API requests.</summary>
public sealed class ActingUserHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    public const string EmployeeHeader = "X-PoolCar-Employee";
    public const string OperatorHeader = "X-PoolCar-Operator";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true && Guid.TryParse(user.GetObjectId(), out var employeeId))
        {
            request.Headers.Remove(EmployeeHeader);
            request.Headers.Add(EmployeeHeader, employeeId.ToString());

            request.Headers.Remove(OperatorHeader);
            if (user.IsOperator())
            {
                request.Headers.Add(OperatorHeader, "true");
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
