using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    public sealed class LobbyScreen : ScreenBase
    {
        public override bool ShowBackButton => false;

        public override void Build()
        {
            var p = Session.Player;
            // header: player card
            var head = UiKit.Panel(Root, UiKit.White, 40, "PlayerHeader", shadow: true);
            var hrt = (RectTransform)head.transform;
            if (Wide) hrt.Place(new Vector2(0, 1), new Vector2(720, 120), new Vector2(30, -30));
            else hrt.Place(new Vector2(0.5f, 1), new Vector2(1000, 130), new Vector2(0, -50));
            var name = UiKit.Text(head.transform, p == null ? "" : $"{p.Nickname}  <size=70%><color=#5B6B8F>UID {p.Uid} · {p.ServerCode}</color></size>", 42, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft);
            name.rectTransform.Stretch(40, 0, 200, 0);
            var lv = UiKit.Text(head.transform, L.T("lobby.level", p?.Level ?? 1), 36, UiKit.Sapphire, UiKit.Weight.Bold, TextAlignmentOptions.MidlineRight);
            lv.rectTransform.Stretch(0, 0, 40, 0);

            // featured pack (center stage)
            var stage = UiKit.Rect(Root, "Featured");
            if (Wide) stage.Place(new Vector2(0.36f, 0.45f), new Vector2(900, 780), Vector2.zero);
            else stage.Place(new Vector2(0.5f, 0.6f), new Vector2(900, 820), Vector2.zero);
            var featured = UiKit.Panel(stage, UiKit.White, 56, "FeaturedCard", shadow: true);
            ((RectTransform)featured.transform).Stretch();
            var pack = PackVisual.Create(featured.transform, "basic_pack", Wide ? 360 : 380);
            ((RectTransform)pack.transform).Place(new Vector2(0.5f, 0.58f), ((RectTransform)pack.transform).sizeDelta, Vector2.zero);
            pack.gameObject.AddComponent<FloatIdle>();
            var owned = p != null && p.Packs.TryGetValue("basic_pack", out var n) ? n : 0;
            var cta = UiKit.Button(featured.transform, owned > 0 ? L.T("lobby.open_pack", owned) : L.T("lobby.go_shop"),
                () => { if (owned > 0) Nav.Push(new PackOpeningScreen("basic_pack")); else Nav.Push(new ShopScreen(ShopScreen.Tab.Packs)); }, UiKit.ButtonStyle.Gold, 44);
            ((RectTransform)cta.transform).Place(new Vector2(0.5f, 0), new Vector2(560, 130), new Vector2(0, 50));

            // navigation tiles
            var nav = UiKit.Rect(Root, "Nav");
            if (Wide) { nav.Place(new Vector2(1, 0.45f), new Vector2(760, 780), new Vector2(-40, 0), new Vector2(1, 0.5f)); }
            else nav.Place(new Vector2(0.5f, 0), new Vector2(1000, 560), new Vector2(0, 60), new Vector2(0.5f, 0));
            var grid = nav.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = Wide ? new Vector2(360, 180) : new Vector2(320, 170);
            grid.spacing = new Vector2(Wide ? 30 : 20, Wide ? 30 : 25);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = Wide ? 2 : 3;
            Tile(nav, "lobby.shop", UiKit.Peach, () => Nav.Push(new ShopScreen(ShopScreen.Tab.Packs)));
            Tile(nav, "lobby.pet_home", UiKit.Mint, () => Nav.Push(new PetHomeScreen()));
            Tile(nav, "lobby.duel", UiKit.Lilac, () => Nav.Push(new DuelLobbyScreen()));
            Tile(nav, "lobby.codex", UiKit.SkyDeep, () => Nav.Push(new CodexScreen()));
            Tile(nav, "lobby.profile", UiKit.Hex("#FFF1C9"), () => Nav.Push(new ProfileScreen()));
            Tile(nav, "lobby.settings", UiKit.Hex("#E8ECF5"), () => Nav.Push(new SettingsScreen()));
        }

        private static void Tile(RectTransform parent, string key, Color color, System.Action onClick)
        {
            var b = UiKit.Button(parent, L.T(key), onClick, UiKit.ButtonStyle.Secondary, 40, 40);
            b.GetComponent<Image>().color = color;
        }

        public override async void OnShow()
        {
            var p = await Call(() => Api.Get<PlayerDto>("/api/player"), overlay: false);
            if (p != null) Session.SetPlayer(p);
            // recover pack openings whose animation was interrupted (connection loss / app kill)
            var pending = await Call(() => Api.Get<System.Collections.Generic.List<PackOpeningDto>>("/api/packs/openings?unacknowledged=true&limit=1"), overlay: false);
            if (pending != null && pending.Count > 0) Nav.Push(new PackOpeningScreen(pending[0]));
        }
    }

    public sealed class SettingsScreen : ScreenBase
    {
        public override string TitleKey => "lobby.settings";

        public override void Build()
        {
            var panel = UiKit.Panel(Root, UiKit.White, 48, "Settings", shadow: true);
            ((RectTransform)panel.transform).Place(new Vector2(0.5f, 0.45f), Wide ? new Vector2(1000, 760) : new Vector2(960, 1100), Vector2.zero);
            var col = UiKit.Rect(panel.transform, "Col").Stretch(60, 60, 60, 60);
            UiKit.VStack(col, 30);
            UiKit.Text(col, L.T("settings.language"), 40, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 70);
            var row = UiKit.Rect(col, "LangRow"); row.Size(-1, 120);
            UiKit.HStack(row, 20).childForceExpandWidth = true;
            UiKit.Button(row, "English", () => SetLang("en"), L.Language == "en" ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary).Size(-1, 110, 1);
            UiKit.Button(row, "Tiếng Việt", () => SetLang("vi"), L.Language == "vi" ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary).Size(-1, 110, 1);
            UiKit.Text(col, L.T("settings.sfx"), 40, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft).Size(-1, 70);
            UiKit.Button(col, Audio.Volume > 0 ? L.T("settings.on") : L.T("settings.off"), () =>
            {
                Audio.Volume = Audio.Volume > 0 ? 0 : 0.8f; PlayerPrefs.SetFloat("settings.sfx", Audio.Volume); Nav.Replace(new SettingsScreen());
            }, UiKit.ButtonStyle.Secondary).Size(-1, 110);
            UiKit.Button(col, L.T("settings.switch_server"), () => Nav.Replace(new ServerSelectScreen()), UiKit.ButtonStyle.Secondary).Size(-1, 110);
            UiKit.Button(col, L.T("settings.logout"), Logout, UiKit.ButtonStyle.Danger).Size(-1, 110);
        }

        private void SetLang(string code)
        {
            L.SetLanguage(code);
            _ = Api.Patch<ProfileDto>("/api/me", new UpdateProfileRequest { Language = code });
            Nav.Replace(new SettingsScreen());
        }

        private async void Logout()
        {
            await Call(() => Api.Post<Newtonsoft.Json.Linq.JObject>("/api/auth/logout", null));
            Session.Logout();
            Nav.Replace(new LoginScreen());
        }
    }

    /// <summary>Gentle idle floating for featured items.</summary>
    public sealed class FloatIdle : MonoBehaviour
    {
        private Vector2 _base; private float _t;
        private void Start() { _base = ((RectTransform)transform).anchoredPosition; _t = Random.value * 6f; }
        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            ((RectTransform)transform).anchoredPosition = _base + new Vector2(0, Mathf.Sin(_t * 1.6f) * 14f);
            transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_t * 1.1f) * 2f);
        }
    }
}
