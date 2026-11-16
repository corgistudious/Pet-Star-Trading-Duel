// Check-only: Unity 6 editor APIs missing from the 2021.1 reference UnityEditor.dll used by this harness.
// Each exists in Unity 6000.0 with this shape (NamedBuildTarget 2021.2+, ManagedStrippingLevel.Minimal 2021.2+,
// PlayerSettings.insecureHttpOption 2022.1+). Never shipped in the project.
namespace UnityEditor.Build
{
    public struct NamedBuildTarget
    {
        public static readonly NamedBuildTarget Android, Standalone, WebGL;
    }
}
namespace UnityEditor
{
    public enum InsecureHttpOption { NotAllowed, DevelopmentOnly, AlwaysAllowed }
    public static class PlayerSettingsShim { }
}
