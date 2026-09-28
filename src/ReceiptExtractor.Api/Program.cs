using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Endpoints;
using ReceiptExtractor.Api.Extraction;
using ReceiptExtractor.Api.Summaries;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter())); // "NeedsReview" instead of 3

// Database: a local SQLite file (receipts.db). Created automatically on first run.
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Receipts") ?? "Data Source=receipts.db"));

builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

builder.Services.AddHttpClient<IReceiptExtractor, OllamaExtractor>((sp, http) =>
{
    http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OllamaOptions>>().Value.BaseUrl);
    http.Timeout = TimeSpan.FromMinutes(5); // the first call also loads the model onto the GPU
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ExtractionQueue>();
builder.Services.AddHostedService<ExtractionWorker>();

var app = builder.Build();

// Create the database tables if they don't exist yet.
// (Switch to EF Core migrations once the schema starts changing.)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SchemaPatches.Apply(db); // add columns introduced since the database was created
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi(); // OpenAPI document at /openapi/v1.json

// The built web app (web/dist, copied into wwwroot by publish.cmd). In development the
// Vite dev server serves it instead, so wwwroot is usually empty/missing there.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapReceiptEndpoints();
app.MapSummaryEndpoints();

app.MapFallback("/api/{**path}", () => Results.NotFound()); // unknown API routes: 404, not the web page
app.MapFallbackToFile("index.html");                         // everything else: the React app handles routing

app.Run();

public partial class Program; // lets integration tests start the app
