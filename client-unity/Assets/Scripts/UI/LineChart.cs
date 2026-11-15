using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PetStar.Client.UI
{
    /// <summary>Lightweight price chart drawn with UI geometry (no image assets): polyline + soft area fill.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LineChart : MaskableGraphic
    {
        private readonly List<float> _values = new();
        public float Thickness = 6f;
        public Color FillColor = new(0.14f, 0.34f, 0.77f, 0.12f);

        public void SetValues(IEnumerable<float> values)
        {
            _values.Clear();
            _values.AddRange(values);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_values.Count < 2) return;
            var r = rectTransform.rect;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in _values) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
            if (Mathf.Approximately(min, max)) { min -= 1; max += 1; }
            Vector2 P(int i) => new(r.xMin + r.width * i / (_values.Count - 1), r.yMin + r.height * 0.08f + r.height * 0.84f * (_values[i] - min) / (max - min));
            // area fill
            for (int i = 0; i < _values.Count - 1; i++)
            {
                var a = P(i); var b = P(i + 1);
                int idx = vh.currentVertCount;
                vh.AddVert(new Vector3(a.x, r.yMin), FillColor, Vector2.zero);
                vh.AddVert(a, FillColor, Vector2.zero);
                vh.AddVert(b, FillColor, Vector2.zero);
                vh.AddVert(new Vector3(b.x, r.yMin), FillColor, Vector2.zero);
                vh.AddTriangle(idx, idx + 1, idx + 2); vh.AddTriangle(idx, idx + 2, idx + 3);
            }
            // line
            for (int i = 0; i < _values.Count - 1; i++)
            {
                var a = P(i); var b = P(i + 1);
                var n = new Vector2(-(b - a).y, (b - a).x).normalized * Thickness * 0.5f;
                int idx = vh.currentVertCount;
                vh.AddVert(a - n, color, Vector2.zero); vh.AddVert(a + n, color, Vector2.zero);
                vh.AddVert(b + n, color, Vector2.zero); vh.AddVert(b - n, color, Vector2.zero);
                vh.AddTriangle(idx, idx + 1, idx + 2); vh.AddTriangle(idx, idx + 2, idx + 3);
            }
        }
    }

    /// <summary>Bottom navigation hub (Home · Collection · Market · Social · Battle) so the Lobby stays uncluttered.</summary>
    public static class HubNav
    {
        public static void Build(RectTransform root, LayoutMode mode, string active)
        {
            var bar = UiKit.Panel(root, new Color(1, 1, 1, 0.96f), 44, "HubNav", shadow: true);
            var rt = (RectTransform)bar.transform;
            if (mode == LayoutMode.Portrait) rt.Place(new Vector2(0.5f, 0), new Vector2(1040, 150), new Vector2(0, 24), new Vector2(0.5f, 0));
            else rt.Place(new Vector2(0, 0.5f), new Vector2(170, 900), new Vector2(16, 0), new Vector2(0, 0.5f));
            var layout = mode == LayoutMode.Portrait ? (HorizontalOrVerticalLayoutGroup)UiKit.HStack(rt, 6, new RectOffset(12, 12, 12, 12)) : UiKit.VStack(rt, 10, new RectOffset(12, 12, 20, 20));
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
            void Tab(string id, System.Action open)
            {
                var b = UiKit.Button(rt, Core.L.T("hub." + id), open, id == active ? UiKit.ButtonStyle.Primary : UiKit.ButtonStyle.Ghost, 28, 32);
                b.Size(-1, -1, 1, 1);
            }
            Tab("home", () => Navigator.I.Replace(new Screens.LobbyScreen()));
            Tab("collection", () => Navigator.I.Replace(new Screens.CollectionHubScreen()));
            Tab("market", () => Navigator.I.Replace(new Screens.MarketHomeScreen()));
            Tab("social", () => Navigator.I.Replace(new Screens.FeedScreen()));
            Tab("battle", () => Navigator.I.Replace(new Screens.BattleHubScreen()));
        }
    }
}
