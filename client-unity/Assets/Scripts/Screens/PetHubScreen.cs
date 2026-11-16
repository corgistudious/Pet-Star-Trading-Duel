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
    /// <summary>
    /// Pet Hub: every card upgrade in one place — stars (1–10), enhancement (+0–+20), breakthrough, awakening, skill
    /// level (1–10), engravings and reset. Costs, gates and the "after" stats come from the server preview
    /// (/api/cards/{id}/upgrades); the client never computes or sends stats. Each confirm sends the value the player saw
    /// plus an idempotency key, so a double tap or a retry can never upgrade twice.
    /// </summary>
    public sealed class PetHubScreen : ScreenBase
    {
        private static readonly string[] Tabs = { "star", "enhance", "breakthrough", "awaken", "skill", "engrave", "reset" };
        public override string TitleKey => "upgrade.title";
        private readonly long _cardId;
        private string _tab = "star";
        private CardUpgradesDto? _data;

        public PetHubScreen(CardDto card) { _cardId = card.Id; }

        public override void Build()
        {
            float top = Wide ? 140 : 300;
            if (_data == null) return;
            var card = _data.Card;
            // card + summary
            float cw = Wide ? 400 : 330;
            var cv = CardView.Create(Root, card, cw);
            var crt = (RectTransform)cv.transform;
            if (Wide) crt.Place(new Vector2(0, 0.5f), crt.sizeDelta, new Vector2(60, -20), new Vector2(0, 0.5f));
            else crt.Place(new Vector2(0, 1), crt.sizeDelta, new Vector2(40, -top), new Vector2(0, 1));
            var sum = UiKit.Rect(Root, "Summary");
            if (Wide) sum.Place(new Vector2(0, 1), new Vector2(560, 300), new Vector2(cw + 100, -top), new Vector2(0, 1));
            else sum.Place(new Vector2(0, 1), new Vector2(620, cw / CardView.Aspect), new Vector2(cw + 70, -top), new Vector2(0, 1));
            UiKit.VStack(sum, 8, null, TextAnchor.UpperLeft);
            UiKit.Text(sum, GameDataClient.PetName(card.FormId), 46, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 64);
            if (card.DivineSet != null) UiKit.Text(sum, L.T("divine." + card.DivineSet), 28, UiKit.GoldDeep, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 40);
            UiKit.Text(sum, StarRow(card.Stars) + (card.EnhanceLevel > 0 ? $"  <color=#2457C5>+{card.EnhanceLevel}</color>" : ""), 36, UiKit.Gold, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 50);
            UiKit.Text(sum, $"Lv {card.Level}/{card.LevelCap} · {L.T("card.power")} {card.Power}" + (card.Awakened ? $" · <color=#AA70E8>{L.T("upgrade.awakened")}</color>" : ""), 28, UiKit.InkSoft, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 44);
            UiKit.Text(sum, $"HP {card.Hp} · ATK {card.Atk} · DEF {card.Def} · SPD {card.Spd} · CRIT {Mathf.RoundToInt((float)card.Crit * 100)}%", 26, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 44);

            // tabs (two rows on portrait so nothing is squeezed)
            float tabsTop = Wide ? top + 320 : top + cw / CardView.Aspect + 30;
            var tabs = UiKit.Rect(Root, "Tabs");
            if (Wide) tabs.Place(new Vector2(0, 1), new Vector2(1240, 96), new Vector2(cw + 100, -tabsTop), new Vector2(0, 1));
            else tabs.Place(new Vector2(0.5f, 1), new Vector2(1000, 200), new Vector2(0, -tabsTop), new Vector2(0.5f, 1));
            var grid = tabs.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = Wide ? new Vector2(170, 90) : new Vector2(240, 90); grid.spacing = new Vector2(10, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = Wide ? 7 : 4;
            foreach (var t in Tabs)
            {
                var id = t;
                UiKit.Button(tabs, L.T("upgrade." + t), () => { _tab = id; Rebuild(); }, _tab == t ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 26);
            }

            var area = UiKit.Rect(Root, "Panel");
            if (Wide) area.Stretch(cw + 100, tabsTop + 110, 50, 40); else area.Stretch(40, tabsTop + 220, 40, 40);
            var content = UiKit.Scroll(area);
            UiKit.VStack(content, 14, null, TextAnchor.UpperLeft);
            if (_tab == "engrave") Engravings(content);
            else if (_tab == "reset") Reset(content);
            else Option(content, _data.Options.First(o => o.Kind == _tab));
        }

        public override async void OnShow() => await Load();

        private async System.Threading.Tasks.Task Load()
        {
            var d = await Call(() => Api.Get<CardUpgradesDto>($"/api/cards/{_cardId}/upgrades"));
            if (d == null) return;
            _data = d; Rebuild();
        }

        private void Rebuild() { UiKit.Clear(Root); Build(); }

        private static string StarRow(int stars) => new string('★', Mathf.Clamp(stars, 0, 10)) + "<color=#D6DCE8>" + new string('★', Mathf.Clamp(10 - stars, 0, 10)) + "</color>";

        private void Option(RectTransform c, UpgradeOptionDto o)
        {
            string head = o.Kind switch
            {
                "star" => $"{L.T("upgrade.star")}: {o.Current} → {Mathf.Min(o.Current + 1, o.Max)} / {o.Max}",
                "enhance" => $"{L.T("upgrade.enhance")}: +{o.Current} → +{Mathf.Min(o.Current + 1, o.Max)}  ({L.T("upgrade.tier_" + Mathf.Clamp((Mathf.Min(o.Current + 1, 20) - 1) / 5 + 1, 1, 4))})",
                "breakthrough" => $"{L.T("upgrade.breakthrough")}: {o.Current} → {Mathf.Min(o.Current + 1, o.Max)} / {o.Max}  ·  {L.T("upgrade.level_cap", 50 + 5 * Mathf.Min(o.Current + 1, o.Max))}",
                "awaken" => o.Current == 1 ? L.T("upgrade.awakened") : L.T("upgrade.awaken"),
                _ => $"{L.T("upgrade.skill")}: {o.Current} → {Mathf.Min(o.Current + 1, o.Max)} / {o.Max}",
            };
            UiKit.Text(c, head, 38, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 64);
            if (o.Kind == "enhance") UiKit.Text(c, L.T("upgrade.always_succeeds"), 26, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 40);
            if (o.Cost == null) { UiKit.Text(c, L.T("upgrade.max"), 32, UiKit.GoldDeep, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 50); return; }

            UiKit.Text(c, L.T("upgrade.cost"), 30, UiKit.Sapphire, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 46);
            CostLine(c, "ui/ui_currency_mini_green_bean", L.T("currency.mini_green_bean"), o.Cost.Beans, _data!.Beans);
            foreach (var kv in o.Cost.Items) CostLine(c, "ui/ui_mat_" + kv.Key, L.T("item." + kv.Key), kv.Value, _data.Materials.TryGetValue(kv.Key, out var have) ? have : 0);
            var gates = new List<string>();
            if (o.Cost.MinLevel > 1) gates.Add(L.T("upgrade.needs_level", o.Cost.MinLevel));
            if (o.Cost.MinStars > 0) gates.Add(L.T("upgrade.needs_stars", o.Cost.MinStars));
            if (o.Cost.MinBreakthrough > 0) gates.Add(L.T("upgrade.needs_bt", o.Cost.MinBreakthrough));
            if (gates.Count > 0) UiKit.Text(c, string.Join(" · ", gates), 26, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 40);

            if (o.Preview != null)
            {
                var a = _data.Card; var b = o.Preview;
                UiKit.Text(c, L.T("upgrade.preview"), 30, UiKit.Sapphire, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 46);
                UiKit.Text(c, $"{L.T("card.power")} {a.Power} → <color=#2E9E5B>{b.Power}</color>   HP {a.Hp} → {b.Hp}   ATK {a.Atk} → {b.Atk}   DEF {a.Def} → {b.Def}", 28, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 80);
            }
            if (o.Blocked != null && o.Blocked != "max_reached")
                UiKit.Text(c, "<color=#E5484D>" + BlockedText(o.Blocked) + "</color>", 28, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 50);
            var btn = UiKit.Button(c, L.T("upgrade.confirm"), () => ConfirmUpgrade(o, head), UiKit.ButtonStyle.Gold, 38);
            btn.Size(-1, 120);
            btn.GetComponent<Button>().interactable = o.Blocked == null;
        }

        private static string BlockedText(string code) => code switch
        {
            "level_too_low" => L.Error("enhance_level_too_low"),
            "stars_too_low" => L.Error("breakthrough_stars_too_low"),
            "breakthrough_too_low" => L.Error("training_breakthrough_too_low"),
            "not_enough_beans" => L.Error("not_enough_beans"),
            _ when code.StartsWith("not_enough_") => L.Error("insufficient_items"),
            _ => L.Error(code),
        };

        private void CostLine(RectTransform c, string icon, string label, long need, long have)
        {
            var row = UiKit.Rect(c, "Cost"); row.Size(-1, 60);
            UiKit.HStack(row, 12);
            var img = UiKit.Icon(row, null, new Vector2(56, 56)); img.Size(56, 56);
            W.LoadIcon(img, icon);
            UiKit.Text(row, $"{label}: {L.Number(need)}  <color={(have >= need ? "#2E9E5B" : "#E5484D")}>({L.Number(have)})</color>", 28, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(820, 56);
        }

        private void ConfirmUpgrade(UpgradeOptionDto o, string head)
        {
            string costText = $"{L.Number(o.Cost!.Beans)} {L.T("currency.mini_green_bean")}" + string.Concat(o.Cost.Items.Select(kv => $"\n{kv.Value} × {L.T("item." + kv.Key)}"));
            W.Confirm(head, L.T("upgrade.cost") + ":\n" + costText, L.T("upgrade.confirm"), async () =>
            {
                string key = ApiClient.NewIdempotencyKey();
                var r = o.Kind == "skill"
                    ? await Call(() => Api.Post<JObject>($"/api/cards/{_cardId}/train", null, key))
                    : await Call(() => Api.Post<JObject>($"/api/cards/{_cardId}/{o.Kind}", new UpgradeRequest { From = o.Current }, key));
                if (r == null) { await Load(); return; }   // stale_state / not enough: show the fresh server state
                Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>());
                Nav.Toast(L.T("upgrade.done"));
                await Load();
                UpgradeBurst.Play(Root, o.Kind, o.Current + 1);
            });
        }

        private void Engravings(RectTransform c)
        {
            var card = _data!.Card;
            UiKit.Text(c, L.T("upgrade.engrave"), 38, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 64);
            for (int slot = 1; slot <= 3; slot++)
            {
                int s = slot;
                var row = UiKit.Rect(c, "Slot" + s); row.Size(-1, 100);
                UiKit.HStack(row, 12);
                var e = card.Engravings.FirstOrDefault(x => x.Slot == s);
                bool open = _data.UnlockedSlots.Contains(s);
                string label = !open ? L.T("upgrade.slot_locked") : e == null ? "—" : $"{L.T("engraving." + e.Type)} Lv{e.Level} (+{Pct(e)})";
                UiKit.Text(row, $"{L.T("upgrade.slot", s)}: {label}", 28, open ? UiKit.Ink : UiKit.InkSoft, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(560, 90);
                if (e != null)
                {
                    long eid = e.Id; int lvl = e.Level;
                    UiKit.Button(row, "Lv+", () => Do($"/api/engravings/{eid}/upgrade", new UpgradeRequest { From = lvl }), UiKit.ButtonStyle.Secondary, 26).Size(150, 86);
                    UiKit.Button(row, L.T("upgrade.unequip"), () => Do($"/api/cards/{_cardId}/engravings/unequip", new UnequipEngravingRequest { Slot = s }), UiKit.ButtonStyle.Ghost, 26).Size(170, 86);
                }
            }
            int? freeSlot = Enumerable.Range(1, 3).Where(s => _data.UnlockedSlots.Contains(s) && card.Engravings.All(x => x.Slot != s)).Cast<int?>().FirstOrDefault();
            foreach (var e in _data.FreeEngravings)
            {
                long eid = e.Id;
                var row = UiKit.Rect(c, "Free"); row.Size(-1, 90);
                UiKit.HStack(row, 12);
                UiKit.Text(row, $"{L.T("engraving." + e.Type)} Lv{e.Level} (+{Pct(e)})", 28, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(560, 84);
                var b = UiKit.Button(row, L.T("upgrade.equip"), () => Do($"/api/cards/{_cardId}/engravings/equip", new EquipEngravingRequest { EngravingId = eid, Slot = freeSlot ?? 1 }), UiKit.ButtonStyle.Primary, 26);
                b.Size(200, 80); b.GetComponent<Button>().interactable = freeSlot != null;
            }
            UiKit.Text(c, $"{L.T("upgrade.craft")}: 10 × {L.T("item.engraving_dust")} + 500 {L.T("currency.mini_green_bean")}  ({_data.Materials.GetValueOrDefault("engraving_dust")})", 26, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 50);
            var crafts = UiKit.Rect(c, "Craft"); crafts.Size(-1, 100);
            UiKit.HStack(crafts, 10).childForceExpandWidth = true;
            foreach (var t in new[] { "atk", "def", "hp", "spd", "crit" })
            {
                var type = t;
                UiKit.Button(crafts, t.ToUpperInvariant(), () => Do("/api/engravings/craft", new CraftEngravingRequest { Type = type }), UiKit.ButtonStyle.Secondary, 26).Size(-1, 90, 1);
            }
        }

        private static string Pct(EngravingDto e) => e.Type == "crit" ? $"{e.Value * 100:0.#}" : $"{e.Value * 100:0.#}%";

        private async void Do(string url, object body)
        {
            var r = await Call(() => Api.Post<JObject>(url, body, ApiClient.NewIdempotencyKey()));
            if (r != null && r["currencies"] != null) Session.UpdateCurrencies(r["currencies"]!.ToObject<Dictionary<string, long>>());
            await Load();
        }

        private void Reset(RectTransform c)
        {
            UiKit.Text(c, L.T("upgrade.reset"), 38, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 64);
            UiKit.Text(c, L.T("upgrade.reset_note"), 26, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 70);
            foreach (var kind in new[] { "enhance", "skill" })
            {
                var k = kind;
                UiKit.Button(c, L.T("upgrade.reset_" + k), async () =>
                {
                    var pv = await Call(() => Api.Get<ResetPreviewDto>($"/api/cards/{_cardId}/reset-preview?kind={k}"));
                    if (pv == null) return;
                    if (pv.Blocked != null) { Nav.Toast(L.Error(pv.Blocked), true); return; }
                    string refund = $"{L.Number(pv.RefundBeans)} {L.T("currency.mini_green_bean")}" + string.Concat(pv.RefundItems.Select(kv => $"\n{kv.Value} × {L.T("item." + kv.Key)}"));
                    W.Confirm(L.T("upgrade.reset_" + k), $"{pv.From} → {pv.To}\n{L.T("upgrade.reset_refund")}:\n{refund}", L.T("upgrade.confirm"), () =>
                        Do($"/api/cards/{_cardId}/reset", new ResetRequest { Kind = k, From = pv.From }));
                }, UiKit.ButtonStyle.Secondary, 32).Size(-1, 110);
            }
        }
    }

    /// <summary>Short, cheap upgrade effect (a ring of stars that flies out and fades); colour by upgrade tier.
    /// Plain UI text objects only, so it is light on WebGL and phones.</summary>
    public sealed class UpgradeBurst : MonoBehaviour
    {
        private float _t; private Vector2 _dir; private TextMeshProUGUI _txt = null!;

        public static void Play(RectTransform parent, string kind, int level)
        {
            Color col = kind switch
            {
                "star" => UiKit.Gold,
                "enhance" => level > 15 ? UiKit.Hex("#E6B95E") : level > 10 ? UiKit.Hex("#AA70E8") : level > 5 ? UiKit.Hex("#4099F4") : UiKit.Hex("#43BD78"),
                "breakthrough" => UiKit.Hex("#DE6DC9"),
                "awaken" => UiKit.Hex("#9673EC"),
                _ => UiKit.Sapphire,
            };
            int n = kind == "awaken" ? 18 : 12;
            for (int i = 0; i < n; i++)
            {
                var t = UiKit.Text(parent, "★", 70, col, UiKit.Weight.ExtraBold);
                t.raycastTarget = false;
                t.rectTransform.Place(new Vector2(0.5f, 0.55f), new Vector2(100, 100), Vector2.zero);
                var b = t.gameObject.AddComponent<UpgradeBurst>();
                float a = i * Mathf.PI * 2 / n;
                b._dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); b._txt = t;
            }
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime / 0.8f;
            var rt = (RectTransform)transform;
            rt.anchoredPosition = _dir * Mathf.SmoothStep(0, 360, _t);
            _txt.alpha = 1 - _t;
            rt.localScale = Vector3.one * (1 + 0.4f * Mathf.Sin(_t * Mathf.PI));
            if (_t >= 1) Destroy(gameObject);
        }
    }
}
