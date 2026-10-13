using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PoolCar.Reservations.Data;
using PoolCar.Reservations.Endpoints;
using PoolCar.Reservations.Tests.Data;

namespace PoolCar.Reservations.Tests.Api;

/// <summary>The Reservations API on its own database and clock, shared by the tests in a class.</summary>
public class ReservationsApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private string _connectionString = string.Empty;

    private readonly ClockSwitch _clock = new();

    /// <summary>The clock of the service; replace it with <see cref="StartClockAt"/>.</summary>
    public FakeTimeProvider Time => _clock.Current;

    /// <summary>Sets the clock of the service to a new instant, also one before the current time.</summary>
    public FakeTimeProvider StartClockAt(DateTimeOffset now) => _clock.Current = new FakeTimeProvider(now);

    public async ValueTask InitializeAsync() => _connectionString = await postgres.CreateDatabaseAsync();

    /// <summary>Interceptors added to the service's database context.</summary>
    protected virtual IEnumerable<IInterceptor> Interceptors => [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:reservations", _connectionString);
        builder.UseSetting("Database:Migrate", "true");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.ConfigureDbContext<ReservationsDbContext>(options => options.AddInterceptors(Interceptors));
        });
    }

    /// <summary>Creates a client that acts as the given employee.</summary>
    public HttpClient CreateClient(Guid? employeeId, bool isOperator = false)
    {
        var client = CreateClient();
        if (employeeId is { } id)
        {
            client.DefaultRequestHeaders.Add(ActingUser.EmployeeHeader, id.ToString());
        }

        if (isOperator)
        {
            client.DefaultRequestHeaders.Add(ActingUser.OperatorHeader, "true");
        }

        return client;
    }

    /// <summary>Creates an employee and returns its id.</summary>
    public async Task<Guid> AddEmployeeAsync(string name = "Sanne de Vries")
    {
        var id = Guid.NewGuid();
        using var client = CreateClient();
        var response = await client.PutAsJsonAsync($"/employees/{id}", new EmployeeRequest(name, "utrecht"), Json);
        response.EnsureSuccessStatusCode();
        return id;
    }

    public ReservationsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ReservationsDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    /// <summary>Forwards to a replaceable fake clock, so every test can start at its own instant.</summary>
    private sealed class ClockSwitch : TimeProvider
    {
        public FakeTimeProvider Current { get; set; } = new();

        public override DateTimeOffset GetUtcNow() => Current.GetUtcNow();

        public override TimeZoneInfo LocalTimeZone => Current.LocalTimeZone;

        public override long GetTimestamp() => Current.GetTimestamp();

        public override long TimestampFrequency => Current.TimestampFrequency;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            Current.CreateTimer(callback, state, dueTime, period);
    }
}
