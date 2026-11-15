using System.Collections;
using System.Collections.Generic;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    public sealed class SplashScreen : ScreenBase
    {
        public override bool ShowCurrencyBar => false;
        public override bool ShowBackButton => false;

        public override void Build()
        {
            var logo = UiKit.Text(Root, "Pet Star", Wide ? 150 : 170, UiKit.Sapphire, UiKit.Weight.ExtraBold);
            logo.rectTransform.Place(new Vector2(0.5f, 0.56f), new Vector2(1200, 220), Vector2.zero);
            var sub = UiKit.Text(Root, "TRADING DUEL", Wide ? 54 : 60, UiKit.GoldDeep, UiKit.Weight.ExtraBold);
            sub.characterSpacing = 18;
            sub.rectTransform.Place(new Vector2(0.5f, 0.56f), new Vector2(1200, 100), new Vector2(0, -150));
            var star = UiKit.Text(Root, "★", 200, UiKit.Gold, UiKit.Weight.ExtraBold);
            star.rectTransform.Place(new Vector2(0.5f, 0.56f), new Vector2(300, 300), new Vector2(0, 230));
            star.gameObject.AddComponent<Spin>();
        }

        public override async void OnShow()
        {
            await System.Threading.Tasks.Task.Delay(900);
            if (Session.HasStoredLogin && await Session.TryRefresh(Api))
            {
                // resume the last server directly when possible
                var last = Session.LastServer;
                if (last.HasValue)
                {
                    try
                    {
                        Session.Apply(await Api.Post<AuthResponse>($"/api/servers/{last.Value}/enter", new EnterServerRequest()));
                        Session.SetPlayer(await Api.Get<PlayerDto>("/api/player"));
                        Nav.Replace(new LobbyScreen());
                        return;
                    }
                    catch (ApiError) { }
                }
                Nav.Replace(new ServerSelectScreen());
                return;
            }
            Nav.Replace(new LoginScreen());
        }
    }

    public sealed class LoginScreen : ScreenBase
    {
        public override bool ShowCurrencyBar => false;
        public override bool ShowBackButton => false;
        private TMP_InputField _email = null!, _password = null!;

        public override void Build()
        {
            var card = AuthCard(Root, Wide);
            var col = UiKit.Rect(card, "Form").Stretch(70, 70, 70, 70);
            UiKit.VStack(col, 34);
            UiKit.Text(col, L.T("auth.welcome"), 64, UiKit.Ink, UiKit.Weight.ExtraBold).Size(-1, 100);
            UiKit.Text(col, L.T("auth.login_subtitle"), 34, UiKit.InkSoft).Size(-1, 60);
            _email = UiKit.Input(col, L.T("auth.email")); _email.Size(-1, 120);
            _email.contentType = TMP_InputField.ContentType.EmailAddress;
            _password = UiKit.Input(col, L.T("auth.password"), true); _password.Size(-1, 120);
            UiKit.Button(col, L.T("auth.login"), Submit).Size(-1, 130);
            UiKit.Button(col, L.T("auth.create_account"), () => Nav.Push(new RegisterScreen()), UiKit.ButtonStyle.Secondary).Size(-1, 120);
            UiKit.Button(col, L.T("auth.forgot"), ForgotPassword, UiKit.ButtonStyle.Ghost, 32).Size(-1, 80);
            LanguageToggle(Root);
        }

        public static RectTransform AuthCard(RectTransform root, bool wide)
        {
            var title = UiKit.Text(root, "Pet Star", wide ? 110 : 120, UiKit.Sapphire, UiKit.Weight.ExtraBold);
            var card = UiKit.Panel(root, UiKit.White, 56, "AuthCard", shadow: true);
            var rt = (RectTransform)card.transform;
            if (wide)
            {
                title.rectTransform.Place(new Vector2(0.27f, 0.5f), new Vector2(800, 200), Vector2.zero);
                rt.Place(new Vector2(0.7f, 0.5f), new Vector2(900, 980), Vector2.zero);
            }
            else
            {
                title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(1000, 200), new Vector2(0, -180));
                rt.Place(new Vector2(0.5f, 0.45f), new Vector2(960, 1180), Vector2.zero);
            }
            return rt;
        }

        public static void LanguageToggle(RectTransform root)
        {
            var b = UiKit.Button(root, L.Language == "vi" ? "Tiếng Việt" : "English", () =>
            {
                L.SetLanguage(L.Language == "vi" ? "en" : "vi");
                Navigator.I.Replace(Navigator.I.ScreenLayer.childCount > 0 && Navigator.I.ScreenLayer.GetChild(0).name == nameof(RegisterScreen) ? new RegisterScreen() : new LoginScreen());
            }, UiKit.ButtonStyle.Secondary, 32);
            ((RectTransform)b.transform).Place(new Vector2(1, 1), new Vector2(260, 90), new Vector2(-40, -60));
        }

        private async void Submit()
        {
            var auth = await Call(() => Api.Post<AuthResponse>("/api/auth/login", new LoginRequest
            {
                Email = _email.text.Trim(), Password = _password.text, Platform = ClientConfig.Platform, DeviceName = SystemInfo.deviceModel,
            }));
            if (auth == null) return;
            Session.Apply(auth);
            Nav.Replace(new ServerSelectScreen());
        }

        private async void ForgotPassword()
        {
            if (string.IsNullOrWhiteSpace(_email.text)) { Nav.Toast(L.T("auth.enter_email_first"), true); return; }
            var r = await Call(() => Api.Post<Newtonsoft.Json.Linq.JObject>("/api/auth/forgot-password", new EmailRequest { Email = _email.text.Trim() }));
            if (r != null) Nav.Toast(L.T("auth.reset_sent"));
        }
    }

    public sealed class RegisterScreen : ScreenBase
    {
        public override bool ShowCurrencyBar => false;
        private TMP_InputField _email = null!, _password = null!, _name = null!;

        public override void Build()
        {
            var card = LoginScreen.AuthCard(Root, Wide);
            var col = UiKit.Rect(card, "Form").Stretch(70, 70, 70, 70);
            UiKit.VStack(col, 30);
            UiKit.Text(col, L.T("auth.create_account"), 60, UiKit.Ink, UiKit.Weight.ExtraBold).Size(-1, 100);
            _name = UiKit.Input(col, L.T("auth.display_name")); _name.Size(-1, 120); _name.characterLimit = 24;
            _email = UiKit.Input(col, L.T("auth.email")); _email.Size(-1, 120);
            _password = UiKit.Input(col, L.T("auth.password_rules"), true); _password.Size(-1, 120);
            UiKit.Button(col, L.T("auth.register"), Submit).Size(-1, 130);
            UiKit.Text(col, L.T("auth.verify_note"), 28, UiKit.InkSoft).Size(-1, 90);
        }

        private async void Submit()
        {
            var auth = await Call(() => Api.Post<AuthResponse>("/api/auth/register", new RegisterRequest
            {
                Email = _email.text.Trim(), Password = _password.text, DisplayName = _name.text.Trim(), Language = L.Language,
            }));
            if (auth == null) return;
            Session.Apply(auth);
            Nav.Toast(L.T("auth.check_email"));
            Nav.Replace(new ServerSelectScreen());
        }
    }

    public sealed class ServerSelectScreen : ScreenBase
    {
        public override bool ShowCurrencyBar => false;
        public override bool ShowBackButton => false;
        public override string TitleKey => "server.title";
        private RectTransform _list = null!;
        private List<ServerDto>? _servers;

        public override void Build()
        {
            var panel = UiKit.Panel(Root, UiKit.White, 48, "ServerPanel", shadow: true);
            var rt = (RectTransform)panel.transform;
            if (Wide) rt.Place(new Vector2(0.5f, 0.46f), new Vector2(1300, 780), Vector2.zero);
            else rt.Place(new Vector2(0.5f, 0.46f), new Vector2(960, 1300), Vector2.zero);
            _list = UiKit.Scroll(UiKit.Rect(rt, "ListArea").Stretch(30, 30, 30, 30));
            UiKit.VStack(_list, 24, new RectOffset(10, 10, 10, 10));
            if (_servers != null) Fill();
        }

        public override async void OnShow()
        {
            if (_servers != null) return;
            _servers = await Call(() => Api.Get<List<ServerDto>>("/api/servers"));
            if (_servers != null && _list != null) Fill();
        }

        private void Fill()
        {
            UiKit.Clear(_list);
            if (_servers!.Count == 0) { UiKit.Text(_list, L.T("server.none"), 40, UiKit.InkSoft).Size(-1, 200); return; }
            foreach (var s in _servers)
            {
                var row = UiKit.Panel(_list, s.HasProgress ? UiKit.Sky : UiKit.White, 36, "Server:" + s.Code);
                row.Size(-1, 180);
                var o = row.gameObject.AddComponent<Outline>(); o.effectColor = UiKit.SkyDeep; o.effectDistance = new Vector2(2, -2);
                var name = UiKit.Text(row.transform, $"{s.Code} • {s.Name}", 50, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft);
                name.rectTransform.Stretch(40, 20, 380, 70);
                var detail = s.HasProgress ? L.T("server.your_progress", s.Nickname ?? "", s.PlayerLevel ?? 1) : (s.RegistrationOpen ? L.T("server.new_start") : L.T("server.registration_closed"));
                var d = UiKit.Text(row.transform, detail, 32, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.MidlineLeft);
                d.rectTransform.Stretch(40, 100, 380, 16);
                var status = UiKit.Text(row.transform, L.T("server.status." + s.Status), 32, StatusColor(s.Status), UiKit.Weight.Bold, TextAlignmentOptions.MidlineRight);
                status.rectTransform.Stretch(0, 20, 300, 100);
                var sid = s.Id;
                bool canEnter = s.Status == "online" && (s.HasProgress || s.RegistrationOpen);
                var b = UiKit.Button(row.transform, L.T(s.HasProgress ? "server.continue" : "server.join"), () => Enter(sid), canEnter ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Secondary, 36);
                ((RectTransform)b.transform).Place(new Vector2(1, 0.5f), new Vector2(240, 100), new Vector2(-30, -20), new Vector2(1, 0.5f));
                b.interactable = canEnter;
            }
        }

        private static Color StatusColor(string s) => s switch { "online" => UiKit.Hex("#2E9E62"), "maintenance" => UiKit.GoldDeep, _ => UiKit.InkSoft };

        private async void Enter(int id)
        {
            var auth = await Call(() => Api.Post<AuthResponse>($"/api/servers/{id}/enter", new EnterServerRequest()));
            if (auth == null) return;
            Session.Apply(auth);
            var p = await Call(() => Api.Get<PlayerDto>("/api/player"));
            if (p == null) return;
            Session.SetPlayer(p);
            Nav.Replace(new LobbyScreen());
        }
    }
}
