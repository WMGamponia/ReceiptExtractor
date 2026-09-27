"""
Lesson 3: run every labeled receipt through each approach and score them.

For each approach it reports:
  - accuracy per field
  - "core" accuracy on date + merchant + total   (SCOPE.md target: >= 85%)
  - median / slowest time per receipt            (SCOPE.md target: < 30s)
  - how many results fail the PH VAT math check  (vatable_sales + vat = total)

Predictions are cached in eval/preds/<approach>/<receipt>.json, so re-running
only re-scores. The cache is thrown away automatically when the model or the
prompt changes. Use --fresh to force a re-run anyway (e.g. after changing
PaddleOCR settings).

Run (from the project root, with .venv active):
    python eval/run_eval.py                          # all approaches
    python eval/run_eval.py --only vision tesseract  # skip slow PaddleOCR
    python eval/run_eval.py --fresh                  # ignore cache

Results are also written to eval/RESULTS.md.
"""

import argparse
import hashlib
import json
import re
import statistics
from datetime import date
from difflib import SequenceMatcher
from pathlib import Path

import ollama

from compare_approaches import APPROACHES, TEXT_PROMPT, Skip, run
from try_vision import PROMPT, Receipt

ROOT = Path(__file__).resolve().parent.parent
IMAGES = ROOT / "datasets" / "labels"
LABELS = ROOT / "datasets" / "receipts"
PREDS = ROOT / "eval" / "preds"
RESULTS_MD = ROOT / "eval" / "RESULTS.md"

FIELDS = list(Receipt.model_fields)          # merchant, date, total, vat_amount, vatable_sales
CORE = ["date", "merchant", "total"]         # the fields SCOPE.md's 85% target is about
IMAGE_EXTS = (".jpg", ".jpeg", ".png")


# --- Matching rules ------------------------------------------------------------

LEGAL_SUFFIXES = r"\b(CORPORATION|CORP|INCORPORATED|INC|CO|LTD|OPC)\b"


def norm_merchant(s) -> str:
    """'Robinsons Supermarket Corp.' and 'ROBINSONS SUPERMARKET' -> 'ROBINSONSSUPERMARKET'."""
    s = str(s or "").upper()
    s = re.sub(LEGAL_SUFFIXES, "", s)
    return re.sub(r"[^A-Z0-9]", "", s)


def load_aliases() -> list[set[str]]:
    """Groups of equivalent merchant names from merchant_aliases.json (normalized)."""
    path = ROOT / "eval" / "merchant_aliases.json"
    if not path.exists():
        return []
    data = json.loads(path.read_text(encoding="utf-8"))
    return [{norm_merchant(k), *(norm_merchant(n) for n in names)}
            for k, names in data.items() if not k.startswith("_")]


ALIASES = load_aliases()


def names_close(a: str, b: str) -> bool:
    return a == b or SequenceMatcher(None, a, b).ratio() >= 0.85


def merchant_ok(got, want) -> bool:
    a, b = norm_merchant(got), norm_merchant(want)
    if not a or not b:
        return a == b
    if names_close(a, b):
        return True
    # Store name vs company name: OK if both belong to the same alias group
    for group in ALIASES:
        if any(names_close(a, n) for n in group) and any(names_close(b, n) for n in group):
            return True
    return False


def field_ok(field: str, got, want) -> bool:
    if field == "merchant":
        return merchant_ok(got, want)
    if field == "date":
        return str(got or "").strip() == str(want or "").strip()
    # money: within 1 centavo; a missing VAT counts as 0
    g = 0.0 if got is None and field != "total" else got
    w = 0.0 if want is None and field != "total" else want
    if g is None or w is None:
        return g == w
    return abs(float(g) - float(w)) < 0.01


def vat_check(p: dict) -> str | None:
    """PH receipts are VAT-inclusive: vatable_sales + vat = total, and vat = total * 12/112.
    Returns a short problem description, or None if the numbers are consistent."""
    total, vat, vatable = p.get("total"), p.get("vat_amount") or 0, p.get("vatable_sales") or 0
    if total is None:
        return "no total"
    if not vat and not vatable:
        return None  # non-VAT receipt, nothing to check
    if abs(vatable + vat - total) > 0.02:
        return f"vatable+vat={vatable + vat:.2f} != total"
    if abs(vat - total * 12 / 112) > 0.02:
        return f"vat != total*12/112 ({total * 12 / 112:.2f})"
    return None


# --- Dataset + cache -----------------------------------------------------------

