"""
Tests for the eval's scoring rules (standard library unittest, no extra installs).

Run (from the project root, with .venv active):
    python -m unittest discover -s eval -p "test_*.py"
"""

import json
import unittest
from pathlib import Path

from run_eval import field_ok, merchant_ok, vat_check

ROOT = Path(__file__).resolve().parent.parent


class VatCheckTests(unittest.TestCase):
    def test_matches_the_shared_cases(self):
        # Same cases as ReceiptValidator.VatFlags (C#) and vatWarnings (web): tests/vat-cases.json.
        cases = json.loads((ROOT / "tests" / "vat-cases.json").read_text(encoding="utf-8"))["cases"]
        for c in cases:
            with self.subTest(c["name"]):
                problem = vat_check({"total": c["total"], "vat_amount": c["vat"], "vatable_sales": c["vatable"],
                                     "vat_exempt_sales": c["exempt"], "discount": c["discount"],
                                     "service_charge": c["service_charge"]})
                self.assertEqual(c["problems"] > 0, problem is not None, problem)

    def test_no_total(self):
        self.assertEqual(vat_check({"total": None}), "no total")


class MerchantTests(unittest.TestCase):
    def test_ignores_case_punctuation_and_legal_suffixes(self):
        self.assertTrue(merchant_ok("Robinsons Supermarket Corp.", "ROBINSONS SUPERMARKET CORPORATION"))
        self.assertTrue(merchant_ok("DATA BLITZ", "DataBlitz"))

    def test_small_misreads_still_count(self):
        self.assertTrue(merchant_ok("DATABLITS", "DataBlitz"))

    def test_different_names_dont(self):
        # (The LAZADA Edifier and WAL TER-APM misses count as right now: see merchant_aliases.json.)
        self.assertFalse(merchant_ok("Shopee", "LAZADA"))
        self.assertFalse(merchant_ok("Mercury Drug", "Watsons"))

    def test_missing(self):
        self.assertFalse(merchant_ok(None, "DataBlitz"))
        self.assertTrue(merchant_ok(None, ""))


class FieldTests(unittest.TestCase):
    def test_money_within_a_centavo(self):
        self.assertTrue(field_ok("total", 10495.004, 10495.00))
        self.assertFalse(field_ok("total", 10495.01, 10495.00))

    def test_missing_vat_counts_as_zero_but_missing_total_does_not(self):
        self.assertTrue(field_ok("vat_amount", None, 0.0))
        self.assertFalse(field_ok("total", None, 0.0))

    def test_date_must_match_exactly(self):
        self.assertTrue(field_ok("date", "2025-12-20", "2025-12-20"))
        self.assertFalse(field_ok("date", "2026-11-01", "2026-01-11"))  # day and month swapped


if __name__ == "__main__":
    unittest.main()
