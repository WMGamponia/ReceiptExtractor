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
    double? ExtractionSeconds,
    string? Error,
    string OriginalFileName,
    DateTime UploadedAtUtc,
    DateTime? ConfirmedAtUtc,
    string ImageUrl)
{
    public static ReceiptResponse From(ReceiptRecord r) => new(
        r.Id, r.DisplayName, r.Status, r.Merchant, r.Date, r.Total, r.VatAmount, r.VatableSales,
        r.VatExemptSales, r.Discount, r.Category, r.Flags, r.ExtractionSeconds, r.Error, r.OriginalFileName, r.UploadedAtUtc,
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

public static class ReceiptEndpoints
{
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
        receipts.MapPost("/{id:guid}/retry", Retry).WithSummary("Run extraction again (e.g. after it failed).");

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
        receipt.ApplyFields(fields, category);

        // Checks still run, but only as warnings: the user has the final say.
        receipt.Flags = ReceiptValidator.Validate(fields, DateOnly.FromDateTime(time.GetLocalNow().DateTime));
        receipt.Status = ReceiptStatus.Confirmed;
        receipt.ConfirmedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ReceiptResponse.From(receipt));
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

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
