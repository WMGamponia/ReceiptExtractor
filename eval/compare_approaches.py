"""
Lesson 2: run the same receipt through three approaches and compare them.

  vision     - the model looks at the photo directly          (Approach B)
  tesseract  - Tesseract reads the text, the model parses it  (Approach A1)
  paddleocr  - PaddleOCR reads the text, the model parses it  (Approach A2)

All three use the same model, prompt and schema as try_vision.py, so the only
difference is "sees the image" vs "reads OCR text".

Setup (once):
    pip install -r requirements.txt
    Tesseract binary: install the UB Mannheim Windows build
    (https://github.com/UB-Mannheim/tesseract/wiki)

Run (from the project root):
    python eval/compare_approaches.py datasets/labels/20251220_DataBlitz.jpg
    python eval/compare_approaches.py datasets/labels/20231203.jpg --only vision tesseract
    python eval/compare_approaches.py datasets/labels/20231203.jpg --hide-ocr

An approach whose OCR tool isn't installed is skipped with a message,
so you can start with just vision + tesseract.
"""

import argparse
import os
import shutil
import time
from pathlib import Path

import ollama

# Reuse the schema, prompt and label lookup from Lesson 1 so everything stays in sync.
from try_vision import PROMPT, Receipt, extract, find_label

APPROACHES = ["vision", "tesseract", "paddleocr"]

TEXT_PROMPT = PROMPT + """

You cannot see the image. Below is the text an OCR tool read from it.
OCR can misread characters (O/0, S/5, l/1) and scramble line order - use context.

OCR text:
"""


class Skip(Exception):
    """Raised when an approach can't run (tool not installed)."""


# --- Parsing OCR text with the model ------------------------------------------

def parse_text(ocr_text: str, model: str) -> Receipt:
    resp = ollama.chat(
        model=model,
        messages=[{"role": "user", "content": TEXT_PROMPT + ocr_text}],
        format=Receipt.model_json_schema(),
        think=False,
        options={"temperature": 0, "num_ctx": 8192},
    )
    return Receipt.model_validate_json(resp.message.content or "{}")


# --- OCR tools -----------------------------------------------------------------

def find_tesseract() -> str | None:
    """Look for tesseract.exe: TESSERACT_CMD env var, PATH, then the usual install folders."""
    env = os.environ.get("TESSERACT_CMD")
    if env and Path(env).exists():
        return env
    on_path = shutil.which("tesseract")
    if on_path:
        return on_path
    candidates = [
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Tesseract-OCR",
        Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Tesseract-OCR",
        Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Tesseract-OCR",  # "install for me only"
        Path(os.environ.get("LOCALAPPDATA", "")) / "Tesseract-OCR",
    ]
    for folder in candidates:
        exe = folder / "tesseract.exe"
        if exe.exists():
            return str(exe)
    return None


def ocr_tesseract(image_path: Path) -> str:
    try:
        import pytesseract
        from PIL import Image
    except ImportError:
        raise Skip("pip install pytesseract pillow")

    exe = find_tesseract()
    if exe is None:
        raise Skip(
            "Tesseract binary not found. Install the UB Mannheim build, or set "
            "TESSERACT_CMD to the full path of tesseract.exe"
        )
    pytesseract.pytesseract.tesseract_cmd = exe

    return pytesseract.image_to_string(Image.open(image_path))


_paddle = None  # loading PaddleOCR is slow, so do it once


def ocr_paddle(image_path: Path) -> str:
    global _paddle
    if _paddle is None:
        try:
            from paddleocr import PaddleOCR
        except ImportError:
            raise Skip("pip install paddlepaddle paddleocr")
        _paddle = PaddleOCR(
            lang="en",
            use_doc_orientation_classify=True,  # some photos are sideways (e.g. 20231203.jpg)
            use_doc_unwarping=False,
            use_textline_orientation=True,
            # Work around a PaddlePaddle 3.3 CPU bug ("ConvertPirAttribute2RuntimeAttribute
            # not support"): turn off the oneDNN (MKL-DNN) accelerator. Slightly slower, works.
            enable_mkldnn=False,
        )

    lines = []
    for res in _paddle.predict(str(image_path)):
        try:
            lines.extend(res["rec_texts"])
        except (KeyError, TypeError):
            lines.extend(res.json["res"]["rec_texts"])
    return "\n".join(lines)


# --- Running one approach ------------------------------------------------------

def run(approach: str, image_path: Path, model: str):
    """Returns (Receipt, ocr_text or None, ocr_seconds, total_seconds)."""
    start = time.perf_counter()
    if approach == "vision":
        result, _ = extract(image_path, model)
        return result, None, 0.0, time.perf_counter() - start

    ocr = ocr_tesseract if approach == "tesseract" else ocr_paddle
    text = ocr(image_path)
    ocr_seconds = time.perf_counter() - start
    return parse_text(text, model), text, ocr_seconds, time.perf_counter() - start


# --- Comparing against the label ----------------------------------------------

def matches(got, want) -> bool:
    if isinstance(want, (int, float)) and isinstance(got, (int, float)):
        return abs(got - want) < 0.01
    return str(got or "").strip().lower() == str(want or "").strip().lower()


def main() -> None:
    parser = argparse.ArgumentParser(description="Compare extraction approaches on one receipt.")
    parser.add_argument("image", type=Path)
    parser.add_argument("--model", default="qwen3-vl:8b-instruct")
    parser.add_argument("--only", nargs="+", choices=APPROACHES, default=APPROACHES)
    parser.add_argument("--hide-ocr", action="store_true", help="Don't print raw OCR text")
    args = parser.parse_args()

    if not args.image.exists():
        raise SystemExit(f"Image not found: {args.image}")

    print(f"Model: {args.model}\nImage: {args.image}")
    results = {}

    for approach in args.only:
        print(f"\n=== {approach} ===")
        try:
            result, text, ocr_s, total_s = run(approach, args.image, args.model)
        except Skip as why:
            print(f"skipped: {why}")
            continue

        if text is not None and not args.hide_ocr:
            print("--- raw OCR text ---")
            print(text.strip() or "(nothing read)")
            print("--------------------")
        timing = f"{total_s:.1f}s" + (f" (OCR {ocr_s:.1f}s)" if text is not None else "")
        print(f"{result.model_dump()}\ntook {timing}")
        results[approach] = (result, total_s)

    if not results:
        return

    # Side-by-side table
    label = find_label(args.image)
    fields = list(Receipt.model_fields)
    col = 26
    header = f"\n{'field':<14}" + "".join(f"{a:<{col}}" for a in results)
    if label:
        header += "label"
    print(header)
    print("-" * len(header))

    for field in fields:
        want = label.get(field) if label else None
        row = f"{field:<14}"
        for result, _ in results.values():
            got = getattr(result, field)
            mark = (" OK" if matches(got, want) else " X") if label else ""
            row += f"{(str(got) + mark)[:col - 1]:<{col}}"
        if label:
            row += str(want)
        print(row)

    row = f"{'seconds':<14}" + "".join(f"{s:<{col}.1f}" for _, s in results.values())
    print(row)

    if label:
        score = f"{'correct':<14}"
        for result, _ in results.values():
            n = sum(matches(getattr(result, f), label.get(f)) for f in fields)
            score += f"{f'{n}/{len(fields)}':<{col}}"
        print(score)
    else:
        print("\n(No matching label JSON found, so no OK/X marks.)")


if __name__ == "__main__":
    main()