def load_dataset() -> list[tuple[Path, dict]]:
    pairs = []
    for label_path in sorted(LABELS.glob("*.json")):
        image = next((IMAGES / f"{label_path.stem}{ext}" for ext in IMAGE_EXTS
                      if (IMAGES / f"{label_path.stem}{ext}").exists()), None)
        if image is None:
            print(f"warning: no image for label {label_path.name}, skipping")
            continue
        pairs.append((image, json.loads(label_path.read_text(encoding="utf-8"))))
    return pairs


def cache_key(approach: str, model: str, image: Path) -> str:
    h = hashlib.sha256()
    for part in (approach, model, PROMPT, TEXT_PROMPT):
        h.update(part.encode())
    h.update(image.read_bytes())
    return h.hexdigest()[:16]


def predict(approach: str, image: Path, model: str, fresh: bool) -> dict:
    path = PREDS / approach / f"{image.stem}.json"
    key = cache_key(approach, model, image)
    if not fresh and path.exists():
        cached = json.loads(path.read_text(encoding="utf-8"))
        if cached.get("key") == key:
            return cached

    result, text, ocr_s, total_s = run(approach, image, model)
    record = {
        "key": key,
        "model": model,
        "prediction": result.model_dump(),
        "seconds": round(total_s, 2),
        "ocr_seconds": round(ocr_s, 2),
        "ocr_text": text,
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(record, indent=2, ensure_ascii=False), encoding="utf-8")
    return record


# --- Main ----------------------------------------------------------------------

def pct(n: int, d: int) -> str:
    return f"{100 * n / d:.0f}%" if d else "-"


def main() -> None:
    parser = argparse.ArgumentParser(description="Score every approach on every labeled receipt.")
    parser.add_argument("--model", default="qwen3-vl:8b-instruct")
    parser.add_argument("--only", nargs="+", choices=APPROACHES, default=APPROACHES)
    parser.add_argument("--fresh", action="store_true", help="Ignore cached predictions")
    args = parser.parse_args()

    dataset = load_dataset()
    print(f"{len(dataset)} labeled receipts, model {args.model}")

    # Load the model once up front so the first receipt's time isn't inflated by loading.
    print("Loading model...")
    ollama.generate(model=args.model, prompt="", keep_alive="10m")

    summary, misses = {}, []
    for approach in args.only:
        print(f"\n=== {approach} ===")
        rows = []
        for image, label in dataset:
            try:
                rec = predict(approach, image, args.model, args.fresh)
            except Skip as why:
                print(f"skipped: {why}")
                rows = None
                break
            p = rec["prediction"]
            ok = {f: field_ok(f, p.get(f), label.get(f)) for f in FIELDS}
            flag = vat_check(p)
            rows.append((image.stem, p, ok, rec["seconds"], flag))
            print(f"  {image.stem:<45} {sum(ok.values())}/{len(FIELDS)}  {rec['seconds']:>6.1f}s"
                  + (f"  VAT-check: {flag}" if flag else ""))
            for f in FIELDS:
                if not ok[f]:
                    misses.append((approach, image.stem, f, p.get(f), label.get(f)))
        if rows:
            summary[approach] = rows

    if not summary:
        return

    # Build the results table
    n = len(dataset)
    lines = [f"# Extraction bake-off results",
             "",
             f"{date.today()} · model `{args.model}` · {n} receipts",
             "",
             "| Approach | " + " | ".join(FIELDS) + " | Core (date, merchant, total) | Median s | Slowest s | VAT-check flags |",
             "|---|" + "---|" * len(FIELDS) + "---|---|---|---|"]
    for approach, rows in summary.items():
        per_field = [pct(sum(r[2][f] for r in rows), n) for f in FIELDS]
        core_ok = sum(r[2][f] for r in rows for f in CORE)
        secs = [r[3] for r in rows]
        flags = sum(1 for r in rows if r[4])
        core = pct(core_ok, n * len(CORE))
        core += " ✅" if core_ok >= 0.85 * n * len(CORE) else " ❌"
        slowest = f"{max(secs):.1f}" + (" ✅" if max(secs) < 30 else " ❌")
        lines.append(f"| {approach} | " + " | ".join(per_field)
                     + f" | {core} | {statistics.median(secs):.1f} | {slowest} | {flags} |")

    lines += ["", "Targets from SCOPE.md: core accuracy ≥ 85%, under 30s per receipt.",
              "VAT-check flags = results where vatable_sales + vat ≠ total (would be sent to review).",
              "", "## Misses", ""]
    if misses:
        lines += ["| Approach | Receipt | Field | Got | Expected |", "|---|---|---|---|---|"]
        lines += [f"| {a} | {r} | {f} | {g} | {w} |" for a, r, f, g, w in misses]
    else:
        lines.append("None 🎉")

    report = "\n".join(lines) + "\n"
    RESULTS_MD.write_text(report, encoding="utf-8")
    print("\n" + report)
    print(f"Saved to {RESULTS_MD.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
