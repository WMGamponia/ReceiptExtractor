"""
Picks Ollama:MinConfidence: for each threshold, how many misread dates/amounts the confidence score catches, and how
many correctly read receipts it would send to review for nothing. Reads the committed eval/model_outputs/vision.

Run (from the project root, after run_eval.py --only vision):
    python eval/calibrate_confidence.py
"""

import json

from confidence import SCORED_FIELDS
from run_eval import MODEL_OUTPUTS, field_ok, load_dataset, vat_check

THRESHOLDS = [0.5, 0.6, 0.7, 0.8, 0.85, 0.9, 0.95, 0.97, 0.99]
MAX_FALSE_ALARMS = 0.20  # at most 1 in 5 correct receipts sent to review for nothing


def rows() -> list[dict]:
    out = []
    for image, label in load_dataset():
        path = MODEL_OUTPUTS / "vision" / f"{image.stem}.json"
        if not path.exists():
            continue
        record = json.loads(path.read_text(encoding="utf-8"))
        conf = record.get("confidence") or {}
        if conf.get("overall") is None:
            continue
        pred = record["prediction"]
        wrong = [f for f in SCORED_FIELDS if f in label and not field_ok(f, pred.get(f), label.get(f))]
        out.append({"name": image.stem, "fields": conf["fields"], "overall": conf["overall"], "wrong": wrong,
                    "checks_flag": vat_check(pred) is not None or pred.get("date") is None})
    return out


def main() -> None:
    data = rows()
    wrong_fields = [(r, f) for r in data for f in r["wrong"]]
    missed_by_checks = [(r, f) for r, f in wrong_fields if not r["checks_flag"]]
    all_right = [r for r in data if not r["wrong"]]
    print(f"{len(data)} receipts, {len(wrong_fields)} misread fields ({len(missed_by_checks)} not caught by the checks), "
          f"{len(all_right)} receipts fully right\n")

    print("| Threshold | Misreads caught | Missed by checks, caught | Right receipts flagged |")
    print("|---|---|---|---|")
    best = None
    for t in THRESHOLDS:
        caught = sum(r["fields"].get(f, 1) < t for r, f in wrong_fields)
        new = sum(r["fields"].get(f, 1) < t for r, f in missed_by_checks)
        false_alarms = sum(r["overall"] < t for r in all_right)
        fa_rate = false_alarms / len(all_right) if all_right else 0
        print(f"| {t:.2f} | {caught}/{len(wrong_fields)} | {new}/{len(missed_by_checks)} | {false_alarms}/{len(all_right)} |")
        if fa_rate <= MAX_FALSE_ALARMS and new > 0 and (best is None or new - false_alarms > best[1]):
            best = (t, new - false_alarms)

    print("\nMisread fields and their confidence:")
    for r, f in sorted(wrong_fields, key=lambda x: x[0]["fields"].get(x[1], 1)):
        print(f"  {r['fields'].get(f, 1):.3f}  {r['name']}  {f}{'' if r['checks_flag'] else '  (checks missed it)'}")

    print(f"\nSuggested MinConfidence: {best[0]}" if best else
          "\nNo threshold catches a misread the checks miss while flagging at most 20% of right receipts: "
          "the score doesn't separate right from wrong reads on this dataset. Stop and report.")


if __name__ == "__main__":
    main()
