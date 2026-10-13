using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoolCar.Web.Clients;

/// <summary>Typed client for the Reservations API. Requests carry the acting user's headers through
/// <see cref="ActingUserHandler"/>. Writes return an <see cref="ApiResult{T}"/>; reads return null for a missing
/// resource and throw <see cref="ApiProblemException"/> for other errors.</summary>
public class ReservationsApiClient(HttpClient httpClient)
{
    /// <summary>The maximum number of car ids per availability or schedule request; larger lists are split.</summary>
    public const int MaxCarsPerRequest = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<string> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode ? "ok" : "error";
        }
        catch (HttpRequestException)
        {
            return "error";
        }
    }

    public Task<Employee?> GetEmployeeAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetOrDefaultAsync<Employee>($"/employees/{id}", cancellationToken);

    /// <summary>Creates or updates the employee's name and home office.</summary>
    public Task<ApiResult<Employee>> SaveEmployeeAsync(
        Guid id,
        string name,
        string homeOffice,
        CancellationToken cancellationToken = default) =>
        SendAsync<Employee>(HttpMethod.Put, $"/employees/{id}", new EmployeeRequest(name, homeOffice), cancellationToken);

    /// <summary>Changes the home office of an existing employee and keeps the name.</summary>
    public Task<ApiResult<Employee>> ChangeHomeOfficeAsync(
        Employee employee,
        string homeOffice,
        CancellationToken cancellationToken = default) =>
        SaveEmployeeAsync(employee.Id, employee.Name, homeOffice, cancellationToken);

    public Task<ApiResult<Reservation>> CreateReservationAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<Reservation>(HttpMethod.Post, "/reservations", request, cancellationToken);

    /// <summary>Reserves the car from now until the end of the current day part and picks it up.</summary>
    public Task<ApiResult<Reservation>> BookNowAsync(string carId, CancellationToken cancellationToken = default) =>
        SendAsync<Reservation>(HttpMethod.Post, "/reservations/now", new BookNowRequest(carId), cancellationToken);

    /// <summary>The acting employee's reservations of the last 30 days and the future.</summary>
    public async Task<IReadOnlyList<Reservation>> GetMyReservationsAsync(CancellationToken cancellationToken = default) =>
        (await SendAsync<List<Reservation>>(HttpMethod.Get, "/reservations/mine", null, cancellationToken))
            .ValueOrThrow();

    public Task<Reservation?> GetReservationAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetOrDefaultAsync<Reservation>($"/reservations/{id}", cancellationToken);

    /// <summary>Cancels the periods that have not been picked up.</summary>
    public Task<ApiResult<Reservation>> CancelReservationAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<Reservation>(HttpMethod.Post, $"/reservations/{id}/cancel", null, cancellationToken);

    public Task<ApiResult<Reservation>> PickUpAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        SendAsync<Reservation>(HttpMethod.Post, $"/periods/{periodId}/pickup", null, cancellationToken);

    public Task<ApiResult<Reservation>> ReturnAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        SendAsync<Reservation>(HttpMethod.Post, $"/periods/{periodId}/return", null, cancellationToken);

    /// <summary>The ids of the cars that are not free for <paramref name="dayPart"/> on every date from
    /// <paramref name="from"/> to <paramref name="to"/>. Fails with a 422 problem when the request breaks a booking
    /// rule, such as more than 14 days.</summary>
    public async Task<ApiResult<IReadOnlyList<string>>> GetBusyCarIdsAsync(
        IReadOnlyCollection<string> carIds,
        DateOnly from,
        DateOnly to,
        DayPart dayPart,
        CancellationToken cancellationToken = default)
    {
        var busy = new List<string>();
        foreach (var batch in Batch(carIds))
        {
            var result = await SendAsync<CarIdsResponse>(
                HttpMethod.Post,
                "/availability/busy",
                new BusyCarsRequest(batch, from, to, dayPart),
                cancellationToken);
            if (!result.IsSuccess)
            {
                return result.Problem;
            }

            busy.AddRange(result.Value.CarIds);
        }

        return ApiResult<IReadOnlyList<string>>.Success(busy);
    }

    /// <summary>The ids of the cars that are in use now or were not returned.</summary>
    public async Task<IReadOnlyList<string>> GetCarIdsInUseAsync(
        IReadOnlyCollection<string> carIds,
        CancellationToken cancellationToken = default)
    {
        var inUse = new List<string>();
        foreach (var batch in Batch(carIds))
        {
            var result = await SendAsync<CarIdsResponse>(
                HttpMethod.Post,
                "/availability/now",
                new CarsNowRequest(batch),
                cancellationToken);
            inUse.AddRange(result.ValueOrThrow().CarIds);
        }

        return inUse;
    }

    /// <summary>The periods that are not cancelled for the cars from <paramref name="from"/> to
    /// <paramref name="to"/> (at most 62 days).</summary>
    public async Task<IReadOnlyList<ScheduleEntry>> GetScheduleAsync(
        IReadOnlyCollection<string> carIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<ScheduleEntry>();
        foreach (var batch in Batch(carIds))
        {
            var result = await SendAsync<List<ScheduleEntry>>(
                HttpMethod.Post,
                "/schedule",
                new ScheduleRequest(batch, from, to),
                cancellationToken);
            entries.AddRange(result.ValueOrThrow());
        }

        return entries;
    }

    private static IEnumerable<string[]> Batch(IReadOnlyCollection<string> carIds) =>
        carIds.Distinct().Chunk(MaxCarsPerRequest);

    private async Task<T?> GetOrDefaultAsync<T>(string uri, CancellationToken cancellationToken)
        where T : class
    {
        var result = await SendAsync<T>(HttpMethod.Get, uri, null, cancellationToken);
        return result.IsSuccess || !result.Problem.IsNotFound ? result.ValueOrThrow() : null;
    }

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string uri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException($"The Reservations API returned an empty body for {uri}.");
            return ApiResult<T>.Success(value);
        }

        return await ReadProblemAsync(response, cancellationToken);
    }

    private static async Task<ApiProblem> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        ProblemBody? body = null;
        if (response.Content.Headers.ContentType?.MediaType is { } mediaType && mediaType.Contains("json"))
        {
            try
            {
                body = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
            }
        }

        var detail = string.IsNullOrWhiteSpace(body?.Detail) || response.StatusCode >= HttpStatusCode.InternalServerError
            ? ApiProblem.FallbackDetail
            : body.Detail;
        return new ApiProblem(status, body?.Code, detail, body?.ConflictingDates ?? []);
    }

    private sealed record ProblemBody(string? Detail, string? Code, IReadOnlyList<DateOnly>? ConflictingDates);
}

internal static class ApiResultExtensions
{
    public static T ValueOrThrow<T>(this ApiResult<T> result) =>
        result.IsSuccess ? result.Value : throw new ApiProblemException(result.Problem);
}
