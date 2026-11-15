"""Pet Star asset pipeline CLI.

  python -m petstar_assets check                      # API connectivity, image models, budget state
  python -m petstar_assets status                     # manifest + spend summary
  python -m petstar_assets gen-pets --ids a,b --dry-run
  python -m petstar_assets gen-pets --ids a,b [--quality medium]
  python -m petstar_assets gen-test                   # one low-quality probe image
  python -m petstar_assets review <asset_id> approve|reject [--note ..]
"""
from __future__ import annotations

import argparse
import fcntl
import hashlib
import json
import sys
import time

from . import budget, openai_client, prompts, validate
from .paths import GENERATED, LOCK, MANIFEST, ROOT


def load_manifest() -> dict:
    if MANIFEST.exists():
        return json.loads(MANIFEST.read_text())
    return {"schema": 1, "assets": {}}


def save_manifest(m: dict) -> None:
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    tmp = MANIFEST.with_suffix(".tmp")
    tmp.write_text(json.dumps(m, indent=2, ensure_ascii=False) + "\n")
    tmp.replace(MANIFEST)


def job_key(prompt: str, model: str, size: str, quality: str, background: str) -> str:
    return hashlib.sha256("|".join([prompt, model, size, quality, background]).encode()).hexdigest()[:16]


def run_job(asset_id: str, category: str, prompt: str, out_rel: str, pet_id: str | None,
            model: str, size: str, quality: str, background: str, dry_run: bool, force_new: bool,
            reference: str | None = None) -> dict:
    manifest = load_manifest()
    key = job_key(prompt + (f"|ref:{reference}" if reference else ""), model, size, quality, background)
    existing = manifest["assets"].get(asset_id)
    if existing and existing.get("jobKey") == key and existing["status"] in ("generated", "approved") and not force_new:
        print(f"  skip {asset_id}: already {existing['status']} with identical job (no duplicate call)")
        return existing
    est = budget.estimate(model, size, quality) + (0.05 if reference else 0.0)  # reference image input tokens
    ref_paths = []
    if reference:
        ref = manifest["assets"].get(reference)
        if not ref or ref["status"] not in ("generated", "approved"):
            raise SystemExit(f"reference {reference} must be generated first")
        ref_paths = [ROOT / ref["filePath"]]
    if dry_run:
        print(f"  [dry-run] {asset_id}: would spend <= ${est:.3f} ({model} {size} {quality})")
        return {}
    rid = budget.reserve(category, est, note=asset_id)
    try:
        if reference:
            img, usage, meta = openai_client.edit(prompt, ref_paths, model, size, quality, background)
        else:
            img, usage, meta = openai_client.generate(prompt, model, size, quality, background, "png")
    except openai_client.ApiError as exc:
        if exc.billed_possible:
            budget.settle(rid, category, est, None)  # assume billed; never under-count
        else:
            budget.release(rid, str(exc)[:200])
        raise
    cost = budget.actual_cost(model, usage, est)
    budget.settle(rid, category, cost, usage)
    version = (existing or {}).get("version", 0) + 1
    out_path = ROOT / out_rel.replace("{v}", f"v{version}")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(img)
    v = validate.validate_png(img, size, expect_alpha=(background == "transparent"))
    entry = {
        "assetId": asset_id, "petId": pet_id, "category": category, "prompt": prompt, "model": model,
        "size": size, "quality": quality, "background": background, "dimensions": f"{v['width']}x{v['height']}",
        "sha256": v["sha256"], "bytes": v["bytes"], "version": version, "generationCost": cost,
        "usage": usage, "jobKey": key, "status": "generated", "validation": v,
        "filePath": str(out_path.relative_to(ROOT)),
        "provenance": {"provider": "OpenAI Images API", "generatedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                       "revisedPrompt": meta.get("revised_prompt"), "attempts": meta.get("attempts"),
                       "pipeline": "asset-pipeline/petstar_assets"},
        "reference": reference,
        "history": ((existing or {}).get("history", []) + ([{k: existing[k] for k in ("version", "sha256", "filePath", "status")}] if existing else [])),
    }
    manifest = load_manifest()
    manifest["assets"][asset_id] = entry
    save_manifest(manifest)
    flag = "OK" if v["passed"] else f"CHECK {v['issues']}"
    print(f"  {asset_id}: ${cost:.4f} {v['width']}x{v['height']} alpha={v['alphaCoverage']} -> {entry['filePath']} [{flag}]")
    return entry


