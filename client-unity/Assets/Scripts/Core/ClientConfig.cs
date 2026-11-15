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
                var url = (string?)env?["apiBaseUrl"] ?? "http://127.0.0.1:5080";
#if UNITY_ANDROID && !UNITY_EDITOR
                url = (string?)env?["androidEmulatorApiBaseUrl"] ?? url;
#endif
                var overrideUrl = PlayerPrefs.GetString("dev.apiBaseUrl", "");
                if (Debug.isDebugBuild && !string.IsNullOrEmpty(overrideUrl)) url = overrideUrl;
                _baseUrl = url.TrimEnd('/');
                return _baseUrl;
            }
        }

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
