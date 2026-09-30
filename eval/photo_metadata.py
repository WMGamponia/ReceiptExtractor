"""
Phone photos carry metadata: GPS location, phone model, time taken. Dataset photos go into git and the Docker
image, so they must not: this strips it (keeping only the orientation tag, which the app needs) without
re-compressing, so the pixels and the model's answers stay the same. No Pillow needed, so CI can run the check.

Run (from the project root):
    python eval/photo_metadata.py strip datasets/labels/<photo>.jpg
    python eval/photo_metadata.py check          -> lists dataset photos with a GPS location
"""

import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATASET_IMAGES = ROOT / "datasets" / "labels"

SOI, EOI, SOS = b"\xff\xd8", b"\xff\xd9", 0xDA
APP1, COM = 0xE1, 0xFE
EXIF_HEADER = b"Exif\x00\x00"
ORIENTATION, GPS_IFD = 0x0112, 0x8825


def segments(data: bytes):
    """(marker, payload) for each JPEG segment before the image data, then (SOS, rest of the file)."""
    if not data.startswith(SOI):
        raise ValueError("not a JPEG")
    i = 2
    while i < len(data):
        marker = data[i + 1]
        if marker == SOS:
            yield marker, data[i:]
            return
        length = struct.unpack(">H", data[i + 2:i + 4])[0]
        yield marker, data[i + 4:i + 2 + length]
        i += 2 + length


def ifd0_tags(exif: bytes) -> dict[int, int]:
    """Tag -> first value of IFD0 in an Exif payload (only short values are read correctly; enough here)."""
    tiff = exif[len(EXIF_HEADER):]
    endian = "<" if tiff[:2] == b"II" else ">"
    offset = struct.unpack(endian + "I", tiff[4:8])[0]
    count = struct.unpack(endian + "H", tiff[offset:offset + 2])[0]
    tags = {}
    for n in range(count):
        entry = tiff[offset + 2 + 12 * n:offset + 14 + 12 * n]
        tag, kind = struct.unpack(endian + "HH", entry[:4])
        tags[tag] = struct.unpack(endian + ("H" if kind == 3 else "I"), entry[8:10] if kind == 3 else entry[8:12])[0]
    return tags


def exif_tags(data: bytes) -> dict[int, int]:
    for marker, payload in segments(data):
        if marker == APP1 and payload.startswith(EXIF_HEADER):
            return ifd0_tags(payload)
    return {}


def has_gps(data: bytes) -> bool:
    return GPS_IFD in exif_tags(data)


def strip(data: bytes) -> bytes:
    """Drops every APPn (Exif, XMP, Samsung's extras...), comment and trailer; keeps the orientation tag."""
    orientation = exif_tags(data).get(ORIENTATION, 1)
    out = bytearray(SOI)
    if orientation != 1:
        # Big-endian TIFF with one IFD0 entry: Orientation (SHORT, count 1).
        tiff = b"MM\x00\x2a" + struct.pack(">I", 8) + struct.pack(">H", 1) \
            + struct.pack(">HHIHH", ORIENTATION, 3, 1, orientation, 0) + struct.pack(">I", 0)
        payload = EXIF_HEADER + tiff
        out += bytes([0xFF, APP1]) + struct.pack(">H", len(payload) + 2) + payload
    for marker, payload in segments(data):
        if marker == SOS:
            # Up to the end-of-image marker: Samsung appends a trailer after it (capture time, network country).
            out += payload[:payload.index(EOI) + 2]
        elif not (0xE0 <= marker <= 0xEF or marker == COM) or marker == 0xE0 and payload.startswith(b"JFIF\x00"):
            out += bytes([0xFF, marker]) + struct.pack(">H", len(payload) + 2) + payload
    return bytes(out)


def main() -> None:
    if sys.argv[1:2] == ["strip"]:
        for name in sys.argv[2:]:
            path = Path(name)
            path.write_bytes(strip(path.read_bytes()))
            print(f"stripped {path}")
    elif sys.argv[1:2] == ["check"]:
        bad = [p.name for p in sorted(DATASET_IMAGES.glob("*.jp*g")) if has_gps(p.read_bytes())]
        print("\n".join(f"has a GPS location: {n}" for n in bad) or "no GPS locations")
        sys.exit(1 if bad else 0)
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main()
