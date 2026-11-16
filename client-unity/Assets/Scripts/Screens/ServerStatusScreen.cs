using System.Threading.Tasks;
using PetStar.Client.Core;
using PetStar.Client.UI;
using UnityEngine;

namespace PetStar.Client.Screens
{
    /// <summary>
    /// Full-screen state when the game cannot reach a playable server: not configured in this build (e.g. the WebGL
    /// test build before the backend is deployed), no connection, server unavailable, or maintenance. Shows the
    /// official logo, a short explanation and "Try again" (back through the splash check). Nothing is faked: no
    /// offline gameplay or local currency.
    /// </summary>
    public sealed class ServerStatusScreen : ScreenBase
    {
        public enum Kind { NotConfigured, Offline, Unavailable, Maintenance }

        private readonly Kind _kind;
        public ServerStatusScreen(Kind kind) { _kind = kind; }
        public override bool ShowCurrencyBar => false;
        public override bool ShowBackButton => false;

        /// <summary>Anonymous GET /api/version: null when the API answers, otherwise the state to show.</summary>
        public static async Task<Kind?> Probe(ApiClient api)
        {
            if (!ClientConfig.IsServerConfigured) return Kind.NotConfigured;
            try
            {
                var v = await api.Get<Newtonsoft.Json.Linq.JObject>("/api/version");
                return v["name"] != null ? null : Kind.Unavailable; // answered, but not by the Pet Star API
            }
            // /api/version is anonymous and always answers when the API is up: any failure means "not playable"
            catch (ApiError e) { return From(e) ?? Kind.Unavailable; }
            catch (System.Exception) { return Kind.Unavailable; } // e.g. an HTML page instead of JSON
        }

        /// <summary>Maps an API failure to a status screen, or null when it is an ordinary error (toast).</summary>
        public static Kind? From(ApiError e) => e.Code switch
        {
            "server_not_configured" => Kind.NotConfigured,
            "server_maintenance" => Kind.Maintenance,
            "server_unavailable" => Kind.Unavailable,
            _ when e.IsNetwork => Kind.Offline,
            _ => null,
        };

        public override void Build()
        {
            string key = _kind switch
            {
                Kind.NotConfigured => "status.not_configured",
                Kind.Offline => "status.offline",
                Kind.Maintenance => "status.maintenance",
                _ => "status.unavailable",
            };
            var col = UiKit.Rect(Root, "Status");
            if (Wide) col.Place(new Vector2(0.5f, 0.5f), new Vector2(1500, 860), Vector2.zero);
            else col.Place(new Vector2(0.5f, 0.52f), new Vector2(940, 1400), Vector2.zero);

            var title = UiKit.Text(col, "Pet Star", 110, UiKit.Sapphire, UiKit.Weight.ExtraBold);
            var logo = BrandLogo.Create(col, Wide ? 520 : 560, true, title.gameObject);
            if (Wide)
            {
                logo.Place(new Vector2(0.2f, 0.5f), logo.sizeDelta, Vector2.zero);
                title.rectTransform.Place(new Vector2(0.2f, 0.5f), new Vector2(600, 160), Vector2.zero);
            }
            else
            {
                logo.Place(new Vector2(0.5f, 1), logo.sizeDelta, Vector2.zero, new Vector2(0.5f, 1));
                title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(900, 160), new Vector2(0, -200), new Vector2(0.5f, 1));
            }

            var card = UiKit.Panel(col, UiKit.White, 48, "StatusCard", shadow: true);
            var crt = (RectTransform)card.transform;
            if (Wide) crt.Place(new Vector2(0.7f, 0.5f), new Vector2(820, 640), Vector2.zero);
            else crt.Place(new Vector2(0.5f, 0), new Vector2(940, 720), Vector2.zero, new Vector2(0.5f, 0));
            var body = UiKit.Rect(card.transform, "Body").Stretch(56, 56, 56, 56);
            UiKit.VStack(body, 28);
            UiKit.Text(body, L.T(key + ".title"), 54, UiKit.Ink, UiKit.Weight.ExtraBold).Size(-1, 140);
            UiKit.Text(body, L.T(key + ".body"), 34, UiKit.InkSoft).Size(-1, 230);
            UiKit.Button(body, L.T("status.retry"), () => Nav.Replace(new SplashScreen()), UiKit.ButtonStyle.Gold, 42).Size(-1, 130);
            LoginScreen.LanguageToggle(Root, () => new ServerStatusScreen(_kind));
        }
    }
}
