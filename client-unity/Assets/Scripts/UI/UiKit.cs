using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PetStar.Client.UI
{
    /// <summary>Simple Premium HD UI kit built from code: rounded panels (generated 9-slice sprites), typography,
    /// buttons with press feedback. White / light blue / sapphire / pastel with soft gold accents.</summary>
    public static class UiKit
    {
        public static readonly Color White = new(1f, 1f, 1f);
        public static readonly Color Sky = Hex("#EAF3FF");
        public static readonly Color SkyDeep = Hex("#D6E8FF");
        public static readonly Color Sapphire = Hex("#2457C5");
        public static readonly Color SapphireDark = Hex("#183C8C");
        public static readonly Color Ink = Hex("#1D2B4F");
        public static readonly Color InkSoft = Hex("#5B6B8F");
        public static readonly Color Gold = Hex("#E8C46A");
        public static readonly Color GoldDeep = Hex("#C79A2E");
        public static readonly Color Mint = Hex("#BFEBDD");
        public static readonly Color Peach = Hex("#FFD9C7");
        public static readonly Color Lilac = Hex("#E3D9FF");
        public static readonly Color Danger = Hex("#E5484D");
        public static readonly Color Shadow = new(0.11f, 0.17f, 0.31f, 0.12f);

        private static readonly Dictionary<int, Sprite> Rounded = new();
        private static Sprite? _circle;
        private static TMP_FontAsset? _regular, _bold, _extra;

        public static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

        public static void LoadFonts()
        {
            _regular = Resources.Load<TMP_FontAsset>("Fonts/BeVietnamPro-Regular SDF");
            _bold = Resources.Load<TMP_FontAsset>("Fonts/BeVietnamPro-Bold SDF") ?? _regular;
            _extra = Resources.Load<TMP_FontAsset>("Fonts/BeVietnamPro-ExtraBold SDF") ?? _bold;
        }

        /// <summary>Anti-aliased rounded rectangle, 9-sliced so any size keeps crisp corners.</summary>
        public static Sprite RoundedSprite(int radius)
        {
            radius = Mathf.Clamp(radius, 2, 64);
            if (Rounded.TryGetValue(radius, out var s)) return s;
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - c) - (c - radius), 0);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - c) - (c - radius), 0);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
            Rounded[radius] = s;
            return s;
        }

        public static Sprite Circle => _circle ??= RoundedSprite(64);

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(this RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        /// <summary>Place with anchors (0..1) and pixel size/offset relative to the anchor point.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        public static Image Panel(Transform parent, Color color, int radius = 28, string name = "Panel", bool shadow = false)
        {
            FollowRect? follow = null;
            if (shadow)
            {
                var sh = Rect(parent, name + "Shadow");
                var si = sh.gameObject.AddComponent<Image>();
                si.sprite = RoundedSprite(radius); si.type = Image.Type.Sliced; si.color = Shadow; si.raycastTarget = false;
                follow = sh.gameObject.AddComponent<FollowRect>();
                follow.Init(offset: new Vector2(0, -8), grow: 4);
            }
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = RoundedSprite(radius);
            img.type = Image.Type.Sliced;
            img.color = color;
            if (follow != null) follow.Target = rt;
            return img;
        }

        public enum Weight { Regular, Bold, ExtraBold }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, Weight w = Weight.Regular,
            TextAlignmentOptions align = TextAlignmentOptions.Center, string name = "Text")
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var font = w switch { Weight.ExtraBold => _extra, Weight.Bold => _bold, _ => _regular };
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        public enum ButtonStyle { Primary, Secondary, Gold, Ghost, Danger }

        public static Button Button(Transform parent, string label, Action onClick, ButtonStyle style = ButtonStyle.Primary, float fontSize = 40, int radius = 40)
        {
            var (bg, fg) = style switch
            {
                ButtonStyle.Primary => (Sapphire, White),
                ButtonStyle.Gold => (Gold, Ink),
                ButtonStyle.Secondary => (White, Sapphire),
                ButtonStyle.Danger => (Danger, White),
                _ => (new Color(1, 1, 1, 0), Sapphire),
            };
            var img = Panel(parent, bg, radius, "Button:" + label);
            var b = img.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { Audio.Play(Audio.Sfx.Click); onClick(); });
            img.gameObject.AddComponent<PressFeedback>();
            if (style == ButtonStyle.Secondary)
            {
                var outline = img.gameObject.AddComponent<Outline>();
                outline.effectColor = SkyDeep; outline.effectDistance = new Vector2(3, -3);
            }
            var t = Text(img.transform, label, fontSize, fg, Weight.Bold);
            t.rectTransform.Stretch(16, 4, 16, 4);
            return b;
        }

        public static void SetLabel(this Button b, string label) => b.GetComponentInChildren<TextMeshProUGUI>().text = label;

        public static Image Icon(Transform parent, Sprite? sprite, Vector2 size, string name = "Icon")
        {
            var rt = Rect(parent, name);
            rt.sizeDelta = size;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.preserveAspect = true; img.raycastTarget = false;
            if (sprite == null) img.color = new Color(1, 1, 1, 0);
            return img;
        }

        public static TMP_InputField Input(Transform parent, string placeholder, bool password = false)
        {
            var bg = Panel(parent, White, 28, "Input");
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = SkyDeep; outline.effectDistance = new Vector2(2, -2);
            var area = Rect(bg.transform, "TextArea").Stretch(28, 8, 28, 8);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Text(area, placeholder, 36, InkSoft, Weight.Regular, TextAlignmentOptions.MidlineLeft, "Placeholder");
            ph.rectTransform.Stretch();
            var txt = Text(area, "", 38, Ink, Weight.Regular, TextAlignmentOptions.MidlineLeft, "Value");
            txt.rectTransform.Stretch();
            txt.enableWordWrapping = false;
            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area; input.textComponent = txt; input.placeholder = ph;
            input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            input.pointSize = 38;
            return input;
        }

        public static VerticalLayoutGroup VStack(RectTransform rt, float spacing, RectOffset? padding = null, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.padding = padding ?? new RectOffset(0, 0, 0, 0); v.childAlignment = align;
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(RectTransform rt, float spacing, RectOffset? padding = null, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.padding = padding ?? new RectOffset(0, 0, 0, 0); h.childAlignment = align;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            return h;
        }

        public static LayoutElement Size(this Component c, float w = -1, float h = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) le.preferredWidth = w;
            if (h >= 0) le.preferredHeight = h;
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        /// <summary>Scrollable vertical content area; returns the content transform.</summary>
        public static RectTransform Scroll(RectTransform viewportParent, bool vertical = true)
        {
            var view = Rect(viewportParent, "Scroll").Stretch();
            view.gameObject.AddComponent<RectMask2D>();
            var sr = view.gameObject.AddComponent<ScrollRect>();
            var content = Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = content; sr.viewport = view; sr.horizontal = !vertical; sr.vertical = vertical;
            sr.movementType = ScrollRect.MovementType.Elastic; sr.scrollSensitivity = 40;
            return content;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }

    /// <summary>Soft press scale feedback for all buttons (touch + mouse).</summary>
    public sealed class PressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private float _target = 1f;
        public void OnPointerDown(PointerEventData e) => _target = 0.95f;
        public void OnPointerUp(PointerEventData e) => _target = 1f;
        public void OnPointerEnter(PointerEventData e) { if (e.pointerId < 0) _target = 1.03f; } // mouse hover (Windows)
        public void OnPointerExit(PointerEventData e) => _target = 1f;
        private void Update() => transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _target, Time.unscaledDeltaTime * 18f);
    }

    /// <summary>Keeps a shadow rect aligned with its target.</summary>
    public sealed class FollowRect : MonoBehaviour
    {
        public RectTransform? Target;
        private Vector2 _offset; private float _grow;
        public void Init(Vector2 offset, float grow) { _offset = offset; _grow = grow; }
        private void LateUpdate()
        {
            if (Target == null) return;
            var rt = (RectTransform)transform;
            rt.anchorMin = Target.anchorMin; rt.anchorMax = Target.anchorMax; rt.pivot = Target.pivot;
            rt.sizeDelta = Target.sizeDelta + Vector2.one * _grow; rt.anchoredPosition = Target.anchoredPosition + _offset;
            rt.localScale = Target.localScale;
            var img = GetComponent<Image>();
            if (img != null) img.enabled = Target.gameObject.activeInHierarchy;
        }
    }
}
