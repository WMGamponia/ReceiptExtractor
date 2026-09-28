using System.Net;
using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.Extraction;

namespace ReceiptExtractor.Tests;

/// <summary>
/// The two failures a new install hits first (e.g. `docker compose up` without the AI profile):
/// Ollama isn't running, or the model isn't downloaded. The receipt's error should say how to fix it.
/// </summary>
public sealed class OllamaErrorTests : IDisposable
{
    private readonly string _image = Path.GetTempFileName();

    public void Dispose() => File.Delete(_image);

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond());
    }

    private Task<ExtractionException> Extract(Func<HttpResponseMessage> respond)
    {
        var options = new OllamaOptions { BaseUrl = "http://ollama:11434", Model = "qwen3-vl:8b-instruct" };
        var http = new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri(options.BaseUrl) };
        var extractor = new OllamaExtractor(http, Options.Create(options));
        return Assert.ThrowsAsync<ExtractionException>(() => extractor.ExtractAsync(_image, [], CancellationToken.None));
    }

    [Fact]
    public async Task Model_not_downloaded_says_how_to_download_it()
    {
        // What Ollama answers on /api/chat for a model that hasn't been pulled.
        var ex = await Extract(() => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":"model \"qwen3-vl:8b-instruct\" not found, try pulling it first"}"""),
        });

        Assert.Contains("qwen3-vl:8b-instruct isn't downloaded", ex.Message);
        Assert.Contains("pull-model", ex.Message);
        Assert.Contains("Try again", ex.Message);
    }

    [Fact]
    public async Task Ollama_not_running_says_where_it_looked()
    {
        var ex = await Extract(() => throw new HttpRequestException("No such host is known. (ollama:11434)"));

        Assert.Contains("Can't reach Ollama at http://ollama:11434", ex.Message);
        Assert.Contains("Try again", ex.Message);
    }

    [Fact]
    public async Task Other_Ollama_errors_keep_the_details()
    {
        var ex = await Extract(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{"error":"out of memory"}"""),
        });

        Assert.Equal("""Ollama returned 500: {"error":"out of memory"}""", ex.Message);
    }
}
