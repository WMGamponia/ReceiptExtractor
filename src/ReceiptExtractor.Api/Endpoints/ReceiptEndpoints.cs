using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Extraction;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.Endpoints;

public sealed class StorageOptions
{
    public string ReceiptsPath { get; set; } = "storage/receipts";
}

public sealed record ReceiptResponse(
    Guid Id,
    string Name,
    ReceiptStatus Status,
    string? Merchant,
    DateOnly? Date,
    decimal? Total,
    decimal? VatAmount,
    decimal? VatableSales,
    decimal? VatExemptSales,
    decimal? Discount,
    string? Category,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Corrections,
    double? ExtractionSeconds,
    string? Error,
    string OriginalFileName,
    DateTime UploadedAtUtc,
    DateTime? ConfirmedAtUtc,
    string ImageUrl)
{
    public static ReceiptResponse From(ReceiptRecord r) => new(
        r.Id, r.DisplayName, r.Status, r.Merchant, r.Date, r.Total, r.VatAmount, r.VatableSales,
        r.VatExemptSales, r.Discount, r.Category, r.Flags, r.Corrections, r.ExtractionSeconds, r.Error, r.OriginalFileName, r.UploadedAtUtc,
        r.ConfirmedAtUtc, $"/api/receipts/{r.Id}/image");
}

/// <summary>What the review screen sends back after the user checks/corrects the fields.</summary>
public sealed record ReceiptUpdate(
    string? Merchant,
    DateOnly? Date,
    decimal? Total,
    decimal? VatAmount,
    decimal? VatableSales,
    decimal? VatExemptSales,
    decimal? Discount,
    string? Category);

/// <summary>
/// "Re-extract with my changes": the fields named in <see cref="Locked"/> (model JSON names, e.g. "total")
/// are taken from <see cref="Fields"/> as correct; everything else is read again by the model.
/// </summary>
public sealed record ReextractRequest(ReceiptUpdate? Fields, IReadOnlyList<string>? Locked);

/// <summary>Bulk confirm: accept the extracted fields of several receipts as they are.</summary>
public sealed record BulkConfirmRequest(IReadOnlyList<Guid>? Ids);

public sealed record BulkSkipped(Guid Id, string Name, string Reason);

public sealed record BulkConfirmResult(IReadOnlyList<ReceiptResponse> Confirmed, IReadOnlyList<BulkSkipped> Skipped);

/// <summary>Bulk delete: remove several receipts and their image files.</summary>
public sealed record BulkDeleteRequest(IReadOnlyList<Guid>? Ids);

public sealed record BulkDeleteResult(IReadOnlyList<Guid> Deleted, IReadOnlyList<BulkSkipped> Skipped);

public static class ReceiptEndpoints
{
    private const int MaxBulk = 500;

    private const long MaxUploadBytes = 15 * 1024 * 1024;

