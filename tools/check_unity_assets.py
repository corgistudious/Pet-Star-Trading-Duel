#!/usr/bin/env python3
"""Unity-free pre-check of the files the Unity build needs (mirrors the file-level checks of
client-unity/Assets/Editor/ProjectValidator.cs so CI fails before a Unity Build Automation run would)."""
import json, pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
U = ROOT / "client-unity"
A = U / "Assets"
errors, warnings, ok = [], [], []

pv = U / "ProjectSettings" / "ProjectVersion.txt"
m = re.search(r"m_EditorVersion:\s*(\S+)", pv.read_text()) if pv.exists() else None
(ok if m else errors).append(f"ProjectVersion.txt: {m.group(1) if m else 'missing'}")

manifest = json.loads((U / "Packages" / "manifest.json").read_text())["dependencies"]
for pkg in ("com.unity.addressables", "com.unity.ugui", "com.unity.nuget.newtonsoft-json", "com.petstar.shared"):
    (ok if pkg in manifest else errors).append(f"package {pkg}: {manifest.get(pkg, 'MISSING')}")
shared = (U / "Packages" / manifest.get("com.petstar.shared", "").replace("file:", "")).resolve()
(ok if (shared / "package.json").exists() else errors).append(f"shared rules package at {shared}")

for name in ("bg_duel_board_portrait", "bg_duel_board_landscape", "bg_pack_opening"):
    f = A / "Art/UI/Backgrounds" / f"{name}.png"
    (ok if f.exists() else errors).append(f"background {f.relative_to(U)}")

pets = json.loads((A / "Resources/Data/pets.json").read_text())
forms = [f["id"] for fam in pets["families"] for f in fam["forms"]]
missing = [f for f in forms if not (A / "Art/Pets" / f"{f}.png").exists()]
(errors if missing else ok).append(f"pet art: {len(forms) - len(missing)}/{len(forms)}" + (f" missing {missing[:10]}" if missing else ""))

icons = ["ui_currency_rainbow_star", "ui_currency_mini_green_bean", "ui_pack_basic", "ui_pack_premium"]
icons += [p["assetId"] for p in json.loads((A / "Resources/Data/economy.json").read_text()).get("iapProducts", [])]
for i in sorted(set(icons)):
    found = list((A / "Art/UI").rglob(f"{i}.png"))
    (ok if found else errors).append(f"icon {i}")
if not list((A / "Art/UI").rglob("ui_pack_legendary.png")):
    warnings.append("no ui_pack_legendary art (pack opening uses the default look)")

for f in ("Editor/PetStarBuild.cs", "Editor/CloudBuild.cs", "Editor/ProjectValidator.cs", "Editor/PetStar.Editor.asmdef",
          "WebGLTemplates/PetStar/index.html", "Plugins/WebGL/PetStarWebSocket.jslib", "Plugins/WebGL/PetStarViewport.jslib", "link.xml"):
    (ok if (A / f).exists() else errors).append(f"Assets/{f}")

tpl = (A / "WebGLTemplates/PetStar/index.html").read_text()
for token in ("{{{ LOADER_FILENAME }}}", "{{{ DATA_FILENAME }}}", "{{{ FRAMEWORK_FILENAME }}}", "createUnityInstance", "PetStarViewport"):
    (ok if token in tpl else errors).append(f"template has {token}")

for f in list((A / "Resources").rglob("*.json")):
    try: json.loads(f.read_text())
    except Exception as e: errors.append(f"invalid JSON {f.relative_to(U)}: {e}")

big = [p for p in A.rglob("*") if p.is_file() and p.stat().st_size > 25 * 1024 * 1024]
(errors if big else ok).append("no source asset over 25 MiB" if not big else f"assets over 25 MiB: {big}")

for s in ok: print("OK   ", s)
for s in warnings: print("WARN ", s)
for s in errors: print("ERROR", s)
print(f"=> {len(errors)} error(s), {len(warnings)} warning(s)")
sys.exit(1 if errors else 0)
