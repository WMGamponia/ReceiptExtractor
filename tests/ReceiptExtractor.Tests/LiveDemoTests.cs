using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Extraction;
using ReceiptExtractor.Api.LiveDemo;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Tests;

/// <summary>The live demo's service, started in memory with a fake model. See docs/superpowers/specs/2026-09-28-live-demo-design.md.</summary>
public sealed class LiveDemoTests : IAsyncLifetime
{
    private const string DemoSite = "https://receipt-tracker-demo.pages.dev";
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private readonly string _root = Directory.CreateTempSubdirectory("live-demo-").FullName;
    private readonly FakeExtractor _extractor = new();
    private WebApplication? _app;
    private HttpClient _http = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _extractor.Release();
        if (_app is not null) await _app.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private async Task Start(params string[] settings)
    {
        _app = LiveDemoApp.Build(["--LiveDemo:Enabled=true", .. settings], _root, b =>
        {
            b.WebHost.UseTestServer();
            b.Services.AddSingleton<IReceiptExtractor>(_extractor);
        });
        await _app.StartAsync();

        // Requests arrive from Tailscale on this PC (loopback), with the visitor's address in X-Forwarded-For.
        _http = new HttpClient(_app.GetTestServer().CreateHandler(c => c.Connection.RemoteIpAddress = IPAddress.Loopback))
        {
            BaseAddress = new Uri("http://localhost"),
        };
    }

    private Task<HttpResponseMessage> Send(string fileName = "receipt.jpg", byte[]? bytes = null, string visitor = "203.0.113.7")
    {
        var photo = new ByteArrayContent(bytes ?? Jpeg);
        photo.Headers.ContentType = new("image/jpeg");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/extract")
        {
            Content = new MultipartFormDataContent { { photo, "file", fileName } },
        };
        request.Headers.Add("X-Forwarded-For", visitor);
        request.Headers.Add("Origin", DemoSite);
        return _http.SendAsync(request);
    }

    private static async Task<string?> Detail(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(50);
        Assert.True(condition());
    }

    [Fact]
    public void Starts_only_when_asked()
    {
        Assert.True(LiveDemoApp.IsRequested(["--LiveDemo:Enabled=true"]));
        Assert.False(LiveDemoApp.IsRequested([]));
    }

