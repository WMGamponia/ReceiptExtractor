using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.Extraction;

/// <summary>
/// Background job: takes receipt IDs off the queue, runs the model, validates the result, saves it.
/// One receipt at a time, because the GPU can only run one model call efficiently anyway.
/// </summary>
public sealed class ExtractionWorker(
    ExtractionQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<ExtractionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var id in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Extraction crashed for receipt {ReceiptId}", id);
            }
        }
    }

    /// <summary>If the app stopped mid-extraction, pick those receipts up again on startup.</summary>
    private async Task RequeueUnfinishedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unfinished = await db.Receipts
            .Where(r => r.Status == ReceiptStatus.Queued || r.Status == ReceiptStatus.Processing)
            .OrderBy(r => r.UploadedAtUtc)
            .Select(r => r.Id)
            .ToListAsync(ct);

        foreach (var id in unfinished)
            await queue.EnqueueAsync(id, ct);

        if (unfinished.Count > 0)
            log.LogInformation("Re-queued {Count} unfinished receipts", unfinished.Count);
    }

    private async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var extractor = scope.ServiceProvider.GetRequiredService<IReceiptExtractor>();

        var receipt = await db.Receipts.FindAsync([id], ct);
        if (receipt is null || receipt.Status is ReceiptStatus.Confirmed)
            return;

        receipt.Status = ReceiptStatus.Processing;
        receipt.Error = null;
        await db.SaveChangesAsync(ct);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await extractor.ExtractAsync(receipt.ImagePath, ct);
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);

            receipt.ApplyFields(result.Fields, result.Category);
            receipt.ModelOutput = result.ModelOutput;
            receipt.Flags = ReceiptValidator.Validate(result.Fields, today);
            receipt.Status = receipt.Flags.Count > 0 ? ReceiptStatus.NeedsReview : ReceiptStatus.Extracted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Extraction failed for receipt {ReceiptId}", id);
            receipt.Status = ReceiptStatus.Failed;
            receipt.Error = ex.Message;
        }

        receipt.ExtractionSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Receipt {ReceiptId}: {Status} in {Seconds}s", id, receipt.Status, receipt.ExtractionSeconds);
    }
}
