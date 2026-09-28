using ReceiptExtractor.Api.Summaries;

namespace ReceiptExtractor.Tests;

public class PeriodRangeTests
{
    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Theory]
    [InlineData("2025-12-20", "2025-12-15", "2025-12-22", "2025-W51")] // Saturday -> Monday..Sunday
    [InlineData("2025-12-15", "2025-12-15", "2025-12-22", "2025-W51")] // Monday is the first day
    [InlineData("2025-12-21", "2025-12-15", "2025-12-22", "2025-W51")] // Sunday is the last day
    [InlineData("2026-01-01", "2025-12-29", "2026-01-05", "2026-W01")] // ISO week 1 starts in December
    [InlineData("2021-01-03", "2020-12-28", "2021-01-04", "2020-W53")] // ...and Jan days can be last year's week 53
    public void Weeks_are_ISO_Monday_to_Sunday(string day, string start, string end, string label)
    {
        var range = PeriodRange.For(Period.Weekly, D(day));

        Assert.Equal(D(start), range.Start);
        Assert.Equal(D(end), range.End);
        Assert.Equal(label, range.Label);
    }

    [Theory]
    [InlineData("2025-01-01", "2025-01-01", "2025-04-01", "2025-Q1")]
    [InlineData("2025-03-31", "2025-01-01", "2025-04-01", "2025-Q1")]
    [InlineData("2025-04-01", "2025-04-01", "2025-07-01", "2025-Q2")]
    [InlineData("2025-12-20", "2025-10-01", "2026-01-01", "2025-Q4")]
    public void Quarters_are_calendar_quarters(string day, string start, string end, string label)
    {
        var range = PeriodRange.For(Period.Quarterly, D(day));

        Assert.Equal(D(start), range.Start);
        Assert.Equal(D(end), range.End);
        Assert.Equal(label, range.Label);
    }

    [Fact]
    public void Daily_monthly_yearly()
    {
        var day = D("2024-02-29");

        Assert.Equal("2024-02-29", PeriodRange.For(Period.Daily, day).Label);
        Assert.Equal((D("2024-02-01"), D("2024-03-01")),
            (PeriodRange.For(Period.Monthly, day).Start, PeriodRange.For(Period.Monthly, day).End));
        Assert.Equal("2024", PeriodRange.For(Period.Yearly, day).Label);
    }

    [Fact]
    public void Previous_and_next_move_one_period()
    {
        var december = PeriodRange.For(Period.Monthly, D("2025-12-20"));

        Assert.Equal("2025-11", december.Previous().Label);
        Assert.Equal("2026-01", december.Next().Label);
    }

    [Fact]
    public void End_is_exclusive()
    {
        var december = PeriodRange.For(Period.Monthly, D("2025-12-20"));

        Assert.True(december.Contains(D("2025-12-31")));
        Assert.False(december.Contains(D("2026-01-01")));
    }
}
