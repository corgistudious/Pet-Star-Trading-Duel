using System;
using System.Collections.Generic;
using System.Linq;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    public sealed class PetHomeScreen : ScreenBase
    {
        public override string TitleKey => "home.title";
        private CardPageDto? _page;
        private string? _rarity, _element;
        private int? _stage;
        private string _sort = "newest";
        private RectTransform _grid = null!;
        private TextMeshProUGUI _count = null!;

        public override void Build()
        {
            float top = Wide ? 140 : 320;
            // filters
            var filters = UiKit.Rect(Root, "Filters");
            filters.Place(new Vector2(0.5f, 1), new Vector2(Wide ? 1840 : 1020, 110), new Vector2(0, -top), new Vector2(0.5f, 1));
            UiKit.HStack(filters, 14).childForceExpandWidth = true;
            Chip(filters, L.T("filter.rarity") + (_rarity != null ? ": " + _rarity : ""), () => Pick(L.T("filter.rarity"), GameDataClient.RarityOrder.ToList(), v => { _rarity = v; Reload(); }));
            Chip(filters, L.T("filter.element") + (_element != null ? ": " + L.T("element." + _element) : ""), () => Pick(L.T("filter.element"), GameDataClient.ElementColors.Keys.ToList(), v => { _element = v; Reload(); }, k => L.T("element." + k)));
            Chip(filters, L.T("filter.stage") + (_stage != null ? ": " + _stage : ""), () => Pick(L.T("filter.stage"), new List<string> { "1", "2", "3" }, v => { _stage = v == null ? null : int.Parse(v); Reload(); }));
            Chip(filters, L.T("sort." + _sort), () => Pick(L.T("filter.sort"), new List<string> { "newest", "power", "rarity", "level", "name" }, v => { _sort = v ?? "newest"; Reload(); }, k => L.T("sort." + k), allowAll: false));

            _count = UiKit.Text(Root, "", 32, UiKit.InkSoft, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft);
            _count.rectTransform.Place(new Vector2(0, 1), new Vector2(600, 60), new Vector2(Wide ? 40 : 40, -(top + 120)));

            var area = UiKit.Rect(Root, "GridArea").Stretch(20, top + 190, 20, 20);
            _grid = UiKit.Scroll(area);
            var g = _grid.gameObject.AddComponent<GridLayoutGroup>();
            float cw = Wide ? 250 : 240;
            g.cellSize = new Vector2(cw, cw / CardView.Aspect); g.spacing = new Vector2(20, 24);
            g.padding = new RectOffset(10, 10, 10, 40);
            g.childAlignment = TextAnchor.UpperCenter;
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g.constraintCount = Wide ? 7 : 4;
            if (_page != null) Fill();
        }

        public override void OnShow() { if (_page == null) Reload(); }

        private async void Reload()
        {
            var q = $"/api/cards?sort={_sort}&pageSize=200" + (_rarity != null ? "&rarity=" + _rarity : "") + (_element != null ? "&element=" + _element : "") + (_stage != null ? "&stage=" + _stage : "");
            _page = await Call(() => Api.Get<CardPageDto>(q));
            if (_page != null && Root != null) { UiKit.Clear(Root); Build(); }
        }

        private void Fill()
        {
            UiKit.Clear(_grid);
            _count.text = L.T("home.count", _page!.Total);
            if (_page.Items.Count == 0)
            {
                var empty = UiKit.Text(_grid, L.T("home.empty"), 40, UiKit.InkSoft);
                empty.Size(-1, 300);
                return;
            }
            float cw = Wide ? 250 : 240;
            foreach (var c in _page.Items)
            {
                var cv = CardView.Create(_grid, c, cw, interactive: false);
                var card = c;
                cv.gameObject.AddComponent<Button>().onClick.AddListener(() => CardDetails.Show(card, _ => Reload()));
                if (_page.CopiesByForm.TryGetValue(c.FormId, out var copies) && copies > 1)
                {
                    var badge = UiKit.Panel(cv.transform, UiKit.Ink, 20, "Copies");
                    ((RectTransform)badge.transform).Place(new Vector2(0, 1), new Vector2(70, 44), new Vector2(10, -10), new Vector2(0, 1));
                    UiKit.Text(badge.transform, "×" + copies, 26, UiKit.White, UiKit.Weight.Bold).rectTransform.Stretch();
                }
            }
        }

        private static void Chip(RectTransform parent, string label, Action onClick) =>
            UiKit.Button(parent, label, onClick, UiKit.ButtonStyle.Secondary, 30, 30).Size(-1, 100, 1);

        private void Pick(string title, List<string> options, Action<string?> onPick, Func<string, string>? label = null, bool allowAll = true)
        {
            var panel = Nav.Popup(new Vector2(900, 1200), new Vector2(900, 900), out var close);
            UiKit.Text(panel, title, 48, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(800, 100), new Vector2(0, -30));
            var list = UiKit.Scroll(UiKit.Rect(panel, "List").Stretch(40, 150, 40, 40));
            UiKit.VStack(list, 16);
            if (allowAll) UiKit.Button(list, L.T("filter.all"), () => { close(); onPick(null); }, UiKit.ButtonStyle.Secondary).Size(-1, 100);
            foreach (var o in options)
            {
                var v = o;
                UiKit.Button(list, label?.Invoke(o) ?? o, () => { close(); onPick(v); }, UiKit.ButtonStyle.Secondary).Size(-1, 100);
            }
        }
    }

    /// <summary>Card Details popup (zoomed card, lore, skills, favorite/lock, evolution).</summary>
    public static class CardDetails
    {
        public static void Show(CardDto card, Action<CardDto> onChanged)
        {
            var nav = Navigator.I;
            var panel = nav.Popup(new Vector2(1000, 1820), new Vector2(1760, 1000), out var close);
            bool wide = nav.Mode != LayoutMode.Portrait;
            float cw = wide ? 470 : 520;
            var cv = CardView.Create(panel, card, cw);
            ((RectTransform)cv.transform).Place(wide ? new Vector2(0, 0.5f) : new Vector2(0.5f, 1), ((RectTransform)cv.transform).sizeDelta,
                wide ? new Vector2(50, 0) : new Vector2(0, -40), wide ? new Vector2(0, 0.5f) : new Vector2(0.5f, 1));
            var info = UiKit.Rect(panel, "Info");
            if (wide) info.Stretch(cw + 100, 50, 50, 50); else info.Stretch(50, cw / CardView.Aspect + 70, 50, 40);
            UiKit.VStack(info, 18, null, TextAnchor.UpperLeft);
            UiKit.Text(info, $"{card.Name}  <size=60%><color=#5B6B8F>{card.Serial}</color></size>", 56, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 80);
            UiKit.Text(info, $"{L.T("element." + card.Element)} · {L.T("role." + card.Role)} · SPD {card.Spd} · CRIT {Mathf.RoundToInt((float)card.Crit * 100)}% · {L.T("card.power")} {card.Power}",
                30, UiKit.InkSoft, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 50);
            UiKit.Text(info, GameDataClient.Lore(card.FormId), 32, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.TopLeft).Size(-1, 110);
            if (card.PassiveId != null)
                UiKit.Text(info, $"<b>{L.T("card.passive")}:</b> {L.T("passive." + card.PassiveId)}", 30, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.TopLeft).Size(-1, 80);
            UiKit.Text(info, L.T("card.exp", card.Exp, card.ExpToNext), 30, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 50);
            var row = UiKit.Rect(info, "Actions"); row.Size(-1, 110);
            UiKit.HStack(row, 16).childForceExpandWidth = true;
            UiKit.Button(row, card.IsFavorite ? L.T("card.unfavorite") : L.T("card.favorite"), async () =>
            {
                try { var r = await App.Api.Post<CardDto>($"/api/cards/{card.Id}/favorite", new FlagRequest { Value = !card.IsFavorite }); close(); onChanged(r); }
                catch (ApiError e) { nav.Toast(L.Error(e.Code), true); }
            }, UiKit.ButtonStyle.Secondary, 32).Size(-1, 100, 1);
            UiKit.Button(row, card.IsLocked ? L.T("card.unlock") : L.T("card.lock"), async () =>
            {
                try { var r = await App.Api.Post<CardDto>($"/api/cards/{card.Id}/lock", new FlagRequest { Value = !card.IsLocked }); close(); onChanged(r); }
                catch (ApiError e) { nav.Toast(L.Error(e.Code), true); }
            }, UiKit.ButtonStyle.Secondary, 32).Size(-1, 100, 1);
            var row2 = UiKit.Rect(info, "Actions2"); row2.Size(-1, 110);
            UiKit.HStack(row2, 16).childForceExpandWidth = true;
            UiKit.Button(row2, L.T("card.evolve"), () => { close(); nav.Push(new EvolutionScreen(card)); }, UiKit.ButtonStyle.Gold, 32).Size(-1, 100, 1);
            UiKit.Button(row2, L.T("card.train"), async () =>
            {
                try
                {
                    var t = await App.Api.Get<Newtonsoft.Json.Linq.JObject>($"/api/cards/{card.Id}/training");
                    var next = t["next"];
                    if (next == null || next.Type == Newtonsoft.Json.Linq.JTokenType.Null) { nav.Toast(L.T("training.max")); return; }
                    W.Confirm(L.T("training.title"), L.T("training.body", (int)t["skillLevel"]! + 1, (int)next["minCardLevel"]!, (int)next["scrolls"]!, (long)t["scrollsOwned"]!, L.Number((long)next["beans"]!)), L.T("card.train"), async () =>
                    {
                        try { var r = await App.Api.Post<Newtonsoft.Json.Linq.JObject>($"/api/cards/{card.Id}/train", null, ApiClient.NewIdempotencyKey()); App.Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>()); nav.Toast(L.T("training.done")); close(); }
                        catch (ApiError e) { nav.Toast(L.Error(e.Code), true); }
                    });
                }
                catch (ApiError e) { nav.Toast(L.Error(e.Code), true); }
            }, UiKit.ButtonStyle.Secondary, 32).Size(-1, 100, 1);
            var row3 = UiKit.Rect(info, "Actions3"); row3.Size(-1, 110);
            UiKit.HStack(row3, 16).childForceExpandWidth = true;
            UiKit.Button(row3, L.T("passport.open"), () => { close(); nav.Push(new PassportScreen(card.Id)); }, UiKit.ButtonStyle.Secondary, 30).Size(-1, 100, 1);
            UiKit.Button(row3, L.T("market.sell"), () => { close(); nav.Push(new SellCardScreen(card)); }, UiKit.ButtonStyle.Secondary, 30).Size(-1, 100, 1);
            UiKit.Button(row3, L.T("passport.share"), () => { close(); nav.Push(new CreatePostScreen(card)); }, UiKit.ButtonStyle.Secondary, 30).Size(-1, 100, 1);
            UiKit.Button(info, L.T("common.close"), close, UiKit.ButtonStyle.Ghost, 34).Size(-1, 90);
        }
    }

    public sealed class EvolutionScreen : ScreenBase
    {
        public override string TitleKey => "evolution.title";
        private readonly CardDto _card;
        private EvolutionPreviewDto? _preview;
        public EvolutionScreen(CardDto card) { _card = card; }

        public override void Build()
        {
            if (_preview == null) return;
            var p = _preview;
            float cw = Wide ? 420 : 400;
            var a = CardView.Create(Root, p.Current, cw);
            ((RectTransform)a.transform).Place(new Vector2(Wide ? 0.25f : 0.27f, Wide ? 0.55f : 0.62f), ((RectTransform)a.transform).sizeDelta, Vector2.zero);
            var arrow = UiKit.Text(Root, "➜", 120, UiKit.Gold, UiKit.Weight.ExtraBold);
            arrow.rectTransform.Place(new Vector2(0.5f, Wide ? 0.55f : 0.62f), new Vector2(200, 200), Vector2.zero);
            if (p.Next != null)
            {
                var b = CardView.Create(Root, p.Next, cw);
                ((RectTransform)b.transform).Place(new Vector2(Wide ? 0.75f : 0.73f, Wide ? 0.55f : 0.62f), ((RectTransform)b.transform).sizeDelta, Vector2.zero);
            }
            var req = UiKit.Panel(Root, UiKit.White, 40, "Requirements", shadow: true);
            ((RectTransform)req.transform).Place(new Vector2(0.5f, 0), Wide ? new Vector2(1200, 220) : new Vector2(1000, 420), new Vector2(0, Wide ? 170 : 220), new Vector2(0.5f, 0));
            var col = UiKit.Rect(req.transform, "Col").Stretch(40, 30, 40, 30);
            UiKit.VStack(col, 10, null, TextAnchor.UpperLeft);
            if (p.Next == null) { UiKit.Text(col, L.T("evolution.final_form"), 40, UiKit.InkSoft).Size(-1, 100); return; }
            Line(col, L.T("evolution.req_level", p.RequiredLevel), p.Current.Level >= p.RequiredLevel);
            Line(col, L.T("evolution.req_essence", L.T("item." + p.EssenceItemId), p.EssenceOwned, p.EssenceRequired), p.EssenceOwned >= p.EssenceRequired);
            Line(col, L.T("evolution.req_beans", L.Number(p.BeansOwned), L.Number(p.BeansRequired)), p.BeansOwned >= p.BeansRequired);
            var btn = UiKit.Button(Root, L.T("evolution.confirm"), Confirm, UiKit.ButtonStyle.Gold, 44);
            ((RectTransform)btn.transform).Place(new Vector2(0.5f, 0), new Vector2(600, 130), new Vector2(0, 30), new Vector2(0.5f, 0));
            btn.interactable = p.Check == "Ok";
        }

        private static void Line(RectTransform col, string text, bool ok) =>
            UiKit.Text(col, (ok ? "<color=#2E9E62>✔</color> " : "<color=#E5484D>✖</color> ") + text, 36, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 60);

        public override async void OnShow()
        {
            if (_preview != null) return;
            _preview = await Call(() => Api.Get<EvolutionPreviewDto>($"/api/cards/{_card.Id}/evolution-preview"));
            if (_preview != null) { UiKit.Clear(Root); Build(); }
        }

        private async void Confirm()
        {
            var r = await Call(() => Api.Post<Newtonsoft.Json.Linq.JObject>($"/api/cards/{_card.Id}/evolve", null, ApiClient.NewIdempotencyKey()));
            if (r == null) return;
            var evolved = r["card"]!.ToObject<CardDto>(Newtonsoft.Json.JsonSerializer.Create(ApiClient.Json))!;
            Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>());
            Audio.Play(Audio.Sfx.Evolution); Audio.Haptic();
            // transformation: white flash, then the evolved card grows in (same card id)
            UiKit.Clear(Root);
            var flash = UiKit.Rect(Root, "Flash").Stretch();
            var fi = flash.gameObject.AddComponent<Image>(); fi.color = Color.white; fi.raycastTarget = false;
            flash.gameObject.AddComponent<FadeOut>();
            var cv = CardView.Create(Root, evolved, Wide ? 520 : 600);
            ((RectTransform)cv.transform).Place(new Vector2(0.5f, 0.55f), ((RectTransform)cv.transform).sizeDelta, Vector2.zero);
            cv.gameObject.AddComponent<GrowIn>();
            var t = UiKit.Text(Root, L.T("evolution.success", GameDataClient.PetName(evolved.FormId)), 50, UiKit.Sapphire, UiKit.Weight.ExtraBold);
            t.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(1000, 120), new Vector2(0, 80));
        }
    }

    public sealed class FadeOut : MonoBehaviour
    {
        private void Update() { var i = GetComponent<Image>(); var c = i.color; c.a -= Time.deltaTime * 1.2f; i.color = c; if (c.a <= 0) Destroy(gameObject); }
    }

    public sealed class GrowIn : MonoBehaviour
    {
        private float _t;
        private void Update() { _t += Time.deltaTime * 2f; float k = Mathf.Clamp01(_t); transform.localScale = Vector3.one * (k < 1 ? 0.3f + 0.8f * Mathf.Sin(k * Mathf.PI * 0.6f) : 1f); if (_t > 1.2f) { transform.localScale = Vector3.one; Destroy(this); } }
    }

    public sealed class CodexScreen : ScreenBase
    {
        public override string TitleKey => "codex.title";
        private CodexDto? _codex;

        public override void Build()
        {
            if (_codex == null) return;
            float top = Wide ? 140 : 320;
            var head = UiKit.Text(Root, L.T("codex.progress", _codex.Discovered, _codex.Total, _codex.Completion.ToString("0.#")), 40, UiKit.Ink, UiKit.Weight.Bold);
            head.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(1000, 80), new Vector2(0, -top));
            var list = UiKit.Scroll(UiKit.Rect(Root, "List").Stretch(30, top + 100, 30, 30));
            UiKit.VStack(list, 24, new RectOffset(10, 10, 10, 40));
            foreach (var fam in _codex.Entries.GroupBy(e => e.FamilyId))
            {
                var row = UiKit.Panel(list, UiKit.White, 36, "Family:" + fam.Key);
                row.Size(-1, 300);
                var h = UiKit.HStack((RectTransform)row.transform, 30, new RectOffset(30, 30, 20, 20), TextAnchor.MiddleLeft);
                foreach (var e in fam.OrderBy(x => x.Stage))
                {
                    var cell = UiKit.Rect(row.transform, e.FormId); cell.Size(220, 260);
                    var icon = UiKit.Icon(cell, null, new Vector2(200, 200));
                    ((RectTransform)icon.transform).Place(new Vector2(0.5f, 1), new Vector2(200, 200), Vector2.zero);
                    if (e.Discovered) LoadArt(icon, e.FormId);
                    else { icon.sprite = UiKit.Circle; icon.color = new Color(0.8f, 0.85f, 0.95f); }
                    var label = UiKit.Text(cell, e.Discovered ? GameDataClient.PetName(e.FormId) : "???", 30, UiKit.Ink, UiKit.Weight.Bold);
                    label.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(220, 50), Vector2.zero);
                    if (e.Stage < fam.Count()) { var arrow = UiKit.Text(row.transform, "›", 70, UiKit.Gold, UiKit.Weight.ExtraBold); arrow.Size(40, 200); }
                }
            }
        }

        private static async void LoadArt(Image img, string formId)
        {
            var s = await GameDataClient.LoadSprite("pet/" + formId);
            if (img != null && s != null) { img.sprite = s; img.color = Color.white; }
        }

        public override async void OnShow()
        {
            if (_codex != null) return;
            _codex = await Call(() => Api.Get<CodexDto>("/api/codex"));
            if (_codex != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class ProfileScreen : ScreenBase
    {
        public override string TitleKey => "profile.title";
        private ProfileDto? _me;

        public override void Build()
        {
            var p = Session.Player;
            var panel = UiKit.Panel(Root, UiKit.White, 48, "Profile", shadow: true);
            ((RectTransform)panel.transform).Place(new Vector2(0.5f, 0.45f), Wide ? new Vector2(1100, 700) : new Vector2(960, 1000), Vector2.zero);
            var col = UiKit.Rect(panel.transform, "Col").Stretch(60, 60, 60, 60);
            UiKit.VStack(col, 22, null, TextAnchor.UpperLeft);
            UiKit.Text(col, p?.Nickname ?? "", 64, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 100);
            UiKit.Text(col, $"UID {Session.Uid} · {p?.ServerCode}", 36, UiKit.InkSoft, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
            UiKit.Text(col, L.T("lobby.level", p?.Level ?? 1), 40, UiKit.Sapphire, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
            UiKit.Text(col, L.T("profile.cards", p?.CardCount ?? 0), 36, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
            UiKit.Text(col, L.T("profile.codex", p?.CodexDiscovered ?? 0, p?.CodexTotal ?? 0), 36, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
            if (_me != null)
                UiKit.Text(col, _me.EmailVerified ? L.T("profile.email_verified") : L.T("profile.email_unverified"), 32, _me.EmailVerified ? UiKit.Hex("#2E9E62") : UiKit.GoldDeep, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
            if (_me != null && !_me.EmailVerified)
                UiKit.Button(col, L.T("profile.resend"), async () => { await Call(() => Api.Post<Newtonsoft.Json.Linq.JObject>("/api/auth/resend-verification", null)); Nav.Toast(L.T("auth.check_email")); }, UiKit.ButtonStyle.Secondary, 32).Size(-1, 100);
        }

        public override async void OnShow()
        {
            if (_me != null) return;
            _me = await Call(() => Api.Get<ProfileDto>("/api/me"), overlay: false);
            if (_me != null) { UiKit.Clear(Root); Build(); }
        }
    }
}
