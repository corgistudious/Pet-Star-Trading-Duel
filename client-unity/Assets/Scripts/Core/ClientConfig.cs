using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PetStar.Client.Core
{
    /// <summary>Reads Resources/Config/client-config.json. No secrets are ever stored in the client.</summary>
    public static class ClientConfig
    {
        private static string? _baseUrl;
        public static string Environment { get; private set; } = "development";

        public static string ApiBaseUrl
        {
            get
            {
                if (_baseUrl != null) return _baseUrl;
                var text = Resources.Load<TextAsset>("Config/client-config")?.text ?? "{}";
                var j = JObject.Parse(text);
                Environment = (string?)j["environment"] ?? "development";
                var env = j["environments"]?[Environment];
                // development falls back to the local API; staging/production may be built before an API exists
                // (empty apiBaseUrl): the game then reports "server not configured" instead of calling a made-up host
                var url = (string?)env?["apiBaseUrl"] ?? (Environment == "development" ? "http://127.0.0.1:5080" : "");
#if UNITY_ANDROID && !UNITY_EDITOR
                url = (string?)env?["androidEmulatorApiBaseUrl"] ?? url;
#endif
                var overrideUrl = PlayerPrefs.GetString("dev.apiBaseUrl", "");
                if (Debug.isDebugBuild && !string.IsNullOrEmpty(overrideUrl)) url = overrideUrl;
                _baseUrl = Resolve(url, PageUrl());
                return _baseUrl;
            }
        }

        /// <summary>
        /// Turns the configured API base into the origin that request paths ("/api/...", "/ws") are appended to.
        ///  - "https://api.host"        -> "https://api.host"
        ///  - "https://host/api"        -> "https://host"   (paths already start with /api)
        ///  - "/api" (same origin)      -> origin of the page that hosts the WebGL build, e.g. https://petstar.pages.dev
        ///                                 (Cloudflare Pages Functions forward /api/* and /ws to the backend)
        ///  - "" or a relative base outside a web page -> "" (not configured)
        /// </summary>
        public static string Resolve(string configured, string? pageUrl)
        {
            var url = (configured ?? "").Trim().TrimEnd('/');
            bool relative = url.StartsWith("/") || (url.Length == 0 && (configured ?? "").Trim() == "/");
            if (url.EndsWith("/api", System.StringComparison.OrdinalIgnoreCase)) url = url.Substring(0, url.Length - 4);
            if (!relative) return url;
            if (pageUrl != null && System.Uri.TryCreate(pageUrl, System.UriKind.Absolute, out var page)
                && (page.Scheme == "https" || page.Scheme == "http"))
                return page.GetLeftPart(System.UriPartial.Authority) + url;
            return "";
        }

        private static string? PageUrl()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Application.absoluteURL;
#else
            return null; // native clients and the Editor need an absolute API origin
#endif
        }

        /// <summary>False when this build has no API origin (e.g. a staging WebGL build made before the backend
        /// is deployed). Requests then fail fast with error code "server_not_configured".</summary>
        public static bool IsServerConfigured => ApiBaseUrl.Length > 0;

        public static string Platform =>
#if UNITY_ANDROID
            "android";
#elif UNITY_STANDALONE_WIN
            "windows";
#else
            "editor";
#endif
    }
}
