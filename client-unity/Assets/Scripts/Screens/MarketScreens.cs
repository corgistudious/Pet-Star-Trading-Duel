using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using PetStar.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    /// <summary>Player marketplace (same Global server). In-game currencies only: no real money, no withdrawal.</summary>
    public sealed class MarketHomeScreen : ScreenBase
    {
        public override string TitleKey => "market.title";
        public override bool ShowBackButton => false;
        private string _section = "new";
        private string? _currency, _rarity, _sort;
        private string _query = "";
        private JObject? _page;

        public override void Build()
        {
            var col = W.Column(this, Root, 18, hubNav: true);
            W.Tabs(col, new[] { ("new", L.T("market.new")), ("rare", L.T("market.rare")), ("recommended", L.T("market.recommended")), ("wishlist", L.T("market.wishlist_matches")), ("mine", L.T("market.mine")) },
                _section, s => { _section = s; Reload(); });
            var search = W.Row(col, 110);
            var q = UiKit.Input(search, L.T("market.search_hint")); q.text = _query; q.Size(-1, 110, 3);
            W.Btn(search, L.T("market.search"), () => { _query = q.text.Trim(); Reload(); }, UiKit.ButtonStyle.Primary);
            var filters = W.Row(col, 90, 10);
            W.Btn(filters, _currency == null ? L.T("market.any_currency") : L.T("currency." + _currency.ToLowerInvariant()), () =>
            { _currency = _currency == null ? Currency.MiniGreenBean : _currency == Currency.MiniGreenBean ? Currency.RainbowStar : null; Reload(); });
            W.Btn(filters, _rarity == null ? L.T("filter.rarity") : _rarity, () =>
            { var order = GameDataClient.RarityOrder.ToList(); _rarity = _rarity == null ? order[3] : order.IndexOf(_rarity) < order.Count - 1 ? order[order.IndexOf(_rarity) + 1] : null; Reload(); });
            W.Btn(filters, L.T("sort." + (_sort ?? "newest")), () =>
            { var s = new[] { "newest", "price_asc", "price_desc", "popular", "rarity" }; _sort = s[(Array.IndexOf(s, _sort ?? "newest") + 1) % s.Length]; Reload(); });
            var actions = W.Row(col, 100, 10);
            W.Btn(actions, L.T("market.auctions"), () => Nav.Push(new AuctionHouseScreen()), UiKit.ButtonStyle.Gold);
            W.Btn(actions, L.T("market.history"), () => Nav.Push(new MarketHistoryScreen()));
            W.Btn(actions, L.T("market.wishlist"), () => Nav.Push(new WishlistScreen()));
            W.Btn(actions, L.T("market.sell"), () => W.PickCards(1, cs => { if (cs.Count == 1) Nav.Push(new SellCardScreen(cs[0])); }));

            if (_page == null) { W.Line(col, L.T("common.loading")); }
            else if (!_page["items"]!.Any()) { W.Line(col, L.T("market.empty"), 34, UiKit.InkSoft, 200); }
            else
            {
                var gridHost = UiKit.Rect(col, "Grid");
                var grid = gridHost.gameObject.AddComponent<GridLayoutGroup>();
                float cw = Wide ? 250 : 300;
                grid.cellSize = new Vector2(cw, cw / CardView.Aspect + 120); grid.spacing = new Vector2(20, 24);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = Wide ? 6 : 3;
                gridHost.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                foreach (var it in _page["items"]!) Tile(gridHost, it, cw);
            }
            HubNav.Build(Root, Mode, "market");
        }

        private void Tile(Transform parent, JToken it, float cw)
        {
            var cell = UiKit.Rect(parent, "Listing");
            var cv = CardView.Create(cell, W.Card(it["card"]!), cw, interactive: false);
            ((RectTransform)cv.transform).Place(new Vector2(0.5f, 1), new Vector2(cw, cw / CardView.Aspect), Vector2.zero, new Vector2(0.5f, 1));
            var price = W.Price(cell, (string)it["currency"]!, (long)it["price"]!, 30);
            price.Place(new Vector2(0.5f, 0), new Vector2(cw, 56), new Vector2(0, 54), new Vector2(0.5f, 0));
            var seller = UiKit.Text(cell, (bool)it["isMine"]! ? L.T("market.yours") + " · " + L.T("market.status." + (string)it["status"]!) : (string)it["sellerNickname"]!, 24, UiKit.InkSoft);
            seller.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(cw, 44), new Vector2(0, 8), new Vector2(0.5f, 0));
            long id = (long)it["listingId"]!;
            cell.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            cell.gameObject.AddComponent<Button>().onClick.AddListener(() => Nav.Push(new ListingDetailScreen(id)));
        }

        public override void OnShow() { if (_page == null) Reload(); }

        private async void Reload()
        {
            var url = $"/api/market/listings?section={_section}&sort={_sort ?? "newest"}" + (_currency != null ? "&currency=" + _currency : "") + (_rarity != null ? "&rarity=" + _rarity : "")
                      + (_query.Length == 12 && _query.All(char.IsLetterOrDigit) ? "&serial=" + _query.ToUpperInvariant() : _query.Length > 0 ? "&q=" + Uri.EscapeDataString(_query) : "");
            _page = await Call(() => Api.Get<JObject>(url), overlay: _page == null);
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class ListingDetailScreen : ScreenBase
    {
        public override string TitleKey => "market.listing";
        private readonly long _id;
        private JObject? _data;
        public ListingDetailScreen(long id) { _id = id; }

        public override void Build()
        {
            if (_data == null) return;
            var l = _data["listing"]!;
            var card = W.Card(l["card"]!);
            float cw = Wide ? 480 : 520;
            var cv = CardView.Create(Root, card, cw);
            ((RectTransform)cv.transform).Place(Wide ? new Vector2(0.25f, 0.5f) : new Vector2(0.5f, 1), ((RectTransform)cv.transform).sizeDelta, Wide ? Vector2.zero : new Vector2(0, -320), Wide ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1));
            var info = UiKit.Rect(Root, "Info");
            if (Wide) info.Stretch(Screen.width > 0 ? 960 : 960, 150, 60, 40); else info.Stretch(40, 320 + cw / CardView.Aspect + 30, 40, 40);
            var col = UiKit.Scroll(info);
            UiKit.VStack(col, 12, new RectOffset(10, 10, 0, 40), TextAnchor.UpperLeft);
            W.Header(col, $"{card.Name}  <size=60%><color=#5B6B8F>{card.Serial}</color></size>", 48);
            W.Line(col, $"{card.Rarity} · {L.T("filter.quality")} {card.Quality} · {L.T("finish." + card.Finish)} · {L.T("card.level", card.Level)}");
            W.Price(col, (string)l["currency"]!, (long)l["price"]!, 44).Size(-1, 80);
            bool mine = (bool)l["isMine"]!;
            W.Line(col, mine ? L.T("market.fee_seller", (long)l["fee"]!, (long)l["sellerNet"]!) : L.T("market.fee_buyer_info", (int)l["feeBps"]! / 100.0), 28);
            if (l["estimatedValue"]?.Type == JTokenType.Integer) W.Line(col, L.T("market.reference_value", L.Number((long)l["estimatedValue"]!)), 28, UiKit.Sapphire);
            W.Line(col, L.T("market.reference_disclaimer"), 24);
            W.Line(col, L.T("market.seller", (string)l["sellerNickname"]!) + " · " + L.T("market.expires", W.Ago((DateTimeOffset)l["expiresAt"]!).Replace("-", "")), 26);
            var trades = _data["cardTradeHistory"]!;
            W.Header(col, L.T("market.trade_history"), 34);
            if (!trades.Any()) W.Line(col, L.T("market.no_trades"), 28);
            foreach (var t in trades) W.Line(col, $"{((DateTimeOffset)t["at"]!).LocalDateTime:yyyy-MM-dd} · {L.Number((long)t["price"]!)} {L.T("currency." + ((string)t["currency"]!).ToLowerInvariant())}", 28);
            var row = W.Row(col, 120);
            if (mine)
            {
                if ((string)l["status"]! == "active") W.Btn(row, L.T("market.cancel_listing"), Cancel, UiKit.ButtonStyle.Danger, 32);
            }
            else if ((string)l["status"]! == "active")
                W.Btn(row, L.T("market.buy"), () => W.Confirm(L.T("market.confirm_buy_title"),
                    L.T("market.confirm_buy_body", card.Name, card.Serial, L.Number((long)l["price"]!), L.T("currency." + ((string)l["currency"]!).ToLowerInvariant())), L.T("market.buy"), Buy), UiKit.ButtonStyle.Gold, 36);
            var row2 = W.Row(col, 100);
            W.Btn(row2, L.T("passport.open"), () => Nav.Push(new PassportScreen(card.Serial)));
            W.Btn(row2, L.T("price.history"), () => Nav.Push(new PriceHistoryScreen(card.FormId, card.Rarity)));
            if (!mine) W.Btn(row2, L.T("report.button"), () => Report.Show("listing", _id));
        }

        public override async void OnShow()
        {
            _data = await Call(() => Api.Get<JObject>($"/api/market/listings/{_id}"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }

        private async void Buy()
        {
            long price = (long)_data!["listing"]!["price"]!;
            var r = await Call(() => Api.Post<JObject>("/api/market/buy", new { listingId = _id, expectedPrice = price }, ApiClient.NewIdempotencyKey()));
            if (r == null) return;
            Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>());
            Audio.Play(Audio.Sfx.RareReveal);
            Nav.Toast(L.T("market.bought"));
            OnShow();
        }

        private async void Cancel()
        {
            if (await Call(() => Api.Post<JObject>($"/api/market/listings/{_id}/cancel", null)) != null) { Nav.Toast(L.T("market.cancelled")); OnShow(); }
        }
    }

    public sealed class SellCardScreen : ScreenBase
    {
        public override string TitleKey => "market.sell";
        private readonly CardDto _card;
        private string _currency = Currency.MiniGreenBean;
        private bool _auction;
        private int _days = 7, _hours = 24;
        private TMP_InputField _price = null!, _buyout = null!;
        private TextMeshProUGUI _quote = null!;
        public SellCardScreen(CardDto card) { _card = card; }

        public override void Build()
        {
            float cw = Wide ? 420 : 380;
            var cv = CardView.Create(Root, _card, cw);
            ((RectTransform)cv.transform).Place(Wide ? new Vector2(0.22f, 0.5f) : new Vector2(0.5f, 1), ((RectTransform)cv.transform).sizeDelta, Wide ? Vector2.zero : new Vector2(0, -320), Wide ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1));
            var info = UiKit.Rect(Root, "Form");
            if (Wide) info.Stretch(820, 150, 60, 40); else info.Stretch(40, 320 + cw / CardView.Aspect + 20, 40, 30);
            var col = UiKit.Scroll(info);
            UiKit.VStack(col, 14, new RectOffset(10, 10, 0, 40), TextAnchor.UpperLeft);
            W.Header(col, $"{_card.Name} · {_card.Serial}", 40);
            W.Tabs(col, new[] { ("fixed", L.T("market.fixed_price")), ("auction", L.T("market.auction")) }, _auction ? "auction" : "fixed", t => { _auction = t == "auction"; Rebuild(); });
            W.Tabs(col, new[] { (Currency.MiniGreenBean, L.T("currency.mini_green_bean")), (Currency.RainbowStar, L.T("currency.rainbow_star")) }, _currency, c => { _currency = c; Rebuild(); });
            _price = W.Input(col, _auction ? L.T("market.start_price") : L.T("market.price"), numeric: true);
            _price.onEndEdit.AddListener(_ => Quote());
            if (_auction)
            {
                _buyout = W.Input(col, L.T("market.buyout_optional"), numeric: true);
                W.Tabs(col, new[] { ("6", "6h"), ("24", "24h"), ("48", "48h"), ("72", "72h") }, _hours.ToString(), h => { _hours = int.Parse(h); });
            }
            else W.Tabs(col, new[] { ("1", L.T("time.days", 1)), ("3", L.T("time.days", 3)), ("7", L.T("time.days", 7)), ("14", L.T("time.days", 14)) }, _days.ToString(), d => { _days = int.Parse(d); });
            _quote = W.Line(col, L.T("market.enter_price"), 30, UiKit.Ink, 180);
            _quote.enableWordWrapping = true;
            W.Line(col, L.T("market.in_game_only"), 24);
            var row = W.Row(col, 120);
            W.Btn(row, _auction ? L.T("market.start_auction") : L.T("market.list_now"), Submit, UiKit.ButtonStyle.Gold, 36);
        }

        private void Rebuild() { UiKit.Clear(Root); Build(); }

        private async void Quote()
        {
            if (!long.TryParse(_price.text, out var p) || p <= 0) return;
            var q = await Call(() => Api.Get<JObject>($"/api/market/sell-quote?cardId={_card.Id}&currency={_currency}&price={p}"), overlay: false);
            if (q == null || _quote == null) return;
            var r = q["reference"]!;
            _quote.text = L.T("market.quote", L.Number((long)q["fee"]!), (int)q["feeBps"]! / 100.0, L.Number((long)q["sellerNet"]!), L.Number((long)r["estimated"]!), L.T("confidence." + (string)r["confidence"]!))
                          + (q["warning"]?.Type == JTokenType.String ? "\n<color=#E5484D>" + L.T("market.warning." + (string)q["warning"]!) + "</color>" : "");
        }

        private async void Submit()
        {
            if (!long.TryParse(_price.text, out var p) || p <= 0) { Nav.Toast(L.T("market.enter_price"), true); return; }
            JObject? r;
            if (_auction)
            {
                long? buyout = long.TryParse(_buyout.text, out var b) && b > 0 ? b : null;
                r = await Call(() => Api.Post<JObject>("/api/auctions", new { cardId = _card.Id, currency = _currency, startPrice = p, buyoutPrice = buyout, hours = _hours }, ApiClient.NewIdempotencyKey()));
            }
            else r = await Call(() => Api.Post<JObject>("/api/market/listings", new { cardId = _card.Id, currency = _currency, price = p, days = _days, confirmPriceWarning = true }, ApiClient.NewIdempotencyKey()));
            if (r == null) return;
            Nav.Toast(L.T(_auction ? "market.auction_started" : "market.listed"));
            Nav.Back();
        }
    }

    public sealed class MarketHistoryScreen : ScreenBase
    {
        public override string TitleKey => "market.history";
        private string _type = "purchases";
        private JArray? _rows;

        public override void Build()
        {
            var col = W.Column(this, Root);
            W.Tabs(col, new[] { ("purchases", L.T("market.purchases")), ("sales", L.T("market.sales")) }, _type, t => { _type = t; _rows = null; OnShow(); });
            if (_rows == null) return;
            if (_rows.Count == 0) W.Line(col, L.T("market.no_history"), 32, null, 160);
            foreach (var r in _rows)
            {
                var c = W.Card(col, null, 150);
                var t = UiKit.Text(c.transform, $"{GameDataClient.PetName((string)r["formId"]!)} · {(string)r["rarity"]!} · {(string)r["serial"]!}\n<size=80%><color=#5B6B8F>{((DateTimeOffset)r["at"]!).LocalDateTime:yyyy-MM-dd HH:mm} · {L.T("market.kind." + (string)r["kind"]!)}</color></size>",
                    30, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft);
                t.rectTransform.Stretch(30, 10, 330, 10);
                var price = W.Price(c.transform, (string)r["currency"]!, _type == "sales" ? (long)r["sellerNet"]! : (long)r["price"]!, 30);
                price.Place(new Vector2(1, 0.5f), new Vector2(300, 60), new Vector2(-20, 0), new Vector2(1, 0.5f));
            }
        }

        public override async void OnShow()
        {
            _rows = await Call(() => Api.Get<JArray>($"/api/market/history?type={_type}"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class WishlistScreen : ScreenBase
    {
        public override string TitleKey => "market.wishlist";
        private JArray? _rows;
        private TMP_InputField _name = null!, _max = null!;
        private string _currency = Currency.MiniGreenBean;
        private string? _minRarity;

        public override void Build()
        {
            var col = W.Column(this, Root);
            W.Header(col, L.T("wishlist.add"), 36);
            _name = W.Input(col, L.T("wishlist.pet_name"));
            var r = W.Row(col, 96, 10);
            W.Btn(r, L.T("currency." + _currency.ToLowerInvariant()), () => { _currency = _currency == Currency.MiniGreenBean ? Currency.RainbowStar : Currency.MiniGreenBean; Rebuild(); });
            W.Btn(r, _minRarity == null ? L.T("wishlist.any_rarity") : "≥ " + _minRarity, () => { var o = GameDataClient.RarityOrder.ToList(); _minRarity = _minRarity == null ? "SR" : o.IndexOf(_minRarity) < o.Count - 1 ? o[o.IndexOf(_minRarity) + 1] : null; Rebuild(); });
            _max = W.Input(col, L.T("wishlist.max_price"), numeric: true);
            W.Btn(W.Row(col, 110), L.T("wishlist.save"), Add, UiKit.ButtonStyle.Gold, 34);
            W.Header(col, L.T("wishlist.entries"), 36);
            foreach (var w in _rows ?? new JArray())
            {
                var c = W.Card(col, null, 130);
                string target = w["formId"]?.Type == JTokenType.String ? GameDataClient.PetName((string)w["formId"]!) : (string?)w["familyId"] ?? "";
                UiKit.Text(c.transform, $"{target} · {((string?)w["minRarity"] ?? L.T("wishlist.any_rarity"))} · ≤ {(w["maxPrice"]?.Type == JTokenType.Integer ? L.Number((long)w["maxPrice"]!) : "∞")}", 30, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(30, 0, 220, 0);
                long id = (long)w["id"]!;
                var del = UiKit.Button(c.transform, L.T("common.delete"), async () => { await Call(() => Api.Delete<JObject>($"/api/market/wishlist/{id}")); OnShow(); }, UiKit.ButtonStyle.Ghost, 28);
                ((RectTransform)del.transform).Place(new Vector2(1, 0.5f), new Vector2(200, 90), new Vector2(-10, 0), new Vector2(1, 0.5f));
            }
        }

        private void Rebuild() { UiKit.Clear(Root); Build(); }

        private async void Add()
        {
            var form = GameDataClient.Forms.Values.FirstOrDefault(f => f.Name.Equals(_name.text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (form == null) { Nav.Toast(L.T("wishlist.unknown_pet"), true); return; }
            long? max = long.TryParse(_max.text, out var m) && m > 0 ? m : null;
            if (await Call(() => Api.Post<JObject>("/api/market/wishlist", new { formId = form.Id, currency = _currency, minRarity = _minRarity, maxPrice = max })) != null) OnShow();
        }

        public override async void OnShow()
        {
            _rows = await Call(() => Api.Get<JArray>("/api/market/wishlist"));
            if (Root != null) Rebuild();
        }
    }

    public sealed class AuctionHouseScreen : ScreenBase
    {
        public override string TitleKey => "auction.title";
        private string _section = "active";
        private JArray? _rows;

        public override void Build()
        {
            var col = W.Column(this, Root);
            W.Tabs(col, new[] { ("active", L.T("auction.active")), ("bidding", L.T("auction.bidding")), ("mine", L.T("auction.mine")) }, _section, s => { _section = s; _rows = null; OnShow(); });
            if (_rows == null) return;
            if (_rows.Count == 0) W.Line(col, L.T("auction.empty"), 32, null, 160);
            foreach (var a in _rows)
            {
                var c = W.Card(col, null, 220);
                var card = W.Card(a["card"]!);
                var cv = CardView.Create(c.transform, card, 112, interactive: false);
                ((RectTransform)cv.transform).Place(new Vector2(0, 0.5f), ((RectTransform)cv.transform).sizeDelta, new Vector2(16, 0), new Vector2(0, 0.5f));
                long? hi = a["highestAmount"]?.Type == JTokenType.Integer ? (long)a["highestAmount"]! : null;
                var left = (DateTimeOffset)a["endsAt"]! - (DateTimeOffset)a["serverTime"]!;
                UiKit.Text(c.transform, $"{card.Name} · {card.Rarity} · {card.Serial}\n<size=80%>{L.T("auction.bids", (int)a["bidCount"]!)} · {W.Remaining(left)}{((bool)a["leading"]! ? " · " + L.T("auction.leading") : "")}</size>",
                    30, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(150, 20, 330, 20);
                var price = W.Price(c.transform, (string)a["currency"]!, hi ?? (long)a["startPrice"]!, 30);
                price.Place(new Vector2(1, 0.5f), new Vector2(300, 60), new Vector2(-20, 0), new Vector2(1, 0.5f));
                long id = (long)a["auctionId"]!;
                c.gameObject.AddComponent<Button>().onClick.AddListener(() => Nav.Push(new AuctionDetailScreen(id)));
            }
        }

        public override async void OnShow()
        {
            _rows = await Call(() => Api.Get<JArray>($"/api/auctions?section={_section}"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class AuctionDetailScreen : ScreenBase
    {
        public override string TitleKey => "auction.detail";
        private readonly long _id;
        private JObject? _data;
        private TimeSpan _serverOffset;
        private TextMeshProUGUI? _timer;
        private TMP_InputField _bid = null!;
        public AuctionDetailScreen(long id) { _id = id; }

        public override void Build()
        {
            if (_data == null) return;
            var a = _data["auction"]!;
            var card = W.Card(a["card"]!);
            float cw = Wide ? 440 : 440;
            var cv = CardView.Create(Root, card, cw);
            ((RectTransform)cv.transform).Place(Wide ? new Vector2(0.24f, 0.5f) : new Vector2(0.5f, 1), ((RectTransform)cv.transform).sizeDelta, Wide ? Vector2.zero : new Vector2(0, -320), Wide ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1));
            var info = UiKit.Rect(Root, "Info");
            if (Wide) info.Stretch(860, 150, 60, 40); else info.Stretch(40, 320 + cw / CardView.Aspect + 20, 40, 30);
            var col = UiKit.Scroll(info);
            UiKit.VStack(col, 12, new RectOffset(10, 10, 0, 40), TextAnchor.UpperLeft);
            W.Header(col, $"{card.Name} · {card.Serial}", 42);
            _timer = W.Line(col, "", 40, UiKit.Sapphire);
            long? hi = a["highestAmount"]?.Type == JTokenType.Integer ? (long)a["highestAmount"]! : null;
            W.Line(col, hi == null ? L.T("auction.no_bids_start", L.Number((long)a["startPrice"]!)) : L.T("auction.highest", L.Number(hi.Value)) + ((bool)a["leading"]! ? " · " + L.T("auction.leading") : ""), 32, UiKit.Ink);
            W.Price(col, (string)a["currency"]!, hi ?? (long)a["startPrice"]!, 40).Size(-1, 70);
            if (a["buyoutPrice"]?.Type == JTokenType.Integer) W.Line(col, L.T("auction.buyout", L.Number((long)a["buyoutPrice"]!)), 30);
            bool active = (string)a["status"]! == "active";
            if (active && !(bool)a["isMine"]!)
            {
                _bid = W.Input(col, L.T("auction.min_bid", L.Number((long)a["nextMinBid"]!)), numeric: true);
                var row = W.Row(col, 120);
                W.Btn(row, L.T("auction.place_bid"), () => Bid(long.TryParse(_bid.text, out var v) ? v : (long)a["nextMinBid"]!), UiKit.ButtonStyle.Gold, 34);
                if (a["buyoutPrice"]?.Type == JTokenType.Integer) W.Btn(row, L.T("auction.buy_now"), () => Bid((long)a["buyoutPrice"]!), UiKit.ButtonStyle.Primary, 34);
                W.Line(col, L.T("auction.hold_info"), 24);
            }
            else if (active && (bool)a["isMine"]! && (int)a["bidCount"]! == 0)
                W.Btn(W.Row(col, 110), L.T("auction.cancel"), async () => { if (await Call(() => Api.Post<JObject>($"/api/auctions/{_id}/cancel", null)) != null) OnShow(); }, UiKit.ButtonStyle.Danger);
            else W.Line(col, L.T("auction.status." + (string)a["status"]!), 32, UiKit.Ink);
            W.Header(col, L.T("auction.bid_history"), 34);
            foreach (var b in _data["bids"]!)
                W.Line(col, $"{L.Number((long)b["amount"]!)} · {W.Ago((DateTimeOffset)b["createdAt"]!)}{((bool)b["mine"]! ? " · " + L.T("auction.you") : "")}", 28);
            Root.gameObject.AddComponent<Ticker>().OnTick = Tick;
        }

        private void Tick()
        {
            if (_data == null || _timer == null) return;
            var ends = (DateTimeOffset)_data["auction"]!["endsAt"]!;
            _timer.text = L.T("auction.time_left", W.Remaining(ends - (DateTimeOffset.UtcNow + _serverOffset)));
        }

        public override async void OnShow()
        {
            _data = await Call(() => Api.Get<JObject>($"/api/auctions/{_id}"));
            if (_data != null) _serverOffset = (DateTimeOffset)_data["serverTime"]! - DateTimeOffset.UtcNow;   // server time is authoritative
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }

        private async void Bid(long amount)
        {
            var r = await Call(() => Api.Post<JObject>("/api/auctions/bid", new { auctionId = _id, amount }, ApiClient.NewIdempotencyKey()));
            if (r == null) return;
            Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>());
            Nav.Toast(L.T("auction.bid_placed"));
            OnShow();
        }
    }

    public sealed class PriceHistoryScreen : ScreenBase
    {
        public override string TitleKey => "price.history";
        private readonly string _formId, _rarity;
        private string _currency = Currency.MiniGreenBean;
        private JObject? _data;
        public PriceHistoryScreen(string formId, string rarity) { _formId = formId; _rarity = rarity; }

        public override void Build()
        {
            var col = W.Column(this, Root);
            W.Header(col, $"{GameDataClient.PetName(_formId)} · {_rarity}", 42);
            W.Tabs(col, new[] { (Currency.MiniGreenBean, L.T("currency.mini_green_bean")), (Currency.RainbowStar, L.T("currency.rainbow_star")) }, _currency, c => { _currency = c; _data = null; OnShow(); });
            if (_data == null) return;
            var snaps = _data["snapshots"]!.ToList();
            var chartPanel = W.Card(col, null, 420);
            var chart = UiKit.Rect(chartPanel.transform, "Chart").Stretch(30, 30, 30, 30).gameObject.AddComponent<LineChart>();
            chart.color = UiKit.Sapphire;
            chart.SetValues(snaps.Select(s => (float)(long)s["estimatedValue"]!));
            if (snaps.Count > 0)
            {
                var last = snaps.Last();
                W.Line(col, L.T("price.estimated", L.Number((long)last["estimatedValue"]!), L.T("confidence." + (string)last["confidence"]!), (int)last["sampleSize"]!), 30, UiKit.Ink);
                W.Line(col, L.T("price.initial", L.Number((long)last["initialReference"]!), (int)last["supply"]!), 28);
            }
            else W.Line(col, L.T("price.no_data"), 30);
            W.Line(col, L.T("market.reference_disclaimer"), 24);
            W.Header(col, L.T("price.recent_trades"), 34);
            var trades = _data["trades"]!.Reverse().Take(30).ToList();
            if (trades.Count == 0) W.Line(col, L.T("market.no_trades"), 28);
            foreach (var t in trades) W.Line(col, $"{((DateTimeOffset)t["at"]!).LocalDateTime:yyyy-MM-dd HH:mm} · {L.Number((long)t["price"]!)}", 28);
        }

        public override async void OnShow()
        {
            _data = await Call(() => Api.Get<JObject>($"/api/valuation/history?formId={_formId}&rarity={_rarity}&currency={_currency}&days=60"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    /// <summary>Per-frame callback host (countdowns).</summary>
    public sealed class Ticker : MonoBehaviour
    {
        public Action? OnTick;
        private float _t;
        private void Update() { _t += Time.unscaledDeltaTime; if (_t >= 0.25f) { _t = 0; OnTick?.Invoke(); } }
    }

    public static class Report
    {
        private static readonly string[] Reasons = { "spam", "harassment", "inappropriate", "scam", "cheating", "other" };

        public static void Show(string targetType, long targetId)
        {
            var panel = Navigator.I.Popup(new Vector2(900, 1100), new Vector2(900, 900), out var close);
            var col = UiKit.Rect(panel, "Col").Stretch(40, 40, 40, 40);
            UiKit.VStack(col, 14);
            W.Header(col, L.T("report.title"), 42);
            foreach (var r in Reasons)
            {
                var reason = r;
                W.Btn(W.Row(col, 100), L.T("report.reason." + r), async () =>
                {
                    close();
                    try { await App.Api.Post<JObject>("/api/reports", new { targetType, targetId, reason }); Navigator.I.Toast(L.T("report.sent")); }
                    catch (ApiError e) { Navigator.I.Toast(L.Error(e.Code), true); }
                });
            }
        }
    }
}
