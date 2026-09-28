---
applyTo: "**/*.cs"
---

# Logging

Logs go to the console only (default ASP.NET Core providers). No Application Insights, OpenTelemetry exporters or other telemetry — data stays on the machine.

## How
- Inject `ILogger<T>` through the primary constructor as `log`. In static endpoint classes, take `ILoggerFactory logs` and call `logs.CreateLogger(typeof(XxxEndpoints))`.
- Message templates with PascalCase placeholders only — never `$"..."` or concatenation:
  ```csharp
  log.LogInformation("Receipt {ReceiptId}: {Status} in {Seconds}s", id, receipt.Status, receipt.ExtractionSeconds);
  ```
- Reuse existing property names: `ReceiptId`, `Status`, `Seconds`, `Count`, `Model`, `Url`, `Files`. One name per concept.
- Pass the exception as the first argument, and log it **once**, where it's handled (for example the worker loop), not at every layer.

## Levels
| Level | Use for | Examples in this app |
|---|---|---|
| Information | Lifecycle events worth seeing in the console | Receipt finished with status + duration; re-queued N unfinished receipts on startup |
| Warning | Handled problems the user can fix or will see | Extraction failed (receipt marked `Failed`); Ollama not running or model not pulled; image file couldn't be deleted |
| Error | Unexpected failures caught by a safety net | Crash in the worker loop |
| Critical | Not used — the app has no failure worse than "Ollama unavailable", which is a Warning | |
| Debug | Temporary troubleshooting; remove before committing | |

Don't log method entry/exit, every loop iteration, or what ASP.NET Core already logs (requests, status codes).

## Never log
Receipt field values, `ModelOutput`, user hints, OCR text, image bytes/base64, full Ollama request bodies, TINs or card digits. Log the receipt ID and let the reader open the receipt in the app.

## Not logging
- What the user needs to act on goes in `receipt.Error` or a `ValidationProblem`/`Conflict` message, in plain English — not only in the log.
- `Console.WriteLine` in `DesktopMode.OnStarted` is intentional user-facing output for the packaged exe, not logging.
