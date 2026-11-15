"""Minimal OpenAI Images client. The API key is never stored in the repo: it is read from
OPENAI_API_KEY, or injected by the sandbox network proxy (then OPENAI_API_KEY may be a placeholder)."""
from __future__ import annotations

import base64
import os
import time

import requests

API = os.environ.get("OPENAI_BASE_URL", "https://api.openai.com/v1")
RETRYABLE = {429, 500, 502, 503, 504}


class ApiError(Exception):
    def __init__(self, status: int, body: str, billed_possible: bool):
        super().__init__(f"HTTP {status}: {body[:500]}")
        self.status = status
        self.billed_possible = billed_possible


def _headers() -> dict:
    key = os.environ.get("OPENAI_API_KEY", "proxy-injected")
    return {"Authorization": f"Bearer {key}", "Content-Type": "application/json"}


def list_models() -> list[str]:
    r = requests.get(f"{API}/models", headers=_headers(), timeout=30)
    r.raise_for_status()
    return sorted(m["id"] for m in r.json()["data"])


def generate(prompt: str, model: str, size: str, quality: str, background: str, output_format: str,
             max_attempts: int = 2) -> tuple[bytes, dict | None, dict]:
    """Returns (image_bytes, usage, meta). Retries only on transient errors, bounded."""
    body = {"model": model, "prompt": prompt, "size": size, "quality": quality, "n": 1,
            "output_format": output_format}
    if background:
        body["background"] = background
    last: Exception | None = None
    for attempt in range(1, max_attempts + 1):
        try:
            r = requests.post(f"{API}/images/generations", headers=_headers(), json=body, timeout=300)
        except requests.RequestException as exc:  # timeout: generation may have been billed
            last = ApiError(0, str(exc), billed_possible=True)
            time.sleep(5 * attempt)
            continue
        if r.status_code == 200:
            data = r.json()
            img = base64.b64decode(data["data"][0]["b64_json"])
            meta = {"created": data.get("created"), "revised_prompt": data["data"][0].get("revised_prompt"),
                    "attempts": attempt, "request": {k: v for k, v in body.items() if k != "prompt"}}
            return img, data.get("usage"), meta
        if r.status_code in RETRYABLE and attempt < max_attempts:
            last = ApiError(r.status_code, r.text, billed_possible=False)
            time.sleep(10 * attempt)
            continue
        raise ApiError(r.status_code, r.text, billed_possible=False)
    assert last is not None
    raise last


def edit(prompt: str, reference_paths: list, model: str, size: str, quality: str, background: str,
         max_attempts: int = 2) -> tuple[bytes, dict | None, dict]:
    """Image edit / reference-guided generation (keeps a family of UI assets visually consistent)."""
    data = {"model": model, "prompt": prompt, "size": size, "quality": quality, "n": "1",
            "output_format": "png", "input_fidelity": "high"}
    if background:
        data["background"] = background
    headers = {"Authorization": _headers()["Authorization"]}
    last: Exception | None = None
    for attempt in range(1, max_attempts + 1):
        files = [("image[]", (p.name, p.read_bytes(), "image/png")) for p in reference_paths]
        try:
            r = requests.post(f"{API}/images/edits", headers=headers, data=data, files=files, timeout=300)
        except requests.RequestException as exc:
            last = ApiError(0, str(exc), billed_possible=True)
            time.sleep(5 * attempt)
            continue
        if r.status_code == 200:
            d = r.json()
            img = base64.b64decode(d["data"][0]["b64_json"])
            return img, d.get("usage"), {"attempts": attempt, "request": {k: v for k, v in data.items() if k != "prompt"},
                                         "references": [p.name for p in reference_paths]}
        if r.status_code in RETRYABLE and attempt < max_attempts:
            last = ApiError(r.status_code, r.text, billed_possible=False)
            time.sleep(10 * attempt)
            continue
        raise ApiError(r.status_code, r.text, billed_possible=False)
    assert last is not None
    raise last
