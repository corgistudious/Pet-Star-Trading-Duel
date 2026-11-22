using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using PetStar.Rules.Arena;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    // ---------------------------------------------------------------- wire types (mirror backend Features/ArenaEndpoints.cs)
    public sealed class ArenaDeckWire { public long Id { get; set; } public string Name { get; set; } = ""; public bool IsDefault { get; set; } public List<string> EnergyTypes { get; set; } = new(); public List<ArenaDeckEntry> Cards { get; set; } = new(); }
    public sealed class ArenaSaveDeck { public long? Id { get; set; } public string Name { get; set; } = ""; public bool IsDefault { get; set; } public List<string> EnergyTypes { get; set; } = new(); public List<ArenaDeckEntry> Cards { get; set; } = new(); }
    public sealed class ArenaPetOwned { public long CardId { get; set; } public string DefId { get; set; } = ""; public string Rarity { get; set; } = ""; public bool Busy { get; set; } }
    public sealed class ArenaCollectionWire { public string Version { get; set; } = ""; public List<ArenaPetOwned> Pets { get; set; } = new(); public Dictionary<string, long> Trainers { get; set; } = new(); }
    public sealed class ArenaView
    {
        public Guid Id { get; set; } public string Mode { get; set; } = ""; public string Status { get; set; } = ""; public int You { get; set; } public int Version { get; set; }
        public DateTimeOffset? DeadlineAt { get; set; } public DateTimeOffset ServerNow { get; set; } public string? OpponentName { get; set; }
        public ArenaState State { get; set; } = new(); public string? RoomCode { get; set; } public string? RewardsJson { get; set; }
    }
    public sealed class ArenaActionWire { public string ActionId { get; set; } = ""; public int Version { get; set; } public ArenaAction Action { get; set; } = new(); }

    /// <summary>Card definitions come from the server (/api/arena/cards) so the client never disagrees with the rules.</summary>
    public static class ArenaCatalog
    {
        public static ArenaRules? Rules;
        public static string Name(ArenaCardDef d) => d.Kind == "pet" ? L.T("pet." + d.Id) : (d.Name.TryGetValue(L.Language, out var n) ? n : d.Id);
        public static string Text(Dictionary<string, string> t) => t.TryGetValue(L.Language, out var s) ? s : t.Values.FirstOrDefault() ?? "";
        public static Color ElementColor(string? el) => el switch
        {
            "fire" => UiKit.Hex("#F2643D"), "water" => UiKit.Hex("#3D9BF2"), "nature" => UiKit.Hex("#55C26A"), "earth" => UiKit.Hex("#B98A52"),
            "lightning" => UiKit.Hex("#F5C83B"), "wind" => UiKit.Hex("#7FD6C9"), "light" => UiKit.Hex("#FFE7A3"), "dark" => UiKit.Hex("#6B4FA8"), _ => UiKit.Hex("#CFD6E4"),
        };
        public static async System.Threading.Tasks.Task<ArenaRules?> Ensure(ApiClient api)
        {
            if (Rules != null) return Rules;
            try { Rules = await api.Get<ArenaRules>("/api/arena/cards"); } catch (ApiError) { }
            return Rules;
        }
        internal static async void LoadSprite(Image img, string address)
        {
            var s = await GameDataClient.LoadSprite(address);
            if (img != null && s != null) { img.sprite = s; img.color = Color.white; }
        }
    }

    // ---------------------------------------------------------------- lobby

    /// <summary>Pet Battle Arena: PvE vs AI, Casual / Ranked matchmaking, Private Room by code, Friend Battle by UID,
    /// reconnect to a running match, match history and ranking.</summary>
    public sealed class ArenaLobbyScreen : ScreenBase
    {
        public override string TitleKey => "arena.title";
        private List<ArenaDeckWire>? _decks;
        private List<PveStageDto>? _stages;
        private JObject? _active;
        private JArray? _history;
        private TMP_InputField? _code, _uid;
        private bool _subscribed;

        public override void Build()
        {
            float top = Wide ? 140 : 320;
            var def = _decks?.FirstOrDefault(d => d.IsDefault);
            var list = UiKit.Scroll(UiKit.Rect(Root, "Body").Stretch(40, top, 40, 40));
            UiKit.VStack(list, 16, new RectOffset(0, 0, 10, 60));

            var head = UiKit.Panel(list, UiKit.White, 32, "Deck", shadow: true); head.Size(-1, 230);
            UiKit.Text(head.transform, def == null ? L.T("arena.no_deck") : L.T("arena.deck_info", def.Name, def.Cards.Sum(c => c.Count), string.Join(" ", def.EnergyTypes.Select(e => L.T("element." + e)))),
                32, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.TopLeft).rectTransform.Stretch(30, 20, 30, 120);
            var row = UiKit.Rect(head.transform, "Row"); row.Place(new Vector2(0.5f, 0), new Vector2(940, 96), new Vector2(0, 16), new Vector2(0.5f, 0));
            UiKit.HStack(row, 12).childForceExpandWidth = true;
            UiKit.Button(row, L.T("arena.deck_builder"), () => Nav.Push(new ArenaDeckBuilderScreen(def)), UiKit.ButtonStyle.Primary, 30).Size(-1, 96, 1);
            UiKit.Button(row, L.T("arena.auto_deck"), AutoDeck, UiKit.ButtonStyle.Secondary, 30).Size(-1, 96, 1);

            var kind = _active?["kind"]?.ToString();
            if (kind == "match")
                UiKit.Button(list, L.T("arena.resume"), () => Nav.Push(new ArenaBattleScreen(_active!["match"]!.ToObject<ArenaView>(Newtonsoft.Json.JsonSerializer.Create(ApiClient.Json))!)), UiKit.ButtonStyle.Gold, 36).Size(-1, 120);
            else if (kind == "queue")
            {
                UiKit.Text(list, L.T("arena.searching", L.T("arena.mode." + _active!["mode"])), 32, UiKit.Sapphire, UiKit.Weight.Bold).Size(-1, 70);
                UiKit.Button(list, L.T("arena.leave_queue"), LeaveQueue, UiKit.ButtonStyle.Secondary, 30).Size(-1, 100);
            }
            else if (kind == "room")
                UiKit.Text(list, L.T("arena.room_waiting", _active!["roomCode"]?.ToString() ?? ""), 34, UiKit.Sapphire, UiKit.Weight.ExtraBold).Size(-1, 90);

            bool can = def != null && kind == null;
            var modes = UiKit.Rect(list, "Modes"); modes.Size(-1, 120);
            UiKit.HStack(modes, 12).childForceExpandWidth = true;
            var casual = UiKit.Button(modes, L.T("arena.casual"), () => Queue("casual"), UiKit.ButtonStyle.Primary, 32); casual.Size(-1, 120, 1); casual.interactable = can;
            var ranked = UiKit.Button(modes, L.T("arena.ranked"), () => Queue("ranked"), UiKit.ButtonStyle.Gold, 32); ranked.Size(-1, 120, 1); ranked.interactable = can;

            var room = UiKit.Panel(list, UiKit.White, 28, "Room"); room.Size(-1, 130);
            _code = UiKit.Input(room.transform, L.T("arena.room_code")); ((RectTransform)_code.transform).Stretch(20, 18, 520, 18);
            var join = UiKit.Button(room.transform, L.T("arena.join"), JoinRoom, UiKit.ButtonStyle.Secondary, 28);
            ((RectTransform)join.transform).Place(new Vector2(1, 0.5f), new Vector2(230, 94), new Vector2(-260, 0), new Vector2(1, 0.5f)); join.interactable = can;
            var create = UiKit.Button(room.transform, L.T("arena.create_room"), CreateRoom, UiKit.ButtonStyle.Primary, 28);
            ((RectTransform)create.transform).Place(new Vector2(1, 0.5f), new Vector2(230, 94), new Vector2(-16, 0), new Vector2(1, 0.5f)); create.interactable = can;

            var friend = UiKit.Panel(list, UiKit.White, 28, "Friend"); friend.Size(-1, 130);
            _uid = UiKit.Input(friend.transform, L.T("arena.friend_uid")); ((RectTransform)_uid.transform).Stretch(20, 18, 280, 18);
            var inv = UiKit.Button(friend.transform, L.T("arena.invite"), Invite, UiKit.ButtonStyle.Secondary, 28);
            ((RectTransform)inv.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 94), new Vector2(-16, 0), new Vector2(1, 0.5f)); inv.interactable = can;

            UiKit.Text(list, L.T("arena.pve"), 38, UiKit.Sapphire, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 64);
            foreach (var s in (_stages ?? new List<PveStageDto>()).Take(12))
            {
                var r = UiKit.Panel(list, s.Unlocked ? UiKit.White : UiKit.SkyDeep, 28, s.Id); r.Size(-1, 120);
                UiKit.Text(r.transform, L.T("stage." + s.Id), 34, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(30, 0, 300, 0);
                var id = s.Id;
                var b = UiKit.Button(r.transform, s.Unlocked ? L.T("duel.fight") : L.T("duel.locked"), () => StartPve(id), s.Unlocked ? UiKit.ButtonStyle.Gold : UiKit.ButtonStyle.Secondary, 30);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 90), new Vector2(-20, 0), new Vector2(1, 0.5f));
                b.interactable = s.Unlocked && def != null && kind == null;
            }

            UiKit.Text(list, L.T("arena.history"), 38, UiKit.Sapphire, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 64);
            foreach (var h in (_history ?? new JArray()).Take(10))
            {
                string res = h["result"]?.ToString() ?? "";
                UiKit.Text(list, $"{L.T("arena.result." + res)} · {L.T("arena.mode." + h["mode"])} · {h["opponent"]} · {L.T("arena.end." + h["endReason"])}", 28,
                    res == "win" ? UiKit.Sapphire : res == "loss" ? UiKit.Danger : UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 50);
            }
        }

        public override async void OnShow()
        {
            if (Realtime.I != null && !_subscribed) { _subscribed = true; Realtime.I.Message += OnRealtime; }
            await ArenaCatalog.Ensure(Api);
            await Call(() => Api.Get<JObject>("/api/arena/collection"), overlay: false);          // grants the starter trainer set once
            _decks = await Call(() => Api.Get<List<ArenaDeckWire>>("/api/arena/decks"));
            _stages = await Call(() => Api.Get<List<PveStageDto>>("/api/pve/stages"), overlay: false);
            _active = await Call(() => Api.Get<JObject>("/api/arena/active"), overlay: false);
            _history = await Call(() => Api.Get<JArray>("/api/arena/history"), overlay: false);
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }

        public override void OnHide() { if (Realtime.I != null && _subscribed) { _subscribed = false; Realtime.I.Message -= OnRealtime; } }

        private void OnRealtime(string type, JToken? data)
        {
            if (type == "arena_match_found") { Nav.Toast(L.T("arena.match_found")); OnShow(); }
            if (type != "arena_invite" || data == null) return;
            var id = data["matchId"]?.ToString(); var from = data["from"]?.ToString() ?? "?";
            if (id == null) return;
            var box = Nav.Popup(new Vector2(900, 420), new Vector2(900, 420), out var close);
            UiKit.Text(box, L.T("arena.invited_by", from), 38, UiKit.Ink, UiKit.Weight.Bold).rectTransform.Stretch(30, 30, 30, 170);
            var row = UiKit.Rect(box, "Row"); row.Place(new Vector2(0.5f, 0), new Vector2(800, 110), new Vector2(0, 30), new Vector2(0.5f, 0));
            UiKit.HStack(row, 16).childForceExpandWidth = true;
            UiKit.Button(row, L.T("tduel.accept"), async () => { close(); var v = await Call(() => Api.Post<ArenaView>($"/api/arena/matches/{id}/accept", new { })); if (v != null) Nav.Push(new ArenaBattleScreen(v)); }, UiKit.ButtonStyle.Gold, 34).Size(-1, 110, 1);
            UiKit.Button(row, L.T("tduel.decline"), async () => { close(); await Call(() => Api.Post<JObject>($"/api/arena/matches/{id}/decline", new { }), overlay: false); }, UiKit.ButtonStyle.Secondary, 34).Size(-1, 110, 1);
        }

        private async void AutoDeck()
        {
            var auto = await Call(() => Api.Post<JObject>("/api/arena/decks/auto", null));
            if (auto == null) return;
            var js = Newtonsoft.Json.JsonSerializer.Create(ApiClient.Json);
            var def = _decks?.FirstOrDefault(d => d.IsDefault);
            var saved = await Call(() => Api.Post<ArenaDeckWire>("/api/arena/decks", new ArenaSaveDeck
            {
                Id = def?.Id, Name = def?.Name ?? L.T("duel.default_deck_name"), IsDefault = true,
                Cards = auto["cards"]!.ToObject<List<ArenaDeckEntry>>(js)!, EnergyTypes = auto["energyTypes"]!.ToObject<List<string>>(js)!,
            }));
            if (saved != null) { Nav.Toast(L.T("duel.deck_saved")); OnShow(); }
        }

        private async void StartPve(string stageId)
        {
            var v = await Call(() => Api.Post<ArenaView>("/api/arena/pve", new { stageId }, ApiClient.NewIdempotencyKey()));
            if (v != null) Nav.Push(new ArenaBattleScreen(v));
        }

        private async void Queue(string mode)
        {
            var r = await Call(() => Api.Post<JObject>("/api/arena/queue", new { mode }));
            if (r == null) return;
            if (r["status"]?.ToString() == "matched") { var v = await Call(() => Api.Get<ArenaView>($"/api/arena/matches/{r["matchId"]}")); if (v != null) Nav.Push(new ArenaBattleScreen(v)); }
            else OnShow();
        }

        private async void LeaveQueue() { await Call(() => Api.Delete<JObject>("/api/arena/queue")); OnShow(); }

        private async void CreateRoom()
        {
            var r = await Call(() => Api.Post<JObject>("/api/arena/rooms", new { }));
            if (r != null) { Nav.Toast(L.T("arena.room_created", r["roomCode"]?.ToString() ?? "")); OnShow(); }
        }

        private async void JoinRoom()
        {
            var code = _code?.text.Trim() ?? "";
            if (code.Length != 6) { Nav.Toast(L.T("arena.room_code_invalid"), true); return; }
            var v = await Call(() => Api.Post<ArenaView>($"/api/arena/rooms/{code}/join", new { }));
            if (v != null) Nav.Push(new ArenaBattleScreen(v));
        }

        private async void Invite()
        {
            if (_uid == null || !long.TryParse(_uid.text.Trim(), out var uid)) { Nav.Toast(L.T("tduel.uid_invalid"), true); return; }
            var r = await Call(() => Api.Post<JObject>("/api/arena/invite", new { uid }));
            if (r != null) Nav.Toast(L.T("tduel.challenge_sent"));
        }
    }

    // ---------------------------------------------------------------- 20-card deck builder

    /// <summary>20 cards, max 2 of the same card, at least one Basic Pet, 1–3 Energy types. The server re-validates
    /// ownership, locks and legality when saving and again when a match starts.</summary>
    public sealed class ArenaDeckBuilderScreen : ScreenBase
    {
        public override string TitleKey => "arena.deck_builder";
        private readonly ArenaDeckWire? _deck;
        private ArenaCollectionWire? _col;
        private readonly HashSet<long> _pets = new();
        private readonly Dictionary<string, int> _trainers = new();
        private readonly List<string> _energy = new();
        private string _tab = "pet";

        public ArenaDeckBuilderScreen(ArenaDeckWire? deck)
        {
            _deck = deck;
            if (deck == null) return;
            foreach (var c in deck.Cards) if (c.CardInstanceId > 0) _pets.Add(c.CardInstanceId); else _trainers[c.DefId] = c.Count;
            _energy.AddRange(deck.EnergyTypes);
        }

        private ArenaRules? R => ArenaCatalog.Rules;
        private int Total => _pets.Count + _trainers.Values.Sum();

        public override void Build()
        {
            if (R == null || _col == null) return;
            float top = Wide ? 140 : 320;
            var head = UiKit.Rect(Root, "Head"); head.Place(new Vector2(0.5f, 1), new Vector2(Wide ? 1500 : 1000, 100), new Vector2(0, -top), new Vector2(0.5f, 1));
            UiKit.HStack(head, 10).childForceExpandWidth = true;
            var count = UiKit.Text(head, L.T("arena.count", Total, R.Config.DeckSize), 32, Total == R.Config.DeckSize ? UiKit.Sapphire : UiKit.Danger, UiKit.Weight.ExtraBold); count.Size(-1, 100, 1.4f);
            foreach (var (tab, key) in new[] { ("pet", "arena.tab_pets"), ("trainer", "arena.tab_trainers"), ("energy", "arena.tab_energy") })
            {
                var t = tab;
                UiKit.Button(head, L.T(key), () => { _tab = t; Rebuild(); }, _tab == tab ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 26).Size(-1, 92, 1);
            }
            UiKit.Button(head, L.T("tduel.save"), Save, UiKit.ButtonStyle.Gold, 28).Size(-1, 92, 1);

            var list = UiKit.Scroll(UiKit.Rect(Root, "List").Stretch(40, top + 120, 40, 40));
            UiKit.VStack(list, 10, new RectOffset(0, 0, 6, 40));
            if (_tab == "pet")
            {
                foreach (var p in _col.Pets.Where(p => R.Has(p.DefId)).OrderBy(p => R.Def(p.DefId).Stage).ThenBy(p => p.DefId, StringComparer.Ordinal))
                {
                    var d = R.Def(p.DefId); long id = p.CardId; bool on = _pets.Contains(id);
                    int sameDef = _col.Pets.Count(x => _pets.Contains(x.CardId) && x.DefId == p.DefId);
                    var r = UiKit.Panel(list, on ? UiKit.Mint : UiKit.White, 26, "P" + id); r.Size(-1, 120);
                    var art = UiKit.Icon(r.transform, null, new Vector2(100, 100), "Art"); ((RectTransform)art.transform).Place(new Vector2(0, 0.5f), new Vector2(100, 100), new Vector2(12, 0), new Vector2(0, 0.5f));
                    ArenaCatalog.LoadSprite(art, "pet/" + d.Id);
                    var stage = d.Stage == 0 ? "Basic" : $"Stage {d.Stage}";
                    UiKit.Text(r.transform, $"{ArenaCatalog.Name(d)}{(d.Ex ? " EX" : "")} · {stage} · HP {d.Hp} · {L.T("element." + d.Element)}", 26, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(126, 0, 250, 0);
                    var b = UiKit.Button(r.transform, on ? L.T("tduel.remove") : p.Busy ? L.T("tduel.busy") : L.T("tduel.add"), () => { if (!_pets.Remove(id)) _pets.Add(id); Rebuild(); }, on ? UiKit.ButtonStyle.Secondary : UiKit.ButtonStyle.Primary, 26);
                    ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(210, 86), new Vector2(-14, 0), new Vector2(1, 0.5f));
                    b.interactable = on || (!p.Busy && sameDef < R.Config.MaxCopies && Total < R.Config.DeckSize);
                }
            }
            else if (_tab == "trainer")
            {
                foreach (var d in R.Cards.Where(c => c.Kind != "pet"))
                {
                    long owned = _col.Trainers.TryGetValue(d.Id, out var o) ? o : 0;
                    int n = _trainers.TryGetValue(d.Id, out var k) ? k : 0; var id = d.Id;
                    var r = UiKit.Panel(list, d.Kind == "supporter" ? UiKit.Lilac : d.Kind == "tool" ? UiKit.Peach : UiKit.Mint, 26, d.Id); r.Size(-1, 150);
                    UiKit.Text(r.transform, $"{ArenaCatalog.Name(d)} · {L.T("arena.kind." + d.Kind)} · {L.T("tduel.owned", owned)}", 28, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(22, 10, 340, 84);
                    UiKit.Text(r.transform, ArenaCatalog.Text(d.Text), 22, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.TopLeft).rectTransform.Stretch(22, 70, 340, 8);
                    var minus = UiKit.Button(r.transform, "−", () => Set(id, n - 1), UiKit.ButtonStyle.Secondary, 40);
                    ((RectTransform)minus.transform).Place(new Vector2(1, 0.5f), new Vector2(96, 86), new Vector2(-226, 0), new Vector2(1, 0.5f)); minus.interactable = n > 0;
                    UiKit.Text(r.transform, n.ToString(), 40, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Place(new Vector2(1, 0.5f), new Vector2(96, 86), new Vector2(-122, 0), new Vector2(1, 0.5f));
                    var plus = UiKit.Button(r.transform, "+", () => Set(id, n + 1), UiKit.ButtonStyle.Primary, 40);
                    ((RectTransform)plus.transform).Place(new Vector2(1, 0.5f), new Vector2(96, 86), new Vector2(-14, 0), new Vector2(1, 0.5f));
                    plus.interactable = n < Math.Min(R.Config.MaxCopies, owned) && Total < R.Config.DeckSize;
                }
            }
            else
            {
                UiKit.Text(list, L.T("arena.energy_hint", R.Config.MaxEnergyTypes), 28, UiKit.InkSoft).Size(-1, 90);
                foreach (var el in R.Elements)
                {
                    var e = el; bool on = _energy.Contains(el);
                    var b = UiKit.Button(list, (on ? "✓ " : "") + L.T("element." + el), () => { if (!_energy.Remove(e) && _energy.Count < R.Config.MaxEnergyTypes) _energy.Add(e); Rebuild(); }, on ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 30);
                    b.Size(-1, 96);
                }
            }
        }

        private void Set(string id, int n) { if (n <= 0) _trainers.Remove(id); else _trainers[id] = n; Rebuild(); }
        private void Rebuild() { if (Root == null) return; UiKit.Clear(Root); Build(); }

        public override async void OnShow()
        {
            await ArenaCatalog.Ensure(Api);
            _col = await Call(() => Api.Get<ArenaCollectionWire>("/api/arena/collection"));
            Rebuild();
        }

        private async void Save()
        {
            if (_col == null) return;
            var cards = _pets.Select(id => new ArenaDeckEntry { DefId = _col.Pets.First(p => p.CardId == id).DefId, CardInstanceId = id, Count = 1 })
                .Concat(_trainers.Select(kv => new ArenaDeckEntry { DefId = kv.Key, Count = kv.Value })).ToList();
            var saved = await Call(() => Api.Post<ArenaDeckWire>("/api/arena/decks", new ArenaSaveDeck { Id = _deck?.Id, Name = _deck?.Name ?? L.T("duel.default_deck_name"), IsDefault = true, EnergyTypes = _energy.ToList(), Cards = cards }));
            if (saved != null) { Nav.Toast(L.T("duel.deck_saved")); Nav.Back(); }
        }
    }

    // ---------------------------------------------------------------- battle board (portrait)

    /// <summary>
    /// Server-authoritative Arena board. Every tap becomes an intent (with a unique action id and the state version the
    /// player saw); the server validates it with the shared ArenaEngine and returns this player's filtered view.
    /// Opponent moves arrive through the response, "arena_update" pushes and polling. Local engine calls are hints only.
    /// </summary>
    public sealed class ArenaBattleScreen : ScreenBase
    {
        public override bool ShowCurrencyBar => false;
        public override bool ShowBackButton => false;
        private ArenaView _v;
        private int _seen;
        private bool _busy, _subscribed, _ticking;
        private string _sel = "";                 // "", hand:<card>, energy, pet:<uid>, attack:<index>
        private readonly List<int> _setupBench = new();
        private int _setupActive;
        private TextMeshProUGUI? _timer;
        private Runner _run = null!;
        private readonly Dictionary<int, RectTransform> _petRects = new();

        public ArenaBattleScreen(ArenaView v) { _v = v; _seen = v.State.Log.Count; }

        private ArenaState St => _v.State;
        private ArenaRules R => ArenaCatalog.Rules!;
        private int Me => _v.You;
        private int Opp => 1 - _v.You;
        private bool Live => _v.Status == "active" && !St.Finished;
        private bool MyMain => Live && St.Phase == "main" && ArenaEngine.MustAct(St, Me) && !St.Players[Me].MustPromote;

        public override async void OnShow()
        {
            await ArenaCatalog.Ensure(Api);
            if (Realtime.I != null && !_subscribed) { _subscribed = true; Realtime.I.Message += OnRealtime; Realtime.I.Connected += OnReconnect; }
            Redraw();
        }

        public override void OnHide()
        {
            if (Realtime.I == null || !_subscribed) return;
            _subscribed = false; Realtime.I.Message -= OnRealtime; Realtime.I.Connected -= OnReconnect;
        }

        private void OnRealtime(string type, JToken? data) { if (type == "arena_update" && data?["matchId"]?.ToString() == _v.Id.ToString()) _ = Refresh(); }
        private void OnReconnect() => _ = Refresh();

        private async System.Threading.Tasks.Task Refresh()
        {
            if (_busy) return;
            var v = await Call(() => Api.Get<ArenaView>($"/api/arena/matches/{_v.Id}"), overlay: false);
            if (v != null && (v.Version != _v.Version || v.Status != _v.Status)) Show(v);
        }

        private async void Send(ArenaAction a)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var body = new ArenaActionWire { ActionId = Guid.NewGuid().ToString("N"), Version = _v.Version, Action = a };
                var v = await Call(() => Api.Post<ArenaView>($"/api/arena/matches/{_v.Id}/actions", body), overlay: false);
                if (v != null) Show(v);
                else { var fresh = await Call(() => Api.Get<ArenaView>($"/api/arena/matches/{_v.Id}"), overlay: false); if (fresh != null) Show(fresh); }
            }
            finally { _busy = false; }
        }

        private void Show(ArenaView v)
        {
            var fresh = v.State.Log.Skip(Math.Min(_seen, v.State.Log.Count)).ToList();
            _v = v; _seen = v.State.Log.Count; _sel = "";
            Redraw();
            if (fresh.Count > 0) _run.StartCoroutine(PlayFx(fresh));
        }

        private void Redraw() { if (Root == null || ArenaCatalog.Rules == null) return; UiKit.Clear(Root); Build(); }

        // ------------------------------------------------------------ layout (portrait 1080 × 1920)

        public override void Build()
        {
            if (ArenaCatalog.Rules == null) return;
            _run = Root.gameObject.GetComponent<Runner>() ?? Root.gameObject.AddComponent<Runner>();
            if (!_ticking) { _ticking = true; _run.StartCoroutine(Tick()); }
            var bg = UiKit.Panel(Root, UiKit.Hex("#1B3478"), 0, "Board"); ((RectTransform)bg.transform).Stretch();
            _petRects.Clear();
            var me = St.Players[Me]; var op = St.Players[Opp];
            Hud(op, false, 0.965f);
            for (int i = 0; i < 3; i++) Pet(op.Bench.ElementAtOrDefault(i), new Vector2(0.22f + i * 0.28f, 0.865f), false);
            Pet(op.Active, new Vector2(0.5f, 0.72f), true);
            var mid = UiKit.Text(Root, $"{L.T("tduel.turn", St.Turn)} · {(St.Active == Me ? L.T("tduel.you") : (_v.OpponentName ?? L.T("tduel.opponent")))}", 30, Color.white, UiKit.Weight.Bold);
            mid.rectTransform.Place(new Vector2(0.5f, 0.615f), new Vector2(900, 50), Vector2.zero);
            _timer = UiKit.Text(Root, "", 30, UiKit.Gold, UiKit.Weight.ExtraBold); _timer.rectTransform.Place(new Vector2(0.88f, 0.615f), new Vector2(200, 50), Vector2.zero);
            Pet(me.Active, new Vector2(0.5f, 0.505f), true);
            for (int i = 0; i < 3; i++) Pet(me.Bench.ElementAtOrDefault(i), new Vector2(0.22f + i * 0.28f, 0.36f), false);
            Hud(me, true, 0.275f);
            Panel(me);
            Hand(me);
            if (St.Phase == "setup" && !me.SetupDone && Live) SetupSheet(me);
            if (me.MustPromote && Live) PromoteSheet(me);
            if (St.Finished || _v.Status == "finished") Result();
        }

        private void Hud(ArenaPlayerState p, bool mine, float y)
        {
            var bar = UiKit.Panel(Root, new Color(0, 0, 0, 0.35f), 28, mine ? "MyHud" : "OppHud");
            ((RectTransform)bar.transform).Place(new Vector2(0.5f, y), new Vector2(1040, 64), Vector2.zero);
            string stars = new string('★', Math.Min(p.Points, R.Config.PointsToWin)) + new string('☆', Math.Max(0, R.Config.PointsToWin - p.Points));
            string name = mine ? p.Name : (_v.OpponentName ?? p.Name);
            UiKit.Text(bar.transform, $"{name}   <color=#FFD86B>{stars}</color>   🂠{p.Hand.Count}  📚{p.Deck.Count}  ⚰{p.Discard.Count}", 28, Color.white, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(20, 0, 260, 0);
            // Energy Zone: current (tap to attach) and next
            var cur = Orb(bar.transform, p.ZoneCurrent, 44); ((RectTransform)cur.transform).Place(new Vector2(1, 0.5f), new Vector2(44, 44), new Vector2(-150, 0));
            if (mine && MyMain && p.ZoneCurrent != null && !p.EnergyAttached)
            {
                var b = cur.gameObject.AddComponent<Button>(); b.onClick.AddListener(() => { _sel = _sel == "energy" ? "" : "energy"; Redraw(); });
                if (_sel == "energy") cur.transform.localScale = Vector3.one * 1.3f;
            }
            UiKit.Text(bar.transform, L.T("arena.next"), 20, new Color(1, 1, 1, 0.6f)).rectTransform.Place(new Vector2(1, 0.5f), new Vector2(70, 40), new Vector2(-80, 0));
            var nx = Orb(bar.transform, p.ZoneNext, 26); ((RectTransform)nx.transform).Place(new Vector2(1, 0.5f), new Vector2(26, 26), new Vector2(-30, 0));
        }

        private Image Orb(Transform parent, string? el, float size)
        {
            var o = UiKit.Icon(parent, UiKit.Circle, new Vector2(size, size), "Orb");
            o.color = el == null ? new Color(1, 1, 1, 0.15f) : ArenaCatalog.ElementColor(el);
            return o;
        }

        private List<int> Targets()
        {
            var me = St.Players[Me]; var op = St.Players[Opp];
            if (!MyMain) return new();
            if (_sel == "energy") return me.InPlay().Select(x => x.Uid).ToList();
            if (_sel.StartsWith("attack:") && me.Active != null) return Pool(R.Def(me.Active.DefId).Attacks[int.Parse(_sel[7..])].Effects)?.Select(x => x.Uid).ToList() ?? new();
            if (!_sel.StartsWith("hand:")) return new();
            var d = R.Def(St.Card(int.Parse(_sel[5..])).DefId);
            if (d.Kind == "tool") return me.InPlay().Where(x => x.Tool == 0).Select(x => x.Uid).ToList();
            if (d.Kind == "pet" && d.Stage > 0) return me.InPlay().Where(x => x.DefId == d.EvolvesFrom && x.PlayedTurn != St.Turn && x.EvolvedTurn != St.Turn).Select(x => x.Uid).ToList();
            if (d.Kind == "supporter" && me.SupportersUsed >= R.Config.SupportersPerTurn) return new();
            if (d.Kind != "pet" && ArenaEngine.TrainerPlayable(R, St, Me, d, new List<int>())) return Pool(d.Effects)?.Select(x => x.Uid).ToList() ?? new();
            return new();
        }

        private List<ArenaPet>? Pool(List<ArenaEffect> effects)
        {
            var me = St.Players[Me]; var op = St.Players[Opp];
            foreach (var e in effects)
            {
                if (e.Op == "bench_damage" && e.Target == "opp_bench_chosen") return op.Bench;
                if (e.Op == "heal" && e.Target == "own_chosen") return me.InPlay().ToList();
                if (e.Op == "switch_self" || (e.Op == "energy_attach" && e.Target == "own_bench_chosen")) return me.Bench;
                if (e.Op == "force_switch") return op.Bench;
                if (e.Op == "energy_transfer") return me.Bench.Where(b => b.Energy.Count > 0).ToList();
            }
            return null;
        }

        private void Pet(ArenaPet? pet, Vector2 anchor, bool big)
        {
            var size = big ? new Vector2(300, 400) : new Vector2(220, 290);
            var rt = UiKit.Rect(Root, pet == null ? "Empty" : "Pet" + pet.Uid);
            rt.Place(anchor, size, Vector2.zero);
            var img = rt.gameObject.AddComponent<Image>(); img.sprite = UiKit.RoundedSprite(28); img.type = Image.Type.Sliced;
            if (pet == null) { img.color = new Color(1, 1, 1, 0.07f); return; }
            _petRects[pet.Uid] = rt;
            var d = R.Def(pet.DefId); int hp = ArenaEngine.MaxHp(R, St, pet); int cur = Math.Max(0, hp - pet.Damage);
            bool glow = Targets().Contains(pet.Uid) || _sel == "pet:" + pet.Uid;
            img.color = glow ? UiKit.Gold : ArenaCatalog.ElementColor(d.Element);
            var inner = UiKit.Panel(rt, UiKit.White, 22, "Inner"); ((RectTransform)inner.transform).Stretch(8, 8, 8, 8); inner.raycastTarget = false;
            var art = UiKit.Icon(rt, null, size * 0.56f, "Art"); ((RectTransform)art.transform).Place(new Vector2(0.5f, 0.6f), size * 0.56f, Vector2.zero); art.raycastTarget = false;
            ArenaCatalog.LoadSprite(art, "pet/" + d.Id);
            float fs = big ? 1f : 0.75f;
            UiKit.Text(rt, $"<color=#C0392B><b>{cur}</b></color>/{hp}", 30 * fs, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.TopRight).rectTransform.Stretch(14, 12, 14, size.y - 60);
            if (d.Ex) UiKit.Text(rt, "EX", 30 * fs, UiKit.GoldDeep, UiKit.Weight.ExtraBold, TextAlignmentOptions.TopLeft).rectTransform.Stretch(16, 12, 14, size.y - 60);
            UiKit.Text(rt, ArenaCatalog.Name(d), 28 * fs, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Place(new Vector2(0.5f, 0), new Vector2(size.x - 20, 40), new Vector2(0, 80 * fs), new Vector2(0.5f, 0));
            var barBg = UiKit.Panel(rt, new Color(0, 0, 0, 0.2f), 6, "HpBg"); ((RectTransform)barBg.transform).Place(new Vector2(0.5f, 0), new Vector2(size.x - 40, 12), new Vector2(0, 60 * fs), new Vector2(0.5f, 0)); barBg.raycastTarget = false;
            var bar = UiKit.Panel(barBg.transform, cur * 100 < hp * 30 ? UiKit.Danger : UiKit.Hex("#43BD78"), 6, "Hp"); var brt = (RectTransform)bar.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = new Vector2(hp == 0 ? 0 : (float)cur / hp, 1); brt.offsetMin = brt.offsetMax = Vector2.zero; bar.raycastTarget = false;
            for (int i = 0; i < pet.Energy.Count; i++) { var o = Orb(rt, pet.Energy[i], 26 * fs + 4); o.raycastTarget = false; ((RectTransform)o.transform).Place(new Vector2(0, 0), new Vector2(26 * fs + 4, 26 * fs + 4), new Vector2(24 + i * 30 * fs, 26 * fs), new Vector2(0, 0)); }
            var flags = new List<string>();
            if (pet.Poisoned) flags.Add(L.T("arena.status.poisoned")); if (pet.Burned) flags.Add(L.T("arena.status.burned")); if (pet.Special != null) flags.Add(L.T("arena.status." + pet.Special));
            if (pet.Tool != 0) flags.Add("🎒");
            if (flags.Count > 0) UiKit.Text(rt, string.Join(" ", flags), 22 * fs, UiKit.Hex("#8a3cc8"), UiKit.Weight.Bold, TextAlignmentOptions.TopLeft).rectTransform.Stretch(16, 50 * fs, 16, size.y - 100 * fs);
            var btn = rt.gameObject.AddComponent<Button>(); var uid = pet.Uid; btn.onClick.AddListener(() => TapPet(uid));
        }

        private void TapPet(int uid)
        {
            var t = Targets();
            if (t.Contains(uid))
            {
                if (_sel == "energy") { Send(new ArenaAction { Type = "attach", Uid = uid }); return; }
                if (_sel.StartsWith("attack:")) { Send(new ArenaAction { Type = "attack", Index = int.Parse(_sel[7..]), Targets = new List<int> { uid } }); return; }
                if (_sel.StartsWith("hand:"))
                {
                    int c = int.Parse(_sel[5..]); var d = R.Def(St.Card(c).DefId);
                    if (d.Kind == "tool") Send(new ArenaAction { Type = "tool", Card = c, Uid = uid });
                    else if (d.Kind == "pet") Send(new ArenaAction { Type = "evolve", Card = c, Uid = uid });
                    else Send(new ArenaAction { Type = d.Kind, Card = c, Targets = new List<int> { uid } });
                    return;
                }
            }
            _sel = _sel == "pet:" + uid ? "" : "pet:" + uid; Redraw();
        }

        private void Hand(ArenaPlayerState me)
        {
            int n = me.Hand.Count; float w = Math.Min(150, 1000f / Math.Max(1, n));
            for (int i = 0; i < n; i++)
            {
                int c = me.Hand[i]; var d = R.Def(St.Card(c).DefId);
                var rt = UiKit.Rect(Root, "H" + i);
                bool sel = _sel == "hand:" + c;
                rt.Place(new Vector2(0.5f, 0), new Vector2(140, 196), new Vector2((i - (n - 1) / 2f) * w, sel ? 46 : 14), new Vector2(0.5f, 0));
                var img = rt.gameObject.AddComponent<Image>(); img.sprite = UiKit.RoundedSprite(18); img.type = Image.Type.Sliced;
                img.color = sel ? UiKit.Gold : d.Kind == "pet" ? ArenaCatalog.ElementColor(d.Element) : d.Kind == "supporter" ? UiKit.Lilac : d.Kind == "tool" ? UiKit.Peach : UiKit.Mint;
                var inner = UiKit.Panel(rt, UiKit.White, 14, "Inner"); ((RectTransform)inner.transform).Stretch(5, 5, 5, 5); inner.raycastTarget = false;
                if (d.Kind == "pet") { var art = UiKit.Icon(rt, null, new Vector2(116, 100), "Art"); ((RectTransform)art.transform).Place(new Vector2(0.5f, 1), new Vector2(116, 100), new Vector2(0, -8), new Vector2(0.5f, 1)); art.raycastTarget = false; ArenaCatalog.LoadSprite(art, "pet/" + d.Id); }
                else UiKit.Text(rt, L.T("arena.kind." + d.Kind), 22, UiKit.InkSoft, UiKit.Weight.Bold).rectTransform.Stretch(6, 20, 6, 120);
                UiKit.Text(rt, ArenaCatalog.Name(d), 20, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Stretch(6, 110, 6, 30);
                if (d.Kind == "pet") UiKit.Text(rt, (d.Stage == 0 ? "Basic" : $"Stage {d.Stage}") + $" · {d.Hp}", 18, UiKit.InkSoft).rectTransform.Stretch(6, 160, 6, 6);
                rt.gameObject.AddComponent<Button>().onClick.AddListener(() => { _sel = _sel == "hand:" + c ? "" : "hand:" + c; Redraw(); });
            }
        }

        private void Panel(ArenaPlayerState me)
        {
            var p = UiKit.Panel(Root, new Color(0, 0, 0, 0.32f), 24, "Panel");
            ((RectTransform)p.transform).Place(new Vector2(0.5f, 0.165f), new Vector2(1040, 190), Vector2.zero);
            var col = UiKit.Rect(p.transform, "Col").Stretch(16, 10, 16, 10);
            UiKit.HStack(col, 10, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            if (_sel.StartsWith("hand:"))
            {
                int c = int.Parse(_sel[5..]); var d = R.Def(St.Card(c).DefId);
                string info = d.Kind == "pet" ? (d.Stage == 0 ? L.T("arena.hint_bench") : L.T("arena.hint_evolve")) : ArenaCatalog.Text(d.Text);
                UiKit.Text(col, $"<b>{ArenaCatalog.Name(d)}</b>\n{info}", 22, Color.white, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(620, 170);
                if (MyMain && d.IsBasic) UiKit.Button(col, L.T("arena.put_bench"), () => Send(new ArenaAction { Type = "bench", Card = c }), UiKit.ButtonStyle.Gold, 26).Size(300, 100);
                else if (MyMain && (d.Kind == "item" || d.Kind == "supporter") && Pool(d.Effects) == null)
                {
                    var b = UiKit.Button(col, L.T("arena.play"), () => Send(new ArenaAction { Type = d.Kind, Card = c }), UiKit.ButtonStyle.Gold, 26); b.Size(300, 100);
                    b.interactable = ArenaEngine.TrainerPlayable(R, St, Me, d, new List<int>()) && !(d.Kind == "supporter" && me.SupportersUsed >= R.Config.SupportersPerTurn);
                }
            }
            else if (_sel.StartsWith("pet:") && St.Players.SelectMany(x => x.InPlay()).FirstOrDefault(x => "pet:" + x.Uid == _sel) is ArenaPet pet)
            {
                var d = R.Def(pet.DefId); bool mineActive = me.Active == pet;
                for (int i = 0; i < d.Attacks.Count; i++)
                {
                    var a = d.Attacks[i]; int idx = i;
                    string cost = string.Join("", a.Cost.Select(x => x == "colorless" ? "⚪" : "●"));
                    var b = UiKit.Button(col, $"{cost} {ArenaCatalog.Text(a.Name)} {(a.Damage > 0 ? a.Damage.ToString() : "")}", () => Attack(idx), UiKit.ButtonStyle.Secondary, 22);
                    b.Size(300, 100); b.interactable = mineActive && MyMain && ArenaEngine.AttackProblem(R, St, Me, i) == null;
                }
                if (mineActive && MyMain && me.Bench.Count > 0)
                {
                    int cost = ArenaEngine.RetreatCost(R, St, Me);
                    var rb = UiKit.Button(col, L.T("arena.retreat", cost), () => { _sel = "retreat"; Redraw(); }, UiKit.ButtonStyle.Ghost, 22); rb.Size(200, 100);
                    rb.interactable = me.Retreats < R.Config.RetreatsPerTurn && pet.Energy.Count >= cost && pet.Special != "asleep" && pet.Special != "paralyzed";
                }
                if (pet.Owner == Me && MyMain && d.Ability?.Trigger == "activated")
                    UiKit.Button(col, "✨ " + ArenaCatalog.Text(d.Ability.Name), () => Send(new ArenaAction { Type = "ability", Uid = pet.Uid }), UiKit.ButtonStyle.Ghost, 22).Size(260, 100);
            }
            else if (_sel == "retreat")
                foreach (var b in me.Bench) { int uid = b.Uid; UiKit.Button(col, "→ " + ArenaCatalog.Name(R.Def(b.DefId)), () => Send(new ArenaAction { Type = "retreat", Uid = uid }), UiKit.ButtonStyle.Secondary, 22).Size(300, 100); }
            else
            {
                UiKit.Text(col, MyMain ? L.T("arena.hint_main") : L.T("arena.waiting"), 24, Color.white, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(620, 170);
            }
            var right = UiKit.Rect(p.transform, "Right"); right.Place(new Vector2(1, 0.5f), new Vector2(200, 170), new Vector2(-12, 0), new Vector2(1, 0.5f));
            UiKit.VStack(right, 8);
            if (MyMain) UiKit.Button(right, L.T("tduel.end_turn"), () => Send(new ArenaAction { Type = "end" }), UiKit.ButtonStyle.Gold, 24).Size(200, 80);
            if (Live) UiKit.Button(right, L.T("duel.surrender"), () => Send(new ArenaAction { Type = "surrender" }), UiKit.ButtonStyle.Ghost, 22).Size(200, 70);
        }

        private void Attack(int idx)
        {
            var pool = Pool(R.Def(St.Players[Me].Active!.DefId).Attacks[idx].Effects);
            if (pool != null && pool.Count > 1) { _sel = "attack:" + idx; Redraw(); return; }
            Send(new ArenaAction { Type = "attack", Index = idx, Targets = pool != null && pool.Count == 1 ? new List<int> { pool[0].Uid } : new List<int>() });
        }

        private void SetupSheet(ArenaPlayerState me)
        {
            var o = UiKit.Panel(Root, new Color(0.02f, 0.04f, 0.12f, 0.75f), 0, "Setup"); ((RectTransform)o.transform).Stretch();
            var box = UiKit.Panel(o.transform, UiKit.White, 36, "Box", shadow: true); ((RectTransform)box.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(980, 760), Vector2.zero);
            UiKit.Text(box.transform, (St.First == Me ? L.T("arena.you_first") : L.T("arena.opp_first")) + "\n" + L.T("arena.setup_hint"), 30, UiKit.Ink, UiKit.Weight.Bold).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(920, 160), new Vector2(0, -20), new Vector2(0.5f, 1));
            var basics = me.Hand.Where(c => R.Def(St.Card(c).DefId).IsBasic).ToList();
            for (int i = 0; i < basics.Count; i++)
            {
                int c = basics[i]; var d = R.Def(St.Card(c).DefId);
                string role = _setupActive == c ? "Active" : _setupBench.Contains(c) ? "Bench" : $"HP {d.Hp}";
                var b = UiKit.Button(box.transform, $"{ArenaCatalog.Name(d)}\n{role}", () =>
                {
                    if (_setupActive == 0) _setupActive = c;
                    else if (_setupActive == c) _setupActive = 0;
                    else if (!_setupBench.Remove(c) && _setupBench.Count < R.Config.BenchSize) _setupBench.Add(c);
                    Redraw();
                }, _setupActive == c ? UiKit.ButtonStyle.Gold : _setupBench.Contains(c) ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 24);
                ((RectTransform)b.transform).Place(new Vector2(0.5f, 0.55f), new Vector2(210, 150), new Vector2((i - (basics.Count - 1) / 2f) * 225, 0));
            }
            var go = UiKit.Button(box.transform, L.T("arena.start"), () =>
            {
                Send(new ArenaAction { Type = "setup", Card = _setupActive, Bench = _setupBench.ToList() });
                _setupActive = 0; _setupBench.Clear();
            }, UiKit.ButtonStyle.Gold, 34);
            ((RectTransform)go.transform).Place(new Vector2(0.5f, 0), new Vector2(400, 110), new Vector2(0, 30), new Vector2(0.5f, 0));
            go.interactable = _setupActive != 0;
        }

        private void PromoteSheet(ArenaPlayerState me)
        {
            var o = UiKit.Panel(Root, new Color(0.02f, 0.04f, 0.12f, 0.75f), 0, "Promote"); ((RectTransform)o.transform).Stretch();
            var box = UiKit.Panel(o.transform, UiKit.White, 36, "Box", shadow: true); ((RectTransform)box.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(900, 560), Vector2.zero);
            UiKit.Text(box.transform, L.T("arena.promote"), 34, UiKit.Ink, UiKit.Weight.Bold).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(860, 80), new Vector2(0, -24), new Vector2(0.5f, 1));
            for (int i = 0; i < me.Bench.Count; i++)
            {
                var b = me.Bench[i]; int uid = b.Uid;
                var btn = UiKit.Button(box.transform, $"{ArenaCatalog.Name(R.Def(b.DefId))}  HP {ArenaEngine.MaxHp(R, St, b) - b.Damage}", () => Send(new ArenaAction { Type = "promote", Uid = uid }), UiKit.ButtonStyle.Secondary, 28);
                ((RectTransform)btn.transform).Place(new Vector2(0.5f, 1), new Vector2(760, 100), new Vector2(0, -130 - i * 115), new Vector2(0.5f, 1));
            }
        }

        private void Result()
        {
            var o = UiKit.Panel(Root, new Color(0.02f, 0.04f, 0.12f, 0.75f), 0, "Over"); ((RectTransform)o.transform).Stretch();
            var box = UiKit.Panel(o.transform, UiKit.White, 40, "Box", shadow: true); ((RectTransform)box.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(900, 560), Vector2.zero);
            bool win = St.Winner == Me, draw = St.Winner == null;
            UiKit.Text(box.transform, draw ? L.T("tduel.draw") : win ? L.T("tduel.victory") : L.T("tduel.defeat"), 80, draw ? UiKit.Ink : win ? UiKit.GoldDeep : UiKit.Danger, UiKit.Weight.ExtraBold)
                .rectTransform.Place(new Vector2(0.5f, 1), new Vector2(840, 120), new Vector2(0, -30), new Vector2(0.5f, 1));
            UiKit.Text(box.transform, $"{L.T("arena.end." + (St.EndReason ?? "points"))}\n★ {St.Players[Me].Points} – {St.Players[Opp].Points}", 32, UiKit.InkSoft)
                .rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(840, 140), new Vector2(0, 20));
            var back = UiKit.Button(box.transform, L.T("tduel.back_lobby"), () => Nav.Back(), UiKit.ButtonStyle.Gold, 34);
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0), new Vector2(400, 110), new Vector2(0, 30), new Vector2(0.5f, 0));
        }

        private IEnumerator PlayFx(List<ArenaEvent> evs)
        {
            foreach (var e in evs.Take(30))
            {
                string? txt = e.Type switch
                {
                    "damage" when e.N > 0 => $"−{e.N}", "heal" => $"+{e.N}", "knockout" => L.T("arena.fx_ko", e.N), "evolve" => L.T("tduel.fx_evolve"),
                    "status" => L.T("arena.status." + e.S), "coin" => e.A == 1 ? L.T("arena.heads") : L.T("arena.tails"), _ => null,
                };
                if (txt == null) continue;
                var host = _petRects.TryGetValue(e.A, out var r) && r != null ? r : (RectTransform)Root;
                var t = UiKit.Text(host, txt, e.Type == "damage" ? 54 : 36, e.Type == "damage" ? UiKit.Danger : e.Type == "heal" ? UiKit.Hex("#43BD78") : UiKit.Gold, UiKit.Weight.ExtraBold);
                t.rectTransform.Place(new Vector2(0.5f, 0.6f), new Vector2(420, 70), Vector2.zero); t.raycastTarget = false;
                t.gameObject.AddComponent<FloatUp>();
                yield return new WaitForSeconds(0.2f);
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
                bool waiting = Live && !ArenaEngine.MustAct(St, Me);
                if (waiting && poll >= 2) { poll = 0; _ = Refresh(); }
                yield return new WaitForSeconds(1f);
            }
            _ticking = false;
        }

        private sealed class Runner : MonoBehaviour { }
    }
}
