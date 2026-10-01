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

    // Same answer with the token probabilities Ollama adds for logprobs: true (one shaky digit in the total).
    private const string AnswerWithLogprobs = """
        {"message":{"content":"{\"date\":\"2026-08-09\",\"total\":100}"},"done_reason":"stop","logprobs":[
          {"token":"{\"date\":\"","logprob":0},{"token":"2026-08-09","logprob":-0.01},{"token":"\",\"total\":","logprob":0},
          {"token":"1","logprob":-0.5},{"token":"00","logprob":0},{"token":"}","logprob":0}]}
        """;

    private sealed class CapturingHandler(string answer) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    private static (OllamaExtractor Extractor, CapturingHandler Handler) Create(string answer = Answer)
    {
        var options = new OllamaOptions();
        var handler = new CapturingHandler(answer);
        var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
        return (new OllamaExtractor(http, Options.Create(options)), handler);
    }

    [Fact]
    public async Task Asks_for_token_probabilities_and_scores_them()
    {
        var (extractor, handler) = Create(AnswerWithLogprobs);

        var result = await extractor.ExtractAsync(Image, [], CancellationToken.None);

        Assert.Contains("\"logprobs\":true", handler.Body);
        Assert.Equal(0.607, result.Confidence!.Overall);
        Assert.Equal(0.99, result.Confidence.Fields["date"]);
    }

    [Fact]
    public async Task An_answer_without_probabilities_has_no_score()
    {
        var (extractor, _) = Create();

        var result = await extractor.ExtractAsync(Image, [], CancellationToken.None);

        Assert.Null(result.Confidence!.Overall);
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
    public async Task Sends_a_sideways_phone_photo_upright()
    {
        var (extractor, handler) = Create();
        var sideways = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sideways-exif6.jpg"));

        await extractor.ExtractAsync(sideways, [], CancellationToken.None);

        Assert.DoesNotContain(Convert.ToBase64String(sideways), handler.Body);
        Assert.Contains(Convert.ToBase64String(PhotoOrientation.Upright(sideways)), handler.Body);
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
