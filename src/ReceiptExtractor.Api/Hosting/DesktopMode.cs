using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.Extraction;

namespace ReceiptExtractor.Api.Hosting;

/// <summary>
/// Behaviour for the packaged ReceiptTracker.exe (anything that isn't Development):
/// where data lives, opening the browser, and checking that Ollama is ready.
/// </summary>
public static class DesktopMode
{
    /// <summary>
    /// Folder for the database and receipt images.
    /// Development: the project folder (as before). Packaged exe: a "data" folder next to ReceiptTracker.exe,
    /// so the whole app is portable — copy the folder, and your receipts come with it.
    /// </summary>
    public static string DataRoot(IWebHostEnvironment env)
    {
        return env.IsDevelopment() ? env.ContentRootPath : Path.Combine(ExeFolder(), "data");
    }

    /// <summary>
    /// The folder ReceiptTracker.exe is in. (Environment.ProcessPath is the real .exe even though
    /// single-file content is unpacked to a temp folder; under `dotnet app.dll` fall back to the app folder.)
    /// </summary>
    public static string ExeFolder()
    {
        var exe = Environment.ProcessPath;
        var isDotnetHost = exe is not null &&
                           Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        return exe is null || isDotnetHost ? AppContext.BaseDirectory : Path.GetDirectoryName(exe)!;
    }

    /// <summary>Optional settings.json next to the exe overrides the built-in appsettings.json (e.g. a different model).</summary>
    public static string SettingsFile() => Path.Combine(ExeFolder(), "settings.json");

    /// <summary>Makes a relative path (from appsettings.json) relative to the data folder.</summary>
    public static string InDataRoot(string dataRoot, string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(dataRoot, path));

    /// <summary>Once the server is listening: print where it is, open the browser, check Ollama.</summary>
    public static void OnStarted(WebApplication app)
    {
        var address = app.Urls.FirstOrDefault() ?? "http://localhost:5080";
        var url = address.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost").Replace("+", "localhost");

        Console.WriteLine();
        Console.WriteLine("  Receipt Tracker is running at " + url);
        Console.WriteLine("  Your data is in " + DataRoot(app.Environment));
        Console.WriteLine("  Close this window to stop it.");
        Console.WriteLine();

        if (app.Configuration.GetValue("OpenBrowser", true))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { app.Logger.LogInformation("Couldn't open a browser ({Message}). Go to {Url}", ex.Message, url); }
        }

        _ = CheckOllamaAsync(app);
    }

    private static async Task CheckOllamaAsync(WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<OllamaOptions>>().Value;
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl), Timeout = TimeSpan.FromSeconds(3) };
            var tags = await http.GetFromJsonAsync<TagsResponse>("/api/tags");
            var installed = tags?.Models?.Select(m => m.Name).ToList() ?? [];

            if (!installed.Any(name => name.Equals(options.Model, StringComparison.OrdinalIgnoreCase)))
                app.Logger.LogWarning(
                    "Ollama is running but the model {Model} isn't downloaded. Run:  ollama pull {Model}",
                    options.Model, options.Model);
        }
        catch (Exception)
        {
            app.Logger.LogWarning(
                "Can't reach Ollama at {Url}. Start the Ollama app — until then, uploads will fail " +
                "(use 'Try again' on the receipt once Ollama is running).",
                options.BaseUrl);
        }
    }

    private sealed record TagsResponse([property: JsonPropertyName("models")] List<TagModel>? Models);

    private sealed record TagModel([property: JsonPropertyName("name")] string Name);
}
