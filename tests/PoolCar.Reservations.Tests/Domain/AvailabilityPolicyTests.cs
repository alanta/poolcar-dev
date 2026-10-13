using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Domain;

public class AvailabilityPolicyTests
{
    private static readonly DateOnly s_date = new(2026, 10, 14);
    private static readonly DateTimeOffset s_morning = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private static ReservationPeriod Period(DateTimeOffset start, double hours) =>
        ReservationPeriod.Schedule(DateOnly.FromDateTime(start.UtcDateTime), start, start.AddHours(hours));

    private static ReservationPeriod PickedUp(DateTimeOffset start, double hours)
    {
        var period = Period(start, hours);
        period.PickUp(start).IsSuccess.ShouldBeTrue();
        return period;
    }

    [Fact]
    public void Overlapping_period_is_a_conflict()
    {
        var existing = Period(s_morning, 4);
        var requested = Period(s_morning.AddHours(2), 4);

        AvailabilityPolicy.FindConflicts([existing], [requested]).ShouldBe([s_date]);
    }

    [Fact]
    public void Adjacent_period_is_no_conflict()
    {
        var existing = Period(s_morning, 4);
        var requested = Period(s_morning.AddHours(4), 6);

        AvailabilityPolicy.FindConflicts([existing], [requested]).ShouldBeEmpty();
    }

    [Fact]
    public void Picked_up_period_blocks()
    {
        var existing = PickedUp(s_morning, 4);
        var requested = Period(s_morning, 10);

        AvailabilityPolicy.FindConflicts([existing], [requested]).ShouldBe([s_date]);
    }

    [Fact]
    public void Returned_period_does_not_block()
    {
        var existing = PickedUp(s_morning, 4);
        existing.Return(s_morning.AddHours(1)).IsSuccess.ShouldBeTrue();

        AvailabilityPolicy.FindConflicts([existing], [Period(s_morning.AddHours(2), 2)]).ShouldBeEmpty();
    }

    [Fact]
    public void Not_picked_up_period_blocks_until_its_end()
    {
        var existing = Period(s_morning, 4);
        existing.IsNotPickedUp(s_morning.AddHours(4)).ShouldBeTrue();

        AvailabilityPolicy.FindConflicts([existing], [Period(s_morning.AddHours(3), 1)]).ShouldBe([s_date]);
        AvailabilityPolicy.FindConflicts([existing], [Period(s_morning.AddHours(4), 1)]).ShouldBeEmpty();
    }

    [Fact]
    public void Conflicts_list_each_date_once_in_order()
    {
        var existing = new[] { Period(s_morning.AddDays(2), 10), Period(s_morning, 10) };
        var requested = new[]
        {
            Period(s_morning.AddDays(2), 4),
            Period(s_morning.AddDays(1), 4),
            Period(s_morning, 4),
            Period(s_morning.AddHours(5), 4),
        };

        AvailabilityPolicy.FindConflicts(existing, requested).ShouldBe([s_date, s_date.AddDays(2)]);
    }

    [Fact]
    public void Ensure_available_returns_a_conflict_error()
    {
        var result = AvailabilityPolicy.EnsureAvailable([Period(s_morning, 4)], [Period(s_morning, 4)]);

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.ConflictCode);
        result.Error.Message.ShouldContain("14-10-2026");
    }

    [Fact]
    public void Ensure_available_succeeds_without_conflicts()
    {
        AvailabilityPolicy.EnsureAvailable([], [Period(s_morning, 4)]).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Car_with_an_overdue_period_is_blocked()
    {
        var overdue = PickedUp(s_morning, 4);

        AvailabilityPolicy.IsBlockedToday([overdue], s_morning.AddHours(3)).ShouldBeFalse();
        AvailabilityPolicy.IsBlockedToday([overdue], s_morning.AddHours(5)).ShouldBeTrue();
    }

    [Fact]
    public void Car_with_a_not_picked_up_period_is_not_blocked()
    {
        AvailabilityPolicy.IsBlockedToday([Period(s_morning, 4)], s_morning.AddHours(5)).ShouldBeFalse();
    }

    [Fact]
    public void Upcoming_period_is_at_risk_when_the_car_is_overdue()
    {
        var overdue = PickedUp(s_morning, 4);
        var upcoming = Period(s_morning.AddDays(1), 4);
        var now = s_morning.AddHours(5);

        AvailabilityPolicy.IsAtRisk(upcoming, [overdue, upcoming], now).ShouldBeTrue();
    }

    [Fact]
    public void Upcoming_period_is_not_at_risk_when_the_car_is_on_time()
    {
        var current = PickedUp(s_morning, 4);
        var upcoming = Period(s_morning.AddDays(1), 4);

        AvailabilityPolicy.IsAtRisk(upcoming, [current, upcoming], s_morning.AddHours(1)).ShouldBeFalse();
    }

    [Fact]
    public void Ended_or_overdue_period_is_not_at_risk()
    {
        var overdue = PickedUp(s_morning, 4);
        var ended = Period(s_morning.AddHours(-10), 2);
        var now = s_morning.AddHours(5);

        AvailabilityPolicy.IsAtRisk(ended, [overdue, ended], now).ShouldBeFalse();
        AvailabilityPolicy.IsAtRisk(overdue, [overdue], now).ShouldBeFalse();
    }
}
