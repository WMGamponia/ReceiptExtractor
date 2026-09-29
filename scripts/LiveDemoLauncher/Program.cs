// LiveDemo.exe: turns the live demo on with a double-click. It runs scripts/live-demo.ps1 in this window, which starts
// Ollama and Tailscale if needed, opens Funnel and runs the extract-only service. Ctrl+C stops it and turns Funnel off.
using System.Diagnostics;
using System.Reflection;

var repo = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(a => a.Key == "RepoRoot").Value!;
var script = Path.Combine(repo, "scripts", "live-demo.ps1");
Console.Title = "Receipt Tracker live demo";

if (!File.Exists(script))
{
    Console.WriteLine($"Can't find {script}. Build LiveDemo.exe again from the repo (see LiveDemoLauncher.csproj).");
    return WaitForEnter(1);
}

// Ctrl+C reaches the script too: it stops the service and turns Funnel off. This window waits for that to finish.
Console.CancelKeyPress += (_, e) => e.Cancel = true;

using var powershell = Process.Start(new ProcessStartInfo("powershell.exe")
{
    ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script },
    WorkingDirectory = repo,
    UseShellExecute = false,
})!;
powershell.WaitForExit();
return WaitForEnter(powershell.ExitCode);

// Keeps the window open so the last lines (why it stopped, "Funnel is off") can be read.
static int WaitForEnter(int exitCode)
{
    Console.WriteLine();
    Console.WriteLine("Press Enter to close this window.");
    Console.ReadLine();
    return exitCode;
}
