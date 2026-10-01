"""
Makes the synthetic receipts for the public demo site (web/, `npm run build:demo`).

The real receipt photos are personal data, so they never go on the public demo. Instead this draws a few
made-up Philippine receipts (made-up stores, TIN 000-000-000-000) and, in a second step, has the real app read
them with the real model. The demo then shows those images with the model's actual answers, mistakes included.

Run (from the project root, with .venv active):
    python eval/make_demo_receipts.py render
        -> web/public/demo/receipts/*.jpg
    python eval/make_demo_receipts.py read --api http://localhost:5080
        -> web/src/demo/model-answers.json
       (needs the app running with Ollama, e.g.
        OLLAMA_URL=http://host.docker.internal:11434 docker compose up --build)
"""

import argparse
import json
import random
import time
import urllib.request
import uuid
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
IMAGES = ROOT / "web" / "public" / "demo" / "receipts"
ANSWERS = ROOT / "web" / "src" / "demo" / "model-answers.json"

WIDTH = 34  # characters per line, like a 58 mm thermal printer


# 1. The receipts. Each is a list of lines; ("center", text) centres a line, ("pair", left, right) aligns an
#    amount to the right. Amounts follow PH VAT rules: VAT is included in the total (VAT = total x 12/112).
def peso(x: float) -> str:
    return f"{x:,.2f}"


def vat_lines(total: float) -> list:
    vat = round(total * 12 / 112, 2)
    return [("pair", "VATable Sales", peso(total - vat)), ("pair", "VAT Amount (12%)", peso(vat)),
            ("pair", "VAT-Exempt Sales", "0.00"), ("pair", "Zero-Rated Sales", "0.00")]


def header(name: str, address: list[str], vat_reg: bool = True) -> list:
    lines = [("center", name)] + [("center", a) for a in address]
    lines.append(("center", ("VAT REG TIN: " if vat_reg else "NON-VAT REG TIN: ") + "000-000-000-000"))
    return lines + [("rule",)]


def footer(or_no: str) -> list:
    return [("rule",), ("center", f"OR No. {or_no}"), ("center", "THIS SERVES AS YOUR OFFICIAL RECEIPT"),
            ("center", "Salamat po! Balik po kayo!")]


RECEIPTS = {
    # Fast food, labeled VAT lines.
    "kusina-ni-aling-rosa": dict(tilt=-1.5, fade=1.0, lines=[
        *header("KUSINA NI ALING ROSA", ["123 Rizal Ave., Brgy. Uno", "Quezon City"]),
        ("left", "Date: 09/14/2026  12:41 PM"), ("left", "Cashier: Joy   Table: 5"), ("rule",),
        ("pair", "1 Chicken Adobo w/ Rice", "145.00"), ("pair", "1 Pork Sinigang", "165.00"),
        ("pair", "2 Gulaman", "50.00"), ("pair", "1 Leche Flan", "55.00"), ("rule",),
        ("pair", "TOTAL", "415.00"), ("pair", "CASH", "500.00"), ("pair", "CHANGE", "85.00"), ("rule",),
        *vat_lines(415.00), *footer("0012345")]),

    # Supermarket, VAT breakdown printed as a tax table (the prompt has a note for these).
    "bayanihan-supermarket": dict(tilt=1.0, fade=1.0, lines=[
        *header("BAYANIHAN SUPERMARKET INC.", ["Lot 4 Mabini St., Poblacion", "Makati City"]),
        ("left", "08/29/2026 18:05  POS 03"), ("rule",),
        ("pair", "RICE 5KG DINORADO", "1,450.00"), ("pair", "EGGS 1 TRAY", "285.00"),
        ("pair", "COOKING OIL 1L", "189.75"), ("pair", "CANNED TUNA 3PC", "174.00"),
        ("pair", "LAUNDRY SOAP BAR", "96.00"), ("pair", "BANANA LAKATAN 1KG", "92.00"),
        ("pair", "FRESH MILK 1L", "182.00"), ("rule",),
        ("pair", "TOTAL", "2,468.75"), ("pair", "GCASH", "2,468.75"), ("rule",),
        ("left", "Tax Code RATE     Amount    TAX amt"),
        ("left", "V        12%    2,204.24     264.51"),
        ("left", "E         0%        0.00       0.00"),
        *footer("SI-0098812")]),

    # Pharmacy, senior citizen discount: VAT-exempt, 20% off, and a "Less VAT" line that isn't VAT charged.
    "botika-san-roque": dict(tilt=0.5, fade=1.0, lines=[
        *header("BOTIKA SAN ROQUE", ["45 Bonifacio St., San Roque", "Marikina City"]),
        ("left", "08/09/2026 09:17 AM"), ("rule",),
        ("pair", "LOSARTAN 50MG x30", "672.00"), ("pair", "METFORMIN 500MG x30", "448.00"), ("rule",),
        ("pair", "SUBTOTAL", "1,120.00"), ("pair", "Less 12% VAT", "120.00"), ("pair", "Net of VAT", "1,000.00"),
        ("pair", "Less SC Disc 20%", "200.00"), ("pair", "AMOUNT DUE", "800.00"), ("rule",),
        ("pair", "VATable Sales", "0.00"), ("pair", "VAT Amount", "0.00"), ("pair", "VAT-Exempt Sales", "1,000.00"),
        ("left", "SC/PWD ID: 00000   Name: J. Dela Cruz"),
        *footer("0004567")]),

    # Gas station, tilted and faded like an old thermal receipt.
    "petrolina-gas-station": dict(tilt=6.0, fade=0.55, lines=[
        *header("PETROLINA GAS STATION", ["Km 21 Aguinaldo Hi-way", "Imus, Cavite"]),
        ("left", "09/21/2026   07:52"), ("left", "Pump 04   Attendant: Mark"), ("rule",),
        ("pair", "UNLEADED 91  28.50 L", ""), ("pair", "  @ 62.35/L", "1,776.98"), ("rule",),
        ("pair", "TOTAL", "1,776.98"), ("pair", "CASH", "2,000.00"), ("pair", "CHANGE", "223.02"), ("rule",),
        *vat_lines(1776.98), *footer("0203344")]),

    # Sari-sari store, not VAT registered: no VAT breakdown at all.
    "tindahan-ni-mang-ben": dict(tilt=-3.0, fade=0.85, lines=[
        *header("TINDAHAN NI MANG BEN", ["Purok 3, Brgy. Maligaya", "Tanauan, Batangas"], vat_reg=False),
        ("left", "Petsa: 09/25/2026"), ("rule",),
        ("pair", "3 Pancit Canton", "57.00"), ("pair", "1 Kape 3-in-1 (10s)", "65.00"),
        ("pair", "2 Pandesal pack", "24.00"), ("rule",), ("pair", "TOTAL", "146.00"),
        ("rule",), ("center", "NON-VAT"), ("center", "Salamat!")]),
}


