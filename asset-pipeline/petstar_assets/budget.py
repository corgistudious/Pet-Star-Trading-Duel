"""Budget guard. Every API call must reserve its worst-case cost first; a call that could push
spend above the category cap or the total cap is refused before any network request is made.

The ledger is append-only JSONL (asset-manifests/cost-ledger.jsonl). Entries:
  {"type":"reserve","id":...,"category":...,"amount":...}
  {"type":"settle","id":...,"category":...,"amount":...,"usage":{...}}   # actual cost
  {"type":"release","id":...}                                            # call never billed (rejected locally / 4xx)
Spend = sum(settled) + sum(open reservations).  Open reservations count fully, so a crash mid-call
can never let the pipeline overspend.
"""
from __future__ import annotations

import json
import time
import uuid
from dataclasses import dataclass

from .paths import LEDGER, PIPELINE


class BudgetExceeded(Exception):
    pass


def load_config() -> dict:
    return json.loads((PIPELINE / "budget.json").read_text())


def _entries() -> list[dict]:
    if not LEDGER.exists():
        return []
    return [json.loads(line) for line in LEDGER.read_text().splitlines() if line.strip()]


def _append(entry: dict) -> None:
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    entry = {"ts": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), **entry}
    with LEDGER.open("a") as f:
        f.write(json.dumps(entry) + "\n")


@dataclass
class SpendSummary:
    settled: dict
    reserved: dict

    def total(self, category: str | None = None) -> float:
        cats = [category] if category else set(self.settled) | set(self.reserved)
        return round(sum(self.settled.get(c, 0.0) + self.reserved.get(c, 0.0) for c in cats), 6)


def summary() -> SpendSummary:
    settled: dict[str, float] = {}
    open_res: dict[str, dict] = {}
    for e in _entries():
        if e["type"] == "reserve":
            open_res[e["id"]] = e
        elif e["type"] == "settle":
            open_res.pop(e["id"], None)
            settled[e["category"]] = settled.get(e["category"], 0.0) + e["amount"]
        elif e["type"] == "release":
            open_res.pop(e["id"], None)
    reserved: dict[str, float] = {}
    for e in open_res.values():
        reserved[e["category"]] = reserved.get(e["category"], 0.0) + e["amount"]
    return SpendSummary(settled, reserved)


def estimate(model: str, size: str, quality: str) -> float:
    cfg = load_config()
    m = cfg["models"].get(model)
    if not m:
        raise BudgetExceeded(f"No pricing configured for model {model!r}; refusing to call an unpriced model.")
    try:
        base = m["perImageEstimate"][size][quality]
    except KeyError as exc:
        raise BudgetExceeded(f"No price for {model} {size} {quality}") from exc
    # prompt text tokens are small (~300) but count them, plus a safety margin
    return round(base + cfg["safetyMarginPerCall"], 6)


def reserve(category: str, amount: float, note: str = "") -> str:
    cfg = load_config()
    cap = cfg["categories"].get(category)
    if cap is None:
        raise BudgetExceeded(f"Unknown budget category {category!r}")
    s = summary()
    if s.total(category) + amount > cap + 1e-9:
        raise BudgetExceeded(
            f"Category '{category}' would reach ${s.total(category) + amount:.4f} > cap ${cap:.2f}")
    if s.total() + amount > cfg["totalCap"] + 1e-9:
        raise BudgetExceeded(f"Total would reach ${s.total() + amount:.4f} > cap ${cfg['totalCap']:.2f}")
    rid = uuid.uuid4().hex
    _append({"type": "reserve", "id": rid, "category": category, "amount": round(amount, 6), "note": note})
    return rid


def actual_cost(model: str, usage: dict | None, fallback: float) -> float:
    """Compute billed cost from the API's usage block (token counts). Falls back to the estimate."""
    if not usage:
        return fallback
    rates = load_config()["models"][model]["tokenRatesPerMillion"]
    details = usage.get("input_tokens_details") or {}
    text_in = details.get("text_tokens", usage.get("input_tokens", 0))
    image_in = details.get("image_tokens", 0)
    out = usage.get("output_tokens", 0)
    return round((text_in * rates["textInput"] + image_in * rates["imageInput"] + out * rates["imageOutput"]) / 1e6, 6)


def settle(rid: str, category: str, amount: float, usage: dict | None) -> None:
    _append({"type": "settle", "id": rid, "category": category, "amount": round(amount, 6), "usage": usage})


def release(rid: str, reason: str) -> None:
    _append({"type": "release", "id": rid, "reason": reason})
