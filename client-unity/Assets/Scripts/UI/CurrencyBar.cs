using System.Collections;
using PetStar.Client.Core;
using PetStar.Client.Effects;
using PetStar.Contracts;
using PetStar.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.UI
{
    /// <summary>Responsive balance bar for RAINBOW_STAR and MINI_GREEN_BEAN. Values come only from the server
    /// player snapshot (Session.Player) and animate when the server returns new balances.</summary>
    public sealed class CurrencyBar : MonoBehaviour
    {
        private TextMeshProUGUI _star = null!, _bean = null!;
        private long _shownStar, _shownBean;

        public static CurrencyBar Create(Transform parent, LayoutMode mode)
        {
            var root = UiKit.Rect(parent, "CurrencyBar");
            UiKit.HStack(root, mode == LayoutMode.Portrait ? 24 : 18, null, TextAnchor.MiddleRight).childForceExpandWidth = true;
            var bar = root.gameObject.AddComponent<CurrencyBar>();
            bar._star = Pill(root, Currency.RainbowStar, true);
            bar._bean = Pill(root, Currency.MiniGreenBean, false);
            App.Session.PlayerChanged += bar.OnPlayer;
            if (App.Session.Player != null) bar.Set(App.Session.Player, false);
            return bar;
        }

        private void OnDestroy() => App.Session.PlayerChanged -= OnPlayer;
        private void OnPlayer(PlayerDto p) { if (this != null) Set(p, true); }

        private static TextMeshProUGUI Pill(RectTransform parent, string currency, bool premium)
        {
            var pill = UiKit.Panel(parent, UiKit.White, 46, "Pill:" + currency, shadow: false);
            pill.Size(-1, 92, 1);
            var outline = pill.gameObject.AddComponent<Outline>();
            outline.effectColor = premium ? new Color(0.91f, 0.77f, 0.42f, 0.9f) : new Color(0.55f, 0.85f, 0.65f, 0.9f);
            outline.effectDistance = new Vector2(3, -3);
            var icon = UiKit.Icon(pill.transform, null, new Vector2(104, 104), "Icon");
            ((RectTransform)icon.transform).Place(new Vector2(0, 0.5f), new Vector2(104, 104), new Vector2(-10, 4), new Vector2(0, 0.5f));
            _ = LoadIcon(icon, currency, premium);
            var txt = UiKit.Text(pill.transform, "0", 40, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineRight, "Amount");
            txt.rectTransform.Stretch(104, 6, premium ? 92 : 30, 6);
            if (premium)
            {
                // "+" opens the Rainbow Star shop (store purchase verified by backend)
                var plus = UiKit.Button(pill.transform, "+", () => Navigator.I.Push(new Screens.ShopScreen(Screens.ShopScreen.Tab.RainbowStar)), UiKit.ButtonStyle.Gold, 44, 32);
                ((RectTransform)plus.transform).Place(new Vector2(1, 0.5f), new Vector2(70, 70), new Vector2(-12, 0), new Vector2(1, 0.5f));
            }
            return txt;
        }

        private static async System.Threading.Tasks.Task LoadIcon(Image icon, string currency, bool premium)
        {
            var s = await GameDataClient.LoadSprite(GameDataClient.CurrencyIcon(currency));
            if (icon == null) return;
            icon.sprite = s; icon.color = Color.white;
            if (premium) StarShimmer.Attach(icon); else BeanReflection.Attach(icon);
        }

        public void Set(PlayerDto p, bool animate)
        {
            long star = p.Currencies.TryGetValue(Currency.RainbowStar, out var a) ? a : 0;
            long bean = p.Currencies.TryGetValue(Currency.MiniGreenBean, out var b) ? b : 0;
            if (!animate || !isActiveAndEnabled) { _shownStar = star; _shownBean = bean; _star.text = L.Number(star); _bean.text = L.Number(bean); return; }
            StopAllCoroutines();
            StartCoroutine(Count(_star, _shownStar, star, v => _shownStar = v));
            StartCoroutine(Count(_bean, _shownBean, bean, v => _shownBean = v));
        }

        private static IEnumerator Count(TextMeshProUGUI t, long from, long to, System.Action<long> store)
        {
            if (from == to) { t.text = L.Number(to); yield break; }
            if (to > from) Audio.Play(Audio.Sfx.Coin);
            float k = 0;
            while (k < 1)
            {
                k += Time.unscaledDeltaTime * 2.5f;
                long v = (long)Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, k));
                t.text = L.Number(v);
                t.transform.localScale = Vector3.one * (1 + 0.08f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            t.text = L.Number(to); t.transform.localScale = Vector3.one; store(to);
        }
    }
}