# 2. Drawing: monospace text on receipt paper, then turned into something that looks like a phone photo.
def font(size: int) -> ImageFont.FreeTypeFont:
    for name in ("consola.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default(size)


def render(name: str, spec: dict) -> Image.Image:
    f = font(20)
    char_w, line_h = f.getbbox("M")[2], 28
    paper_w = char_w * WIDTH + 60
    paper = Image.new("RGB", (paper_w, line_h * len(spec["lines"]) + 80), "#fbfaf6")
    d = ImageDraw.Draw(paper)
    y = 40
    for line in spec["lines"]:
        kind = line[0]
        if kind == "rule":
            d.text((30, y), "-" * WIDTH, font=f, fill="#333")
        elif kind == "center":
            d.text((paper_w / 2, y), line[1], font=f, fill="#222", anchor="ma")
        elif kind == "pair":
            d.text((30, y), line[1], font=f, fill="#222")
            d.text((paper_w - 30, y), line[2], font=f, fill="#222", anchor="ra")
        else:
            d.text((30, y), line[1], font=f, fill="#222")
        y += line_h

    # Faded thermal paper: less contrast.
    paper = ImageEnhance.Contrast(paper).enhance(spec["fade"])

    # A table top behind the paper, the paper a little tilted, a slight blur like a phone camera.
    rng = random.Random(name)
    table = Image.new("RGB", (paper.width + 160, paper.height + 160), (120 + rng.randint(0, 30), 96, 72))
    rotated = paper.rotate(spec["tilt"], expand=True, fillcolor=table.getpixel((0, 0)), resample=Image.BICUBIC)
    table.paste(rotated, ((table.width - rotated.width) // 2, (table.height - rotated.height) // 2))
    return table.filter(ImageFilter.GaussianBlur(0.6))


def render_all() -> None:
    IMAGES.mkdir(parents=True, exist_ok=True)
    for name, spec in RECEIPTS.items():
        path = IMAGES / f"{name}.jpg"
        render(name, spec).save(path, quality=82)
        print(f"{path.relative_to(ROOT)}  {path.stat().st_size // 1024} KB")


# 3. Reading: upload each image to a running Receipt Tracker (real model, real prompt) and save its answers.
def call(url: str, data: bytes | None = None, content_type: str | None = None) -> dict:
    req = urllib.request.Request(url, data=data, method="POST" if data else "GET")
    if content_type:
        req.add_header("Content-Type", content_type)
    with urllib.request.urlopen(req, timeout=600) as response:
        return json.loads(response.read())


def upload(api: str, path: Path) -> dict:
    boundary = uuid.uuid4().hex
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{path.name}\"\r\n"
            f"Content-Type: image/jpeg\r\n\r\n").encode() + path.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
    return call(f"{api}/api/receipts", body, f"multipart/form-data; boundary={boundary}")


def read_all(api: str) -> None:
    answers = {}
    for name in RECEIPTS:
        receipt = upload(api, IMAGES / f"{name}.jpg")
        while receipt["status"] in ("Queued", "Processing"):
            time.sleep(1)
            receipt = call(f"{api}/api/receipts/{receipt['id']}")
        if receipt["status"] == "Failed":
            raise SystemExit(f"{name}: {receipt['error']}")
        keep = ("merchant", "date", "total", "vatAmount", "vatableSales", "vatExemptSales", "discount", "serviceCharge", "category",
                "flags", "extractionSeconds", "confidence", "fieldConfidence")
        answers[name] = {k: receipt[k] for k in keep}
        print(f"{name}: {receipt['status']} in {receipt['extractionSeconds']}s")
    ANSWERS.parent.mkdir(parents=True, exist_ok=True)
    ANSWERS.write_text(json.dumps(answers, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Saved to {ANSWERS.relative_to(ROOT)}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Synthetic receipts for the demo site.")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("render", help="Draw the receipts")
    read = sub.add_parser("read", help="Have a running app read them")
    read.add_argument("--api", default="http://localhost:5080")
    args = parser.parse_args()
    render_all() if args.command == "render" else read_all(args.api.rstrip("/"))
