using System.Threading.Tasks;
using PetStar.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.UI
{
    /// <summary>
    /// The official Pet Star: Trading Duel logo (Addressable "ui/brand_logo", imported unchanged from
    /// assets-source/brand by the build). Square, aspect preserved, corners softly rounded (about 12 % of the side,
    /// far from the lettering), with a faint breathing glow behind it. Until the art has loaded, or if a build has no
    /// logo file, the given fallback objects (the text title) stay visible.
    /// </summary>
    public static class BrandLogo
    {
        public const string Address = "ui/brand_logo";

        public static RectTransform Create(Transform parent, float size, bool glow = true, params GameObject[] fallback)
        {
            var root = UiKit.Rect(parent, "BrandLogo");
            root.sizeDelta = new Vector2(size, size);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;

            if (glow)
            {
                var g = UiKit.Icon(root, UiKit.Circle, new Vector2(size * 1.18f, size * 1.18f), "Glow");
                g.type = Image.Type.Sliced;
                g.color = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.22f);
                g.gameObject.AddComponent<LogoGlow>();
            }

            var frame = UiKit.Rect(root, "Frame");
            frame.Stretch();
            var maskImg = frame.gameObject.AddComponent<Image>();
            maskImg.sprite = UiKit.RoundedSprite(64);
            maskImg.type = Image.Type.Sliced;
            maskImg.pixelsPerUnitMultiplier = Mathf.Max(0.01f, 65f / (0.12f * size)); // corner radius = 12 % of the side
            maskImg.raycastTarget = false;
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var art = UiKit.Icon(frame, null, Vector2.zero, "Art");
            ((RectTransform)art.transform).Stretch();
            art.preserveAspect = true;

            _ = Load(group, art, fallback);
            return root;
        }

        private static async Task Load(CanvasGroup group, Image art, GameObject[] fallback)
        {
            var sprite = await GameDataClient.LoadSprite(Address);
            if (art == null || group == null) return; // screen closed meanwhile
            if (sprite == null) { group.gameObject.SetActive(false); return; } // no logo in this build: keep the text title
            art.sprite = sprite; art.color = Color.white;
            foreach (var f in fallback) if (f != null) f.SetActive(false);
            group.gameObject.AddComponent<LogoFadeIn>();
        }
    }

    /// <summary>0.4 s fade + slight scale-in when the logo appears.</summary>
    public sealed class LogoFadeIn : MonoBehaviour
    {
        private CanvasGroup _g = null!; private float _t;
        private void Awake() { _g = GetComponent<CanvasGroup>(); }
        private void Update()
        {
            _t += Time.unscaledDeltaTime / 0.4f;
            float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(_t));
            _g.alpha = k;
            transform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, k);
            if (_t >= 1) Destroy(this);
        }
    }

    /// <summary>Slow, subtle light behind the logo (never flashes).</summary>
    public sealed class LogoGlow : MonoBehaviour
    {
        private Image _img = null!; private float _a, _t;
        private void Awake() { _img = GetComponent<Image>(); _a = _img.color.a; }
        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            var c = _img.color; c.a = _a * (0.75f + 0.25f * Mathf.Sin(_t * 1.2f));
            _img.color = c;
        }
    }
}
