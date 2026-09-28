using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ReceiptExtractor.Api.Data;

namespace ReceiptExtractor.Api.Summaries;

public sealed record CategoryTotal(string Category, decimal Total, int Count);

public sealed record PeriodTotal(string Label, DateOnly Start, decimal Total, int Count);

public sealed record SummaryResponse(
    Period Period,
    string Label,
    DateOnly Start,
    DateOnly End,             // exclusive
    decimal Total,
    decimal VatTotal,
    int Count,
    IReadOnlyList<CategoryTotal> ByCategory,
    IReadOnlyList<PeriodTotal> History, // this period and the ones before it, oldest first
    DateOnly Previous,        // pass as ?date= to move back one period
    DateOnly Next);           // pass as ?date= to move forward one period

public static class SummaryEndpoints
{
    private const int HistoryLength = 6;

    public static IEndpointRouteBuilder MapSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/summary", GetSummary)
            .WithTags("Summaries")
            .WithSummary("Spending for one period (confirmed receipts only), with a per-category breakdown and recent history.");
        return app;
    }

    /// <param name="period">Daily, Weekly, Monthly, Quarterly or Yearly (default Monthly)</param>
    /// <param name="date">Any day inside the period you want (default today)</param>
    private static async Task<Ok<SummaryResponse>> GetSummary(
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct,
        Period period = Period.Monthly,
        DateOnly? date = null)
    {
        var current = PeriodRange.For(period, date ?? DateOnly.FromDateTime(time.GetLocalNow().DateTime));

        var ranges = new List<PeriodRange> { current };
        while (ranges.Count < HistoryLength)
            ranges.Insert(0, ranges[0].Previous());

        var from = ranges[0].Start;
        var to = current.End;

        // Only confirmed receipts count: those are the ones a human has checked.
        // Money is summed in C# because SQLite can't sum decimals reliably.
        var receipts = await db.Receipts
            .Where(r => r.Status == ReceiptStatus.Confirmed && r.Date >= from && r.Date < to)
            .Select(r => new { Date = r.Date!.Value, r.Total, r.VatAmount, r.Category })
            .ToListAsync(ct);

        var inCurrent = receipts.Where(r => current.Contains(r.Date)).ToList();

        var byCategory = inCurrent
            .GroupBy(r => r.Category ?? "Other")
            .Select(g => new CategoryTotal(g.Key, g.Sum(r => r.Total ?? 0), g.Count()))
            .OrderByDescending(c => c.Total)
            .ToList();

        var history = ranges
            .Select(range =>
            {
                var matches = receipts.Where(r => range.Contains(r.Date)).ToList();
                return new PeriodTotal(range.Label, range.Start, matches.Sum(r => r.Total ?? 0), matches.Count);
            })
            .ToList();

        return TypedResults.Ok(new SummaryResponse(
            period,
            current.Label,
            current.Start,
            current.End,
            inCurrent.Sum(r => r.Total ?? 0),
            inCurrent.Sum(r => r.VatAmount ?? 0),
            inCurrent.Count,
            byCategory,
            history,
            current.Previous().Start,
            current.Next().Start));
    }
}
