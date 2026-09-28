---
applyTo: "eval/**,datasets/**"
---

# Extraction eval (Python) and dataset

Python 3.12 in `.venv`, dependencies in `requirements.txt` (`ollama`, `pydantic`, OCR libraries). Scripts run from the project root and build paths from `PROJECT_ROOT`.

## Scripts
- `try_vision.py` — one receipt through the vision model. Its `PROMPT` must match `OllamaExtractor.Prompt` in the API.
- `compare_approaches.py` — vision vs Tesseract vs PaddleOCR on one receipt.
- `run_eval.py` — scores every approach on every labeled receipt and writes `eval/RESULTS.md`. Flags: `--only vision tesseract`, `--model`, `--fresh`.
- Scripts are written to teach: a module docstring with setup and run commands, numbered step comments. Keep that style.

## Rules
- **Never commit `eval/preds/`** (cached predictions, including full OCR text with TINs). Never paste OCR text or prediction contents into committed files.
- `RESULTS.md` is generated — don't hand-edit numbers. After a run, copy the new numbers into the README bake-off table.
- Report accuracy honestly: don't change scoring, labels or aliases to make a number go up. Regressions go in README "Known issues".
- Store name and company name both count as the merchant; add equivalents to `merchant_aliases.json` rather than loosening the comparison.

## Dataset
- Images in `datasets/labels/`, ground truth in `datasets/receipts/<YYYYMMDD>_<Merchant>.json` with the same base name.
- Label fields (snake_case, amounts as numbers with centavos): `merchant`, `date` (`YYYY-MM-DD`), `total`, `vat_amount`, `vatable_sales`, `vat_exempt_sales`, `discount`, `category`, `notes`. Use `0.00` when the receipt has no such line.
- Label what's printed on the receipt, not what the model returned.
- Redact TINs and card digits in images before committing.