    private static readonly Dictionary<string, (string Ext, string ContentType)> AllowedFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = (".jpg", "image/jpeg"),
            [".jpeg"] = (".jpg", "image/jpeg"),
            [".png"] = (".png", "image/png"),
        };

    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        var receipts = app.MapGroup("/api/receipts").WithTags("Receipts");

        receipts.MapPost("/", Upload)
            .DisableAntiforgery() // local API called by our own frontend; no cookie auth to protect
            .WithSummary("Upload a receipt image. Extraction runs in the background; poll GET /{id} for the result.");
        receipts.MapGet("/", List).WithSummary("All receipts, newest first.");
        receipts.MapGet("/{id:guid}", Get).WithSummary("One receipt: status, extracted fields and review flags.");
        receipts.MapGet("/{id:guid}/image", GetImage).WithSummary("The receipt image.");
        receipts.MapPut("/{id:guid}/image", ReplaceImage)
            .DisableAntiforgery()
            .WithSummary("Replace the image (e.g. after rotating it). Extracted fields are kept; extraction is NOT re-run.");
        receipts.MapPut("/{id:guid}", Confirm).WithSummary("Save the reviewed/corrected fields and mark the receipt confirmed.");
        receipts.MapPost("/confirm", ConfirmMany)
            .WithSummary("Confirm several receipts at once, keeping their extracted fields. Ones that can't be confirmed are skipped with a reason.");
        receipts.MapPost("/{id:guid}/retry", Retry).WithSummary("Run extraction again (e.g. after it failed).");
        receipts.MapPost("/{id:guid}/reextract", Reextract)
            .WithSummary("Keep the fields the user corrected, tell the model they're correct, and re-read the rest.");
        receipts.MapDelete("/{id:guid}", Delete)
            .WithSummary("Delete a receipt and its image files (including the pre-rotation backup). Not allowed while the model is reading it.");
        receipts.MapPost("/delete", DeleteMany)
            .WithSummary("Delete several receipts and their image files. Ones the model is reading right now are skipped with a reason.");

        return app;
    }

    private static async Task<Results<Accepted<ReceiptResponse>, ValidationProblem>> Upload(
        IFormFile file,
        AppDbContext db,
        ExtractionQueue queue,
        IOptions<StorageOptions> storage,
        TimeProvider time,
        CancellationToken ct)
    {
        if (file.Length is 0 or > MaxUploadBytes)
            return Invalid("file", $"File must be between 1 byte and {MaxUploadBytes / 1024 / 1024} MB.");

        if (!AllowedFiles.TryGetValue(Path.GetExtension(file.FileName), out var type))
            return Invalid("file", "Only JPG and PNG are supported for now (PDF e-receipts are planned).");
        if (!await LooksLikeImageAsync(file, ct))
            return Invalid("file", "That file isn't a valid JPG or PNG image.");

        var id = Guid.NewGuid();
        var folder = Path.GetFullPath(storage.Value.ReceiptsPath);
        Directory.CreateDirectory(folder);
        var imagePath = Path.Combine(folder, $"{id}{type.Ext}");

        await using (var stream = File.Create(imagePath))
            await file.CopyToAsync(stream, ct);

        var receipt = new ReceiptRecord
        {
            Id = id,
            Status = ReceiptStatus.Queued,
            OriginalFileName = Path.GetFileName(file.FileName),
            ImagePath = imagePath,
            ContentType = type.ContentType,
            UploadedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(ct);

        await queue.EnqueueAsync(id, ct);

        return TypedResults.Accepted($"/api/receipts/{id}", ReceiptResponse.From(receipt));
    }

    private static async Task<Ok<List<ReceiptResponse>>> List(AppDbContext db, CancellationToken ct)
    {
        var receipts = await db.Receipts.OrderByDescending(r => r.UploadedAtUtc).ToListAsync(ct);
        return TypedResults.Ok(receipts.Select(ReceiptResponse.From).ToList());
    }

    private static async Task<Results<Ok<ReceiptResponse>, NotFound>> Get(Guid id, AppDbContext db, CancellationToken ct)
    {
        var receipt = await db.Receipts.FindAsync([id], ct);
        return receipt is null ? TypedResults.NotFound() : TypedResults.Ok(ReceiptResponse.From(receipt));
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound>> GetImage(
        Guid id, AppDbContext db, HttpResponse response, CancellationToken ct)
    {
        var receipt = await db.Receipts.FindAsync([id], ct);
        if (receipt is null || !File.Exists(receipt.ImagePath))
            return TypedResults.NotFound();

        // The image can change (rotation), so browsers must re-check it each time.
        // Last-Modified makes that re-check cheap: an unchanged image comes back as "304 Not Modified".
        response.Headers.CacheControl = "no-cache";
        return TypedResults.PhysicalFile(receipt.ImagePath, receipt.ContentType,
            lastModified: File.GetLastWriteTimeUtc(receipt.ImagePath));
    }

    private static async Task<Results<Ok<ReceiptResponse>, NotFound, Conflict<string>, ValidationProblem>> ReplaceImage(
        Guid id, IFormFile file, AppDbContext db, CancellationToken ct)
    {
        var receipt = await db.Receipts.FindAsync([id], ct);
        if (receipt is null)
            return TypedResults.NotFound();
        if (receipt.Status is ReceiptStatus.Queued or ReceiptStatus.Processing)
            return TypedResults.Conflict("Wait until extraction has finished before changing the image.");

        if (file.Length is 0 or > MaxUploadBytes)
            return Invalid("file", $"File must be between 1 byte and {MaxUploadBytes / 1024 / 1024} MB.");
        if (!AllowedFiles.TryGetValue(Path.GetExtension(file.FileName), out var type))
            return Invalid("file", "Only JPG and PNG are supported.");
        if (!await LooksLikeImageAsync(file, ct))
            return Invalid("file", "That file isn't a valid JPG or PNG image.");

        var folder = Path.GetDirectoryName(receipt.ImagePath)!;

        // Keep the very first version of the photo, in case an edit goes wrong.
        var backup = Path.Combine(folder, $"{id}.original{Path.GetExtension(receipt.ImagePath)}");
        if (!File.Exists(backup) && File.Exists(receipt.ImagePath))
            File.Copy(receipt.ImagePath, backup);

        // Write to a temp file first, then swap it in, so a failed upload never leaves a half-written image.
        var newPath = Path.Combine(folder, $"{id}{type.Ext}");
        var tempPath = newPath + ".tmp";
        await using (var stream = File.Create(tempPath))
            await file.CopyToAsync(stream, ct);
        File.Move(tempPath, newPath, overwrite: true);

        if (!string.Equals(newPath, receipt.ImagePath, StringComparison.OrdinalIgnoreCase) && File.Exists(receipt.ImagePath))
            File.Delete(receipt.ImagePath);

        receipt.ImagePath = newPath;
        receipt.ContentType = type.ContentType;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ReceiptResponse.From(receipt));
    }

    /// <summary>Checks the file's first bytes: JPEG starts with FF D8 FF, PNG with 89 50 4E 47.</summary>
    private static async Task<bool> LooksLikeImageAsync(IFormFile file, CancellationToken ct)
    {
        var header = new byte[4];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (read < 3) return false;
        var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var isPng = read == 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        return isJpeg || isPng;
    }

    private static async Task<Results<Ok<ReceiptResponse>, NotFound, Conflict<string>, ValidationProblem>> Confirm(
        Guid id,
        ReceiptUpdate update,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var receipt = await db.Receipts.FindAsync([id], ct);
        if (receipt is null)
            return TypedResults.NotFound();
        if (receipt.Status is ReceiptStatus.Queued or ReceiptStatus.Processing)
            return TypedResults.Conflict("Extraction is still running for this receipt.");

        // SCOPE.md: date, total and category are required; merchant and VAT are optional.
        var errors = new Dictionary<string, string[]>();
        if (update.Date is null) errors["date"] = ["Date is required."];
        if (update.Total is not > 0) errors["total"] = ["Total is required and must be more than zero."];
        var category = Categories.Normalize(update.Category);
        if (category is null) errors["category"] = [$"Category must be one of: {string.Join(", ", Categories.All)}."];
        if (update.VatExemptSales < 0) errors["vatExemptSales"] = ["VAT-exempt sales can't be negative."];
        if (update.Discount < 0) errors["discount"] = ["Enter the discount as a positive amount."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var fields = new ReceiptFields(
            MerchantRules.Normalize(update.Merchant), update.Date, update.Total, update.VatAmount, update.VatableSales,
            update.VatExemptSales, update.Discount);
        receipt.Corrections = DiffFromModel(receipt, fields, category);
        receipt.ApplyFields(fields, category);

        // Checks still run, but only as warnings: the user has the final say.
        receipt.Flags = ReceiptValidator.Validate(fields, DateOnly.FromDateTime(time.GetLocalNow().DateTime));
        receipt.Status = ReceiptStatus.Confirmed;
        receipt.ConfirmedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ReceiptResponse.From(receipt));
    }

    private static async Task<Results<Ok<BulkConfirmResult>, ValidationProblem>> ConfirmMany(
        BulkConfirmRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var ids = request.Ids?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
            return Invalid("ids", "Pick at least one receipt.");
        if (ids.Count > MaxBulk)
            return Invalid("ids", $"At most {MaxBulk} receipts at a time.");

        var found = await db.Receipts.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var now = time.GetUtcNow().UtcDateTime;

        var confirmed = new List<ReceiptResponse>();
        var skipped = new List<BulkSkipped>();

        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var receipt))
            {
                skipped.Add(new(id, "(unknown)", "Not found."));
                continue;
            }

            var reason = WhyNotBulkConfirmable(receipt);
            if (reason is not null)
            {
                skipped.Add(new(id, receipt.DisplayName, reason));
                continue;
            }

            // Same as confirming in the review screen with nothing changed.
            receipt.Corrections = [];
            receipt.Flags = ReceiptValidator.Validate(receipt.Fields, today); // kept as warnings
            receipt.Status = ReceiptStatus.Confirmed;
            receipt.ConfirmedAtUtc = now;
            confirmed.Add(ReceiptResponse.From(receipt));
        }

        await db.SaveChangesAsync(ct); // all-or-nothing for the ones that passed
        return TypedResults.Ok(new BulkConfirmResult(confirmed, skipped));
    }

    /// <summary>Null if the receipt can be confirmed as-is; otherwise the reason it can't.</summary>
    private static string? WhyNotBulkConfirmable(ReceiptRecord r)
    {
        switch (r.Status)
        {
            case ReceiptStatus.Queued or ReceiptStatus.Processing: return "Still being read.";
            case ReceiptStatus.Failed: return "Extraction failed — open it and retry.";
            case ReceiptStatus.Confirmed: return "Already confirmed.";
        }

        // Same required fields as the review screen (SCOPE.md).
        var missing = new List<string>();
        if (r.Date is null) missing.Add("date");
        if (r.Total is not > 0) missing.Add("total");
        if (Categories.Normalize(r.Category) is null) missing.Add("category");
        return missing.Count > 0 ? $"Missing {string.Join(", ", missing)} — open it to fill in." : null;
    }

    private static async Task<Results<Accepted<ReceiptResponse>, NotFound, Conflict<string>>> Retry(
        Guid id, AppDbContext db, ExtractionQueue queue, CancellationToken ct)
    {
        var receipt = await db.Receipts.FindAsync([id], ct);
        if (receipt is null)
            return TypedResults.NotFound();
        if (receipt.Status is ReceiptStatus.Queued or ReceiptStatus.Processing or ReceiptStatus.Confirmed)
            return TypedResults.Conflict($"Can't retry a receipt that is {receipt.Status}.");

        receipt.Status = ReceiptStatus.Queued;
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(id, ct);

        return TypedResults.Accepted($"/api/receipts/{id}", ReceiptResponse.From(receipt));
    }

    private const string BusyReading = "The model is reading this receipt right now. Try again once it's done.";

    private static async Task<Results<NoContent, NotFound, Conflict<string>>> Delete(
        Guid id, AppDbContext db, ILoggerFactory logs, CancellationToken ct)
    {
        var receipt = await db.Receipts.FindAsync([id], ct);
        if (receipt is null)
            return TypedResults.NotFound();
        // The worker has the image open and is about to save its result; let it finish first.
        // (Queued is fine: the worker skips receipts that no longer exist.)
        if (receipt.Status is ReceiptStatus.Processing)
            return TypedResults.Conflict(BusyReading);

        await RemoveAsync([receipt], db, logs, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<BulkDeleteResult>, ValidationProblem>> DeleteMany(
        BulkDeleteRequest request, AppDbContext db, ILoggerFactory logs, CancellationToken ct)
    {
        var ids = request.Ids?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
            return Invalid("ids", "Pick at least one receipt.");
        if (ids.Count > MaxBulk)
            return Invalid("ids", $"At most {MaxBulk} receipts at a time.");

        var found = await db.Receipts.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var toDelete = new List<ReceiptRecord>();
        var skipped = new List<BulkSkipped>();

        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var receipt))
                skipped.Add(new(id, "(unknown)", "Not found — maybe already deleted."));
            else if (receipt.Status is ReceiptStatus.Processing)
                skipped.Add(new(id, receipt.DisplayName, BusyReading));
            else
                toDelete.Add(receipt);
        }

        await RemoveAsync(toDelete, db, logs, ct);
        return TypedResults.Ok(new BulkDeleteResult(toDelete.Select(r => r.Id).ToList(), skipped));
    }

    /// <summary>
    /// Records first (one transaction), then files: if a file is locked we're left with a stray file,
    /// never with a record whose image is gone.
    /// </summary>
    private static async Task RemoveAsync(
        IReadOnlyCollection<ReceiptRecord> receipts, AppDbContext db, ILoggerFactory logs, CancellationToken ct)
    {
        if (receipts.Count == 0)
            return;

        db.Receipts.RemoveRange(receipts);
        await db.SaveChangesAsync(ct);

        foreach (var receipt in receipts)
        {
            var leftovers = ReceiptFiles.DeleteAll(receipt.ImagePath, receipt.Id);
            if (leftovers.Count > 0)
                logs.CreateLogger(typeof(ReceiptEndpoints)).LogWarning(
                    "Deleted receipt {ReceiptId} but couldn't delete {Files}", receipt.Id, string.Join(", ", leftovers));
        }
    }

    private static async Task<Results<Accepted<ReceiptResponse>, NotFound, Conflict<string>, ValidationProblem>> Reextract(
        Guid id, ReextractRequest request, AppDbContext db, ExtractionQueue queue, CancellationToken ct)
    {
        var receipt = await db.Receipts.FindAsync([id], ct);
        if (receipt is null)
            return TypedResults.NotFound();
        if (receipt.Status is ReceiptStatus.Queued or ReceiptStatus.Processing)
            return TypedResults.Conflict("Extraction is already running for this receipt.");

        var locked = (request.Locked ?? []).Distinct().ToList();
        var update = request.Fields;
        var errors = new Dictionary<string, string[]>();
        if (update is null || locked.Count == 0)
            errors["locked"] = ["Change at least one field to re-extract with."];
        else if (locked.FirstOrDefault(f => !UserHints.IsKnown(f)) is { } unknown)
            errors["locked"] = [$"Unknown field '{unknown}'. Use: {string.Join(", ", UserHints.Fields)}."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        // Same rules as confirming, but only for the fields being locked.
        var category = Categories.Normalize(update!.Category);
        if (locked.Contains(UserHints.Total) && update.Total is not > 0) errors["total"] = ["Total must be more than zero."];
        if (locked.Contains(UserHints.Category) && category is null)
            errors["category"] = [$"Category must be one of: {string.Join(", ", Categories.All)}."];
        if (locked.Contains(UserHints.VatExemptSales) && update.VatExemptSales < 0) errors["vatExemptSales"] = ["VAT-exempt sales can't be negative."];
        if (locked.Contains(UserHints.Discount) && update.Discount < 0) errors["discount"] = ["Enter the discount as a positive amount."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        // Store the user's values for the locked fields now; the worker keeps them and re-reads the rest.
        var user = new ReceiptFields(MerchantRules.Normalize(update.Merchant), update.Date, update.Total,
            update.VatAmount, update.VatableSales, update.VatExemptSales, update.Discount);
        var set = locked.ToHashSet();
        receipt.ApplyFields(UserHints.Keep(receipt.Fields, user, set), set.Contains(UserHints.Category) ? category : receipt.Category);
        receipt.LockedFields = locked;
        receipt.Status = ReceiptStatus.Queued;
        receipt.ConfirmedAtUtc = null; // needs confirming again once the model is done
        receipt.Error = null;
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(id, ct);

        return TypedResults.Accepted($"/api/receipts/{id}", ReceiptResponse.From(receipt));
    }

    /// <summary>What the user changed compared with what the model extracted.</summary>
    private static List<string> DiffFromModel(ReceiptRecord receipt, ReceiptFields confirmed, string? category)
    {
        if (receipt.ModelOutput is null)
            return [];

        ExtractionResult model;
        try { model = OllamaExtractor.ParseModelOutput(receipt.ModelOutput); }
        catch (ExtractionException) { return ["model output was unreadable"]; }

        var changes = new List<string>();
        void Compare(string name, object? before, object? after, Func<object?, string> show)
        {
            if (!Equals(before, after))
                changes.Add($"{name}: {show(before)} → {show(after)}");
        }

        static string Text(object? v) => v?.ToString() ?? "(empty)";
        static string Money(object? v) => v is decimal amount ? Peso.Format(amount) : "(empty)";
        static string Day(object? v) => v is DateOnly d ? d.ToString("yyyy-MM-dd") : "(empty)";

        var extracted = model.Fields;
        Compare("merchant", extracted.Merchant, confirmed.Merchant, Text);
        Compare("date", extracted.Date, confirmed.Date, Day);
        Compare("total", extracted.Total, confirmed.Total, Money);
        Compare("vat_amount", extracted.VatAmount ?? 0m, confirmed.VatAmount ?? 0m, Money);
        Compare("vatable_sales", extracted.VatableSales ?? 0m, confirmed.VatableSales ?? 0m, Money);
        Compare("vat_exempt_sales", extracted.VatExemptSales ?? 0m, confirmed.VatExemptSales ?? 0m, Money);
        Compare("discount", extracted.Discount ?? 0m, confirmed.Discount ?? 0m, Money);
        Compare("category", model.Category, category, Text);
        return changes;
    }

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