def cmd_check(_args) -> None:
    models = openai_client.list_models()
    img_models = [m for m in models if "image" in m or "dall" in m]
    print("OpenAI API reachable. Image models:", ", ".join(img_models))
    cmd_status(None)


def cmd_status(_args) -> None:
    s = budget.summary()
    cfg = budget.load_config()
    for cat, cap in cfg["categories"].items():
        print(f"  budget {cat}: spent ${s.total(cat):.4f} / ${cap:.2f}")
    print(f"  budget total: ${s.total():.4f} / ${cfg['totalCap']:.2f}")
    m = load_manifest()
    by = {}
    for a in m["assets"].values():
        by[a["status"]] = by.get(a["status"], 0) + 1
    print("  assets:", by or "none")


def cmd_gen_pets(args) -> None:
    cfg = budget.load_config()["defaults"]
    forms = prompts.load_pets()
    wanted = set(args.ids.split(",")) if args.ids else {f["id"] for f in forms}
    sel = [f for f in forms if f["id"] in wanted]
    missing = wanted - {f["id"] for f in sel}
    if missing:
        sys.exit(f"unknown pet ids: {missing}")
    est_total = sum(budget.estimate(cfg["model"], cfg["size"], args.quality) for _ in sel)
    print(f"{len(sel)} pets, worst-case ${est_total:.3f}")
    for f in sel:
        run_job(f"art_{f['id']}", "pet", prompts.pet_prompt(f), f"assets-source/generated/pets/{f['id']}_{{v}}.png",
                f["id"], cfg["model"], cfg["size"], args.quality, cfg["background"], args.dry_run, args.force_new)
    cmd_status(None)


def cmd_gen_test(args) -> None:
    prompt = ("Premium HD 2D chibi fantasy illustration of a small round original star-shaped spirit creature, "
              "big glossy eyes, soft cel shading, isolated on a transparent background. No text.")
    run_job("probe_test", "ui", prompt, "assets-source/generated/probe/probe_{v}.png", None,
            "gpt-image-1.5", "1024x1024", "low", "transparent", args.dry_run, args.force_new)


def cmd_gen_ui(args) -> None:
    from .paths import PIPELINE
    spec = json.loads((PIPELINE / "ui_assets.json").read_text())
    cfg = budget.load_config()["defaults"]
    wanted = set(args.ids.split(",")) if args.ids else None
    for a in spec["assets"]:
        if wanted and a["id"] not in wanted:
            continue
        prompt = spec["style"] + "\n" + a["prompt"]
        run_job(a["id"], "ui", prompt, f"assets-source/generated/ui/{a['id']}_{{v}}.png", None, cfg["model"],
                a.get("size", "1024x1024"), a["quality"], a.get("background", "transparent"), args.dry_run, args.force_new, a.get("reference"))
    cmd_status(None)


def cmd_review(args) -> None:
    m = load_manifest()
    a = m["assets"][args.asset_id]
    a["status"] = "approved" if args.decision == "approve" else "rejected"
    a["review"] = {"decision": args.decision, "note": args.note, "at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    save_manifest(m)
    print(args.asset_id, "->", a["status"])


def main() -> None:
    p = argparse.ArgumentParser(prog="petstar_assets")
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("check").set_defaults(fn=cmd_check)
    sub.add_parser("status").set_defaults(fn=cmd_status)
    g = sub.add_parser("gen-pets")
    g.add_argument("--ids", default="")
    g.add_argument("--quality", default="medium", choices=["low", "medium", "high"])
    g.add_argument("--dry-run", action="store_true")
    g.add_argument("--force-new", action="store_true", help="generate a new version even if one exists")
    g.set_defaults(fn=cmd_gen_pets)
    t = sub.add_parser("gen-test")
    t.add_argument("--dry-run", action="store_true")
    t.add_argument("--force-new", action="store_true")
    t.set_defaults(fn=cmd_gen_test)
    u = sub.add_parser("gen-ui")
    u.add_argument("--ids", default="")
    u.add_argument("--dry-run", action="store_true")
    u.add_argument("--force-new", action="store_true")
    u.set_defaults(fn=cmd_gen_ui)
    r = sub.add_parser("review")
    r.add_argument("asset_id")
    r.add_argument("decision", choices=["approve", "reject"])
    r.add_argument("--note", default="")
    r.set_defaults(fn=cmd_review)
    args = p.parse_args()
    LOCK.parent.mkdir(parents=True, exist_ok=True)
    with LOCK.open("w") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            sys.exit("another pipeline run holds the lock")
        try:
            args.fn(args)
        except budget.BudgetExceeded as exc:
            sys.exit(f"BUDGET STOP: {exc}")
