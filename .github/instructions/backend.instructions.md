---
applyTo: "**/*.cs"
---

# C# / ASP.NET Core

.NET 10, nullable enabled, implicit usings, file-scoped namespaces matching the folder (`ReceiptExtractor.Api.Extraction`).

## API shape (minimal APIs — no controllers, services layer, repositories or MediatR)
- One `static class XxxEndpoints` per area with a `MapXxxEndpoints(this IEndpointRouteBuilder app)` extension, called from `Program.cs`.
- `app.MapGroup("/api/<plural-resource>").WithTags(...)`; every route gets `.WithSummary(...)`. Routes are lowercase, plural, kebab-case; GUID ids use `{id:guid}`.
- Handlers are `private static` methods named after the action (`Upload`, `Confirm`, `Retry`) that take `AppDbContext db` and other services as parameters and return `TypedResults` with an explicit `Results<...>` union.
- Status codes: validation → `TypedResults.ValidationProblem` keyed by camelCase field name; missing → `NotFound`; receipt `Queued`/`Processing` → `Conflict("<plain-English reason>")`; background work started → `Accepted`.
- Error messages are shown to the user as-is: plain English, say what to do next.
- Bulk endpoints dedupe ids, cap at `MaxBulk`, and return what succeeded plus what was skipped with a reason.

## Types
- DTOs are `sealed record`s next to their endpoints: `XxxResponse` (built with `static From(entity)`), `XxxRequest`, `XxxResult`. Never return `ReceiptRecord` from an endpoint.
- Classes are `sealed` unless designed for inheritance. Use primary constructors for DI.
- Config classes are `XxxOptions`, bound in `Program.cs`, read via `IOptions<T>`.
- Business rules go in **pure code** — static classes like `ReceiptValidator`, `MerchantRules`, `UserHints`, `ReceiptFiles`, or records like `PeriodRange` — so they can be tested without Ollama or the database. Put new rules there, not in handlers or the worker.

## Data and time
- EF Core + SQLite. Load, modify, `SaveChangesAsync(ct)`. Schema is `EnsureCreated()`; a new column also goes in `Data/SchemaPatches.cs`.
- Inject `TimeProvider`; never call `DateTime.Now`/`UtcNow` directly. Timestamps are UTC `DateTime` with a `Utc` suffix (`UploadedAtUtc`); receipt dates are `DateOnly`; "today" is `DateOnly.FromDateTime(time.GetLocalNow().DateTime)`.
- Money is `decimal`, rounded to 2 places with `MidpointRounding.AwayFromZero`. Discounts are stored as positive amounts.
- Receipt status flow: `Queued → Processing → Extracted | NeedsReview | Failed → Confirmed`. Endpoints must not change a receipt that's `Queued` or `Processing`.

## Extraction
- `ExtractionWorker` handles one receipt at a time (one GPU) from `ExtractionQueue`, with a new DI scope per receipt. Don't make it concurrent.
- Ollama is called only through the typed `HttpClient` registered for `IReceiptExtractor`. Keep `temperature = 0` and the JSON schema in sync with the prompt.
- Keep the model's first, unaided output in `ModelOutput`; it's the eval baseline for user corrections.

## Files and uploads
- Check size, extension **and** magic bytes. Build storage paths from the receipt GUID, never from the uploaded file name.
- Write to a temp file, then `File.Move(..., overwrite: true)`, so a failed write never leaves a half-written image.

## Async
- Async all the way; no `.Result`/`.Wait()`. `CancellationToken ct` is the last parameter and is passed to every async call.
- Methods returning `Task` end in `Async` — except endpoint handlers, which are named after the action.

## Naming
Standard .NET conventions, plus what this codebase already does:
- PascalCase types, members, constants (no `UPPER_SNAKE`); camelCase locals/parameters; `I` prefix for interfaces.
- `Id`, not `ID`; acronyms as words: `Api`, `Json`, `Vat`, `Utc`.
- Booleans read as a question: `isDevelopment`, `isJpeg`, `IsKnown`.
- Established short names — keep using them: `ct` (CancellationToken), `db` (AppDbContext), `log` (ILogger), `http` (HttpClient), `time` (TimeProvider).
- Domain vocabulary: Receipt, Merchant, Date, Total, VatAmount, VatableSales, VatExemptSales, Discount, Category, Flags (review warnings), Corrections, LockedFields, Hints. Model JSON is snake_case (`vat_amount`); API JSON is camelCase.
- Avoid vague names (`Helper`, `Manager`, `Utility`, `Data`, `Process`) when a specific one exists. Don't rename public members for style alone.

## Tests (xUnit, `Assert.*`, no mocking library)
- Class: `<Subject>Tests`. Method: a sentence with underscores describing the behavior — `Dropped_centavos_are_flagged`, `Non_VAT_receipt_skips_the_VAT_check`.
- Use real receipt values as fixtures, with a comment on where they came from. Use a fixed `Today` date instead of the clock.
- `[Theory]` + `[InlineData]` for tables of cases. Test pure logic directly; `Program` is `partial` if an integration test is ever needed.