    [Fact]
    public async Task Says_which_model_is_reading()
    {
        await Start();

        var status = await _http.GetFromJsonAsync<JsonElement>("/api/live");

        Assert.Equal("qwen3-vl:8b-instruct", status.GetProperty("model").GetString());
        Assert.Equal(20, status.GetProperty("perVisitorPerHour").GetInt32());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/receipts")]
    [InlineData("/api/receipts/export.csv")]
    [InlineData("/api/summary")]
    public async Task Nothing_else_is_there(string path)
    {
        await Start();

        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("receipt.jpg")]
    [InlineData("IMG_0001.JPG")]
    [InlineData("scan.jpeg")]
    public async Task Reads_a_photo_and_returns_the_fields(string fileName)
    {
        await Start();

        var response = await Send(fileName);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reading = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = reading.GetProperty("fields");
        Assert.Equal("Bayanihan Supermarket", fields.GetProperty("merchant").GetString());
        Assert.Equal("2026-09-01", fields.GetProperty("date").GetString());
        Assert.Equal(1124.46m, fields.GetProperty("total").GetDecimal());
        Assert.Equal(120.48m, fields.GetProperty("vatAmount").GetDecimal());
        Assert.Equal("Groceries", reading.GetProperty("category").GetString());
        Assert.True(reading.GetProperty("extractionSeconds").GetDouble() >= 0);
        Assert.Equal(Jpeg, Assert.Single(_extractor.Images));
    }

    [Theory]
    [InlineData("receipt.jpg", new byte[] { 0x68, 0x69 }, "That file isn't a valid JPG or PNG image.")]
    [InlineData("receipt.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }, "Only JPG and PNG photos can be read.")]
    public async Task Rejects_what_isnt_a_photo(string fileName, byte[] bytes, string message)
    {
        await Start();

        var response = await Send(fileName, bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(message, problem.GetProperty("errors").GetProperty("file")[0].GetString());
        Assert.Empty(_extractor.Images);
    }

    [Fact]
    public async Task Rejects_a_photo_over_15_MB()
    {
        await Start();
        var bytes = new byte[15 * 1024 * 1024 + 1];
        Jpeg.CopyTo(bytes, 0);

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(bytes: bytes)).StatusCode);
        Assert.Empty(_extractor.Images);
    }

    [Fact]
    public async Task Tells_visitors_a_plain_reason_when_the_model_fails()
    {
        await Start();
        _extractor.Throw = new ExtractionException(
            "Can't reach Ollama at http://localhost:11434. Start Ollama (with Docker: docker compose --profile ai up), then use Try again.");

        var response = await Send();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(LiveDemoEndpoints.CouldntRead, body);
        Assert.DoesNotContain("11434", body);
    }

    [Fact]
    public async Task Gives_up_on_a_slow_read()
    {
        await Start("--LiveDemo:ReadTimeout=00:00:00.2");
        _extractor.Hold();

        var response = await Send();

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(LiveDemoEndpoints.TooSlow, await Detail(response));
    }

    [Fact]
    public async Task Only_the_demo_site_may_read_the_answers()
    {
        await Start();

        var fromDemo = await Send();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live");
        request.Headers.Add("Origin", "https://someone-else.example");
        var fromElsewhere = await _http.SendAsync(request);

        Assert.Equal(DemoSite, fromDemo.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(fromElsewhere.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Each_visitor_gets_their_own_hourly_limit()
    {
        await Start("--LiveDemo:PerVisitorPerHour=2");

        Assert.Equal(HttpStatusCode.OK, (await Send(visitor: "203.0.113.7")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor: "203.0.113.7")).StatusCode);
        var third = await Send(visitor: "203.0.113.7");
        var someoneElse = await Send(visitor: "198.51.100.9");

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(LiveDemoEndpoints.OverLimit(2), await Detail(third));
        Assert.Equal(DemoSite, third.Headers.GetValues("Access-Control-Allow-Origin").Single()); // the site can show why
        Assert.Equal(HttpStatusCode.OK, someoneElse.StatusCode);
    }

    [Fact]
    public async Task Stops_reading_when_the_budget_is_used()
    {
        await Start("--LiveDemo:Budget=1");

        Assert.Equal(HttpStatusCode.OK, (await Send()).StatusCode);
        var second = await Send(visitor: "198.51.100.9");

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(LiveDemoEndpoints.BudgetUsed, await Detail(second));
    }

    [Fact]
    public async Task Says_busy_when_too_many_are_waiting()
    {
        await Start("--LiveDemo:MaxWaiting=1");
        _extractor.Hold();
        var gate = _app!.Services.GetRequiredService<ReadGate>();

        var reading = Send(visitor: "203.0.113.1");
        var waiting = Send(visitor: "203.0.113.2");
        await WaitFor(() => gate.InLine == 2);
        var third = await Send(visitor: "203.0.113.3");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, third.StatusCode);
        Assert.Equal(LiveDemoEndpoints.Busy, await Detail(third));
        _extractor.Release();
        Assert.Equal(HttpStatusCode.OK, (await reading).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await waiting).StatusCode);
    }

    [Fact]
    public async Task Keeps_nothing()
    {
        await Start();

        (await Send()).EnsureSuccessStatusCode();
        var status = await _http.GetAsync("/api/live");

        Assert.Null(_app!.Services.GetService<AppDbContext>());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories));
        Assert.True(status.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Stops_itself_after_StopAfter()
    {
        await Start("--LiveDemo:StopAfter=00:00:00.2");

        await WaitFor(() => _app!.Lifetime.ApplicationStopping.IsCancellationRequested);
    }

    /// <summary>Answers like the model would, optionally holding reads until <see cref="Release"/>.</summary>
    private sealed class FakeExtractor : IReceiptExtractor
    {
        private TaskCompletionSource _held = Open();

        public List<byte[]> Images { get; } = [];
        public Exception? Throw { get; set; }

        public ExtractionResult Result { get; } = new(
            new ReceiptFields("Bayanihan Supermarket", new DateOnly(2026, 9, 1), 1124.46m, 120.48m, 1003.98m),
            "Groceries",
            "{}");

        public void Hold() => _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _held.TrySetResult();

        public async Task<ExtractionResult> ExtractAsync(byte[] image, IReadOnlyList<FieldHint> hints, CancellationToken ct)
        {
            lock (Images) Images.Add(image);
            await _held.Task.WaitAsync(ct);
            return Throw is null ? Result : throw Throw;
        }

        public Task<ExtractionResult> ExtractAsync(string imagePath, IReadOnlyList<FieldHint> hints, CancellationToken ct) =>
            throw new NotSupportedException("The live demo never reads from a file.");

        private static TaskCompletionSource Open()
        {
            var open = new TaskCompletionSource();
            open.SetResult();
            return open;
        }
    }
}
