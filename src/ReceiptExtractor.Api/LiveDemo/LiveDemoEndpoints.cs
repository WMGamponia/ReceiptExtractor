using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.Endpoints;
using ReceiptExtractor.Api.Extraction;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.LiveDemo;

/// <summary>GET /api/live: the demo site's "is the author's PC on?" check.</summary>
public sealed record LiveStatus(string Model, int? PerVisitorPerHour);

/// <summary>POST /api/extract: what the model read. Checks and flags are worked out in the browser (demoApi).</summary>
public sealed record LiveReading(ReceiptFields Fields, string? Category, double ExtractionSeconds);

public static class LiveDemoEndpoints
{
    public const string VisitorPolicy = "visitor";

    public const string Busy = "The author's PC is busy; try again in a minute.";
    public const string BudgetUsed = "The live demo has read all it will for now; try the sample receipts.";
    public const string TooSlow = "Reading took too long; try again.";
    public const string CouldntRead = "The model couldn't read this photo; try another.";

    public static string OverLimit(int perHour) =>
        $"You've reached the demo's limit of {perHour} receipts an hour; try again later.";

    // Visitors show up in the log as a short code, not their address. The salt is new every run.
    private static readonly byte[] Salt = RandomNumberGenerator.GetBytes(16);

    public static IEndpointRouteBuilder MapLiveDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/live", (IOptions<OllamaOptions> ollama, IOptions<LiveDemoOptions> live) =>
            new LiveStatus(ollama.Value.Model, live.Value.PerVisitorPerHour > 0 ? live.Value.PerVisitorPerHour : null));

        app.MapPost("/api/extract", Extract)
            .DisableAntiforgery() // no cookies to protect; CORS decides which site may read the answer
            .RequireRateLimiting(VisitorPolicy);

        return app;
    }

    private static async Task<IResult> Extract(
        IFormFile file,
        IReceiptExtractor extractor,
        ReadGate gate,
        IOptions<LiveDemoOptions> options,
        ILoggerFactory logs,
        HttpContext http,
        CancellationToken ct)
    {
        if (file.Length is 0 or > UploadRules.MaxBytes)
            return Invalid($"File must be between 1 byte and {UploadRules.MaxBytes / 1024 / 1024} MB.");
        if (!UploadRules.AllowedFiles.ContainsKey(Path.GetExtension(file.FileName)))
            return Invalid("Only JPG and PNG photos can be read.");
        if (!await UploadRules.LooksLikeImageAsync(file, ct))
            return Invalid("That file isn't a valid JPG or PNG image.");

        // In memory only: the photo is never written anywhere, and it's gone when this request ends.
        var image = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
            await stream.ReadExactlyAsync(image, ct);

        var (turn, refused) = await gate.WaitTurnAsync(ct);
        if (refused is ReadGate.Refusal.Busy)
            return Results.Problem(Busy, statusCode: StatusCodes.Status503ServiceUnavailable);
        if (refused is ReadGate.Refusal.BudgetUsed)
            return Results.Problem(BudgetUsed, statusCode: StatusCodes.Status429TooManyRequests);

        using var _ = turn;
        var log = logs.CreateLogger("LiveDemo");
        var visitor = Visitor(http);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Value.ReadTimeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await extractor.ExtractAsync(image, [], timeout.Token);
            var seconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2);
            log.LogInformation("Read a {Kb} KB photo for visitor {Visitor} in {Seconds}s; {Left} of {Budget} reads left",
                image.Length / 1024, visitor, seconds, gate.Left, options.Value.Budget);
            return Results.Ok(new LiveReading(result.Fields, result.Category, seconds));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Gave up on a photo for visitor {Visitor} after {Seconds}s", visitor, Math.Round(stopwatch.Elapsed.TotalSeconds));
            return Results.Problem(TooSlow, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (ExtractionException ex)
        {
            // The real reason names local addresses and setup steps: it's for my log, not for visitors.
            log.LogWarning("Couldn't read a photo for visitor {Visitor}: {Error}", visitor, ex.Message);
            return Results.Problem(CouldntRead, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult Invalid(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });

    private static string Visitor(HttpContext http) =>
        http.Connection.RemoteIpAddress is { } ip
            ? Convert.ToHexString(HMACSHA256.HashData(Salt, ip.GetAddressBytes()))[..6]
            : "unknown";
}
