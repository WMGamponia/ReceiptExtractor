"""try_vision.upright() turns sideways phone photos upright, like PhotoOrientation.cs (same fixtures as its tests).
Skipped in CI, which doesn't install Pillow."""

import io
import unittest
from pathlib import Path

from try_vision import upright

FIXTURES = Path(__file__).resolve().parent.parent / "tests" / "ReceiptExtractor.Tests" / "Fixtures"

try:
    from PIL import Image
except ImportError:
    Image = None


@unittest.skipIf(Image is None, "needs Pillow")
class UprightTests(unittest.TestCase):
    def test_turns_a_sideways_photo_upright(self):
        with Image.open(io.BytesIO(upright(FIXTURES / "sideways-exif6.jpg"))) as im:
            self.assertEqual(im.size, (20, 40))
            self.assertLess(im.convert("L").getpixel((10, 5)), 50)     # top is black
            self.assertGreater(im.convert("L").getpixel((10, 35)), 200)  # bottom is white

    def test_leaves_an_upright_photo_as_it_is(self):
        path = FIXTURES / "upright.jpg"
        self.assertEqual(upright(path), path.read_bytes())


if __name__ == "__main__":
    unittest.main()
