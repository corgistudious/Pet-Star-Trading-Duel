using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    public sealed class ShopScreen : ScreenBase
    {
        public enum Tab { Packs, RainbowStar }
        public override string TitleKey => "shop.title";
        private Tab _tab;
        private List<PackShopItemDto>? _packs;
        private List<JObject>? _products;

        public ShopScreen(Tab tab) { _tab = tab; }

        public override void Build()
        {
            float top = Wide ? 140 : 320;
            var tabs = UiKit.Rect(Root, "Tabs");
            tabs.Place(new Vector2(0.5f, 1), new Vector2(Wide ? 900 : 1000, 110), new Vector2(0, -top), new Vector2(0.5f, 1));
            UiKit.HStack(tabs, 20).childForceExpandWidth = true;
            UiKit.Button(tabs, L.T("shop.tab_packs"), () => Switch(Tab.Packs), _tab == Tab.Packs ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 34).Size(-1, 100, 1);
            UiKit.Button(tabs, L.T("shop.tab_rainbow_star"), () => Switch(Tab.RainbowStar), _tab == Tab.RainbowStar ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 34).Size(-1, 100, 1);
            var area = UiKit.Rect(Root, "Area").Stretch(30, top + 140, 30, 30);
            var content = UiKit.Scroll(area);
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = Wide ? new Vector2(520, 800) : new Vector2(470, 790);
            grid.spacing = new Vector2(30, 30); grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = Wide ? 3 : 2;
            if (_tab == Tab.Packs && _packs != null) foreach (var p in _packs) PackCard(content, p);
            if (_tab == Tab.RainbowStar && _products != null) foreach (var p in _products) ProductCard(content, p);
        }

        private void Switch(Tab t) { _tab = t; UiKit.Clear(Root); Build(); OnShow(); }

        public override async void OnShow()
        {
            if (_tab == Tab.Packs && _packs == null)
            {
                _packs = await Call(() => Api.Get<List<PackShopItemDto>>("/api/packs"));
                if (_packs != null) { UiKit.Clear(Root); Build(); }
            }
            if (_tab == Tab.RainbowStar && _products == null)
            {
                _products = await Call(() => Api.Get<List<JObject>>("/api/iap/products"));
                if (_products != null) { UiKit.Clear(Root); Build(); }
            }
        }

        private void PackCard(RectTransform parent, PackShopItemDto p)
        {
            var card = UiKit.Panel(parent, UiKit.White, 44, "Pack:" + p.Id, shadow: true);
            var pv = PackVisual.Create(card.transform, p.Id, 260);
            ((RectTransform)pv.transform).Place(new Vector2(0.5f, 1), ((RectTransform)pv.transform).sizeDelta, new Vector2(0, -30));
            string name = p.Names != null && p.Names.TryGetValue(L.Language, out var n) ? n : L.T(p.NameKey);
            UiKit.Text(card.transform, name, 40, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Place(new Vector2(0.5f, 0), new Vector2(440, 60), new Vector2(0, 300));
            UiKit.Text(card.transform, L.T("shop.owned", p.Owned), 30, UiKit.InkSoft, UiKit.Weight.Bold).rectTransform.Place(new Vector2(0.5f, 0), new Vector2(440, 50), new Vector2(0, 255));
            // purchase conditions (evaluated by the server; the client only displays them)
            var req = new List<string>();
            if (p.MinLevel > 0) req.Add(L.T("pack.req_level", p.MinLevel));
            if (p.MinVip > 0) req.Add(L.T("pack.req_vip", p.MinVip));
            if (p.RequiresEvent) req.Add(p.Event != null ? L.T("pack.event_left", p.Event.Sold, p.Event.TotalLimit?.ToString() ?? "∞") : L.T("pack.req_event"));
            if (p.RequiresInvitation) req.Add(p.InvitationPacksLeft is int left ? L.T("pack.invite_left", left) : L.T("pack.req_invite"));
            if (req.Count > 0 || p.BlockedReason != null)
            {
                string line = string.Join(" · ", req);
                if (p.BlockedReason != null && p.BlockedReason != "pack_not_available") line += (line.Length > 0 ? "\n" : "") + "<color=#E5484D>" + L.Error(p.BlockedReason) + "</color>";
                UiKit.Text(card.transform, line, 24, UiKit.InkSoft, UiKit.Weight.Bold).rectTransform.Place(new Vector2(0.5f, 0), new Vector2(450, 70), new Vector2(0, 195));
            }
            var price = UiKit.Rect(card.transform, "Price"); price.Place(new Vector2(0.5f, 0), new Vector2(420, 70), new Vector2(0, 125));
            UiKit.HStack(price, 10);
            var icon = UiKit.Icon(price, null, new Vector2(64, 64)); icon.Size(64, 64);
            LoadIcon(icon, GameDataClient.CurrencyIcon(p.PriceCurrency));
            UiKit.Text(price, L.Number(p.PriceAmount), 40, UiKit.Ink, UiKit.Weight.ExtraBold).Size(200, 64);
            var id = p.Id;
            var row = UiKit.Rect(card.transform, "Buttons"); row.Place(new Vector2(0.5f, 0), new Vector2(440, 100), new Vector2(0, 20));
            UiKit.HStack(row, 14).childForceExpandWidth = true;
            UiKit.Button(row, L.T("shop.buy"), () => Buy(id), UiKit.ButtonStyle.Gold, 34).Size(-1, 96, 1).GetComponent<Button>().interactable = p.PriceAmount > 0 && p.CanBuy;
            UiKit.Button(row, L.T("shop.open"), () => Nav.Push(new PackOpeningScreen(id)), UiKit.ButtonStyle.Primary, 34).Size(-1, 96, 1).GetComponent<Button>().interactable = p.Owned > 0;
            var odds = UiKit.Button(card.transform, L.T("shop.odds"), () => ShowOdds(id), UiKit.ButtonStyle.Ghost, 26);
            ((RectTransform)odds.transform).Place(new Vector2(1, 1), new Vector2(150, 60), new Vector2(-10, -10), new Vector2(1, 1));
        }

        private async void Buy(string id)
        {
            var r = await Call(() => Api.Post<PurchasePackResponse>("/api/packs/purchase", new PurchasePackRequest { PackTypeId = id, Quantity = 1 }, ApiClient.NewIdempotencyKey()));
            if (r == null) return;
            Session.UpdateCurrencies(r.Currencies);
            if (Session.Player != null) Session.Player.Packs[id] = r.Owned;
            _packs = null; UiKit.Clear(Root); Build(); OnShow();
        }

        private async void ShowOdds(string id)
        {
            var o = await Call(() => Api.Get<PackOddsDto>($"/api/packs/{id}/odds"));
            if (o == null) return;
            var panel = Nav.Popup(new Vector2(960, 1500), new Vector2(1300, 900), out var close);
            var content = UiKit.Scroll(UiKit.Rect(panel, "Odds").Stretch(40, 40, 40, 40));
            UiKit.VStack(content, 10, null, TextAnchor.UpperLeft);
            UiKit.Text(content, L.T("shop.odds_title"), 44, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft).Size(-1, 80);
            void Table(string title, Dictionary<string, double> t, IEnumerable<string> order)
            {
                UiKit.Text(content, title, 34, UiKit.Sapphire, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
                foreach (var k in order) if (t.TryGetValue(k, out var v)) UiKit.Text(content, $"{k}: {v:0.####}%", 30, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 44);
            }
            UiKit.Text(content, L.T("pack.odds_version", o.Version) + "  ·  " + (o.SameTableAllSlots ? L.T("pack.odds_same_table") : ""), 26, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 70);
            // per card and per whole pack ("at least one in 5 cards"), computed by the server from the live table
            UiKit.Text(content, $"<b>{L.T("rarity.title")}</b>   {L.T("pack.odds_per_slot")}   ·   {L.T("pack.odds_per_pack")}", 28, UiKit.Sapphire, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 54);
            foreach (var r in o.Pack)
                UiKit.Text(content, $"{(r.Rarity == "DV" ? L.T("rarity.DV") : r.Rarity)}:   {r.PerSlot:0.####}%   ·   {r.AtLeastOneInPack:0.####}%", 30, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 44);
            if (o.DivinePets.Count > 0)
            {
                UiKit.Text(content, L.T("pack.odds_divine", o.DivineGroupPerSlot.ToString("0.###"), o.DivinePerPetPerSlot.ToString("0.###")), 30, UiKit.GoldDeep, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 60);
                foreach (var f in o.DivinePets) UiKit.Text(content, "★ " + GameDataClient.PetName(f), 28, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).Size(-1, 42);
            }
            Table(L.T("shop.odds_quality"), o.Quality, new[] { "D", "C", "B", "A", "S", "SS", "SSS" });
            Table(L.T("shop.odds_finish"), o.Finish, new[] { "Normal", "Foil", "Holographic", "Shiny", "Prismatic" });
            UiKit.Button(content, L.T("common.close"), close, UiKit.ButtonStyle.Secondary).Size(-1, 100);
        }

        private void ProductCard(RectTransform parent, JObject p)
        {
            var card = UiKit.Panel(parent, UiKit.White, 44, "Product", shadow: true);
            var icon = UiKit.Icon(card.transform, null, new Vector2(300, 300));
            ((RectTransform)icon.transform).Place(new Vector2(0.5f, 1), new Vector2(300, 300), new Vector2(0, -40));
            LoadIcon(icon, "ui/" + (string)p["assetId"]!);
            long amount = (long)p["amount"]!, bonus = (long)p["bonus"]!;
            UiKit.Text(card.transform, L.Number(amount) + (bonus > 0 ? $" <size=70%><color=#C79A2E>+{L.Number(bonus)}</color></size>" : ""), 50, UiKit.Ink, UiKit.Weight.ExtraBold)
                .rectTransform.Place(new Vector2(0.5f, 0), new Vector2(440, 80), new Vector2(0, 240));
            UiKit.Text(card.transform, L.T("currency.rainbow_star"), 30, UiKit.InkSoft, UiKit.Weight.Bold).rectTransform.Place(new Vector2(0.5f, 0), new Vector2(440, 50), new Vector2(0, 190));
            // Store purchase: the client obtains a purchase token from Google Play Billing and the backend verifies it
            // (/api/iap/verify) before granting. Requires Play Console products + Unity IAP configuration.
            var b = UiKit.Button(card.transform, L.T("shop.iap_unavailable"), () => Nav.Toast(L.T("shop.iap_unavailable_detail")), UiKit.ButtonStyle.Secondary, 30);
            ((RectTransform)b.transform).Place(new Vector2(0.5f, 0), new Vector2(420, 100), new Vector2(0, 40));
        }

        private static async void LoadIcon(Image img, string address)
        {
            var s = await GameDataClient.LoadSprite(address);
            if (img != null && s != null) { img.sprite = s; img.color = Color.white; }
        }
    }
}
