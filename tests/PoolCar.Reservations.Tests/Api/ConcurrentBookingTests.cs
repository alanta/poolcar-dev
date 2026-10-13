using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using PoolCar.Reservations.Domain;
using PoolCar.Reservations.Endpoints;
using PoolCar.Reservations.Tests.Data;
using static PoolCar.Reservations.Tests.Api.TestTimes;

namespace PoolCar.Reservations.Tests.Api;

public class ConcurrentBookingTests(ConcurrentBookingTests.Api api) : IClassFixture<ConcurrentBookingTests.Api>
{
    private const int Requests = 10;

    [Fact]
    public async Task Only_one_of_simultaneous_bookings_for_the_same_period_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        api.StartClockAt(Morning(Today));
        var carId = ReservationsDatabase.NewCarId();
        var clients = new List<HttpClient>();
        for (var i = 0; i < Requests; i++)
        {
            clients.Add(api.CreateClient(await api.AddEmployeeAsync($"Medewerker {i}")));
        }

        var request = new CreateReservationRequest(carId, Today.AddDays(1), Today.AddDays(1), DayPart.Morning);
        using var start = new Barrier(Requests);
        var responses = await Task.WhenAll(clients.Select(client => Task.Run(
            async () =>
            {
                start.SignalAndWait(ct);
                return await client.PostAsJsonAsync("/reservations", request, ReservationsApiFactory.Json, ct);
            },
            ct)));

        api.SaveBarrier.AllArrived.ShouldBeTrue();
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var refused = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        refused.Count.ShouldBe(Requests - 1);
        foreach (var response in refused)
        {
            (await response.ReadProblemAsync()).ConflictingDates().ShouldBe([Today.AddDays(1)]);
        }

        api.SaveBarrier.ExclusionViolations.ShouldBe(Requests - 1);
        await using var db = api.CreateContext();
        (await db.Reservations.CountAsync(r => r.CarId == carId, ct)).ShouldBe(1);
        clients.ForEach(c => c.Dispose());
    }

    public sealed class Api(PostgresFixture postgres) : ReservationsApiFactory(postgres)
    {
        public SimultaneousSaves SaveBarrier { get; } = new(Requests);

        protected override IEnumerable<IInterceptor> Interceptors => [SaveBarrier];
    }

    /// <summary>Holds new reservations until all requests have passed the availability check, so only the
    /// database can refuse the double bookings.</summary>
    public sealed class SimultaneousSaves(int count) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        private int _exclusionViolations;

        public bool AllArrived => _allArrived.Task.IsCompletedSuccessfully;

        public int ExclusionViolations => _exclusionViolations;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var addsReservation = eventData.Context!.ChangeTracker.Entries<Reservation>()
                .Any(e => e.State == EntityState.Added);
            if (addsReservation)
            {
                if (Interlocked.Increment(ref _arrived) == count)
                {
                    _allArrived.TrySetResult();
                }

                await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Exception.InnerException is PostgresException
                {
                    SqlState: PostgresErrorCodes.ExclusionViolation,
                })
            {
                Interlocked.Increment(ref _exclusionViolations);
            }

            return Task.CompletedTask;
        }
    }
}
