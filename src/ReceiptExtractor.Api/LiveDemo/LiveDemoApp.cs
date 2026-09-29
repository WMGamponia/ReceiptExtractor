using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.Extraction;

namespace ReceiptExtractor.Api.LiveDemo;

/// <summary>
/// The live demo (scripts/live-demo.ps1): a separate, minimal app that only reads photos for the demo site, through
/// Tailscale Funnel. It registers no database and no storage, so nothing a visitor sends can be kept.
/// See docs/superpowers/specs/2026-09-28-live-demo-design.md.
/// </summary>
public static class LiveDemoApp
{
    /// <summary>True when started with --LiveDemo:Enabled=true (or the LiveDemo__Enabled environment variable).</summary>
    public static bool IsRequested(string[] args) =>
        new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build().GetValue<bool>("LiveDemo:Enabled");

    /// <param name="contentRoot">Where appsettings.json is (default: next to the app's code). Tests pass an empty folder.</param>
    /// <param name="configure">Tests: swap services (e.g. a fake extractor) and use a test server.</param>
    public static WebApplication Build(string[] args, string? contentRoot = null, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRoot ?? AppContext.BaseDirectory,
        });
        var options = builder.Configuration.GetSection("LiveDemo").Get<LiveDemoOptions>() ?? new();

        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
        builder.Services.AddProblemDetails();
        builder.Services.Configure<LiveDemoOptions>(builder.Configuration.GetSection("LiveDemo"));
        builder.Services.AddOllamaExtractor(builder.Configuration);
        builder.Services.AddSingleton<ReadGate>();

        // Funnel hands us requests from this PC: trust its X-Forwarded-For (from loopback only, the default).
        builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor);
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(options.AllowedOrigin).WithMethods("GET", "POST")));
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LiveDemoEndpoints.VisitorPolicy, http => options.PerVisitorPerHour > 0
                ? RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = options.PerVisitorPerHour, Window = TimeSpan.FromHours(1) })
                : RateLimitPartition.GetNoLimiter("everyone"));
            // Before a photo is taken in: at most one being read, MaxWaiting waiting and one more on its way, so the
            // photos held in memory stay few. The rest are told it's busy without being uploaded.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                HttpMethods.IsPost(http.Request.Method) && http.Request.Path == "/api/extract"
                    ? RateLimitPartition.GetConcurrencyLimiter("uploads",
                        _ => new ConcurrencyLimiterOptions { PermitLimit = options.MaxWaiting + 2, QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("other"));
            o.OnRejected = (context, _) => new ValueTask(
                (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var _) // the hourly window; uploads have none
                    ? Results.Problem(LiveDemoEndpoints.OverLimit(options.PerVisitorPerHour), statusCode: StatusCodes.Status429TooManyRequests)
                    : Results.Problem(LiveDemoEndpoints.Busy, statusCode: StatusCodes.Status503ServiceUnavailable))
                .ExecuteAsync(context.HttpContext));
        });

        configure?.Invoke(builder);

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseCors();
        app.UseRateLimiter();
        app.Use((http, next) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return next(http);
        });
        app.MapLiveDemoEndpoints();

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var model = app.Services.GetRequiredService<IOptions<OllamaOptions>>().Value.Model;
            Console.WriteLine();
            Console.WriteLine($"  Live demo: reading photos with {model}, at most {options.Budget}; stops at {DateTime.Now + options.StopAfter:HH:mm}.");
            Console.WriteLine();

            var stop = new Timer(_ => app.Lifetime.StopApplication(), null, options.StopAfter, Timeout.InfiniteTimeSpan);
            app.Lifetime.ApplicationStopping.Register(stop.Dispose);
        });

        return app;
    }
}
