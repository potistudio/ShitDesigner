#!/usr/bin/env bash
set -euo pipefail

script_root="$(cd "$(dirname "$0")" && pwd)"
project_root="$(cd "${script_root}/../.." && pwd)"
editor_version="$(awk '/^m_EditorVersion: / { print $2; exit }' "${project_root}/ProjectSettings/ProjectVersion.txt")"
unity_app="${1:-/Applications/${editor_version}/Unity.app}"
plugin_api="${unity_app}/Contents/Resources/PluginAPI"
output="${project_root}/Assets/ShitDesigner/Plugins/macOS/shitdesigner_mac_display.dylib"

test -f "${plugin_api}/IUnityGraphicsMetal.h"
xcrun clang++ \
  -dynamiclib \
  -fobjc-arc \
  -std=c++17 \
  -arch arm64 \
  -arch x86_64 \
  -mmacosx-version-min=12.0 \
  -framework AppKit \
  -framework Metal \
  -framework QuartzCore \
  -framework Foundation \
  -I"${plugin_api}" \
  "${script_root}/ShitDesignerMacDisplay.mm" \
  -Wl,-install_name,@rpath/shitdesigner_mac_display.dylib \
  -o "${output}"

lipo "${output}" -verify_arch arm64 x86_64
echo "Built macOS external display plugin: ${output}"
