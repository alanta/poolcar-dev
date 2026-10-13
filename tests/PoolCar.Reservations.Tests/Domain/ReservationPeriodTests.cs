using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Domain;

public class ReservationPeriodTests
{
    private static readonly DateOnly s_date = new(2026, 10, 14);
    private static readonly DateTimeOffset s_start = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_end = s_start.AddHours(4);

    private static ReservationPeriod CreatePeriod() => ReservationPeriod.Schedule(s_date, s_start, s_end);

    [Fact]
    public void New_period_is_scheduled()
    {
        var period = CreatePeriod();

        period.Status.ShouldBe(PeriodStatus.Scheduled);
        period.Date.ShouldBe(s_date);
        period.PickedUpAt.ShouldBeNull();
        period.ReturnedAt.ShouldBeNull();
    }

    [Fact]
    public void Period_must_end_after_it_starts()
    {
        Should.Throw<ArgumentException>(() => ReservationPeriod.Schedule(s_date, s_start, s_start));
    }

    [Fact]
    public void Pick_up_at_start_succeeds()
    {
        var period = CreatePeriod();

        var result = period.PickUp(s_start);

        result.IsSuccess.ShouldBeTrue();
        period.Status.ShouldBe(PeriodStatus.PickedUp);
        period.PickedUpAt.ShouldBe(s_start);
    }

    [Fact]
    public void Pick_up_before_start_is_refused()
    {
        var period = CreatePeriod();

        var result = period.PickUp(s_start.AddMinutes(-1));

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.NotAllowedCode);
        period.Status.ShouldBe(PeriodStatus.Scheduled);
    }

    [Fact]
    public void Pick_up_at_end_is_refused()
    {
        var period = CreatePeriod();

        var result = period.PickUp(s_end);

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.NotAllowedCode);
        period.Status.ShouldBe(PeriodStatus.Scheduled);
    }

    [Fact]
    public void Pick_up_twice_is_refused()
    {
        var period = CreatePeriod();
        period.PickUp(s_start).IsSuccess.ShouldBeTrue();

        var result = period.PickUp(s_start.AddMinutes(5));

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.NotAllowedCode);
        period.PickedUpAt.ShouldBe(s_start);
    }

    [Fact]
    public void Early_return_frees_the_rest_of_the_period()
    {
        var period = CreatePeriod();
        period.PickUp(s_start).IsSuccess.ShouldBeTrue();
        var returnedAt = s_start.AddHours(1);

        var result = period.Return(returnedAt);

        result.IsSuccess.ShouldBeTrue();
        period.Status.ShouldBe(PeriodStatus.Returned);
        period.ReturnedAt.ShouldBe(returnedAt);
        period.End.ShouldBe(returnedAt);
    }

    [Fact]
    public void Late_return_keeps_the_period_end()
    {
        var period = CreatePeriod();
        period.PickUp(s_start).IsSuccess.ShouldBeTrue();
        var returnedAt = s_end.AddHours(2);

        var result = period.Return(returnedAt);

        result.IsSuccess.ShouldBeTrue();
        period.End.ShouldBe(s_end);
        period.ReturnedAt.ShouldBe(returnedAt);
    }

    [Fact]
    public void Return_of_a_period_that_was_not_picked_up_is_refused()
    {
        var period = CreatePeriod();

        var result = period.Return(s_start.AddHours(1));

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.NotAllowedCode);
        period.Status.ShouldBe(PeriodStatus.Scheduled);
        period.End.ShouldBe(s_end);
    }

    [Fact]
    public void Scheduled_period_is_not_picked_up_once_it_has_ended()
    {
        var period = CreatePeriod();

        period.IsNotPickedUp(s_end.AddTicks(-1)).ShouldBeFalse();
        period.IsNotPickedUp(s_end).ShouldBeTrue();
        period.IsOverdue(s_end).ShouldBeFalse();
    }

    [Fact]
    public void Picked_up_period_is_overdue_once_it_has_ended()
    {
        var period = CreatePeriod();
        period.PickUp(s_start).IsSuccess.ShouldBeTrue();

        period.IsOverdue(s_end.AddTicks(-1)).ShouldBeFalse();
        period.IsOverdue(s_end).ShouldBeTrue();
        period.IsNotPickedUp(s_end).ShouldBeFalse();
    }

    [Fact]
    public void Returned_period_is_neither_overdue_nor_not_picked_up()
    {
        var period = CreatePeriod();
        period.PickUp(s_start).IsSuccess.ShouldBeTrue();
        period.Return(s_end.AddHours(1)).IsSuccess.ShouldBeTrue();

        period.IsOverdue(s_end.AddHours(2)).ShouldBeFalse();
        period.IsNotPickedUp(s_end.AddHours(2)).ShouldBeFalse();
    }
}
