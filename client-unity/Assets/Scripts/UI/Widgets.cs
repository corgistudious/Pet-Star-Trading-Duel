using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PetStar.Client.Core;
using PetStar.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.UI
{
    /// <summary>Small reusable building blocks for the community / market screens.</summary>
    public static class W
    {
        private static readonly JsonSerializer Ser = JsonSerializer.Create(ApiClient.Json);

        public static CardDto Card(JToken t) => t.ToObject<CardDto>(Ser)!;
        public static T To<T>(JToken t) => t.ToObject<T>(Ser)!;

        /// <summary>Standard scrolling content column under the title/currency chrome.</summary>
        public static RectTransform Column(ScreenBase s, RectTransform root, float spacing = 20, bool hubNav = false)
        {
            bool wide = Navigator.I.Mode != LayoutMode.Portrait;
            float top = wide ? 140 : 320;
            float bottom = hubNav && !wide ? 190 : 30;
            float left = hubNav && wide ? 210 : 30;
            var area = UiKit.Rect(root, "Column").Stretch(left, top, 30, bottom);
            var content = UiKit.Scroll(area);
            UiKit.VStack(content, spacing, new RectOffset(10, 10, 10, 60));
            return content;
        }

        public static TextMeshProUGUI Header(Transform parent, string text, float size = 40) =>
            (TextMeshProUGUI)UiKit.Text(parent, text, size, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, size * 1.8f).GetComponent<TextMeshProUGUI>();

        public static TextMeshProUGUI Line(Transform parent, string text, float size = 32, Color? color = null, float h = -1) =>
            UiKit.Text(parent, text, size, color ?? UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, h < 0 ? size * 1.7f : h).GetComponent<TextMeshProUGUI>();

        public static RectTransform Row(Transform parent, float height, float spacing = 16)
        {
            var r = UiKit.Rect(parent, "Row");
            r.Size(-1, height);
            var h = UiKit.HStack(r, spacing);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            return r;
        }

        public static Button Btn(Transform parent, string label, Action onClick, UiKit.ButtonStyle style = UiKit.ButtonStyle.Secondary, float size = 30)
        {
            var b = UiKit.Button(parent, label, onClick, style, size, 32);
            b.Size(-1, -1, 1, 1);
            return b;
        }

        public static Image Card(Transform parent, Color? color = null, float height = 200)
        {
            var p = UiKit.Panel(parent, color ?? UiKit.White, 36, "Card", shadow: false);
            p.Size(-1, height);
            return p;
        }

        /// <summary>Price pill with the currency icon (Rainbow Star shimmer / Mini Green Bean reflection).</summary>
        public static RectTransform Price(Transform parent, string currency, long amount, float size = 34)
        {
            var row = UiKit.Rect(parent, "Price");
            var h = UiKit.HStack(row, 8, null, TextAnchor.MiddleLeft);
            h.childForceExpandWidth = false;
            var icon = UiKit.Icon(row, null, new Vector2(size * 1.5f, size * 1.5f));
            icon.Size(size * 1.5f, size * 1.5f);
            LoadIcon(icon, GameDataClient.CurrencyIcon(currency));
            UiKit.Text(row, L.Number(amount), size, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(260, size * 1.5f);
            return row;
        }

        public static async void LoadIcon(Image img, string address)
        {
            var s = await GameDataClient.LoadSprite(address);
            if (img != null && s != null) { img.sprite = s; img.color = Color.white; }
        }

        public static TMP_InputField Input(Transform parent, string placeholder, bool numeric = false)
        {
            var i = UiKit.Input(parent, placeholder);
            i.Size(-1, 110);
            if (numeric) i.contentType = TMP_InputField.ContentType.IntegerNumber;
            return i;
        }

        public static void Tabs(Transform parent, IList<(string id, string label)> tabs, string active, Action<string> select)
        {
            var row = Row(parent, 96, 10);
            foreach (var (id, label) in tabs)
            {
                var tid = id;
                Btn(row, label, () => select(tid), id == active ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 26);
            }
        }

        public static string Ago(DateTimeOffset t)
        {
            var d = DateTimeOffset.UtcNow - t;
            if (d.TotalMinutes < 1) return L.T("time.now");
            if (d.TotalHours < 1) return L.T("time.minutes", (int)d.TotalMinutes);
            if (d.TotalDays < 1) return L.T("time.hours", (int)d.TotalHours);
            return L.T("time.days", (int)d.TotalDays);
        }

        public static string Remaining(TimeSpan d) =>
            d.TotalSeconds <= 0 ? L.T("time.ended") : d.TotalHours >= 1 ? $"{(int)d.TotalHours}h {d.Minutes:00}m" : $"{d.Minutes:00}:{d.Seconds:00}";

        public static void Confirm(string title, string body, string confirmLabel, Action onConfirm)
        {
            var panel = Navigator.I.Popup(new Vector2(900, 760), new Vector2(900, 640), out var close);
            UiKit.Text(panel, title, 46, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(820, 110), new Vector2(0, -30));
            UiKit.Text(panel, body, 34, UiKit.InkSoft).rectTransform.Stretch(50, 150, 50, 190);
            var row = UiKit.Rect(panel, "Buttons"); row.Place(new Vector2(0.5f, 0), new Vector2(820, 120), new Vector2(0, 40));
            UiKit.HStack(row, 20).childForceExpandWidth = true;
            Btn(row, L.T("common.cancel"), close, UiKit.ButtonStyle.Secondary, 34);
            Btn(row, confirmLabel, () => { close(); onConfirm(); }, UiKit.ButtonStyle.Gold, 34);
        }

        /// <summary>Lets the player pick up to `max` of their own cards (used by Sell, Create Post, Albums, Showcase).</summary>
        public static async void PickCards(int max, Action<List<CardDto>> done)
        {
            CardPageDto page;
            try { page = await App.Api.Get<CardPageDto>("/api/cards?sort=rarity&pageSize=200"); }
            catch (ApiError e) { Navigator.I.Toast(L.Error(e.Code), true); return; }
            var panel = Navigator.I.Popup(new Vector2(1000, 1700), new Vector2(1700, 980), out var close);
            var picked = new List<CardDto>();
            var title = UiKit.Text(panel, L.T("picker.title", 0, max), 42, UiKit.Ink, UiKit.Weight.ExtraBold);
            title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(900, 90), new Vector2(0, -20));
            var grid = UiKit.Scroll(UiKit.Rect(panel, "Grid").Stretch(30, 120, 30, 160));
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(200, 356); g.spacing = new Vector2(14, 14); g.childAlignment = TextAnchor.UpperCenter;
            foreach (var c in page.Items)
            {
                var cv = CardView.Create(grid, c, 200, interactive: false);
                var card = c;
                var outline = cv.gameObject.AddComponent<Outline>(); outline.effectDistance = new Vector2(6, -6); outline.effectColor = new Color(0, 0, 0, 0);
                cv.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    if (c.IsBusy) { Navigator.I.Toast(L.T("picker.busy"), true); return; }
                    if (picked.Contains(card)) { picked.Remove(card); outline.effectColor = new Color(0, 0, 0, 0); }
                    else if (picked.Count < max) { picked.Add(card); outline.effectColor = UiKit.Gold; }
                    title.text = L.T("picker.title", picked.Count, max);
                });
            }
            var ok = UiKit.Button(panel, L.T("common.confirm"), () => { close(); done(picked); }, UiKit.ButtonStyle.Gold, 38);
            ((RectTransform)ok.transform).Place(new Vector2(0.5f, 0), new Vector2(500, 120), new Vector2(0, 25));
        }
    }
}
