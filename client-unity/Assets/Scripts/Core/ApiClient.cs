using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using PetStar.Contracts;
using UnityEngine;
using UnityEngine.Networking;

namespace PetStar.Client.Core
{
    public sealed class ApiError : Exception
    {
        public long Status { get; }
        public string Code { get; }
        public ApiError(long status, string code) : base(code) { Status = status; Code = code; }
        public bool IsNetwork => Status == 0;
    }

    /// <summary>
    /// REST client. The server is the only source of truth: the client sends intents (open pack, evolve, actions)
    /// and renders the authoritative response. Mutations carry an Idempotency-Key so a retry after a network
    /// drop can never apply twice.
    /// </summary>
    public sealed class ApiClient
    {
        public static readonly JsonSerializerSettings Json = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
        };

        private readonly Session _session;
        public ApiClient(Session session) { _session = session; }

        public static string NewIdempotencyKey() => "c" + Guid.NewGuid().ToString("N");

        public Task<T> Get<T>(string path) => Send<T>("GET", path, null, null, true);
        public Task<T> Post<T>(string path, object? body, string? idempotencyKey = null) => Send<T>("POST", path, body, idempotencyKey, true);
        public Task<T> Put<T>(string path, object? body) => Send<T>("PUT", path, body, null, true);
        public Task<T> Patch<T>(string path, object? body) => Send<T>("PATCH", path, body, null, true);
        public Task<T> Delete<T>(string path) => Send<T>("DELETE", path, null, null, true);

        /// <summary>Retries network failures for idempotent requests (GET or with an idempotency key).</summary>
        private async Task<T> Send<T>(string method, string path, object? body, string? idemKey, bool allowRefresh)
        {
            int attempts = (method == "GET" || idemKey != null) ? 3 : 1;
            for (int attempt = 1; ; attempt++)
            {
                try { return await SendOnce<T>(method, path, body, idemKey, allowRefresh); }
                catch (ApiError e) when (e.IsNetwork && attempt < attempts)
                {
                    await Wait.Seconds(0.6f * attempt);
                }
            }
        }

        private async Task<T> SendOnce<T>(string method, string path, object? body, string? idemKey, bool allowRefresh)
        {
            using var req = new UnityWebRequest(ClientConfig.ApiBaseUrl + path, method);
            req.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, Json)));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            if (!string.IsNullOrEmpty(_session.AccessToken)) req.SetRequestHeader("Authorization", "Bearer " + _session.AccessToken);
            if (idemKey != null) req.SetRequestHeader("Idempotency-Key", idemKey);
            req.SetRequestHeader("Accept-Language", L.Language);
            req.timeout = 20;

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
                throw new ApiError(0, "network");

            var text = req.downloadHandler.text;
            if (req.responseCode == 401 && allowRefresh && await _session.TryRefresh(this))
                return await SendOnce<T>(method, path, body, idemKey, false);
            if (req.responseCode < 200 || req.responseCode >= 300)
            {
                string code = "http_" + req.responseCode;
                try { code = (string?)JObject.Parse(text)["code"] ?? code; } catch (JsonException) { }
                throw new ApiError(req.responseCode, code);
            }
            if (typeof(T) == typeof(JObject)) return (T)(object)JObject.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
            return JsonConvert.DeserializeObject<T>(text, Json)!;
        }

        internal async Task<AuthResponse?> RawRefresh(string refreshToken, int? serverId)
        {
            using var req = new UnityWebRequest(ClientConfig.ApiBaseUrl + "/api/auth/refresh", "POST");
            req.downloadHandler = new DownloadHandlerBuffer();
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new RefreshRequest { RefreshToken = refreshToken }, Json)));
            req.SetRequestHeader("Content-Type", "application/json");
            if (serverId.HasValue) req.SetRequestHeader("X-Server-Id", serverId.Value.ToString());
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.responseCode != 200) return null;
            return JsonConvert.DeserializeObject<AuthResponse>(req.downloadHandler.text, Json);
        }
    }
}
