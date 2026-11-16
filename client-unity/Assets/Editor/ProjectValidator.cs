using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Build;
using UnityEngine;

namespace PetStar.EditorTools
{
    /// <summary>
    /// Checks everything a headless build depends on and fails early with a readable list instead of a broken
    /// player: Unity version + project path, scenes, duel board sprites, every Addressables address the runtime
    /// code loads, fonts, WebGL template/plugins, client API address and data files.
    /// Report: Debug log lines "[PetStar][CHECK]" and Library/PetStar/validation-report.txt.
    /// Batch: -executeMethod PetStar.EditorTools.ProjectValidator.RunFromCommandLine (exit code 1 on errors).
    /// </summary>
    public static class ProjectValidator
    {
        public sealed class Report
        {
            public readonly List<string> Errors = new(), Warnings = new(), Info = new();
            public bool Failed => Errors.Count > 0;
            public void Error(string s) => Errors.Add(s);
            public void Warn(string s) => Warnings.Add(s);
            public void Ok(string s) => Info.Add(s);
            public override string ToString()
            {
                var sb = new StringBuilder();
                foreach (var s in Info) sb.AppendLine("OK    " + s);
                foreach (var s in Warnings) sb.AppendLine("WARN  " + s);
                foreach (var s in Errors) sb.AppendLine("ERROR " + s);
                sb.AppendLine($"=> {Errors.Count} error(s), {Warnings.Count} warning(s)");
                return sb.ToString();
            }
        }

        [MenuItem("Pet Star/2. Validate Project")]
        public static void Menu() => Debug.Log(Run(EditorUserBuildSettings.activeBuildTarget).ToString());

        public static void RunFromCommandLine()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            PetStarBuild.Setup(target);
            var r = Run(target);
            EditorApplication.Exit(r.Failed ? 1 : 0);
        }

        public static Report RunOrThrow(BuildTarget target)
        {
            var r = Run(target);
            if (r.Failed) throw new BuildFailedException("Pet Star project validation failed:\n" + string.Join("\n", r.Errors));
            return r;
        }

        public static Report Run(BuildTarget target)
        {
            var r = new Report();
            CheckEnvironment(r, target);
            CheckScenes(r);
            CheckBoardSprites(r);
            CheckAddresses(r);
            CheckFonts(r);
            if (target == BuildTarget.WebGL) CheckWebGL(r);
            CheckClientConfig(r, target);
            CheckData(r);
            var text = r.ToString();
            foreach (var line in text.Split('\n').Where(l => l.Length > 0))
            {
                if (line.StartsWith("ERROR")) Debug.LogError("[PetStar][CHECK] " + line);
                else if (line.StartsWith("WARN")) Debug.LogWarning("[PetStar][CHECK] " + line);
                else Debug.Log("[PetStar][CHECK] " + line);
            }
            Directory.CreateDirectory("Library/PetStar");
            File.WriteAllText("Library/PetStar/validation-report.txt", text);
            return r;
        }

        private static void CheckEnvironment(Report r, BuildTarget target)
        {
            var projectPath = Path.GetDirectoryName(Application.dataPath)!.Replace('\\', '/');
            r.Ok($"Unity {Application.unityVersion}, target {target}, project {projectPath}");
            var pv = "ProjectSettings/ProjectVersion.txt";
            if (File.Exists(pv))
            {
                var wanted = File.ReadAllLines(pv).FirstOrDefault(l => l.StartsWith("m_EditorVersion:"))?.Split(':')[1].Trim();
                if (wanted == Application.unityVersion) r.Ok("Editor version matches ProjectVersion.txt (" + wanted + ")");
                else r.Warn($"Editor {Application.unityVersion} differs from ProjectVersion.txt {wanted} (pick that version in Build Automation if available)");
            }
            else r.Error("ProjectSettings/ProjectVersion.txt missing");
            if (!projectPath.EndsWith("/client-unity")) r.Warn("project folder is not 'client-unity' (Build Automation 'Project subfolder' must point at client-unity)");
            if (!File.Exists("Packages/manifest.json")) r.Error("Packages/manifest.json missing");
            else
            {
                var manifest = File.ReadAllText("Packages/manifest.json");
                foreach (var pkg in new[] { "com.unity.addressables", "com.unity.ugui", "com.unity.nuget.newtonsoft-json", "com.petstar.shared" })
                    if (!manifest.Contains("\"" + pkg + "\"")) r.Error("package missing from manifest: " + pkg);
                if (!Directory.Exists("../shared-contracts/Runtime")) r.Error("shared rules package not found at ../shared-contracts (clone the whole repository)");
            }
#if UNITY_CLOUD_BUILD
            r.Ok("running in Unity Build Automation (UNITY_CLOUD_BUILD)");
#endif
        }

