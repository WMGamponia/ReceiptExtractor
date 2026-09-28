using System.Globalization;

namespace ReceiptExtractor.Api.Summaries;

public enum Period { Daily, Weekly, Monthly, Quarterly, Yearly }

/// <summary>
/// A date range for one summary period. <see cref="End"/> is exclusive.
/// Rules from SCOPE.md: weeks are ISO weeks (Monday–Sunday), quarters are calendar quarters.
/// </summary>
public sealed record PeriodRange(Period Period, DateOnly Start, DateOnly End, string Label)
{
    public static PeriodRange For(Period period, DateOnly anyDayInPeriod)
    {
        var d = anyDayInPeriod;
        switch (period)
        {
            case Period.Daily:
                return new(period, d, d.AddDays(1), d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            case Period.Weekly:
                var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); // DayOfWeek.Sunday = 0
                var dt = monday.ToDateTime(TimeOnly.MinValue);
                return new(period, monday, monday.AddDays(7),
                    $"{ISOWeek.GetYear(dt)}-W{ISOWeek.GetWeekOfYear(dt):00}");

            case Period.Monthly:
                var month = new DateOnly(d.Year, d.Month, 1);
                return new(period, month, month.AddMonths(1), month.ToString("yyyy-MM", CultureInfo.InvariantCulture));

            case Period.Quarterly:
                var quarter = (d.Month - 1) / 3;
                var qStart = new DateOnly(d.Year, quarter * 3 + 1, 1);
                return new(period, qStart, qStart.AddMonths(3), $"{d.Year}-Q{quarter + 1}");

            case Period.Yearly:
                var year = new DateOnly(d.Year, 1, 1);
                return new(period, year, year.AddYears(1), d.Year.ToString(CultureInfo.InvariantCulture));

            default:
                throw new ArgumentOutOfRangeException(nameof(period), period, null);
        }
    }

    public PeriodRange Previous() => For(Period, Start.AddDays(-1));

    public PeriodRange Next() => For(Period, End);

    public bool Contains(DateOnly date) => date >= Start && date < End;
}
