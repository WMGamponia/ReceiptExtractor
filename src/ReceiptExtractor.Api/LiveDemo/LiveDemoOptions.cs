namespace ReceiptExtractor.Api.LiveDemo;

/// <summary>The "LiveDemo" settings (scripts/live-demo.ps1). Defaults are the spec's; the script only sets Enabled.</summary>
public sealed class LiveDemoOptions
{
    public bool Enabled { get; set; }

    /// <summary>The only site whose pages may read the answers (CORS).</summary>
    public string AllowedOrigin { get; set; } = "https://receipt-tracker-demo.pages.dev";

    /// <summary>Reads per visitor (IP address) per hour; 0 turns it off (if the tunnel doesn't pass the visitor's address).</summary>
    public int PerVisitorPerHour { get; set; } = 20;

    /// <summary>Reads per run, for everyone together.</summary>
    public int Budget { get; set; } = 100;

    /// <summary>Photos that may wait while one is being read; more are told the PC is busy.</summary>
    public int MaxWaiting { get; set; } = 3;

    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The service shuts itself down after this long.</summary>
    public TimeSpan StopAfter { get; set; } = TimeSpan.FromHours(3);
}
