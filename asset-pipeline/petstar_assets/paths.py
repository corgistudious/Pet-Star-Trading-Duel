from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PIPELINE = ROOT / "asset-pipeline"
SEED = ROOT / "database" / "seed"
MANIFESTS = ROOT / "asset-manifests"
GENERATED = ROOT / "assets-source" / "generated"
LEDGER = MANIFESTS / "cost-ledger.jsonl"
MANIFEST = MANIFESTS / "asset-manifest.json"
LOCK = MANIFESTS / ".pipeline.lock"
