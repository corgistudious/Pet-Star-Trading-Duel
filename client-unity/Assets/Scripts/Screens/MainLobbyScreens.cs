using System;
using System.Collections;
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
    /// <summary>Procedural sprites for the new lobby (pointy-top hexagon), so no third-party UI art is needed.</summary>
    public static class LobbyArt
    {
        private static Sprite? _hex;
        public static Sprite Hex => _hex ??= MakeHex(256, 282);

        private static Sprite MakeHex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pts = new[] { new Vector2(0.5f, 1f), new Vector2(0.96f, 0.75f), new Vector2(0.96f, 0.25f), new Vector2(0.5f, 0f), new Vector2(0.04f, 0.25f), new Vector2(0.04f, 0.75f) }
                .Select(p => new Vector2(p.x * w, p.y * h)).ToArray();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // signed distance to the polygon edge, 1.5 px anti-aliasing
                    float d = float.MaxValue; bool inside = true;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                        var e = b - a; var n = new Vector2(e.y, -e.x).normalized;
                        float s = Vector2.Dot(new Vector2(x + 0.5f, y + 0.5f) - a, n);
                        if (s > 0) inside = false;
                        d = Mathf.Min(d, Mathf.Abs(s));
                    }
                    float alpha = inside ? Mathf.Clamp01(d / 1.5f) : 0f;
                    tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100);
        }

        public static async void Load(Image img, string address)
        {
            var s = await GameDataClient.LoadSprite(address);
            if (img != null && s != null) { img.sprite = s; img.color = Color.white; img.preserveAspect = true; }
        }
    }

    /// <summary>First login: choose the male or female chibi Trainer (saved on the server, once).</summary>
    public sealed class TrainerSelectScreen : ScreenBase
    {
        public override bool ShowBackButton => false;
        public override bool ShowCurrencyBar => false;
        public override string TitleKey => "trainer.choose";
        private string _pick = "trainer_boy";

        public override void Build()
        {
            var bg = UiKit.Icon(Root, null, Vector2.zero, "Bg"); ((RectTransform)bg.transform).Stretch(); bg.preserveAspect = false; LobbyArt.Load(bg, "ui/bg_lobby_main"); bg.preserveAspect = false;
            var row = UiKit.Rect(Root, "Row"); row.Place(new Vector2(0.5f, 0.55f), Wide ? new Vector2(1300, 760) : new Vector2(1000, 1100), Vector2.zero);
            UiKit.HStack(row, 40).childForceExpandWidth = true;
            foreach (var id in new[] { "trainer_boy", "trainer_girl" })
            {
                var card = UiKit.Panel(row, _pick == id ? UiKit.Hex("#FFF8E1") : UiKit.White, 40, id, shadow: true); card.Size(-1, -1, 1, 1);
                var art = UiKit.Icon(card.transform, null, Vector2.zero, "Art"); ((RectTransform)art.transform).Stretch(20, 20, 20, 120);
                LobbyArt.Load(art, "ui/" + id + (_pick == id ? "_happy" : ""));
                UiKit.Text(card.transform, L.T(id == "trainer_boy" ? "trainer.boy" : "trainer.girl"), 40, UiKit.Ink, UiKit.Weight.ExtraBold).rectTransform.Place(new Vector2(0.5f, 0), new Vector2(560, 90), new Vector2(0, 20), new Vector2(0.5f, 0));
                var pick = id;
                var b = card.gameObject.AddComponent<Button>(); b.onClick.AddListener(() => { _pick = pick; UiKit.Clear(Root); Build(); });
            }
            var go = UiKit.Button(Root, L.T("trainer.confirm"), Confirm, UiKit.ButtonStyle.Gold, 40);
            ((RectTransform)go.transform).Place(new Vector2(0.5f, 0), new Vector2(520, 120), new Vector2(0, 40), new Vector2(0.5f, 0));
            UiKit.Text(Root, L.T("trainer.locked_note"), 26, Color.white).rectTransform.Place(new Vector2(0.5f, 0), new Vector2(1400, 50), new Vector2(0, 170), new Vector2(0.5f, 0));
        }

        private async void Confirm()
        {
            var r = await Call(() => Api.Post<JObject>("/api/trainer", new { characterId = _pick }));
            if (r != null) Nav.Replace(new MainLobbyScreen());
        }
    }

    /// <summary>
    /// Main lobby (landscape layout from the owner's reference; original Pet Star art): profile header, both currencies,
    /// Messages / Friends, the chosen Trainer on a podium (idle, blink, happy on tap) with the companion Pet, four
    /// hexagon hubs (Pet Plaza, Mini Games, Pet World, Battle 3v3) and the utility bar.
    /// </summary>
    public sealed class MainLobbyScreen : ScreenBase
    {
        public override bool ShowBackButton => false;
        private string _trainer = "trainer_boy";
        private string? _companionForm;
        private Image? _trainerImg;
        private Runner? _run;
        private bool _loaded;

        public override void Build()
        {
            _run = Root.gameObject.GetComponent<Runner>() ?? Root.gameObject.AddComponent<Runner>();
            var bg = UiKit.Icon(Root, null, Vector2.zero, "Bg"); ((RectTransform)bg.transform).Stretch(); LobbyArt.Load(bg, "ui/bg_lobby_main"); bg.preserveAspect = false;
            var p = Session.Player;

            // profile header (avatar = Trainer face, name, UID, level)
            var head = UiKit.Panel(Root, new Color(1, 1, 1, 0.92f), 40, "Profile", shadow: true);
            ((RectTransform)head.transform).Place(new Vector2(0, 1), new Vector2(620, 130), new Vector2(Wide ? 40 : 30, Wide ? -24 : -170));
            var mask = UiKit.Panel(head.transform, UiKit.Gold, 24, "AvatarMask"); ((RectTransform)mask.transform).Place(new Vector2(0, 0.5f), new Vector2(110, 110), new Vector2(10, 0), new Vector2(0, 0.5f));
            mask.gameObject.AddComponent<Mask>();
            var face = UiKit.Icon(mask.transform, null, new Vector2(300, 450), "Face"); ((RectTransform)face.transform).Place(new Vector2(0.5f, 1), new Vector2(300, 450), new Vector2(0, 40), new Vector2(0.5f, 1));
            LobbyArt.Load(face, "ui/" + _trainer);
            UiKit.Text(head.transform, $"{p?.Nickname}\n<size=70%><color=#5B6B8F>UID {p?.Uid} · {L.T("lobby.level", p?.Level ?? 1)}</color></size>", 36, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft)
                .rectTransform.Stretch(140, 0, 20, 0);
            head.gameObject.AddComponent<Button>().onClick.AddListener(() => Nav.Push(new ProfileScreen()));

            // left menu: Messages, Friends
            var left = UiKit.Rect(Root, "Left"); left.Place(new Vector2(0, 0.62f), new Vector2(170, 420), new Vector2(40, 0), new Vector2(0, 0.5f));
            UiKit.VStack(left, 30);
            IconButton(left, "ui/ui_icon_mail", L.T("lobby.messages"), () => Nav.Push(new MailboxScreen()), 150);
            IconButton(left, "ui/ui_icon_friends", L.T("lobby.friends"), () => Nav.Push(new FriendsScreen()), 150);

            // stage: podium, Trainer, companion Pet
            var stage = UiKit.Rect(Root, "Stage");
            if (Wide) stage.Place(new Vector2(0.3f, 0), new Vector2(900, 860), new Vector2(0, 30), new Vector2(0.5f, 0));
            else stage.Place(new Vector2(0.5f, 0.42f), new Vector2(900, 900), Vector2.zero);
            var podium = UiKit.Icon(stage, null, new Vector2(760, 500), "Podium"); ((RectTransform)podium.transform).Place(new Vector2(0.45f, 0), new Vector2(760, 500), new Vector2(0, -80), new Vector2(0.5f, 0));
            LobbyArt.Load(podium, "ui/ui_podium");
            _trainerImg = UiKit.Icon(stage, null, new Vector2(460, 690), "Trainer"); ((RectTransform)_trainerImg.transform).Place(new Vector2(0.4f, 0), new Vector2(460, 690), new Vector2(0, 150), new Vector2(0.5f, 0));
            LobbyArt.Load(_trainerImg, "ui/" + _trainer);
            _trainerImg.gameObject.AddComponent<FloatIdle>();
            _trainerImg.gameObject.AddComponent<Button>().onClick.AddListener(Happy);
            if (_companionForm != null)
            {
                var pet = UiKit.Icon(stage, null, new Vector2(240, 240), "Companion"); ((RectTransform)pet.transform).Place(new Vector2(0.72f, 0), new Vector2(240, 240), new Vector2(0, 170), new Vector2(0.5f, 0));
                LobbyArt.Load(pet, "pet/" + _companionForm);
                pet.gameObject.AddComponent<FloatIdle>();
                pet.gameObject.AddComponent<Button>().onClick.AddListener(() => Nav.Push(new CollectionHubScreen()));
            }

            // four hexagon hubs (2 + 2 staggered, Battle is the largest)
            var hexes = UiKit.Rect(Root, "Hexes");
            if (Wide) hexes.Place(new Vector2(1, 0.55f), new Vector2(900, 700), new Vector2(-40, 0), new Vector2(1, 0.5f));
            else hexes.Place(new Vector2(0.5f, 0), new Vector2(1000, 760), new Vector2(0, 250), new Vector2(0.5f, 0));
            HexButton(hexes, L.T("lobby.hex_plaza"), UiKit.Hex("#8F63E8"), new Vector2(0.24f, 0.75f), 330, "pet_bloomhare", () => Nav.Push(new FeedScreen()));
            HexButton(hexes, L.T("lobby.hex_games"), UiKit.Hex("#3D7CF0"), new Vector2(0.62f, 0.75f), 330, "pet_floatlet", () => Nav.Toast(L.T("lobby.games_preview")));
            HexButton(hexes, L.T("lobby.hex_world"), UiKit.Hex("#19B9A6"), new Vector2(0.43f, 0.27f), 330, "pet_dandefluff", () => Nav.Push(new PetWorldScreen()));
            HexButton(hexes, L.T("lobby.hex_battle"), UiKit.Hex("#F5B521"), new Vector2(0.8f, 0.3f), 380, "pet_pyrelynx", () => Nav.Push(new TeamHubScreen()));

            // utility bar
            var bar = UiKit.Rect(Root, "Bar");
            bar.Place(new Vector2(1, 0), new Vector2(Wide ? 1100 : 1040, 170), new Vector2(-30, 14), new Vector2(1, 0));
            UiKit.HStack(bar, 10).childForceExpandWidth = true;
            IconButton(bar, "ui/ui_icon_starter_gift", L.T("lobby.starter_gift"), () => Nav.Push(new RewardsScreen()), 120);
            IconButton(bar, "ui/ui_icon_center", L.T("lobby.center"), () => Nav.Push(new NotificationCenterScreen()), 120);
            IconButton(bar, "ui/ui_icon_gifts", L.T("lobby.gifts"), () => Nav.Push(new RewardsScreen()), 120);
            IconButton(bar, "ui/ui_icon_season", L.T("lobby.season"), () => Nav.Toast(L.T("lobby.season_preview")), 120);
            IconButton(bar, "ui/ui_icon_events", L.T("lobby.events"), () => Nav.Push(new MissionsScreen()), 120);
            IconButton(bar, "ui/ui_icon_shop", L.T("lobby.shop"), () => Nav.Push(new ShopScreen(ShopScreen.Tab.Packs)), 120);

            var settings = UiKit.Button(Root, "⚙", () => Nav.Push(new SettingsScreen()), UiKit.ButtonStyle.Ghost, 44);
            ((RectTransform)settings.transform).Place(new Vector2(1, 1), new Vector2(110, 110), new Vector2(-30, Wide ? -150 : -300), new Vector2(1, 1));
            if (_loaded && _run != null) _run.StartCoroutine(Blink());
        }

        public override async void OnShow()
        {
            var t = await Call(() => Api.Get<JObject>("/api/trainer"), overlay: false);
            if (t == null) return;
            if (t["chosen"]?.Value<bool>() != true) { Nav.Replace(new TrainerSelectScreen()); return; }
            _trainer = t["characterId"]?.ToString() ?? "trainer_boy";
            _companionForm = t["companion"]?["formId"]?.ToString();
            _loaded = true;
            if (Root != null) { UiKit.Clear(Root); Build(); }
        }

        private void Happy()
        {
            if (_trainerImg == null || _run == null) return;
            _run.StartCoroutine(Frame("_happy", 1.2f));
        }

        private IEnumerator Frame(string suffix, float seconds)
        {
            if (_trainerImg == null) yield break;
            LobbyArt.Load(_trainerImg, "ui/" + _trainer + suffix);
            yield return new WaitForSeconds(seconds);
            if (_trainerImg != null) LobbyArt.Load(_trainerImg, "ui/" + _trainer);
        }

        private IEnumerator Blink()
        {
            while (Root != null && _trainerImg != null)
            {
                yield return new WaitForSeconds(UnityEngine.Random.Range(2.6f, 5f));
                yield return Frame("_blink", 0.16f);
            }
        }

        private static void IconButton(RectTransform parent, string icon, string label, Action onClick, float size)
        {
            var cell = UiKit.Rect(parent, label); cell.Size(-1, size + 50, 1);
            var img = UiKit.Icon(cell, null, new Vector2(size, size), "Icon"); ((RectTransform)img.transform).Place(new Vector2(0.5f, 1), new Vector2(size, size), Vector2.zero, new Vector2(0.5f, 1));
            LobbyArt.Load(img, icon);
            var t = UiKit.Text(cell, label, 26, Color.white, UiKit.Weight.ExtraBold); t.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(size + 60, 44), Vector2.zero, new Vector2(0.5f, 0));
            t.outlineWidth = 0.25f; t.outlineColor = new Color32(0, 0, 0, 200);
            cell.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            cell.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick());
        }

        private static void HexButton(RectTransform parent, string label, Color color, Vector2 anchor, float size, string petForm, Action onClick)
        {
            var rim = UiKit.Icon(parent, LobbyArt.Hex, new Vector2(size, size * 1.1f), label); rim.color = Color.Lerp(color, Color.white, 0.55f);
            ((RectTransform)rim.transform).Place(anchor, new Vector2(size, size * 1.1f), Vector2.zero);
            var body = UiKit.Icon(rim.transform, LobbyArt.Hex, Vector2.zero, "Body"); body.color = color; ((RectTransform)body.transform).Stretch(10, 10, 10, 10);
            var art = UiKit.Icon(body.transform, null, Vector2.zero, "Art"); ((RectTransform)art.transform).Stretch(size * 0.15f, size * 0.1f, size * 0.15f, size * 0.32f);
            LobbyArt.Load(art, "pet/" + petForm);
            var t = UiKit.Text(body.transform, label, size * 0.11f, Color.white, UiKit.Weight.ExtraBold); t.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(size, size * 0.2f), new Vector2(0, size * 0.14f), new Vector2(0.5f, 0));
            t.outlineWidth = 0.2f; t.outlineColor = new Color32(0, 0, 0, 160);
            var b = rim.gameObject.AddComponent<Button>(); b.onClick.AddListener(() => onClick());
            b.transition = Selectable.Transition.ColorTint;
        }

        private sealed class Runner : MonoBehaviour { }
    }

    /// <summary>Pet World hub: links to the systems the player already has (album, packs, team, evolution, missions,
    /// trading, rankings, friends, events).</summary>
    public sealed class PetWorldScreen : ScreenBase
    {
        public override string TitleKey => "lobby.hex_world";
        public override void Build()
        {
            var list = UiKit.Scroll(UiKit.Rect(Root, "List").Stretch(40, Wide ? 140 : 320, 40, 40));
            UiKit.VStack(list, 16, new RectOffset(0, 0, 10, 40));
            foreach (var (key, open) in new (string, Action)[]
            {
                ("world.album", () => Nav.Push(new CollectionHubScreen())), ("world.packs", () => Nav.Push(new ShopScreen(ShopScreen.Tab.Packs))),
                ("world.team", () => Nav.Push(new TeamHubScreen())), ("world.evolution", () => Nav.Push(new CollectionHubScreen())),
                ("world.missions", () => Nav.Push(new MissionsScreen())), ("world.trade", () => Nav.Push(new MarketHomeScreen())),
                ("world.ranking", () => Nav.Push(new RankedScreen())), ("world.friends", () => Nav.Push(new FriendsScreen())),
                ("world.events", () => Nav.Push(new RewardsScreen())),
            })
                UiKit.Button(list, L.T(key), () => open(), UiKit.ButtonStyle.Secondary, 38).Size(-1, 130);
        }
    }
}
