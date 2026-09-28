using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.Data;
using ReceiptExtractor.Api.Endpoints;
using ReceiptExtractor.Api.Extraction;
using ReceiptExtractor.Api.Hosting;
using ReceiptExtractor.Api.Summaries;

var isDevelopment = string.Equals(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // The packaged exe unpacks appsettings.json and the web app (wwwroot) next to its own code,
    // which isn't the folder you launched it from — so point the content root there.
    ContentRootPath = isDevelopment ? null : AppContext.BaseDirectory,
});

// Packaged exe: an optional settings.json next to ReceiptTracker.exe overrides the built-in settings.
if (!isDevelopment)
    builder.Configuration.AddJsonFile(DesktopMode.SettingsFile(), optional: true, reloadOnChange: false);

// Packaged exe: listen on http://localhost:5080 unless told otherwise (e.g. ReceiptTracker.exe --urls http://0.0.0.0:5080).
// Development keeps the port from launchSettings.json (5036), which the Vite dev server proxies to.
if (!isDevelopment && string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://localhost:5080");

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter())); // "NeedsReview" instead of 3

// Where the database and receipt images live (project folder in dev, "data" next to the exe otherwise).
var dataRoot = DesktopMode.DataRoot(builder.Environment);
Directory.CreateDirectory(dataRoot);

// Database: a local SQLite file (receipts.db). Created automatically on first run.
var connection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("Receipts") ?? "Data Source=receipts.db");
connection.DataSource = DesktopMode.InDataRoot(dataRoot, connection.DataSource);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection.ToString()));

builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.PostConfigure<StorageOptions>(o => o.ReceiptsPath = DesktopMode.InDataRoot(dataRoot, o.ReceiptsPath));

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

if (!app.Environment.IsDevelopment())
    app.Lifetime.ApplicationStarted.Register(() => DesktopMode.OnStarted(app));

app.Run();

public partial class Program; // lets integration tests start the app
