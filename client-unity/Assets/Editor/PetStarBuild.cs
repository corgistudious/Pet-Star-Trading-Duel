using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PetStar.EditorTools
{
    /// <summary>
    /// One-click / CI project setup and builds. Nothing here depends on hand-authored scenes or prefabs:
    ///  - creates the Bootstrap scene (the App creates all UI from code at runtime)
    ///  - configures sprite importers, the currency Sprite Atlas and Addressables groups/addresses
    ///  - creates TextMeshPro font assets (Be Vietnam Pro, full Vietnamese coverage)
    ///  - builds Android APK / AAB and Windows x64
    /// CI usage: Unity -batchmode -quit -projectPath client-unity -executeMethod PetStar.EditorTools.PetStarBuild.BuildAndroidApk
    /// </summary>
    public static class PetStarBuild
    {
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";

        [MenuItem("Pet Star/1. Setup Project")]
        public static void Setup()
        {
            ConfigurePlayer();
            ImportArt();
            CreateFonts();
            CreateCurrencyAtlas();
            ConfigureAddressables();
            CreateBootstrapScene();
            IncludeShader("PetStar/UI/CardFinish");
            AssetDatabase.SaveAssets();
            Debug.Log("[PetStar] project setup complete");
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Pet Star Studio";
            PlayerSettings.productName = "Pet Star: Trading Duel";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.petstar.tradingduel");
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.petstar.tradingduel");
            PlayerSettings.bundleVersion = Environment.GetEnvironmentVariable("PETSTAR_VERSION") ?? "0.1.0";
            PlayerSettings.Android.bundleVersionCode = int.TryParse(Environment.GetEnvironmentVariable("PETSTAR_BUILD_NUMBER"), out var n) ? n : 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto; // latest installed SDK (Play requires a recent target API)
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly; // production uses HTTPS only
        }

        private static void ImportArt()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                bool bg = path.Contains("/Backgrounds/");
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = !bg;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = bg ? 2048 : 1024;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true, maxTextureSize = bg ? 2048 : 1024, format = TextureImporterFormat.ASTC_6x6 });
                ti.SaveAndReimport();
            }
        }

        private static void CreateFonts()
        {
            Directory.CreateDirectory("Assets/Resources/Fonts");
            foreach (var w in new[] { "Regular", "Bold", "ExtraBold" })
            {
                var target = $"Assets/Resources/Fonts/BeVietnamPro-{w} SDF.asset";
                if (File.Exists(target)) continue;
                var font = AssetDatabase.LoadAssetAtPath<Font>($"Assets/Fonts/BeVietnamPro-{w}.ttf");
                if (font == null) { Debug.LogError("Missing font " + w); continue; }
                // dynamic atlas: glyphs (incl. Vietnamese diacritics) are added on demand
                var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                AssetDatabase.CreateAsset(fa, target);
                fa.atlasTextures[0].name = fa.name + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = fa.name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
        }

        private static void CreateCurrencyAtlas()
        {
            const string path = "Assets/Art/UI/CurrencyAtlas.spriteatlasv2";
            if (File.Exists(path)) return;
            var atlas = new SpriteAtlasAsset();
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Art/UI/Currency");
            atlas.Add(new[] { folder });
            SpriteAtlasAsset.Save(atlas, path);
            AssetDatabase.ImportAsset(path);
        }

        private static void ConfigureAddressables()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.FindGroup("Art") ?? settings.CreateGroup("Art", false, false, true, null,
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema),
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema));
            void Mark(string folder, string prefix)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    var e = settings.CreateOrMoveEntry(guid, group, false, false);
                    e.address = prefix + Path.GetFileNameWithoutExtension(p);
                }
            }
            Mark("Assets/Art/Pets", "pet/");
            Mark("Assets/Art/UI/Currency", "ui/");
            Mark("Assets/Art/UI/Packs", "ui/");
            Mark("Assets/Art/UI/Backgrounds", "ui/");
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        }

        private static void CreateBootstrapScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void IncludeShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) return;
            var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
        }

        private static void SetEnvironment()
        {
            var env = Environment.GetEnvironmentVariable("PETSTAR_ENV") ?? "development";
            const string cfg = "Assets/Resources/Config/client-config.json";
            var text = File.ReadAllText(cfg);
            text = System.Text.RegularExpressions.Regex.Replace(text, "\"environment\":\\s*\"[a-z]+\"", $"\"environment\": \"{env}\"");
            File.WriteAllText(cfg, text);
            AssetDatabase.ImportAsset(cfg);
        }

        private static void BuildAddressables()
        {
            AddressableAssetSettings.CleanPlayerContent();
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (!string.IsNullOrEmpty(result.Error)) throw new Exception("Addressables build failed: " + result.Error);
        }

        [MenuItem("Pet Star/Build Android APK (test)")]
        public static void BuildAndroidApk() => BuildAndroid(false);

        [MenuItem("Pet Star/Build Android AAB (Google Play)")]
        public static void BuildAndroidAab() => BuildAndroid(true);

        private static void BuildAndroid(bool aab)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Setup(); SetEnvironment(); BuildAddressables();
            EditorUserBuildSettings.buildAppBundle = aab;
            // Signing: keystore is provided by CI secrets (never committed). Without it, APK is debug-signed.
            var ks = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
            if (!string.IsNullOrEmpty(ks) && File.Exists(ks))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks;
                PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
                PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_NAME");
                PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS");
            }
            else if (aab) throw new Exception("AAB release build requires ANDROID_KEYSTORE_PATH and passwords (CI secrets)");
            var ver = PlayerSettings.bundleVersion;
            Run(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, target = BuildTarget.Android,
                locationPathName = $"Builds/Android/PetStar-{ver}.{(aab ? "aab" : "apk")}",
                options = aab ? BuildOptions.None : BuildOptions.Development,
            });
        }

        [MenuItem("Pet Star/Build Windows x64")]
        public static void BuildWindows()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            Setup(); SetEnvironment(); BuildAddressables();
            Run(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, target = BuildTarget.StandaloneWindows64,
                locationPathName = $"Builds/Windows/PetStar-{PlayerSettings.bundleVersion}/PetStar.exe",
            });
        }

        private static void Run(BuildPlayerOptions o)
        {
            var report = BuildPipeline.BuildPlayer(o);
            Debug.Log($"[PetStar] build {report.summary.result}: {o.locationPathName} ({report.summary.totalSize / 1048576f:0.0} MB, {report.summary.totalErrors} errors)");
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
        }
    }
}
