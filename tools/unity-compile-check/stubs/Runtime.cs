// Minimal API stubs for Unity PACKAGES that are not downloadable here (TextMeshPro in com.unity.ugui 2.0,
// Addressables 2.x, Input System 1.x). Only members our code uses; signatures follow the real packages.
#pragma warning disable CS0626, CS0067, CS8618
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TMPro
{
    public enum TextAlignmentOptions { TopLeft = 257, Top = 258, TopRight = 260, Left = 513, Center = 514, Right = 516, BottomLeft = 1025, Bottom = 1026, BottomRight = 1028, MidlineLeft = 4097, Midline = 4098, MidlineRight = 4100 }
    public enum TextOverflowModes { Overflow = 0, Ellipsis = 1, Masking = 2, Truncate = 3, ScrollRect = 4, Page = 5, Linked = 6 }
    [Flags] public enum FontStyles { Normal = 0, Bold = 1, Italic = 2, Underline = 4, LowerCase = 8, UpperCase = 16, SmallCaps = 32, Strikethrough = 64 }
    public enum AtlasPopulationMode { Static = 0, Dynamic = 1, DynamicOS = 2 }
    public class TMP_Asset : ScriptableObject { }
    public class TMP_FontAsset : TMP_Asset
    {
        public Texture2D[] atlasTextures;
        public Material material;
        public static TMP_FontAsset CreateFontAsset(Font font, int samplingPointSize, int atlasPadding, UnityEngine.TextCore.LowLevel.GlyphRenderMode renderMode, int atlasWidth, int atlasHeight, AtlasPopulationMode atlasPopulationMode = AtlasPopulationMode.Dynamic, bool enableMultiAtlasSupport = true) => null;
    }
    public abstract class TMP_Text : MaskableGraphic
    {
        public virtual string text { get; set; }
        public TMP_FontAsset font { get; set; }
        public float fontSize { get; set; }
        public FontStyles fontStyle { get; set; }
        public TextAlignmentOptions alignment { get; set; }
        public TextOverflowModes overflowMode { get; set; }
        public bool enableWordWrapping { get; set; }
        public bool richText { get; set; }
        public float characterSpacing { get; set; }
        public float lineSpacing { get; set; }
        public bool enableAutoSizing { get; set; }
        public float fontSizeMin { get; set; }
        public float fontSizeMax { get; set; }
        public float outlineWidth { get; set; }
        public Color32 outlineColor { get; set; }
        public Vector4 margin { get; set; }
        public float alpha { get; set; }
        public float preferredHeight => 0;
        public float preferredWidth => 0;
        public new RectTransform rectTransform => null;
    }
    public class TextMeshProUGUI : TMP_Text { }
    public class TMP_InputField : Selectable
    {
        public enum ContentType { Standard, Autocorrected, IntegerNumber, DecimalNumber, Alphanumeric, Name, EmailAddress, Password, Pin, Custom }
        public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline }
        [Serializable] public class SubmitEvent : UnityEngine.Events.UnityEvent<string> { }
        [Serializable] public class OnChangeEvent : UnityEngine.Events.UnityEvent<string> { }
        public string text { get; set; }
        public int characterLimit { get; set; }
        public ContentType contentType { get; set; }
        public LineType lineType { get; set; }
        public TMP_Text textComponent { get; set; }
        public Graphic placeholder { get; set; }
        public RectTransform textViewport { get; set; }
        public SubmitEvent onEndEdit { get; set; }
        public SubmitEvent onSubmit { get; set; }
        public OnChangeEvent onValueChanged { get; set; }
        public float caretWidth { get; set; }
        public Color selectionColor { get; set; }
        public int pointSize { get; set; }
    }
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public struct AsyncOperationHandle<TObject>
    {
        public bool IsDone => true;
        public AsyncOperationStatus Status => AsyncOperationStatus.None;
        public TObject Result => default;
        public System.Threading.Tasks.Task<TObject> Task => null;
        public bool IsValid() => true;
    }
    public struct AsyncOperationHandle { public bool IsDone => true; }
}

namespace UnityEngine.AddressableAssets
{
    using UnityEngine.ResourceManagement.AsyncOperations;
    public static class Addressables
    {
        public static AsyncOperationHandle<TObject> LoadAssetAsync<TObject>(object key) => default;
        public static void Release<TObject>(AsyncOperationHandle<TObject> handle) { }
        public static void Release<TObject>(TObject obj) { }
    }
}

namespace UnityEngine.InputSystem
{
    public abstract class InputControl<TValue> where TValue : struct { public TValue ReadValue() => default; }
    public class Vector2Control : InputControl<Vector2> { }
    public class Pointer { public Vector2Control position { get; } }
    public class Mouse : Pointer { public static Mouse current { get; } }
}

namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : BaseInputModule { public override void Process() { } }
}

// Engine-module gap in the NuGet 2021.3 reference set (Unity 6 ships com.unity.modules.vibration).
namespace UnityEngine { public static class Handheld { public static void Vibrate() { } } }
