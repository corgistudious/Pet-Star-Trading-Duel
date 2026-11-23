using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using PetStar.Rules.Team;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    public sealed class TeamView
    {
        public Guid Id { get; set; } public string Mode { get; set; } = ""; public string Status { get; set; } = ""; public int You { get; set; } public int Version { get; set; }
        public DateTimeOffset? DeadlineAt { get; set; } public DateTimeOffset ServerNow { get; set; } public string? OpponentName { get; set; }
        public TeamState State { get; set; } = new(); public string? RoomCode { get; set; } public string? RewardsJson { get; set; }
    }

    public static class TeamCatalog
    {
        public static TeamRules? Rules;
        public static async System.Threading.Tasks.Task<TeamRules?> Ensure(ApiClient api)
        {
            if (Rules != null) return Rules;
            try { Rules = await api.Get<TeamRules>("/api/team/rules"); } catch (ApiError) { }
            return Rules;
        }
        public static string SkillName(TeamSkill s) => s.Name.TryGetValue(L.Language, out var n) ? n : s.Id;
    }

    /// <summary>Battle 3v3 hub: team (3 owned Pets), PvE / Boss / Practice, Casual and Ranked matchmaking, Private room,
    /// Friend invite, resume, match history.</summary>
    public sealed class TeamHubScreen : ScreenBase
    {
        public override string TitleKey => "team.title";
        private List<CardDto>? _cards;
        private List<long> _team = new();
        private List<PveStageDto>? _stages;
        private JObject? _active;
        private JArray? _history;
        private TMP_InputField? _code, _uid;
        private bool _subscribed;

        public override void Build()
        {
            float top = Wide ? 140 : 320;
            var list = UiKit.Scroll(UiKit.Rect(Root, "Body").Stretch(40, top, 40, 40));
            UiKit.VStack(list, 16, new RectOffset(0, 0, 10, 60));

            var kind = _active?["kind"]?.ToString();
            if (kind == "match") UiKit.Button(list, L.T("team.resume"), () => Nav.Push(new TeamBattleScreen(_active!["match"]!.ToObject<TeamView>(Newtonsoft.Json.JsonSerializer.Create(ApiClient.Json))!)), UiKit.ButtonStyle.Gold, 36).Size(-1, 120);
            else if (kind == "queue") { UiKit.Text(list, L.T("team.searching"), 32, UiKit.Sapphire, UiKit.Weight.Bold).Size(-1, 60); UiKit.Button(list, L.T("team.leave_queue"), LeaveQueue, UiKit.ButtonStyle.Secondary, 30).Size(-1, 100); }
            else if (kind == "room") UiKit.Text(list, L.T("team.room_waiting", _active!["roomCode"]?.ToString() ?? ""), 32, UiKit.Sapphire, UiKit.Weight.ExtraBold).Size(-1, 80);

            // team picker: tap 3 Pets
            UiKit.Text(list, L.T("team.your_team", _team.Count), 36, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
            var grid = UiKit.Rect(list, "Cards"); grid.Size(-1, 260);
            var h = UiKit.HStack(grid, 10, null, TextAnchor.MiddleLeft); h.childForceExpandWidth = false;
            foreach (var c in (_cards ?? new List<CardDto>()).Take(8))
            {
                var id = c.Id; bool on = _team.Contains(id);
                var cell = UiKit.Panel(grid, on ? UiKit.Hex("#FFF1C9") : UiKit.White, 28, "C" + id); cell.Size(200, 250);
                var art = UiKit.Icon(cell.transform, null, new Vector2(160, 160), "Art"); ((RectTransform)art.transform).Place(new Vector2(0.5f, 1), new Vector2(160, 160), new Vector2(0, -8), new Vector2(0.5f, 1));
                LobbyArt.Load(art, "pet/" + c.FormId);
                UiKit.Text(cell.transform, $"{L.T("pet." + c.FormId)}\n<size=75%>⚔{c.Atk} ❤{c.Hp}</size>", 22, UiKit.Ink, UiKit.Weight.Bold).rectTransform.Stretch(6, 170, 6, 6);
                cell.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (!_team.Remove(id) && _team.Count < 3) _team.Add(id); Redraw(); });
            }
            var save = UiKit.Button(list, L.T("team.save"), SaveTeam, UiKit.ButtonStyle.Primary, 32); save.Size(-1, 100); save.interactable = _team.Count == 3;

            bool free = kind == null;
            var modes = UiKit.Rect(list, "Modes"); modes.Size(-1, 120); UiKit.HStack(modes, 12).childForceExpandWidth = true;
            UiKit.Button(modes, L.T("team.casual"), () => Queue("casual"), UiKit.ButtonStyle.Primary, 30).Size(-1, 120, 1);
            UiKit.Button(modes, L.T("team.ranked"), () => Queue("ranked"), UiKit.ButtonStyle.Gold, 30).Size(-1, 120, 1);
            UiKit.Button(modes, L.T("team.practice"), () => StartPve(_stages?.FirstOrDefault()?.Id ?? "c1_s1", "practice"), UiKit.ButtonStyle.Ghost, 30).Size(-1, 120, 1);

            var room = UiKit.Panel(list, UiKit.White, 28, "Room"); room.Size(-1, 130);
            _code = UiKit.Input(room.transform, L.T("team.room_code")); ((RectTransform)_code.transform).Stretch(20, 18, 520, 18);
            var join = UiKit.Button(room.transform, L.T("team.join"), JoinRoom, UiKit.ButtonStyle.Secondary, 28); ((RectTransform)join.transform).Place(new Vector2(1, 0.5f), new Vector2(230, 94), new Vector2(-260, 0), new Vector2(1, 0.5f));
            var create = UiKit.Button(room.transform, L.T("team.create_room"), CreateRoom, UiKit.ButtonStyle.Primary, 28); ((RectTransform)create.transform).Place(new Vector2(1, 0.5f), new Vector2(230, 94), new Vector2(-16, 0), new Vector2(1, 0.5f));
            join.interactable = create.interactable = free;
            var friend = UiKit.Panel(list, UiKit.White, 28, "Friend"); friend.Size(-1, 130);
            _uid = UiKit.Input(friend.transform, L.T("team.friend_uid")); ((RectTransform)_uid.transform).Stretch(20, 18, 280, 18);
            var inv = UiKit.Button(friend.transform, L.T("team.invite"), Invite, UiKit.ButtonStyle.Secondary, 28); ((RectTransform)inv.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 94), new Vector2(-16, 0), new Vector2(1, 0.5f)); inv.interactable = free;

            UiKit.Text(list, L.T("team.pve"), 36, UiKit.Sapphire, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
            foreach (var s in (_stages ?? new List<PveStageDto>()).Take(12))
            {
                var r = UiKit.Panel(list, s.Unlocked ? UiKit.White : UiKit.SkyDeep, 28, s.Id); r.Size(-1, 120);
                UiKit.Text(r.transform, L.T("stage." + s.Id), 32, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(30, 0, 560, 0);
                var id = s.Id;
                var pve = UiKit.Button(r.transform, L.T("duel.fight"), () => StartPve(id, "pve"), UiKit.ButtonStyle.Gold, 28); ((RectTransform)pve.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 90), new Vector2(-270, 0), new Vector2(1, 0.5f));
                var boss = UiKit.Button(r.transform, L.T("team.boss"), () => StartPve(id, "boss"), UiKit.ButtonStyle.Secondary, 28); ((RectTransform)boss.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 90), new Vector2(-20, 0), new Vector2(1, 0.5f));
                pve.interactable = boss.interactable = s.Unlocked && free;
            }
            UiKit.Text(list, L.T("team.history"), 36, UiKit.Sapphire, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
            foreach (var x in (_history ?? new JArray()).Take(10))
                UiKit.Text(list, $"{x["result"]} · {x["mode"]} · {x["opponent"]} · {x["endReason"]}", 26, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 44);
        }

        private void Redraw() { if (Root == null) return; UiKit.Clear(Root); Build(); }

        public override async void OnShow()
        {
            if (Realtime.I != null && !_subscribed) { _subscribed = true; Realtime.I.Message += OnRealtime; }
            await TeamCatalog.Ensure(Api);
            var page = await Call(() => Api.Get<CardPageDto>("/api/cards?sort=power&pageSize=60"), overlay: false);
            _cards = page?.Items ?? new List<CardDto>();
            var lineups = await Call(() => Api.Get<JArray>("/api/team/lineups"), overlay: false);
            var def = lineups?.FirstOrDefault(l => l["isDefault"]?.Value<bool>() == true);
            _team = def?["cardIds"]?.ToObject<List<long>>() ?? _cards.Take(3).Select(c => c.Id).ToList();
            _stages = await Call(() => Api.Get<List<PveStageDto>>("/api/pve/stages"), overlay: false);
            _active = await Call(() => Api.Get<JObject>("/api/team/active"), overlay: false);
            _history = await Call(() => Api.Get<JArray>("/api/team/history"), overlay: false);
            Redraw();
        }

        public override void OnHide() { if (Realtime.I != null && _subscribed) { _subscribed = false; Realtime.I.Message -= OnRealtime; } }

        private void OnRealtime(string type, JToken? data)
        {
            if (type == "team_match_found") { Nav.Toast(L.T("team.match_found")); OnShow(); }
            if (type != "team_invite" || data == null) return;
            var id = data["matchId"]?.ToString(); if (id == null) return;
            var box = Nav.Popup(new Vector2(900, 420), new Vector2(900, 420), out var close);
            UiKit.Text(box, L.T("team.invited_by", data["from"]?.ToString() ?? "?"), 38, UiKit.Ink, UiKit.Weight.Bold).rectTransform.Stretch(30, 30, 30, 170);
            var row = UiKit.Rect(box, "Row"); row.Place(new Vector2(0.5f, 0), new Vector2(800, 110), new Vector2(0, 30), new Vector2(0.5f, 0));
            UiKit.HStack(row, 16).childForceExpandWidth = true;
            UiKit.Button(row, L.T("tduel.accept"), async () => { close(); var v = await Call(() => Api.Post<TeamView>($"/api/team/matches/{id}/accept", new { })); if (v != null) Nav.Push(new TeamBattleScreen(v)); }, UiKit.ButtonStyle.Gold, 34).Size(-1, 110, 1);
            UiKit.Button(row, L.T("tduel.decline"), async () => { close(); await Call(() => Api.Post<JObject>($"/api/team/matches/{id}/decline", new { }), overlay: false); }, UiKit.ButtonStyle.Secondary, 34).Size(-1, 110, 1);
        }

        private async void SaveTeam()
        {
            var r = await Call(() => Api.Post<JObject>("/api/team/lineups", new { name = "Main", isDefault = true, cardIds = _team }));
            if (r != null) Nav.Toast(L.T("team.saved"));
        }

        private async void StartPve(string stageId, string kind)
        {
            var v = await Call(() => Api.Post<TeamView>("/api/team/pve", new { stageId, kind }, ApiClient.NewIdempotencyKey()));
            if (v != null) Nav.Push(new TeamBattleScreen(v));
        }

        private async void Queue(string mode)
        {
            var r = await Call(() => Api.Post<JObject>("/api/team/queue", new { mode }));
            if (r == null) return;
            if (r["status"]?.ToString() == "matched") { var v = await Call(() => Api.Get<TeamView>($"/api/team/matches/{r["matchId"]}")); if (v != null) Nav.Push(new TeamBattleScreen(v)); }
            else OnShow();
        }

        private async void LeaveQueue() { await Call(() => Api.Delete<JObject>("/api/team/queue")); OnShow(); }
        private async void CreateRoom() { var r = await Call(() => Api.Post<JObject>("/api/team/rooms", new { })); if (r != null) { Nav.Toast(L.T("team.room_created", r["roomCode"]?.ToString() ?? "")); OnShow(); } }
        private async void JoinRoom()
        {
            var code = _code?.text.Trim() ?? "";
            if (code.Length != 6) { Nav.Toast(L.T("team.room_code_invalid"), true); return; }
            var v = await Call(() => Api.Post<TeamView>($"/api/team/rooms/{code}/join", new { }));
            if (v != null) Nav.Push(new TeamBattleScreen(v));
        }
        private async void Invite()
        {
            if (_uid == null || !long.TryParse(_uid.text.Trim(), out var uid)) { Nav.Toast(L.T("tduel.uid_invalid"), true); return; }
            var r = await Call(() => Api.Post<JObject>("/api/team/invite", new { uid }));
            if (r != null) Nav.Toast(L.T("tduel.challenge_sent"));
        }
    }

    /// <summary>
    /// Battle 3v3 board (landscape). Each round: pick an order for each of your 3 Pets (basic / skill / ultimate / guard, tap an
    /// enemy to target) and lock; the server resolves when both sides locked (or the timer expires) and returns your view.
    /// The opponent's plan is never sent to this client before the round resolves.
    /// </summary>
    public sealed class TeamBattleScreen : ScreenBase
    {
        public override bool IsDuel => true;
        public override bool ShowCurrencyBar => false;
        public override bool ShowBackButton => false;
        private TeamView _v;
        private int _seen;
        private bool _busy, _subscribed, _ticking;
        private int _sel;
        private readonly Dictionary<int, TeamOrder> _orders = new();
        private readonly Dictionary<int, RectTransform> _rects = new();
        private TextMeshProUGUI? _timer;
        private Runner _run = null!;

        public TeamBattleScreen(TeamView v) { _v = v; _seen = v.State.Log.Count; }
        private TeamState St => _v.State;
        private TeamRules R => TeamCatalog.Rules!;
        private int Me => _v.You;
        private bool Live => _v.Status == "active" && !St.Finished;

        public override async void OnShow()
        {
            await TeamCatalog.Ensure(Api);
            if (Realtime.I != null && !_subscribed) { _subscribed = true; Realtime.I.Message += OnRealtime; Realtime.I.Connected += OnReconnect; }
            Redraw();
        }

        public override void OnHide() { if (Realtime.I != null && _subscribed) { _subscribed = false; Realtime.I.Message -= OnRealtime; Realtime.I.Connected -= OnReconnect; } }
        private void OnRealtime(string type, JToken? data) { if (type == "team_update" && data?["matchId"]?.ToString() == _v.Id.ToString()) _ = Refresh(); }
        private void OnReconnect() => _ = Refresh();

        private async System.Threading.Tasks.Task Refresh()
        {
            if (_busy) return;
            var v = await Call(() => Api.Get<TeamView>($"/api/team/matches/{_v.Id}"), overlay: false);
            if (v != null && (v.Version != _v.Version || v.Status != _v.Status)) Show(v);
        }

        private void Show(TeamView v)
        {
            var fresh = v.State.Log.Skip(Math.Min(_seen, v.State.Log.Count)).ToList();
            _v = v; _seen = v.State.Log.Count; _orders.Clear();
            Redraw();
            if (fresh.Count > 0) _run.StartCoroutine(PlayFx(fresh));
        }

        private void Redraw() { if (Root == null || TeamCatalog.Rules == null) return; UiKit.Clear(Root); Build(); }

        public override void Build()
        {
            if (TeamCatalog.Rules == null) return;
            _run = Root.gameObject.GetComponent<Runner>() ?? Root.gameObject.AddComponent<Runner>();
            if (!_ticking) { _ticking = true; _run.StartCoroutine(Tick()); }
            var bg = UiKit.Icon(Root, null, Vector2.zero, "Bg"); ((RectTransform)bg.transform).Stretch(); LobbyArt.Load(bg, "ui/bg_duel_board_landscape"); bg.preserveAspect = false;
            _rects.Clear();
            var top = UiKit.Text(Root, $"{L.T("team.round", St.Round, R.Config.MaxRounds)}   ·   {St.Players[Me].Name}  vs  {_v.OpponentName}", 34, Color.white, UiKit.Weight.ExtraBold);
            top.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(1500, 70), new Vector2(0, -16), new Vector2(0.5f, 1));
            _timer = UiKit.Text(Root, "", 34, UiKit.Gold, UiKit.Weight.ExtraBold); _timer.rectTransform.Place(new Vector2(1, 1), new Vector2(200, 70), new Vector2(-30, -16), new Vector2(1, 1));
            var mine = St.Units.Where(u => u.Side == Me).OrderBy(u => u.Slot).ToList();
            var foes = St.Units.Where(u => u.Side != Me).OrderBy(u => u.Slot).ToList();
            if (_sel == 0 || mine.All(u => u.Uid != _sel || !u.Alive)) _sel = mine.FirstOrDefault(u => u.Alive)?.Uid ?? 0;
            for (int i = 0; i < 3; i++)
            {
                if (i < mine.Count) Unit(mine[i], new Vector2(0.25f + (i == 1 ? -0.06f : 0), 0.78f - i * 0.2f), true);
                if (i < foes.Count) Unit(foes[i], new Vector2(0.75f + (i == 1 ? 0.06f : 0), 0.78f - i * 0.2f), false);
            }
            Panel(mine);
            if (!Live) Result();
        }

        private void Unit(TeamUnit u, Vector2 anchor, bool mine)
        {
            var rt = UiKit.Rect(Root, "U" + u.Uid); rt.Place(anchor, new Vector2(520, 170), Vector2.zero);
            var img = rt.gameObject.AddComponent<Image>(); img.sprite = UiKit.RoundedSprite(26); img.type = Image.Type.Sliced;
            bool targeted = !mine && _orders.Values.Any(o => o.Target == u.Uid);
            img.color = !u.Alive ? new Color(0.4f, 0.4f, 0.45f) : mine && u.Uid == _sel ? UiKit.Gold : targeted ? UiKit.Danger : Color.white;
            _rects[u.Uid] = rt;
            var art = UiKit.Icon(rt, null, new Vector2(150, 150), "Art"); ((RectTransform)art.transform).Place(new Vector2(0, 0.5f), new Vector2(150, 150), new Vector2(10, 0), new Vector2(0, 0.5f)); art.raycastTarget = false;
            LobbyArt.Load(art, "pet/" + u.FormId);
            UiKit.Text(rt, L.T("pet." + u.FormId), 28, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.TopLeft).rectTransform.Stretch(170, 12, 12, 110);
            Bar(rt, 70, (float)u.Hp / Math.Max(1, u.MaxHp), u.Hp * 100 < u.MaxHp * 30 ? UiKit.Danger : UiKit.Hex("#43BD78"), 18);
            Bar(rt, 96, (float)u.En / R.Config.MaxEn, UiKit.Hex("#3D9BF2"), 10);
            var st = string.Join(" ", u.Statuses.Where(s => s.Type != "guard").Select(s => L.T("team.status." + s.Type)));
            UiKit.Text(rt, $"{u.Hp}/{u.MaxHp} · {u.En} EN{(u.Shield > 0 ? $" · 🛡{u.Shield}" : "")}  {st}", 20, UiKit.InkSoft, UiKit.Weight.Bold, TextAlignmentOptions.TopLeft).rectTransform.Stretch(170, 116, 12, 10);
            if (mine && _orders.TryGetValue(u.Uid, out var o)) UiKit.Text(rt, o.Slot == "guard" ? "🧱" : o.Slot == "basic" ? "⚔" : o.Slot == "skill" ? "✦" : "★", 36, UiKit.Sapphire, UiKit.Weight.ExtraBold).rectTransform.Place(new Vector2(1, 1), new Vector2(60, 60), new Vector2(-6, -6), new Vector2(1, 1));
            var uid = u.Uid;
            rt.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (mine) { _sel = uid; Redraw(); } else Target(uid); });
        }

        private static void Bar(RectTransform parent, float y, float pct, Color color, float h)
        {
            var bg = UiKit.Panel(parent, new Color(0, 0, 0, 0.18f), 8, "Bar"); var brt = (RectTransform)bg.transform;
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0, 1); brt.offsetMin = new Vector2(170, -y - h); brt.offsetMax = new Vector2(-14, -y); bg.raycastTarget = false;
            var fill = UiKit.Panel(bg.transform, color, 8, "Fill"); var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(Mathf.Clamp01(pct), 1); frt.offsetMin = frt.offsetMax = Vector2.zero; fill.raycastTarget = false;
        }

        private void Panel(List<TeamUnit> mine)
        {
            var p = UiKit.Panel(Root, new Color(0.04f, 0.08f, 0.24f, 0.8f), 0, "Panel"); var prt = (RectTransform)p.transform;
            prt.anchorMin = new Vector2(0, 0); prt.anchorMax = new Vector2(1, 0); prt.pivot = new Vector2(0.5f, 0); prt.sizeDelta = new Vector2(0, 200); prt.anchoredPosition = Vector2.zero;
            var row = UiKit.Rect(p.transform, "Row").Stretch(20, 14, 320, 14);
            UiKit.HStack(row, 12).childForceExpandWidth = true;
            var u = mine.FirstOrDefault(x => x.Uid == _sel);
            bool canPlan = Live && TeamEngine.MustAct(St, Me) && u != null;
            foreach (var slot in new[] { "basic", "skill", "ultimate", "guard" })
            {
                var sk = slot == "guard" || u == null ? null : TeamEngine.SkillFor(R, u, slot);
                if (slot != "guard" && sk == null) continue;
                string label = slot == "guard" ? L.T("team.guard") : $"{TeamCatalog.SkillName(sk!)}\n<size=70%>{(sk!.Cost > 0 ? sk.Cost + " EN" : "+" + R.Config.BasicEnGain + " EN")}</size>";
                var b = UiKit.Button(row, label, () => Order(slot), _orders.TryGetValue(_sel, out var o) && o.Slot == slot ? UiKit.ButtonStyle.Gold : UiKit.ButtonStyle.Secondary, 24);
                b.Size(-1, 170, 1);
                b.interactable = canPlan && TeamEngine.OrderProblem(R, St, Me, new TeamOrder { Unit = _sel, Slot = slot }) == null;
            }
            var right = UiKit.Rect(p.transform, "Right"); right.Place(new Vector2(1, 0.5f), new Vector2(290, 170), new Vector2(-16, 0), new Vector2(1, 0.5f));
            UiKit.VStack(right, 8);
            var lockB = UiKit.Button(right, Live && !TeamEngine.MustAct(St, Me) ? L.T("team.waiting") : L.T("team.lock"), Lock, UiKit.ButtonStyle.Gold, 28); lockB.Size(290, 90);
            lockB.interactable = Live && TeamEngine.MustAct(St, Me);
            if (Live) UiKit.Button(right, L.T("duel.surrender"), () => Send(new TeamAction { Type = "surrender" }), UiKit.ButtonStyle.Ghost, 22).Size(290, 70);
        }

        private void Order(string slot)
        {
            var prev = _orders.TryGetValue(_sel, out var o) ? o.Target : 0;
            _orders[_sel] = new TeamOrder { Unit = _sel, Slot = slot, Target = prev };
            Redraw();
        }

        private void Target(int uid)
        {
            if (!_orders.TryGetValue(_sel, out var o)) o = new TeamOrder { Unit = _sel, Slot = "basic" };
            var u = St.Units.First(x => x.Uid == _sel);
            var sk = o.Slot == "guard" ? null : TeamEngine.SkillFor(R, u, o.Slot);
            if (sk != null && sk.Target != "enemy_single") return;
            _orders[_sel] = new TeamOrder { Unit = _sel, Slot = o.Slot == "guard" ? "basic" : o.Slot, Target = uid };
            var next = St.Units.Where(x => x.Side == Me && x.Alive && !_orders.ContainsKey(x.Uid)).OrderBy(x => x.Slot).FirstOrDefault();
            if (next != null) _sel = next.Uid;
            Redraw();
        }

        private void Lock() => Send(new TeamAction { Type = "plan", Orders = _orders.Values.ToList() });

        private async void Send(TeamAction a)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var v = await Call(() => Api.Post<TeamView>($"/api/team/matches/{_v.Id}/actions", new { actionId = Guid.NewGuid().ToString("N"), version = _v.Version, action = a }), overlay: false);
                if (v != null) Show(v); else { var f = await Call(() => Api.Get<TeamView>($"/api/team/matches/{_v.Id}"), overlay: false); if (f != null) Show(f); }
            }
            finally { _busy = false; }
        }

        private IEnumerator PlayFx(List<TeamEvent> evs)
        {
            foreach (var e in evs.Take(60))
            {
                string? txt = e.Type switch
                {
                    "damage" => $"−{e.N}", "heal" => $"+{e.N}", "counter" => $"↩−{e.N}", "burn" => $"🔥−{e.N}", "poison" => $"☠−{e.N}",
                    "down" => L.T("team.fx_down"), "status" => L.T("team.status." + e.S), "skip" => L.T("team.fx_skip"), _ => null,
                };
                if (txt == null) continue;
                int target = e.Type is "skip" ? e.A : e.B;
                var host = _rects.TryGetValue(target, out var r) && r != null ? r : (RectTransform)Root;
                var t = UiKit.Text(host, txt, e.Type == "damage" ? 48 : 34, e.Type is "heal" ? UiKit.Hex("#43BD78") : e.Type is "status" or "skip" ? UiKit.Gold : UiKit.Danger, UiKit.Weight.ExtraBold);
                t.rectTransform.Place(new Vector2(0.5f, 0.7f), new Vector2(400, 70), Vector2.zero); t.raycastTarget = false;
                t.gameObject.AddComponent<FloatUp>();
                yield return new WaitForSeconds(0.18f);
            }
        }

        private IEnumerator Tick()
        {
            float poll = 0;
            while (Root != null)
            {
                if (_timer != null && _v.DeadlineAt is DateTimeOffset dl && Live)
                {
                    var left = dl - _v.ServerNow - (DateTimeOffset.UtcNow - _v.ServerNow);
                    _timer.text = left.TotalSeconds > 0 ? $"⏱ {(int)left.TotalSeconds}" : "⏱ …";
                }
                poll += 1;
                if (Live && !TeamEngine.MustAct(St, Me) && poll >= 2) { poll = 0; _ = Refresh(); }
                yield return new WaitForSeconds(1f);
            }
            _ticking = false;
        }

        private void Result()
        {
            var o = UiKit.Panel(Root, new Color(0.02f, 0.04f, 0.12f, 0.75f), 0, "Over"); ((RectTransform)o.transform).Stretch();
            var box = UiKit.Panel(o.transform, UiKit.White, 40, "Box", shadow: true); ((RectTransform)box.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(900, 480), Vector2.zero);
            bool win = St.Winner == Me, draw = St.Winner == null;
            UiKit.Text(box.transform, draw ? L.T("tduel.draw") : win ? L.T("tduel.victory") : L.T("tduel.defeat"), 80, draw ? UiKit.Ink : win ? UiKit.GoldDeep : UiKit.Danger, UiKit.Weight.ExtraBold)
                .rectTransform.Place(new Vector2(0.5f, 1), new Vector2(840, 120), new Vector2(0, -30), new Vector2(0.5f, 1));
            UiKit.Text(box.transform, L.T("team.end." + (St.EndReason ?? "knockout")), 32, UiKit.InkSoft).rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(840, 80), new Vector2(0, 10));
            var back = UiKit.Button(box.transform, L.T("tduel.back_lobby"), () => Nav.Back(), UiKit.ButtonStyle.Gold, 34);
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0), new Vector2(400, 110), new Vector2(0, 30), new Vector2(0.5f, 0));
        }

        private sealed class Runner : MonoBehaviour { }
    }
}
