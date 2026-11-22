using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using PetStar.Rules.Duel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    // ---------------------------------------------------------------- wire types (mirror backend/.../DuelEndpoints.cs)
    public sealed class DuelDeckEntry { public string Kind { get; set; } = "pet"; public long? CardId { get; set; } public string? TacticId { get; set; } public int Count { get; set; } = 1; }
    public sealed class DuelDeckDto { public long Id { get; set; } public string Name { get; set; } = ""; public bool IsDefault { get; set; } public List<DuelDeckEntry> Entries { get; set; } = new(); }
    public sealed class SaveDuelDeck { public long? Id { get; set; } public string Name { get; set; } = ""; public bool IsDefault { get; set; } public List<DuelDeckEntry> Entries { get; set; } = new(); }
    public sealed class DuelTacticOwned { public TacticDef Def { get; set; } = new(); public int Owned { get; set; } }
    public sealed class DuelCollectionDto { public DuelConfig Config { get; set; } = new(); public List<DuelTacticOwned> Tactics { get; set; } = new(); }
    public sealed class DuelView
    {
        public Guid Id { get; set; } public string Mode { get; set; } = ""; public string Status { get; set; } = ""; public int You { get; set; } public int Version { get; set; }
        public DateTimeOffset? DeadlineAt { get; set; } public DateTimeOffset ServerNow { get; set; } public string? OpponentName { get; set; }
        public DuelState State { get; set; } = new(); public string? RewardsJson { get; set; }
    }
    public sealed class DuelActionBody { public int Version { get; set; } public DuelAction Action { get; set; } = new(); }

    /// <summary>Tactic definitions (bilingual names, text, cost, targeting) come from the server, so client and server never disagree.</summary>
    public static class DuelCatalog
    {
        public static DuelCollectionDto? Data;
        public static TacticDef? Tactic(string? id) => id == null ? null : Data?.Tactics.FirstOrDefault(t => t.Def.Id == id)?.Def;
        public static string Name(TacticDef d) => d.Name.TryGetValue(L.Language, out var n) ? n : d.Name.Values.FirstOrDefault() ?? d.Id;
        public static string Text(TacticDef d) => d.Text.TryGetValue(L.Language, out var n) ? n : d.Text.Values.FirstOrDefault() ?? "";
    }

    // ---------------------------------------------------------------- lobby

    /// <summary>Pet Card Tactical Duel lobby: deck status, PvE stages, Ranked, PvP challenge and reconnect to an active duel.</summary>
    public sealed class TacticalDuelLobbyScreen : ScreenBase
    {
        public override string TitleKey => "tduel.title";
        private List<PveStageDto>? _stages;
        private List<DuelDeckDto>? _decks;
        private DuelView? _active;
        private TMP_InputField? _uid;
        private bool _subscribed;

        public override void Build()
        {
            float top = Wide ? 140 : 320;
            var def = _decks?.FirstOrDefault(d => d.IsDefault);
            var head = UiKit.Panel(Root, UiKit.White, 36, "Deck", shadow: true);
            ((RectTransform)head.transform).Place(new Vector2(0.5f, 1), new Vector2(Wide ? 1500 : 1000, Wide ? 130 : 260), new Vector2(0, -top), new Vector2(0.5f, 1));
            var info = def == null ? L.T("tduel.no_deck") : L.T("tduel.deck_info", def.Name, def.Entries.Where(e => e.Kind == "pet").Sum(e => e.Count), def.Entries.Sum(e => e.Count));
            UiKit.Text(head.transform, info, 34, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(36, 0, Wide ? 760 : 40, Wide ? 0 : 130);
            var row = UiKit.Rect(head.transform, "Buttons");
            if (Wide) row.Place(new Vector2(1, 0.5f), new Vector2(720, 100), new Vector2(-16, 0), new Vector2(1, 0.5f));
            else row.Place(new Vector2(0.5f, 0), new Vector2(940, 100), new Vector2(0, 16), new Vector2(0.5f, 0));
            UiKit.HStack(row, 12).childForceExpandWidth = true;
            UiKit.Button(row, L.T("tduel.deck_builder"), () => Nav.Push(new DuelDeckBuilderScreen(def)), UiKit.ButtonStyle.Primary, 30).Size(-1, 100, 1);
            UiKit.Button(row, L.T("tduel.auto_deck"), AutoDeck, UiKit.ButtonStyle.Secondary, 30).Size(-1, 100, 1);

            var list = UiKit.Scroll(UiKit.Rect(Root, "Body").Stretch(40, top + (Wide ? 150 : 280), 40, 40));
            UiKit.VStack(list, 18, new RectOffset(0, 0, 10, 40));
            if (_active != null)
                UiKit.Button(list, L.T("tduel.resume", L.T("tduel.mode." + _active.Mode)), () => Nav.Push(new TacticalDuelScreen(_active)), UiKit.ButtonStyle.Gold, 36).Size(-1, 120);
            UiKit.Text(list, L.T("tduel.rules_short"), 28, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 110);

            var modes = UiKit.Rect(list, "Modes"); modes.Size(-1, 120);
            UiKit.HStack(modes, 14).childForceExpandWidth = true;
            var ranked = UiKit.Button(modes, L.T("tduel.ranked"), StartRanked, UiKit.ButtonStyle.Primary, 34); ranked.Size(-1, 120, 1); ranked.interactable = def != null;
            UiKit.Button(modes, L.T("tduel.classic"), () => Nav.Push(new DuelLobbyScreen()), UiKit.ButtonStyle.Ghost, 30).Size(-1, 120, 1);

            var pvp = UiKit.Panel(list, UiKit.White, 32, "Pvp"); pvp.Size(-1, 140);
            _uid = UiKit.Input(pvp.transform, L.T("tduel.challenge_uid"));
            ((RectTransform)_uid.transform).Stretch(24, 20, 340, 20);
            var ch = UiKit.Button(pvp.transform, L.T("tduel.challenge"), Challenge, UiKit.ButtonStyle.Secondary, 30);
            ((RectTransform)ch.transform).Place(new Vector2(1, 0.5f), new Vector2(300, 100), new Vector2(-20, 0), new Vector2(1, 0.5f));
            ch.interactable = def != null;

            UiKit.Text(list, L.T("duel.pve_chapter", 1), 40, UiKit.Sapphire, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 70);
            foreach (var s in _stages ?? new List<PveStageDto>())
            {
                var r = UiKit.Panel(list, s.Unlocked ? UiKit.White : UiKit.SkyDeep, 32, s.Id); r.Size(-1, 150);
                UiKit.Text(r.transform, L.T("stage." + s.Id), 40, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(36, 16, 320, 70);
                UiKit.Text(r.transform, L.T("duel.stage_info", s.ClearCount, s.RewardedClearsLeftToday), 26, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(36, 85, 320, 10);
                var id = s.Id;
                var b = UiKit.Button(r.transform, s.Unlocked ? L.T("duel.fight") : L.T("duel.locked"), () => StartPve(id), s.Unlocked ? UiKit.ButtonStyle.Gold : UiKit.ButtonStyle.Secondary, 34);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(260, 100), new Vector2(-24, 0), new Vector2(1, 0.5f));
                b.interactable = s.Unlocked && def != null && _active == null;
            }
        }

        public override async void OnShow()
        {
            if (Realtime.I != null && !_subscribed) { _subscribed = true; Realtime.I.Message += OnRealtime; }
            DuelCatalog.Data = await Call(() => Api.Get<DuelCollectionDto>("/api/duel/collection"), overlay: false) ?? DuelCatalog.Data;
            _decks = await Call(() => Api.Get<List<DuelDeckDto>>("/api/duel/decks"));
            _stages = await Call(() => Api.Get<List<PveStageDto>>("/api/pve/stages"), overlay: false);
            _active = await Call(() => Api.Get<DuelView>("/api/duels/active"), overlay: false);   // 204 → null
            if (_active != null && _active.Id == Guid.Empty) _active = null;
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }

        public override void OnHide()
        {
            if (Realtime.I != null && _subscribed) { _subscribed = false; Realtime.I.Message -= OnRealtime; }
        }

        private void OnRealtime(string type, JToken? data)
        {
            if (type != "duel_challenge" || data == null) return;
            var id = data["duelId"]?.ToString(); var from = data["from"]?.ToString() ?? "?";
            if (id == null) return;
            var box = Nav.Popup(new Vector2(900, 420), new Vector2(900, 420), out var close);
            UiKit.Text(box, L.T("tduel.challenged_by", from), 38, UiKit.Ink, UiKit.Weight.Bold).rectTransform.Stretch(30, 30, 30, 170);
            var row = UiKit.Rect(box, "Row"); row.Place(new Vector2(0.5f, 0), new Vector2(800, 110), new Vector2(0, 30), new Vector2(0.5f, 0));
            UiKit.HStack(row, 16).childForceExpandWidth = true;
            UiKit.Button(row, L.T("tduel.accept"), async () =>
            {
                close();
                var v = await Call(() => Api.Post<DuelView>($"/api/duels/{id}/accept", new { }));
                if (v != null) Nav.Push(new TacticalDuelScreen(v));
            }, UiKit.ButtonStyle.Gold, 34).Size(-1, 110, 1);
            UiKit.Button(row, L.T("tduel.decline"), async () => { close(); await Call(() => Api.Post<JObject>($"/api/duels/{id}/decline", new { }), overlay: false); }, UiKit.ButtonStyle.Secondary, 34).Size(-1, 110, 1);
        }

        private async void AutoDeck()
        {
            var auto = await Call(() => Api.Post<JObject>("/api/duel/decks/auto", null));
            if (auto == null) return;
            var entries = auto["entries"]!.ToObject<List<DuelDeckEntry>>(Newtonsoft.Json.JsonSerializer.Create(ApiClient.Json))!;
            var def = _decks?.FirstOrDefault(d => d.IsDefault);
            var saved = await Call(() => Api.Post<DuelDeckDto>("/api/duel/decks", new SaveDuelDeck { Id = def?.Id, Name = def?.Name ?? L.T("duel.default_deck_name"), IsDefault = true, Entries = entries }));
            if (saved != null) { Nav.Toast(L.T("duel.deck_saved")); OnShow(); }
        }

        private async void StartPve(string stageId)
        {
            var v = await Call(() => Api.Post<DuelView>("/api/duels/pve", new { stageId }, ApiClient.NewIdempotencyKey()));
            if (v != null) Nav.Push(new TacticalDuelScreen(v));
        }

        private async void StartRanked()
        {
            var v = await Call(() => Api.Post<DuelView>("/api/duels/ranked", new { }, ApiClient.NewIdempotencyKey()));
            if (v != null) Nav.Push(new TacticalDuelScreen(v));
        }

        private async void Challenge()
        {
            if (_uid == null || !long.TryParse(_uid.text.Trim(), out var uid)) { Nav.Toast(L.T("tduel.uid_invalid"), true); return; }
            var r = await Call(() => Api.Post<JObject>("/api/duels/pvp/challenge", new { uid }));
            if (r != null) Nav.Toast(L.T("tduel.challenge_sent"));
        }
    }

    // ---------------------------------------------------------------- 40-card deck builder

    /// <summary>Builds a 40-card deck (Pets 10–25 + Spells/Traps, max 3 copies, Divine max 1). The server re-validates
    /// ownership, locks and legality on save; the counters here only guide the player.</summary>
    public sealed class DuelDeckBuilderScreen : ScreenBase
    {
        public override string TitleKey => "tduel.deck_builder";
        private readonly DuelDeckDto? _deck;
        private List<CardDto>? _pets;
        private readonly HashSet<long> _petPick = new();
        private readonly Dictionary<string, int> _tacticPick = new();
        private string _tab = "pet";
        private TextMeshProUGUI _count = null!;

        public DuelDeckBuilderScreen(DuelDeckDto? deck)
        {
            _deck = deck;
            if (deck == null) return;
            foreach (var e in deck.Entries)
                if (e.Kind == "pet" && e.CardId is long id) _petPick.Add(id);
                else if (e.TacticId != null) _tacticPick[e.TacticId] = e.Count;
        }

        private DuelConfig Cfg => DuelCatalog.Data?.Config ?? new DuelConfig();
        private int Total => _petPick.Count + _tacticPick.Values.Sum();

        public override void Build()
        {
            float top = Wide ? 140 : 320;
            var head = UiKit.Rect(Root, "Head"); head.Place(new Vector2(0.5f, 1), new Vector2(Wide ? 1600 : 1000, 110), new Vector2(0, -top), new Vector2(0.5f, 1));
            UiKit.HStack(head, 12).childForceExpandWidth = true;
            _count = UiKit.Text(head, "", 32, UiKit.Ink, UiKit.Weight.ExtraBold); _count.Size(-1, 110, 2);
            UiKit.Button(head, L.T("tduel.tab_pets"), () => { _tab = "pet"; Rebuild(); }, _tab == "pet" ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 30).Size(-1, 100, 1);
            UiKit.Button(head, L.T("tduel.tab_tactics"), () => { _tab = "tactic"; Rebuild(); }, _tab == "tactic" ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 30).Size(-1, 100, 1);
            UiKit.Button(head, L.T("tduel.save"), Save, UiKit.ButtonStyle.Gold, 32).Size(-1, 100, 1);
            UpdateCount();

            var list = UiKit.Scroll(UiKit.Rect(Root, "List").Stretch(40, top + 130, 40, 40));
            UiKit.VStack(list, 12, new RectOffset(0, 0, 6, 40));
            if (_tab == "pet")
            {
                foreach (var c in _pets ?? new List<CardDto>())
                {
                    var id = c.Id; bool on = _petPick.Contains(id);
                    var r = UiKit.Panel(list, on ? UiKit.Mint : UiKit.White, 28, "Pet" + id); r.Size(-1, 120);
                    var art = UiKit.Icon(r.transform, null, new Vector2(100, 100), "Art");
                    ((RectTransform)art.transform).Place(new Vector2(0, 0.5f), new Vector2(100, 100), new Vector2(14, 0), new Vector2(0, 0.5f));
                    LoadSprite(art, "pet/" + c.FormId);
                    UiKit.Text(r.transform, $"{L.T("pet." + c.FormId)}  ·  {c.Rarity}  ·  {L.T("tduel.stage_n", c.Stage)}  ·  ⚔{c.Atk} ❤{c.Hp}", 28, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft)
                        .rectTransform.Stretch(130, 0, 260, 0);
                    var busy = c.IsBusy;
                    var b = UiKit.Button(r.transform, on ? L.T("tduel.remove") : busy ? L.T("tduel.busy") : L.T("tduel.add"), () => { if (!_petPick.Remove(id)) _petPick.Add(id); Rebuild(); },
                        on ? UiKit.ButtonStyle.Secondary : UiKit.ButtonStyle.Primary, 28);
                    ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(220, 90), new Vector2(-16, 0), new Vector2(1, 0.5f));
                    b.interactable = on || !busy;
                }
            }
            else
            {
                foreach (var t in DuelCatalog.Data?.Tactics ?? new List<DuelTacticOwned>())
                {
                    var d = t.Def; int n = _tacticPick.TryGetValue(d.Id, out var k) ? k : 0;
                    var r = UiKit.Panel(list, d.Type == "trap" ? UiKit.Lilac : UiKit.Mint, 28, d.Id); r.Size(-1, 150);
                    UiKit.Text(r.transform, $"{(d.Type == "trap" ? "⛓" : d.Speed == "quick" ? "⚡" : "✦")} {DuelCatalog.Name(d)}  ·  ⚡{d.Cost}  ·  {L.T("tduel.owned", t.Owned)}", 30, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft)
                        .rectTransform.Stretch(24, 12, 360, 80);
                    UiKit.Text(r.transform, DuelCatalog.Text(d), 22, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.TopLeft).rectTransform.Stretch(24, 72, 360, 8);
                    var id = d.Id; int max = Math.Min(Cfg.MaxCopies, t.Owned);
                    var minus = UiKit.Button(r.transform, "−", () => { Set(id, n - 1); }, UiKit.ButtonStyle.Secondary, 40);
                    ((RectTransform)minus.transform).Place(new Vector2(1, 0.5f), new Vector2(100, 90), new Vector2(-230, 0), new Vector2(1, 0.5f)); minus.interactable = n > 0;
                    UiKit.Text(r.transform, n.ToString(), 40, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Place(new Vector2(1, 0.5f), new Vector2(100, 90), new Vector2(-125, 0), new Vector2(1, 0.5f));
                    var plus = UiKit.Button(r.transform, "+", () => { Set(id, n + 1); }, UiKit.ButtonStyle.Primary, 40);
                    ((RectTransform)plus.transform).Place(new Vector2(1, 0.5f), new Vector2(100, 90), new Vector2(-16, 0), new Vector2(1, 0.5f)); plus.interactable = n < max;
                }
            }
        }

        private void Set(string id, int n) { if (n <= 0) _tacticPick.Remove(id); else _tacticPick[id] = n; Rebuild(); }
        private void Rebuild() { if (Root == null) return; UiKit.Clear(Root); Build(); }

        private void UpdateCount()
        {
            int pets = _petPick.Count;
            bool ok = Total == Cfg.DeckSize && pets >= Cfg.PetMin && pets <= Cfg.PetMax;
            _count.text = L.T("tduel.count", Total, Cfg.DeckSize, pets, Cfg.PetMin, Cfg.PetMax);
            _count.color = ok ? UiKit.Sapphire : UiKit.Danger;
        }

        public override async void OnShow()
        {
            DuelCatalog.Data ??= await Call(() => Api.Get<DuelCollectionDto>("/api/duel/collection"));
            var page = await Call(() => Api.Get<CardPageDto>("/api/cards?sort=power&pageSize=200"), overlay: false);
            _pets = page?.Items ?? new List<CardDto>();
            Rebuild();
        }

        private async void Save()
        {
            var entries = _petPick.Select(id => new DuelDeckEntry { Kind = "pet", CardId = id, Count = 1 })
                .Concat(_tacticPick.Select(kv => new DuelDeckEntry { Kind = "tactic", TacticId = kv.Key, Count = kv.Value })).ToList();
            var saved = await Call(() => Api.Post<DuelDeckDto>("/api/duel/decks", new SaveDuelDeck { Id = _deck?.Id, Name = _deck?.Name ?? L.T("duel.default_deck_name"), IsDefault = true, Entries = entries }));
            if (saved != null) { Nav.Toast(L.T("duel.deck_saved")); Nav.Back(); }
        }

        private static async void LoadSprite(Image img, string address)
        {
            var s = await GameDataClient.LoadSprite(address);
            if (img != null && s != null) { img.sprite = s; img.color = Color.white; }
        }
    }

    // ---------------------------------------------------------------- duel board (landscape 16:9)

    /// <summary>
    /// Server-authoritative Tactical Duel board. The client only sends intents with the state version it saw; the server
    /// applies the shared DuelEngine rules (also used by the backend tests and the web preview) and returns this player's
    /// masked view. Opponent turns (AI or player) arrive through the response, realtime pushes and polling.
    /// </summary>
    public sealed class TacticalDuelScreen : ScreenBase
    {
        public override bool IsDuel => true;
        public override bool ShowCurrencyBar => false;
        public override bool ShowBackButton => false;

        private DuelView _v;
        private int _seenLog;
        private bool _busy, _subscribed, _ticking;
        private int? _handSel, _unitSel;
        private string _mode = "";                 // "", move, attack, skill, ultimate
        private readonly List<int> _picks = new(); // tributes / own_two targets
        private TextMeshProUGUI? _timer;
        private Runner _run = null!;
        private readonly Dictionary<int, RectTransform> _unitRects = new();

        public TacticalDuelScreen(DuelView v) { _v = v; _seenLog = v.State.Log.Count; }

        private DuelState St => _v.State;
        private int Me => _v.You;
        private int Opp => 1 - _v.You;
        private bool Active => _v.Status == "active" && !St.Finished;
        private bool MyControl => Active && St.Priority < 0 && St.Active == Me;
        private bool MyReaction => Active && St.Priority == Me;

        public override void OnShow()
        {
            if (Realtime.I == null || _subscribed) return;
            _subscribed = true;
            Realtime.I.Message += OnRealtime;
            Realtime.I.Connected += OnReconnect;
        }

        public override void OnHide()
        {
            if (Realtime.I == null || !_subscribed) return;
            _subscribed = false;
            Realtime.I.Message -= OnRealtime;
            Realtime.I.Connected -= OnReconnect;
        }

        private void OnRealtime(string type, JToken? data) { if (type == "duel_update" && data?["duelId"]?.ToString() == _v.Id.ToString()) _ = Refresh(); }
        private void OnReconnect() => _ = Refresh();

        private async System.Threading.Tasks.Task Refresh()
        {
            if (_busy) return;
            var v = await Call(() => Api.Get<DuelView>($"/api/duels/{_v.Id}"), overlay: false);
            if (v != null && (v.Version != _v.Version || v.Status != _v.Status)) Show(v);
        }

        private async void Send(DuelAction a)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var v = await Call(() => Api.Post<DuelView>($"/api/duels/{_v.Id}/actions", new DuelActionBody { Version = _v.Version, Action = a }), overlay: false);
                if (v != null) Show(v);
                else await Refresh0();
            }
            finally { _busy = false; ClearSel(); }
        }

        private async System.Threading.Tasks.Task Refresh0()
        {
            var v = await Call(() => Api.Get<DuelView>($"/api/duels/{_v.Id}"), overlay: false);   // stale_state etc.: resync
            if (v != null) Show(v);
        }

        private void Show(DuelView v)
        {
            var fresh = v.State.Log.Skip(Math.Min(_seenLog, v.State.Log.Count)).ToList();
            _v = v; _seenLog = v.State.Log.Count;
            ClearSel();
            if (fresh.Count > 0) _run.StartCoroutine(PlayFx(fresh));
        }

        private void ClearSel() { _handSel = null; _unitSel = null; _mode = ""; _picks.Clear(); Redraw(); }
        private void Redraw() { if (Root == null) return; UiKit.Clear(Root); Build(); }

        // ------------------------------------------------------------ layout

        private static readonly float[] ZoneX = { 0.25f, 0.39f, 0.53f, 0.32f, 0.46f };
        private const float BoardW = 0.78f;
        private static Vector2 Anchor(bool mine, int zone)
        {
            float x = ZoneX[zone] * BoardW / 0.78f;
            float y = mine ? (zone < 3 ? 0.42f : 0.29f) : (zone < 3 ? 0.62f : 0.75f);
            return new Vector2(x, y);
        }

        public override void Build()
        {
            _run = Root.gameObject.GetComponent<Runner>() ?? Root.gameObject.AddComponent<Runner>();
            if (!_ticking) { _ticking = true; _run.StartCoroutine(Tick()); }
            var bg = UiKit.Panel(Root, UiKit.Hex("#16245A"), 0, "Board"); ((RectTransform)bg.transform).Stretch();
            _unitRects.Clear();
            var me = St.Players[Me]; var op = St.Players[Opp];

            Hud(op, new Vector2(0.39f, 0.965f), false);
            Hud(me, new Vector2(0.39f, 0.205f), true);

            // phase strip
            var mid = UiKit.Text(Root, PhaseText(), 30, Color.white, UiKit.Weight.Bold);
            mid.rectTransform.Place(new Vector2(0.39f, 0.52f), new Vector2(1100, 50), Vector2.zero);
            _timer = UiKit.Text(Root, "", 30, UiKit.Gold, UiKit.Weight.ExtraBold);
            _timer.rectTransform.Place(new Vector2(0.66f, 0.52f), new Vector2(200, 50), Vector2.zero);

            for (int z = 0; z < Math.Max(5, Cfg.PetZones); z++) { Zone(true, z); Zone(false, z); }
            StRow(true); StRow(false);
            Hand(me);
            Panel(me);
            if (St.Finished || _v.Status == "finished") Result();
        }

        private DuelConfig Cfg => DuelCatalog.Data?.Config ?? new DuelConfig();

        private string PhaseText()
        {
            var who = St.Active == Me ? L.T("tduel.you") : (_v.OpponentName ?? L.T("tduel.opponent"));
            var phase = St.Phase == "battle" ? L.T("tduel.phase_battle") : St.Phase == "main" ? L.T("tduel.phase_main") : L.T("tduel.phase_end");
            var chain = St.Chain.Count > 0 ? "  ·  " + L.T("tduel.chain", St.Chain.Count) : "";
            return $"{L.T("tduel.turn", St.Turn)}  ·  {who}  ·  {phase}{chain}";
        }

        private void Hud(DuelPlayerState p, Vector2 anchor, bool mine)
        {
            var bar = UiKit.Panel(Root, new Color(0, 0, 0, 0.35f), 30, mine ? "MyHud" : "OppHud");
            ((RectTransform)bar.transform).Place(anchor, new Vector2(1250, 56), Vector2.zero);
            var name = mine ? "🔵 " + p.Name : "🔴 " + (_v.OpponentName ?? p.Name);
            UiKit.Text(bar.transform, $"{name}    LP <b>{p.Lp}</b>    ⚡{p.Energy}/{p.MaxEnergy}    🂠 {p.Hand.Count}    📚 {p.Deck.Count}    ⚰ {p.Grave.Count}", 30, Color.white, UiKit.Weight.Bold)
                .rectTransform.Stretch(10, 0, 10, 0);
        }

        private void Zone(bool mine, int z)
        {
            var side = mine ? Me : Opp;
            var u = St.Players[side].Units.FirstOrDefault(x => x.Zone == z);
            var rt = UiKit.Rect(Root, $"Z{side}{z}");
            rt.Place(Anchor(mine, z), new Vector2(250, 128), Vector2.zero);
            var img = rt.gameObject.AddComponent<Image>(); img.sprite = UiKit.RoundedSprite(20); img.type = Image.Type.Sliced;
            bool glow = u != null && Highlight(u);
            img.color = u == null ? new Color(1, 1, 1, 0.08f) : glow ? UiKit.Gold : mine ? UiKit.White : UiKit.Peach;
            var btn = rt.gameObject.AddComponent<Button>(); btn.onClick.AddListener(() => TapZone(mine, z));
            if (u == null) { UiKit.Text(rt, z < 3 ? L.T("tduel.front") : L.T("tduel.back"), 24, new Color(1, 1, 1, 0.5f)).rectTransform.Stretch(); return; }
            _unitRects[u.Uid] = rt;
            var art = UiKit.Icon(rt, null, new Vector2(100, 100), "Art"); ((RectTransform)art.transform).Place(new Vector2(0, 0.58f), new Vector2(100, 100), new Vector2(8, 0), new Vector2(0, 0.5f));
            art.raycastTarget = false; LoadSprite(art, "pet/" + u.FormId);
            UiKit.Text(rt, L.T("pet." + u.FormId), 22, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.TopLeft).rectTransform.Stretch(112, 6, 6, 80);
            UiKit.Text(rt, $"⚔{u.Atk} 🛡{u.Def}" + (u.Shield > 0 ? $" +{u.Shield}" : ""), 22, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.TopLeft).rectTransform.Stretch(112, 40, 6, 50);
            var st = string.Join(" ", u.Statuses.Where(s => s.Remaining > 0).Select(s => L.T("tduel.status." + s.Type)));
            if (st.Length > 0) UiKit.Text(rt, st, 18, UiKit.Hex("#8a3cc8"), UiKit.Weight.Bold, TextAlignmentOptions.TopLeft).rectTransform.Stretch(112, 70, 6, 26);
            var hpBg = UiKit.Panel(rt, new Color(0, 0, 0, 0.25f), 6, "HpBg"); ((RectTransform)hpBg.transform).Place(new Vector2(0.5f, 0), new Vector2(230, 14), new Vector2(0, 26), new Vector2(0.5f, 0));
            hpBg.raycastTarget = false;
            var hp = UiKit.Panel(hpBg.transform, (u.Hp * 100 < u.MaxHp * 30) ? UiKit.Danger : UiKit.Hex("#43BD78"), 6, "Hp");
            var hrt = (RectTransform)hp.transform; hrt.anchorMin = Vector2.zero; hrt.anchorMax = new Vector2(Mathf.Clamp01((float)u.Hp / Math.Max(1, u.MaxHp)), 1); hrt.offsetMin = hrt.offsetMax = Vector2.zero; hp.raycastTarget = false;
            UiKit.Text(rt, $"{u.Hp}/{u.MaxHp}", 20, UiKit.Ink, UiKit.Weight.Bold).rectTransform.Place(new Vector2(0.5f, 0), new Vector2(230, 24), new Vector2(0, 2), new Vector2(0.5f, 0));
            if (u.Divine) { var d = UiKit.Text(rt, "✦", 26, UiKit.GoldDeep, UiKit.Weight.ExtraBold); d.rectTransform.Place(new Vector2(1, 1), new Vector2(30, 30), new Vector2(-4, -4), new Vector2(1, 1)); }
            if (mine && St.Phase == "battle" && St.Active == Me && !DuelEngine.CanAct(St, u)) img.color = new Color(0.75f, 0.78f, 0.85f);
        }

        private bool Highlight(DuelUnit u)
        {
            if (_unitSel == u.Uid || _picks.Contains(u.Uid)) return true;
            if (u.Owner != Me && (_mode == "attack" || _mode == "skill" || _mode == "ultimate")) return DuelEngine.LegalTargets(St, Me).Any(t => t.Uid == u.Uid);
            var d = SelTactic();
            if (d != null) return u.Owner == Me ? d.Targeting is "own_pet" or "own_two" : d.Targeting == "enemy_pet";
            return false;
        }

        private void StRow(bool mine)
        {
            var side = mine ? Me : Opp; var zones = St.Players[side].Zones;
            for (int z = 0; z < zones.Count; z++)
            {
                var zc = zones[z]; int zi = z;
                bool ready = mine && MyReaction && DuelEngine.TrapReady(Rules, St, Me, zc);
                var rt = UiKit.Rect(Root, $"S{side}{z}");
                rt.Place(new Vector2(ZoneX[z], mine ? 0.155f : 0.86f), new Vector2(250, 52), Vector2.zero);
                var img = rt.gameObject.AddComponent<Image>(); img.sprite = UiKit.RoundedSprite(14); img.type = Image.Type.Sliced;
                img.color = zc == null ? new Color(1, 1, 1, 0.06f) : ready ? UiKit.Gold : UiKit.Hex("#5a2f96");
                rt.gameObject.AddComponent<Button>().onClick.AddListener(() => TapSt(mine, zi));
                var label = zc == null ? "S/T" : mine ? (DuelCatalog.Tactic(zc.TacticId) is TacticDef d ? DuelCatalog.Name(d) : zc.TacticId) : L.T("tduel.set_card");
                UiKit.Text(rt, label, 22, Color.white, UiKit.Weight.Bold).rectTransform.Stretch(6, 0, 6, 0);
            }
        }

        private void Hand(DuelPlayerState me)
        {
            int n = me.Hand.Count;
            for (int i = 0; i < n; i++)
            {
                int cid = me.Hand[i]; var c = St.Card(cid);
                float off = (i - (n - 1) / 2f);
                var rt = UiKit.Rect(Root, "H" + i);
                bool sel = _handSel == cid;
                rt.Place(new Vector2(0.39f, 0), new Vector2(150, 200), new Vector2(off * 128, sel ? 60 : 8), new Vector2(0.5f, 0));
                rt.localRotation = Quaternion.Euler(0, 0, -off * 3f);
                var d = c.Kind == "tactic" ? DuelCatalog.Tactic(c.TacticId) : null;
                var img = rt.gameObject.AddComponent<Image>(); img.sprite = UiKit.RoundedSprite(18); img.type = Image.Type.Sliced;
                img.color = sel ? UiKit.Gold : d == null ? UiKit.White : d.Type == "trap" ? UiKit.Lilac : UiKit.Mint;
                rt.gameObject.AddComponent<Button>().onClick.AddListener(() => TapHand(cid));
                if (c.Kind == "pet" && c.Forms.Count > 0)
                {
                    var f = c.Forms[0];
                    var art = UiKit.Icon(rt, null, new Vector2(120, 100), "Art"); ((RectTransform)art.transform).Place(new Vector2(0.5f, 1), new Vector2(120, 100), new Vector2(0, -6), new Vector2(0.5f, 1));
                    art.raycastTarget = false; LoadSprite(art, "pet/" + f.FormId);
                    UiKit.Text(rt, L.T("pet." + f.FormId), 20, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Stretch(4, 108, 4, 46);
                    var trib = DuelEngine.TributesFor(Rules, f);
                    UiKit.Text(rt, $"⚔{f.Atk} ❤{f.Hp}" + (trib > 0 ? $" ⛧{trib}" : ""), 18, UiKit.InkSoft, UiKit.Weight.Bold).rectTransform.Stretch(4, 156, 4, 6);
                }
                else if (d != null)
                {
                    UiKit.Text(rt, d.Type == "trap" ? "⛓" : d.Speed == "quick" ? "⚡" : "✦", 56, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Stretch(4, 6, 4, 110);
                    UiKit.Text(rt, DuelCatalog.Name(d), 20, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Stretch(4, 90, 4, 40);
                    UiKit.Text(rt, $"⚡{d.Cost}", 20, UiKit.InkSoft, UiKit.Weight.Bold).rectTransform.Stretch(4, 160, 4, 6);
                }
            }
        }

        private void Panel(DuelPlayerState me)
        {
            var p = UiKit.Panel(Root, new Color(0, 0, 0, 0.35f), 0, "Panel");
            var prt = (RectTransform)p.transform; prt.anchorMin = new Vector2(BoardW, 0); prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
            var col = UiKit.Rect(p.transform, "Col").Stretch(16, 16, 16, 16);
            UiKit.VStack(col, 10, null, TextAnchor.UpperCenter).childForceExpandHeight = false;

            if (MyReaction)
            {
                UiKit.Text(col, L.T("tduel.reaction"), 30, UiKit.Gold, UiKit.Weight.ExtraBold).Size(-1, 44);
                UiKit.Text(col, L.T("tduel.reaction_hint"), 22, Color.white).Size(-1, 70);
                UiKit.Button(col, L.T("tduel.pass"), () => Send(new DuelAction { Type = "pass" }), UiKit.ButtonStyle.Gold, 30).Size(-1, 90);
            }
            int h = _handSel ?? 0; var hs = _handSel != null ? St.Card(h) : null;
            if (hs != null)
            {
                var d = hs.Kind == "tactic" ? DuelCatalog.Tactic(hs.TacticId) : null;
                if (d != null)
                {
                    UiKit.Text(col, $"{DuelCatalog.Name(d)} · ⚡{d.Cost}", 28, Color.white, UiKit.Weight.ExtraBold).Size(-1, 44);
                    UiKit.Text(col, DuelCatalog.Text(d), 22, new Color(1, 1, 1, 0.85f), UiKit.Weight.Regular, TextAlignmentOptions.TopLeft).Size(-1, 120);
                    if (d.Type == "trap") UiKit.Text(col, L.T("tduel.hint_set"), 22, UiKit.Gold).Size(-1, 40);
                    else if (d.Targeting == "none") UiKit.Button(col, L.T("tduel.activate"), () => Send(new DuelAction { Type = "spell", Card = h, Targets = new List<int>() }), UiKit.ButtonStyle.Gold, 30).Size(-1, 90);
                    else if (d.Targeting is "hand_pet" or "grave_pet")
                    {
                        var pool = d.Targeting == "hand_pet" ? me.Hand.Where(x => x != h) : me.Grave.AsEnumerable();
                        foreach (var x in pool.Where(x => St.Card(x).Kind == "pet").Distinct().Take(6))
                        {
                            int xi = x;
                            UiKit.Button(col, L.T("pet." + St.Card(x).Forms[0].FormId), () => Send(new DuelAction { Type = "spell", Card = h, Targets = new List<int> { xi } }), UiKit.ButtonStyle.Secondary, 24).Size(-1, 70);
                        }
                    }
                    else UiKit.Text(col, L.T("tduel.hint_target"), 22, UiKit.Gold).Size(-1, 40);
                }
                else if (hs.Forms.Count > 0)
                {
                    var f = hs.Forms[0]; int trib = DuelEngine.TributesFor(Rules, f);
                    UiKit.Text(col, $"{L.T("pet." + f.FormId)} · {L.T("tduel.stage_n", f.Stage)}", 28, Color.white, UiKit.Weight.ExtraBold).Size(-1, 44);
                    UiKit.Text(col, trib > 0 ? L.T("tduel.hint_tribute", trib, _picks.Count) : L.T("tduel.hint_summon"), 22, UiKit.Gold).Size(-1, 70);
                }
            }
            if (_unitSel is int us && DuelEngine.Unit(St, us) is DuelUnit u)
            {
                UiKit.Text(col, $"{L.T("pet." + u.FormId)}  ❤{u.Hp}/{u.MaxHp}", 26, Color.white, UiKit.Weight.ExtraBold).Size(-1, 44);
                if (St.Phase == "main")
                {
                    var cd = St.Card(u.CardId);
                    if (u.FormIndex + 1 < cd.Forms.Count)
                    {
                        int cost = Cfg.EvolveCost.Count > 0 ? Cfg.EvolveCost[Math.Min(Cfg.EvolveCost.Count - 1, Math.Max(1, u.Stage))] : 0;
                        UiKit.Button(col, L.T("tduel.evolve", cost), () => Send(new DuelAction { Type = "evolve", Unit = us }), UiKit.ButtonStyle.Gold, 26).Size(-1, 80);
                    }
                    UiKit.Button(col, L.T("tduel.move"), () => { _mode = "move"; Redraw(); }, UiKit.ButtonStyle.Secondary, 26).Size(-1, 80);
                }
                else if (DuelEngine.CanAct(St, u))
                {
                    UiKit.Button(col, L.T("tduel.attack"), () => { _mode = "attack"; Redraw(); }, _mode == "attack" ? UiKit.ButtonStyle.Gold : UiKit.ButtonStyle.Secondary, 26).Size(-1, 80);
                    if (u.Skill != null) UiKit.Button(col, L.T("skill." + u.Skill) + (Rules.Skills.TryGetValue(u.Skill, out var sk) ? $" ⚡{sk.Cost}" : ""), () => { _mode = "skill"; Redraw(); }, _mode == "skill" ? UiKit.ButtonStyle.Gold : UiKit.ButtonStyle.Secondary, 24).Size(-1, 80);
                    if (u.Ultimate != null) UiKit.Button(col, "★ " + L.T("skill." + u.Ultimate) + (Rules.Skills.TryGetValue(u.Ultimate, out var ul) ? $" ⚡{ul.Cost}" : ""), () => { _mode = "ultimate"; Redraw(); }, _mode == "ultimate" ? UiKit.ButtonStyle.Gold : UiKit.ButtonStyle.Secondary, 24).Size(-1, 80);
                }
            }
            if (MyControl && St.Phase == "main")
            {
                bool firstTurn = St.First == Me && me.TurnsTaken == 1;
                var b = UiKit.Button(col, firstTurn ? L.T("tduel.no_first_attack") : L.T("tduel.battle"), () => Send(new DuelAction { Type = "battle" }), UiKit.ButtonStyle.Primary, 28);
                b.Size(-1, 90); b.interactable = !firstTurn;
            }
            if (MyControl) UiKit.Button(col, L.T("tduel.end_turn"), () => Send(new DuelAction { Type = "end" }), UiKit.ButtonStyle.Secondary, 28).Size(-1, 90);
            if (_handSel != null || _unitSel != null) UiKit.Button(col, L.T("tduel.cancel"), ClearSel, UiKit.ButtonStyle.Ghost, 24).Size(-1, 70);

            var log = string.Join("\n", St.Log.AsEnumerable().Reverse().Where(e => e.Type is not ("coin" or "buff")).Take(8).Select(LogLine));
            UiKit.Text(col, log, 20, new Color(1, 1, 1, 0.8f), UiKit.Weight.Regular, TextAlignmentOptions.TopLeft).Size(-1, 220);
            if (Active) UiKit.Button(col, L.T("duel.surrender"), () => Send(new DuelAction { Type = "surrender" }), UiKit.ButtonStyle.Ghost, 24).Size(-1, 70);
        }

        private string LogLine(DuelEvent e)
        {
            var who = e.P == Me ? L.T("tduel.you") : e.P == Opp ? (_v.OpponentName ?? L.T("tduel.opponent")) : "";
            switch (e.Type)
            {
                case "spell": case "trap": return $"{who}: {(DuelCatalog.Tactic(e.S) is TacticDef d ? DuelCatalog.Name(d) : e.S)}";
                case "damage": return $"  −{e.N} HP";
                case "heal": return $"  +{e.N} HP";
                case "destroyed": return $"{who}: {L.T("tduel.log_destroyed", Cfg.DeathPenalty)}";
                default: return L.T("tduel.log." + e.Type, who);
            }
        }

        // ------------------------------------------------------------ interaction

        private TacticDef? SelTactic() => _handSel is int h && St.Card(h) is DuelCard c && c.Kind == "tactic" ? DuelCatalog.Tactic(c.TacticId) : null;

        private void TapHand(int cid)
        {
            if (!MyControl && !MyReaction) return;
            if (_handSel == cid) { ClearSel(); return; }
            _handSel = cid; _unitSel = null; _mode = ""; _picks.Clear();
            var d = SelTactic();
            if (MyReaction && d != null && d.Type == "spell" && d.Speed == "quick" && d.Targeting == "none") { Send(new DuelAction { Type = "spell", Card = cid, Targets = new List<int>() }); return; }
            Redraw();
        }

        private void TapZone(bool mine, int z)
        {
            var side = mine ? Me : Opp;
            var u = St.Players[side].Units.FirstOrDefault(x => x.Zone == z);
            var card = _handSel is int h ? St.Card(h) : null;
            var d = SelTactic();
            if (mine)
            {
                if (card != null && card.Kind == "pet" && card.Forms.Count > 0)
                {
                    int need = DuelEngine.TributesFor(Rules, card.Forms[0]);
                    if (u != null && _picks.Count < need) { if (!_picks.Remove(u.Uid)) _picks.Add(u.Uid); Redraw(); return; }
                    if (_picks.Count == need) { Send(new DuelAction { Type = "summon", Card = card.Id, Zone = z, Tributes = _picks.ToList() }); return; }
                }
                if (d != null && u != null && d.Targeting == "own_pet") { Send(new DuelAction { Type = "spell", Card = _handSel ?? 0, Targets = new List<int> { u.Uid } }); return; }
                if (d != null && u != null && d.Targeting == "own_two")
                {
                    if (_picks.Count == 1 && _picks[0] != u.Uid) { Send(new DuelAction { Type = "spell", Card = _handSel ?? 0, Targets = new List<int> { _picks[0], u.Uid } }); return; }
                    _picks.Clear(); _picks.Add(u.Uid); Redraw(); return;
                }
                if (_unitSel is int us && _mode == "move") { Send(new DuelAction { Type = "move", Unit = us, Zone = z }); return; }
                if (u != null && MyControl) { _unitSel = u.Uid; _handSel = null; _mode = ""; _picks.Clear(); Redraw(); }
                return;
            }
            if (u == null) return;
            if (_unitSel is int a && _mode is "attack" or "skill" or "ultimate") { Send(new DuelAction { Type = _mode, Unit = a, Target = u.Uid }); return; }
            if (d != null && d.Targeting == "enemy_pet") Send(new DuelAction { Type = "spell", Card = _handSel ?? 0, Targets = new List<int> { u.Uid } });
        }

        private void TapSt(bool mine, int z)
        {
            var d = SelTactic();
            if (mine)
            {
                if (MyReaction && DuelEngine.TrapReady(Rules, St, Me, St.Players[Me].Zones[z])) { Send(new DuelAction { Type = "activate", Zone = z }); return; }
                if (d != null && d.Type == "trap" && MyControl) Send(new DuelAction { Type = "set", Card = _handSel ?? 0, Zone = z });
                return;
            }
            if (d != null && d.Targeting == "enemy_st") Send(new DuelAction { Type = "spell", Card = _handSel ?? 0, Targets = new List<int> { z } });
        }

        /// <summary>Rules for client-side hints only (tributes, trap readiness, legal targets); the server decides.</summary>
        private DuelRules Rules => _rules ??= new DuelRules
        {
            Config = Cfg,
            Tactics = (DuelCatalog.Data?.Tactics ?? new List<DuelTacticOwned>()).ToDictionary(t => t.Def.Id, t => t.Def),
        };
        private DuelRules? _rules;

        // ------------------------------------------------------------ effects, timer, result

        private IEnumerator PlayFx(List<DuelEvent> evs)
        {
            foreach (var e in evs.Take(24))
            {
                string? txt = e.Type switch
                {
                    "damage" => $"−{e.N}", "heal" => $"+{e.N}", "destroyed" => L.T("tduel.fx_destroyed", Cfg.DeathPenalty),
                    "evolve" => L.T("tduel.fx_evolve"), "summon" => L.T("tduel.fx_summon"), "negate" => L.T("tduel.fx_negate"), _ => null,
                };
                if (e.Type is "spell" or "trap") Banner((e.P == Me ? L.T("tduel.you") : L.T("tduel.opponent")) + ": " + (DuelCatalog.Tactic(e.S) is TacticDef d ? DuelCatalog.Name(d) : e.S));
                if (txt == null) continue;
                var host = _unitRects.TryGetValue(e.A, out var r) && r != null ? r : (RectTransform)Root;
                var t = UiKit.Text(host, txt, e.Type == "damage" ? 44 : 32, e.Type == "damage" ? UiKit.Danger : e.Type == "heal" ? UiKit.Hex("#43BD78") : UiKit.Gold, UiKit.Weight.ExtraBold);
                t.rectTransform.Place(new Vector2(0.5f, 0.7f), new Vector2(360, 60), Vector2.zero); t.raycastTarget = false;
                t.gameObject.AddComponent<FloatUp>();
                yield return new WaitForSeconds(0.18f);
            }
        }

        private void Banner(string text)
        {
            var b = UiKit.Panel(Root, UiKit.Gold, 24, "Banner"); ((RectTransform)b.transform).Place(new Vector2(0.39f, 0.52f), new Vector2(900, 80), Vector2.zero);
            b.raycastTarget = false;
            UiKit.Text(b.transform, text, 32, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Stretch();
            UnityEngine.Object.Destroy(b.gameObject, 1.4f);
        }

        private IEnumerator Tick()
        {
            float poll = 0;
            while (Root != null)
            {
                if (_timer != null && _v.DeadlineAt is DateTimeOffset dl && Active)
                {
                    var skew = _v.ServerNow - DateTimeOffset.UtcNow;   // display only; the server enforces deadlines
                    var left = dl - (DateTimeOffset.UtcNow + skew);
                    _timer.text = left.TotalSeconds > 0 ? $"⏱ {(int)left.TotalSeconds}s" : "⏱ …";
                }
                poll += 1;
                bool waiting = Active && St.ToAct != Me;
                bool expired = _v.DeadlineAt is DateTimeOffset d2 && d2 < _v.ServerNow + (DateTimeOffset.UtcNow - _v.ServerNow);
                if ((waiting && poll >= 2) || expired) { poll = 0; _ = Refresh(); }
                yield return new WaitForSeconds(1f);
            }
            _ticking = false;
        }

        private void Result()
        {
            var o = UiKit.Panel(Root, new Color(0.02f, 0.04f, 0.12f, 0.75f), 0, "Over"); ((RectTransform)o.transform).Stretch();
            var box = UiKit.Panel(o.transform, UiKit.White, 40, "Box", shadow: true); ((RectTransform)box.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(900, 520), Vector2.zero);
            var win = St.Winner == Me; var draw = St.Winner == null;
            UiKit.Text(box.transform, draw ? L.T("tduel.draw") : win ? L.T("tduel.victory") : L.T("tduel.defeat"), 80, draw ? UiKit.Ink : win ? UiKit.GoldDeep : UiKit.Danger, UiKit.Weight.ExtraBold)
                .rectTransform.Place(new Vector2(0.5f, 1), new Vector2(840, 120), new Vector2(0, -30), new Vector2(0.5f, 1));
            UiKit.Text(box.transform, L.T("tduel.end." + (St.EndReason ?? "lp_zero")) + $"\nLP {St.Players[Me].Lp} / {St.Players[Opp].Lp}", 32, UiKit.InkSoft)
                .rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(840, 140), new Vector2(0, 20));
            if (!string.IsNullOrEmpty(_v.RewardsJson) && win) UiKit.Text(box.transform, L.T("tduel.rewards_granted"), 28, UiKit.Sapphire, UiKit.Weight.Bold)
                .rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(840, 50), new Vector2(0, -70));
            var back = UiKit.Button(box.transform, L.T("tduel.back_lobby"), () => Nav.Back(), UiKit.ButtonStyle.Gold, 34);
            ((RectTransform)back.transform).Place(new Vector2(0.5f, 0), new Vector2(400, 110), new Vector2(0, 30), new Vector2(0.5f, 0));
        }

        private static async void LoadSprite(Image img, string address)
        {
            var s = await GameDataClient.LoadSprite(address);
            if (img != null && s != null) { img.sprite = s; img.color = Color.white; }
        }

        private sealed class Runner : MonoBehaviour { }
    }
}
