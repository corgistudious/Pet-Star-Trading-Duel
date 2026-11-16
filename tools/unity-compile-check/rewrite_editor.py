"""Copies the Editor scripts and maps the few Unity 6-only PlayerSettings overloads to shims (check-only)."""
import re, shutil, sys, pathlib
src = pathlib.Path(sys.argv[1]); dst = pathlib.Path(sys.argv[2])
shutil.rmtree(dst, ignore_errors=True); shutil.copytree(src, dst, ignore=shutil.ignore_patterns('*.meta', '*.asmdef'))
p = dst / 'PetStarBuild.cs'; s = p.read_text()
s = s.replace("PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;", "Shim.SetInsecure(InsecureHttpOption.DevelopmentOnly);")
s = s.replace("ManagedStrippingLevel.Minimal", "ManagedStrippingLevel.Low")
for m in ("SetApplicationIdentifier", "SetScriptingBackend", "SetManagedStrippingLevel"):
    s = re.sub(rf"PlayerSettings\.{m}\(NamedBuildTarget\.", f"Shim.{m}(NamedBuildTarget.", s)
s += """
namespace PetStar.EditorTools { static class Shim {
  public static void SetInsecure(UnityEditor.InsecureHttpOption o) {}
  public static void SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget t, string id) {}
  public static void SetScriptingBackend(UnityEditor.Build.NamedBuildTarget t, UnityEditor.ScriptingImplementation s) {}
  public static void SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget t, UnityEditor.ManagedStrippingLevel l) {}
} }
"""
p.write_text(s)
