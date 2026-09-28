using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.Extraction;

namespace ReceiptExtractor.Tests;

/// <summary>The image goes to Ollama as base64, whether it comes from a file (the app) or from memory (the live demo).</summary>
public sealed class OllamaRequestTests
{
    // What Ollama's /api/chat returns: the model's JSON answer as a string inside message.content.
    private const string Answer = """
        {"message":{"content":"{\"merchant\":\"Botika San Roque\",\"date\":\"2026-08-09\",\"total\":100,\"vat_amount\":0,\"vatable_sales\":0,\"vat_exempt_sales\":100,\"discount\":0,\"category\":\"Health\"}"},"done_reason":"stop"}
        """;

    private static readonly byte[] Image = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Answer, Encoding.UTF8, "application/json") };
        }
    }

    private static (OllamaExtractor Extractor, CapturingHandler Handler) Create()
    {
        var options = new OllamaOptions();
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
        return (new OllamaExtractor(http, Options.Create(options)), handler);
    }

    [Fact]
    public async Task Sends_the_image_bytes_and_parses_the_answer()
    {
        var (extractor, handler) = Create();

        var result = await extractor.ExtractAsync(Image, [], CancellationToken.None);

        Assert.Contains(Convert.ToBase64String(Image), handler.Body);
        Assert.Equal(new DateOnly(2026, 8, 9), result.Fields.Date);
        Assert.Equal(100m, result.Fields.Total);
        Assert.Equal("Health", result.Category);
    }

    [Fact]
    public async Task Reading_from_a_file_sends_the_same_bytes()
    {
        var (extractor, handler) = Create();
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, Image);
            await extractor.ExtractAsync(path, [], CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Contains(Convert.ToBase64String(Image), handler.Body);
    }
}
