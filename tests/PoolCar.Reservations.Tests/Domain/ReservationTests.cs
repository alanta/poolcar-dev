using Microsoft.Extensions.Time.Testing;
using PoolCar.Reservations.Domain;

namespace PoolCar.Reservations.Tests.Domain;

public class ReservationTests
{
    private static readonly Guid s_employeeId = Guid.NewGuid();
    private static readonly DateOnly s_friday = new(2026, 10, 16);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));

    private Reservation CreateReservation(DateOnly from, DateOnly to, DayPart part = DayPart.FullDay)
    {
        var result = Reservation.Create(s_employeeId, "HPN72V", from, to, part, ReservationSource.User, _time.GetUtcNow());
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Fact]
    public void Create_sets_the_reservation_details()
    {
        var reservation = CreateReservation(s_friday, s_friday, DayPart.Morning);

        reservation.Id.ShouldNotBe(Guid.Empty);
        reservation.EmployeeId.ShouldBe(s_employeeId);
        reservation.CarId.ShouldBe("HPN72V");
        reservation.FromDate.ShouldBe(s_friday);
        reservation.ToDate.ShouldBe(s_friday);
        reservation.DayPart.ShouldBe(DayPart.Morning);
        reservation.Source.ShouldBe(ReservationSource.User);
        reservation.CreatedAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public void Create_adds_one_period_per_date_including_weekends()
    {
        var monday = s_friday.AddDays(3);

        var reservation = CreateReservation(s_friday, monday, DayPart.Afternoon);

        reservation.Periods.Select(p => p.Date).ShouldBe([s_friday, s_friday.AddDays(1), s_friday.AddDays(2), monday]);
        reservation.Periods.ShouldAllBe(p => p.Status == PeriodStatus.Scheduled);
        foreach (var period in reservation.Periods)
        {
            var (start, end) = DayParts.ToPeriod(period.Date, DayPart.Afternoon);
            period.Start.ShouldBe(start);
            period.End.ShouldBe(end);
        }
    }

    [Fact]
    public void Full_day_reservation_has_one_ten_hour_period_per_date()
    {
        var reservation = CreateReservation(s_friday, s_friday);

        var period = reservation.Periods.ShouldHaveSingleItem();
        (period.End - period.Start).ShouldBe(TimeSpan.FromHours(10));
        DayParts.DateOf(period.Start).ShouldBe(s_friday);
    }

    [Fact]
    public void Create_allows_fourteen_days()
    {
        var reservation = CreateReservation(s_friday, s_friday.AddDays(13));

        reservation.Periods.Count.ShouldBe(14);
    }

    [Fact]
    public void Create_refuses_fifteen_days()
    {
        var result = Reservation.Create(
            s_employeeId, "HPN72V", s_friday, s_friday.AddDays(14), DayPart.FullDay, ReservationSource.User,
            _time.GetUtcNow());

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.InvalidCode);
    }

    [Fact]
    public void Create_refuses_from_date_after_to_date()
    {
        var result = Reservation.Create(
            s_employeeId, "HPN72V", s_friday, s_friday.AddDays(-1), DayPart.FullDay, ReservationSource.User,
            _time.GetUtcNow());

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.InvalidCode);
    }

    [Fact]
    public void Create_refuses_missing_car()
    {
        var result = Reservation.Create(
            s_employeeId, " ", s_friday, s_friday, DayPart.FullDay, ReservationSource.User, _time.GetUtcNow());

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.InvalidCode);
    }

    [Fact]
    public void Create_refuses_unknown_day_part()
    {
        var result = Reservation.Create(
            s_employeeId, "HPN72V", s_friday, s_friday, (DayPart)42, ReservationSource.User, _time.GetUtcNow());

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.InvalidCode);
    }

    [Fact]
    public void Cancel_cancels_every_scheduled_period()
    {
        var reservation = CreateReservation(s_friday, s_friday.AddDays(2));

        reservation.Cancel(_time.GetUtcNow()).IsSuccess.ShouldBeTrue();

        reservation.Periods.ShouldAllBe(p => p.Status == PeriodStatus.Cancelled);
        reservation.CancelledAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public void Cancel_leaves_picked_up_periods_untouched()
    {
        var reservation = CreateReservation(s_friday, s_friday.AddDays(1));
        var first = reservation.Periods[0];
        first.PickUp(first.Start).IsSuccess.ShouldBeTrue();

        reservation.Cancel(first.Start.AddHours(1)).IsSuccess.ShouldBeTrue();

        first.Status.ShouldBe(PeriodStatus.PickedUp);
        reservation.Periods[1].Status.ShouldBe(PeriodStatus.Cancelled);
    }

    [Fact]
    public void Cancel_without_scheduled_periods_is_refused()
    {
        var reservation = CreateReservation(s_friday, s_friday);
        var period = reservation.Periods[0];
        period.PickUp(period.Start).IsSuccess.ShouldBeTrue();

        var result = reservation.Cancel(period.Start.AddHours(1));

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.NotAllowedCode);
        period.Status.ShouldBe(PeriodStatus.PickedUp);
        reservation.CancelledAt.ShouldBeNull();
    }

    [Fact]
    public void Cancel_twice_is_refused()
    {
        var reservation = CreateReservation(s_friday, s_friday);
        reservation.Cancel(_time.GetUtcNow()).IsSuccess.ShouldBeTrue();

        reservation.Cancel(_time.GetUtcNow()).Error.ShouldNotBeNull().Code.ShouldBe(DomainError.NotAllowedCode);
    }

    [Fact]
    public void Book_now_in_the_morning_runs_until_the_end_of_the_morning()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 14, 9, 0, 42, TimeSpan.Zero));
        var now = _time.GetUtcNow();

        var result = Reservation.BookNow(s_employeeId, "HPN72V", ReservationSource.User, now);

        result.IsSuccess.ShouldBeTrue();
        var reservation = result.Value;
        var today = DayParts.DateOf(now);
        reservation.DayPart.ShouldBe(DayPart.Morning);
        reservation.FromDate.ShouldBe(today);
        reservation.ToDate.ShouldBe(today);
        var period = reservation.Periods.ShouldHaveSingleItem();
        period.Date.ShouldBe(today);
        period.Start.ShouldBe(now.AddSeconds(-42));
        period.End.ShouldBe(DayParts.ToPeriod(today, DayPart.Morning).End);
        period.Status.ShouldBe(PeriodStatus.PickedUp);
        period.PickedUpAt.ShouldBe(now);
    }

    [Fact]
    public void Book_now_in_the_afternoon_runs_until_the_end_of_the_afternoon()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 14, 13, 0, 0, TimeSpan.Zero));
        var now = _time.GetUtcNow();

        var result = Reservation.BookNow(s_employeeId, "HPN72V", ReservationSource.Simulator, now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DayPart.ShouldBe(DayPart.Afternoon);
        result.Value.Source.ShouldBe(ReservationSource.Simulator);
        var period = result.Value.Periods.ShouldHaveSingleItem();
        period.Start.ShouldBe(now);
        period.End.ShouldBe(DayParts.ToPeriod(DayParts.DateOf(now), DayPart.Afternoon).End);
    }

    [Fact]
    public void Book_now_outside_day_part_hours_is_refused()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero));

        var result = Reservation.BookNow(s_employeeId, "HPN72V", ReservationSource.User, _time.GetUtcNow());

        result.Error.ShouldNotBeNull().Code.ShouldBe(DomainError.InvalidCode);
        result.Error.Message.ShouldBe("Nu kan je geen auto boeken. Plan vooruit.");
    }

    [Fact]
    public void Book_now_blocks_the_car_for_the_rest_of_the_day_part()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));
        var now = _time.GetUtcNow();
        var booked = Reservation.BookNow(s_employeeId, "HPN72V", ReservationSource.User, now).Value!;
        var today = DayParts.DateOf(now);

        var morning = CreateReservation(today, today, DayPart.Morning);
        var afternoon = CreateReservation(today, today, DayPart.Afternoon);

        AvailabilityPolicy.FindConflicts(booked.Periods, morning.Periods).ShouldBe([today]);
        AvailabilityPolicy.FindConflicts(booked.Periods, afternoon.Periods).ShouldBeEmpty();
    }
}
