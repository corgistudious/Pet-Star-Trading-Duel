#!/usr/bin/env bash
# Semantic compile check of the Unity client WITHOUT a Unity Editor or license (runs in CI and locally).
# It catches API, type and name errors before Unity Build Automation runs. It is NOT a Unity build: shader,
# asset import and IL2CPP problems can still appear only in the real build.
set -uo pipefail
cd "$(dirname "$0")"
mkdir -p obj
fail=0
report() { # $1 label, $2 log
  if grep -qE " error " "$2"; then echo "FAIL $1"; grep -E " error " "$2" | sed -E 's#.*/client-unity/#client-unity/#; s#.*/obj/editor-src/#client-unity/Assets/Editor/#; s# \[.*##' | sort -u; fail=1; else echo "ok   $1"; fi
}
for plat in "UNITY_WEBGL+ENABLE_INPUT_SYSTEM" "UNITY_WEBGL" "UNITY_ANDROID+ENABLE_INPUT_SYSTEM" "UNITY_STANDALONE_WIN+ENABLE_INPUT_SYSTEM" "UNITY_EDITOR+UNITY_WEBGL+ENABLE_INPUT_SYSTEM"; do
  dotnet build Runtime.csproj -v q -nologo "-p:Platform=$plat" > "obj/runtime-$plat.log" 2>&1
  report "runtime [$plat]" "obj/runtime-$plat.log"
done
python3 rewrite_editor.py ../../client-unity/Assets/Editor obj/editor-src
dotnet build Editor.csproj -v q -nologo > obj/editor.log 2>&1
report "editor scripts" obj/editor.log
exit $fail
