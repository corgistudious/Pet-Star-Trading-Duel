using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PetStar.Client.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.UI
{
    public enum LayoutMode { Portrait, Landscape, Desktop }

    /// <summary>Base class for code-built screens. Build() is called again when the layout mode changes
    /// (rotation / window resize), so portrait and landscape get their own layouts instead of stretching.</summary>
    public abstract class ScreenBase
    {
        public RectTransform Root = null!;
        protected Navigator Nav => Navigator.I;
        protected ApiClient Api => App.Api;
        protected Session Session => App.Session;
        protected LayoutMode Mode => Nav.Mode;
        protected bool Wide => Mode != LayoutMode.Portrait;

        public abstract void Build();
        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual bool ShowCurrencyBar => true;
        public virtual bool ShowBackButton => true;
        public virtual string TitleKey => "";

        /// <summary>Runs an API call with loading overlay and localized error toast.</summary>
        protected async Task<T?> Call<T>(Func<Task<T>> fn, bool overlay = true) where T : class
        {
            if (overlay) Nav.SetLoading(true);
            try { return await fn(); }
            catch (ApiError e)
            {
                if (e.Status == 401) { Session.Logout(); Nav.Replace(new Screens.LoginScreen()); }
                else Nav.Toast(e.IsNetwork ? L.T("error.network") : L.Error(e.Code), true);
                return null;
            }
            finally { if (overlay) Nav.SetLoading(false); }
        }
    }

    public sealed class Navigator : MonoBehaviour
    {
        public static Navigator I { get; private set; } = null!;
        public LayoutMode Mode { get; private set; }
        public RectTransform ScreenLayer = null!;
        private RectTransform _overlay = null!, _toastLayer = null!, _chrome = null!;
        private CanvasScaler _scaler = null!;
        private readonly Stack<ScreenBase> _stack = new();
        private Vector2Int _lastSize;
        private GameObject? _loading;
        private CurrencyBar? _currencyBar;

        public static Navigator Create(Transform parent)
        {
            var canvasGo = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var nav = canvasGo.AddComponent<Navigator>();
            nav._scaler = canvasGo.GetComponent<CanvasScaler>();
            nav._scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var bg = UiKit.Rect(canvasGo.transform, "Background").Stretch();
            bg.gameObject.AddComponent<Image>().color = UiKit.Sky;
            nav.ScreenLayer = UiKit.Rect(canvasGo.transform, "Screens").Stretch();
            nav._chrome = UiKit.Rect(canvasGo.transform, "Chrome").Stretch();
            nav._overlay = UiKit.Rect(canvasGo.transform, "Overlay").Stretch();
            nav._toastLayer = UiKit.Rect(canvasGo.transform, "Toasts").Stretch();
            I = nav;
            nav.UpdateMode(force: true);
            return nav;
        }

        private void Update() => UpdateMode(false);

        private void UpdateMode(bool force)
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (!force && size == _lastSize) return;
            _lastSize = size;
            bool desktop = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.OSXEditor;
            var mode = size.x < size.y ? LayoutMode.Portrait : desktop ? LayoutMode.Desktop : LayoutMode.Landscape;
            // reference resolutions per layout: portrait matches width, landscape/desktop match height
            if (mode == LayoutMode.Portrait) { _scaler.referenceResolution = new Vector2(1080, 1920); _scaler.matchWidthOrHeight = 0f; }
            else { _scaler.referenceResolution = new Vector2(1920, 1080); _scaler.matchWidthOrHeight = 1f; }
            bool changed = mode != Mode;
            Mode = mode;
            if ((changed || force) && _stack.Count > 0) Rebuild();
        }

        public void Push(ScreenBase s)
        {
            if (_stack.Count > 0) { _stack.Peek().OnHide(); _stack.Peek().Root.gameObject.SetActive(false); }
            _stack.Push(s);
            Show(s, true);
        }

        public void Replace(ScreenBase s)
        {
            while (_stack.Count > 0) { var old = _stack.Pop(); old.OnHide(); Destroy(old.Root.gameObject); }
            _stack.Push(s);
            Show(s, true);
        }

        public void Back()
        {
            if (_stack.Count <= 1) return;
            var old = _stack.Pop(); old.OnHide(); Destroy(old.Root.gameObject);
            var top = _stack.Peek();
            top.Root.gameObject.SetActive(true);
            BuildChrome(top);
            top.OnShow();
        }

        private void Show(ScreenBase s, bool animate)
        {
            s.Root = UiKit.Rect(ScreenLayer, s.GetType().Name).Stretch();
            s.Build();
            BuildChrome(s);
            s.OnShow();
            if (animate) StartCoroutine(FadeIn(s.Root));
        }

        private void Rebuild()
        {
            var s = _stack.Peek();
            UiKit.Clear(s.Root);
            s.Build();
            BuildChrome(s);
            s.OnShow();
        }

        private void BuildChrome(ScreenBase s)
        {
            UiKit.Clear(_chrome);
            _currencyBar = null;
            float top = Mode == LayoutMode.Portrait ? 60 : 24;
            if (s.ShowBackButton && _stack.Count > 1)
            {
                var b = UiKit.Button(_chrome, "‹", Back, UiKit.ButtonStyle.Secondary, 60, 44);
                ((RectTransform)b.transform).Place(new Vector2(0, 1), new Vector2(110, 110), new Vector2(30, -top));
            }
            if (!string.IsNullOrEmpty(s.TitleKey))
            {
                var t = UiKit.Text(_chrome, L.T(s.TitleKey), Mode == LayoutMode.Portrait ? 52 : 46, UiKit.Ink, UiKit.Weight.ExtraBold);
                t.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(700, 110), new Vector2(0, -top));
                if (Mode != LayoutMode.Portrait) { t.rectTransform.Place(new Vector2(0, 1), new Vector2(600, 110), new Vector2(160, -top)); t.alignment = TextAlignmentOptions.MidlineLeft; }
            }
            if (s.ShowCurrencyBar && App.Session.Player != null)
            {
                _currencyBar = CurrencyBar.Create(_chrome, Mode);
                var rt = (RectTransform)_currencyBar.transform;
                if (Mode == LayoutMode.Portrait) rt.Place(new Vector2(0.5f, 1), new Vector2(1000, 96), new Vector2(0, -(top + 130)));
                else rt.Place(new Vector2(1, 1), new Vector2(760, 92), new Vector2(-30, -top - 8));
            }
        }

        private static IEnumerator FadeIn(RectTransform rt)
        {
            var cg = rt.gameObject.AddComponent<CanvasGroup>();
            float t = 0;
            while (t < 1 && cg != null)
            {
                t += Time.unscaledDeltaTime * 5f;
                cg.alpha = Mathf.SmoothStep(0, 1, t);
                rt.anchoredPosition = new Vector2(0, Mathf.Lerp(-30, 0, Mathf.SmoothStep(0, 1, t)));
                yield return null;
            }
        }

        public void SetLoading(bool on)
        {
            if (on && _loading == null)
            {
                var block = UiKit.Rect(_overlay, "Loading").Stretch();
                block.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.15f, 0.3f, 0.18f);
                var spinner = UiKit.Panel(block, UiKit.White, 40, "Spinner", shadow: true);
                ((RectTransform)spinner.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(240, 240), Vector2.zero);
                var dot = UiKit.Panel(spinner.transform, UiKit.Sapphire, 20, "Dot");
                ((RectTransform)dot.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(60, 60), new Vector2(0, 50));
                spinner.gameObject.AddComponent<Spin>();
                var txt = UiKit.Text(spinner.transform, L.T("common.loading"), 30, UiKit.InkSoft);
                txt.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(220, 60), new Vector2(0, 30));
                _loading = block.gameObject;
            }
            else if (!on && _loading != null) { Destroy(_loading); _loading = null; }
        }

        public void Toast(string message, bool error = false)
        {
            var p = UiKit.Panel(_toastLayer, error ? UiKit.Danger : UiKit.Ink, 36, "Toast", shadow: true);
            var rt = (RectTransform)p.transform;
            rt.Place(new Vector2(0.5f, 0), new Vector2(Mode == LayoutMode.Portrait ? 960 : 900, 120), new Vector2(0, 160));
            var t = UiKit.Text(p.transform, message, 36, UiKit.White, UiKit.Weight.Bold);
            t.rectTransform.Stretch(30, 10, 30, 10);
            StartCoroutine(ToastLife(p.gameObject));
        }

        private static IEnumerator ToastLife(GameObject go)
        {
            yield return new WaitForSecondsRealtime(2.6f);
            if (go != null) Destroy(go);
        }

        /// <summary>Modal popup container; returns the content panel.</summary>
        public RectTransform Popup(Vector2 portraitSize, Vector2 wideSize, out Action close)
        {
            var dim = UiKit.Rect(_overlay, "Popup").Stretch();
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0.08f, 0.12f, 0.25f, 0.45f);
            var panel = UiKit.Panel(dim, UiKit.White, 48, "PopupPanel", shadow: true);
            var rt = (RectTransform)panel.transform;
            rt.Place(new Vector2(0.5f, 0.5f), Mode == LayoutMode.Portrait ? portraitSize : wideSize, Vector2.zero);
            var go = dim.gameObject;
            close = () => { if (go != null) Destroy(go); };
            var closeAction = close;
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => closeAction());
            panel.gameObject.AddComponent<Button>(); // swallow clicks on the panel itself
            return rt;
        }
    }

    public sealed class Spin : MonoBehaviour
    {
        private void Update() => transform.Rotate(0, 0, -240 * Time.unscaledDeltaTime);
    }
}
