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
    /// <summary>Card Passport: a collectible certificate. Public view hides private fields (server decides).</summary>
    public sealed class PassportScreen : ScreenBase
    {
        public override string TitleKey => "passport.title";
        private readonly string? _serial;
        private readonly long? _cardId;
        private JObject? _p;
        public PassportScreen(string serial) { _serial = serial; }
        public PassportScreen(long cardId) { _cardId = cardId; }

        public override void Build()
        {
            if (_p == null) return;
            var card = W.Card(_p["card"]!);
            // certificate frame: gold border, sapphire header
            var cert = UiKit.Panel(Root, UiKit.Gold, 48, "Certificate", shadow: true);
            var crt = (RectTransform)cert.transform;
            if (Wide) crt.Stretch(60, 140, 60, 30); else crt.Stretch(30, 320, 30, 30);
            var inner = UiKit.Panel(cert.transform, UiKit.Hex("#FFFDF6"), 40, "Inner");
            ((RectTransform)inner.transform).Stretch(10, 10, 10, 10);
            float cw = Wide ? 400 : 340;
            var cv = CardView.Create(inner.transform, card, cw);
            ((RectTransform)cv.transform).Place(Wide ? new Vector2(0, 0.5f) : new Vector2(0.5f, 1), ((RectTransform)cv.transform).sizeDelta, Wide ? new Vector2(50, 0) : new Vector2(0, -40), Wide ? new Vector2(0, 0.5f) : new Vector2(0.5f, 1));
            var area = UiKit.Rect(inner.transform, "Details");
            if (Wide) area.Stretch(cw + 100, 40, 40, 40); else area.Stretch(30, cw / CardView.Aspect + 70, 30, 30);
            var col = UiKit.Scroll(area);
            UiKit.VStack(col, 8, new RectOffset(0, 0, 0, 40), TextAnchor.UpperLeft);
            W.Line(col, L.T("passport.heading"), 26, UiKit.GoldDeep);
            W.Header(col, (string)_p["serial"]!, 60).characterSpacing = 10;
            W.Line(col, $"{card.Name} · {L.T("element." + card.Element)} · {L.T("card.stage", card.Stage)}", 32, UiKit.Ink);
            W.Line(col, $"{card.Rarity} · {L.T("filter.quality")} {card.Quality} · {L.T("finish." + card.Finish)} · {L.T("card.level", card.Level)} · {L.T("card.skill_level", card.SkillLevel)}", 28);
            W.Line(col, L.T("passport.issued", ((System.DateTimeOffset)_p["issuedAt"]!).LocalDateTime.ToString("yyyy-MM-dd"), (string)_p["issuedServer"]!, L.T("origin." + (string)_p["origin"]!)), 28);
            W.Line(col, L.T("passport.owner", (string)_p["ownerNickname"]!, (int)_p["transferCount"]!), 28, UiKit.Ink);
            W.Line(col, L.T("passport.lock." + (string)_p["lockStatus"]!), 28, UiKit.Sapphire);
            W.Header(col, L.T("passport.values"), 32);
            foreach (var v in _p["values"]!)
                W.Line(col, L.T("passport.value_line", L.T("currency." + ((string)v["currency"]!).ToLowerInvariant()), L.Number((long)v["initial"]!), L.Number((long)v["estimated"]!), L.T("confidence." + (string)v["confidence"]!)), 26);
            W.Line(col, L.T("market.reference_disclaimer"), 22);
            void Section(string key, string field)
            {
                var list = _p![field]!.ToList();
                if (list.Count == 0) return;
                W.Header(col, L.T(key), 30);
                foreach (var e in list.Take(10))
                {
                    string when = ((System.DateTimeOffset)e["at"]!).LocalDateTime.ToString("yyyy-MM-dd");
                    string what = field switch
                    {
                        "evolutions" => $"{GameDataClient.PetName((string)e["fromFormId"]!)} → {GameDataClient.PetName((string)e["toFormId"]!)}",
                        "upgrades" => L.T("passport.upgrade." + (string)e["type"]!),
                        _ => $"{L.Number((long?)e["price"] ?? 0)} {L.T("currency." + ((string?)e["currency"] ?? "").ToLowerInvariant())}" + (e["status"]?.Type == JTokenType.String ? " · " + L.T("market.status." + (string)e["status"]!) : ""),
                    };
                    W.Line(col, $"{when} · {what}", 26);
                }
            }
            Section("passport.evolutions", "evolutions");
            Section("passport.upgrades", "upgrades");
            Section("passport.listings", "listings");
            Section("passport.trades", "trades");
            var row = W.Row(col, 100);
            W.Btn(row, L.T("price.history"), () => Nav.Push(new PriceHistoryScreen(card.FormId, card.Rarity)));
            if ((bool)_p["isOwner"]!) W.Btn(row, L.T("passport.share"), () => Nav.Push(new CreatePostScreen(new CardDto { Id = (long)_p["cardId"]!, Serial = card.Serial, FormId = card.FormId, Name = card.Name, Rarity = card.Rarity, Quality = card.Quality, Finish = card.Finish, Element = card.Element, Role = card.Role, FamilyId = card.FamilyId, Stage = card.Stage, Level = card.Level })), UiKit.ButtonStyle.Gold);
        }

        public override async void OnShow()
        {
            _p = await Call(() => Api.Get<JObject>(_cardId.HasValue ? $"/api/cards/{_cardId}/passport" : $"/api/passport/{_serial}"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class NotificationCenterScreen : ScreenBase
    {
        public override string TitleKey => "notifications.title";
        private JObject? _data;

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_data == null) return;
            W.Btn(W.Row(col, 96), L.T("notifications.mark_all"), async () => { await Call(() => Api.Post<JObject>("/api/notifications/read", new List<long>())); OnShow(); });
            var items = _data["items"]!.ToList();
            if (items.Count == 0) W.Line(col, L.T("notifications.empty"), 32, null, 160);
            foreach (var n in items)
            {
                var c = W.Card(col, (bool)n["read"]! ? UiKit.White : UiKit.Sky, 120);
                UiKit.Text(c.transform, $"{L.T("notif." + (string)n["type"]!)}  <size=75%><color=#5B6B8F>{W.Ago((System.DateTimeOffset)n["createdAt"]!)}</color></size>", 30, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 0, 24, 0);
            }
        }

        public override async void OnShow()
        {
            _data = await Call(() => Api.Get<JObject>("/api/notifications"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class MailboxScreen : ScreenBase
    {
        public override string TitleKey => "mailbox.title";
        private JArray? _rows;

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_rows == null) return;
            if (_rows.Count == 0) W.Line(col, L.T("mailbox.empty"), 32, null, 160);
            foreach (var m in _rows)
            {
                long id = (long)m["id"]!;
                var c = W.Card(col, null, 170);
                UiKit.Text(c.transform, $"<b>{L.T((string)m["titleKey"]!)}</b>\n<size=80%>{(string)m["body"]!}</size>", 30, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 10, 280, 10);
                var b = UiKit.Button(c.transform, (bool)m["claimed"]! ? L.T("mailbox.claimed") : L.T("mailbox.claim"), async () =>
                {
                    var r = await Call(() => Api.Post<JObject>($"/api/mailbox/{id}/claim", null));
                    if (r != null) { Session.UpdateCurrencies(r["currencies"]?.ToObject<Dictionary<string, long>>()); OnShow(); }
                }, (bool)m["claimed"]! ? UiKit.ButtonStyle.Secondary : UiKit.ButtonStyle.Gold, 28);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 100), new Vector2(-16, 0), new Vector2(1, 0.5f));
                b.interactable = !(bool)m["claimed"]!;
            }
        }

        public override async void OnShow()
        {
            _rows = await Call(() => Api.Get<JArray>("/api/mailbox"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class CollectionHubScreen : ScreenBase
    {
        public override string TitleKey => "hub.collection";
        public override bool ShowBackButton => false;

        public override void Build()
        {
            var col = W.Column(this, Root, 20, hubNav: true);
            foreach (var (key, color, open) in new (string, Color, System.Action)[]
                     {
                         ("lobby.pet_home", UiKit.Mint, () => Nav.Push(new PetHomeScreen())),
                         ("lobby.codex", UiKit.SkyDeep, () => Nav.Push(new CodexScreen())),
                         ("albums.title", UiKit.Lilac, () => Nav.Push(new AlbumsScreen())),
                         ("achievements.title", UiKit.Hex("#FFF1C9"), () => Nav.Push(new AchievementsScreen())),
                     })
            {
                var b = UiKit.Button(col, L.T(key), open, UiKit.ButtonStyle.Secondary, 40, 40);
                b.GetComponent<Image>().color = color; b.Size(-1, 170);
            }
            HubNav.Build(Root, Mode, "collection");
        }
    }

    public sealed class AlbumsScreen : ScreenBase
    {
        public override string TitleKey => "albums.title";
        private JObject? _data;

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_data == null) return;
            W.Header(col, L.T("albums.codex_completion", (double)_data["codexCompletion"]!), 36);
            foreach (var a in _data["system"]!)
            {
                int total = (int)a["total"]!, done = (int)a["discovered"]!;
                if (total == 0) { W.Line(col, L.T("album." + (string)a["id"]!) + " · " + L.T("albums.coming_soon")); continue; }
                var c = W.Card(col, (bool)a["complete"]! ? UiKit.Mint : UiKit.White, 120);
                UiKit.Text(c.transform, $"{L.T("album." + (string)a["id"]!)}  {done}/{total}", 32, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 0, 300, 0);
                var barBack = UiKit.Panel(c.transform, UiKit.SkyDeep, 12, "Bar");
                ((RectTransform)barBack.transform).Place(new Vector2(1, 0.5f), new Vector2(260, 24), new Vector2(-24, 0), new Vector2(1, 0.5f));
                var fill = UiKit.Panel(barBack.transform, UiKit.Sapphire, 12, "Fill");
                var frt = (RectTransform)fill.transform; frt.Stretch(); frt.anchorMax = new Vector2(Mathf.Clamp01(done / (float)total), 1);
            }
            W.Header(col, L.T("albums.custom"), 36);
            foreach (var a in _data["custom"]!)
            {
                long id = (long)a["id"]!;
                var c = W.Card(col, null, 110);
                UiKit.Text(c.transform, $"{(string)a["name"]!} · {a["cardIds"]!.Count()} {L.T("albums.cards")}", 30, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 0, 420, 0);
                var btns = UiKit.Rect(c.transform, "B"); btns.Place(new Vector2(1, 0.5f), new Vector2(400, 90), new Vector2(-10, 0), new Vector2(1, 0.5f));
                UiKit.HStack(btns, 10).childForceExpandWidth = true;
                W.Btn(btns, L.T("feed.share"), async () => { if (await Call(() => Api.Post<JObject>("/api/feed/posts", new { type = "album", refId = id.ToString(), caption = "" })) != null) Nav.Toast(L.T("feed.published")); }, UiKit.ButtonStyle.Secondary, 26);
                W.Btn(btns, L.T("common.delete"), async () => { await Call(() => Api.Delete<JObject>($"/api/collections/{id}")); OnShow(); }, UiKit.ButtonStyle.Ghost, 26);
            }
            var name = W.Input(col, L.T("albums.name_hint"));
            W.Btn(W.Row(col, 110), L.T("albums.create"), () => W.PickCards(60, async cs =>
            {
                if (await Call(() => Api.Post<JObject>("/api/collections", new { name = name.text, cardIds = cs.Select(c => c.Id).ToList() })) != null) OnShow();
            }), UiKit.ButtonStyle.Gold);
        }

        public override async void OnShow()
        {
            _data = await Call(() => Api.Get<JObject>("/api/collections"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class AchievementsScreen : ScreenBase
    {
        public override string TitleKey => "achievements.title";
        private JArray? _rows;

        public override void Build()
        {
            var col = W.Column(this, Root);
            foreach (var a in _rows ?? new JArray())
            {
                bool ok = (bool)a["unlocked"]!;
                string id = (string)a["id"]!;
                var c = W.Card(col, ok ? UiKit.Mint : UiKit.White, 120);
                UiKit.Text(c.transform, (ok ? "★ " : "☆ ") + L.T("ach." + id), 32, ok ? UiKit.Ink : UiKit.InkSoft, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 0, 260, 0);
                if (ok)
                {
                    var b = UiKit.Button(c.transform, L.T("feed.share"), async () => { if (await Call(() => Api.Post<JObject>("/api/feed/posts", new { type = "achievement", refId = id, caption = "" })) != null) Nav.Toast(L.T("feed.published")); }, UiKit.ButtonStyle.Secondary, 26);
                    ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(220, 90), new Vector2(-10, 0), new Vector2(1, 0.5f));
                }
            }
        }

        public override async void OnShow()
        {
            _rows = await Call(() => Api.Get<JArray>("/api/achievements"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class BattleHubScreen : ScreenBase
    {
        public override string TitleKey => "hub.battle";
        public override bool ShowBackButton => false;

        public override void Build()
        {
            var col = W.Column(this, Root, 20, hubNav: true);
            foreach (var (key, color, open) in new (string, Color, System.Action)[]
                     {
                         ("battle.pve", UiKit.Lilac, () => Nav.Push(new DuelLobbyScreen())),
                         ("battle.ranked", UiKit.Peach, () => Nav.Push(new RankedScreen())),
                         ("battle.friend", UiKit.Mint, () => Nav.Push(new FriendsScreen())),
                         ("battle.history", UiKit.SkyDeep, () => Nav.Push(new BattleHistoryScreen())),
                     })
            {
                var b = UiKit.Button(col, L.T(key), open, UiKit.ButtonStyle.Secondary, 40, 40);
                b.GetComponent<Image>().color = color; b.Size(-1, 170);
            }
            HubNav.Build(Root, Mode, "battle");
        }

        public static async void FriendDuel(long uid)
        {
            try
            {
                var b = await App.Api.Post<BattleDto>("/api/duels/friend", new { uid }, ApiClient.NewIdempotencyKey());
                Navigator.I.Push(new DuelBattleScreen(b));
            }
            catch (ApiError e) { Navigator.I.Toast(L.Error(e.Code), true); }
        }
    }

    public sealed class RankedScreen : ScreenBase
    {
        public override string TitleKey => "battle.ranked";
        private JObject? _me, _board;

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_me == null) return;
            var hero = W.Card(col, UiKit.White, 220);
            UiKit.Text(hero.transform, $"<size=60%>{(string)_me["season"]!["name"]!}</size>\n{L.T("ranked.rating", (int)_me["rating"]!)}\n<size=60%>{L.T("ranked.record", (int)_me["wins"]!, (int)_me["losses"]!, (int)_me["streak"]!)}</size>",
                44, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Stretch(20, 10, 20, 10);
            W.Btn(W.Row(col, 130), L.T("ranked.find_match"), FindMatch, UiKit.ButtonStyle.Gold, 40);
            W.Line(col, L.T("ranked.fair_play"), 24);
            W.Header(col, L.T("ranked.leaderboard"), 36);
            foreach (var e in _board?["entries"] ?? new JArray())
                W.Line(col, $"#{(int)e["rank"]!}  {(string)e["nickname"]!}  ·  {(int)e["rating"]!}  ·  {(int)e["wins"]!}W {(int)e["losses"]!}L", 30, (long)e["uid"]! == Session.Uid ? UiKit.Sapphire : UiKit.Ink);
        }

        private async void FindMatch()
        {
            var b = await Call(() => Api.Post<BattleDto>("/api/ranked/match", new { }, ApiClient.NewIdempotencyKey()));
            if (b != null) Nav.Push(new DuelBattleScreen(b));
        }

        public override async void OnShow()
        {
            _me = await Call(() => Api.Get<JObject>("/api/ranked"));
            _board = await Call(() => Api.Get<JObject>("/api/ranked/leaderboard"), overlay: false);
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class BattleHistoryScreen : ScreenBase
    {
        public override string TitleKey => "battle.history";
        private JArray? _rows;

        public override void Build()
        {
            var col = W.Column(this, Root);
            foreach (var b in _rows ?? new JArray())
            {
                bool won = (bool)b["won"]!;
                var c = W.Card(col, won ? UiKit.Mint : UiKit.White, 110);
                UiKit.Text(c.transform, $"{L.T("battle.mode." + (string)b["mode"]!)} · {(won ? L.T("duel.victory") : L.T("duel.defeat"))} · {L.T("duel.round", (int)b["round"]!)} · {W.Ago((System.DateTimeOffset)b["createdAt"]!)}", 30, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 0, 260, 0);
                if (won && (bool)b["attacker"]!)
                {
                    string id = (string)b["id"]!;
                    var s = UiKit.Button(c.transform, L.T("feed.share"), async () => { if (await Call(() => Api.Post<JObject>("/api/feed/posts", new { type = "duel", refId = id, caption = "" })) != null) Nav.Toast(L.T("feed.published")); }, UiKit.ButtonStyle.Secondary, 26);
                    ((RectTransform)s.transform).Place(new Vector2(1, 0.5f), new Vector2(220, 90), new Vector2(-10, 0), new Vector2(1, 0.5f));
                }
            }
        }

        public override async void OnShow()
        {
            _rows = await Call(() => Api.Get<JArray>("/api/battles/history"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    /// <summary>Moderator reports queue (visible only to moderator/gm/admin/owner; server enforces RBAC).</summary>
    public sealed class ModerationReportsScreen : ScreenBase
    {
        public override string TitleKey => "moderation.title";
        private JArray? _rows;

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_rows == null) return;
            if (_rows.Count == 0) W.Line(col, L.T("moderation.empty"), 32, null, 160);
            foreach (var r in _rows)
            {
                long id = (long)r["id"]!;
                var c = W.Card(col, null, 170);
                UiKit.Text(c.transform, $"#{id} · {(string)r["targetType"]!} {(long)r["targetId"]!} · {L.T("report.reason." + (string)r["reason"]!)}\n<size=80%>{(string?)r["details"] ?? ""}</size>", 28, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 10, 520, 10);
                var btns = UiKit.Rect(c.transform, "B"); btns.Place(new Vector2(1, 0.5f), new Vector2(500, 100), new Vector2(-10, 0), new Vector2(1, 0.5f));
                UiKit.HStack(btns, 10).childForceExpandWidth = true;
                foreach (var action in new[] { "dismiss", "hide", "restrict" })
                {
                    var act = action;
                    W.Btn(btns, L.T("moderation." + act), async () => { await Call(() => Api.Post<JObject>($"/api/admin/reports/{id}/resolve", new { action = act, reason = "moderation", restrictHours = 24 })); OnShow(); }, act == "dismiss" ? UiKit.ButtonStyle.Secondary : UiKit.ButtonStyle.Danger, 24);
                }
            }
        }

        public override async void OnShow()
        {
            _rows = await Call(() => Api.Get<JArray>("/api/admin/reports"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }
}