        private static void CheckScenes(Report r)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
            if (scenes.Length == 0) { r.Error("no enabled scene in Build Settings"); return; }
            if (scenes[0].path != PetStarBuild.ScenePath) r.Error($"first scene is {scenes[0].path}, expected {PetStarBuild.ScenePath}");
            else if (!File.Exists(scenes[0].path)) r.Error("bootstrap scene file missing: " + scenes[0].path);
            else r.Ok($"first scene {scenes[0].path} ({scenes.Length} scene(s) in build; UI is created from code, no prefabs required)");
            var deps = AssetDatabase.GetDependencies(PetStarBuild.ScenePath, true).Where(d => d != PetStarBuild.ScenePath).ToArray();
            r.Ok($"bootstrap scene dependencies: {deps.Length} (content loads via Addressables and Resources)");
        }

        private static void CheckBoardSprites(Report r)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            foreach (var (path, address) in PetStarBuild.BoardSprites)
            {
                if (!File.Exists(path)) { r.Error("board sprite file missing: " + path); continue; }
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null || ti.textureType != TextureImporterType.Sprite) r.Error("board sprite not imported as Sprite: " + path);
                var guid = AssetDatabase.AssetPathToGUID(path);
                var entry = settings?.FindAssetEntry(guid);
                if (entry == null) r.Error("board sprite not addressable: " + path);
                else if (entry.address != address) r.Error($"board sprite address is '{entry.address}', expected '{address}'");
                else if (entry.parentGroup.Name != PetStarBuild.BoardGroup) r.Warn($"board sprite in group {entry.parentGroup.Name}, expected {PetStarBuild.BoardGroup}");
                else r.Ok($"board sprite {address} -> {path} (group {PetStarBuild.BoardGroup})");
            }
        }

        /// <summary>Every address the runtime asks for: pet/{formId} for all forms, currency icons, pack art,
        /// IAP product icons, backgrounds.</summary>
        private static void CheckAddresses(Report r)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { r.Error("Addressables settings missing (run Setup)"); return; }
            var all = new HashSet<string>(settings.groups.Where(g => g != null).SelectMany(g => g.entries).Select(e => e.address));
            var required = new List<string> { "ui/ui_currency_rainbow_star", "ui/ui_currency_mini_green_bean", "ui/bg_pack_opening", "ui/ui_pack_basic", "ui/ui_pack_premium" };
            var pets = JObject.Parse(File.ReadAllText("Assets/Resources/Data/pets.json"));
            required.AddRange(pets["families"]!.SelectMany(f => f["forms"]!).Select(f => "pet/" + (string)f["id"]!));
            var economy = JObject.Parse(File.ReadAllText("Assets/Resources/Data/economy.json"));
            required.AddRange((economy["iapProducts"] as JArray ?? new JArray()).Select(p => "ui/" + (string)p["assetId"]!));
            var missing = required.Distinct().Where(a => !all.Contains(a)).ToList();
            if (missing.Count == 0) r.Ok($"all {required.Distinct().Count()} runtime addresses resolve ({all.Count} addressable entries)");
            else r.Error($"{missing.Count} address(es) missing: {string.Join(", ", missing.Take(15))}{(missing.Count > 15 ? " ..." : "")}");
            if (!all.Contains("ui/brand_logo")) r.Warn("official logo missing (upload assets-source/brand/petstar-logo-original.png); splash/login/lobby show the text title");
            else r.Ok("official logo ui/brand_logo");
            // card system expansion: 9 pack arts, upgrade materials and Divine crests (Divine Pet art is covered by pets.json above)
            foreach (var id in new[] { "ui_pack_basic", "ui_pack_premium", "ui_pack_transcendent", "ui_pack_epic", "ui_pack_legendary", "ui_pack_mythic",
                         "ui_pack_divine_premium", "ui_pack_divine_limited", "ui_pack_divine_star", "ui_mat_pet_shard", "ui_mat_enhance_stone",
                         "ui_mat_breakthrough_core", "ui_mat_awakening_crystal", "ui_mat_engraving_dust", "ui_mat_training_scroll",
                         "ui_crest_premium", "ui_crest_limited", "ui_crest_star" })
                if (!all.Contains("ui/" + id)) r.Error("card system art not addressable: ui/" + id);
        }

        private static void CheckFonts(Report r)
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length == 0) r.Error("TMP Settings missing (TMP Essential Resources not imported)");
            foreach (var w in new[] { "Regular", "Bold", "ExtraBold" })
            {
                var p = $"Assets/Resources/Fonts/BeVietnamPro-{w} SDF.asset";
                if (File.Exists(p)) r.Ok("font asset " + p); else r.Warn("font asset missing (Vietnamese text falls back): " + p);
            }
        }

        private static void CheckWebGL(Report r)
        {
            if (!File.Exists("Assets/WebGLTemplates/PetStar/index.html")) r.Error("WebGL template Assets/WebGLTemplates/PetStar/index.html missing");
            else if (PlayerSettings.WebGL.template != PetStarBuild.WebGLTemplate) r.Warn("WebGL template is " + PlayerSettings.WebGL.template);
            else r.Ok("WebGL template PetStar (black full-viewport page + rotate overlay)");
            foreach (var p in new[] { "Assets/Plugins/WebGL/PetStarWebSocket.jslib", "Assets/Plugins/WebGL/PetStarViewport.jslib" })
                if (File.Exists(p)) r.Ok("WebGL plugin " + p); else r.Error("WebGL plugin missing: " + p);
            if (!File.Exists("Assets/link.xml")) r.Warn("Assets/link.xml missing (code stripping may remove JSON-deserialized types)");
            r.Ok($"WebGL compression {PlayerSettings.WebGL.compressionFormat}, decompression fallback {PlayerSettings.WebGL.decompressionFallback}");
        }

        private static void CheckClientConfig(Report r, BuildTarget target)
        {
            var json = JObject.Parse(File.ReadAllText("Assets/Resources/Config/client-config.json"));
            var env = (string?)json["environment"] ?? "development";
            if (json["environments"]?[env] == null) { r.Error($"unknown environment '{env}' (PETSTAR_ENV must be development, staging or production)"); return; }
            var url = (string?)json["environments"]?[env]?["apiBaseUrl"] ?? "";
            if (url.Length == 0)
            {
                // A production build must point at a real deployed API; development/staging may be built first.
                if (env == "production") r.Error("production build without an API: set PETSTAR_API_BASE to the deployed https API");
                else r.Warn($"no API for environment '{env}' (PETSTAR_API_BASE not set): the build opens and shows 'server not configured'; login works after the backend is deployed and PETSTAR_API_BASE is set");
                return;
            }
            if (url.StartsWith("/"))
            {
                // same-origin API (e.g. "/api" on https://petstar.pages.dev, forwarded by Pages Functions)
                if (url.TrimEnd('/') != "/api") r.Error($"relative API '{url}' must be '/api'");
                else if (target != BuildTarget.WebGL) r.Error($"relative API '{url}' only works in WebGL; {target} needs an absolute https API");
                else r.Ok($"API same origin {url} (environment {env}); the page's Pages Functions forward it to the backend");
                return;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http") || (uri.AbsolutePath.Trim('/').Length > 0 && uri.AbsolutePath.Trim('/') != "api"))
            { r.Error($"API '{url}' must be an origin like https://host or https://host/api"); return; }
            bool local = uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "10.0.2.2";
            if (env == "production" && (local || uri.Scheme != "https" || uri.Host.EndsWith(".example")))
                r.Error($"production API {url} must be a real https host");
            else if (target == BuildTarget.WebGL && uri.Scheme == "http" && !local)
                r.Error($"WebGL pages are served over HTTPS; API {url} must be https (set PETSTAR_API_BASE)");
            else if (uri.Host.EndsWith(".example")) r.Error($"API {url} is a placeholder host; set PETSTAR_API_BASE or leave it empty");
            else if (local && target == BuildTarget.WebGL) r.Warn($"API {url} only works when the game is opened on the same machine as the API");
            else r.Ok($"API {url} (environment {env})");
        }

        private static void CheckData(Report r)
        {
            foreach (var f in Directory.GetFiles("Assets/Resources/Data", "*.json").Concat(Directory.GetFiles("Assets/Resources/Localization", "*.json")))
            {
                try { JToken.Parse(File.ReadAllText(f)); } catch (Exception e) { r.Error($"invalid JSON {f}: {e.Message}"); }
            }
            r.Ok("Resources data and localization JSON parse");
        }
    }
}
