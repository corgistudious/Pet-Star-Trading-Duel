using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;

namespace PetStar.EditorTools
{
    /// <summary>
    /// Fully automatic project setup + builds. Runs unattended (batch mode, Unity Build Automation pre-export,
    /// menu): nothing depends on hand-authored scenes, prefabs or Editor clicks.
    ///  - Bootstrap scene (the App builds all UI from code at runtime) and Scenes-in-Build
    ///  - sprite importers (incl. the duel board sprites), optional currency Sprite Atlas
    ///  - Addressables: groups "Board" (duel board sprites) and "Art" (pets, currency, packs, backgrounds)
    ///  - TextMeshPro Essential Resources + Be Vietnam Pro font assets (full Vietnamese coverage)
    ///  - Player settings incl. WebGL (template, compression with decompression fallback, ASTC for iPhone)
    /// WebGL is the current target; Android/Windows methods are kept but not used yet.
    /// </summary>
    public static class PetStarBuild
    {
        public const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        public const string BoardGroup = "Board";
        public const string ArtGroup = "Art";
        public const string WebGLTemplate = "PROJECT:PetStar";

        /// <summary>Duel board sprites (slot-free art; the 3 front + 2 back slots are drawn by code) and their addresses.</summary>
        public static readonly (string path, string address)[] BoardSprites =
        {
            ("Assets/Art/UI/Backgrounds/bg_duel_board_portrait.png", "ui/bg_duel_board_portrait"),
            ("Assets/Art/UI/Backgrounds/bg_duel_board_landscape.png", "ui/bg_duel_board_landscape"),
        };

        public static void Log(string msg) => Debug.Log("[PetStar] " + msg);
        public static void Warn(string msg) => Debug.LogWarning("[PetStar] " + msg);

        [MenuItem("Pet Star/1. Setup Project")]
        public static void SetupMenu() => Setup(EditorUserBuildSettings.activeBuildTarget);

        public static void Setup(BuildTarget target)
        {
            Log($"setup for {target} (Unity {Application.unityVersion}, project {Path.GetDirectoryName(Application.dataPath)})");
            ConfigurePlayer();
            if (target == BuildTarget.WebGL) ConfigureWebGL();
            ImportTmpEssentials();
            CopyBrandLogo();
            ImportArt();
            CreateFonts();
            TryCreateCurrencyAtlas();
            ConfigureAddressables();
            EnsureBootstrapScene();
            IncludeShader("PetStar/UI/CardFinish");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log("project setup complete");
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Pet Star Studio";
            PlayerSettings.productName = "Pet Star: Trading Duel";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.petstar.tradingduel");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.petstar.tradingduel");
            PlayerSettings.bundleVersion = Environment.GetEnvironmentVariable("PETSTAR_VERSION") ?? "0.1.0";
            PlayerSettings.Android.bundleVersionCode = int.TryParse(Environment.GetEnvironmentVariable("PETSTAR_BUILD_NUMBER"), out var n) ? n : 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // portrait outside duels; the game switches orientation per screen on mobile (Navigator)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540; PlayerSettings.defaultScreenHeight = 960; // portrait window outside duels
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly; // production uses HTTPS only
        }

