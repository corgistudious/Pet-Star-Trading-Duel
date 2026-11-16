using System;
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
    public sealed class FeedScreen : ScreenBase
    {
        public override string TitleKey => "feed.title";
        public override bool ShowBackButton => false;
        private string _tab = "global";
        private JObject? _page;

        public override void Build()
        {
            var col = W.Column(this, Root, 18, hubNav: true);
            W.Tabs(col, new[] { ("global", L.T("feed.global")), ("following", L.T("feed.following")), ("friends", L.T("feed.friends")), ("featured", L.T("feed.featured")), ("mine", L.T("feed.mine")), ("saved", L.T("feed.saved")) },
                _tab, t => { _tab = t; _page = null; OnShow(); });
            var actions = W.Row(col, 100, 12);
            W.Btn(actions, L.T("feed.create"), () => Nav.Push(new CreatePostScreen()), UiKit.ButtonStyle.Gold);
            W.Btn(actions, L.T("feed.friends_btn"), () => Nav.Push(new FriendsScreen()));
            W.Btn(actions, L.T("lobby.notifications"), () => Nav.Push(new NotificationCenterScreen()));
            if (_page == null) { W.Line(col, L.T("common.loading")); }
            else if (!_page["items"]!.Any()) W.Line(col, L.T(_page["note"]?.Type == JTokenType.String ? "feed." + (string)_page["note"]! : "feed.empty"), 32, null, 200);
            else foreach (var p in _page["items"]!) PostView.Build(col, p, Wide, () => OnShow());
            HubNav.Build(Root, Mode, "social");
        }

        public override async void OnShow()
        {
            _page = await Call(() => Api.Get<JObject>($"/api/feed?tab={_tab}"), overlay: _page == null);
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    /// <summary>Renders one feed post: author, caption, real card snapshots (tap → public Passport), interactions.</summary>
    public static class PostView
    {
        public static void Build(Transform parent, JToken p, bool wide, System.Action refresh, bool detail = false)
        {
            var cards = p["cards"]!.ToList();
            float cw = wide ? 190 : 200;
            var panel = W.Card(parent, null, 230 + (cards.Count > 0 ? cw / CardView.Aspect + 20 : 0) + (detail ? 0 : 0));
            var col = UiKit.Rect(panel.transform, "Col").Stretch(28, 20, 28, 16);
            UiKit.VStack(col, 8, null, TextAnchor.UpperLeft);
            long authorUid = (long)p["authorUid"]!;
            var head = UiKit.Text(col, $"<b>{(string)p["authorNickname"]!}</b>  <size=75%><color=#5B6B8F>{W.Ago((DateTimeOffset)p["createdAt"]!)} · {L.T("feed.type." + (string)p["type"]!)}</color></size>", 32, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft);
            head.Size(-1, 50);
            head.gameObject.AddComponent<Button>().onClick.AddListener(() => Navigator.I.Push(new PlayerProfileScreen(authorUid)));
            head.raycastTarget = true;
            var caption = (string)p["caption"]!;
            if (caption.Length > 0) UiKit.Text(col, caption, 30, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.TopLeft).Size(-1, 60);
            if (cards.Count > 0)
            {
                var row = UiKit.Rect(col, "Cards"); row.Size(-1, cw / CardView.Aspect + 10);
                UiKit.HStack(row, 12, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
                foreach (var c in cards)
                {
                    var snap = W.Card(c["snapshot"]!);
                    var cv = CardView.Create(row, snap, cw, interactive: false);
                    cv.Size(cw, cw / CardView.Aspect);
                    string serial = (string)c["serial"]!;
                    cv.gameObject.AddComponent<Button>().onClick.AddListener(() => Navigator.I.Push(new PassportScreen(serial)));
                    if ((bool)c["ownerChanged"]!)
                    {
                        var tag = UiKit.Panel(cv.transform, UiKit.Ink, 16, "Owner");
                        ((RectTransform)tag.transform).Place(new Vector2(0.5f, 0), new Vector2(cw - 10, 44), new Vector2(0, 6), new Vector2(0.5f, 0));
                        UiKit.Text(tag.transform, L.T("feed.now_owned_by", (string)c["currentOwnerNickname"]!), 18, UiKit.White, UiKit.Weight.Bold).rectTransform.Stretch(4, 0, 4, 0);
                    }
                }
            }
            long id = (long)p["id"]!;
            bool liked = (bool)p["likedByMe"]!, saved = (bool)p["savedByMe"]!;
            var bar = W.Row(col, 80, 10);
            W.Btn(bar, (liked ? "♥ " : "♡ ") + (int)p["likes"]!, async () =>
            {
                try { if (liked) await App.Api.Delete<JObject>($"/api/feed/posts/{id}/like"); else await App.Api.Post<JObject>($"/api/feed/posts/{id}/like", null); refresh(); }
                catch (ApiError e) { Navigator.I.Toast(L.Error(e.Code), true); }
            }, liked ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 26);
            W.Btn(bar, L.T("feed.comments", (int)p["comments"]!), () => Navigator.I.Push(new PostDetailScreen(id)), UiKit.ButtonStyle.Secondary, 26);
            W.Btn(bar, saved ? L.T("feed.saved_btn") : L.T("feed.save"), async () =>
            {
                try { if (saved) await App.Api.Delete<JObject>($"/api/feed/posts/{id}/save"); else await App.Api.Post<JObject>($"/api/feed/posts/{id}/save", null); refresh(); }
                catch (ApiError e) { Navigator.I.Toast(L.Error(e.Code), true); }
            }, UiKit.ButtonStyle.Secondary, 26);
            if ((bool)p["isMine"]!)
                W.Btn(bar, L.T("common.delete"), async () => { try { await App.Api.Delete<JObject>($"/api/feed/posts/{id}"); refresh(); } catch (ApiError e) { Navigator.I.Toast(L.Error(e.Code), true); } }, UiKit.ButtonStyle.Ghost, 26);
            else
            {
                W.Btn(bar, L.T("feed.share"), async () => { try { await App.Api.Post<JObject>("/api/feed/posts", new { type = "share", refId = id.ToString() }); Navigator.I.Toast(L.T("feed.shared")); } catch (ApiError e) { Navigator.I.Toast(L.Error(e.Code), true); } }, UiKit.ButtonStyle.Secondary, 26);
                W.Btn(bar, L.T("report.button"), () => Report.Show("post", id), UiKit.ButtonStyle.Ghost, 26);
            }
        }
    }

    public sealed class CreatePostScreen : ScreenBase
    {
        public override string TitleKey => "feed.create";
        private TMP_InputField _caption = null!;
        private readonly List<CardDto> _cards = new();
        private readonly string? _presetType;
        private readonly string? _refId;

        public CreatePostScreen(CardDto? card = null, string? type = null, string? refId = null)
        {
            if (card != null) _cards.Add(card);
            _presetType = type; _refId = refId;
        }

        public override void Build()
        {
            var col = W.Column(this, Root);
            _caption = W.Input(col, L.T("feed.caption_hint"));
            _caption.Size(-1, 200);
            _caption.lineType = TMP_InputField.LineType.MultiLineNewline;
            _caption.characterLimit = 500;
            W.Btn(W.Row(col, 100), L.T("feed.pick_cards", _cards.Count), () => W.PickCards(5, cs => { _cards.Clear(); _cards.AddRange(cs); Rebuild(); }));
            if (_cards.Count > 0)
            {
                var row = UiKit.Rect(col, "Preview"); row.Size(-1, 360);
                UiKit.HStack(row, 12, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
                foreach (var c in _cards) CardView.Create(row, c, 190, interactive: false).Size(190, 338);
            }
            W.Line(col, L.T("feed.rules"), 24);
            W.Btn(W.Row(col, 120), L.T("feed.publish"), Publish, UiKit.ButtonStyle.Gold, 36);
        }

        private void Rebuild() { var t = _caption?.text; UiKit.Clear(Root); Build(); if (t != null) _caption.text = t; }

        private async void Publish()
        {
            string type = _presetType ?? (_cards.Count > 0 ? "card" : "text");
            var r = await Call(() => Api.Post<JObject>("/api/feed/posts", new { type, caption = _caption.text, cardIds = _cards.Select(c => c.Id).ToList(), refId = _refId }));
            if (r == null) return;
            Nav.Toast(L.T("feed.published"));
            Nav.Back();
        }
    }

    public sealed class PostDetailScreen : ScreenBase
    {
        public override string TitleKey => "feed.post";
        private readonly long _id;
        private JObject? _data;
        private TMP_InputField _comment = null!;
        public PostDetailScreen(long id) { _id = id; }

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_data == null) return;
            PostView.Build(col, _data["post"]!, Wide, OnShow, detail: true);
            W.Header(col, L.T("feed.comments_title"), 34);
            foreach (var c in _data["comments"]!)
            {
                var row = W.Card(col, UiKit.Sky, 120);
                UiKit.Text(row.transform, $"<b>{(string)c["authorNickname"]!}</b>  {(string)c["text"]!}", 28, UiKit.Ink, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 6, 230, 6);
                long cid = (long)c["id"]!;
                if ((bool)c["mine"]!)
                {
                    var del = UiKit.Button(row.transform, L.T("common.delete"), async () => { await Call(() => Api.Delete<JObject>($"/api/feed/comments/{cid}")); OnShow(); }, UiKit.ButtonStyle.Ghost, 24);
                    ((RectTransform)del.transform).Place(new Vector2(1, 0.5f), new Vector2(200, 80), new Vector2(-10, 0), new Vector2(1, 0.5f));
                }
                else
                {
                    var rep = UiKit.Button(row.transform, L.T("report.button"), () => Report.Show("comment", cid), UiKit.ButtonStyle.Ghost, 24);
                    ((RectTransform)rep.transform).Place(new Vector2(1, 0.5f), new Vector2(200, 80), new Vector2(-10, 0), new Vector2(1, 0.5f));
                }
            }
            _comment = W.Input(col, L.T("feed.comment_hint"));
            _comment.characterLimit = 300;
            W.Btn(W.Row(col, 110), L.T("feed.send"), async () =>
            {
                if (await Call(() => Api.Post<JObject>($"/api/feed/posts/{_id}/comments", new { text = _comment.text })) != null) OnShow();
            }, UiKit.ButtonStyle.Primary);
        }

        public override async void OnShow()
        {
            _data = await Call(() => Api.Get<JObject>($"/api/feed/posts/{_id}"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class FriendsScreen : ScreenBase
    {
        public override string TitleKey => "friends.title";
        private JArray? _friends, _results;
        private TMP_InputField _q = null!;

        public override void Build()
        {
            var col = W.Column(this, Root);
            var search = W.Row(col, 110);
            _q = UiKit.Input(search, L.T("friends.search_hint")); _q.Size(-1, 110, 3);
            W.Btn(search, L.T("market.search"), Search, UiKit.ButtonStyle.Primary);
            foreach (var r in _results ?? new JArray())
            {
                long uid = (long)r["uid"]!;
                var c = W.Card(col, UiKit.Sky, 110);
                UiKit.Text(c.transform, $"{(string)r["nickname"]!} · UID {uid} · {L.T("lobby.level", (int)r["level"]!)}", 30, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 0, 24, 0);
                c.gameObject.AddComponent<Button>().onClick.AddListener(() => Nav.Push(new PlayerProfileScreen(uid)));
            }
            W.Header(col, L.T("friends.list"), 36);
            if (_friends == null || _friends.Count == 0) W.Line(col, L.T("friends.empty"));
            foreach (var f in _friends ?? new JArray())
            {
                long uid = (long)f["uid"]!; long reqId = (long)f["requestId"]!; string status = (string)f["status"]!;
                var c = W.Card(col, null, 120);
                UiKit.Text(c.transform, $"{(string)f["nickname"]!} · {L.T("friends.status." + status)}", 30, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).rectTransform.Stretch(24, 0, 420, 0);
                var btns = UiKit.Rect(c.transform, "B"); btns.Place(new Vector2(1, 0.5f), new Vector2(400, 90), new Vector2(-10, 0), new Vector2(1, 0.5f));
                UiKit.HStack(btns, 10).childForceExpandWidth = true;
                if (status == "incoming") W.Btn(btns, L.T("friends.accept"), async () => { await Call(() => Api.Post<JObject>($"/api/friends/requests/{reqId}/accept", null)); OnShow(); }, UiKit.ButtonStyle.Primary, 26);
                if (status == "friends") W.Btn(btns, L.T("friends.duel"), () => BattleHubScreen.FriendDuel(uid), UiKit.ButtonStyle.Gold, 26);
                W.Btn(btns, L.T("friends.profile"), () => Nav.Push(new PlayerProfileScreen(uid)), UiKit.ButtonStyle.Secondary, 26);
            }
        }

        private async void Search()
        {
            var q = _q.text.Trim();
            if (q.Length < 2) return;
            _results = await Call(() => Api.Get<JArray>("/api/players/search?q=" + System.Uri.EscapeDataString(q)));
            UiKit.Clear(Root); Build(); _q.text = q;
        }

        public override async void OnShow()
        {
            _friends = await Call(() => Api.Get<JArray>("/api/friends"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }

    public sealed class PlayerProfileScreen : ScreenBase
    {
        public override string TitleKey => "profile.player";
        private readonly long _uid;
        private JObject? _p;
        public PlayerProfileScreen(long uid) { _uid = uid; }

        public override void Build()
        {
            var col = W.Column(this, Root);
            if (_p == null) return;
            W.Header(col, $"{(string)_p["nickname"]!}  <size=60%><color=#5B6B8F>UID {_uid}</color></size>", 54);
            W.Line(col, $"{L.T("lobby.level", (int)_p["level"]!)} · {L.T("profile.codex", (int)_p["codexDiscovered"]!, (int)_p["codexTotal"]!)} · {L.T("profile.achievements", (int)_p["achievements"]!)}", 30, UiKit.Ink);
            W.Line(col, L.T("profile.follow_counts", (int)_p["followers"]!, (int)_p["following"]!), 28);
            var showcase = _p["showcase"]!.ToList();
            W.Header(col, L.T("profile.showcase"), 34);
            if (showcase.Count == 0) W.Line(col, L.T("profile.showcase_empty"));
            else
            {
                var row = UiKit.Rect(col, "Showcase"); row.Size(-1, 360);
                UiKit.HStack(row, 12, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
                foreach (var c in showcase)
                {
                    var card = W.Card(c);
                    var cv = CardView.Create(row, card, 190, interactive: false); cv.Size(190, 338);
                    cv.gameObject.AddComponent<Button>().onClick.AddListener(() => Nav.Push(new PassportScreen(card.Serial)));
                }
            }
            if ((bool)_p["isMe"]!)
            {
                W.Btn(W.Row(col, 110), L.T("profile.edit_showcase"), () => W.PickCards(5, async cs =>
                {
                    if (await Call(() => Api.Put<JObject>("/api/profile/showcase", new { cardIds = cs.Select(c => c.Id).ToList() })) != null) OnShow();
                }), UiKit.ButtonStyle.Gold);
                return;
            }
            var r1 = W.Row(col, 110);
            bool following = (bool)_p["isFollowing"]!;
            W.Btn(r1, following ? L.T("profile.unfollow") : L.T("profile.follow"), () => Act(following ? "DELETE" : "POST", $"/api/players/{_uid}/follow"), following ? UiKit.ButtonStyle.Secondary : UiKit.ButtonStyle.Primary);
            string fr = (string)_p["friendship"]!;
            if (fr == "none") W.Btn(r1, L.T("profile.add_friend"), async () => { await Call(() => Api.Post<JObject>("/api/friends/requests", new { uid = _uid })); OnShow(); });
            else W.Btn(r1, L.T("friends.status." + (fr == "friends" ? "friends" : fr == "request_sent" ? "outgoing" : "incoming")), () => Nav.Push(new FriendsScreen()));
            var r2 = W.Row(col, 100);
            bool blocked = (bool)_p["blockedByMe"]!;
            W.Btn(r2, blocked ? L.T("profile.unblock") : L.T("profile.block"), () => Act(blocked ? "DELETE" : "POST", $"/api/players/{_uid}/block"), UiKit.ButtonStyle.Danger, 28);
            W.Btn(r2, L.T("profile.mute"), () => Act("POST", $"/api/players/{_uid}/mute"), UiKit.ButtonStyle.Secondary, 28);
            W.Btn(r2, L.T("report.button"), () => Report.Show("user", _uid), UiKit.ButtonStyle.Ghost, 28);
        }

        private async void Act(string method, string path)
        {
            if (await Call(() => method == "DELETE" ? Api.Delete<JObject>(path) : Api.Post<JObject>(path, null)) != null) OnShow();
        }

        public override async void OnShow()
        {
            _p = await Call(() => Api.Get<JObject>($"/api/players/{_uid}"));
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }
    }
}
