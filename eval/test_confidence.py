"""The eval's copy of ConfidenceScorer.cs, checked against the same cases (tests/confidence-cases.json)."""

import json
import unittest
from pathlib import Path

from confidence import score

ROOT = Path(__file__).resolve().parent.parent


class ScorerTests(unittest.TestCase):
    def test_matches_the_shared_cases(self):
        cases = json.loads((ROOT / "tests" / "confidence-cases.json").read_text(encoding="utf-8"))["scorer"]
        for c in cases:
            with self.subTest(c["name"]):
                self.assertEqual(score(c["content"], c["tokens"]), {"overall": c["overall"], "fields": c["fields"]})

    def test_no_tokens(self):
        self.assertEqual(score('{"total":5}', None), {"overall": None, "fields": {}})


if __name__ == "__main__":
    unittest.main()