        /// <summary>WebGL for iPhone Safari first: ASTC textures, Brotli with decompression fallback (works on any static
        /// host without special headers), cached data, conservative memory cap, custom black full-viewport template.</summary>
        private static void ConfigureWebGL()
        {
            PlayerSettings.WebGL.template = WebGLTemplate;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            // members that differ between Unity versions are set by name (no compile dependency)
            SetStatic(typeof(PlayerSettings.WebGL), "maximumMemorySize", 1024);   // MB; iOS Safari is memory constrained
            SetEnumStatic(typeof(EditorUserBuildSettings), "webGLBuildSubtarget", "ASTC");
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal); // + Assets/link.xml
            Log("WebGL player settings applied (template PetStar, Brotli + fallback, ASTC, 1 GB max memory)");
        }

        private static void SetStatic(Type t, string prop, object value)
        {
            var p = t.GetProperty(prop, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p == null || !p.CanWrite) { Warn($"{t.Name}.{prop} not available in this Unity version (skipped)"); return; }
            p.SetValue(null, Convert.ChangeType(value, p.PropertyType));
        }

        private static void SetEnumStatic(Type t, string prop, string enumName)
        {
            var p = t.GetProperty(prop, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p == null || !p.CanWrite || !p.PropertyType.IsEnum || !Enum.GetNames(p.PropertyType).Contains(enumName))
            { Warn($"{t.Name}.{prop}={enumName} not available in this Unity version (skipped)"); return; }
            p.SetValue(null, Enum.Parse(p.PropertyType, enumName));
        }

        /// <summary>TextMeshPro needs its Essential Resources (shaders, TMP Settings). AssetDatabase.ImportPackage is
        /// asynchronous, which is unsafe in Build Automation (the player may build before it finishes), so the
        /// .unitypackage (a gzipped tar) is extracted directly into Assets/ and imported synchronously.</summary>
        public static void ImportTmpEssentials()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return;
            foreach (var pkg in new[] { "com.unity.ugui", "com.unity.textmeshpro" })
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + pkg);
                if (info == null) continue;
                var file = Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
                if (!File.Exists(file)) continue;
                int n = UnityPackageExtractor.Extract(file);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Log($"extracted TMP Essential Resources from {pkg} ({n} files)");
                return;
            }
            Warn("TMP Essential Resources package not found; text will use fallbacks");
        }

        /// <summary>Official logo source (uploaded by the owner, never regenerated) -> Unity sprite "ui/brand_logo".</summary>
        public static readonly string[] BrandLogoSources =
        {
            "../assets-source/brand/petstar-logo-original.png", "../assets-source/brand/petstar-logo-original.jpg",
            "../assets-source/brand/petstar-logo-original.jpeg", "../assets-source/brand/petstar-logo-original.webp",
        };
        public const string BrandFolder = "Assets/Art/UI/Brand";

        /// <summary>Copies the original logo file byte for byte (no re-encode, no crop) so Unity imports the exact art.</summary>
        public static void CopyBrandLogo()
        {
            var src = BrandLogoSources.FirstOrDefault(File.Exists);
            if (src == null) { Warn("official logo not found in assets-source/brand (petstar-logo-original.png/.jpg); screens use the text title"); return; }
            Directory.CreateDirectory(BrandFolder);
            var ext = Path.GetExtension(src).ToLowerInvariant();
            if (ext == ".webp") { Warn("Unity cannot import .webp; upload the logo as PNG or JPG"); return; }
            foreach (var old in Directory.GetFiles(BrandFolder, "brand_logo.*")) if (!old.EndsWith(".meta") && Path.GetExtension(old) != ext) AssetDatabase.DeleteAsset(old);
            var dst = BrandFolder + "/brand_logo" + ext;
            File.Copy(src, dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
            Log("official logo copied: " + src + " -> " + dst);
        }

        private static void ImportArt()
        {
            var board = BoardSprites.Select(b => b.path).ToHashSet();
            int changed = 0;
            // batch the ~220 reimports into one import pass (much faster on build machines)
            AssetDatabase.StartAssetEditing();
            try
            {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                bool bg = path.Contains("/Backgrounds/");
                int max = bg ? 2048 : 1024;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = !bg;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = max;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                if (board.Contains(path)) ti.filterMode = FilterMode.Bilinear;
                foreach (var platform in new[] { "Android", "WebGL" })
                    ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = platform, overridden = true, maxTextureSize = max, format = TextureImporterFormat.ASTC_6x6 });
                ti.SaveAndReimport();
                changed++;
            }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Log($"sprite importers configured: {changed} textures");
        }

        private static void CreateFonts()
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null) { Warn("TMP shaders missing; font assets not created"); return; }
            Directory.CreateDirectory("Assets/Resources/Fonts");
            foreach (var w in new[] { "Regular", "Bold", "ExtraBold" })
            {
                var target = $"Assets/Resources/Fonts/BeVietnamPro-{w} SDF.asset";
                if (File.Exists(target)) continue;
                var font = AssetDatabase.LoadAssetAtPath<Font>($"Assets/Fonts/BeVietnamPro-{w}.ttf");
                if (font == null) { Debug.LogError("[PetStar] missing font " + w); continue; }
                // dynamic atlas: glyphs (incl. Vietnamese diacritics) are added on demand at runtime
                var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                if (fa == null) { Warn("could not create font asset " + w); continue; }
                AssetDatabase.CreateAsset(fa, target);
                fa.atlasTextures[0].name = fa.name + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = fa.name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
        }

        /// <summary>Optional optimisation; never fails the build.</summary>
        private static void TryCreateCurrencyAtlas()
        {
            const string path = "Assets/Art/UI/CurrencyAtlas.spriteatlasv2";
            if (File.Exists(path)) return;
            try
            {
                EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
                var atlas = new SpriteAtlasAsset();
                atlas.Add(new[] { AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Art/UI/Currency") });
                SpriteAtlasAsset.Save(atlas, path);
                AssetDatabase.ImportAsset(path);
            }
            catch (Exception e) { Warn("currency sprite atlas skipped: " + e.Message); }
        }

        public static AddressableAssetSettings ConfigureAddressables()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            AddressableAssetGroup Group(string name)
            {
                var g = settings.FindGroup(name) ?? settings.CreateGroup(name, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                var schema = g.GetSchema<BundledAssetGroupSchema>() ?? g.AddSchema<BundledAssetGroupSchema>();
                // one bundle per sprite: loads only what a screen needs and keeps every file far below hosting limits
                schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
                schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
                schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
                schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
                return g;
            }
            var art = Group(ArtGroup);
            var boardGroup = Group(BoardGroup);
            var boardPaths = BoardSprites.Select(b => b.path).ToHashSet();

            void Mark(string folder, string prefix)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (boardPaths.Contains(p)) continue; // board sprites live in their own group
                    var e = settings.CreateOrMoveEntry(guid, art, false, false);
                    e.address = prefix + Path.GetFileNameWithoutExtension(p);
                }
            }
            Mark("Assets/Art/Pets", "pet/");
            Mark("Assets/Art/UI/Currency", "ui/");
            Mark("Assets/Art/UI/Packs", "ui/");
            Mark("Assets/Art/UI/Backgrounds", "ui/");
            if (Directory.Exists(BrandFolder)) Mark(BrandFolder, "ui/"); // ui/brand_logo

            foreach (var (path, address) in BoardSprites)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) { Debug.LogError("[PetStar] board sprite missing: " + path); continue; }
                var e = settings.CreateOrMoveEntry(guid, boardGroup, false, false);
                e.address = address;
                e.SetLabel("board", true, true, false);
            }
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            Log($"Addressables: {art.entries.Count} art entries, {boardGroup.entries.Count} board entries");
            return settings;
        }

        public static void EnsureBootstrapScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                Log("created " + ScenePath);
            }
            // the only scene: App boots itself after the first scene loads (RuntimeInitializeOnLoadMethod)
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void IncludeShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) { Warn("shader not found: " + name); return; }
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gs == null) { Warn("GraphicsSettings.asset not loadable; shader not force-included"); return; }
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
        }

        /// <summary>Selects the client environment (PETSTAR_ENV, default development) and optionally overrides its API
        /// origin (PETSTAR_API_BASE, e.g. https://api.your-domain). Both come from Build Automation environment variables.</summary>
        public static void SetEnvironment()
        {
            var env = Environment.GetEnvironmentVariable("PETSTAR_ENV")?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(env)) env = "development";
            var apiBase = Environment.GetEnvironmentVariable("PETSTAR_API_BASE");
            const string cfg = "Assets/Resources/Config/client-config.json";
            var json = JObject.Parse(File.ReadAllText(cfg));
            json["environment"] = env;
            // only a non-empty PETSTAR_API_BASE changes the API; nothing is invented when it is missing
            if (!string.IsNullOrWhiteSpace(apiBase))
            {
                apiBase = apiBase.Trim();
                var envs = (JObject?)json["environments"] ?? new JObject();
                var section = (JObject?)envs[env] ?? new JObject();
                section["apiBaseUrl"] = apiBase.TrimEnd('/');
                envs[env] = section;
                json["environments"] = envs;
            }
            File.WriteAllText(cfg, json.ToString());
            AssetDatabase.ImportAsset(cfg);
            Log($"client environment: {env}{(string.IsNullOrWhiteSpace(apiBase) ? "" : " (API " + apiBase + ")")}");
        }

        public static void BuildAddressables()
        {
            AddressableAssetSettings.CleanPlayerContent();
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (!string.IsNullOrEmpty(result.Error)) throw new BuildFailedException("Addressables build failed: " + result.Error);
            Log($"Addressables content built in {result.Duration:0.0}s");
        }

        // ------------------------------------------------------------------ builds

        [MenuItem("Pet Star/Build WebGL")]
        public static void BuildWebGL()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            Setup(BuildTarget.WebGL); SetEnvironment();
            ProjectValidator.RunOrThrow(BuildTarget.WebGL);
            BuildAddressables();
            Run(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, target = BuildTarget.WebGL,
                locationPathName = "Builds/WebGL/PetStar",
                options = Environment.GetEnvironmentVariable("PETSTAR_ENV") == "production" ? BuildOptions.None : BuildOptions.Development,
            });
        }

        [MenuItem("Pet Star/Build Android APK (test, deferred)")]
        public static void BuildAndroidApk() => BuildAndroid(false);

        [MenuItem("Pet Star/Build Android AAB (deferred)")]
        public static void BuildAndroidAab() => BuildAndroid(true);

        private static void BuildAndroid(bool aab)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Setup(BuildTarget.Android); SetEnvironment(); BuildAddressables();
            EditorUserBuildSettings.buildAppBundle = aab;
            // Signing: keystore only from CI secrets (never committed). Without it the APK is debug-signed.
            var ks = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
            if (!string.IsNullOrEmpty(ks) && File.Exists(ks))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks;
                PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
                PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_NAME");
                PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS");
            }
            else if (aab) throw new BuildFailedException("AAB release build requires ANDROID_KEYSTORE_PATH and passwords (CI secrets)");
            Run(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, target = BuildTarget.Android,
                locationPathName = $"Builds/Android/PetStar-{PlayerSettings.bundleVersion}.{(aab ? "aab" : "apk")}",
                options = aab ? BuildOptions.None : BuildOptions.Development,
            });
        }

        [MenuItem("Pet Star/Build Windows x64 (deferred)")]
        public static void BuildWindows()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            Setup(BuildTarget.StandaloneWindows64); SetEnvironment(); BuildAddressables();
            Run(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, target = BuildTarget.StandaloneWindows64,
                locationPathName = $"Builds/Windows/PetStar-{PlayerSettings.bundleVersion}/PetStar.exe",
            });
        }

        private static void Run(BuildPlayerOptions o)
        {
            var report = BuildPipeline.BuildPlayer(o);
            Log($"build {report.summary.result}: {o.locationPathName} ({report.summary.totalSize / 1048576f:0.0} MB, {report.summary.totalErrors} errors)");
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Build failed: " + report.summary.result);
        }
    }

    /// <summary>Minimal .unitypackage reader: entries "{guid}/pathname", "{guid}/asset", "{guid}/asset.meta" inside a
    /// gzipped tar. Only paths under Assets/ are written (no path traversal).</summary>
    public static class UnityPackageExtractor
    {
        public static int Extract(string packagePath)
        {
            var entries = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, byte[]>>();
            using (var fs = File.OpenRead(packagePath))
            using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
            {
                var header = new byte[512];
                string? longName = null;
                while (ReadFull(gz, header) == 512 && header[0] != 0)
                {
                    string name = longName ?? Field(header, 0, 100);
                    longName = null;
                    var prefix = Field(header, 345, 155);
                    if (prefix.Length > 0 && !name.Contains("/")) name = prefix + "/" + name;
                    long size = Convert.ToInt64(Field(header, 124, 12).Trim() is { Length: > 0 } o ? o : "0", 8);
                    char type = (char)header[156];
                    var data = new byte[size];
                    ReadFull(gz, data);
                    var pad = (int)((512 - size % 512) % 512);
                    if (pad > 0) ReadFull(gz, new byte[pad]);
                    if (type == 'L') { longName = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0'); continue; }
                    if (type != '0' && type != '\0') continue; // directories / other entries
                    var parts = name.TrimStart('.', '/').Split('/');
                    if (parts.Length != 2) continue;
                    if (!entries.TryGetValue(parts[0], out var e)) entries[parts[0]] = e = new System.Collections.Generic.Dictionary<string, byte[]>();
                    e[parts[1]] = data;
                }
            }
            int written = 0;
            foreach (var e in entries.Values)
            {
                if (!e.TryGetValue("pathname", out var pn)) continue;
                var path = System.Text.Encoding.UTF8.GetString(pn).Split('\n')[0].Trim().Replace('\\', '/');
                if (!path.StartsWith("Assets/") || path.Contains("..")) continue;
                if (e.TryGetValue("asset", out var asset))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, asset); written++;
                }
                else Directory.CreateDirectory(path);
                if (e.TryGetValue("asset.meta", out var meta)) File.WriteAllBytes(path + ".meta", meta);
            }
            return written;
        }

        private static string Field(byte[] h, int offset, int len)
        {
            int end = offset;
            while (end < offset + len && h[end] != 0) end++;
            return System.Text.Encoding.ASCII.GetString(h, offset, end - offset);
        }

        private static int ReadFull(Stream s, byte[] buf)
        {
            int total = 0;
            while (total < buf.Length) { int n = s.Read(buf, total, buf.Length - total); if (n <= 0) break; total += n; }
            return total;
        }
    }
}
