using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using PetStar.Rules.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    public sealed class DuelLobbyScreen : ScreenBase
    {
        public override string TitleKey => "duel.title";
        private List<PveStageDto>? _stages;
        private List<DeckDto>? _decks;

        public override void Build()
        {
            float top = Wide ? 140 : 320;
            var deckInfo = UiKit.Panel(Root, UiKit.White, 36, "DeckInfo", shadow: true);
            ((RectTransform)deckInfo.transform).Place(new Vector2(0.5f, 1), new Vector2(Wide ? 1300 : 1000, 130), new Vector2(0, -top), new Vector2(0.5f, 1));
            var def = _decks?.FirstOrDefault(d => d.IsDefault);
            UiKit.Text(deckInfo.transform, def == null ? L.T("duel.no_deck") : L.T("duel.deck_power", def.Name, def.TotalPower), 36, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Stretch(40, 0, 420, 0);
            var auto = UiKit.Button(deckInfo.transform, L.T("duel.auto_deck"), AutoDeck, UiKit.ButtonStyle.Secondary, 32);
            ((RectTransform)auto.transform).Place(new Vector2(1, 0.5f), new Vector2(360, 100), new Vector2(-20, 0), new Vector2(1, 0.5f));

            var list = UiKit.Scroll(UiKit.Rect(Root, "Stages").Stretch(40, top + 170, 40, 40));
            UiKit.VStack(list, 20, new RectOffset(0, 0, 10, 40));
            UiKit.Text(list, L.T("duel.pve_chapter", 1), 40, UiKit.Sapphire, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 70);
            foreach (var s in _stages ?? new List<PveStageDto>())
            {
                var row = UiKit.Panel(list, s.Unlocked ? UiKit.White : UiKit.SkyDeep, 36, s.Id); row.Size(-1, 170);
                UiKit.Text(row.transform, L.T("stage." + s.Id), 42, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(40, 20, 320, 80);
                UiKit.Text(row.transform, L.T("duel.stage_info", s.ClearCount, s.RewardedClearsLeftToday), 28, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(40, 95, 320, 15);
                var id = s.Id;
                var b = UiKit.Button(row.transform, s.Unlocked ? L.T("duel.fight") : L.T("duel.locked"), () => Start(id), s.Unlocked ? UiKit.ButtonStyle.Gold : UiKit.ButtonStyle.Secondary, 36);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(260, 110), new Vector2(-30, 0), new Vector2(1, 0.5f));
                b.interactable = s.Unlocked && def != null;
            }
            var pvp = UiKit.Text(list, L.T("duel.pvp_next_phase"), 30, UiKit.InkSoft, UiKit.Weight.Regular);
            pvp.Size(-1, 100);
        }

        public override async void OnShow()
        {
            _stages = await Call(() => Api.Get<List<PveStageDto>>("/api/pve/stages"));
            _decks = await Call(() => Api.Get<List<DeckDto>>("/api/decks"), overlay: false);
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }

        /// <summary>Builds a valid default deck from the strongest cards (max 2 per family), server validates it.</summary>
        private async void AutoDeck()
        {
            var page = await Call(() => Api.Get<CardPageDto>("/api/cards?sort=power&pageSize=200"));
            if (page == null) return;
            var pick = new List<CardDto>();
            foreach (var c in page.Items)
                if (pick.Count < 5 && pick.Count(x => x.FamilyId == c.FamilyId) < 2) pick.Add(c);
            if (pick.Count < 5) { Nav.Toast(L.T("duel.need_cards"), true); return; }
            // tanky cards in front, damage/support in back
            var ordered = pick.OrderByDescending(c => c.Role == "guardian" ? 3 : c.Role == "striker" ? 2 : c.Role == "controller" ? 1 : 0).ThenByDescending(c => c.Hp).ToList();
            var req = new SaveDeckRequest { Name = L.T("duel.default_deck_name"), IsDefault = true, Slots = ordered.Select((c, i) => new DeckSlotDto { Position = i, CardId = c.Id }).ToList() };
            var existing = _decks?.FirstOrDefault(d => d.IsDefault);
            var saved = existing == null ? await Call(() => Api.Post<DeckDto>("/api/decks", req)) : await Call(() => Api.Put<DeckDto>($"/api/decks/{existing.Id}", req));
            if (saved != null) { Nav.Toast(L.T("duel.deck_saved")); OnShow(); }
        }

        private async void Start(string stageId)
        {
            var b = await Call(() => Api.Post<BattleDto>("/api/battles/pve", new StartPveRequest { StageId = stageId }, ApiClient.NewIdempotencyKey()));
            if (b != null) Nav.Push(new DuelBattleScreen(b));
        }
    }

    /// <summary>
    /// Duel board: 5 cards per side (3 frontline + 2 backline), energy, round, plan actions, combat log.
    /// The client only submits intentions; the server validates, resolves (priority, SPD, tie rule) and returns events.
    /// </summary>
    public sealed class DuelBattleScreen : ScreenBase
    {
        public override bool IsDuel => true;
        public override bool ShowCurrencyBar => false;
        public override bool ShowBackButton => false;
        private BattleDto _b;
        private readonly Dictionary<int, PlannedAction> _plan = new();
        private int? _selected;
        private readonly Dictionary<(int side, int idx), RectTransform> _slots = new();
        private TextMeshProUGUI _energy = null!, _log = null!;
        private bool _busy;
        private FxRunner _anim = null!;
        private float Cw => Wide ? 130 : 168;

        // Live PvP: sealed plans, 30 s deadline; the server resolves when both sides submitted (or at the deadline)
        private readonly bool _live;
        private bool _waiting;
        private TextMeshProUGUI? _timer;
        private bool _subscribed, _ticking;

        public DuelBattleScreen(BattleDto b, bool live = false) { _live = live; _b = live ? LiveBattle.Normalize(b) : b; }

        public override void OnShow()
        {
            if (!_live || Realtime.I == null || _subscribed) return; // OnShow runs again on layout rebuilds
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

        private void OnRealtime(string type, JToken? data)
        {
            if (type == "pvp_round" && data?["id"]?.ToString() == _b.Id.ToString()) _ = FetchLive();
        }

        private void OnReconnect() => _ = FetchLive(); // never trust that a push arrived while disconnected

        private async System.Threading.Tasks.Task FetchLive()
        {
            if (_busy) return; // a round animation is playing; the next poll catches up
            var v = await Call(() => Api.Get<JObject>($"/api/pvp/battles/{_b.Id}"), overlay: false);
            if (v == null) return;
            var next = LiveBattle.Normalize(v["battle"]!.ToObject<BattleDto>(Newtonsoft.Json.JsonSerializer.Create(ApiClient.Json))!);
            if (next.Round != _b.Round || next.Status != _b.Status)
            {
                _waiting = false;
                _plan.Clear(); _selected = null;
                _busy = true; // Play() clears it when the animation ends
                _anim.StartCoroutine(Play(next));
            }
        }

        private IEnumerator LiveTick()
        {
            while (_live && _b.Status == "active" && Root != null)
            {
                if (_timer != null && _b.RoundDeadline is System.DateTimeOffset dl)
                {
                    var left = dl - System.DateTimeOffset.UtcNow;
                    _timer.text = (_waiting ? L.T("web.waiting_opponent") + "  " : "") + (left.TotalSeconds > 0 ? $"{(int)left.TotalSeconds}s" : "…");
                }
                // fallback polling while waiting (covers missed pushes and server-side deadline resolution)
                if (_waiting || (_b.RoundDeadline is System.DateTimeOffset d2 && d2 < System.DateTimeOffset.UtcNow)) _ = FetchLive();
                yield return new WaitForSeconds(1.5f);
            }
            _ticking = false;
        }

        public override void Build()
        {
            _anim = Root.gameObject.AddComponent<FxRunner>();
            var bg = UiKit.Icon(Root, null, Vector2.zero, "Board"); ((RectTransform)bg.transform).Stretch(); bg.preserveAspect = false;
            LoadBg(bg, Wide ? "ui/bg_duel_board_landscape" : "ui/bg_duel_board_portrait");

            // HUD
            var hud = UiKit.Panel(Root, new Color(1, 1, 1, 0.92f), 36, "Hud", shadow: true);
            ((RectTransform)hud.transform).Place(new Vector2(0.5f, 1), new Vector2(Wide ? 1100 : 1020, 110), new Vector2(0, Wide ? -16 : -50), new Vector2(0.5f, 1));
            UiKit.Text(hud.transform, L.T("duel.round", _b.Round), 38, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(30, 0, 600, 0);
            _energy = UiKit.Text(hud.transform, "", 38, UiKit.GoldDeep, UiKit.Weight.ExtraBold);
            _energy.rectTransform.Stretch(250, 0, 250, 0);
            var surrender = UiKit.Button(hud.transform, L.T("duel.surrender"), Surrender, UiKit.ButtonStyle.Ghost, 28);
            ((RectTransform)surrender.transform).Place(new Vector2(1, 0.5f), new Vector2(220, 90), new Vector2(-10, 0), new Vector2(1, 0.5f));

            // board: opponent on top/right, player bottom/left; front row toward the center line
            float cw = Cw;
            foreach (var u in _b.Units)
            {
                var slot = UiKit.Rect(Root, $"Slot{u.Side}{u.Index}");
                slot.sizeDelta = new Vector2(cw, cw / CardView.Aspect);
                slot.anchorMin = slot.anchorMax = SlotAnchor(u.Side, u.Position);
                slot.anchoredPosition = Vector2.zero;
                _slots[(u.Side, u.Index)] = slot;
                DrawUnit(slot, u, cw);
            }

            // action bar + log
            var bar = UiKit.Rect(Root, "Actions");
            if (Wide) bar.Place(new Vector2(0.5f, 0), new Vector2(1100, 120), new Vector2(0, 16), new Vector2(0.5f, 0));
            else bar.Place(new Vector2(0.5f, 0), new Vector2(1020, 120), new Vector2(0, 40), new Vector2(0.5f, 0));
            UiKit.HStack(bar, 14).childForceExpandWidth = true;
            UiKit.Button(bar, L.T("duel.basic"), () => Choose(ActionType.Basic), UiKit.ButtonStyle.Secondary, 30).Size(-1, 110, 1);
            UiKit.Button(bar, L.T("duel.skill"), () => Choose(ActionType.Skill), UiKit.ButtonStyle.Secondary, 30).Size(-1, 110, 1);
            UiKit.Button(bar, L.T("duel.ultimate"), () => Choose(ActionType.Ultimate), UiKit.ButtonStyle.Secondary, 30).Size(-1, 110, 1);
            UiKit.Button(bar, L.T("duel.defend"), () => Choose(ActionType.Defend), UiKit.ButtonStyle.Secondary, 30).Size(-1, 110, 1);
            UiKit.Button(bar, L.T("duel.end_turn"), Submit, UiKit.ButtonStyle.Gold, 32).Size(-1, 110, 1.4f);

            var logPanel = UiKit.Panel(Root, new Color(1, 1, 1, 0.86f), 30, "Log");
            var lrt = (RectTransform)logPanel.transform;
            if (Wide) lrt.Place(new Vector2(1, 0.5f), new Vector2(380, 640), new Vector2(-16, 0), new Vector2(1, 0.5f));
            else lrt.Place(new Vector2(0.5f, 0), new Vector2(1020, 140), new Vector2(0, 168), new Vector2(0.5f, 0));
            _log = UiKit.Text(logPanel.transform, "", 24, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.BottomLeft);
            _log.rectTransform.Stretch(20, 10, 20, 10);
            _log.overflowMode = TextOverflowModes.Truncate;
            if (_live)
            {
                _timer = UiKit.Text(hud.transform, "", 30, UiKit.Danger, UiKit.Weight.Bold, TextAlignmentOptions.MidlineRight);
                _timer.rectTransform.Stretch(600, 0, 240, 0);
                if (!_ticking) { _ticking = true; _anim.StartCoroutine(LiveTick()); }
            }
            RefreshHud();
            if (_b.Status != "active") ShowEnd();
        }

        /// <summary>Layouts keep all 10 cards visible without overlap (portrait: rows; landscape: columns).</summary>
        private Vector2 SlotAnchor(int side, int position)
        {
            bool front = position < 3;
            int i = front ? position : position - 3;
            if (Wide)
            {
                float x = side == 0 ? (front ? 0.32f : 0.18f) : (front ? 0.56f : 0.70f); // log column on the right
                float y = front ? 0.76f - i * 0.26f : 0.63f - i * 0.26f;
                return new Vector2(x, y);
            }
            float yy = side == 0 ? (front ? 0.455f : 0.275f) : (front ? 0.645f : 0.82f);
            float xx = front ? 0.2f + i * 0.3f : 0.35f + i * 0.3f;
            return new Vector2(xx, yy);
        }

        private void DrawUnit(RectTransform slot, BattleUnitDto u, float cw)
        {
            UiKit.Clear(slot);
            var card = new CardDto
            {
                Id = u.CardId, FormId = u.FormId, Name = GameDataClient.PetName(u.FormId), Element = u.Element, Rarity = u.Rarity, Level = u.Level,
                Hp = u.Hp, Atk = u.Atk, Def = u.Def, Spd = u.Spd, SkillId = u.SkillId, UltimateId = u.UltimateId, PassiveId = u.PassiveId,
                Quality = "", Finish = "Normal", Role = GameDataClient.Forms.TryGetValue(u.FormId, out var f) ? f.Role : "",
                FamilyId = f?.FamilyId ?? "", Stage = f?.Stage ?? 1,
            };
            var cv = CardView.Create(slot, card, cw, interactive: false);
            var crt = (RectTransform)cv.transform; crt.Stretch();
            var cg = cv.gameObject.AddComponent<CanvasGroup>();
            if (u.Hp <= 0) { cg.alpha = 0.35f; cv.GetComponent<Image>().color = Color.gray; }
            // HP bar
            var bar = UiKit.Panel(slot, new Color(0, 0, 0, 0.35f), 8, "HpBack");
            ((RectTransform)bar.transform).Place(new Vector2(0.5f, 0), new Vector2(cw - 10, 18), new Vector2(0, -24), new Vector2(0.5f, 0));
            var fill = UiKit.Panel(bar.transform, u.Side == 0 ? UiKit.Hex("#3BB273") : UiKit.Danger, 8, "HpFill");
            var frt = (RectTransform)fill.transform; frt.Stretch();
            frt.anchorMax = new Vector2(Mathf.Clamp01(u.Hp / (float)Mathf.Max(1, u.MaxHp)), 1);
            var hpText = UiKit.Text(slot, $"{u.Hp}/{u.MaxHp}" + (u.Shield > 0 ? $" <color=#3D8BF2>+{u.Shield}</color>" : ""), 22, UiKit.Ink, UiKit.Weight.Bold);
            hpText.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(cw + 40, 30), new Vector2(0, -52), new Vector2(0.5f, 0));
            if (u.Statuses.Count > 0)
            {
                var st = UiKit.Text(slot, string.Join(" ", u.Statuses.Select(s => L.T("status.short." + s))), 22, UiKit.Danger, UiKit.Weight.Bold);
                st.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(cw + 40, 30), new Vector2(0, 30), new Vector2(0.5f, 1));
            }
            var btn = slot.gameObject.GetComponent<Button>() ?? slot.gameObject.AddComponent<Button>();
            if (slot.GetComponent<Image>() == null) { var hit = slot.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); }
            btn.onClick.RemoveAllListeners();
            int side = u.Side, idx = u.Index;
            btn.onClick.AddListener(() => OnSlot(side, idx));
            if (_plan.TryGetValue(u.Index, out var a) && u.Side == 0)
            {
                var tag = UiKit.Panel(slot, UiKit.Gold, 16, "Plan");
                ((RectTransform)tag.transform).Place(new Vector2(0.5f, 1), new Vector2(cw, 40), new Vector2(0, 8), new Vector2(0.5f, 0));
                UiKit.Text(tag.transform, L.T("duel.short." + a.Type) + (a.TargetIndex.HasValue ? " → " + (a.TargetIndex + 1) : ""), 22, UiKit.Ink, UiKit.Weight.Bold).rectTransform.Stretch();
            }
            if (_selected == u.Index && u.Side == 0) { var o = cv.gameObject.AddComponent<Outline>(); o.effectColor = UiKit.Gold; o.effectDistance = new Vector2(6, -6); }
        }

        private void OnSlot(int side, int idx)
        {
            if (_busy || _b.Status != "active") return;
            var u = _b.Units.First(x => x.Side == side && x.Index == idx);
            if (side == 0)
            {
                if (u.Hp <= 0 || u.Statuses.Contains("stun")) return;
                _selected = idx;
            }
            else if (_selected.HasValue && u.Hp > 0)
            {
                // choose a target for the selected unit's planned (or default basic) action
                var a = _plan.TryGetValue(_selected.Value, out var existing) ? existing : new PlannedAction { UnitIndex = _selected.Value, Type = ActionType.Basic };
                a.TargetIndex = idx;
                _plan[_selected.Value] = a;
            }
            else { CardDetailsForUnit(u); return; }
            Redraw();
        }

        private void CardDetailsForUnit(BattleUnitDto u)
        {
            var c = new CardDto { Id = u.CardId, FormId = u.FormId, Name = GameDataClient.PetName(u.FormId), Element = u.Element, Rarity = u.Rarity, Level = u.Level, Hp = u.Hp, Atk = u.Atk, Def = u.Def, Spd = u.Spd, SkillId = u.SkillId, UltimateId = u.UltimateId, PassiveId = u.PassiveId, Quality = "", Finish = "Normal" };
            var panel = Nav.Popup(new Vector2(700, 1180), new Vector2(620, 1000), out var close);
            var cv = CardView.Create(panel, c, 560);
            ((RectTransform)cv.transform).Place(new Vector2(0.5f, 0.5f), ((RectTransform)cv.transform).sizeDelta, Vector2.zero);
        }

        private void Choose(ActionType t)
        {
            if (!_selected.HasValue) { Nav.Toast(L.T("duel.select_card_first")); return; }
            var u = _b.Units.First(x => x.Side == 0 && x.Index == _selected.Value);
            if (t == ActionType.Skill && u.SkillId == null || t == ActionType.Ultimate && u.UltimateId == null) { Nav.Toast(L.T("duel.no_such_skill"), true); return; }
            var prev = _plan.TryGetValue(u.Index, out var p) ? p : null;
            var a = new PlannedAction { UnitIndex = u.Index, Type = t, TargetIndex = prev?.TargetIndex };
            _plan[u.Index] = a;
            if (PlannedCost() > _b.MyEnergy) { _plan.Remove(u.Index); if (prev != null) _plan[u.Index] = prev; Nav.Toast(L.T("error.not_enough_energy"), true); }
            Redraw();
        }

        private int Cost(BattleUnitDto u, ActionType t)
        {
            string? id = t == ActionType.Skill ? u.SkillId : t == ActionType.Ultimate ? u.UltimateId : null;
            return id != null && GameDataClient.Skills.TryGetValue(id, out var s) ? s.Cost : 0;
        }

        private int PlannedCost() => _plan.Values.Sum(a => Cost(_b.Units.First(x => x.Side == 0 && x.Index == a.UnitIndex), a.Type));

        private void RefreshHud() => _energy.text = L.T("duel.energy", _b.MyEnergy - PlannedCost(), _b.MyEnergy);

        private void Redraw()
        {
            foreach (var u in _b.Units) DrawUnit(_slots[(u.Side, u.Index)], u, Cw);
            RefreshHud();
        }

        private async void Submit()
        {
            if (_busy || _waiting || _b.Status != "active") return;
            _busy = true;
            var req = new SubmitRoundRequest { Round = _b.Round, Actions = _plan.Values.ToList() };
            if (_live)
            {
                var r = await Call(() => Api.Post<JObject>($"/api/pvp/battles/{_b.Id}/rounds", req), overlay: false);
                _busy = false;
                if (r == null) return;
                _waiting = true; // plan locked; the round resolves when the opponent submits or the deadline passes
                if ((bool?)r["resolved"] == true) await FetchLive();
                return;
            }
            var next = await Call(() => Api.Post<BattleDto>($"/api/battles/{_b.Id}/rounds", req), overlay: false);
            if (next == null) { _busy = false; return; }
            _plan.Clear(); _selected = null;
            _anim.StartCoroutine(Play(next));
        }

        /// <summary>Plays server events in order: attack lunge, shake, damage numbers, heals, shields, defeats.</summary>
        private IEnumerator Play(BattleDto next)
        {
            var lines = new List<string>();
            foreach (var e in next.LastEvents)
            {
                switch (e.Type)
                {
                    case "action":
                        var key = e.SkillId == "basic_attack" ? Audio.Sfx.BasicAttack : GameDataClient.Skills.TryGetValue(e.SkillId ?? "", out var sd) && sd.Kind == "ultimate" ? Audio.Sfx.Ultimate : Audio.Sfx.Skill;
                        Audio.Play(key);
                        if (_slots.TryGetValue((e.ActorSide, e.ActorIndex), out var actor)) yield return Lunge(actor, e.ActorSide);
                        lines.Add($"{Who(e.ActorSide, e.ActorIndex)}: {(e.SkillId == "basic_attack" ? L.T("duel.basic") : SkillText.Name(e.SkillId ?? ""))}");
                        break;
                    case "damage":
                        if (_slots.TryGetValue((e.TargetSide, e.TargetIndex), out var t1)) { _anim.StartCoroutine(Shake(t1)); Float(t1, "-" + e.Amount + (e.Crit ? "!" : ""), e.ElementMultiplier > 1 ? UiKit.Hex("#FF7A1A") : UiKit.Danger, e.Crit ? 64 : 50); }
                        lines.Add($"  → {Who(e.TargetSide, e.TargetIndex)} -{e.Amount}{(e.Crit ? " CRIT" : "")}{(e.ElementMultiplier > 1 ? " ▲" : e.ElementMultiplier < 1 ? " ▼" : "")}");
                        yield return new WaitForSeconds(0.18f);
                        break;
                    case "heal":
                        if (e.Amount > 0 && _slots.TryGetValue((e.TargetSide, e.TargetIndex), out var t2)) Float(t2, "+" + e.Amount, UiKit.Hex("#2E9E62"), 48);
                        break;
                    case "shield":
                        if (_slots.TryGetValue((e.TargetSide, e.TargetIndex), out var t3)) Float(t3, "+" + e.Amount, UiKit.Hex("#3D8BF2"), 40);
                        break;
                    case "status_applied":
                        lines.Add($"  {Who(e.TargetSide, e.TargetIndex)}: {L.T("status." + e.Status)}");
                        break;
                    case "status_tick":
                        if (_slots.TryGetValue((e.TargetSide, e.TargetIndex), out var t4)) Float(t4, "-" + e.Amount, UiKit.Hex("#A15CE0"), 40);
                        break;
                    case "defeated":
                        lines.Add($"  ✖ {Who(e.TargetSide, e.TargetIndex)}");
                        break;
                }
            }
            _b = next;
            _log.text = string.Join("\n", lines.TakeLast(Wide ? 22 : 5));
            yield return new WaitForSeconds(0.4f);
            UiKit.Clear(Root);
            Build();
            _log.text = string.Join("\n", lines.TakeLast(Wide ? 22 : 5));
            _busy = false;
        }

        private string Who(int side, int idx)
        {
            var u = _b.Units.FirstOrDefault(x => x.Side == side && x.Index == idx);
            return (side == 0 ? "" : L.T("duel.enemy") + " ") + (u != null ? GameDataClient.PetName(u.FormId) : "?");
        }

        private static IEnumerator Lunge(RectTransform rt, int side)
        {
            var start = rt.anchoredPosition; float t = 0;
            var dir = side == 0 ? new Vector2(0, 40) : new Vector2(0, -40);
            while (t < 1) { t += Time.deltaTime * 6f; rt.anchoredPosition = start + dir * Mathf.Sin(t * Mathf.PI); yield return null; }
            rt.anchoredPosition = start;
        }

        private static IEnumerator Shake(RectTransform rt)
        {
            var start = rt.anchoredPosition; float t = 0;
            while (t < 0.3f) { t += Time.deltaTime; rt.anchoredPosition = start + new Vector2(Mathf.Sin(t * 90) * 10 * (1 - t / 0.3f), 0); yield return null; }
            rt.anchoredPosition = start;
        }

        private void Float(RectTransform at, string text, Color color, float size)
        {
            var t = UiKit.Text(Root, text, size, color, UiKit.Weight.ExtraBold);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = at.anchorMin;
            t.rectTransform.sizeDelta = new Vector2(300, 90);
            t.rectTransform.anchoredPosition = at.anchoredPosition + new Vector2(0, 60);
            t.outlineWidth = 0.2f; t.outlineColor = Color.white;
            t.gameObject.AddComponent<FloatUp>();
        }

        private async void Surrender()
        {
            if (_live)
            {
                if (await Call(() => Api.Post<JObject>($"/api/pvp/battles/{_b.Id}/surrender", null)) != null) await FetchLive();
                return;
            }
            var r = await Call(() => Api.Post<BattleDto>($"/api/battles/{_b.Id}/surrender", null));
            if (r != null) { _b = r; UiKit.Clear(Root); Build(); }
        }

        private void ShowEnd()
        {
            bool win = _b.WinnerSide == 0;
            Audio.Play(win ? Audio.Sfx.Victory : Audio.Sfx.Defeat);
            var panel = Nav.Popup(new Vector2(900, 900), new Vector2(900, 760), out var close);
            UiKit.Text(panel, L.T(win ? "duel.victory" : _b.WinnerSide == -1 ? "duel.draw" : "duel.defeat"), 80, win ? UiKit.GoldDeep : UiKit.InkSoft, UiKit.Weight.ExtraBold)
                .rectTransform.Place(new Vector2(0.5f, 1), new Vector2(800, 140), new Vector2(0, -50));
            var r = _b.Rewards;
            var lines = new List<string>();
            if (r != null)
            {
                foreach (var kv in r.Currencies) lines.Add($"+{L.Number(kv.Value)} {L.T("currency." + kv.Key.ToLowerInvariant())}");
                foreach (var kv in r.Items) lines.Add($"+{kv.Value} {L.T("item." + kv.Key)}");
                foreach (var kv in r.Packs) lines.Add($"+{kv.Value} {L.T("pack." + kv.Key.Replace("_pack", "") + ".name")}");
                if (r.CardExp > 0) lines.Add(L.T("duel.card_exp", r.CardExp));
                if (r.FirstClear) lines.Add(L.T("duel.first_clear"));
                if (r.DailyCapReached) lines.Add(L.T("duel.daily_cap"));
            }
            UiKit.Text(panel, string.Join("\n", lines), 36, UiKit.Ink, UiKit.Weight.Bold).rectTransform.Stretch(60, 220, 60, 200);
            var b = UiKit.Button(panel, L.T("common.continue"), () => { close(); Nav.Back(); }, UiKit.ButtonStyle.Gold, 40);
            ((RectTransform)b.transform).Place(new Vector2(0.5f, 0), new Vector2(500, 120), new Vector2(0, 50));
            _ = RefreshPlayer();
        }

        private async System.Threading.Tasks.Task RefreshPlayer()
        {
            try { Session.SetPlayer(await Api.Get<PlayerDto>("/api/player")); } catch (ApiError) { }
        }

        private static async void LoadBg(Image img, string address)
        {
            var s = await GameDataClient.LoadSprite(address);
            if (img == null) return;
            if (s != null) { img.sprite = s; img.color = Color.white; } else img.color = UiKit.Sky;
        }

        private sealed class FxRunner : MonoBehaviour { }
    }

    public sealed class FloatUp : MonoBehaviour
    {
        private float _t;
        private void Update()
        {
            _t += Time.deltaTime;
            ((RectTransform)transform).anchoredPosition += new Vector2(0, 120 * Time.deltaTime);
            var t = GetComponent<TextMeshProUGUI>(); t.alpha = Mathf.Clamp01(1.4f - _t);
            if (_t > 1.4f) Destroy(gameObject);
        }
    }
}

namespace PetStar.Client.Screens
{
    /// <summary>The duel screen draws "me" as side 0. Live PvP players may be side 1, so their view is mirrored:
    /// unit/event sides and the winner are swapped. Plans only carry my unit indices and enemy target indices, so
    /// nothing is changed on the way back to the server.</summary>
    public static class LiveBattle
    {
        public static BattleDto Normalize(BattleDto b)
        {
            if (b.MySide == 0) return b;
            int Flip(int s) => s < 0 ? s : 1 - s;
            foreach (var u in b.Units) u.Side = Flip(u.Side);
            foreach (var e in b.LastEvents) { e.ActorSide = Flip(e.ActorSide); e.TargetSide = Flip(e.TargetSide); }
            if (b.WinnerSide is int w) b.WinnerSide = Flip(w);
            b.MySide = 0;
            return b;
        }
    }
}
