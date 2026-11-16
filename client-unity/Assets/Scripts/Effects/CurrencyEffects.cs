using PetStar.Client.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.Effects
{
    /// <summary>Rainbow Star: periodic diagonal shimmer sweep + soft glow pulse.</summary>
    public sealed class StarShimmer : MonoBehaviour
    {
        private RectTransform _sweep = null!;
        private Image _glow = null!;
        private float _t;

        public static void Attach(Image icon)
        {
            var host = icon.gameObject;
            var glowRt = UI.UiKit.Rect(icon.transform.parent, "StarGlow");
            glowRt.SetSiblingIndex(icon.transform.GetSiblingIndex());
            var glow = glowRt.gameObject.AddComponent<Image>();
            glow.sprite = UI.UiKit.Circle; glow.color = new Color(1f, 0.92f, 0.6f, 0.0f); glow.raycastTarget = false;
            var mask = UI.UiKit.Rect(icon.transform, "ShimmerMask").Stretch();
            mask.gameObject.AddComponent<RectMask2D>();
            var sweep = UI.UiKit.Rect(mask, "Sweep");
            var si = sweep.gameObject.AddComponent<Image>();
            si.color = new Color(1, 1, 1, 0.55f); si.raycastTarget = false;
            sweep.anchorMin = new Vector2(0, 0); sweep.anchorMax = new Vector2(0, 1); sweep.sizeDelta = new Vector2(14, 40);
            sweep.localRotation = Quaternion.Euler(0, 0, -25);
            var c = host.AddComponent<StarShimmer>();
            c._sweep = sweep; c._glow = glow;
            c._t = Random.value * 3f;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            var rt = (RectTransform)transform;
            float w = rt.rect.width;
            float phase = (_t % 3.2f) / 0.7f; // sweep lasts 0.7s every 3.2s
            _sweep.gameObject.SetActive(phase <= 1f);
            if (phase <= 1f) _sweep.anchoredPosition = new Vector2(Mathf.Lerp(-0.2f * w, 1.2f * w, phase), 0);
            var g = (RectTransform)_glow.transform;
            g.anchorMin = rt.anchorMin; g.anchorMax = rt.anchorMax; g.pivot = rt.pivot;
            g.anchoredPosition = rt.anchoredPosition; g.sizeDelta = rt.sizeDelta * 1.35f;
            _glow.color = new Color(1f, 0.9f, 0.55f, 0.18f + 0.12f * Mathf.Sin(_t * 2.4f));
        }
    }

    /// <summary>Mini Green Bean: soft moving specular reflection.</summary>
    public sealed class BeanReflection : MonoBehaviour
    {
        private Image _spec = null!;
        private float _t;

        public static void Attach(Image icon)
        {
            var spec = UI.UiKit.Rect(icon.transform, "Reflection");
            spec.anchorMin = spec.anchorMax = new Vector2(0.38f, 0.62f);
            spec.sizeDelta = new Vector2(18, 10);
            var img = spec.gameObject.AddComponent<Image>();
            img.sprite = UI.UiKit.Circle; img.raycastTarget = false; img.color = new Color(1, 1, 1, 0.4f);
            var c = icon.gameObject.AddComponent<BeanReflection>();
            c._spec = img; c._t = Random.value * 4f;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            var rt = (RectTransform)_spec.transform;
            float s = ((RectTransform)transform).rect.width / 72f;
            rt.sizeDelta = new Vector2(18, 10) * s;
            rt.anchoredPosition = new Vector2(Mathf.Sin(_t * 0.9f) * 4f * s, Mathf.Cos(_t * 0.9f) * 2f * s);
            _spec.color = new Color(1, 1, 1, 0.25f + 0.2f * (0.5f + 0.5f * Mathf.Sin(_t * 1.7f)));
        }
    }
}
