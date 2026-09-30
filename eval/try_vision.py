"""
Lesson 1: send one receipt image to a local vision model and get structured fields back.

Setup (once):
    pip install -r requirements.txt
    ollama pull qwen3-vl:8b-instruct

Run (from the project root):
    python eval/try_vision.py datasets/labels/20251220_DataBlitz.jpg
    python eval/try_vision.py datasets/labels/20231203.jpg --model qwen3-vl:4b-instruct

If a hand-labeled JSON with the same name exists in datasets/, it prints a
side-by-side comparison so you can see what the model got right.
"""

import argparse
import json
import time
from pathlib import Path

import ollama
from pydantic import BaseModel, Field

PROJECT_ROOT = Path(__file__).resolve().parent.parent
LABEL_DIRS = [PROJECT_ROOT / "datasets" / "receipts", PROJECT_ROOT / "datasets" / "labels"]


# 1. The shape we want back. Field names match your label JSON files.
#    Ollama turns this into a JSON schema and forces the model to follow it.
class Receipt(BaseModel):
    merchant: str | None = Field(description="Store/business name as printed, or null")
    date: str | None = Field(description="Transaction date as YYYY-MM-DD, or null")
    total: float | None = Field(description="Final amount paid in PHP")
    vat_amount: float | None = Field(description="12% VAT amount in PHP; 0 if non-VAT")
    vatable_sales: float | None = Field(description="VATable sales in PHP; 0 if non-VAT")
    vat_exempt_sales: float | None = Field(description="VAT-exempt sales in PHP; 0 if none")
    discount: float | None = Field(description="All discounts added up, positive, in PHP; 0 if none")
    service_charge: float | None = Field(description="Service charge in PHP, positive; 0 if none (tips are not)")


# 2. The instructions. Same prompt for every receipt, so results are comparable.
PROMPT = """You are reading a receipt from the Philippines.
Copy amounts exactly as printed, including centavos (e.g. 1124.46)
Extract these fields:
- merchant: the store or company name as printed at the top. For online
  orders, the marketplace (e.g. Lazada, Shopee), not the seller's shop name
- date: the transaction date, converted to YYYY-MM-DD
- total: the final amount paid (TOTAL / AMOUNT DUE) after discounts, not cash tendered or change
- vat_amount: the "VAT Amount" / "12% VAT" line; 0 if the receipt has none.
  Not the "Less 12% VAT" / "Less VAT" line of a senior citizen / PWD computation
  (that is VAT removed from exempt items, not VAT charged)
- vatable_sales: the "VATable Sales" line; 0 if the receipt has none
- vat_exempt_sales: the "VAT-Exempt Sales" line; 0 if the receipt has none
- discount: all discounts on the receipt added up (senior citizen, PWD, promo, etc.),
  as a positive number; 0 if there are none. "Less VAT" lines are not discounts
- service_charge: the service charge line (often 10% at restaurants), as a positive
  number; 0 if there is none. Tips are not a service charge
Some receipts print the VAT breakdown as a tax table (Tax Code / RATE / Amount / TAX amt)
instead of labeled lines: the V (12%) row's Amount is vatable_sales and its TAX is vat_amount,
and the E (0%) row's Amount is vat_exempt_sales.
Keep the decimal point: 2,345.67 is 2345.67, never 2345 or 234567.
Amounts are in PHP. Numbers only, no commas or peso signs.
Use null if a field is unreadable."""


def upright(image_path: Path) -> bytes:
    """Phones save photos sideways plus an EXIF tag saying how to turn them; Ollama ignores the tag, so the
    model would read the receipt on its side. Same as PhotoOrientation.cs in the app."""
    from PIL import Image, ImageOps  # imported here: CI scores saved outputs without Pillow installed
    import io

    data = image_path.read_bytes()
    with Image.open(io.BytesIO(data)) as im:
        if im.getexif().get(0x0112, 1) == 1:  # 0x0112 = Orientation; 1 = already upright
            return data
        out = io.BytesIO()
        ImageOps.exif_transpose(im).convert("RGB").save(out, "JPEG", quality=92)
        return out.getvalue()


def extract(image_path: Path, model: str) -> tuple[Receipt, float]:
    start = time.perf_counter()
    resp = ollama.chat(
        model=model,
        messages=[{"role": "user", "content": PROMPT, "images": [upright(image_path)]}],
        format=Receipt.model_json_schema(),  # force the JSON shape
        think=False,                         # skip "thinking" mode; answer directly
        options={
            "temperature": 0,  # same input -> same output
            "num_ctx": 8192,   # context window; the image alone can take ~4000 tokens
        },
    )
    seconds = time.perf_counter() - start
    content = resp.message.content or ""
    if not content.strip():
        thinking = (getattr(resp.message, "thinking", None) or "")[:500]
        raise SystemExit(
            f"Model returned no answer (done_reason={resp.done_reason!r}).\n"
            f"Thinking output (first 500 chars):\n{thinking or '(none)'}"
        )
    return Receipt.model_validate_json(content), seconds


def find_label(image_path: Path) -> dict | None:
    for folder in LABEL_DIRS:
        candidate = folder / f"{image_path.stem}.json"
        if candidate.exists():
            return json.loads(candidate.read_text(encoding="utf-8"))
    return None


def main() -> None:
    parser = argparse.ArgumentParser(description="Extract fields from one receipt image.")
    parser.add_argument("image", type=Path, help="Path to a receipt image (JPG/PNG)")
    parser.add_argument("--model", default="qwen3-vl:8b-instruct", help="Ollama model tag")
    args = parser.parse_args()

    if not args.image.exists():
        raise SystemExit(f"Image not found: {args.image}")

    print(f"Model: {args.model}\nImage: {args.image}\nThinking...\n")
    result, seconds = extract(args.image, args.model)

    print(json.dumps(result.model_dump(), indent=2))
    print(f"\nTook {seconds:.1f}s")

    label = find_label(args.image)
    if label is None:
        print("\n(No matching label JSON found, so no comparison.)")
        return

    print("\nField          Model                          Label                          Match")
    print("-" * 88)
    for field in Receipt.model_fields:
        got, want = getattr(result, field), label.get(field)
        if isinstance(want, (int, float)) and isinstance(got, (int, float)):
            ok = abs(got - want) < 0.01
        else:
            ok = str(got or "").strip().lower() == str(want or "").strip().lower()
        print(f"{field:<14} {str(got):<30} {str(want):<30} {'OK' if ok else 'X'}")


if __name__ == "__main__":
    main()
