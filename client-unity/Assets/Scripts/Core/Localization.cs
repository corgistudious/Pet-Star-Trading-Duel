using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace PetStar.Client.Core
{
    /// <summary>Key-based localization. Every player-visible string goes through L.T(key). Adding a language =
    /// adding Resources/Localization/{code}.json and listing it in Supported.</summary>
    public static class L
    {
        public static readonly string[] Supported = { "en", "vi" };
        private static Dictionary<string, string> _table = new();
        private static Dictionary<string, string> _fallback = new();
        public static string Language { get; private set; } = "en";
        public static event Action? Changed;

        public static void Init()
        {
            _fallback = Load("en");
            var saved = PlayerPrefs.GetString("settings.language", "");
            if (string.IsNullOrEmpty(saved))
                saved = Application.systemLanguage == SystemLanguage.Vietnamese ? "vi" : "en";
            SetLanguage(saved);
        }

        public static void SetLanguage(string code)
        {
            if (Array.IndexOf(Supported, code) < 0) code = "en";
            Language = code;
            _table = code == "en" ? _fallback : Load(code);
            PlayerPrefs.SetString("settings.language", code);
            Changed?.Invoke();
        }

        private static Dictionary<string, string> Load(string code)
        {
            var asset = Resources.Load<TextAsset>("Localization/" + code);
            return asset == null ? new Dictionary<string, string>() : JsonConvert.DeserializeObject<Dictionary<string, string>>(asset.text) ?? new();
        }

        public static string T(string key)
        {
            if (_table.TryGetValue(key, out var v)) return v;
            if (_fallback.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string T(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch (FormatException) { return T(key); }
        }

        /// <summary>Server error codes map to "error.{code}" keys.</summary>
        public static string Error(string code)
        {
            var k = "error." + code;
            var v = T(k);
            return v == k ? T("error.generic") : v;
        }

        public static string Number(long n) =>
            Language == "vi" ? n.ToString("N0", new System.Globalization.CultureInfo("vi-VN")) : n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
