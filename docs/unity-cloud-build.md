# Unity WebGL via Unity Build Automation (no computer needed)

WebGL is the first Unity target. Android and Windows are deferred, and their build methods stay in the project
unused. Build Automation provides the Unity Editor license on its own build machines, so this flow needs **no
`UNITY_LICENSE`, no `UNITY_SERIAL` and no Android keystore**. GitHub Actions GameCI was removed.

## What the project does automatically

`PetStar.EditorTools.CloudBuild.PreExport` (`client-unity/Assets/Editor/CloudBuild.cs`) runs inside the cloud
Editor before the player is built. No Editor clicks or hand-made scenes or prefabs are needed. It:

1. **Logs the setup.** It writes the Unity version, project path, target and the `PETSTAR_*` variables to the
   build log.
2. **Runs `PetStarBuild.Setup(WebGL)`:**
   - Player settings, plus WebGL settings:
     - template `PetStar`: black, full viewport, safe area, rotate overlay;
     - Brotli compression with decompression fallback, which works on any static host;
     - ASTC textures for iPhone, data caching, a 1 GB memory cap, managed stripping `Minimal` with `link.xml`.
   - **TextMeshPro Essential Resources**, extracted synchronously from the installed package. This is safe in
     batch mode, unlike `AssetDatabase.ImportPackage`, which is asynchronous.
   - **Sprite importers** for all art, including the duel board sprites (Sprite, no mipmaps, clamp, ASTC).
   - **Font assets** for Be Vietnam Pro, with dynamic Vietnamese glyphs.
   - **Addressables:**
     - group **Board**: `ui/bg_duel_board_portrait` and `ui/bg_duel_board_landscape`, label `board`;
     - group **Art**: 200 × `pet/{formId}`, plus the currency, pack and background sprites;
     - one bundle per sprite (LZ4, local paths).
   - **Bootstrap scene:** `Assets/Scenes/Bootstrap.unity` is created and set as the only, and first, scene in
     Build Settings. The app builds all UI from code after it loads.
3. **Selects the client environment.** It uses `PETSTAR_ENV`, and optionally `PETSTAR_API_BASE`.
4. **Validates the project** (`ProjectValidator`). It stops the build with a readable list if anything is wrong:
   - Editor version and `client-unity` path;
   - first scene;
   - board sprites (file, importer, address, group);
   - **all 208 runtime addresses** (200 pets, currency, pack, IAP and background sprites);
   - TMP settings and fonts;
   - WebGL template, plugins and `link.xml`;
   - an HTTPS API for WebGL;
   - JSON data.
5. **Builds Addressables content.** Build Automation then builds the player.

## One-time setup in Unity Cloud (cloud.unity.com → DevOps → Build Automation)

1. Connect the GitHub repository `corgistudious/Pet-Star-Trading-Duel`.
2. **New build configuration** with these values:

   | Setting | Value |
   |---|---|
   | Platform | **WebGL** |
   | Branch | `claude/pet-star-trading-duel-luftdb` (later `main`) |
   | Project subfolder | **`client-unity`** |
   | Unity version | **6000.0.58f2**. If it isn't listed, pick the newest 6000.0.x (the validator logs the difference). |

   **Advanced settings:**

   | Setting | Value |
   |---|---|
   | Pre-export method name | **`PetStar.EditorTools.CloudBuild.PreExport`** |
   | Scene list | empty (uses the Build Settings that the pre-export sets) |
   | Addressables content build | off. The pre-export builds it; leaving it on only rebuilds it. |
   | Clean build | recommended for the first run |

   **Environment variables (optional):**

   | Variable | Value |
   |---|---|
   | `PETSTAR_ENV` | `development` / `staging` / `production` |
   | `PETSTAR_API_BASE` | `https://api.<your-domain>` |
   | `PETSTAR_VERSION` | e.g. `0.1.0` |

   Without `PETSTAR_API_BASE`, the build starts and shows the login screen but cannot reach a server.
3. Start the build. The log shows lines prefixed `[PetStar]` and `[PetStar][CHECK]`.

Build Automation minutes may be billed by Unity depending on your plan. Check the DevOps billing page.

## Opening the WebGL build from an iPhone

Build Automation produces a ZIP. The manual GitHub workflow **"Unity WebGL publish"** downloads the latest
successful build and publishes it to a Cloudflare Pages **preview** URL. No computer is needed. It requires:

- secret `UNITY_BUILD_API_AUTH`: a Unity Cloud service-account key `keyId:secretKey` with Build Automation access;
- variables `UNITY_ORG_ID`, `UNITY_PROJECT_ID`, `UNITY_BUILDTARGET_ID`;
- variable `CF_PAGES_UNITY_PROJECT`: a Cloudflare Pages project created once as Direct Upload.

Production publishing needs the `production` environment approval. For login to work there, also add the
published origin to the API's `WEB_ORIGIN_2`.

## Checks that run on every push (GitHub Actions, no Unity license)

- `tools/check_unity_assets.py`: every file the build needs exists. That covers ProjectVersion, packages, the shared
  package path, both board sprites, 200/200 pet art, icons, Editor scripts, template, plugins, `link.xml`, JSON,
  and the 25 MiB limit.
- `tools/unity-syntax-check`: Roslyn C# 9 parse of `Scripts`, `Editor` and the shared rules.
- `tools/unity-compile-check/run.sh`: **semantic compile** against real UnityEngine modules (2021.3 LTS),
  UnityEditor (2021.1), UnityEngine.UI (2020.3) and Newtonsoft 13, in 5 configurations:
  - WebGL;
  - WebGL without the Input System;
  - Android;
  - Windows;
  - Editor.

  The package APIs that aren't on NuGet (TextMeshPro, Addressables, Input System) are small stubs that follow the
  real signatures. Four Unity 6-only APIs are shims: `NamedBuildTarget`, `ManagedStrippingLevel.Minimal`,
  `insecureHttpOption`, and the `NamedBuildTarget` overloads.

## Known limits (honest)

- A real Unity Editor has **never** compiled or built this project; this environment cannot reach Unity's
  servers. The semantic check uses older reference DLLs and stubs, so a few Unity 6 or package-specific issues can
  still appear only in the first cloud build. Asset import, shader compilation, IL2CPP/WebAssembly linking and
  runtime behaviour can't be tested here either.
- The build has not been opened on an iPhone.
- No legendary-pack art exists. Pack opening shows the default look for that pack.
