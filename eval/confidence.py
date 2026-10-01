"""
Confidence from token probabilities: the least likely token of each scored value.
Same rules as src/ReceiptExtractor.Api/Extraction/ConfidenceScorer.cs; both pass tests/confidence-cases.json.
"""

import math
import re

# Dates and amounts, in review-form order. Merchant and category are not scored.
SCORED_FIELDS = ["date", "total", "vat_amount", "vatable_sales", "vat_exempt_sales", "discount", "service_charge"]

_WHITESPACE = set(b" \n\r\t")


def _value_span(content: str, key: str) -> tuple[int, int] | None:
    """The value's byte range: inside the quotes for a string, up to the next , } or whitespace otherwise."""
    m = re.search(r'[{,]\s*"' + key + r'"\s*:\s*', content)
    if not m:
        return None
    start = m.end()
    if start < len(content) and content[start] == '"':
        start += 1
        end = content.find('"', start)
        if end < 0:
            return None
    else:
        end = start
        while end < len(content) and content[end] not in ',} \n\r\t':
            end += 1
    if end <= start:
        return None
    return len(content[:start].encode("utf-8")), len(content[:end].encode("utf-8"))


def score(content: str, tokens: list[dict] | None) -> dict:
    """tokens: [{"token", "logprob", "bytes"?}] as Ollama returns them. No or mismatched tokens -> no score."""
    none = {"overall": None, "fields": {}}
    if not tokens:
        return none
    # UTF-8 bytes: a token can be half of "ñ", which shifts every offset after it.
    pieces = [bytes(t["bytes"]) if t.get("bytes") is not None else t["token"].encode("utf-8") for t in tokens]
    if b"".join(pieces) != content.encode("utf-8"):
        return none

    starts, pos = [], 0
    for p in pieces:
        starts.append(pos)
        pos += len(p)

    fields = {}
    for name in SCORED_FIELDS:
        span = _value_span(content, name)
        if span is None:
            continue
        lo, hi = span
        logprobs = [t["logprob"] for t, p, s in zip(tokens, pieces, starts)
                    if s < hi and s + len(p) > lo and not set(p) <= _WHITESPACE]
        if logprobs:
            fields[name] = round(math.exp(min(logprobs)), 3)
    return {"overall": min(fields.values()), "fields": fields} if fields else none
