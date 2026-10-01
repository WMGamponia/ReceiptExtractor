using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Extraction;

namespace ReceiptExtractor.Tests;

/// <summary>The worker with a fake model: the confidence gate on the saved receipt (spec: "The gate").</summary>
public sealed class ExtractionWorkerTests : IAsyncLifetime
{
    // A Texas Roadhouse read that passes every VAT/total check, so the only possible flag is the confidence one.
    private const string ModelOutput = """
        {"merchant":"TEXAS ROADHOUSE","date":"2026-08-08","total":6323.43,"vat_amount":361.34,"vatable_sales":3011.16,"vat_exempt_sales":3011.16,"discount":602.23,"service_charge":542.01,"category":"Food"}
        """;

    private sealed class FakeExtractor : IReceiptExtractor
    {
        public Confidence? Confidence { get; set; }

        public Task<ExtractionResult> ExtractAsync(byte[] image, IReadOnlyList<FieldHint> hints, CancellationToken ct) =>
            Task.FromResult(OllamaExtractor.ParseModelOutput(ModelOutput) with { Confidence = Confidence });

        public Task<ExtractionResult> ExtractAsync(string imagePath, IReadOnlyList<FieldHint> hints, CancellationToken ct) =>
            ExtractAsync([], hints, ct);
    }

    // One in-memory database, but a connection per DbContext: the worker runs on another thread, and a SQLite
    // connection can't be shared between threads. This connection only keeps the database alive.
    private readonly string _database = $"Data Source=worker-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly FakeExtractor _extractor = new();
    private ServiceProvider _services = null!;
    private ExtractionWorker? _worker;

    public ExtractionWorkerTests() => _keepAlive = new SqliteConnection(_database);

    public Task InitializeAsync()
    {
        _keepAlive.Open();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_database));
        services.AddSingleton<IReceiptExtractor>(_extractor);
        services.Configure<OllamaOptions>(o => o.MinConfidence = 0.85);
        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
        }
        await _services.DisposeAsync();
        _keepAlive.Dispose();
    }

    private static Confidence Scored(double date, double total) =>
        new(Math.Min(date, total), new Dictionary<string, double> { ["date"] = date, ["total"] = total });

    /// <summary>Queues a receipt, waits for the worker to finish it, returns what was saved.</summary>
    private async Task<ReceiptRecord> Extract(Confidence? confidence, params string[] locked)
    {
        _extractor.Confidence = confidence;
        var id = Guid.NewGuid();
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var receipt = new ReceiptRecord
            {
                Id = id, Status = ReceiptStatus.Queued, OriginalFileName = "r.jpg", ImagePath = "r.jpg", ContentType = "image/jpeg",
                LockedFields = [.. locked],
            };
            receipt.ApplyFields(OllamaExtractor.ParseModelOutput(ModelOutput).Fields, "Food");
            db.Receipts.Add(receipt);
            await db.SaveChangesAsync();
        }

        // Started after the receipt is saved: the worker picks up Queued receipts when it starts, exactly once.
        _worker = new ExtractionWorker(new ExtractionQueue(), _services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, NullLogger<ExtractionWorker>.Instance);
        await _worker.StartAsync(CancellationToken.None);

        for (var waited = 0; waited < 100; waited++)
        {
            using var scope = _services.CreateScope();
            var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Receipts.AsNoTracking().SingleAsync(r => r.Id == id);
            if (saved.Status is not (ReceiptStatus.Queued or ReceiptStatus.Processing))
                return saved;
            await Task.Delay(50);
        }
        throw new TimeoutException("The worker didn't finish the receipt.");
    }

    [Fact]
    public async Task A_shaky_read_needs_review_and_keeps_its_score()
    {
        var saved = await Extract(Scored(date: 0.99, total: 0.62));

        Assert.Equal(ReceiptStatus.NeedsReview, saved.Status);
        Assert.Equal(["The model was unsure about Total (62%). Check it against the photo."], saved.Flags);
        Assert.Equal(0.62, saved.Confidence);
        Assert.Equal(0.62, saved.FieldConfidence["total"]);
    }

    [Fact]
    public async Task A_sure_read_is_ready_to_confirm()
    {
        var saved = await Extract(Scored(date: 0.99, total: 0.9));

        Assert.Equal(ReceiptStatus.Extracted, saved.Status);
        Assert.Empty(saved.Flags);
        Assert.Equal(0.9, saved.Confidence);
    }

    [Fact]
    public async Task A_field_the_user_locked_counts_as_sure()
    {
        var saved = await Extract(Scored(date: 0.99, total: 0.62), "total");

        Assert.Equal(ReceiptStatus.Extracted, saved.Status);
        Assert.Equal(0.99, saved.Confidence);
        Assert.Equal(1.0, saved.FieldConfidence["total"]);
    }

    [Fact]
    public async Task No_probabilities_means_no_score_and_no_flag()
    {
        var saved = await Extract(Confidence.None);

        Assert.Equal(ReceiptStatus.Extracted, saved.Status);
        Assert.Null(saved.Confidence);
        Assert.Empty(saved.FieldConfidence);
    }
}
