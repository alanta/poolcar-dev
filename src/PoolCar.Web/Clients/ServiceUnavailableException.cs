using Polly;

namespace PoolCar.Web.Clients;

/// <summary>The backend services that the web app calls.</summary>
public enum BackendService
{
    Fleet,
    Reservations,
}

/// <summary>Thrown when a backend service cannot be reached, times out or fails with a server error. Responses
/// with a 4xx status are not outages and keep their own handling.</summary>
public sealed class ServiceUnavailableException(BackendService service, Exception? innerException = null)
    : Exception($"The {service} API is unavailable.", innerException)
{
    public BackendService Service { get; } = service;

    /// <summary>Whether the exception means the service is unavailable: a connection failure, a server error, a
    /// timeout or the resilience pipeline giving up. A cancelled request is not an outage.</summary>
    public static bool IsOutage(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } => IsServerError((int)status),
        ExecutionRejectedException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

    public static bool IsServerError(int status) => status >= StatusCodes.Status500InternalServerError;

    /// <summary>The <see cref="ServiceUnavailableException"/> in the exception or its inner exceptions.</summary>
    public static ServiceUnavailableException? Find(Exception? exception) => exception switch
    {
        null => null,
        ServiceUnavailableException unavailable => unavailable,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find).FirstOrDefault(e => e is not null),
        _ => Find(exception.InnerException),
    };
}
