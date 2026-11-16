using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PetStar.Client.Core
{
    /// <summary>Realtime push (same protocol as the web client): single-use 60 s ticket over HTTPS, then
    /// ws(s)://api/ws?ticket=...; JSON messages {type, data, at}; "ping" every 20 s; reconnect with capped
    /// exponential backoff. Messages are dispatched on the main thread. Screens refetch after reconnect; pushes
    /// are hints, the server state is the truth. (Android / Windows; not used in WebGL.)</summary>
    public sealed class Realtime : MonoBehaviour
    {
        public static Realtime? I { get; private set; }
        public event Action<string, JToken?>? Message;
        public event Action? Connected;
#if UNITY_WEBGL && !UNITY_EDITOR
        // ---------------- WebGL: browser WebSocket via Plugins/WebGL/PetStarWebSocket.jslib ----------------
        [DllImport("__Internal")] private static extern void PetStarWsOpen(string url, string gameObjectName);
        [DllImport("__Internal")] private static extern void PetStarWsClose();

        private bool _running, _open;
        private int? _server;
        private int _attempt;
        public bool IsConnected => _open;

        public static Realtime Ensure(GameObject host) => I ??= host.AddComponent<Realtime>();

        public void ConnectFor(int serverId)
        {
            if (_server == serverId && _running) return;
            Disconnect();
            _server = serverId;
            Connect();
        }

        public void Connect()
        {
            if (_running) return;
            _running = true;
            _ = Open();
        }

        public void Disconnect()
        {
            _running = false; _open = false; _server = null;
            PetStarWsClose();
        }

        private async System.Threading.Tasks.Task Open()
        {
            try
            {
                var t = await App.Api.Post<JObject>("/api/realtime/ticket", null);
                if (!_running) return;
                var baseUrl = ClientConfig.ApiBaseUrl;
                var url = (baseUrl.StartsWith("https") ? "wss" + baseUrl.Substring(5) : "ws" + baseUrl.Substring(4)) + "/ws?ticket=" + Uri.EscapeDataString((string)t["ticket"]!);
                PetStarWsOpen(url, gameObject.name);
            }
            catch (Exception) { _ = Retry(); }
        }

        private async System.Threading.Tasks.Task Retry()
        {
            if (!_running) return;
            await Wait.Seconds(Backoff(_attempt++) / 1000f);
            if (_running && !_open) _ = Open();
        }

        // called from JavaScript (SendMessage)
        public void OnWsOpen(string unused) { _open = true; _attempt = 0; Connected?.Invoke(); }
        public void OnWsClosed(string unused) { _open = false; _ = Retry(); }
        public void OnWsMessage(string json)
        {
            try { var j = JObject.Parse(json); Message?.Invoke((string?)j["type"] ?? "", j["data"]); } catch { /* malformed: ignore */ }
        }

        private void OnDestroy() => Disconnect();

        private static int Backoff(int attempt) =>
            (int)(Math.Min(30_000, 1000 * Math.Pow(2, Math.Min(attempt, 5))) * (0.75 + UnityEngine.Random.value * 0.5));
#else
        // ---------------- Android / Windows / Editor: System.Net.WebSockets ----------------
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentQueue<(string type, JToken? data)> _inbox = new();
        private volatile bool _justConnected;

        public static Realtime Ensure(GameObject host) => I ??= host.AddComponent<Realtime>();

        private int? _server;

        /// <summary>Connects for the selected server; switching servers reconnects with a new ticket.</summary>
        public void ConnectFor(int serverId)
        {
            if (_server == serverId && _cts != null) return;
            Disconnect();
            _server = serverId;
            Connect();
        }

        public void Connect()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _ = Loop(_cts.Token);
        }

        public void Disconnect()
        {
            _cts?.Cancel(); _cts = null; _server = null;
            try { _ws?.Abort(); } catch { /* ignore */ }
            _ws = null;
        }

        private void OnDestroy() => Disconnect();

        private void OnApplicationPause(bool paused)
        {
            // mobile OSes kill sockets in the background; reconnect promptly when the app returns
            if (!paused && _cts != null && !IsConnected && _server is int sid) { Disconnect(); ConnectFor(sid); }
        }

        private void Update()
        {
            if (_justConnected) { _justConnected = false; Connected?.Invoke(); }
            while (_inbox.TryDequeue(out var m)) Message?.Invoke(m.type, m.data);
        }

        private static int Backoff(int attempt) =>
            (int)(Math.Min(30_000, 1000 * Math.Pow(2, Math.Min(attempt, 5))) * (0.75 + UnityEngine.Random.value * 0.5));

        private async Task Loop(CancellationToken ct)
        {
            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var t = await App.Api.Post<JObject>("/api/realtime/ticket", null);
                    var baseUrl = ClientConfig.ApiBaseUrl;
                    var url = (baseUrl.StartsWith("https") ? "wss" + baseUrl.Substring(5) : "ws" + baseUrl.Substring(4)) + "/ws?ticket=" + Uri.EscapeDataString((string)t["ticket"]!);
                    _ws = new ClientWebSocket();
                    await _ws.ConnectAsync(new Uri(url), ct);
                    attempt = 0;
                    _justConnected = true;
                    _ = PingLoop(_ws, ct);
                    var buf = new byte[16 * 1024];
                    var sb = new StringBuilder();
                    while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                        if (!r.EndOfMessage) continue;
                        var s = sb.ToString(); sb.Clear();
                        if (s == "pong") continue;
                        try { var j = JObject.Parse(s); _inbox.Enqueue(((string?)j["type"] ?? "", j["data"])); } catch { /* malformed: ignore */ }
                    }
                }
                catch (Exception) when (!ct.IsCancellationRequested) { /* network / auth: retry below */ }
                catch (OperationCanceledException) { break; }
                try { _ws?.Dispose(); } catch { /* ignore */ }
                _ws = null;
                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(Backoff(attempt++), ct); } catch (OperationCanceledException) { break; }
            }
        }

        private static async Task PingLoop(ClientWebSocket ws, CancellationToken ct)
        {
            var ping = new ArraySegment<byte>(Encoding.UTF8.GetBytes("ping"));
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    await Task.Delay(20_000, ct);
                    if (ws.State == WebSocketState.Open) await ws.SendAsync(ping, WebSocketMessageType.Text, true, ct);
                }
            }
            catch { /* the receive loop handles reconnection */ }
        }
    #endif
    }
}
