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

eval/preds/ holds OCR text (with TINs), so it's never committed. A slim copy of
each prediction (the extracted fields and timing, no OCR text) goes to
eval/model_outputs/, which IS committed: that's what lets GitHub Actions score
the eval without a GPU (--offline).

Run (from the project root, with .venv active):
    python eval/run_eval.py                          # all approaches
    python eval/run_eval.py --only vision tesseract  # skip slow PaddleOCR
    python eval/run_eval.py --fresh                  # ignore cache
    python eval/run_eval.py --offline --only vision --check   # what CI runs

Results are also written to eval/RESULTS.md (not in --offline mode).
"""

import argparse
import hashlib
import json
import os
import re
import sys
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
MODEL_OUTPUTS = ROOT / "eval" / "model_outputs"
RESULTS_MD = ROOT / "eval" / "RESULTS.md"

FIELDS = list(Receipt.model_fields)          # merchant, date, total, VAT fields, discount
CORE = ["date", "merchant", "total"]         # the fields SCOPE.md's 85% target is about
CORE_TARGET = 0.85                           # SCOPE.md: >= 85% core accuracy
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


class Stale(Exception):
    """--offline, but the committed model output is missing or was made with another prompt/model/image."""


def save_model_output(approach: str, image: Path, record: dict) -> None:
    """The committable part of a prediction: fields and timing, never the OCR text."""
    path = MODEL_OUTPUTS / approach / f"{image.stem}.json"
    slim = {k: record[k] for k in ("key", "model", "prediction", "seconds")}
    text = json.dumps(slim, indent=2, ensure_ascii=False) + "\n"
    if not path.exists() or path.read_text(encoding="utf-8") != text:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")


def load_model_output(approach: str, image: Path, model: str) -> dict:
    path = MODEL_OUTPUTS / approach / f"{image.stem}.json"
    if not path.exists():
        raise Stale(f"no saved model output for {image.stem}")
    record = json.loads(path.read_text(encoding="utf-8"))
    if record.get("key") != cache_key(approach, model, image):
        raise Stale(f"{image.stem} was scored with a different prompt, model or image")
    return record


def predict(approach: str, image: Path, model: str, fresh: bool) -> dict:
    path = PREDS / approach / f"{image.stem}.json"
    key = cache_key(approach, model, image)
    if not fresh and path.exists():
        cached = json.loads(path.read_text(encoding="utf-8"))
        if cached.get("key") == key:
            save_model_output(approach, image, cached)
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
    save_model_output(approach, image, record)
    return record


# --- Main ----------------------------------------------------------------------

def pct(n: int, d: int) -> str:
    return f"{100 * n / d:.0f}%" if d else "-"


def main() -> None:
    parser = argparse.ArgumentParser(description="Score every approach on every labeled receipt.")
    parser.add_argument("--model", default="qwen3-vl:8b-instruct")
    parser.add_argument("--only", nargs="+", choices=APPROACHES, default=APPROACHES)
    parser.add_argument("--fresh", action="store_true", help="Ignore cached predictions")
    parser.add_argument("--offline", action="store_true",
                        help="Score the committed eval/model_outputs/ only; never call Ollama (CI)")
    parser.add_argument("--check", action="store_true",
                        help=f"Exit with an error if core accuracy is under {CORE_TARGET:.0%} (CI)")
    args = parser.parse_args()

    dataset = load_dataset()
    print(f"{len(dataset)} labeled receipts, model {args.model}")

    if not args.offline:
        # Load the model once up front so the first receipt's time isn't inflated by loading.
        print("Loading model...")
        ollama.generate(model=args.model, prompt="", keep_alive="10m")

    summary, misses = {}, []
    for approach in args.only:
        print(f"\n=== {approach} ===")
        rows = []
        for image, label in dataset:
            try:
                if args.offline:
                    rec = load_model_output(approach, image, args.model)
                else:
                    rec = predict(approach, image, args.model, args.fresh)
            except Stale as why:
                sys.exit(f"{approach}: {why}.\nThe prompt, model or dataset changed since the eval last ran. "
                         f"Re-run it on a machine with Ollama and commit eval/model_outputs/:\n"
                         f"    python eval/run_eval.py --only {approach}")
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
        core += " ✅" if core_ok >= CORE_TARGET * n * len(CORE) else " ❌"
        slowest = f"{max(secs):.1f}" + (" ✅" if max(secs) < 30 else " ❌")
        lines.append(f"| {approach} | " + " | ".join(per_field)
                     + f" | {core} | {statistics.median(secs):.1f} | {slowest} | {flags} |")

    lines += ["", "Targets from SCOPE.md: core accuracy ≥ 85%, under 30s per receipt.",
              "VAT-check flags = results whose VAT breakdown doesn't add up (would be sent to review).",
              "", "## Misses", ""]
    if misses:
        lines += ["| Approach | Receipt | Field | Got | Expected |", "|---|---|---|---|---|"]
        lines += [f"| {a} | {r} | {f} | {g} | {w} |" for a, r, f, g, w in misses]
    else:
        lines.append("None 🎉")

    report = "\n".join(lines) + "\n"
    print("\n" + report)
    if not args.offline:
        RESULTS_MD.write_text(report, encoding="utf-8")
        print(f"Saved to {RESULTS_MD.relative_to(ROOT)}")
    if os.environ.get("GITHUB_STEP_SUMMARY"):  # show the table on the GitHub Actions run page
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary_md:
            summary_md.write(report)

    if args.check:
        below = [a for a, rows in summary.items()
                 if sum(r[2][f] for r in rows for f in CORE) < CORE_TARGET * n * len(CORE)]
        if below:
            sys.exit(f"Core accuracy is under the {CORE_TARGET:.0%} target for: {', '.join(below)}")
        print(f"Core accuracy meets the {CORE_TARGET:.0%} target.")


if __name__ == "__main__":
    main()
