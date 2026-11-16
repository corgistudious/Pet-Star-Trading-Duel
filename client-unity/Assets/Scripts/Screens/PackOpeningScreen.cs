using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PetStar.Client.Core;
using PetStar.Client.UI;
using PetStar.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PetStar.Client.Screens
{
    /// <summary>Pack artwork with a separable top strip (used for the interactive tear).</summary>
    public sealed class PackVisual : MonoBehaviour
    {
        public RectTransform Strip = null!, Body = null!, TearLine = null!;
        public const float StripRatio = 0.17f;

        public static PackVisual Create(Transform parent, string packTypeId, float width)
        {
            var root = UiKit.Rect(parent, "Pack:" + packTypeId);
            root.sizeDelta = new Vector2(width, width * 1.5f);
            var v = root.gameObject.AddComponent<PackVisual>();
            var hit = root.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); // raycast target for gestures

            v.Body = Part(root, packTypeId, false);
            v.Strip = Part(root, packTypeId, true);
            v.TearLine = UiKit.Rect(root, "TearLine");
            v.TearLine.anchorMin = v.TearLine.anchorMax = new Vector2(0, 1 - StripRatio); v.TearLine.pivot = new Vector2(0, 0.5f);
            v.TearLine.sizeDelta = new Vector2(0, 10);
            var tl = v.TearLine.gameObject.AddComponent<Image>(); tl.color = new Color(1, 1, 1, 0.95f); tl.raycastTarget = false;
            return v;
        }

        private static RectTransform Part(RectTransform root, string packTypeId, bool top)
        {
            var clip = UiKit.Rect(root, top ? "Strip" : "Body");
            clip.anchorMin = new Vector2(0, top ? 1 - StripRatio : 0); clip.anchorMax = new Vector2(1, top ? 1 : 1 - StripRatio);
            clip.offsetMin = clip.offsetMax = Vector2.zero;
            clip.pivot = top ? new Vector2(0.5f, 0) : new Vector2(0.5f, 0.5f);
            clip.gameObject.AddComponent<RectMask2D>();
            var art = UiKit.Rect(clip, "Art");
            art.anchorMin = art.anchorMax = new Vector2(0.5f, top ? 1 : 0); art.pivot = new Vector2(0.5f, top ? 1 : 0);
            art.sizeDelta = new Vector2(root.sizeDelta.x, root.sizeDelta.y);
            art.anchoredPosition = Vector2.zero;
            var img = art.gameObject.AddComponent<Image>(); img.raycastTarget = false; img.preserveAspect = false;
            img.sprite = UiKit.RoundedSprite(36); img.type = Image.Type.Sliced; img.color = packTypeId == "premium_pack" ? UiKit.Hex("#8E6BD8") : UiKit.Hex("#7FB2F0");
            _ = Load(img, packTypeId);
            return clip;
        }

        private static async Task Load(Image img, string packTypeId)
        {
            var s = await GameDataClient.LoadSprite(GameDataClient.PackArt(packTypeId));
            if (img == null || s == null) return;
            img.sprite = s; img.type = Image.Type.Simple; img.color = Color.white;
        }
    }

    public sealed class PackOpeningScreen : ScreenBase
    {
        private enum Phase { Ready, Tearing, Revealing, Results }

        public override bool ShowCurrencyBar => false;
        private readonly string _packTypeId;
        private PackOpeningDto? _result;
        private Task<PackOpeningDto?>? _request;
        private Phase _phase = Phase.Ready;
        private int _revealed;
        private RectTransform _stage = null!;
        private PackVisual? _pack;
        private TextMeshProUGUI _hint = null!;
        private readonly List<CardView> _stack = new();
        private Runner _run = null!;

        public PackOpeningScreen(string packTypeId) { _packTypeId = packTypeId; }

        /// <summary>Recovery: resume an opening whose result is already stored on the server.</summary>
        public PackOpeningScreen(PackOpeningDto pending) { _packTypeId = pending.PackTypeId; _result = pending; _phase = Phase.Revealing; }

        public override void Build()
        {
            _run = Root.gameObject.AddComponent<Runner>();
            var bg = UiKit.Icon(Root, null, Vector2.zero, "Bg");
            ((RectTransform)bg.transform).Stretch();
            bg.preserveAspect = false;
            _ = LoadBg(bg);
            _stage = UiKit.Rect(Root, "Stage").Stretch(0, Wide ? 40 : 200, 0, Wide ? 40 : 260);
            _hint = UiKit.Text(Root, "", 40, UiKit.Ink, UiKit.Weight.Bold);
            _hint.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(1000, 120), new Vector2(0, Wide ? 40 : 140));
            var skip = UiKit.Button(Root, L.T("pack.skip"), Skip, UiKit.ButtonStyle.Secondary, 36);
            ((RectTransform)skip.transform).Place(new Vector2(1, 1), new Vector2(220, 100), new Vector2(-40, Wide ? -30 : -60));
            Render();
        }

        private static async Task LoadBg(Image img)
        {
            var s = await GameDataClient.LoadSprite("ui/bg_pack_opening");
            if (img == null) return;
            if (s != null) { img.sprite = s; img.color = Color.white; } else { img.color = UiKit.Sky; }
        }

        private void Render()
        {
            UiKit.Clear(_stage);
            _stack.Clear();
            switch (_phase)
            {
                case Phase.Ready:
                case Phase.Tearing:
                    _pack = PackVisual.Create(_stage, _packTypeId, Wide ? 420 : 560);
                    ((RectTransform)_pack.transform).Place(new Vector2(0.5f, 0.5f), ((RectTransform)_pack.transform).sizeDelta, Vector2.zero);
                    _pack.gameObject.AddComponent<FloatIdle>();
                    var g = _pack.gameObject.AddComponent<TearGesture>();
                    g.OnStart = BeginTear; g.OnProgress = TearProgress; g.OnComplete = () => _run.StartCoroutine(FinishTear());
                    _hint.text = L.T("pack.swipe_to_tear");
                    break;
                case Phase.Revealing:
                    _run.StartCoroutine(ShowStack());
                    break;
                case Phase.Results:
                    ShowResults();
                    break;
            }
        }

        // ---------------- tear ----------------
        private void BeginTear()
        {
            if (_phase != Phase.Ready) return;
            _phase = Phase.Tearing;
            Audio.Play(Audio.Sfx.PackTear);
            Audio.Haptic();
            // commit: the server consumes the pack and stores the 5 cards now; the client shows them only on flip
            _request ??= OpenOnServer();
        }

        private async Task<PackOpeningDto?> OpenOnServer()
        {
            var key = ApiClient.NewIdempotencyKey();
            try
            {
                var r = await Api.Post<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = _packTypeId }, key);
                _result = r;
                return r;
            }
            catch (ApiError e)
            {
                Nav.Toast(e.IsNetwork ? L.T("pack.network_recover") : L.Error(e.Code), true);
                return null;
            }
        }

        private void TearProgress(float p)
        {
            if (_pack == null) return;
            var w = ((RectTransform)_pack.transform).sizeDelta.x;
            _pack.TearLine.sizeDelta = new Vector2(w * p, 10);
            _pack.Strip.localRotation = Quaternion.Euler(0, 0, p * 9f);
            _pack.Strip.anchoredPosition = new Vector2(p * 30f, p * 26f);
        }

        private IEnumerator FinishTear()
        {
            Audio.Play(Audio.Sfx.PackTear, 1.2f);
            Audio.Haptic();
            _hint.text = "";
            if (_pack != null)
            {
                float t = 0;
                var strip = _pack.Strip; var body = (RectTransform)_pack.transform;
                UnityEngine.Object.Destroy(_pack.GetComponent<FloatIdle>());
                while (t < 1)
                {
                    t += Time.deltaTime * 2.2f;
                    strip.anchoredPosition += new Vector2(1400 * Time.deltaTime, 700 * Time.deltaTime);
                    strip.localRotation = Quaternion.Euler(0, 0, 9 + t * 60);
                    body.anchoredPosition = new Vector2(0, -t * 900);
                    yield return null;
                }
            }
            while (_request != null && !_request.IsCompleted) { _hint.text = L.T("common.loading"); yield return null; }
            if (_result == null) { Nav.Back(); yield break; }
            _phase = Phase.Revealing;
            Render();
        }

        // ---------------- flip ----------------
        private IEnumerator ShowStack()
        {
            _hint.text = L.T("pack.swipe_to_flip");
            float w = Wide ? 400 : 560;
            var cards = _result!.Cards;
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                var cv = CardView.Create(_stage, cards[i], w, interactive: false, faceDown: true);
                var rt = (RectTransform)cv.transform;
                rt.Place(new Vector2(0.5f, 0.5f), rt.sizeDelta, new Vector2(i * 6, -i * 6));
                _stack.Insert(0, cv);
            }
            // deal-in animation
            for (int i = 0; i < _stack.Count; i++)
            {
                var rt = (RectTransform)_stack[i].transform;
                var target = rt.anchoredPosition;
                rt.anchoredPosition = target + new Vector2(0, -900);
                _run.StartCoroutine(Move(rt, target, 0.35f + i * 0.06f));
            }
            yield return new WaitForSeconds(0.7f);
            ArmTopCard();
        }

        private void ArmTopCard()
        {
            if (_revealed >= _stack.Count) { _run.StartCoroutine(GoResults()); return; }
            var top = _stack[_revealed];
            var g = top.gameObject.AddComponent<SwipeOrTap>();
            g.OnFire = () => { UnityEngine.Object.Destroy(g); _run.StartCoroutine(Flip(top, _revealed == _stack.Count - 1)); };
        }

        private IEnumerator Flip(CardView card, bool last)
        {
            var rt = (RectTransform)card.transform;
            if (last)
            {
                // suspense beat for the final card (identical for every rarity, so nothing is revealed early)
                float s = 0;
                while (s < 0.9f) { s += Time.deltaTime; rt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(s * 40) * 2f * s); yield return null; }
                rt.localRotation = Quaternion.identity;
            }
            Audio.Play(Audio.Sfx.CardFlip);
            float t = 0;
            while (t < 1) { t += Time.deltaTime * 5f; rt.localScale = new Vector3(Mathf.Lerp(1, 0, t), 1, 1); yield return null; }
            card.SetFaceDown(false);
            card.gameObject.AddComponent<CardTilt>();
            t = 0;
            while (t < 1) { t += Time.deltaTime * 5f; rt.localScale = new Vector3(Mathf.Lerp(0, 1, t), 1, 1); yield return null; }
            rt.localScale = Vector3.one;
            int rank = GameDataClient.RarityRank(card.Card.Rarity);
            if (rank >= 2) RareBurst(rt, card.Card.Rarity, rank);
            bool divine = card.Card.DivineSet != null;
            if (divine)
            {
                // Divine Pet: longer reveal with a star burst in the set's colours and an announcement
                UpgradeBurst.Play((RectTransform)rt.parent, "awaken", 0);
                Nav.Toast(L.T("divine.obtained") + " " + L.T("divine." + card.Card.DivineSet));
            }
            yield return new WaitForSeconds(divine ? 1.8f : rank >= 2 ? 0.9f : 0.35f);
            // move to the side to uncover the next one
            var target = rt.anchoredPosition + new Vector2(Wide ? -700 : 0, Wide ? 0 : 700);
            _run.StartCoroutine(Move(rt, target, 0.3f, shrink: 0.45f));
            _revealed++;
            yield return new WaitForSeconds(0.25f);
            ArmTopCard();
        }

        private void RareBurst(RectTransform card, string rarity, int rank)
        {
            Audio.Play(Audio.Sfx.RareReveal, 1f + rank * 0.03f);
            Audio.Haptic();
            var col = GameDataClient.RarityColors[rarity];
            var glow = UiKit.Rect(card.parent, "Glow");
            glow.SetSiblingIndex(card.GetSiblingIndex());
            glow.Place(new Vector2(0.5f, 0.5f), card.sizeDelta * 1.25f, card.anchoredPosition);
            var gi = glow.gameObject.AddComponent<Image>(); gi.sprite = UiKit.RoundedSprite(64); gi.type = Image.Type.Sliced;
            gi.color = new Color(col.r, col.g, col.b, 0.6f); gi.raycastTarget = false;
            glow.gameObject.AddComponent<Pulse>();
            int sparkles = 10 + rank * 4;
            for (int i = 0; i < sparkles; i++)
            {
                var sp = UiKit.Text(card.parent, "✦", UnityEngine.Random.Range(30, 70), Color.Lerp(col, Color.white, 0.5f), UiKit.Weight.Bold, TextAlignmentOptions.Center, "Sparkle");
                sp.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(80, 80), card.anchoredPosition);
                sp.gameObject.AddComponent<Sparkle>().Init(UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(300, 700));
            }
        }

        private IEnumerator GoResults()
        {
            yield return new WaitForSeconds(0.4f);
            _phase = Phase.Results;
            Render();
        }

        private void Skip()
        {
            // Skip only shortens the animation; the result is already fixed on the server.
            if (_phase == Phase.Results) return;
            if (_result == null)
            {
                _request ??= OpenOnServer();
                _run.StartCoroutine(SkipWhenReady());
                return;
            }
            _phase = Phase.Results;
            _run.StopAllCoroutines();
            Render();
        }

        private IEnumerator SkipWhenReady()
        {
            _phase = Phase.Tearing;
            while (_request != null && !_request.IsCompleted) yield return null;
            if (_result == null) { Nav.Back(); yield break; }
            _phase = Phase.Results;
            Render();
        }

        // ---------------- results ----------------
        private void ShowResults()
        {
            _hint.text = "";
            var cards = _result!.Cards;
            var grid = UiKit.Rect(_stage, "Results");
            grid.Stretch(30, 0, 30, Wide ? 140 : 220);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            float cw = Wide ? 300 : 330;
            layout.cellSize = new Vector2(cw, cw / CardView.Aspect);
            layout.spacing = new Vector2(24, 30);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Wide ? 5 : 3;
            foreach (var c in cards)
            {
                var cv = CardView.Create(grid, c, cw);
                var card = c;
                var btn = cv.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() => CardDetails.Show(card, updated => { }));
                var fav = UiKit.Button(cv.transform, c.IsFavorite ? "♥" : "♡", () => ToggleFav(card), UiKit.ButtonStyle.Secondary, 40, 30);
                ((RectTransform)fav.transform).Place(new Vector2(1, 1), new Vector2(80, 80), new Vector2(-6, -6), new Vector2(1, 1));
            }
            var row = UiKit.Rect(_stage, "Actions");
            row.Place(new Vector2(0.5f, 0), new Vector2(Wide ? 1200 : 1000, 130), new Vector2(0, 20), new Vector2(0.5f, 0));
            UiKit.HStack(row, 24).childForceExpandWidth = true;
            UiKit.Button(row, L.T("pack.to_pet_home"), () => Nav.Replace(new PetHomeScreen()), UiKit.ButtonStyle.Secondary).Size(-1, 120, 1);
            int left = Session.Player != null && Session.Player.Packs.TryGetValue(_packTypeId, out var n) ? n - 1 : 0;
            UiKit.Button(row, L.T("pack.open_another"), () => Nav.Replace(new PackOpeningScreen(_packTypeId)), UiKit.ButtonStyle.Gold).Size(-1, 120, 1)
                .GetComponent<Button>().interactable = left > 0;
            _ = Ack();
        }

        private async Task Ack()
        {
            try
            {
                await Api.Post<Newtonsoft.Json.Linq.JObject>($"/api/packs/openings/{_result!.OpeningId}/ack", null);
                Session.SetPlayer(await Api.Get<PlayerDto>("/api/player"));
            }
            catch (ApiError) { /* will be recovered later */ }
        }

        private async void ToggleFav(CardDto c)
        {
            var r = await Call(() => Api.Post<CardDto>($"/api/cards/{c.Id}/favorite", new FlagRequest { Value = !c.IsFavorite }), overlay: false);
            if (r == null) return;
            c.IsFavorite = r.IsFavorite;
            Render();
        }

        private static IEnumerator Move(RectTransform rt, Vector2 target, float duration, float shrink = 1f)
        {
            var start = rt.anchoredPosition; var s0 = rt.localScale.x; float t = 0;
            while (t < 1 && rt != null)
            {
                t += Time.deltaTime / duration;
                float k = 1 - Mathf.Pow(1 - Mathf.Clamp01(t), 3);
                rt.anchoredPosition = Vector2.Lerp(start, target, k);
                rt.localScale = Vector3.one * Mathf.Lerp(s0, shrink, k);
                yield return null;
            }
        }

        private sealed class Runner : MonoBehaviour { }
    }

    /// <summary>Horizontal swipe across the top edge of the pack (finger on Android, press-and-drag on Windows).</summary>
    public sealed class TearGesture : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
    {
        public Action? OnStart; public Action<float>? OnProgress; public Action? OnComplete;
        private bool _active, _done; private float _startX, _maxCovered;

        public void OnPointerDown(PointerEventData e) { }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_done) return;
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local)) return;
            float fromTop = rt.rect.height / 2 - local.y;
            if (fromTop > rt.rect.height * 0.32f) return; // must start near the top edge
            _active = true; _startX = local.x; _maxCovered = 0;
            OnStart?.Invoke();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_active || _done) return;
            var rt = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local);
            _maxCovered = Mathf.Max(_maxCovered, Mathf.Abs(local.x - _startX));
            float p = Mathf.Clamp01(_maxCovered / (rt.rect.width * 0.8f));
            OnProgress?.Invoke(p);
            if (p >= 1f) { _done = true; _active = false; OnComplete?.Invoke(); }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_done || !_active) return;
            _active = false;
            OnProgress?.Invoke(0); // tear snaps back; the player can try again (server result is already stored)
        }
    }

    /// <summary>Swipe (any direction, > 80px) or tap to reveal the top card.</summary>
    public sealed class SwipeOrTap : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action? OnFire;
        private Vector2 _start; private bool _fired;
        private void Awake() { if (GetComponent<Graphic>() is Graphic g) g.raycastTarget = true; }
        public void OnPointerClick(PointerEventData e) { if (!e.dragging) Fire(); }
        public void OnBeginDrag(PointerEventData e) => _start = e.position;
        public void OnDrag(PointerEventData e) { if ((e.position - _start).magnitude > 80) Fire(); }
        public void OnEndDrag(PointerEventData e) { }
        private void Fire() { if (_fired) return; _fired = true; OnFire?.Invoke(); }
    }

    public sealed class Pulse : MonoBehaviour
    {
        private float _t;
        private void Update()
        {
            _t += Time.deltaTime;
            transform.localScale = Vector3.one * (1 + 0.04f * Mathf.Sin(_t * 5));
            var img = GetComponent<Image>();
            if (_t > 2.5f) { var c = img.color; c.a = Mathf.MoveTowards(c.a, 0, Time.deltaTime); img.color = c; if (c.a <= 0) Destroy(gameObject); }
        }
    }

    public sealed class Sparkle : MonoBehaviour
    {
        private Vector2 _v; private float _life;
        public void Init(Vector2 velocity) { _v = velocity; }
        private void Update()
        {
            _life += Time.deltaTime;
            var rt = (RectTransform)transform;
            rt.anchoredPosition += _v * Time.deltaTime;
            _v *= 0.94f;
            rt.localRotation = Quaternion.Euler(0, 0, _life * 180);
            var t = GetComponent<TextMeshProUGUI>();
            t.alpha = Mathf.Clamp01(1.2f - _life);
            if (_life > 1.3f) Destroy(gameObject);
        }
    }
}
