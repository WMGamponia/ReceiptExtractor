using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
            catch (DbUpdateConcurrencyException)
            {
                // Deleted by the user while it was queued; nothing left to save.
                log.LogInformation("Receipt {ReceiptId} was deleted during extraction", id);
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
        var minConfidence = scope.ServiceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value.MinConfidence;

        var receipt = await db.Receipts.FindAsync([id], ct);
        if (receipt is null || receipt.Status is ReceiptStatus.Confirmed)
            return;

        receipt.Status = ReceiptStatus.Processing;
        receipt.Error = null;
        await db.SaveChangesAsync(ct);

        // Fields the user corrected and asked to keep ("re-extract with my changes").
        var locked = receipt.LockedFields.Where(UserHints.IsKnown).ToHashSet();
        var userFields = receipt.Fields;
        var userCategory = receipt.Category;
        var hints = UserHints.Describe(userFields, userCategory, locked);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await extractor.ExtractAsync(receipt.ImagePath, hints, ct);
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);

            var fields = UserHints.Keep(result.Fields, userFields, locked);
            var category = locked.Contains(UserHints.Category) ? userCategory : result.Category;
            receipt.ApplyFields(fields, category);

            // Keep the first, unaided answer: it's what corrections are measured against (free eval data).
            if (hints.Count == 0 || receipt.ModelOutput is null)
                receipt.ModelOutput = result.ModelOutput;

            var confidence = ConfidenceGate.WithLocked(result.Confidence ?? Confidence.None, locked);
            receipt.Confidence = confidence.Overall;
            receipt.FieldConfidence = new(confidence.Fields);

            receipt.LockedFields = [];
            receipt.Flags = ReceiptValidator.Validate(fields, today);
            if (ConfidenceGate.Flag(confidence, minConfidence) is { } unsure)
                receipt.Flags.Add(unsure); // a shaky read is not "Ready to confirm"
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
