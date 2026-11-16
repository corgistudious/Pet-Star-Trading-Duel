// Syntax-only check (C# 9 = Unity 6) of the Unity client scripts. It does NOT replace a Unity compile (no UnityEngine
// types here); it catches broken syntax in CI until the Unity build pipeline is licensed.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
int errors = 0, files = 0;
var opts = new CSharpParseOptions(LanguageVersion.CSharp9); // Unity 6 supports C# 9
foreach (var f in Directory.GetFiles(args[0], "*.cs", SearchOption.AllDirectories))
{
    files++;
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(f), opts, f);
    foreach (var d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)) { errors++; Console.WriteLine(d); }
}
Console.WriteLine($"{files} files, {errors} syntax errors (C# 9)");
return errors == 0 ? 0 : 1;
