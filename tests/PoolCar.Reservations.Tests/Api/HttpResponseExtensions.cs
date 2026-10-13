using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace PoolCar.Reservations.Tests.Api;

internal static class HttpResponseExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
        where T : class
    {
        var value = await response.Content.ReadFromJsonAsync<T>(ReservationsApiFactory.Json);
        return value.ShouldNotBeNull();
    }

    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static string? Code(this JsonElement problem) => problem.GetProperty("code").GetString();

    public static IReadOnlyList<DateOnly> ConflictingDates(this JsonElement problem) =>
        [
            .. problem.GetProperty("conflictingDates").EnumerateArray()
                .Select(d => DateOnly.ParseExact(d.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
        ];
}
