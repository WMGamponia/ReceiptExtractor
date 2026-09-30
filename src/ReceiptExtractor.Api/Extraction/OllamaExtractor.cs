using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.Validation;

namespace ReceiptExtractor.Api.Extraction;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3-vl:8b-instruct";
    public string KeepAlive { get; set; } = "10m";   // keep the model loaded between receipts
    public int ContextLength { get; set; } = 8192;   // the image alone can take ~4000 tokens
}

public sealed record ExtractionResult(ReceiptFields Fields, string? Category, string ModelOutput);

public interface IReceiptExtractor
{
    /// <param name="image">The photo (JPEG or PNG) as bytes; the live demo reads it from memory, never from a file.</param>
    /// <param name="hints">Values the user checked; the model is told they're correct (see <see cref="UserHints"/>).</param>
    Task<ExtractionResult> ExtractAsync(byte[] image, IReadOnlyList<FieldHint> hints, CancellationToken ct);

    /// <summary>Reads the image file, then as above.</summary>
    Task<ExtractionResult> ExtractAsync(string imagePath, IReadOnlyList<FieldHint> hints, CancellationToken ct);
}

public sealed class ExtractionException(string message) : Exception(message);

/// <summary>
/// Sends a receipt image to a local vision model via Ollama's /api/chat and parses the JSON it returns.
/// Same model, prompt and schema as eval/try_vision.py (the approach that won the bake-off).
/// </summary>
public sealed class OllamaExtractor(HttpClient http, IOptions<OllamaOptions> options) : IReceiptExtractor
{
    // Keep in sync with PROMPT in eval/try_vision.py (plus the category line).
    public const string Prompt = """
        You are reading a receipt from the Philippines.
        Copy amounts exactly as printed, including centavos (e.g. 1124.46)
        Extract these fields:
        - merchant: the store or company name as printed at the top. For online
          orders, the marketplace (e.g. Lazada, Shopee), not the seller's shop name
        - date: the transaction date, converted to YYYY-MM-DD
        - total: the final amount paid (TOTAL / AMOUNT DUE) after discounts, not cash tendered or change
        - vat_amount: the "VAT Amount" / "12% VAT" line; 0 if the receipt has none.
          Not the "Less 12% VAT" / "Less VAT" line of a senior citizen / PWD computation
          (that is VAT removed from exempt items, not VAT charged)
        - vatable_sales: the "VATable Sales" line; 0 if the receipt has none
        - vat_exempt_sales: the "VAT-Exempt Sales" line; 0 if the receipt has none
        - discount: all discounts on the receipt added up (senior citizen, PWD, promo, etc.),
          as a positive number; 0 if there are none. "Less VAT" lines are not discounts
        - category: the best fit of:
          Food (restaurants, fast food, cafes, carinderia),
          Groceries (supermarkets, sari-sari stores, public markets),
          Transport (Grab, taxis, buses, LRT/MRT, tolls, parking),
          Fuel (gas stations such as Petron, Shell, Caltex, Seaoil),
          Health (pharmacies such as Mercury Drug, Watsons, clinics, labs),
          Utilities (electricity, water, internet, mobile load, e.g. Meralco, Maynilad, PLDT, Globe),
          Shopping (Lazada, Shopee, department stores, gadgets, clothes, hardware),
          Other
        Some receipts print the VAT breakdown as a tax table (Tax Code / RATE / Amount / TAX amt)
        instead of labeled lines: the V (12%) row's Amount is vatable_sales and its TAX is vat_amount,
        and the E (0%) row's Amount is vat_exempt_sales.
        Amounts are in PHP. Numbers only, no commas or peso signs.
        Use null if a field is unreadable.
        """;

