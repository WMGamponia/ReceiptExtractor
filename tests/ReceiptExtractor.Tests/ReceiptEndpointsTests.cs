using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Endpoints;
using ReceiptExtractor.Api.Extraction;

namespace ReceiptExtractor.Tests;

/// <summary>The receipt endpoints in memory (SQLite in memory, no model): confirming a reviewed receipt.</summary>
public sealed class ReceiptEndpointsTests : IAsyncLifetime
{
    // What the model read from a Texas Roadhouse receipt.
    private const string ModelOutput = """
        {"merchant":"TEXAS ROADHOUSE","date":"2026-08-08","total":6323.43,"vat_amount":361.34,"vatable_sales":3011.16,"vat_exempt_sales":3011.16,"discount":602.23,"service_charge":542.01,"category":"Food"}
        """;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _connection.Open();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        builder.Services.Configure<StorageOptions>(o => o.ReceiptsPath = Path.GetTempPath());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ExtractionQueue>();
        _app = builder.Build();
        _app.MapReceiptEndpoints();
        await _app.StartAsync();
        _http = _app.GetTestClient();

        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _connection.Dispose();
    }

    private Guid AddReceipt(string? modelOutput = ModelOutput)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var receipt = new ReceiptRecord
        {
            Id = Guid.NewGuid(), Status = ReceiptStatus.Extracted, OriginalFileName = "r.jpg", ImagePath = "r.jpg",
            ContentType = "image/jpeg", ModelOutput = modelOutput,
        };
        if (modelOutput is not null)
            receipt.ApplyFields(OllamaExtractor.ParseModelOutput(modelOutput).Fields, "Food");
        db.Receipts.Add(receipt);
        db.SaveChanges();
        return receipt.Id;
    }

    /// <summary>The review form's request: the model's answer, with <paramref name="serviceCharge"/> as given (omitted if "-").</summary>
    private Task<HttpResponseMessage> Confirm(Guid id, object? serviceCharge)
    {
        var body = new Dictionary<string, object?>
        {
            ["merchant"] = "TEXAS ROADHOUSE", ["date"] = "2026-08-08", ["total"] = 6323.43m, ["vatAmount"] = 361.34m,
            ["vatableSales"] = 3011.16m, ["vatExemptSales"] = 3011.16m, ["discount"] = 602.23m, ["category"] = "Food",
        };
        if (serviceCharge is not "-")
            body["serviceCharge"] = serviceCharge;
        return _http.PutAsJsonAsync($"/api/receipts/{id}", body);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Rejects_a_negative_service_charge()
    {
        var response = await Confirm(AddReceipt(), -542.01m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter the service charge as a positive amount.",
            (await Json(response)).GetProperty("errors").GetProperty("serviceCharge")[0].GetString());
    }

    [Fact]
    public async Task Records_a_changed_service_charge_as_a_correction()
    {
        var saved = await Json(await Confirm(AddReceipt(), 500m));

        Assert.Equal(500m, saved.GetProperty("serviceCharge").GetDecimal());
        Assert.Equal(["service_charge: ₱542.01 → ₱500.00"],
            saved.GetProperty("corrections").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task Keeps_the_service_charge_when_an_older_page_leaves_it_out()
    {
        // A review page opened before the update doesn't know the field.
        var saved = await Json(await Confirm(AddReceipt(), "-"));

        Assert.Equal(542.01m, saved.GetProperty("serviceCharge").GetDecimal());
        Assert.Empty(saved.GetProperty("corrections").EnumerateArray());
        Assert.Empty(saved.GetProperty("flags").EnumerateArray());
    }

    [Fact]
    public async Task A_receipt_read_before_the_field_existed_confirms_without_a_correction()
    {
        var older = ModelOutput.Replace(""","service_charge":542.01""", "");
        var saved = await Json(await Confirm(AddReceipt(older), 0m));

        Assert.Empty(saved.GetProperty("corrections").EnumerateArray());
    }
}
