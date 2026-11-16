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
# card system expansion: art for all 9 packs, upgrade materials and Divine crests
for i in ["ui_pack_basic", "ui_pack_premium", "ui_pack_transcendent", "ui_pack_epic", "ui_pack_legendary", "ui_pack_mythic",
          "ui_pack_divine_premium", "ui_pack_divine_limited", "ui_pack_divine_star",
          "ui_mat_pet_shard", "ui_mat_enhance_stone", "ui_mat_breakthrough_core", "ui_mat_awakening_crystal", "ui_mat_engraving_dust",
          "ui_mat_training_scroll", "ui_crest_premium", "ui_crest_limited", "ui_crest_star"]:
    (ok if list((A / "Art/UI").rglob(f"{i}.png")) else errors).append(f"card-system art {i}")

for f in ("Editor/PetStarBuild.cs", "Editor/CloudBuild.cs", "Editor/ProjectValidator.cs", "Editor/PetStar.Editor.asmdef",
          "WebGLTemplates/PetStar/index.html", "Plugins/WebGL/PetStarWebSocket.jslib", "Plugins/WebGL/PetStarViewport.jslib", "link.xml"):
    (ok if (A / f).exists() else errors).append(f"Assets/{f}")

tpl = (A / "WebGLTemplates/PetStar/index.html").read_text()
for token in ("{{{ LOADER_FILENAME }}}", "{{{ DATA_FILENAME }}}", "{{{ FRAMEWORK_FILENAME }}}", "createUnityInstance", "PetStarViewport"):
    (ok if token in tpl else errors).append(f"template has {token}")

# first scene committed (Build Automation reads the scene list; the pre-export also re-asserts it)
ebs = U / "ProjectSettings" / "EditorBuildSettings.asset"
scene_meta = A / "Scenes" / "Bootstrap.unity.meta"
if ebs.exists() and scene_meta.exists() and (A / "Scenes/Bootstrap.unity").exists():
    guid = re.search(r"guid:\s*([0-9a-f]{32})", scene_meta.read_text())
    good = guid and "path: Assets/Scenes/Bootstrap.unity" in ebs.read_text() and guid.group(1) in ebs.read_text()
    (ok if good else errors).append("first scene Assets/Scenes/Bootstrap.unity in EditorBuildSettings (guid matches)")
else:
    errors.append("Assets/Scenes/Bootstrap.unity(.meta) or ProjectSettings/EditorBuildSettings.asset missing")

# client environments: staging = same origin /api (petstar.pages.dev), production must be set explicitly
cfg = json.loads((A / "Resources/Config/client-config.json").read_text())["environments"]
for env, url in ((e, v.get("apiBaseUrl", "")) for e, v in cfg.items()):
    if ".example" in url or re.search(r"\d+\.\d+\.\d+\.\d+", url) and "127.0.0.1" not in url and "10.0.2.2" not in url:
        errors.append(f"client-config {env}: placeholder or raw IP API '{url}'")
    else:
        ok.append(f"client-config {env}: '{url or '(empty: set PETSTAR_API_BASE)'}'")

# official logo (uploaded by the owner, never generated)
brand = [ROOT / "assets-source/brand" / f"petstar-logo-original.{e}" for e in ("png", "jpg", "jpeg")]
found_logo = next((b for b in brand if b.exists()), None)
(ok if found_logo else warnings).append(f"official logo {found_logo.relative_to(ROOT)}" if found_logo else
    "official logo not uploaded yet (assets-source/brand/petstar-logo-original.png): text title and default icons are used")
for token in ("manifest.webmanifest", "apple-touch-icon", "og:image", "brand/logo-512.webp", "no-logo"):
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
