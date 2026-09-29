using Microsoft.Extensions.Options;

namespace ReceiptExtractor.Api.Extraction;

public static class ExtractionServices
{
    /// <summary>The Ollama extractor with the "Ollama" settings, for the app and for the live demo.</summary>
    public static IServiceCollection AddOllamaExtractor(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));
        services.AddHttpClient<IReceiptExtractor, OllamaExtractor>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OllamaOptions>>().Value.BaseUrl);
            http.Timeout = TimeSpan.FromMinutes(5); // the first call also loads the model onto the GPU
        });
        return services;
    }
}
