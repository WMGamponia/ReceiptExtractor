# Service Charge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract a receipt's service charge and count it in the PH VAT check, so restaurant receipts that add one on top of the breakdown stop being flagged.

**Architecture:** A new money field `service_charge` follows the existing `discount` field through every layer (model prompt and schema, `ReceiptFields`, DB column, API DTOs, CSV, review page, demo). The VAT check, which exists three times (C#, TS, Python) and is pinned by `tests/vat-cases.json`, accepts the service charge either inside the breakdown or on top of it, the same way it treats the discount.

**Tech Stack:** ASP.NET Core (.NET 10) minimal APIs, EF Core + SQLite, xUnit; React + TypeScript + Vite, vitest; Python eval (pydantic, Ollama `qwen3-vl:8b-instruct`).

**Spec:** `docs/superpowers/specs/2026-09-30-service-charge-design.md`

## Global Constraints

- Names: `service_charge` (model JSON, label JSON, eval, re-extract lock names), `ServiceCharge` (C#), `serviceCharge` (TS / API JSON).
- Stored and sent as a positive amount; `0`/`null` = none. Negative user input → 400 with `"Enter the service charge as a positive amount."` under key `serviceCharge`.
- Check rule: adds up if `vatable + vat + exempt − d + s = total` (±0.02) for some `d ∈ {0, discount}`, `s ∈ {0, service charge}`. VAT = 12% of VATable is unchanged.
- The prompt text is identical in `OllamaExtractor.Prompt` and `eval/try_vision.py` (`SyncTests.Prompt_matches_the_eval_prompt`).
- Commits: no `Co-Authored-By` trailer, no attribution lines (user preference).
- Build/test .NET with `--artifacts-path` in the scratchpad (the user's running API locks `bin/`): `dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/dotnet"`. Web: never `npm ci` (use `npm install` if needed).
- Out of scope: tips, service charge in made-up sample receipts (`SampleReceipts.cs`, `sampleReceipts.ts`).

`$SCRATCH` below = `C:/Users/Wesley/AppData/Local/Temp/claude/E--Work-Projects-ReceiptExtractor/ab22033b-1f18-4a4c-96df-1986a68921f6/scratchpad`.

## Review Focus

- A receipt with a service charge but no VAT breakdown (non-VAT restaurant) → no flag. Pinned by a shared case in Task 1.
- The model printing the charge as `-542.01` or omitting the key (older stored `ModelOutput`) → stored positive / null. Pinned in Task 2. Confirming such an old receipt must record no spurious `service_charge` correction: Task 3's `DiffFromModel` compares `?? 0m` on both sides (there's no endpoint test harness for confirm; the reviewer checks that line).
- An existing `receipts.db` without the column → patched on startup, old rows still load. Pinned in Task 3.
- Re-extract locking `service_charge` with a negative value → 400, in both the API (`ReextractRequest.Check`) and the demo. Pinned in Tasks 3 and 5.
- The review page's VAT auto-fill with a service charge on top picks the VATable/VAT pair that fits 12%. Pinned by a `vatComplement` test in Task 1.

---

### Task 1: The VAT check understands service charges (C#, TS, Python)

**Files:**
- Modify: `src/ReceiptExtractor.Api/Validation/ReceiptFields.cs` (add `ServiceCharge`)
- Modify: `src/ReceiptExtractor.Api/Validation/ReceiptValidator.cs`
- Modify: `web/src/format.ts` (`Amounts`, `vatWarnings`, `vatComplement`)
- Modify: `eval/run_eval.py` (`vat_check`)
- Modify: `tests/vat-cases.json`
- Test: `tests/ReceiptExtractor.Tests/SyncTests.cs`, `tests/ReceiptExtractor.Tests/ReceiptValidatorTests.cs`, `web/src/format.test.ts`, `eval/test_scoring.py`

**Interfaces:**
- Produces: `ReceiptFields(..., decimal? Discount = null, decimal? ServiceCharge = null)`; `ReceiptValidator.VatFlags(decimal total, decimal vat, decimal vatable, decimal exempt, decimal discount, decimal serviceCharge = 0)`; TS `Amounts.serviceCharge?: number | null`; Python `vat_check(p)` reads `p["service_charge"]`.

- [ ] **Step 1: Add the shared cases**

Add `"service_charge": null` to every existing case in `tests/vat-cases.json` (right after `"discount"`), then append these cases to `cases`, and add ", service_charge" to the `_about` sentence's field list if it lists fields (it doesn't today; leave it):

```bash
cd /e/Work/Projects/ReceiptExtractor && .venv/Scripts/python - <<'EOF'
import json, re
from pathlib import Path
p = Path("tests/vat-cases.json")
text = p.read_text(encoding="utf-8")
text = re.sub(r'("discount": [^,]+), ("problems")', r'\1, "service_charge": null, \2', text)
new = [
    '{ "name": "Texas Roadhouse: SC discount, then a service charge on top", "total": 6323.43, "vat": 361.34, "vatable": 3011.16, "exempt": 3011.16, "discount": 602.23, "service_charge": 542.01, "problems": 0 }',
    '{ "name": "Barbecue house: same shape", "total": 6498.91, "vat": 466.43, "vatable": 3886.92, "exempt": 1943.45, "discount": 388.69, "service_charge": 590.81, "problems": 0 }',
    '{ "name": "Texas Roadhouse without the service charge is flagged", "total": 6323.43, "vat": 361.34, "vatable": 3011.16, "exempt": 3011.16, "discount": 602.23, "service_charge": null, "problems": 1 }',
    '{ "name": "Service charge on top, no discount", "total": 1220.00, "vat": 120.00, "vatable": 1000.00, "exempt": 0, "discount": 0, "service_charge": 100.00, "problems": 0 }',
    '{ "name": "Service charge already inside VATable sales", "total": 1120.00, "vat": 120.00, "vatable": 1000.00, "exempt": 0, "discount": 0, "service_charge": 100.00, "problems": 0 }',
    '{ "name": "Service charge that does not cover the gap", "total": 1250.00, "vat": 120.00, "vatable": 1000.00, "exempt": 0, "discount": 0, "service_charge": 100.00, "problems": 1 }',
    '{ "name": "Non-VAT receipt with a service charge", "total": 1100.00, "vat": 0, "vatable": 0, "exempt": 0, "discount": 0, "service_charge": 100.00, "problems": 0 }',
]
text = text.replace('"problems": 1 }\n  ]', '"problems": 1 },\n    ' + ',\n    '.join(new) + '\n  ]')
json.loads(text)  # still valid JSON
p.write_text(text, encoding="utf-8")
EOF
```

Check: `git diff tests/vat-cases.json` shows 14 existing cases gained `"service_charge": null` and 7 new cases at the end.

- [ ] **Step 2: Pass the new key in all three runners**

`tests/ReceiptExtractor.Tests/SyncTests.cs`, in `Vat_check_matches_the_shared_cases`:

```csharp
        var flags = ReceiptValidator.VatFlags(Amount(c, "total"), Amount(c, "vat"), Amount(c, "vatable"),
            Amount(c, "exempt"), Amount(c, "discount"), Amount(c, "service_charge"));
```

`web/src/format.test.ts`, the shared-case test:

```ts
  it.each(vatCases.cases)('matches the shared case: $name', (c) =>
    expect(
      vatWarnings({ total: c.total, vat: c.vat, vatable: c.vatable, exempt: c.exempt, discount: c.discount, serviceCharge: c.service_charge }),
    ).toHaveLength(c.problems))
```

`eval/test_scoring.py`, in the shared-case loop:

```python
                problem = vat_check({"total": c["total"], "vat_amount": c["vat"], "vatable_sales": c["vatable"],
                                     "vat_exempt_sales": c["exempt"], "discount": c["discount"],
                                     "service_charge": c["service_charge"]})
```

Add C# tests to `tests/ReceiptExtractor.Tests/ReceiptValidatorTests.cs`:

```csharp
    [Fact]
    public void Service_charge_on_top_of_the_breakdown_passes()
    {
        // Texas Roadhouse: senior discount off the breakdown, then a 10% service charge added.
        var fields = new ReceiptFields("TEXAS ROADHOUSE", new DateOnly(2026, 8, 8), 6323.43m, 361.34m, 3011.16m, 3011.16m, 602.23m,
            ServiceCharge: 542.01m);

        Assert.Empty(ReceiptValidator.Validate(fields, Today));
    }

    [Fact]
    public void Flag_shows_the_total_with_the_service_charge()
    {
        var flags = ReceiptValidator.VatFlags(total: 1300m, vat: 120m, vatable: 1000m, exempt: 0m, discount: 0m, serviceCharge: 100m);

        Assert.Contains(flags, f => f.Contains("(₱1,220.00 with the ₱100.00 service charge)") && f.Contains("but total is ₱1,300.00"));
    }
```

Add TS tests to `web/src/format.test.ts` (inside `describe('vatWarnings ...')` and `describe('vatComplement', ...)` respectively):

```ts
  it('names the service charge when it still does not add up', () =>
    expect(vatWarnings({ total: 1300, vat: 120, vatable: 1000, exempt: 0, discount: 0, serviceCharge: 100 })).toEqual([
      'VATable sales + VAT = ₱1,120.00 (₱1,220.00 with the ₱100.00 service charge), but total is ₱1,300.00.',
    ]))
```

```ts
  it('takes a service charge on top into account', () =>
    expect(
      vatComplement({ total: 6323.43, vat: 361.34, vatable: null, exempt: 3011.16, discount: 602.23, serviceCharge: 542.01 }, 'vat'),
    ).toBe(3011.15))
```

(3011.15, not .16: the total is rounded on the receipt, and the helper picks the pair closest to 12%.)

- [ ] **Step 3: Run the tests to see them fail**

```bash
cd /e/Work/Projects/ReceiptExtractor && dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/dotnet" --filter "FullyQualifiedName~SyncTests|FullyQualifiedName~ReceiptValidatorTests" 2>&1 | tail -3
cd web && npx vitest run src/format.test.ts 2>&1 | tail -5; cd ..
.venv/Scripts/python -m unittest eval/test_scoring.py 2>&1 | tail -3
```

Expected: C# does not compile (`ServiceCharge`, `serviceCharge` unknown); vitest fails the new cases (Texas Roadhouse etc.); Python fails the Texas Roadhouse subtest.

- [ ] **Step 4: Implement in C#**

`src/ReceiptExtractor.Api/Validation/ReceiptFields.cs`:

```csharp
    decimal? VatExemptSales = null, // e.g. senior citizen / PWD purchases
    decimal? Discount = null,       // all discounts on the receipt added up, as a positive amount
    decimal? ServiceCharge = null); // e.g. a restaurant's 10% service charge, as a positive amount
```

`src/ReceiptExtractor.Api/Validation/ReceiptValidator.cs`: in `Validate`,

```csharp
            flags.AddRange(VatFlags(total, f.VatAmount ?? 0, f.VatableSales ?? 0, f.VatExemptSales ?? 0, f.Discount ?? 0,
                f.ServiceCharge ?? 0));
```

and replace `VatFlags` (doc comment included):

```csharp
    /// <summary>
    /// The VAT breakdown on a PH receipt adds up to the total:
    ///   VATable sales + VAT + VAT-exempt sales = total, or
    ///   ... − discount = total   (senior citizen / PWD discounts are taken off after the breakdown), and/or
    ///   ... + service charge = total   (restaurants often add it after the breakdown).
    /// Promo discounts and some service charges are already inside the breakdown, so every combination is accepted.
    /// </summary>
    public static List<string> VatFlags(decimal total, decimal vat, decimal vatable, decimal exempt, decimal discount,
        decimal serviceCharge = 0)
    {
        var flags = new List<string>();
        if (vat == 0 && vatable == 0 && exempt == 0)
            return flags; // no breakdown printed (non-VAT receipt): nothing to check

        var breakdown = vatable + vat + exempt;
        decimal[] discounts = discount != 0 ? [0, discount] : [0];
        decimal[] charges = serviceCharge != 0 ? [0, serviceCharge] : [0];
        var adds = discounts.Any(d => charges.Any(s => Math.Abs(breakdown - d + s - total) <= Tolerance));
        if (!adds)
        {
            var parts = exempt != 0 ? "VATable sales + VAT + VAT-exempt sales" : "VATable sales + VAT";
            var tried = new List<string>();
            if (discount != 0)
                tried.Add($"{Peso.Format(breakdown - discount)} after the {Peso.Format(discount)} discount");
            if (serviceCharge != 0)
                tried.Add($"{Peso.Format(breakdown - discount + serviceCharge)} with the {Peso.Format(serviceCharge)} service charge");
            var less = tried.Count > 0 ? $" ({string.Join(", ", tried)})" : "";
            flags.Add($"{parts} = {Peso.Format(breakdown)}{less}, but total is {Peso.Format(total)}.");
        }

        var expectedVat = Math.Round(vatable * VatRate, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(vat - expectedVat) > Tolerance)
            flags.Add($"VAT {Peso.Format(vat)} isn't 12% of VATable sales (expected {Peso.Format(expectedVat)}). Could be a misread.");

        return flags;
    }
```

- [ ] **Step 5: Implement in TS**

`web/src/format.ts`:

```ts
export interface Amounts {
  total: number | null
  vat: number | null
  vatable: number | null
  exempt: number | null
  discount: number | null
  serviceCharge?: number | null // positive; on top of the breakdown or already inside it
}
```

In `vatWarnings`, update the doc comment's rule line to "... = total + discount − service charge (either may already be inside the breakdown)" and replace the body from `const discount = ...` through the `if (!adds) {...}` block:

```ts
  const discount = num(a.discount)
  const serviceCharge = num(a.serviceCharge ?? null)
  if (a.total == null || Number.isNaN(a.total) || (vat === 0 && vatable === 0 && exempt === 0)) return []
  const total = a.total

  const warnings: string[] = []
  const breakdown = vatable + vat + exempt
  const discounts = discount !== 0 ? [0, discount] : [0]
  const charges = serviceCharge !== 0 ? [0, serviceCharge] : [0]
  const adds = discounts.some((d) => charges.some((s) => Math.abs(breakdown - d + s - total) <= TOLERANCE))
  if (!adds) {
    const parts = exempt !== 0 ? 'VATable sales + VAT + VAT-exempt sales' : 'VATable sales + VAT'
    const tried: string[] = []
    if (discount !== 0) tried.push(`${formatPeso(breakdown - discount)} after the ${formatPeso(discount)} discount`)
    if (serviceCharge !== 0)
      tried.push(`${formatPeso(breakdown - discount + serviceCharge)} with the ${formatPeso(serviceCharge)} service charge`)
    const less = tried.length > 0 ? ` (${tried.join(', ')})` : ''
    warnings.push(`${parts} = ${formatPeso(breakdown)}${less}, but total is ${formatPeso(total)}.`)
  }
```

In `vatComplement`, replace the NaN guard and the `bases` lines:

```ts
  if (a.total == null || value == null || [a.total, value, a.exempt, a.discount, a.serviceCharge].some((n) => Number.isNaN(n))) return null
  if (value <= 0) return null

  const base = a.total - num(a.exempt)
  const discounts = num(a.discount) > 0 ? [0, num(a.discount)] : [0]
  const charges = num(a.serviceCharge ?? null) > 0 ? [0, num(a.serviceCharge ?? null)] : [0]
  const bases = discounts.flatMap((d) => charges.map((s) => base + d - s))
```

Update its doc comment: "Given the total (and any VAT-exempt sales / discount / service charge) ... With a discount or service charge it can go either way ...".

- [ ] **Step 6: Implement in Python**

`eval/run_eval.py`, `vat_check`:

```python
def vat_check(p: dict) -> str | None:
    """Same rule as ReceiptValidator.cs: vatable + vat + exempt = total, allowing for a discount taken off
    and a service charge added after the breakdown (either may also be inside it), and vat = 12% of vatable.
    Returns a short problem description, or None if the numbers are consistent."""
    total = p.get("total")
    vat, vatable = p.get("vat_amount") or 0, p.get("vatable_sales") or 0
    exempt, discount = p.get("vat_exempt_sales") or 0, abs(p.get("discount") or 0)
    service = abs(p.get("service_charge") or 0)
    if total is None:
        return "no total"
    if not vat and not vatable and not exempt:
        return None  # no VAT breakdown printed, nothing to check
    breakdown = vatable + vat + exempt
    if not any(abs(breakdown - d + s - total) <= 0.02 for d in {0, discount} for s in {0, service}):
        return (f"vatable+vat+exempt={breakdown:.2f} != total" + (f" (discount {discount:.2f})" if discount else "")
                + (f" (service charge {service:.2f})" if service else ""))
    if abs(vat - round(vatable * 0.12, 2)) > 0.02:
        return f"vat != 12% of vatable ({vatable * 0.12:.2f})"
    return None
```

Update the `vat-cases.json` `_about` text only if it becomes wrong (it doesn't).

- [ ] **Step 7: Run the tests to see them pass**

Same commands as Step 3. Expected: all pass (C# SyncTests + ReceiptValidatorTests, vitest `format.test.ts`, Python `test_scoring`). Then `cd web && npx tsc -b` → no errors (ReviewPage/demoApi still compile: `serviceCharge` is optional).

- [ ] **Step 8: Commit**

```bash
git add tests/vat-cases.json src/ReceiptExtractor.Api/Validation web/src/format.ts web/src/format.test.ts eval/run_eval.py eval/test_scoring.py tests/ReceiptExtractor.Tests/SyncTests.cs tests/ReceiptExtractor.Tests/ReceiptValidatorTests.cs
git commit -m "VAT check: allow a service charge on top of the breakdown (C#, TS, Python)"
```

---

### Task 2: The model reads the service charge

**Files:**
- Modify: `src/ReceiptExtractor.Api/Extraction/OllamaExtractor.cs` (prompt, schema, `ModelReceipt`, `ParseModelOutput`)
- Modify: `eval/try_vision.py` (`Receipt`, `PROMPT`)
- Modify: `src/ReceiptExtractor.Api/Extraction/UserHints.cs`
- Test: `tests/ReceiptExtractor.Tests/ExtractionTests.cs`, `tests/ReceiptExtractor.Tests/UserHintsTests.cs`, `tests/ReceiptExtractor.Tests/SyncTests.cs` (existing prompt sync test)

**Interfaces:**
- Consumes: `ReceiptFields.ServiceCharge` (Task 1).
- Produces: `UserHints.ServiceCharge = "service_charge"` (in `UserHints.Fields` after `Discount`); `OllamaExtractor.ParseModelOutput` fills `Fields.ServiceCharge` (positive or null); model JSON key `service_charge`.

- [ ] **Step 1: Write the failing tests**

`tests/ReceiptExtractor.Tests/ExtractionTests.cs`, add:

```csharp
    [Fact]
    public void Reads_the_service_charge_as_a_positive_amount()
    {
        const string json = """
            {"merchant":"TEXAS ROADHOUSE","date":"2026-08-08","total":6323.43,"vat_amount":361.34,"vatable_sales":3011.16,"vat_exempt_sales":3011.16,"discount":602.23,"service_charge":-542.01,"category":"Food"}
            """;

        var result = OllamaExtractor.ParseModelOutput(json);

        Assert.Equal(542.01m, result.Fields.ServiceCharge);
    }
```

and in `Older_answers_without_the_new_fields_still_parse` add `Assert.Null(result.Fields.ServiceCharge);`.

`tests/ReceiptExtractor.Tests/UserHintsTests.cs`, add:

```csharp
    [Fact]
    public void Service_charge_can_be_locked()
    {
        var model = new ReceiptFields("X", new DateOnly(2026, 8, 8), 1220m, 120m, 1000m, ServiceCharge: 0m);
        var user = model with { ServiceCharge = 100m };

        Assert.Equal(new[] { new FieldHint("service_charge", "100.00") }, UserHints.Describe(user, null, ["service_charge"]));
        Assert.Equal(100m, UserHints.Keep(model, user, new HashSet<string> { "service_charge" }).ServiceCharge);
    }
```

- [ ] **Step 2: Run to see them fail**

```bash
dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/dotnet" --filter "FullyQualifiedName~ExtractionTests|FullyQualifiedName~UserHintsTests" 2>&1 | tail -3
```

Expected: `Reads_the_service_charge...` fails (null), `Service_charge_can_be_locked` fails (hint not described).

- [ ] **Step 3: Implement**

Prompt: in **both** `OllamaExtractor.Prompt` and `eval/try_vision.py` `PROMPT`, insert after the two `discount` lines (same indentation as the neighbors: 8 spaces + `- ` in C#, none in Python):

```
- service_charge: the service charge line (often 10% at restaurants), as a positive
  number; 0 if there is none. Tips are not a service charge
```

Schema (`OllamaExtractor.Schema`): add after `discount`

```json
            "service_charge": { "anyOf": [ { "type": "number" }, { "type": "null" } ] },
```

and add `"service_charge"` to `required` after `"discount"`.

`ModelReceipt`: `[JsonPropertyName("service_charge")] public decimal? ServiceCharge { get; set; }` after `Discount`.

`ParseModelOutput`: pass it to the fields, and widen the helper's comment:

```csharp
            PositiveMoney(model.Discount),
            PositiveMoney(model.ServiceCharge));
```

```csharp
    /// <summary>Discounts are often printed as "-20.00" (and a model may sign a charge too); we store them as positive amounts.</summary>
```

`eval/try_vision.py` `Receipt`: add after `discount`

```python
    service_charge: float | None = Field(description="Service charge in PHP, positive; 0 if none (tips are not)")
```

`UserHints.cs`:

```csharp
    public const string ServiceCharge = "service_charge";
    ...
    public static readonly string[] Fields =
        [Merchant, Date, Total, VatAmount, VatableSales, VatExemptSales, Discount, ServiceCharge, Category];
```

In `Prompt`, the last line becomes:

```csharp
        lines.Add("(VATable sales + VAT + VAT-exempt sales must add up to the total, before or after the discount and service charge).");
```

`Keep`: add `locked.Contains(ServiceCharge) ? user.ServiceCharge : model.ServiceCharge` as the 8th constructor argument. `Format`: `ServiceCharge => Money(f.ServiceCharge ?? 0),`.

- [ ] **Step 4: Run all backend tests**

```bash
dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/dotnet" 2>&1 | tail -3
```

Expected: everything passes except possibly `SeedDataTests.Labeled_receipts_wait_in_review_with_their_photos` (fixed in Task 3). `Prompt_matches_the_eval_prompt` must pass; if `OllamaRequestTests` or a hints test asserts the old "add up" sentence or the schema's `required` list, update that expectation to the new text.

- [ ] **Step 5: Commit**

```bash
git add src/ReceiptExtractor.Api/Extraction eval/try_vision.py tests/ReceiptExtractor.Tests/ExtractionTests.cs tests/ReceiptExtractor.Tests/UserHintsTests.cs
git commit -m "Extractor: read the service charge (prompt, schema, re-extract hints)"
```

(The committed `eval/model_outputs/` are now stale for the new prompt; Task 4 re-runs the eval before the PR.)

---

### Task 3: Store, show over the API, export, and seed the service charge

**Files:**
- Modify: `src/ReceiptExtractor.Api/Data/ReceiptRecord.cs`, `src/ReceiptExtractor.Api/Data/SchemaPatches.cs`
- Modify: `src/ReceiptExtractor.Api/Endpoints/ReceiptEndpoints.cs` (`ReceiptResponse`, `ReceiptUpdate`, `ReextractRequest.Check`, confirm validation, `DiffFromModel`)
- Modify: `src/ReceiptExtractor.Api/Export/ReceiptCsv.cs`
- Modify: `src/ReceiptExtractor.Api/Seeding/LabeledReceipts.cs`
- Test: `tests/ReceiptExtractor.Tests/SchemaPatchesTests.cs`, `ReceiptCsvTests.cs`, `UserHintsTests.cs`, `SeedDataTests.cs`

**Interfaces:**
- Consumes: `ReceiptFields.ServiceCharge` (Task 1), `UserHints.ServiceCharge` (Task 2).
- Produces: API JSON `serviceCharge` on receipts; `ReceiptUpdate(..., string? Category, decimal? ServiceCharge = null)`; CSV header `Date,Merchant,Category,Total,VAT,VATable sales,VAT-exempt sales,Discount,Service charge,Receipt`; label JSON key `service_charge` read by seeding.

- [ ] **Step 1: Write the failing tests**

`SchemaPatchesTests.cs`: add `"ServiceCharge"` to `AddedSinceFirstRelease`:

```csharp
        ["VatExemptSales", "Discount", "Corrections", "LockedFields", "ServiceCharge"];
```

`ReceiptCsvTests.cs`: add `decimal? serviceCharge = null` to the `Receipt(...)` helper parameters and `ServiceCharge = serviceCharge,` to its initializer; update expectations:

```csharp
        Assert.Equal("Date,Merchant,Category,Total,VAT,VATable sales,VAT-exempt sales,Discount,Service charge,Receipt", lines[0]);
        Assert.Equal("20251220,DataBlitz,Shopping,10495.00,1124.46,9370.54,,,,2025-12-20_DataBlitz", lines[1]);
```

and in `Amounts_are_plain_numbers_whatever_the_PC_region`:

```csharp
            var row = Lines(ReceiptCsv.Write([Receipt(total: 1234567.8m, exempt: 500m, discount: 100m, serviceCharge: 50m)]))[1];

            Assert.Contains(",1234567.80,1124.46,9370.54,500.00,100.00,50.00,", row);
```

Fix any other row expectations in the file (e.g. `Missing_values_are_empty_cells`) by adding one empty cell after the discount cell.

`UserHintsTests.cs` (re-extract validation lives on `ReextractRequest`):

```csharp
    [Fact]
    public void Re_extract_rejects_a_negative_service_charge()
    {
        var update = new ReceiptUpdate("X", new DateOnly(2026, 8, 8), 1220m, 120m, 1000m, null, null, "Food", ServiceCharge: -100m);

        var (valid, errors) = new ReextractRequest(update, ["service_charge"]).Check();

        Assert.Null(valid);
        Assert.Equal(new[] { "Enter the service charge as a positive amount." }, errors["serviceCharge"]);
    }
```

(add `using ReceiptExtractor.Api.Endpoints;` if the file lacks it.)

`SeedDataTests.cs`, `Labeled_receipts_wait_in_review_with_their_photos`: replace the status assertion so it follows the checks (the PSI Hose label legitimately fails them):

```csharp
        Assert.All(labeled, r =>
        {
            // Labels are real receipts: one whose own numbers don't add up waits in review like any other.
            Assert.Equal(r.Flags.Count > 0 ? ReceiptStatus.NeedsReview : ReceiptStatus.Extracted, r.Status);
            Assert.Equal(Path.Combine(_storage, $"{r.Id}.jpg"), r.ImagePath);
            Assert.True(File.Exists(r.ImagePath));
            Assert.Equal("image/jpeg", r.ContentType);
        });
```

Rename the test to `Labeled_receipts_wait_for_review_with_their_photos` only if nothing else references it (grep first).

- [ ] **Step 2: Run to see them fail**

```bash
dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/dotnet" --filter "FullyQualifiedName~SchemaPatches|FullyQualifiedName~ReceiptCsv|FullyQualifiedName~UserHints|FullyQualifiedName~SeedData" 2>&1 | tail -3
```

Expected: compile errors (`ServiceCharge` on `ReceiptRecord`/`ReceiptUpdate`).

- [ ] **Step 3: Implement**

`ReceiptRecord.cs`: `public decimal? ServiceCharge { get; set; }` after `Discount`; `Fields => new(Merchant, Date, Total, VatAmount, VatableSales, VatExemptSales, Discount, ServiceCharge);`; `ApplyFields`: `ServiceCharge = f.ServiceCharge;` after `Discount = f.Discount;`.

`SchemaPatches.cs`: append `(nameof(ReceiptRecord.ServiceCharge), "TEXT NULL"),` to `AddedColumns`.

`ReceiptEndpoints.cs`:
- `ReceiptResponse`: add `decimal? ServiceCharge,` after `decimal? Discount,`; in `From`, `r.VatExemptSales, r.Discount, r.ServiceCharge, r.Category, ...`.
- `ReceiptUpdate`: add a last parameter `decimal? ServiceCharge = null` (after `string? Category`); JSON binds by name, and existing positional callers keep compiling.
- `ReextractRequest.Check`: after the discount rule
  ```csharp
          if (locked.Contains(UserHints.ServiceCharge) && Fields.ServiceCharge < 0)
              errors["serviceCharge"] = ["Enter the service charge as a positive amount."];
  ```
  and build `user` with `..., Fields.VatExemptSales, Fields.Discount, Fields.ServiceCharge);`.
- Confirm handler: after the discount rule `if (update.ServiceCharge < 0) errors["serviceCharge"] = ["Enter the service charge as a positive amount."];` and `update.VatExemptSales, update.Discount, update.ServiceCharge);` in the `ReceiptFields` construction.
- `DiffFromModel`: after the discount line
  ```csharp
          Compare("service_charge", extracted.ServiceCharge ?? 0m, confirmed.ServiceCharge ?? 0m, Money);
  ```
  (`?? 0m` on both sides: a receipt read before this field existed has `null`, and confirming it with 0 is not a correction.)

`ReceiptCsv.cs`: header `..., "Discount", "Service charge", "Receipt"]` and `Money(r.ServiceCharge),` after `Money(r.Discount),`.

`LabeledReceipts.cs`: add to `Label` after `decimal? Discount,`:

```csharp
        [property: JsonPropertyName("service_charge")] decimal? ServiceCharge,
```

(keeping `string? Category` last) and pass `label.Discount, label.ServiceCharge)` to `ReceiptFields`.

Grep for any other `new ReceiptFields(` / `ReceiptResponse(` / `.Discount` sites that should carry the field:

```bash
git grep -n "\.Discount\b" -- src | grep -v ServiceCharge
```

Each hit should now also handle `ServiceCharge` unless it's in `SampleReceipts.cs` (out of scope).

- [ ] **Step 4: Run all backend tests**

```bash
dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/dotnet" 2>&1 | tail -3
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ReceiptExtractor.Api tests/ReceiptExtractor.Tests
git commit -m "API: store, return, validate and export the service charge; seed it from labels"
```

---

### Task 4: New dataset receipts, labels with service charge, eval and demo answers

**Files:**
- Add: the 10 new photos in `datasets/labels/` and their JSONs in `datasets/receipts/` (already in the working tree, verified by the user 2026-09-30)
- Modify: every `datasets/receipts/*.json` (add `service_charge`)
- Modify: `eval/merchant_aliases.json` (already edited in the working tree)
- Modify: `eval/make_demo_receipts.py` (`keep`), regenerate `web/src/demo/model-answers.json`
- Regenerate: `eval/RESULTS.md`, `eval/model_outputs/vision/*.json`

**Interfaces:**
- Consumes: the prompt/schema from Task 2 (eval) and the API field `serviceCharge` from Task 3 (demo read step).
- Produces: `model-answers.json` entries with a `serviceCharge` key (Task 5's `answerFields` reads it).

- [ ] **Step 1: Add `service_charge` to every label**

```bash
cd /e/Work/Projects/ReceiptExtractor && .venv/Scripts/python - <<'EOF'
import re
from pathlib import Path
charges = {"20260808_TexasRoadhouse": "542.01", "20260821_BarbecueHouse": "590.81"}
for p in sorted(Path("datasets/receipts").glob("*.json")):
    text = p.read_text(encoding="utf-8")
    if '"service_charge"' in text:
        continue
    value = charges.get(p.stem, "0.00")
    text = re.sub(r'("discount": [0-9.]+,)', r'\1\n  "service_charge": ' + value + ',', text, count=1)
    p.write_text(text, encoding="utf-8")
    print(p.name, value)
EOF
git diff --stat datasets/receipts
```

Expected: 18 files, each with one new line; Texas Roadhouse 542.01, barbecue house 590.81, the rest 0.00. In `20260821_BarbecueHouse.json`'s `notes`, drop the trailing "10% service charge 590.81." sentence (the field says it now).

- [ ] **Step 2: Photos have no location, labels load**

```bash
.venv/Scripts/python eval/photo_metadata.py check
.venv/Scripts/python -m unittest discover -s eval -p "test_*.py" 2>&1 | tail -3
dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/dotnet" --filter "FullyQualifiedName~SeedData" 2>&1 | tail -3
```

Expected: "no GPS locations"; Python tests OK; seed tests pass (Texas Roadhouse and barbecue house now `Extracted`, PSI Hose `NeedsReview`).

- [ ] **Step 3: Commit the dataset**

```bash
git add datasets/labels datasets/receipts eval/merchant_aliases.json
git commit -m "Dataset: 10 more receipts (restaurants, fuel, auto supply, handwritten), service charge in every label"
```

- [ ] **Step 4: Re-run the eval with the new prompt** (needs Ollama running with `qwen3-vl:8b-instruct`; ~20 receipts × ~15 s)

```bash
.venv/Scripts/python eval/run_eval.py --only vision
```

Expected: `eval/RESULTS.md` rewritten for 18 receipts with a `service_charge` column; core accuracy ≥ 85% (CI's bar). If it's below 85%, stop and report the misses to the user before going on: don't tune the prompt inside this task.

Then confirm CI's offline check passes and nothing private is staged:

```bash
.venv/Scripts/python eval/run_eval.py --offline --only vision --check
git status --short eval/
```

`eval/preds/` must not appear as staged (it's ignored; never commit it).

- [ ] **Step 5: Commit the eval**

```bash
git add eval/RESULTS.md eval/model_outputs
git commit -m "Eval: re-run with the service charge field on 18 receipts"
```

- [ ] **Step 6: Regenerate the demo's model answers**

In `eval/make_demo_receipts.py`, `read_all`, add `"serviceCharge"` to `keep` after `"discount"`.

Start a throwaway Production instance of this branch (its own `data` folder, port 5080; the user's desktop app must not be running on 5080, ask them to close it if `curl -s localhost:5080` answers):

```bash
dotnet publish src/ReceiptExtractor.Api -c Release -o "$SCRATCH/demo-read-app" --artifacts-path "$SCRATCH/dotnet"
cd "$SCRATCH/demo-read-app" && ./ReceiptExtractor.Api.exe --Seed:Enabled=false
```

Run that with `run_in_background`. Then:

```bash
cd /e/Work/Projects/ReceiptExtractor && .venv/Scripts/python eval/make_demo_receipts.py read --api http://localhost:5080
```

Expected: 5 receipts read; `web/src/demo/model-answers.json` has `"serviceCharge": 0.0` (or a value) in each entry. Stop the background app and delete `$SCRATCH/demo-read-app`. If an answer changed besides the new key (the model reads a little differently with the new prompt), keep it: that's the real answer. Check `docs/live-demo.md`'s description of the model's mistakes (pharmacy date swapped, sari-sari store as Food) still matches the new answers and fix the sentence if not.

- [ ] **Step 7: Commit**

```bash
git add eval/make_demo_receipts.py web/src/demo/model-answers.json docs/live-demo.md
git commit -m "Demo: re-read the synthetic receipts with the new prompt"
```

---

### Task 5: Review page and demo site

**Files:**
- Modify: `web/src/api.ts`, `web/src/pages/ReviewPage.tsx`
- Modify: `web/src/demo/demoApi.ts`, `web/src/demo/csv.ts`
- Test: `web/src/demo/demo.test.ts`, `web/src/demo/liveReader.test.ts`

**Interfaces:**
- Consumes: API JSON `serviceCharge` and CSV header (Task 3); `Amounts.serviceCharge`, `vatWarnings`, `vatComplement` (Task 1); `model-answers.json` `serviceCharge` (Task 4); lock name `service_charge` (Task 2).
- Produces: nothing later tasks use.

- [ ] **Step 1: Types first (they make the compiler list every site)**

`web/src/api.ts`: in `Receipt` after `discount`: `serviceCharge: number | null // positive; e.g. a restaurant's 10%`; in `ReceiptUpdate` after `discount`: `serviceCharge: number | null`; in `LockableField` add `| 'service_charge'` after `'discount'`.

```bash
cd web && npx tsc -b 2>&1 | head -30
```

Expected: errors in `demoApi.ts`, `ReviewPage.tsx`, `demo.test.ts`, `liveReader.test.ts` (missing `serviceCharge` / `service_charge`). Those are the sites for Steps 2–4.

- [ ] **Step 2: Write the failing demo tests**

`web/src/demo/demo.test.ts`:
- the `receipt(...)` fixture: add `serviceCharge: null,` after `discount: null,`;
- the live `READING` fixture (and `liveReader.test.ts`'s `READING.fields`): add `serviceCharge: null` after `discount: null`;
- the samples test at line ~87: pass `serviceCharge: r.serviceCharge` to `vatWarnings`;
- CSV expectations: header `...,Discount,Service charge,Receipt` and one more empty cell (`10495.00,1124.46,9370.54,,,,2025-12-20_DataBlitz`; `100.00,,,,,,"a, b"`);
- new tests in the `demoApi` describe that has `checks required fields like the API`:

```ts
  it('checks the service charge like the API', async () => {
    const r = (await demoApi.listReceipts())[0]
    await expect(demoApi.confirmReceipt(r.id, { ...r, serviceCharge: -1 })).rejects.toMatchObject({
      status: 400,
      fieldErrors: { serviceCharge: ['Enter the service charge as a positive amount.'] },
    })
  })

  it('does not flag a service charge on top of the breakdown', async () => {
    const r = (await demoApi.listReceipts())[0]
    const saved = await demoApi.confirmReceipt(r.id, {
      ...r, date: '2026-08-08', category: 'Food', total: 1220, vatAmount: 120, vatableSales: 1000, vatExemptSales: 0, discount: 0, serviceCharge: 100,
    })
    expect(saved.flags).toEqual([])
    expect(saved.serviceCharge).toBe(100)
  })
```

and in the live-demo describe, next to the other re-extract validation test (or after `runs the same checks on the answer`):

```ts
  it('rejects a negative service charge on re-extract', async () => {
    fakePc()
    const r = (await demoApi.listReceipts())[0]
    await expect(demoApi.reextractReceipt(r.id, { ...r, serviceCharge: -1 }, ['service_charge'])).rejects.toMatchObject({
      status: 400,
      fieldErrors: { serviceCharge: ['Enter the service charge as a positive amount.'] },
    })
  })
```

(If `confirmReceipt`'s flags for the sample receipt include a date-in-the-future or other flag, pick a date before today; the point is no VAT flag.)

Run: `npx vitest run src/demo` → the new tests fail.

- [ ] **Step 3: Implement the demo**

`web/src/demo/demoApi.ts`:
- `blank(...)`: `serviceCharge: null,` after `discount: null,`
- `answerFields`: `serviceCharge: a.serviceCharge,` after `discount: a.discount,`
- `checks`: add `'serviceCharge'` to the `Pick<...>` and pass `serviceCharge: r.serviceCharge` to `vatWarnings`
- `corrections`: after the discount compare `compare('service_charge', model.serviceCharge ?? 0, confirmed.serviceCharge ?? 0, money)`
- `FIELD_OF`: `service_charge: 'serviceCharge',` after `discount`
- confirm validation: `if ((update.serviceCharge ?? 0) < 0) errors.serviceCharge = ['Enter the service charge as a positive amount.']` after the discount rule, and `serviceCharge: update.serviceCharge,` in `fields` after `discount`
- re-extract validation: `if (keep.includes('service_charge') && (update.serviceCharge ?? 0) < 0) errors.serviceCharge = ['Enter the service charge as a positive amount.']` after the discount rule

`web/src/demo/csv.ts`: header `..., 'Discount', 'Service charge', 'Receipt']` and `money(r.serviceCharge),` after `money(r.discount),`.

- [ ] **Step 4: Implement the review page**

`web/src/pages/ReviewPage.tsx`:
- `FormState`: `serviceCharge: string` after `discount`
- `amountsOf`: `serviceCharge: parseMoney(f.serviceCharge),`
- `LOCKABLE`: `serviceCharge: { name: 'service_charge', label: 'service charge', money: true },` after `discount`
- `initial`: `serviceCharge: money(receipt.serviceCharge),`
- `setAmount`: widen the field type to `'total' | 'vatExemptSales' | 'discount' | 'serviceCharge'` and its comment to "Total, VAT-exempt sales, discount or service charge changed: ..."
- destructure `const { total, vat, vatable, exempt, discount, serviceCharge } = amounts`
- `toUpdate`: `serviceCharge,` after `discount,`
- both `[total, vat, vatable, exempt, discount].some(...)` NaN checks: add `serviceCharge`
- after the VAT-exempt / Discount `field-pair`:

```tsx
      <Field label="Service charge (₱)" hint="e.g. 10% at restaurants" error={fieldError('serviceCharge')}>
        <MoneyInput value={form.serviceCharge} onChange={(v) => setAmount('serviceCharge', v)} />
      </Field>
```

- [ ] **Step 5: Run the web checks**

```bash
cd web && npx tsc -b && npm run lint && npm test 2>&1 | tail -5 && npm run build:demo 2>&1 | tail -3
```

Expected: no type errors, lint clean, all vitest tests pass, demo build succeeds.

- [ ] **Step 6: See it in the app**

Start the API (`dotnet run --project src/ReceiptExtractor.Api`, port 5036) and the web dev server (`cd web && npm run dev`) via the Browser pane's preview (add `.claude/launch.json` entries if missing), open a receipt's review page, type a total of 1,220.00, VATable 1,000.00, VAT 120.00 and service charge 100.00: no "VAT doesn't add up" warning; clear the service charge: the warning appears. Take a screenshot for the PR.

- [ ] **Step 7: Commit**

```bash
git add web/src
git commit -m "Web: service charge on the review page, in the demo and its CSV"
```

---

### Task 6: Docs

**Files:**
- Modify: `README.md`, `ROADMAP.md` (if a checkbox applies)

- [ ] **Step 1: README**

Per CLAUDE.md: copy the new numbers from `eval/RESULTS.md` into the README's bake-off table and receipt count; bump "Last updated" to the commit date; under "What I learned" add one bullet, e.g. "**Restaurants add a service charge after the VAT breakdown.** Two of the new receipts (Texas Roadhouse, a barbecue house) only add up once the 10% service charge is counted, so it's now its own field and the VAT check allows it on top of the breakdown or inside it, like a discount." Mention in "Known issues" any new eval misses that are notable (e.g. handwritten receipts), in one line each, from `RESULTS.md`'s misses table.

- [ ] **Step 2: ROADMAP**

Open `ROADMAP.md`; if an item covers VAT checks, dataset size or service charges, tick or update it. Otherwise leave it unchanged.

- [ ] **Step 3: Full verification**

```bash
cd /e/Work/Projects/ReceiptExtractor
dotnet test tests/ReceiptExtractor.Tests --artifacts-path "$SCRATCH/dotnet" 2>&1 | tail -3
.venv/Scripts/python -m unittest discover -s eval -p "test_*.py" 2>&1 | tail -3
.venv/Scripts/python eval/run_eval.py --offline --only vision --check
cd web && npm run lint && npm test 2>&1 | tail -3 && npm run build 2>&1 | tail -2
```

Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add README.md ROADMAP.md
git commit -m "README: service charges, 18-receipt eval"
```

The PR (`[Feature] Service charge in the VAT check`, body from `.github/pull_request_template.md`, no attribution line) is opened after the user reviews the branch; it stacks on `fix/photo-orientation`, which merges first.
