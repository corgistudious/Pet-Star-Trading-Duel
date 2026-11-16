using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PetStar.Contracts;
using UnityEngine;

namespace PetStar.Client.Core
{
    /// <summary>Authentication + selected-server state, and the latest server-authoritative player snapshot.
    /// Balances shown anywhere in the UI come from <see cref="Player"/> (never computed locally).</summary>
    public sealed class Session
    {
        private const string RefreshKey = "auth.refresh";
        private const string ServerKey = "auth.server";

        public string? AccessToken { get; private set; }
        public long Uid { get; private set; }
        public int? ServerId { get; private set; }
        public PlayerDto? Player { get; private set; }
        public event Action<PlayerDto>? PlayerChanged;
        public event Action? LoggedOut;

        private Task<bool>? _refreshing;

        public bool HasStoredLogin => !string.IsNullOrEmpty(SecureStore.Get(RefreshKey));
        public int? LastServer => PlayerPrefs.HasKey(ServerKey) ? PlayerPrefs.GetInt(ServerKey) : (int?)null;

        public void Apply(AuthResponse a)
        {
            AccessToken = a.AccessToken;
            Uid = a.Uid;
            if (!string.IsNullOrEmpty(a.RefreshToken)) SecureStore.Set(RefreshKey, a.RefreshToken);
            if (a.ServerId.HasValue) { ServerId = a.ServerId; PlayerPrefs.SetInt(ServerKey, a.ServerId.Value); }
        }

        public void SetPlayer(PlayerDto p)
        {
            Player = p;
            PlayerChanged?.Invoke(p);
        }

        /// <summary>Applies a server-returned balance map (after purchase, evolve, reward...).</summary>
        public void UpdateCurrencies(Dictionary<string, long>? balances)
        {
            if (Player == null || balances == null) return;
            foreach (var kv in balances) Player.Currencies[kv.Key] = kv.Value;
            PlayerChanged?.Invoke(Player);
        }

        public void Logout()
        {
            AccessToken = null; Player = null; ServerId = null;
            SecureStore.Delete(RefreshKey);
            LoggedOut?.Invoke();
        }

        /// <summary>Single-flight refresh: concurrent 401s share one refresh call (refresh tokens rotate).</summary>
        public Task<bool> TryRefresh(ApiClient api)
        {
            return _refreshing ??= DoRefresh(api);
        }

        private async Task<bool> DoRefresh(ApiClient api)
        {
            try
            {
                var rt = SecureStore.Get(RefreshKey);
                if (string.IsNullOrEmpty(rt)) return false;
                var a = await api.RawRefresh(rt!, ServerId ?? LastServer);
                if (a == null) { SecureStore.Delete(RefreshKey); return false; }
                Apply(a);
                return true;
            }
            finally { _refreshing = null; }
        }
    }

    /// <summary>Refresh-token storage. Uses PlayerPrefs obfuscated per device; production builds should move this to
    /// Android Keystore / Windows DPAPI (tracked in docs/security.md).</summary>
    public static class SecureStore
    {
        private static string DeviceSalt => SystemInfo.deviceUniqueIdentifier;

        public static void Set(string key, string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            var salt = System.Text.Encoding.UTF8.GetBytes(DeviceSalt);
            for (int i = 0; i < bytes.Length; i++) bytes[i] ^= salt[i % salt.Length];
            PlayerPrefs.SetString(key, Convert.ToBase64String(bytes));
            PlayerPrefs.Save();
        }

        public static string? Get(string key)
        {
            var s = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(s)) return null;
            try
            {
                var bytes = Convert.FromBase64String(s);
                var salt = System.Text.Encoding.UTF8.GetBytes(DeviceSalt);
                for (int i = 0; i < bytes.Length; i++) bytes[i] ^= salt[i % salt.Length];
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException) { return null; }
        }

        public static void Delete(string key) { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); }
    }
}
