using System.Collections.Generic;
using PetStar.Client.Core;
using PetStar.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PetStar.Client.UI
{
    /// <summary>
    /// Master Pet Card (9:16). Every card uses the same layout (Resources/Data/card-layout.json, reference 1080x1920);
    /// only artwork, texts, stats, rarity frame color and finish effect change. Text/stats are live UI, never baked
    /// into images.
    /// </summary>
    public sealed class CardView : MonoBehaviour
    {
        public const float Aspect = 9f / 16f;
        public CardDto Card { get; private set; } = null!;
        private float _s;
        private RectTransform _root = null!;
        private Image? _finish;
        private GameObject? _back;

        /// <summary>Creates a card of the given width (height = width * 16/9).</summary>
        public static CardView Create(Transform parent, CardDto card, float width, bool interactive = true, bool faceDown = false)
        {
            var root = UiKit.Rect(parent, "Card:" + card.FormId);
            root.sizeDelta = new Vector2(width, width / Aspect);
            var v = root.gameObject.AddComponent<CardView>();
            v._root = root; v._s = width / 1080f; v.Card = card;
            v.BuildFront();
            if (interactive) root.gameObject.AddComponent<CardTilt>();
            v.SetFaceDown(faceDown);
            return v;
        }

        private Rect R(string key)
        {
            var a = GameDataClient.CardLayout["regions"]![key]!;
            return new Rect((float)a[0]! * _s, (float)a[1]! * _s, (float)a[2]! * _s, (float)a[3]! * _s);
        }

        private RectTransform At(Transform parent, string key, string name)
        {
            var r = R(key);
            var rt = UiKit.Rect(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(r.x, -r.y); rt.sizeDelta = new Vector2(r.width, r.height);
            return rt;
        }

        private int Rad(float px) => Mathf.Clamp(Mathf.RoundToInt(px * _s), 4, 64);

        private void BuildFront()
        {
            var c = Card;
            var rarity = GameDataClient.RarityColors.TryGetValue(c.Rarity, out var rc) ? rc : Color.gray;
            var elem = GameDataClient.ElementColors.TryGetValue(c.Element, out var ec) ? ec : Color.white;
            var radius = Rad((float)GameDataClient.CardLayout["cornerRadius"]!);
            float thick = (float)GameDataClient.CardLayout["frameThickness"]! * _s;

            var frame = _root.gameObject.AddComponent<Image>();
            frame.sprite = UiKit.RoundedSprite(radius); frame.type = Image.Type.Sliced; frame.color = rarity;
            var gradient = UiKit.Rect(_root, "FrameShade").Stretch();
            var gi = gradient.gameObject.AddComponent<Image>();
            gi.sprite = UiKit.RoundedSprite(radius); gi.type = Image.Type.Sliced; gi.color = new Color(1, 1, 1, 0.22f); gi.raycastTarget = false;
            gradient.anchorMin = new Vector2(0, 0.55f);

            var inner = UiKit.Panel(_root, UiKit.Hex("#F7FAFF"), Mathf.Max(4, radius - Mathf.RoundToInt(thick)), "Inner");
            ((RectTransform)inner.transform).Stretch(thick, thick, thick, thick);
            inner.raycastTarget = false;

            // name bar + element
            var nameBar = At(_root, "nameBar", "NameBar");
            var nb = nameBar.gameObject.AddComponent<Image>(); nb.sprite = UiKit.RoundedSprite(Rad(40)); nb.type = Image.Type.Sliced; nb.color = Color.white;
            var name = UiKit.Text(_root, c.Name, 66 * _s, UiKit.Ink, UiKit.Weight.ExtraBold, TextAlignmentOptions.MidlineLeft, "Name");
            Fit(name.rectTransform, "name");
            name.enableAutoSizing = true; name.fontSizeMax = 66 * _s; name.fontSizeMin = 36 * _s;
            var el = At(_root, "element", "Element");
            var eli = el.gameObject.AddComponent<Image>(); eli.sprite = UiKit.Circle; eli.color = elem;
            var elt = UiKit.Text(el, ElementCode(c.Element), 34 * _s, Luma(elem) > 0.66f ? UiKit.Ink : Color.white, UiKit.Weight.ExtraBold);
            elt.rectTransform.Stretch();

            Pill("rarity", c.Rarity, rarity, Color.white, 42);
            Pill("quality", c.Quality, UiKit.Sapphire, Color.white, 40);
            Pill("level", L.T("card.level", c.Level), Color.white, UiKit.Ink, 40, UiKit.Gold);

            // art window
            var win = At(_root, "artWindow", "ArtWindow");
            var wi = win.gameObject.AddComponent<Image>(); wi.sprite = UiKit.RoundedSprite(Rad(44)); wi.type = Image.Type.Sliced;
            wi.color = Color.Lerp(elem, Color.white, 0.62f); wi.raycastTarget = false;
            var glow = UiKit.Rect(win, "Glow").Stretch(win.sizeDelta.x * 0.12f, win.sizeDelta.y * 0.12f, win.sizeDelta.x * 0.12f, win.sizeDelta.y * 0.12f);
            var gli = glow.gameObject.AddComponent<Image>(); gli.sprite = UiKit.Circle; gli.color = new Color(1, 1, 1, 0.55f); gli.raycastTarget = false;
            var art = At(_root, "artwork", "Artwork");
            var ai = art.gameObject.AddComponent<Image>(); ai.preserveAspect = true; ai.raycastTarget = false; ai.color = new Color(1, 1, 1, 0);
            LoadArt(ai, c.FormId);

            // finish effect overlay (shader-driven, cosmetic only)
            if (c.Finish != "Normal")
            {
                var fin = At(_root, "artWindow", "Finish");
                _finish = fin.gameObject.AddComponent<Image>();
                _finish.sprite = UiKit.RoundedSprite(Rad(44)); _finish.type = Image.Type.Sliced; _finish.raycastTarget = false;
                var shader = Shader.Find("PetStar/UI/CardFinish");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    mat.SetFloat("_Mode", FinishMode(c.Finish));
                    _finish.material = mat;
                }
                else _finish.color = new Color(1, 1, 1, 0.15f);
            }

            Stat("statHp", "HP", c.Hp, UiKit.Hex("#3BB273"));
            Stat("statAtk", "ATK", c.Atk, UiKit.Hex("#F06548"));
            Stat("statDef", "DEF", c.Def, UiKit.Hex("#3D8BF2"));

            var panel = At(_root, "skillPanel", "SkillPanel");
            var pi = panel.gameObject.AddComponent<Image>(); pi.sprite = UiKit.RoundedSprite(Rad(40)); pi.type = Image.Type.Sliced; pi.color = Color.white; pi.raycastTarget = false;
            if (c.SkillId != null) SkillLine(c.SkillId, "skillName", "skillCost", 46, false);
            if (c.SkillId != null)
            {
                var desc = UiKit.Text(_root, SkillText.Describe(c.SkillId), 34 * _s, UiKit.InkSoft, UiKit.Weight.Regular, TextAlignmentOptions.TopLeft, "SkillText");
                Fit(desc.rectTransform, "skillText");
            }
            if (c.UltimateId != null) SkillLine(c.UltimateId, "ultName", "ultCost", 40, true);

            // footer: stage dots, role, finish
            var footer = At(_root, "footer", "Footer");
            int stages = GameDataClient.Families.TryGetValue(c.FamilyId, out var fam) ? fam.Stages : 1;
            for (int i = 0; i < stages; i++)
            {
                var dot = UiKit.Rect(footer, "Stage" + i);
                dot.Place(new Vector2(0, 0.5f), new Vector2(26 * _s, 26 * _s), new Vector2((18 + i * 44) * _s, 0), new Vector2(0, 0.5f));
                var di = dot.gameObject.AddComponent<Image>(); di.sprite = UiKit.Circle; di.color = i < c.Stage ? rarity : new Color(rarity.r, rarity.g, rarity.b, 0.25f);
            }
            var role = UiKit.Text(footer, L.T("role." + c.Role), 30 * _s, UiKit.InkSoft, UiKit.Weight.Bold);
            role.rectTransform.Stretch();
            // collectible serial: bottom-right of the footer (small, crisp, monospace-like spacing)
            var serial = UiKit.Text(footer, string.IsNullOrEmpty(c.Serial) ? "" : c.Serial, 28 * _s, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineRight, "Serial");
            serial.characterSpacing = 6;
            serial.rectTransform.Stretch(0, 0, 10 * _s, 0);
            if (c.IsLocked || c.IsFavorite)
            {
                var badge = UiKit.Text(_root, (c.IsFavorite ? "♥" : "") + (c.IsLocked ? " " + L.T("card.locked_short") : ""), 44 * _s, UiKit.Danger, UiKit.Weight.Bold, TextAlignmentOptions.TopRight, "Flags");
                badge.rectTransform.Place(new Vector2(1, 1), new Vector2(200 * _s, 80 * _s), new Vector2(-90 * _s, -300 * _s), new Vector2(1, 1));
            }
        }

        private void Fit(RectTransform rt, string key)
        {
            var r = R(key);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(r.x, -r.y); rt.sizeDelta = new Vector2(r.width, r.height);
        }

        private void Pill(string key, string text, Color bg, Color fg, float size, Color? outline = null)
        {
            var rt = At(_root, key, "Pill:" + key);
            var img = rt.gameObject.AddComponent<Image>(); img.sprite = UiKit.RoundedSprite(Rad(36)); img.type = Image.Type.Sliced; img.color = bg; img.raycastTarget = false;
            if (outline.HasValue) { var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = outline.Value; o.effectDistance = new Vector2(2, -2); }
            var t = UiKit.Text(rt, text, size * _s, fg, UiKit.Weight.ExtraBold); t.rectTransform.Stretch();
        }

        private void Stat(string key, string label, int value, Color color)
        {
            var rt = At(_root, key, "Stat:" + label);
            var img = rt.gameObject.AddComponent<Image>(); img.sprite = UiKit.RoundedSprite(Rad(34)); img.type = Image.Type.Sliced; img.color = Color.white; img.raycastTarget = false;
            var l = UiKit.Text(rt, label, 34 * _s, color, UiKit.Weight.Bold); l.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(rt.sizeDelta.x, 60 * _s), new Vector2(0, -14 * _s), new Vector2(0.5f, 1));
            var v = UiKit.Text(rt, value.ToString(), 58 * _s, UiKit.Ink, UiKit.Weight.ExtraBold); v.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(rt.sizeDelta.x, 80 * _s), new Vector2(0, 12 * _s), new Vector2(0.5f, 0));
        }

        private void SkillLine(string skillId, string nameKey, string costKey, float size, bool ultimate)
        {
            var title = UiKit.Text(_root, SkillText.Name(skillId), size * _s, UiKit.Ink, UiKit.Weight.Bold, TextAlignmentOptions.MidlineLeft, "Skill:" + skillId);
            Fit(title.rectTransform, nameKey);
            if (ultimate) { title.text = "<size=60%><color=#2457C5>" + L.T("card.ultimate") + "</color></size>\n" + title.text; title.lineSpacing = -20; }
            var cost = At(_root, costKey, "Cost:" + skillId);
            int n = GameDataClient.Skills.TryGetValue(skillId, out var sd) ? sd.Cost : 0;
            for (int i = 0; i < n; i++)
            {
                var pip = UiKit.Rect(cost, "Pip" + i);
                pip.Place(new Vector2(1, 0.5f), new Vector2(32 * _s, 32 * _s), new Vector2(-(10 + i * 40) * _s, 0), new Vector2(1, 0.5f));
                var pi = pip.gameObject.AddComponent<Image>(); pi.sprite = UiKit.Circle; pi.color = UiKit.Gold;
            }
        }

        private static async void LoadArt(Image img, string formId)
        {
            var s = await GameDataClient.LoadSprite("pet/" + formId);
            if (img == null || s == null) return;
            img.sprite = s; img.color = Color.white;
        }

        public void SetFaceDown(bool down)
        {
            if (down && _back == null)
            {
                var back = UiKit.Rect(_root, "Back").Stretch();
                var bi = back.gameObject.AddComponent<Image>(); bi.sprite = UiKit.RoundedSprite(Rad(72)); bi.type = Image.Type.Sliced; bi.color = UiKit.Sapphire;
                var inner = UiKit.Panel(back, UiKit.SapphireDark, Rad(56), "BackInner");
                ((RectTransform)inner.transform).Stretch(30 * _s, 30 * _s, 30 * _s, 30 * _s);
                var star = UiKit.Text(back, "★", 300 * _s, UiKit.Gold, UiKit.Weight.ExtraBold); star.rectTransform.Stretch();
                var logo = UiKit.Text(back, "PET STAR", 72 * _s, Color.white, UiKit.Weight.ExtraBold);
                logo.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(900 * _s, 120 * _s), new Vector2(0, 200 * _s));
                _back = back.gameObject;
            }
            if (_back != null) _back.SetActive(down);
        }

        public void SetTilt(Vector2 t)
        {
            if (_finish != null && _finish.material != null) _finish.material.SetVector("_Tilt", new Vector4(t.x, t.y, 0, 0));
        }

        private static float FinishMode(string f) => f switch { "Foil" => 0, "Holographic" => 1, "Shiny" => 2, "Prismatic" => 3, _ => 0 };
        private static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        public static string ElementCode(string e) =>
            new Dictionary<string, string> { ["fire"] = "FI", ["water"] = "WA", ["nature"] = "NA", ["earth"] = "EA", ["lightning"] = "EL", ["wind"] = "WI", ["light"] = "LI", ["dark"] = "DA" }
                .TryGetValue(e, out var s) ? s : "?";
    }

    /// <summary>Card tilt toward pointer/drag; drives the finish shader reflection.</summary>
    public sealed class CardTilt : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IDragHandler, IEndDragHandler
    {
        private Vector2 _target, _cur;
        private bool _hover;
        private CardView? _view;
        private void Awake() => _view = GetComponent<CardView>();
        public void OnPointerEnter(PointerEventData e) => _hover = true;
        public void OnPointerExit(PointerEventData e) { _hover = false; _target = Vector2.zero; }
        public void OnDrag(PointerEventData e) => Aim(e.position);
        public void OnEndDrag(PointerEventData e) => _target = Vector2.zero;

        private void Aim(Vector2 screen)
        {
            var rt = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out var local))
                _target = new Vector2(Mathf.Clamp(local.x / (rt.rect.width / 2), -1, 1), Mathf.Clamp(local.y / (rt.rect.height / 2), -1, 1));
        }

        private void Update()
        {
            if (_hover && UnityEngine.InputSystem.Mouse.current != null) Aim(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
            _cur = Vector2.Lerp(_cur, _target, Time.unscaledDeltaTime * 8f);
            transform.localRotation = Quaternion.Euler(-_cur.y * 8f, _cur.x * 10f, 0);
            _view?.SetTilt(_cur);
        }
    }

    public static class SkillText
    {
        public static string Name(string skillId) => L.T("skill." + skillId);

        public static string Describe(string skillId)
        {
            if (!GameDataClient.Skills.TryGetValue(skillId, out var s)) return "";
            var parts = new List<string>();
            foreach (var e in s.Effects)
            {
                switch (e.Type)
                {
                    case "damage": parts.Add(L.T("effect.damage", Mathf.RoundToInt((float)e.Power * 100))); break;
                    case "heal": parts.Add(L.T("effect.heal", Mathf.RoundToInt((float)e.Power * 100))); break;
                    case "shield": parts.Add(L.T("effect.shield", Mathf.RoundToInt((float)e.Power * 100), e.Scale.ToUpperInvariant())); break;
                    case "status": parts.Add(L.T("effect.status", Mathf.RoundToInt((float)e.Chance * 100), L.T("status." + e.Status), e.Duration)); break;
                    case "cleanse": parts.Add(L.T("effect.cleanse")); break;
                }
            }
            return string.Join(", ", parts) + " · " + L.T("target." + s.Target);
        }
    }
}
