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
            if (Wide) stage.Place(new Vector2(0.45f, 0.47f), new Vector2(860, 760), Vector2.zero);
            else stage.Place(new Vector2(0.5f, 0.58f), new Vector2(900, 820), Vector2.zero);
            var featured = UiKit.Panel(stage, UiKit.White, 56, "FeaturedCard", shadow: true);
            ((RectTransform)featured.transform).Stretch();
            var pack = PackVisual.Create(featured.transform, "basic_pack", Wide ? 360 : 380);
            ((RectTransform)pack.transform).Place(new Vector2(0.5f, 0.58f), ((RectTransform)pack.transform).sizeDelta, Vector2.zero);
            pack.gameObject.AddComponent<FloatIdle>();
            var owned = p != null && p.Packs.TryGetValue("basic_pack", out var n) ? n : 0;
            var cta = UiKit.Button(featured.transform, owned > 0 ? L.T("lobby.open_pack", owned) : L.T("lobby.go_shop"),
                () => { if (owned > 0) Nav.Push(new PackOpeningScreen("basic_pack")); else Nav.Push(new ShopScreen(ShopScreen.Tab.Packs)); }, UiKit.ButtonStyle.Gold, 44);
            ((RectTransform)cta.transform).Place(new Vector2(0.5f, 0), new Vector2(560, 130), new Vector2(0, 50));

            // quick actions (notifications, mailbox, settings) + bottom Game Hub navigation
            var quick = UiKit.Rect(Root, "Quick");
            if (Wide) quick.Place(new Vector2(1, 0.5f), new Vector2(560, 520), new Vector2(-40, 0), new Vector2(1, 0.5f));
            else quick.Place(new Vector2(0.5f, 0), new Vector2(1000, 130), new Vector2(0, 200), new Vector2(0.5f, 0));
            var ql = Wide ? (HorizontalOrVerticalLayoutGroup)UiKit.VStack(quick, 24) : UiKit.HStack(quick, 20);
            ql.childForceExpandWidth = true;
            Tile(quick, "lobby.notifications", UiKit.SkyDeep, () => Nav.Push(new NotificationCenterScreen()));
            Tile(quick, "lobby.mailbox", UiKit.Mint, () => Nav.Push(new MailboxScreen()));
            Tile(quick, "lobby.profile", UiKit.Hex("#FFF1C9"), () => Nav.Push(new ProfileScreen()));
            Tile(quick, "lobby.settings", UiKit.Hex("#E8ECF5"), () => Nav.Push(new SettingsScreen()));
            HubNav.Build(Root, Mode, "home");
        }

        public static void Tile(RectTransform parent, string key, Color color, System.Action onClick)
        {
            var b = UiKit.Button(parent, L.T(key), onClick, UiKit.ButtonStyle.Secondary, 36, 40);
            b.GetComponent<Image>().color = color;
            b.Size(-1, 120, 1);
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
            UiKit.Button(col, L.T("moderation.title"), () => Nav.Push(new ModerationReportsScreen()), UiKit.ButtonStyle.Ghost, 30).Size(-1, 90); // server enforces RBAC
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