    // JSON schema Ollama uses to force the shape of the answer ("structured outputs").
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "merchant":      { "anyOf": [ { "type": "string" }, { "type": "null" } ] },
            "date":          { "anyOf": [ { "type": "string" }, { "type": "null" } ] },
            "total":         { "anyOf": [ { "type": "number" }, { "type": "null" } ] },
            "vat_amount":    { "anyOf": [ { "type": "number" }, { "type": "null" } ] },
            "vatable_sales": { "anyOf": [ { "type": "number" }, { "type": "null" } ] },
            "vat_exempt_sales": { "anyOf": [ { "type": "number" }, { "type": "null" } ] },
            "discount":      { "anyOf": [ { "type": "number" }, { "type": "null" } ] },
            "category":      { "type": "string",
                               "enum": [ "Food", "Groceries", "Transport", "Fuel", "Health", "Utilities", "Shopping", "Other" ] }
          },
          "required": [ "merchant", "date", "total", "vat_amount", "vatable_sales", "vat_exempt_sales", "discount", "category" ]
        }
        """).RootElement.Clone();

    private static readonly JsonSerializerOptions ModelJson = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<ExtractionResult> ExtractAsync(string imagePath, IReadOnlyList<FieldHint> hints, CancellationToken ct) =>
        await ExtractAsync(await File.ReadAllBytesAsync(imagePath, ct), hints, ct);

    public async Task<ExtractionResult> ExtractAsync(byte[] image, IReadOnlyList<FieldHint> hints, CancellationToken ct)
    {
        var opts = options.Value;

        var request = new
        {
            model = opts.Model,
            stream = false,
            think = false,                 // answer directly, no "thinking" output
            keep_alive = opts.KeepAlive,
            format = Schema,
            options = new { temperature = 0, num_ctx = opts.ContextLength }, // same input -> same output
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = Prompt + UserHints.Prompt(hints),
                    images = new[] { Convert.ToBase64String(PhotoOrientation.Upright(image)) },
                },
            },
        };

        HttpResponseMessage sent;
        try
        {
            sent = await http.PostAsJsonAsync("/api/chat", request, ct);
        }
        catch (HttpRequestException)
        {
            // First thing a new install hits (Ollama not started; Docker without the "ai" profile), so say how to fix it.
            throw new ExtractionException(
                $"Can't reach Ollama at {opts.BaseUrl}. Start Ollama (with Docker: docker compose --profile ai up), then use Try again.");
        }

        using var response = sent;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.NotFound && body.Contains("not found", StringComparison.OrdinalIgnoreCase))
                throw new ExtractionException(
                    $"The model {opts.Model} isn't downloaded yet. Run scripts/pull-model (with Docker, wait for the " +
                    "ollama-pull container to finish), then use Try again.");
            throw new ExtractionException($"Ollama returned {(int)response.StatusCode}: {body}");
        }

        var chat = await response.Content.ReadFromJsonAsync<ChatResponse>(ct)
                   ?? throw new ExtractionException("Ollama returned an empty response.");

        var content = chat.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new ExtractionException($"Model returned no answer (done_reason={chat.DoneReason}).");

        return ParseModelOutput(content);
    }

    /// <summary>Turns the model's JSON into clean fields. Public so it can be unit-tested without Ollama.</summary>
    public static ExtractionResult ParseModelOutput(string json)
    {
        ModelReceipt model;
        try
        {
            model = JsonSerializer.Deserialize<ModelReceipt>(json, ModelJson)
                    ?? throw new ExtractionException("Model returned 'null'.");
        }
        catch (JsonException ex)
        {
            throw new ExtractionException($"Model returned invalid JSON: {ex.Message}");
        }

        DateOnly? date = DateOnly.TryParseExact(model.Date?.Trim(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        var fields = new ReceiptFields(
            MerchantRules.Normalize(model.Merchant),
            date,
            Money(model.Total),
            Money(model.VatAmount),
            Money(model.VatableSales),
            Money(model.VatExemptSales),
            PositiveMoney(model.Discount));

        return new ExtractionResult(fields, Categories.Normalize(model.Category), json);
    }

    private static decimal? Money(decimal? value) =>
        value is { } v ? Math.Round(v, 2, MidpointRounding.AwayFromZero) : null;

    /// <summary>Discounts are often printed as "-20.00"; we store them as positive amounts.</summary>
    private static decimal? PositiveMoney(decimal? value) => Money(value) is { } v ? Math.Abs(v) : null;

    private sealed class ModelReceipt
    {
        [JsonPropertyName("merchant")] public string? Merchant { get; set; }
        [JsonPropertyName("date")] public string? Date { get; set; }
        [JsonPropertyName("total")] public decimal? Total { get; set; }
        [JsonPropertyName("vat_amount")] public decimal? VatAmount { get; set; }
        [JsonPropertyName("vatable_sales")] public decimal? VatableSales { get; set; }
        [JsonPropertyName("vat_exempt_sales")] public decimal? VatExemptSales { get; set; }
        [JsonPropertyName("discount")] public decimal? Discount { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
    }

    private sealed record ChatResponse(
        [property: JsonPropertyName("message")] ChatMessage? Message,
        [property: JsonPropertyName("done_reason")] string? DoneReason);

    private sealed record ChatMessage([property: JsonPropertyName("content")] string? Content);
}
