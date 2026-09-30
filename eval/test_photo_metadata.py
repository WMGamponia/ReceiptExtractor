"""Dataset photos go into git and the Docker image, so none may carry a GPS location (photo_metadata.py)."""

import unittest
from pathlib import Path

from photo_metadata import DATASET_IMAGES, ORIENTATION, exif_tags, has_gps, strip

FIXTURES = Path(__file__).resolve().parent.parent / "tests" / "ReceiptExtractor.Tests" / "Fixtures"


class DatasetPhotoTests(unittest.TestCase):
    def test_no_dataset_photo_has_a_gps_location(self):
        photos = sorted(DATASET_IMAGES.glob("*.jp*g"))
        self.assertTrue(photos)
        for photo in photos:
            with self.subTest(photo.name):
                self.assertFalse(has_gps(photo.read_bytes()),
                                 f"run: python eval/photo_metadata.py strip {photo.relative_to(DATASET_IMAGES.parent.parent)}")


class StripTests(unittest.TestCase):
    def test_keeps_the_orientation_and_the_image_data(self):
        data = (FIXTURES / "sideways-exif6.jpg").read_bytes()
        stripped = strip(data)
        self.assertEqual(exif_tags(stripped), {ORIENTATION: 6})
        self.assertEqual(stripped[stripped.index(b"\xff\xda"):], data[data.index(b"\xff\xda"):])

    def test_drops_a_trailer_after_the_image(self):
        data = (FIXTURES / "upright.jpg").read_bytes()
        self.assertEqual(strip(data + b"SEFH...Image_UTC_Data...SEFT"), strip(data))
        self.assertTrue(strip(data).endswith(b"\xff\xd9"))

    def test_an_upright_photo_gets_no_exif(self):
        self.assertEqual(exif_tags(strip((FIXTURES / "upright.jpg").read_bytes())), {})


if __name__ == "__main__":
    unittest.main()
