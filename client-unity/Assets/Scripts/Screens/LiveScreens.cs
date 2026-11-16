using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    /// <summary>Shared text helpers for reward bundles {currencies, items, packs, cards}.</summary>
    internal static class Rewards
    {
        public static string Text(JToken? r)
        {
            if (r == null || r.Type != JTokenType.Object) return "";
            var parts = new List<string>();
            foreach (var p in (r["currencies"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                parts.Add($"+{L.Number((long)p.Value)} {L.T("currency." + p.Name.ToLowerInvariant())}");
            foreach (var p in (r["items"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                parts.Add($"{L.T("item." + p.Name)} ×{(long)p.Value}");
            foreach (var p in (r["packs"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                parts.Add($"{L.T("pack." + p.Name.Replace("_pack", "") + ".name")} ×{(long)p.Value}");
            foreach (var c in (r["cards"] as JArray) ?? new JArray())
                parts.Add($"{GameDataClient.PetName((string)c["formId"]!)} {(string?)c["rarity"]}");
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Daily (00:00 UTC) and weekly (Monday 00:00 UTC) missions. Progress is computed by the server.</summary>
    public sealed class MissionsScreen : ScreenBase
    {
        public override string TitleKey => "web.missions";
        private JObject? _data;
        private string _tab = "daily";

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_data == null) return;
            W.Tabs(col, new List<(string, string)> { ("daily", L.T("web.daily")), ("weekly", L.T("web.weekly")) }, _tab, t => { _tab = t; UiKit.Clear(Root); Build(); });
            var period = _data[_tab]!;
            var left = (System.DateTimeOffset)period["resetsAt"]! - (System.DateTimeOffset)_data["serverTime"]!;
            W.Line(col, $"{L.T("web.resets_in")} {W.Remaining(left)} · UTC", 26);
            foreach (var m in period["items"]!)
            {
                string id = (string)m["id"]!;
                int progress = (int)m["progress"]!, target = (int)m["target"]!;
                bool completed = (bool)m["completed"]!, claimed = (bool)m["claimed"]!;
                var c = W.Card(col, claimed ? UiKit.Sky : UiKit.White, 170);
                UiKit.Text(c.transform, $"<b>{L.T("mission." + id, target)}</b>\n<size=80%>{progress}/{target}   {Rewards.Text(m["rewards"])}</size>", 30, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft)
                    .rectTransform.Stretch(24, 10, 280, 10);
                var b = UiKit.Button(c.transform, claimed ? L.T("mailbox.claimed") : L.T("mailbox.claim"), async () =>
                {
                    var r = await Call(() => Api.Post<JObject>($"/api/missions/{id}/claim", null));
                    if (r != null) { Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>()); Nav.Toast(L.T("web.claimed")); OnShow(); }
                }, completed && !claimed ? UiKit.ButtonStyle.Gold : UiKit.ButtonStyle.Secondary, 28);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 100), new Vector2(-16, 0), new Vector2(1, 0.5f));
                b.interactable = completed && !claimed;
            }
            W.Line(col, L.T("web.missions_note"), 24);
        }

        public override async void OnShow()
        {
            _data = await Call(() => Api.Get<JObject>("/api/missions"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    /// <summary>7-day login rewards, gift code redemption and live events (all paid through the server ledger).</summary>
    public sealed class RewardsScreen : ScreenBase
    {
        public override string TitleKey => "web.rewards";
        private JObject? _login;
        private JArray? _events;

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_login == null) return;
            W.Header(col, L.T("web.daily_login"), 34);
            int streak = (int)_login["streakDay"]!; bool today = (bool)_login["claimedToday"]!;
            var cycle = _login["cycle"]!.ToList();
            for (int i = 0; i < cycle.Count; i++)
            {
                int day = i + 1;
                bool done = day < streak || (day == streak && today);
                W.Line(col, $"{(done ? "✔" : day == streak ? "★" : "·")} {L.T("web.day", day)}: {Rewards.Text(cycle[i])}", 28, day == streak && !today ? UiKit.GoldDeep : null);
            }
            var claim = W.Btn(W.Row(col, 100), today ? L.T("web.come_back_tomorrow") : L.T("mailbox.claim"), async () =>
            {
                var r = await Call(() => Api.Post<JObject>("/api/rewards/login/claim", null));
                if (r != null) { Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>()); Nav.Toast(L.T("web.claimed")); OnShow(); }
            }, UiKit.ButtonStyle.Gold);
            claim.interactable = !today;

            W.Header(col, L.T("web.giftcode"), 34);
            var row = W.Row(col, 100);
            var input = W.Input(row, "PETSTAR-2026");
            input.characterLimit = 32;
            W.Btn(row, L.T("web.redeem"), async () =>
            {
                var code = input.text.Trim().ToUpperInvariant();
                if (code.Length < 4) return;
                var r = await Call(() => Api.Post<JObject>("/api/giftcodes/redeem", new { code }));
                if (r != null) { Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>()); Nav.Toast(L.T("web.redeemed")); input.text = ""; }
            }, UiKit.ButtonStyle.Primary);

            W.Header(col, L.T("web.events"), 34);
            if (_events == null || _events.Count == 0) W.Line(col, L.T("web.no_events"), 28);
            foreach (var e in _events ?? new JArray())
            {
                long id = (long)e["id"]!;
                bool started = (bool)e["started"]!, claimed = (bool)e["claimed"]!;
                string key = (string)e["key"]!;
                var c = W.Card(col, null, 160);
                UiKit.Text(c.transform, $"<b>{key}</b>\n<size=80%>{Rewards.Text(e["rewards"])}</size>", 30, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 10, 280, 10);
                var b = UiKit.Button(c.transform, claimed ? L.T("mailbox.claimed") : L.T("mailbox.claim"), async () =>
                {
                    var r = await Call(() => Api.Post<JObject>($"/api/events/{id}/claim", null));
                    if (r != null) { Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>()); OnShow(); }
                }, started && !claimed ? UiKit.ButtonStyle.Gold : UiKit.ButtonStyle.Secondary, 28);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 100), new Vector2(-16, 0), new Vector2(1, 0.5f));
                b.interactable = started && !claimed;
            }
        }

        public override async void OnShow()
        {
            _login = await Call(() => Api.Get<JObject>("/api/rewards/login"));
            _events = await Call(() => Api.Get<JArray>("/api/events"), overlay: false);
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    /// <summary>Guild: find/create when not a member; members, roles, requests, announcements, leave when a member.</summary>
    public sealed class GuildScreen : ScreenBase
    {
        public override string TitleKey => "web.guild";
        private JObject? _mine;
        private JArray? _list;

        private static int Rank(string? r) => r == "leader" ? 3 : r == "officer" ? 2 : 1;

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_mine == null) return;
            if (_mine["guild"]?.Type != JTokenType.Object) { BuildFind(col); return; }
            var g = _mine["guild"]!;
            string myRole = (string)_mine["myRole"]!;
            W.Header(col, (string)g["name"]!, 44);
            W.Line(col, (string?)g["description"] ?? "", 28);
            W.Btn(W.Row(col, 96), "💬 " + L.T("web.chat"), () => Nav.Push(new ChatScreen("guild")));
            if (Rank(myRole) >= 2)
                foreach (var r in _mine["requests"] ?? new JArray())
                {
                    long rid = (long)r["id"]!;
                    var row = W.Row(col, 96);
                    W.Line(row, $"{L.T("web.join_requests")}: {(string)r["nickname"]!}", 28);
                    W.Btn(row, L.T("web.accept"), async () => { if (await Call(() => Api.Post<JObject>($"/api/guilds/requests/{rid}/accept", null)) != null) OnShow(); }, UiKit.ButtonStyle.Primary);
                    W.Btn(row, L.T("web.decline"), async () => { if (await Call(() => Api.Post<JObject>($"/api/guilds/requests/{rid}/reject", null)) != null) OnShow(); });
                }
            W.Header(col, L.T("web.members"), 34);
            foreach (var m in _mine["members"] ?? new JArray())
            {
                long uid = (long)m["uid"]!; string role = (string)m["role"]!;
                var row = W.Row(col, 96);
                W.Line(row, $"{(string)m["nickname"]!} · {L.T("web.role_" + role)} · Lv.{(int)m["level"]!}", 28, UiKit.Ink);
                if (uid != Session.Uid && Rank(myRole) == 3)
                    W.Btn(row, role == "officer" ? L.T("web.demote") : L.T("web.promote"), async () => { if (await Call(() => Api.Post<JObject>($"/api/guilds/members/{uid}/role", new { role = role == "officer" ? "member" : "officer" })) != null) OnShow(); });
                if (uid != Session.Uid && Rank(myRole) > Rank(role) && Rank(myRole) >= 2)
                    W.Btn(row, L.T("web.kick"), async () => { if (await Call(() => Api.Post<JObject>($"/api/guilds/members/{uid}/kick", null)) != null) OnShow(); }, UiKit.ButtonStyle.Danger);
            }
            W.Header(col, L.T("web.announcements"), 34);
            if (Rank(myRole) >= 2)
            {
                var row = W.Row(col, 100);
                var input = W.Input(row, L.T("web.announcement_hint"));
                input.characterLimit = 300;
                W.Btn(row, L.T("feed.publish"), async () => { if (input.text.Trim().Length > 0 && await Call(() => Api.Post<JObject>("/api/guilds/announcements", new { text = input.text })) != null) OnShow(); }, UiKit.ButtonStyle.Primary);
            }
            foreach (var a in _mine["announcements"] ?? new JArray())
                W.Line(col, $"{(string)a["text"]!}  <size=75%><color=#5B6B8F>{(string)a["author"]!} · {W.Ago((System.DateTimeOffset)a["createdAt"]!)}</color></size>", 28, UiKit.Ink);
            W.Btn(W.Row(col, 96), L.T("web.leave_guild"), () => W.Confirm(L.T("web.guild"), L.T("web.leave_guild") + "?", L.T("web.ok"), async () =>
            {
                if (await Call(() => Api.Post<JObject>("/api/guilds/leave", null)) != null) OnShow();
            }), UiKit.ButtonStyle.Danger);
        }

        private void BuildFind(RectTransform col)
        {
            W.Header(col, L.T("web.search_guilds"), 34);
            if (_list == null || _list.Count == 0) W.Line(col, L.T("web.no_guilds"), 28);
            foreach (var g in _list ?? new JArray())
            {
                long id = (long)g["id"]!;
                bool open = (string)g["joinPolicy"]! == "open";
                var c = W.Card(col, null, 150);
                UiKit.Text(c.transform, $"<b>{(string)g["name"]!}</b>  {(int)g["members"]!}/{(int)g["maxMembers"]!}\n<size=80%>{(string?)g["description"]}</size>", 30, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 10, 280, 10);
                var b = UiKit.Button(c.transform, open ? L.T("web.join") : L.T("web.request_join"), async () =>
                {
                    var r = await Call(() => Api.Post<JObject>($"/api/guilds/{id}/join", null));
                    if (r != null) { Nav.Toast((string?)r["status"] == "joined" ? L.T("web.join") : L.T("web.request_join")); OnShow(); }
                }, UiKit.ButtonStyle.Primary, 28);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 100), new Vector2(-16, 0), new Vector2(1, 0.5f));
            }
            W.Header(col, L.T("web.create_guild"), 34);
            var name = W.Input(col, L.T("web.guild_name")); name.characterLimit = 24;
            var desc = W.Input(col, L.T("web.guild_desc")); desc.characterLimit = 200;
            string policy = "open";
            var pol = W.Row(col, 90);
            Button? openB = null, apprB = null;
            openB = W.Btn(pol, L.T("web.policy_open"), () => { policy = "open"; openB!.GetComponent<Image>().color = UiKit.Sky; apprB!.GetComponent<Image>().color = UiKit.White; });
            apprB = W.Btn(pol, L.T("web.policy_approval"), () => { policy = "approval"; apprB!.GetComponent<Image>().color = UiKit.Sky; openB!.GetComponent<Image>().color = UiKit.White; });
            W.Btn(W.Row(col, 100), L.T("web.create_guild"), async () =>
            {
                if (name.text.Trim().Length < 3) return;
                if (await Call(() => Api.Post<JObject>("/api/guilds", new { name = name.text.Trim(), description = desc.text, joinPolicy = policy })) != null) { Nav.Toast(L.T("web.guild_created")); OnShow(); }
            }, UiKit.ButtonStyle.Gold);
        }

        public override async void OnShow()
        {
            _mine = await Call(() => Api.Get<JObject>("/api/guilds/mine"));
            _list = _mine != null && _mine["guild"]?.Type != JTokenType.Object ? await Call(() => Api.Get<JArray>("/api/guilds"), overlay: false) : null;
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    /// <summary>World / guild / direct-message chat with live updates over the realtime socket.</summary>
    public sealed class ChatScreen : ScreenBase
    {
        public override string TitleKey => "web.chat";
        private string _channel;
        private readonly List<JToken> _msgs = new();
        private TextMeshProUGUI? _logText;
        private bool _subscribed;

        public ChatScreen(string channel = "world") { _channel = channel; }

        public override void Build()
        {
            var col = W.Column(this, Root);
            W.Tabs(col, new List<(string, string)> { ("world", L.T("web.chat_world")), ("guild", L.T("web.guild")) }, _channel.StartsWith("dm:") ? "" : _channel,
                c => { _channel = c; _msgs.Clear(); OnShow(); });
            var log = W.Card(col, null, Wide ? 520 : 900);
            _logText = UiKit.Text(log.transform, "", 28, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.BottomLeft);
            _logText.rectTransform.Stretch(20, 14, 20, 14);
            _logText.overflowMode = TextOverflowModes.Truncate;
            Render();
            var row = W.Row(col, 100);
            var input = W.Input(row, L.T("web.chat_hint"));
            input.characterLimit = 200;
            W.Btn(row, L.T("feed.send"), async () =>
            {
                var text = input.text.Trim();
                if (text.Length == 0) return;
                if (await Call(() => Api.Post<JObject>($"/api/chat/{_channel}", new { text }), overlay: false) != null) { input.text = ""; if (_channel.StartsWith("dm:")) OnShow(); }
            }, UiKit.ButtonStyle.Primary);
        }

        private void Render()
        {
            if (_logText == null) return;
            _logText.text = _msgs.Count == 0 ? L.T("web.chat_empty")
                : string.Join("\n", _msgs.TakeLast(Wide ? 14 : 24).Select(m => $"<b>{(string)m["authorNickname"]!}</b>: {Escape((string)m["text"]!)}"));
        }

        private static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›"); // no rich-text injection from other players

        private void OnRealtime(string type, JToken? data)
        {
            if (type != "chat" || data == null) return;
            string key = (string?)data["channel"] ?? "";
            bool mine = _channel == "world" ? key == "world" : _channel == "guild" ? key.StartsWith("guild:") : key.StartsWith("dm:") && (long?)data["authorUid"] == long.Parse(_channel.Substring(3));
            if (!mine || _msgs.Any(m => (long?)m["id"] == (long?)data["id"])) return;
            _msgs.Add(data);
            Render();
        }

        public override async void OnShow()
        {
            if (!_subscribed && Realtime.I != null) { _subscribed = true; Realtime.I.Message += OnRealtime; }
            var rows = await Call(() => Api.Get<JArray>($"/api/chat/{_channel}"), overlay: false);
            _msgs.Clear();
            if (rows != null) _msgs.AddRange(rows);
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }

        public override void OnHide()
        {
            if (_subscribed && Realtime.I != null) { _subscribed = false; Realtime.I.Message -= OnRealtime; }
        }
    }

    /// <summary>Live PvP lobby: quick match (queue) and challenges between friends. The server pushes pvp_start.</summary>
    public sealed class PvpLobbyScreen : ScreenBase
    {
        public override string TitleKey => "web.live_pvp";
        private JArray? _challenges, _friends;
        private bool _queued, _subscribed;

        public override void Build()
        {
            var col = W.Column(this, Root);
            W.Line(col, L.T("web.live_pvp_info"), 28);
            if (_queued)
            {
                W.Line(col, L.T("web.searching"), 32, UiKit.Sapphire);
                W.Btn(W.Row(col, 100), L.T("web.leave_queue"), async () => { await Call(() => Api.Delete<JObject>("/api/pvp/queue")); _queued = false; UiKit.Clear(Root); Build(); });
            }
            else W.Btn(W.Row(col, 110), L.T("web.quick_match"), Queue, UiKit.ButtonStyle.Gold);

            W.Header(col, L.T("web.challenges"), 34);
            if (_challenges == null || _challenges.Count == 0) W.Line(col, L.T("web.no_challenges"), 28);
            foreach (var c in _challenges ?? new JArray())
            {
                long id = (long)c["id"]!; bool incoming = (bool)c["incoming"]!;
                var row = W.Row(col, 96);
                W.Line(row, (incoming ? "⬅ " : "➡ ") + (string)c["otherNickname"]!, 30, UiKit.Ink);
                if (incoming)
                {
                    W.Btn(row, L.T("web.accept"), () => Decide(id, "accept"), UiKit.ButtonStyle.Primary);
                    W.Btn(row, L.T("web.decline"), () => Decide(id, "decline"));
                }
                else W.Btn(row, L.T("web.cancel"), () => Decide(id, "cancel"));
            }
            W.Header(col, L.T("web.challenge_friend"), 34);
            var friends = (_friends ?? new JArray()).Where(f => (string?)f["status"] == "friends").ToList();
            if (friends.Count == 0) W.Line(col, L.T("web.no_friends"), 28);
            foreach (var f in friends)
            {
                long uid = (long)f["uid"]!;
                var row = W.Row(col, 96);
                W.Line(row, (string)f["nickname"]!, 30, UiKit.Ink);
                W.Btn(row, "⚔ " + L.T("web.challenge"), async () => { if (await Call(() => Api.Post<JObject>("/api/pvp/challenges", new { uid })) != null) { Nav.Toast(L.T("web.challenge_sent")); OnShow(); } });
            }
        }

        private async void Queue()
        {
            var r = await Call(() => Api.Post<JObject>("/api/pvp/queue", null));
            if (r == null) return;
            var status = (string?)r["status"];
            if (status == "matched") await OpenBattle((string)r["battleId"]!);
            else if (status == "queued") { _queued = true; UiKit.Clear(Root); Build(); }
            else Nav.Toast(L.T("web.queue_retry"));
        }

        private async void Decide(long id, string decision)
        {
            var r = await Call(() => Api.Post<JObject>($"/api/pvp/challenges/{id}/{decision}", null));
            if (r?["battleId"] != null) await OpenBattle((string)r["battleId"]!);
            else OnShow();
        }

        private async System.Threading.Tasks.Task OpenBattle(string battleId)
        {
            _queued = false;
            var v = await Call(() => Api.Get<JObject>($"/api/pvp/battles/{battleId}"));
            if (v == null) return;
            Nav.Push(new DuelBattleScreen(v["battle"]!.ToObject<BattleDto>(Newtonsoft.Json.JsonSerializer.Create(ApiClient.Json))!, live: true));
        }

        private void OnRealtime(string type, JToken? data)
        {
            if (type == "pvp_start" && data?["battleId"] != null) _ = OpenBattle((string)data["battleId"]!);
            else if (type == "pvp_declined" || type == "notification") OnShow();
        }

        public override async void OnShow()
        {
            if (!_subscribed && Realtime.I != null) { _subscribed = true; Realtime.I.Message += OnRealtime; }
            _challenges = await Call(() => Api.Get<JArray>("/api/pvp/challenges"), overlay: false);
            _friends = await Call(() => Api.Get<JArray>("/api/friends"), overlay: false);
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }

        public override void OnHide()
        {
            if (_subscribed && Realtime.I != null) { _subscribed = false; Realtime.I.Message -= OnRealtime; }
        }
    }
}
