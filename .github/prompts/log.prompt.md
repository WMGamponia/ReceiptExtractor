---
description: 'Add or review logging in the selected C# code'
agent: 'agent'
---

Add or fix logging in ${selection} (or ${file} if nothing is selected), following `.github/instructions/logging.instructions.md`.

- Add logs only where they help someone reading the console: lifecycle events, handled failures, unexpected crashes. Remove noise (entry/exit, per-iteration, duplicates of what ASP.NET Core already logs).
- Pick levels from the table in the instructions. Log each exception once, where it's handled.
- Use message templates with the existing property names (`ReceiptId`, `Status`, `Seconds`, `Count`, …); replace any `$"..."` or concatenated messages.
- Remove anything that logs receipt contents: field values, `ModelOutput`, hints, OCR text, request bodies.
- Don't add logging packages or telemetry.

Make the edits, then list what you changed and why in a few bullets.
