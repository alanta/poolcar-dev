using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PoolCar.Reservations.Data;

/// <summary>Stores instants in UTC, as required for <c>timestamptz</c> columns.</summary>
internal sealed class UtcDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);
