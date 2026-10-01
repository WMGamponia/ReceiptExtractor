"""calibrate_confidence.py's suggestion: judged per receipt, because the gate sends whole receipts to review."""

import unittest

from calibrate_confidence import suggest


def receipt(overall, wrong=(), fields=None, checks_flag=False):
    return {"name": "r", "overall": overall, "fields": fields or {}, "wrong": list(wrong), "checks_flag": checks_flag}


class SuggestTests(unittest.TestCase):
    def test_a_receipt_counts_once_its_lowest_field_is_under_the_threshold(self):
        # The misread date scores 0.96, but the receipt's total scores 0.90: at 0.95 it already goes to review.
        data = [receipt(0.90, wrong=["date"], fields={"date": 0.96, "total": 0.90}), receipt(0.99), receipt(0.98)]
        self.assertEqual(suggest(data, [0.95, 0.97]), 0.95)

    def test_false_alarms_count_against_a_threshold(self):
        data = [receipt(0.90, wrong=["date"], fields={"date": 0.90})] + [receipt(0.96)] * 4 + [receipt(0.99)]
        self.assertEqual(suggest(data, [0.95, 0.97]), 0.95)

    def test_misreads_the_checks_already_flag_add_nothing(self):
        data = [receipt(0.5, wrong=["total"], fields={"total": 0.5}, checks_flag=True), receipt(0.99)]
        self.assertIsNone(suggest(data, [0.95]))


if __name__ == "__main__":
    unittest.main()
